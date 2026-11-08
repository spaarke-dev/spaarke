using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 165b-3 part B (EB-D-208; ontology-r1 O2): "a row the BFF writer identity created" has ONE definition,
/// <c>Services/Signals/WriterCreatedRecord.cs</c> (<c>createdby</c> the writer AND <c>createdonbehalfby</c> empty). A query that builds its own
/// <c>createdby</c> or <c>createdonbehalfby</c> condition is a second copy of the rule, and a copy that drops <c>createdonbehalfby</c> counts a
/// row created on the writer's behalf by another identity as the server's.
/// </summary>
/// <remarks>
/// <para>Enforced by: this source scan (CI and the Stop hook) over <c>Services/Finance/**</c> and <c>Services/Signals/**</c> of the BFF. A condition
/// is a column name any file can spell, so no type can stop a copy. Outside the helper's file, no code text (line comments stripped)</para>
/// <list type="bullet">
/// <item>passes <c>"createdby"</c>, <c>"createdonbehalfby"</c>, <c>CreatedByColumn</c> or <c>CreatedOnBehalfByColumn</c> (qualified or not) as the
/// column of <c>AddCondition(...)</c> or <c>new ConditionExpression(...)</c> (QueryExpression);</item>
/// <item>passes one as the value of <c>new XAttribute("attribute", ...)</c> (FetchXML built with XElement); or</item>
/// <item>spells <c>attribute="createdby"</c> / <c>attribute='createdonbehalfby'</c> in a FetchXML string.</item>
/// </list>
/// <para>Not caught, by design of a scan: the column held in a local or a field of another name, an in-memory comparison of a read row's
/// <c>createdby</c>, and a FetchXML string whose line holds <c>//</c> before the attribute (a URL). Ratchet baseline: the ontology's own lookups
/// (DI-144, #2632 moves them onto the helper); a new site fails, and a site removed must lower its count here. Controls:
/// <see cref="TheDetector_FiresOnEachForm"/> and <see cref="TheDetector_StaysQuietOnTheHelperAndOtherColumns"/>.</para>
/// </remarks>
public class WriterCreatedRecordSingleSourceGuardTests
{
    private const string HelperFile = "Services/Signals/WriterCreatedRecord.cs";

    private static readonly string[] ScannedFolders = ["Services/Finance/", "Services/Signals/"];

