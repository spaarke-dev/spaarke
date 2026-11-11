using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 116 fix round 4 (EB-D-216, condition 3): a redraft deactivates the drafts it supersedes, so every query that reads draft
/// <c>sprk_policyversion</c> rows must name <c>statecode</c>, or an inactive draft comes back into a list, a repeat check, a plan or a publish.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> Billing drafts live only in eBilling's code (<c>Services/Finance/**</c> and <c>Api/Finance/**</c>). Ontology stores no
/// Draft state, and its all-versions reads (<c>OntologyAdminAuthoringService.ReadVersionsAsync</c>) and the Signals readers (by id, or the
/// current version) read inactive rows on purpose; those folders are out of scope (ontology-r1's code, not edited here).</para>
/// <para><b>Rule.</b> Billing decides "draft" in memory (<c>sprk_publishstate</c> and the Procedure Version), not in the query, so a query
/// filtered by policy or Procedure Version returns drafts. Every <c>new QueryExpression(...)</c> over <c>sprk_policyversion</c> in scope must,
/// before its <c>RetrieveMultipleAsync</c> / <c>ReadAsync</c> call, either carry a condition on <c>"statecode"</c> (a column set or an order naming it filters nothing), or be one of the reads that cannot return a
/// draft or must see inactive rows: a read by id (<c>sprk_policyversionid</c>: a citation must still resolve a deactivated draft), a read of
/// Published versions only (<c>PublishStatePublished</c>: never deactivated), or a read annotated <c>// all versions:</c> with its reason
/// (numbering, the alternate key, the deactivation's own re-read).</para>
/// <para>Enforced by this test (CLAUDE.md §16 enforcement ladder: a source scan; a type cannot see a query's conditions). No ratchet baseline:
/// the scope has no violation today.</para>
/// </remarks>
public sealed class DraftPolicyVersionStatecodeGuardTests
{
    private static readonly string[] ScopedFolders = { Path.Combine("Services", "Finance"), Path.Combine("Api", "Finance") };

