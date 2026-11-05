using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 095d-1c (notes 095c 16.4, 16.7, 16.8 "Seams for 1c", 16.9): Set firm's Decision Record, after the apply (owner option 1,
/// apply then record), through the existing billing path (<see cref="BillingDecisionRecorder.RecordAsync"/>, faked here at the service's seam);
/// the Incomplete outcome when the record fails or gets no answer; and the repeat that finishes it from the stored decision alone.
/// </summary>
/// <remarks>
/// <para><b>Seed proofs</b> (2026-11-04, each restored byte-identical, <c>cmp</c>): (1) the record failure mapped to <c>Assigned</c> instead of
/// Incomplete makes <c>RecordFails_AfterTheApply_IsIncomplete_Never200</c> fail; (2) the repeat's never-apply builder replaced by one that
/// builds an apply makes <c>Repeat_ThatReachesTheApplyBuilder_NeverAppliesAndRecordsNothing</c> fail; (3) the core-record rights check removed
/// from the repeat path makes <c>Repeat_ReasksTheRights_AsThisCaller_BeforeRecording</c> fail.</para>
/// </remarks>
public sealed partial class InvoiceAssignFirmServiceTests
{
    private const string IncompleteSentence = "The firm was set, but its decision was not recorded. Repeat the request to finish it.";

    [Fact(DisplayName = "095d-1c: after the apply commits, ONE Decision Record is written from the decision the commit stored - assign-firm on the invoice, no Signal resolved, the Signal set, old and new number and bounded counts in the facts")]
    public async Task Assigned_RecordsTheStoredDecision_AfterTheApply()
    {
        var w = new World();
        var signal = w.AddFirmMissingSignal();
        List<string>? callsAtApply = null;
        w.OnRenumber = build =>
        {
            callsAtApply = [.. w.Calls];
            w.Applied = build(new RenumberCandidate("INV-7.1", 1));
            return RenumberResult.Renumbered("INV-7.1", 1);
        };

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.Assigned);
        result.Status.Should().Be(StatusCodes.Status200OK);
        result.DecisionRecordId.Should().Be(World.RecordId);
        callsAtApply.Should().NotContain("record", "apply, then record (owner option 1)");
        w.Calls.Last().Should().Be("record", "the record is the last step");

