using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Every table the BFF treats as carrying a trustworthy <c>sprk_createdbyperson</c> — <see cref="RecordCreatorPerson.IsStamped"/>,
/// i.e. <see cref="RecordCreatorPerson.StampedTables"/> and <see cref="RecordCreatorPerson.StampedChildTables"/> — declares that
/// column FIELD-SECURED (<c>&lt;IsSecured&gt;1&lt;/IsSecured&gt;</c>) in its <c>src/dataverse/solutions/**/Entities/&lt;table&gt;/Entity.xml</c>
/// (#2273, fix round 1).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> #2273 made the secure-exit creator check (<c>RecordOwnershipResolver</c>, <c>SecureDesignationRemoval</c>) and
/// people-targeting (<c>MembershipResolverService</c>) trust the column ONLY on a stamped table. That trust holds only while the column
/// there is field-secured so that only the BFF can write it; on an unsecured column any Write holder can name themselves as the
/// creator and then move the row out of secure isolation. Adding a table to the stamped sets is therefore a promise about its
/// schema, and this guard checks the promise against the solution source.</para>
/// <para><b>A column that exists only through a deploy script.</b> A stamped table with no Entity.xml declaration fails, unless it is
/// in <see cref="SecuredOnlyByDeployScript"/> with a reason and the script that secures it (the script must exist and name the
/// table as a whole quoted token, <see cref="NamesTable"/>). ebilling task 013's tables (<c>sprk_invoicesubmission</c>, <c>sprk_invoiceapproval</c>, <c>sprk_invoicecorrection</c>,
/// <c>sprk_externalidentifier</c>) are expected to land here when they join the stamped set; that is intended.</para>
/// <para><b>Deploy gate.</b> Source is not the environment: <c>scripts/Set-RecordCreatorPersonSchema.ps1 -Verify</c> (roots) and
/// <c>scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Verify</c> (children) must exit 0 before a BFF that stamps a new table deploys
/// (<c>docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md</c> §6.5.2).</para>
/// <para>Enforced by: <c>CreatorPersonColumnSecuredGuardTests.EveryStampedTableDeclaresTheColumnSecured</c>.</para>
/// </remarks>
public class CreatorPersonColumnSecuredGuardTests
{
    private const string Column = RecordCreatorPerson.Column;

    /// <summary>A stamped table whose column is created and secured by a deploy script, with no Entity.xml in the repo.</summary>
    internal sealed record DeployScriptSecured(string Table, string Reason, string Script);

    /// <summary>
    /// Stamped tables whose <c>sprk_createdbyperson</c> exists only through a deploy script. Each entry names the reason and the
    /// repo-relative script that secures it. ebilling-r1 task 081 (master merge, uac-r2): the Procedure and policy tables. No
    /// Entity.xml in the repo declares the column on them (sprk_procedure and sprk_procedureversion have no Entity.xml at all;
    /// sprk_policy's and sprk_policyversion's do not define the column), and the script lists all four in its <c>$Tables</c>.
    /// Task 090 (uac-r2 round-2 K1): sprk_ruleset (its Entity.xml does not define the column) and sprk_procedureassignment (no
    /// Entity.xml), both in the script's <c>$Tables</c>.
    /// </summary>
    internal static readonly DeployScriptSecured[] SecuredOnlyByDeployScript =
    [
        new("sprk_procedure", EbillingScriptReason, ChildCreatorPersonScript),
        new("sprk_procedureversion", EbillingScriptReason, ChildCreatorPersonScript),
        new("sprk_policy", EbillingScriptReason, ChildCreatorPersonScript),
        new("sprk_policyversion", EbillingScriptReason, ChildCreatorPersonScript),
        new("sprk_ruleset", EbillingScriptReason, ChildCreatorPersonScript),
        new("sprk_procedureassignment", EbillingScriptReason, ChildCreatorPersonScript),
    ];

    private const string ChildCreatorPersonScript = "scripts/Set-ChildRecordCreatorPersonSchema.ps1";

    private const string EbillingScriptReason =
        "column created by the ebilling deploy scripts; secured by the script's empty-before-securing gate";

    private static string SolutionsRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions");

