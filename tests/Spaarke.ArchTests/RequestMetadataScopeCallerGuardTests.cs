using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 (#3184), field-mapping-r1 review K2: <c>CallerMetadataReadMemo.RequestScope.Of(httpContext)</c> is called from exactly
/// one production file, <c>Api/ChildRecordEndpoints.cs</c> (the child-records route's <c>CreateAsync</c>).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The request-scoped table-metadata store is shared by every create of one request over one caller's
/// <c>DataverseUserClient</c>. It is safe only because the route that opts in is one request, one caller, one bearer token. A
/// second caller of <c>RequestScope.Of</c> (a job, a background service, a handler running across callers) could hand one
/// caller's metadata answers to another or keep them past the request. A new caller is therefore a deliberate edit of this guard
/// with a reason, not an accident.</para>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text for the call shape
/// <c>RequestScope.Of(</c> (qualified by <c>CallerMetadataReadMemo.</c> or not). Line comments (<c>//</c>, so XML docs too)
/// and block comments are stripped first, so a <c>&lt;see cref="RequestScope.Of"/&gt;</c> or prose mention is not a call. The
/// definition in <c>CallerMetadataReadMemo.cs</c> (<c>RequestScope? Of(HttpContext? request)</c>) is not the call shape, so that
/// file needs no exclusion (the must-not-fire control pins this). A call through reflection, a <c>using static</c>, a method group
/// built elsewhere or a string literal that contains the text is not seen or is over-counted; none exists today. Enforced by:
/// this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class RequestMetadataScopeCallerGuardTests
{
    private const string AllowedCaller = "src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs";

    private static readonly Regex ScopeCall = new(
        @"\bRequestScope\s*\.\s*Of\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The number of <c>RequestScope.Of(</c> calls in <paramref name="source"/>'s code (comments excluded).</summary>
    internal static int ScopeCalls(string source) =>
        ScopeCall.Matches(SourceScan.CodeText(BlockComment.Replace(source, " ").Split('\n'))).Count;

    [Fact(DisplayName = "ISS-010 K2: RequestScope.Of is called from ChildRecordEndpoints.cs and nowhere else")]
    public void OnlyTheChildRecordsRoute_OpensARequestMetadataScope()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var calls = ScopeCalls(File.ReadAllText(file));
            if (calls == 0)
                continue;

            seen.Add(relative);
            if (relative != AllowedCaller)
                problems.Add($"{relative}: {calls} call(s) of CallerMetadataReadMemo.RequestScope.Of outside the child-records route");
        }

        if (!seen.Contains(AllowedCaller))
            problems.Add($"{AllowedCaller}: listed but calls RequestScope.Of nowhere: the scan lost its signal or the route no longer opts in");

        Assert.True(problems.Count == 0,
            "The request-scoped metadata store is shared across every create of one request, so only the child-records route "
            + "(one request, one caller) may open it (ISS-010, #3184):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ISS-010 K2 guard control (must fire): a call from any other file is counted")]
    public void Control_ACallElsewhere_IsCounted()
    {
        Assert.Equal(1, ScopeCalls("MetadataReads = CallerMetadataReadMemo.RequestScope.Of(httpContext),"));
        Assert.Equal(1, ScopeCalls("var scope = RequestScope.Of(ctx);"));
        Assert.Equal(1, ScopeCalls("var scope = CallerMetadataReadMemo.RequestScope\n    .Of (ctx);"));

        // The scan is over a file's text, so the same text in a file named otherwise is a violation of the real scan.
        var relative = "src/server/api/Sprk.Bff.Api/Services/Jobs/SomeJob.cs";
        Assert.NotEqual(AllowedCaller, relative);
        Assert.True(ScopeCalls("class SomeJob { void Run() { var s = CallerMetadataReadMemo.RequestScope.Of(http); } }") > 0);
    }

    [Fact(DisplayName = "ISS-010 K2 guard control (must not fire): the definition, a comment, an XML doc and another name are not calls")]
    public void Control_NonCalls_AreNotCounted()
    {
        // The definition inside CallerMetadataReadMemo.cs.
        Assert.Equal(0, ScopeCalls("        internal static RequestScope? Of(HttpContext? request)"));
        // The type used as a parameter or property, as RecordCreationService and the request record do.
        Assert.Equal(0, ScopeCalls("public CallerMetadataReadMemo.RequestScope? MetadataReads { get; init; }"));
        Assert.Equal(0, ScopeCalls("internal static CallerMetadataReadMemo For(IDataverseUserClient inner, RequestScope? requestReads) =>"));
        // Line comment, XML doc, block comment.
        Assert.Equal(0, ScopeCalls("// MetadataReads = CallerMetadataReadMemo.RequestScope.Of(httpContext),"));
        Assert.Equal(0, ScopeCalls("/// <see cref=\"CallerMetadataReadMemo.RequestScope.Of(HttpContext)\"/> opens the scope"));
        Assert.Equal(0, ScopeCalls("/* var s = RequestScope.Of(ctx); */ var x = 1;"));
        Assert.Equal(0, ScopeCalls("/*\n RequestScope.Of(ctx)\n*/"));
        // Another name.
        Assert.Equal(0, ScopeCalls("var s = OtherRequestScope.Of(ctx); var t = RequestScopes.Of(ctx); var u = RequestScope.Offer(ctx);"));
    }
}
