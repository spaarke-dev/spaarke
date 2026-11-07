using System.ServiceModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 164a-4a (EB-D-188, brief <c>notes/add-line-brief.md</c> sections 3 and 8): <see cref="InvoiceLineAddService"/> steps 0 to 6 over the
/// in-memory Dataverse of <see cref="CorrectionWorld"/> (the transaction handler is replaced here so it also takes the line create), 072's real in-memory
/// evaluator and the real code-set and timekeeper resolution. The route's rights and step 0 are the contract tests
/// (<c>InvoiceLineReviewEndpointsAuthorizationContractTests</c>).
/// </summary>
public sealed partial class InvoiceLineAddServiceTests
{
    private static readonly Guid RequestId = Guid.Parse("16400000-0000-4000-8000-000000000001");
    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap); // 12 hours a day

    /// <summary>The world, its transactions and the service under test.</summary>
    private sealed class AddLineWorld
    {
        public readonly CorrectionWorld World = new();
        public readonly List<List<OrganizationRequest>> Transactions = [];
        public readonly Mock<IIdempotencyService> Locks = new();
        public readonly Mock<IIdentityNormalizationService> RecorderIdentity = new(MockBehavior.Strict);
        public Func<IReadOnlyList<OrganizationRequest>, Exception?>? Fault;
        public Guid Person;

        /// <summary>164a-4b: the Decision Record writer's harness (its created records), the totals store and its writes.</summary>
        public readonly DecisionRecordWriterTests.Harness H = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        public readonly Mock<IFieldMappingDataverseService> Records = new(MockBehavior.Strict);
        public readonly List<Dictionary<string, object?>> TotalsWrites = [];
        public Exception? TotalsFault;

        /// <summary>164a-4b: replaces the evaluator's result of a request (null: the real result), to shape a before/after difference.</summary>
        public Func<BillingEvaluationRequest, BillingEvaluationResult, BillingEvaluationResult>? Shape;

        /// <summary>The order of the commit, the totals write and the record write (R8: the record only after the line write).</summary>
        public readonly List<string> Order = [];

        public AddLineWorld()
        {
            World.DefaultCodeSetId = W.CodeSetId;
            InvoiceCorrectionReevaluationTests.Store(World);
            Person = World.AddPerson("A. Lee").Id;
            World.AddRate(Person, 100m);
            Locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // The recorder's identity is STRICT: only the contact read of a record that is going to be written is set up.
            RecorderIdentity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid user, CancellationToken _) =>
                {
                    Order.Add("record");
                    return new PersonIdentity(user, ContactId: Guid.Parse("16400000-0000-4000-8000-0000000000e1"));
                });
            H.Rows[("sprk_invoice", W.InvoiceId)] = new Entity("sprk_invoice", W.InvoiceId) { ["sprk_project"] = new EntityReference("sprk_project", W.Core) };

            // 016's reads and its conditional write of the five totals (the real InvoiceTotalsService over the world's lines).
            Records.Setup(r => r.RetrieveRecordFieldsAsync("sprk_invoice", W.InvoiceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns(() => TotalsFault is { } fault
                    ? Task.FromException<Dictionary<string, object?>>(fault)
                    : Task.FromResult(new Dictionary<string, object?>
                    {
                        ["sprk_totalamount"] = World.Invoice.GetAttributeValue<Money>("sprk_totalamount").Value,
                        ["versionnumber"] = World.Invoice.GetAttributeValue<long>("versionnumber"),
                    }));
            Records.Setup(r => r.UpdateRecordFieldsIfUnchangedAsync("sprk_invoice", W.InvoiceId, It.IsAny<Dictionary<string, object?>>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .Callback((string _, Guid _, Dictionary<string, object?> fields, long _, CancellationToken _) =>
                {
                    Order.Add("totals");
                    TotalsWrites.Add(fields);
                    World.Invoice["versionnumber"] = World.Invoice.GetAttributeValue<long>("versionnumber") + 1;
                })
                .Returns(Task.CompletedTask);

            // One transaction, all or nothing: every conditional update is checked first (the invoice, the submission, a line), then the create and the
            // updates are applied. Replaces CorrectionWorld's handler (Moq: the last setup wins), which has no create.
            World.Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) =>
                {
                    Transactions.Add(requests.ToList());
                    if (Fault?.Invoke(requests) is { } fault)
                    {
                        return Task.FromException(fault);
                    }

                    lock (World.Lines)
                    {
                        foreach (var update in requests.OfType<UpdateRequest>())
                        {
                            // As Dataverse: only a conditional update is checked against the row version (seed S3 drops the condition).
                            if (RowOf(update.Target) is not { } row
                                || (update.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches
                                    && update.Target.RowVersion != row.GetAttributeValue<long>("versionnumber").ToString()))
                            {
                                return Task.FromException(Mismatch(requests, update));
                            }
                        }

                        foreach (var create in requests.OfType<CreateRequest>())
                        {
                            if (World.Lines.Any(l => l.Id == create.Target.Id))
                            {
                                return Task.FromException(new InvalidOperationException("duplicate key"));
                            }
                        }

                        foreach (var create in requests.OfType<CreateRequest>())
                        {
                            var copy = W.Copy(create.Target);
                            copy["versionnumber"] = 1L;
                            World.Lines.Add(copy);
                            Order.Add("commit");

                            // The record writer files a line's record under the line's core record (D-75): it reads the row it was handed.
                            H.Rows[("sprk_billingevent", copy.Id)] = new Entity("sprk_billingevent", copy.Id)
                            {
                                ["sprk_project"] = new EntityReference("sprk_project", W.Core),
                                ["sprk_invoice"] = new EntityReference("sprk_invoice", W.InvoiceId),
                            };
                        }

                        foreach (var update in requests.OfType<UpdateRequest>())
                        {
                            var row = RowOf(update.Target)!;
                            foreach (var (name, value) in update.Target.Attributes)
                            {
                                row[name] = value;
                            }

                            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
                        }
                    }

                    return Task.CompletedTask;
                });
        }

        private Entity? RowOf(Entity target) => target.LogicalName switch
        {
            "sprk_invoice" => World.Invoice,
            "sprk_invoicesubmission" => World.Submission?.Id == target.Id ? World.Submission : World.OtherSubmissions.SingleOrDefault(s => s.Id == target.Id),
            _ => World.Lines.SingleOrDefault(l => l.Id == target.Id),
        };

        private static DataverseTransactionException Mismatch(IReadOnlyList<OrganizationRequest> requests, OrganizationRequest failed) =>
            new("simulated row version mismatch: the transaction was rolled back", requests.ToList().IndexOf(failed), false,
                new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch }));

        public InvoiceLineAddService Service(params BillingRuleCandidate[] rules) => Service(Kit.Loaded(rules));

        public InvoiceLineAddService Service(BillingLoadedFacts loaded)
        {
            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new EbillingSettings { DefaultCodeSetId = World.DefaultCodeSetId });
            var codeSets = new CodeSetResolver(World.Entities.Object, settings.Object, new Mock<ICodeSetSuggestionProvider>().Object, NullLogger<CodeSetResolver>.Instance);
            var ai = new Mock<IInvoiceAi>();
            ai.Setup(a => a.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadOnlyMemory<float>([0f, 0f, 0f, 1f]));
            var resolution = new InvoiceResolutionService(
                World.Entities.Object, settings.Object, new TimekeeperMatcher(ai.Object, NullLogger<TimekeeperMatcher>.Instance), NullLogger<InvoiceResolutionService>.Instance);

            var recorder = new BillingDecisionRecorder(H.Writer, RecorderIdentity.Object, NullLogger<BillingDecisionRecorder>.Instance);
            var totals = new InvoiceTotalsService(Records.Object, World.Entities.Object, NullLogger<InvoiceTotalsService>.Instance);
            var evaluator = Kit.Evaluator();
            return new InvoiceLineAddService(
                World.Entities.Object, codeSets, resolution.ResolveAsync, (_, _) => Task.FromResult(loaded),
                (request, facts) => Shape is { } shape ? shape(request, evaluator.Evaluate(request, facts)) : evaluator.Evaluate(request, facts),
                recorder, Locks.Object, World.Ownership.Object, totals.RecalculateAsync, World.WriteSignalAsync, World.CloseSignalAsync,
                NullLogger<InvoiceLineAddService>.Instance, World.Time);
        }

        public JsonObject Findings() => JsonNode.Parse(World.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

        /// <summary>The Decision Records the writer created.</summary>
        public List<Entity> DecisionRecords() => H.Created.Where(c => c.LogicalName == "sprk_decisionrecord").ToList();
    }

    private static AddLineCommand Expense(
        Guid? requestId = null, decimal? amount = 75m, string? code = "E101", string? reason = "Courier charge missing from the bill",
        string? description = "Courier to court") =>
        new(W.InvoiceId, requestId ?? RequestId, "expense", new DateOnly(2026, 9, 14), null, null, null, amount, null, null, code, description, reason, W.Reviewer);

    private static AddLineCommand Fee(
        Guid? requestId = null, decimal? hours = 2m, decimal? rate = 100m, decimal? amount = null, string? task = "l110", string? activity = "A101",
        string? timekeeper = "A. Lee", DateOnly? date = null) =>
        new(W.InvoiceId, requestId ?? RequestId, "fee", date ?? new DateOnly(2026, 9, 12), timekeeper, hours, rate, amount, task, activity, null,
            "Prepare witness outline", "Work done but not billed", W.Reviewer);

    private static Guid ExpectedLineId(Guid? requestId = null) =>
        ExtractionProvenance.AddedLineIdFor(W.InvoiceId, InvoiceLineAddService.DecisionIdFor(W.InvoiceId, requestId ?? RequestId));

    // =========================================================================================
    // The id contract and the committed row (164a-2, EB-D-198)
    // =========================================================================================

    [Fact(DisplayName = "164a-4a: an added Expense line is created Flagged with the derived id and the reviewer-added marker, in one transaction")]
    public async Task AnExpenseLine_IsCreatedFlagged_WithTheDerivedId_AndTheMarker()
    {
        var add = new AddLineWorld();

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        result.AlreadyApplied.Should().BeFalse();
        result.LineId.Should().Be(ExpectedLineId());
        result.DecisionId.Should().Be(InvoiceLineAddService.DecisionIdFor(W.InvoiceId, RequestId));
        result.LineStatus.Should().Be("Flagged");

        var row = add.World.Lines.Single(l => l.Id == ExpectedLineId());
        ExtractionProvenance.IsReviewerAdded(row).Should().BeTrue("the row id and the marker name the same decision");
        row.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged);
        row.GetAttributeValue<OptionSetValue>("sprk_costtype").Value.Should().Be((int)InvoiceLineCostType.Expense);
        row.GetAttributeValue<Money>("sprk_amount").Value.Should().Be(75m);
        row.GetAttributeValue<int>("sprk_linesequence").Should().Be(5, "after the stored lines 3 and 4");
        row.GetAttributeValue<string>("sprk_expensecodeasbilled").Should().Be("E101", "D-117: the typed code is the as-billed code");
        row.GetAttributeValue<EntityReference>("sprk_project").Id.Should().Be(W.Core);
        row.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(W.Firm);
        row.Contains("ownerid").Should().BeTrue("the owner is resolved and applied before the create");
        row.Contains("sprk_resolvedtimekeeper").Should().BeFalse();

        var transaction = add.Transactions[0];
        add.Transactions.SelectMany(t => t).OfType<CreateRequest>().Should().ContainSingle("one line, created in the commit");
        transaction.OfType<CreateRequest>().Should().ContainSingle();
        transaction.OfType<UpdateRequest>().Should().OnlyContain(u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches);
        transaction.OfType<UpdateRequest>().Select(u => u.Target.LogicalName).Should().Contain(["sprk_invoicesubmission", "sprk_invoice"]);
        transaction.OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoice").Target.Attributes
            .Should().BeEmpty("the invoice update is version-only");

        var findings = add.Findings();
        JsonNode.Parse((string)transaction.OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoicesubmission").Target["sprk_findingsjson"])!
            [InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateCommitted, "the commit writes committed");
        findings[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateDone, "step 9 moves it on");
        findings[InvoiceLineAddService.AddLineMember]!["lineId"]!.GetValue<string>().Should().Be(ExpectedLineId().ToString("D"));
        findings["lineFindings"]!.AsArray().OfType<JsonObject>()
            .Should().Contain(e => e["line"]!.GetValue<int>() == 4 && e["code"]!.GetValue<string>() == InvoiceLineAddService.LineFindingReviewerAdded);
        add.DecisionRecords().Should().ContainSingle();
    }

    [Fact(DisplayName = "164a-3 open points 1 and 2: a Fee line keeps the typed codes as billed, the field confidence and the timekeeper reasons")]
    public async Task AFeeLine_StoresTheTypedCodes_TheFieldConfidence_AndTheTimekeeperReasons()
    {
        var add = new AddLineWorld();

        var result = await add.Service().AddAsync(Fee(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        var row = add.World.Lines.Single(l => l.Id == ExpectedLineId());
        row.GetAttributeValue<Money>("sprk_amount").Value.Should().Be(200m, "hours times rate");
        row.GetAttributeValue<string>("sprk_taskcodeasbilled").Should().Be("L110", "the canonical code of the set, as typed");
        row.GetAttributeValue<string>("sprk_activitycodeasbilled").Should().Be("A101");
        row.GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper")?.Id.Should().Be(add.Person);

        var extraction = JsonNode.Parse(row.GetAttributeValue<string>("sprk_extractionconfidence"))!.AsObject();
        extraction["provenance"]!["source"]!.GetValue<string>().Should().Be("reviewer");
        extraction["fields"]!["Hours"]!["combined"]!.GetValue<decimal>().Should().Be(1m);
        extraction["coding"]!["task"]!["code"]!.GetValue<string>().Should().Be("L110");
        extraction["coding"]!["task"]!["mismatch"]!.GetValue<bool>().Should().BeFalse();
        extraction["coding"]!["codeSetId"]!.GetValue<string>().Should().Be(W.CodeSetId.ToString("D"));
        extraction["resolutionChange"]!["after"]!["timekeeperReason"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    [Fact(DisplayName = "EB-D-198: a repeat of the same request answers alreadyApplied and writes nothing more")]
    public async Task ARepeat_IsAlreadyApplied_AndWritesNothingMore()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);

        var transactions = add.Transactions.Count;
        var repeat = await service.AddAsync(Expense(), CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        repeat.LineId.Should().Be(ExpectedLineId());
        add.Transactions.Should().HaveCount(transactions, "a repeat of a finished add writes nothing");
        add.World.Lines.Should().ContainSingle(l => l.Id == ExpectedLineId());
    }

    [Fact(DisplayName = "EB-D-198: the same request id with other content is 409 id_reused and writes nothing")]
    public async Task TheSameRequestId_WithOtherContent_IsIdReused()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);

        var transactions = add.Transactions.Count;

        var reused = await service.AddAsync(Expense(amount: 80m), CancellationToken.None);

        reused.Status.Should().Be(AddLineStatus.IdReused);
        reused.Code.Should().Be(AddLineCodes.IdReused);
        add.Transactions.Should().HaveCount(transactions);
    }

    [Fact(DisplayName = "N8 (version conditions): a change to the invoice between the read and the commit is 409 changed; nothing is created")]
    public async Task AnInvoiceChangedBeforeTheCommit_IsChanged_AndNothingIsCreated()
    {
        var add = new AddLineWorld();
        var linesBefore = add.World.Lines.Count;
        add.Fault = requests =>
        {
            add.World.Invoice["versionnumber"] = 6L; // a concurrent writer bumped the invoice after the read
            add.Fault = null;
            return null;
        };

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Changed);
        result.Code.Should().Be(AddLineCodes.Changed);
        add.World.Lines.Should().HaveCount(linesBefore);
    }

    // =========================================================================================
    // N3: the firm gate (EB-D-185 / EB-D-197) and N4: closed statuses
    // =========================================================================================

    public static TheoryData<string> GateStates() => new() { "NoFirm", "HeldForFirm", "RerunPending" };

    [Theory(DisplayName = "N3: no firm, held for a firm and the re-run pending each refuse 409 firm_missing; nothing written, no recorder call")]
    [MemberData(nameof(GateStates))]
    public async Task TheFirmGate_Refuses_AndWritesNothing(string state)
    {
        var add = new AddLineWorld();
        switch (state)
        {
            case "NoFirm":
                add.World.Invoice.Attributes.Remove("sprk_vendororg");
                break;
            case "HeldForFirm":
                add.World.Invoice[InvoiceFirmHold.InvoiceHoldColumn] = "ebilling.submission.firm_unresolved";
                break;
            default:
                add.World.Invoice[InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceRerunPendingValue;
                break;
        }

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.FirmMissing, state);
        result.Code.Should().Be("ebilling.line_add.firm_missing");
        add.Transactions.Should().BeEmpty();
        add.RecorderIdentity.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "EB-D-197: Add line is refused while sprk_firmhold holds the re-run-pending value, with the re-run sentence")]
    public async Task WhileTheRerunIsPending_AddLineIsRefused()
    {
        var add = new AddLineWorld();
        add.World.Invoice[InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceRerunPendingValue;

        var result = await add.Service().AddAsync(Fee(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.FirmMissing);
        result.Detail.Should().Be(AddLineCodes.FirmDetailFor(InvoiceFirmGateResult.RerunPending));
        add.Transactions.Should().BeEmpty();
        add.World.Lines.Should().NotContain(l => l.Id == ExpectedLineId());
    }

    [Theory(DisplayName = "N4: each closed status is 409 not_in_review and writes nothing")]
    [InlineData(InvoiceStatusCodes.Approved)]
    [InlineData(InvoiceStatusCodes.Rejected)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm)]
    [InlineData(InvoiceStatusCodes.Superseded)]
    [InlineData(InvoiceStatusCodes.SentToAp)]
    [InlineData(InvoiceStatusCodes.Paid)]
    public async Task AClosedInvoice_IsNotInReview(int status)
    {
        var add = new AddLineWorld();
        add.World.Invoice["statuscode"] = new OptionSetValue(status);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.NotInReview);
        result.Code.Should().Be("ebilling.invoice.not_in_review");
        add.Transactions.Should().BeEmpty();
    }

    // =========================================================================================
    // N5 and N6: the 400s (before any read) and code_set_unavailable
    // =========================================================================================

    public static TheoryData<AddLineCommand, string> BadRequests() => new()
    {
        { Expense(reason: null), "ebilling.line.reason_required" },
        { Expense(reason: "   "), "ebilling.line.reason_required" },
        { Expense(reason: new string('r', InvoiceLineAddService.ReasonMaxLength + 1)), "ebilling.line.reason_too_long" },
        { Expense(description: null), AddLineCodes.Invalid },
        { Expense(amount: null), AddLineCodes.Invalid },
        { Expense(amount: 0m), AddLineCodes.Invalid },
        { Expense(amount: 10.005m), AddLineCodes.Invalid },
        { Expense() with { CostType = "adjustment" }, AddLineCodes.Invalid },
        { Expense() with { Date = null }, AddLineCodes.Invalid },
        { Expense() with { Hours = 1m, Rate = 75m }, AddLineCodes.Invalid },
        { Expense() with { TaskCode = "L110" }, AddLineCodes.Invalid },
        { Fee(timekeeper: null), AddLineCodes.Invalid },
        { Fee(rate: null), AddLineCodes.Invalid },
        { Fee(hours: null, rate: null, amount: null), AddLineCodes.Invalid },
        { Fee(amount: 150m), AddLineCodes.AmountMismatch },
        { Fee() with { ExpenseCode = "E101" }, AddLineCodes.Invalid },
        { Fee(task: new string('L', InvoiceLineAddService.CodeMaxLength + 1)), AddLineCodes.Invalid },
    };

    [Theory(DisplayName = "N5 / N6: a malformed request is 400 with its stable code before any read")]
    [MemberData(nameof(BadRequests))]
    public async Task AMalformedRequest_Is400_BeforeAnyRead(AddLineCommand command, string code)
    {
        var add = new AddLineWorld();
        add.World.Entities.Invocations.Clear();

        var result = await add.Service().AddAsync(command, CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.InvalidRequest);
        result.Code.Should().Be(code);
        add.World.Entities.Invocations.Should().BeEmpty("step 0 reads nothing");
        add.Locks.Invocations.Should().BeEmpty("step 0 takes no lock");
    }

    [Fact(DisplayName = "N6: a code outside the invoice's code set is 400 code_not_allowed and is never stored")]
    public async Task ACodeOutsideTheSet_IsCodeNotAllowed()
    {
        var add = new AddLineWorld();

        var result = await add.Service().AddAsync(Fee(task: "L999"), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.InvalidRequest);
        result.Code.Should().Be(AddLineCodes.CodeNotAllowed);
        add.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "N6: no resolvable code set is 409 code_set_unavailable and writes nothing")]
    public async Task NoCodeSet_IsCodeSetUnavailable()
    {
        var add = new AddLineWorld();
        add.World.DefaultCodeSetId = null;
        add.World.CodeSets.Clear();
        add.World.Codes.Clear();

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.CodeSetUnavailable);
        result.Code.Should().Be(AddLineCodes.CodeSetUnavailable);
        add.Transactions.Should().BeEmpty();
    }

    // =========================================================================================
    // N9 and N11: a new gap, an unapplied correction, an unresolved owner
    // =========================================================================================

    [Fact(DisplayName = "N9: a line that brings in a Procedure gap is 409 procedure_gap; the findings are not patched")]
    public async Task ANewProcedureGap_IsRefused()
    {
        var add = new AddLineWorld();
        var stored = add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson");
        var kit = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 9, 15));
        kit.Rule("Ended", "ENDED-CAP", RuleType.DailyHoursCap);
        var loaded = Kit.Loaded() with { Procedures = kit.Build() };

        // Dated after the Procedure ends: the new line is a subject in a gap the stored findings do not have.
        var result = await add.Service(loaded).AddAsync(Fee(date: new DateOnly(2026, 9, 20)), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.ProcedureGap);
        result.Code.Should().Be(AddLineCodes.ProcedureGap);
        add.Transactions.Should().BeEmpty();
        add.World.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored);
    }

    [Fact(DisplayName = "N9: a correction saved but not followed through is 409 corrections_pending; nothing written")]
    public async Task AnUnappliedCorrection_IsRefused()
    {
        var add = new AddLineWorld();
        add.World.AddRow(new Entity("sprk_invoicecorrection", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", W.InvoiceId),
            ["sprk_invoiceline"] = new EntityReference("sprk_billingevent", W.FeeLineId),
            ["sprk_fieldname"] = "Hours",
            ["sprk_aivalue"] = "25",
            ["sprk_finalvalue"] = "2.5",
            ["sprk_outcometype"] = new OptionSetValue(InvoiceCorrectionService.OutcomeCorrected),
            ["createdon"] = W.T0,
        });

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.CorrectionsPending);
        result.Code.Should().Be("ebilling.line.corrections_pending");
        add.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "N11: an unresolved owner is OwnerUnresolved (500) and nothing is created")]
    public async Task AnUnresolvedOwner_IsRefused()
    {
        var add = new AddLineWorld();
        add.World.Owner = RecordOwnerResolution.Refused("record_owner.no_parent", "no parent");

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.OwnerUnresolved);
        result.Code.Should().Be("ebilling.decision.owner_unresolved");
        add.Transactions.Should().BeEmpty();
        var http = Sprk.Bff.Api.Api.Finance.InvoiceLineReviewEndpoints.ToResult(result, new Microsoft.AspNetCore.Http.DefaultHttpContext(), RequestId)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        http.StatusCode.Should().Be(500);
    }

    [Fact(DisplayName = "The rules: a reviewer-added Fee line over the daily cap is found by the rule like any line")]
    public async Task TheRules_JudgeTheAddedLine()
    {
        var add = new AddLineWorld();

        // 13 hours on one day for one timekeeper: over the 12-hour cap on the added line alone.
        var result = await add.Service(Cap).AddAsync(Fee(hours: 13m), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.Findings()["findings"]!.AsArray().OfType<JsonObject>()
            .Should().Contain(f => f["line"] != null && f["line"]!.GetValue<int>() == 4,
                "the evaluator ran over the stored lines plus the new one");
    }

    // =========================================================================================
    // 164a-4b: steps 7 to 9 (totals, the add-line record, the Signals), the resume, N7, N8, N10
    // =========================================================================================

    private const string LineEntity = BillingFindingSignalMapper.LineEntity;
    private const string CapFingerprint = BillingReasonCodes.DailyHoursOverCap + "|Flag";

    /// <summary>The stored plan entry of the stored fee line's daily-cap group (index 2), as 049 writes it.</summary>
    private static JsonObject FeeCapPlan(string fingerprint = CapFingerprint, string? decision = null)
    {
        var entry = new JsonObject
        {
            ["policyId"] = Cap.PolicyId.ToString("D"),
            ["policyCode"] = Cap.PolicyCode,
            ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"),
            ["subjectEntity"] = LineEntity,
            ["subjectId"] = W.FeeLineId.ToString("D"),
            ["fingerprint"] = fingerprint,
        };
        if (decision is not null)
        {
            entry["decision"] = decision;
        }

        return entry;
    }

    private static void StoreRoot(AddLineWorld add, Action<JsonObject> change)
    {
        var root = add.Findings();
        change(root);
        add.World.Submission!["sprk_findingsjson"] = root.ToJsonString();
    }

    [Fact(DisplayName = "T1: a Fee line is finished - Q1 totals stored, one add-line record (the line, the caller, the reason), after the line write")]
    public async Task T1_AFeeLine_StoresQ1Totals_AndOneAddLineRecord_AfterTheLineWrite()
    {
        var add = new AddLineWorld();

        var result = await add.Service().AddAsync(Fee(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        var row = add.World.Lines.Single(l => l.Id == ExpectedLineId());
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column).Id.Should().Be(W.Reviewer, "sprk_createdbyperson is the caller");

        // EB-D-199: billed (2500) + the added line's 200; the added line stays outside the fees total (the firm's 2500) and the expenses (40).
        result.Totals.Should().Be(new ReviewTotalsDto(2500m, 2500m, 40m, 0m, 0m, 2700m));
        var stored = add.TotalsWrites.Should().ContainSingle().Subject;
        stored["sprk_approvedtotal"].Should().Be(2700m);
        stored["sprk_feestotal"].Should().Be(2500m);

        var record = add.DecisionRecords().Should().ContainSingle().Subject;
        record.GetAttributeValue<string>("sprk_actioncode").Should().Be(InvoiceLineAddService.ActionCode);
        record.GetAttributeValue<EntityReference>("sprk_regardinginvoiceline").Id.Should().Be(ExpectedLineId(), "D-75: the subject is the new line");
        record.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(W.Reviewer, "the decider is the caller");
        record.GetAttributeValue<string>("sprk_reason").Should().Contain("Work done but not billed");
        var facts = record.GetAttributeValue<string>("sprk_factsnapshot");
        facts.Should().Contain(ExpectedLineId().ToString("D")).And.NotContain("Prepare witness outline").And.NotContain("A. Lee");
        add.Order.Should().Equal(["commit", "totals", "record"], "R8: the record only after the line write and the totals");
    }

    [Fact(DisplayName = "T2: an Expense line is finished - the approved total takes its amount, billed and expenses unchanged, one record")]
    public async Task T2_AnExpenseLine_StoresQ1Totals_AndOneAddLineRecord()
    {
        var add = new AddLineWorld();

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        result.Totals.Should().Be(new ReviewTotalsDto(2500m, 2500m, 40m, 0m, 0m, 2575m));
        add.DecisionRecords().Should().ContainSingle().Which.GetAttributeValue<string>("sprk_actioncode").Should().Be("add-line");
    }

    [Fact(DisplayName = "O3 / uac A5: the add-line decision - the caller, the reason, no resolved Signals, facts of ids and codes only")]
    public void TheDecision_NamesTheCaller_AndHoldsIdsAndCodesOnly()
    {
        InvoiceLineAddService.ShapeError(Fee(), out var shape).Should().BeNull();
        var decisionId = InvoiceLineAddService.DecisionIdFor(W.InvoiceId, RequestId);

        var decision = InvoiceLineAddService.BuildDecision(
            W.InvoiceId, decisionId, ExpectedLineId(), 5, shape!,
            new Dictionary<BillingCodeKind, string?> { [BillingCodeKind.Task] = "L110", [BillingCodeKind.Activity] = "A101", [BillingCodeKind.Expense] = null },
            Guid.Empty, ["b", "a", "b"], W.Reviewer, W.T0);

        decision.ActionCode.Should().Be("add-line");
        decision.ResolvedSignalIds.Should().BeNullOrEmpty();
        decision.Reason.Should().Be("Work done but not billed");
        decision.Facts.Keys.Should().BeEquivalentTo(
            ["invoiceId", "lineId", "lineSequence", "costType", "taskCode", "activityCode", "expenseCode", "timekeeperId", "ruleReasonCodes"]);
        decision.Facts["timekeeperId"].Should().BeNull();
        decision.Facts["ruleReasonCodes"].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact(DisplayName = "T3: a rule on the new line raises its Signal keyed {policy}|sprk_billingevent|{lineId}|1; the pending change is cleared")]
    public async Task T3_ARuleOnTheNewLine_RaisesEpisodeOne()
    {
        var add = new AddLineWorld();

        var result = await add.Service(Cap).AddAsync(Fee(hours: 13m, date: new DateOnly(2026, 9, 14)), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.World.SignalWrites.Should().Contain((Cap.PolicyCode, ExpectedLineId(), 1));
        add.World.Signals.Select(s => s.GetAttributeValue<string>("sprk_dedupekey"))
            .Should().Contain(SignalWriter.BuildDedupeKey(Cap.PolicyCode, LineEntity, ExpectedLineId().ToString("D").ToLowerInvariant(), 1));
        SignalWriter.BuildDedupeKey(Cap.PolicyCode, LineEntity, ExpectedLineId().ToString("D").ToLowerInvariant(), 1)
            .Should().EndWith($"|sprk_billingevent|{ExpectedLineId():D}|1".ToLowerInvariant(), "the D-13 key: policy, the line, episode 1");
        add.World.SignalCloses.Should().BeEmpty();
        add.Findings().ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse("step 9 reconciles and removes the change");
        add.DecisionRecords().Should().ContainSingle("Signals are written record-free");
    }

    /// <summary>T4's shape: once the line is added the rules find a second reason on the stored fee line's daily-cap group (any rule over several
    /// lines can do this), so the group's reason set, and with it its fingerprint, changes.</summary>
    private static BillingEvaluationResult SecondReasonOnTheFeeLine(BillingEvaluationRequest request, BillingEvaluationResult result) =>
        request.Lines.Count > 2
            ? result with
            {
                Findings = result.Findings
                    .Concat(result.Findings.Where(f => f.Subject.LineIndex == 2 && f.PolicyCode == Cap.PolicyCode)
                        .Select(f => f with { ReasonCode = BillingReasonCodes.TimekeeperUnmatched }))
                    .ToList(),
            }
            : result;

    [Theory(DisplayName = "T4: a changed reason set is Superseded (no record) then episode n+1; must-not-fire: an unchanged set refreshes the open episode in place")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task T4_AChangedSet_IsSuperseded_AnUnchangedSet_IsRefreshed(bool changed)
    {
        // The reason set after the add, as the plan stores it (read from a first world).
        var probe = new AddLineWorld { Shape = SecondReasonOnTheFeeLine };
        StoreRoot(probe, root => root["signalPlan"] = new JsonArray(FeeCapPlan()));
        (await probe.Service(Cap).AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        var newFingerprint = probe.Findings()["signalPlan"]!.AsArray().OfType<JsonObject>()
            .Single(e => e["subjectId"]!.GetValue<string>() == W.FeeLineId.ToString("D"))["fingerprint"]!.GetValue<string>();
        newFingerprint.Should().NotBe(CapFingerprint);

        var add = new AddLineWorld { Shape = SecondReasonOnTheFeeLine };
        StoreRoot(add, root => root["signalPlan"] = new JsonArray(FeeCapPlan()));
        var open = changed ? CapFingerprint : newFingerprint; // must-not-fire: the open episode already carries the set the add leaves
        add.World.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, open);

        var result = await add.Service(Cap).AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        if (changed)
        {
            add.World.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal(
                (1, true, SignalWriter.ResolutionSuperseded, CapFingerprint),
                (2, false, (int?)null, newFingerprint));
        }
        else
        {
            add.World.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal((1, false, (int?)null, newFingerprint));
            add.World.SignalCloses.Should().BeEmpty("the same reason set is refreshed in place, never closed");
            add.World.SignalWrites.Should().Equal([(Cap.PolicyCode, W.FeeLineId, 1)], "the open episode is written again (its last-evaluated time)");
        }

        add.DecisionRecords().Should().ContainSingle("a supersede writes no record: the one record is the add-line record");
    }

    [Fact(DisplayName = "T5: a group the add clears is closed ConditionCleared, record-free and unlinked")]
    public async Task T5_AClearedGroup_IsConditionCleared_RecordFree()
    {
        var add = new AddLineWorld();
        StoreRoot(add, root => root["signalPlan"] = new JsonArray(FeeCapPlan()));
        add.World.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);

        // The rules (any rule over several lines) stop finding the stored fee line once the line is added: the after side has no finding on it.
        add.Shape = (request, result) => request.Lines.Count > 2
            ? result with { Findings = result.Findings.Where(f => f.Subject.LineIndex != 2).ToList() }
            : result;

        var result = await add.Service(Cap).AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.World.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal((1, true, SignalWriter.ResolutionConditionCleared, CapFingerprint));
        add.DecisionRecords().Should().ContainSingle("ConditionCleared is record-free: the one record is the add-line record")
            .Which.GetAttributeValue<string>("sprk_actioncode").Should().Be("add-line");
    }

    [Theory(DisplayName = "T6 (Q7 must-not-fire): on a held invoice no hold link and no ConditionCleared; the Blocked entry and the override stamp stay byte-identical")]
    [InlineData("expense")]
    [InlineData("fee")]
    public async Task T6_OnAHeldInvoice_NothingIsCleared_AndTheHoldIsByteIdentical(string kind)
    {
        var add = new AddLineWorld();
        StoreRoot(add, root =>
        {
            root["kind"] = "Blocked";
            root["decision"] = new JsonObject { ["outcome"] = "Rejected", ["deciding"] = new JsonArray(0) };
            root["findings"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["ruleType"] = RuleType.DailyHoursCap.ToString(), ["policyId"] = Cap.PolicyId.ToString("D"), ["policyCode"] = Cap.PolicyCode,
                ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"), ["subject"] = "Line", ["line"] = 2, ["reasonCode"] = BillingReasonCodes.DailyHoursOverCap,
                ["consequence"] = "Reject", ["rejects"] = true,
            });
            root["signalPlan"] = new JsonArray(FeeCapPlan(decision: nameof(BillingGroupDecision.Blocked)));
            root["override"] = new JsonObject { ["decisionId"] = Guid.Parse("16400000-0000-4000-8000-0000000000aa").ToString("D"), ["on"] = "2026-10-30" };
        });
        add.World.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);
        var before = add.Findings();
        var blocked = before["signalPlan"]![0]!.ToJsonString();
        var stamp = before["override"]!.ToJsonString();

        var result = await add.Service(Cap).AddAsync(kind == "fee" ? Fee(hours: 2m, date: new DateOnly(2026, 9, 14)) : Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        var after = add.Findings();
        after["signalPlan"]!.AsArray().OfType<JsonObject>()
            .Single(e => e["subjectId"]!.GetValue<string>() == W.FeeLineId.ToString("D")).ToJsonString().Should().Be(blocked);
        after["override"]!.ToJsonString().Should().Be(stamp);
        add.World.SignalCloses.Should().BeEmpty("no Signal is closed, ConditionCleared or otherwise");
        add.World.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal((1, false, (int?)null, CapFingerprint));
        add.DecisionRecords().Should().ContainSingle("no link record: the one record is the add-line record")
            .Which.GetAttributeValue<string>("sprk_actioncode").Should().Be("add-line");
    }

    [Fact(DisplayName = "T6c (EB-D-192): a reviewer-typed code is judged by the same guideline rule as an imported line's, and raises its Signal")]
    public async Task T6c_AReviewerTypedCode_IsJudgedLikeAnImportedOne()
    {
        var nonBillable = Kit.Version("BILL-NB", RuleType.NonBillableActivity);
        var add = new AddLineWorld();

        var result = await add.Service(nonBillable).AddAsync(Fee(activity: "A101", date: new DateOnly(2026, 9, 14)), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Applied, result.Detail);
        add.Findings()["findings"]!.AsArray().OfType<JsonObject>()
            .Should().Contain(f => f["line"] != null && f["line"]!.GetValue<int>() == 4 && f["reasonCode"]!.GetValue<string>() == BillingReasonCodes.NonBillableCode);
        add.World.SignalWrites.Should().Contain((nonBillable.PolicyCode, ExpectedLineId(), 1));
    }

    [Fact(DisplayName = "N7: a repeat is alreadyApplied - one row, one record, no new Signal episode, no write; another reason is 409 id_reused")]
    public async Task N7_ARepeat_IsOneRowOneRecordNoNewEpisode_AndAnotherReasonIsIdReused()
    {
        var add = new AddLineWorld();
        var service = add.Service(Cap);
        var command = Fee(hours: 13m, date: new DateOnly(2026, 9, 14));
        (await service.AddAsync(command, CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        var writes = add.World.SignalWrites.Count;
        var transactions = add.Transactions.Count;

        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        repeat.Totals!.Approved.Should().Be(3800m, "the totals are answered from the rows (2500 + 13 x 100)");
        add.World.Lines.Should().ContainSingle(l => l.Id == ExpectedLineId());
        add.DecisionRecords().Should().ContainSingle();
        add.World.SignalWrites.Should().HaveCount(writes);
        add.Transactions.Should().HaveCount(transactions);
        add.TotalsWrites.Should().ContainSingle();

        var otherReason = await service.AddAsync(command with { Reason = "Another reason" }, CancellationToken.None);

        otherReason.Status.Should().Be(AddLineStatus.IdReused, "the recorder's hash sees the other reason");
        add.DecisionRecords().Should().ContainSingle();
    }

    [Fact(DisplayName = "N8: a submission changed between the read and the commit is 409 changed - no row, no record, no Signal")]
    public async Task N8_ASubmissionChangedBeforeTheCommit_IsChanged()
    {
        var add = new AddLineWorld();
        var linesBefore = add.World.Lines.Count;
        add.Fault = _ =>
        {
            add.World.Submission!["versionnumber"] = 31L;
            add.Fault = null;
            return null;
        };

        var result = await add.Service(Cap).AddAsync(Fee(hours: 13m, date: new DateOnly(2026, 9, 14)), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Changed);
        add.World.Lines.Should().HaveCount(linesBefore);
        add.DecisionRecords().Should().BeEmpty();
        add.World.SignalWrites.Should().BeEmpty();
        add.TotalsWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "N8: a held lock is 409 busy and reads nothing")]
    public async Task N8_AHeldLock_IsBusy()
    {
        var add = new AddLineWorld();
        add.Locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await add.Service().AddAsync(Expense(), CancellationToken.None);

        result.Status.Should().Be(AddLineStatus.Busy);
        result.Code.Should().Be(AddLineCodes.Busy);
        add.Transactions.Should().BeEmpty();
    }

    public static TheoryData<string> FailingSteps() => new() { "totals", "record", "signals" };

    [Theory(DisplayName = "N10: a fault at totals, record or signals is 503 incomplete naming the step; the repeat completes it with one record")]
    [MemberData(nameof(FailingSteps))]
    public async Task N10_AFaultAfterTheCommit_IsIncomplete_AndTheRepeatCompletesIt(string step)
    {
        var add = new AddLineWorld();
        var service = add.Service(Cap);
        var command = Fee(hours: 13m, date: new DateOnly(2026, 9, 14));
        switch (step)
        {
            case "totals": add.TotalsFault = new TimeoutException(); break;
            case "record": add.H.LookupFault = new TimeoutException(); break;
            default: add.World.SignalWriteFault = _ => new TimeoutException(); break;
        }

        var failed = await service.AddAsync(command, CancellationToken.None);

        failed.Status.Should().Be(AddLineStatus.Incomplete);
        failed.Code.Should().Be("ebilling.decision.incomplete");
        failed.FailedStep.Should().Be(step);
        failed.LineId.Should().Be(ExpectedLineId());
        var http = Sprk.Bff.Api.Api.Finance.InvoiceLineReviewEndpoints.ToResult(failed, new Microsoft.AspNetCore.Http.DefaultHttpContext(), RequestId)
            .Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        http.StatusCode.Should().Be(503);
        http.ProblemDetails.Extensions["failedStep"].Should().Be(step);
        add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateCommitted);

        add.TotalsFault = null;
        add.H.LookupFault = null;
        add.World.SignalWriteFault = null;
        var repeat = await service.AddAsync(command, CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied, repeat.Detail);
        repeat.AlreadyApplied.Should().BeTrue();
        add.World.Lines.Should().ContainSingle(l => l.Id == ExpectedLineId());
        add.DecisionRecords().Should().ContainSingle("exactly one record");
        add.World.SignalsOf(Cap.PolicyCode, ExpectedLineId()).Should().ContainSingle("episode 1, once");
        add.Findings()[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateDone);
        add.Findings().ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
    }

    [Fact(DisplayName = "164a-4b: a second add overwrites addLine; a repeat of the first still answers alreadyApplied with one record")]
    public async Task ASecondAdd_OverwritesAddLine_AndTheFirstRepeatStillConverges()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        var second = Guid.Parse("16400000-0000-4000-8000-000000000002");
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        (await service.AddAsync(Expense(requestId: second, amount: 20m), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        add.Findings()[InvoiceLineAddService.AddLineMember]!["lineId"]!.GetValue<string>().Should().Be(ExpectedLineId(second).ToString("D"));

        var repeat = await service.AddAsync(Expense(), CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied, repeat.Detail);
        repeat.AlreadyApplied.Should().BeTrue();
        add.DecisionRecords().Should().HaveCount(2, "one record per add");
        add.World.Lines.Count(l => ExtractionProvenance.IsReviewerAdded(l)).Should().Be(2);
    }

    [Fact(DisplayName = "T7 (R10): the added line is created Flagged (the approval and Accept all Clean seam: T7_ApprovalWithAnAddedLine_...)")]
    public async Task T7_TheAddedLine_IsCreatedFlagged()
    {
        var add = new AddLineWorld();
        (await add.Service().AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);
        var row = add.World.Lines.Single(l => l.Id == ExpectedLineId());

        row.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value.Should().Be((int)InvoiceLineStatus.Flagged,
            "R10: created Flagged, so it is decided like any line before approval");
    }
}
