using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Resource-level authorization for session-scoped routes (ADR-008): the caller must OWN the session
/// named in the route before the handler runs. Issue #863.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> Until 2026-08-28 a chat session had a tenant and no owner. Task 059 closed the
/// cross-TENANT half — a caller can no longer name someone else's tenant — but within a tenant every
/// session-scoped route was open to every authenticated user: read the transcript, rename it, switch
/// its context, post messages into it, delete it. The mitigation recorded at the time ("session ids are
/// 128-bit random, so exploitation needs a leaked id") did not hold, because
/// <c>GET /api/ai/chat/sessions</c> listed the ids — with titles and content previews — to the whole
/// tenant.
/// </para>
/// <para>
/// <b>Why a filter and not a check in each handler.</b> There are ~15 session-scoped routes and 44
/// <c>GetSessionAsync</c> call sites. A rule that each handler must remember is the same shape as the
/// 21 duplicated tenant-resolution sites task 059 deleted, and it fails the same way: the twentieth
/// site is written by copying the nineteenth, and nothing objects. Here the check is attached to the
/// route, and <see cref="SessionOwnershipGuardTests"/> fails the build if a route whose template
/// contains <c>{sessionId}</c> does not carry it — so the omission is a red build rather than a quiet
/// hole. This is also what ADR-008 prescribes: endpoint filters for resource authorization.
/// </para>
/// <para>
/// <b>404, not 403.</b> A non-owner is told the session does not exist. 403 would confirm that a given
/// session id is real, which turns any id the caller can guess or overhear into an existence oracle —
/// and existence alone leaks that a colleague has a conversation about a given matter. Matches the
/// cross-tenant convention already stated in <c>tests/integration/tenant/README</c> ("cross-tenant
/// reads MUST 404").
/// </para>
/// <para>
/// <b>Unowned sessions (pre-#863) fail closed</b> — see <c>ChatSession.OwnerOid</c> for the reasoning
/// and the bounded cost. Do NOT add an <c>OwnerOid is null =&gt; allow</c> branch here: that would
/// reopen the disclosure on precisely the sessions most likely to still be live, and it is the
/// migration shortcut this filter exists to refuse.
/// </para>
/// </remarks>
public static class SessionOwnershipFilterExtensions
{
    /// <summary>
    /// The stable <c>errorCode</c> (ADR-019) this filter answers with. ONE code for all three
    /// denial reasons — missing session, unowned session, someone else's session — because
    /// distinguishing them on the wire is the existence oracle the 404 exists to avoid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This code supersedes per-route "session not found" codes on every <c>{sessionId}</c>
    /// route</b>, because the filter runs before the handler and therefore before the handler's own
    /// not-found branch. The known casualty is <c>dispatch.session-not-found</c> on
    /// <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>; a client matching that string must
    /// match this one instead. Owner-approved 2026-08-28 (issue #863) as the honest option: the
    /// alternative — passing through on not-found so the handler could still answer its own code —
    /// would have told a caller which session ids exist, which is the disclosure this whole change
    /// closes.
    /// </para>
    /// <para>
    /// Anything that needs to tell the three cases apart reads the server log line, never the
    /// response.
    /// </para>
    /// </remarks>
    public const string NotFoundOrNotOwnedErrorCode = "session.not-found-or-not-owned";

    /// <summary>
    /// Stable <c>errorCode</c> for a principal carrying no <c>tid</c> claim. Deliberately the SAME
    /// string <c>SummarizeSessionEndpoint</c> and <c>DispatchSessionEndpoint</c> already publish —
    /// the filter now answers this ahead of them on every <c>{sessionId}</c> route, and reusing
    /// their code keeps that transparent to clients instead of introducing a third spelling.
    /// </summary>
    public const string TenantMissingErrorCode = "auth.tid-missing";

    /// <summary>
    /// Requires that the caller owns the session identified by the route's <c>{sessionId}</c> value.
    /// Answers <c>401</c> when the caller carries no Entra <c>oid</c>, and <c>404</c> when the session
    /// is missing, unowned, or owned by someone else.
    /// </summary>
    /// <remarks>
    /// Apply to every route whose template contains <c>{sessionId}</c>. Routes that carry the session
    /// id in the BODY rather than the route (for example the analysis promote/continue endpoints) are
    /// not covered by this filter and must resolve ownership in the handler — those are enumerated,
    /// with their reasons, in <c>SessionOwnershipGuardTests.BodyScopedSessionRoutes</c>.
    /// </remarks>
    public static TBuilder AddSessionOwnershipFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var sessionManager = context.HttpContext.RequestServices
                .GetRequiredService<ChatSessionManager>();

