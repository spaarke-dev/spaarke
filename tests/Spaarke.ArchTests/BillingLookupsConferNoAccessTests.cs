using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Membership;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 089a (procedure model 5.2, K-2): a core record's <c>sprk_billingfirm</c> and <c>sprk_billingresponsible</c>
/// are organization lookups the eBilling routing reads. They name who bills a record and who pays, never who may read it, so:
/// <list type="number">
///   <item>the access-conferring registry (<c>MembershipOptionsDefaults.CanonicalAccessConferringRegistry</c>, and any operator
///   <c>Membership:AccessConferringRoles</c> in the BFF's appsettings) holds no <c>sprk_billing*</c> column;</item>
///   <item>no grant or share path (external access, the secure-child share mirror, membership resolution) names either column;</item>
///   <item>the external module registry (<c>ExternalAccessModule</c>'s <c>RecordEntity</c> descriptors) gains none of the
///   procedure-model tables, so no external route serves an assignment, a rule set, a rule, a Procedure or a stored file.</item>
/// </list>
/// The behavioural half (AccessConferringOnly resolution confers nothing through either column) is
/// <c>MembershipResolverServiceTests.ResolveAsync_AccessConferringOnly_BillingFirmAndBillingResponsible_ConferNothing</c> in the
/// unit suite (Moq is not available here).
/// </summary>
/// <remarks>Source scans are crude by design, like every <see cref="SourceScan"/> rule; each has a must-fire control.</remarks>
public sealed class BillingLookupsConferNoAccessTests
{
    private static readonly string[] CoreTables = { "sprk_matter", "sprk_project", "sprk_workassignment" };

