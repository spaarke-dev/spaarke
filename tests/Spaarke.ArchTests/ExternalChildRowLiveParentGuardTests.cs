using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 187 (owner round 99, D-123; #1857): an external CHILD-row read decides over its stamped
/// roots AND the live roots of its parents, never over the stamps alone.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> In server source (<c>src/server/**</c>):</para>
/// <list type="number">
/// <item><c>ExternalMultiRootRule.SecureRootsReadable</c> (the stamp-level decision) is called or referenced only inside
/// <c>ExternalMultiRootRule.cs</c>. Every child-row read calls <c>ExternalMultiRootRule.ChildRowReadable</c> with a
/// <c>ChildRowRoots</c>, a type built only by <c>ChildRowRoots.Of(table, row, stamped, live[, authorizedParent])</c>.</item>
/// <item>(Type level, by reflection.) No <c>ChildRowRoots.Of</c> overload accepts a list of parents: each takes the child
/// TABLE and the ROW as read and derives the parents itself through the lineage map (fix round 1, R3). So the old
/// bypass spellings (<c>parents: null</c>, a cast null, a local empty array) no longer compile.</item>
/// <item>No <c>ChildRowRoots.Of(...)</c> call passes a literal EMPTY ROW (<c>null</c>, <c>default</c>, a cast of either,
/// <c>new()</c>, <c>new Dictionary&lt;...&gt;()</c>, <c>[]</c>, <c>...Empty</c>) as its row argument: a row with no
/// columns names no parent, which is the stamps-only decision #1857 found, spelled differently.</item>
/// </list>
/// <para><b>What it does not see</b> (covered by unit tests instead): a <c>$select</c> that leaves a parent lookup out
/// hands <c>Of</c> a row in which that parent is "not filed there". Every child-row read's select is pinned in
/// <c>ExternalLiveParentRuleTests.TheListAndSingleRowReads_SelectEveryLiveParentLookup</c> and the module/thread pins, and
/// the HTTP-level tests run over fakes that project through <c>$select</c> / the <c>ColumnSet</c>.</para>
/// <para><b>Why a source guard.</b> A stamps-only call passes every test whose stamps happen to be current. The ladder
/// (CLAUDE.md §16) puts the rule where the edit that breaks it fails: first the type (rule 2), then this scan for what the
/// type cannot stop (rules 1 and 3). Enforced by: the type and this guard (ArchTests, CI and the Stop hook). No ratchet
/// baseline: at task 187 there are no existing violations.</para>
/// <para>Crude by design (<see cref="SourceScan"/>): code text with line comments stripped; argument lists split at
/// top-level commas. An argument list it cannot split into four or five is itself reported, so it fails loud, never
/// vacuously green. Each rule has a must-fire and a must-not-fire control below.</para>
/// </remarks>
public class ExternalChildRowLiveParentGuardTests
{
    internal const string RuleFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalMultiRootRule.cs";

    private static readonly Regex StampDecision = new(
        @"\bSecureRootsReadable\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChildRowRootsOf = new(
        @"\bChildRowRoots\s*\.\s*Of\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A literal empty row, optionally behind a cast: null, default, default(T), new(), new X<...>() / new X<...> { },
    // [], or a static ...Empty.
    private static readonly Regex EmptyRow = new(
        @"^\s*(\(\s*[\w.<>,?\s]+\)\s*)?(null|default|default\s*\([^)]*\)|new\s*\(\s*\)"
        + @"|new\s+[\w.]+\s*(<[^>]*>)?\s*(\(\s*\))?\s*(\{\s*\})?|\[\s*\]|[\w.]+\s*(<[^>]*>)?\s*\.\s*Empty)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The rule over one file's code text (line comments stripped): the violations, empty when it holds.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string code)
    {
        var violations = new List<string>();
        var isRuleFile = string.Equals(relativePath, RuleFile, StringComparison.OrdinalIgnoreCase);

        if (!isRuleFile)
        {
            foreach (Match m in StampDecision.Matches(code))
            {
                violations.Add($"{relativePath}:{LineOf(code, m.Index)}: uses ExternalMultiRootRule.SecureRootsReadable directly "
                    + "(the stamps are a copy; call ExternalMultiRootRule.ChildRowReadable with ChildRowRoots.Of(table, row, stamps, live))");
            }
        }

        foreach (Match m in ChildRowRootsOf.Matches(code))
        {
            var args = Arguments(code, m.Index + m.Length);
            if (args is null || args.Count is < 4 or > 5)
            {
                violations.Add($"{relativePath}:{LineOf(code, m.Index)}: a ChildRowRoots.Of call whose (table, row, stamps, live) "
                    + "arguments the guard cannot read (rewrite it plainly, or extend the guard)");
                continue;
            }

            if (EmptyRow.IsMatch(args[1]))
            {
                violations.Add($"{relativePath}:{LineOf(code, m.Index)}: ChildRowRoots.Of passes an empty row (`{args[1].Trim()}`), "
                    + "which names no parent: a stamps-only decision (#1857)");
            }
        }

        return violations;
    }

    /// <summary>
    /// The top-level, comma-separated arguments of the call whose <c>(</c> ends just before <paramref name="start"/>;
    /// <c>null</c> when the parentheses do not close. Nesting counts <c>()</c>, <c>[]</c>, <c>{}</c> and generic
    /// <c>&lt;&gt;</c> after an identifier; string and char literals are skipped.
    /// </summary>
    internal static IReadOnlyList<string>? Arguments(string code, int start)
    {
        var args = new List<string>();
        var depth = 0;
        var from = start;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(code, i);
                if (i < 0) return null;
                continue;
            }

            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                {
                    if (c != ')') return null;
                    args.Add(code[from..i]);
                    return args;
                }

                depth--;
            }
            else if (c == '<' && i > start && (char.IsLetterOrDigit(code[i - 1]) || code[i - 1] == '_') && GenericClose(code, i) is var close and > 0)
            {
                i = close; // a type argument list: its commas are not argument separators
            }
            else if (c == ',' && depth == 0)
            {
                args.Add(code[from..i]);
                from = i + 1;
            }
        }

        return null;
    }

    /// <summary>The index of the <c>&gt;</c> closing a type argument list opened at <paramref name="open"/>, or -1 when
    /// the text there is not one (a comparison).</summary>
    private static int GenericClose(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '<') depth++;
            else if (c == '>' && --depth == 0) return i;
            else if (!(char.IsLetterOrDigit(c) || c is '_' or '.' or ',' or '?' or ' ' or '[' or ']')) return -1;
        }

        return -1;
    }

    private static int SkipLiteral(string code, int open)
    {
        var quote = code[open];
        for (var i = open + 1; i < code.Length; i++)
        {
            if (code[i] == '\\') { i++; continue; }
            if (code[i] == quote) return i;
        }

        return -1;
    }

    private static int LineOf(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

    [Fact(DisplayName = "Task 187: external child-row reads decide over stamps AND live parents, never over the stamps alone")]
    public void ExternalChildRowReads_NeverDecideOverTheStampsAlone()
    {
        var violations = new List<string>();
        var ofCalls = 0;
        var sawRuleFile = false;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            sawRuleFile |= string.Equals(relative, RuleFile, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(relative, RuleFile, StringComparison.OrdinalIgnoreCase))
                ofCalls += ChildRowRootsOf.Matches(code).Count;
            violations.AddRange(Violations(relative, code));
        }

        if (!sawRuleFile)
            violations.Add($"{RuleFile} was not found: the guard would be vacuous.");
        if (ofCalls < 5)
        {
            violations.Add($"only {ofCalls} ChildRowRoots.Of call(s) outside the rule file: the to-do list, events, documents, "
                + "messages and the documents module each build one, so the guard would be vacuous.");
        }

        Assert.True(violations.Count == 0,
            "An external child-row read decides over its stamped roots only. The stamps are a COPY, so a carrier row (a to-do "
            + "filed to an invoice of a secure matter) or a stale one leaks (owner round 99, D-123; #1857). Read the row's "
            + "parent lookups (ExternalMultiRootRule.LiveParentSelect), resolve their live roots "
            + "(ExternalMultiRootRule.ReadLiveParentsAsync), and decide with ExternalMultiRootRule.ChildRowReadable over "
            + "ChildRowRoots.Of(table, row, stamps, live).\n" + string.Join("\n", violations));
    }

    [Fact(DisplayName = "Task 187 guard (type level): ChildRowRoots.Of takes the table and the row, never a list of parents")]
    public void ChildRowRootsOf_TakesTheTableAndTheRow_NeverAParentList()
    {
        var type = typeof(ChildRowRoots);
        var overloads = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == nameof(ChildRowRoots.Of))
            .ToList();

        Assert.NotEmpty(overloads); // non-vacuous: the builder exists under this name
        Assert.All(overloads, m =>
        {
            var parameters = m.GetParameters();
            Assert.True(parameters.Length >= 4, $"{m}: expected (table, row, stamped, live[, authorizedParent])");
            Assert.Equal(typeof(string), parameters[0].ParameterType);
            Assert.True(parameters[1].ParameterType.IsGenericType
                        && parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>),
                $"{m}: the second parameter must be the row as read");
            Assert.DoesNotContain(parameters, p => typeof(IEnumerable<LiveParentLookup>).IsAssignableFrom(p.ParameterType)
                                                   || p.ParameterType == typeof(LiveParentLookup));
        });

        // Only Of builds a value: no public constructor takes links (a default value is withheld by ChildRowReadable).
        Assert.Empty(type.GetConstructors());
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    private const string OtherFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs";

    private const string Sanctioned = """
        var roots = read.Rows
            .Select(r => ChildRowRoots.Of(
                TodoTable, r.Extra,
                ExternalMultiRootRule.Stamps(r.SprkRegardingprojectValue, r.SprkRegardingmatterValue, r.SprkRegardingworkassignmentValue),
                live))
            .ToArray();
        var readable = read.Rows.Where((_, i) => ExternalMultiRootRule.ChildRowReadable(caller, flags, roots[i])).ToList();
        """;

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: the sanctioned table-and-row shape passes")]
    public void Control_TheSanctionedShape_HasNoViolation()
        => Assert.Empty(Violations(OtherFile, Sanctioned));

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: the rule file itself may define and call the stamp decision")]
    public void Control_TheRuleFile_MayUseTheStampDecision()
        => Assert.Empty(Violations(RuleFile, """
            public static bool SecureRootsReadable(CallerPrincipal caller, SecureRootFlags flags, params (ExternalGrantRootType, string?)[] links) => true;
            public static bool ChildRowReadable(CallerPrincipal caller, SecureRootFlags flags, ChildRowRoots row) =>
                row.IsBuilt && SecureRootsReadable(caller, flags, row.Links);
            """));

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: comments, the document method and the real call shapes pass")]
    public void Control_CommentsAndSecureRootsReadableBy_HaveNoViolation()
        => Assert.Empty(Violations(OtherFile, SourceScan.CodeText((Sanctioned
            + "\n// never call ExternalMultiRootRule.SecureRootsReadable( here, nor ChildRowRoots.Of(TodoTable, null, stamps, live)\n"
            + "return Roots.SecureRootsReadableBy(caller, flags, live);\n"
            + "var r = ChildRowRoots.Of(DocumentTable, Row, Links, live ?? LiveParentRoots.Empty, AuthorizedParent);\n"
            + "var m = ChildRowRoots.Of(CommunicationTable, row.Row, ExternalMultiRootRule.Stamps(row.Project, row.Matter, row.WorkAssignment), live);\n"
            + "var g = ChildRowRoots.Of(RecordEntity, row, RootLinks(row), liveParents ?? LiveParentRoots.Empty);\n").Split('\n'))));

    [Theory(DisplayName = "Task 187 guard, must-fire control: a stamps-only decision is caught")]
    [InlineData("return r => ExternalMultiRootRule.SecureRootsReadable(principal, flags, r.Project, r.Matter, r.WorkAssignment);")]
    [InlineData("var ok = ExternalMultiRootRule.SecureRootsReadable(caller, flags, ExternalMultiRootRule.Stamps(p, m, w));")]
    [InlineData("Func<CallerPrincipal, SecureRootFlags, (ExternalGrantRootType, string?)[], bool> f = ExternalMultiRootRule.SecureRootsReadable;")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, null, stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, default, stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, (IReadOnlyDictionary<string, JsonElement>?)null, stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, default(IReadOnlyDictionary<string, JsonElement>), stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, new Dictionary<string, JsonElement>(), stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, new Dictionary<string, object?> { }, stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, new(), stamps, live);")]
    [InlineData("var roots = ChildRowRoots.Of(TodoTable, [], stamps, live);")]
    [InlineData("var roots = ChildRowRoots . Of ( TodoTable , ImmutableDictionary<string, JsonElement>.Empty , stamps , live ) ;")]
    public void Control_AStampsOnlyDecision_IsCaught(string statement)
        => Assert.NotEmpty(Violations(OtherFile, Sanctioned + "\n" + statement));

    [Fact(DisplayName = "Task 187 guard, must-fire control: a ChildRowRoots.Of call it cannot read is reported, never skipped")]
    public void Control_AnUnreadableCall_IsReported()
        => Assert.Contains(Violations(OtherFile, "var roots = ChildRowRoots.Of(TodoTable, row, stamps"), v => v.Contains("cannot read", StringComparison.Ordinal));

    [Fact(DisplayName = "Task 187 guard, must-fire control: the old (stamps, parents, live) shape is reported")]
    public void Control_TheOldThreeArgumentShape_IsReported()
        => Assert.Contains(Violations(OtherFile, "var roots = ChildRowRoots.Of(stamps, parents, live);"), v => v.Contains("cannot read", StringComparison.Ordinal));

    [Fact(DisplayName = "Task 187 guard, control: the argument splitter respects nesting, generics and string literals")]
    public void Control_TheArgumentSplitter_RespectsNestingAndLiterals()
    {
        var code = "ChildRowRoots.Of(T, Parse(\"x, y)\", [1, 2]), new Dictionary<string, JsonElement>(), a < b, live) tail";
        var args = Arguments(code, code.IndexOf('(') + 1);

        Assert.NotNull(args);
        Assert.Equal(5, args!.Count);
        Assert.Equal(" Parse(\"x, y)\", [1, 2])", args[1]);
        Assert.Equal(" new Dictionary<string, JsonElement>()", args[2]);
        Assert.Equal(" a < b", args[3]);
    }
}
