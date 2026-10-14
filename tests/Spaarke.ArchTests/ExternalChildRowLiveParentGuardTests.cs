using System.Text.RegularExpressions;
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
/// <c>ChildRowRoots</c>, a type whose only constructor is <c>ChildRowRoots.Of(stamped, parents, live)</c>.</item>
/// <item>No <c>ChildRowRoots.Of(...)</c> call passes a literal "no parents" (<c>null</c>, <c>default</c>, <c>[]</c>,
/// <c>Array.Empty&lt;...&gt;()</c>, <c>Enumerable.Empty&lt;...&gt;()</c>) as its parents argument: that is the stamps-only
/// decision #1857 found, spelled differently.</item>
/// </list>
/// <para><b>What it does not see</b> (covered by unit tests instead): a <c>$select</c> that leaves a parent lookup out
/// would hand <c>Of</c> parents that are all "not filed there". Every child-row read's select is pinned in
/// <c>ExternalLiveParentRuleTests.TheListAndSingleRowReads_SelectEveryLiveParentLookup</c> and the module/thread pins, and
/// the HTTP-level tests run over a fake that projects through <c>$select</c>.</para>
/// <para><b>Why a source guard.</b> A stamps-only call passes every test whose stamps happen to be current. The ladder
/// (CLAUDE.md §16) puts the rule where the edit that breaks it fails. Enforced by: this guard (ArchTests, CI and the Stop
/// hook). No ratchet baseline: at task 187 there are no existing violations.</para>
/// <para>Crude by design (<see cref="SourceScan"/>): code text with line comments stripped; argument lists split at
/// top-level commas. An argument list it cannot split into three is itself reported, so it fails loud, never vacuously
/// green. Each rule has a must-fire and a must-not-fire control below.</para>
/// </remarks>
public class ExternalChildRowLiveParentGuardTests
{
    internal const string RuleFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalMultiRootRule.cs";