    private static readonly Regex BillingColumn = new(@"\bsprk_billing\w*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BillingLookup = new(@"\bsprk_billing(?:firm|responsible)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RecordEntity = new(@"\bRecordEntity\s*=\s*""(?<table>[a-z_0-9]+)""", RegexOptions.Compiled);

    /// <summary>The tables of the procedure model that no external module may expose (089a and the tables it builds on).</summary>
    private static readonly string[] ProcedureModelTables =
    {
        "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion", "sprk_procedure",
        "sprk_procedureversion", "sprk_storedfile",
    };

    /// <summary>The folders under <c>Sprk.Bff.Api</c> that grant, share or resolve access.</summary>
    private static readonly string[] GrantAndSharePaths =
    {
        Path.Combine("Infrastructure", "ExternalAccess"),
        Path.Combine("Services", "ExternalAccess"),
        Path.Combine("Services", "Access"),
        Path.Combine("Services", "Ai", "Membership"),
    };

    private static string BffRoot => Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    internal static IReadOnlyList<string> BillingColumnsIn(IReadOnlyDictionary<string, IReadOnlyList<AccessConferringColumn>> registry) =>
        registry
            .SelectMany(entry => entry.Value.Select(column => (Entity: entry.Key, column.Field)))
            .Where(c => BillingColumn.IsMatch(c.Field))
            .Select(c => $"{c.Entity}.{c.Field}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    internal static IReadOnlyList<string> BillingLookupsIn(IReadOnlyDictionary<string, string> codeByFile) =>
        codeByFile
            .SelectMany(file => BillingLookup.Matches(file.Value).Select(m => $"{file.Key}:{SourceScan.LineOf(file.Value, m.Index)}: {m.Value}"))
            .ToList();

    internal static IReadOnlyList<string> RecordEntitiesIn(string code) =>
        RecordEntity.Matches(code).Select(m => m.Groups["table"].Value).ToList();

    // ── 1. The access-conferring registry ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "089a K-2: the canonical access-conferring registry holds no sprk_billing* column, on a core table or any other")]
    public void TheCanonicalRegistryHoldsNoBillingColumn()
    {
        var registry = MembershipOptionsDefaults.CanonicalAccessConferringRegistry;

        // Not vacuous: the registry is the one the resolver seeds, with every core table in it.
        Assert.All(CoreTables, t => Assert.True(registry.ContainsKey(t), $"{t} is not in the registry; the guard would be vacuous"));

        var found = BillingColumnsIn(registry);
        Assert.True(found.Count == 0,
            "A billing column confers access (procedure model 5.2, K-2: it names who bills the record, not who may read it): "
            + string.Join(", ", found));
    }

    [Fact(DisplayName = "089a K-2: control — a billing column in a registry fires (must fire); the law-firm columns do not (must not fire)")]
    public void BillingColumnsIn_Controls()
    {
        var seeded = new Dictionary<string, IReadOnlyList<AccessConferringColumn>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = new[]
            {
                new AccessConferringColumn { Field = "sprk_assignedlawfirm1", IdentityType = "Organization" },
                new AccessConferringColumn { Field = "sprk_BillingFirm", IdentityType = "Organization" },
            },
            ["sprk_project"] = new[] { new AccessConferringColumn { Field = "sprk_billingresponsible", IdentityType = "Organization" } },
            ["sprk_workassignment"] = new[] { new AccessConferringColumn { Field = "sprk_assignedlawfirm2", IdentityType = "Organization" } },
        };

        Assert.Equal(new[] { "sprk_matter.sprk_BillingFirm", "sprk_project.sprk_billingresponsible" }, BillingColumnsIn(seeded));
        Assert.Empty(BillingColumnsIn(new Dictionary<string, IReadOnlyList<AccessConferringColumn>>
        {
            ["sprk_matter"] = new[] { new AccessConferringColumn { Field = "sprk_assignedlawfirm1", IdentityType = "Organization" } },
        }));
    }

    [Fact(DisplayName = "089a K-2: no BFF appsettings file names a billing lookup (an operator registry replaces the seed)")]
    public void NoAppSettingsNamesABillingLookup()
    {
        var settings = Directory.EnumerateFiles(BffRoot, "appsettings*.json", SearchOption.TopDirectoryOnly).ToList();
        Assert.NotEmpty(settings);

        var found = BillingLookupsIn(settings.ToDictionary(f => Path.GetFileName(f), f => File.ReadAllText(f)));
        Assert.True(found.Count == 0,
            "A BFF settings file names a billing lookup; Membership:AccessConferringRoles must never list one: " + string.Join("; ", found));
    }

    // ── 2. Grant and share paths ─────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "089a K-2: no grant, share or membership path names sprk_billingfirm or sprk_billingresponsible")]
    public void NoGrantOrSharePathNamesABillingLookup()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var folder in GrantAndSharePaths)
        {
            var root = Path.Combine(BffRoot, folder);
            Assert.True(Directory.Exists(root), $"{folder} was not found; the guard would be vacuous.");
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                files[SourceScan.Relative(path)] = SourceScan.CodeText(File.ReadAllLines(path));
            }
        }

        Assert.True(files.Count > 50, $"only {files.Count} files scanned; the guard would be vacuous.");

        var found = BillingLookupsIn(files);
        Assert.True(found.Count == 0,
            "A grant, share or membership path reads a billing lookup (procedure model 5.2, K-2): " + string.Join("; ", found));
    }

    [Fact(DisplayName = "089a K-2: control — a planted billing lookup in code fires (must fire); a comment and another column do not (must not fire)")]
    public void BillingLookupsIn_Controls()
    {
        var planted = SourceScan.CodeText(new[]
        {
            "class Grants {",
            "    // sprk_billingfirm is never read here (stripped)",
            "    void A(Entity e) { var firm = e.GetAttributeValue<EntityReference>(\"sprk_billingfirm\"); }",
            "    const string Pays = \"sprk_BillingResponsible\";",
            "    void B(Entity e) { var lawFirm = e.GetAttributeValue<EntityReference>(\"sprk_assignedlawfirm1\"); }",
            "}",
        });

        var found = BillingLookupsIn(new Dictionary<string, string> { ["Grants.cs"] = planted });

        Assert.Equal(new[] { "Grants.cs:3: sprk_billingfirm", "Grants.cs:4: sprk_BillingResponsible" }, found);
    }

    // ── 3. The external module registry ──────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "089a K-2: the external module registry holds none of the procedure-model tables")]
    public void TheExternalModuleRegistryHoldsNoProcedureModelTable()
    {
        var path = Path.Combine(BffRoot, "Infrastructure", "DI", "ExternalAccessModule.cs");
        Assert.True(File.Exists(path), "ExternalAccessModule.cs was not found; the guard would be vacuous.");

        var entities = RecordEntitiesIn(SourceScan.CodeText(File.ReadAllLines(path)));

        // Not vacuous: the scan reads the descriptors it means to.
        Assert.Contains("sprk_matter", entities);
        Assert.Contains("sprk_servicerequest", entities);

        var exposed = entities.Intersect(ProcedureModelTables, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(exposed.Count == 0,
            "An external module exposes a procedure-model table (procedure model 5.2, K-2; Procedures are internal-only): "
            + string.Join(", ", exposed));
    }

    [Fact(DisplayName = "089a K-2: control — a planted procedure-model descriptor fires (must fire); the core descriptors do not (must not fire)")]
    public void RecordEntitiesIn_Controls()
    {
        var code = SourceScan.CodeText(new[]
        {
            "services.AddExternalModule(new ExternalModuleDescriptor { Key = \"m\", RecordEntity = \"sprk_matter\" });",
            "services.AddExternalModule(new ExternalModuleDescriptor { Key = \"r\", RecordEntity = \"sprk_ruleset\" });",
            "// RecordEntity = \"sprk_procedureassignment\" (a comment, stripped)",
        });

        var entities = RecordEntitiesIn(code);

        Assert.Equal(new[] { "sprk_matter", "sprk_ruleset" }, entities);
        Assert.Equal(new[] { "sprk_ruleset" }, entities.Intersect(ProcedureModelTables).ToArray());
    }
}
