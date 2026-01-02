using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates analysis execution across Dataverse, SPE, and Azure OpenAI.
/// Implements the BFF orchestration pattern per ADR-001 and ADR-013.
/// </summary>
/// <remarks>
/// Document text extraction flow:
/// 1. Get DocumentEntity from Dataverse (metadata with GraphDriveId, GraphItemId)
/// 2. Download file from SPE via ISpeFileOperations
/// 3. Extract text via ITextExtractor (supports PDF, DOCX, TXT, etc.)
/// 4. Pass extracted text to Azure OpenAI for analysis
/// </remarks>
public class AnalysisOrchestrationService : IAnalysisOrchestrationService
{
    private readonly IDataverseService _dataverseService;
    private readonly ISpeFileOperations _speFileStore;
    private readonly ITextExtractor _textExtractor;
    private readonly IOpenAiClient _openAiClient;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IAnalysisContextBuilder _contextBuilder;
    private readonly IWorkingDocumentService _workingDocumentService;
    private readonly ExportServiceRegistry _exportRegistry;
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisOrchestrationService> _logger;
    private readonly AiTelemetry? _telemetry;

    // In-memory store for Phase 1 (will be replaced with Dataverse in Task 032)
    private static readonly Dictionary<Guid, AnalysisInternalModel> _analysisStore = new();

