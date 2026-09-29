using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Security alerts and secure score endpoints for the SPE Admin application.
///
/// Exposes two read-only endpoints that surface Microsoft Graph Security API data:
///
///   GET /api/spe/security/alerts?configId={id}
///     Returns the most recent security alerts for the tenant scoped to a container type config.
///     Surfaces suspicious activities, policy violations, and other security events.
///
///   GET /api/spe/security/score?configId={id}
///     Returns the Microsoft Secure Score (currentScore / maxScore + comparative benchmarks).
///     Provides a quantified measure of the tenant's overall security posture.
///
/// Authorization is inherited from the /api/spe route group (SpeAdminAuthorizationFilter +
/// RequireAuthorization()), so no per-endpoint filter is added here.
/// </summary>
/// <remarks>
/// ADR-001: Minimal API — MapGroup() on the parent /api/spe group, static handler methods.
/// ADR-007: SpeAdminGraphService facade — no Graph SDK types in API responses.
/// ADR-008: Authorization inherited from parent /api/spe route group (applied in SpeAdminEndpoints).
/// ADR-019: ProblemDetails for all error responses.
/// </remarks>
public static class SecurityEndpoints
{
    /// <summary>
    /// Composes the access-denied summary from what Graph ACTUALLY reported, instead of leading with a
    /// single guess on every denial.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The previous text opened with <c>"The most common cause is a missing SecurityEvents.Read.All
    /// grant"</c> on EVERY denial. UAT on 2026-08-28 showed why that is a defect and not just wording:
    /// Graph's own words were <c>"Unauthorized request - Account is not provisioned"</c>, and task 013
    /// had ALREADY granted <c>SecurityEvents.Read.All</c>. So the screen sent the operator to re-check a
    /// grant that was demonstrably already present, to fix a condition that no grant can fix.
    /// </para>
    /// <para>
    /// That is this project's signature defect wearing a different hat: a layer asserting a confident
    /// cause it has not established. The denial IS established (we are inside a 403 filter). WHICH cause
    /// produced it is not — unless Graph names it, which in the "not provisioned" case it does.
    /// </para>
    /// <para>
    /// So: branch on Graph's message. Assert only what Graph established, and offer the rest as
    /// candidates the app explicitly cannot distinguish. <c>ToProblemDetails</c> appends Graph's verbatim
    /// text after this summary, so the operator always sees the raw signal too.
    /// </para>
    /// </remarks>
    internal static string AccessDeniedSummary(SpaarkeStorageException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Graph's wording for "this tenant has no Security API to talk to". It is a tenant
        // provisioning/licensing condition, NOT a permission condition — granting anything leaves it
        // unchanged. Naming it precisely is the difference between a 5-minute check and a wild goose chase.
        if ((ex.Message ?? string.Empty).Contains("not provisioned", StringComparison.OrdinalIgnoreCase))
        {
            return "The tenant is not provisioned for the Graph Security API. This is a tenant "
                 + "licensing/provisioning condition, NOT a missing permission — granting "
                 + "SecurityEvents.Read.All will not change it. Verify in the Microsoft 365 Security "
                 + "Center (security.microsoft.com) whether this tenant has a security subscription "
                 + "that exposes the Security API.";
        }

        // Genuinely ambiguous. Say so rather than picking a favourite.
        return "Access denied to the Graph Security API. This app cannot tell which of two causes "
             + "produced it: the app registration lacks SecurityEvents.Read.All, or a conditional-access "
             + "or tenant policy blocked the call. Check the grant first, then policy.";
    }