    /// <summary>DI-144 (ontology-r1, #2632): the ontology's existing writer-identity lookups, by file and count. Work it down; never up.</summary>
    internal static readonly IReadOnlyDictionary<string, int> Baseline = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Services/Signals/DecisionCommitService.cs"] = 1,
        ["Services/Signals/DecisionRecordWriter.cs"] = 3,
        ["Services/Signals/SignalReadService.cs"] = 1,
        ["Services/Signals/SignalReevaluationJob.cs"] = 1,
    };

    private const string Column =
        @"(?:""createdby""|""createdonbehalfby""|(?<![\w])(?:[A-Za-z_]\w*\.)*(?:CreatedByColumn|CreatedOnBehalfByColumn)\b)";

    private static readonly Regex[] Forms =
    [
        // QueryExpression: AddCondition(column, ...) or AddCondition("entity", column, ...); new ConditionExpression(column, ...).
        new(@"\b(?:AddCondition|ConditionExpression)\s*\(\s*(?:""[^""]*""\s*,\s*)?" + Column, RegexOptions.Compiled),
        // FetchXML built with XElement: new XAttribute("attribute", column).
        new(@"\bXAttribute\s*\(\s*""attribute""\s*,\s*" + Column, RegexOptions.Compiled),
        // FetchXML in a string: attribute="createdby", attribute='createdonbehalfby', attribute=\"createdby\".
        new(@"\battribute\s*=\s*\\?[""']created(?:by|onbehalfby)\\?[""']", RegexOptions.Compiled),
    ];

    /// <summary>The 1-based lines of <paramref name="code"/> that build a <c>createdby</c> or <c>createdonbehalfby</c> condition (line comments
    /// stripped; a condition split across lines is reported on its first line).</summary>
    internal static IReadOnlyList<int> ConditionLines(string code)
    {
        var text = SourceScan.CodeText(code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
        return Forms.SelectMany(f => f.Matches(text).Select(m => SourceScan.LineOf(text, m.Index))).Distinct().Order().ToList();
    }

    private static string BffRoot => Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    private static string RelativeToBff(string file) => Path.GetRelativePath(BffRoot, file).Replace('\\', '/');

    private static Dictionary<string, List<int>> Sites() =>
        SourceScan.ServerSourceFiles()
            .Where(f => f.StartsWith(BffRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(f => (File: RelativeToBff(f), Path: f))
            .Where(f => ScannedFolders.Any(folder => f.File.StartsWith(folder, StringComparison.Ordinal)) && f.File != HelperFile)
            .Select(f => (f.File, Lines: ConditionLines(File.ReadAllText(f.Path)).ToList()))
            .Where(f => f.Lines.Count > 0)
            .ToDictionary(f => f.File, f => f.Lines, StringComparer.Ordinal);

    [Fact(DisplayName = "165b-3: no source in Services/Finance or Services/Signals builds a createdby / createdonbehalfby condition outside WriterCreatedRecord (beyond the DI-144 baseline)")]
    public void NoWriterCondition_IsBuiltOutsideTheHelper()
    {
        var over = Sites()
            .Where(s => s.Value.Count > Baseline.GetValueOrDefault(s.Key))
            .Select(s => $"{s.Key}:{string.Join(",", s.Value)} ({s.Value.Count}, baseline {Baseline.GetValueOrDefault(s.Key)})")
            .ToList();

        Assert.True(over.Count == 0,
            "builds a createdby / createdonbehalfby condition (a copy of the writer-created rule); use WriterCreatedRecord.AddConditions / FetchXmlFilter: "
            + string.Join("; ", over));
    }

    [Fact(DisplayName = "165b-3 ratchet: a DI-144 baseline entry that shrank is lowered (the baseline never hides a new site)")]
    public void TheBaseline_IsTight()
    {
        var sites = Sites();
        var loose = Baseline
            .Where(b => sites.GetValueOrDefault(b.Key)?.Count is not { } count || count < b.Value)
            .Select(b => $"{b.Key}: baseline {b.Value}, found {sites.GetValueOrDefault(b.Key)?.Count ?? 0}")
            .ToList();

        Assert.True(loose.Count == 0, "lower the baseline to what is found: " + string.Join("; ", loose));
    }

    [Fact(DisplayName = "165b-3 control: the helper file exists and holds the rule (a rename would silently widen the scan's blind spot)")]
    public void TheHelper_ExistsAndHoldsTheRule()
    {
        var path = Path.Combine(BffRoot, HelperFile);
        Assert.True(File.Exists(path), HelperFile);
        Assert.NotEmpty(ConditionLines(File.ReadAllText(path)));
    }

    [Fact(DisplayName = "165b-3 control (must fire): AddCondition, ConditionExpression, XAttribute and a FetchXML string, each column spelling, across a line break")]
    public void TheDetector_FiresOnEachForm()
    {
        const string code = """
            query.Criteria.AddCondition("createdby", ConditionOperator.Equal, writer);
            criteria.AddCondition(CreatedOnBehalfByColumn, ConditionOperator.Null);
            query.Criteria.AddCondition(DecisionRecordWriter.CreatedByColumn, ConditionOperator.Equal, id);
            var c = new ConditionExpression("createdonbehalfby", ConditionOperator.Null);
            filter.AddCondition("sprk_decisionrecord", "createdby", ConditionOperator.Equal, id);
            new XElement("condition", new XAttribute("attribute", DecisionRecordWriter.CreatedByColumn), new XAttribute("operator", "eq"));
            new XElement("condition", new XAttribute("attribute", "createdonbehalfby"), new XAttribute("operator", "null"));
            var fetch = "<condition attribute='createdby' operator='eq' value='x' />";
            var fetch2 = "<condition attribute=\"createdonbehalfby\" operator=\"null\" />";
            query.Criteria.AddCondition(
                WriterCreatedRecord.CreatedOnBehalfByColumn, ConditionOperator.Null);
            """;

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, ConditionLines(code));
    }

    [Fact(DisplayName = "165b-3 control (must not fire): the helper's calls, a column set, an in-memory read, other created-by columns, a comment")]
    public void TheDetector_StaysQuietOnTheHelperAndOtherColumns()
    {
        const string code = """
            WriterCreatedRecord.AddConditions(query.Criteria, writer);
            link.LinkCriteria.Add(WriterCreatedRecord.FetchXmlFilter(writer));
            var columns = new ColumnSet(DecisionRecordWriter.CreatedByColumn, "createdby");
            var mine = row.GetAttributeValue<EntityReference>(DecisionRecordWriter.CreatedByColumn)?.Id == writerUserId;
            query.Criteria.AddCondition("sprk_createdbyperson", ConditionOperator.Equal, caller);
            query.Criteria.AddCondition(CreatedByPersonColumn, ConditionOperator.Equal, caller);
            new XAttribute("attribute", "sprk_createdbycontact");
            // query.Criteria.AddCondition("createdby", ConditionOperator.Equal, writer);
            var fetch = "<attribute name='createdby' />";
            """;

        Assert.Empty(ConditionLines(code));
    }
}
