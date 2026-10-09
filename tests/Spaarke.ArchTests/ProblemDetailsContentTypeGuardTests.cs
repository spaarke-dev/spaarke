using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-019 (GitHub #975, problem-json-content-type-r1): a <c>ProblemDetails</c> must never be returned as a
/// plain JSON VALUE from <c>src/server/api/Sprk.Bff.Api</c>.
///
/// <para><b>Why this exists.</b> <c>Results.BadRequest(new ProblemDetails { … })</c> reads as an RFC 7807
/// response and serializes an RFC 7807 body, but it is a value result (<c>BadRequest&lt;object&gt;</c>) and
/// writes <c>Content-Type: application/json</c>. ADR-019 requires <c>application/problem+json</c>, and RFC
/// 7807 defines the body by that media type. #975 fixed the global exception handler and three hand-rolled
/// <c>WriteAsJsonAsync</c> sites; its survey looked for the header being LOST and so missed this shape,
/// which never sets it at all — 44 sites in six endpoint files (36 BadRequest, 7 NotFound, 1 Conflict).
/// They were converted to <c>Results.Problem(problemDetails)</c> (<c>ProblemHttpResult</c>), which writes
/// <c>application/problem+json</c>. The shape is easy to reintroduce because it compiles, reads correctly,
/// and every status-code test still passes; only a header assertion notices. This guard makes it a build
/// failure instead.</para>
///
/// <para><b>The sanctioned shapes</b> are <c>Results.Problem(...)</c> / <c>TypedResults.Problem(...)</c> (named
/// arguments or a <c>ProblemDetails</c> instance — when passing an instance, SET <c>Status</c>: the framework
/// defaults a null status to 500), <c>Results.ValidationProblem(...)</c>, and the <c>ProblemDetailsHelper</c>
/// factories, all of which return <c>ProblemHttpResult</c>.</para>
///
/// <para><b>What is detected</b> (source scan, comments stripped, multi-line aware):</para>
/// <list type="number">
///   <item>a <c>Results.</c>/<c>TypedResults.</c> value factory —
///   <c>BadRequest|NotFound|Conflict|UnprocessableEntity|Json|Ok|Accepted|Created|InternalServerError</c> —
///   whose argument is <c>new ProblemDetails</c> / <c>new ValidationProblemDetails</c> /
///   <c>new HttpValidationProblemDetails</c>;</item>
///   <item>the same factory applied (optionally with <c>!</c>) to a LOCAL in the same file that is declared as
///   one of those types, initialised with <c>new</c> one, or assigned by <c>var</c> from a call to a method
///   declared anywhere in the BFF to return one (e.g. <c>var p = materializer.EvaluatePolicy(...);
///   return Results.BadRequest(p);</c>);</item>
///   <item>a typed-result signature that bakes the value shape in, e.g.
///   <c>Results&lt;Ok&lt;T&gt;, BadRequest&lt;ProblemDetails&gt;&gt;</c> or <c>JsonHttpResult&lt;ProblemDetails&gt;</c>.</item>
/// </list>
///
/// <para>Line, doc and <c>/* … */</c> block comments are blanked first (string literals are respected, so a
/// <c>"*/*"</c> Accept value cannot open a comment), so prose describing the banned shape is not flagged.</para>
///
/// <para><b>Known limits, stated so nobody over-trusts it:</b> a <c>ProblemDetails</c> reached through a
/// PROPERTY or an inline call expression (e.g. <c>Results.BadRequest(outcome.Problem)</c>,
/// <c>Results.BadRequest(Evaluate())</c>), or through a cast (<c>(object)pd</c>), is not type-resolved by a
/// source scan. No such site exists today; the regression tests in
/// <c>tests/integration/regression/Issue975_*</c> assert the header on the wire for each converted file.</para>
/// </summary>
public class ProblemDetailsContentTypeGuardTests
{
    // =============================================================================================
    // THE ALLOWLIST — empty, and expected to stay so.
    // ---------------------------------------------------------------------------------------------
    // An entry is justified only when a client contract REQUIRES application/json for an error body
    // and cannot be changed in the same PR. Write the reason as a sentence a reviewer can evaluate,
    // cite the ADR-019 tension (root CLAUDE.md §6.5 path A/B), and name the client that forces it.
    // "Legacy" is not a reason. Keyed by file name + the offending text so one entry cannot silently
    // cover a second site added to the same file.
    // =============================================================================================
    private static readonly IReadOnlyList<AllowlistEntry> Allowlist = Array.Empty<AllowlistEntry>();

    private const string ValueFactories =
        "BadRequest|NotFound|Conflict|UnprocessableEntity|Json|Ok|Accepted|Created|InternalServerError";

    private const string ProblemTypes = "(?:Http)?(?:Validation)?ProblemDetails";

    /// <summary>
    /// Rule 1 — <c>Results.X(new ProblemDetails</c>, allowing generic type arguments, a leading named
    /// argument (<c>value:</c>) and, for Accepted/Created, a leading uri argument without nested parens.
    /// </summary>
    private static readonly Regex InlineValueProblem = new(
        $@"\b(?:Typed)?Results\s*\.\s*(?<factory>{ValueFactories})\s*(?:<[^>()]*>)?\s*\(\s*(?:[^;()]*?,\s*)?(?:\w+\s*:\s*)?new\s+(?:Microsoft\.AspNetCore\.(?:Mvc|Http)\.)?{ProblemTypes}\b",
        RegexOptions.Compiled);

    /// <summary>Rule 3 — a typed-result type argument that is a problem type.</summary>
    private static readonly Regex TypedValueProblem = new(
        $@"\b(?:BadRequest|NotFound|Conflict|UnprocessableEntity|JsonHttpResult|Ok|Accepted|Created|InternalServerError)\s*<\s*(?:Microsoft\.AspNetCore\.(?:Mvc|Http)\.)?{ProblemTypes}\s*>",
        RegexOptions.Compiled);

    /// <summary>Locals/fields declared as, or initialised with, a problem type (rule 2's first half).</summary>
    private static readonly Regex ProblemLocalDeclaration = new(
        $@"(?:\b{ProblemTypes}\??\s+(?<name>[A-Za-z_]\w*)\s*[=;,)])|(?:\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+{ProblemTypes}\b)",
        RegexOptions.Compiled);

    /// <summary>
    /// A method (or local function) DECLARED to return a problem type — <c>ProblemDetails</c>,
    /// <c>ProblemDetails?</c>, <c>ValidationProblemDetails</c>, or a <c>Task</c>/<c>ValueTask</c> of one. A
    /// <c>var</c> assigned from a call to one of these is a problem local (rule 2), e.g.
    /// <c>var p = materializer.EvaluatePolicy(...); return Results.BadRequest(p);</c>.
    /// </summary>
    private static readonly Regex ProblemReturningMethodDeclaration = new(
        $@"(?:\b(?:Task|ValueTask)\s*<\s*{ProblemTypes}\??\s*>|\b{ProblemTypes})\??\s+(?<method>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// String/char literals (kept, so a <c>"*/*"</c> Accept header cannot open a "comment"), line comments
    /// (kept here; <see cref="SourceScan.CodeText"/> blanks them), and block comments (blanked, newlines
    /// preserved so reported line numbers stay right). Leftmost-match semantics make a <c>/*</c> inside a
    /// string or after <c>//</c> part of that token, never the start of a block comment.
    /// </summary>
    private static readonly Regex LiteralOrComment = new(
        @"@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'|//[^\n]*|/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Every problem-returning method name declared anywhere in the BFF (computed once).</summary>
    private static readonly Lazy<IReadOnlySet<string>> BffProblemReturningMethods = new(() =>
        BffSourceFiles()
            .SelectMany(f => ProblemReturningMethods(StripComments(File.ReadAllText(f))))
            .ToHashSet(StringComparer.Ordinal));

    // =============================================================================================
    // The ban
    // =============================================================================================

    [Fact(DisplayName = "ADR-019 #975: no ProblemDetails returned as a plain JSON value in Sprk.Bff.Api")]
    public void NoProblemDetailsReturnedAsAPlainJsonValue()
    {
        var violations = ScanBff()
            .Where(v => !Allowlist.Any(a => v.File.EndsWith(a.FileName, StringComparison.Ordinal)
                                            && v.Text.Contains(a.Match, StringComparison.Ordinal)))
            .Select(v => $"{v.File}:{v.Line}: [{v.Rule}] {v.Text}")
            .ToList();

        Assert.True(
            violations.Count == 0,
            "ADR-019 violation (GitHub #975): a ProblemDetails is returned through a VALUE result, which "
            + "writes Content-Type application/json. RFC 7807 problem responses must be "
            + "application/problem+json.\n\n"
            + "Fix: return Results.Problem(problemDetails) / TypedResults.Problem(...) instead — and keep "
            + "Status set on the instance (a null Status becomes 500). For a typed signature use "
            + "ProblemHttpResult in place of BadRequest<ProblemDetails>, and declare the status with "
            + ".ProducesProblem(code).\n\n"
            + "Offending sites:\n" + string.Join("\n", violations));
    }

    [Fact(DisplayName = "ADR-019 #975: every allowlist entry carries a written reason and an ADR reference")]
    public void EveryAllowlistEntryCarriesAReasonAndAnAdrReference()
    {
        var unexplained = Allowlist
            .Where(e => string.IsNullOrWhiteSpace(e.Adr)
                        || string.IsNullOrWhiteSpace(e.Match)
                        || string.IsNullOrWhiteSpace(e.Reason)
                        || e.Reason.Trim().Length < 60)
            .Select(e => e.FileName)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every ProblemDetails content-type allowlist entry needs a match string, a substantive written "
            + "reason and an ADR citation. Entries missing one: " + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "ADR-019 #975: the scan actually reads the BFF source (not an empty directory)")]
    public void ScanCoversTheBffSource()
    {
        // A guard over zero files passes forever. The BFF has hundreds of endpoint files and dozens of
        // sanctioned Results.Problem(...) sites; if the scan sees neither, it is not looking at the BFF.
        var files = BffSourceFiles().ToList();
        Assert.True(files.Count > 100, $"Expected the Sprk.Bff.Api source tree; found {files.Count} .cs files.");
        Assert.Contains(files, f => f.EndsWith("RagEndpoints.cs", StringComparison.Ordinal));
        Assert.True(
            files.Sum(f => Regex.Matches(File.ReadAllText(f), @"\bResults\.Problem\(").Count) > 50,
            "Expected many sanctioned Results.Problem( sites in the BFF source.");
    }

    // =============================================================================================
    // Controls — a detector nobody has seen fire is a detector nobody knows works.
    // =============================================================================================

    [Theory(DisplayName = "ADR-019 #975: negative control — the detector fires on each seeded violation")]
    [InlineData("        return Results.BadRequest(new ProblemDetails\n        {\n            Title = \"x\",\n            Status = 400\n        });")]
    [InlineData("        return Results.NotFound(new ProblemDetails { Title = \"x\", Status = 404 });")]
    [InlineData("        return Results.Conflict(new ProblemDetails { Status = 409 });")]
    [InlineData("        return Results.UnprocessableEntity(new ValidationProblemDetails(errors));")]
    [InlineData("        return Results.Json(new ProblemDetails { Status = 400 }, statusCode: 400);")]
    [InlineData("        return Results.Ok(new HttpValidationProblemDetails(errors));")]
    [InlineData("        return TypedResults.BadRequest(new ProblemDetails { Status = 400 });")]
    [InlineData("        return Results.BadRequest<ProblemDetails>(new ProblemDetails { Status = 400 });")]
    [InlineData("        return Results.Json(value: new Microsoft.AspNetCore.Mvc.ProblemDetails { Status = 400 });")]
    [InlineData("        return Results.Created(\"/api/x\", new ProblemDetails { Status = 400 });")]
    [InlineData("        var problem = new ProblemDetails { Status = 400 };\n        return Results.BadRequest(problem);")]
    [InlineData("        ProblemDetails pd = Build();\n        return TypedResults.Json(pd, statusCode: 400);")]
    [InlineData("        var problem = new ProblemDetails { Status = 400 };\n        return Results.BadRequest(problem!);")]
    [InlineData("        var p = materializer.EvaluatePolicy(request, size);\n        return Results.BadRequest(p);")]
    [InlineData("    private ProblemDetails? Check(Request r) => null;\n    IResult H(Request r)\n    {\n        var p = Check(r);\n        return Results.BadRequest(p);\n    }")]
    [InlineData("    private static async Task<ProblemDetails?> CheckAsync() => null;\n    async Task<IResult> H()\n    {\n        var p = await this.CheckAsync();\n        return Results.Conflict(p!);\n    }")]
    [InlineData("        /* a block comment does not hide code after it */ return Results.NotFound(new ProblemDetails { Status = 404 });")]
    [InlineData("    static async Task<Results<Ok<Dto>, BadRequest<ProblemDetails>>> Handle() => default!;")]
    [InlineData("    static JsonHttpResult<ProblemDetails> Handle() => default!;")]
    public void Detector_NegativeControl_FiresOnSeededViolation(string seeded)
    {
        Assert.NotEmpty(Scan("Seeded.cs", seeded));
    }

    [Theory(DisplayName = "ADR-019 #975: positive control — sanctioned problem shapes are NOT flagged")]
    [InlineData("        return Results.Problem(new ProblemDetails\n        {\n            Title = \"x\",\n            Status = 400\n        });")]
    [InlineData("        return Results.Problem(title: \"Not Found\", detail: d, statusCode: 404);")]
    [InlineData("        return TypedResults.Problem(statusCode: 409, title: \"Locked\");")]
    [InlineData("        return Results.ValidationProblem(errors);")]
    [InlineData("        return ProblemDetailsHelper.ValidationError(\"bad\");")]
    [InlineData("        var problem = new ProblemDetails { Status = 400 };\n        return Results.Problem(problem);")]
    [InlineData("    static async Task<Results<Ok<Dto>, ProblemHttpResult>> Handle() => default!;")]
    [InlineData("        return Results.BadRequest(new { error = \"not a problem body\" });")]
    [InlineData("        return Results.Ok(details);")]
    [InlineData("        // return Results.BadRequest(new ProblemDetails { Status = 400 });")]
    [InlineData("        /// <c>Results.BadRequest(new ProblemDetails { … })</c> is the banned shape.")]
    [InlineData("        /* Never write\n           return Results.BadRequest(new ProblemDetails { Status = 400 });\n           — use Results.Problem. */")]
    [InlineData("        /** Results.NotFound(new ProblemDetails()) */ return Results.Problem(statusCode: 404);")]
    [InlineData("        var accept = \"*/*\"; return Results.Problem(statusCode: 400); var x = \"/*\";")]
    [InlineData("    private static ProblemDetails Problem(int s) => new() { Status = s };\n    IResult H()\n    {\n        var r = Results.Problem(statusCode: 400);\n        return Results.Ok(r);\n    }")]
    [InlineData("    private ProblemDetails? Check(Request r) => null;\n    IResult H(Request r)\n    {\n        var p = Check(r);\n        return p is null ? Results.Ok() : Results.Problem(p);\n    }")]
    public void Detector_PositiveControl_AcceptsSanctionedShapes(string sanctioned)
    {
        Assert.Empty(Scan("Sanctioned.cs", sanctioned));
    }

    [Fact(DisplayName = "ADR-019 #975: problem-returning BFF methods are collected (feeds the var-from-call rule)")]
    public void ProblemReturningMethodsAreCollectedFromTheBff()
    {
        // The var-from-call negative control above relies on EvaluatePolicy being in this set without the
        // seeded source declaring it — i.e. on the cross-file collection actually reading the BFF.
        Assert.Contains("EvaluatePolicy", BffProblemReturningMethods.Value);
    }

    [Fact(DisplayName = "ADR-019 #975: negative control — the converted files are clean on disk")]
    public void ConvertedFilesAreCleanOnDisk()
    {
        // Belt and braces against the real files, so the ban cannot pass on a stub while the six files
        // that carried the 44 sites regress on disk.
        var converted = new[]
        {
            "JobsEndpoints.cs", "MembershipAdminEndpoints.cs", "VisualizationEndpoints.cs",
            "SemanticSearchEndpoints.cs", "RecordSearchEndpoints.cs", "RagEndpoints.cs",
        };
        var hits = ScanBff().Where(v => converted.Any(c => v.File.EndsWith(c, StringComparison.Ordinal))).ToList();
        Assert.True(hits.Count == 0, "Converted file regressed:\n" + string.Join("\n", hits.Select(h => $"{h.File}:{h.Line}: {h.Text}")));
    }

    // =============================================================================================
    // Scanner
    // =============================================================================================

    private static IEnumerable<string> BffSourceFiles()
    {
        var bffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");
        return SourceScan.ServerSourceFiles()
            .Where(f => f.StartsWith(bffRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static List<Violation> ScanBff()
        => BffSourceFiles()
            .SelectMany(f => Scan(SourceScan.Relative(f), File.ReadAllText(f)))
            .ToList();

    private static List<Violation> Scan(string file, string source)
    {
        // Block comments, then line comments (and therefore /// doc comments), are blanked with line
        // structure preserved, so prose that DESCRIBES the banned shape — this file's own subject — is
        // never flagged.
        var code = StripComments(source);
        var found = new List<Violation>();

        foreach (Match m in InlineValueProblem.Matches(code))
        {
            found.Add(new Violation(file, SourceScan.LineOf(code, m.Index), "inline", Collapse(m.Value)));
        }

        foreach (Match m in TypedValueProblem.Matches(code))
        {
            found.Add(new Violation(file, SourceScan.LineOf(code, m.Index), "typed", Collapse(m.Value)));
        }

        var locals = ProblemLocalDeclaration.Matches(code)
            .Select(m => m.Groups["name"].Value)
            .Concat(LocalsAssignedFromProblemReturningCalls(code))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var name in locals)
        {
            // `!?` — a null-forgiving `Results.BadRequest(pd!)` is the same value result.
            var viaLocal = new Regex(
                $@"\b(?:Typed)?Results\s*\.\s*(?:{ValueFactories})\s*(?:<[^>()]*>)?\s*\(\s*(?:[^;()]*?,\s*)?(?:\w+\s*:\s*)?{Regex.Escape(name)}\s*!?\s*[,)]");
            foreach (Match m in viaLocal.Matches(code))
            {
                found.Add(new Violation(file, SourceScan.LineOf(code, m.Index), "local", Collapse(m.Value)));
            }
        }

        return found;
    }

    /// <summary>
    /// <c>var x = [await] [receiver.]Method(</c> where <c>Method</c> is declared (in the BFF, or in the scanned
    /// source itself) to return a problem type. A call on <c>Results</c>/<c>TypedResults</c> is excluded: their
    /// <c>Problem(...)</c> returns an <c>IResult</c>, and a private helper that happens to be named
    /// <c>Problem</c> must not turn every <c>var r = Results.Problem(...)</c> into a "problem local".
    /// </summary>
    private static IEnumerable<string> LocalsAssignedFromProblemReturningCalls(string code)
    {
        var methods = BffProblemReturningMethods.Value
            .Concat(ProblemReturningMethods(code))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (methods.Count == 0)
        {
            yield break;
        }

        var assignedFromCall = new Regex(
            $@"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:await\s+)?(?:[A-Za-z_][\w?!]*\s*\.\s*)*(?<!Results\s*\.\s*)(?:{string.Join("|", methods.Select(Regex.Escape))})\s*(?:<[^<>()]*>)?\s*\(");
        foreach (Match m in assignedFromCall.Matches(code))
        {
            yield return m.Groups["name"].Value;
        }
    }

    private static IEnumerable<string> ProblemReturningMethods(string code)
        => ProblemReturningMethodDeclaration.Matches(code).Select(m => m.Groups["method"].Value);

    /// <summary>Blanks block comments (newlines kept), then line comments, leaving line structure intact.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = LiteralOrComment.Replace(source, m =>
            m.Value.StartsWith("/*", StringComparison.Ordinal)
                ? Regex.Replace(m.Value, @"[^\n]", " ")
                : m.Value);
        return SourceScan.CodeText(withoutBlocks.Split('\n'));
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private sealed record Violation(string File, int Line, string Rule, string Text);

    private sealed record AllowlistEntry(string FileName, string Match, string Adr, string Reason);
}
