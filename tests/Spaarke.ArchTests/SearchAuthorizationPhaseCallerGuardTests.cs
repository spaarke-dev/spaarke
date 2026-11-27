using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE SEARCH ROUTES' INTERNAL PHASE METHODS HAVE FOUR CALLERS (spaarkeai-mcp-bff-api-r2 task 027, ADR-008 / ADR-028 A10).
///
/// <para><b>Why.</b> Each search route's access decision is two phases. The request phase (tenant, caller, scope) runs in an
/// endpoint filter and publishes its decision; the result phase runs after the search and authorizes every returned row.
/// <c>SemanticSearchAuthorizationFilter.SearchAndAuthorizeResultsAsync</c>, <c>SemanticSearchAuthorizationFilter.DecideCount</c>
/// and <c>RecordSearchAuthorizationFilter.SearchAndAuthorizeRowsAsync</c> are the result-phase halves. They are
/// <c>internal</c>, which is not a boundary inside one assembly: an MCP tool in <c>Api/Mcp/**</c> could call one with a
/// <c>SemanticSearchAuthorization</c> it built by hand (a public record with public init setters), skip the request phase and
/// have rows authorized against a decision nobody made. The sanctioned entry for a host that is not the route is the PUBLIC
/// <c>SearchDocumentsAsCallerAsync</c> / <c>SearchRecordsAsCallerAsync</c>, which run both phases in order and never accept a
/// pre-built decision (verifier finding F2 on task 027, promoted to a guard).</para>
///
/// <para><b>Allowed, with the reason for each</b> (ADR-008: the filter decides, the handler enforces what the filter published):</para>
/// <list type="bullet">
/// <item><c>Api/Ai/SemanticSearchEndpoints.cs</c> and <c>Api/Ai/RecordSearchEndpoints.cs</c> — the REST handlers run after the
/// route's filter has published its request decision on <c>HttpContext.Items</c>; they refuse to run without it (the forcing
/// function), so the decision they hand to the result phase is the filter's own.</item>
/// <item><c>Api/Filters/SemanticSearchAuthorizationFilter.cs</c> and <c>Api/Filters/RecordSearchAuthorizationFilter.cs</c> — the
/// definitions, and the public composers that run the request phase first.</item>
/// </list>
///
/// <para><b>Limits, stated exactly</b>: a source scan of <c>src/server/**</c>, allowing the four files by repo-relative path.
/// It sees the two unique phase names anywhere (qualified, <c>using static</c>, a delegate or <c>nameof</c>), the qualified
/// <c>DecideCount</c> (a bare <c>DecideCount</c> is too common a name to flag), a quoted name of any of the three (reflection by
/// string), and a <c>using static</c> or <c>using</c> alias of either filter type, plain or <c>global</c>. An alias is flagged at
/// its declaration, so a call through it (<c>Flt.DecideCount(...)</c>) needs no separate rule. A bare <c>DecideCount(...)</c> from a
/// class derived from a filter is closed by a different mechanism, not by this scan: both filter classes are <c>sealed</c>, and
/// <see cref="BothSearchFiltersAreSealed"/> keeps them so. A call through reflection that builds the name at run time, or a
/// delegate captured inside an allowed file and handed out, is not seen. Tests (<c>tests/**</c>) are not scanned: they reach the
/// internals through <c>InternalsVisibleTo</c> and are not a production caller.</para>
///
/// <para><b>Maintenance procedure.</b> This test failed because a file other than the four above names one of the three phase
/// methods. Do not add the file to <see cref="AllowedFiles"/>. Call the public method instead —
/// <c>SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync</c> or <c>RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync</c>
/// with the request's own <c>HttpContext</c> and your search delegate — so the request phase runs first and its decision is the
/// one the result phase enforces. Add a file to <see cref="AllowedFiles"/> only for a new REST route whose endpoint filter
/// publishes the request decision and whose handler refuses to run without it; give it a one-line reason and an ADR citation in
/// the list above, and keep the control cases below passing. If a phase method is renamed or a fourth is added, update
/// <see cref="PhaseNameCall"/> / <see cref="QualifiedDecideCount"/> / <see cref="QuotedName"/> in the same change and extend the
/// must-fire controls to match.</para>
/// </summary>
public class SearchAuthorizationPhaseCallerGuardTests
{
    // Repo-relative paths, '/'-separated: a file of the same name elsewhere is not allowed.
    private static readonly string[] AllowedFiles =
    [
        "src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs",
        "src/server/api/Sprk.Bff.Api/Api/Ai/RecordSearchEndpoints.cs",
        "src/server/api/Sprk.Bff.Api/Api/Filters/SemanticSearchAuthorizationFilter.cs",
        "src/server/api/Sprk.Bff.Api/Api/Filters/RecordSearchAuthorizationFilter.cs",
    ];

