using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Insights.Precedents;

namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Admin endpoints for manual SME authoring of Precedents (D-P3 Phase 1 mode of D-61).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — these endpoints invoke
/// <see cref="IPrecedentBoard"/> only. No AI internals are imported. The
/// §3.5.4 forbidden-imports grep is asserted against this folder before merge.
/// </para>
/// <para>
/// <b>Endpoints</b> (under <c>/api/insights/admin/precedents</c>):
/// <list type="bullet">
///   <item>POST / — create a Tentative Precedent (admin role required)</item>
/// </list>
/// </para>
/// <para>
/// <b>Auth model</b>: <see cref="SpeAdminAuthorizationFilter"/> reused per
/// ADR-008 — checks for the <c>Admin</c> / <c>SystemAdmin</c> Azure AD role
/// or <c>roles</c> claim. Returns 401 with ProblemDetails (no user identity)
/// or 403 with deny-code ProblemDetails (insufficient role). Per ADR-019,
/// all errors are surfaced as RFC-7807 problem documents.
/// </para>
/// <para>
/// <b>Rate limit</b>: <c>api-key-admin</c> policy (60/min per caller) per
/// ADR-016 — admin write endpoints are low-volume but the policy provides
/// defense-in-depth against script-driven bulk inserts that would dirty the
/// Precedent corpus.
/// </para>
/// </remarks>
public static class PrecedentAdminEndpoints
{
    /// <summary>
    /// Registers the <c>/api/insights/admin/precedents</c> route group on the
    /// supplied endpoint route builder. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapPrecedentAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights/admin/precedents")
            .RequireAuthorization()
            .AddSpeAdminAuthorizationFilter()
            .RequireRateLimiting("api-key-admin")
            .WithTags("Insights Admin");

        group.MapPost("/", CreatePrecedent)
            .WithName("CreateInsightsPrecedent")
            .WithSummary("Create a Tentative Precedent (manual SME authoring)")
            .WithDescription(
                "Creates a new sprk_precedent row with status=Tentative, reviewerBy=current user " +
                "(or the supplied reviewerByUserId), producedBy=manual-sme-author. Supporting " +
                "matters are associated via the sprk_precedent_matter N:N relationship. " +
                "Phase 1 D-P3 mode of D-61 two-mode authoring.")
            .Produces<CreatePrecedentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/{id:guid}/confirm", ConfirmPrecedent)
            .WithName("ConfirmInsightsPrecedent")
            .WithSummary("Promote a Tentative Precedent to Confirmed and fire projection sync (D-P4)")
            .WithDescription(
                "Sets sprk_status=Confirmed on the supplied Precedent and fires the D-P4 " +
                "projection sync (fire-and-forget) which writes a row to spaarke-insights-index " +
                "with artifactType=precedent per SPEC §3.4.2. The Dataverse update is awaited " +
                "before responding; the AI Search projection runs in the background and any " +
                "failure is logged but does not affect this response. The endpoint is idempotent " +
                "(re-confirming a Confirmed Precedent updates sprk_reviewdate and re-projects).")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Header name used to convey the tenant id for the projection sync (D-52). The
    /// D-P15 endpoint will eventually centralize tenant resolution; for the admin
    /// confirm path the caller supplies tenant explicitly via this header.
    /// </summary>
    private const string TenantIdHeader = "X-Spaarke-Tenant-Id";