    public AnalysisOrchestrationService(
        IDataverseService dataverseService,
        ISpeFileOperations speFileStore,
        ITextExtractor textExtractor,
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolver,
        IAnalysisContextBuilder contextBuilder,
        IWorkingDocumentService workingDocumentService,
        ExportServiceRegistry exportRegistry,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisOrchestrationService> logger,
        AiTelemetry? telemetry = null)
    {
        _dataverseService = dataverseService;
        _speFileStore = speFileStore;
        _textExtractor = textExtractor;
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _contextBuilder = contextBuilder;
        _workingDocumentService = workingDocumentService;
        _exportRegistry = exportRegistry;
        _options = options.Value;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAnalysisAsync(
        AnalysisExecuteRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Phase 1: Process only the first document
        var documentId = request.DocumentIds[0];

        _logger.LogInformation("Starting analysis for document {DocumentId}, action {ActionId}",
            documentId, request.ActionId);

        // 1. Get document details from Dataverse
        var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found");

        // 2. Create analysis record (in-memory for Phase 1)
        var analysisId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N")[..12];

        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = documentId,
            DocumentName = document.Name ?? "Unknown",
            ActionId = request.ActionId,
            Status = "InProgress",
            StartedOn = DateTime.UtcNow,
            ChatHistory = []
        };
        _analysisStore[analysisId] = analysis;

        _logger.LogDebug("Created analysis record {AnalysisId} with session {SessionId}", analysisId, sessionId);

        // Emit metadata chunk
        yield return AnalysisStreamChunk.Metadata(analysisId, document.Name ?? "Unknown");

        // 3. Resolve scopes (Skills, Knowledge, Tools)
        var scopes = request.PlaybookId.HasValue
            ? await _scopeResolver.ResolvePlaybookScopesAsync(request.PlaybookId.Value, cancellationToken)
            : await _scopeResolver.ResolveScopesAsync(
                request.SkillIds ?? [],
                request.KnowledgeIds ?? [],
                request.ToolIds ?? [],
                cancellationToken);

        // 4. Get action definition
        var action = await _scopeResolver.GetActionAsync(request.ActionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Action {request.ActionId} not found");

        // 5. Extract document text from SPE via TextExtractor
        var documentText = await ExtractDocumentTextAsync(document, cancellationToken);

        // 6. Build prompts
        var systemPrompt = _contextBuilder.BuildSystemPrompt(action, scopes.Skills);
        var userPrompt = await _contextBuilder.BuildUserPromptAsync(
            documentText, scopes.Knowledge, cancellationToken);

        // Store document text and system prompt for continuation
        analysis = analysis with
        {
            DocumentText = documentText,
            SystemPrompt = systemPrompt
        };
        _analysisStore[analysisId] = analysis;

        // 7. Stream AI completion
        var outputBuilder = new StringBuilder();
        var inputTokens = EstimateTokens(systemPrompt + userPrompt);
        var outputTokens = 0;

        var fullPrompt = BuildFullPrompt(systemPrompt, userPrompt);

        await foreach (var token in _openAiClient.StreamCompletionAsync(fullPrompt, cancellationToken: cancellationToken))
        {
            outputBuilder.Append(token);
            outputTokens = EstimateTokens(outputBuilder.ToString());

            // Stream chunk to client
            yield return AnalysisStreamChunk.TextChunk(token);

            // Update working document periodically (every 500 chars)
            if (outputBuilder.Length % 500 == 0)
            {
                await _workingDocumentService.UpdateWorkingDocumentAsync(
                    analysisId, outputBuilder.ToString(), cancellationToken);
            }
        }

        // 8. Finalize analysis (preserve DocumentText and SystemPrompt for continuations)
        var finalOutput = outputBuilder.ToString();
        analysis = analysis with
        {
            WorkingDocument = finalOutput,
            FinalOutput = finalOutput,
            Status = "Completed",
            CompletedOn = DateTime.UtcNow,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
            // DocumentText and SystemPrompt already set above
        };
        _analysisStore[analysisId] = analysis;

        await _workingDocumentService.FinalizeAnalysisAsync(analysisId, inputTokens, outputTokens, cancellationToken);

        _logger.LogInformation("Analysis {AnalysisId} completed: {InputTokens} input, {OutputTokens} output tokens",
            analysisId, inputTokens, outputTokens);

        // Emit completion chunk
        yield return AnalysisStreamChunk.Completed(analysisId, new TokenUsage(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ContinueAnalysisAsync(
        Guid analysisId,
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Continuing analysis {AnalysisId}", analysisId);

        // Get analysis from in-memory store, or reload from Dataverse if not found
        if (!_analysisStore.TryGetValue(analysisId, out var analysis))
        {
            _logger.LogInformation("Analysis {AnalysisId} not in memory, reloading from Dataverse", analysisId);
            analysis = await ReloadAnalysisFromDataverseAsync(analysisId, cancellationToken);
            _analysisStore[analysisId] = analysis;
        }

        // Build full prompt with document context via context builder service
        var fullPrompt = _contextBuilder.BuildContinuationPromptWithContext(
            analysis.SystemPrompt,
            analysis.DocumentText,
            analysis.ChatHistory,
            userMessage,
            analysis.WorkingDocument ?? string.Empty);

        // Update chat history with user message
        var chatHistory = analysis.ChatHistory.ToList();
        chatHistory.Add(new ChatMessageModel("user", userMessage, DateTime.UtcNow));

        // Stream AI completion
        var outputBuilder = new StringBuilder();
        var inputTokens = EstimateTokens(fullPrompt);

        await foreach (var token in _openAiClient.StreamCompletionAsync(
            fullPrompt,
            cancellationToken: cancellationToken))
        {
            outputBuilder.Append(token);
            yield return AnalysisStreamChunk.TextChunk(token);
        }

        // Save assistant response
        var response = outputBuilder.ToString();
        var outputTokens = EstimateTokens(response);
        chatHistory.Add(new ChatMessageModel("assistant", response, DateTime.UtcNow));

        // Update analysis in store (preserve DocumentText and SystemPrompt)
        analysis = analysis with
        {
            WorkingDocument = response,
            ChatHistory = chatHistory.ToArray(),
            InputTokens = analysis.InputTokens + inputTokens,
            OutputTokens = analysis.OutputTokens + outputTokens
        };
        _analysisStore[analysisId] = analysis;

        await _workingDocumentService.UpdateWorkingDocumentAsync(analysisId, response, cancellationToken);

        _logger.LogInformation("Analysis continuation completed for {AnalysisId}", analysisId);

        yield return AnalysisStreamChunk.Completed(analysisId, new TokenUsage(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        AnalysisSaveRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving working document for analysis {AnalysisId}", analysisId);

        if (!_analysisStore.TryGetValue(analysisId, out var analysis))
        {
            throw new KeyNotFoundException($"Analysis {analysisId} not found");
        }

        if (string.IsNullOrWhiteSpace(analysis.WorkingDocument))
        {
            throw new InvalidOperationException("Analysis has no working document to save");
        }

        // Convert working document to requested format
        var (content, contentType) = ConvertToFormat(analysis.WorkingDocument, request.Format);

        // Save to SPE via working document service
        return _workingDocumentService.SaveToSpeAsync(
            analysisId,
            request.FileName,
            content,
            contentType,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ExportResult> ExportAnalysisAsync(
        Guid analysisId,
        AnalysisExportRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var formatName = request.Format.ToString().ToLowerInvariant();

        _logger.LogInformation("Exporting analysis {AnalysisId} to {Format}", analysisId, request.Format);

        if (!_analysisStore.TryGetValue(analysisId, out var analysis))
        {
            throw new KeyNotFoundException($"Analysis {analysisId} not found");
        }

        // Get the export service for the requested format
        var exportService = _exportRegistry.GetService(request.Format);
        if (exportService == null)
        {
            _logger.LogWarning("Export format {Format} not supported", request.Format);
            stopwatch.Stop();
            _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "format_not_supported");
            return new ExportResult
            {
                ExportType = request.Format,
                Success = false,
                Error = $"Export format {request.Format} is not supported"
            };
        }

        // Build export context from analysis
        var context = new ExportContext
        {
            AnalysisId = analysisId,
            Title = $"Analysis of {analysis.DocumentName}",
            Content = analysis.WorkingDocument ?? analysis.FinalOutput ?? string.Empty,
            Summary = ExtractSummary(analysis.FinalOutput),
            SourceDocumentName = analysis.DocumentName,
            SourceDocumentId = analysis.DocumentId,
            CreatedAt = analysis.StartedOn.HasValue
                ? new DateTimeOffset(analysis.StartedOn.Value, TimeSpan.Zero)
                : DateTimeOffset.UtcNow,
            CreatedBy = "User", // TODO: Extract from context when Dataverse integration is complete
            Options = request.Options
        };

        // Validate
        var validation = exportService.Validate(context);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Export validation failed for {AnalysisId}: {Errors}",
                analysisId, string.Join(", ", validation.Errors));
            stopwatch.Stop();
            _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "validation_failed");
            return new ExportResult
            {
                ExportType = request.Format,
                Success = false,
                Error = string.Join("; ", validation.Errors)
            };
        }

        // Execute export
        var result = await exportService.ExportAsync(context, cancellationToken);

        if (!result.Success)
        {
            stopwatch.Stop();
            _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "export_failed");
            return new ExportResult
            {
                ExportType = request.Format,
                Success = false,
                Error = result.Error
            };
        }

        // For file exports (DOCX, PDF), save to SPE and return result
        if (result.FileBytes != null && request.Format is ExportFormat.Docx or ExportFormat.Pdf)
        {
            var savedDoc = await _workingDocumentService.SaveToSpeAsync(
                analysisId,
                result.FileName ?? $"export_{analysisId:N}.{request.Format.ToString().ToLowerInvariant()}",
                result.FileBytes,
                result.ContentType ?? "application/octet-stream",
                cancellationToken);

            stopwatch.Stop();
            _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true, fileSizeBytes: result.FileBytes.Length);

            return new ExportResult
            {
                ExportType = request.Format,
                Success = true,
                Details = new ExportDetails
                {
                    DocumentId = savedDoc.DocumentId,
                    WebUrl = savedDoc.WebUrl,
                    Status = "Saved to SharePoint"
                }
            };
        }

        // For Email format, extract metadata from the result
        if (request.Format == ExportFormat.Email && result.Metadata != null)
        {
            var recipients = result.Metadata.TryGetValue("Recipients", out var r) ? r as string[] : null;
            var subject = result.Metadata.TryGetValue("Subject", out var s) ? s as string : null;
            var sentAt = result.Metadata.TryGetValue("SentAt", out var t) ? t : null;

            stopwatch.Stop();
            _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true);

            return new ExportResult
            {
                ExportType = request.Format,
                Success = true,
                Details = new ExportDetails
                {
                    Status = $"Email sent to {recipients?.Length ?? 0} recipient(s)" +
                             (sentAt != null ? $" at {sentAt:g}" : string.Empty)
                }
            };
        }

        // For other formats (Teams), return the result directly
        stopwatch.Stop();
        _telemetry?.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true);

        return new ExportResult
        {
            ExportType = request.Format,
            Success = true,
            Details = new ExportDetails
            {
                Status = "Export completed"
            }
        };
    }

