using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 089 (procedure model 5.2 K-2): billing data never confers access. <c>sprk_billingfirm</c> (the firm billed for a core
/// record) and <c>sprk_billingresponsible</c> (who answers for the bill) name WHO IS BILLED, not who works on or sees the record; and the
/// assignment and override tables decide which rules check an invoice, never who sees anything. So:
/// <list type="number">
///   <item>the access-conferring registry (<see cref="MembershipOptionsDefaults.CanonicalAccessConferringRegistry"/>, the seed of
///   <c>Membership:AccessConferringRoles</c>, and any appsettings that override it) holds no <c>sprk_billing*</c> column;</item>
///   <item>no grant, share, membership or external-access code names <c>sprk_billingfirm</c> or <c>sprk_billingresponsible</c>;</item>
///   <item>no external module (<c>ExternalModuleRegistry</c>, descriptors in <c>ExternalAccessModule</c>) serves the assignment, rule-set,
///   rule, version or Procedure tables.</item>
/// </list>
/// The behavioural twin is <c>MembershipResolverServiceTests.ResolveAsync_AccessConferringOnly_BillingFirmAndBillingResponsible_ConferNothing</c>.
/// Each rule has a must-fire and a must-not-fire control.
/// </summary>
public sealed class BillingLookupsConferNoAccessTests
{
    private static readonly Regex BillingColumn = new(@"^sprk_billing", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BillingLookupReference = new(
        @"\bsprk_billing(?:firm|responsible)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExternalRecordEntity = new(
        @"\bRecordEntity\s*=\s*""(?<table>[a-z_]+)""", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The tables no external module may serve (the assignment and override family, and the Procedure tables).</summary>
    internal static readonly string[] OverrideFamilyTables =
    {
        "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion", "sprk_procedure", "sprk_procedureversion",
    };

    /// <summary>The folders that decide access: grants, shares, membership, external access, the access cache.</summary>
    private static readonly string[] AccessCodeFolders =
    {
        Path.Combine("Services", "Access"),
        Path.Combine("Services", "ExternalAccess"),
        Path.Combine("Infrastructure", "ExternalAccess"),
        Path.Combine("Services", "Ai", "Membership"),
        Path.Combine("Infrastructure", "Dataverse"),
    };

    internal static List<string> RegistryProblems(IReadOnlyDictionary<string, IReadOnlyList<AccessConferringColumn>> registry) =>
        registry
            .SelectMany(e => e.Value.Where(c => BillingColumn.IsMatch(c.Field)).Select(c => $"{e.Key}.{c.Field} ({c.IdentityType})"))
            .ToList();

    internal static List<string> SourceProblems(IReadOnlyDictionary<string, string> files) =>
        files
            .SelectMany(f => BillingLookupReference.Matches(SourceScan.CodeText(f.Value.Split('\n')))
                .Select(m => $"{f.Key}: {m.Value}"))
            .ToList();

    internal static List<string> ExternalModuleProblems(IReadOnlyDictionary<string, string> files) =>
        files
            .SelectMany(f => ExternalRecordEntity.Matches(SourceScan.CodeText(f.Value.Split('\n')))
                .Where(m => OverrideFamilyTables.Contains(m.Groups["table"].Value, StringComparer.OrdinalIgnoreCase))
                .Select(m => $"{f.Key}: RecordEntity = {m.Groups["table"].Value}"))
            .ToList();

    private static string BffRoot => Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    [Fact(DisplayName = "Task 089 K-2: the access-conferring registry holds no billing column")]
    public void TheAccessConferringRegistryHoldsNoBillingColumn()
    {
        var registry = MembershipOptionsDefaults.CanonicalAccessConferringRegistry;
        Assert.Contains("sprk_matter", registry.Keys); // not vacuous: the core records are in it
        var problems = RegistryProblems(registry);
        Assert.True(problems.Count == 0, "Billing columns in the access-conferring registry (K-2): " + string.Join(", ", problems));

        // An appsettings override of Membership:AccessConferringRoles must not add one either.
        var settings = Directory.EnumerateFiles(BffRoot, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Where(f => File.ReadAllText(f).Contains("AccessConferringRoles", StringComparison.Ordinal)
                        && Regex.IsMatch(File.ReadAllText(f), @"""sprk_billing", RegexOptions.IgnoreCase))
            .Select(SourceScan.Relative)
            .ToList();
        Assert.True(settings.Count == 0, "appsettings that list a billing column as access-conferring (K-2): " + string.Join(", ", settings));
    }

    [Fact(DisplayName = "Task 089 K-2: no grant, share, membership or external-access code names the billing lookups")]
    public void NoAccessCodeNamesTheBillingLookups()
    {
        var files = AccessCodeFolders
            .Select(folder => Path.Combine(BffRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            .ToDictionary(SourceScan.Relative, File.ReadAllText);
        Assert.True(files.Count > 20, $"Only {files.Count} access files were scanned; the guard would be vacuous.");

        var problems = SourceProblems(files);
        Assert.True(problems.Count == 0, "Access code names a billing lookup (K-2): " + string.Join("; ", problems));
    }

    [Fact(DisplayName = "Task 089 K-2: no external module serves an assignment, rule-set, rule, version or Procedure table")]
    public void NoExternalModuleServesTheOverrideFamily()
    {
        var files = SourceScan.ServerSourceFiles().ToDictionary(SourceScan.Relative, File.ReadAllText);
        var served = files.SelectMany(f => ExternalRecordEntity.Matches(f.Value)).Select(m => m.Groups["table"].Value).ToList();
        Assert.Contains("sprk_project", served); // not vacuous: the descriptors are where the scan looks

        var problems = ExternalModuleProblems(files);
        Assert.True(problems.Count == 0, "External modules serving the override family (K-2): " + string.Join("; ", problems));
    }

    [Fact(DisplayName = "Task 089 K-2: controls - each rule fires on a planted billing grant and not on the clean shapes")]
    public void Controls()
    {
        // Registry. Must fire: a billing column on a core table. Must not fire: the assigned-firm column.
        var planted = new Dictionary<string, IReadOnlyList<AccessConferringColumn>>
        {
            ["sprk_matter"] = new[]
            {
                new AccessConferringColumn { Field = "sprk_assignedlawfirm1", IdentityType = "Organization" },
                new AccessConferringColumn { Field = "sprk_billingfirm", IdentityType = "Organization" },
            },
        };
        Assert.Equal(new[] { "sprk_matter.sprk_billingfirm (Organization)" }, RegistryProblems(planted));
        Assert.Empty(RegistryProblems(new Dictionary<string, IReadOnlyList<AccessConferringColumn>>
        {
            ["sprk_matter"] = new[] { new AccessConferringColumn { Field = "sprk_assignedlawfirm1", IdentityType = "Organization" } },
        }));

        // Source. Must fire: a grant that reads the billing responsible. Must not fire: a comment, or another column.
        Assert.Single(SourceProblems(new Dictionary<string, string>
        {
            ["Grant.cs"] = "var who = record.GetAttributeValue<EntityReference>(\"sprk_billingresponsible\");",
        }));
        Assert.Empty(SourceProblems(new Dictionary<string, string>
        {
            ["Grant.cs"] = "// sprk_billingfirm never confers access\nvar who = record.GetAttributeValue<EntityReference>(\"sprk_assignedlawfirm1\");",
        }));

        // External modules. Must fire: a descriptor serving rule sets. Must not fire: the project module.
        Assert.Single(ExternalModuleProblems(new Dictionary<string, string>
        {
            ["Module.cs"] = "new ExternalModuleDescriptor { RecordEntity = \"sprk_ruleset\" };",
        }));
        Assert.Empty(ExternalModuleProblems(new Dictionary<string, string>
        {
            ["Module.cs"] = "new ExternalModuleDescriptor { RecordEntity = \"sprk_project\" };",
        }));
    }
}
