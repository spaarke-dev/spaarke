using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using AiExtractedEntities = Sprk.Bff.Api.Models.Ai.ExtractedEntities;
// Explicit aliases to resolve ambiguity with types in the same namespace (Sprk.Bff.Api.Services.Ai).
// AppOnlyAnalysisService defines DocumentAnalysisResult; AnalysisEndpoints defines ExtractedEntities.
using AnalysisDocumentResult = Sprk.Bff.Api.Models.Ai.DocumentAnalysisResult;

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
///
/// Constructor dependencies reduced from 21 to 10 by extracting:
/// - AnalysisDocumentLoader (text extraction, document reload, caching)
/// - AnalysisRagProcessor (RAG search, cache key computation, tenant resolution)
/// - AnalysisResultPersistence (output storage, RAG indexing enqueue, working doc finalization)
/// </remarks>
public class AnalysisOrchestrationService : IAnalysisOrchestrationService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IScopeResolverService _scopeResolver;
    private readonly IAnalysisContextBuilder _contextBuilder;
    private readonly IPlaybookService _playbookService;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;
    private readonly INodeService _nodeService;
    private readonly AnalysisDocumentLoader _documentLoader;
    private readonly AnalysisRagProcessor _ragProcessor;
    private readonly AnalysisResultPersistence _resultPersistence;
    private readonly ILogger<AnalysisOrchestrationService> _logger;

    /// <summary>
    /// Constructor with 10 parameters (ADR-010 compliant).
    /// Reduced from 21 by extracting AnalysisDocumentLoader, AnalysisRagProcessor,
    /// and AnalysisResultPersistence.
    /// </summary>
    public AnalysisOrchestrationService(
        IOpenAiClient openAiClient,
        IScopeResolverService scopeResolver,
        IAnalysisContextBuilder contextBuilder,
        IPlaybookService playbookService,
        IToolHandlerRegistry toolHandlerRegistry,
        INodeService nodeService,
        AnalysisDocumentLoader documentLoader,
        AnalysisRagProcessor ragProcessor,
        AnalysisResultPersistence resultPersistence,
        ILogger<AnalysisOrchestrationService> logger)
    {
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _contextBuilder = contextBuilder;
        _playbookService = playbookService;
        _toolHandlerRegistry = toolHandlerRegistry;
        _nodeService = nodeService;
        _documentLoader = documentLoader;
        _ragProcessor = ragProcessor;
        _resultPersistence = resultPersistence;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAnalysisAsync(
        AnalysisExecuteRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CRITICAL FIX: If PlaybookId is provided, delegate to ExecutePlaybookAsync
        // which executes each tool in the playbook (SummaryHandler, GenericAnalysisHandler, etc.)
        // and stores structured outputs in Dataverse Document fields.
        // Without this delegation, the method just does a raw OpenAI call that doesn't use tools.
        if (request.PlaybookId.HasValue)
        {
            _logger.LogInformation(
                "ExecuteAnalysisAsync: Delegating to ExecutePlaybookAsync for playbook {PlaybookId}",
                request.PlaybookId.Value);

            var playbookRequest = new PlaybookExecuteRequest
            {
                PlaybookId = request.PlaybookId.Value,
                DocumentIds = request.DocumentIds,
                ActionId = request.ActionId,
                AdditionalContext = null,
                AnalysisId = request.AnalysisId
            };

            await foreach (var chunk in ExecutePlaybookAsync(playbookRequest, httpContext, cancellationToken))
            {
                yield return chunk;
            }

            yield break; // Exit after playbook execution
        }

        // No playbook specified - continue with action-based analysis (raw OpenAI call)
        // Phase 1: Process only the first document
        var documentId = request.DocumentIds[0];

        _logger.LogInformation("Starting action-based analysis for document {DocumentId}, action {ActionId}",
            documentId, request.ActionId);

        // 1. Get document details from Dataverse
        var document = await _documentLoader.GetDocumentAsync(documentId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found");

        // Log document details for debugging extraction issues
        _logger.LogInformation(
            "Document retrieved from Dataverse: Id={DocumentId}, Name={Name}, HasFile={HasFile}, " +
            "FileName={FileName}, GraphDriveId={GraphDriveId}, GraphItemId={GraphItemId}",
            document.Id, document.Name, document.HasFile,
            document.FileName, document.GraphDriveId ?? "(null)", document.GraphItemId ?? "(null)");

        // 2. Use existing analysis record ID from Dataverse (if provided) or generate a new one.
        var analysisId = request.AnalysisId ?? Guid.NewGuid();
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
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        _logger.LogDebug("Created analysis record {AnalysisId} with session {SessionId}", analysisId, sessionId);

        // Emit metadata chunk
        yield return AnalysisStreamChunk.Metadata(analysisId, document.Name ?? "Unknown");
        yield return AnalysisStreamChunk.Progress("document_loaded", "Opening document...");

        // 3. Resolve scopes (Skills, Knowledge, Tools) and action
        if (!request.ActionId.HasValue)
        {
            throw new ArgumentException("ActionId is required when not using a playbook");
        }

        var actionId = request.ActionId.Value;

        // Resolve scopes and action in parallel — both are independent Dataverse reads
        var scopesTask = _scopeResolver.ResolveScopesAsync(
            request.SkillIds ?? [],
            request.KnowledgeIds ?? [],
            request.ToolIds ?? [],
            cancellationToken);
        var actionTask = _scopeResolver.GetActionAsync(actionId, cancellationToken);
        await Task.WhenAll(scopesTask, actionTask);
        var scopes = await scopesTask;
        var action = await actionTask
            ?? throw new KeyNotFoundException($"Action {actionId} not found");

        // Update analysis record with resolved action ID
        analysis.ActionId = actionId;

        // 5. Extract document text from SPE via TextExtractor (uses OBO auth)
        yield return AnalysisStreamChunk.Progress("extracting_text", "Reading content...");
        var documentText = await _documentLoader.ExtractDocumentTextAsync(document, httpContext, cancellationToken);
        yield return AnalysisStreamChunk.Progress("text_extracted", "Preparing analysis...");

        // Log extraction result metadata only (ADR-015: MUST NOT log document content)
        _logger.LogInformation(
            "Document text extracted: Length={TextLength}",
            documentText.Length);

        // Check for extraction failure indicators
        if (documentText.Contains("No file content available") ||
            documentText.Contains("not configured") ||
            documentText.Contains("not supported") ||
            documentText.Contains("Failed to download"))
        {
            _logger.LogWarning("Document extraction returned fallback/error message for document (textLength={TextLength})", documentText.Length);
        }

        // 6. Process RAG knowledge sources (query Azure AI Search)
        var ragAnalysisContext = new AnalysisDocumentResult
        {
            Summary = documentText.Length > 500 ? documentText[..500] : documentText,
            Keywords = string.Empty,
            Entities = new AiExtractedEntities()
        };
        var processedKnowledge = await _ragProcessor.ProcessRagKnowledgeAsync(
            scopes.Knowledge, ragAnalysisContext, cancellationToken);
        yield return AnalysisStreamChunk.Progress("context_ready", "Running analysis...");

        // 7. Build prompts
        var systemPrompt = _contextBuilder.BuildSystemPrompt(action, scopes.Skills);
        var userPrompt = await _contextBuilder.BuildUserPromptAsync(
            documentText, processedKnowledge, cancellationToken);

        // Store document text and system prompt for continuation
        analysis = analysis with
        {
            DocumentText = documentText,
            SystemPrompt = systemPrompt
        };
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        // 7. Stream AI completion
        var outputBuilder = new StringBuilder();
        var inputTokens = EstimateTokens(systemPrompt + userPrompt);
        var outputTokens = 0;

        var fullPrompt = BuildFullPrompt(systemPrompt, userPrompt);

        yield return AnalysisStreamChunk.Progress("analyzing", "Analyzing...");
        await foreach (var token in _openAiClient.StreamCompletionAsync(fullPrompt, cancellationToken: cancellationToken))
        {
            outputBuilder.Append(token);
            outputTokens = EstimateTokens(outputBuilder.ToString());

            // Stream chunk to client
            yield return AnalysisStreamChunk.TextChunk(token);

            // Update working document periodically (every 500 chars)
            if (outputBuilder.Length % 500 == 0)
            {
                await _resultPersistence.UpdateWorkingDocumentAsync(
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
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        // Persist final content to Dataverse (sprk_workingdocument)
        await _resultPersistence.UpdateWorkingDocumentAsync(analysisId, finalOutput, cancellationToken);
        await _resultPersistence.FinalizeAnalysisAsync(analysisId, inputTokens, outputTokens, cancellationToken);

        _logger.LogInformation("Analysis {AnalysisId} completed: {InputTokens} input, {OutputTokens} output tokens",
            analysisId, inputTokens, outputTokens);

        // Enqueue RAG indexing job after analysis completes (ADR-001 / ADR-004).
        var analysisTenantId = _ragProcessor.GetTenantIdFromClaims() ?? "unknown";
        await _resultPersistence.EnqueueRagIndexingJobAsync(
            analysisId.ToString(),
            documentId.ToString(),
            analysisTenantId,
            document.GraphDriveId,
            document.GraphItemId,
            cancellationToken);

        // Emit completion chunk
        yield return AnalysisStreamChunk.Completed(analysisId, new TokenUsage(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ContinueAnalysisAsync(
        Guid analysisId,
        string userMessage,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Continuing analysis {AnalysisId}", analysisId);

        // Get analysis from Redis cache, or reload from Dataverse if cache miss
        var cachedEntry = await _documentLoader.GetCachedAnalysisAsync(analysisId);
        AnalysisInternalModel analysis;
        if (cachedEntry == null)
        {
            _logger.LogInformation("Analysis {AnalysisId} not in cache, reloading from Dataverse", analysisId);
            analysis = await _documentLoader.ReloadAnalysisFromDataverseAsync(analysisId, httpContext, cancellationToken);
            await _documentLoader.CacheAnalysisAsync(analysisId, analysis);
        }
        else
        {
            // Rebuild full model from Dataverse using cache hint for document text
            analysis = await _documentLoader.ReloadAnalysisFromDataverseAsync(analysisId, httpContext, cancellationToken);
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
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        await _resultPersistence.UpdateWorkingDocumentAsync(analysisId, response, cancellationToken);

        var chatHistoryJson = JsonSerializer.Serialize(chatHistory);
        await _resultPersistence.UpdateChatHistoryAsync(analysisId, chatHistoryJson, cancellationToken);

        _logger.LogInformation("Analysis continuation completed for {AnalysisId}", analysisId);

        yield return AnalysisStreamChunk.Completed(analysisId, new TokenUsage(inputTokens, outputTokens));
    }

    /// <inheritdoc />
    public async Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        AnalysisSaveRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving working document for analysis {AnalysisId}", analysisId);

        var analysis = await _documentLoader.GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

        if (string.IsNullOrWhiteSpace(analysis.WorkingDocument))
        {
            throw new InvalidOperationException("Analysis has no working document to save");
        }

        // Convert working document to requested format
        var (content, contentType) = ConvertToFormat(analysis.WorkingDocument, request.Format);

        // Save to SPE via result persistence
        return await _resultPersistence.SaveToSpeAsync(
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

        var analysis = await _documentLoader.GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

        // Get the export service for the requested format
        var exportService = _resultPersistence.GetExportService(request.Format);
        if (exportService == null)
        {
            _logger.LogWarning("Export format {Format} not supported", request.Format);
            stopwatch.Stop();
            _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "format_not_supported");
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
            CreatedBy = "User", // TRACKED: GitHub #233 - Extract from context when Dataverse integration complete
            Options = request.Options
        };

        // Validate
        var validation = exportService.Validate(context);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Export validation failed for {AnalysisId}: {Errors}",
                analysisId, string.Join(", ", validation.Errors));
            stopwatch.Stop();
            _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "validation_failed");
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
            _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, false, errorCode: "export_failed");
            return new ExportResult
            {
                ExportType = request.Format,
                Success = false,
                Error = result.Error
            };
        }

        // For file exports (DOCX, PDF), return bytes directly for client-side download.
        // The caller (ExportAnalysis endpoint) will stream these bytes as a file response.
        if (result.FileBytes != null && request.Format is ExportFormat.Docx or ExportFormat.Pdf)
        {
            stopwatch.Stop();
            _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true, fileSizeBytes: result.FileBytes.Length);

            return new ExportResult
            {
                ExportType = request.Format,
                Success = true,
                FileBytes = result.FileBytes,
                FileContentType = result.ContentType ?? "application/octet-stream",
                FileName = result.FileName ?? $"export_{analysisId:N}.{formatName}",
                Details = new ExportDetails
                {
                    Status = "Ready for download"
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
            _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true);

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
        _resultPersistence.RecordExport(formatName, stopwatch.Elapsed.TotalMilliseconds, true);

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
    public async Task<AnalysisDetailResult> GetAnalysisAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Retrieving analysis {AnalysisId}", analysisId);

        var analysis = await _documentLoader.GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

        return new AnalysisDetailResult
        {
            Id = analysis.Id,
            DocumentId = analysis.DocumentId,
            DocumentName = analysis.DocumentName,
            Action = new AnalysisActionInfo(analysis.ActionId ?? Guid.Empty, analysis.ActionName),
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
        };
    }

    /// <inheritdoc />
    public async Task<AnalysisResumeResult> ResumeAnalysisAsync(
        Guid analysisId,
        AnalysisResumeRequest request,
        HttpContext httpContext,
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
                chatHistory = _documentLoader.DeserializeChatHistory(request.ChatHistory, analysisId);
                chatMessagesRestored = chatHistory.Length;
            }

            // Extract document text for context in chat continuations
            string? documentText = null;
            string? systemPrompt = null;

            try
            {
                var document = await _documentLoader.GetDocumentAsync(
                    request.DocumentId.ToString(), cancellationToken);

                if (document != null)
                {
                    _logger.LogDebug("Extracting document text for resumed analysis {AnalysisId}", analysisId);
                    documentText = await _documentLoader.ExtractDocumentTextAsync(document, httpContext, cancellationToken);

                    // Build a default system prompt for continuations
                    systemPrompt = AnalysisDocumentLoader.BuildDefaultSystemPrompt();

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

            await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

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

    private static int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return (int)Math.Ceiling(text.Length / 4.0);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecutePlaybookAsync(
        PlaybookExecuteRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documentId = request.DocumentIds[0];

        _logger.LogInformation("Starting playbook execution: Playbook {PlaybookId}, Document {DocumentId}, AnalysisId {AnalysisId}",
            request.PlaybookId, documentId, request.AnalysisId);

        // 1. Load playbook from Dataverse
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 1: Loading playbook {PlaybookId}", request.PlaybookId);
        var playbook = await _playbookService.GetPlaybookAsync(request.PlaybookId, cancellationToken)
            ?? throw new KeyNotFoundException($"Playbook {request.PlaybookId} not found");

        _logger.LogInformation("[PLAYBOOK-EXEC] Step 1 OK: Loaded playbook '{PlaybookName}' with {ToolCount} tools, {SkillCount} skills, {KnowledgeCount} knowledge, {ActionCount} actions",
            playbook.Name, playbook.ToolIds?.Length ?? 0, playbook.SkillIds?.Length ?? 0,
            playbook.KnowledgeIds?.Length ?? 0, playbook.ActionIds?.Length ?? 0);

        // 2. Get document details from Dataverse
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 2: Loading document {DocumentId}", documentId);
        var document = await _documentLoader.GetDocumentAsync(documentId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found");
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 2 OK: Loaded document '{DocumentName}'", document.Name);

        // 3. Use existing analysis record ID from Dataverse (if provided) or generate a new one.
        var analysisId = request.AnalysisId ?? Guid.NewGuid();

        // Use first action from playbook if no override specified
        var actionId = request.ActionId ?? playbook.ActionIds?.FirstOrDefault() ?? Guid.Empty;

        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = documentId,
            DocumentName = document.Name ?? "Unknown",
            ActionId = actionId,
            Status = "InProgress",
            StartedOn = DateTime.UtcNow,
            ChatHistory = []
        };
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        // Emit metadata chunk
        yield return AnalysisStreamChunk.Metadata(analysisId, document.Name ?? "Unknown");

        // 3b. Just-in-time canvas-to-node sync.
        var canvasLayout = await _playbookService.GetCanvasLayoutAsync(request.PlaybookId, cancellationToken);
        if (canvasLayout?.Layout?.Nodes is { Length: > 0 })
        {
            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Step 3b: Syncing canvas to nodes — {NodeCount} canvas nodes, {EdgeCount} edges",
                canvasLayout.Layout.Nodes.Length, canvasLayout.Layout.Edges?.Length ?? 0);
            await _nodeService.SyncCanvasToNodesAsync(
                request.PlaybookId, canvasLayout.Layout, cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Step 3b: No canvas layout found for playbook {PlaybookId} — skipping node sync",
                request.PlaybookId);
        }

        // 3c. Node-based execution detection
        var nodes = await _nodeService.GetNodesAsync(request.PlaybookId, cancellationToken);
        if (nodes.Length > 0)
        {
            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Node-based mode: {NodeCount} nodes found for playbook {PlaybookId}, delegating to PlaybookOrchestrationService",
                nodes.Length, request.PlaybookId);
            yield return AnalysisStreamChunk.TextChunk(
                $"[Node-based execution: {nodes.Length} nodes detected]\n");

            // Load document text before delegating
            yield return AnalysisStreamChunk.TextChunk("[Extracting document text...]\n");
            var nodeDocText = await _documentLoader.ExtractDocumentTextAsync(document, httpContext, cancellationToken);
            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Document text extracted: {CharCount} characters", nodeDocText.Length);
            yield return AnalysisStreamChunk.TextChunk(
                $"[Document text: {nodeDocText.Length} chars extracted]\n");

            var documentContext = new DocumentContext
            {
                DocumentId = documentId,
                Name = document.Name ?? "Unknown",
                FileName = document.FileName,
                ExtractedText = nodeDocText,
                Metadata = new Dictionary<string, object?>
                {
                    ["PlaybookId"] = request.PlaybookId,
                    ["PlaybookName"] = playbook.Name,
                    ["GraphDriveId"] = document.GraphDriveId,
                    ["GraphItemId"] = document.GraphItemId
                }
            };

            // Resolve PlaybookOrchestrationService from request services to avoid circular DI.
            var playbookOrchestrator = httpContext.RequestServices
                .GetRequiredService<IPlaybookOrchestrationService>();

            var runRequest = new PlaybookRunRequest
            {
                PlaybookId = request.PlaybookId,
                DocumentIds = request.DocumentIds,
                UserContext = request.AdditionalContext,
                Document = documentContext
            };

            var totalTokensIn = 0;
            var totalTokensOut = 0;
            var allContent = new StringBuilder();
            string? deliverOutputContent = null;
            var hasFailedNodes = false;

            await foreach (var evt in playbookOrchestrator.ExecuteAsync(
                runRequest, httpContext, cancellationToken))
            {
                var chunk = BridgePlaybookEventToStreamChunk(evt, analysisId);
                if (chunk != null)
                {
                    yield return chunk;
                }

                // Accumulate streamed content for working document fallback
                if (evt.Type == PlaybookEventType.NodeProgress && evt.Content != null)
                {
                    allContent.Append(evt.Content);
                }

                // Capture Deliver Output node's rendered markdown from NodeCompleted events.
                // Use IsDeliverOutput flag — avoids capturing side-effect node messages
                // (e.g., "Updated sprk_document record" from UpdateRecord nodes) as content.
                if (evt.Type == PlaybookEventType.NodeCompleted &&
                    evt.NodeOutput is { IsDeliverOutput: true } deliverOutput &&
                    !string.IsNullOrEmpty(deliverOutput.TextContent))
                {
                    deliverOutputContent = deliverOutput.TextContent;
                }

                // Track node failures for partial data handling
                if (evt.Type == PlaybookEventType.NodeFailed)
                {
                    hasFailedNodes = true;
                }

                // Capture run-level metrics from completion event
                if (evt.Type == PlaybookEventType.RunCompleted && evt.Metrics != null)
                {
                    totalTokensIn = evt.Metrics.TotalTokensIn;
                    totalTokensOut = evt.Metrics.TotalTokensOut;
                }

                // On run-level failure, persist what we have and finalize with error status
                if (evt.Type == PlaybookEventType.RunFailed)
                {
                    var partialContent = allContent.ToString();
                    if (!string.IsNullOrEmpty(partialContent))
                    {
                        await _resultPersistence.UpdateWorkingDocumentAsync(
                            analysisId, partialContent, cancellationToken);
                    }

                    analysis = analysis with { Status = "Failed", CompletedOn = DateTime.UtcNow };
                    await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

                    yield return AnalysisStreamChunk.FromError(
                        evt.Error ?? "Playbook execution failed");
                    yield break;
                }
            }

            // Determine final content: prefer Deliver Output node's rendered result,
            // fall back to accumulated streamed content from all nodes.
            var finalContent = !string.IsNullOrEmpty(deliverOutputContent)
                ? deliverOutputContent
                : allContent.ToString();

            // Determine status
            var finalStatus = "Completed";
            bool? partialStorage = null;
            string? storageMessage = null;

            if (hasFailedNodes && !string.IsNullOrEmpty(finalContent))
            {
                finalStatus = "CompletedWithWarnings";
                partialStorage = true;
                storageMessage = "Some nodes failed during execution but partial results are available.";
                _logger.LogWarning(
                    "[PLAYBOOK-EXEC] Node-based execution completed with warnings: some nodes failed. AnalysisId={AnalysisId}",
                    analysisId);
            }
            else if (string.IsNullOrEmpty(finalContent))
            {
                finalStatus = "Failed";
                _logger.LogError(
                    "[PLAYBOOK-EXEC] Node-based execution produced no output. AnalysisId={AnalysisId}",
                    analysisId);
            }

            // Persist to working document (skip if no content to write)
            if (!string.IsNullOrEmpty(finalContent))
            {
                await _resultPersistence.UpdateWorkingDocumentAsync(
                    analysisId, finalContent, cancellationToken);
            }

            await _resultPersistence.FinalizeAnalysisAsync(
                analysisId, totalTokensIn, totalTokensOut, cancellationToken);

            // Update in-memory analysis store
            analysis = analysis with
            {
                WorkingDocument = finalContent,
                FinalOutput = finalContent,
                Status = finalStatus,
                CompletedOn = DateTime.UtcNow,
                InputTokens = totalTokensIn,
                OutputTokens = totalTokensOut
            };
            await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Node-based execution {Status}: Analysis {AnalysisId}, {NodeCount} nodes, output {OutputLength} chars",
                finalStatus, analysisId, nodes.Length, finalContent?.Length ?? 0);

            if (finalStatus == "Failed")
            {
                yield return AnalysisStreamChunk.FromError(
                    "Node-based execution produced no output");
            }
            else
            {
                yield return AnalysisStreamChunk.Completed(
                    analysisId,
                    new TokenUsage(totalTokensIn, totalTokensOut),
                    partialStorage: partialStorage,
                    storageMessage: storageMessage);
            }
            yield break;
        }

        // Legacy path: No nodes — execute tools sequentially using playbook-level scopes.
        // DEPRECATED: This path is for backward compatibility with playbooks that have not been
        // migrated to the Playbook Builder (node-based). New playbooks should always use nodes.
        _logger.LogWarning(
            "[PLAYBOOK-EXEC] DEPRECATED Legacy mode: No nodes found for playbook {PlaybookId}. " +
            "Falling back to sequential tool execution. Migrate this playbook to the Playbook Builder for node-based execution.",
            request.PlaybookId);

        // 4. Resolve playbook scopes (Skills, Knowledge, Tools)
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 4: Resolving playbook scopes");
        yield return AnalysisStreamChunk.TextChunk("[Resolving playbook scopes...]\n");
        var scopes = await _scopeResolver.ResolvePlaybookScopesAsync(request.PlaybookId, cancellationToken);

        _logger.LogInformation("[PLAYBOOK-EXEC] Step 4 OK: Resolved {SkillCount} skills, {KnowledgeCount} knowledge, {ToolCount} tools",
            scopes.Skills.Length, scopes.Knowledge.Length, scopes.Tools.Length);
        yield return AnalysisStreamChunk.TextChunk(
            $"[Scopes resolved: {scopes.Tools.Length} tools, {scopes.Skills.Length} skills, {scopes.Knowledge.Length} knowledge]\n");

        // 5. Get action definition
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 5: Loading action {ActionId}", actionId);
        var action = await _scopeResolver.GetActionAsync(actionId, cancellationToken)
            ?? new AnalysisAction
            {
                Id = actionId,
                Name = "Playbook Analysis",
                Description = $"Analysis using playbook: {playbook.Name}",
                SystemPrompt = AnalysisDocumentLoader.BuildDefaultSystemPrompt()
            };
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 5 OK: Action '{ActionName}'", action.Name);

        // 6. Extract document text from SPE
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 6: Extracting document text");
        yield return AnalysisStreamChunk.TextChunk("[Extracting document text...]\n");
        var documentText = await _documentLoader.ExtractDocumentTextAsync(document, httpContext, cancellationToken);

        _logger.LogInformation("[PLAYBOOK-EXEC] Step 6 OK: Extracted {CharCount} characters from document {DocumentId}",
            documentText.Length, documentId);
        yield return AnalysisStreamChunk.TextChunk(
            $"[Document text: {documentText.Length} chars extracted]\n");

        // 7. Process RAG knowledge sources
        var ragAnalysisContext = new AnalysisDocumentResult
        {
            Summary = documentText.Length > 500 ? documentText[..500] : documentText,
            Keywords = string.Empty,
            Entities = new AiExtractedEntities()
        };
        var processedKnowledge = await _ragProcessor.ProcessRagKnowledgeAsync(scopes.Knowledge, ragAnalysisContext, cancellationToken);

        // Get tenant ID from claims
        var tenantId = _ragProcessor.GetTenantIdFromClaims() ?? "default";

        // 8. Build execution context for tools
        var executionContext = new ToolExecutionContext
        {
            AnalysisId = analysisId,
            TenantId = tenantId,
            Document = new DocumentContext
            {
                DocumentId = documentId,
                Name = document.Name ?? "Unknown",
                FileName = document.FileName,
                ExtractedText = documentText,
                Metadata = new Dictionary<string, object?>
                {
                    ["PlaybookId"] = request.PlaybookId,
                    ["PlaybookName"] = playbook.Name
                }
            },
            UserContext = request.AdditionalContext
        };

        // Store document text and system prompt
        analysis = analysis with
        {
            DocumentText = documentText,
            SystemPrompt = action.SystemPrompt
        };
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        // 9. Execute tools from playbook
        var toolResults = new StringBuilder();
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var executedToolResults = new List<ToolResult>(); // Collect tool results for output extraction

        _logger.LogInformation("[PLAYBOOK-EXEC] Step 9: Executing {ToolCount} tools: [{ToolNames}]",
            scopes.Tools.Length,
            string.Join(", ", scopes.Tools.Select(t => $"{t.Name} ({t.HandlerClass ?? t.Type.ToString()})")));
        yield return AnalysisStreamChunk.TextChunk(
            $"[Executing {scopes.Tools.Length} tools: {string.Join(", ", scopes.Tools.Select(t => t.Name))}]\n");

        foreach (var tool in scopes.Tools)
        {
            // Stream progress update
            yield return AnalysisStreamChunk.TextChunk($"[Executing: {tool.Name}]\n");

            // Execute tool and collect result (can't yield inside try-catch)
            ToolResult? toolResult = null;
            string? errorMessage = null;

            // Get handler for this tool
            IAnalysisToolHandler? handler = null;

            if (!string.IsNullOrWhiteSpace(tool.HandlerClass))
            {
                handler = _toolHandlerRegistry.GetHandler(tool.HandlerClass);

                if (handler == null)
                {
                    var availableHandlers = _toolHandlerRegistry.GetRegisteredHandlerIds();
                    _logger.LogWarning(
                        "Custom handler '{HandlerClass}' not found for tool '{ToolName}'. " +
                        "Available handlers: [{AvailableHandlers}]. Falling back to GenericAnalysisHandler.",
                        tool.HandlerClass, tool.Name, string.Join(", ", availableHandlers));

                    handler = _toolHandlerRegistry.GetHandler("GenericAnalysisHandler");

                    yield return AnalysisStreamChunk.TextChunk(
                        $"[Tool {tool.Name}: Handler '{tool.HandlerClass}' not found, using generic handler]\n");
                }
            }
            else
            {
                var handlers = _toolHandlerRegistry.GetHandlersByType(tool.Type);
                handler = handlers.FirstOrDefault();
            }

            if (handler == null)
            {
                var availableHandlers = _toolHandlerRegistry.GetRegisteredHandlerIds();
                _logger.LogWarning(
                    "No handler found for tool '{ToolName}' (Type={ToolType}). " +
                    "Available handlers: [{AvailableHandlers}]. Skipping tool.",
                    tool.Name, tool.Type, string.Join(", ", availableHandlers));
                yield return AnalysisStreamChunk.TextChunk($"[Tool {tool.Name}: No handler available]\n");
                continue;
            }

            yield return AnalysisStreamChunk.TextChunk(
                $"[Tool {tool.Name}: Handler resolved → {handler.HandlerId}]\n");

            // Validate
            var toolValidation = handler.Validate(executionContext, tool);
            if (!toolValidation.IsValid)
            {
                _logger.LogWarning("Tool validation failed for {ToolName}: {Errors}",
                    tool.Name, string.Join(", ", toolValidation.Errors));
                yield return AnalysisStreamChunk.TextChunk(
                    $"[Tool {tool.Name}: Validation FAILED - {string.Join("; ", toolValidation.Errors)}]\n");
                continue;
            }

            // Execute with error handling — streaming-aware dispatch
            var streamed = false;

            if (handler is IStreamingAnalysisToolHandler streamingHandler)
            {
                yield return AnalysisStreamChunk.TextChunk($"### {tool.Name}\n");
                toolResults.AppendLine($"### {tool.Name}");

                await foreach (var evt in streamingHandler.StreamExecuteAsync(
                    executionContext, tool, cancellationToken))
                {
                    if (evt is ToolStreamEvent.Token t)
                    {
                        toolResults.Append(t.Text);
                        yield return AnalysisStreamChunk.TextChunk(t.Text);
                    }
                    else if (evt is ToolStreamEvent.Completed c)
                    {
                        toolResult = c.Result;
                    }
                }

                toolResults.AppendLine();
                toolResults.AppendLine();
                yield return AnalysisStreamChunk.TextChunk("\n\n");
                streamed = true;

                // Persist accumulated content
                await _resultPersistence.UpdateWorkingDocumentAsync(
                    analysisId, toolResults.ToString(), cancellationToken);
            }
            else
            {
                // Non-streaming fallback
                try
                {
                    toolResult = await handler.ExecuteAsync(executionContext, tool, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing tool {ToolName}", tool.Name);
                    errorMessage = ex.Message;
                }
            }

            // Process result (outside try-catch so we can yield)
            if (toolResult != null && toolResult.Success)
            {
                if (!streamed)
                {
                    var summary = toolResult.Summary ?? "No summary available";
                    toolResults.AppendLine($"### {tool.Name}");
                    toolResults.AppendLine(summary);
                    toolResults.AppendLine();

                    yield return AnalysisStreamChunk.TextChunk($"### {tool.Name}\n{summary}\n\n");
                }

                totalInputTokens += toolResult.Execution.InputTokens.GetValueOrDefault();
                totalOutputTokens += toolResult.Execution.OutputTokens.GetValueOrDefault();

                executedToolResults.Add(toolResult);
            }
            else if (toolResult != null)
            {
                _logger.LogWarning("Tool {ToolName} execution failed: {Error}",
                    tool.Name, toolResult.ErrorMessage);
                yield return AnalysisStreamChunk.TextChunk($"[Tool {tool.Name}: {toolResult.ErrorMessage}]\n");
            }
            else if (errorMessage != null)
            {
                yield return AnalysisStreamChunk.TextChunk($"[Tool {tool.Name}: Error - {errorMessage}]\n");
            }
        }

        // 10. Store outputs in Dataverse (dual storage for Document Profile)
        var structuredOutputs = new Dictionary<string, string?>();

        var outputKeyToTypeName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tldr"] = "TL;DR",
            ["summary"] = "Summary",
            ["keywords"] = "Keywords",
            ["documentType"] = "Document Type",
            ["entities"] = "Entities"
        };

        _logger.LogInformation(
            "Extracting structured outputs from {ToolCount} tool results for Document Profile storage",
            executedToolResults.Count);

        foreach (var toolResult in executedToolResults)
        {
            if (toolResult.Data is null)
            {
                _logger.LogDebug("Tool {ToolName} has no structured data", toolResult.ToolName);
                continue;
            }

            _logger.LogDebug(
                "Extracting outputs from tool {ToolName} with data: {Data}",
                toolResult.ToolName,
                toolResult.Data.Value.GetRawText());

            try
            {
                using var dataDoc = JsonDocument.Parse(toolResult.Data.Value.GetRawText());
                var root = dataDoc.RootElement;

                // JPS structured output
                if (root.TryGetProperty("tldr", out _) || root.TryGetProperty("summary", out _))
                {
                    foreach (var (outputKey, outputTypeName) in outputKeyToTypeName)
                    {
                        if (root.TryGetProperty(outputKey, out var outputValue))
                        {
                            string? outputText = outputValue.ValueKind switch
                            {
                                JsonValueKind.String => outputValue.GetString(),
                                JsonValueKind.Array or JsonValueKind.Object => JsonSerializer.Serialize(outputValue),
                                _ => outputValue.ToString()
                            };

                            if (!string.IsNullOrWhiteSpace(outputText))
                            {
                                structuredOutputs[outputTypeName] = outputText;
                                _logger.LogDebug(
                                    "Extracted JPS output: {OutputType} = {Length} characters",
                                    outputTypeName, outputText.Length);
                            }
                        }
                    }
                }
                // SummaryHandler -> TL;DR, Summary, Keywords outputs
                else if (toolResult.HandlerId.Equals("SummaryHandler", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("fullText", out var fullTextValue) ||
                        root.TryGetProperty("FullText", out fullTextValue))
                    {
                        var summaryText = fullTextValue.GetString();
                        if (!string.IsNullOrWhiteSpace(summaryText))
                        {
                            var tldr = ExtractTldr(summaryText);
                            if (!string.IsNullOrWhiteSpace(tldr))
                            {
                                structuredOutputs["TL;DR"] = tldr;
                                _logger.LogDebug("Extracted TL;DR output: {Length} characters", tldr.Length);
                            }

                            structuredOutputs["Summary"] = summaryText;
                            _logger.LogDebug("Extracted Summary output: {Length} characters", summaryText.Length);

                            if (root.TryGetProperty("sections", out var sectionsValue) ||
                                root.TryGetProperty("Sections", out sectionsValue))
                            {
                                var keywords = ExtractKeywordsFromSections(sectionsValue);
                                if (!string.IsNullOrWhiteSpace(keywords))
                                {
                                    structuredOutputs["Keywords"] = keywords;
                                    _logger.LogDebug("Extracted Keywords output: {Length} characters", keywords.Length);
                                }
                            }
                        }
                    }
                }
                // DocumentClassifierHandler -> Document Type output
                else if (toolResult.HandlerId.Equals("DocumentClassifierHandler", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("documentType", out var docTypeValue) ||
                        root.TryGetProperty("DocumentType", out docTypeValue) ||
                        root.TryGetProperty("classification", out docTypeValue))
                    {
                        var docType = docTypeValue.ValueKind == JsonValueKind.String
                            ? docTypeValue.GetString()
                            : docTypeValue.ToString();

                        if (!string.IsNullOrWhiteSpace(docType))
                        {
                            structuredOutputs["Document Type"] = docType;
                            _logger.LogDebug("Extracted Document Type output: {DocumentType}", docType);
                        }
                    }
                }
                // Generic fallback
                else
                {
                    foreach (var (outputKey, outputTypeName) in outputKeyToTypeName)
                    {
                        if (root.TryGetProperty(outputKey, out var outputValue))
                        {
                            string? outputText = null;

                            if (outputValue.ValueKind == JsonValueKind.String)
                            {
                                outputText = outputValue.GetString();
                            }
                            else if (outputValue.ValueKind == JsonValueKind.Array ||
                                     outputValue.ValueKind == JsonValueKind.Object)
                            {
                                outputText = JsonSerializer.Serialize(outputValue);
                            }
                            else
                            {
                                outputText = outputValue.ToString();
                            }

                            if (!string.IsNullOrWhiteSpace(outputText))
                            {
                                structuredOutputs[outputTypeName] = outputText;
                                _logger.LogDebug(
                                    "Extracted output: {OutputType} = {Length} characters",
                                    outputTypeName,
                                    outputText.Length);
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to parse tool data for {ToolName}. Skipping output extraction.",
                    toolResult.ToolName);
            }
        }

        _logger.LogInformation(
            "Extracted {OutputCount} structured outputs for Document Profile storage: {OutputTypes}",
            structuredOutputs.Count,
            string.Join(", ", structuredOutputs.Keys));

        // Store in Dataverse if we have outputs
        DocumentProfileResult? storageResult = null;
        if (structuredOutputs.Any())
        {
            storageResult = await _resultPersistence.StoreDocumentProfileOutputsAsync(
                analysisId,
                documentId,
                playbook.Name ?? "Unknown",
                structuredOutputs,
                cancellationToken);

            if (!storageResult.Success)
            {
                _logger.LogError(
                    "Failed to store Document Profile outputs: {Message}",
                    storageResult.Message);
            }
            else if (storageResult.PartialStorage)
            {
                _logger.LogWarning(
                    "Document Profile completed with partial storage: {Message}",
                    storageResult.Message);
            }
        }

        // 11. Finalize analysis
        var finalOutput = toolResults.ToString();
        analysis = analysis with
        {
            WorkingDocument = finalOutput,
            FinalOutput = finalOutput,
            Status = "Completed",
            CompletedOn = DateTime.UtcNow,
            InputTokens = totalInputTokens,
            OutputTokens = totalOutputTokens
        };
        await _documentLoader.CacheAnalysisAsync(analysisId, analysis);

        // Persist final content to Dataverse (sprk_workingdocument) — matches action-based path
        await _resultPersistence.UpdateWorkingDocumentAsync(analysisId, finalOutput, cancellationToken);
        await _resultPersistence.FinalizeAnalysisAsync(analysisId, totalInputTokens, totalOutputTokens, cancellationToken);

        _logger.LogInformation("Playbook execution completed: Analysis {AnalysisId}, {ToolCount} tools executed, output {OutputLength} chars",
            analysisId, scopes.Tools.Length, finalOutput.Length);
        yield return AnalysisStreamChunk.TextChunk(
            $"\n[Execution complete: {executedToolResults.Count}/{scopes.Tools.Length} tools succeeded, {finalOutput.Length} chars output]\n");

        // Enqueue RAG indexing job after playbook execution completes (ADR-001 / ADR-004).
        await _resultPersistence.EnqueueRagIndexingJobAsync(
            analysisId.ToString(),
            documentId.ToString(),
            tenantId,
            document.GraphDriveId,
            document.GraphItemId,
            cancellationToken);

        // Include storage result in completion event for Document Profile
        yield return AnalysisStreamChunk.Completed(
            analysisId,
            new TokenUsage(totalInputTokens, totalOutputTokens),
            partialStorage: storageResult?.PartialStorage,
            storageMessage: storageResult?.PartialStorage == true ? storageResult.Message : null);
    }

    /// <summary>
    /// Bridge a PlaybookStreamEvent from node-based orchestration to an AnalysisStreamChunk
    /// for the SSE channel. Maps node lifecycle events to text chunks and completion/error events.
    /// </summary>
    private static AnalysisStreamChunk? BridgePlaybookEventToStreamChunk(
        PlaybookStreamEvent evt, Guid analysisId)
    {
        return evt.Type switch
        {
            PlaybookEventType.RunStarted =>
                AnalysisStreamChunk.TextChunk(
                    $"[Playbook execution started: {evt.Metrics?.TotalNodes ?? 0} nodes]\n"),

            PlaybookEventType.NodeStarted =>
                AnalysisStreamChunk.TextChunk($"### {evt.NodeName ?? "Node"}\n"),

            PlaybookEventType.NodeProgress when evt.Content != null =>
                AnalysisStreamChunk.TextChunk(evt.Content),

            PlaybookEventType.NodeCompleted =>
                AnalysisStreamChunk.TextChunk(
                    $"\n[Node '{evt.NodeName ?? "Node"}' completed]\n\n"),

            PlaybookEventType.NodeSkipped =>
                AnalysisStreamChunk.TextChunk(
                    $"[Node '{evt.NodeName ?? "Node"}' skipped: {evt.Content}]\n"),

            PlaybookEventType.NodeFailed =>
                AnalysisStreamChunk.TextChunk(
                    $"\n[Node '{evt.NodeName ?? "Node"}' failed: {evt.Error}]\n"),

            // RunCompleted/RunFailed/RunCancelled are handled in the calling loop
            _ => null
        };
    }

    /// <summary>
    /// Extract TL;DR (ultra-concise summary) from full summary text.
    /// Takes the first 1-2 sentences or first paragraph.
    /// </summary>
    private static string ExtractTldr(string summaryText)
    {
        if (string.IsNullOrWhiteSpace(summaryText))
            return string.Empty;

        // Try to find an "Executive Summary" section
        var execSummaryMatch = System.Text.RegularExpressions.Regex.Match(
            summaryText,
            @"(?:##?\s*(?:Executive\s+)?Summary[\s:]*\n)(.*?)(?=\n##|\z)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        string textToExtract;
        if (execSummaryMatch.Success && execSummaryMatch.Groups[1].Value.Trim().Length > 0)
        {
            textToExtract = execSummaryMatch.Groups[1].Value.Trim();
        }
        else
        {
            // No executive summary section - use first paragraph
            var firstParagraph = summaryText.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();
            textToExtract = firstParagraph ?? summaryText;
        }

        // Extract first 1-2 sentences (up to 200 chars for brevity)
        var sentences = System.Text.RegularExpressions.Regex.Split(textToExtract, @"(?<=[.!?])\s+");
        var tldr = new StringBuilder();
        var charCount = 0;

        foreach (var sentence in sentences.Take(2))
        {
            if (charCount + sentence.Length > 200 && tldr.Length > 0)
                break;

            tldr.Append(sentence);
            if (!sentence.EndsWith(" "))
                tldr.Append(" ");
            charCount += sentence.Length;
        }

        return tldr.ToString().Trim();
    }

    /// <summary>
    /// Extract keywords from summary sections.
    /// Looks for "Key Terms" or similar sections and extracts terms.
    /// </summary>
    private static string ExtractKeywordsFromSections(JsonElement sectionsElement)
    {
        if (sectionsElement.ValueKind != JsonValueKind.Object)
            return string.Empty;

        // Try to find "Key Terms" section
        if (sectionsElement.TryGetProperty("Key Terms", out var keyTermsSection) ||
            sectionsElement.TryGetProperty("key_terms", out keyTermsSection))
        {
            var keyTermsText = keyTermsSection.GetString();
            if (!string.IsNullOrWhiteSpace(keyTermsText))
            {
                // Extract terms from bullet points
                var terms = System.Text.RegularExpressions.Regex.Matches(
                    keyTermsText,
                    @"^[\s-•*]+(.+?)(?::|$)",
                    System.Text.RegularExpressions.RegexOptions.Multiline);

                if (terms.Count > 0)
                {
                    var keywords = terms.Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Groups[1].Value.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Take(20); // Limit to 20 keywords

                    return string.Join(", ", keywords);
                }
            }
        }

        // Fallback: Extract first few words from notable sections
        var allSections = new List<string>();
        foreach (var property in sectionsElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var sectionText = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(sectionText) && sectionText.Length > 20)
                {
                    allSections.Add(sectionText);
                }
            }
        }

        if (allSections.Count > 0)
        {
            // Extract important-looking words (capitalized, longer than 4 chars)
            var combinedText = string.Join(" ", allSections);
            var words = System.Text.RegularExpressions.Regex.Matches(
                combinedText,
                @"\b[A-Z][a-z]{4,}\b");

            var keywords = words.Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Distinct()
                .Take(15);

            return string.Join(", ", keywords);
        }

        return string.Empty;
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
    public Guid? ActionId { get; set; }  // Nullable and mutable - resolved from playbook if not provided
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