    private static IReadOnlyList<string> AllStamped() =>
        RecordCreatorPerson.StampedTables.Concat(RecordCreatorPerson.StampedChildTables)
            .Select(t => t.Trim().ToLowerInvariant()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();

    [Fact(DisplayName = "Every stamped table declares sprk_createdbyperson field-secured in its Entity.xml (#2273)")]
    public void EveryStampedTableDeclaresTheColumnSecured()
    {
        var stamped = AllStamped();
        // Sanity floor: 3 roots + 25 children at #2273. A smaller set means the stamped sets shrank or the read broke.
        Assert.True(stamped.Count >= 28, $"Only {stamped.Count} stamped tables; expected at least 28.");

        var declarations = ReadDeclarations(SolutionsRoot);
        Assert.True(declarations.Count >= 28,
            $"Found {Column} in only {declarations.Count} Entity.xml tables under src/dataverse/solutions; the scan is probably broken.");

        var violations = Violations(stamped, declarations, SecuredOnlyByDeployScript, ScriptText);

        Assert.True(violations.Count == 0,
            $"A table RecordCreatorPerson.IsStamped trusts does not declare {Column} field-secured. On an unsecured column any Write "
            + "holder can name themselves as creator and move the row out of secure isolation (#2273). Declare the column with "
            + "<IsSecured>1</IsSecured> in the table's Entity.xml, or — only when the column exists through a deploy script — add an "
            + "entry to CreatorPersonColumnSecuredGuardTests.SecuredOnlyByDeployScript naming the reason and the script:\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "Creator-person guard must-not-fire: each current stamped table has a secured declaration, or a deploy-script entry")]
    public void CurrentStampedTablesAreEachSecured()
    {
        var declarations = ReadDeclarations(SolutionsRoot);
        var scripted = SecuredOnlyByDeployScript.Select(e => e.Table).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var table in AllStamped())
        {
            if (!declarations.TryGetValue(table, out var found))
            {
                // ebilling-r1 (master merge): the column exists only through the deploy script that secures it.
                Assert.True(scripted.Contains(table), $"{table}: no {Column} declaration found, and no SecuredOnlyByDeployScript entry.");
                continue;
            }

            Assert.All(found, d => Assert.True(d.IsSecured, $"{table}: {d.File} declares {Column} unsecured."));
        }
    }

    [Fact(DisplayName = "Creator-person guard must-fire: flipping any one real stamped table's column to IsSecured=0 is reported, exactly that table")]
    public void GuardFiresOnEachRealTableMadeUnsecured()
    {
        var stamped = AllStamped();
        var real = ReadDeclarations(SolutionsRoot);
        Assert.Empty(Violations(stamped, real, SecuredOnlyByDeployScript, ScriptText));

        foreach (var table in stamped)
        {
            if (!real.ContainsKey(table))
            {
                // ebilling-r1 (master merge): a deploy-script table has no declaration to flip; removing its entry is reported instead.
                var withoutEntry = SecuredOnlyByDeployScript.Where(e => !string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase));
                var missing = Assert.Single(Violations(stamped, real, withoutEntry, ScriptText));
                Assert.StartsWith(table + ":", missing, StringComparison.Ordinal);
                Assert.Contains("no Entity.xml", missing, StringComparison.Ordinal);
                continue;
            }

            var perturbed = real.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            perturbed[table] = real[table].Select(d => d with { IsSecured = false }).ToList();

            var violation = Assert.Single(Violations(stamped, perturbed, SecuredOnlyByDeployScript, ScriptText));
            Assert.Contains(table, violation, StringComparison.Ordinal);
            Assert.Contains("unsecured", violation, StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName = "Creator-person guard must-fire: a synthetic stamped table with no column, an unsecured column or no IsSecured element")]
    public void GuardFiresOnSyntheticStampedTablesWithoutASecuredColumn()
    {
        string[] stamped = ["sprk_matter", "sprk_synthetic_missing", "sprk_synthetic_unsecured", "sprk_synthetic_noflag"];
        var declarations = new Dictionary<string, IReadOnlyList<Declaration>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = Parse(EntityXml("sprk_Matter", "<IsSecured>1</IsSecured>"), "m.xml"),
            ["sprk_synthetic_unsecured"] = Parse(EntityXml("sprk_Synthetic_Unsecured", "<IsSecured>0</IsSecured>"), "u.xml"),
            ["sprk_synthetic_noflag"] = Parse(EntityXml("sprk_Synthetic_NoFlag", ""), "n.xml"),
        };

        var violations = Violations(stamped, declarations, [], _ => null);

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.StartsWith("sprk_synthetic_missing:", StringComparison.Ordinal) && v.Contains("no Entity.xml"));
        Assert.Contains(violations, v => v.StartsWith("sprk_synthetic_unsecured:", StringComparison.Ordinal) && v.Contains("unsecured"));
        Assert.Contains(violations, v => v.StartsWith("sprk_synthetic_noflag:", StringComparison.Ordinal) && v.Contains("unsecured"));
        Assert.DoesNotContain(violations, v => v.StartsWith("sprk_matter:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Creator-person guard: one unsecured declaration fails the table even when another solution declares it secured")]
    public void GuardFiresWhenAnySolutionDeclaresItUnsecured()
    {
        var declarations = new Dictionary<string, IReadOnlyList<Declaration>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_todo"] = [.. Parse(EntityXml("sprk_Todo", "<IsSecured>1</IsSecured>"), "a.xml"),
                             .. Parse(EntityXml("sprk_Todo", "<IsSecured>0</IsSecured>"), "b.xml")],
        };

