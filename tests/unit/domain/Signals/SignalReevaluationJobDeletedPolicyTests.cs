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
        line.Should().Contain("2 open Signal(s)").And.Contain(open.ToString("D")).And.Contain(acknowledged.ToString("D"));
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
            ? q.EntityName == "sprk_signal" && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_policy" && c.Operator == Microsoft.Xrm.Sdk.Query.ConditionOperator.Null)
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
            && e.Message.Contains("1 open Signal(s) with no policy lookup have a dedupe key that is not", StringComparison.Ordinal));
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
}
