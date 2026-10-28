// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194b R4b-2 (owner round 131, the 086 ruling) — the reconciler's side of the hard-deny tables. An
//     Unsecure moves a team-owned hard-deny row to the restricted-content owner team (read back, counted moved); the ordinary
//     pass expects that team for team-owned rows (no I-6 report) and keeps its report on a user-owned (author-owned) row as it
//     was; Make Secure is unchanged; an unusable team refuses, and the pass reports the member check.
//
// Doubles are module boundaries only (ADR-038 §4): the REAL reconciler and resolver over task 148's world (SecureChildShareWorld).

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

[Trait("status", "task-194b-uac-r2")]
public class RestrictedContentReconcileTests
{
    private const string Matter = "sprk_matter";

    private static readonly Guid GeneralBu = SecureChildShareWorld.GeneralBu;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid ContentTeam = SecureChildShareWorld.RestrictedContentTeam;

    /// <summary>Business unit B (under the root), where the matter was reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000194b01");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000194b02");

    private static readonly Guid MatterId = Guid.Parse("1b000000-0000-4000-8000-000000194b10");
    private static readonly Guid PolicyId = Guid.Parse("1b000000-0000-4000-8000-000000194b11");
    private static readonly Guid VersionId = Guid.Parse("1b000000-0000-4000-8000-000000194b12");
    private static readonly Guid Author = Guid.Parse("5e000000-0000-4000-8000-000000194b20");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"), ("parentbusinessunitid", new EntityReference("businessunit", GeneralBu)))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0);

    private readonly FakeRecordShareTable _shares = new();
    private readonly SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi _webApi;

    public RestrictedContentReconcileTests() => _webApi = new SecureChildOrdinaryParentMoveTests.ConditionalOwnerWebApi(() => _world);

    private SecureChildReconciler Reconciler() => SecureChildShareWorld.ReconcilerOver(() => _world, _shares, _webApi);

    private static EntityReference TeamRef(Guid id) => new("team", id);

    private void Row(string table, Guid id, EntityReference owner, params (string Column, string Target, Guid TargetId)[] lookups) =>
        _world.Add(table, id, lookups.Select(l => (l.Column, (object)new EntityReference(l.Target, l.TargetId)))
            .Prepend((owner.LogicalName == "team" ? "owningteam" : "owninguser", owner))
            .Append(("versionnumber", (object)1L))
            .ToArray());

    private void MatterMovedToB() =>
        _world.Add(Matter, MatterId, ("owningteam", TeamRef(TeamB)), ("sprk_issecure", false));

    // ── Unsecure ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-2 must fire: an Unsecure moves an isolated policy and its version to the restricted-content team, read back and counted")]
    public async Task AnUnsecure_MovesIsolatedHardDenyRows_ToTheRestrictedContentTeam()
    {
        _world.FlaggedNotIsolatedRoot(Matter, MatterId);
        _world.SecureChild("sprk_policy", PolicyId, ("sprk_matter", Matter, MatterId));
        _world.SecureChild("sprk_policyversion", VersionId, ("sprk_policy", "sprk_policy", PolicyId));

        var report = await Reconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(ContentTeam));
        _world.OwnerOf("sprk_policyversion", VersionId).Should().Be(DataversePrincipalRef.Team(ContentTeam));
        _world.OwnerWrites.Select(w => w.Id).Should().Contain(new[] { PolicyId, VersionId }).And.NotContain(MatterId);
        report.Changes.Where(c => c.Id == PolicyId || c.Id == VersionId).Should().OnlyContain(c =>
            c.Outcome == SecureChildRowOutcome.Changed && c.TargetTeamId == ContentTeam);
        report.ChildrenReowned.Should().Be(2);
        report.RestrictedContentOwnerTeam.Should().NotBeNull();
        report.RestrictedContentOwnerTeam!.Status.Should().Be(RestrictedContentOwnerTeamStatus.Resolved);
        report.RestrictedContentOwnerTeam.MembersFound.Should().Be(0, "the pass reports the member check");
    }

    [Fact(DisplayName = "R4b-2 must not fire: an Unsecure leaves an isolated policy isolated when the restricted-content team has a member — refused, and the report names the member check")]
    public async Task AnUnsecure_WhenTheRestrictedContentTeamHasAMember_RefusesAndLeavesTheRowIsolated()
    {
        _world.FlaggedNotIsolatedRoot(Matter, MatterId);
        _world.SecureChild("sprk_policy", PolicyId, ("sprk_matter", Matter, MatterId));
        _world.Add("teammembership", Guid.NewGuid(), ("teamid", ContentTeam), ("systemuserid", SecureChildShareWorld.SomeUser));

        var report = await Reconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam), "fail closed: it stays isolated");
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Refused
            && c.Detail!.Contains(Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved));
        report.RestrictedContentOwnerTeam!.Status.Should().Be(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers);
        report.RestrictedContentOwnerTeam.MembersFound.Should().Be(1);
    }

    // ── Make Secure ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-2: Make Secure (provisioning) is unchanged — a restricted-content policy under a secured matter moves into isolation")]
    public async Task AMakeSecure_MovesARestrictedContentPolicyIntoIsolation()
    {
        _world.SecureRoot(Matter, MatterId);
        Row("sprk_policy", PolicyId, TeamRef(ContentTeam), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam));
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    // ── The ordinary pass ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-2 must not fire: the ordinary pass expects the restricted-content team for a team-owned policy — AlreadyCorrect, nothing written, nothing reported")]
    public async Task TheOrdinaryPass_ExpectsTheRestrictedContentTeam()
    {
        MatterMovedToB();
        Row("sprk_policy", PolicyId, TeamRef(ContentTeam), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(ContentTeam));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().NotContain(c => c.Id == PolicyId, "no I-6 report: it is where the rule puts it");
    }

    [Fact(DisplayName = "R4b-2 must fire: the ordinary pass moves a policy still owned by a business unit's default team to the restricted-content team")]
    public async Task TheOrdinaryPass_MovesADefaultTeamPolicy_ToTheRestrictedContentTeam()
    {
        MatterMovedToB();
        Row("sprk_policy", PolicyId, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(ContentTeam), "never B's default team");
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.TargetTeamId.Should().Be(ContentTeam);
    }

    [Fact(DisplayName = "R4b-2 rule 1: the ordinary pass never moves an author-owned policy, and reports it exactly as before (silent in the matter's unit)")]
    public async Task TheOrdinaryPass_LeavesAnAuthorOwnedPolicy_AndReportsItAsBefore()
    {
        var inOtherUnit = Guid.NewGuid();
        var otherAuthor = Guid.NewGuid();
        MatterMovedToB();
        _world.User(Author, BuB).User(otherAuthor, GeneralBu);
        Row("sprk_policy", PolicyId, new EntityReference("systemuser", Author), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", inOtherUnit, new EntityReference("systemuser", otherAuthor), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.User(Author), "a kept creator is never replaced");
        _world.OwnerOf("sprk_policy", inOtherUnit).Should().Be(DataversePrincipalRef.User(otherAuthor));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().NotContain(c => c.Id == PolicyId,
            "its author is in the matter's unit, the unit of the ordinary answer it had before (B): not reported, as before");
        report.Changes.Should().ContainSingle(c => c.Id == inOtherUnit).Which.Detail.Should().Contain("owned by a user",
            "an author in another unit is reported, as before");
    }
}
