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
/// <item>passes one as the value of <c>new XAttribute("attribute", ...)</c> (FetchXML built with XElement);</item>
/// <item>spells <c>attribute="createdby"</c> / <c>attribute='createdonbehalfby'</c> in a FetchXML string;</item>
/// <item>(fix round, review K2) sets one as <c>AttributeName = ...</c> (a <c>ConditionExpression</c> initializer) or passes one as a named
/// <c>attributeName:</c> argument;</item>
/// <item>joins on one with <c>LinkFromAttributeName = ...</c> (a <c>LinkEntity</c> from the creator); or</item>
/// <item>spells the Web API form <c>_createdby_value</c> / <c>_createdonbehalfby_value</c> (an OData <c>$filter</c>).</item>
/// </list>
/// <para>Not caught, by design of a scan: the column held in a local or a field of another name, an in-memory comparison of a read row's
/// <c>createdby</c> (the seventh DI-144 site, <c>Services/Signals/GateDecisionRecordSharing.cs:521</c>, compares a read row's <c>createdby</c> in
/// memory and is invisible to this scan; DI-144 covers it), a <c>LinkEntity</c> built with positional constructor arguments, and a FetchXML
/// string whose line holds <c>//</c> before the attribute (a URL). Controls: <see cref="TheDetector_FiresOnEachForm"/>,
/// <see cref="TheDetector_FiresOnTheK2Forms"/> and <see cref="TheDetector_StaysQuietOnTheHelperAndOtherColumns"/>.</para>
/// <para><b>Maintenance.</b> If this fails on your code: build the condition with <c>WriterCreatedRecord.AddConditions</c> (a query's or a link's
/// criteria) or <c>WriterCreatedRecord.FetchXmlFilter</c>, never by hand. The <see cref="Baseline"/> is a ratchet for the ontology's existing
/// lookups (DI-144, #2632 moves them onto the helper): a new site fails; a site removed must lower its count here (<see cref="TheBaseline_IsTight"/>);
/// an entry is added only with its own reason and ADR citation, and only for code this project does not own. An ebilling site is never
/// baselined: it moves onto the helper.</para>
/// </remarks>
public class WriterCreatedRecordSingleSourceGuardTests
{
    private const string HelperFile = "Services/Signals/WriterCreatedRecord.cs";

    private static readonly string[] ScannedFolders = ["Services/Finance/", "Services/Signals/"];

    /// <summary>One baselined file: how many sites it may hold, why, and the decisions that allow it until it is worked down.</summary>
    internal sealed record BaselineEntry(int Count, string Reason, string Citation);

    /// <summary>The cite every entry shares: ArchTests are a KEEP path (ADR-038 A1), this guard enforces EB-D-208's one definition of
    /// "writer-created", and each site is ontology-r1's, tracked onto the helper by DI-144 (#2632).</summary>
    private const string Cite = "ADR-038 A1; EB-D-208; DI-144 (#2632, ontology-r1 owns the file)";