        var recorded = w.Recorded.Should().ContainSingle("one record per apply").Subject;
        var stored = AssignFirmStoredDecision.Read(FindingsOf(w.Applied!))!;
        recorded.Should().BeEquivalentTo(InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, stored),
            "the record is built from exactly the decision the committed attempt stored");
        recorded.DecisionId.Should().Be(InvoiceAssignFirmService.DecisionIdFor(SubmissionId));
        recorded.ActionCode.Should().Be("assign-firm");
        recorded.SubjectId.Should().Be(InvoiceId);
        recorded.Decider.SystemUserId.Should().Be(Decider);
        recorded.ResolvedSignalIds.Should().BeNull("16.7: the shared firm_missing Signal is not closed in 095d-1");
        recorded.Values!["firm"].Should().Be(Firm.ToString("D"));

        var facts = recorded.Facts;
        facts["previousTrackingNumber"].Should().Be("INV-7.3");
        facts["trackingNumber"].Should().Be("INV-7.1");
        facts["firmMissingCleared"].Should().Be(true);
        var set = (Dictionary<string, object?>)facts["firmMissingSignals"]!;
        ((IEnumerable<Dictionary<string, object?>>)set["signals"]!).Select(s => s["id"]).Should().Equal(signal.ToString("D"));
        ((Dictionary<string, object?>)facts["renamedCounts"]!).Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["firmSetLines"] = 2, ["lines"] = 2, ["approval"] = 1, ["storedFiles"] = 2, ["findings"] = 1,
        }, "bounded counts, never a row per rename (1b review F1)");
        facts.Should().NotContainKey("renamed", "the full mapping stays in the stored member");
        w.Log.Entries.Should().NotContain(e => e.State.ContainsKey("CallerUserId"), "only a repeat's completion log names the repeating caller");
    }

    [Theory(DisplayName = "095d-1c (seed proof 1): the record refused, faulting or unanswered after the apply is 503 incomplete at failedStep record - never 200, no id and no exception text (must fire)")]
    [InlineData("refused")]
    [InlineData("no-answer")]
    [InlineData("fault")]
    [InlineData("timeout-cancel")]
    public async Task RecordFails_AfterTheApply_IsIncomplete_Never200(string kind)
    {
        var w = new World
        {
            OnRecord = _ => kind switch
            {
                "refused" => throw new DecisionRecordRefusedException(DecisionRecordRefusalReason.BillingRecordLookupFailed, "secret writer text"),
                "no-answer" => throw new TimeoutException("secret socket text"),
                // 095d-1c fix round 1 (seed proof 5): an HTTP-client timeout is a TaskCanceledException while the REQUEST token is not cancelled.
                "timeout-cancel" => throw new TaskCanceledException("secret timeout text"),
                _ => throw new InvalidOperationException("secret fault text"),
            },
        };

        var result = await w.Assign();

        result.Outcome.Should().Be(AssignFirmOutcome.Incomplete);
        (result.Status, result.Code, result.FailedStep).Should().Be((503, AssignFirmCodes.Incomplete, "record"));
        result.Detail.Should().Be(IncompleteSentence, "one fixed sentence");
        result.Detail.Should().NotContain("secret").And.NotContain(InvoiceId.ToString()).And.NotContain(SubmissionId.ToString());
        w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.State.ContainsKey("Step"))
            .Which.State.Should().NotContainKey("CallerUserId", "095d-1c uac-r2 K2: only a repeat names its caller; the first apply's own incomplete does not");
        (result.DecisionId, result.DecisionRecordId, result.TrackingNumber, result.PreviousTrackingNumber).Should().Be((null, null, null, null));
        w.Applied.Should().NotBeNull("the apply committed before the record was asked");
        w.Recorded.Should().ContainSingle();
        w.Locks.Released.Should().BeEquivalentTo(w.Locks.Held, "both locks are released");
    }

    [Fact(DisplayName = "095d-1c: with the REAL allocator, a record that got no answer is incomplete; the repeat finishes the SAME record from the stored decision and the apply is sent once")]
    public async Task Incomplete_ThenRepeat_FinishesTheRecord_WithoutApplyingAgain()
    {
        var w = new World();
        w.AddFirmMissingSignal();
        w.OnRecord = _ => throw new TimeoutException("no answer");

        var first = await w.Assign(allocator: RealAllocator(w));

        (first.Outcome, first.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, "record"));
        CommitInto(w, w.Transactions.Single());
        w.OnRecord = null;
        w.Calls.Clear();

        var again = await w.Assign(allocator: RealAllocator(w));

        again.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        (again.Status, again.TrackingNumber, again.PreviousTrackingNumber, again.DecisionRecordId).Should().Be((200, "INV-7.1", "INV-7.3", World.RecordId));
        w.Transactions.Should().HaveCount(1, "a repeat never applies twice");
        w.Recorded.Should().HaveCount(2);
        w.Recorded[1].Should().BeEquivalentTo(w.Recorded[0], "the repeat rebuilds the same decision (same id, same content) from the stored member alone");
        w.Calls.Should().Equal(["rights", "filing", "record"], "the rights as this caller, then the filing, then the record; no firm read");
    }

    [Fact(DisplayName = "095d-1c: a repeat whose record already exists is 200 alreadyAssigned with that record (the writer's exactly-once answer)")]
    public async Task Repeat_RecordAlreadyWritten_IsAlreadyAssigned()
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        w.OnRecord = _ => new BillingDecisionRecordResult(World.RecordId, AlreadyRecorded: true, RecordedDecisionClass.Judgement);

        var result = await w.Assign();

        (result.Outcome, result.Status, result.DecisionRecordId).Should().Be((AssignFirmOutcome.AlreadyAssigned, 200, World.RecordId));
        w.Transactions.Should().BeEmpty();
    }

    [Theory(DisplayName = "095d-1c (seed proof 3): a repeat that records asks the core-record rights AGAIN as this caller first - refused, nothing recorded (must fire); granted, rights then filing then record (must not fire)")]
    [InlineData((int)DecisionCallerRefusal.CoreAppendToMissing, 403, AssignFirmCodes.NotAuthorized)]
    [InlineData((int)DecisionCallerRefusal.Unavailable, 503, AssignFirmCodes.Unavailable)]
    [InlineData((int)DecisionCallerRefusal.None, 200, "ok")]
    public async Task Repeat_ReasksTheRights_AsThisCaller_BeforeRecording(int refusal, int status, string code)
    {
        var w = Applied(Firm);
        w.CloseRights = (DecisionCallerRefusal)refusal;
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);

        // A different caller, with a note: the stored member (decider Decider, no note) is what is recorded, not the request.
        var result = await w.Assign(deciderUserId: OtherCaller);

        (result.Status, result.Code).Should().Be((status, code));
        if (status == 200)
        {
            w.Calls.Should().Equal(["rights", "filing", "record"]);
            var recorded = w.Recorded.Should().ContainSingle().Subject;
            recorded.Decider.SystemUserId.Should().Be(Decider, "the stored member's decider, not the caller who repeated").And.NotBe(OtherCaller);
            recorded.Values.Should().NotContainKey("note", "the stored member holds no note; the repeat's request note is not recorded");
            recorded.Should().BeEquivalentTo(InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, AssignFirmStoredDecision.Read(
                JsonNode.Parse(w.Submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)!)!.AsObject())!));
            return;
        }

        w.Recorded.Should().BeEmpty("a caller without the rights records nothing");
        w.Calls.Should().Equal(["rights"], "refused before the repeat is judged, the filing and the record");
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1c: on a repeat, a filing that cannot be confirmed is 503 incomplete at failedStep filing and records nothing (must fire)")]
    [InlineData("transient")]
    [InlineData("another core record")]
    public async Task Repeat_FilingNotConfirmed_IsIncomplete_AndRecordsNothing(string kind)
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        w.Filing = kind == "transient"
            ? new CoreAncestorResult(CoreAncestorStatus.Error, [], "fault") { IsPermanent = false }
            : new CoreAncestorResult(CoreAncestorStatus.Derived, [new CoreAncestorStamp("sprk_matter", "sprk_matter", Guid.NewGuid())], null);

        var result = await w.Assign();

        (result.Outcome, result.Status, result.Code, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, 503, AssignFirmCodes.Incomplete, "filing"));
        result.Detail.Should().Be(IncompleteSentence);
        w.Recorded.Should().BeEmpty();
        w.Transactions.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1c (seed proof 2): a repeat whose allocator reaches for the apply builder never applies and records nothing (must fire)")]
    public async Task Repeat_ThatReachesTheApplyBuilder_NeverAppliesAndRecordsNothing()
    {
        var w = Applied(Firm);
        w.OnRenumber = build =>
        {
            // A misbehaving allocator that would apply: whatever the builder hands back is sent.
            var apply = build(new RenumberCandidate("INV-7.2", 2));
            w.Transactions.Add(apply.Requests);
            return RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        };

        var result = await w.Assign();

        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.Changed));
        w.Transactions.Should().BeEmpty("the repeat's builder throws: nothing to send");
        w.Recorded.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1c: a conflict lost to a committed Set firm of this firm records it, without asking the filing twice (it was asked before this request's apply)")]
    public async Task Conflict_LostToTheSameFirm_RecordsWithoutASecondFiling()
    {
        var w = new World();
        var calls = 0;
        w.OnRenumber = build =>
        {
            if (calls++ == 0)
            {
                var apply = build(new RenumberCandidate("INV-7.1", 1));
                Commit(w, Firm, "INV-7.1", 1, FindingsOf(apply).ToJsonString());
                return RenumberResult.Conflict();
            }

            return RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        };

        var result = await w.Assign();

        (result.Outcome, result.DecisionRecordId).Should().Be((AssignFirmOutcome.AlreadyAssigned, World.RecordId));
        w.Calls.Count(c => c == "filing").Should().Be(1);
        w.Calls.Last().Should().Be("record");
    }

    [Theory(DisplayName = "095d-1c: a repeat naming another firm, or one whose number disagrees, records nothing (must not fire)")]
    [InlineData("other firm")]
    [InlineData("other number")]
    public async Task Repeat_NotThisFirmsDecision_RecordsNothing(string kind)
    {
        var w = Applied(kind == "other firm" ? OtherFirm : Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.2", 2);

        var result = await w.Assign();

        result.Status.Should().Be(409);
        w.Recorded.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1c fix round 1 (seed proof 4): a repeat by ANOTHER caller records the stored member alone - the first caller's decider and note, not the repeater's (must fire)")]
    public async Task Repeat_ByAnotherCaller_RecordsTheStoredMemberAlone()
    {
        var w = new World { OnRecord = _ => throw new TimeoutException("no answer") };

        var first = await w.Assign(note: "N1 first note", deciderUserId: Decider);

        (first.Outcome, first.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, "record"));
        Commit(w, Firm, "INV-7.1", 1, FindingsOf(w.Applied!).ToJsonString());
        var storedByTheApply = AssignFirmStoredDecision.Read(FindingsOf(w.Applied!))!;
        w.OnRecord = null;
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        w.Recorded.Clear();

        var again = await w.Assign(note: "N2 second note", deciderUserId: OtherCaller);

        again.Outcome.Should().Be(AssignFirmOutcome.AlreadyAssigned);
        var recorded = w.Recorded.Should().ContainSingle("exactly one record is written by the repeat").Subject;
        recorded.Should().BeEquivalentTo(InvoiceAssignFirmService.BuildDecision(InvoiceId, SubmissionId, storedByTheApply),
            "built from the stored member alone");
        recorded.Decider.SystemUserId.Should().Be(Decider).And.NotBe(OtherCaller);
        recorded.Values!["note"].Should().Be("N1 first note");
        recorded.Values.Values.Should().NotContain(v => (v as string) == "N2 second note");
        w.Transactions.Should().HaveCount(0, "a repeat never applies");
        w.Log.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.State.ContainsKey("CallerUserId") && (Guid)e.State["CallerUserId"]! == OtherCaller,
            "095d-1c review K2: the repeat's completion log names the caller who asked");
    }

    [Fact(DisplayName = "095d-1c fix round 1 (seed proof 5): the caller's token cancelled while the record is in flight propagates - neither incomplete nor 200 (must not be swallowed)")]
    public async Task RecordCancelled_ByTheCallersToken_Propagates()
    {
        using var cts = new CancellationTokenSource();
        var w = new World
        {
            OnRecord = _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };

        var act = () => w.Assign(ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        w.Applied.Should().NotBeNull("the apply committed before the record was asked");
        w.Locks.Released.Should().BeEquivalentTo(w.Locks.Held, "both locks are released even when the request is cancelled");
    }

    [Fact(DisplayName = "095d-1c fix round 1 (K3 hardening, must fire): a stored decision whose id is not derived from its submission is 409 changed on a repeat - nothing recorded, the allocator not asked")]
    public async Task Repeat_StoredDecisionIdNotDerivedFromTheSubmission_IsChanged_AndRecordsNothing()
    {
        var w = new World();
        var stored = new AssignFirmStoredDecision(
            Guid.NewGuid(), Firm, null, "INV-7.3", 3, "INV-7.1", 1, Decider, Now,
            new InvoiceSubmissionAfterSteps.ResolvedSignalSet(Now.UtcDateTime, []), [], 0, "pending");
        Commit(w, Firm, "INV-7.1", 1, new JsonObject { ["trackingNumber"] = "INV-7.1", [InvoiceAssignFirmService.StoredDecisionMember] = stored.ToJson() }.ToJsonString());

        var result = await w.Assign();

        (result.Status, result.Code).Should().Be((409, AssignFirmCodes.Changed));
        w.Recorded.Should().BeEmpty();
        w.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1c fix round 1 (K1): a repeat whose filing is another core record is logged at Error with Permanent=true and the filing code; a transient fault carries no such marker")]
    [InlineData("transient", false)]
    [InlineData("another core record", true)]
    public async Task Repeat_FilingMismatch_PermanentIsMarkedInTheLog(string kind, bool permanent)
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        w.Filing = kind == "transient"
            ? new CoreAncestorResult(CoreAncestorStatus.Error, [], "fault") { IsPermanent = false }
            : new CoreAncestorResult(CoreAncestorStatus.Derived, [new CoreAncestorStamp("sprk_matter", "sprk_matter", Guid.NewGuid())], null);

        var result = await w.Assign();

        (result.Outcome, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, "filing"));
        var marked = w.Log.Entries.Where(e => e.Level == LogLevel.Error && e.State.ContainsKey("Permanent")).ToList();
        if (permanent)
        {
            var entry = marked.Should().ContainSingle().Subject;
            entry.State["Permanent"].Should().Be(true);
            entry.State["FilingCode"].Should().Be(AssignFirmCodes.CoreUnresolved);
        }
        else
        {
            marked.Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "095d-1c uac-r2 K2: a repeat's incomplete log carries the repeating caller as {CallerUserId}; the first apply's incomplete carries none; the response is unchanged")]
    [InlineData("filing")]
    [InlineData("record")]
    public async Task Repeat_Incomplete_LogsTheRepeatingCaller(string step)
    {
        var w = Applied(Firm);
        w.OnRenumber = _ => RenumberResult.AlreadyRenumbered("INV-7.1", 1);
        if (step == "filing")
            w.Filing = new CoreAncestorResult(CoreAncestorStatus.Error, [], "fault") { IsPermanent = false };
        else
            w.OnRecord = _ => throw new InvalidOperationException("the writer is down");

        var result = await w.Assign(deciderUserId: OtherCaller);

        (result.Outcome, result.Status, result.FailedStep).Should().Be((AssignFirmOutcome.Incomplete, 503, step));
        result.Detail.Should().Be(IncompleteSentence, "the response is unchanged");
        var entry = w.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.State.ContainsKey("Step")).Subject;
        entry.State["CallerUserId"].Should().Be(OtherCaller, "the repeating caller's attempt is auditable");
        entry.Message.Should().NotContain("the writer is down", "no exception text in the log either");
    }
}
