using System.Security.Claims;
using Sprk.Bff.Api.Api.Workspace.Contracts;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Api.Filters;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// API endpoints for the Legal Operations Workspace portfolio aggregation and scoring.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-009: Redis-first caching (5-minute TTL on portfolio data).
/// Follows ADR-019: ProblemDetails for error responses.
/// </remarks>
public static class WorkspaceEndpoints
{
    private const int BatchScoreMaxItems = 50;

    /// <summary>
    /// Registers workspace endpoints with the application.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace")
            .RequireAuthorization()
            .WithTags("Workspace");

        group.MapGet("/portfolio", GetPortfolio)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .WithName("GetPortfolio")
            .WithSummary("Get aggregated portfolio data for the authenticated user")
            .WithDescription(
                "Returns aggregated portfolio metrics for all matters the user has access to, " +
                "including total spend, budget utilization, matters at risk, and overdue events. " +
                "Results are cached in Redis for 5 minutes per user.")
            .Produces<PortfolioSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/health", GetHealthMetrics)
            .WithName("GetHealthMetrics")
            .WithSummary("Get health metrics for Portfolio Health Summary UI")
            .WithDescription(
                "Returns focused health indicators: matters at risk, overdue events, active matters, " +
                "and budget utilization. Results are cached in Redis for 5 minutes per user.")
            .Produces<HealthMetricsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/workspace/briefing — Quick Summary briefing for the legal operations workspace.
        // Returns structured metrics (always deterministic) plus a narrative string (template-based
        // by default; AI-enhanced when PlaybookService/OpenAI is available and responds within 3 s).
        // Results are cached in Redis for 10 minutes per user.
        group.MapGet("/briefing", GetBriefing)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .WithName("GetBriefing")
            .WithSummary("Get quick summary briefing for the authenticated user")
            .WithDescription(
                "Returns portfolio metrics and a narrative briefing for the Quick Summary card and " +
                "briefing dialog. Metrics are always deterministic. Narrative is template-based by " +
                "default; AI-enhanced when available. Results are cached in Redis for 10 minutes per user.")
            .Produces<BriefingResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/workspace/calculate-scores — Batch scoring for initial page load.
        // Client assembles scoring inputs from data it already has; BFF runs deterministic
        // scoring engines and returns scores with full factor breakdowns. Max 50 items.
        group.MapPost("/calculate-scores", CalculateBatchScores)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .WithName("CalculateBatchScores")
            .WithSummary("Calculate priority and effort scores for a batch of events")
            .WithDescription(
                "Accepts pre-assembled scoring inputs for up to 50 to-do events. " +
                "Runs PriorityScoringService and EffortScoringService for each item and returns " +
                "scores with full factor breakdowns. Used on initial page load to avoid N round-trips.")
            .Produces<BatchScoreResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/workspace/events/{id}/scores — Single-event scoring for the AI Summary dialog.
        // Client supplies the event ID and pre-assembled scoring inputs as query parameters or
        // request body. Returns full priority + effort breakdown for that event.
        group.MapGet("/events/{id:guid}/scores", GetEventScores)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .WithName("GetEventScores")
            .WithSummary("Calculate priority and effort scores for a single event")
            .WithDescription(
                "Returns priority and effort scores with full factor breakdowns for a single to-do event. " +
                "Accepts pre-assembled scoring inputs in the request body. " +
                "Used by the AI Summary dialog to show scoring details.")
            .Produces<ScoreResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Returns the aggregated portfolio summary for the authenticated user.
    /// </summary>
    /// <param name="portfolioService">Service that aggregates portfolio data.</param>
    /// <param name="httpContext">HTTP context for extracting identity and correlation ID.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Portfolio summary or ProblemDetails on error.</returns>
    private static async Task<IResult> GetPortfolio(
        PortfolioService portfolioService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // userId is guaranteed non-null by WorkspaceAuthorizationFilter (stored in Items)
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        logger.LogInformation(
            "Portfolio request received. UserId={UserId}, CorrelationId={CorrelationId}",
            userId,
            httpContext.TraceIdentifier);

        try
        {
            var result = await portfolioService.GetPortfolioSummaryAsync(userId, ct);

            logger.LogInformation(
                "Portfolio aggregation complete. UserId={UserId}, ActiveMatters={ActiveMatters}, " +
                "MattersAtRisk={MattersAtRisk}, UtilizationPercent={UtilizationPercent:F1}, " +
                "CachedAt={CachedAt}, CorrelationId={CorrelationId}",
                userId,
                result.ActiveMatters,
                result.MattersAtRisk,
                result.UtilizationPercent,
                result.CachedAt,
                httpContext.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Portfolio aggregation failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                detail: "An error occurred while retrieving portfolio data",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// Returns focused health metrics for the Portfolio Health Summary UI.
    /// </summary>
    /// <param name="portfolioService">Service that aggregates portfolio and health data.</param>
    /// <param name="httpContext">HTTP context for extracting identity and correlation ID.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Health metrics response or ProblemDetails on error.</returns>
    private static async Task<IResult> GetHealthMetrics(
        PortfolioService portfolioService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        try
        {
            var result = await portfolioService.GetHealthMetricsAsync(userId, ct);
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Health metrics failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                detail: "An error occurred while retrieving health metrics",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// Returns the portfolio briefing (metrics + narrative) for the authenticated user.
    /// GET /api/workspace/briefing
    /// </summary>
    /// <param name="briefingService">Service that aggregates metrics and builds the briefing.</param>
    /// <param name="httpContext">HTTP context for extracting identity and correlation ID.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>BriefingResponse or ProblemDetails on error.</returns>
    private static async Task<IResult> GetBriefing(
        BriefingService briefingService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // userId is guaranteed non-null by WorkspaceAuthorizationFilter (stored in Items)
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        logger.LogInformation(
            "Briefing request received. UserId={UserId}, CorrelationId={CorrelationId}",
            userId,
            httpContext.TraceIdentifier);

        try
        {
            var result = await briefingService.GetBriefingAsync(userId, ct);

            logger.LogInformation(
                "Briefing complete. UserId={UserId}, ActiveMatters={ActiveMatters}, " +
                "MattersAtRisk={MattersAtRisk}, IsAiEnhanced={IsAiEnhanced}, " +
                "GeneratedAt={GeneratedAt}, CorrelationId={CorrelationId}",
                userId,
                result.ActiveMatters,
                result.MattersAtRisk,
                result.IsAiEnhanced,
                result.GeneratedAt,
                httpContext.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Briefing aggregation failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                detail: "An error occurred while generating the portfolio briefing",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
    }

    /// <summary>
    /// Batch scoring endpoint — calculates priority and effort scores for up to 50 events.
    /// POST /api/workspace/calculate-scores
    /// </summary>
    /// <param name="request">Batch of score requests with pre-assembled inputs.</param>
    /// <param name="priorityService">Deterministic priority scoring engine.</param>
    /// <param name="effortService">Deterministic effort scoring engine.</param>
    /// <param name="httpContext">HTTP context for correlation ID and logging.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>BatchScoreResponse or ProblemDetails on error.</returns>
    private static IResult CalculateBatchScores(
        BatchScoreRequest request,
        PriorityScoringService priorityService,
        EffortScoringService effortService,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        var correlationId = httpContext.TraceIdentifier;

        // Guard: null body
        if (request?.Items == null)
        {
            return Results.Problem(
                detail: "Request body is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
        }

        // Guard: batch size limit
        if (request.Items.Count > BatchScoreMaxItems)
        {
            return Results.Problem(
                detail: $"Batch size {request.Items.Count} exceeds the maximum of {BatchScoreMaxItems} items per request.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Batch Size Exceeded",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["itemCount"] = request.Items.Count,
                    ["maxItems"] = BatchScoreMaxItems
                });
        }

        logger.LogInformation(
            "Batch score request received. ItemCount={ItemCount}, CorrelationId={CorrelationId}",
            request.Items.Count,
            correlationId);

        try
        {
            var results = new List<ScoreResponse>(request.Items.Count);

            foreach (var item in request.Items)
            {
                var priorityResult = priorityService.CalculatePriorityScore(item.PriorityInput);
                var effortResult = effortService.CalculateEffortScore(item.EffortInput);

                results.Add(new ScoreResponse(
                    item.EventId,
                    priorityResult.Score,
                    priorityResult.Level,
                    priorityResult.Factors,
                    priorityResult.ReasonString,
                    effortResult.Score,
                    effortResult.Level,
                    effortResult.BaseEffort,
                    effortResult.AppliedMultipliers,
                    effortResult.ReasonString));
            }

            logger.LogInformation(
                "Batch scoring complete. ItemCount={ItemCount}, CorrelationId={CorrelationId}",
                results.Count,
                correlationId);

            return TypedResults.Ok(new BatchScoreResponse(results));
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Batch scoring failed. ItemCount={ItemCount}, CorrelationId={CorrelationId}",
                request.Items.Count,
                correlationId);

            return Results.Problem(
                detail: "An error occurred while calculating batch scores",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Single-event scoring endpoint — returns priority and effort scores with full breakdowns.
    /// GET /api/workspace/events/{id}/scores
    /// </summary>
    /// <remarks>
    /// The scoring engines are purely deterministic (same inputs → same outputs), so the
    /// client supplies pre-assembled scoring inputs in the request body rather than the
    /// BFF querying Dataverse server-side. This is the R1 approach: simpler, faster,
    /// and avoids a server-side Dataverse dependency in the scoring path.
    /// </remarks>
    /// <param name="id">Dataverse sprk_event GUID from the route.</param>
    /// <param name="request">Pre-assembled scoring inputs for the event.</param>
    /// <param name="priorityService">Deterministic priority scoring engine.</param>
    /// <param name="effortService">Deterministic effort scoring engine.</param>
    /// <param name="httpContext">HTTP context for correlation ID and logging.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>ScoreResponse or ProblemDetails on error.</returns>
    private static IResult GetEventScores(
        Guid id,
        ScoreRequest request,
        PriorityScoringService priorityService,
        EffortScoringService effortService,
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        var correlationId = httpContext.TraceIdentifier;

        // Guard: null body
        if (request == null)
        {
            return Results.Problem(
                detail: "Request body with scoring inputs is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId });
        }

        // Guard: route ID must match body event ID to prevent ambiguity
        if (request.EventId != id)
        {
            return Results.Problem(
                detail: $"Route event ID '{id}' does not match request body EventId '{request.EventId}'.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Event ID Mismatch",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["routeId"] = id,
                    ["bodyEventId"] = request.EventId
                });
        }

        logger.LogInformation(
            "Single event score request received. EventId={EventId}, CorrelationId={CorrelationId}",
            id,
            correlationId);

        try
        {
            var priorityResult = priorityService.CalculatePriorityScore(request.PriorityInput);
            var effortResult = effortService.CalculateEffortScore(request.EffortInput);

            var response = new ScoreResponse(
                id,
                priorityResult.Score,
                priorityResult.Level,
                priorityResult.Factors,
                priorityResult.ReasonString,
                effortResult.Score,
                effortResult.Level,
                effortResult.BaseEffort,
                effortResult.AppliedMultipliers,
                effortResult.ReasonString);

            logger.LogInformation(
                "Event scoring complete. EventId={EventId}, PriorityScore={PriorityScore}, " +
                "PriorityLevel={PriorityLevel}, EffortScore={EffortScore}, EffortLevel={EffortLevel}, " +
                "CorrelationId={CorrelationId}",
                id,
                priorityResult.Score,
                priorityResult.Level,
                effortResult.Score,
                effortResult.Level,
                correlationId);

            return TypedResults.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Event scoring failed. EventId={EventId}, CorrelationId={CorrelationId}",
                id,
                correlationId);

            return Results.Problem(
                detail: $"An error occurred while calculating scores for event {id}",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["eventId"] = id
                });
        }
    }
}
