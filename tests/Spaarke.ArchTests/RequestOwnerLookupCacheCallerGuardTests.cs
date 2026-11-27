using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part D1 (#3184), binding condition 6: the per-request <c>RecordOwnerLookupCache</c> is built only by the
/// child-records route. <c>RecordOwnerLookupCache.ForRequest(httpContext)</c> is called from exactly one production file,
/// <c>Api/ChildRecordEndpoints.cs</c> (its <c>CreateAsync</c>), and only that file hands a create one
/// (<c>RecordCreationRequest.OwnerConfigurationReads = …</c>).
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The request's instance keeps the business-unit and team configuration answers for every create of one
/// request. It is safe because that route is one request, and nothing its creates or the decision commit run writes a team or
/// business unit (<see cref="DecisionCommitTeamWriteGuardTests"/>). A second builder (a job, a repair pass, a background
/// service) could keep those answers across work nobody audited for it; field-mapping-r1 condition 1: Repair, jobs and every
/// create not via <c>ChildRecordEndpoints</c> get no cache. A new caller is a deliberate edit of this guard with a reason.</para>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (comments stripped, as
/// <see cref="RequestMetadataScopeCallerGuardTests"/> does) for <c>RecordOwnerLookupCache.ForRequest(</c> and for an assignment
/// to <c>OwnerConfigurationReads</c>. The definition (<c>RecordOwnerLookupCache? ForRequest(HttpContext? request)</c>) and the property
/// declaration are not those shapes. A call through reflection, a <c>using static</c> or a method group is not seen; none exists.
/// A reconciler pass's own cache (<c>new RecordOwnerLookupCache()</c>) is not a request's and is not this guard's. Enforced by:
/// this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class RequestOwnerLookupCacheCallerGuardTests
{
    private const string AllowedCaller = "src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs";

    private static readonly Regex BuildCall = new(
        @"\bRecordOwnerLookupCache\s*\.\s*ForRequest\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HandOver = new(@"\bOwnerConfigurationReads\s*=(?![=>])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    private static string Code(string source) => SourceScan.CodeText(BlockComment.Replace(source, " ").Split('\n'));

    /// <summary>The number of <c>RecordOwnerLookupCache.ForRequest(</c> calls in <paramref name="source"/>'s code.</summary>
    internal static int BuildCalls(string source) => BuildCall.Matches(Code(source)).Count;

    /// <summary>The number of assignments to <c>OwnerConfigurationReads</c> in <paramref name="source"/>'s code.</summary>
    internal static int HandOvers(string source) => HandOver.Matches(Code(source)).Count;

    [Fact(DisplayName = "ISS-010 D1: RecordOwnerLookupCache.ForRequest is called, and OwnerConfigurationReads set, in ChildRecordEndpoints.cs only")]
    public void OnlyTheChildRecordsRoute_BuildsARequestOwnerLookupCache()
    {
        var problems = new List<string>();
        var builds = 0;
        var handOvers = 0;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            var b = BuildCalls(source);
            var h = HandOvers(source);
            if (b == 0 && h == 0)
                continue;

            if (relative == AllowedCaller)
            {
                builds += b;
                handOvers += h;
                continue;
            }

            if (b > 0)
                problems.Add($"{relative}: {b} call(s) of RecordOwnerLookupCache.ForRequest outside the child-records route");
            if (h > 0)
                problems.Add($"{relative}: {h} assignment(s) of OwnerConfigurationReads outside the child-records route");
        }

        if (builds != 1 || handOvers != 1)
        {
            problems.Add($"{AllowedCaller}: {builds} ForRequest call(s) and {handOvers} OwnerConfigurationReads assignment(s), expected one each: "
                + "the scan lost its signal or the route no longer opts in");
        }

        Assert.True(problems.Count == 0,
            "The per-request owner lookup cache keeps business-unit and team answers for one request, so only the child-records "
            + "route builds it and hands it to a create (ISS-010 D1):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ISS-010 D1 guard control (must fire): a build or a hand-over in any other file is counted")]
    public void Control_ACallElsewhere_IsCounted()
    {
        Assert.Equal(1, BuildCalls("OwnerConfigurationReads = RecordOwnerLookupCache.ForRequest(httpContext),"));
        Assert.Equal(1, BuildCalls("var c = Sprk.Bff.Api.Services.Dataverse.RecordOwnerLookupCache\n    .ForRequest (ctx);"));
        Assert.Equal(1, HandOvers("new RecordCreationRequest(t, p) { OwnerConfigurationReads = cache }"));
        Assert.Equal(1, HandOvers("request = request with { OwnerConfigurationReads= new RecordOwnerLookupCache() };"));

        var relative = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordRepairPassJob.cs";
        Assert.NotEqual(AllowedCaller, relative);
        Assert.True(BuildCalls("class Job { void Run() { var c = RecordOwnerLookupCache.ForRequest(http); } }") > 0);
    }

    [Fact(DisplayName = "ISS-010 D1 guard control (must not fire): the definition, the property, a read, a comment and another name")]
    public void Control_NonCalls_AreNotCounted()
    {
        Assert.Equal(0, BuildCalls("    internal static RecordOwnerLookupCache? ForRequest(HttpContext? request)"));
        Assert.Equal(0, HandOvers("    public RecordOwnerLookupCache? OwnerConfigurationReads { get; init; }"));
        Assert.Equal(0, HandOvers("secureOwnerTeamAnswer ??= _shares.SecureOwnerTeamAsync(token, request.OwnerConfigurationReads);"));
        Assert.Equal(0, HandOvers("if (request.OwnerConfigurationReads == null) { }"));
        Assert.Equal(0, HandOvers("x => x.OwnerConfigurationReads => y"));
        Assert.Equal(0, HandOvers("LookupCache = ownerLookups,"));
        Assert.Equal(0, BuildCalls("// OwnerConfigurationReads = RecordOwnerLookupCache.ForRequest(httpContext),"));
        Assert.Equal(0, BuildCalls("/// <see cref=\"RecordOwnerLookupCache.ForRequest(HttpContext)\"/> builds it"));
        Assert.Equal(0, BuildCalls("/* RecordOwnerLookupCache.ForRequest(ctx) */ var x = 1;"));
        Assert.Equal(0, BuildCalls("var a = RecordOwnerLookupCache.ForRequests(ctx); var b = OtherCache.ForRequest(ctx); var c = new RecordOwnerLookupCache();"));
    }
}
