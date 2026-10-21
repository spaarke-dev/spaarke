using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// unified-access-control-r2 #2084 (owner round 102 item 4, agreed with spaarke-ontology-platform-r1): a rule scoped to a matter
/// (<c>sprk_policy.sprk_matter</c>) and its versions (<c>sprk_policyversion.sprk_policy</c>) are the matter's secure family. Driven over
/// the REAL <see cref="SecureChildReconciler"/>, the REAL ownership resolver and one in-memory Dataverse
/// (<see cref="SecureChildShareWorld"/>): when the matter turns Secure the rule and its versions follow it; a rule with no matter, and
/// a rule on an ordinary matter, keep their owner; a Signal is never filed under the rule it cites.
/// </summary>
public class SecureChildPolicyLineageTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid Author = SecureChildShareWorld.SomeUser;
    private static readonly Guid OtherBu = Guid.Parse("20840000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBuTeam = Guid.Parse("20840000-0000-4000-8000-0000000000b2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Litigation"))
        .Team(OtherBuTeam, OtherBu, "Litigation", isDefault: true, teamType: 0);

    /// <summary>A matter root's pass reads its Assign cascade (document locations) over the Web API; this one has none.</summary>
    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    [Fact(DisplayName = "#2084 must-fire: a matter turning Secure takes its scoped rule and the rule's versions into isolation")]
    public async Task AMatterTurningSecure_ReownsItsScopedRule_AndTheRulesVersions()
    {
        var (matter, rule, v1, v2) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild("sprk_policy", rule, ("sprk_matter", "sprk_matter", matter))
            .UserOwnedChild("sprk_policyversion", v1, ("sprk_policy", "sprk_policy", rule))
            .UserOwnedChild("sprk_policyversion", v2, ("sprk_policy", "sprk_policy", rule));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_policy", rule).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _world.OwnerOf("sprk_policyversion", v1).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _world.OwnerOf("sprk_policyversion", v2).Should().Be(DataversePrincipalRef.Team(SecureTeam));
    }

    [Fact(DisplayName = "#2084 must-not-fire: a rule with no matter, and a rule on another (ordinary) matter, keep their owner")]
    public async Task ARuleWithNoMatter_AndARuleOnAnOrdinaryMatter_AreUntouched()
    {
        var (secureMatter, ordinaryMatter) = (Guid.NewGuid(), Guid.NewGuid());
        var (unscoped, unscopedVersion, onOrdinary, onOrdinaryVersion) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", secureMatter)
            .OrdinaryRoot("sprk_matter", ordinaryMatter)
            .UserOwnedChild("sprk_policy", unscoped)
            .UserOwnedChild("sprk_policyversion", unscopedVersion, ("sprk_policy", "sprk_policy", unscoped))
            .UserOwnedChild("sprk_policy", onOrdinary, ("sprk_matter", "sprk_matter", ordinaryMatter))
            .UserOwnedChild("sprk_policyversion", onOrdinaryVersion, ("sprk_policy", "sprk_policy", onOrdinary));

        await Reconciler().ReconcileAsync(
            "sprk_matter", secureMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);
        await Reconciler().ReconcileAsync(
            "sprk_matter", ordinaryMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None);

        _world.OwnerWrites.Should().BeEmpty("neither rule is filed under a Secure matter");
        foreach (var (table, id) in new[]
                 {
                     ("sprk_policy", unscoped), ("sprk_policyversion", unscopedVersion),
                     ("sprk_policy", onOrdinary), ("sprk_policyversion", onOrdinaryVersion),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.User(Author), $"{table} {id} keeps its author");
        }
    }

    [Fact(DisplayName = "#2084: a Signal is never filed under the rule it cites — leaving isolation it takes its matter's team, not the rule's")]
    public async Task ASignalLeavingIsolation_TakesItsMattersTeam_NotTheTeamOfTheRuleItCites()
    {
        // An all-matters rule owned in another business unit. The Signal's rule lookup comes FIRST in its row, so if it were read
        // as a parent it would be the primary one and hand the Signal that unit's team (RecordOwnershipResolver.IsCitation).
        var (matter, rule, signal) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", matter) // mid-unsecure: moved off the Secure team, flag cleared last
            .Add("sprk_policy", rule, ("owningteam", new EntityReference("team", OtherBuTeam)))
            .SecureChild("sprk_signal", signal, ("sprk_policy", "sprk_policy", rule), ("sprk_matter", "sprk_matter", matter));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_signal", signal).Should().Be(DataversePrincipalRef.Team(GeneralTeam),
            "the Signal is filed under its matter; the rule it cites is not a parent");
        _world.OwnerOf("sprk_policy", rule).Should().Be(DataversePrincipalRef.Team(OtherBuTeam), "an unscoped rule is never reached");
    }
}