            var denial = await EvaluateAsync(context.HttpContext, sessionManager);
            return denial ?? await next(context);
        });
    }

    /// <summary>
    /// The whole decision: returns the denial <see cref="IResult"/>, or <see langword="null"/> to
    /// allow the request through.
    /// </summary>
    /// <remarks>
    /// <b>Separated from the filter lambda so tests exercise THIS code rather than a copy of its
    /// branch</b> — the same reason <c>ChatEndpoints.DeleteSessionAsync</c> was made <c>internal</c>
    /// for the task-059 tenant tests. A test that re-implements the condition it is checking stays
    /// green through the exact edit that breaks production.
    /// </remarks>
    internal static async Task<IResult?> EvaluateAsync(
        HttpContext httpContext,
        ChatSessionManager sessionManager)
    {
        {
            var callerOid = CallerResolution.ResolveObjectId(httpContext.User);
            if (string.IsNullOrEmpty(callerOid))
            {
                // Unidentifiable, not unauthorized-for-this-resource. CallerResolution's contract:
                // null means 401, never 403.
                return Results.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "User identity not found",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1");
            }

            var sessionId = httpContext.Request.RouteValues.TryGetValue("sessionId", out var raw)
                ? raw as string
                : null;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                // The filter was attached to a route with no {sessionId} segment. Passing through
                // would make the guard silently inert on that route, which is worse than a loud
                // failure — the guard test exists to keep this branch unreachable in practice.
                var logger = httpContext.RequestServices
                    .GetService<ILogger<SessionOwnershipFilterMarker>>();
                logger?.LogError(
                    "AddSessionOwnershipFilter is attached to {Route}, which has no {{sessionId}} " +
                    "route value. The ownership check cannot run — refusing the request rather than " +
                    "allowing it through unchecked.",
                    httpContext.Request.Path);

                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Session ownership could not be established.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
            }

            var tenantId = TenantResolution.ResolveTenantId(httpContext.User);
            if (string.IsNullOrEmpty(tenantId))
            {
                // 401 + `auth.tid-missing`, reusing the code SummarizeSessionEndpoint and
                // DispatchSessionEndpoint already document rather than inventing one. Running
                // ahead of the handlers makes this answer uniform across every {sessionId} route;
                // several previously answered 400 here, which was never a contract anyone relied
                // on and is the less accurate of the two — a principal whose tenant cannot be
                // established is unidentifiable, not malformed (same doctrine as
                // CallerResolution: null means 401).
                return Results.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Tenant ID not found in token claims (tid).",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = TenantMissingErrorCode,
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }

            var session = await sessionManager.GetSessionAsync(
                tenantId, sessionId, httpContext.RequestAborted);

            // Missing, unowned, and owned-by-another are deliberately ONE answer. Distinguishing them
            // would let a caller enumerate which session ids exist.
            if (session is null
                || string.IsNullOrEmpty(session.OwnerOid)
                || !string.Equals(session.OwnerOid, callerOid, StringComparison.Ordinal))
            {
                if (session is not null)
                {
                    var logger = httpContext.RequestServices
                        .GetService<ILogger<SessionOwnershipFilterMarker>>();

                    // The log is the ONLY place the three cases are distinguished. An operator
                    // needs to tell "expired" from "someone probing colleagues' sessions"; a caller
                    // must not be able to.
                    logger?.LogWarning(
                        "Session ownership DENIED: session={SessionId}, tenant={TenantId}, " +
                        "owned={IsOwned}, corr={CorrelationId}. Answered 404 with errorCode={ErrorCode}.",
                        sessionId, tenantId, !string.IsNullOrEmpty(session.OwnerOid),
                        httpContext.TraceIdentifier, NotFoundOrNotOwnedErrorCode);
                }

                // ADR-019: ProblemDetails with a stable errorCode + correlationId, so a client can
                // tell THIS 404 from any other on the route, and the response can be joined to the
                // log line above. See NotFoundOrNotOwnedErrorCode for what this supersedes.
                return Results.Problem(
                    statusCode: 404,
                    title: "Not Found",
                    // ADR-019: no identifier in the detail string. Asserted by
                    // DispatchSessionEndpointContractTests — an echoed id both leaks into logs and
                    // sinks that were never scoped for it, and, here specifically, would hand the
                    // caller back confirmation of the very id they were probing with.
                    detail: "The chat session was not found.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = NotFoundOrNotOwnedErrorCode,
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }

            // Owned by the caller — allow.
            return null;
        }
    }
}

/// <summary>
/// Logging category marker for <see cref="SessionOwnershipFilterExtensions"/>. The filter is a lambda
/// on a static class, so it has no type of its own to name the logger after.
/// </summary>
public sealed class SessionOwnershipFilterMarker;
