using System.Security.Claims;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// Workspace Matter endpoints: Create Matter wizard pre-fill and related matter actions.
/// Registered as a separate class to avoid parallel-agent file conflicts with WorkspaceEndpoints.cs.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern — MapPost with handler delegate.
/// Follows ADR-007: File uploads through SpeFileStore facade (delegated to MatterPreFillService).
/// Follows ADR-008: Endpoint authorization filter per endpoint.
/// Follows ADR-013: AI document analysis rate-limited (uses existing "ai-stream" policy at 10 req/min).
/// </remarks>
public static class WorkspaceMatterEndpoints
{
    /// <summary>
    /// Registers workspace matter endpoints under /api/workspace/matters.
    /// Call this from Program.cs after <c>app.UseAuthentication()</c> and <c>app.UseRateLimiter()</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceMatterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace/matters")
            .RequireAuthorization()
            .WithTags("Workspace Matters");

        // POST /api/workspace/matters/pre-fill
        // Accepts multipart/form-data with one or more file uploads.
        // Stores files temporarily via SpeFileStore, runs AI extraction, returns pre-filled matter fields.
        group.MapPost("/pre-fill", HandlePreFill)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")          // 10 req/min per user (ADR-013)
            .DisableAntiforgery()                       // Required for multipart/form-data in Minimal API
            .WithName("MatterPreFill")
            .WithSummary("AI pre-fill for Create Matter wizard")
            .WithDescription(
                "Accepts multipart/form-data uploads (PDF, DOCX, XLSX — max 10 MB each). " +
                "Files are stored temporarily via SpeFileStore, analyzed by the AI, and structured " +
                "matter field values are returned. Partial extraction is handled gracefully — " +
                "unextracted fields are null. Returns empty response (confidence=0) on AI timeout.")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces<PreFillResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// POST /api/workspace/matters/pre-fill
    /// Validates uploaded files, delegates to MatterPreFillService for AI extraction,
    /// and returns a PreFillResponse with extracted matter field values.
    /// </summary>
    private static async Task<IResult> HandlePreFill(
        IFormFileCollection files,
        MatterPreFillService preFillService,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // UserId is guaranteed non-null by WorkspaceAuthorizationFilter (stored in Items)
        var userId = httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";

        logger.LogInformation(
            "Matter AI pre-fill request received. UserId={UserId}, FileCount={FileCount}, " +
            "CorrelationId={CorrelationId}",
            userId, files?.Count ?? 0, httpContext.TraceIdentifier);

        // --- File Validation ---
        var validationErrors = MatterPreFillService.ValidateFiles(files!);
        if (validationErrors.Count > 0)
        {
            logger.LogWarning(
                "File validation failed for pre-fill. UserId={UserId}, Errors={Errors}, " +
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

        // --- AI Analysis via MatterPreFillService ---
        try
        {
            var result = await preFillService.AnalyzeFilesAsync(files!, userId, httpContext, ct);

            logger.LogInformation(
                "Matter AI pre-fill complete. UserId={UserId}, FieldsExtracted={FieldCount}, " +
                "Confidence={Confidence}, CorrelationId={CorrelationId}",
                userId, result.PreFilledFields.Length, result.Confidence, httpContext.TraceIdentifier);

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Matter AI pre-fill failed. UserId={UserId}, CorrelationId={CorrelationId}",
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