    /// <summary>
    /// POST /api/insights/admin/precedents
    /// </summary>
    private static async Task<IResult> CreatePrecedent(
        [FromBody] CreatePrecedentApiRequest? request,
        HttpContext httpContext,
        IPrecedentBoard board,
        ILogger<DataversePrecedentBoard> logger,
        CancellationToken ct)
    {
        // ---------------------------------------------------------------
        // Validation — ADR-019 ProblemDetails for every error
        // ---------------------------------------------------------------
        if (request is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Request body is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (string.IsNullOrWhiteSpace(request.PatternStatement))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "patternStatement is required and cannot be empty.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        if (request.PatternStatement.Length > 4000)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "patternStatement exceeds the 4000-character limit of sprk_patternstatement.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        // Resolve current admin user identity for the default reviewer fallback.
        // Mirrors the chain used by other authorization filters in this codebase.
        var callerOid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // The SpeAdminAuthorizationFilter already rejects requests with no identity,
        // but we re-check defensively so unit tests that bypass the filter still get
        // a clean 401 instead of an unexplained 500.
        if (string.IsNullOrWhiteSpace(callerOid))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // Default reviewer to the calling admin when not supplied — the Precedent
        // row records who took responsibility for the claim at authoring time.
        Guid? reviewerByUserId = request.ReviewerByUserId;
        if ((reviewerByUserId is null || reviewerByUserId == Guid.Empty)
            && Guid.TryParse(callerOid, out var callerGuid))
        {
            // NOTE: this is the Entra ID 'oid' claim, which matches the systemuser
            // azureactivedirectoryobjectid field in Dataverse but NOT the systemuserid
            // (Dataverse's own GUID). For Phase 1 we accept this — the SME promotion
            // flow can correct it. Phase 1.5+ should resolve oid → systemuserid via
            // a per-request mapping service.
            reviewerByUserId = callerGuid;
        }

        // Defensive: dedupe + drop empty supporting matter ids
        var supportingMatterIds = (request.SupportingMatterIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        // ---------------------------------------------------------------
        // Delegate to IPrecedentBoard
        // ---------------------------------------------------------------
        try
        {
            var boardRequest = new CreatePrecedentRequest(
                PatternStatement: request.PatternStatement,
                Scope: request.Scope,
                SupportingMatterIds: supportingMatterIds,
                ReviewerByUserId: reviewerByUserId);

            var precedentId = await board.CreateTentativeAsync(boardRequest, ct);

            logger.LogInformation(
                "[INSIGHTS-PRECEDENT-ADMIN] Created Precedent {PrecedentId} by {CallerOid} (supportingMatters={Count})",
                precedentId, callerOid, supportingMatterIds.Length);

            return Results.Created(
                $"/api/insights/admin/precedents/{precedentId}",
                new CreatePrecedentResponse(
                    Id: precedentId,
                    StatusValue: PrecedentStatus.Tentative,
                    Status: "Tentative",
                    SupportingMatterCount: supportingMatterIds.Length,
                    ReviewerByUserId: reviewerByUserId));
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[INSIGHTS-PRECEDENT-ADMIN] Failed to create Precedent");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create Precedent. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    /// <summary>
    /// POST /api/insights/admin/precedents/{id}/confirm
    /// </summary>
    /// <remarks>
    /// Promotes the supplied Precedent to <see cref="PrecedentStatus.Confirmed"/> and
    /// fires the D-P4 projection sync fire-and-forget. The HTTP response returns once
    /// the Dataverse update succeeds; the projection runs on a background task so
    /// AI Search latency or transient failures do not block the caller.
    /// </remarks>
    private static async Task<IResult> ConfirmPrecedent(
        Guid id,
        HttpContext httpContext,
        IPrecedentBoard board,
        IPrecedentProjectionSync projectionSync,
        ILogger<DataversePrecedentBoard> logger,
        CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Precedent id is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        // Tenant id is mandatory for the projection sync (D-52). The endpoint accepts
        // it via X-Spaarke-Tenant-Id header to keep the URL clean and to mirror the
        // direction the D-P15 ask endpoint (task 061) is heading.
        var tenantId = httpContext.Request.Headers[TenantIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: $"Header '{TenantIdHeader}' is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var callerOid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(callerOid))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var confirmedByUserId = Guid.TryParse(callerOid, out var callerGuid) ? callerGuid : Guid.Empty;

        try
        {
            await board.ConfirmAsync(id, confirmedByUserId, ct);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[INSIGHTS-PRECEDENT-ADMIN] Failed to confirm Precedent {PrecedentId}", id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to confirm Precedent. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        // Fire-and-forget D-P4 projection sync. Wrapping in Task.Run uses a thread-pool
        // thread so we don't tie up the request-handling thread; the inner try/catch
        // ensures any projection failure is logged but does not crash the process.
        // CancellationToken is NOT propagated — once the HTTP response is sent, the
        // request token is disposed; the projection should run to completion on its own.
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await projectionSync.ProjectAsync(id, tenantId, CancellationToken.None);
                logger.LogInformation(
                    "[INSIGHTS-PRECEDENT-ADMIN] Projection sync for Precedent {PrecedentId}: outcome={Outcome} documentId={DocumentId}",
                    id, result.Outcome, result.DocumentId ?? "(none)");
            }
            catch (Exception ex)
            {
                // Structured log only — fire-and-forget can never surface to the caller.
                // Re-projection is safe (idempotent by document id), so an operator can
                // retry by re-calling the confirm endpoint.
                logger.LogError(ex,
                    "[INSIGHTS-PRECEDENT-ADMIN] Fire-and-forget projection failed for Precedent {PrecedentId} tenant {TenantId}",
                    id, tenantId);
            }
        }, CancellationToken.None);

        logger.LogInformation(
            "[INSIGHTS-PRECEDENT-ADMIN] Confirmed Precedent {PrecedentId} by {CallerOid}; projection sync dispatched (tenantId={TenantId})",
            id, callerOid, tenantId);

        // 202 Accepted reflects that the projection runs asynchronously; the Dataverse
        // update has already completed before this response.
        return Results.Accepted(
            $"/api/insights/admin/precedents/{id}",
            new ConfirmPrecedentResponse(
                Id: id,
                StatusValue: PrecedentStatus.Confirmed,
                Status: "Confirmed",
                ProjectionDispatched: true));
    }
}

/// <summary>
/// Request body for <c>POST /api/insights/admin/precedents</c>.
/// </summary>
/// <param name="PatternStatement">
///   Full pattern claim (required, up to 4000 chars). Maps to <c>sprk_patternstatement</c>.
/// </param>
/// <param name="Scope">
///   Optional scope discriminator (e.g. <c>ip-licensing-bigfirm-llp</c>).
///   Stored as a JSON tag inside <c>sprk_clusterdefinition</c> in Phase 1.
/// </param>
/// <param name="SupportingMatterIds">
///   Optional list of <c>sprk_matter</c> ids whose Observations support this
///   Precedent. Associated via the <c>sprk_precedent_matter</c> N:N relationship.
/// </param>
/// <param name="ReviewerByUserId">
///   Optional reviewer (Entra ID oid). When omitted, the calling admin's oid is used.
/// </param>
public sealed record CreatePrecedentApiRequest(
    string? PatternStatement,
    string? Scope,
    Guid[]? SupportingMatterIds,
    Guid? ReviewerByUserId);

/// <summary>
/// 201 response body for <c>POST /api/insights/admin/precedents</c>.
/// </summary>
/// <param name="Id">New <c>sprk_precedent</c> row id.</param>
/// <param name="StatusValue">Numeric option-set value (<c>100000000</c>=Tentative for Phase 1 creates).</param>
/// <param name="Status">Human-readable status name (<c>Tentative</c> for Phase 1 creates).</param>
/// <param name="SupportingMatterCount">Number of supporting matters associated.</param>
/// <param name="ReviewerByUserId">Effective reviewer id used (caller fallback applied if request omitted).</param>
public sealed record CreatePrecedentResponse(
    Guid Id,
    int StatusValue,
    string Status,
    int SupportingMatterCount,
    Guid? ReviewerByUserId);

/// <summary>
/// 202 response body for <c>POST /api/insights/admin/precedents/{id}/confirm</c>.
/// </summary>
/// <param name="Id">The Precedent row id.</param>
/// <param name="StatusValue">Numeric option-set value (<c>100000001</c>=Confirmed after this call).</param>
/// <param name="Status">Human-readable status name (<c>Confirmed</c>).</param>
/// <param name="ProjectionDispatched">Always <c>true</c> when the response is 202; the
/// projection runs asynchronously and its outcome is logged server-side. Re-call this
/// endpoint to retry projection (idempotent via the deterministic document id).</param>
public sealed record ConfirmPrecedentResponse(
    Guid Id,
    int StatusValue,
    string Status,
    bool ProjectionDispatched);
