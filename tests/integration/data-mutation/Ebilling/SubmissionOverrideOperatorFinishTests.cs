using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Filters;
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
/// <remarks>Review round 1: the writer harness gives every create its own id (<c>WithDistinctCreateIds</c>), so A's record and the operator's
/// are told apart by id; the operator's record is one per submission, verb and operator (F1).</remarks>
public sealed class SubmissionOverrideOperatorFinishTests
{
    private const string Reason = "Negotiated rate is not yet in the rate card.";
    private const string FirstNote = "First note";
    private const string OperatorReason = "The decider has left; finishing their recorded decision.";
    private const string OperatorNote = "Operator note";
    private const string FinishCode = "finish-stranded-decision";

    /// <summary>The characters a <c>"padding"</c> member adds to a JSON object beside its value: <c>,"padding":""</c>.</summary>
    private const int PaddingOverhead = 13;

    private static string VerbOf(string strand) => strand.StartsWith("return", StringComparison.Ordinal) ? "return" : "override";

    private static SubmissionOverrideService.Verb ServiceVerb(string verb) =>
        verb == "override" ? SubmissionOverrideService.Verb.Override : SubmissionOverrideService.Verb.Return;

    private static Task<BlockedDecisionResult> Finish(World w, string? reason = OperatorReason, string? note = OperatorNote) =>
        w.Service.FinishStrandedAsync(World.SubmissionId, reason, note, World.Token, CancellationToken.None);

