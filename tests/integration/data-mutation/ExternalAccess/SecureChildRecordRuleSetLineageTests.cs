// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: ebilling-r1 task 089a (procedure model 5.2) — a Procedure assignment and a record-scoped rule set (with its
//     record rules and their versions) are secure children of the core record they name. Driven over the REAL reconciler and the
//     REAL ownership resolver on task 148's in-memory Dataverse (SecureChildShareWorld), as SecureChildPolicyLineageTests and
//     RestrictedContentReconcileTests are: the chain version → rule → rule set → matter follows the matter into isolation and out
//     of it (to the restricted-content owner team, EB-D-168); a Procedure rule set and the rule an assignment excludes are never
//     reached.
//
// Doubles are module boundaries only (ADR-038 §4).

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

public class SecureChildRecordRuleSetLineageTests
{
    private const string Matter = "sprk_matter";

    private static readonly Guid ContentTeam = SecureChildShareWorld.RestrictedContentTeam;
    private static readonly Guid CustomerTeam = SecureChildShareWorld.CustomerTeam;

    private static readonly Guid MatterId = Guid.Parse("08900000-0000-4000-8000-0000000089a0");
    private static readonly Guid RecordSetId = Guid.Parse("08900000-0000-4000-8000-0000000089a1");
    private static readonly Guid RecordRuleId = Guid.Parse("08900000-0000-4000-8000-0000000089a2");
    private static readonly Guid RecordVersionId = Guid.Parse("08900000-0000-4000-8000-0000000089a3");
    private static readonly Guid AssignmentId = Guid.Parse("08900000-0000-4000-8000-0000000089a4");
    private static readonly Guid ProcedureId = Guid.Parse("08900000-0000-4000-8000-0000000089b0");
    private static readonly Guid ProcedureSetId = Guid.Parse("08900000-0000-4000-8000-0000000089b1");
    private static readonly Guid BaseRuleId = Guid.Parse("08900000-0000-4000-8000-0000000089b2");
    private static readonly Guid BaseVersionId = Guid.Parse("08900000-0000-4000-8000-0000000089b3");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly FakeRecordShareTable _shares = new();
    private readonly SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi _webApi;

    public SecureChildRecordRuleSetLineageTests() =>
        _webApi = new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(() => _world);

