using System.Xml.Linq;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 130e-1 fix round 1 (F2a): a deploy-order guard. Every <c>sprk_policyversion</c> column the BFF selects exists in the solution
/// source, or is listed in <see cref="PendingSchema"/> with the deploy script that adds it. Dataverse refuses a query that names a column the
/// table does not have, so a BFF build that selects a column no environment has yet fails every rule read (billing evaluation, the Rule Review
/// page, record-rule changes, the plan replay) at run time, with no build error.
/// </summary>
/// <remarks>
/// <para><b>The source.</b> The attributes of <c>src/dataverse/solutions/SpaarkeMaster/Entities/sprk_PolicyVersion/Entity.xml</c> (logical names,
/// lower-cased), plus <see cref="PlatformColumns"/> (platform attributes no solution export lists). The column sets checked are
/// <see cref="Selected"/>: every <c>sprk_policyversion</c> column set that names <c>sprk_firmdescription</c>.</para>
/// <para><b>Ratchet.</b> A <see cref="PendingSchema"/> entry is a column a deploy script adds and the solution source does not carry yet. An entry
/// that IS in Entity.xml fails <see cref="PendingSchema_ListsOnlyColumnsTheSourceLacks"/>, so the list only shrinks: task 079 exports the
/// solution, Entity.xml gains the columns, and their entries must then be removed.</para>
/// <para><b>Controls.</b> <see cref="Missing"/> is pure. A synthetic set naming an unknown column MUST be reported (must fire); the real sets with
/// the pending list MUST NOT be (must not fire); a pending entry that the source already carries MUST be reported as stale.</para>
/// <para><b>Maintenance.</b> When <see cref="EverySelectedColumn_ExistsOrIsPendingSchema"/> fails, a BFF read names a column the solution source
/// does not have. Either (a) the column is new: write or name its deploy script, run it on every environment before the BFF build that selects it
/// is deployed, and add an entry here naming the script and the reason; or (b) the name is wrong: fix the read. When
/// <see cref="PendingSchema_ListsOnlyColumnsTheSourceLacks"/> fails, the solution export now carries the column: delete its entry. A new
/// <c>sprk_policyversion</c> column set in the BFF is added to <see cref="Selected"/>.</para>
/// </remarks>
public class PolicyVersionColumnsGuardTests
{
    /// <summary>Columns selected by the BFF that a deploy script adds and the solution source does not carry yet: column, then the script and the reason.</summary>
    internal static readonly IReadOnlyDictionary<string, string> PendingSchema = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Empty since task 079 (2026-11-25): its SpaarkeMaster sync carries sprk_firmdescription (130e-1) and sprk_rejectionreason
        // (D-113, ISS-024) in Entity.xml. A new column a deploy script adds before the next export gets an entry here again.
    };

    /// <summary>Platform attributes every table has that a solution export does not list.</summary>
    internal static readonly IReadOnlySet<string> PlatformColumns = new HashSet<string>(StringComparer.Ordinal) { "versionnumber" };

    /// <summary>Every BFF column set of <c>sprk_policyversion</c> that selects the firm description, by name.</summary>
    internal static IEnumerable<(string Name, IReadOnlyList<string> Columns)> Selected()
    {
        yield return ("BillingFactLoader.VersionColumns", BillingFactLoader.VersionColumns);
        yield return ("ProcedureResolver.PolicyVersionColumns", ProcedureResolver.PolicyVersionColumns);
        yield return ("RuleSetService.VersionColumns", RuleSetService.VersionColumns);
        yield return ("ProcedureAssignmentService.BaseVersionColumns", ProcedureAssignmentService.BaseVersionColumns);
        yield return ("ProcedureAssignmentService.ReadBackVersionColumns", ProcedureAssignmentService.ReadBackVersionColumns);
        yield return ("BillingFirmReasonReplay.VersionColumns", BillingFirmReasonReplay.VersionColumns);
    }

    private static IReadOnlySet<string> SourceAttributes()
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Entities", "sprk_PolicyVersion", "Entity.xml");
        Assert.True(File.Exists(file), "The solution source has no sprk_PolicyVersion/Entity.xml; the scan went stale.");
        return XDocument.Load(file).Descendants("attribute")
            .Select(a => ((string?)a.Element("LogicalName") ?? (string?)a.Attribute("PhysicalName"))?.ToLowerInvariant())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The columns of <paramref name="columns"/> that are neither attributes, platform columns nor pending schema (pure).</summary>
    internal static IReadOnlyList<string> Missing(IEnumerable<string> columns, IReadOnlySet<string> attributes, IReadOnlyDictionary<string, string> pending) =>
        columns.Select(c => c.ToLowerInvariant())
            .Where(c => !attributes.Contains(c) && !PlatformColumns.Contains(c) && !pending.ContainsKey(c))
            .Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

    /// <summary>Pending entries the source already carries (pure): stale, to be removed.</summary>
    internal static IReadOnlyList<string> Stale(IReadOnlyDictionary<string, string> pending, IReadOnlySet<string> attributes) =>
        pending.Keys.Where(attributes.Contains).OrderBy(c => c, StringComparer.Ordinal).ToList();

    [Fact(DisplayName = "130e-1 F2a: every sprk_policyversion column the BFF selects exists in the solution source or is pending schema with its deploy script")]
    public void EverySelectedColumn_ExistsOrIsPendingSchema()
    {
        var attributes = SourceAttributes();
        var problems = Selected()
            .Select(s => (s.Name, Missing: Missing(s.Columns, attributes, PendingSchema)))
            .Where(s => s.Missing.Count > 0)
            .Select(s => $"{s.Name}: {string.Join(", ", s.Missing)}")
            .ToList();
        Assert.True(problems.Count == 0,
            "A BFF read selects sprk_policyversion columns the solution source does not have and no pending-schema entry names (see the class "
            + "remarks, Maintenance):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "130e-1 F2a ratchet: a pending-schema entry the solution source already carries is stale and must be removed")]
    public void PendingSchema_ListsOnlyColumnsTheSourceLacks()
    {
        var stale = Stale(PendingSchema, SourceAttributes());
        Assert.True(stale.Count == 0, $"Remove these PendingSchema entries; Entity.xml carries them now: {string.Join(", ", stale)}.");
    }

    /// <summary>Pending entries whose reason does not start with the path of an existing deploy script (pure, given the repo root).</summary>
    internal static IReadOnlyList<string> WithoutExistingScript(IReadOnlyDictionary<string, string> pending, string repoRoot) =>
        pending.Where(p =>
            {
                var script = p.Value.Split(' ')[0].TrimEnd(':');
                return !(script.StartsWith("scripts/", StringComparison.Ordinal) && script.EndsWith(".ps1", StringComparison.Ordinal)
                    && File.Exists(Path.Combine(repoRoot, script)));
            })
            .Select(p => p.Key).OrderBy(c => c, StringComparer.Ordinal).ToList();

    [Fact(DisplayName = "130e-1 F2a: each pending entry names its deploy script, and that script exists")]
    public void PendingSchema_EachEntryNamesAnExistingScript()
    {
        Assert.Empty(WithoutExistingScript(PendingSchema, SourceScan.RepoRoot));
        // Controls (PendingSchema is empty since task 079, so the line above checks nothing on its own): a real script passes; a missing
        // script, a non-script path and a reason with no path are reported.
        Assert.Equal(["b", "c", "d"], WithoutExistingScript(new Dictionary<string, string>
        {
            ["a"] = "scripts/ebilling/Deploy-EbillingFormsViews.ps1 adds it.",
            ["b"] = "scripts/ebilling/Deploy-NoSuchScript.ps1 adds it.",
            ["c"] = "docs/x.md adds it.",
            ["d"] = "Added by hand.",
        }, SourceScan.RepoRoot));
    }

    /// <summary>An entry that says its column is deployed (pure). A pending entry is a column this repo cannot show exists anywhere, so the
    /// claim is unverifiable here and was wrong once (fix round 2 F3b: sprk_rejectionreason, ISS-024).</summary>
    internal static IReadOnlyList<string> ClaimsDeployed(IReadOnlyDictionary<string, string> pending) =>
        pending.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p.Value, @"\bDeployed\b|\balready (deployed|applied)\b"))
            .Select(p => p.Key).OrderBy(c => c, StringComparer.Ordinal).ToList();

    [Fact(DisplayName = "130e-1 fix round 2 F3b: no pending-schema entry claims its column is deployed; each says what still has to run")]
    public void PendingSchema_ClaimsNoDeployment()
    {
        Assert.Empty(ClaimsDeployed(PendingSchema));
        Assert.Equal(["x"], ClaimsDeployed(new Dictionary<string, string> { ["x"] = "scripts/a.ps1: Deployed; not exported.", ["y"] = "scripts/b.ps1: NOT yet applied." }));
    }

    [Fact(DisplayName = "130e-1 F2a controls: an unknown column is reported (must fire); the real sets are not (must not fire); a carried pending entry is stale")]
    public void Controls()
    {
        var attributes = SourceAttributes();

        Assert.Equal(["sprk_nosuchcolumn"], Missing([.. RuleSetService.VersionColumns, "sprk_NoSuchColumn"], attributes, PendingSchema));
        // A selected column the source lacks is reported (must fire): the real attributes minus one the replay selects.
        var withoutFirmDescription = attributes.Where(a => a != "sprk_firmdescription").ToHashSet(StringComparer.Ordinal);
        Assert.Equal(["sprk_firmdescription"], Missing(BillingFirmReasonReplay.VersionColumns, withoutFirmDescription, new Dictionary<string, string>()));
        // ...and a pending entry for it hides the gap (the ratchet's purpose).
        Assert.Empty(Missing(BillingFirmReasonReplay.VersionColumns, withoutFirmDescription, new Dictionary<string, string> { ["sprk_firmdescription"] = "scripts/x.ps1" }));
        foreach (var (_, columns) in Selected())
        {
            Assert.Empty(Missing(columns, attributes, PendingSchema));
        }

        Assert.Equal(["sprk_ruletype"], Stale(new Dictionary<string, string> { ["sprk_ruletype"] = "x", ["sprk_nosuchcolumn"] = "y" }, attributes));
    }
}
