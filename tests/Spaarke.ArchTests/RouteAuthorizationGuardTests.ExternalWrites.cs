using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Owner round 59 item 2, mechanised (unified-access-control-r2 task 036 fix round 1; CLAUDE.md §16 enforcement ladder):
/// <b>every write route on the <c>/api/v1/external</c> group confirms the write against Dataverse</b>. A
/// <c>MapPost</c> / <c>MapPatch</c> / <c>MapPut</c> / <c>MapDelete</c> on that group either reaches
/// <c>ExternalProjectDataEndpoints.WithDataverseRightsAsync</c> (the ONE confirmation: for a caller whose access Dataverse
/// decides, the caller's own Dataverse rights on the target record, asked as the caller) from its handler, or is listed in
/// <see cref="ExternalWriteExemptions"/> with the reason it rests on no membership (task 036 notes §2.4).
/// </summary>
/// <remarks>
/// <para><b>How "reaches" is read.</b> From the registration's handler (a method group, or a lambda's body), the scanner
/// follows called method NAMES through the external-access endpoint sources (<c>Api/ExternalAccess/</c>), at most
/// <see cref="MaxExternalWriteHops"/> hops deep, and passes when a body contains the confirmation as a whole identifier.
/// Code only: comments and string literals are blanked by the lexer, so a comment naming the call credits nothing.</para>
/// <para><b>What this rule does NOT catch, stated plainly.</b> Presence is not correctness. Name-based resolution can
/// over-approximate (a same-named helper elsewhere in <c>Api/ExternalAccess/</c>), and a confirmation on the wrong record,
/// or one reached only on some branches (the message route skips a service request by design), passes. The behavioural
/// tests of task 036 (<c>Fr20_*</c>) are the proof; this guard makes a NEW write route that forgets the confirmation
/// impossible to merge silently, and makes every exemption a written, reviewable line.</para>
/// </remarks>
public partial class RouteAuthorizationGuardTests
{
    /// <summary>
    /// The live route census — every registered route's key and the BFF-relative file that registers it, from the scanner's
    /// whole-BFF scan (<c>Program.cs</c> and <c>Infrastructure/</c> included). Exposed for
    /// <c>ExternalPlaneNoOboGuardTests</c>' scope control (task 036 fix round 4, K1).
    /// </summary>
    internal static IReadOnlyList<(string Key, string File)> LiveRouteCensus() =>
        Real.Live.Select(r => (r.Key, r.File)).ToList();

    /// <summary>The one Dataverse write confirmation on the external surface.</summary>
    private const string DataverseWriteConfirmation = "WithDataverseRightsAsync";

    /// <summary>Upload is the deepest real path: adapter → UploadDocumentForParent → RightsForDocumentParentAsync →
    /// RightsForRootWriteAsync → the confirmation (4). One spare.</summary>
    private const int MaxExternalWriteHops = 5;

    private const string ExternalGroupPrefix = "/api/v1/external/";

    private static readonly string[] ExternalWriteVerbs = { "POST ", "PATCH ", "PUT ", "DELETE " };

