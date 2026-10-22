using System.Security.Claims;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// AI integration endpoints for the Legal Operations Workspace.
/// Mounted on a separate class to avoid file conflicts with <see cref="WorkspaceEndpoints"/>
/// during parallel development.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern — MapPost with handler delegates, NOT controllers.
/// Follows ADR-008: Endpoint filter for authorization — NOT global middleware.
/// Follows ADR-013: Uses <see cref="WorkspaceAiService"/> (PlaybookService platform) — NOT direct OpenAI.
///
/// Rate limiting: uses existing "ai-stream" policy (10 req/min per user) registered in Program.cs.
/// Route group: /api/workspace/ai (separate from /api/workspace to avoid merge conflicts).
/// </remarks>
public static class WorkspaceAiEndpoints
{
    /// <summary>
    /// Registers workspace AI endpoints with the application.
    /// Call this from Program.cs alongside <c>app.MapWorkspaceEndpoints()</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace/ai")
            .RequireAuthorization()
            .WithTags("Workspace AI");

        // POST /api/workspace/ai/summary
        //
        // Authorization (unified-access-control-r2 task 163, sweep finding #46): WorkspaceAuthorizationFilter
        // only establishes WHO the caller is. The record decision is the declaration filter after it: Read on
        // the named sprk_event / sprk_matter / sprk_project / sprk_document, AS THE CALLER, before
        // WorkspaceAiService reads that row app-only. An unreadable and an absent record get the identical
        // uniform 404 — and so does a record that disappears between the check and the fetch (handler).
        group.MapPost("/summary", HandleAiSummary)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .AddFinanceAuthorizationFilter(ResolveSummaryTargets, FinanceDenial.UniformNotFound)
            .RequireRateLimiting("ai-stream")
            .WithName("GenerateWorkspaceAiSummary")
            .WithSummary("Generate AI summary for a feed item or to-do item")
            .WithDescription(
                "Accepts an entity type and entity ID, fetches the entity from Dataverse, " +
                "and calls the AI Playbook platform (WorkspaceAiService → PlaybookOrchestrationService) " +
                "to generate a structured analysis response with suggested actions.")
            .Produces<AiSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    /// <summary>
    /// Authorization declaration for <c>POST /api/workspace/ai/summary</c> (task 163): Read ("read") on the
    /// named record, for exactly the four types <see cref="WorkspaceAiService"/> supports (compared
    /// case-insensitively). A document is decided on the Document path (its rights, then the external document
    /// narrowing; task 188 round 4c); the other three take the Record path over the shared <c>EntityAccessFilter</c>
    /// map. An empty type, an empty id and an unsupported type are the
    /// handler's own 400s (same bodies), returned with NO rights query.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveSummaryTargets(EndpointFilterInvocationContext context)
    {
        var httpContext = context.HttpContext;
        var request = context.Arguments.OfType<AiSummaryRequest>().FirstOrDefault();

        if (request is null || string.IsNullOrWhiteSpace(request.EntityType))
        {
            return FinanceAuthorizationTargets.Reject(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "EntityType is required and cannot be empty.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["field"] = "entityType",
                    ["correlationId"] = httpContext.TraceIdentifier
                }));
        }

        if (request.EntityId == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Reject(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "EntityId must be a valid non-empty GUID.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["field"] = "entityId",
                    ["correlationId"] = httpContext.TraceIdentifier
                }));
        }

        if (!WorkspaceAiService.IsSupportedEntityType(request.EntityType))
        {
            // The same 400 body the handler's InvalidOperationException branch returns.
            return FinanceAuthorizationTargets.Reject(Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: WorkspaceAiService.UnsupportedEntityTypeMessage(request.EntityType),
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                }));
        }

        // sprk_document: the DOCUMENT path (task 188 round 4c, F1) — the caller's rights on the document and then THE
        // external document narrowing (FinanceAuthorizationFilter → InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync),
        // so a document the narrowing hides is the same uniform 404 as an unreadable one and the playbook never runs over
        // its name or type. The Record path asked only the root grants. The other three types: the shared
        // EntityAccessFilter map (the record types a document can be FILED under, not the document itself).
        if (string.Equals(request.EntityType, "sprk_document", StringComparison.OrdinalIgnoreCase))
        {
            return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Document,
                EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                RecordId = request.EntityId,
                Operation = "read",
                Source = "body.entityId",
            });
        }

        if (!EntityAccessFilter.TryResolveEntitySet(request.EntityType, out var entitySet))
        {
            // A supported type the shared map cannot resolve declares no check — denied, never passed.
            return FinanceAuthorizationTargets.Authorize();
        }

        return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = entitySet,
            RecordId = request.EntityId,
            Operation = "read",
            Source = "body.entityId",
        });
    }

    /// <summary>
    /// Handles POST /api/workspace/ai/summary.
    /// Validates the request, resolves user identity, calls the AI summary service,
    /// and returns a structured <see cref="AiSummaryResponse"/> or appropriate ProblemDetails.
    /// </summary>
    private static async Task<IResult> HandleAiSummary(
        AiSummaryRequest request,
        WorkspaceAiService aiService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // --- Validate request ---
        if (string.IsNullOrWhiteSpace(request.EntityType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "EntityType is required and cannot be empty.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["field"] = "entityType",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        if (request.EntityId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "EntityId must be a valid non-empty GUID.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["field"] = "entityId",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // Context field max length guard (optional field)
        const int MaxContextLength = 2000;
        if (request.Context?.Length > MaxContextLength)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: $"Context must not exceed {MaxContextLength} characters.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["field"] = "context",
                    ["maxLength"] = MaxContextLength,
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // --- Resolve user identity ---
        // WorkspaceAuthorizationFilter has already run and stored UserId in Items.
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? CallerResolution.ResolveObjectId(httpContext.User);

        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity not found.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        logger.LogInformation(
            "AI summary request received. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
            "CorrelationId={CorrelationId}",
            userId,
            request.EntityType,
            request.EntityId,
            httpContext.TraceIdentifier);

        try
        {
            var result = await aiService.GenerateAiSummaryAsync(request, userId, httpContext, ct);

            logger.LogInformation(
                "AI summary completed. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
                "Confidence={Confidence:F2}, CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                result.Confidence,
                httpContext.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            logger.LogWarning(
                "Entity not found for AI summary. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
                "CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                httpContext.TraceIdentifier);

            // Task 163: the SAME uniform 404 the route's declaration filter gives an unreadable or absent
            // record — no entityType / entityId echoed — so "deleted between the check and the fetch" is
            // indistinguishable from the other two.
            return FinanceAuthorizationFilter.UniformRecordNotFound(httpContext);
        }
        catch (InvalidOperationException ex)
        {
            // Unsupported entity type or AI service configuration error
            logger.LogWarning(
                ex,
                "Invalid operation during AI summary. UserId={UserId}, EntityType={EntityType}, " +
                "EntityId={EntityId}, CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
        catch (TimeoutException ex)
        {
            logger.LogError(
                ex,
                "AI summary timed out. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
                "CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Gateway Timeout",
                detail: "The AI analysis timed out. Please try again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.5",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected — log and return 503 (request was abandoned)
            logger.LogInformation(
                "AI summary cancelled by client. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
                "CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: "The request was cancelled.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "AI summary failed unexpectedly. UserId={UserId}, EntityType={EntityType}, EntityId={EntityId}, " +
                "CorrelationId={CorrelationId}",
                userId,
                request.EntityType,
                request.EntityId,
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: "The AI analysis service is currently unavailable. Please try again later.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
    }
}
