using System.Text;
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
/// filtered by policy or Procedure Version returns drafts. Every query over <c>sprk_policyversion</c> in scope must, within its own method and
/// before its <c>RetrieveMultipleAsync</c> / <c>ReadAsync</c> call, either carry a condition on <c>"statecode"</c> (a column set or an order
/// naming it filters nothing), or be one of the reads that cannot return a draft or must see inactive rows: a read by id
/// (<c>sprk_policyversionid</c>: a citation must still resolve a deactivated draft), a read of Published versions only
/// (<c>PublishStatePublished</c>: never deactivated), or a read annotated <c>// all versions:</c> with its reason (numbering, the alternate
/// key, the deactivation's own re-read).</para>
/// <para><b>Shapes</b> (fix round 5, K3): <c>new QueryExpression(T)</c>, a target-typed <c>QueryExpression q = new(T)</c> and an
/// <c>EntityName = T</c> initialiser or assignment, where <c>T</c> is the literal, a constant of any name the file defines as
/// <c>"sprk_policyversion"</c>, or <c>Class.Name</c> for such a constant anywhere in the BFF.</para>
/// <para>Enforced by this test (CLAUDE.md §16 enforcement ladder: a source scan; a type cannot see a query's conditions). No ratchet baseline:
/// the scope has no violation today.</para>
/// </remarks>
public sealed class DraftPolicyVersionStatecodeGuardTests
{
    private static readonly string[] ScopedFolders = { Path.Combine("Services", "Finance"), Path.Combine("Api", "Finance") };

    private const string Literal = "\"sprk_policyversion\"";
    private const string Target = @"(?<target>""[^""\r\n]*""|[A-Za-z_][\w.]*)";

    /// <summary>The three ways a query names its table.</summary>
    private static readonly Regex[] QueryShapes =
    {
        new(@"new\s+QueryExpression\s*\(\s*" + Target + @"\s*\)", RegexOptions.CultureInvariant),
        new(@"\bQueryExpression\s+\w+\s*=\s*new\s*\(\s*" + Target + @"\s*\)", RegexOptions.CultureInvariant),
        new(@"\bEntityName\s*=\s*" + Target + @"\s*[,;}\r\n]", RegexOptions.CultureInvariant),
    };

    private static readonly Regex QuerySent = new(@"\b(RetrieveMultipleAsync|ReadAsync|AllAsync)\s*\(", RegexOptions.CultureInvariant);

    /// <summary>A constant (or static readonly field) of any name with the value <c>"sprk_policyversion"</c>.</summary>
    private static readonly Regex PolicyVersionConstant = new(
        @"(const\s+string|static\s+readonly\s+string)\s+(?<name>\w+)\s*=\s*""sprk_policyversion""\s*;", RegexOptions.CultureInvariant);

    private static readonly Regex TypeDeclaration = new(@"\b(class|struct|record|interface|enum)\s+(?<name>\w+)", RegexOptions.CultureInvariant);