    /// <summary>DI-144 (ontology-r1, #2632): the ontology's existing writer-identity lookups, by file, count and reason. Work it down; never up.
    /// Re-checked in the 165b-3 part B fix round with the K2 forms added: no new site in either folder, so no entry grew.</summary>
    internal static readonly IReadOnlyDictionary<string, BaselineEntry> Baseline = new Dictionary<string, BaselineEntry>(StringComparer.Ordinal)
    {
        ["Services/Signals/DecisionCommitService.cs"] = new(1,
            "FindRecordAsync (:1868): the commit's 'already recorded' lookup filters createdby = the writer without createdonbehalfby.", Cite),
        ["Services/Signals/DecisionRecordWriter.cs"] = new(3,
            "FindGateRecordAsync (:733), FindConditionClearedRecordAsync (:1017) and AddBillingWriterFilterAsync (:1673): the writer's own " +
            "'already recorded' lookups filter createdby = the writer without createdonbehalfby (completion's K3 guard covers the billing one).", Cite),
        ["Services/Signals/SignalReadService.cs"] = new(1,
            "ReadDecisionRecordsAsync (:1193): the FetchXML read of a Signal's Decision Records filters createdby = the writer without createdonbehalfby.", Cite),
        ["Services/Signals/SignalReevaluationJob.cs"] = new(1,
            "RetriesReplacingAPriorLinkAsync (:955): the re-evaluation reads only the writer's prior records by createdby, without createdonbehalfby.", Cite),
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
        // Fix round (review K2): new ConditionExpression { AttributeName = column, ... }.
        new(@"\bAttributeName\s*=\s*" + Column, RegexOptions.Compiled),
        // A named argument: AddCondition(attributeName: column, ...), new ConditionExpression(attributeName: column, ...).
        new(@"\battributeName\s*:\s*" + Column, RegexOptions.Compiled),
        // A join from the creator: new LinkEntity { LinkFromAttributeName = column, ... }.
        new(@"\bLinkFromAttributeName\s*=\s*" + Column, RegexOptions.Compiled),
        // The Web API form: $filter=_createdby_value eq ..., _createdonbehalfby_value eq null.
        new(@"\b_created(?:by|onbehalfby)_value\b", RegexOptions.Compiled),
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
            .Where(s => s.Value.Count > (Baseline.GetValueOrDefault(s.Key)?.Count ?? 0))
            .Select(s => $"{s.Key}:{string.Join(",", s.Value)} ({s.Value.Count}, baseline {Baseline.GetValueOrDefault(s.Key)?.Count ?? 0})")
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
            .Where(b => sites.GetValueOrDefault(b.Key)?.Count is not { } count || count < b.Value.Count)
            .Select(b => $"{b.Key}: baseline {b.Value.Count}, found {sites.GetValueOrDefault(b.Key)?.Count ?? 0}")
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

    [Fact(DisplayName = "165b-3 control: every baseline entry carries its own reason and an ADR citation (tests/CLAUDE.md)")]
    public void EveryBaselineEntry_HasAReasonAndACitation()
    {
        Assert.All(Baseline, b =>
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Value.Reason), b.Key);
            Assert.Contains("ADR-", b.Value.Citation, StringComparison.Ordinal);
        });
        Assert.Equal(Baseline.Count, Baseline.Values.Select(b => b.Reason).Distinct(StringComparer.Ordinal).Count());
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

    [Theory(DisplayName = "165b-3 fix round control (must fire, review K2): an AttributeName initializer, a named attributeName: argument, a LinkFromAttributeName join and the Web API _created*_value form")]
    [InlineData("var c = new ConditionExpression { AttributeName = \"createdby\", Operator = ConditionOperator.Equal };")]
    [InlineData("var c = new ConditionExpression { AttributeName = \"createdonbehalfby\", Operator = ConditionOperator.Null };")]
    [InlineData("var c = new ConditionExpression { AttributeName = DecisionRecordWriter.CreatedByColumn };")]
    [InlineData("query.Criteria.AddCondition(attributeName: \"createdby\", conditionOperator: ConditionOperator.Equal, values: writer);")]
    [InlineData("var c = new ConditionExpression(attributeName: WriterCreatedRecord.CreatedOnBehalfByColumn, conditionOperator: ConditionOperator.Null);")]
    [InlineData("var link = new LinkEntity { LinkFromEntityName = \"sprk_decisionrecord\", LinkFromAttributeName = \"createdby\", LinkToEntityName = \"systemuser\" };")]
    [InlineData("var link = new LinkEntity { LinkFromAttributeName = \"createdonbehalfby\" };")]
    [InlineData("var filter = $\"_createdby_value eq {writer}\";")]
    [InlineData("var filter = \"sprk_decisionrecords?$filter=_createdonbehalfby_value eq null\";")]
    public void TheDetector_FiresOnTheK2Forms(string line)
    {
        Assert.Equal(new[] { 1 }, ConditionLines(line));
    }

    [Fact(DisplayName = "165b-3 control (must not fire): the helper's calls, a column set, an in-memory read, other created-by columns, a comment")]
    public void TheDetector_StaysQuietOnTheHelperAndOtherColumns()
    {
        const string code = """
            WriterCreatedRecord.AddConditions(query.Criteria, writer);
            WriterCreatedRecord.AddConditions(link, writer);
            linkElement.Add(WriterCreatedRecord.FetchXmlFilter(writer));
            var columns = new ColumnSet(DecisionRecordWriter.CreatedByColumn, "createdby");
            var mine = row.GetAttributeValue<EntityReference>(DecisionRecordWriter.CreatedByColumn)?.Id == writerUserId;
            query.Criteria.AddCondition("sprk_createdbyperson", ConditionOperator.Equal, caller);
            query.Criteria.AddCondition(CreatedByPersonColumn, ConditionOperator.Equal, caller);
            new XAttribute("attribute", "sprk_createdbycontact");
            // query.Criteria.AddCondition("createdby", ConditionOperator.Equal, writer);
            var fetch = "<attribute name='createdby' />";
            var byPerson = new ConditionExpression { AttributeName = "sprk_createdbyperson", Operator = ConditionOperator.Equal };
            query.Criteria.AddCondition(attributeName: "sprk_createdbyperson", conditionOperator: ConditionOperator.Equal, values: caller);
            var personLink = new LinkEntity { LinkFromAttributeName = "sprk_createdbyperson", LinkToEntityName = "contact" };
            var select = "$select=_sprk_createdbyperson_value";
            """;

        Assert.Empty(ConditionLines(code));
    }
}
