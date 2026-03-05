using System.Security.Claims;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// Workspace Project endpoints: Create Project wizard pre-fill and related project actions.
/// Follows the same pattern as WorkspaceMatterEndpoints but with project-specific service and response.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern — MapPost with handler delegate.
/// Follows ADR-007: File uploads through SpeFileStore facade (delegated to ProjectPreFillService).
/// Follows ADR-008: Endpoint authorization filter per endpoint.
/// Follows ADR-013: AI document analysis rate-limited (uses existing "ai-stream" policy at 10 req/min).
/// </remarks>
public static class WorkspaceProjectEndpoints
{
    /// <summary>
    /// Registers workspace project endpoints under /api/workspace/projects.
    /// Call this from Program.cs after <c>app.UseAuthentication()</c> and <c>app.UseRateLimiter()</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace/projects")
            .RequireAuthorization()
            .WithTags("Workspace Projects");

        // POST /api/workspace/projects/pre-fill
        // Accepts multipart/form-data with one or more file uploads.
        // Stores files temporarily via SpeFileStore, runs AI extraction, returns pre-filled project fields.
        group.MapPost("/pre-fill", HandlePreFill)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")          // 10 req/min per user (ADR-013)
            .DisableAntiforgery()                       // Required for multipart/form-data in Minimal API
            .WithName("ProjectPreFill")
            .WithSummary("AI pre-fill for Create Project wizard")
            .WithDescription(
                "Accepts multipart/form-data uploads (PDF, DOCX, XLSX — max 10 MB each). " +
                "Files are stored temporarily via SpeFileStore, analyzed by the AI, and structured " +
                "project field values are returned. Partial extraction is handled gracefully — " +
                "unextracted fields are null. Returns empty response (confidence=0) on AI timeout.")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces<ProjectPreFillResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// POST /api/workspace/projects/pre-fill
    /// Validates uploaded files, delegates to ProjectPreFillService for AI extraction,
    /// and returns a ProjectPreFillResponse with extracted project field values.
    /// </summary>
    private static async Task<IResult> HandlePreFill(
        IFormFileCollection files,
        ProjectPreFillService preFillService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";

        logger.LogInformation(
            "Project AI pre-fill request received. UserId={UserId}, FileCount={FileCount}, " +
            "CorrelationId={CorrelationId}",
            userId, files?.Count ?? 0, httpContext.TraceIdentifier);

        // --- File Validation ---
        var validationErrors = ProjectPreFillService.ValidateFiles(files!);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "File validation failed for project pre-fill. UserId={UserId}, Errors={Errors}, " +
                "CorrelationId={CorrelationId}",
                userId, string.Join("; ", validationErrors), httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Files",
                detail: string.Join(" | ", validationErrors),
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier,
                    ["errors"] = validationErrors
                });
        }

        // --- AI Analysis via ProjectPreFillService ---
        try
        {
            var result = await preFillService.AnalyzeFilesAsync(files!, userId, httpContext, ct);

            logger.LogInformation(
                "Project AI pre-fill complete. UserId={UserId}, FieldsExtracted={FieldCount}, " +
                "Confidence={Confidence}, CorrelationId={CorrelationId}",
                userId, result.PreFilledFields.Length, result.Confidence, httpContext.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Project AI pre-fill failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return Results.Problem(
                detail: "An error occurred while analyzing the uploaded documents.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
    }
}
