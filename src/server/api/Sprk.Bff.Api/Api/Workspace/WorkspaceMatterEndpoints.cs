using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Workspace.Models;
using Sprk.Bff.Api.Services.Ai;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

        // POST /api/workspace/matters/ai-summary  (SSE stream)
        // Generates a professional AI-drafted summary for a matter given name/type/practice area.
        group.MapPost("/ai-summary", HandleAiSummary)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")
            .WithName("MatterAiSummary")
            .WithSummary("Generate AI draft summary for a matter (SSE stream)")
            .WithDescription(
                "Accepts JSON body {matterName, matterType, practiceArea}. " +
                "Generates a professional summary using AI and streams progress events " +
                "followed by a result event with {summary: string}.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
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

    // =========================================================================
    // POST /api/workspace/matters/ai-summary  (SSE stream)
    // =========================================================================

    private record AiSummaryRequest(string MatterName, string MatterType, string PracticeArea);

    private static async Task HandleAiSummary(
        AiSummaryRequest request,
        IOpenAiClient openAiClient,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var response = httpContext.Response;

        if (string.IsNullOrWhiteSpace(request?.MatterName))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            response.ContentType = "application/problem+json";
            await response.WriteAsync(
                JsonSerializer.Serialize(new { title = "Bad Request", status = 400, detail = "matterName is required." }, JsonOptions), ct);
            return;
        }

        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        logger.LogInformation(
            "Matter AI summary SSE request. MatterName={MatterName}, CorrelationId={CorrelationId}",
            request.MatterName, httpContext.TraceIdentifier);

        try
        {
            // Steps advance quickly since there is no document to extract
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("document_loaded", "Loading matter details..."), ct);
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("extracting_text", "Reading matter information..."), ct);
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("context_ready", "Preparing summary prompt..."), ct);
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("analyzing", "Generating summary..."), ct);

            var prompt =
                $"""
                You are a legal assistant helping draft professional matter summaries for a law firm.
                Generate a concise, professional summary (2-3 paragraphs) for a legal matter with the following details:
                - Matter Name: {request.MatterName}
                - Matter Type: {request.MatterType ?? "Not specified"}
                - Practice Area: {request.PracticeArea ?? "Not specified"}

                The summary should:
                1. Open with the nature and purpose of the matter
                2. Describe the key legal work and objectives
                3. Note any important considerations or typical next steps for this type of matter

                Write in clear, professional language suitable for internal distribution to legal staff and partners.
                Do not use placeholder text, hypothetical examples, or state that you are an AI.
                Respond with only the summary text — no headings, labels, or JSON.
                """;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            var summaryText = await openAiClient.GetCompletionAsync(prompt, cancellationToken: timeoutCts.Token);

            var resultJson = JsonSerializer.Serialize(new { summary = summaryText.Trim() }, JsonOptions);
            await WriteSSEAsync(response, AnalysisStreamChunk.Result(resultJson), ct);
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("delivering", "Delivering summary..."), ct);

            await response.WriteAsync("data: [DONE]\n\n", ct);
            await response.Body.FlushAsync(ct);

            logger.LogInformation(
                "Matter AI summary complete. CharCount={CharCount}, CorrelationId={CorrelationId}",
                summaryText.Length, httpContext.TraceIdentifier);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Matter AI summary timed out. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            await WriteSSEAsync(response, AnalysisStreamChunk.FromError("Summary generation timed out. Please try again."), CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Matter AI summary SSE failed. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            await WriteSSEAsync(response, AnalysisStreamChunk.FromError("An error occurred while generating the summary."), CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
    }

    private static async Task WriteSSEAsync(HttpResponse response, AnalysisStreamChunk chunk, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