    private SecureChildReconciler Reconciler() => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _webApi);

    /// <summary>A matter root's Make Secure pass reads its Assign cascade (document locations) over the Web API; this one has none
    /// (as SecureChildPolicyLineageTests).</summary>
    private SecureChildReconciler MakeSecureReconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, _shares, new Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient());

    private static EntityReference TeamRef(Guid id) => new("team", id);

    private void Row(string table, Guid id, EntityReference owner, params (string Column, string Target, Guid TargetId)[] lookups) =>
        _world.Add(table, id, lookups.Select(l => (l.Column, (object)new EntityReference(l.Target, l.TargetId)))
            .Prepend((owner.LogicalName == "team" ? "owningteam" : "owninguser", owner))
            .Append(("versionnumber", (object)1L))
            .ToArray());

    /// <summary>The Procedure's own rule set, its base rule and the rule's version: restricted-content owned, no core record.</summary>
    private void ProcedureRules()
    {
        Row("sprk_ruleset", ProcedureSetId, TeamRef(ContentTeam), ("sprk_procedure", "sprk_procedure", ProcedureId));
        Row("sprk_policy", BaseRuleId, TeamRef(ContentTeam), ("sprk_ruleset", "sprk_ruleset", ProcedureSetId));
        Row("sprk_policyversion", BaseVersionId, TeamRef(ContentTeam), ("sprk_policy", "sprk_policy", BaseRuleId));
    }

    private void AssertProcedureRulesUntouched()
    {
        foreach (var (table, id) in new[] { ("sprk_ruleset", ProcedureSetId), ("sprk_policy", BaseRuleId), ("sprk_policyversion", BaseVersionId) })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(ContentTeam), $"{table} names no core record and is never reached");
        }

        _world.OwnerWrites.Select(w => w.Id).Should().NotContain(new[] { ProcedureSetId, BaseRuleId, BaseVersionId });
    }

    [Fact(DisplayName = "089a must fire: Make Secure takes the record's rule set, its record rule, the rule's version and the Procedure assignment into isolation")]
    public async Task AMakeSecure_ReownsTheRecordRuleChain_AndTheAssignment()
    {
        _world.SecureRoot(Matter, MatterId);
        Row("sprk_ruleset", RecordSetId, TeamRef(ContentTeam), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", RecordRuleId, TeamRef(ContentTeam), ("sprk_ruleset", "sprk_ruleset", RecordSetId));
        Row("sprk_policyversion", RecordVersionId, TeamRef(ContentTeam), ("sprk_policy", "sprk_policy", RecordRuleId));
        Row("sprk_procedureassignment", AssignmentId, TeamRef(ContentTeam),
            ("sprk_matter", Matter, MatterId), ("sprk_procedure", "sprk_procedure", ProcedureId),
            ("sprk_excludedpolicy", "sprk_policy", BaseRuleId));
        ProcedureRules();

        var report = await MakeSecureReconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        foreach (var (table, id) in new[]
                 {
                     ("sprk_ruleset", RecordSetId), ("sprk_policy", RecordRuleId), ("sprk_policyversion", RecordVersionId),
                     ("sprk_procedureassignment", AssignmentId),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam), $"{table} follows its matter");
        }

        // The assignment CITES the rule it excludes: the Procedure's base rule is not filed under the assignment.
        AssertProcedureRulesUntouched();
    }

    [Fact(DisplayName = "089a must fire: an Unsecure moves the isolated record rule chain and the assignment to the restricted-content owner team")]
    public async Task AnUnsecure_MovesTheRecordRuleChain_ToTheRestrictedContentTeam()
    {
        _world.Add(Matter, MatterId, ("owningteam", TeamRef(CustomerTeam)), ("sprk_issecure", true)); // mid-unsecure, flag cleared last
        _world.SecureChild("sprk_ruleset", RecordSetId, ("sprk_matter", Matter, MatterId))
            .SecureChild("sprk_policy", RecordRuleId, ("sprk_ruleset", "sprk_ruleset", RecordSetId))
            .SecureChild("sprk_policyversion", RecordVersionId, ("sprk_policy", "sprk_policy", RecordRuleId))
            .SecureChild("sprk_procedureassignment", AssignmentId,
                ("sprk_matter", Matter, MatterId), ("sprk_excludedpolicy", "sprk_policy", BaseRuleId));
        ProcedureRules();

        var report = await Reconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        foreach (var (table, id) in new[]
                 {
                     ("sprk_ruleset", RecordSetId), ("sprk_policy", RecordRuleId), ("sprk_policyversion", RecordVersionId),
                     ("sprk_procedureassignment", AssignmentId),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(ContentTeam), $"{table} is a hard-deny row (EB-D-168)");
        }

        report.Changes.Where(c => c.Id == RecordSetId || c.Id == RecordRuleId || c.Id == RecordVersionId || c.Id == AssignmentId)
            .Should().HaveCount(4).And.OnlyContain(c => c.Outcome == SecureChildRowOutcome.Changed && c.TargetTeamId == ContentTeam);
        AssertProcedureRulesUntouched();
    }

    [Fact(DisplayName = "089a must not fire: a Procedure rule set and a record rule set on an ordinary matter are untouched when another matter turns Secure")]
    public async Task AProcedureRuleSet_AndARecordSetOnAnOrdinaryMatter_AreNeverReached()
    {
        var ordinaryMatter = Guid.NewGuid();
        var (ordinarySet, ordinaryRule) = (Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(Matter, MatterId).OrdinaryRoot(Matter, ordinaryMatter);
        Row("sprk_ruleset", ordinarySet, TeamRef(ContentTeam), ("sprk_matter", Matter, ordinaryMatter));
        Row("sprk_policy", ordinaryRule, TeamRef(ContentTeam), ("sprk_ruleset", "sprk_ruleset", ordinarySet));
        ProcedureRules();

        var report = await MakeSecureReconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerWrites.Should().BeEmpty("nothing is filed under the Secure matter");
        _world.OwnerOf("sprk_ruleset", ordinarySet).Should().Be(DataversePrincipalRef.Team(ContentTeam));
        _world.OwnerOf("sprk_policy", ordinaryRule).Should().Be(DataversePrincipalRef.Team(ContentTeam));
        AssertProcedureRulesUntouched();
    }
}