    /// <summary>
    /// Registers the security alerts and secure score endpoints on the /api/spe route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/>.
    /// </summary>
    /// <param name="group">The /api/spe route group (auth already applied).</param>
    /// <returns>The route group for chaining.</returns>
    public static RouteGroupBuilder MapSecurityEndpoints(this RouteGroupBuilder group)
    {
        var security = group.MapGroup("/security")
            .WithTags("SpeAdmin.Security");

        // GET /api/spe/security/alerts?configId={id}
        security.MapGet("/alerts", GetSecurityAlertsAsync)
            .WithName("GetSecurityAlerts")
            .WithSummary("Get security alerts for the SPE environment")
            .WithDescription(
                "Returns the most recent security alerts from the Microsoft Graph Security API " +
                "for the tenant associated with the specified container type configuration. " +
                "Alerts include suspicious activities, policy violations, and other security events.")
            .Produces<SecurityAlertsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/spe/security/score?configId={id}
        security.MapGet("/score", GetSecureScoreAsync)
            .WithName("GetSecureScore")
            .WithSummary("Get the Microsoft Secure Score for the SPE environment")
            .WithDescription(
                "Returns the most recent Microsoft Secure Score from the Graph Security API " +
                "for the tenant associated with the specified container type configuration. " +
                "Includes currentScore, maxScore, and averageComparativeScores benchmarks.")
            .Produces<SecureScoreDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handlers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/security/alerts?configId={id}
    ///
    /// Query parameters:
    ///   configId — (required) GUID of the sprk_specontainertypeconfig record to scope credentials.
    ///   top      — (optional) Maximum alerts to return; defaults to 50, max 200.
    ///
    /// Responses:
    ///   200 OK                — Returns SecurityAlertsResponse with items array (may be empty).
    ///   400 Bad Request       — configId missing or invalid.
    ///   403 Forbidden         — App registration lacks SecurityEvents.Read.All permission.
    ///   500 Internal Error    — Graph API call failed unexpectedly.
    /// </summary>
    private static async Task<IResult> GetSecurityAlertsAsync(
        [FromQuery] Guid? configId,
        [FromQuery] int? top,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // configId is required
        if (configId is null || configId == Guid.Empty)
        {
            return TypedResults.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.security.alerts.missing_config_id",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        var maxAlerts = Math.Clamp(top ?? 50, 1, 200);

        logger.LogInformation(
            "GET /api/spe/security/alerts ConfigId={ConfigId} Top={Top} TraceId={TraceId}",
            configId, maxAlerts, context.TraceIdentifier);

        try
        {
            // Resolve config → Graph client (SpeAdminGraphService.ContainerTypeConfig nested type)
            var config = await graphService.ResolveConfigAsync(configId.Value, ct);
            if (config is null)
            {
                return TypedResults.Problem(
                    detail: $"Container type configuration '{configId}' was not found.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Configuration Not Found",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.security.alerts.config_not_found",
                        ["traceId"] = context.TraceIdentifier
                    });
            }

            var alerts = await graphService.GetSecurityAlertsForConfigAsync(
                config, maxAlerts, ct);

            // Map Graph service domain models → API response DTOs (ADR-007)
            var dtos = alerts.Select(a => new SecurityAlertDto
            {
                Id = a.Id,
                Title = a.Title,
                Severity = a.Severity,
                Status = a.Status,
                CreatedDateTime = a.CreatedDateTime,
                Description = a.Description
            }).ToList();

            return TypedResults.Ok(new SecurityAlertsResponse(dtos, dtos.Count));
        }
        catch (SpaarkeStorageException ex)
            when (ex.StatusCode == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                "Graph Security API access denied for alerts (configId {ConfigId}). " +
                "Graph code={GraphCode} message={GraphMessage}. TraceId={TraceId}",
                configId, ex.ErrorCode, ex.Message, context.TraceIdentifier);

            // The catch is filtered to 403, so "access denied" IS established. WHICH cause produced it is
            // not — so the summary is derived from Graph's own words. See AccessDeniedSummary.
            return ex.ToProblemDetails(
                summary: AccessDeniedSummary(ex),
                errorCode: "spe.security.alerts.graph_access_denied",
                statusCode: StatusCodes.Status403Forbidden,
                traceId: context.TraceIdentifier,
                title: "Security API Access Denied");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error fetching security alerts for configId {ConfigId}. TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving security alerts.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Security Alerts Retrieval Failed",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.security.alerts.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// GET /api/spe/security/score?configId={id}
    ///
    /// Query parameters:
    ///   configId — (required) GUID of the sprk_specontainertypeconfig record to scope credentials.
    ///
    /// Responses:
    ///   200 OK             — Returns SecureScoreDto with currentScore, maxScore, and benchmarks.
    ///   204 No Content     — Graph returned no score data (new tenant, no historical data).
    ///   400 Bad Request    — configId missing or invalid / config not found.
    ///   403 Forbidden      — App registration lacks SecurityEvents.Read.All permission.
    ///   500 Internal Error — Graph API call failed unexpectedly.
    /// </summary>
    private static async Task<IResult> GetSecureScoreAsync(
        [FromQuery] Guid? configId,
        SpeAdminGraphService graphService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // configId is required
        if (configId is null || configId == Guid.Empty)
        {
            return TypedResults.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.security.score.missing_config_id",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        logger.LogInformation(
            "GET /api/spe/security/score ConfigId={ConfigId} TraceId={TraceId}",
            configId, context.TraceIdentifier);

        try
        {
            // Resolve config → Graph client (SpeAdminGraphService.ContainerTypeConfig nested type)
            var config = await graphService.ResolveConfigAsync(configId.Value, ct);
            if (config is null)
            {
                return TypedResults.Problem(
                    detail: $"Container type configuration '{configId}' was not found.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Configuration Not Found",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "spe.security.score.config_not_found",
                        ["traceId"] = context.TraceIdentifier
                    });
            }

            var scoreResult = await graphService.GetSecureScoreForConfigAsync(
                config, ct);

            if (scoreResult is null)
            {
                // No score data available — new tenant or no historical data yet
                return TypedResults.NoContent();
            }

            // Map Graph service domain model → API response DTO (ADR-007)
            var dto = new SecureScoreDto
            {
                CurrentScore = scoreResult.CurrentScore,
                MaxScore = scoreResult.MaxScore,
                AverageComparativeScores = scoreResult.AverageComparativeScores?
                    .Select(c => new AverageComparativeScoreDto
                    {
                        Basis = c.Basis,
                        AverageScore = c.AverageScore
                    })
                    .ToList()
            };

            return TypedResults.Ok(dto);
        }
        catch (SpaarkeStorageException ex)
            when (ex.StatusCode == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                "Graph Security API access denied for secure score (configId {ConfigId}). " +
                "Graph code={GraphCode} message={GraphMessage}. TraceId={TraceId}",
                configId, ex.ErrorCode, ex.Message, context.TraceIdentifier);

            // See the note on the alerts path above — 403 is established, the specific cause is not.
            return ex.ToProblemDetails(
                summary: AccessDeniedSummary(ex),
                errorCode: "spe.security.score.graph_access_denied",
                statusCode: StatusCodes.Status403Forbidden,
                traceId: context.TraceIdentifier,
                title: "Security API Access Denied");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error fetching secure score for configId {ConfigId}. TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: ProblemDetailsHelper.Explain("An unexpected error occurred while retrieving the secure score.", ex),
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Secure Score Retrieval Failed",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.security.score.unexpected_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response envelope
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Response envelope for the GET /api/spe/security/alerts endpoint.
    /// Wraps the array of alerts with a count for easy client-side pagination awareness.
    /// </summary>
    public sealed record SecurityAlertsResponse(
        List<SecurityAlertDto> Items,
        int Count);
}