    /// <summary>
    /// Extracts a summary from the analysis output.
    /// Looks for a Summary section or takes the first paragraph.
    /// </summary>
    private static string? ExtractSummary(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        // Try to find a summary section
        var summaryMatch = System.Text.RegularExpressions.Regex.Match(
            output,
            @"(?:##?\s*(?:Executive\s+)?Summary[\s:]*\n)([\s\S]*?)(?=\n##|\z)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (summaryMatch.Success)
        {
            return summaryMatch.Groups[1].Value.Trim();
        }

        // Otherwise, take the first paragraph (up to 500 chars)
        var firstPara = output.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

        if (firstPara != null && firstPara.Length > 500)
        {
            firstPara = firstPara[..500] + "...";
        }

        return firstPara;
    }

    /// <inheritdoc />
    public Task<AnalysisDetailResult> GetAnalysisAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Retrieving analysis {AnalysisId}", analysisId);

        if (!_analysisStore.TryGetValue(analysisId, out var analysis))
        {
            throw new KeyNotFoundException($"Analysis {analysisId} not found");
        }

        return Task.FromResult(new AnalysisDetailResult
        {
            Id = analysis.Id,
            DocumentId = analysis.DocumentId,
            DocumentName = analysis.DocumentName,
            Action = new AnalysisActionInfo(analysis.ActionId, analysis.ActionName),
            Status = analysis.Status,
            WorkingDocument = analysis.WorkingDocument,
            FinalOutput = analysis.FinalOutput,
            ChatHistory = analysis.ChatHistory
                .Select(m => new ChatMessageInfo(m.Role, m.Content, m.Timestamp))
                .ToArray(),
            TokenUsage = analysis.InputTokens > 0 || analysis.OutputTokens > 0
                ? new TokenUsage(analysis.InputTokens, analysis.OutputTokens)
                : null,
            StartedOn = analysis.StartedOn,
            CompletedOn = analysis.CompletedOn
        });
    }