    private static readonly Regex PolicyVersionQuery = new(
        @"new\s+QueryExpression\s*\(\s*(?<target>""sprk_policyversion""|RuleSetService\.VersionEntity|VersionEntity|VersionTable|PolicyVersionEntity)\s*\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex QuerySent = new(@"\b(RetrieveMultipleAsync|ReadAsync|AllAsync)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>The bare constants a file may use for <c>sprk_policyversion</c>, valid only where the file defines them so.</summary>
    private static readonly Regex LocalPolicyVersionConstant = new(
        @"const\s+string\s+(?<name>VersionEntity|VersionTable|PolicyVersionEntity)\s*=\s*""sprk_policyversion""", RegexOptions.CultureInvariant);

    internal sealed record Finding(string File, int Line, string Snippet);

    /// <summary>A CONDITION on <paramref name="operand"/> (an <c>AddCondition</c> or a <c>ConditionExpression</c>): naming the column in a
    /// column set or an order does not filter anything.</summary>
    private static Regex Condition(string operand) =>
        new(@"(AddCondition|ConditionExpression)\s*\(\s*" + operand, RegexOptions.CultureInvariant);

    internal static IReadOnlyList<Finding> Scan(string file, string source)
    {
        var local = LocalPolicyVersionConstant.Matches(source).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var findings = new List<Finding>();
        foreach (Match m in PolicyVersionQuery.Matches(source))
        {
            var target = m.Groups["target"].Value;
            if (target is not ("\"sprk_policyversion\"" or "RuleSetService.VersionEntity") && !local.Contains(target))
            {
                continue; // e.g. ProcedureService's VersionEntity is sprk_procedureversion
            }

            var sent = QuerySent.Match(source, m.Index + m.Length);
            var end = sent.Success ? sent.Index : source.Length;
            var lineStart = source.LastIndexOf('\n', m.Index) + 1;
            var preceding = source[Math.Max(0, source.LastIndexOf('\n', Math.Max(0, lineStart - 2)) + 1)..lineStart];
            var window = source[m.Index..end];
            if (Condition("\"statecode\"").IsMatch(window)
                || Condition("\"sprk_policyversionid\"").IsMatch(window)
                || Condition(@"[^;]*?PublishStatePublished").IsMatch(window)
                || preceding.Contains("// all versions:", StringComparison.Ordinal)
                || window.Contains("// all versions:", StringComparison.Ordinal))
            {
                continue;
            }

            var line = source[..m.Index].Count(c => c == '\n') + 1;
            findings.Add(new Finding(file, line, source[lineStart..Math.Min(source.Length, m.Index + m.Length)].Trim()));
        }

        return findings;
    }

    [Fact(DisplayName = "116 fix 4 (EB-D-216): every billing query that reads draft sprk_policyversion rows names statecode")]
    public void EveryDraftPolicyVersionQuery_NamesStatecode()
    {
        var bff = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        var files = ScopedFolders
            .Select(f => Path.Combine(bff, f))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .ToList();
        Assert.NotEmpty(files);

        var scanned = files.Select(f => (File: SourceScan.Relative(f), Source: File.ReadAllText(f))).ToList();
        var findings = scanned.SelectMany(s => Scan(s.File, s.Source)).ToList();

        Assert.True(findings.Count == 0,
            "A query over sprk_policyversion in eBilling's code does not name statecode, so a draft a redraft deactivated (EB-D-216) comes back. "
            + "Add `statecode = 0`, or, if the read must see inactive rows (numbering, a key), annotate it `// all versions: <reason>`:\n"
            + string.Join("\n", findings.Select(f => $"  {f.File}:{f.Line}  {f.Snippet}")));

        // Non-vacuity: the scan reaches the draft readers it exists for.
        var ruleSetService = scanned.Single(s => s.File.EndsWith("RuleSetService.cs", StringComparison.Ordinal) && s.File.Contains("Finance", StringComparison.Ordinal));
        Assert.True(PolicyVersionQuery.Matches(ruleSetService.Source).Count >= 5, "the scan must see RuleSetService's policy-version queries");
    }

    private const string Header = "internal const string VersionEntity = \"sprk_policyversion\";\n";

    [Fact(DisplayName = "116 fix 4 guard control (must-fire): a draft list read by policy without statecode is reported")]
    public void Control_ADraftListReadWithoutStatecode_IsReported()
    {
        const string draftRead = Header + """
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("x.cs", draftRead));

        const string literal = """
            var q = new QueryExpression("sprk_policyversion") { ColumnSet = new ColumnSet("sprk_policy") };
            q.Criteria.AddCondition(RuleSetService.ProcedureVersionColumn, ConditionOperator.Equal, id);
            var rows = await ReadAsync(q, ct);
            """;
        Assert.Single(Scan("y.cs", literal));

        // statecode named only AFTER the query is sent does not count.
        const string late = Header + """
            var query = new QueryExpression(VersionEntity);
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, id);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            var active = versions.Entities.Where(v => v["statecode"] is null);
            """;
        Assert.Single(Scan("z.cs", late));

        // The columns named only in a column set or an order (LoadRulesAsync orders by the id) filter nothing.
        const string orderedOnly = Header + """
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet("statecode", "sprk_versionnumber") };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            query.Orders.Add(new OrderExpression("sprk_policyversionid", OrderType.Ascending));
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("w.cs", orderedOnly));
    }

    [Fact(DisplayName = "116 fix 4 guard control (must-not-fire): statecode named, a by-id read, a Published-only read, an all-versions read and a sprk_procedureversion read")]
    public void Control_AllowedReads_AreNotReported()
    {
        const string withState = Header + """
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Empty(Scan("a.cs", withState));

        // A citation resolves its version by id, active or not.
        const string byId = Header + """
            var versionQuery = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("sprk_policy", "sprk_clausereference"),
                Criteria = { Conditions = { new ConditionExpression("sprk_policyversionid", ConditionOperator.Equal, policyVersionId) } },
            };
            var version = (await _entities.RetrieveMultipleAsync(versionQuery, ct)).Entities.FirstOrDefault();
            """;
        Assert.Empty(Scan("b.cs", byId));

        const string published = """
            var query = new QueryExpression(RuleSetService.VersionEntity) { ColumnSet = new ColumnSet(RuleSetService.ProcedureVersionColumn) };
            query.Criteria.AddCondition(RuleSetService.PublishStateColumn, ConditionOperator.Equal, RuleSetService.PublishStatePublished);
            var rows = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Empty(Scan("c.cs", published));

        // The shape of OntologyAdminAuthoringService.ReadVersionsAsync: every version of one policy, inactive ones included, on purpose.
        const string allVersions = Header + """
            // all versions: the next number and sprk_inforceto need the inactive rows too.
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet("sprk_versionnumber", "sprk_inforceto") };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
            var rows = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Empty(Scan("d.cs", allVersions));

        // ProcedureService's VersionEntity is sprk_procedureversion: not a policy-version read.
        const string procedureVersion = """
            public const string VersionEntity = "sprk_procedureversion";
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            var rows = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Empty(Scan("e.cs", procedureVersion));
    }

    [Fact(DisplayName = "116 fix 4 guard: the scope leaves out Ontology and Signals (ontology-r1's readers read inactive rows on purpose)")]
    public void Scope_LeavesOutOntologyAndSignals()
    {
        Assert.DoesNotContain(ScopedFolders, f => f.Contains("Ontology", StringComparison.Ordinal) || f.Contains("Signals", StringComparison.Ordinal));

        // Were ReadVersionsAsync in scope it would need the annotation: its shape is a by-policy read with no statecode.
        var ontology = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Ontology", "OntologyAdminAuthoringService.cs");
        Assert.True(File.Exists(ontology), "the ontology reader this scope leaves out still exists");
    }
}
