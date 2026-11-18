using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-038 (ebilling-r1, uac-r2): every place in <c>src/server</c> that names a rule set's <c>sprk_visibletofirms</c> column is a reviewed entry
/// of <see cref="AllowList"/>, so nothing outside the one helper decides what a firm sees of a rule set's content by the flag.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A record-scoped rule set holds every changed rule of one record (and firm) and takes its flag from the first rule changed
/// there; rules changed later inherit it. Per-record visibility therefore uses both rule sets' flags (see uac-r2 thread): the record set's and the
/// base rule's own set's. <c>RuleSetClauseVisibility.VisibleToFirms</c> (<c>Services/Finance/RuleSetService.cs</c>) is THE reading of the flag for
/// a firm-visibility decision, and its answer feeds <c>RuleSetClauseVisibility.ForFirm</c>, the single place that shows or hides clause text. A
/// new reader that took a record set's flag alone would bring the defect back.</para>
/// <para><b>Enforced by:</b> this source scan (CI tier 1 runs the whole ArchTests suite, and the Stop hook). An occurrence is a match of the
/// column name (any casing; also the Web API <c>_..._value</c> form) in a <c>.cs</c> file under <c>src/server</c>, with line comments removed (a <c>//</c> inside a string literal is not a comment: <see cref="StripComments"/>).
/// Each occurrence is attributed to its enclosing member: the nearest line above it that starts with an access modifier and declares a method,
/// constructor, record, field or property. The guard fails when a (file, member) pair names the column and is not listed, when a listed pair names
/// it a different number of times than listed (an added read inside a reviewed member is a new read), and when a listed pair no longer names it
/// (a stale entry). Controls: planted readers fire; a comment, a longer column name and a reviewed shape stay quiet; the real decision
/// (<c>ReadFirmClauseAsync</c>) names the column nowhere.</para>
/// <para><b>Maintenance.</b> To add a reader: decide whether it decides what a firm sees of a rule set's content. If it does, call
/// <c>RuleSetClauseVisibility.VisibleToFirms</c> instead (record-scoped sets need their base rule sets). If it does not (a writer of the flag, a
/// column list, an internal admin read of a set that is never record-scoped), add an entry with its count, its kind and a written reason that
/// says why it is not a firm-visibility decision, plus the citation, and have code review approve it. To remove or move one: change the entry in
/// the same commit. Never widen an entry's count without re-reading every occurrence in that member.</para>
/// <para><b>Known limits.</b> Per member, not per statement: two reads in one reviewed member are told apart by count only. A column name built at
/// run time, a block comment (<c>/* */</c>) and readers outside <c>src/server</c> (the RuleReview code page reads the flag through the BFF's own
/// rule-set routes, which are internal-admin routes) are not seen. A member whose declaration line does not start with an access modifier (a
/// local function, a lambda) is attributed to the member that contains it.</para>
/// </remarks>
public sealed class RuleSetVisibleToFirmsReaderGuardTests
{
    private static readonly Regex Column = new(
        @"(?<![A-Za-z0-9_])_?sprk_visibletofirms(?:_value)?(?![A-Za-z0-9_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MemberLine = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|internal|protected)\b", RegexOptions.CultureInvariant);

