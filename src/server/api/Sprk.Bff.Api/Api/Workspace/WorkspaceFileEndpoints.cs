using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai;

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

    // Summarize playbook — "Summarize New File(s)" playbook in Dataverse
    private const string SummarizePlaybookIdConfigKey = "Workspace:SummarizePlaybookId";
    private static readonly Guid DefaultSummarizePlaybookId =
        Guid.Parse("4a72f99c-a119-f111-8343-7ced8d1dc988");

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

        // POST /api/workspace/files/summarize
        group.MapPost("/summarize", HandleSummarize)
            .AddEndpointFilter<WorkspaceAuthorizationFilter>()
            .RequireRateLimiting("ai-stream")
            .DisableAntiforgery()
            .WithName("FileSummarize")
            .WithSummary("Summarize uploaded files using AI")
            .WithDescription(
                "Accepts multipart/form-data uploads (PDF, DOCX, XLSX — max 10 MB each). " +
                "Extracts text, invokes the Summarize playbook, and returns a structured summary " +
                "with TL;DR, narrative summary, practice areas, mentioned parties, and call to action.")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .Produces<SummarizeResponse>(StatusCodes.Status200OK)
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
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Text extraction failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerError("An error occurred while extracting text from the uploaded documents.", httpContext);
        }
    }

    // =========================================================================
    // POST /api/workspace/files/summarize
    // =========================================================================

    private static async Task<IResult> HandleSummarize(
        IFormFileCollection files,
        ITextExtractor textExtractor,
        IPlaybookOrchestrationService playbookService,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var userId = ResolveUserId(httpContext);

        logger.LogInformation(
            "File summarize request received. UserId={UserId}, FileCount={FileCount}, " +
            "CorrelationId={CorrelationId}",
            userId, files?.Count ?? 0, httpContext.TraceIdentifier);

        var validationErrors = ValidateFiles(files!);
        if (validationErrors.Count > 0)
            return ValidationProblem(validationErrors, httpContext);

        try
        {
            // Step 1: Extract text from all files
            var extractedText = await ExtractTextFromFilesAsync(files!, textExtractor, logger, ct);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                logger.LogWarning(
                    "No text could be extracted from any uploaded files. CorrelationId={CorrelationId}",
                    httpContext.TraceIdentifier);

                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "No Text Extracted",
                    detail: "No text could be extracted from the uploaded files. Please try different files.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            }

            logger.LogInformation(
                "Text extraction complete for summarize. TotalChars={TotalChars}. CorrelationId={CorrelationId}",
                extractedText.Length, httpContext.TraceIdentifier);

            // Step 2: Invoke summarize playbook
            var result = await RunSummarizePlaybookAsync(
                extractedText, playbookService, configuration, httpContext, logger, ct);

            return TypedResults.Ok(new SummarizeResponse(result));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Summarize request timed out. CorrelationId={CorrelationId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Timeout",
                detail: "The summarization timed out. Please try again with fewer or smaller files.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "File summarize failed. UserId={UserId}, CorrelationId={CorrelationId}",
                userId, httpContext.TraceIdentifier);

            return ServerError("An error occurred while summarizing the uploaded documents.", httpContext);
        }
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

    /// <summary>
    /// Invokes the Summarize playbook with extracted text and collects the structured output.
    /// </summary>
    private static async Task<JsonElement> RunSummarizePlaybookAsync(
        string documentText,
        IPlaybookOrchestrationService playbookService,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        // Truncate to ~80KB to avoid excessive token usage
        const int maxTextChars = 80_000;
        if (documentText.Length > maxTextChars)
        {
            logger.LogDebug(
                "Truncating combined text from {Original} to {Truncated} chars for summarize.",
                documentText.Length, maxTextChars);
            documentText = documentText[..maxTextChars] + "\n\n[... content truncated ...]";
        }

        // Resolve playbook ID from configuration
        var playbookIdStr = configuration[SummarizePlaybookIdConfigKey];
        var playbookId = !string.IsNullOrEmpty(playbookIdStr) && Guid.TryParse(playbookIdStr, out var parsed)
            ? parsed
            : DefaultSummarizePlaybookId;

        logger.LogInformation(
            "Invoking summarize playbook. PlaybookId={PlaybookId}, TextLength={TextLength}",
            playbookId, documentText.Length);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60)); // Summary may take longer than pre-fill

        var request = new PlaybookRunRequest
        {
            PlaybookId = playbookId,
            DocumentIds = [],
            UserContext = documentText,
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Summarize upload",
                ExtractedText = documentText,
            },
            Parameters = new Dictionary<string, string>
            {
                ["operation"] = "summarize",
            }
        };

        // Collect the final structured output from the playbook stream
        JsonElement? structuredOutput = null;
        string? textOutput = null;

        await foreach (var evt in playbookService.ExecuteAsync(request, httpContext, timeoutCts.Token))
        {
            if (evt.Type == PlaybookEventType.NodeCompleted && evt.NodeOutput != null)
            {
                if (evt.NodeOutput.StructuredData.HasValue)
                {
                    structuredOutput = evt.NodeOutput.StructuredData.Value;
                    logger.LogDebug(
                        "Received structured summarize data from node '{NodeName}'. Confidence={Confidence}.",
                        evt.NodeName, evt.NodeOutput.Confidence);
                }
                else if (!string.IsNullOrWhiteSpace(evt.NodeOutput.TextContent))
                {
                    textOutput = evt.NodeOutput.TextContent;
                }
            }

            if (evt.Type == PlaybookEventType.RunFailed)
            {
                logger.LogWarning("Summarize playbook failed. Error={Error}.", evt.Error);
                throw new InvalidOperationException($"Summarize playbook failed: {evt.Error}");
            }
        }

        // Prefer structured data; fall back to parsing text output as JSON
        if (structuredOutput.HasValue)
        {
            return structuredOutput.Value;
        }

        if (!string.IsNullOrWhiteSpace(textOutput))
        {
            var json = StripMarkdownCodeFences(textOutput.Trim());
            try
            {
                return JsonDocument.Parse(json).RootElement;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex,
                    "Failed to parse summarize text output as JSON. Wrapping as text response.");
                // Return a minimal result with the text as summary
                return JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    tldr = json.Length > 200 ? json[..200] + "..." : json,
                    summary = json,
                    shortSummary = json.Length > 200 ? json[..200] + "..." : json,
                    confidence = 0.5
                })).RootElement;
            }
        }

        logger.LogWarning("Summarize playbook completed but produced no output.");
        throw new InvalidOperationException("Summarize playbook completed but produced no output.");
    }

    private static string StripMarkdownCodeFences(string text)
    {
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..].TrimStart();
        else if (text.StartsWith("```"))
            text = text[3..].TrimStart();

        if (text.EndsWith("```"))
            text = text[..^3].TrimEnd();

        return text;
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
}

/// <summary>
/// Response from the text extraction endpoint.
/// </summary>
public record ExtractTextResponse(string Text);

/// <summary>
/// Response from the summarize endpoint. Wraps the playbook's structured output.
/// </summary>
/// <param name="Result">The structured summary result from the AI playbook.</param>
public record SummarizeResponse(JsonElement Result);
