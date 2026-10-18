using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology platform R1 task 102b, fix round 3 (owner 2026-10-17): deleting a matter deletes a rule limited to it
/// (<c>sprk_policy_matter</c> CascadeDelete=Cascade), and the rule's Signals keep their rows with <c>sprk_policy</c> nulled
/// (<c>sprk_signal_policy</c> is RemoveLink). The nightly job closes every Open or Acknowledged Signal whose policy lookup is empty and
/// whose dedupe key names no existing policy, as <c>PolicyRetired</c> (<see cref="SignalClosureService.CloseSignalsOfDeletedPoliciesAsync"/>).
/// The real job and closure service run over the in-memory seams of <see cref="SignalReevaluationJobTests"/>.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private const string DeletedPolicyCode = "POL-DELETED";

    /// <summary>An Open (or other status) Signal of a policy that was deleted: no policy lookup (RemoveLink nulled it), and its key names a
    /// code no policy row carries.</summary>
    private static Guid SeedOrphan(Harness h, Guid subject, int status = SignalWriter.SignalStatusOpen) =>
        h.Org.SeedSignal(Key(DeletedPolicyCode, "sprk_matter", subject, 1), status, coreId: subject,
            versionId: Guid.NewGuid()); // the cited version survives too (policyversion is RemoveLink as well)

    [Fact]
    public async Task Run_OpenAndAcknowledgedSignalsOfADeletedPolicy_AreClosedPolicyRetired_AndAResolvedOneIsUntouched()
    {
        // (a) must-fire.
        var h = new Harness().WithPathBPolicy();
        var open = SeedOrphan(h, MatterId);
        var acknowledged = SeedOrphan(h, OtherMatterId, SignalWriter.SignalStatusAcknowledged);
        var resolved = h.Org.SeedSignal(Key(DeletedPolicyCode, "sprk_matter", ThirdMatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: ThirdMatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[open], SignalWriter.ResolutionPolicyRetired);
        AssertSystemClosure(h.Org.Rows[acknowledged], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[resolved].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionDismissed);
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == resolved, "a decided Signal is never re-closed");
        h.Org.DecisionRecordRows.Should().BeEmpty("a PolicyRetired closure writes no Decision Record");
        result.ResultJson.Should().Contain("\"deletedPolicyRetired\":2").And.Contain("\"policyRetiredClosed\":2");
        var line = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Information
            && e.Message.Contains("of a deleted policy were closed PolicyRetired", StringComparison.Ordinal)).Subject.Message;
        line.Should().Contain("2 open Signal(s)").And.Contain(open.ToString("D")).And.Contain(acknowledged.ToString("D"))
            .And.Contain($"policy code(s) {DeletedPolicyCode}.", "fix round 4 (K a): the deleted rules' codes are logged too");
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("off")]
    [InlineData("retired")]
    [InlineData("scoped")]
    public async Task Run_ASignalWithNoPolicyLookup_WhosePolicyStillExists_IsNeverClosedByTheDeletedPolicySweep(string kind)
    {
        // (b) must-not-fire: the code check is what keeps it open. Every one of these Signals has an EMPTY policy lookup (the condition the
        // sweep reads on), and its key names a policy that exists in the given state. Nothing else in the run would close it either: the
        // enabled and scoped policies still hold for the subject, and the Off/Retired close finds Signals by the lookup.
        var h = new Harness();
        string code;
        switch (kind)
        {
            case "enabled":
                h.WithPathBPolicy().WithMatterHit(MatterId);
                code = PolicyCode;
                break;
            case "off":
                h.Sysadmin.AddPolicy(code = "POL-OFF", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
                break;
            case "retired":
                h.Sysadmin.AddPolicy(code = "POL-RETIRED", SignalWriter.LaneDecide, true, PathBBody, PathBTemplate);
                h.Sysadmin.SetPolicyState(code, enabled: true, statecode: 1);
                break;
            default:
                h.Sysadmin.AddPolicy(code = ScopedPolicyCode, SignalWriter.LaneDecide, true, AnyMatterBody, AnyMatterTemplate);
                h.Sysadmin.SetMatterScope(code, MatterId);
                QualifyingMatter(h, MatterId, "M");
                break;
        }

        var live = h.Org.SeedSignal(Key(code, "sprk_matter", MatterId, 1), SignalWriter.SignalStatusOpen, coreId: MatterId);
        var orphan = SeedOrphan(h, OtherMatterId); // the control: the sweep does run in this world

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[orphan], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[live].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen,
            $"policy {code} still exists ({kind}): its Signal is never the deleted-policy sweep's");
        h.Org.Rows[live].Contains("sprk_resolutiontype").Should().BeFalse();
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == live && u.Fields.ContainsKey("sprk_resolutiontype"));
        result.ResultJson.Should().Contain("\"deletedPolicyRetired\":1");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains("name a policy code that still exists; they were left alone", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("signals")]
    [InlineData("policies")]
    public async Task Run_WhenTheSweepCannotRead_ItClosesNothing_TheRunFailsVisibly_AndContinues(string fault)
    {
        // (c) fail closed: a read fault (the candidate Signals, or the list of existing policies that decides which are orphans) closes
        // nothing; the run is failed and logged, and the rest of the run still happens.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var orphan = SeedOrphan(h, OtherMatterId);
        h.Sysadmin.FaultOn = q => fault == "signals"
            // The close's read (Open/Acknowledged); fix round 4's redaction read of every status has its own test.
            ? q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Null)
                && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_signalstatus")
                ? new TimeoutException("Dataverse timed out.") : null
            : q.EntityName == "sprk_policy" && q.Criteria.Conditions.Count == 0
                ? new TimeoutException("Dataverse timed out.") : null;

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a sweep that could not read is a failed run, never a quiet success");
        result.ResultJson.Should().Contain("\"policyFailures\":1").And.Contain("\"deletedPolicyRetired\":0")
            .And.Contain("\"policiesEvaluated\":1", "the rest of the run still happens");
        h.Org.Rows[orphan].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == orphan);
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("Open Signals of deleted policies could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_ASignalWithNoPolicyLookup_AndAnUnparseableKey_IsLeftOpen_AndLogged()
    {
        // (d): the policy cannot be named, so it is never presumed deleted. The parseable orphan beside it is still closed.
        var h = new Harness().WithPathBPolicy();
        var unparseable = h.Org.SeedSignal($"{DeletedPolicyCode}|sprk_matter|not-a-guid|1", SignalWriter.SignalStatusOpen, coreId: MatterId);
        var orphan = SeedOrphan(h, OtherMatterId);

        var result = await h.RunAsync();

        h.Org.Rows[unparseable].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == unparseable);
        AssertSystemClosure(h.Org.Rows[orphan], SignalWriter.ResolutionPolicyRetired);
        result.ResultJson.Should().Contain("\"deletedPolicyRetired\":1");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("1 open Signal(s) with no policy lookup have a dedupe key that is not", StringComparison.Ordinal)
            && e.Message.Contains(unparseable.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_ManySignalsOfADeletedPolicy_LogTheExactCount_AndOnlyTheFirstFiftyIds()
    {
        // The K4 pattern: one bounded line however many Signals the sweep closes.
        var h = new Harness().WithPathBPolicy();
        var orphans = Enumerable.Range(0, 60).Select(_ => SeedOrphan(h, Guid.NewGuid())).ToList();

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"deletedPolicyRetired\":60");
        orphans.Should().OnlyContain(id => h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value == SignalWriter.ResolutionPolicyRetired);
        var line = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Information
            && e.Message.Contains("of a deleted policy were closed PolicyRetired", StringComparison.Ordinal)).Subject.Message;
        line.Should().Contain("60 open Signal(s)").And.Contain("and 10 more");
        orphans.Count(id => line.Contains(id.ToString("D"), StringComparison.Ordinal)).Should().Be(SignalReevaluationJob.MaxLoggedOutOfScopeIds);
    }

    // ── Fix round 4 (task 102b) ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Signal with an EMPTY policy lookup under <paramref name="code"/>, created at <paramref name="createdOn"/>.</summary>
    private static Guid SeedPolicyless(Harness h, string code, Guid subject, DateTime? createdOn, int status = SignalWriter.SignalStatusOpen)
    {
        var id = h.Org.SeedSignal(Key(code, "sprk_matter", subject, 1), status, coreId: subject);
        if (createdOn is { } on) h.Org.Rows[id]["createdon"] = on;
        return id;
    }

    [Fact]
    public async Task Run_R4_AResolvedSignalOfADeletedRule_WhoseWitnessMovedIntoASecureRecord_HasItsStoredEvidenceRedacted()
    {
        // Item 1 (D-123), must-fire. The shape of Run_T145F1_ASignalOfAPolicySwitchedOffLater_IsStillRedacted…: the Signal was created
        // while its witness was ordinary, it was then decided (Resolved), the rule was deleted (RemoveLink empties the Signal's lookup), and
        // the witness was re-filed under secure X. The deleted rule's code is neither in force nor Off/Retired, so only the deleted-policy
        // redaction reaches this Signal.
        var h = new Harness().WithPathBPolicy().WithMatterHit(MatterId);
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001c4");
        var signal = await SignalCreatedWithAnOrdinaryWitness(h, secureProject);
        signal["sprk_signalstatus"] = new OptionSetValue(SignalWriter.SignalStatusResolved);
        signal["sprk_resolutiontype"] = new OptionSetValue(SignalWriter.ResolutionDismissed);
        signal["sprk_resolvedon"] = RunAt.UtcDateTime.AddDays(-1);
        h.Sysadmin.DeletePolicy(PolicyCode);
        signal.Contains("sprk_policy").Should().BeFalse("the precondition: RemoveLink emptied the lookup");
        RefileTheWitnessUnder(h, secureProject);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"policiesEvaluated\":0").And.Contain("\"deletedPolicyRetired\":0").And.Contain("\"evidenceRedacted\":1");
        var row = h.Org.Rows[signal.Id];
        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionDismissed, "a decided Signal stays decided");
        row.GetAttributeValue<string>("sprk_evidencerefs").Should().NotContain("Rate increase").And.NotContain("partner@firm.example")
            .And.Contain($"sprk_communication:{Lower(CommunicationId)}");
    }

    [Fact]
    public async Task DeletedPolicyCodes_R4_ACodeThatStillExists_IsNotADeletedCode_WhateverTheSignalsStatus()
    {
        // Item 1, must-not-fire (the list itself): an Off policy's Signal with an empty lookup, created after the policy, is that policy's
        // (a hand edit), so its code is never swept "as deleted". The deleted code beside it is listed, Resolved Signals included.
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-OFF", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyCreatedOn("POL-OFF", RunAt.UtcDateTime.AddDays(-30));
        SeedPolicyless(h, "POL-OFF", MatterId, RunAt.UtcDateTime.AddDays(-2), SignalWriter.SignalStatusResolved);
        SeedPolicyless(h, "POL-OFF", OtherMatterId, null, SignalWriter.SignalStatusResolved);
        SeedPolicyless(h, DeletedPolicyCode, ThirdMatterId, RunAt.UtcDateTime.AddDays(-2), SignalWriter.SignalStatusResolved);

        var existing = await h.Closure.ExistingPolicyCodesAsync(default);
        var deleted = await h.Closure.DeletedPolicyCodesWithSignalsAsync(existing, default);

        deleted.Codes.Should().Equal(DeletedPolicyCode);
        deleted.MoreRemain.Should().BeFalse();
    }

    [Fact]
    public async Task Run_R4_ACodeThatStillExists_IsSweptOnce_AndNeverAsDeleted()
    {
        // Item 1, must-not-fire (the run): the Off policy's Signals are read by prefix exactly once (the not-in-force sweep), and a fault
        // there names that sweep, not "was deleted".
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-OFF", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        SeedPolicyless(h, "POL-OFF", MatterId, null, SignalWriter.SignalStatusResolved);
        SeedPolicyless(h, DeletedPolicyCode, OtherMatterId, null, SignalWriter.SignalStatusResolved);
        var offReads = 0;
        var deletedReads = 0;
        h.Sysadmin.FaultOn = q =>
        {
            var prefix = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_dedupekey")?.Values[0] as string;
            if (q.EntityName != "sprk_signal" || prefix is null) return null;
            if (prefix == SignalWriter.DedupeKeyPolicyPrefix("POL-OFF") && ++offReads > 0) return new TimeoutException("Dataverse timed out.");
            if (prefix == SignalWriter.DedupeKeyPolicyPrefix(DeletedPolicyCode)) deletedReads++;
            return null;
        };

        var result = await h.RunAsync();

        offReads.Should().Be(1, "a policy's Signals are swept once per run");
        deletedReads.Should().Be(1, "the control: the deleted code is swept");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("Policy POL-OFF is not in force", StringComparison.Ordinal));
        h.Log.Should().NotContain(e => e.Message.Contains("POL-OFF was deleted", StringComparison.Ordinal));
        result.ResultJson.Should().Contain("\"policyFailures\":1");
    }

    [Fact]
    public async Task Run_R4_WhenTheDeletedCodesCannotBeRead_NothingExtraIsRedacted_TheRunFailsVisibly_AndTheCloseStillHappens()
    {
        // Item 1, fail closed: the redaction's read (every status, no status condition) faults.
        var h = new Harness().WithPathBPolicy();
        var orphan = SeedOrphan(h, OtherMatterId);
        var deletedPrefixReads = 0;
        h.Sysadmin.FaultOn = q =>
        {
            if (q.EntityName == "sprk_signal" && q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_dedupekey")?.Values[0] is string p
                && p == SignalWriter.DedupeKeyPolicyPrefix(DeletedPolicyCode)) deletedPrefixReads++;
            return q.EntityName == "sprk_signal"
                   && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Null)
                   && q.Criteria.Conditions.All(c => c.AttributeName != "sprk_signalstatus")
                ? new TimeoutException("Dataverse timed out.") : null;
        };

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a sweep that could not read is a failed run, never a quiet success");
        result.ResultJson.Should().Contain("\"policyFailures\":1").And.Contain("\"deletedPolicyRetired\":1");
        AssertSystemClosure(h.Org.Rows[orphan], SignalWriter.ResolutionPolicyRetired);
        deletedPrefixReads.Should().Be(0, "no deleted code was swept on a guess");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("Signals of deleted policies could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_R4_AReusedCode_TheOldRulesSignalIsClosed_AndASignalCreatedAfterTheNewRuleIsNeverTouched()
    {
        // Item 3 (F1). A rule is deleted and its code recreated (a new rule saves Off) before the nightly run. The old Signal, created
        // BEFORE the policy now holding the code, is the deleted rule's: closed. A Signal created AFTER that policy with an empty lookup
        // is a hand edit of the new rule's own Signal (Console User has Write on sprk_signal): never touched.
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-REUSED", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyCreatedOn("POL-REUSED", RunAt.UtcDateTime.AddDays(-1));
        var old = SeedPolicyless(h, "POL-REUSED", MatterId, RunAt.UtcDateTime.AddDays(-10));
        var handEdited = SeedPolicyless(h, "POL-REUSED", OtherMatterId, RunAt.UtcDateTime.AddHours(-1));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[old], SignalWriter.ResolutionPolicyRetired);
        h.Org.Rows[handEdited].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == handEdited);
        result.ResultJson.Should().Contain("\"deletedPolicyRetired\":1");
    }

    [Theory]
    [InlineData(-10, true)]  // created before the policy now holding the code: the deleted rule's
    [InlineData(-0.5, false)] // created after it: the live rule's (a hand edit)
    public async Task DeletedPolicyCodes_R4_AReusedCode_IsListedOnlyForASignalOlderThanThePolicyHoldingIt(double signalAgeDays, bool listed)
    {
        // Item 3 applied to item 1: the reused code's OLD Signals are redacted as a deleted rule's too.
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-REUSED", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        h.Sysadmin.SetPolicyCreatedOn("POL-REUSED", RunAt.UtcDateTime.AddDays(-1));
        SeedPolicyless(h, "POL-REUSED", MatterId, RunAt.UtcDateTime.AddDays(signalAgeDays), SignalWriter.SignalStatusResolved);

        var deleted = await h.Closure.DeletedPolicyCodesWithSignalsAsync(await h.Closure.ExistingPolicyCodesAsync(default), default);

        if (listed) deleted.Codes.Should().Equal("POL-REUSED");
        else deleted.Codes.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_R4_ManyUnparseableKeys_LogTheExactCount_AndOnlyTheFirstFiftyIds()
    {
        // K b: the unparseable-key warning is one bounded line.
        var h = new Harness().WithPathBPolicy();
        var ids = Enumerable.Range(0, 55)
            .Select(i => h.Org.SeedSignal($"{DeletedPolicyCode}|sprk_matter|not-a-guid-{i}|1", SignalWriter.SignalStatusOpen, coreId: MatterId))
            .ToList();

        await h.RunAsync();

        var line = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Warning
            && e.Message.Contains("with no policy lookup have a dedupe key that is not", StringComparison.Ordinal)).Subject.Message;
        line.Should().Contain("55 open Signal(s)").And.Contain("and 5 more");
        ids.Count(id => line.Contains(id.ToString("D"), StringComparison.Ordinal)).Should().Be(SignalReevaluationJob.MaxLoggedOutOfScopeIds);
    }

    [Fact]
    public async Task Run_R4_ThePolicyListIsReadOverTwoPages_AndAPolicyOnTheSecondPageStillExists()
    {
        // K c: the existence read pages. With one policy per page, whichever policy lands on page 2 would look deleted if the read stopped
        // after page 1, and its policy-less Signal would be closed.
        var h = new Harness();
        h.Sysadmin.AddPolicy("POL-A", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        h.Sysadmin.AddPolicy("POL-B", SignalWriter.LaneDecide, false, PathBBody, PathBTemplate);
        var a = SeedPolicyless(h, "POL-A", MatterId, null);
        var b = SeedPolicyless(h, "POL-B", OtherMatterId, null);
        var orphan = SeedOrphan(h, ThirdMatterId);
        h.Sysadmin.PageSizeCap = 1;

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        h.Sysadmin.PagedReads.Where(r => r.Table == "sprk_policy").Select(r => r.Page).Should().Equal(1, 2);
        AssertSystemClosure(h.Org.Rows[orphan], SignalWriter.ResolutionPolicyRetired);
        foreach (var live in new[] { a, b })
        {
            h.Org.Rows[live].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusOpen);
        }
    }

    [Fact]
    public async Task Run_R4_MoreOrphansThanOneRunReads_ClosesTheBound_WarnsMoreRemain_AndLeavesTheRestForTheNextRun()
    {
        // K c: the MoreRemain warning. One Signal per page, MaxPages pages per run, one more orphan than that.
        var h = new Harness();
        var orphans = Enumerable.Range(0, SignalEvaluator.MaxPages + 1).Select(_ => SeedOrphan(h, Guid.NewGuid())).ToList();
        h.Sysadmin.PageSizeCap = 1;

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain($"\"deletedPolicyRetired\":{SignalEvaluator.MaxPages}");
        orphans.Count(id => h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value == SignalWriter.SignalStatusOpen)
            .Should().Be(1, "the one past the bound is left for the next run");
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains($"More open Signals with no policy lookup remain than one run reads ({SignalEvaluator.MaxPages} pages)", StringComparison.Ordinal));
        // The redaction's read of every status is bounded the same way; Resolved Signals never drop out of it, so its bound is a failed run.
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains("the stored evidence of the deleted policies beyond them was not checked", StringComparison.Ordinal));
        result.Success.Should().BeFalse();
    }

    // ── Fix round 5 (task 102b): the date rules of ExistingPolicyCodes, and the cap on the logged codes ─────────────────────────────

    private static readonly DateTime PolicyDate = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExistingPolicyCodes_R5_EqualTimestamps_AreNotAnOrphan()
    {
        // K3: a Signal created at the very instant of the policy holding its code is not older than it: not an orphan.
        var existing = new ExistingPolicyCodes();
        existing.Add("POL-X", PolicyDate);

        existing.IsOrphan("POL-X", PolicyDate).Should().BeFalse("equal timestamps are not 'created before'");
        existing.IsOrphan("POL-X", PolicyDate.AddTicks(-1)).Should().BeTrue("the boundary is exact: one tick older is the deleted rule's");
    }

    [Fact]
    public void ExistingPolicyCodes_R5_DuplicateCodesIgnoringCase_TheEarliestDateWins_WhateverTheOrder()
    {
        // K3: two rows hold the same code (ignoring case). The EARLIEST date is kept, so the Signal between the two dates counts as the
        // live rule's (never closed), not as an orphan of the later one.
        var earlier = PolicyDate.AddDays(-10);
        var later = PolicyDate;
        var between = PolicyDate.AddDays(-5);

        var laterFirst = new ExistingPolicyCodes();
        laterFirst.Add("POL-X", later);
        laterFirst.Add("pol-x", earlier);
        var earlierFirst = new ExistingPolicyCodes();
        earlierFirst.Add("pol-x", earlier);
        earlierFirst.Add("POL-X", later);

        foreach (var existing in new[] { laterFirst, earlierFirst })
        {
            existing.Contains("Pol-X").Should().BeTrue();
            existing.IsOrphan("POL-X", between).Should().BeFalse("the earliest date wins: the Signal is newer than the earlier policy");
            existing.IsOrphan("POL-X", earlier.AddDays(-1)).Should().BeTrue("a Signal older than both policies is still an orphan");
        }
    }

    [Fact]
    public void ExistingPolicyCodes_R5_AnUnknownDate_BeatsAKnownOne_AndCountsAsExisting_WhateverTheOrder()
    {
        // K3: a policy row with no createdon wins over a dated one with the same code: the code "exists" for every Signal.
        var old = PolicyDate.AddDays(-30);
        var knownFirst = new ExistingPolicyCodes();
        knownFirst.Add("POL-X", PolicyDate);
        knownFirst.Add("POL-X", null);
        var unknownFirst = new ExistingPolicyCodes();
        unknownFirst.Add("POL-X", null);
        unknownFirst.Add("POL-X", PolicyDate);

        foreach (var existing in new[] { knownFirst, unknownFirst })
        {
            existing.IsOrphan("POL-X", old).Should().BeFalse("an unknown policy date means the code exists: never an orphan");
            existing.IsOrphan("POL-X", null).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Run_R5_ManyDeletedPolicies_LogTheExactCount_AndOnlyTheFirstFiftyCodes()
    {
        // K3: the codes in the close line are capped like the ids (MaxLoggedOutOfScopeIds), with the "and N more" tail.
        var h = new Harness();
        var codes = Enumerable.Range(0, 60).Select(i => $"POL-GONE-{i:D3}").ToList();
        foreach (var code in codes) SeedPolicyless(h, code, Guid.NewGuid(), RunAt.UtcDateTime.AddDays(-2));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        var line = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Information
            && e.Message.Contains("of a deleted policy were closed PolicyRetired", StringComparison.Ordinal)).Subject.Message;
        var codePart = line[line.IndexOf("policy code(s) ", StringComparison.Ordinal)..];
        codes.Count(code => codePart.Contains(code, StringComparison.Ordinal)).Should().Be(SignalReevaluationJob.MaxLoggedOutOfScopeIds);
        codePart.Should().Contain(codes[0]).And.Contain(codes[49]).And.NotContain(codes[50]).And.EndWith("and 10 more.");
    }
}