    private static readonly Regex MethodName = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^()]*>)?\s*\(", RegexOptions.CultureInvariant);

    private static readonly Regex FieldName = new(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=|\{|$)", RegexOptions.CultureInvariant);

    private const string RuleSetServiceFile = "src/server/api/Sprk.Bff.Api/Services/Finance/RuleSetService.cs";
    private const string AssignmentFile = "src/server/api/Sprk.Bff.Api/Services/Finance/Procedures/ProcedureAssignmentService.cs";
    private const string ProcedureFile = "src/server/api/Sprk.Bff.Api/Services/Finance/Procedures/ProcedureService.cs";
    private const string BillingGuidelinesFile = "src/server/api/Sprk.Bff.Api/Services/Finance/BillingGuidelinesService.cs";

    internal sealed record Entry(int Count, string Kind, string Reason);

    /// <summary>
    /// The reviewed readers and writers, by (repo-relative file, enclosing member). <b>Every entry carries a written reason and a citation</b>
    /// (tests/CLAUDE.md; ADR-038 Amendment A1 makes <c>tests/Spaarke.ArchTests/**</c> the home of structural fitness functions). The governing
    /// decisions are ebilling-r1 D-66 / D-79 (the flag and the single clause decision) and the uac-r2 ISS-038 ruling (per-record visibility uses both
    /// rule sets' flags).
    /// </summary>
    internal static readonly Dictionary<(string File, string Member), Entry> AllowList = new()
    {
        // Reason: THE helper. Reads the record set's flag and each base rule set's flag; a non-record set answers by its own flag.
        // Citation: ADR-038 A1; uac-r2 ISS-038; D-66 / D-79.
        [(RuleSetServiceFile, "VisibleToFirms")] = new(2, "Decision (the helper)", "the one reading of the flag for a firm-visibility decision"),

        // Reason: the column list of every rule-set read here (LoadBillingRuleSetAsync, ListAsync). Selecting the column decides nothing; the
        // helper and the internal admin summaries below read it. Citation: ADR-038 A1; D-66.
        [(RuleSetServiceFile, "RuleSetColumns")] = new(1, "Select", "column list only"),

        // Reason: the internal admin publish response reports the set's own flag. The set comes from LoadRuleSetAsync, which refuses a
        // record-scoped set, and the response goes to the internal Rule Review page, not to a firm. Citation: ADR-038 A1; D-66.
        [(RuleSetServiceFile, "PublishedResult")] = new(1, "Internal admin read (never record-scoped)", "publish response to an internal admin"),

        // Reason: the internal admin rule-set summary (list, get, visibility change). Its sets come from ListAsync (record-scoped sets filtered
        // out) or LoadRuleSetAsync (refuses them); it reports the set's own flag to the internal Rule Review page. Citation: ADR-038 A1; D-66.
        [(RuleSetServiceFile, "Summarize")] = new(1, "Internal admin read (never record-scoped)", "rule-set summary for an internal admin"),

        // Reason: writer. The admin visibility route sets the flag of a non-record set (LoadRuleSetAsync refuses a record-scoped one).
        // Citation: ADR-038 A1; D-66.
        [(RuleSetServiceFile, "SetVisibilityAsync")] = new(1, "Write", "sets the flag"),

        // Reason: writer. A new Procedure's rule set takes the initial flag of its type. Citation: ADR-038 A1; D-66.
        [(RuleSetServiceFile, "NewProcedureRuleSet")] = new(1, "Write", "sets the initial flag"),

        // Reason: reads the BASE rule's set (LoadRuleAsync refuses a record-scoped set) to seed a new record set's flag (the writer below). It
        // decides nothing a firm sees: the firm clause read applies the helper to the record set AND the base set. Citation: ADR-038 A1; ISS-038.
        [(AssignmentFile, "LoadRuleAsync")] = new(2, "Select + seed read (base set, never record-scoped)", "seed for the record set writer"),

        // Reason: writer. A record-scoped set is created with the flag of the rule it first takes; the helper never trusts it alone.
        // Citation: ADR-038 A1; ISS-038.
        [(AssignmentFile, "RecordRuleSetCreateAsync")] = new(1, "Write", "sets the record set's initial flag"),

        // Reason: the Billing Guidelines view (ebilling-r1 task 135) names the column once, as its constant, to SELECT it with the rule set row. The
        // decision is not made here: every rule goes through RuleSetClauseVisibility.VisibleToFirms (the record set AND, for a record rule, its
        // base rule set read live), and the flag is never read from the row by this class. Citation: ADR-038 A1; ISS-038; D-66.
        [(BillingGuidelinesFile, "VisibleColumn")] = new(1, "Select", "column name only; the decision is the helper"),

        // Reason: writer. A Procedure's type change resets its rule set's flag to the type's initial value. Citation: ADR-038 A1; D-66.
        [(ProcedureFile, "UpdateAsync")] = new(1, "Write", "resets the flag on a type change"),
    };

    /// <summary>Each occurrence of the column in <paramref name="lines"/> (line comments removed), with its enclosing member and 1-based line.</summary>
    internal static List<(string Member, int Line)> Occurrences(IReadOnlyList<string> lines)
    {
        var code = StripComments(lines);
        var found = new List<(string, int)>();
        for (var i = 0; i < code.Count; i++)
        {
            foreach (Match _ in Column.Matches(code[i]))
            {
                found.Add((EnclosingMember(code, i), i + 1));
            }
        }

        return found;
    }

    /// <summary>
    /// The lines with <c>//</c> line comments (and so <c>///</c>) removed, ignoring a <c>//</c> inside a string literal: regular, verbatim
    /// (<c>@"..."</c>), interpolated (<c>$"..."</c>, <c>$@"..."</c>) and raw (<c>"""..."""</c>) strings, and char literals. A verbatim or raw string
    /// that spans lines keeps its state across them. String text is kept, so a column named inside a string (a URL, an OData <c>$select</c>) is
    /// still seen. Local to this guard: <see cref="SourceScan.StripLineComment"/> cuts at the first <c>//</c> wherever it is, and other guards
    /// rely on that. Block comments are not handled (see the Known limits).
    /// </summary>
    internal static List<string> StripComments(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        var inVerbatim = false;
        var rawQuotes = 0;
        foreach (var line in lines)
        {
            var sb = new StringBuilder();
            var inRegular = false;
            var i = 0;
            while (i < line.Length)
            {
                var c = line[i];
                if (rawQuotes > 0)
                {
                    var run = QuoteRun(line, i);
                    if (c == '"' && run >= rawQuotes)
                    {
                        rawQuotes = 0;
                        sb.Append(line, i, run);
                        i += run;
                    }
                    else
                    {
                        sb.Append(c);
                        i++;
                    }
                }
                else if (inVerbatim)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append("\"\"");
                        i += 2;
                        continue;
                    }

                    inVerbatim = c != '"';
                    sb.Append(c);
                    i++;
                }
                else if (inRegular)
                {
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        sb.Append(c).Append(line[i + 1]);
                        i += 2;
                        continue;
                    }

                    inRegular = c != '"';
                    sb.Append(c);
                    i++;
                }
                else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    break; // the rest of the line is a comment
                }
                else if (c == '"')
                {
                    var run = QuoteRun(line, i);
                    if (run >= 3)
                    {
                        rawQuotes = run;
                    }
                    else if (run == 2)
                    {
                        sb.Append("\"\"");
                        i += 2;
                        continue; // an empty string
                    }
                    else if (sb.Length > 0 && (sb[^1] == '@' || (sb[^1] == '$' && sb.Length > 1 && sb[^2] == '@')))
                    {
                        inVerbatim = true;
                    }
                    else
                    {
                        inRegular = true;
                    }

                    sb.Append(line, i, run);
                    i += run;
                }
                else if (c == '\'')
                {
                    // A char literal ('"' and '\'' must not open or close a string).
                    var length = i + 1 < line.Length && line[i + 1] == '\\'
                        ? line.IndexOf('\'', Math.Min(i + 3, line.Length)) - i + 1
                        : i + 2 < line.Length && line[i + 2] == '\'' ? 3 : 1;
                    length = length < 1 ? 1 : length;
                    sb.Append(line, i, length);
                    i += length;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            result.Add(sb.ToString());
        }

        return result;
    }

    private static int QuoteRun(string line, int start)
    {
        var end = start;
        while (end < line.Length && line[end] == '"')
        {
            end++;
        }

        return end - start;
    }

    /// <summary>The name declared by the nearest line at or above <paramref name="index"/> that starts with an access modifier.</summary>
    internal static string EnclosingMember(IReadOnlyList<string> code, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (!MemberLine.IsMatch(code[i]))
            {
                continue;
            }

            var declaration = code[i];
            var method = MethodName.Match(declaration);
            if (method.Success)
            {
                return method.Groups["name"].Value;
            }

            var beforeValue = declaration.Split('=')[0].TrimEnd();
            var field = FieldName.Match(beforeValue);
            Match? last = null;
            for (; field.Success; field = field.NextMatch())
            {
                last = field;
            }

            return last?.Groups["name"].Value ?? "<unknown>";
        }

        return "<file>";
    }

    /// <summary>The problems of <paramref name="found"/> (occurrence counts by file and member) against <paramref name="allowList"/>.</summary>
    internal static List<string> Problems(
        IReadOnlyDictionary<(string File, string Member), int> found, IReadOnlyDictionary<(string File, string Member), Entry> allowList)
    {
        var problems = new List<string>();
        foreach (var ((file, member), count) in found)
        {
            if (!allowList.TryGetValue((file, member), out var entry))
            {
                problems.Add($"{file} ({member}): names sprk_visibletofirms {count} time(s) and is not a reviewed entry. A firm-visibility decision "
                    + "must call RuleSetClauseVisibility.VisibleToFirms (a record-scoped set needs its base rule sets); anything else needs a reviewed "
                    + "entry with a reason (see the Maintenance remark).");
            }
            else if (entry.Count != count)
            {
                problems.Add($"{file} ({member}): names sprk_visibletofirms {count} time(s); the reviewed entry says {entry.Count}. Re-read every "
                    + "occurrence in this member, then correct the entry.");
            }
        }

        foreach (var ((file, member), entry) in allowList.Where(e => !found.ContainsKey(e.Key)))
        {
            problems.Add($"{file} ({member}): a reviewed entry ({entry.Kind}) but the member no longer names sprk_visibletofirms; remove the entry");
        }

        return problems;
    }

    private static Dictionary<(string File, string Member), int> Scan(IEnumerable<string> files) => files
        .SelectMany(f => Occurrences(File.ReadAllLines(f)).Select(o => (File: SourceScan.Relative(f).Replace('\\', '/'), o.Member)))
        .GroupBy(o => o)
        .ToDictionary(g => g.Key, g => g.Count());

    [Fact(DisplayName = "ISS-038: every reader of sprk_visibletofirms in src/server is a reviewed entry with its count; none decides a record set's content by its flag alone")]
    public void EveryVisibleToFirmsReader_IsAReviewedEntry()
    {
        var files = SourceScan.ServerSourceFiles().ToList();
        Assert.True(files.Count > 100, $"Only {files.Count} server source files were found; the guard would be vacuous.");

        var found = Scan(files);
        Assert.True(found.Values.Sum() >= 10, $"Only {found.Values.Sum()} occurrences were found; the scan would be vacuous.");
        Assert.All(AllowList.Values, e => Assert.False(string.IsNullOrWhiteSpace(e.Reason), "every entry carries a reason"));

        var problems = Problems(found, AllowList);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "ISS-038: the firm clause decision names the column nowhere itself; the helper is where the flag is read")]
    public void TheFirmClauseDecision_ReadsTheFlagOnlyThroughTheHelper()
    {
        var file = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "RuleSetService.cs");
        Assert.True(File.Exists(file), $"{file} was not found; the control would be vacuous.");
        var lines = File.ReadAllLines(file);
        var occurrences = Occurrences(lines);

        Assert.DoesNotContain(occurrences, o => o.Member is "ReadFirmClauseAsync" or "GetFirmClauseAsync" or "ForFirm");
        Assert.Contains(occurrences, o => o.Member == "VisibleToFirms");
        Assert.Contains(lines, l => l.Contains("RuleSetClauseVisibility.VisibleToFirms(set, baseRuleSets)", StringComparison.Ordinal));
    }

    private static string[] Fixture(params string[] body) =>
        ["namespace Fixture;", "internal static class Planted", "{", .. body, "}"];

    [Theory(DisplayName = "ISS-038: a planted reader in an unlisted member fires (must-fire control)")]
    [InlineData("var visible = set.GetAttributeValue<bool>(\"sprk_visibletofirms\");")]
    [InlineData("var visible = set.GetAttributeValue<bool?>(\"SPRK_VisibleToFirms\") ?? false;")]
    [InlineData("var path = \"sprk_rulesets?$select=sprk_name,sprk_visibletofirms\";")]
    [InlineData("query.ColumnSet.AddColumn(\"sprk_visibletofirms\");")]
    public void TheGuard_FiresOnAPlantedReader(string planted)
    {
        var lines = Fixture("    internal static bool ShowsClause(Entity set)", "    {", "        " + planted, "        return true;", "    }");
        var found = Occurrences(lines).GroupBy(o => ("src/server/Planted.cs", o.Member)).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(new[] { "ShowsClause" }, found.Keys.Select(k => k.Item2));
        Assert.Contains(Problems(found, AllowList), p => p.StartsWith("src/server/Planted.cs (ShowsClause)", StringComparison.Ordinal)
            && p.Contains("not a reviewed entry", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ISS-038: an extra read inside a reviewed member fires, and a stale entry fires (must-fire control)")]
    public void TheGuard_FiresOnAnExtraReadInAReviewedMember_AndOnAStaleEntry()
    {
        var lines = Fixture(
            "    internal static bool VisibleToFirms(Entity ruleSet, IReadOnlyCollection<Entity?> baseRuleSets)",
            "    {",
            "        var a = ruleSet.GetAttributeValue<bool?>(\"sprk_visibletofirms\");",
            "        var b = baseRuleSets.All(s => s!.GetAttributeValue<bool?>(\"sprk_visibletofirms\") == true);",
            "        var c = ruleSet.GetAttributeValue<bool>(\"sprk_visibletofirms\"); // the planted extra read",
            "        return a == true && b && c;",
            "    }");
        var allow = new Dictionary<(string, string), Entry> { [("src/server/Planted.cs", "VisibleToFirms")] = new(2, "Decision", "planted") };
        var found = Occurrences(lines).GroupBy(o => ("src/server/Planted.cs", o.Member)).ToDictionary(g => g.Key, g => g.Count());

        Assert.Contains(Problems(found, allow), p => p.Contains("3 time(s); the reviewed entry says 2", StringComparison.Ordinal));
        Assert.Contains(Problems(new Dictionary<(string, string), int>(), allow), p => p.Contains("no longer names", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "ISS-038: a comment, a longer column name and another column stay quiet (must-not-fire control)")]
    [InlineData("// sprk_visibletofirms is read only by the helper")]
    [InlineData("/// <summary>The sprk_visibletofirms flag.</summary>")]
    [InlineData("var note = row.GetAttributeValue<string>(\"sprk_visibletofirmsnote\");")]
    [InlineData("var visible = row.GetAttributeValue<bool>(\"sprk_isvisibletofirms\");")]
    [InlineData("var visible = RuleSetClauseVisibility.VisibleToFirms(set, baseRuleSets);")]
    public void TheGuard_StaysQuiet_OnANonReader(string line)
    {
        var lines = Fixture("    internal static void Read()", "    {", "        " + line, "    }");

        Assert.Empty(Occurrences(lines));
    }

    [Theory(DisplayName = "ISS-038: a column named inside a string that holds // (a URL) is still reported (must-fire control)")]
    [InlineData("var url = $\"https://{org}/api/data/v9.2/sprk_rulesets?$select=sprk_visibletofirms\";")]
    [InlineData("var url = \"https://example.org/api/data/v9.2/sprk_rulesets?$select=sprk_visibletofirms\";")]
    [InlineData("var url = @\"https://example.org/sprk_rulesets?$select=sprk_visibletofirms\";")]
    [InlineData("var url = $@\"https://{org}/sprk_rulesets?$select=sprk_visibletofirms\";")]
    [InlineData("var url = \"\"\"https://example.org/sprk_rulesets?$select=sprk_visibletofirms\"\"\";")]
    [InlineData("var quote = '\"'; var url = \"https://x/sprk_visibletofirms\";")]
    [InlineData("var url = \"a \\\" b https://x/sprk_visibletofirms\";")]
    public void TheGuard_FiresOnTheColumnInsideAStringThatHoldsSlashes(string planted)
    {
        var lines = Fixture("    internal static void Read()", "    {", "        " + planted, "    }");

        Assert.Equal(new[] { "Read" }, Occurrences(lines).Select(o => o.Member));
    }

    [Fact(DisplayName = "ISS-038: a // inside a multi-line verbatim string does not hide the column; a real comment after a string does (control)")]
    public void TheGuard_KeepsStringStateAcrossLines_AndStillStripsRealComments()
    {
        var lines = Fixture(
            "    internal static void Read()",
            "    {",
            "        var query = @\"GET https://example.org/api",
            "            ?$select=sprk_visibletofirms\";",
            "        var other = \"https://example.org\"; // sprk_visibletofirms is named in this comment only",
            "        var third = \"https://example.org\"; /// sprk_visibletofirms",
            "    }");

        Assert.Equal(new[] { "Read" }, Occurrences(lines).Select(o => o.Member));
    }

    [Fact(DisplayName = "ISS-038: a reviewed shape (a listed member with its count) stays quiet (must-not-fire control); members are attributed by declaration")]
    public void TheGuard_StaysQuiet_OnAReviewedShape()
    {
        var lines = Fixture(
            "    private static readonly string[] Columns = new[]",
            "    {",
            "        \"sprk_name\", \"sprk_visibletofirms\",",
            "    };",
            "",
            "    private async Task<(BaseRule? Rule, RuleSetOutcome<T>? Refusal)> LoadRuleAsync<T>(IGenericEntityService entities, Guid policyId)",
            "    {",
            "        var set = await Load(new[] { \"sprk_scope\", \"sprk_visibletofirms\" });",
            "        return (new BaseRule(set.GetAttributeValue<bool?>(\"sprk_visibletofirms\") ?? false), null);",
            "    }");
        var occurrences = Occurrences(lines);
        Assert.Equal(new[] { "Columns", "LoadRuleAsync", "LoadRuleAsync" }, occurrences.Select(o => o.Member));

        var allow = new Dictionary<(string, string), Entry>
        {
            [("src/server/Planted.cs", "Columns")] = new(1, "Select", "planted"),
            [("src/server/Planted.cs", "LoadRuleAsync")] = new(2, "Select + seed read", "planted"),
        };
        var found = occurrences.GroupBy(o => ("src/server/Planted.cs", o.Member)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Empty(Problems(found, allow));
    }
}
