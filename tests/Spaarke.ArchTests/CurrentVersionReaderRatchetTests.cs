using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 082d (model 7.2 ratchet, ontology-r1 F3-b; notes/procedure-and-file-model.md section 7.2): every reader of
/// <c>sprk_policy.sprk_currentversion</c> in <c>src/server</c> applies <c>RuleDomains</c>, except the readers in the baseline, which are
/// listed and worked down.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> For billing rules <c>sprk_currentversion</c> now means "latest published", for display only (ontology-r1 amends D-14): a
/// billing rule's versions are keyed to Procedure Versions and chosen by <c>ProcedureResolver</c>. A reader that follows the pointer without
/// telling the two domains apart (<c>RuleDomains.AddBillingFilter</c> / <c>IsBillingRow</c> for billing,
/// <c>AddOntologyPolicyAllowlist</c> / <c>IsOntologyPolicy</c> for ontology, ...) can judge, close or show a billing rule by the wrong
/// version.</para>
/// <para><b>Enforced by:</b> this source scan (CI and the Stop hook). A reader is a <c>.cs</c> file under <c>src/server</c> whose code
/// (comments removed) names the column: <c>sprk_currentversion</c> (any casing; NOT <c>sprk_currentversionid</c>, the document version
/// lookup) or the Web API form <c>_sprk_currentversion_value</c>. It applies <c>RuleDomains</c> when its code calls one of the domain filters
/// or checks below (a constant such as <c>RuleDomains.PolicyDomainColumn</c> alone does not count). A reader that does not, and is not in
/// <see cref="Baseline"/>, fails; a baseline entry that now applies <c>RuleDomains</c>, or no longer reads the column, fails too, so the
/// list only shrinks. Controls: a planted reader with no <c>RuleDomains</c> call fires; a planted reader that applies it does not.</para>
/// <para><b>Granularity (known limit):</b> per file. A new read site added to a file that already applies <c>RuleDomains</c> elsewhere is not
/// told apart; the file-level rule is the one the model states ("every reader applies RuleDomains"), and the per-site check would need a
/// semantic model of where each query is filtered. Not covered either: a column name built at run time, and readers outside
/// <c>src/server</c> (client code, scripts).</para>
/// </remarks>
public sealed class CurrentVersionReaderRatchetTests
{
    private static readonly Regex Column = new(@"(?<![A-Za-z0-9])_?sprk_currentversion(?:_value)?(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AppliesRuleDomains = new(
        @"\bRuleDomains\s*\.\s*(AddBillingFilter|IsBillingRow|IsBilling|AddOntologyPolicyAllowlist|IsOntologyPolicy|IsOntology|AddClosableOnRetirementAllowlist|IsClosableOnRetirement|DomainOf)\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Known readers that do not apply <c>RuleDomains</c> (repo-relative, forward slashes), each with its owner. Worked down, never added to
    /// without an owner's decision recorded beside the entry. <b>Every entry carries a written reason and an ADR citation</b> (tests/CLAUDE.md:
    /// "Every allowlist / census entry carries a written reason and an ADR citation"; ADR-038 Amendment A1 makes <c>tests/Spaarke.ArchTests/**</c>
    /// the protected home of structural fitness functions). No domain ADR governs the rule-version pointer, so each entry also cites the decision
    /// that does: ebilling-r1 <c>notes/procedure-and-file-model.md</c> section 7.2, which records ontology-r1's amendment of D-14 and asks for this
    /// ratchet with a baseline (ontology F3-b).
    /// </summary>
    private static readonly Dictionary<string, string> Baseline = new(StringComparer.Ordinal)
    {
        // Reason: ontology-r1 owns the re-key of this reader (publish: moves the pointer after the new version is in force). It tells the domains
        // apart with OntologyDomain, not RuleDomains, so the file-level check cannot see it. Model 7.2 gives the re-key to ontology-r1.
        // Citation: ADR-038 Amendment A1 (an ArchTests allowlist entry needs a reason and an ADR citation); model 7.2 (D-14 amended by ontology-r1).
        ["src/server/api/Sprk.Bff.Api/Services/Ontology/OntologyAdminAuthoringService.cs"] = "ontology-r1",

        // Reason: reads the pointer through the Web API ($select=_sprk_currentversion_value, model 7.2 ":658/:685/:705": "ontology-r1 fixes
        // OntologyAdminClassificationService") and tells the domains apart with OntologyDomain, not RuleDomains.
        // Citation: ADR-038 Amendment A1; model 7.2 (D-14 amended by ontology-r1).
        ["src/server/api/Sprk.Bff.Api/Services/Ontology/OntologyAdminClassificationService.cs"] = "ontology-r1",

        // Reason: lists ontology policies with their pointed-to version (admin read) and tells the domains apart with OntologyDomain, not
        // RuleDomains; ontology-r1 owns the re-key.
        // Citation: ADR-038 Amendment A1; model 7.2 (D-14 amended by ontology-r1).
        ["src/server/api/Sprk.Bff.Api/Services/Ontology/OntologyAdminRulesService.cs"] = "ontology-r1",
    };

    private static string ServerFile(params string[] parts) =>
        Path.Combine([SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", .. parts]);

    /// <summary>Whether the code (line comments removed) reads the column, and whether it applies <c>RuleDomains</c>.</summary>
    internal static (bool Reads, bool Applies) Classify(IEnumerable<string> lines)
    {
        var code = SourceScan.CodeText(lines);
        return (Column.IsMatch(code), AppliesRuleDomains.IsMatch(code));
    }

    private static string Key(string file) => SourceScan.Relative(file).Replace('\\', '/');

    [Fact(DisplayName = "Task 082d (model 7.2, ontology F3-b): every sprk_currentversion reader in src/server applies RuleDomains, except the listed baseline")]
    public void EveryCurrentVersionReader_AppliesRuleDomains_OrIsInTheBaseline()
    {
        var files = SourceScan.ServerSourceFiles().ToList();
        Assert.True(files.Count > 100, $"Only {files.Count} server source files were found; the guard would be vacuous.");

        var readers = files
            .Select(f => (Key: Key(f), Class: Classify(File.ReadAllLines(f))))
            .Where(x => x.Class.Reads)
            .ToDictionary(x => x.Key, x => x.Class.Applies, StringComparer.Ordinal);
        Assert.True(readers.Count(r => r.Value) >= 5, $"Only {readers.Count(r => r.Value)} readers apply RuleDomains; the scan would be vacuous.");

        var problems = new List<string>();
        foreach (var (file, applies) in readers.Where(r => !r.Value && !Baseline.ContainsKey(r.Key)))
        {
            problems.Add($"{file}: reads sprk_currentversion but applies no RuleDomains filter or check; for billing it means \"latest published\", "
                + "display only (model 7.2): filter by domain, or choose billing versions through ProcedureResolver");
        }

        foreach (var (file, owner) in Baseline)
        {
            if (!readers.TryGetValue(file, out var applies))
            {
                problems.Add($"{file} ({owner}): in the baseline but no longer reads sprk_currentversion; remove the entry");
            }
            else if (applies)
            {
                problems.Add($"{file} ({owner}): in the baseline but now applies RuleDomains; remove the entry");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory(DisplayName = "Task 082d: the ratchet fires on a planted reader with no RuleDomains call (must-fire control)")]
    [InlineData("var current = policy.GetAttributeValue<EntityReference>(\"sprk_currentversion\")?.Id;")]
    [InlineData("var query = new QueryExpression(\"sprk_policy\") { ColumnSet = new ColumnSet(\"sprk_policycode\", \"SPRK_CURRENTVERSION\") };")]
    [InlineData("var path = \"sprk_policies?$select=sprk_policyid,_sprk_currentversion_value\";")]
    [InlineData("var columns = new[] { RuleDomains.PolicyDomainColumn, \"sprk_currentversion\" }; // a constant is not a filter")]
    public void TheRatchet_FiresOnAPlantedReader(string planted)
    {
        string[] fixture = ["namespace Fixture;", "internal static class PlantedReader", "{", "    internal static void Read()", "    {", "        " + planted, "    }", "}"];

        var (reads, applies) = Classify(fixture);

        Assert.True(reads, "the planted line reads the column");
        Assert.False(applies, "the planted reader applies no RuleDomains filter or check");
    }

    [Theory(DisplayName = "Task 082d: the ratchet stays quiet on a reader that applies RuleDomains, on the document version lookup and on a comment (must-not-fire control)")]
    [InlineData("RuleDomains.AddBillingFilter(query); var current = policy.GetAttributeValue<EntityReference>(\"sprk_currentversion\")?.Id;", true)]
    [InlineData("if (!RuleDomains.IsBillingRow(policy)) return; var c = policy.GetAttributeValue<EntityReference>(\"sprk_currentversion\");", true)]
    [InlineData("RuleDomains.AddOntologyPolicyAllowlist(query); query.ColumnSet.AddColumn(\"sprk_currentversion\");", true)]
    [InlineData("var document = row.GetAttributeValue<EntityReference>(\"sprk_currentversionid\");", false)]
    [InlineData("// sprk_currentversion is display only for billing (7.2)", false)]
    public void TheRatchet_StaysQuiet_OnACompliantReader(string planted, bool reads)
    {
        string[] fixture = ["namespace Fixture;", "internal static class Reader", "{", "    internal static void Read()", "    {", "        " + planted, "    }", "}"];

        var (actualReads, applies) = Classify(fixture);

        Assert.Equal(reads, actualReads);
        Assert.True(!actualReads || applies, "a reader that applies RuleDomains passes");
    }

    [Fact(DisplayName = "Task 082d: the real readers named by model 7.2 are seen by the scan: the loader and RuleSetService apply RuleDomains; a baseline file fires when it is not listed")]
    public void TheRealReaders_AreClassified()
    {
        foreach (var file in new[] { ServerFile("Signals", "Billing", "BillingFactLoader.cs"), ServerFile("Finance", "RuleSetService.cs"), ServerFile("Signals", "SignalReevaluationJob.cs") })
        {
            Assert.True(File.Exists(file), $"{file} was not found; the control would be vacuous.");
            Assert.Equal((true, true), Classify(File.ReadAllLines(file)));
        }

        var baseline = ServerFile("Ontology", "OntologyAdminRulesService.cs");
        Assert.True(File.Exists(baseline), $"{baseline} was not found; the control would be vacuous.");
        Assert.Equal((true, false), Classify(File.ReadAllLines(baseline)));
        Assert.True(Baseline.ContainsKey(Key(baseline)), "a reader without RuleDomains passes only while it is listed");
    }
}
