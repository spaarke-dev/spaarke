using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using World = Sprk.Bff.Api.Tests.Services.Finance.SubmissionOverrideServiceTests.World;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 ISS-034 (#2888, EB-D-244, task iss034a): a blocked submission's Override or Return that was recorded and then failed before it
/// finished can be finished by a billing admin who is not its decider (<see cref="SubmissionOverrideService.FinishStrandedAsync"/>), exactly as
/// recorded: the stored reason, note, Signal set and decider, plus ONE append-only <c>finish-stranded-decision</c> record of the operator's own.
/// Over the real <see cref="SubmissionOverrideService"/> and the real Decision Record writer harness of <see cref="World"/>, each test proves the
/// stored state: the records, the release transaction, the transition, the submission, the Signals and their closes.
/// </summary>
public sealed class SubmissionOverrideOperatorFinishTests
{
    private const string Reason = "Negotiated rate is not yet in the rate card.";
    private const string FirstNote = "First note";
    private const string OperatorReason = "The decider has left; finishing their recorded decision.";
    private const string OperatorNote = "Operator note";
    private const string FinishCode = "finish-stranded-decision";
    private static readonly Guid SecondOperator = Guid.Parse("10500000-0000-4000-8000-0000000000c5");

    private static string VerbOf(string strand) => strand.StartsWith("return", StringComparison.Ordinal) ? "return" : "override";

    private static Task<BlockedDecisionResult> Finish(World w, string? reason = OperatorReason, string? note = OperatorNote) =>
        w.Service.FinishStrandedAsync(World.SubmissionId, reason, note, World.Token, CancellationToken.None);

    private static Task<BlockedDecisionResult> Decide(World w, string verb, string? reason = Reason) =>
        verb == "override"
            ? w.Service.OverrideAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None)
            : w.Service.ReturnAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None);

    /// <summary>The caller becomes <paramref name="who"/>, a billing admin (the marker; the World already holds the Override privilege).</summary>
    private static void ActAsAdmin(World w, Guid who)
    {
        w.Probe.CallerSystemUserId = who;
        w.Probe.Hold(SubmissionOverrideService.AdminMarkerPrivilege);
    }

    private static List<Entity> Records(World w, string? code = null) =>
        w.Records.Created.Where(e => e.LogicalName == "sprk_decisionrecord" && (code is null || e.GetAttributeValue<string>("sprk_actioncode") == code)).ToList();

    private static JsonObject ValuesOf(Entity record) =>
        JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!["steps"]!.AsArray().Single()!["values"]!.AsObject();

    private static string Fingerprint(Entity record) => string.Join(";", record.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal)
        .Select(a => a.Key + "=" + (a.Value switch { EntityReference r => r.LogicalName + ":" + r.Id, OptionSetValue o => o.Value.ToString(), var v => v?.ToString() })));

    private static JsonObject StampOf(World w, string member) => JsonNode.Parse(w.FindingsJson)![member]!.AsObject();

    private static string? DecisionNoteOf(World w) =>
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn) ? w.Invoice[InvoiceCorrectionNote.DecisionColumn] as string : null;

    /// <summary>Everything a finish can write, as it stands now: compared before and after a refused call.</summary>
    private sealed record Snapshot(
        int RecordCreates, int Recorded, int Updates, int Transactions, int Transitions, int SetStores, int Closes, int Notified,
        string Findings, string? Code, InvoiceLifecycleStatus InvoiceStatus, string? DecisionNote, string Signals)
    {
        public static Snapshot Of(World w) => new(
            w.RecordCreates, w.Recorded.Count, w.Updates.Count, w.Transactions.Count, w.Transitions.Count, w.SetStores.Count, w.Closes.Count,
            w.Notified.Count, w.FindingsJson, w.Code, w.InvoiceStatus, DecisionNoteOf(w),
            string.Join(",", w.Signals.Select(s => $"{s.Id}:{s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value}")));
    }

    /// <summary>
    /// Decider A (<see cref="World.Caller"/>) records a decision that then fails before it finishes. The 095f-2c strands, plus two where the
    /// Signal closes fail too (so the finish must close them, crediting A): <c>override-closes-and-release</c>, <c>return-closes-and-transition</c>.
    /// Every strand has one Open Signal on the invoice before A decides.
    /// </summary>
    private static async Task<BlockedDecisionResult> StrandAsync(World w, string strand)
    {
        if (strand == "return-closes-and-transition")
        {
            w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
            w.CloseFault = _ => new TimeoutException("dataverse");
            w.TransitionFault = new TimeoutException("dataverse");
            var first = await w.Service.ReturnAsync(World.SubmissionId, Reason, FirstNote, World.Token, CancellationToken.None);
            w.CloseFault = _ => null;
            w.TransitionFault = null;
            return first;
        }

        if (strand != "override-closes-and-release")
        {
            w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE"); // override-closes-and-release adds its own
        }

        return await SubmissionOverrideDecisionNoteTests.StrandAsync(w, strand, FirstNote);
    }

    // ── The finish: as recorded, crediting the decider, plus one operator record ──

    [Theory(DisplayName = "iss034a AC1: a decision stranded by decider A is finished by operator B exactly as recorded: released or returned, A's record unchanged, Signals closed crediting A under A's record, the stamp and column hold A's text, and ONE finish-stranded-decision record by B names A's record")]
    [InlineData("override-release")]
    [InlineData("override-closes-and-release")]
    [InlineData("override-invoice-bound")]
    [InlineData("return-transition")]
    [InlineData("return-closes-and-transition")]
    [InlineData("return-state-write")]
    public async Task StrandedDecision_IsFinishedByAnOperator_AsRecorded(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        (await StrandAsync(w, strand)).Kind.Should().Be(BlockedDecisionKind.Refused, "the first call stranded");
        w.Code.Should().Be(SubmissionReasonCodes.Blocked, "the submission is still held");
        var decided = Records(w).Should().ContainSingle("A's record was written before the strand").Subject;
        var decidedBefore = Fingerprint(decided);
        // The writer harness answers every create with the same id (NewId), so A's record id is NewId; the operator record is told apart by its
        // code and its key (the harness's lookups match on the key in sprk_steps, as the writer's do).
        var decidedId = w.Records.NewId;
        ActAsAdmin(w, World.Operator);

        var finished = await Finish(w);

        finished.Kind.Should().Be(BlockedDecisionKind.Decided);
        finished.Outcome.Should().Be(verb == "override" ? "Overridden" : "Returned");
        finished.RecordId.Should().Be(decidedId, "the finished decision is A's; its record is the one the state names");
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
        if (verb == "override")
        {
            w.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
            w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview, "released to approval");
            DecisionNoteOf(w).Should().Be(FirstNote, "the release writes A's stored note, never the operator's");
        }
        else
        {
            w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.ReturnedToFirm);
            w.TransitionNotes.Where(n => n is not null).Should().OnlyContain(n => n == FirstNote, "the transition carries A's stored note");
        }

        // A's record is untouched (append-only), and the decision stays A's.
        Fingerprint(Records(w)[0]).Should().Be(decidedBefore);
        var stamp = StampOf(w, verb);
        stamp["reason"]!.GetValue<string>().Should().Be(Reason, "the stamp holds A's stored reason");
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller, "the stamp names A");
        Guid.Parse(stamp["recordId"]!.GetValue<string>()).Should().Be(decidedId);
        w.Signals.Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusResolved, "every Signal is closed");
        w.Closes.Should().NotBeEmpty().And.OnlyContain(c => c.DecidedBy == World.Caller && c.RecordId == decidedId, "closed under A's record, crediting A");
        w.Notified.Should().ContainSingle();

        // One operator record: B confirmed, B's reason and note, naming A's decision and record; it resolves no Signal.
        Records(w).Should().HaveCount(2);
        var op = Records(w, FinishCode).Should().ContainSingle().Subject;
        op.GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id.Should().Be(World.Operator);
        op.GetAttributeValue<string>("sprk_reason").Should().Be(OperatorReason);
        var values = ValuesOf(op);
        values["finishedDecision"]!.GetValue<string>().Should().Be(verb == "override" ? "override-submission" : "return-to-firm");
        values["finishedRecord"]!.GetValue<string>().Should().Be(decidedId.ToString("D"));
        values["note"]!.GetValue<string>().Should().Be(OperatorNote);
        w.Recorded.Last().DecisionId.Should().Be(SubmissionOverrideService.OperatorFinishDecisionIdFor(
            World.SubmissionId, verb == "override" ? SubmissionOverrideService.Verb.Override : SubmissionOverrideService.Verb.Return));
        w.Recorded.Last().ResolvedSignalIds.Should().BeNull("the finished decision's own record resolves the Signals");
        w.Order.IndexOf("record").Should().BeLessThan(w.Order.LastIndexOf("update"), "the operator record comes before the finish's state write");
    }

    [Fact(DisplayName = "iss034a: the operator record is written before the finish's first write (the Signal close), after the lock")]
    public async Task TheOperatorRecord_IsWrittenBeforeTheFirstFinishWrite()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-closes-and-release");
        ActAsAdmin(w, World.Operator);
        w.Order.Clear();

        (await Finish(w)).Kind.Should().Be(BlockedDecisionKind.Decided);

        w.Order.Should().ContainInOrder("lock", "record", "close", "update", "notify", "unlock");
        w.Order.Count(o => o == "record").Should().Be(1, "only the operator's record; A's is never recorded again");
    }

    // ── Refusals write nothing ──

    public static TheoryData<string, string, int, string> Refusals => new()
    {
        { "override-release", "no-admin-marker", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "return-transition", "no-admin-marker", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-release", "no-override-privilege", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-release", "privilege-probe-fault", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "return-transition", "privilege-probe-fault", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-release", "unidentified-caller", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-closes-and-release", "no-fr51-rights", 403, BlockedSubmissionErrors.NotAuthorized },
        { "return-closes-and-transition", "no-fr51-rights", 403, BlockedSubmissionErrors.NotAuthorized },
        { "override-release", "caller-is-the-decider", 409, BlockedSubmissionErrors.DecisionConflict },
        { "return-transition", "caller-is-the-decider", 409, BlockedSubmissionErrors.DecisionConflict },
        { "override-release", "lookup-fault", 503, BlockedSubmissionErrors.Unavailable },
        { "return-transition", "lookup-fault", 503, BlockedSubmissionErrors.Unavailable },
        { "override-release", "operator-record-lookup-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "override-release", "firm-gate", 409, BlockedSubmissionErrors.FirmMissing },
    };

    [Theory(DisplayName = "iss034a AC2: every refusal of the operator finish writes nothing (no operator record, no close, no release, no transition, no submission write, no notice)")]
    [MemberData(nameof(Refusals))]
    public async Task OperatorFinishRefusal_WritesNothing(string strand, string refusal, int status, string code)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, strand);
        ActAsAdmin(w, refusal == "caller-is-the-decider" ? World.Caller : World.Operator);
        var reads = w.Entities.Invocations.Count;
        var finds = w.FindCalls;
        switch (refusal)
        {
            case "no-admin-marker":
                w.Probe.Release(SubmissionOverrideService.AdminMarkerPrivilege);
                break;
            case "no-override-privilege":
                w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
                break;
            case "privilege-probe-fault":
                w.Probe.PrivilegeFault = new TimeoutException("dataverse");
                break;
            case "unidentified-caller":
                w.Probe.CallerSystemUserId = null;
                break;
            case "no-fr51-rights":
                w.Probe.Rights = (set, id) => Spaarke.Dataverse.AccessRights.Read;
                break;
            case "lookup-fault":
                w.FindFaultFor = id => id == SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId) ? new TimeoutException("dataverse") : null;
                break;
            case "operator-record-lookup-fault":
                w.FindFaultFor = id => id == SubmissionOverrideService.OperatorFinishDecisionIdFor(World.SubmissionId, SubmissionOverrideService.Verb.Override)
                    ? new TimeoutException("dataverse") : null;
                break;
            case "firm-gate":
                w.InvoiceOwnHold = SubmissionReasonCodes.FirmMissing; // the invoice is held for its firm
                break;
        }

        var before = Snapshot.Of(w);

        var refused = await Finish(w);

        refused.Kind.Should().Be(BlockedDecisionKind.Refused);
        refused.StatusCode.Should().Be(status);
        refused.ErrorCode.Should().Be(code);
        Snapshot.Of(w).Should().Be(before, "nothing is written");
        Records(w, FinishCode).Should().BeEmpty("no operator record");
        if (refusal is "no-admin-marker" or "unidentified-caller" || (refusal == "privilege-probe-fault"))
        {
            w.Entities.Invocations.Count.Should().Be(reads, "the admin marker is asked before anything about the submission is read");
            w.FindCalls.Should().Be(finds, "and before any record lookup");
        }

        if (refusal == "caller-is-the-decider")
        {
            refused.Detail.Should().Be(BlockedSubmissionErrors.OwnDecisionDetail, "the decider is told to repeat their own decision");
        }
    }

    [Fact(DisplayName = "iss034a AC2: nothing stranded (a held submission with neither record) is 409 nothing_stranded, nothing written")]
    public async Task NothingStranded_Is409_AndWritesNothing()
    {
        var w = await World.CreateAsync();
        ActAsAdmin(w, World.Operator);

        var refused = await Finish(w);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.NothingStranded);
        refused.ErrorCode.Should().Be("ebilling.blocked.nothing_stranded", "the wire code is a contract");
        w.AssertNothingWritten();
        w.HeldKeys.Should().BeEmpty();
        w.LockKeys.Should().BeEmpty("refused before the lock");
    }

    [Fact(DisplayName = "iss034a AC2: both records stored is 409 other_decision_pending (no finish either way), nothing written")]
    public async Task BothRecordsStored_Is409_AndWritesNothing()
    {
        var w = await World.CreateAsync();
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, "override-release", null);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(w.FindingsJson)!;
        var returnDecision = SubmissionOverrideService.BuildDecision(
            SubmissionOverrideService.Verb.Return, stored, World.SubmissionId, World.InvoiceId, World.Caller, Reason,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(w.Clock.GetUtcNow().UtcDateTime, []));
        await w.Recorder.RecordAsync(returnDecision, CancellationToken.None);
        ActAsAdmin(w, World.Operator);
        var before = Snapshot.Of(w);

        var refused = await Finish(w);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending);
        refused.Detail.Should().Be(BlockedSubmissionErrors.BothDecisionsRecordedDetail);
        Snapshot.Of(w).Should().Be(before);
    }

    [Fact(DisplayName = "iss034a: the operator's reason is checked by the decisions' rules before anything is read (400 reason_required)")]
    public async Task AMissingOperatorReason_Is400_BeforeAnyRead()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        ActAsAdmin(w, World.Operator);
        var reads = w.Entities.Invocations.Count;
        var before = Snapshot.Of(w);

        var refused = await Finish(w, reason: "  ");

        refused.StatusCode.Should().Be(400);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.ReasonRequired);
        w.Entities.Invocations.Count.Should().Be(reads);
        Snapshot.Of(w).Should().Be(before);
    }

    // ── Repeats ──

    [Theory(DisplayName = "iss034a AC3: a repeat of the operator finish after a full finish answers as an already-decided repeat, with no second operator record")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task ARepeatAfterAFullFinish_IsAlreadyDecided_WithNoSecondOperatorRecord(string strand)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, strand);
        ActAsAdmin(w, World.Operator);
        (await Finish(w)).Kind.Should().Be(BlockedDecisionKind.Decided);
        var before = Snapshot.Of(w);

        var repeat = await Finish(w, reason: "Another reason entirely.");

        repeat.Kind.Should().Be(BlockedDecisionKind.AlreadyDecided);
        repeat.Outcome.Should().Be(VerbOf(strand) == "override" ? "Overridden" : "Returned");
        Records(w, FinishCode).Should().ContainSingle();
        (Snapshot.Of(w) with { Closes = before.Closes }).Should().Be(before, "a repeat writes nothing (its re-close of the stored set finds every Signal already closed by the record)");
    }

    [Theory(DisplayName = "iss034a AC3: a finish interrupted after the operator record completes on the next call (even with another text) with still exactly ONE operator record")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task AFinishInterruptedAfterTheOperatorRecord_CompletesOnTheNextCall_WithOneOperatorRecord(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        ActAsAdmin(w, World.Operator);
        if (verb == "override")
        {
            w.UpdateFault = new TimeoutException("dataverse");
        }
        else
        {
            w.TransitionFault = new TimeoutException("dataverse");
        }

        var interrupted = await Finish(w);
        interrupted.StatusCode.Should().Be(503);
        Records(w, FinishCode).Should().ContainSingle("the operator record was written before the failed write");
        w.Code.Should().Be(SubmissionReasonCodes.Blocked, "still held");
        w.UpdateFault = null;
        w.TransitionFault = null;

        var completed = await Finish(w, reason: "Another reason entirely.", note: null);

        completed.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
        var op = Records(w, FinishCode).Should().ContainSingle("the repeat found the operator record and wrote no second one").Subject;
        op.GetAttributeValue<string>("sprk_reason").Should().Be(OperatorReason, "the first operator text stands");
        Records(w).Should().HaveCount(2);
    }

    [Fact(DisplayName = "iss034a (known limit): an operator finish interrupted after its record is another operator's to complete: a second operator is refused 409 decision_conflict, nothing written")]
    public async Task AnInterruptedFinish_IsRefusedToASecondOperator()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        ActAsAdmin(w, World.Operator);
        w.UpdateFault = new TimeoutException("dataverse");
        (await Finish(w)).StatusCode.Should().Be(503);
        w.UpdateFault = null;
        w.Probe.CallerSystemUserId = SecondOperator;
        var before = Snapshot.Of(w);

        var refused = await Finish(w);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.DecisionConflict);
        refused.Detail.Should().Be(BlockedSubmissionErrors.AnotherOperatorDetail);
        Snapshot.Of(w).Should().Be(before);
    }

    // ── Must not fire: the existing routes are unchanged ──

    [Theory(DisplayName = "iss034a must-not-fire: a billing admin who is not the decider still gets 409 decision_conflict from the ordinary repeat and other_decision_pending from the other verb; nothing written")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task TheExistingRoutes_AreUnchanged_ForAnAdmin(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        ActAsAdmin(w, World.Operator);
        var before = Snapshot.Of(w);

        var repeat = await Decide(w, verb);
        repeat.StatusCode.Should().Be(409);
        repeat.ErrorCode.Should().Be(BlockedSubmissionErrors.DecisionConflict);

        var other = await Decide(w, verb == "override" ? "return" : "override");
        other.StatusCode.Should().Be(409);
        other.ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending);
        Snapshot.Of(w).Should().Be(before);

        // The decider's own repeat still finishes, with no operator record.
        w.Probe.CallerSystemUserId = World.Caller;
        (await Decide(w, verb)).Kind.Should().Be(BlockedDecisionKind.Decided);
        Records(w, FinishCode).Should().BeEmpty();
    }

    // ── GET: the read-only hints ──

    [Fact(DisplayName = "iss034a AC5: GET shows the stranded Override to the operator, who may finish it")]
    public async Task Get_ShowsTheStrandedDecision_AndCanFinish_ForTheOperator()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var recordId = w.Records.NewId;
        ActAsAdmin(w, World.Operator);

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.StrandedDecision.Should().Be(new BlockedStrandedDecision("Override", recordId, CallerIsDecider: false));
        view.CanFinishAsOperator.Should().BeTrue();
    }

    [Fact(DisplayName = "iss034a AC5: GET shows a stranded Return to an operator WITHOUT the Override privilege, who may finish it (a Return needs none)")]
    public async Task Get_AStrandedReturn_CanBeFinishedWithoutTheOverridePrivilege()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "return-transition");
        ActAsAdmin(w, World.Operator);
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.StrandedDecision!.Verb.Should().Be("Return");
        view.CanFinishAsOperator.Should().BeTrue();
    }

    [Theory(DisplayName = "iss034a AC5: GET gives CanFinishAsOperator false for the decider, a non-admin, an operator without the Override privilege and a probe fault")]
    [InlineData("decider")]
    [InlineData("non-admin")]
    [InlineData("no-override-privilege")]
    [InlineData("probe-fault")]
    public async Task Get_CannotFinish_ForTheDecider_ANonAdmin_OrAProbeFault(string who)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var recordId = w.Records.NewId;
        ActAsAdmin(w, who == "decider" ? World.Caller : World.Operator);
        switch (who)
        {
            case "non-admin":
                w.Probe.Release(SubmissionOverrideService.AdminMarkerPrivilege);
                break;
            case "no-override-privilege":
                w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
                break;
            case "probe-fault":
                w.Probe.PrivilegeFault = new TimeoutException("dataverse");
                break;
        }

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.CanFinishAsOperator.Should().BeFalse();
        view.StrandedDecision.Should().Be(new BlockedStrandedDecision("Override", recordId, CallerIsDecider: who == "decider"));
    }

    [Fact(DisplayName = "iss034a AC5: GET gives no stranded hint when nothing is stranded, or when the lookup faults (the view still loads)")]
    public async Task Get_NoHint_WhenNothingIsStranded_OrTheLookupFaults()
    {
        var w = await World.CreateAsync();
        ActAsAdmin(w, World.Operator);
        var clean = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;
        clean.StrandedDecision.Should().BeNull();
        clean.CanFinishAsOperator.Should().BeFalse();
        clean.CanOverride.Should().BeTrue("the ordinary hints are unchanged");

        await StrandAsync(w, "override-release");
        w.FindFault = new TimeoutException("dataverse");
        var faulted = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;
        faulted.StrandedDecision.Should().BeNull();
        faulted.CanFinishAsOperator.Should().BeFalse();
        faulted.State.Should().Be(BlockedSubmissionStates.Blocked);
    }

    [Fact(DisplayName = "iss034a: the admin marker is BillingAdminAuthorization.Admin()'s one check (no new privilege)")]
    public void TheAdminMarker_IsTheBillingAdminCheck()
    {
        SubmissionOverrideService.AdminMarkerPrivilege.Should().Be(BillingAdminAuthorization.WriteRuleSetPrivilege);
        BlockedSubmissionErrors.OperatorNotPermitted.Should().Be("ebilling.blocked.operator_not_permitted", "the wire code is a contract");
    }
}
