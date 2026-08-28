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
            var httpContext = context.HttpContext;

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
                return Results.Problem(
                    statusCode: 400,
                    title: "Bad Request",
                    detail: "Tenant ID not found in token claims (tid).");
            }

            var sessionManager = httpContext.RequestServices.GetRequiredService<ChatSessionManager>();
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
                    logger?.LogWarning(
                        "Session ownership DENIED: session={SessionId}, tenant={TenantId}, " +
                        "owned={IsOwned}. Answered 404.",
                        sessionId, tenantId, !string.IsNullOrEmpty(session.OwnerOid));
                }

                return Results.NotFound(new { error = $"Session {sessionId} not found" });
            }

            return await next(context);
        });
    }
}

/// <summary>
/// Logging category marker for <see cref="SessionOwnershipFilterExtensions"/>. The filter is a lambda
/// on a static class, so it has no type of its own to name the logger after.
/// </summary>
public sealed class SessionOwnershipFilterMarker;