    private static readonly Regex ControlHeader = new(
        @"^(if|else|for|foreach|while|do|switch|using|try|catch|finally|lock|fixed|checked|unchecked|unsafe|await\s+using|await\s+foreach)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex MethodHeader = new(
        @"^[\w<>\[\],.?\s]*?\b\w+\s*(<[^()]*>)?\s*\(.*\)\s*(where\b.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>A parenthesised group followed by what follows a type (<c>&gt;</c>, <c>?</c>, <c>,</c>, <c>[</c>, or a name other than
    /// <c>where</c>): a tuple type.</summary>
    private static readonly Regex TupleType = new(@"\([^()]*\)(?=\s*[>?,\[]|\s+(?!where\b)\w)", RegexOptions.CultureInvariant);

    private static readonly Regex AccessorHeader = new(@"^(\[[^\]]*\]\s*)*(get|set|init|add|remove)$", RegexOptions.CultureInvariant);

    internal sealed record Finding(string File, int Line, string Snippet);

    /// <summary>A CONDITION on <paramref name="operand"/> (an <c>AddCondition</c> or a <c>ConditionExpression</c>): naming the column in a
    /// column set or an order does not filter anything.</summary>
    private static Regex Condition(string operand) =>
        new(@"(AddCondition|ConditionExpression)\s*\(\s*" + operand, RegexOptions.CultureInvariant);

    /// <summary><c>Class.Name</c> for every constant of the value <c>"sprk_policyversion"</c> in <paramref name="source"/>.</summary>
    internal static IEnumerable<string> QualifiedConstants(string source)
    {
        var masked = Mask(source); // a type named in a comment or a string is not a declaration
        return PolicyVersionConstant.Matches(source).Select(m =>
        {
            var type = TypeDeclaration.Matches(masked[..m.Index]).LastOrDefault();
            return type is null ? m.Groups["name"].Value : $"{type.Groups["name"].Value}.{m.Groups["name"].Value}";
        }).ToList();
    }

    /// <param name="qualified"><c>Class.Name</c> constants for <c>"sprk_policyversion"</c> defined anywhere (the scan of the BFF).</param>
    internal static IReadOnlyList<Finding> Scan(string file, string source, IReadOnlySet<string>? qualified = null)
    {
        qualified ??= new HashSet<string>(StringComparer.Ordinal) { "RuleSetService.VersionEntity" };
        var local = PolicyVersionConstant.Matches(source).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var masked = Mask(source);
        var findings = new List<Finding>();
        foreach (var m in Anchors(source, masked))
        {
            var target = m.Groups["target"].Value;
            if (target != Literal && !qualified.Contains(target) && !local.Contains(target))
            {
                continue; // e.g. ProcedureService's VersionEntity is sprk_procedureversion
            }

            var sent = QuerySent.Match(masked, m.Index + m.Length);
            var end = Math.Min(sent.Success ? sent.Index : source.Length, EndOfEnclosingMethod(masked, m.Index));
            var lineStart = source.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            var window = source[m.Index..Math.Max(m.Index, end)];
            if (Condition("\"statecode\"").IsMatch(window)
                || Condition("\"sprk_policyversionid\"").IsMatch(window)
                || Condition(@"[^;]*?PublishStatePublished").IsMatch(window)
                || PrecedingLines(source, lineStart, 3).Contains("// all versions:", StringComparison.Ordinal)
                || window.Contains("// all versions:", StringComparison.Ordinal))
            {
                continue;
            }

            var line = source[..m.Index].Count(c => c == '\n') + 1;
            var lineEnd = source.IndexOf('\n', m.Index);
            findings.Add(new Finding(file, line, source[lineStart..(lineEnd < 0 ? source.Length : lineEnd)].Trim()));
        }

        return findings;
    }

    /// <summary>Every query shape in code (not in a comment or a string), one per position.</summary>
    private static IEnumerable<Match> Anchors(string source, string masked) =>
        QueryShapes.SelectMany(r => r.Matches(source))
            .Where(m => masked[m.Index] == source[m.Index]) // a shape inside a comment or a string is masked out
            .GroupBy(m => m.Index).Select(g => g.First())
            .OrderBy(m => m.Index);

    private static string PrecedingLines(string source, int lineStart, int count)
    {
        var start = lineStart;
        for (var i = 0; i < count && start > 0; i++)
        {
            start = source.LastIndexOf('\n', Math.Max(0, start - 2)) + 1;
        }

        return source[start..lineStart];
    }

    /// <summary>
    /// The end of the method (or local function, or accessor) that encloses <paramref name="at"/>: walk out through the enclosing blocks,
    /// skipping control blocks, lambdas and initialisers, to the first block whose header declares a method; its closing brace. In a type
    /// body (a field or an expression-bodied member), the end of the statement.
    /// </summary>
    internal static int EndOfEnclosingMethod(string masked, int at)
    {
        var depth = 0;
        for (var i = at - 1; i >= 0; i--)
        {
            var c = masked[i];
            if (c == '}')
            {
                depth++;
            }
            else if (c == '{' && depth > 0)
            {
                depth--;
            }
            else if (c == '{')
            {
                var header = HeaderBefore(masked, i);
                if (TypeDeclaration.IsMatch(header) || header.StartsWith("namespace", StringComparison.Ordinal))
                {
                    return EndOfStatement(masked, at);
                }

                if (IsMethodHeader(header))
                {
                    return MatchingBrace(masked, i);
                }
            }
        }

        return masked.Length;
    }

    private static bool IsMethodHeader(string header)
    {
        if (AccessorHeader.IsMatch(header))
        {
            return true;
        }

        if (header.Length == 0 || ControlHeader.IsMatch(header) || header.EndsWith("=>", StringComparison.Ordinal))
        {
            return false;
        }

        // A tuple type (a return type such as Task<(A Rules, bool Truncated)>) is a type, not the parameter list: fold it to one token.
        string folded;
        while ((folded = TupleType.Replace(header, "T")) != header)
        {
            header = folded;
        }

        var paren = header.IndexOf('(');
        if (paren < 0)
        {
            return false;
        }

        var beforeParen = header[..paren];
        return !beforeParen.Contains('=') && !Regex.IsMatch(beforeParen, @"\b(new|return|await|throw)\b") && MethodHeader.IsMatch(header);
    }

    /// <summary>The text between the previous statement or block boundary and the brace at <paramref name="brace"/>, attributes dropped.</summary>
    private static string HeaderBefore(string masked, int brace)
    {
        var start = masked.LastIndexOfAny(new[] { ';', '{', '}' }, Math.Max(0, brace - 1)) + 1;
        var header = Regex.Replace(masked[start..brace], @"^\s*(\[[^\]]*\]\s*)+", string.Empty);
        return Regex.Replace(header, @"\s+", " ").Trim();
    }

    private static int MatchingBrace(string masked, int open)
    {
        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '{')
            {
                depth++;
            }
            else if (masked[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return masked.Length;
    }

    private static int EndOfStatement(string masked, int at)
    {
        var depth = 0;
        for (var i = at; i < masked.Length; i++)
        {
            switch (masked[i])
            {
                case '{' or '(':
                    depth++;
                    break;
                case '}' or ')':
                    if (--depth < 0)
                    {
                        return i;
                    }

                    break;
                case ';' when depth == 0:
                    return i;
            }
        }

        return masked.Length;
    }

    /// <summary>The source with every comment and string or char literal blanked (same length, line breaks kept), so a brace or a query
    /// shape inside one is not code.</summary>
    internal static string Mask(string source)
    {
        var sb = new StringBuilder(source);
        var i = 0;
        void Blank(int from, int to)
        {
            for (var k = from; k < to && k < sb.Length; k++)
            {
                if (sb[k] is not ('\n' or '\r'))
                {
                    sb[k] = ' ';
                }
            }
        }

        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                Blank(i, end);
                i = end;
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(i, end);
                i = end;
            }
            else if (c == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var quotes = 3;
                while (i + quotes < source.Length && source[i + quotes] == '"')
                {
                    quotes++;
                }

                var end = source.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + quotes;
                Blank(i, end);
                i = end;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && (source[i - 1] == '@' || (i > 1 && source[i - 1] == '$' && source[i - 2] == '@'));
                var k = i + 1;
                while (k < source.Length)
                {
                    if (!verbatim && source[k] == '\\')
                    {
                        k += 2;
                        continue;
                    }

                    if (source[k] == '"')
                    {
                        if (verbatim && k + 1 < source.Length && source[k + 1] == '"')
                        {
                            k += 2;
                            continue;
                        }

                        break;
                    }

                    if (!verbatim && source[k] == '\n')
                    {
                        break;
                    }

                    k++;
                }

                Blank(i, Math.Min(source.Length, k + 1));
                i = k + 1;
            }
            else if (c == '\'' && i + 2 < source.Length)
            {
                var end = source[i + 1] == '\\' ? source.IndexOf('\'', i + 3) : source[i + 2] == '\'' ? i + 2 : -1;
                if (end > 0)
                {
                    Blank(i, end + 1);
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return sb.ToString();
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

        // Constants for the table may live anywhere in the BFF (Class.Name); only the queries in scope are checked.
        var qualified = Directory.EnumerateFiles(bff, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => QualifiedConstants(File.ReadAllText(f)))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("RuleSetService.VersionEntity", qualified);

        var scanned = files.Select(f => (File: SourceScan.Relative(f), Source: File.ReadAllText(f))).ToList();
        var findings = scanned.SelectMany(s => Scan(s.File, s.Source, qualified)).ToList();

        Assert.True(findings.Count == 0,
            "A query over sprk_policyversion in eBilling's code does not name statecode, so a draft a redraft deactivated (EB-D-216) comes back. "
            + "Add `statecode = 0` in the same method before the query is sent, or, if the read must see inactive rows (numbering, a key), annotate "
            + "it `// all versions: <reason>`:\n"
            + string.Join("\n", findings.Select(f => $"  {f.File}:{f.Line}  {f.Snippet}")));

        // Non-vacuity: the scan reaches the draft readers it exists for.
        var ruleSetService = scanned.Single(s => s.File.EndsWith("RuleSetService.cs", StringComparison.Ordinal) && s.File.Contains("Finance", StringComparison.Ordinal));
        Assert.True(Anchors(ruleSetService.Source, Mask(ruleSetService.Source)).Count(m => m.Groups["target"].Value is "VersionEntity" or Literal) >= 5,
            "the scan must see RuleSetService's policy-version queries");
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

    [Fact(DisplayName = "116 fix 5 (K3) guard control (must-fire): a target-typed new(...), an EntityName initialiser or assignment, and constants of other names are reported")]
    public void Control_TheWiderShapes_AreReported()
    {
        const string targetTyped = Header + """
            QueryExpression query = new(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("t.cs", targetTyped));

        const string initialiser = Header + """
            var query = new QueryExpression { EntityName = VersionEntity, ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("i.cs", initialiser));

        const string assignment = """
            var query = new QueryExpression();
            query.EntityName = "sprk_policyversion";
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("s.cs", assignment));

        // A constant of another name, defined in the file.
        const string otherName = """
            private const string PvTable = "sprk_policyversion";
            var query = new QueryExpression(PvTable) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("o.cs", otherName));

        // Another class's constant, by its qualified name (found by the BFF-wide constant scan).
        var elsewhere = QualifiedConstants("""
            internal sealed class InvoiceAdjustmentService
            {
                internal const string PolicyVersionEntity = "sprk_policyversion";
            }
            """).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("InvoiceAdjustmentService.PolicyVersionEntity", elsewhere);
        const string qualifiedUse = """
            var query = new QueryExpression(InvoiceAdjustmentService.PolicyVersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
            query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
            var versions = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Single(Scan("q.cs", qualifiedUse, elsewhere));
    }

    [Fact(DisplayName = "116 fix 5 (K3) guard control (must-fire): the window ends with the enclosing method; a statecode in the next method does not count")]
    public void Control_AStatecodeInAnotherMethod_DoesNotCount()
    {
        const string twoMethods = Header + """
            private static QueryExpression DraftsOf(Guid policyId)
            {
                var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
                query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
                return query;
            }

            private async Task<int> CountActiveAsync(Guid id, CancellationToken ct = default)
            {
                var other = new QueryExpression("sprk_policy");
                other.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                return (await _entities.RetrieveMultipleAsync(other, ct)).Entities.Count;
            }
            """;
        var finding = Assert.Single(Scan("m.cs", twoMethods));
        Assert.Equal(4, finding.Line);
    }

    [Fact(DisplayName = "116 fix 4/5 guard control (must-not-fire): statecode named, a by-id read, a Published-only read, an all-versions read and a sprk_procedureversion read")]
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

        // The new shapes with statecode named, a query built inside control blocks and a lambda with the condition later in the same
        // method, a brace inside a string, and a shape inside a comment or a string are not reported.
        const string inBlocks = Header + """
            private async Task<IReadOnlyList<Entity>> DraftsAsync(Guid id, CancellationToken ct = default)
            {
                if (id != Guid.Empty)
                {
                    QueryExpression query = new(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
                    foreach (var batch in batches)
                    {
                        query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch.Select(x => { return x; }).ToArray());
                    }

                    _logger.LogInformation("Reading drafts of {Id} }", id);
                    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                    return (await _entities.RetrieveMultipleAsync(query, ct)).Entities;
                }

                // new QueryExpression(VersionEntity) in a comment is not a query.
                var text = "new QueryExpression(\"sprk_policyversion\")";
                var named = new QueryExpression { EntityName = VersionEntity };
                named.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                return (await _entities.RetrieveMultipleAsync(named, ct)).Entities;
            }
            """;
        Assert.Empty(Scan("f.cs", inBlocks));

        // A method returning a tuple (LoadRulesAsync's shape): the window still runs to the send in that method.
        const string tupleReturn = Header + """
            private async Task<(List<RuleSetRule> Rules, bool Truncated)> LoadRulesAsync(Guid ruleSetId, CancellationToken ct, Guid? version = null)
            {
                foreach (var batch in ids.Chunk(MaxInValues))
                {
                    var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet(VersionColumns) };
                    query.Criteria.AddCondition("sprk_policy", ConditionOperator.In, batch);
                    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                    var versions = await _entities.RetrieveMultipleAsync(query, ct);
                }
            }
            """;
        Assert.Empty(Scan("h.cs", tupleReturn));

        // The annotation may sit up to three lines above the query.
        const string annotatedAbove = Header + """
            // all versions: the alternate key covers inactive rows too,
            // so the repeat check must see them
            // (fix round 4).
            var query = new QueryExpression(VersionEntity) { ColumnSet = new ColumnSet("sprk_versionnumber") };
            var rows = await _entities.RetrieveMultipleAsync(query, ct);
            """;
        Assert.Empty(Scan("g.cs", annotatedAbove));
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