    private static readonly Regex StampDecision = new(
        @"\bSecureRootsReadable\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ChildRowRootsOf = new(
        @"\bChildRowRoots\s*\.\s*Of\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NoParents = new(
        @"^\s*(null|default|default\s*\([^)]*\)|\[\s*\]|Array\s*\.\s*Empty\s*<[^>]*>\s*\(\s*\)|Enumerable\s*\.\s*Empty\s*<[^>]*>\s*\(\s*\)"
        + @"|new\s+LiveParentLookup\s*\[\s*0?\s*\]\s*(\{\s*\})?)\s*$",
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
                    + "(the stamps are a copy; call ExternalMultiRootRule.ChildRowReadable with ChildRowRoots.Of(stamps, parents, live))");
            }
        }

        foreach (Match m in ChildRowRootsOf.Matches(code))
        {
            var args = Arguments(code, m.Index + m.Length);
            if (args is null || args.Count != 3)
            {
                violations.Add($"{relativePath}:{LineOf(code, m.Index)}: a ChildRowRoots.Of call whose three arguments the guard "
                    + "cannot read (rewrite it plainly, or extend the guard)");
                continue;
            }

            if (NoParents.IsMatch(args[1]))
            {
                violations.Add($"{relativePath}:{LineOf(code, m.Index)}: ChildRowRoots.Of passes no parents (`{args[1].Trim()}`): "
                    + "a stamps-only decision (#1857)");
            }
        }

        return violations;
    }

    /// <summary>
    /// The top-level, comma-separated arguments of the call whose <c>(</c> ends just before <paramref name="start"/>;
    /// <c>null</c> when the parentheses do not close. Nesting counts <c>()</c>, <c>[]</c> and <c>{}</c>; string and char
    /// literals are skipped.
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
            else if (c == ',' && depth == 0)
            {
                args.Add(code[from..i]);
                from = i + 1;
            }
        }

        return null;
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
            + "ChildRowRoots.Of(stamps, parents, live).\n" + string.Join("\n", violations));
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    private const string OtherFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs";

    private const string Sanctioned = """
        var roots = read.Rows
            .Select((r, i) => ChildRowRoots.Of(
                ExternalMultiRootRule.Stamps(r.SprkRegardingprojectValue, r.SprkRegardingmatterValue, r.SprkRegardingworkassignmentValue),
                ExternalMultiRootRule.LiveParentsOf(TodoTable, r.Extra), live))
            .ToArray();
        var readable = read.Rows.Where((_, i) => ExternalMultiRootRule.ChildRowReadable(caller, flags, roots[i])).ToList();
        """;

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: the sanctioned union shape passes")]
    public void Control_TheSanctionedShape_HasNoViolation()
        => Assert.Empty(Violations(OtherFile, Sanctioned));

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: the rule file itself may define and call the stamp decision")]
    public void Control_TheRuleFile_MayUseTheStampDecision()
        => Assert.Empty(Violations(RuleFile, """
            public static bool SecureRootsReadable(CallerPrincipal caller, SecureRootFlags flags, params (ExternalGrantRootType, string?)[] links) => true;
            public static bool ChildRowReadable(CallerPrincipal caller, SecureRootFlags flags, ChildRowRoots row) =>
                row.IsBuilt && SecureRootsReadable(caller, flags, row.Links);
            """));

    [Fact(DisplayName = "Task 187 guard, must-not-fire control: comments and the similarly named document method are ignored")]
    public void Control_CommentsAndSecureRootsReadableBy_HaveNoViolation()
        => Assert.Empty(Violations(OtherFile, SourceScan.CodeText((Sanctioned
            + "\n// never call ExternalMultiRootRule.SecureRootsReadable( here, nor ChildRowRoots.Of(stamps, null, live)\n"
            + "return Roots.SecureRootsReadableBy(caller, flags, live);\n"
            + "var r = ChildRowRoots.Of(Links, Parents, live ?? LiveParentRoots.Empty);\n").Split('\n'))));

    [Theory(DisplayName = "Task 187 guard, must-fire control: a stamps-only decision is caught")]
    [InlineData("return r => ExternalMultiRootRule.SecureRootsReadable(principal, flags, r.Project, r.Matter, r.WorkAssignment);")]
    [InlineData("var ok = ExternalMultiRootRule.SecureRootsReadable(caller, flags, ExternalMultiRootRule.Stamps(p, m, w));")]
    [InlineData("Func<CallerPrincipal, SecureRootFlags, (ExternalGrantRootType, string?)[], bool> f = ExternalMultiRootRule.SecureRootsReadable;")]
    [InlineData("var roots = ChildRowRoots.Of(stamps, null, live);")]
    [InlineData("var roots = ChildRowRoots.Of(stamps, default, live);")]
    [InlineData("var roots = ChildRowRoots.Of(stamps, [], live);")]
    [InlineData("var roots = ChildRowRoots.Of(ExternalMultiRootRule.Stamps(p, m, w), Array.Empty<LiveParentLookup>(), live);")]
    [InlineData("var roots = ChildRowRoots.Of(stamps, Enumerable.Empty<LiveParentLookup>(), live);")]
    [InlineData("var roots = ChildRowRoots . Of ( stamps , new LiveParentLookup[0] , live ) ;")]
    public void Control_AStampsOnlyDecision_IsCaught(string statement)
        => Assert.NotEmpty(Violations(OtherFile, Sanctioned + "\n" + statement));

    [Fact(DisplayName = "Task 187 guard, must-fire control: a ChildRowRoots.Of call it cannot read is reported, never skipped")]
    public void Control_AnUnreadableCall_IsReported()
        => Assert.Contains(Violations(OtherFile, "var roots = ChildRowRoots.Of(stamps, parents"), v => v.Contains("cannot read", StringComparison.Ordinal));

    [Fact(DisplayName = "Task 187 guard, control: the argument splitter respects nesting and string literals")]
    public void Control_TheArgumentSplitter_RespectsNestingAndLiterals()
    {
        var code = "ChildRowRoots.Of(Stamps(a, b, c), Parse(\"x, y)\", [1, 2]), live) tail";
        var args = Arguments(code, code.IndexOf('(') + 1);

        Assert.NotNull(args);
        Assert.Equal(3, args!.Count);
        Assert.Equal(" Parse(\"x, y)\", [1, 2])", args[1]);
    }
}