    /// <inheritdoc />
    public async Task<AnalysisResumeResult> ResumeAnalysisAsync(
        Guid analysisId,
        AnalysisResumeRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Resuming analysis {AnalysisId} for document {DocumentId}, IncludeChatHistory={IncludeChatHistory}",
            analysisId, request.DocumentId, request.IncludeChatHistory);

        try
        {
            // Parse chat history if provided and requested
            var chatHistory = Array.Empty<ChatMessageModel>();
            var chatMessagesRestored = 0;

            if (request.IncludeChatHistory && !string.IsNullOrWhiteSpace(request.ChatHistory))
            {
                try
                {
                    var messages = System.Text.Json.JsonSerializer.Deserialize<ChatMessageModel[]>(
                        request.ChatHistory,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (messages != null)
                    {
                        chatHistory = messages;
                        chatMessagesRestored = messages.Length;
                        _logger.LogDebug("Restored {Count} chat messages for analysis {AnalysisId}",
                            chatMessagesRestored, analysisId);
                    }
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize chat history for analysis {AnalysisId}", analysisId);
                    // Continue with empty chat history rather than failing
                }
            }

            // Extract document text for context in chat continuations
            string? documentText = null;
            string? systemPrompt = null;

            try
            {
                var document = await _dataverseService.GetDocumentAsync(
                    request.DocumentId.ToString(), cancellationToken);

                if (document != null)
                {
                    _logger.LogDebug("Extracting document text for resumed analysis {AnalysisId}", analysisId);
                    documentText = await ExtractDocumentTextAsync(document, cancellationToken);

                    // Build a default system prompt for continuations
                    systemPrompt = BuildDefaultSystemPrompt();

                    _logger.LogInformation(
                        "Extracted {CharCount} characters of document text for analysis {AnalysisId}",
                        documentText?.Length ?? 0, analysisId);
                }
                else
                {
                    _logger.LogWarning("Document {DocumentId} not found in Dataverse", request.DocumentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to extract document text for resumed analysis {AnalysisId}. Continuing without document context.",
                    analysisId);
                // Continue without document text rather than failing the resume
            }

            // Create or update in-memory session with document context
            var analysis = new AnalysisInternalModel
            {
                Id = analysisId,
                DocumentId = request.DocumentId,
                DocumentName = request.DocumentName ?? "Unknown",
                ActionId = Guid.Empty, // Not needed for resumed session
                Status = "InProgress",
                DocumentText = documentText,
                SystemPrompt = systemPrompt,
                WorkingDocument = request.WorkingDocument,
                ChatHistory = chatHistory,
                StartedOn = DateTime.UtcNow
            };

            _analysisStore[analysisId] = analysis;

            _logger.LogInformation(
                "Analysis {AnalysisId} resumed successfully: {ChatMessages} messages, WorkingDoc={HasWorkingDoc}, HasDocText={HasDocText}",
                analysisId, chatMessagesRestored, !string.IsNullOrWhiteSpace(request.WorkingDocument),
                !string.IsNullOrWhiteSpace(documentText));

            return new AnalysisResumeResult
            {
                AnalysisId = analysisId,
                Success = true,
                ChatMessagesRestored = chatMessagesRestored,
                WorkingDocumentRestored = !string.IsNullOrWhiteSpace(request.WorkingDocument)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume analysis {AnalysisId}", analysisId);

            return new AnalysisResumeResult
            {
                AnalysisId = analysisId,
                Success = false,
                Error = ex.Message
            };
        }
    }

    // === Private Helper Methods ===

    /// <summary>
    /// Extract text from a document stored in SharePoint Embedded.
    /// Downloads the file and uses TextExtractor to extract readable text.
    /// </summary>
    private async Task<string> ExtractDocumentTextAsync(
        DocumentEntity document,
        CancellationToken cancellationToken)
    {
        // Check if document has a file
        if (!document.HasFile || string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} has no file attached (HasFile={HasFile}, DriveId={DriveId}, ItemId={ItemId})",
                document.Id, document.HasFile, document.GraphDriveId, document.GraphItemId);

            return $"[Document: {document.Name}]\n\nNo file content available for this document.";
        }

        // Check if file type is supported for extraction
        var fileName = document.FileName ?? document.Name ?? "unknown";
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        if (!_textExtractor.IsSupported(extension))
        {
            _logger.LogWarning(
                "File type {Extension} is not supported for text extraction (Document: {DocumentId})",
                extension, document.Id);

            return $"[Document: {document.Name}]\n\nFile type '{extension}' is not supported for text extraction.";
        }

        try
        {
            _logger.LogDebug(
                "Downloading document {DocumentId} from SPE (Drive={DriveId}, Item={ItemId})",
                document.Id, document.GraphDriveId, document.GraphItemId);

            // Download file from SharePoint Embedded
            using var fileStream = await _speFileStore.DownloadFileAsync(
                document.GraphDriveId,
                document.GraphItemId,
                cancellationToken);

            if (fileStream == null)
            {
                _logger.LogWarning("Failed to download document {DocumentId} from SPE", document.Id);
                return $"[Document: {document.Name}]\n\nFailed to download file from storage.";
            }

            _logger.LogDebug("Extracting text from {FileName} ({FileSize} bytes)", fileName, document.FileSize);

            // Extract text using TextExtractor
            var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            if (!extractionResult.Success)
            {
                _logger.LogWarning(
                    "Text extraction failed for document {DocumentId}: {Error}",
                    document.Id, extractionResult.ErrorMessage);

                return $"[Document: {document.Name}]\n\nText extraction failed: {extractionResult.ErrorMessage}";
            }

            // Check for vision-required files (images)
            if (extractionResult.IsVisionRequired)
            {
                _logger.LogInformation(
                    "Document {DocumentId} is an image file requiring vision model processing",
                    document.Id);

                // For now, return a note that image analysis would be here
                // Full vision integration is a separate enhancement
                return $"[Document: {document.Name}]\n\n[Image file - vision analysis not yet integrated for Analysis feature]";
            }

            _logger.LogInformation(
                "Successfully extracted {CharCount} characters from document {DocumentId} via {Method}",
                extractionResult.Text?.Length ?? 0, document.Id, extractionResult.Method);

            return extractionResult.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting text from document {DocumentId}", document.Id);
            return $"[Document: {document.Name}]\n\nError extracting text: {ex.Message}";
        }
    }

