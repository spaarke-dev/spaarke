// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `data-mutation`
//   - Path:     `tests/integration/data-mutation/ExternalAccess/**`
//   - Justification: UAC task 194b R4b-2 (owner round 131, the 086 ruling) — the reconciler's side of the hard-deny tables. An
//     Unsecure moves a team-owned hard-deny row to the restricted-content owner team (read back, counted moved); the ordinary
//     pass expects that team for team-owned rows (no I-6 report) and keeps its report on a user-owned (author-owned) row as it
//     was; Make Secure is unchanged; an unusable team refuses, and the pass reports the member check. R4b-2c (owner round 136):
//     the team is the customer top unit's (the root's direct child above the answer); an answer in the root refuses; a row the
//     pass never moves (user-owned, a non-default team) is reported, not refused, when the team cannot be used (F2).
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
    private static readonly Guid CustomerBu = SecureChildShareWorld.CustomerBu;
    private static readonly Guid CustomerTeam = SecureChildShareWorld.CustomerTeam;

    /// <summary>R4b-2c: a second customer top unit (a direct child of the root) with NO restricted-content owner team, and its
    /// default team and a non-default owner team.</summary>
    private static readonly Guid BareCustomerBu = Guid.Parse("0c000000-0000-4000-8000-000000194c01");
    private static readonly Guid BareCustomerTeam = Guid.Parse("0c000000-0000-4000-8000-000000194c02");
    private static readonly Guid BareNonDefaultTeam = Guid.Parse("0c000000-0000-4000-8000-000000194c03");

    /// <summary>Business unit B (under the customer's top unit), where the matter was reassigned, and its default team.</summary>
    private static readonly Guid BuB = Guid.Parse("0b000000-0000-4000-8000-000000194b01");
    private static readonly Guid TeamB = Guid.Parse("0b000000-0000-4000-8000-000000194b02");

    private static readonly Guid MatterId = Guid.Parse("1b000000-0000-4000-8000-000000194b10");
    private static readonly Guid PolicyId = Guid.Parse("1b000000-0000-4000-8000-000000194b11");
    private static readonly Guid VersionId = Guid.Parse("1b000000-0000-4000-8000-000000194b12");
    private static readonly Guid Author = Guid.Parse("5e000000-0000-4000-8000-000000194b20");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", BuB, ("name", "Litigation"), ("parentbusinessunitid", new EntityReference("businessunit", CustomerBu)))
        .Team(TeamB, BuB, "Litigation", isDefault: true, teamType: 0)
        .Add("businessunit", BareCustomerBu, ("name", "Customer Two"), ("parentbusinessunitid", new EntityReference("businessunit", GeneralBu)))
        .Team(BareCustomerTeam, BareCustomerBu, "Customer Two", isDefault: true, teamType: 0)
        .Team(BareNonDefaultTeam, BareCustomerBu, "Customer Two Paralegals", isDefault: false, teamType: 0);

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

    /// <summary>The matter, ordinary, reassigned to <paramref name="team"/>'s business unit.</summary>
    private void MatterMovedTo(Guid team) =>
        _world.Add(Matter, MatterId, ("owningteam", TeamRef(team)), ("sprk_issecure", false));

    /// <summary>Mid-unsecure: the matter is owned by the customer's default team again, its flag cleared last.</summary>
    private void MatterBeingUnsecured() =>
        _world.Add(Matter, MatterId, ("owningteam", TeamRef(CustomerTeam)), ("sprk_issecure", true));

    // ── Unsecure ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-2 must fire: an Unsecure moves an isolated policy and its version to the restricted-content team, read back and counted")]
    public async Task AnUnsecure_MovesIsolatedHardDenyRows_ToTheRestrictedContentTeam()
    {
        MatterBeingUnsecured();
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
        report.RestrictedContentOwnerTeams.Should().ContainSingle().Which.Should().Match<RestrictedContentOwnerTeamIdentity>(t =>
            t.Status == RestrictedContentOwnerTeamStatus.Resolved && t.CustomerBusinessUnitId == CustomerBu && t.MembersFound == 0,
            "the pass reports the customer unit's team and its member check");
    }

    [Fact(DisplayName = "R4b-2 must not fire: an Unsecure leaves an isolated policy isolated when the restricted-content team has a member — refused, and the report names the member check")]
    public async Task AnUnsecure_WhenTheRestrictedContentTeamHasAMember_RefusesAndLeavesTheRowIsolated()
    {
        MatterBeingUnsecured();
        _world.SecureChild("sprk_policy", PolicyId, ("sprk_matter", Matter, MatterId));
        _world.Add("teammembership", Guid.NewGuid(), ("teamid", ContentTeam), ("systemuserid", SecureChildShareWorld.SomeUser));

        var report = await Reconciler().ReconcileAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(_world.OwnerTeam), "fail closed: it stays isolated");
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Refused
            && c.Detail!.Contains(Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.RestrictedContentOwnerTeamUnresolved));
        report.RestrictedContentOwnerTeams.Should().ContainSingle().Which.Should().Match<RestrictedContentOwnerTeamIdentity>(t =>
            t.Status == RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers && t.MembersFound == 1);
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

    // ── R4b-4 ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-4 A-F1 must fire: a fault reading the replaced default team fails THAT user-owned policy only — the pass returns, the default-team policy beside it still moves")]
    public async Task TheOrdinaryPass_WhenTheReplacedDefaultTeamCannotBeRead_FailsThatRowOnly()
    {
        var defaultOwned = Guid.NewGuid();
        MatterMovedToB();
        _world.User(Author, GeneralBu);
        Row("sprk_policy", PolicyId, new EntityReference("systemuser", Author), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", defaultOwned, TeamRef(GeneralTeam), ("sprk_matter", Matter, MatterId));
        _world.FailingRowReadsOf("team", TeamB);

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete, "the failed row carries the root");
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Failed && c.Detail == "its owner could not be decided");
        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.User(Author), "a failed row is left as it is");
        _world.OwnerOf("sprk_policy", defaultOwned).Should().Be(DataversePrincipalRef.Team(ContentTeam),
            "the other rows of the pass are still decided");
        report.Changes.Should().ContainSingle(c => c.Id == defaultOwned).Which.Outcome.Should().Be(SecureChildRowOutcome.Changed);
    }

    [Fact(DisplayName = "R4b-4 A-K1 must fire: a policy owned by a non-default team in the answer's own unit is reported by id as a hard-deny row whose owner is not the restricted-content team — never moved")]
    public async Task TheOrdinaryPass_ReportsAPolicyOwnedByANonDefaultTeam_ByIdOnly()
    {
        const string TeamName = "Litigation Paralegals";
        var nonDefault = Guid.NewGuid();
        var elsewhere = Guid.NewGuid();
        var elsewherePolicy = Guid.NewGuid();
        MatterMovedToB();
        _world.Team(nonDefault, BuB, TeamName, isDefault: false, teamType: 0)
            .Team(elsewhere, GeneralBu, TeamName + " Two", isDefault: false, teamType: 0)
            .Add("teammembership", Guid.NewGuid(), ("teamid", nonDefault), ("systemuserid", SecureChildShareWorld.SomeUser));
        Row("sprk_policy", PolicyId, TeamRef(nonDefault), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", elsewherePolicy, TeamRef(elsewhere), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, "a row this pass never moves is not work left undone");
        _world.OwnerWrites.Should().BeEmpty("the pass moves default teams only");
        foreach (var (policy, team) in new[] { (PolicyId, nonDefault), (elsewherePolicy, elsewhere) })
        {
            report.Changes.Should().ContainSingle(c => c.Id == policy).Which.Should().Match<SecureChildRowChange>(c =>
                c.Outcome == SecureChildRowOutcome.Untouched
                && c.Detail!.Contains($"owned by team {team:D}")
                && c.Detail.Contains($"not the restricted-content owner team ({ContentTeam:D})")
                && !c.Detail.Contains(TeamName));
        }
    }

    [Fact(DisplayName = "R4b-4 A-K1 must not fire: a non-default-team row of an ORDINARY table in the answer's unit is still not reported")]
    public async Task TheOrdinaryPass_LeavesANonDefaultTeamEventInTheAnswersUnit_Unreported()
    {
        var nonDefault = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        MatterMovedToB();
        _world.Team(nonDefault, BuB, "Litigation Paralegals", isDefault: false, teamType: 0);
        Row("sprk_event", eventId, TeamRef(nonDefault), ("sprk_regardingmatter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, report.Detail);
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().NotContain(c => c.Id == eventId);
    }

    // ── R4b-2c: the customer's top unit ──────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "R4b-2c F2 must not fire: with no usable team in the customer's unit, the ordinary pass leaves an author-owned and a non-default-team policy, reports them, and COMPLETES")]
    public async Task TheOrdinaryPass_WithNoUsableTeam_LeavesRowsItNeverMoves_AndCompletes()
    {
        var nonDefault = Guid.NewGuid();
        MatterMovedTo(BareCustomerTeam);
        _world.User(Author, BareCustomerBu);
        Row("sprk_policy", PolicyId, new EntityReference("systemuser", Author), ("sprk_matter", Matter, MatterId));
        Row("sprk_policy", nonDefault, TeamRef(BareNonDefaultTeam), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, "a row this pass never moves is not work left undone");
        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.User(Author));
        _world.OwnerOf("sprk_policy", nonDefault).Should().Be(DataversePrincipalRef.Team(BareNonDefaultTeam));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Where(c => c.Id == PolicyId || c.Id == nonDefault).Should().HaveCount(2).And.OnlyContain(c =>
            c.Outcome == SecureChildRowOutcome.Untouched && c.Detail!.Contains("cannot be used")
            && c.Detail.Contains(nameof(RestrictedContentOwnerTeamStatus.OwnerTeamNotFound)));
        report.Changes.Single(c => c.Id == PolicyId).Detail.Should().Contain("owned by a user").And.Contain("I-6");
        report.RestrictedContentOwnerTeams.Should().ContainSingle().Which.Should().Match<RestrictedContentOwnerTeamIdentity>(t =>
            t.Status == RestrictedContentOwnerTeamStatus.OwnerTeamNotFound && t.CustomerBusinessUnitId == BareCustomerBu);
    }

    [Fact(DisplayName = "R4b-2c F2 must fire: with no usable team in the customer's unit, a DEFAULT-team policy is refused — nothing written, the pass incomplete")]
    public async Task TheOrdinaryPass_WithNoUsableTeam_RefusesADefaultTeamPolicy()
    {
        MatterMovedTo(BareCustomerTeam);
        Row("sprk_policy", PolicyId, TeamRef(CustomerTeam), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete);
        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(CustomerTeam), "never the default team");
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Outcome.Should().Be(SecureChildRowOutcome.Refused);
    }

    [Fact(DisplayName = "R4b-2c must fire: a policy whose answer is the ROOT business unit is refused — customer records never go to the root")]
    public async Task TheOrdinaryPass_RefusesAPolicyWhoseAnswerIsTheRoot()
    {
        MatterMovedTo(GeneralTeam);
        Row("sprk_policy", PolicyId, TeamRef(TeamB), ("sprk_matter", Matter, MatterId));

        var report = await Reconciler().ReconcileOrdinaryParentMoveAsync(
            Matter, MatterId, SecureChildReconcileMode.Apply, maxWrites: 200, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Incomplete);
        _world.OwnerOf("sprk_policy", PolicyId).Should().Be(DataversePrincipalRef.Team(TeamB));
        _world.OwnerWrites.Should().BeEmpty();
        report.Changes.Should().ContainSingle(c => c.Id == PolicyId).Which.Should().Match<SecureChildRowChange>(c =>
            c.Outcome == SecureChildRowOutcome.Refused
            && c.Detail!.Contains(nameof(RestrictedContentOwnerTeamStatus.AnswerIsRootBusinessUnit)));
        report.RestrictedContentOwnerTeams.Should().ContainSingle().Which.CustomerBusinessUnitId.Should().BeNull();
    }
}
