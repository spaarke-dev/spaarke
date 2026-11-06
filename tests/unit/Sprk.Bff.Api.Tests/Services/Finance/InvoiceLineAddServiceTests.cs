using System.ServiceModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
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
public sealed class InvoiceLineAddServiceTests
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

        public AddLineWorld()
        {
            World.DefaultCodeSetId = W.CodeSetId;
            InvoiceCorrectionReevaluationTests.Store(World);
            Person = World.AddPerson("A. Lee").Id;
            World.AddRate(Person, 100m);
            Locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

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
                            if (RowOf(update.Target) is not { } row
                                || update.ConcurrencyBehavior != ConcurrencyBehavior.IfRowVersionMatches
                                || update.Target.RowVersion != row.GetAttributeValue<long>("versionnumber").ToString())
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
            "sprk_invoicesubmission" => World.Submission,
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

            // The recorder's identity is STRICT: 4a only preflights (no I/O), so any record write would fail the test (N3: no recorder call).
            var recorder = new BillingDecisionRecorder(
                DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness()).Writer, RecorderIdentity.Object,
                NullLogger<BillingDecisionRecorder>.Instance);
            return new InvoiceLineAddService(
                World.Entities.Object, codeSets, resolution.ResolveAsync, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate,
                recorder, Locks.Object, World.Ownership.Object, NullLogger<InvoiceLineAddService>.Instance, World.Time);
        }

        public JsonObject Findings() => JsonNode.Parse(World.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();
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
        ExtractionProvenance.AddedLineIdFor(InvoiceLineAddService.DecisionIdFor(W.InvoiceId, requestId ?? RequestId));

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

        var transaction = add.Transactions.Should().ContainSingle().Subject;
        transaction.OfType<CreateRequest>().Should().ContainSingle();
        transaction.OfType<UpdateRequest>().Should().OnlyContain(u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches);
        transaction.OfType<UpdateRequest>().Select(u => u.Target.LogicalName).Should().Contain(["sprk_invoicesubmission", "sprk_invoice"]);
        transaction.OfType<UpdateRequest>().Single(u => u.Target.LogicalName == "sprk_invoice").Target.Attributes
            .Should().BeEmpty("the invoice update is version-only");

        var findings = add.Findings();
        findings[InvoiceLineAddService.AddLineMember]!["state"]!.GetValue<string>().Should().Be(InvoiceLineAddService.StateCommitted);
        findings[InvoiceLineAddService.AddLineMember]!["lineId"]!.GetValue<string>().Should().Be(ExpectedLineId().ToString("D"));
        findings["lineFindings"]!.AsArray().OfType<JsonObject>()
            .Should().Contain(e => e["line"]!.GetValue<int>() == 4 && e["code"]!.GetValue<string>() == InvoiceLineAddService.LineFindingReviewerAdded);
        add.RecorderIdentity.VerifyNoOtherCalls();
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

        var repeat = await service.AddAsync(Expense(), CancellationToken.None);

        repeat.Status.Should().Be(AddLineStatus.Applied);
        repeat.AlreadyApplied.Should().BeTrue();
        repeat.LineId.Should().Be(ExpectedLineId());
        add.Transactions.Should().ContainSingle();
        add.World.Lines.Should().ContainSingle(l => l.Id == ExpectedLineId());
    }

    [Fact(DisplayName = "EB-D-198: the same request id with other content is 409 id_reused and writes nothing")]
    public async Task TheSameRequestId_WithOtherContent_IsIdReused()
    {
        var add = new AddLineWorld();
        var service = add.Service();
        (await service.AddAsync(Expense(), CancellationToken.None)).Status.Should().Be(AddLineStatus.Applied);

        var reused = await service.AddAsync(Expense(amount: 80m), CancellationToken.None);

        reused.Status.Should().Be(AddLineStatus.IdReused);
        reused.Code.Should().Be(AddLineCodes.IdReused);
        add.Transactions.Should().ContainSingle();
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
}