        var violation = Assert.Single(Violations(["sprk_todo"], declarations, [], _ => null));
        Assert.Contains("b.xml", violation, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Creator-person guard allow-list: a script-secured table passes only with a reason and an existing script that names it")]
    public void AllowListNeedsAReasonAndAScriptThatNamesTheTable()
    {
        string[] stamped = ["sprk_synthetic_scripted"];
        var none = new Dictionary<string, IReadOnlyList<Declaration>>(StringComparer.OrdinalIgnoreCase);
        static string? Script(string path) => path switch
        {
            "scripts/Good.ps1" => "$tables = @('sprk_synthetic_scripted')",
            "scripts/Other.ps1" => "sprk_x",
            "scripts/Longer.ps1" => "$tables = @('sprk_synthetic_scriptedversion', 'sprk_synthetic_scripted_child')",
            "scripts/Unquoted.ps1" => "# sprk_synthetic_scripted is secured elsewhere",
            _ => null,
        };

        // Must-not-fire: a complete entry.
        Assert.Empty(Violations(stamped, none, [new("sprk_synthetic_scripted", "ebilling 013 adds it by script", "scripts/Good.ps1")], Script));

        // Must-fire: blank reason, missing script, script that does not name the table.
        Assert.Single(Violations(stamped, none, [new("sprk_synthetic_scripted", " ", "scripts/Good.ps1")], Script));
        Assert.Single(Violations(stamped, none, [new("sprk_synthetic_scripted", "reason", "scripts/Missing.ps1")], Script));
        Assert.Single(Violations(stamped, none, [new("sprk_synthetic_scripted", "reason", "scripts/Other.ps1")], Script));

        // Must-fire: a script that names only LONGER tables starting with this one (sprk_procedure vs 'sprk_procedureversion'),
        // or names it only unquoted in a comment, does not name it.
        Assert.Single(Violations(stamped, none, [new("sprk_synthetic_scripted", "reason", "scripts/Longer.ps1")], Script));
        Assert.Single(Violations(stamped, none, [new("sprk_synthetic_scripted", "reason", "scripts/Unquoted.ps1")], Script));

        // Must-fire: an allow-listed table that is declared UNSECURED in an Entity.xml is not excused by the entry.
        var unsecured = new Dictionary<string, IReadOnlyList<Declaration>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_synthetic_scripted"] = Parse(EntityXml("sprk_Synthetic_Scripted", "<IsSecured>0</IsSecured>"), "s.xml"),
        };
        Assert.NotEmpty(Violations(stamped, unsecured, [new("sprk_synthetic_scripted", "reason", "scripts/Good.ps1")], Script));

        // Must-fire: an entry for a table that is not stamped is dead and must be removed.
        Assert.Single(Violations([], none, [new("sprk_synthetic_scripted", "reason", "scripts/Good.ps1")], Script));
    }

    [Fact(DisplayName = "Creator-person guard parse: matches the column by LogicalName on the entity's Name, not a look-alike column")]
    public void ParseMatchesTheExactColumn()
    {
        var xml = EntityXml("sprk_Matter", "<IsSecured>1</IsSecured>")
            .Replace("</attributes>", """
                <attribute PhysicalName="sprk_CreatedByPersonName"><LogicalName>sprk_createdbypersonname</LogicalName><IsSecured>0</IsSecured></attribute>
              </attributes>
              """, StringComparison.Ordinal);

        var declaration = Assert.Single(Parse(xml, "m.xml"));
        Assert.Equal("sprk_matter", declaration.Table);
        Assert.True(declaration.IsSecured);
    }

    // ------------------------------------------------------------------------------------------------------------------

    internal sealed record Declaration(string Table, string File, bool IsSecured);

