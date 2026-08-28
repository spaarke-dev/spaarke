using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The executable form of issue #863: every route that names a session must establish that the
/// caller owns it.
///
/// <para><b>The defect.</b> A chat session carried a tenant and no owner. Task 059 closed the
/// cross-tenant half; within a tenant, all ~28 session-scoped routes remained open to every
/// authenticated user — read the transcript, rename it, switch its context, post messages into it,
/// delete it. <c>GET /api/ai/chat/sessions</c> compounded it by listing every user's session ids,
/// titles and content previews to the whole tenant, so the "ids are unguessable" mitigation that
/// had been recorded never held: the ids were published.</para>
///
/// <para><b>Why a guard and not a convention.</b> The fix is one line per route
/// (<c>.AddSessionOwnershipFilter()</c>). One line per route is exactly the shape that decays — the
/// twenty-ninth route gets written by copying the twenty-eighth, and a route registration that is
/// merely MISSING a line looks identical to one that never needed it. Same instrument and same
/// reasoning as <see cref="CredentialGuardTests"/> and <see cref="CallerIdentityGuardTests"/>.</para>
/// </summary>
public class SessionOwnershipGuardTests
{
    // =============================================================================================
    // BODY-SCOPED SESSION ROUTES — the enumerated exceptions.
    //
    // RULE 1 below can only see routes whose TEMPLATE contains {sessionId}, because that is what
    // SessionOwnershipFilter reads (deliberately: a filter that dug the id out of a deserialized
    // body would be reaching into the handler's contract). Routes that take the session id in the
    // request BODY must therefore check ownership in the handler, and they are listed here so the
    // set stays closed and reviewable rather than implicit.
    //
    // MAINTENANCE — before adding a row, ask whether the route could take {sessionId} in its path
    // instead. If it can, do that and delete the row; the filter is stronger than a hand-written
    // check. A row is justified only when the id genuinely arrives in the payload.
    // =============================================================================================
    public static readonly IReadOnlyDictionary<string, string> BodyScopedSessionRoutes =
        new Dictionary<string, string>
        {
            ["Services/Compose/ComposeService.cs"] =
                "POST /api/compose/documents/{documentSpeId} carries SessionId in the load body (an "
                + "OPTIONAL resume hint, not a path identity). LoadAsync resolves the caller oid from "
                + "the principal and drops a candidate session it does not own, falling through to a "
                + "fresh session so the user still gets a working document.",

            ["Api/Ai/AnalysisEndpoints.cs"] =
                "POST /api/ai/analysis/fork takes priorSessionId in the body and COPIES its messages. "
                + "The handler answers 404 for a prior session the caller does not own — deliberately "
                + "the same answer as a missing one, so the route is not an existence oracle.",

            ["Api/Agent/AgentEndpoints.cs"] =
                "POST /api/agent/message takes ConversationReference (a session id) in the body. A "
                + "reference the caller does not own is treated exactly like a stale one: mint a new "
                + "session rather than resume. Note ExtractUserId() there returns the literal "
                + "\"unknown\" when the oid is absent — correct for a log line, never for ownership.",
        };

    /// <summary>
    /// A route registration whose template contains <c>{sessionId}</c>. Captures the whole
    /// <c>Map…(…)</c> call so RULE 1 can look at the lines that follow it.
    /// </summary>
    private static readonly Regex SessionScopedRoute = new(
        """(?:group|routes|app)\.Map\w*\(\s*(?:\[[^\]]*\]\s*,\s*)?"[^"]*\{sessionId\}""",
        RegexOptions.Compiled);

    private const string OwnershipFilter = ".AddSessionOwnershipFilter()";

