using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095d-1b (notes 095c 16.4 to 16.7): <see cref="InvoiceAssignFirmService"/>, Set firm on a held no-firm invoice. Over an
/// in-memory Dataverse (<see cref="World"/>); the allocator is mocked to capture the one apply it is handed, except in the tests that run the
/// REAL <see cref="TrackingNumberAllocator"/> to prove what the one transaction sends.
/// </summary>
/// <remarks>
/// <para><b>Refusals (16.4 condition 1).</b> Each refusal has a must-fire test (refused, and NOTHING is written: no allocator renumber and no
/// Dataverse write) and a must-not-fire control next to it (the neighbouring case is assigned).</para>
/// <para><b>Seed proofs</b> (run on 2026-11-04, then restored byte-identical): removing the not-held check makes
/// <c>Refused_NotHeld</c> fail (the apply is sent); removing the <c>sprk_firmhold</c> clear from the apply makes
/// <c>RealAllocator_TheOneTransaction_ClearsTheHold_AndCarriesTheStoredDecision</c> fail.</para>
/// </remarks>
public sealed partial class InvoiceAssignFirmServiceTests
{
    internal static readonly Guid SubmissionId = Guid.Parse("095d1b00-0000-4000-8000-000000000001");
    internal static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
    internal static readonly Guid Firm = Guid.Parse("095d1b00-0000-4000-8000-0000000000f1");
    internal static readonly Guid OtherFirm = Guid.Parse("095d1b00-0000-4000-8000-0000000000f2");
    internal static readonly Guid Matter = Guid.Parse("095d1b00-0000-4000-8000-0000000000a1");
    internal static readonly Guid Decider = Guid.Parse("095d1b00-0000-4000-8000-0000000000d1");
    /// <summary>095d-1c fix round 1: a second reviewer, for the repeat that must record the FIRST reviewer's stored decision.</summary>
    internal static readonly Guid OtherCaller = Guid.Parse("095d1b00-0000-4000-8000-0000000000d2");
    internal static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 30, 15, 500, TimeSpan.Zero);

    // ── The world ──

    internal sealed class World
    {
        public Entity? Invoice = new(InvoiceReviewReadService.InvoiceEntity, InvoiceId)
        {
            ["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateActive),
            ["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview),
            ["versionnumber"] = 5L,
            [InvoiceReviewService.InvTrackingNumber] = "INV-7.3",
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
            [InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceHoldValue,
        };

        public List<Entity> Submissions =
        [
            new(TrackingNumberAllocator.SubmissionEntity, SubmissionId)
            {
                ["versionnumber"] = 11L,
                [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference(InvoiceReviewReadService.InvoiceEntity, InvoiceId),
                [TrackingNumberAllocator.ScopeColumn] = TrackingNumberAllocator.NoFirmScope,
                [TrackingNumberAllocator.SequenceColumn] = 3,
                [TrackingNumberAllocator.TrackingNumberColumn] = "INV-7.3",
                [TrackingNumberAllocator.FirmInvoiceNumberColumn] = "INV-7",
                [InvoiceSubmissionPersister.SubmissionReasonCode] = SubmissionReasonCodes.FirmMissing,
                [InvoiceSubmissionPersister.SubmissionFindingsJson] = """{"trackingNumber":"INV-7.3","pipelineFindings":[{"code":"firm_missing"}],"keep":1}""",
            },
        ];

        public List<Entity> Lines =
        [
            new(InvoiceSubmissionPersister.LineEntity, InvoiceSubmissionPersister.LineIdFor(SubmissionId, 0)) { [InvoiceSubmissionPersister.LineName] = "INV-7.3 #1" },
            new(InvoiceSubmissionPersister.LineEntity, InvoiceSubmissionPersister.LineIdFor(SubmissionId, 1)) { [InvoiceSubmissionPersister.LineName] = "INV-7.3 #2" },
        ];

        public List<Entity> Approvals =
        [
            new(InvoiceSubmissionPersister.ApprovalEntity, InvoiceSubmissionPersister.ApprovalIdFor(SubmissionId)) { [InvoiceSubmissionPersister.ApprovalName] = "Approval INV-7.3" },
        ];

        public List<Entity> Files =
        [
            new(StoredFileSchema.Entity, Guid.Parse("095d1b00-0000-4000-8000-0000000000e1")) { [StoredFileSchema.Name] = "INV-7.3.pdf" },
            new(StoredFileSchema.Entity, Guid.Parse("095d1b00-0000-4000-8000-0000000000e2")) { [StoredFileSchema.Name] = "INV-7.3-2.docx" },
            new(StoredFileSchema.Entity, Guid.Parse("095d1b00-0000-4000-8000-0000000000e3")) { [StoredFileSchema.Name] = "INV-7.30.pdf" },
        ];

        public List<Entity> Signals = [];

        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly Mock<ITrackingNumberAllocator> Allocator = new();
        public readonly FakeLocks Locks = new();
        public readonly List<IReadOnlyList<OrganizationRequest>> Transactions = [];

        public bool P2 = true;
        public bool? ActiveFirm = true;
        public InvoiceFirmLink Link = InvoiceFirmLink.Linked;
        public DecisionCallerRefusal CloseRights = DecisionCallerRefusal.None;
        public CoreAncestorResult Filing = new(CoreAncestorStatus.Derived, [new CoreAncestorStamp("sprk_matter", "sprk_matter", Matter)], null);
        /// <summary>Null (the default): the REAL <see cref="BillingDecisionRecorder.Preflight"/> over the real writer (095d-1b review F1: a no-op
        /// stub let a decision the writer refuses pass). A test that needs a refusal sets its own.</summary>
        public Action<BillingDecision>? Preflight;
        private BillingDecisionRecorder? _recorder;
        public Func<QueryExpression, bool> ReadFault = _ => false;
        public readonly List<BillingDecision> Preflighted = [];
        public readonly List<string> Calls = [];

        /// <summary>095d-1c: the Decision Record id the fake recorder answers.</summary>
        public static readonly Guid RecordId = Guid.Parse("095d1c00-0000-4000-8000-0000000000dd");

        /// <summary>095d-1c: every decision handed to the recorder, in order (a failed attempt included).</summary>
        public readonly List<BillingDecision> Recorded = [];

        /// <summary>095d-1c: what the recorder does; null (the default) writes the record once per decision id and answers AlreadyRecorded after,
        /// as the writer does. A test that needs a failure throws from it.</summary>
        public Func<BillingDecision, BillingDecisionRecordResult>? OnRecord;
        private readonly HashSet<Guid> _written = [];

        /// <summary>What the mocked allocator answers; by default it builds the apply for INV-7.1 and answers Renumbered.</summary>
        public Func<Func<RenumberCandidate, RenumberApply>, RenumberResult>? OnRenumber;
        public RenumberApply? Applied;
        public Func<RenumberCandidate, RenumberApply>? Builder;
        public (string Version, Guid Firm, string Number)? RenumberArgs;

        public World()
        {
            Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression q, CancellationToken _) => Route(q));
            Entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyList<OrganizationRequest> r, CancellationToken _) => Transactions.Add(r))
                .Returns(Task.CompletedTask);

            Allocator.Setup(a => a.NumbersSubmissionsWithoutFirmAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => P2);
            Allocator.Setup(a => a.RenumberSubmissionAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<Func<RenumberCandidate, RenumberApply>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string version, Guid firm, string number, Func<RenumberCandidate, RenumberApply> build, CancellationToken _) =>
                {
                    RenumberArgs = (version, firm, number);
                    Builder = build;
                    if (OnRenumber is { } custom)
                    {
                        return custom(build);
                    }

                    Applied = build(new RenumberCandidate("INV-7.1", 1));
                    return RenumberResult.Renumbered("INV-7.1", 1);
                });
        }

        public InvoiceAssignFirmService Service(ITrackingNumberAllocator? allocator = null) => new(
            Entities.Object,
            allocator ?? Allocator.Object,
            Locks,
            d => { Preflighted.Add(d); (Preflight ?? RealPreflight)(d); },
            (d, _) => { Calls.Add("record"); Recorded.Add(d); return Task.FromResult((OnRecord ?? WriteOnce)(d)); },
            (_, _) => { Calls.Add("active"); return Task.FromResult(ActiveFirm); },
            (_, _, _) => { Calls.Add("link"); return Task.FromResult(Link); },
            (_, _, _, _) => { Calls.Add("rights"); return Task.FromResult(CloseRights); },
            (_, _) => { Calls.Add("filing"); return Task.FromResult(Filing); },
            Log,
            new FixedTime(Now))
        {
            ReviewDetailProbe = ReviewDetailProbe,
        };

        /// <summary>082c-2: whether the review detail column exists (fixed per test; never the process-wide probe).</summary>
        public ReviewDetailColumnProbe ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(present: true);

        /// <summary>095d-1c fix round 1: what the service logged (level, formatted text, structured parameters).</summary>
        public readonly CapturingLogger Log = new();

        public Task<AssignFirmResult> Assign(
            Guid? firm = null, string? note = "Firm confirmed by phone", ITrackingNumberAllocator? allocator = null,
            Guid? deciderUserId = null, CancellationToken ct = default) =>
            Service(allocator).SetFirmAsync(InvoiceId, firm ?? Firm, note, deciderUserId ?? Decider, "caller-token", ct);

        public Entity Submission => Submissions[0];

        /// <summary>The writer's exactly-once answer (per decision id): written the first time, already recorded after.</summary>
        private BillingDecisionRecordResult WriteOnce(BillingDecision decision) =>
            new(RecordId, !_written.Add(decision.DecisionId), RecordedDecisionClass.Judgement);

        private void RealPreflight(BillingDecision decision) =>
            (_recorder ??= new BillingDecisionRecorder(
                DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness()).Writer,
                new Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>().Object,
                NullLogger<BillingDecisionRecorder>.Instance)).Preflight(decision);

        public Guid AddFirmMissingSignal(string policy = BillingRuleCatalogue.SystemPolicyCode)
        {
            var id = Guid.NewGuid();
            Signals.Add(new Entity("sprk_signal", id)
            {
                ["sprk_dedupekey"] = SignalWriter.BuildDedupeKey(policy, "sprk_invoice", InvoiceId.ToString("D"), 1),
                ["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusOpen),
                ["sprk_regardingrecordid"] = InvoiceId.ToString("D"),
                [SignalWriter.RegardingLookupByEntity["sprk_invoice"]] = new EntityReference("sprk_invoice", InvoiceId),
            });
            return id;
        }

        /// <summary>Nothing was written: no renumber reached the allocator and no Dataverse write was made.</summary>
        public void ShouldHaveWrittenNothing()
        {
            Allocator.Verify(a => a.RenumberSubmissionAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<Func<RenumberCandidate, RenumberApply>>(), It.IsAny<CancellationToken>()), Times.Never);
            Entities.Invocations.Where(i => i.Method.Name != nameof(IGenericEntityService.RetrieveMultipleAsync)).Should().BeEmpty("a refusal writes nothing");
            Transactions.Should().BeEmpty();
        }

        private EntityCollection Route(QueryExpression q)
        {
            if (ReadFault(q))
            {
                throw new TimeoutException("read fault");
            }

            var columns = q.ColumnSet.Columns;
            IEnumerable<Entity> rows = q.EntityName switch
            {
                "sprk_invoice" => Invoice is null ? [] : [Invoice],
                "sprk_invoicesubmission" when columns.Contains("versionnumber") || columns.Contains(InvoiceSubmissionPersister.SubmissionReasonCode) => Submissions,
                "sprk_invoicesubmission" => [], // the allocator's highest-sequence read: no number taken yet in the firm's scope
                "sprk_billingevent" => Lines,
                "sprk_invoiceapproval" => Approvals,
                "sprk_storedfile" => Files,
                "sprk_signal" => Signals,
                _ => [],
            };
            return new EntityCollection(rows.Select(Clone).ToList());
        }

        private static Entity Clone(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var attribute in row.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return copy;
        }
    }

    internal sealed class FakeLocks : IIdempotencyService
    {
        public bool Grant = true;
        public bool Fault;
        public readonly List<string> Held = [];
        public readonly List<string> Released = [];

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default) =>
            TryAcquireProcessingLockAsync(eventId, "none", lockDuration, cancellationToken);

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, string ownerId, TimeSpan? lockDuration = null, CancellationToken cancellationToken = default)
        {
            if (Fault)
            {
                throw new TimeoutException("redis");
            }

            if (Grant)
            {
                Held.Add(eventId);
            }

            return Task.FromResult(Grant);
        }

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken cancellationToken = default)
        {
            Released.Add(eventId);
            return Task.CompletedTask;
        }

        public Task ReleaseProcessingLockAsync(string eventId, string ownerId, CancellationToken cancellationToken = default) =>
            ReleaseProcessingLockAsync(eventId, cancellationToken);
    }

    internal sealed class CapturingLogger : ILogger<InvoiceAssignFirmService>
    {
        public readonly List<(LogLevel Level, string Message, Dictionary<string, object?> State)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception),
                (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(p => p.Key, p => p.Value) ?? []));
    }

    internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static UpdateRequest Update(RenumberApply apply, string table, Guid id) =>
        apply.Requests.OfType<UpdateRequest>().Should().ContainSingle(r => r.Target.LogicalName == table && r.Target.Id == id, $"{table} {id}").Which;

    private static JsonObject FindingsOf(RenumberApply apply) =>
        (JsonObject)JsonNode.Parse((string)apply.SubmissionFields![InvoiceSubmissionPersister.SubmissionFindingsJson])!;

    // ── The apply (16.4 condition 2; 16.7 "renumber everything") ──

    // 082c-2 fix round 3 (ontology F1): the review detail after Set firm is derived from the holds that REMAIN, through InvoiceOpenHolds (the
    // firm hold as approval reads it, united with the Procedure holds). The apply keeps every findings member and the stored outcome reason, so a
    // firm_missing marker survives it and the detail stays "Waiting for the firm" until 095d-2's re-run clears the markers. The fixtures use the
    // production shape (pipelineFindings[].reasonCode; a lineFindings[] rules.not_checked cause), not a member nothing reads.

    private const string FirmMarkerPipeline = "{\"reasonCode\":\"" + SubmissionReasonCodes.FirmMissing + "\",\"values\":{}}";

    private static string LineCause(string cause) =>
        $$"""{"code":"{{InvoiceSubmissionPersister.LineFindingRulesNotChecked}}","{{InvoiceFirmHold.LineFindingCauseMember}}":"{{cause}}"}""";

    private static string Findings(string? pipeline = null, string? lines = null) =>
        $$"""{"trackingNumber":"INV-7.3","pipelineFindings":[{{pipeline}}],"{{InvoiceSubmissionPersister.LineFindingsMember}}":[{{lines}}],"keep":1}""";

    /// <summary>A world whose firm markers are cleared from the stored submission (no outcome reason, no firm finding), the invoice's own
    /// hold still set (so Set firm is allowed), and the findings as given.</summary>
    private static World WithFirmMarkersCleared(string findingsJson)
    {
        var w = new World();
        w.Submission.Attributes.Remove(InvoiceSubmissionPersister.SubmissionReasonCode);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = findingsJson;
        return w;
    }

    private static int? DetailOf(World w) =>
        Update(w.Applied!, "sprk_invoice", InvoiceId).Target.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column)?.Value;

    [Theory(DisplayName = "082c-2 fix round 3 (F1, must fire): Set firm with a firm_missing marker still stored keeps 'Waiting for the firm', and approval, over the state the apply leaves, still refuses on the firm hold")]
    [InlineData("reason", true, false, false)]
    [InlineData("pipeline", false, true, false)]
    [InlineData("line", false, false, true)]
    [InlineData("all", true, true, true)]
    public async Task ReviewDetail_SetFirm_FirmMarkerStillStored_StaysWaitingForTheFirm_AndApprovalRefuses(string marker, bool reason, bool pipeline, bool line)
    {
        var w = WithFirmMarkersCleared(Findings(
            pipeline: pipeline ? FirmMarkerPipeline : null,
            lines: line ? LineCause(SubmissionReasonCodes.FirmMissing) : null));
        if (reason)
        {
            w.Submission[InvoiceSubmissionPersister.SubmissionReasonCode] = SubmissionReasonCodes.FirmMissing;
        }

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned, marker);

        DetailOf(w).Should().Be(InvoiceReviewDetail.WaitingForFirm).And.Be(659490002, marker);

        // Approval over the stored state the apply leaves: the firm set, the invoice's own hold cleared, the stored findings the apply wrote and the
        // outcome reason it did not touch. The real approval service, the real firm hold: refused as held for the firm.
        var approval = new ApprovalWorld();
        approval.AddLine(InvoiceLineStatus.Accepted, approved: 100m);
        var left = approval.HoldForFirm(reasonOnly: false, findingsJson: (string)w.Applied!.SubmissionFields![InvoiceSubmissionPersister.SubmissionFindingsJson]);
        approval.Rows[("sprk_invoicesubmission", left)]["sprk_outcomereasoncode"] = reason ? SubmissionReasonCodes.FirmMissing : null;
        var result = await approval.ApproveAs(ApprovalWorld.AliceUser);
        result.Outcome.Should().Be(ApprovalOutcome.Held, marker);
        result.Code.Should().Be(InvoiceApprovalCodes.FirmMissing, marker);
        approval.Writes.Should().Be(0, "a refused approve writes nothing");
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, must fire): the firm comes before a Procedure gap that is also stored (D-205 priority)")]
    public async Task ReviewDetail_SetFirm_FirmMarkerAndGapStored_IsWaitingForTheFirm()
    {
        var w = WithFirmMarkersCleared(Findings(pipeline: FirmMarkerPipeline, lines: LineCause(SubmissionReasonCodes.ProcedureGap)));

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        DetailOf(w).Should().Be(InvoiceReviewDetail.WaitingForFirm);
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, must not fire): with the firm markers cleared and no other hold, Set firm blanks the review detail in the same invoice update")]
    public async Task ReviewDetail_SetFirm_FirmMarkersCleared_NoOtherHold_IsBlank()
    {
        var w = WithFirmMarkersCleared(Findings());

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        Update(w.Applied!, "sprk_invoice", InvoiceId).Target.Attributes.Should().ContainKey(InvoiceReviewDetail.Column).WhoseValue.Should().BeNull();
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, must not fire): with the firm markers cleared and a Procedure gap open, the detail is 'Waiting for billing terms', not blank")]
    public async Task ReviewDetail_SetFirm_FirmMarkersCleared_GapStillOpen_IsWaitingForBillingTerms()
    {
        var w = WithFirmMarkersCleared(Findings(lines: LineCause(SubmissionReasonCodes.ProcedureGap)));

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        DetailOf(w).Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1, must not fire): with the firm markers cleared and the terms unresolved, the detail is 'Billing terms could not be checked'")]
    public async Task ReviewDetail_SetFirm_FirmMarkersCleared_Unresolved_IsBillingTermsNotChecked()
    {
        var w = WithFirmMarkersCleared(Findings(pipeline: "{\"reasonCode\":\"" + SubmissionReasonCodes.ProceduresUnresolved + "\",\"values\":{}}"));

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        DetailOf(w).Should().Be(InvoiceReviewDetail.BillingTermsNotChecked);
    }

    [Fact(DisplayName = "082c-2 fix round 3 (F1): a Procedure hold stored on another submission linked to the invoice is read too, as approval reads it")]
    public async Task ReviewDetail_SetFirm_AnotherLinkedSubmissionHoldsAGap_IsWaitingForBillingTerms()
    {
        var w = WithFirmMarkersCleared(Findings());
        w.Submissions.Add(new Entity(TrackingNumberAllocator.SubmissionEntity, Guid.Parse("095d1b00-0000-4000-8000-000000000002"))
        {
            ["versionnumber"] = 3L,
            [InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference(InvoiceReviewReadService.InvoiceEntity, InvoiceId),
            [InvoiceSubmissionPersister.SubmissionReasonCode] = SubmissionReasonCodes.ProcedureGap,
        });

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        DetailOf(w).Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
    }

    [Fact(DisplayName = "082c-2 (mismatch): where the column does not exist yet, Set firm still applies and leaves the column out")]
    public async Task ReviewDetail_SetFirm_ColumnMissing_LeavesItOut()
    {
        var w = new World { ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(present: false) };

        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned);

        Update(w.Applied!, "sprk_invoice", InvoiceId).Target.Attributes.Should().NotContainKey(InvoiceReviewDetail.Column);
    }

    [Fact(DisplayName = "095d-1b: a linked firm on a held no-firm invoice is assigned in ONE apply - the firm and the new number everywhere, the hold cleared, the decision stored")]
    public async Task Assigned_TheOneApply_CarriesTheFirmTheNumberTheHoldClearAndTheStoredDecision()
    {
        var w = new World();
        var signal = w.AddFirmMissingSignal();
        w.AddFirmMissingSignal("EB-DUP"); // another policy's invoice Signal: not the firm_missing set

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
        result.Status.Should().Be(StatusCodes.Status200OK);
        result.TrackingNumber.Should().Be("INV-7.1");
        result.PreviousTrackingNumber.Should().Be("INV-7.3");
        result.RulesRerun.Should().Be("pending");
        result.DecisionId.Should().Be(InvoiceAssignFirmService.DecisionIdFor(SubmissionId));
        w.RenumberArgs.Should().Be(("11", Firm, "INV-7"), "conditioned on the submission version read, for the firm, with the row's own firm invoice number");
        w.Transactions.Should().BeEmpty("the allocator sends the one transaction; the service writes nothing of its own");
        w.Entities.Invocations.Where(i => i.Method.Name != nameof(IGenericEntityService.RetrieveMultipleAsync)).Should().BeEmpty();

        var apply = w.Applied!;
        var invoice = Update(apply, "sprk_invoice", InvoiceId);
        invoice.Target.GetAttributeValue<EntityReference>("sprk_vendororg").Id.Should().Be(Firm);
        invoice.Target.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.1");
        invoice.Target.Attributes.Should().ContainKey(InvoiceFirmHold.InvoiceHoldColumn).WhoseValue.Should().BeNull("the hold is cleared");
        invoice.Target.RowVersion.Should().Be("5");
        invoice.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);

        foreach (var (line, index) in w.Lines.Select((l, i) => (l, i)))
        {
            var update = Update(apply, InvoiceSubmissionPersister.LineEntity, line.Id).Target;
            update.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineVendorOrg).Id.Should().Be(Firm);
            update.GetAttributeValue<string>(InvoiceSubmissionPersister.LineName).Should().Be(InvoiceSubmissionPersister.LineNameFor("INV-7.1", index));
        }

        Update(apply, InvoiceSubmissionPersister.ApprovalEntity, w.Approvals[0].Id).Target.Attributes.Should()
            .ContainSingle().Which.Should().Be(new KeyValuePair<string, object>(InvoiceSubmissionPersister.ApprovalName, "Approval INV-7.1"));
        Update(apply, StoredFileSchema.Entity, w.Files[0].Id).Target.Attributes.Should()
            .ContainSingle().Which.Should().Be(new KeyValuePair<string, object>(StoredFileSchema.Name, "INV-7.1.pdf"));
        Update(apply, StoredFileSchema.Entity, w.Files[1].Id).Target.GetAttributeValue<string>(StoredFileSchema.Name).Should().Be("INV-7.1-2.docx");
        apply.Requests.OfType<UpdateRequest>().Should().NotContain(r => r.Target.Id == w.Files[2].Id, "INV-7.30.pdf is not INV-7.3's file");
        apply.Requests.Should().HaveCount(1 + 2 + 1 + 2);
        apply.Requests.OfType<UpdateRequest>().Should().NotContain(u => u.Target.LogicalName == TrackingNumberAllocator.SubmissionEntity,
            "the submission's own update is the allocator's");

        apply.SubmissionFields.Should().ContainSingle().Which.Key.Should().Be(InvoiceSubmissionPersister.SubmissionFindingsJson);
        var findings = FindingsOf(apply);
        findings["trackingNumber"]!.GetValue<string>().Should().Be("INV-7.1");
        findings["keep"]!.GetValue<int>().Should().Be(1, "every other member is kept");
        findings["pipelineFindings"]!.AsArray().Should().HaveCount(1, "the submission markers stay until 095d-2");
        var stored = AssignFirmStoredDecision.Read(findings)!;
        stored.DecisionId.Should().Be(InvoiceAssignFirmService.DecisionIdFor(SubmissionId));
        stored.FirmId.Should().Be(Firm);
        stored.Note.Should().Be("Firm confirmed by phone");
        stored.PreviousTrackingNumber.Should().Be("INV-7.3");
        stored.PreviousSequence.Should().Be(3);
        stored.TrackingNumber.Should().Be("INV-7.1");
        stored.Sequence.Should().Be(1);
        stored.DeciderUserId.Should().Be(Decider);
        stored.DecidedOn.Should().Be(new DateTimeOffset(2026, 11, 4, 12, 30, 15, TimeSpan.Zero), "to the second");
        stored.RulesRerun.Should().Be("pending");
        stored.FirmMissingSignals.Signals.Select(s => s.Id).Should().Equal(signal);
        stored.FirmMissingSignals.SetReadAtUtc.Should().Be(Now.UtcDateTime);
        stored.Renamed.Select(r => (r.Entity, r.From, r.To)).Should().BeEquivalentTo(new[]
        {
            (InvoiceSubmissionPersister.LineEntity, "INV-7.3 #1", "INV-7.1 #1"),
            (InvoiceSubmissionPersister.LineEntity, "INV-7.3 #2", "INV-7.1 #2"),
            (InvoiceSubmissionPersister.ApprovalEntity, "Approval INV-7.3", "Approval INV-7.1"),
            (StoredFileSchema.Entity, "INV-7.3.pdf", "INV-7.1.pdf"),
            (StoredFileSchema.Entity, "INV-7.3-2.docx", "INV-7.1-2.docx"),
        });
        w.Locks.Released.Should().BeEquivalentTo(w.Locks.Held, "both locks are released");
        w.Locks.Held.Should().HaveCount(2);
    }

    [Fact(DisplayName = "095d-1b: the builder is pure and follows each attempt's number - a retry's names and stored number match the number it writes")]
    public async Task Builder_FollowsEachAttemptsNumber()
    {
        var w = new World();
        await w.Assign();

        var second = w.Builder!(new RenumberCandidate("INV-7.4", 4));
        var first = w.Builder!(new RenumberCandidate("INV-7.1", 1));

        Update(second, "sprk_invoice", InvoiceId).Target.GetAttributeValue<string>("sprk_trackingnumber").Should().Be("INV-7.4");
        Update(second, InvoiceSubmissionPersister.LineEntity, w.Lines[0].Id).Target.GetAttributeValue<string>("sprk_name").Should().Be("INV-7.4 #1");
        AssignFirmStoredDecision.Read(FindingsOf(second))!.TrackingNumber.Should().Be("INV-7.4");
        FindingsOf(first).ToJsonString().Should().Be(FindingsOf(w.Applied!).ToJsonString(), "the same candidate builds the same apply");
    }

    [Fact(DisplayName = "095d-1b: the decision the stored one rebuilds passes the REAL Decision Record writer's preflight (assign-firm, the invoice, the firm value, no Signal resolved)")]
    public async Task TheStoredDecision_RebuildsADecisionTheWriterAccepts()
    {
        var w = new World();
        w.AddFirmMissingSignal();
        await w.Assign();
        var stored = AssignFirmStoredDecision.Read(FindingsOf(w.Applied!))!;

        var decision = InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, stored);
        var h = DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness());
        var identity = new Mock<Sprk.Bff.Api.Services.Ai.Membership.IIdentityNormalizationService>();
        var recorder = new BillingDecisionRecorder(h.Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance);

        recorder.Invoking(r => r.Preflight(decision)).Should().NotThrow();
        decision.ActionCode.Should().Be("assign-firm");
        decision.SubjectId.Should().Be(InvoiceId);
        decision.ResolvedSignalIds.Should().BeNull("16.7: no Signal is closed in 095d-1; its id is in the facts");
        decision.Values!["firm"].Should().Be(Firm.ToString("D"));
        decision.DecidedOn.Should().Be(stored.DecidedOn);
        w.Preflighted.Should().ContainSingle("the service preflights before the apply").Which.ActionCode.Should().Be("assign-firm");
        InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, stored).Should().BeEquivalentTo(decision, "rebuilt from the stored decision alone");
    }

    [Fact(DisplayName = "095d-1b: the stored decision round-trips through its JSON")]
    public void StoredDecision_RoundTrips()
    {
        var stored = new AssignFirmStoredDecision(
            Guid.NewGuid(), Firm, null, "INV-7.3", null, "INV-7.1", 1, Decider, Now,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(Now.UtcDateTime, [new InvoiceSubmissionAfterSteps.ResolvedSignal(Guid.NewGuid(), "P")]),
            [new AssignFirmRename("sprk_billingevent", Guid.NewGuid(), "sprk_name", "a", "b")], 3, "pending");
        var root = new JsonObject { [InvoiceAssignFirmService.StoredDecisionMember] = stored.ToJson() };

        var read = AssignFirmStoredDecision.Read(JsonNode.Parse(root.ToJsonString())!.AsObject())!;

        read.Should().BeEquivalentTo(stored, o => o.Excluding(s => s.FirmMissingSignals));
        read.FirmMissingSignals.Signals.Should().BeEquivalentTo(stored.FirmMissingSignals.Signals);
        AssignFirmStoredDecision.Read(new JsonObject()).Should().BeNull("no member: no decision");
        FluentActions.Invoking(() => AssignFirmStoredDecision.Read(new JsonObject { [InvoiceAssignFirmService.StoredDecisionMember] = "x" }))
            .Should().Throw<FormatException>("a member that does not parse is never read as no decision");
    }

    [Theory(DisplayName = "095d-1b: the rename rules - a name carries the old number only when it starts with it at a boundary")]
    [InlineData("INV-7.3.pdf", "INV-7.1.pdf")]
    [InlineData("INV-7.3-2.pdf", "INV-7.1-2.pdf")]
    [InlineData("INV-7.3", "INV-7.1")]
    [InlineData("INV-7.30.pdf", null)]
    [InlineData("invoice.pdf", null)]
    [InlineData(null, null)]
    public void RenamedFile_Rules(string? name, string? expected) =>
        InvoiceAssignFirmService.RenamedFile(name, "INV-7.3", "INV-7.1").Should().Be(expected);

    [Fact(DisplayName = "095d-1b: line and approval names are renamed only when they carry the old number")]
    public void RenamedLineAndApproval_Rules()
    {
        InvoiceAssignFirmService.RenamedLine("INV-7.3 #12", "INV-7.3", "INV-7.1").Should().Be("INV-7.1 #12");
        InvoiceAssignFirmService.RenamedLine("Renamed by hand", "INV-7.3", "INV-7.1").Should().BeNull();
        InvoiceAssignFirmService.RenamedLine("INV-7.30 #1", "INV-7.3", "INV-7.1").Should().BeNull();
        InvoiceAssignFirmService.RenamedApproval("Approval INV-7.3", "INV-7.3", "INV-7.1").Should().Be("Approval INV-7.1");
        InvoiceAssignFirmService.RenamedApproval("Approval INV-7.30", "INV-7.3", "INV-7.1").Should().BeNull();
    }

    [Fact(DisplayName = "095d-1b: a line renamed by hand keeps its name but still gets the firm")]
    public async Task ALineRenamedByHand_KeepsItsName_ButGetsTheFirm()
    {
        var w = new World();
        w.Lines[1][InvoiceSubmissionPersister.LineName] = "Disbursements";

        await w.Assign();

        var line = Update(w.Applied!, InvoiceSubmissionPersister.LineEntity, w.Lines[1].Id).Target;
        line.Attributes.Should().NotContainKey(InvoiceSubmissionPersister.LineName);
        line.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.LineVendorOrg).Id.Should().Be(Firm);
    }

    // ── The real allocator: what the one transaction sends ──

    private static TrackingNumberAllocator RealAllocator(World w) => new(
        w.Entities.Object,
        _ => Task.FromResult<TrackingScopePhase?>(TrackingScopePhase.P2ScopeKeyActive),
        new TrackingScopePhaseCache(TimeProvider.System),
        NullLogger<TrackingNumberAllocator>.Instance);

    [Fact(DisplayName = "095d-1b (seed proof): with the REAL allocator the ONE transaction renumbers the submission, clears sprk_firmhold and stores the decision")]
    public async Task RealAllocator_TheOneTransaction_ClearsTheHold_AndCarriesTheStoredDecision()
    {
        var w = new World();

        var result = await w.Assign(allocator: RealAllocator(w));

        result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
        result.TrackingNumber.Should().Be("INV-7.1", "the firm scope's first number for INV-7");
        var sent = w.Transactions.Should().ContainSingle("one transaction or none").Which;
        var submission = sent[0].Should().BeOfType<UpdateRequest>().Subject;
        submission.Target.LogicalName.Should().Be(TrackingNumberAllocator.SubmissionEntity);
        submission.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        submission.Target.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn).Id.Should().Be(Firm);
        submission.Target.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn).Should().Be("INV-7.1");
        submission.Target.GetAttributeValue<int?>(TrackingNumberAllocator.PreviousSequenceColumn).Should().Be(3);
        var findings = JsonNode.Parse(submission.Target.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!.AsObject();
        AssignFirmStoredDecision.Read(findings)!.TrackingNumber.Should().Be("INV-7.1");

        var invoice = sent.OfType<UpdateRequest>().Should().ContainSingle(r => r.Target.LogicalName == "sprk_invoice").Which;
        invoice.Target.Attributes.Should().ContainKey(InvoiceFirmHold.InvoiceHoldColumn).WhoseValue.Should().BeNull("the apply clears the invoice's own hold");
        invoice.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        sent.Should().HaveCount(1 + 1 + 2 + 1 + 2);
    }

    [Fact(DisplayName = "095d-1b: with the REAL allocator a repeat after the commit sends nothing and answers already assigned")]
    public async Task RealAllocator_ARepeatAfterTheCommit_SendsNothing()
    {
        var w = new World();
        await w.Assign(allocator: RealAllocator(w));
        var sent = w.Transactions.Single();
        CommitInto(w, sent);

        var again = await w.Assign(allocator: RealAllocator(w));

        again.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        again.TrackingNumber.Should().Be("INV-7.1");
        w.Transactions.Should().HaveCount(1, "a repeat never applies twice");
    }

    /// <summary>Applies the sent submission and invoice updates to the world, as Dataverse would on commit.</summary>
    private static void CommitInto(World w, IReadOnlyList<OrganizationRequest> sent)
    {
        foreach (var update in sent.OfType<UpdateRequest>())
        {
            var row = update.Target.LogicalName switch
            {
                "sprk_invoicesubmission" => w.Submission,
                "sprk_invoice" => w.Invoice,
                _ => null,
            };
            if (row is null)
            {
                continue;
            }

            foreach (var attribute in update.Target.Attributes)
            {
                row[attribute.Key] = attribute.Value;
            }

            row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
        }
    }

    // ── Refusals: each before the apply, each writes nothing (must fire), each with its must-not-fire neighbour ──

    [Fact(DisplayName = "095d-1b refusal: no firm is 400 firm_required (must fire)")]
    public async Task Refused_NoFirm()
    {
        var w = new World();
        var result = await w.Service().SetFirmAsync(InvoiceId, Guid.Empty, null, Decider, "t", CancellationToken.None);
        (result.Status, result.Code).Should().Be((400, AssignFirmCodes.FirmRequired));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: a note longer than 2000 is 400 note_too_long (must fire); 2000 is assigned (must not fire)")]
    [InlineData(2001, false)]
    [InlineData(2000, true)]
    public async Task Refused_NoteTooLong(int length, bool assigned)
    {
        var w = new World();
        var result = await w.Assign(note: new string('n', length));
        if (assigned)
        {
            result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
            return;
        }

        (result.Status, result.Code).Should().Be((400, AssignFirmCodes.NoteTooLong));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: a held lock is 409 busy and a lock fault 503 (must fire)")]
    [InlineData(false, 409, AssignFirmCodes.Busy)]
    [InlineData(true, 503, AssignFirmCodes.Unavailable)]
    public async Task Refused_Lock(bool fault, int status, string code)
    {
        var w = new World();
        w.Locks.Grant = false;
        w.Locks.Fault = fault;
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((status, code));
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal: a missing invoice is the uniform 404 (must fire)")]
    public async Task Refused_InvoiceMissing()
    {
        var w = new World { Invoice = null };
        var result = await w.Assign();
        result.Status.Should().Be(404);
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal: no submission whose id derives the invoice is 409 no_submission (must fire); one extra linked row that does not derive it is ignored (must not fire)")]
    public async Task Refused_NoSubmission()
    {
        var w = new World();
        var stray = new Entity(TrackingNumberAllocator.SubmissionEntity, Guid.NewGuid()) { ["versionnumber"] = 1L };
        w.Submissions.Add(stray);
        (await w.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned, "a linked row whose id does not derive the invoice is not its submission");

        var none = new World();
        none.Submissions.Clear();
        none.Submissions.Add(stray);
        var result = await none.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.NoSubmission));
        none.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: an invoice not Active and In Review is 409 not_in_review (must fire)")]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    public async Task Refused_NotInReview(int state, int status)
    {
        var w = new World();
        w.Invoice!["statecode"] = new OptionSetValue(state);
        w.Invoice["statuscode"] = new OptionSetValue(status);
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.NotInReview));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: a firm on the invoice or the submission is 409 firm_already_set (must fire); an empty lookup is no firm (must not fire)")]
    [InlineData("invoice", true)]
    [InlineData("submission", true)]
    [InlineData("invoice-empty", false)]
    public async Task Refused_FirmAlreadySet(string where, bool refused)
    {
        var w = new World();
        switch (where)
        {
            case "invoice": w.Invoice!["sprk_vendororg"] = new EntityReference("sprk_organization", OtherFirm); break;
            case "submission": w.Submission["sprk_vendororg"] = new EntityReference("sprk_organization", OtherFirm); break;
            default: w.Invoice!["sprk_vendororg"] = new EntityReference("sprk_organization", Guid.Empty); break;
        }

        var result = await w.Assign();
        if (!refused)
        {
            result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
            return;
        }

        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.FirmAlreadySet));
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal (seed proof): an invoice not held for a firm is 409 not_held (must fire); held by the submission marker alone is assigned (must not fire)")]
    public async Task Refused_NotHeld()
    {
        var w = new World();
        w.Invoice![InvoiceFirmHold.InvoiceHoldColumn] = null;
        w.Submission[InvoiceSubmissionPersister.SubmissionReasonCode] = null;
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = """{"trackingNumber":"INV-7.3"}""";
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.NotHeld));
        w.ShouldHaveWrittenNothing();

        var marker = new World();
        marker.Invoice![InvoiceFirmHold.InvoiceHoldColumn] = null;
        (await marker.Assign()).Outcome.Should().Be(AssignFirmOutcome.Assigned, "the submission's firm_missing marker holds the invoice too");
    }

    [Fact(DisplayName = "095d-1b refusal: a hold that cannot be read is 503 (must fire)")]
    public async Task Refused_HoldUnreadable()
    {
        var w = new World { ReadFault = q => q.EntityName == "sprk_invoice" && q.ColumnSet.Columns.SequenceEqual([InvoiceFirmHold.InvoiceHoldColumn]) };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((503, AssignFirmCodes.Unavailable));
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal: an invoice with no single core record is 500 core_unresolved (must fire)")]
    public async Task Refused_NoSingleCore()
    {
        var w = new World();
        w.Invoice!["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid());
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((500, AssignFirmCodes.CoreUnresolved));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: an inactive or missing firm is 400 firm_invalid and an unreadable one 503 (must fire)")]
    [InlineData(false, 400, InvoiceSubmissionErrors.FirmInvalid)]
    [InlineData(null, 503, InvoiceSubmissionErrors.FirmUnverifiable)]
    public async Task Refused_FirmNotActive(bool? active, int status, string code)
    {
        var w = new World { ActiveFirm = active };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((status, code));
        w.Calls.Should().NotContain("link", "the link is not asked for a firm that is not active");
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: a firm not linked to the core record is 403 and an unreadable link 503, naming no other firm (must fire)")]
    [InlineData(InvoiceFirmLink.NotLinked, 403, InvoiceSubmissionErrors.FirmNotLinked)]
    [InlineData(InvoiceFirmLink.Unreadable, 503, InvoiceSubmissionErrors.FirmUnverifiable)]
    public async Task Refused_FirmNotLinked(InvoiceFirmLink link, int status, string code)
    {
        var w = new World { Link = link };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((status, code));
        result.Detail.Should().NotContain(OtherFirm.ToString()).And.NotContain(Firm.ToString());
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal: before P2 is 503 scope_key_inactive (must fire)")]
    public async Task Refused_NotP2()
    {
        var w = new World { P2 = false };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((503, TrackingNumberReasons.ScopeKeyInactive));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: the Signal-close rights refused are 403 and unavailable 503 (must fire)")]
    [InlineData((int)DecisionCallerRefusal.CoreAppendToMissing, 403, AssignFirmCodes.NotAuthorized)]
    [InlineData((int)DecisionCallerRefusal.SubjectWriteMissing, 403, AssignFirmCodes.NotAuthorized)]
    [InlineData((int)DecisionCallerRefusal.Unavailable, 503, AssignFirmCodes.Unavailable)]
    public async Task Refused_CloseRights(int refusal, int status, string code)
    {
        var w = new World { CloseRights = (DecisionCallerRefusal)refusal };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((status, code));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal order (fix round 2, K1): a caller without AppendTo on the core record gets the SAME refusal for a linked, an unlinked, an inactive and an unreadable firm, and no answer about the firm is read first (must fire)")]
    [InlineData(true, InvoiceFirmLink.Linked)]
    [InlineData(true, InvoiceFirmLink.NotLinked)]
    [InlineData(true, InvoiceFirmLink.Unreadable)]
    [InlineData(false, InvoiceFirmLink.NotLinked)]
    [InlineData(null, InvoiceFirmLink.Linked)]
    public async Task NoCloseRights_IsRefusedTheSame_WhateverFirmIsNamed(bool? active, InvoiceFirmLink link)
    {
        var w = new World { CloseRights = DecisionCallerRefusal.CoreAppendToMissing, ActiveFirm = active, Link = link };

        var result = await w.Assign();

        (result.Status, result.Code).Should().Be((403, AssignFirmCodes.NotAuthorized));
        result.Detail.Should().Be("You do not have permission to record a decision on this invoice's record.");
        w.Calls.Should().NotContain("active").And.NotContain("link", "the firm is not read for a caller who lacks the core record's rights");
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b refusal: a filing that is not the invoice's core record is 500 and a transient one 503 (must fire)")]
    public async Task Refused_Filing()
    {
        var other = new World { Filing = new CoreAncestorResult(CoreAncestorStatus.Derived, [new CoreAncestorStamp("sprk_matter", "sprk_matter", Guid.NewGuid())], null) };
        var result = await other.Assign();
        (result.Status, result.Code).Should().Be((500, AssignFirmCodes.CoreUnresolved));
        other.ShouldHaveWrittenNothing();

        var transient = new World { Filing = new CoreAncestorResult(CoreAncestorStatus.Error, [], "fault") { IsPermanent = false } };
        result = await transient.Assign();
        (result.Status, result.Code).Should().Be((503, AssignFirmCodes.Unavailable));
        transient.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: stored findings that are not a JSON object, or a stored decision that does not parse, are 500 findings_unreadable (must fire)")]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    [InlineData("""{"trackingNumber":"INV-7.3","assignFirm":{"firmId":"x"}}""")]
    public async Task Refused_FindingsUnreadable(string json)
    {
        var w = new World();
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = json;
        w.Submission[InvoiceSubmissionPersister.SubmissionReasonCode] = SubmissionReasonCodes.FirmMissing;
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((500, AssignFirmCodes.FindingsUnreadable));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b refusal: an apply above the transaction ceiling is 409 too_large (must fire); one at the ceiling is assigned through the REAL Decision Record preflight (must not fire)")]
    [InlineData(999, false)]
    [InlineData(998, true)]
    public async Task Refused_AboveTheCeiling(int lines, bool assigned)
    {
        var w = new World();
        w.Approvals.Clear();
        w.Files.Clear();
        w.Lines = Enumerable.Range(0, lines)
            .Select(i => new Entity(InvoiceSubmissionPersister.LineEntity, Guid.NewGuid()) { [InvoiceSubmissionPersister.LineName] = $"INV-7.3 #{i + 1}" })
            .ToList();

        var result = await w.Assign();
        if (assigned)
        {
            // 095d-1b review F1: the Preflight here is the real writer's (World's default), not a no-op. The decision's facts carry counts, not
            // one row per rename, so the largest invoice the service accepts still fits the writer's 100,000-character fact cap.
            result.Outcome.Should().Be(AssignFirmOutcome.Assigned, "1 invoice + 998 lines = 999 caller requests, the most that fits");
            w.Preflight.Should().BeNull("the real recorder preflighted it");
            w.Preflighted.Should().ContainSingle();
            w.Applied!.Requests.Should().HaveCount(TrackingNumberAllocator.MaxRenumberCallerRequests);
            return;
        }

        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.TooLarge));
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b: the Decision Record's facts hold a BOUNDED summary of the rename (counts per table, the old and new number) - never a row per rename; the stored member keeps the full mapping")]
    public async Task TheDecisionFacts_AreBounded_TheStoredMemberKeepsTheMapping()
    {
        var w = new World();
        // A third line whose name does not carry the number: its firm is set, its name is not renamed.
        w.Lines.Add(new Entity(InvoiceSubmissionPersister.LineEntity, Guid.NewGuid()) { [InvoiceSubmissionPersister.LineName] = "Disbursements" });
        await w.Assign();
        var stored = AssignFirmStoredDecision.Read(FindingsOf(w.Applied!))!;

        var facts = InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, stored).Facts;

        facts.Should().NotContainKey("renamed");
        facts["previousTrackingNumber"].Should().Be("INV-7.3");
        facts["trackingNumber"].Should().Be("INV-7.1");
        var counts = facts["renamedCounts"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        counts["lines"].Should().Be(2);
        counts["firmSetLines"].Should().Be(3, "every line gets the firm, renamed or not; 'lines' counts only the renamed");
        counts["approval"].Should().Be(1);
        counts["storedFiles"].Should().Be(2, "INV-7.3.pdf and INV-7.3-2.docx; INV-7.30.pdf is not the old number's");
        counts["findings"].Should().Be(1);
        stored.Renamed.Should().HaveCount(5, "the stored member keeps every old-to-new name");
    }

    [Fact(DisplayName = "095d-1b refusal: a findings member that would pass 1,048,576 characters with the new number is 409 too_large and nothing is written (must fire)")]
    public async Task Refused_FindingsAboveTheSize_JustOver()
    {
        var w = await WorldWithFindingsAtTheLimit(extra: 1);
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.TooLarge));
        w.ShouldHaveWrittenNothing();
        w.Preflighted.Should().BeEmpty("refused before the Decision Record preflight");
    }

    [Fact(DisplayName = "095d-1b refusal: a findings member of exactly 1,048,576 characters with the new number is assigned (must not fire)")]
    public async Task Refused_FindingsAboveTheSize_AtTheLimit_IsAssigned()
    {
        var w = await WorldWithFindingsAtTheLimit(extra: 0);
        var result = await w.Assign();
        result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
        w.Applied.Should().NotBeNull();
    }

    private static World FindingsWorld(int pad)
    {
        var world = new World();
        world.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] =
            new JsonObject
            {
                ["trackingNumber"] = "INV-7.3",
                ["pipelineFindings"] = new JsonArray(new JsonObject { ["code"] = "firm_missing" }),
                ["pad"] = new string('x', pad),
            }.ToJsonString();
        return world;
    }

    /// <summary>The length of the apply's findings JSON built with the longest number the allocator can hand out: what the service's size check measures.</summary>
    private static async Task<int> ProbeLengthAsync(World world)
    {
        var length = 0;
        world.OnRenumber = build =>
        {
            length = FindingsOf(build(new RenumberCandidate($"INV-7.{int.MaxValue}", int.MaxValue))).ToJsonString().Length;
            return RenumberResult.Refused(TrackingNumberReasons.Unavailable);
        };
        await world.Assign();
        return length;
    }

    /// <summary>
    /// A world whose probe findings JSON is exactly <see cref="InvoiceSubmissionPersister.FindingsJsonMaxLength"/> + <paramref name="extra"/>
    /// characters. The padding member is ASCII, so length is linear in it: measured with no padding and with 1,000 characters (a world padded to
    /// the limit is refused by the very check under test, so it cannot be measured through the service).
    /// </summary>
    private static async Task<World> WorldWithFindingsAtTheLimit(int extra)
    {
        var baseLength = await ProbeLengthAsync(FindingsWorld(0));
        baseLength.Should().BeGreaterThan(0, "the measuring run reached the allocator");
        var pad = InvoiceSubmissionPersister.FindingsJsonMaxLength + extra - baseLength;
        (await ProbeLengthAsync(FindingsWorld(1000))).Should().Be(baseLength + 1000, "one padding character is one character of the probe: the length is exact");
        return FindingsWorld(pad);
    }

    [Fact(DisplayName = "095d-1b refusal: a decision the Decision Record writer would refuse is 500 and nothing is applied (must fire)")]
    public async Task Refused_Preflight()
    {
        var w = new World { Preflight = _ => throw new DecisionRecordRefusedException(DecisionRecordRefusalReason.ReviewInvalid, "no") };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((500, AssignFirmCodes.DecisionInvalid));
        result.Detail.Should().Be("The firm could not be set because its decision cannot be recorded; nothing was changed.", "a stated sentence: no enum name, no exception message");
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b: every refusal is asked before the apply - the allocator is the last call")]
    public async Task EveryCheck_RunsBeforeTheApply()
    {
        var w = new World();
        var order = new List<string>();
        w.OnRenumber = build =>
        {
            order.AddRange(w.Calls);
            w.Applied = build(new RenumberCandidate("INV-7.1", 1));
            return RenumberResult.Renumbered("INV-7.1", 1);
        };

        await w.Assign();

        order.Should().Equal("rights", "filing", "active", "link");
        w.Preflighted.Should().ContainSingle();
    }

    // ── The allocator's outcomes ──

    [Theory(DisplayName = "095d-1b: each allocator refusal maps to its code, nothing written by the service")]
    [InlineData(TrackingNumberReasons.Unavailable, 503, TrackingNumberReasons.Unavailable)]
    [InlineData(TrackingNumberReasons.ScopeKeyInactive, 503, TrackingNumberReasons.ScopeKeyInactive)]
    [InlineData(TrackingNumberReasons.SchemaMissing, 503, TrackingNumberReasons.SchemaMissing)]
    [InlineData(TrackingNumberReasons.TooManyRequests, 409, AssignFirmCodes.TooLarge)]
    [InlineData(TrackingNumberReasons.NotUnnumbered, 409, AssignFirmCodes.NotRenumberable)]
    public async Task AllocatorRefused_MapsToItsCode(string reason, int status, string code)
    {
        var w = new World { OnRenumber = _ => RenumberResult.Refused(reason) };
        var result = await w.Assign();
        result.Outcome.Should().Be(AssignFirmOutcome.Refused);
        (result.Status, result.Code).Should().Be((status, code));
        w.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1b: an allocator ArgumentException is 500, any other fault 503 (outcome unknown; the repeat decides)")]
    public async Task AllocatorThrows_Maps()
    {
        var bug = new World { OnRenumber = _ => throw new ArgumentException("owned column") };
        (await bug.Assign()).Should().Match<AssignFirmResult>(r => r.Status == 500 && r.Code == AssignFirmCodes.DecisionInvalid);
        bug.RenumberArgs.Should().NotBeNull("the exception came from the allocator, so the apply was reached and not refused before it");
        bug.Preflighted.Should().ContainSingle("the Decision Record preflight ran before the allocator");

        var fault = new World { OnRenumber = _ => throw new TimeoutException("lost reply") };
        (await fault.Assign()).Should().Match<AssignFirmResult>(r => r.Status == 503 && r.Code == AssignFirmCodes.Unavailable);
    }

    [Fact(DisplayName = "095d-1b: a row-version conflict with no stored decision is 409 changed")]
    public async Task Conflict_NoStoredDecision_IsChanged()
    {
        var w = new World { OnRenumber = _ => RenumberResult.Conflict() };
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.Changed));
    }

    [Theory(DisplayName = "095d-1b: a conflict lost to a committed Set firm is judged by the stored decision - this firm already assigned, another firm 409")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Conflict_LostToACommittedDecision(bool sameFirm)
    {
        var w = new World();
        var winner = sameFirm ? Firm : OtherFirm;
        var calls = 0;
        w.OnRenumber = build =>
        {
            if (calls++ == 0)
            {
                // Another request for the firm committed first: the row now holds its apply.
                var apply = build(new RenumberCandidate("INV-7.1", 1));
                var findings = FindingsOf(apply);
                if (!sameFirm)
                {
                    findings[InvoiceAssignFirmService.StoredDecisionMember]!["firmId"] = OtherFirm.ToString("D");
                }

                Commit(w, winner, "INV-7.1", 1, findings.ToJsonString());
                return RenumberResult.Conflict();
            }

            FluentActions.Invoking(() => build(new RenumberCandidate("INV-7.2", 2))).Should().Throw<InvalidOperationException>("a repeat never applies");
            return RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        };

        var result = await w.Assign();

        if (sameFirm)
        {
            result.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
            result.TrackingNumber.Should().Be("INV-7.1");
        }
        else
        {
            (result.Status, result.Code).Should().Be((409, AssignFirmCodes.AssignedOtherFirm));
            result.Detail.Should().NotContain(OtherFirm.ToString());
        }
    }

    /// <summary>Puts a committed Set firm into the world's submission and invoice.</summary>
    private static void Commit(World w, Guid firm, string number, int sequence, string findings, int? previous = 3)
    {
        w.Submission[TrackingNumberAllocator.FirmColumn] = new EntityReference("sprk_organization", firm);
        w.Submission[TrackingNumberAllocator.ScopeColumn] = TrackingNumberAllocator.ScopeOf(firm);
        w.Submission[TrackingNumberAllocator.SequenceColumn] = sequence;
        w.Submission[TrackingNumberAllocator.TrackingNumberColumn] = number;
        w.Submission[TrackingNumberAllocator.PreviousSequenceColumn] = previous;
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = findings;
        w.Submission["versionnumber"] = 12L;
        w.Invoice!["sprk_vendororg"] = new EntityReference("sprk_organization", firm);
        w.Invoice[InvoiceFirmHold.InvoiceHoldColumn] = null;
        w.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview);
    }

    /// <summary>A world whose submission already carries a committed Set firm for <paramref name="firm"/>.</summary>
    private static World Applied(Guid firm, int? previousSequence = 3)
    {
        var w = new World();
        CommitStoredDecision(w, firm, previousSequence);
        return w;
    }

    /// <summary>Commits a stored Set firm for <paramref name="firm"/> into an existing world (the contract tests start from a host's world).</summary>
    internal static void CommitStoredDecision(World w, Guid firm, int? previousSequence = 3)
    {
        var stored = new AssignFirmStoredDecision(
            InvoiceAssignFirmService.DecisionIdFor(SubmissionId), firm, null, "INV-7.3", previousSequence, "INV-7.1", 1, Decider, Now,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(Now.UtcDateTime, []), [], 0, "pending");
        var findings = new JsonObject { ["trackingNumber"] = "INV-7.1", [InvoiceAssignFirmService.StoredDecisionMember] = stored.ToJson() };
        Commit(w, firm, "INV-7.1", 1, findings.ToJsonString(), previousSequence);
    }

    // ── Repeats (1a review K3; 16.6.1) ──

    [Fact(DisplayName = "095d-1b repeat: the same firm after the commit is 200 alreadyAssigned; the refusals that the apply itself ended (firm set, hold cleared) do not refuse it, and nothing is applied")]
    public async Task Repeat_SameFirm_IsAlreadyAssigned()
    {
        var w = Applied(Firm);
        w.OnRenumber = build =>
        {
            FluentActions.Invoking(() => build(new RenumberCandidate("INV-7.2", 2))).Should().Throw<InvalidOperationException>();
            return RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        };

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        (result.Status, result.TrackingNumber, result.PreviousTrackingNumber).Should().Be((200, "INV-7.1", "INV-7.3"));
        w.RenumberArgs!.Value.Version.Should().Be("12", "asked at the current version");
        w.Preflighted.Should().BeEmpty("a repeat applies nothing");
        w.Transactions.Should().BeEmpty();
        w.Recorded.Should().ContainSingle("095d-1c: the repeat completes the record from the stored decision");
    }

    [Fact(DisplayName = "095d-1b repeat: another firm after the commit is 409 assigned_other_firm and the allocator is not asked")]
    public async Task Repeat_OtherFirm_IsRefused()
    {
        var w = Applied(OtherFirm);
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.AssignedOtherFirm));
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b fix round 3 (F1): a repeat by a caller without the core record's rights is one refusal for the stored firm and for another firm, and carries none of the stored decision (must fire)")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeat_WithoutCoreRights_IsRefusedTheSame_WhateverFirmIsNamed(bool namesTheStoredFirm)
    {
        var w = Applied(OtherFirm);
        w.CloseRights = DecisionCallerRefusal.CoreAppendToMissing;

        var result = await w.Assign(namesTheStoredFirm ? OtherFirm : Firm);

        result.Should().Be(AssignFirmResult.Refuse(403, AssignFirmCodes.NotAuthorized,
            "You do not have permission to record a decision on this invoice's record.", SubmissionId));
        (result.DecisionId, result.TrackingNumber, result.PreviousTrackingNumber).Should().Be((null, null, null), "no stored decision content is returned");
        w.Calls.Should().Contain("rights").And.NotContain("filing", "refused before the filing").And.NotContain("record", "and before the record (095d-1c)");
        w.ShouldHaveWrittenNothing();
    }

    [Fact(DisplayName = "095d-1b repeat: the allocator naming another number than the stored one is 409 changed (must not answer already assigned)")]
    public async Task Repeat_AnotherNumber_IsChanged()
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.2", 2);
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.Changed));
    }

    [Fact(DisplayName = "095d-1b repeat (16.6.1 legacy edge): a row renumbered without a previous sequence is not_unnumbered to the allocator; the stored decision decides (must not crash)")]
    public async Task Repeat_LegacyNoSequence_StoredDecisionDecides()
    {
        var w = Applied(Firm, previousSequence: null);
        w.Submission[TrackingNumberAllocator.PreviousSequenceColumn] = null;
        w.OnRenumber = _ => RenumberResult.Refused(TrackingNumberReasons.NotUnnumbered);

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        result.TrackingNumber.Should().Be("INV-7.1");
        // 095d-1c uac-r2 F3-1: the legacy branch completes the record like the native repeat (filing, then one record), not the 1b answer alone.
        w.Recorded.Should().ContainSingle("the legacy repeat records the stored decision");
        w.Calls.TakeLast(2).Should().Equal("filing", "record");
        w.Transactions.Should().BeEmpty("a repeat applies nothing");
    }

    [Fact(DisplayName = "095d-1c uac-r2 F3-1: on the 16.6.1 legacy branch, a filing that cannot be confirmed is 503 incomplete at failedStep filing and records nothing (must fire)")]
    public async Task Repeat_LegacyNoSequence_FilingNotConfirmed_IsIncomplete_AndRecordsNothing()
    {
        var w = Applied(Firm, previousSequence: null);
        w.Submission[TrackingNumberAllocator.PreviousSequenceColumn] = null;
        w.OnRenumber = _ => RenumberResult.Refused(TrackingNumberReasons.NotUnnumbered);
        w.Filing = new CoreAncestorResult(CoreAncestorStatus.Error, [], "fault") { IsPermanent = false };

        var result = await w.Assign();

        (result.Outcome, result.Status, result.Code, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, 503, AssignFirmCodes.Incomplete, "filing"));
        w.Recorded.Should().BeEmpty("no record without the filing");
        w.Calls.Should().NotContain("record");
        w.Transactions.Should().BeEmpty();
    }

    [Theory(DisplayName = "095d-1b repeat (16.6.1, must not fire): not_unnumbered is not taken as already assigned when the stored decision had a previous sequence, or the row holds another number")]
    [InlineData(3, "INV-7.1")]
    [InlineData(null, "INV-7.9")]
    public async Task Repeat_NotUnnumbered_OtherwiseIsChanged(int? previousSequence, string rowNumber)
    {
        var w = Applied(Firm, previousSequence);
        w.Submission[TrackingNumberAllocator.TrackingNumberColumn] = rowNumber;
        w.OnRenumber = _ => RenumberResult.Refused(TrackingNumberReasons.NotUnnumbered);

        var result = await w.Assign();

        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.Changed));
    }

    [Theory(DisplayName = "095d-1b repeat: a transient fault asking the allocator is 503 unavailable (must fire); the builder's never-apply-twice trip stays 409 changed (must not fire)")]
    [InlineData("timeout", 503, AssignFirmCodes.Unavailable)]
    [InlineData("allocator-bug", 503, AssignFirmCodes.Unavailable)]
    [InlineData("builder-trip", 409, AssignFirmCodes.Changed)]
    public async Task Repeat_AllocatorFault_IsUnavailable_ExceptTheBuildersTrip(string fault, int status, string code)
    {
        var w = Applied(Firm);
        w.OnRenumber = build =>
        {
            switch (fault)
            {
                case "timeout": throw new TimeoutException("read fault");
                case "allocator-bug": throw new InvalidOperationException("the allocator's own");
                default:
                    build(new RenumberCandidate("INV-7.2", 2)); // the repeat's builder throws: a repeat never applies
                    return RenumberResult.AlreadyRenumbered("INV-7.1", 1);
            }
        };

        var result = await w.Assign();

        (result.Status, result.Code).Should().Be((status, code));
        result.Detail.Should().NotContain("TimeoutException").And.NotContain("read fault");
        w.Transactions.Should().BeEmpty();
        w.Entities.Invocations.Where(i => i.Method.Name != nameof(IGenericEntityService.RetrieveMultipleAsync)).Should().BeEmpty("a repeat writes nothing");
    }

    [Fact(DisplayName = "095d-1b repeat: an allocator refusal other than not_unnumbered keeps its code")]
    public async Task Repeat_OtherRefusal_KeepsItsCode()
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.Refused(TrackingNumberReasons.SchemaMissing);
        var result = await w.Assign();
        (result.Status, result.Code).Should().Be((503, TrackingNumberReasons.SchemaMissing));
    }
}
