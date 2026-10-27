using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// ebilling-r1 task 089 (procedure model 5.1, uac-r2 conditions 1 and 6): a record's overrides are its secure family. An assignment or
/// override row (<c>sprk_procedureassignment</c>) and a record-scoped rule set (<c>sprk_ruleset</c>) are filed under their core record; a
/// record rule (<c>sprk_policy.sprk_ruleset</c>) under its set; the rule's versions under the rule. Driven over the REAL
/// <see cref="SecureChildReconciler"/>, the REAL ownership resolver and one in-memory Dataverse (<see cref="SecureChildShareWorld"/>), as
/// <see cref="SecureChildPolicyLineageTests"/>: version → policy → rule set → core follows the matter into isolation and back out, and
/// the Procedure, pinned version and excluded rule an assignment names are citations, never parents.
/// </summary>
public class SecureChildRecordOverrideLineageTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid Author = SecureChildShareWorld.SomeUser;
    private static readonly Guid OtherBu = Guid.Parse("20890000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBuTeam = Guid.Parse("20890000-0000-4000-8000-0000000000b2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Billing"))
        .Team(OtherBuTeam, OtherBu, "Billing", isDefault: true, teamType: 0);

    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    [Fact(DisplayName = "089 must-fire: a matter turning Secure takes its override row, its record rule set, the rule in it and the rule's version into isolation")]
    public async Task AMatterTurningSecure_ReownsTheVersionThePolicyTheRuleSetAndTheAssignment()
    {
        var (matter, set, rule, version, assignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild("sprk_ruleset", set, ("sprk_matter", "sprk_matter", matter))
            .UserOwnedChild("sprk_policy", rule, ("sprk_ruleset", "sprk_ruleset", set))
            .UserOwnedChild("sprk_policyversion", version, ("sprk_policy", "sprk_policy", rule))
            .UserOwnedChild("sprk_procedureassignment", assignment, ("sprk_matter", "sprk_matter", matter));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        foreach (var (table, id) in new[]
                 {
                     ("sprk_ruleset", set), ("sprk_policy", rule), ("sprk_policyversion", version), ("sprk_procedureassignment", assignment),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(SecureTeam), $"{table} is filed under the Secure matter");
        }
    }

    [Fact(DisplayName = "089 must-fire: a matter leaving isolation takes its overrides out with it, to the matter's team, not the team of what they cite")]
    public async Task AMatterLeavingIsolation_ReleasesTheChain_AndTheAssignmentsCitationsAreNotParents()
    {
        // The excluded base rule and the Procedure live in another business unit. The assignment's citation lookups come FIRST in its
        // row, so if one were read as a parent it would hand the row that unit's team (RecordOwnershipResolver.CitationLookups).
        var (matter, set, rule, version, assignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (baseRule, procedure) = (Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", matter) // mid-unsecure: moved off the Secure team, flag cleared last
            .Add("sprk_policy", baseRule, ("owningteam", new EntityReference("team", OtherBuTeam)))
            .Add("sprk_procedure", procedure, ("owningteam", new EntityReference("team", OtherBuTeam)))
            .SecureChild("sprk_ruleset", set, ("sprk_matter", "sprk_matter", matter))
            .SecureChild("sprk_policy", rule, ("sprk_ruleset", "sprk_ruleset", set))
            .SecureChild("sprk_policyversion", version, ("sprk_policy", "sprk_policy", rule))
            .SecureChild("sprk_procedureassignment", assignment,
                ("sprk_excludedpolicy", "sprk_policy", baseRule), ("sprk_procedure", "sprk_procedure", procedure), ("sprk_matter", "sprk_matter", matter));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        foreach (var (table, id) in new[]
                 {
                     ("sprk_ruleset", set), ("sprk_policy", rule), ("sprk_policyversion", version), ("sprk_procedureassignment", assignment),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.Team(GeneralTeam), $"{table} leaves isolation with its matter");
        }

        _world.OwnerOf("sprk_policy", baseRule).Should().Be(DataversePrincipalRef.Team(OtherBuTeam), "a cited base rule is never reached");
        _world.OwnerOf("sprk_procedure", procedure).Should().Be(DataversePrincipalRef.Team(OtherBuTeam), "a cited Procedure is never reached");
    }

    [Fact(DisplayName = "089 must-not-fire: a client-default rule set and its rules, and another matter's overrides, keep their owner")]
    public async Task AClientDefaultSet_AndAnOrdinaryMattersOverrides_AreUntouched()
    {
        var (secureMatter, ordinaryMatter) = (Guid.NewGuid(), Guid.NewGuid());
        var (clientSet, clientRule, ordinarySet, ordinaryAssignment) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", secureMatter)
            .OrdinaryRoot("sprk_matter", ordinaryMatter)
            .UserOwnedChild("sprk_ruleset", clientSet)
            .UserOwnedChild("sprk_policy", clientRule, ("sprk_ruleset", "sprk_ruleset", clientSet))
            .UserOwnedChild("sprk_ruleset", ordinarySet, ("sprk_matter", "sprk_matter", ordinaryMatter))
            .UserOwnedChild("sprk_procedureassignment", ordinaryAssignment, ("sprk_matter", "sprk_matter", ordinaryMatter));

        await Reconciler().ReconcileAsync(
            "sprk_matter", secureMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);
        await Reconciler().ReconcileAsync(
            "sprk_matter", ordinaryMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None);

        _world.OwnerWrites.Should().BeEmpty("nothing here is filed under a Secure matter");
        foreach (var (table, id) in new[]
                 {
                     ("sprk_ruleset", clientSet), ("sprk_policy", clientRule), ("sprk_ruleset", ordinarySet), ("sprk_procedureassignment", ordinaryAssignment),
                 })
        {
            _world.OwnerOf(table, id).Should().Be(DataversePrincipalRef.User(Author), $"{table} {id} keeps its author");
        }
    }
}