    [Fact]
    public void Rule1_EverySessionScopedRouteCarriesTheOwnershipFilter()
    {
        var offenders = new List<string>();

        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var rel = Relative(file);
            if (!rel.Contains("/Api/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!SessionScopedRoute.IsMatch(lines[i]))
                {
                    continue;
                }

                // The fluent chain for one registration: every following line that starts with '.'
                // (or is blank) belongs to it, up to the terminating ';'.
                var carriesFilter = false;
                for (var j = i; j < lines.Length; j++)
                {
                    if (lines[j].Contains(OwnershipFilter, StringComparison.Ordinal))
                    {
                        carriesFilter = true;
                    }

                    if (lines[j].TrimEnd().EndsWith(";", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                if (!carriesFilter)
                {
                    offenders.Add($"{rel}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Session-scoped route(s) with no ownership check. Within a tenant, ANY authenticated user\n"
            + "can reach these — that is issue #863. Add the filter to the registration:\n\n"
            + "    group.MapGet(\"/sessions/{sessionId}/thing\", GetThingAsync)\n"
            + "        .AddSessionOwnershipFilter()          // <-- this\n"
            + "        .AddAiAuthorizationFilter()\n\n"
            + "If the session id genuinely arrives in the BODY rather than the path, check ownership in\n"
            + "the handler and add a row to BodyScopedSessionRoutes with the reason.\n\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void Rule2_TheHistoryListIsFilteredByOwner()
    {
        // The disclosure was not a missing check on a route — it was a missing PREDICATE in a query.
        // Rule 1 cannot see that: ListRecentSessions has no {sessionId} in its template, because it
        // is the endpoint that HANDS OUT session ids. It needs its own rule.
        var source = File.ReadAllText(Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionPersistenceService.cs"));

        Assert.True(
            source.Contains("c.ownerOid = @ownerOid", StringComparison.Ordinal),
            "The History query lost its owner predicate. Without `c.ownerOid = @ownerOid` it returns "
            + "EVERY user's sessions in the tenant — ids, titles and content previews — to every "
            + "caller, which is both the #863 disclosure and the delivery mechanism for it.");

        // The specific weakening that would look like a kindness and reopen the hole on the oldest,
        // most numerous documents.
        Assert.False(
            source.Contains("NOT IS_DEFINED(c.ownerOid)", StringComparison.Ordinal),
            "The History query treats sessions with no owner as visible. Pre-#863 sessions must match "
            + "NOBODY — see ChatSession.OwnerOid for why fail-closed is the chosen migration and what "
            + "it costs.");
    }

    [Fact]
    public void Rule3_OwnershipSurvivesTheWarmTier()
    {
        // If either half of the Cosmos round-trip is dropped, a session that falls out of Redis
        // reloads with OwnerOid == null and then fails closed FOR ITS OWN OWNER. That is a silent
        // outage rather than a security hole, and it would be blamed on the cache, not on a mapping.
        var source = File.ReadAllText(Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs"));

        Assert.True(
            source.Contains("OwnerOid = session.OwnerOid", StringComparison.Ordinal),
            "MapChatSessionToStoredSession no longer writes OwnerOid — ownership is lost on the way "
            + "INTO Cosmos, so every evicted session becomes inaccessible to its owner.");

        Assert.True(
            source.Contains("OwnerOid = stored.OwnerOid", StringComparison.Ordinal),
            "MapStoredSessionToChatSession no longer restores OwnerOid — ownership is lost on the way "
            + "OUT of Cosmos, with the same effect.");
    }

    [Fact]
    public void EveryBodyScopedEntryCarriesAReasonAndStillExists()
    {
        var blank = BodyScopedSessionRoutes
            .Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key)
            .ToList();

        Assert.True(blank.Count == 0,
            "Body-scoped entries must carry a written justification — an entry without one is how the "
            + "next audit concludes the exemption is permanent:\n" + string.Join("\n", blank));

        // A stale entry silently widens: a path that no longer exists exempts nothing today, but will
        // exempt a NEW file that happens to land there.
        var missing = BodyScopedSessionRoutes.Keys
            .Where(k => !SourceScan.ServerSourceFiles()
                .Any(f => Relative(f).EndsWith(k, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(missing.Count == 0,
            "Body-scoped route file(s) no longer exist. Remove the entries:\n"
            + string.Join("\n", missing));
    }

    [Fact]
    public void NegativeControl_TheRouteDetectorActuallyMatchesTheDefect()
    {
        // Rule 1 passing while matching nothing is the same failure mode as the fixtures that let the
        // original defect through a green suite. These are the four registration shapes in the tree.
        Assert.Matches(SessionScopedRoute, """group.MapDelete("/sessions/{sessionId}", DeleteSessionAsync)""");
        Assert.Matches(SessionScopedRoute, """group.MapGet("/sessions/{sessionId}/history", GetHistoryAsync)""");
        Assert.Matches(SessionScopedRoute, """group.MapMethods("/sessions/{sessionId}", ["PATCH"], RenameSessionAsync)""");
        Assert.Matches(SessionScopedRoute, """group.MapPost("/{sessionId}/review-memo", GenerateReviewMemo)""");

        // And must NOT fire on routes that merely mention a session elsewhere, or on the list
        // endpoint that Rule 2 owns.
        Assert.DoesNotMatch(SessionScopedRoute, """group.MapGet("/sessions", ListRecentSessionsAsync)""");
        Assert.DoesNotMatch(SessionScopedRoute, """group.MapGet("/sessions/by-analysis/{analysisId:guid}", GetSessionByAnalysisAsync)""");
    }

    private static string Relative(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');
}
