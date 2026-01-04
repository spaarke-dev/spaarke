using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Analysis endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides AI-driven document analysis with configurable actions, scopes, and output types.
/// Uses Server-Sent Events (SSE) for real-time streaming responses.
/// </summary>
public static class AnalysisEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/analysis")
            .RequireAuthorization()
            .WithTags("AI Analysis");

        // POST /api/ai/analysis/execute - Execute new analysis with SSE streaming
        group.MapPost("/execute", ExecuteAnalysis)
            .AddAnalysisExecuteAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ExecuteAnalysis")
            .WithSummary("Execute document analysis with SSE streaming")
            .WithDescription("Executes AI-driven analysis on documents with configurable actions, skills, knowledge, and tools. Returns results via Server-Sent Events.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        // POST /api/ai/analysis/{analysisId}/continue - Continue analysis via chat
        group.MapPost("/{analysisId:guid}/continue", ContinueAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .RequireRateLimiting("ai-stream")
            .WithName("ContinueAnalysis")
            .WithSummary("Continue analysis via conversational chat")
            .WithDescription("Continues an existing analysis using conversational refinement. Streams updated working document via SSE.")
            .Produces(200, contentType: "text/event-stream")
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/{analysisId}/save - Save working document to SPE
        group.MapPost("/{analysisId:guid}/save", SaveWorkingDocument)
            .AddAnalysisRecordAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("SaveAnalysisDocument")
            .WithSummary("Save working document to SharePoint Embedded")
            .WithDescription("Saves the analysis working document to SPE and creates a new Document record in Dataverse.")
            .Produces<SavedDocumentResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/ai/analysis/{analysisId}/export - Export analysis output
        group.MapPost("/{analysisId:guid}/export", ExportAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("ExportAnalysis")
            .WithSummary("Export analysis to various destinations")
            .WithDescription("Exports analysis output as email, Teams message, PDF, or DOCX.")
            .Produces<ExportResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // GET /api/ai/analysis/{analysisId} - Get analysis with history
        group.MapGet("/{analysisId:guid}", GetAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .WithName("GetAnalysis")
            .WithSummary("Get analysis record with chat history")
            .WithDescription("Retrieves an analysis record including working document, final output, and chat history.")
            .Produces<AnalysisDetailResult>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        // POST /api/ai/analysis/{analysisId}/resume - Resume existing analysis session
        group.MapPost("/{analysisId:guid}/resume", ResumeAnalysis)
            .AddAnalysisRecordAuthorizationFilter()
            .WithName("ResumeAnalysis")
            .WithSummary("Resume an existing analysis session")
            .WithDescription("Creates an in-memory session for an existing analysis. Call this before /continue when reopening an analysis from Dataverse.")
            .Produces<AnalysisResumeResult>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Execute a new analysis with SSE streaming.
    /// POST /api/ai/analysis/execute
    /// </summary>
    private static async Task ExecuteAnalysis(
        AnalysisExecuteRequest request,
        IAnalysisOrchestrationService orchestrationService,
        IOptions<AnalysisOptions> options,
        HttpContext context,
        ILogger<AnalysisOrchestrationService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Check if Analysis feature is enabled
        if (!options.Value.Enabled)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await response.WriteAsJsonAsync(new { error = "Analysis feature is disabled" }, cancellationToken);
            return;
        }

        // Phase 1: Only single document supported
        if (request.DocumentIds.Length > 1 && !options.Value.MultiDocumentEnabled)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Multi-document analysis coming in Phase 2. Currently only single document is supported." }, cancellationToken);
            return;
        }

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "Starting analysis execution for documents [{DocumentIds}], ActionId={ActionId}, TraceId={TraceId}",
            string.Join(",", request.DocumentIds), request.ActionId, context.TraceIdentifier);

        try
        {
            await foreach (var chunk in orchestrationService.ExecuteAnalysisAsync(request, context, cancellationToken))
            {
                await WriteSSEAsync(response, chunk, cancellationToken);
            }

            logger.LogInformation("Analysis execution completed for TraceId={TraceId}", context.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during analysis execution, TraceId={TraceId}",
                context.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during analysis execution, TraceId={TraceId}", context.TraceIdentifier);

            if (!cancellationToken.IsCancellationRequested)
            {
                var errorChunk = new AnalysisStreamChunk("error", null, true, Error: ex.Message);
                await WriteSSEAsync(response, errorChunk, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Continue an existing analysis via conversational chat.
    /// POST /api/ai/analysis/{analysisId}/continue
    /// </summary>
    private static async Task ContinueAnalysis(
        Guid analysisId,
        AnalysisContinueRequest request,
        IAnalysisOrchestrationService orchestrationService,
        HttpContext context,
        ILogger<AnalysisOrchestrationService> logger)
    {
        var cancellationToken = context.RequestAborted;
        var response = context.Response;

        // Set SSE headers
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        logger.LogInformation(
            "Continuing analysis {AnalysisId} with message, TraceId={TraceId}",
            analysisId, context.TraceIdentifier);

        try
        {
            await foreach (var chunk in orchestrationService.ContinueAnalysisAsync(analysisId, request.Message, context, cancellationToken))
            {
                await WriteSSEAsync(response, chunk, cancellationToken);
            }

            logger.LogInformation("Analysis continuation completed for {AnalysisId}", analysisId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "Client disconnected during analysis continuation, AnalysisId={AnalysisId}",
                analysisId);
        }
        catch (KeyNotFoundException)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            await response.WriteAsJsonAsync(new { error = $"Analysis {analysisId} not found" }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during analysis continuation, AnalysisId={AnalysisId}", analysisId);

            if (!cancellationToken.IsCancellationRequested)
            {
                var errorChunk = new AnalysisStreamChunk("error", null, true, Error: ex.Message);
                await WriteSSEAsync(response, errorChunk, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Save working document to SPE and create Document record.
    /// POST /api/ai/analysis/{analysisId}/save
    /// </summary>
    private static async Task<IResult> SaveWorkingDocument(
        Guid analysisId,
        AnalysisSaveRequest request,
        IAnalysisOrchestrationService orchestrationService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Saving working document for analysis {AnalysisId}, FileName={FileName}, Format={Format}",
            analysisId, request.FileName, request.Format);

        try
        {
            var result = await orchestrationService.SaveWorkingDocumentAsync(analysisId, request, cancellationToken);

            logger.LogInformation("Saved document {DocumentId} for analysis {AnalysisId}",
                result.DocumentId, analysisId);

            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Export analysis output to various destinations.
    /// POST /api/ai/analysis/{analysisId}/export
    /// </summary>
    private static async Task<IResult> ExportAnalysis(
        Guid analysisId,
        AnalysisExportRequest request,
        IAnalysisOrchestrationService orchestrationService,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        // Validate export format is enabled
        var analysisOptions = options.Value;
        var validationError = ValidateExportFormat(request.Format, analysisOptions);
        if (validationError != null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        logger.LogInformation("Exporting analysis {AnalysisId} to {Format}",
            analysisId, request.Format);

        try
        {
            var result = await orchestrationService.ExportAnalysisAsync(analysisId, request, cancellationToken);

            if (result.Success)
            {
                logger.LogInformation("Exported analysis {AnalysisId} to {Format}: Status={Status}",
                    analysisId, request.Format, result.Details?.Status);
            }
            else
            {
                logger.LogWarning("Export failed for analysis {AnalysisId}: {Error}",
                    analysisId, result.Error);
            }

            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get analysis record with chat history.
    /// GET /api/ai/analysis/{analysisId}
    /// </summary>
    private static async Task<IResult> GetAnalysis(
        Guid analysisId,
        IAnalysisOrchestrationService orchestrationService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("Retrieving analysis {AnalysisId}", analysisId);

        try
        {
            var result = await orchestrationService.GetAnalysisAsync(analysisId, cancellationToken);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Analysis {analysisId} not found" });
        }
    }

    /// <summary>
    /// Resume an existing analysis by creating an in-memory session.
    /// POST /api/ai/analysis/{analysisId}/resume
    /// </summary>
    private static async Task<IResult> ResumeAnalysis(
        Guid analysisId,
        AnalysisResumeRequest request,
        HttpContext httpContext,
        IAnalysisOrchestrationService orchestrationService,
        ILogger<AnalysisOrchestrationService> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Resuming analysis {AnalysisId}, IncludeChatHistory={IncludeChatHistory}",
            analysisId, request.IncludeChatHistory);

        var result = await orchestrationService.ResumeAnalysisAsync(analysisId, request, httpContext, cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning("Failed to resume analysis {AnalysisId}: {Error}", analysisId, result.Error);
            return Results.BadRequest(new { error = result.Error });
        }

        logger.LogInformation("Analysis {AnalysisId} resumed: {ChatMessages} messages restored",
            analysisId, result.ChatMessagesRestored);

        return Results.Ok(result);
    }

    /// <summary>
    /// Validate export format against enabled options.
    /// </summary>
    private static string? ValidateExportFormat(ExportFormat format, AnalysisOptions options)
    {
        return format switch
        {
            ExportFormat.Email when !options.EnableEmailExport => "Email export is disabled",
            ExportFormat.Teams when !options.EnableTeamsExport => "Teams export is disabled",
            ExportFormat.Pdf when !options.EnablePdfExport => "PDF export is disabled",
            ExportFormat.Docx when !options.EnableDocxExport => "DOCX export is disabled",
            _ => null
        };
    }

    /// <summary>
    /// Write a chunk in SSE format: "data: {json}\n\n"
    /// </summary>
    private static async Task WriteSSEAsync(
        HttpResponse response,
        AnalysisStreamChunk chunk,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        var sseData = $"data: {json}\n\n";

        await response.WriteAsync(sseData, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// SSE stream chunk for analysis execution.
/// </summary>
/// <param name="Type">Event type: "metadata", "chunk", "done", "error"</param>
/// <param name="Content">Text content for chunk events.</param>
/// <param name="Done">Whether this is the final chunk.</param>
/// <param name="AnalysisId">Analysis record ID (set on metadata and done events).</param>
/// <param name="DocumentName">Source document name (set on metadata event).</param>
/// <param name="TokenUsage">Token usage statistics (set on done event).</param>
/// <param name="Error">Error message (set on error event).</param>
public record AnalysisStreamChunk(
    string Type,
    string? Content,
    bool Done,
    Guid? AnalysisId = null,
    string? DocumentName = null,
    TokenUsage? TokenUsage = null,
    string? Error = null)
{
    public static AnalysisStreamChunk Metadata(Guid analysisId, string documentName) =>
        new("metadata", null, false, AnalysisId: analysisId, DocumentName: documentName);

    public static AnalysisStreamChunk TextChunk(string content) =>
        new("chunk", content, false);

    public static AnalysisStreamChunk Completed(Guid analysisId, TokenUsage tokenUsage) =>
        new("done", null, true, AnalysisId: analysisId, TokenUsage: tokenUsage);

    public static AnalysisStreamChunk FromError(string error) =>
        new("error", null, true, Error: error);
}

/// <summary>
/// Token usage statistics for an analysis.
/// </summary>
public record TokenUsage(int Input, int Output);

/// <summary>
/// Detailed analysis result including chat history.
/// </summary>
public record AnalysisDetailResult
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = string.Empty;
    public AnalysisActionInfo Action { get; init; } = null!;
    public string Status { get; init; } = string.Empty;
    public string? WorkingDocument { get; init; }
    public string? FinalOutput { get; init; }
    public ChatMessageInfo[] ChatHistory { get; init; } = [];
    public TokenUsage? TokenUsage { get; init; }
    public DateTime? StartedOn { get; init; }
    public DateTime? CompletedOn { get; init; }
}

/// <summary>
/// Analysis action info for response.
/// </summary>
public record AnalysisActionInfo(Guid Id, string Name);

/// <summary>
/// Chat message info for response.
/// </summary>
public record ChatMessageInfo(string Role, string Content, DateTime Timestamp);