    /// <summary>
    /// The write routes on <c>/api/v1/external</c> that are NOT confirmed against Dataverse, each with its reason (task 036
    /// notes §2.4, the not-applicable rows; owner decision (e) accepted 2026-10-17 by the main session under the round-75
    /// delegation). None of them rests on membership, so none can over-grant the way round 59 targets.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExternalWriteExemptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["POST /api/v1/external/servicerequests"] =
                "intake: files a NEW request as the caller's own contact; no existing record to confirm against; gated by "
                + "the Service Requests module entitlement, never membership (ExternalIntakeEndpoints).",
            ["POST /api/v1/external/servicerequests/{id:guid}/documents"] =
                "requester rule: the caller's own service request (sprk_requestedby), explicit access Dataverse cannot see, "
                + "like the B-17 contact grants; not the membership guess (decision (e), task 036).",
            ["DELETE /api/v1/external/servicerequests/{id:guid}"] =
                "requester rule: withdrawing the caller's own request (decision (e), task 036).",
            ["POST /api/v1/external/notifications/seen"] =
                "the caller's own last-seen marker; no core record is written (ExternalNotificationFeedEndpoints).",
            ["POST /api/v1/external/registration"] =
                "the caller's own identity link (ADR-028 A9); no core record (ExternalRegistrationEndpoints).",
            ["POST /api/v1/external/workforce/link"] =
                "the caller's own workforce identity link (ADR-028 A9); no core record (ExternalWorkforceLinkEndpoints).",
            ["POST /api/v1/external/contact-grants"] =
                "unreachable by a systemuser: ContactGrantorAuthorizationFilter refuses one, so no Dataverse-decided caller "
                + "reaches it; a contact grantor's level is a grant (B-17), not membership.",
            ["POST /api/v1/external/contact-grants/revoke"] =
                "unreachable by a systemuser (ContactGrantorAuthorizationFilter), as the grant route.",
            ["POST /api/v1/external/matters/{id:guid}/invoice-submissions"] =
                "unreachable by a systemuser: ExternalInvoiceSubmissionAuthorizationFilter refuses any principal with a SystemUserId "
                + "(UseInternalRoute) before any read or write, and WithDataverseRightsAsync confirms only Dataverse-decided (systemuser) "
                + "callers; a contact's submit rests on its composed post-veto Read+Create on the record, its conferring firm and the firm's "
                + "link to the record (ebilling-r1 044/051), not membership.",
            ["POST /api/v1/external/projects/{id:guid}/invoice-submissions"] =
                "unreachable by a systemuser: ExternalInvoiceSubmissionAuthorizationFilter refuses any principal with a SystemUserId "
                + "(UseInternalRoute) before any read or write, and WithDataverseRightsAsync confirms only Dataverse-decided (systemuser) "
                + "callers; a contact's submit rests on its composed post-veto Read+Create on the record, its conferring firm and the firm's "
                + "link to the record (ebilling-r1 044/051), not membership.",
            ["POST /api/v1/external/workassignments/{id:guid}/invoice-submissions"] =
                "unreachable by a systemuser: ExternalInvoiceSubmissionAuthorizationFilter refuses any principal with a SystemUserId "
                + "(UseInternalRoute) before any read or write, and WithDataverseRightsAsync confirms only Dataverse-decided (systemuser) "
                + "callers; a contact's submit rests on its composed post-veto Read+Create on the record, its conferring firm and the firm's "
                + "link to the record (ebilling-r1 044/051), not membership.",
            ["POST /api/v1/external/api/dataverse/fetch"] =
                "a READ: the module data query (FetchXML in the body, so POST), scoped to the caller's accessible set; it "
                + "writes nothing (ExternalModuleDataEndpoints).",
        };

    [Fact]
    public void EveryExternalWriteRoute_ReachesTheDataverseConfirmation_OrIsAnExemptionWithAReason()
    {
        var violations = ExternalWriteViolations(Real.Live, ExternalAccessEndpointUnits(), ExternalWriteExemptions);

        Assert.True(violations.Count == 0,
            "Owner round 59 item 2 (task 036): every write on /api/v1/external confirms against Dataverse through "
            + $"{DataverseWriteConfirmation}, or is exempt with a reason in ExternalWriteExemptions.\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void ExternalWriteExemptions_EachNameALiveWriteRoute_AndCarryAReason()
    {
        var writeKeys = Real.Live.Where(IsExternalWrite).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var stale = ExternalWriteExemptions.Keys.Where(k => !writeKeys.Contains(k)).ToList();
        var unreasoned = ExternalWriteExemptions.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();

        Assert.True(stale.Count == 0, "Exemptions that name no live write route (delete them):\n" + string.Join("\n", stale));
        Assert.True(unreasoned.Count == 0, "Exemptions without a reason:\n" + string.Join("\n", unreasoned));
        Assert.True(writeKeys.Count > ExternalWriteExemptions.Count,
            "the census found fewer external write routes than exemptions: the group prefix no longer matches the scan");
    }

    [Fact]
    public void ExternalWriteGuard_FiresOnAWriteRouteWithoutTheConfirmation_AndNotOnOneThatReachesIt()
    {
        const string endpoints = """
            namespace Fake;

            public static class FakeExternalEndpoints
            {
                public static void MapFakeExternal(this WebApplication app)
                {
                    var externalGroup = app.MapGroup("/api/v1/external").RequireAuthorization("ExternalCollaboration");
                    externalGroup.MapPost("/projects/{id:guid}/widgets", CreateWidget);
                    externalGroup.MapDelete("/widgets/{id:guid}", DeleteWidget);
                    externalGroup.MapPatch("/widgets/{id:guid}", (Guid id) => PatchWidgetCore(id));
                    externalGroup.MapPut("/widgets/{id:guid}/name", RenameWidget);
                    externalGroup.MapGet("/projects/{id:guid}/widgets", ListWidgets);
                }

                private static Task<IResult> CreateWidget(Guid id) => CreateWidgetCore(id);

                private static async Task<IResult> CreateWidgetCore(Guid id)
                {
                    var rights = await RightsForWidgetAsync(id);
                    return Results.Ok(rights);
                }

                private static Task<int> RightsForWidgetAsync(Guid id) =>
                    ExternalProjectDataEndpoints.WithDataverseRightsAsync(null!, default, "sprk_widgets", id, null, null!, default);

                private static Task<IResult> PatchWidgetCore(Guid id) => Task.FromResult(Results.Ok(RightsForWidgetAsync(id)));

                // Names the confirmation only in a comment and a string: WithDataverseRightsAsync must not credit it.
                private static Task<IResult> DeleteWidget(Guid id) => Task.FromResult(Results.Ok("WithDataverseRightsAsync"));

                private static Task<IResult> RenameWidget(Guid id) => Task.FromResult(Results.Ok());

                private static Task<IResult> ListWidgets(Guid id) => Task.FromResult(Results.Ok());
            }
            """;

        var routes = ScanFixtures(
            new[] { ("Api/ExternalAccess/FakeExternalEndpoints.cs", endpoints), ("Program.cs", "var app = builder.Build();\napp.MapFakeExternal();") },
            "Api/ExternalAccess/FakeExternalEndpoints.cs");
        Assert.DoesNotContain(routes, r => r.Unparseable);
        var units = routes.Select(r => r.Unit).OfType<SourceUnit>().Distinct().ToList();

        var violations = ExternalWriteViolations(routes, units, new Dictionary<string, string>(StringComparer.Ordinal));

        // Must fire: a DELETE whose handler names the confirmation only in a literal, and a PUT that never reaches it.
        Assert.Contains(violations, v => v.StartsWith("DELETE /api/v1/external/widgets/{id:guid}", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.StartsWith("PUT /api/v1/external/widgets/{id:guid}/name", StringComparison.Ordinal));
        // Must not fire: a POST reaching it through two named hops, a PATCH lambda reaching it through one, and a GET.
        Assert.Equal(2, violations.Count);

        // An exemption with a reason silences exactly its route.
        var exempt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PUT /api/v1/external/widgets/{id:guid}/name"] = "fixture reason",
        };
        Assert.Single(ExternalWriteViolations(routes, units, exempt));
    }

    private static bool IsExternalWrite(RouteRegistration route) =>
        ExternalWriteVerbs.Any(verb => route.Key.StartsWith(verb + ExternalGroupPrefix, StringComparison.Ordinal));

    /// <summary>The external-access endpoint sources the handlers and their helpers live in.</summary>
    private static IReadOnlyList<SourceUnit> ExternalAccessEndpointUnits() =>
        Real.Set.Units.Where(u => u.Path.Replace('\\', '/').Contains("Api/ExternalAccess/", StringComparison.Ordinal)).ToList();

    private static List<string> ExternalWriteViolations(
        IEnumerable<RouteRegistration> routes, IReadOnlyList<SourceUnit> units, IReadOnlyDictionary<string, string> exemptions)
    {
        var methodsByName = units.SelectMany(u => u.Methods)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var violations = new List<string>();
        foreach (var route in routes.Where(r => !r.Unparseable && !r.Unbound && IsExternalWrite(r)))
        {
            if (exemptions.ContainsKey(route.Key))
            {
                continue;
            }

            if (!HandlerReachesConfirmation(route, methodsByName))
            {
                violations.Add($"{route.Key} ({route.File}:{route.Line}) — its handler does not reach {DataverseWriteConfirmation} "
                               + $"within {MaxExternalWriteHops} hops, and it is not in ExternalWriteExemptions");
            }
        }

        return violations;
    }

    private static readonly Regex CalledName = new(@"(?<![\w])([A-Za-z_]\w*)\s*(?:<[^;(){}]*>)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex MethodGroup = new(@"^(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*)$", RegexOptions.Compiled);

    private static bool HandlerReachesConfirmation(RouteRegistration route, IReadOnlyDictionary<string, List<MethodDecl>> methodsByName)
    {
        if (route.Unit is null || route.HandlerEnd <= route.HandlerStart)
        {
            return false;
        }

        var handlerCode = route.Unit.Code[route.HandlerStart..route.HandlerEnd].Trim();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var bodies = new List<string>();
        if (MethodGroup.Match(Regex.Replace(handlerCode, @"\s+", string.Empty)) is { Success: true } group)
        {
            bodies.AddRange(BodiesOf(group.Groups[1].Value, methodsByName, visited));
        }
        else
        {
            bodies.Add(handlerCode);
        }

        for (var hop = 0; hop <= MaxExternalWriteHops && bodies.Count > 0; hop++)
        {
            if (bodies.Any(b => ContainsToken(b, DataverseWriteConfirmation)))
            {
                return true;
            }

            bodies = bodies
                .SelectMany(b => CalledName.Matches(b).Select(m => m.Groups[1].Value))
                .Distinct(StringComparer.Ordinal)
                .SelectMany(name => BodiesOf(name, methodsByName, visited))
                .ToList();
        }

        return false;
    }

    private static IEnumerable<string> BodiesOf(
        string name, IReadOnlyDictionary<string, List<MethodDecl>> methodsByName, HashSet<string> visited) =>
        visited.Add(name) && methodsByName.TryGetValue(name, out var methods)
            ? methods.Select(m => m.Body)
            : Enumerable.Empty<string>();
}
