using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 #2084 round 2: a Signal CITES the rule that raised it (<c>sprk_signal.sprk_policy</c>) and is never
/// filed under it. The create path derives a new row's parents through the same table-keyed predicate the reconciler and the
/// resolver apply (<see cref="RecordOwnershipResolver.IsCitation"/>), so a direct <c>sprk_signal</c> create binding a rule held by
/// a Secure matter's team takes its owner from its core matter only. Driven through the REAL <c>dataverse.create_record</c> handler
/// and the REAL resolver.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid SecureHeldRule = Guid.Parse("a2084000-0000-4000-8000-0000000000a1");

    [Fact]
    public async Task CreateRecord_ASignalCitingARuleHeldSecure_IsOwnedByItsOrdinaryMattersTeam_NeverFiledUnderTheRule()
    {
        // Must fire: the rule is held by the Secure Record Owners team. Read as a parent, secure-if-any would hand an ordinary
        // matter's Signal to that team.
        _world.WithRecord("sprk_policy", SecureHeldRule, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam);
        _user.Held.UnionWith(["prvCreatesprk_signal", "prvAppendsprk_signal"]);

        var result = await CreateRecord("sprk_signal",
            Lookup("sprk_matter", "sprk_matter", OrdinaryMatter),
            Lookup("sprk_policy", "sprk_policy", SecureHeldRule));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var (table, _, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_signal");
        Owner(fields).Should().Be(Directory.ChildTeam, "the Signal is filed under its matter; the rule it cites files nothing");
        Owner(fields).Should().NotBe(Directory.SecureNamedTeam);
        Bind(fields, "sprk_Policy@odata.bind").Should().Be($"/sprk_policies({SecureHeldRule:D})", "the citation itself is still written");
    }
}