    private static (byte[] Content, string ContentType) ConvertToFormat(string markdown, SaveDocumentFormat format)
    {
        // Phase 1: Return markdown as text for all formats
        // Full DOCX/PDF generation will be added in later tasks
        return format switch
        {
            SaveDocumentFormat.Md => (Encoding.UTF8.GetBytes(markdown), "text/markdown"),
            SaveDocumentFormat.Txt => (Encoding.UTF8.GetBytes(markdown), "text/plain"),
            SaveDocumentFormat.Docx => (Encoding.UTF8.GetBytes(markdown), "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            SaveDocumentFormat.Pdf => (Encoding.UTF8.GetBytes(markdown), "application/pdf"),
            _ => (Encoding.UTF8.GetBytes(markdown), "text/plain")
        };
    }

    private static string BuildFullPrompt(string systemPrompt, string userPrompt)
    {
        return $"{systemPrompt}\n\n---\n\n{userPrompt}";
    }

    /// <summary>
    /// Build a default system prompt for resumed analysis sessions.
    /// Used when the original action/skills are not available.
    /// </summary>
    private static string BuildDefaultSystemPrompt()
    {
        return """
            You are an AI assistant helping to analyze and discuss documents.

            ## Instructions
            - Provide helpful, accurate responses to questions about the document
            - Use the document content as your primary source of information
            - If asked to modify or update the analysis, provide the complete updated content
            - Format responses in clear, readable Markdown
            - Be concise but thorough in your answers

            ## Output Format
            Provide your response in Markdown format with appropriate headings and structure.
            """;
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <summary>
    /// Reload analysis context from Dataverse when not found in memory.
    /// Extracts document text so chat continuations have context.
    /// </summary>
    private async Task<AnalysisInternalModel> ReloadAnalysisFromDataverseAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reloading analysis {AnalysisId} from Dataverse", analysisId);

        // 1. Get analysis record from Dataverse
        var analysisRecord = await _dataverseService.GetAnalysisAsync(analysisId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Analysis {analysisId} not found in Dataverse");

        // 2. Get the associated document for text extraction
        string? documentText = null;
        string documentName = "Unknown";

        if (analysisRecord.DocumentId != Guid.Empty)
        {
            try
            {
                var document = await _dataverseService.GetDocumentAsync(
                    analysisRecord.DocumentId.ToString(), cancellationToken);

                if (document != null)
                {
                    documentName = document.Name ?? document.FileName ?? "Unknown";
                    documentText = await ExtractDocumentTextAsync(document, cancellationToken);
                    _logger.LogInformation(
                        "Extracted {CharCount} characters of document text for reloaded analysis {AnalysisId}",
                        documentText?.Length ?? 0, analysisId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to extract document text for analysis {AnalysisId}. Continuing without document context.",
                    analysisId);
            }
        }

        // 3. Parse chat history from the analysis record
        var chatHistory = Array.Empty<ChatMessageModel>();
        if (!string.IsNullOrWhiteSpace(analysisRecord.ChatHistory))
        {
            try
            {
                var messages = System.Text.Json.JsonSerializer.Deserialize<ChatMessageModel[]>(
                    analysisRecord.ChatHistory,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (messages != null)
                {
                    chatHistory = messages;
                    _logger.LogDebug("Restored {Count} chat messages for analysis {AnalysisId}",
                        messages.Length, analysisId);
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize chat history for analysis {AnalysisId}", analysisId);
            }
        }

        // 4. Build the internal model with document context
        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = analysisRecord.DocumentId,
            DocumentName = documentName,
            ActionId = Guid.Empty,
            Status = "InProgress",
            DocumentText = documentText,
            SystemPrompt = BuildDefaultSystemPrompt(),
            WorkingDocument = analysisRecord.WorkingDocument,
            ChatHistory = chatHistory,
            StartedOn = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Successfully reloaded analysis {AnalysisId} with {DocChars} chars of document text and {ChatCount} chat messages",
            analysisId, documentText?.Length ?? 0, chatHistory.Length);

        return analysis;
    }
}

// === Internal Models (will be moved to Spaarke.Dataverse in Task 032) ===

/// <summary>
/// Internal model for analysis with chat history.
/// </summary>
public record AnalysisInternalModel
{
    public Guid Id { get; init; }
    public Guid DocumentId { get; init; }
    public string DocumentName { get; init; } = string.Empty;
    public Guid ActionId { get; init; }
    public string ActionName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? DocumentText { get; init; }  // Extracted document content for context
    public string? SystemPrompt { get; init; }  // System prompt for continuations
    public string? WorkingDocument { get; init; }
    public string? FinalOutput { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public DateTime? StartedOn { get; init; }
    public DateTime? CompletedOn { get; init; }
    public ChatMessageModel[] ChatHistory { get; init; } = [];
}

/// <summary>
/// Internal model for chat message.
/// </summary>
public record ChatMessageModel(string Role, string Content, DateTime Timestamp);