    // The two endpoint files whose handlers are the sanctioned REST callers; the non-vacuous check counts their calls.
    private static readonly string[] EndpointFiles =
    [
        "src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs",
        "src/server/api/Sprk.Bff.Api/Api/Ai/RecordSearchEndpoints.cs",
    ];

    private static readonly Regex PhaseNameCall = new(
        @"\b(?<name>SearchAndAuthorizeResultsAsync|SearchAndAuthorizeRowsAsync)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QualifiedDecideCount = new(
        @"\b(?:Semantic|Record)SearchAuthorizationFilter\s*\.\s*(?<name>DecideCount)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex QuotedName = new(
        @"""(?<name>SearchAndAuthorizeResultsAsync|SearchAndAuthorizeRowsAsync|DecideCount)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UsingStaticOrAlias = new(
        @"^\s*(?:global\s+)?using\s+(?:static\s+|\w+\s*=\s*)(?:global::)?(?:Sprk\.Bff\.Api\.Api\.Filters\.)?(?<type>SemanticSearchAuthorizationFilter|RecordSearchAuthorizationFilter)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    /// <summary>"what" for every reference to a phase method (or a using static / alias of a filter) in <paramref name="code"/> (comments stripped).</summary>
    internal static List<string> ViolationsIn(string code)
    {
        var found = PhaseNameCall.Matches(code).Select(m => m.Groups["name"].Value).ToList();
        found.AddRange(QualifiedDecideCount.Matches(code).Select(m => m.Groups["name"].Value));
        found.AddRange(QuotedName.Matches(code).Select(m => "\"" + m.Groups["name"].Value + "\""));
        found.AddRange(UsingStaticOrAlias.Matches(code).Select(m => "using static/alias " + m.Groups["type"].Value));
        return found;
    }

    [Fact(DisplayName = "r2 task 027: only the two search endpoints and the two search filters reference the internal search phase methods")]
    public void OnlyTheRestHandlersAndTheFiltersReferenceThePhaseMethods()
    {
        var violations = new List<string>();
        var endpointReferences = 0;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            var found = ViolationsIn(code);
            if (found.Count == 0)
            {
                continue;
            }

            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (AllowedFiles.Contains(relative, StringComparer.Ordinal))
            {
                if (EndpointFiles.Contains(relative, StringComparer.Ordinal))
                {
                    endpointReferences += found.Count;
                }

                continue;
            }

            violations.AddRange(found.Select(f => $"{SourceScan.Relative(file)}: {f}"));
        }

        Assert.True(
            violations.Count == 0,
            "These reference a search route's internal phase method. Those methods are the RESULT half of the access decision and "
            + "assume the request phase (tenant, caller, scope) already ran and published its decision, which only the REST filter + "
            + "handler pair guarantees. Any other host (an MCP tool above all) must call SemanticSearchAuthorizationFilter."
            + "SearchDocumentsAsCallerAsync or RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync, which run both phases.\n\n  "
            + string.Join("\n  ", violations));

        // Non-vacuous: the three REST handler calls (documents, count, records) are seen, and allowed.
        Assert.True(endpointReferences >= 3, $"expected the three REST handler calls in the two search endpoint files, saw {endpointReferences}");
    }

    [Fact(DisplayName = "r2 task 027 controls: a phase method named from another file fires; the sanctioned public methods and the route's own shapes do not")]
    public void PhaseCallerGuard_Controls()
    {
        // Must fire (a source snippet "from another file" — the scan is run on a string, not on the allowed files).
        Assert.Equal(
            ["SearchAndAuthorizeResultsAsync"],
            ViolationsIn("var r = await SemanticSearchAuthorizationFilter.SearchAndAuthorizeResultsAsync(ctx, req, auth, tid, search, svc, trim, log, ct);"));
        Assert.Equal(
            ["SearchAndAuthorizeRowsAsync"],
            ViolationsIn("await Sprk.Bff.Api.Api.Filters.RecordSearchAuthorizationFilter . SearchAndAuthorizeRowsAsync (http, req, search, svc, log, ct);"));
        Assert.Equal(
            ["DecideCount"],
            ViolationsIn("var c = SemanticSearchAuthorizationFilter.DecideCount(request, hand, logger);"));
        Assert.Contains(
            "SearchAndAuthorizeRowsAsync",
            ViolationsIn("using static Sprk.Bff.Api.Api.Filters.RecordSearchAuthorizationFilter;\nvar r = await SearchAndAuthorizeRowsAsync(http, req, s, a, l, ct);"));
        Assert.Contains(
            "using static/alias RecordSearchAuthorizationFilter",
            ViolationsIn("using static Sprk.Bff.Api.Api.Filters.RecordSearchAuthorizationFilter;\nclass X {}"));
        Assert.Contains(
            "using static/alias SemanticSearchAuthorizationFilter",
            ViolationsIn("using F = SemanticSearchAuthorizationFilter;\nclass X {}"));
        // global using forms (a project-wide alias or static import is the widest reach one line can have).
        Assert.Contains(
            "using static/alias SemanticSearchAuthorizationFilter",
            ViolationsIn("global using static Sprk.Bff.Api.Api.Filters.SemanticSearchAuthorizationFilter;\nclass X {}"));
        Assert.Contains(
            "using static/alias RecordSearchAuthorizationFilter",
            ViolationsIn("global using static global::Sprk.Bff.Api.Api.Filters.RecordSearchAuthorizationFilter;"));
        Assert.Contains(
            "using static/alias SemanticSearchAuthorizationFilter",
            ViolationsIn("global using Flt = Sprk.Bff.Api.Api.Filters.SemanticSearchAuthorizationFilter;\nclass X { int n = Flt.DecideCount(a, b, c); }"));
        Assert.Equal(["\"DecideCount\""], ViolationsIn("typeof(SemanticSearchAuthorizationFilter).GetMethod(\"DecideCount\")"));
        Assert.Equal(
            ["SearchAndAuthorizeResultsAsync"],
            ViolationsIn("var n = nameof(SemanticSearchAuthorizationFilter.SearchAndAuthorizeResultsAsync);"));

        // Must not fire (the sanctioned public entries, and unrelated names).
        Assert.Empty(ViolationsIn(
            "var a = await SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync(http, request, search, ct);"
            + " var b = await RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync(http, request, search, ct);"));
        Assert.Empty(ViolationsIn("var n = Counter.DecideCount(x); static int DecideCount(int a) => a;"));
        Assert.Empty(ViolationsIn("using Sprk.Bff.Api.Api.Filters;\nvar f = new SemanticSearchAuthorizationFilter(logger);"));
        Assert.Empty(ViolationsIn("global using Sprk.Bff.Api.Api.Filters;\nvar f = new RecordSearchAuthorizationFilter(logger);"));
    }

    [Fact(DisplayName = "r2 task 027: both search filters are sealed, so no subclass can call the internal DecideCount / phase methods unqualified")]
    public void BothSearchFiltersAreSealed()
    {
        // The scan above cannot see a bare DecideCount(...) inside a derived class (the name is too common to flag). Sealing the
        // two filter classes removes that route instead of trying to detect it. If a test needs a double, extract an interface;
        // do not unseal.
        Assert.True(typeof(Sprk.Bff.Api.Api.Filters.SemanticSearchAuthorizationFilter).IsSealed, "SemanticSearchAuthorizationFilter must stay sealed");
        Assert.True(typeof(Sprk.Bff.Api.Api.Filters.RecordSearchAuthorizationFilter).IsSealed, "RecordSearchAuthorizationFilter must stay sealed");
    }
}
