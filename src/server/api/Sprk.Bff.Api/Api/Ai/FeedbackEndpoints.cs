using System.Security.Claims;
using Sprk.Bff.Api.Services.Ai.Feedback;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Minimal API endpoints for per-response user feedback (AIPU2-036).
///
/// Routes:
///   POST  /api/ai/feedback                     — submit thumbs up/down feedback for an AI response
///   GET   /api/ai/feedback/playbook/{id}        — aggregated stats for a playbook (optional from/to)
///   GET   /api/ai/feedback/capability/{id}      — aggregated stats for a capability (optional from/to)
///
/// All routes require authentication (ADR-008). Tenant isolation is enforced by extracting
/// the <c>tid</c> claim from the caller's JWT. The <c>oid</c> claim is used as userId.
/// </summary>
public static class FeedbackEndpoints
{
    /// <summary>
    /// Registers all feedback endpoints on the provided route builder.
    /// Called from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/feedback")
            .RequireAuthorization()
            .WithTags("AI Feedback");

        // POST /api/ai/feedback — submit feedback for one AI response
        group.MapPost("/", SubmitAsync)
            .WithName("SubmitAiFeedback")
            .WithSummary("Submit thumbs up/down feedback for an AI response")
            .WithDescription(
                "Stores a FeedbackEntry linked to a specific session turn. " +
                "Comments are truncated to 500 characters. Returns 201 Created with the entry id.")
            .Produces<SubmitFeedbackResponse>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/ai/feedback/playbook/{id} — aggregation by playbook
        group.MapGet("/playbook/{id}", GetByPlaybookAsync)
            .WithName("GetFeedbackAggregateByPlaybook")
            .WithSummary("Get aggregated feedback statistics for a playbook")
            .WithDescription(
                "Returns thumbs-up/down counts, satisfaction rate, and top negative comments " +
                "for the specified playbook over an optional date range.")
            .Produces<FeedbackAggregate>()
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // GET /api/ai/feedback/capability/{id} — aggregation by capability
        group.MapGet("/capability/{id}", GetByCapabilityAsync)
            .WithName("GetFeedbackAggregateByCapability")
            .WithSummary("Get aggregated feedback statistics for a capability")
            .WithDescription(
                "Returns thumbs-up/down counts, satisfaction rate, and top negative comments " +
                "for the specified AI capability/tool over an optional date range.")
            .Produces<FeedbackAggregate>()
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    // =========================================================================
    // Handlers
    // =========================================================================

    /// <summary>
    /// POST /api/ai/feedback
    /// Validates the request, enriches it with identity claims, and stores the entry.
    /// </summary>
    private static async Task<IResult> SubmitAsync(
        SubmitFeedbackRequest request,
        HttpContext httpContext,
        IFeedbackService feedbackService,
        CancellationToken ct = default)
    {
        var (tenantId, userId) = ExtractClaims(httpContext);
        if (tenantId is null || userId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Missing tenant or user identity claims (tid / oid).");

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "SessionId is required.");

        if (!Enum.IsDefined(request.Rating))
            return Results.Problem(statusCode: 400, title: "Bad Request",
                detail: "Rating must be ThumbsUp (1) or ThumbsDown (-1).");

        var entry = new FeedbackEntry
        {
            TenantId = tenantId,
            UserId = userId,
            SessionId = request.SessionId,
            TurnIndex = request.TurnIndex,
            Rating = request.Rating,
            Comment = request.Comment,
            PlaybookId = request.PlaybookId,
            CapabilityId = request.CapabilityId
        };

        try
        {
            var id = await feedbackService.SubmitAsync(tenantId, entry, ct);
            return Results.Created($"/api/ai/feedback/{id}", new SubmitFeedbackResponse(id));
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FeedbackEndpoints");
            logger.LogError(ex, "Failed to store feedback for session {SessionId}", request.SessionId);
            return Results.Problem(statusCode: 500, title: "Internal Server Error",
                detail: "Failed to store feedback.");
        }
    }

    /// <summary>
    /// GET /api/ai/feedback/playbook/{id}?from=&amp;to=
    /// </summary>
    private static async Task<IResult> GetByPlaybookAsync(
        string id,
        HttpContext httpContext,
        IFeedbackService feedbackService,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Missing tenant identity claim (tid).");

        try
        {
            var aggregate = await feedbackService.GetAggregateByPlaybookAsync(
                tenantId, id, from, to, ct);

            return aggregate is null
                ? Results.Problem(statusCode: 404, title: "Not Found",
                    detail: $"No feedback found for playbook '{id}' in the specified date range.")
                : Results.Ok(aggregate);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FeedbackEndpoints");
            logger.LogError(ex, "Failed to aggregate feedback for playbook {PlaybookId}", id);
            return Results.Problem(statusCode: 500, title: "Internal Server Error",
                detail: "Failed to retrieve feedback aggregate.");
        }
    }

    /// <summary>
    /// GET /api/ai/feedback/capability/{id}?from=&amp;to=
    /// </summary>
    private static async Task<IResult> GetByCapabilityAsync(
        string id,
        HttpContext httpContext,
        IFeedbackService feedbackService,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var (tenantId, _) = ExtractClaims(httpContext);
        if (tenantId is null)
            return Results.Problem(statusCode: 401, title: "Unauthorized",
                detail: "Missing tenant identity claim (tid).");

        try
        {
            var aggregate = await feedbackService.GetAggregateByCapabilityAsync(
                tenantId, id, from, to, ct);

            return aggregate is null
                ? Results.Problem(statusCode: 404, title: "Not Found",
                    detail: $"No feedback found for capability '{id}' in the specified date range.")
                : Results.Ok(aggregate);
        }
        catch (Exception ex)
        {
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("FeedbackEndpoints");
            logger.LogError(ex, "Failed to aggregate feedback for capability {CapabilityId}", id);
            return Results.Problem(statusCode: 500, title: "Internal Server Error",
                detail: "Failed to retrieve feedback aggregate.");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Extracts <c>tid</c> (tenantId) and <c>oid</c> (userId) claims from the caller's JWT.
    /// Returns (null, null) when either claim is absent.
    /// </summary>
    private static (string? tenantId, string? userId) ExtractClaims(HttpContext httpContext)
    {
        var user = httpContext.User;
        var tenantId = user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        var userId = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return (tenantId, userId);
    }
}

// =========================================================================
// Request / response DTOs (endpoint-local, not part of the service layer)
// =========================================================================

/// <summary>
/// Request body for <c>POST /api/ai/feedback</c>.
/// </summary>
public record SubmitFeedbackRequest
{
    /// <summary>Session correlation identifier.</summary>
    public required string SessionId { get; init; }

    /// <summary>Zero-based turn index within the session.</summary>
    public int TurnIndex { get; init; }

    /// <summary>
    /// User rating: <see cref="FeedbackRating.ThumbsUp"/> (1) or
    /// <see cref="FeedbackRating.ThumbsDown"/> (-1).
    /// </summary>
    public FeedbackRating Rating { get; init; }

    /// <summary>Optional free-text comment (max 500 chars, enforced on write).</summary>
    public string? Comment { get; init; }

    /// <summary>Playbook ID of the rated response, if applicable.</summary>
    public string? PlaybookId { get; init; }

    /// <summary>Capability/tool ID of the rated response, if applicable.</summary>
    public string? CapabilityId { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/ai/feedback</c>.
/// </summary>
/// <param name="Id">The Cosmos DB document id of the stored feedback entry.</param>
public record SubmitFeedbackResponse(string Id);