    private static Task<BlockedDecisionResult> Decide(World w, string verb, string? reason = Reason) =>
        verb == "override"
            ? w.Service.OverrideAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None)
            : w.Service.ReturnAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None);

    private static IEnumerable<string> AdminMarker() => SubmissionOverrideService.AdminMarkerChecks().Select(c => c.Operation);

    /// <summary>The caller becomes <paramref name="who"/>, a billing admin (every check of the marker; the World already holds the Override privilege).</summary>
    private static void ActAsAdmin(World w, Guid who)
    {
        w.Probe.CallerSystemUserId = who;
        foreach (var privilege in AdminMarker())
        {
            w.Probe.Hold(privilege);
        }
    }

    private static void ReleaseAdmin(World w)
    {
        foreach (var privilege in AdminMarker())
        {
            w.Probe.Release(privilege);
        }
    }

    private static List<Entity> Records(World w, string? code = null) =>
        w.Records.Created.Where(e => e.LogicalName == "sprk_decisionrecord" && (code is null || e.GetAttributeValue<string>("sprk_actioncode") == code)).ToList();

    private static Entity OperatorRecordOf(World w, Guid operatorId) =>
        Records(w, FinishCode).Should().ContainSingle(r => r.GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id == operatorId).Subject;

    private static JsonObject StepOf(Entity record) =>
        JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!["steps"]!.AsArray().Single()!.AsObject();

    private static JsonObject ValuesOf(Entity record) => StepOf(record)["values"]!.AsObject();

    private static string Fingerprint(Entity record) => string.Join(";", record.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal)
        .Select(a => a.Key + "=" + (a.Value switch { EntityReference r => r.LogicalName + ":" + r.Id, OptionSetValue o => o.Value.ToString(), var v => v?.ToString() })));

    private static JsonObject StampOf(World w, string member) => JsonNode.Parse(w.FindingsJson)![member]!.AsObject();

    private static string? DecisionNoteOf(World w) =>
        w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn) ? w.Invoice[InvoiceCorrectionNote.DecisionColumn] as string : null;

    private static DecisionRecordRefusedException LookupFailed() =>
        new(DecisionRecordRefusalReason.BillingRecordLookupFailed, "the lookup faulted", innerException: new TimeoutException("dataverse"));

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

    /// <summary>Operator B (<see cref="World.Operator"/>) starts the finish of A's stranded decision; it writes B's record and then faults (the
    /// release, or the transition), so B's record exists and the decision is still held.</summary>
    private static async Task InterruptedOperatorFinishAsync(World w, string verb)
    {
        ActAsAdmin(w, World.Operator);
        if (verb == "override")
        {
            w.UpdateFault = new TimeoutException("dataverse");
        }
        else
        {
            w.TransitionFault = new TimeoutException("dataverse");
        }

        (await Finish(w)).StatusCode.Should().Be(503, "B's finish was interrupted after B's record");
        w.UpdateFault = null;
        w.TransitionFault = null;
        OperatorRecordOf(w, World.Operator);
        w.Code.Should().Be(SubmissionReasonCodes.Blocked, "still held");
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
        var decidedId = decided.Id; // review round 1 (F2): every record has its own id, so A's is told apart from the operator's by id
        decidedId.Should().NotBeEmpty();
        ActAsAdmin(w, World.Operator);

        var finished = await Finish(w);

        finished.Kind.Should().Be(BlockedDecisionKind.Decided);
        finished.Outcome.Should().Be(verb == "override" ? "Overridden" : "Returned");
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

        // One operator record: B confirmed, B's reason and note, naming A's decision and record; it resolves no Signal; its id is its own.
        Records(w).Should().HaveCount(2);
        var op = Records(w, FinishCode).Should().ContainSingle().Subject;
        op.Id.Should().NotBe(decidedId, "the operator's record is a record of its own");
        op.GetAttributeValue<EntityReference>("sprk_confirmedby")!.Id.Should().Be(World.Operator);
        op.GetAttributeValue<string>("sprk_reason").Should().Be(OperatorReason);
        StepOf(op)["outcome"]!.GetValue<string>().Should().Be("FinishRequested", "review round 1: the record says the finish was asked for, never that it finished");
        var values = ValuesOf(op);
        values["finishedDecision"]!.GetValue<string>().Should().Be(verb == "override" ? "override-submission" : "return-to-firm");
        values["finishedRecord"]!.GetValue<string>().Should().Be(decidedId.ToString("D"), "the operator record names A's record");
        values["note"]!.GetValue<string>().Should().Be(OperatorNote);
        w.Recorded.Last().DecisionId.Should().Be(SubmissionOverrideService.OperatorFinishDecisionIdFor(World.SubmissionId, ServiceVerb(verb), World.Operator));
        w.Recorded.Last().ResolvedSignalIds.Should().BeNull("the finished decision's own record resolves the Signals");
        w.Order.IndexOf("record").Should().BeLessThan(w.Order.LastIndexOf("update"), "the operator record comes before the finish's state write");

        // The response, the stamp and the closes name A's record, never the operator's (F2).
        finished.RecordId.Should().Be(decidedId, "the finished decision is A's; its record is the one the state names").And.NotBe(op.Id);
        Fingerprint(Records(w)[0]).Should().Be(decidedBefore, "A's record is untouched (append-only)");
        var stamp = StampOf(w, verb);
        stamp["reason"]!.GetValue<string>().Should().Be(Reason, "the stamp holds A's stored reason");
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller, "the stamp names A");
        Guid.Parse(stamp["recordId"]!.GetValue<string>()).Should().Be(decidedId, "the stamp names A's record, not the operator's");
        w.Signals.Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusResolved, "every Signal is closed");
        w.Closes.Should().NotBeEmpty().And.OnlyContain(c => c.DecidedBy == World.Caller && c.RecordId == decidedId, "closed under A's record, crediting A");
        w.ClosedBy.Values.Should().OnlyContain(id => id == decidedId, "every Signal stands closed by A's record");
        w.Notified.Should().ContainSingle();
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
        { "override-release", "override-probe-fault-only", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-release", "unidentified-caller", 403, BlockedSubmissionErrors.OperatorNotPermitted },
        { "override-closes-and-release", "no-fr51-rights", 403, BlockedSubmissionErrors.NotAuthorized },
        { "return-closes-and-transition", "no-fr51-rights", 403, BlockedSubmissionErrors.NotAuthorized },
        { "override-release", "caller-is-the-decider", 409, BlockedSubmissionErrors.OwnDecisionPending },
        { "return-transition", "caller-is-the-decider", 409, BlockedSubmissionErrors.OwnDecisionPending },
        { "override-release", "lookup-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "return-transition", "lookup-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "override-release", "operator-record-lookup-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "override-release", "operator-record-write-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "return-transition", "operator-record-write-fault", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "override-release", "operator-record-writer-refusal", 503, BlockedSubmissionErrors.RecordUnavailable },
        { "override-release", "stored-record-names-no-decider", 503, BlockedSubmissionErrors.Unavailable },
        { "return-transition", "stored-record-names-no-decider", 503, BlockedSubmissionErrors.Unavailable },
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
                ReleaseAdmin(w);
                break;
            case "no-override-privilege":
                w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
                break;
            case "privilege-probe-fault":
                w.Probe.PrivilegeFault = new TimeoutException("dataverse");
                break;
            case "override-probe-fault-only":
                // Review round 1: the marker is held and answered; only the Override privilege's probe faults (fail closed).
                w.Probe.PrivilegeFaultFor = p => p == EbillingOverridePrivilege.PrivilegeName ? new TimeoutException("dataverse") : null;
                break;
            case "unidentified-caller":
                w.Probe.CallerSystemUserId = null;
                break;
            case "no-fr51-rights":
                w.Probe.Rights = (set, id) => Spaarke.Dataverse.AccessRights.Read;
                break;
            case "lookup-fault":
                // Review round 1: as the real lookup faults (FindBillingDecisionResolvedSetAsync wraps every fault as a refusal).
                w.FindFaultFor = id => id == SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId) ? LookupFailed() : null;
                break;
            case "operator-record-lookup-fault":
                w.FindFaultFor = id => id == SubmissionOverrideService.OperatorFinishDecisionIdFor(World.SubmissionId, SubmissionOverrideService.Verb.Override, World.Operator)
                    ? LookupFailed() : null;
                break;
            case "operator-record-write-fault":
                w.RecordFault = new TimeoutException("dataverse"); // only the operator's record is written by a finish
                break;
            case "operator-record-writer-refusal":
                w.RecordFault = LookupFailed();
                break;
            case "stored-record-names-no-decider":
                Records(w).Single().Attributes.Remove("sprk_confirmedby"); // a corrupted record (K4): no decider to credit
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
        if (refusal.StartsWith("operator-record-write", StringComparison.Ordinal))
        {
            // The write was attempted (the recorder was asked) and failed: no record was created, and nothing after it ran.
            w.Recorded.Should().HaveCount(before.Recorded + 1);
            (Snapshot.Of(w) with { Recorded = before.Recorded }).Should().Be(before, "nothing is written");
        }
        else
        {
            Snapshot.Of(w).Should().Be(before, "nothing is written");
        }

        Records(w, FinishCode).Should().BeEmpty("no operator record");
        if (refusal is "no-admin-marker" or "unidentified-caller" or "privilege-probe-fault")
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

    [Fact(DisplayName = "iss034a AC2 (review round 1): both records stored is 409 both_decisions_recorded (its own code: needs support), nothing written")]
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
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.BothDecisionsRecorded).And.Be("ebilling.blocked.both_decisions_recorded", "the wire code is a contract");
        refused.Detail.Should().Be(BlockedSubmissionErrors.BothDecisionsRecordedDetail);
        Snapshot.Of(w).Should().Be(before);
    }

    [Fact(DisplayName = "iss034a (review round 1): the caller-is-the-decider code is its own stable wire code")]
    public void TheOperatorRouteCodes_AreStable()
    {
        BlockedSubmissionErrors.OwnDecisionPending.Should().Be("ebilling.blocked.own_decision_pending");
        BlockedSubmissionErrors.BothDecisionsRecorded.Should().Be("ebilling.blocked.both_decisions_recorded");
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

    // ── Review round 1 (F4): the stamp's size is checked before any write ──

    [Fact(DisplayName = "iss034a review round 1 (F4): an operator finish whose stamp does not fit the findings column is 409 findings_too_large BEFORE the operator record and the Signal closes; nothing written")]
    public async Task AStampThatDoesNotFit_IsRefusedBeforeTheOperatorRecord()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var decidedId = Records(w).Single().Id;
        // Pad the stored findings so that they fit, but not with the finish's stamp (the margin is half the stamp, either way).
        var stampLength = SubmissionOverrideService.WithStamp(w.FindingsJson, SubmissionOverrideService.OverrideMember,
            new BlockedDecisionStamp(decidedId, World.Caller, w.Clock.GetUtcNow(), Reason)).Length - w.FindingsJson.Length;
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root["padding"] = new string('p', InvoiceSubmissionPersister.FindingsJsonMaxLength - (stampLength / 2) - w.FindingsJson.Length - PaddingOverhead);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        w.FindingsJson.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength, "the findings fit");
        (w.FindingsJson.Length + stampLength).Should().BeGreaterThan(InvoiceSubmissionPersister.FindingsJsonMaxLength, "the stamp does not");
        ActAsAdmin(w, World.Operator);
        var before = Snapshot.Of(w);

        var refused = await Finish(w);

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsTooLarge);
        Snapshot.Of(w).Should().Be(before, "nothing is written: no operator record, no Signal close");
        Records(w, FinishCode).Should().BeEmpty();
    }

    [Fact(DisplayName = "iss034a review round 1 (F4, found in passing): a first decision whose Signal set fits the findings but whose stamp does not is 409 findings_too_large BEFORE the set's store and the Decision Record; nothing written")]
    public async Task ADecisionWhoseStampDoesNotFit_IsRefusedBeforeAnyWrite()
    {
        // The lengths, measured on an unpadded world: the set the decision stores (no Signal is open) and the stamp it writes.
        var probe = await World.CreateAsync();
        var plain = probe.FindingsJson;
        var withSet = SubmissionOverrideService.WithHumanDecisionSet(plain, SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId),
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(probe.Clock.GetUtcNow().UtcDateTime, []), []);
        var setLength = withSet.Length - plain.Length;
        var stampLength = SubmissionOverrideService.WithStamp(withSet, SubmissionOverrideService.OverrideMember,
            new BlockedDecisionStamp(Guid.NewGuid(), World.Caller, probe.Clock.GetUtcNow(), Reason)).Length - withSet.Length;
        var padding = InvoiceSubmissionPersister.FindingsJsonMaxLength - setLength - (stampLength / 2) - plain.Length - PaddingOverhead;
        var w = await World.CreateAsync(json =>
        {
            json["padding"] = new string('p', padding);
            return json;
        });
        (w.FindingsJson.Length + setLength).Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength, "the set fits");
        (w.FindingsJson.Length + setLength + stampLength).Should().BeGreaterThan(InvoiceSubmissionPersister.FindingsJsonMaxLength, "the stamp does not");

        var refused = await w.OverrideAsync();

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsTooLarge);
        w.AssertNothingWritten();
        w.Records.Created.Should().BeEmpty("no Decision Record");
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

    [Theory(DisplayName = "iss034a AC3: a finish interrupted after the operator record completes on the same operator's next call (even with another text) with still exactly ONE operator record")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task AFinishInterruptedAfterTheOperatorRecord_CompletesOnTheNextCall_WithOneOperatorRecord(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        await InterruptedOperatorFinishAsync(w, verb);

        var completed = await Finish(w, reason: "Another reason entirely.", note: null);

        completed.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
        var op = Records(w, FinishCode).Should().ContainSingle("the repeat found the operator record and wrote no second one").Subject;
        op.GetAttributeValue<string>("sprk_reason").Should().Be(OperatorReason, "the first operator text stands");
        Records(w).Should().HaveCount(2);
    }

    [Theory(DisplayName = "iss034a review round 1 (F1): a finish operator B left interrupted after B's record is completed by operator C, who writes C's own record; each operator has exactly one record and the decision is finished once")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task AnInterruptedFinish_IsCompletedByASecondOperator_WithTheirOwnRecord(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        var decidedId = Records(w).Single().Id;
        await InterruptedOperatorFinishAsync(w, verb);
        var decidedCode = verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned;
        ActAsAdmin(w, World.SecondOperator);

        var completed = await Finish(w, reason: "B is away; completing the finish.", note: null);

        completed.Kind.Should().Be(BlockedDecisionKind.Decided);
        completed.RecordId.Should().Be(decidedId, "the finished decision is still A's");
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
        var b = OperatorRecordOf(w, World.Operator);
        var c = OperatorRecordOf(w, World.SecondOperator);
        Records(w, FinishCode).Should().HaveCount(2, "exactly one record for each operator");
        c.Id.Should().NotBe(b.Id);
        c.GetAttributeValue<string>("sprk_reason").Should().Be("B is away; completing the finish.");
        ValuesOf(b)["finishedRecord"]!.GetValue<string>().Should().Be(decidedId.ToString("D"));
        ValuesOf(c)["finishedRecord"]!.GetValue<string>().Should().Be(decidedId.ToString("D"));
        Records(w, verb == "override" ? "override-submission" : "return-to-firm").Should().ContainSingle("A's decision is recorded once");
        w.Notified.Should().ContainSingle("the decision is announced once");
        w.Updates.Count(u => u.TryGetValue(InvoiceSubmissionPersister.SubmissionReasonCode, out var written) && Equals(written, decidedCode)).Should().Be(1, "the decided state is written once");
        Guid.Parse(StampOf(w, verb)["recordId"]!.GetValue<string>()).Should().Be(decidedId);
    }

    [Theory(DisplayName = "iss034a review round 1: after operator B's interrupted finish, decider A's own repeat finishes the decision with no operator record of its own, and B's record stays")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task AfterAnInterruptedOperatorFinish_TheDecidersOwnRepeatFinishes_AndTheOperatorRecordStays(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        var decidedId = Records(w).Single().Id;
        await InterruptedOperatorFinishAsync(w, verb);
        var b = Fingerprint(OperatorRecordOf(w, World.Operator));
        w.Probe.CallerSystemUserId = World.Caller;

        var repeated = await Decide(w, verb);

        repeated.Kind.Should().Be(BlockedDecisionKind.Decided);
        repeated.RecordId.Should().Be(decidedId);
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
        Fingerprint(OperatorRecordOf(w, World.Operator)).Should().Be(b, "B's record stays as written (append-only)");
        Records(w).Should().HaveCount(2, "A's record and B's; the decider's repeat writes none");
        w.Notified.Should().ContainSingle();
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

    // ── Disabled decider (question from uac-r2) ──

    [Theory(DisplayName = "iss034a review round 1: a finish credits the stored decider only as lookup values and stamp text (the Signal closes' resolved-by, the stamp); no write assigns, owns or notifies the decider, so a disabled decider does not stop it")]
    [InlineData("override-closes-and-release")]
    [InlineData("return-closes-and-transition")]
    public async Task AFinish_NamesTheDeciderOnlyAsALookupAndInTheStamp(string strand)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, strand);
        ActAsAdmin(w, World.Operator);
        var stateWritesBefore = w.Updates.Count;

        (await Finish(w)).Kind.Should().Be(BlockedDecisionKind.Decided);

        // The closes credit A (sprk_resolvedby, a systemuser lookup: Dataverse accepts one to a disabled user).
        w.Closes.Should().OnlyContain(c => c.DecidedBy == World.Caller);
        // No state write (submission, release transaction, invoice) carries A as a lookup, an owner or anything else.
        var written = w.Updates.Skip(stateWritesBefore).SelectMany(u => u.Values)
            .Concat(w.Transactions.SelectMany(t => t.OfType<Microsoft.Xrm.Sdk.Messages.UpdateRequest>()).SelectMany(u => u.Target.Attributes.Values));
        written.OfType<EntityReference>().Should().NotContain(r => r.Id == World.Caller, "nothing is assigned to or owned by the decider");
        // The operator record is the operator's, and the notification names no person.
        var op = Records(w, FinishCode).Single();
        op.Attributes.Values.OfType<EntityReference>().Should().NotContain(r => r.Id == World.Caller, "the operator record links no lookup to the decider");
        typeof(SubmissionDecidedNotification).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("User", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Recipient", StringComparison.OrdinalIgnoreCase) || n.Contains("Decider", StringComparison.OrdinalIgnoreCase), "the notice carries no person");
        w.Notified.Single().Should().NotBeNull();
    }

    // ── GET: the read-only hints ──

    [Fact(DisplayName = "iss034a AC5: GET shows the stranded Override to the operator, who may finish it")]
    public async Task Get_ShowsTheStrandedDecision_AndCanFinish_ForTheOperator()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var recordId = Records(w).Single().Id;
        ActAsAdmin(w, World.Operator);

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.StrandedDecision.Should().Be(new BlockedStrandedDecision("Override", recordId, CallerIsDecider: false));
        view.CanFinishAsOperator.Should().BeTrue();
    }

    [Fact(DisplayName = "iss034a review round 1: GET runs the two record lookups together, and asks the Override privilege once (the hint reuses canOverride's answer)")]
    public async Task Get_RunsTheLookupsTogether_AndAsksTheOverridePrivilegeOnce()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var recordId = Records(w).Single().Id;
        ActAsAdmin(w, World.Operator);
        // The Override record's lookup completes only once the Return record's lookup has started: run one after the other, it times out
        // (a fault, so no hint); run together, both complete.
        var returnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        w.FindGate = id =>
        {
            if (id == SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId))
            {
                returnStarted.TrySetResult();
                return Task.CompletedTask;
            }

            return id == SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId)
                ? returnStarted.Task.WaitAsync(TimeSpan.FromSeconds(5))
                : Task.CompletedTask;
        };
        w.Probe.Calls.Clear();

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.StrandedDecision.Should().Be(new BlockedStrandedDecision("Override", recordId, CallerIsDecider: false), "both lookups completed");
        view.CanFinishAsOperator.Should().BeTrue();
        view.CanOverride.Should().BeTrue();
        w.Probe.Calls.Count(c => c.Privilege == EbillingOverridePrivilege.PrivilegeName).Should().Be(1, "the hint reuses canOverride's privilege answer");
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
        var recordId = Records(w).Single().Id;
        ActAsAdmin(w, who == "decider" ? World.Caller : World.Operator);
        switch (who)
        {
            case "non-admin":
                ReleaseAdmin(w);
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

    [Fact(DisplayName = "iss034a AC5: GET gives no stranded hint when nothing is stranded, or when a lookup faults (the view still loads)")]
    public async Task Get_NoHint_WhenNothingIsStranded_OrTheLookupFaults()
    {
        var w = await World.CreateAsync();
        ActAsAdmin(w, World.Operator);
        var clean = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;
        clean.StrandedDecision.Should().BeNull();
        clean.CanFinishAsOperator.Should().BeFalse();
        clean.CanOverride.Should().BeTrue("the ordinary hints are unchanged");

        await StrandAsync(w, "override-release");
        w.FindFault = LookupFailed();
        var faulted = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;
        faulted.StrandedDecision.Should().BeNull();
        faulted.CanFinishAsOperator.Should().BeFalse();
        faulted.State.Should().Be(BlockedSubmissionStates.Blocked);

        w.FindFault = null;
        w.FindFaultFor = id => id == SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId) ? LookupFailed() : null;
        var oneFaulted = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;
        oneFaulted.StrandedDecision.Should().BeNull("a fault in either lookup gives no hint");
        oneFaulted.CanFinishAsOperator.Should().BeFalse();
    }

    // ── iss034c (EB-D-253): the stamp names the user whose call completed the decision ──

    private const string CompletedMember = "completedBySystemUserId";

    private static Guid? CompletedBy(JsonObject stamp) =>
        stamp[CompletedMember] is { } node ? Guid.Parse(node.GetValue<string>()) : null;

    [Theory(DisplayName = "iss034c AC1: a first-time Override and a first-time Return write the decider as the completer")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task AFirstTimeDecision_WritesTheDeciderAsTheCompleter(string verb)
    {
        var w = await World.CreateAsync();

        (await Decide(w, verb)).Kind.Should().Be(BlockedDecisionKind.Decided);

        var stamp = StampOf(w, verb);
        CompletedBy(stamp).Should().Be(World.Caller);
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller);
    }

    [Theory(DisplayName = "iss034c AC1: the decider's own repeat of a stranded decision writes the decider as the completer (no operator was involved)")]
    [InlineData("override-release")]
    [InlineData("override-closes-and-release")]
    [InlineData("override-invoice-bound")]
    [InlineData("return-transition")]
    [InlineData("return-closes-and-transition")]
    [InlineData("return-state-write")]
    public async Task TheDecidersRepeat_WritesTheDeciderAsTheCompleter(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        JsonNode.Parse(w.FindingsJson)![verb].Should().BeNull("the stranded write never reached the stamp, so no completer exists yet");
        w.Probe.CallerSystemUserId = World.Caller;

        (await Decide(w, verb)).Kind.Should().Be(BlockedDecisionKind.Decided);

        var stamp = StampOf(w, verb);
        CompletedBy(stamp).Should().Be(World.Caller);
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller);
    }

    [Theory(DisplayName = "iss034c AC1: an operator finish writes the operator as the completer while bySystemUserId stays the stored decider; the Signal closes and the stored record still credit the decider")]
    [InlineData("override-release")]
    [InlineData("override-closes-and-release")]
    [InlineData("override-invoice-bound")]
    [InlineData("return-transition")]
    [InlineData("return-closes-and-transition")]
    [InlineData("return-state-write")]
    public async Task AnOperatorFinish_WritesTheOperatorAsTheCompleter_AndKeepsTheDecider(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        var decidedId = Records(w).Single().Id;
        ActAsAdmin(w, World.Operator);

        (await Finish(w)).Kind.Should().Be(BlockedDecisionKind.Decided);

        var stamp = StampOf(w, verb);
        CompletedBy(stamp).Should().Be(World.Operator, "the operator's call made the completing write");
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller, "the stored decider is unchanged");
        Guid.Parse(stamp["recordId"]!.GetValue<string>()).Should().Be(decidedId);
        stamp["reason"]!.GetValue<string>().Should().Be(Reason);
        w.Closes.Should().OnlyContain(c => c.DecidedBy == World.Caller, "the closes still credit the stored decider");
    }

    [Theory(DisplayName = "iss034c AC2: a finish that failed before the completing write leaves no completer; a later completion by someone else writes that person, never the earlier operator")]
    [InlineData("override-release", "operator")]
    [InlineData("return-transition", "operator")]
    [InlineData("override-release", "decider")]
    [InlineData("return-transition", "decider")]
    public async Task AFailedFinish_WritesNoCompleter_AndALaterCompletionNamesItsOwnCaller(string strand, string laterCompleter)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        await InterruptedOperatorFinishAsync(w, verb); // operator B's finish faulted after B's record, before the stamp
        JsonNode.Parse(w.FindingsJson)![verb].Should().BeNull("B's interrupted finish wrote no stamp, so it named no completer");
        w.FindingsJson.Should().NotContain(World.Operator.ToString("D"), "B appears nowhere in the findings");
        var expected = laterCompleter == "decider" ? World.Caller : World.SecondOperator;
        if (laterCompleter == "decider")
        {
            w.Probe.CallerSystemUserId = World.Caller;
            (await Decide(w, verb)).Kind.Should().Be(BlockedDecisionKind.Decided);
        }
        else
        {
            ActAsAdmin(w, World.SecondOperator);
            (await Finish(w, reason: "B is away; completing the finish.", note: null)).Kind.Should().Be(BlockedDecisionKind.Decided);
        }

        var stamp = StampOf(w, verb);
        CompletedBy(stamp).Should().Be(expected);
        Guid.Parse(stamp["bySystemUserId"]!.GetValue<string>()).Should().Be(World.Caller);
    }

    [Theory(DisplayName = "iss034c AC2: a refused finish (no admin marker, the decider as the caller) writes no stamp and no completer")]
    [InlineData("no-marker")]
    [InlineData("own-decision")]
    public async Task ARefusedFinish_WritesNoCompleter(string refusal)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        if (refusal == "no-marker")
        {
            ActAsAdmin(w, World.Operator);
            ReleaseAdmin(w);
        }
        else
        {
            ActAsAdmin(w, World.Caller);
        }

        var before = Snapshot.Of(w);
        (await Finish(w)).StatusCode.Should().Be(refusal == "no-marker" ? 403 : 409);

        Snapshot.Of(w).Should().Be(before);
        JsonNode.Parse(w.FindingsJson)![SubmissionOverrideService.OverrideMember].Should().BeNull();
    }

    [Fact(DisplayName = "iss034c AC3 must-not-fire: a stamp written before the completer existed reads as 'not recorded' (null completer, no fault); a malformed or empty member reads the same way")]
    public void AStampWithoutTheMember_ReadsAsNotRecorded()
    {
        var recordId = Guid.NewGuid();
        var decider = Guid.NewGuid();
        var old = new JsonObject
        {
            ["override"] = new JsonObject
            {
                ["recordId"] = recordId.ToString("D"),
                ["bySystemUserId"] = decider.ToString("D"),
                ["on"] = "2026-11-03T10:00:00.0000000Z",
                ["reason"] = "Because.",
            },
        };

        var read = SubmissionOverrideService.ReadStamp(old, "override");

        read.Should().NotBeNull();
        read!.RecordId.Should().Be(recordId);
        read.BySystemUserId.Should().Be(decider);
        read.Reason.Should().Be("Because.");
        read.CompletedBySystemUserId.Should().BeNull();
        InvoiceBlockedHold.IsValidStamp(read).Should().BeTrue("an old stamp is still a valid release stamp");

        foreach (var bad in new[] { "not-a-guid", "", Guid.Empty.ToString("D") })
        {
            old["override"]![CompletedMember] = bad;
            SubmissionOverrideService.ReadStamp(old, "override")!.CompletedBySystemUserId.Should().BeNull($"'{bad}' is not a recorded completer");
        }

        var completer = Guid.NewGuid();
        old["override"]![CompletedMember] = completer.ToString("D");
        SubmissionOverrideService.ReadStamp(old, "override")!.CompletedBySystemUserId.Should().Be(completer);
    }

    [Fact(DisplayName = "iss034c AC3: WithStamp writes the member only for a stamp that names a completer, and keeps every other member")]
    public void WithStamp_WritesTheMemberOnlyWhenTheStampNamesACompleter()
    {
        var stamp = new BlockedDecisionStamp(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.Parse("2026-11-03T10:00:00Z"), "Because.");

        var without = JsonNode.Parse(SubmissionOverrideService.WithStamp("{\"findings\":[]}", "return", stamp))!["return"]!.AsObject();
        var completer = Guid.NewGuid();
        var withIt = JsonNode.Parse(SubmissionOverrideService.WithStamp("{\"findings\":[]}", "return", stamp with { CompletedBySystemUserId = completer }))!.AsObject();

        without.ContainsKey(CompletedMember).Should().BeFalse("no member is written for a stamp that names no completer");
        withIt["findings"].Should().NotBeNull();
        withIt["return"]![CompletedMember]!.GetValue<string>().Should().Be(completer.ToString("D"));
    }

    [Theory(DisplayName = "iss034c AC3: the GET view carries the completer on the stamp, and the wire name is completedBySystemUserId")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task Get_CarriesTheCompleter(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await StrandAsync(w, strand);
        ActAsAdmin(w, World.Operator);
        (await Finish(w)).Kind.Should().Be(BlockedDecisionKind.Decided);

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        var stamp = (verb == "override" ? view.Override : view.Return)!;
        stamp.CompletedBySystemUserId.Should().Be(World.Operator);
        stamp.BySystemUserId.Should().Be(World.Caller);
        var wire = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(view, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
        wire[verb]![CompletedMember]!.GetValue<string>().Should().Be(World.Operator.ToString("D"));
    }

    [Fact(DisplayName = "iss034c AC3 must-not-fire: the GET view of a submission whose stamp was written before the completer existed loads with a null completer")]
    public async Task Get_OfAnOldStamp_HasNoCompleterAndDoesNotFault()
    {
        var w = await World.CreateAsync();
        (await Decide(w, "override")).Kind.Should().Be(BlockedDecisionKind.Decided);
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root["override"]!.AsObject().Remove(CompletedMember);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();

        var view = (await w.Service.GetAsync(World.SubmissionId, World.Token, CancellationToken.None))!;

        view.Override.Should().NotBeNull();
        view.Override!.CompletedBySystemUserId.Should().BeNull();
        view.Override.BySystemUserId.Should().Be(World.Caller);
    }

    [Theory(DisplayName = "iss034c AC4: the findings_too_large pre-check counts the completer: a stamp that fits only without it is refused before any write, and one that fits with it is written")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThePreCheck_CountsTheCompleter(bool fitsWithIt)
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        var decidedId = Records(w).Single().Id;
        var plain = new BlockedDecisionStamp(decidedId, World.Caller, w.Clock.GetUtcNow(), Reason);
        var withoutLength = SubmissionOverrideService.WithStamp(w.FindingsJson, SubmissionOverrideService.OverrideMember, plain).Length;
        var withLength = SubmissionOverrideService.WithStamp(w.FindingsJson, SubmissionOverrideService.OverrideMember, plain with { CompletedBySystemUserId = World.Operator }).Length;
        withLength.Should().BeGreaterThan(withoutLength, "the member adds length");
        // Pad so the stamped findings are exactly the column's limit with the completer (fits) or exactly the limit without it (too long with it).
        var target = fitsWithIt ? withLength : withoutLength;
        var padding = InvoiceSubmissionPersister.FindingsJsonMaxLength - target - PaddingOverhead;
        var root = JsonNode.Parse(w.FindingsJson)!.AsObject();
        root["padding"] = new string('p', padding);
        w.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = root.ToJsonString();
        ActAsAdmin(w, World.Operator);
        var before = Snapshot.Of(w);

        var result = await Finish(w);

        if (fitsWithIt)
        {
            result.Kind.Should().Be(BlockedDecisionKind.Decided, "the stamp with its completer fits exactly");
            CompletedBy(StampOf(w, "override")).Should().Be(World.Operator);
            w.FindingsJson.Length.Should().BeLessThanOrEqualTo(InvoiceSubmissionPersister.FindingsJsonMaxLength);
        }
        else
        {
            result.StatusCode.Should().Be(409);
            result.ErrorCode.Should().Be(BlockedSubmissionErrors.FindingsTooLarge);
            Snapshot.Of(w).Should().Be(before, "refused before any write: no operator record, no close, no release");
        }
    }

    // ── The admin marker ──

    [Fact(DisplayName = "iss034a: the admin marker is BillingAdminAuthorization.Admin()'s checks (no new privilege)")]
    public void TheAdminMarker_IsTheBillingAdminCheck()
    {
        SubmissionOverrideService.AdminMarkerChecks().Should().BeEquivalentTo(BillingAdminAuthorization.Admin().Checks, o => o.WithStrictOrdering());
        SubmissionOverrideService.AdminMarkerChecks().Should().OnlyContain(c => c.Path == FinanceCheckPath.Privilege);
        AdminMarker().Should().Contain(BillingAdminAuthorization.WriteRuleSetPrivilege);
        BlockedSubmissionErrors.OperatorNotPermitted.Should().Be("ebilling.blocked.operator_not_permitted", "the wire code is a contract");
    }

    [Fact(DisplayName = "iss034a review round 1: the operator gate asks exactly every check of Admin(), as the caller, before anything else")]
    public async Task TheOperatorGate_AsksExactlyTheAdminChecks()
    {
        var w = await World.CreateAsync();
        await StrandAsync(w, "override-release");
        ActAsAdmin(w, World.Operator);
        w.Probe.Calls.Clear();

        // A missing reason is refused right after the gate, before the Override privilege is asked: the calls are the gate's alone.
        (await Finish(w, reason: " ")).StatusCode.Should().Be(400);

        w.Probe.Calls.Select(c => c.Privilege).Should().Equal(BillingAdminAuthorization.Admin().Checks.Select(c => c.Operation));
        w.Probe.Calls.Should().OnlyContain(c => c.HasToken, "asked as the caller");
    }
}
