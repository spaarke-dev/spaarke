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

        return app;
    }

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
