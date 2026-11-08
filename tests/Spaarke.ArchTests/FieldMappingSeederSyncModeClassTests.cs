using System.Collections;
using System.Reflection;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (fix round 1, one Assigned-To classifier): the provisioning seeder writes each
/// seeded field-mapping rule's <c>sprk_syncmode</c> explicitly, and the value must be the one the rule's CLASS calls for, as the
/// BFF decides the class: an Assigned-To target (<see cref="PushRuleSelection.AssignedToColumnsOf"/> over the registry the BFF
/// binds) is One-time (0), anything else Manual Refresh (1). The seeder is a separate deployable with no reference to the BFF,
/// so this test asks the BFF's classifier about every seeded rule; a seeded value that disagrees fails here, naming the rule.
/// </summary>
/// <remarks>The seeder is loaded from its build output (the BuildL2ForCosmosGuard target builds it), as
/// <see cref="SecureRecordOwnerRoleSetParityTests"/> does.</remarks>
public class FieldMappingSeederSyncModeClassTests
{
    private const string L2CoreAssembly = "Sprk.Provisioning.ControlPlane.Core";
    private const string SeederType = "Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed.DataverseWebApiFieldMappingSeeder";

    [Fact]
    public void EverySeededRule_CarriesTheSyncModeOfItsClass_AsTheBffClassifiesIt()
    {
        var classify = PushRuleSelection.AssignedToColumnsOf(BoundRegistry());
        var oneTime = SeederConst("SyncModeOneTime");
        var manualRefresh = SeederConst("SyncModeManualRefresh");

        var wrong = Disagreements(SeededRules(), classify, oneTime, manualRefresh);

        Assert.True(wrong.Count == 0, "Seeded sync modes disagree with the BFF's Assigned-To classifier:\n" + string.Join("\n", wrong));
    }

    /// <summary>The comparison the guard runs: every rule whose seeded value is not the one its class calls for.</summary>
    private static List<string> Disagreements(
        IEnumerable<SeededRule> rules, Func<string, string, bool> classify, int oneTime, int manualRefresh)
        => rules
            .Where(r => r.SyncMode != (classify(r.Table, r.TargetField) ? oneTime : manualRefresh))
            .Select(r => $"{r.Profile}: {r.Table}.{r.TargetField} is seeded {r.SyncMode}, its class calls for "
                         + (classify(r.Table, r.TargetField) ? $"{oneTime} (Assigned-To)" : $"{manualRefresh} (plain data)"))
            .ToList();

    /// <summary>Must-fire control: the seed holds both classes, so the comparison above is not vacuous.</summary>
    [Fact]
    public void TheSeed_HoldsBothClasses_SoTheCheckHasTeeth()
    {
        var classify = PushRuleSelection.AssignedToColumnsOf(BoundRegistry());
        var rules = SeededRules();

        Assert.NotEmpty(rules);
        Assert.Contains(rules, r => classify(r.Table, r.TargetField));
        Assert.Contains(rules, r => !classify(r.Table, r.TargetField));
    }

    /// <summary>
    /// Must-fire and must-not-fire controls on the guard's own comparison: a seed whose values disagree with their class is
    /// caught, naming each wrong rule, and the same rules seeded with the right values pass.
    /// </summary>
    [Fact]
    public void AValueThatDisagreesWithItsClass_IsCaught_AndTheRightValuePasses()
    {
        var classify = PushRuleSelection.AssignedToColumnsOf(BoundRegistry());
        var oneTime = SeederConst("SyncModeOneTime");
        var manualRefresh = SeederConst("SyncModeManualRefresh");

        // An event Assigned-To column seeded as Manual Refresh, and a plain column seeded as One-time.
        var wrongSeed = new[]
        {
            new SeededRule("Control", "sprk_event", "sprk_assignedattorney1", manualRefresh),
            new SeededRule("Control", "sprk_event", "sprk_clientreference", oneTime),
        };
        var rightSeed = new[]
        {
            new SeededRule("Control", "sprk_event", "sprk_assignedattorney1", oneTime),
            new SeededRule("Control", "sprk_event", "sprk_clientreference", manualRefresh),
        };

        var caught = Disagreements(wrongSeed, classify, oneTime, manualRefresh);
        Assert.Equal(2, caught.Count);
        Assert.Contains(caught, m => m.StartsWith("Control: sprk_event.sprk_assignedattorney1 ", StringComparison.Ordinal));
        Assert.Contains(caught, m => m.StartsWith("Control: sprk_event.sprk_clientreference ", StringComparison.Ordinal));

        Assert.Empty(Disagreements(rightSeed, classify, oneTime, manualRefresh));
    }

    private static AccessConferringRegistry BoundRegistry()
    {
        var options = new MembershipOptions();
        new MembershipOptionsDefaults().PostConfigure(null, options);
        return options.AccessConferringRoles;
    }

    private sealed record SeededRule(string Profile, string Table, string TargetField, int SyncMode);

    private static List<SeededRule> SeededRules()
    {
        var seeder = LoadL2Core().GetType(SeederType, throwOnError: true)!;
        var profiles = (IEnumerable)seeder.GetField("SeedProfiles", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
        var result = new List<SeededRule>();
        foreach (var profile in profiles)
        {
            var name = (string)Property(profile, "ProfileName")!;
            var table = (string)Property(profile, "TargetEntityLogicalName")!;
            var syncMode = (int)Property(profile, "RuleSyncMode")!;
            foreach (var rule in (IEnumerable)Property(profile, "Rules")!)
                result.Add(new SeededRule(name, table, (string)Property(rule, "TargetField")!, syncMode));
        }

        return result;
    }

    private static int SeederConst(string name)
        => (int)LoadL2Core().GetType(SeederType, throwOnError: true)!
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;

    private static object? Property(object instance, string name)
        => instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(instance);

    private static Assembly LoadL2Core()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == L2CoreAssembly);
        if (loaded is not null)
            return loaded;

        var bin = Path.Combine(SourceScan.RepoRoot, "src", "server", "services", L2CoreAssembly, "bin");
        var candidate = Directory.Exists(bin)
            ? Directory.EnumerateFiles(bin, $"{L2CoreAssembly}.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        return candidate is not null
            ? Assembly.LoadFrom(candidate)
            : throw new FileNotFoundException(
                $"{L2CoreAssembly}.dll is not built under '{bin}'. The BuildL2ForCosmosGuard target builds it; run "
                + $"dotnet build src/server/services/{L2CoreAssembly}/ if this test runs outside that target.");
    }
}
