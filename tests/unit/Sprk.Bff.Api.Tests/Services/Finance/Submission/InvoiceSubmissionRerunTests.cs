using System.ServiceModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-2 (EB-D-185..187; uac-r2 conditions 2 and 3): ONE re-run after Set firm over an in-memory Dataverse that applies a
/// transaction all or nothing and checks every row version. The stored rows are a real firmless intake (the persister's own transaction), with
/// Set firm's effects applied: the firm on the submission and the invoice, the rerun-pending marker, and the stored decision with
/// <c>rulesRerun</c> = pending.
/// </summary>
/// <remarks>
/// The quote an observation carries reaches the rebuilt read only as intake stored it; where it may go is pinned by uac-r2 ruling (B)'s
/// containment tests (<see cref="ObservationQuoteContainmentTests"/>), which this re-run does not change: it keeps the stored coding of every
/// line whose code set does not change (<see cref="RerunLineCoding"/>).
/// </remarks>
public sealed class InvoiceSubmissionRerunTests
{
    private static readonly Guid SubmissionId = Guid.Parse("04900000-0000-4000-8000-0000000000b1");
    private static readonly Guid Firm = Guid.Parse("04900000-0000-4000-8000-0000000000f1");
    private static readonly Guid InvoiceId = InvoiceSubmissionPersister.InvoiceIdFor(SubmissionId);
    private static readonly Guid DecisionId = InvoiceAssignFirmService.DecisionIdFor(SubmissionId);
    private static readonly Guid Reviewer = Guid.Parse("095d2a20-0000-4000-8000-0000000000c1");

    // ── the commit ──

    [Fact(DisplayName = "095d-2a-2: a completed re-run clears the marker and writes rulesRerun = completed in ONE conditional commit with the new outcome, the lines and the review detail")]
    public async Task ACompletedRerun_ClearsTheMarker_AndCompletesTheRerun_InOneCommit()
    {
        var world = await World.SeedAsync();
        var submissionBefore = world.Findings();

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        result.Permanent.Should().BeFalse();
        result.Kind.Should().Be(SubmissionEvaluationKind.Accepted);
        result.RerunState.Should().Be(InvoiceSubmissionRerun.StateCommitted);

        // One transaction: the submission, the invoice and every line, each conditioned on the version read.
        var commit = world.Applied.Should().ContainSingle().Subject;
        commit.Should().AllBeOfType<UpdateRequest>();
        commit.Cast<UpdateRequest>().Should().OnlyContain(u => u.ConcurrencyBehavior == ConcurrencyBehavior.IfRowVersionMatches && u.Target.RowVersion != null);
        commit.Select(r => ((UpdateRequest)r).Target.LogicalName).Should().Equal(
            ["sprk_invoicesubmission", "sprk_invoice", .. Enumerable.Repeat("sprk_billingevent", world.LineIds.Count)]);

        // The marker, cleared in the same commit that writes rulesRerun = completed.
        var invoice = world.Row(InvoiceId);
        invoice.GetAttributeValue<string>(InvoiceFirmHold.InvoiceHoldColumn).Should().BeNull();
        InvoiceFirmHold.StateOf(invoice).Should().Be(InvoiceFirmHold.InvoiceHoldState.None);
        var findings = world.Findings();
        findings[InvoiceAssignFirmService.StoredDecisionMember]!["rulesRerun"]!.GetValue<string>().Should().Be(InvoiceAssignFirmService.RulesRerunCompleted);
        findings[InvoiceSubmissionRerun.StateMember]!["state"]!.GetValue<string>().Should().Be(InvoiceSubmissionRerun.StateCommitted);
        findings[InvoiceSubmissionRerun.StateMember]!["decisionId"]!.GetValue<string>().Should().Be(DecisionId.ToString("D"));

        // The three firm markers are replaced by the new outcome; the stored decision's hashed values, the invoice name and what the analyzer
        // read are kept; the Override stamp is dropped (R14).
        var submission = world.Row(SubmissionId);
        var reason = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode);
        reason.Should().NotBe(SubmissionReasonCodes.FirmMissing);
        InvoiceFirmHold.IsHeld(reason, findings.ToJsonString(), out var unreadable).Should().BeFalse();
        unreadable.Should().BeFalse();
        findings["invoiceName"]!.ToJsonString().Should().Be(submissionBefore["invoiceName"]!.ToJsonString());
        findings[ExtractionProvenance.Member]!.ToJsonString().Should().Be(submissionBefore[ExtractionProvenance.Member]!.ToJsonString());
        findings.ContainsKey(SubmissionOverrideService.OverrideMember).Should().BeFalse();
        var decisionAfter = (JsonObject)findings[InvoiceAssignFirmService.StoredDecisionMember]!.DeepClone();
        var decisionBefore = (JsonObject)submissionBefore[InvoiceAssignFirmService.StoredDecisionMember]!.DeepClone();
        decisionAfter.Remove("rulesRerun");
        decisionBefore.Remove("rulesRerun");
        decisionAfter.ToJsonString().Should().Be(decisionBefore.ToJsonString(), "only rulesRerun changes in the stored decision");

