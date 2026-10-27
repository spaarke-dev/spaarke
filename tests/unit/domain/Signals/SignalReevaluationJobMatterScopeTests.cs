using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ontology;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.AccessControl.Ontology;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Ontology platform R1 task 102b (D-165; DI-62 #1647, DI-112 #2069): a policy limited to one matter (<c>sprk_policy.sprk_matter</c>) is
/// evaluated within that matter by the existing subject-scoped evaluation, never writes, closes or re-owns a Signal outside it, and a
/// publish that changes the scope closes the Signals now outside it (Superseded) in that same pass. The real job, evaluator, writer,
/// closure and suppression services run over the in-memory seams of <see cref="SignalReevaluationJobTests"/>.
/// </summary>
public partial class SignalReevaluationJobTests
{
    private static readonly Guid ThirdMatterId = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc98a");

    private const string ScopedPolicyCode = "POL-SCOPED";

    /// <summary>A rule about matters every matter row satisfies (the World does not model <c>statecode</c>).</summary>
    private const string AnyMatterBody = """{"type":"Existence","subject":"sprk_matter","when":{"statecode":0},"all":[]}""";

    private const string AnyMatterTemplate = "A matter needs a look.";

    /// <summary>A matter the rule holds for: its row (the subject) and its owning unit (the writer's ownership read).</summary>
    private static void QualifyingMatter(Harness h, Guid matterId, string number)
    {
        h.Sysadmin.SubjectRows["sprk_matter"].Add(new Entity("sprk_matter", matterId)
        {
            ["sprk_matterid"] = matterId,
            ["sprk_mattername"] = "Matter " + number,
            ["sprk_matternumber"] = number,
        });
        h.Sysadmin.Stub(new Entity("sprk_matter", matterId) { ["owningbusinessunit"] = new EntityReference("businessunit", BusinessUnitId) });
    }

    private static Harness ScopedHarness(Guid? matterScope, string body = AnyMatterBody)
    {
        var h = new Harness();
        h.Sysadmin.AddPolicy(ScopedPolicyCode, SignalWriter.LaneDecide, true, body, AnyMatterTemplate);
        h.Sysadmin.SetMatterScope(ScopedPolicyCode, matterScope);
        return h;
    }

    private static Guid SeedOpenSignal(Harness h, Guid matterId, int episode = 1) =>
        h.Org.SeedSignal(Key(ScopedPolicyCode, "sprk_matter", matterId, episode), SignalWriter.SignalStatusOpen, coreId: matterId,
            policyId: h.Sysadmin.PolicyIdOf(ScopedPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(ScopedPolicyCode));

    private static IEnumerable<string> CreatedKeys(Harness h) =>
        h.Org.CreatedPayloads.Select(p => p.GetAttributeValue<string>("sprk_dedupekey"));

    private static int StatusOf(Harness h, Guid signalId) => h.Org.Rows[signalId].GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value;

    // =====================================================================================
    // AC2: the nightly run evaluates a matter-scoped rule within its matter
    // =====================================================================================

    [Fact]
    public async Task Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_AndAQualifyingSubjectOnAnotherMatterGetsNoSignal()
    {
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"scopedNotEvaluated\":0").And.Contain("\"policiesEvaluated\":1");
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", MatterId, 1));
        CreatedKeys(h).Should().NotContain(k => k.Contains(Lower(OtherMatterId), StringComparison.Ordinal),
            "must-not-fire: matter N qualifies but is outside the rule's scope");
        h.Sysadmin.Fetches.Should().Contain(f => f.Contains($"attribute=\"sprk_matterid\" operator=\"eq\" value=\"{MatterId:D}\"", StringComparison.Ordinal),
            "the existing subject-scoped evaluation, confined in the query itself");
    }

    [Fact]
    public async Task Run_TheSameDataWithoutAScope_RaisesForBothMatters()
    {
        // The control for the test above: the fixture does raise for N when the rule is not limited.
        var h = ScopedHarness(null);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        CreatedKeys(h).Should().BeEquivalentTo(
            Key(ScopedPolicyCode, "sprk_matter", MatterId, 1), Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1));
    }

    [Fact]
    public async Task Run_AMatterScopedRule_ClosesConditionClearedOnlyInsideItsMatter_AndClosesAnOpenSignalOutsideIt_Superseded()
    {
        // Fix round 1 (uac-r2 F2): an open Signal outside the scope is closed Superseded (the scope supersedes it), never left open.
        var h = ScopedHarness(MatterId); // no matter qualifies any more
        var inside = SeedOpenSignal(h, MatterId);
        var outside = SeedOpenSignal(h, OtherMatterId); // e.g. the scope was changed outside the BFF, without a publish

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[inside], SignalWriter.ResolutionConditionCleared);
        AssertSystemClosure(h.Org.Rows[outside], SignalWriter.ResolutionSuperseded);
        result.ResultJson.Should().Contain("\"outOfScopeSuperseded\":1").And.Contain("\"supersededClosed\":1");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("outside its matter scope", StringComparison.Ordinal)
            && e.Message.Contains(outside.ToString("D"), StringComparison.Ordinal), "the closure names the Signal it closed");
        h.Log.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("outside its matter scope", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_AMatterScopeChange_ClosingManySignals_LogsTheExactCount_AndOnlyTheFirstFiftyIds()
    {
        // Fix round 2 (K4): one bounded line however many Signals a scope change closes.
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        var outside = Enumerable.Range(0, 60).Select(_ => SeedOpenSignal(h, Guid.NewGuid())).ToList();

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.ResultJson.Should().Contain("\"outOfScopeSuperseded\":60", "the count is exact");
        outside.Should().OnlyContain(id => h.Org.Rows[id].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value == SignalWriter.ResolutionSuperseded);
        var line = h.Log.Should().ContainSingle(e => e.Level == LogLevel.Information
            && e.Message.Contains("outside its matter scope", StringComparison.Ordinal)).Subject.Message;
        line.Should().Contain("60 open Signal(s)").And.Contain("and 10 more");
        outside.Count(id => line.Contains(id.ToString("D"), StringComparison.Ordinal)).Should().Be(SignalReevaluationJob.MaxLoggedOutOfScopeIds);
    }

    [Fact]
    public async Task Run_AMatterScopedRule_NeverReopensOrOverwrites_ADecidedSignalOutsideItsMatter()
    {
        // Fix round 2 (F3): the out-of-scope supersede is for OPEN Signals only (SignalClosureService, `s.IsOpen`). A Signal a person
        // already dismissed, on a matter outside the scope and citing the current version, keeps its resolution and status.
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        var dismissedOnN = h.Org.SeedSignal(Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: OtherMatterId,
            policyId: h.Sysadmin.PolicyIdOf(ScopedPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(ScopedPolicyCode));

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        var row = h.Org.Rows[dismissedOnN];
        row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")!.Value.Should().Be(SignalWriter.SignalStatusResolved);
        row.GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionDismissed,
            "a decided Signal is never re-closed as Superseded");
        h.Org.ConditionalUpdates.Should().NotContain(u => u.Id == dismissedOnN, "no conditional update targets it");
        result.ResultJson.Should().Contain("\"outOfScopeSuperseded\":0");
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", MatterId, 1)); // M still qualifies and is raised
    }

    [Fact]
    public async Task Run_AMatterScopedRule_StillRedactsTheStoredEvidenceOfASignalOutsideItsMatter()
    {
        // Fix round 2 (F3): the D-123 redaction covers the policy's Signals in and out of the scope (SignalReevaluationJob passes
        // existing + outOfScope). The policy is valid and evaluated, so only its own pass (not the sweep) can redact this Signal.
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        h.WithMatterHit(OtherMatterId); // the witness communication is filed on N, outside the scope
        var secureProject = Guid.Parse("7a1c2b3d-0000-4000-8000-0000000001c1");
        h.Sysadmin.SecureProject(secureProject);
        h.Sysadmin.Stub(new Entity("sprk_matter", OtherMatterId) { ["owningteam"] = new EntityReference("team", DefaultTeamId), ["sprk_issecure"] = false });
        h.Sysadmin.WitnessRows["sprk_communication"].Single()["owningteam"] = new EntityReference("team", DefaultTeamId);
        var onN = h.Org.SeedSignal(Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusResolved,
            resolution: SignalWriter.ResolutionDismissed, resolvedOn: RunAt.UtcDateTime.AddDays(-1), coreId: OtherMatterId,
            policyId: h.Sysadmin.PolicyIdOf(ScopedPolicyCode), versionId: h.Sysadmin.CurrentVersionOf(ScopedPolicyCode));
        h.Org.Rows[onN]["sprk_regardingmatter"] = new EntityReference("sprk_matter", OtherMatterId);
        h.Org.Rows[onN]["sprk_matter"] = new EntityReference("sprk_matter", OtherMatterId);
        h.Org.Rows[onN]["sprk_evidencerefs"] = StoredEvidenceQuotingTheWitness();
        RefileTheWitnessUnder(h, secureProject); // the witness now sits behind secure X

        var result = await h.RunAsync();

        result.ResultJson.Should().Contain("\"evidenceRedacted\":1");
        var redaction = h.Org.ConditionalUpdates.Where(u => u.Fields.ContainsKey("sprk_evidencerefs")).Should().ContainSingle().Subject;
        redaction.Id.Should().Be(onN);
        ((string)redaction.Fields["sprk_evidencerefs"]).Should().NotContain("Rate increase").And.NotContain("partner@firm.example")
            .And.Contain($"sprk_communication:{Lower(CommunicationId)}");
        h.Org.Rows[onN].GetAttributeValue<OptionSetValue>("sprk_resolutiontype")!.Value.Should().Be(SignalWriter.ResolutionDismissed,
            "the redaction removes detail and nothing else");
    }

    [Fact]
    public async Task Run_AScopeChangedInDataverseFromMToN_WithoutAPublish_ClosesMsOpenSignal_Superseded_AndRaisesN()
    {
        // Fix round 1 (uac-r2 F2), route (a): sprk_matter edited from M to N directly in Dataverse, so M's open Signal cites the CURRENT
        // version and the version-based supersede alone would leave it open.
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");
        var onM = SeedOpenSignal(h, MatterId);
        h.Sysadmin.SetMatterScope(ScopedPolicyCode, OtherMatterId);

        var result = await h.RunAsync();

        result.Success.Should().BeTrue(result.ErrorMessage);
        AssertSystemClosure(h.Org.Rows[onM], SignalWriter.ResolutionSuperseded);
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1));
        result.ResultJson.Should().Contain("\"outOfScopeSuperseded\":1");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains("outside its matter scope", StringComparison.Ordinal) && e.Message.Contains(onM.ToString("D"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_Narrowing_ClosesANoVersionOpenSignalOutsideTheNewScope_Superseded()
    {
        // Fix round 1 (uac-r2 F2), route (b): a Signal citing no version is not superseded by the version rule, but outside the new scope
        // the scope supersedes it.
        var h = ScopedHarness(null);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");
        var onM = SeedOpenSignal(h, MatterId);
        var noVersionOnN = h.Org.SeedSignal(Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1), SignalWriter.SignalStatusOpen,
            coreId: OtherMatterId, policyId: h.Sysadmin.PolicyIdOf(ScopedPolicyCode));
        h.Sysadmin.PublishNewVersion(ScopedPolicyCode, AnyMatterBody, AnyMatterTemplate);
        h.Sysadmin.SetMatterScope(ScopedPolicyCode, MatterId);

        var result = await h.Job.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(ScopedPolicyCode));

        result.Success.Should().BeTrue(result.FailureSummary);
        AssertSystemClosure(h.Org.Rows[noVersionOnN], SignalWriter.ResolutionSuperseded);
        AssertSystemClosure(h.Org.Rows[onM], SignalWriter.ResolutionSuperseded);
        result.SupersededClosed.Should().Be(2);
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", MatterId, 2));
    }

    [Fact]
    public async Task Run_AScopedEvaluationThatReturnsAnotherSubject_FailsThePolicy_AndWritesNothing()
    {
        // Fix round 1 (uac-r2 F3): the fail-closed hit guard's must-fire. Dataverse is made to ignore the scoped root condition, so the
        // evaluation returns N as well; the must-not-fire control is Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_...
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");
        SeedOpenSignal(h, OtherMatterId);
        h.Sysadmin.IgnoreSubjectIdCondition = true;

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("a hit outside the scope fails the policy for this pass");
        result.ResultJson.Should().Contain("\"policyFailures\":1").And.Contain("\"written\":0").And.Contain("\"closed\":0");
        h.Org.CreateCalls.Should().Be(0);
        h.Org.WriteCalls.Should().Be(0, "nothing is raised, closed, reconciled or redacted for the policy");
        h.Log.Should().Contain(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("returned another subject", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_AMatterScopeOnARuleThatIsNotAboutMatters_IsScopedNotEvaluated_AndNothingIsWritten()
    {
        var h = ScopedHarness(MatterId, TodoOverdueBody).WithTodo(TodoId, due: new DateTime(2026, 10, 1));

        var result = await h.RunAsync();

        result.Success.Should().BeFalse("an unevaluable policy is visible, never evaluated as if unscoped");
        result.ResultJson.Should().Contain("\"scopedNotEvaluated\":1");
        h.Org.CreateCalls.Should().Be(0);
        h.Log.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId.Id == OntologyWriterEvents.EvaluatorPolicyFailed.Id
            && e.Message.Contains("not about matters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trigger_OnAnotherMatter_DoesNotEvaluateAMatterScopedRule_OnItsMatterItDoes()
    {
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");

        var other = await h.Job.EvaluateSubjectAsync("sprk_matter", OtherMatterId, default);
        other.PoliciesEvaluated.Should().Be(0, "a trigger on N cannot hold or clear for a rule limited to M");
        h.Org.CreateCalls.Should().Be(0);

        var own = await h.Job.EvaluateSubjectAsync("sprk_matter", MatterId, default);
        own.PoliciesEvaluated.Should().Be(1);
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", MatterId, 1));
    }

    // =====================================================================================
    // AC5: a publish that changes the scope closes the Signals now outside it, in that publish
    // =====================================================================================

    [Fact]
    public async Task Publish_FromAllMattersToM_ClosesTheOpenSignalsOutsideM_Superseded_AndReraisesOnlyM()
    {
        var h = ScopedHarness(null);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");
        var onM = SeedOpenSignal(h, MatterId);
        var onN = SeedOpenSignal(h, OtherMatterId);
        var onO = SeedOpenSignal(h, ThirdMatterId);
        var v2 = h.Sysadmin.PublishNewVersion(ScopedPolicyCode, AnyMatterBody, AnyMatterTemplate);
        h.Sysadmin.SetMatterScope(ScopedPolicyCode, MatterId); // the same conditional write as the pointer (the authoring service)

        var result = await h.Job.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(ScopedPolicyCode));

        result.Success.Should().BeTrue(result.FailureSummary);
        AssertSystemClosure(h.Org.Rows[onN], SignalWriter.ResolutionSuperseded);
        AssertSystemClosure(h.Org.Rows[onO], SignalWriter.ResolutionSuperseded);
        AssertSystemClosure(h.Org.Rows[onM], SignalWriter.ResolutionSuperseded); // the publish's own supersede (D-13), then re-raised
        result.SupersededClosed.Should().Be(3);
        result.Raised.Should().Be(1);
        var created = h.Org.CreatedPayloads.Should().ContainSingle("N still qualifies but is outside the new scope: never re-raised").Subject;
        created.GetAttributeValue<string>("sprk_dedupekey").Should().Be(Key(ScopedPolicyCode, "sprk_matter", MatterId, 2));
        created.GetAttributeValue<EntityReference>("sprk_policyversion")!.Id.Should().Be(v2);
        h.Org.ConditionalUpdates.Should().HaveCount(3, "each old Signal closed once, as Superseded; nothing else is touched");
    }

    [Fact]
    public async Task Publish_FromMToN_ClosesM_Superseded_AndRaisesN()
    {
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N");
        var onM = SeedOpenSignal(h, MatterId);
        h.Sysadmin.PublishNewVersion(ScopedPolicyCode, AnyMatterBody, AnyMatterTemplate);
        h.Sysadmin.SetMatterScope(ScopedPolicyCode, OtherMatterId);

        var result = await h.Job.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(ScopedPolicyCode));

        result.Success.Should().BeTrue(result.FailureSummary);
        AssertSystemClosure(h.Org.Rows[onM], SignalWriter.ResolutionSuperseded);
        result.SupersededClosed.Should().Be(1);
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", OtherMatterId, 1));
        h.Log.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("outside its matter scope", StringComparison.Ordinal),
            "nothing outside the new scope is left open");
        h.Log.Should().Contain(e => e.Level == LogLevel.Information
            && e.Message.Contains("outside its matter scope", StringComparison.Ordinal) && e.Message.Contains(onM.ToString("D"), StringComparison.Ordinal),
            "the closure of M's Signal, now outside the scope, is named");
    }

    [Fact]
    public async Task Publish_KeepingTheScope_ClosesNothingInsideIt_ThatTheNewVersionDoesNotReraise()
    {
        // "None inside it": an in-scope Signal is superseded and re-raised exactly as for any publish (D-13), never closed for the scope.
        var h = ScopedHarness(MatterId);
        QualifyingMatter(h, MatterId, "M");
        var onM = SeedOpenSignal(h, MatterId);
        h.Sysadmin.PublishNewVersion(ScopedPolicyCode, AnyMatterBody, AnyMatterTemplate);

        var result = await h.Job.ReevaluatePolicyAsync(h.Sysadmin.PolicyIdOf(ScopedPolicyCode));

        result.Success.Should().BeTrue(result.FailureSummary);
        AssertSystemClosure(h.Org.Rows[onM], SignalWriter.ResolutionSuperseded);
        CreatedKeys(h).Should().Equal(Key(ScopedPolicyCode, "sprk_matter", MatterId, 2));
    }

    // =====================================================================================
    // AC3: the dry run's counts are the publish's counts (DI-112), over the same data
    // =====================================================================================

    /// <summary>The authoring engine as production wires it (the real evaluator and job). Before the job's pass, the caller's world's
    /// publish (the new version's body, the pointer and the scope it wrote) is applied to the job's app-only world, as Dataverse would
    /// show the one write to both identities.</summary>
    private sealed class MirroredEngine(Harness h, AuthoringWorld caller, string code) : IOntologyAdminAuthoringSignalEngine
    {
        private readonly OntologyAdminAuthoringSignalEngine _production = new(h.Evaluator, h.Job);

        public Task<SignalEvaluation> EvaluateAsync(SignalEvaluationPolicy policy, Guid? matterScope, CancellationToken ct) =>
            _production.EvaluateAsync(policy, matterScope, ct);

        public Task<PolicyReevaluationResult> ReevaluateAsync(Guid policyId, CancellationToken ct)
        {
            var version = caller.Writes.Last(w => w.Kind == "POST sprk_policyversions").Json;
            h.Sysadmin.PublishNewVersion(code, version.GetProperty("sprk_rulebody").GetString()!, version.GetProperty("sprk_messagetemplate").GetString()!);
            h.Sysadmin.SetMatterScope(code, caller.Policies[policyId].Matter);
            return _production.ReevaluateAsync(policyId, ct);
        }
    }

    [Fact]
    public async Task DryRunWithinAMatter_CountsExactlyWhatThePublishSupersedesAndReraises()
    {
        var h = ScopedHarness(null);
        QualifyingMatter(h, MatterId, "M");
        QualifyingMatter(h, OtherMatterId, "N"); // qualifies, outside the scope
        var policyId = h.Sysadmin.PolicyIdOf(ScopedPolicyCode);
        var v1 = h.Sysadmin.CurrentVersionOf(ScopedPolicyCode);

        // The caller's view of the same rows: the policy, its version, and the policy's open Signals on M, N and O.
        var caller = new AuthoringWorld { Caller = Guid.NewGuid() };
        caller.AddPolicy(policyId, ScopedPolicyCode, SignalWriter.LaneDecide, enabled: true, domain: null, current: v1);
        caller.AddVersion(v1, policyId, number: 1, inForceTo: null);
        foreach (var matter in new[] { MatterId, OtherMatterId, ThirdMatterId })
        {
            SeedOpenSignal(h, matter);
            caller.AddSignal(policyId, "sprk_matter", matter, 1, SignalWriter.SignalStatusOpen, core: matter, coreType: MatterType);
        }

        var schema = new RuleBodySchemaValidator();
        var service = new OntologyAdminAuthoringService(caller,
            new PolicyVersionValidator(schema, new PredicateCompiler(schema, h.Clock), NullLogger<PolicyVersionValidator>.Instance),
            new MirroredEngine(h, caller, ScopedPolicyCode),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), // #2084: an ordinary owner, as before
            new TenantCache(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<TenantCache>.Instance),
            h.Clock, NullLogger<OntologyAdminAuthoringService>.Instance);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "admin"), new Claim("tid", "tenant-102b")], "test"));
        var draft = new OntologyRuleDraft("Existence", AnyMatterBody, AnyMatterTemplate, "Needs a look", "Review the matter",
            """{"actions":["send-budget-inquiry"]}""");

        var dryRun = await service.DryRunAsync(principal,
            new OntologyDryRunRequest(policyId, null, "Existence", AnyMatterBody, AnyMatterTemplate, MatterId), default);
        var publish = await service.PublishVersionAsync(principal, policyId, new OntologyPublishRequest(draft, MatterId), default);

        dryRun.Outcome.Should().Be(OntologyAdminAuthoringOutcome.Ok, string.Join("; ", dryRun.Errors?.SelectMany(e => e.Value) ?? []));
        publish.Outcome.Should().Be(OntologyAdminAuthoringOutcome.Ok, string.Join("; ", publish.Errors?.SelectMany(e => e.Value) ?? []));
        var counts = dryRun.Value!;
        var published = publish.Value!;
        published.Reevaluation.Should().Be("completed");
        counts.Rows.Select(r => r.SubjectId).Should().Equal(MatterId);
        (counts.WouldSupersede, counts.WouldReraise, counts.New).Should().Be((3, 1, 0));
        published.SupersededClosed.Should().Be(counts.WouldSupersede, "every open Signal of the policy cites the old version");
        published.Raised.Should().Be(counts.WouldReraise + counts.New, "only the in-scope subject is raised again");
        caller.Policies[policyId].Matter.Should().Be(MatterId);
    }
}