    /// <summary>Every <c>sprk_createdbyperson</c> declaration under <paramref name="solutionsRoot"/>, keyed by table logical name.</summary>
    internal static Dictionary<string, IReadOnlyList<Declaration>> ReadDeclarations(string solutionsRoot) =>
        Directory.EnumerateFiles(solutionsRoot, "Entity.xml", SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(f))), "Entities", StringComparison.OrdinalIgnoreCase))
            .SelectMany(f => Parse(File.ReadAllText(f), Path.GetRelativePath(SourceScan.RepoRoot, f)))
            .GroupBy(d => d.Table, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Declaration>)g.ToList(), StringComparer.OrdinalIgnoreCase);

    /// <summary>The column's declarations in one Entity.xml. A missing <c>IsSecured</c> element counts as unsecured.</summary>
    internal static IReadOnlyList<Declaration> Parse(string entityXml, string file)
    {
        var doc = XDocument.Parse(entityXml);
        var table = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(table))
            return [];

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "attribute"
                        && string.Equals(e.Elements().FirstOrDefault(c => c.Name.LocalName == "LogicalName")?.Value.Trim(), Column, StringComparison.OrdinalIgnoreCase))
            .Select(e => new Declaration(table, file, e.Elements().FirstOrDefault(c => c.Name.LocalName == "IsSecured")?.Value.Trim() == "1"))
            .ToList();
    }

    /// <summary>The guard's verdict: one line per violation, prefixed with the table.</summary>
    internal static List<string> Violations(
        IEnumerable<string> stamped,
        IReadOnlyDictionary<string, IReadOnlyList<Declaration>> declarations,
        IEnumerable<DeployScriptSecured> allowList,
        Func<string, string?> readScript)
    {
        var stampedSet = new HashSet<string>(stamped, StringComparer.OrdinalIgnoreCase);
        var allowed = allowList.ToDictionary(a => a.Table, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var table in stampedSet.OrderBy(t => t, StringComparer.Ordinal))
        {
            if (declarations.TryGetValue(table, out var found) && found.Count > 0)
            {
                foreach (var d in found.Where(d => !d.IsSecured))
                    violations.Add($"{table}: {d.File} declares {Column} unsecured (IsSecured is not 1).");
                continue;
            }

            if (!allowed.TryGetValue(table, out var entry))
            {
                violations.Add($"{table}: no Entity.xml under src/dataverse/solutions declares {Column}, and it is not in SecuredOnlyByDeployScript.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
                violations.Add($"{table}: the SecuredOnlyByDeployScript entry gives no reason.");
            else if (readScript(entry.Script) is not { } script)
                violations.Add($"{table}: the SecuredOnlyByDeployScript entry names '{entry.Script}', which does not exist.");
            else if (!NamesTable(script, table))
                violations.Add($"{table}: '{entry.Script}' does not name the table, so it cannot be what secures its {Column}.");
        }

        foreach (var dead in allowed.Keys.Where(t => !stampedSet.Contains(t)).OrderBy(t => t, StringComparer.Ordinal))
            violations.Add($"{dead}: listed in SecuredOnlyByDeployScript but not stamped; remove the entry.");

        return violations;
    }

    /// <summary>
    /// True when <paramref name="script"/> names <paramref name="table"/> as a whole quoted token (<c>'sprk_x'</c> or <c>"sprk_x"</c>),
    /// the form the creator-person scripts' <c>$Tables</c> lists use. A substring match would let <c>'sprk_procedureversion'</c>
    /// satisfy <c>sprk_procedure</c> and <c>'sprk_policyversion'</c> satisfy <c>sprk_policy</c> (ebilling F4/F3 re-check, F2).
    /// </summary>
    internal static bool NamesTable(string script, string table) =>
        Regex.IsMatch(script, $"""(['"]){Regex.Escape(table)}\1""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? ScriptText(string repoRelativePath)
    {
        var path = Path.Combine(SourceScan.RepoRoot, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string EntityXml(string schemaName, string isSecuredElement) => $"""
        <Entity>
          <Name LocalizedName="X" OriginalName="X">{schemaName}</Name>
          <EntityInfo>
            <entity Name="{schemaName}">
              <attributes>
                <attribute PhysicalName="sprk_CreatedByPerson">
                  <Type>lookup</Type>
                  <LogicalName>sprk_createdbyperson</LogicalName>
                  {isSecuredElement}
                </attribute>
              </attributes>
            </entity>
          </EntityInfo>
        </Entity>
        """;
}