        // The review detail is re-derived from the holds that remain (none): the stored "waiting for firm" is cleared.
        invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Should().BeNull();
        result.ReviewDetail.Should().BeNull();

        // No line keeps the firmless "rules not checked" flag; no billed value is written; the invoice status is never written.
        world.Lines().Should().OnlyContain(l => l.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus).Value != (int)InvoiceLineStatus.Pending);
        commit.Skip(2).SelectMany(r => ((UpdateRequest)r).Target.Attributes.Keys).Distinct().Should().BeSubsetOf(
        [
            InvoiceSubmissionPersister.LineResolvedTimekeeper, InvoiceSubmissionPersister.LineApprovedRate, InvoiceSubmissionPersister.LineCodingConfidence,
            InvoiceTotalsService.LineStatus, InvoiceSubmissionPersister.LineExtractionConfidence,
        ]);
        ((UpdateRequest)commit[1]).Target.Attributes.Keys.Should().BeEquivalentTo([InvoiceFirmHold.InvoiceHoldColumn, InvoiceReviewDetail.Column]);
    }

    [Fact(DisplayName = "095d-2a-2: a retry after the commit writes nothing (idempotent): rulesRerun completed answers AlreadyCompleted")]
    public async Task ARetry_AfterTheCommit_WritesNothing()
    {
        var world = await World.SeedAsync();
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        var after = world.Snapshot();

        var retry = await world.RunAsync();

        retry.Status.Should().Be(InvoiceSubmissionRerunStatus.AlreadyCompleted);
        retry.RerunState.Should().Be(InvoiceSubmissionRerun.StateCommitted);
        world.Applied.Should().ContainSingle("the first run's commit only");
        world.Attempts.Should().Be(1);
        world.Snapshot().Should().Equal(after);
    }

    // ── fail closed ──

    public enum Failure
    {
        EvaluationFaults,
        ProceduresUnresolvedOnTheFinalDelivery,
        CommitFaults,
        CommitOutcomeUnknown,
        LockBusy,
    }

    [Theory(DisplayName = "095d-2a-2 (uac-r2 condition 2): a failed re-run writes nothing, so the marker, the firm hold and rulesRerun = pending all stay; the job retries")]
    [InlineData(Failure.EvaluationFaults, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.EvaluationFailed)]
    [InlineData(Failure.ProceduresUnresolvedOnTheFinalDelivery, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.ProceduresUnresolved)]
    [InlineData(Failure.CommitFaults, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.Unavailable)]
    [InlineData(Failure.CommitOutcomeUnknown, InvoiceSubmissionRerunStatus.Failed, InvoiceSubmissionRerunCodes.OutcomeUnknown)]
    [InlineData(Failure.LockBusy, InvoiceSubmissionRerunStatus.Busy, InvoiceSubmissionRerunCodes.Busy)]
    public async Task AFailedRerun_KeepsTheMarker_AndWritesNothing(Failure failure, InvoiceSubmissionRerunStatus status, string code)
    {
        var world = await World.SeedAsync();
        var before = world.Snapshot();
        world.Processor = failure switch
        {
            Failure.EvaluationFaults => new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, EvaluationFault = new InvalidOperationException("resolver down") },
            Failure.ProceduresUnresolvedOnTheFinalDelivery => new ProcessorHarness
            {
                Channel = InvoiceSubmissionChannel.Internal,
                FinalDelivery = true,
                EvaluationFault = new ProcedureResolutionFaultException("two versions in force"),
            },
            _ => world.Processor,
        };
        world.CommitFault = failure switch
        {
            Failure.CommitFaults => new InvalidOperationException("service unavailable"),
            Failure.CommitOutcomeUnknown => new DataverseTransactionException("timed out", null, true, new TimeoutException()),
            _ => null,
        };
        if (failure == Failure.LockBusy)
        {
            world.BusyKeys.Add(InvoiceLineDecisionService.LockKeyFor(InvoiceId));
        }

        var result = await world.RunAsync(finalDelivery: failure == Failure.ProceduresUnresolvedOnTheFinalDelivery);

        result.Status.Should().Be(status);
        result.Code.Should().Be(code);
        result.Permanent.Should().BeFalse("a failure is retried, never an operator matter");
        world.Applied.Should().BeEmpty();
        world.Snapshot().Should().Equal(before, "nothing was written");
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending);
        world.Findings()[InvoiceAssignFirmService.StoredDecisionMember]!["rulesRerun"]!.GetValue<string>().Should().Be(InvoiceAssignFirmService.RulesRerunPending);
        world.Locks.Released.Should().BeEquivalentTo(world.Locks.Acquired, "every lock taken is released");

        // Retryable: the same run, with the fault gone, completes.
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal };
        world.CommitFault = null;
        world.BusyKeys.Clear();
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.None);
    }

    public enum Refusal
    {
        FactsMissing,
        StoredRowsInconsistent,
        StoredDecisionMissing,
        DecisionForeign,
        MarkerMissing,
        FirmMismatch,
        NotInReview,
        LookupRepointed,
    }

    [Theory(DisplayName = "095d-2a-2: a refused re-run writes nothing (no transaction is sent), leaves the hold and the marker, and logs Permanent=true")]
    [InlineData(Refusal.FactsMissing, StoredSubmissionRead.FactsMissing)]
    [InlineData(Refusal.StoredRowsInconsistent, StoredSubmissionRead.StoredRowsInconsistent)]
    [InlineData(Refusal.StoredDecisionMissing, InvoiceSubmissionRerunCodes.StoredDecisionMissing)]
    [InlineData(Refusal.DecisionForeign, InvoiceSubmissionRerunCodes.DecisionForeign)]
    [InlineData(Refusal.MarkerMissing, InvoiceSubmissionRerunCodes.MarkerMissing)]
    [InlineData(Refusal.FirmMismatch, InvoiceSubmissionRerunCodes.FirmMismatch)]
    [InlineData(Refusal.NotInReview, InvoiceSubmissionRerunCodes.NotInReview)]
    [InlineData(Refusal.LookupRepointed, InvoiceSubmissionRerunCodes.NotLinked)]
    public async Task ARefusedRerun_WritesNothing_AndLogsPermanent(Refusal refusal, string code)
    {
        var world = await World.SeedAsync();
        var decisionId = DecisionId;
        switch (refusal)
        {
            case Refusal.FactsMissing:
                world.EditFindings(f => f.Remove(ExtractionProvenance.Member));
                break;
            case Refusal.StoredRowsInconsistent:
                world.RemoveRow(world.LineIds[^1]);
                break;
            case Refusal.StoredDecisionMissing:
                world.EditFindings(f => f.Remove(InvoiceAssignFirmService.StoredDecisionMember));
                break;
            case Refusal.DecisionForeign:
                decisionId = Guid.NewGuid();
                break;
            case Refusal.MarkerMissing:
                // A row that never went through Set firm (or whose marker was already cleared): nothing reached here may clear it again.
                world.Row(InvoiceId)[InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceHoldValue;
                break;
            case Refusal.FirmMismatch:
                world.Row(InvoiceId)[InvoiceSubmissionPersister.InvoiceVendorOrg] = new EntityReference("sprk_organization", Guid.NewGuid());
                break;
            case Refusal.NotInReview:
                world.Row(InvoiceId)["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview + 1);
                break;
            case Refusal.LookupRepointed:
                // Nothing reached through the submission lookup may clear a marker: the invoice is derived from the submission id.
                world.Row(SubmissionId)[InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", Guid.NewGuid());
                break;
        }

        var before = world.Snapshot();

        var result = await world.RunAsync(decisionId: decisionId);

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Refused);
        result.Code.Should().Be(code);
        result.Permanent.Should().BeTrue();
        world.Attempts.Should().Be(0, "a refusal is decided before anything is sent");
        world.Snapshot().Should().Equal(before);
        world.Logger.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Permanent=True", StringComparison.Ordinal));
        if (refusal is not (Refusal.MarkerMissing or Refusal.StoredDecisionMissing or Refusal.DecisionForeign))
        {
            world.Row(InvoiceId).GetAttributeValue<string>(InvoiceFirmHold.InvoiceHoldColumn).Should().Be(InvoiceFirmHold.InvoiceRerunPendingValue);
        }
    }

    [Fact(DisplayName = "095d-2a-2: a row version changed under the commit writes nothing; the run evaluates once more, then fails for a retry, which completes")]
    public async Task AVersionConflict_WritesNothing_AndCanBeRetried()
    {
        var world = await World.SeedAsync();
        var before = world.Snapshot();
        world.ConcurrentChanges = 2; // every commit of this run finds a line changed since the read

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Changed);
        result.Code.Should().Be(InvoiceSubmissionRerunCodes.Changed);
        result.Permanent.Should().BeFalse();
        world.Attempts.Should().Be(1 + InvoiceSubmissionRerun.MaxReEvaluations, "one commit, one re-evaluation");
        world.Applied.Should().BeEmpty();
        world.Snapshot().Where(kv => !kv.Key.EndsWith("versionnumber", StringComparison.Ordinal)).Should().Equal(
            before.Where(kv => !kv.Key.EndsWith("versionnumber", StringComparison.Ordinal)), "only the concurrent writer's versions moved");
        InvoiceFirmHold.StateOf(world.Row(InvoiceId)).Should().Be(InvoiceFirmHold.InvoiceHoldState.RerunPending);

        // The retry reads the new versions and commits.
        var retry = await world.RunAsync();
        retry.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.Applied.Should().ContainSingle();
    }

    [Fact(DisplayName = "095d-2a-2: one concurrent change is absorbed by the one re-evaluation (read again under the new versions, then commit)")]
    public async Task OneConcurrentChange_IsReEvaluated_AndCommitted()
    {
        var world = await World.SeedAsync();
        world.ConcurrentChanges = 1;

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        world.Attempts.Should().Be(2);
        world.Applied.Should().ContainSingle();
    }

    // ── EB-D-187 ──

    [Fact(DisplayName = "095d-2a-2 (EB-D-187): a new hold (a Procedure gap) holds the invoice for review; the marker is cleared and the review detail is the gap's")]
    public async Task ANewHold_HoldsTheInvoiceForReview()
    {
        var world = await World.SeedAsync();
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Procedures = GappedProcedures() };

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        result.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        result.ReviewDetail.Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
        var invoice = world.Row(InvoiceId);
        invoice.GetAttributeValue<OptionSetValue>(InvoiceReviewDetail.Column).Value.Should().Be(InvoiceReviewDetail.WaitingForBillingTerms);
        InvoiceFirmHold.StateOf(invoice).Should().Be(InvoiceFirmHold.InvoiceHoldState.None, "the firm hold is gone; the invoice is held by the gap");
        InvoiceProcedureHold.ReasonsOf(world.Row(SubmissionId).GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode), world.Findings().ToJsonString())
            .Should().Contain(SubmissionReasonCodes.ProcedureGap);
        invoice.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview, "held for review, never moved");
    }

    [Fact(DisplayName = "095d-2a-2 (EB-D-187): a finding that would reject holds the invoice for review (Blocked); nothing is rejected")]
    public async Task AWouldBeRejection_IsHeldForReview()
    {
        var aged = new[] { InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20)) };
        var world = await World.SeedAsync(aged);
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        result.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        result.ReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        var (status, _) = InvoiceSubmissionPersister.TerminalFor(SubmissionEvaluationKind.Blocked, SubmissionReasonCodes.Blocked);
        world.Row(SubmissionId).GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus).Value.Should().Be((int)status);
        world.Row(InvoiceId).GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(InvoiceStatusCodes.InReview, "never rejected");
        ((UpdateRequest)world.Applied.Single()[1]).Target.Attributes.Keys.Should().NotContain(["statuscode", "statecode"]);
    }

    [Fact(DisplayName = "095d-2a-2 (EB-D-187): an automatic rejection outcome is turned into a hold for review (the SPA classification never reaches the commit)")]
    public async Task AnAutoRejectedOutcome_IsHeldInstead()
    {
        var spa = new ProcessorHarness { Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        spa.Lines = [InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))];
        var rejected = await spa.RunAsync();
        rejected.Kind.Should().Be(SubmissionEvaluationKind.AutoRejected, "the setup");

        var held = InvoiceSubmissionRerun.HeldInsteadOfRejected(rejected);

        held.Kind.Should().Be(SubmissionEvaluationKind.Blocked);
        held.ReasonCode.Should().Be(SubmissionReasonCodes.Blocked);
        InvoiceSubmissionRerun.HeldInsteadOfRejected(held).Should().BeSameAs(held, "any other outcome is unchanged");
    }

    [Fact(DisplayName = "095d-2a-2 (EB-D-187 / Q4): a line decided before EB-D-185 is reset to the re-run's derivation; the decision is kept as history in the findings")]
    public async Task ALineDecidedBefore_IsResetToTheRerunsDerivation()
    {
        var world = await World.SeedAsync();
        var decided = world.Row(world.LineIds[0]);
        decided[InvoiceTotalsService.LineStatus] = new OptionSetValue((int)InvoiceLineStatus.Reduced);
        decided[InvoiceTotalsService.LineApprovedAmount] = new Money(500m);
        decided[InvoiceReviewReadService.LineReason] = "rate above the agreed rate";
        decided[InvoiceReviewReadService.LineReviewedBy] = new EntityReference("systemuser", Reviewer);
        decided[InvoiceReviewReadService.LineReviewedOn] = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = await world.RunAsync();

        result.Status.Should().Be(InvoiceSubmissionRerunStatus.Completed);
        result.ResetLines.Should().Be(1);
        var line = world.Row(world.LineIds[0]);
        line.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus).Value.Should().BeOneOf((int)InvoiceLineStatus.Clean, (int)InvoiceLineStatus.Flagged);
        line.GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount).Should().BeNull();
        line.GetAttributeValue<string>(InvoiceReviewReadService.LineReason).Should().BeNull();
        line.GetAttributeValue<EntityReference>(InvoiceReviewReadService.LineReviewedBy).Should().BeNull();
        line.Attributes[InvoiceReviewReadService.LineReviewedOn].Should().BeNull();

        var history = world.Findings()[InvoiceSubmissionRerun.StateMember]!["resetLines"]!.AsArray().Should().ContainSingle().Subject!;
        history["lineId"]!.GetValue<string>().Should().Be(world.LineIds[0].ToString("D"));
        history["previousStatus"]!.GetValue<string>().Should().Be(nameof(InvoiceLineStatus.Reduced));
        history["previousApprovedAmount"]!.GetValue<decimal>().Should().Be(500m);

        // An undecided line's decision columns are not in its update.
        world.Applied.Single().Skip(3).Should().OnlyContain(r => !((UpdateRequest)r).Target.Attributes.ContainsKey(InvoiceReviewReadService.LineReviewedBy));
    }

    // ── harness ──

    private static ProcedureResolution GappedProcedures()
    {
        var kit = new Sprk.Bff.Api.Tests.Domain.Signals.Billing.ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-INCREMENT", Sprk.Bff.Api.Services.Signals.RuleType.TimeIncrement);
        return kit.Build();
    }

    internal sealed class ListLogger : ILogger<InvoiceSubmissionRerun>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    internal sealed class FakeLocks
    {
        public List<string> Acquired { get; } = [];

        public List<string> Released { get; } = [];
    }

    /// <summary>The stored rows after Set firm, in memory: reads return copies; a transaction is applied all or nothing, every row version checked.</summary>
    private sealed class World
    {
        private readonly Dictionary<Guid, Entity> _rows = [];

        public List<Guid> LineIds { get; } = [];

        public List<IReadOnlyList<OrganizationRequest>> Applied { get; } = [];

        /// <summary>Transactions sent (applied or not).</summary>
        public int Attempts { get; private set; }

        public Exception? CommitFault { get; set; }

        /// <summary>How many of the next commits find a line changed by another writer since the read.</summary>
        public int ConcurrentChanges { get; set; }

        public HashSet<string> BusyKeys { get; } = [];

        public FakeLocks Locks { get; } = new();

        public ListLogger Logger { get; } = new();

        public ProcessorHarness Processor { get; set; } = new() { Channel = InvoiceSubmissionChannel.Internal };

        public static async Task<World> SeedAsync(IReadOnlyList<ExtractedInvoiceLine>? lines = null)
        {
            var (submission, invoice, stored) = await InvoiceSubmissionPersisterTests.StoredFirmlessIntakeAsync(lines);
            var world = new World();
            InvoiceFirmHold.StateOf(invoice).Should().Be(InvoiceFirmHold.InvoiceHoldState.Held, "the setup: a firmless intake is held for its firm");

            // Set firm's effects (095d-1b): the firm, the rerun-pending marker, the stored decision with rulesRerun = pending.
            var firm = new EntityReference("sprk_organization", Firm);
            submission[InvoiceSubmissionPersister.SubmissionInvoice] = new EntityReference("sprk_invoice", InvoiceId);
            submission[TrackingNumberAllocator.FirmColumn] = firm;
            var root = JsonNode.Parse(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!.AsObject();
            root[InvoiceAssignFirmService.StoredDecisionMember] = new AssignFirmStoredDecision(
                DecisionId, Firm, null, "INV-100.2", 2, "INV-100.1", 1, Reviewer, new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero),
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc), []), [], stored.Count,
                InvoiceAssignFirmService.RulesRerunPending).ToJson();
            root[SubmissionOverrideService.OverrideMember] = new JsonObject { ["schema"] = 1 };
            submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();

            invoice[InvoiceSubmissionPersister.InvoiceVendorOrg] = firm;
            invoice[InvoiceFirmHold.InvoiceHoldColumn] = InvoiceFirmHold.InvoiceRerunPendingValue;
            invoice["statecode"] = new OptionSetValue(InvoiceStatusCodes.StateActive);
            invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.InReview);
            invoice[InvoiceReviewDetail.Column] = new OptionSetValue(InvoiceReviewDetail.WaitingForFirm);

            foreach (var row in (Entity[])[submission, invoice, .. stored])
            {
                row["versionnumber"] = 10L;
                world._rows[row.Id] = row;
            }

            world.LineIds.AddRange(stored.Select(l => l.Id));
            return world;
        }

        public Entity Row(Guid id) => _rows[id];

        public IEnumerable<Entity> Lines() => LineIds.Where(_rows.ContainsKey).Select(id => _rows[id]);

        public void RemoveRow(Guid id) => _rows.Remove(id);

        public JsonObject Findings() =>
            JsonNode.Parse(Row(SubmissionId).GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson))!.AsObject();

        public void EditFindings(Action<JsonObject> edit)
        {
            var root = Findings();
            edit(root);
            Row(SubmissionId)[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        }

        /// <summary>Every stored value, as text (row id and column), for "nothing was written".</summary>
        public Dictionary<string, string> Snapshot() =>
            _rows.Values.SelectMany(r => r.Attributes.Select(a => ($"{r.Id:D}.{a.Key}", Describe(a.Value)))).ToDictionary(p => p.Item1, p => p.Item2);

        private static string Describe(object? value) => value switch
        {
            null => "<null>",
            EntityReference r => $"{r.LogicalName}:{r.Id:D}",
            OptionSetValue o => $"option:{o.Value}",
            Money m => $"money:{m.Value}",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        };

        public Task<InvoiceSubmissionRerunResult> RunAsync(Guid? decisionId = null, bool finalDelivery = false)
        {
            var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
            entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QueryExpression query, CancellationToken _) => Retrieve(query));
            entities.Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((IReadOnlyList<OrganizationRequest> requests, CancellationToken _) => Execute(requests));

            var locks = new Mock<IIdempotencyService>(MockBehavior.Strict);
            locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, string _, TimeSpan? _, CancellationToken _) =>
                {
                    if (BusyKeys.Contains(key))
                    {
                        return false;
                    }

                    Locks.Acquired.Add(key);
                    return true;
                });
            locks.Setup(l => l.ReleaseProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string key, string _, CancellationToken _) => Locks.Released.Add(key))
                .Returns(Task.CompletedTask);

            var rerun = new InvoiceSubmissionRerun(
                entities.Object, Processor.Build(), Processor.Coding, (_, _) => Task.FromResult<ResolvedCodeSet?>(null), locks.Object, Logger, null)
            {
                ReviewDetailProbe = ReviewDetailColumnProbe.Fixed(true),
            };
            return rerun.RunAsync(SubmissionId, decisionId ?? DecisionId, finalDelivery);
        }

        private EntityCollection Retrieve(QueryExpression query)
        {
            var rows = _rows.Values.Where(r => r.LogicalName == query.EntityName && query.Criteria.Conditions.All(c => Matches(r, c)));
            return new EntityCollection(rows.Select(Copy).ToList()) { MoreRecords = false };
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            if (condition.AttributeName == $"{row.LogicalName}id")
            {
                return condition.Values.Contains(row.Id);
            }

            return row.Attributes.TryGetValue(condition.AttributeName, out var value) && value switch
            {
                EntityReference r => condition.Values.Contains(r.Id),
                OptionSetValue o => condition.Values.Contains(o.Value),
                _ => condition.Values.Contains(value),
            };
        }

        private Task Execute(IReadOnlyList<OrganizationRequest> requests)
        {
            Attempts++;
            if (CommitFault is { } fault)
            {
                return Task.FromException(fault);
            }

            if (ConcurrentChanges > 0)
            {
                // Another writer changed a line after the read: its version moved.
                ConcurrentChanges--;
                var line = _rows[LineIds[^1]];
                line["versionnumber"] = line.GetAttributeValue<long>("versionnumber") + 1;
            }

            var updates = requests.Cast<UpdateRequest>().ToList();
            foreach (var update in updates)
            {
                if (!_rows.TryGetValue(update.Target.Id, out var row)
                    || update.ConcurrencyBehavior != ConcurrencyBehavior.IfRowVersionMatches
                    || update.Target.RowVersion != row.GetAttributeValue<long>("versionnumber").ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    return Task.FromException(new DataverseTransactionException(
                        "simulated row version mismatch: the transaction was rolled back", requests.ToList().IndexOf(update), false,
                        new FaultException<OrganizationServiceFault>(
                            new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch })));
                }
            }

            foreach (var update in updates)
            {
                var row = _rows[update.Target.Id];
                foreach (var (name, value) in update.Target.Attributes)
                {
                    row[name] = value;
                }

                row["versionnumber"] = row.GetAttributeValue<long>("versionnumber") + 1;
            }

            Applied.Add(requests);
            return Task.CompletedTask;
        }

        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var (key, value) in row.Attributes)
            {
                copy[key] = value;
            }

            return copy;
        }
    }
}
