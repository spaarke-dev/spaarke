using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using SeededRule = Spaarke.ArchTests.FieldMappingSeederSyncModeClassTests.SeededRule;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 022c (unified-access-control-r2, 2026-11-12): an access column is never a
/// field-mapping target (<see cref="RecordCreationService.AccessColumnsNeverMapped"/>: the secure flag, the Access Permission,
/// the access inheritance record, the security business unit and the container). The push and Repair skip such a rule at run
/// time; this guard keeps the rules Spaarke itself ships from naming one. The shipped rules are the provisioning seeder's
/// <c>DataverseWebApiFieldMappingSeeder.SeedProfiles</c> (the only checked-in field-mapping rule data), read from its build
/// output as <see cref="FieldMappingSeederSyncModeClassTests"/> reads them. The five names are read from the BFF's list, never
/// retyped here.
/// </summary>
/// <remarks>
/// <para><b>Maintenance procedure.</b></para>
/// <list type="bullet">
/// <item>This test fails, naming a seeded rule: the seeder ships a rule whose target is an access column. Remove the rule from
/// <c>SeedProfiles</c>. There is no allowlist: an access column changes only through the access endpoints and the stamp /
/// reconcile path, never through a mapping rule. A rule that seems to need one is a change to the access model, raised with
/// unified-access-control (ADR-028 / CLAUDE.md §6.5), not an exemption here.</item>
/// <item>A new access column: add it to <see cref="RecordCreationService.AccessColumnsNeverMapped"/> in the BFF; this guard,
/// the push and Repair all follow the one list.</item>
/// <item>Shipped rules move out of the seeder (a JSON file, solution data, a script): point <see cref="ShippedRules"/> at the
/// new source in the same change, and keep the must-fire and must-not-fire controls below passing.</item>
/// <item>The seeder assembly is not built: see <see cref="FieldMappingSeederSyncModeClassTests"/> (the BuildL2ForCosmosGuard
/// target builds it).</item>
/// </list>
/// </remarks>
public class FieldMappingSeederAccessColumnTests
{
    [Fact]
    public void NoShippedFieldMappingRule_TargetsAnAccessColumn()
    {
        var flagged = AccessColumnTargets(ShippedRules());

        Assert.True(flagged.Count == 0,
            "Shipped field-mapping rules target an access column (never a mapping target):\n" + string.Join("\n", flagged));
    }

    /// <summary>The guard reads real rules: an empty seed would pass vacuously.</summary>
    [Fact]
    public void TheShippedRules_AreRead_SoTheCheckHasTeeth()
        => Assert.NotEmpty(ShippedRules());

    /// <summary>Must fire: a rule onto the Access Permission (and, in another form, each list column) is flagged by name.</summary>
    [Fact]
    public void ARuleTargetingAnAccessColumn_IsFlagged()
    {
        var seed = RecordCreationService.AccessColumnsNeverMapped
            .Select(c => new SeededRule("Control", "sprk_event", c, 1))
            .Append(new SeededRule("Control", "sprk_todo", " SPRK_AccessPermission ", 1))
            .ToList();

        var flagged = AccessColumnTargets(seed);

        Assert.Equal(seed.Count, flagged.Count);
        Assert.Contains(flagged, m => m.StartsWith("Control: sprk_event.sprk_accesspermission ", StringComparison.Ordinal));
    }

    /// <summary>Must not fire: an ordinary target, an Assigned-To target and the regarding pair pass.</summary>
    [Fact]
    public void ARuleTargetingAnOrdinaryColumn_Passes()
    {
        var seed = new[]
        {
            new SeededRule("Control", "sprk_event", "sprk_clientreference", 1),
            new SeededRule("Control", "sprk_event", "sprk_assignedattorney1", 0),
            new SeededRule("Control", "sprk_event", "sprk_regardingrecordid", 1),
        };

        Assert.Empty(AccessColumnTargets(seed));
    }

    private static List<SeededRule> ShippedRules() => FieldMappingSeederSyncModeClassTests.SeededRules();

    /// <summary>The comparison the guard runs, through the BFF's own matcher (any case, any Web API key form).</summary>
    private static List<string> AccessColumnTargets(IEnumerable<SeededRule> rules)
        => rules
            .Where(r => FieldMappingEndpoints.IsAccessColumnNeverMapped(r.TargetField))
            .Select(r => $"{r.Profile}: {r.Table}.{r.TargetField.Trim().ToLowerInvariant()} is an access column")
            .ToList();
}
