using System.Security.Claims;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// Workspace state read endpoint (R6 Pillar 6a / D-C-03 / FR-33).
/// </summary>
/// <remarks>
/// <para>
/// <b>Route</b>: <c>GET /api/workspace/state?sessionId={sessionId}</c>
/// </para>
/// <para>
/// <b>Auth model</b> (ADR-008): group-level <c>RequireAuthorization()</c> for the
/// 401 unauth path; per-handler claim extraction for the tenant scope. <c>tid</c>
/// (Entra ID tenant claim) is the authoritative tenant for state lookup — the
/// endpoint never accepts a <c>tenantId</c> query parameter, which structurally
/// prevents the "client claims one tenant, queries another" attack vector and
/// makes a 403 cross-tenant check impossible BY DESIGN. A missing/empty <c>tid</c>
/// claim returns 401 ProblemDetails (matches the canonical
/// <c>InsightEndpoints.Ask</c> pattern). The 403 wording in the original POML is
/// reinterpreted as 401-missing-tid; documented in evidence note + tested.
/// </para>
/// <para>
/// <b>Rate limit</b> (ADR-016): <c>ai-context</c> sliding window — 60 req/min per
/// caller. Read-heavy state lookup; same semantic class as the chat/analysis
/// context resolvers + the Insights ask + scope LIST endpoints.
/// </para>
/// <para>
/// <b>Response shape</b> (FR-33): <see cref="WorkspaceStateResponse"/> —
/// <list type="bullet">
///   <item><c>tabs</c>: unfiltered <see cref="WorkspaceTab"/> list (the prompt
///   builder applies <see cref="WorkspaceTab.VisibleToAssistant"/> per Pillar 9).</item>
///   <item><c>activeTabId</c>: <c>null</c> in C-G1. Reserved for C-G2 (Pillar 6b
///   <c>send_workspace_artifact</c> sets active tab on insert; affordances update
///   via PaneEventBus <c>workspace.tab_focused</c>).</item>
///   <item><c>userSelection</c>: <c>null</c> in C-G1. Reserved for C-G6 (Pillar 6c
///   user-selection events on the workspace channel per FR-38).</item>
/// </list>
/// Empty session returns 200 OK with
/// <c>{ "tabs": [], "activeTabId": null, "userSelection": null }</c> — NOT 404.
/// "No tabs" is a legitimate state, not a missing resource.
/// </para>
/// <para>
/// <b>Placement</b> (CLAUDE.md §10 / ADR-013): consumes
/// <see cref="IWorkspaceStateService"/> only — workspace-state plumbing, not AI
/// capability. Zero AI-internal types injected.
/// </para>
/// <para>
/// <b>Wiring</b> (ADR-010): registered in
/// <c>Infrastructure/DI/EndpointMappingExtensions.MapDomainEndpoints</c> alongside
/// the five other workspace endpoint groups. Zero new Program.cs lines.
/// </para>
/// </remarks>
public static class WorkspaceStateEndpoints
{
    /// <summary>
    /// Registers <c>GET /api/workspace/state</c> on the supplied builder. Called
    /// from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceStateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context") // ADR-016 — read-heavy state lookup
            .WithTags("Workspace State");

        group.MapGet("/state", GetState)
            .WithName("GetWorkspaceState")
            .WithSummary("Get current workspace tabs + active tab + user selection (R6 Pillar 6a)")
            .WithDescription(
                "Returns the merged (Redis hot + Cosmos durable) tab list for the (tenant, session) tuple. " +
                "Tenant scope is derived from the caller's 'tid' claim. Empty sessions return " +
                "{ tabs: [], activeTabId: null, userSelection: null }. " +
                "Filter logic for the Pillar 9 agent prompt-builder snapshot lives in the prompt builder " +
                "(task 074), NOT here — this endpoint returns the FULL state.")
            .Produces<WorkspaceStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// GET /api/workspace/state?sessionId={sessionId}
    /// </summary>
    private static async Task<IResult> GetState(
        HttpContext httpContext,
        IWorkspaceStateService workspaceState,
        ILogger<WorkspaceStateResponse> logger,
        CancellationToken ct,
        string? sessionId = null)
    {
        // ---------------------------------------------------------------
        // Validation
        // ---------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "'sessionId' query parameter is required and cannot be empty.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        // ---------------------------------------------------------------
        // Tenant scope — derive from 'tid' claim. No query-parameter
        // tenant override is accepted, by design (NFR-16 isolation).
        // The "403 tenant mismatch" branch in the original POML is
        // reinterpreted: there is no way for the caller to assert a tenant
        // other than their token's tid, so a malformed/missing tid yields
        // 401 (matches the canonical InsightEndpoints.Ask precedent).
        // ---------------------------------------------------------------
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // ---------------------------------------------------------------
        // Delegate to service — never let exceptions leak per ADR-019.
        // ---------------------------------------------------------------
        try
        {
            var tabs = await workspaceState.GetTabsAsync(tenantId, sessionId, ct);

            // Phase C-G1: activeTabId + userSelection are null. C-G2 (Pillar 6b)
            // and C-G6 (Pillar 6c) will surface these via this same endpoint.
            var response = new WorkspaceStateResponse(
                Tabs: tabs,
                ActiveTabId: null,
                UserSelection: null);

            logger.LogDebug(
                "[WORKSPACE-STATE] session={SessionId} tenant={TenantId} tabCount={TabCount}",
                sessionId, tenantId, tabs.Count);

            return Results.Ok(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // ADR-019: never leak internal detail. Generic 500 with correlation id.
            logger.LogError(ex,
                "[WORKSPACE-STATE] GetTabsAsync failed for session {SessionId} tenant {TenantId}",
                sessionId, tenantId);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to retrieve workspace state. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "WORKSPACE_STATE_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }
    }
}
