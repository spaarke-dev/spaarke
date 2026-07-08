using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Workspace;

/// <summary>
/// Workspace File endpoints: standalone file operations (text extraction, summarization)
/// that are not tied to a specific entity pre-fill workflow.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern — MapPost with handler delegate.
/// Follows ADR-008: Endpoint authorization filter per endpoint.
/// Follows ADR-013: AI document analysis rate-limited (uses existing "ai-stream" policy at 10 req/min).
/// </remarks>
public static class WorkspaceFileEndpoints
{
    // Supported file extensions
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".xlsx", ".txt", ".md", ".csv" };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    // Summarize execution — FR-P3-05 hard cutover (ai-architecture-redesign-r1 task 044):
    // summarize-file executes EXCLUSIVELY on the prompted executor. IActionResolver
    // resolves the summarize-file Binding row's Action (sprk_playbookconsumer →
    // sprk_analysisaction, single routing surface per ADR-039) and IActionRunner renders
    // the Action's JPS prompt + output schema. The former consumer-specific wrapper class
    // and the Playbook Engine fall-through (dispatch when the row had no Action target)
    // were DELETED per NFR-08 — a Binding row without an Action target is a catalog
    // authoring error surfaced as an SSE error chunk, never an engine fallback.
    //
    // Historical: the prior hardcoded GUID fallback was removed in Phase 1
    // (chat-routing-redesign-r1 task 019); the config-key fallback surface was removed
    // by FR-P3-01 (task 040); the engine fall-through + wrapper were removed by FR-P3-05.

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Registers workspace file endpoints under /api/workspace/files.
    /// </summary>
    public static IEndpointRouteBuilder MapWorkspaceFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workspace/files")
            .RequireAuthorization()
            .WithTags("Workspace Files");

        // POST /api/workspace/files/extract-text
        group.MapPost("/extract-text", HandleExtractText)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")
            .DisableAntiforgery()
            .WithName("FileExtractText")
            .WithSummary("Extract text from uploaded files")
            .WithDescription(
                "Accepts multipart/form-data uploads (PDF, DOCX, XLSX, TXT, MD, CSV — max 10 MB each). " +
                "Extracts text content and returns a single concatenated text string.")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces<ExtractTextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/workspace/files/summarize  (SSE stream)
        group.MapPost("/summarize", HandleSummarize)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")
            .DisableAntiforgery()
            .WithName("FileSummarize")
            .WithSummary("Summarize uploaded files using AI (SSE stream)")
            .WithDescription(
                "Accepts multipart/form-data uploads (PDF, DOCX, XLSX — max 10 MB each). " +
                "Extracts text, invokes the Summarize playbook, and streams progress events " +
                "followed by a structured result chunk (tldr, summary, practice areas, parties, call to action).")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // =========================================================================
    // POST /api/workspace/files/extract-text
    // =========================================================================

    private static async Task<IResult> HandleExtractText(
        IFormFileCollection files,
        ITextExtractor textExtractor,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = ResolveUserId(httpContext);

        logger.LogInformation(
            "Text extraction request received. UserId={UserId}, FileCount={FileCount}, " +
            "CorrelationId={CorrelationId}",
            userId, files?.Count ?? 0, httpContext.TraceIdentifier);

        var validationErrors = ValidateFiles(files!);
        if (validationErrors.Count > 0)
            return ValidationProblem(validationErrors, httpContext);

        try
        {
            var text = await ExtractTextFromFilesAsync(files!, textExtractor, logger, ct);

            logger.LogInformation(
                "Text extraction complete. TotalChars={TotalChars}, CorrelationId={CorrelationId}",
                text.Length, httpContext.TraceIdentifier);

            return TypedResults.Ok(new ExtractTextResponse(text));
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 L4): NullTextExtractor surfaced.
            logger.LogDebug(
                "Text extraction called while AI feature disabled. ErrorCode={ErrorCode}, CorrelationId={CorrelationId}",
                ex.ErrorCode, httpContext.TraceIdentifier);
            return ex.AsFeatureDisabled503();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Text extraction failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerError("An error occurred while extracting text from the uploaded documents.", httpContext);
        }
    }

    // =========================================================================
    // POST /api/workspace/files/summarize  (SSE stream)
    // =========================================================================

    private static async Task HandleSummarize(
        IFormFileCollection files,
        ITextExtractor textExtractor,
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var response = httpContext.Response;
        var userId = ResolveUserId(httpContext);

        // Validate before setting SSE headers so we can still return a proper 400
        var validationErrors = ValidateFiles(files!);
        if (validationErrors.Count > 0)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            response.ContentType = "application/problem+json";
            var problem = JsonSerializer.Serialize(new
            {
                title = "Invalid Files",
                status = 400,
                detail = string.Join(" | ", validationErrors),
                correlationId = httpContext.TraceIdentifier
            }, JsonOptions);
            await response.WriteAsync(problem, ct);
            return;
        }

        // Set SSE headers — must happen before first write
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        logger.LogInformation(
            "File summarize SSE request. UserId={UserId}, FileCount={FileCount}, CorrelationId={CorrelationId}",
            userId, files!.Count, httpContext.TraceIdentifier);

        try
        {
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("document_loaded", "Opening document..."), ct);

            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("extracting_text", "Reading content..."), ct);
            var extractedText = await ExtractTextFromFilesAsync(files!, textExtractor, logger, ct);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                logger.LogWarning("No text extracted from uploaded files. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
                await WriteSSEAsync(response, AnalysisStreamChunk.FromError("No text could be extracted from the uploaded files."), CancellationToken.None);
                await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                await response.Body.FlushAsync(CancellationToken.None);
                return;
            }

            logger.LogInformation(
                "Text extraction complete for summarize. TotalChars={TotalChars}. CorrelationId={CorrelationId}",
                extractedText.Length, httpContext.TraceIdentifier);

            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("context_ready", "Preparing analysis..."), ct);
            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("analyzing", "Analyzing..."), ct);

            // FR-P3-05 hard cutover (ai-architecture-redesign-r1 task 044): summarize-file
            // executes EXCLUSIVELY on the prompted executor (IActionResolver resolves the
            // summarize-file Binding row's Action; IActionRunner renders + runs it). The
            // former wrapper class and the Playbook Engine fall-through (used when the row
            // had no Action target) were DELETED per NFR-08 — a row without an Action
            // target surfaces as an error chunk from the resolver, never an engine dispatch.
            var displayName = files.FirstOrDefault()?.FileName ?? "combined-input";
            await foreach (var chunk in ExecuteSummarizeActionAsync(
                extractedText, displayName, actionResolver, actionRunner, httpContext, logger, ct))
            {
                await WriteSSEAsync(response, chunk, ct);
            }

            await WriteSSEAsync(response, AnalysisStreamChunk.Progress("delivering", "Delivering results..."), ct);
            await response.WriteAsync("data: [DONE]\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 L3/L4): NullTextExtractor or
            // NullPlaybookOrchestrationService surfaced. Response is SSE — emit error chunk.
            logger.LogDebug(
                "File summarize called while AI feature disabled. ErrorCode={ErrorCode}, CorrelationId={CorrelationId}",
                ex.ErrorCode, httpContext.TraceIdentifier);
            await WriteSSEAsync(response, AnalysisStreamChunk.FromError($"[{ex.ErrorCode}] {ex.Message}"), CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Summarize SSE timed out. CorrelationId={CorrelationId}", httpContext.TraceIdentifier);
            await WriteSSEAsync(response, AnalysisStreamChunk.FromError("Summarization timed out. Please try again with fewer or smaller files."), CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "File summarize SSE failed. UserId={UserId}, CorrelationId={CorrelationId}", userId, httpContext.TraceIdentifier);
            await WriteSSEAsync(response, AnalysisStreamChunk.FromError("An error occurred while summarizing the uploaded documents."), CancellationToken.None);
            await response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
        }
    }

    /// <summary>
    /// Executes summarize-file on the prompted executor and emits progress + a single
    /// "result" SSE chunk with the structured output (FR-P3-05 wrapper absorption:
    /// resolve the Binding row's Action via <see cref="IActionResolver"/>, run it via
    /// <see cref="IActionRunner"/> — behavior preserved from the deleted wrapper class).
    /// Resolution/LLM failures surface as error chunks so the stream never dies silently.
    /// </summary>
    private static async IAsyncEnumerable<AnalysisStreamChunk> ExecuteSummarizeActionAsync(
        string extractedText,
        string fileName,
        IActionResolver actionResolver,
        IActionRunner actionRunner,
        HttpContext httpContext,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var tenantId = httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        var runContext = new LinearRunContext
        {
            ConsumerType = ConsumerTypes.SummarizeFile,
            CorrelationId = httpContext.TraceIdentifier,
            TenantId = tenantId,
        };

        yield return AnalysisStreamChunk.Progress("resolving_action", "Resolving action configuration…");
        AnalysisAction? action = null;
        string? resolveError = null;
        try
        {
            action = await actionResolver.ResolveAsync(ConsumerTypes.SummarizeFile, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException)
        {
            // Kill-switch (ADR-032 P3): propagate so the endpoint's catch emits the
            // canonical 503 / SSE-error-chunk pattern (parity with the deleted Null wrapper).
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to resolve action for consumerType={ConsumerType}. CorrelationId={CorrelationId}",
                ConsumerTypes.SummarizeFile, httpContext.TraceIdentifier);
            resolveError = $"Failed to resolve action: {ex.Message}";
        }
        if (resolveError != null)
        {
            yield return AnalysisStreamChunk.FromError(resolveError);
            yield break;
        }

        var docText = new DocumentText
        {
            DocumentId = null,
            FileName = fileName,
            ExtractedText = extractedText,
        };

        yield return AnalysisStreamChunk.Progress("calling_llm", "Analyzing document(s) with AI…");
        JsonElement aiOutput = default;
        string? llmError = null;
        try
        {
            aiOutput = await actionRunner.RunAsync(action!, docText, runContext, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LLM call failed for consumerType={ConsumerType}. CorrelationId={CorrelationId}",
                ConsumerTypes.SummarizeFile, httpContext.TraceIdentifier);
            llmError = $"AI analysis failed: {ex.Message}";
        }
        if (llmError != null)
        {
            yield return AnalysisStreamChunk.FromError(llmError);
            yield break;
        }

        // Emit the entire structured output as a single SSE `result` chunk —
        // the client parses Content as JSON (contract unchanged from the wrapper).
        yield return AnalysisStreamChunk.Result(aiOutput.GetRawText());
    }

    // =========================================================================
    // Shared helpers
    // =========================================================================

    /// <summary>
    /// Extracts text from all uploaded files. Returns concatenated text.
    /// </summary>
    private static async Task<string> ExtractTextFromFilesAsync(
        IFormFileCollection files,
        ITextExtractor textExtractor,
        ILogger logger,
        CancellationToken ct)
    {
        var allExtractedText = new StringBuilder();

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

            if (!textExtractor.IsSupported(extension))
            {
                logger.LogWarning(
                    "Text extractor does not support extension '{Extension}' for file '{FileName}'. Skipping.",
                    extension, fileName);
                continue;
            }

            using var fileStream = file.OpenReadStream();
            var extractionResult = await textExtractor.ExtractAsync(fileStream, fileName, ct);

            if (extractionResult.Success && !string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                if (files.Count > 1)
                {
                    allExtractedText.AppendLine($"===== Document: {fileName} =====");
                }
                allExtractedText.AppendLine(extractionResult.Text);
                allExtractedText.AppendLine();

                logger.LogDebug(
                    "Extracted {CharCount} characters from '{FileName}'.",
                    extractionResult.CharacterCount, fileName);
            }
            else
            {
                logger.LogWarning(
                    "Text extraction failed for '{FileName}': {Error}.",
                    fileName, extractionResult.ErrorMessage);
            }
        }

        return allExtractedText.ToString().Trim();
    }

    private static string ResolveUserId(HttpContext httpContext)
    {
        return httpContext.Items["UserId"]?.ToString()
            ?? httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }

    private static List<string> ValidateFiles(IFormFileCollection files)
    {
        var errors = new List<string>();

        if (files == null || files.Count == 0)
        {
            errors.Add("At least one file must be uploaded.");
            return errors;
        }

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                errors.Add($"File '{file.FileName}' is empty.");
                continue;
            }

            if (file.Length > MaxFileSizeBytes)
            {
                errors.Add($"File '{file.FileName}' exceeds the maximum allowed size of 10 MB " +
                            $"({file.Length / 1024 / 1024:F1} MB uploaded).");
            }

            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
            if (!AllowedExtensions.Contains(extension))
            {
                errors.Add($"File '{file.FileName}' has unsupported type '{extension}'. " +
                            "Only PDF, DOCX, XLSX, TXT, MD, and CSV files are accepted.");
            }
        }

        return errors;
    }

    private static IResult ValidationProblem(List<string> errors, HttpContext httpContext)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid Files",
            detail: string.Join(" | ", errors),
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = httpContext.TraceIdentifier,
                ["errors"] = errors
            });
    }

    private static IResult ServerError(string detail, HttpContext httpContext)
    {
        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["correlationId"] = httpContext.TraceIdentifier
            });
    }

    private static async Task WriteSSEAsync(HttpResponse response, AnalysisStreamChunk chunk, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(chunk, JsonOptions);
        await response.WriteAsync($"data: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}

/// <summary>
/// Response from the text extraction endpoint.
/// </summary>
public record ExtractTextResponse(string Text);
