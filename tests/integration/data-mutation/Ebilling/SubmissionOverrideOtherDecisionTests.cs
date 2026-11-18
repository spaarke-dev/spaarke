using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using ScriptedProbe = Sprk.Bff.Api.Tests.Services.Finance.SubmissionOverrideServiceTests.ScriptedProbe;
using World = Sprk.Bff.Api.Tests.Services.Finance.SubmissionOverrideServiceTests.World;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 ISS-031 (#2882): one blocked submission ends with ONE decision. An Override that recorded its Decision Record but did not finish
/// (its release failed) leaves the submission held and its Signals closed, and a Return used to go ahead on top of it (the reverse too: a Return
/// stranded before its transition, then an Override). Over the real <see cref="SubmissionOverrideService"/> and the real Decision Record writer
/// harness of <see cref="World"/>, each test proves the stored state: the records, the release
/// transaction, the lifecycle transition, the submission and the Signals.
/// </summary>
public sealed class SubmissionOverrideOtherDecisionTests
{
    private const string Reason = "Negotiated rate is not yet in the rate card.";

    private static Task<BlockedDecisionResult> Decide(World w, string verb, string? reason = Reason) =>
        verb == "override"
            ? w.Service.OverrideAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None)
            : w.Service.ReturnAsync(World.SubmissionId, reason, null, World.Token, CancellationToken.None);

    private static string VerbOf(string strand) => strand.StartsWith("return", StringComparison.Ordinal) ? "return" : "override";

    private static string OtherOf(string verb) => verb == "override" ? "return" : "override";

    private static Guid DecisionIdOf(string verb) =>
        verb == "override" ? SubmissionOverrideService.OverrideDecisionIdFor(World.SubmissionId) : SubmissionOverrideService.ReturnDecisionIdFor(World.SubmissionId);

    /// <summary>Everything a decision can write, as it stands now: compared before and after a refused call.</summary>
    private sealed record Snapshot(
        int RecordCreates, int Recorded, int Updates, int Transactions, int Transitions, int SetStores, int Closes, int Notified,
        string Findings, string? Code, InvoiceLifecycleStatus InvoiceStatus, string? DecisionNote, string Signals)
    {
        public static Snapshot Of(World w) => new(
            w.RecordCreates, w.Recorded.Count, w.Updates.Count, w.Transactions.Count, w.Transitions.Count, w.SetStores.Count, w.Closes.Count,
            w.Notified.Count, w.FindingsJson, w.Code, w.InvoiceStatus,
            w.Invoice!.Contains(InvoiceCorrectionNote.DecisionColumn) ? w.Invoice[InvoiceCorrectionNote.DecisionColumn] as string : null,
            string.Join(",", w.Signals.Select(s => $"{s.Id}:{s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value}")));
    }

    // ── Must fire: the other verb's record exists, so this verb is refused and writes nothing ──

    [Theory(DisplayName = "ISS-031: a decision stranded after its record (Override release, closes and release, invoice bound; Return transition) refuses the OTHER verb 409 other_decision_pending, naming the pending decision; nothing is written; the pending decision's repeat then finishes, and the other verb is refused by state")]
    [InlineData("override-release")]
    [InlineData("override-closes-and-release")]
    [InlineData("override-invoice-bound")]
    [InlineData("return-transition")]
    public async Task StrandedDecision_RefusesTheOtherVerb_AndWritesNothing(string strand)
    {
        var w = await World.CreateAsync();
        if (strand == "return-transition")
        {
            w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE"); // closed by the stranded Return, as the issue describes
        }

        var pending = VerbOf(strand);
        var other = OtherOf(pending);
        (await SubmissionOverrideDecisionNoteTests.StrandAsync(w, strand, "First note")).Kind.Should().Be(BlockedDecisionKind.Refused, "the first call stranded");
        w.RecordCreates.Should().Be(1, "the record was written before the strand");
        w.Code.Should().Be(SubmissionReasonCodes.Blocked, "the submission is still held");
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.InReview, "and its invoice In Review");
        var before = Snapshot.Of(w);

        var refused = await Decide(w, other, "The other reviewer's reason.");

        refused.Kind.Should().Be(BlockedDecisionKind.Refused);
        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending);
        refused.ErrorCode.Should().Be("ebilling.blocked.other_decision_pending", "the wire code is a contract");
        refused.Detail.Should().Be(BlockedSubmissionErrors.OtherDecisionPendingDetail(pendingIsOverride: pending == "override", callerIsDecider: true));
        refused.Detail.Should().Contain(pending == "override" ? "Repeat the override" : "Repeat the return",
            "the caller is the pending decision's own decider, so the detail tells them to repeat it");
        refused.InvoiceId.Should().Be(World.InvoiceId);
        Snapshot.Of(w).Should().Be(before, "no record, no store, no close, no release, no transition, no submission write, no notice");

        // The pending decision's own repeat still finishes from its stored record (095f-2c), with no second record.
        (await Decide(w, pending)).Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        w.Code.Should().Be(pending == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);

        // And the other verb is now refused by the finished state, as before ISS-031.
        var after = await Decide(w, other);
        after.StatusCode.Should().Be(409);
        after.ErrorCode.Should().Be(pending == "override" ? BlockedSubmissionErrors.AlreadyOverridden : BlockedSubmissionErrors.AlreadyReturned);
        w.RecordCreates.Should().Be(1, "one decision, one record");
    }

    [Theory(DisplayName = "ISS-031 review round 1 F1: another person (not the pending decision's decider) is refused 409 other_decision_pending with a detail that says the person who recorded it completes it, never \"repeat it\"; nothing is written")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task StrandedDecision_AnotherPerson_IsToldItIsTheirsToComplete(string strand)
    {
        var w = await World.CreateAsync();
        var pending = VerbOf(strand);
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, strand, "First note");
        var before = Snapshot.Of(w);
        w.Probe.CallerSystemUserId = Guid.Parse("10500000-0000-4000-8000-0000000000c9");

        var refused = await Decide(w, OtherOf(pending), "Another reviewer's reason.");

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending, "the same code for every caller");
        refused.Detail.Should().Be(BlockedSubmissionErrors.OtherDecisionPendingDetail(pendingIsOverride: pending == "override", callerIsDecider: false));
        refused.Detail.Should().StartWith("Another person's " + (pending == "override" ? "override" : "return"));
        refused.Detail.Should().Contain("They complete it by repeating it.");
        refused.Detail.Should().NotContain("Repeat the", "this caller cannot finish another person's decision (095f-2c decider rule)");
        Snapshot.Of(w).Should().Be(before);
    }

    [Fact(DisplayName = "ISS-031 review round 1: with BOTH records stored (only reachable before ISS-031), an Override and a Return each answer 409 other_decision_pending and write nothing")]
    public async Task BothRecordsStored_BothVerbsAreRefused_AndWriteNothing()
    {
        var w = await World.CreateAsync();
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, "override-release", null);
        var stored = InvoiceSubmissionAfterSteps.StoredFindings.Parse(w.FindingsJson)!;
        var returnDecision = SubmissionOverrideService.BuildDecision(
            SubmissionOverrideService.Verb.Return, stored, World.SubmissionId, World.InvoiceId, World.Caller, Reason,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(w.Clock.GetUtcNow().UtcDateTime, []));
        (await w.Recorder.RecordAsync(returnDecision, CancellationToken.None)).AlreadyRecorded.Should().BeFalse("the Return record is seeded");
        w.RecordCreates.Should().Be(2, "an Override record and a Return record");
        var before = Snapshot.Of(w);

        foreach (var verb in new[] { "override", "return", "override" })
        {
            var refused = await Decide(w, verb);
            refused.StatusCode.Should().Be(409, verb);
            refused.ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending, verb);
            Snapshot.Of(w).Should().Be(before, "{0} wrote nothing", verb);
        }
    }

    [Fact(DisplayName = "ISS-031: a Return stranded after its transition (the invoice already Returned to Firm) refuses an Override by the invoice's state, already_returned, before the other-verb lookup; nothing is written")]
    public async Task ReturnStrandedAfterItsTransition_RefusesAnOverride_ByState()
    {
        var w = await World.CreateAsync();
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, "return-state-write", null);
        w.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.ReturnedToFirm);
        var before = Snapshot.Of(w);

        var refused = await Decide(w, "override");

        refused.StatusCode.Should().Be(409);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.AlreadyReturned);
        Snapshot.Of(w).Should().Be(before);
    }

    // ── Must not fire ──

    [Theory(DisplayName = "ISS-031 must-not-fire: with no record of either verb, an Override and a Return each decide as before (one record, the state changed)")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task NoRecordOfEitherVerb_DecidesAsBefore(string verb)
    {
        var w = await World.CreateAsync();

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
    }

    [Theory(DisplayName = "ISS-031 must-not-fire: an attempt of the other verb that failed BEFORE its record (the record write faulted) left no record, so this verb decides")]
    [InlineData("override")]
    [InlineData("return")]
    public async Task OtherVerbFailedBeforeItsRecord_DoesNotBlock(string verb)
    {
        var w = await World.CreateAsync();
        w.RecordFault = new TimeoutException("dataverse");
        (await Decide(w, OtherOf(verb))).StatusCode.Should().Be(503);
        w.RecordFault = null;
        w.RecordCreates.Should().Be(0, "nothing recorded for the other verb");

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
    }

    [Theory(DisplayName = "ISS-031 must-not-fire: the SAME verb's repeat after a strand finishes from its stored record, with one record")]
    [InlineData("override-release")]
    [InlineData("override-closes-and-release")]
    [InlineData("return-transition")]
    [InlineData("return-state-write")]
    public async Task SameVerbRepeat_FinishesAsBefore(string strand)
    {
        var w = await World.CreateAsync();
        var verb = VerbOf(strand);
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, strand, "First note");

        var repeat = await Decide(w, verb);

        repeat.Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
        w.Recorded.Should().ContainSingle("the repeat never asks the writer again");
        w.Code.Should().Be(verb == "override" ? SubmissionReasonCodes.Overridden : SubmissionReasonCodes.Returned);
    }

    // ── A fault on the other verb's lookup: 503, nothing written ──

    [Theory(DisplayName = "ISS-031: a fault on the OTHER verb's record lookup (a raw fault, or the writer's lookup-failed refusal) answers 503 and writes nothing; with the lookup healthy the decision goes ahead")]
    [InlineData("override", "timeout")]
    [InlineData("override", "lookup-refused")]
    [InlineData("return", "timeout")]
    [InlineData("return", "lookup-refused")]
    public async Task AFaultOnTheOtherVerbsLookup_Is503_AndWritesNothing(string verb, string fault)
    {
        var w = await World.CreateAsync();
        w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
        var otherId = DecisionIdOf(OtherOf(verb));
        w.FindFaultFor = id => id == otherId
            ? fault == "timeout"
                ? new TimeoutException("dataverse")
                : new DecisionRecordRefusedException(DecisionRecordRefusalReason.BillingRecordLookupFailed, "the lookup faulted", innerException: new TimeoutException("dataverse"))
            : null;

        var result = await Decide(w, verb);

        result.Kind.Should().Be(BlockedDecisionKind.Refused);
        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be(fault == "timeout" ? BlockedSubmissionErrors.Unavailable : BlockedSubmissionErrors.RecordUnavailable,
            "as the same verb's lookup answers each fault");
        w.AssertNothingWritten();
        w.Signals.Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusOpen);

        w.FindFaultFor = _ => null;
        (await Decide(w, verb)).Kind.Should().Be(BlockedDecisionKind.Decided);
        w.RecordCreates.Should().Be(1);
    }

    [Theory(DisplayName = "ISS-031: a fault on the other verb's lookup after a strand is 503 too, and leaves the stranded decision as it was")]
    [InlineData("override-release")]
    [InlineData("return-transition")]
    public async Task AFaultOnTheOtherVerbsLookup_AfterAStrand_Is503_AndWritesNothing(string strand)
    {
        var w = await World.CreateAsync();
        var pending = VerbOf(strand);
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, strand, "First note");
        var before = Snapshot.Of(w);
        var pendingId = DecisionIdOf(pending);
        w.FindFaultFor = id => id == pendingId ? new TimeoutException("dataverse") : null;

        var result = await Decide(w, OtherOf(pending));

        result.StatusCode.Should().Be(503);
        result.ErrorCode.Should().Be(BlockedSubmissionErrors.Unavailable);
        Snapshot.Of(w).Should().Be(before);
    }

    // ── The rights come first ──

    [Fact(DisplayName = "ISS-031: after a stranded Override whose closes failed (its Signals still open), a Return by a caller without Write is 403 not_authorized, not the 409 that says a decision is pending; nothing written")]
    public async Task StrandedOverride_ReturnWithoutRights_Is403First()
    {
        var w = await World.CreateAsync();
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, "override-closes-and-release", "First note");
        var before = Snapshot.Of(w);
        w.Probe.Rights = (_, _) => Spaarke.Dataverse.AccessRights.Read;

        var refused = await Decide(w, "return");

        refused.StatusCode.Should().Be(403);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        Snapshot.Of(w).Should().Be(before);
    }

    [Fact(DisplayName = "ISS-031: after a stranded Return whose closes failed, an Override by a caller without Write on the Signals' subjects is 403 not_authorized; one without the Override privilege is 403 not_permitted; nothing written")]
    public async Task StrandedReturn_OverrideWithoutRights_Is403First()
    {
        var w = await World.CreateAsync();
        w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
        w.CloseFault = _ => new TimeoutException("dataverse"); // reset by StrandAsync after the first call
        await SubmissionOverrideDecisionNoteTests.StrandAsync(w, "return-transition", "First note");
        w.RecordCreates.Should().Be(1);
        w.Signals.Should().OnlyContain(s => s.GetAttributeValue<OptionSetValue>("sprk_signalstatus").Value == SignalWriter.SignalStatusOpen, "the closes failed");
        var before = Snapshot.Of(w);

        w.Probe.Rights = (_, _) => Spaarke.Dataverse.AccessRights.Read;
        var noWrite = await Decide(w, "override");
        noWrite.StatusCode.Should().Be(403);
        noWrite.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);

        w.Probe.Rights = ScriptedProbe.RealReviewerRights;
        w.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
        var noPrivilege = await Decide(w, "override");
        noPrivilege.StatusCode.Should().Be(403);
        noPrivilege.ErrorCode.Should().Be(BlockedSubmissionErrors.NotPermitted);

        Snapshot.Of(w).Should().Be(before);

        // With the rights, the same caller gets the 409.
        w.Probe.Hold(EbillingOverridePrivilege.PrivilegeName);
        (await Decide(w, "override")).ErrorCode.Should().Be(BlockedSubmissionErrors.OtherDecisionPending);
        Snapshot.Of(w).Should().Be(before);
    }

    [Fact(DisplayName = "ISS-031: a caller without the rights gets 403 even when the other verb's lookup would fault (the lookup runs after the rights)")]
    public async Task NoRights_Is403_EvenWhenTheOtherLookupWouldFault()
    {
        var w = await World.CreateAsync();
        w.AddSignal("sprk_invoice", World.InvoiceId, "EB-AGE");
        var overrideId = DecisionIdOf("override");
        w.FindFaultFor = id => id == overrideId ? new TimeoutException("dataverse") : null;
        w.Probe.Rights = (_, _) => Spaarke.Dataverse.AccessRights.Read;

        var refused = await Decide(w, "return");

        refused.StatusCode.Should().Be(403);
        refused.ErrorCode.Should().Be(BlockedSubmissionErrors.NotAuthorized);
        w.AssertNothingWritten();
    }
}
