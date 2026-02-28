using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;

// Explicit aliases to resolve ambiguity with types in the same namespace (Sprk.Bff.Api.Services.Ai).
// AppOnlyAnalysisService defines DocumentAnalysisResult; AnalysisEndpoints defines ExtractedEntities.
using AnalysisDocumentResult = Sprk.Bff.Api.Models.Ai.DocumentAnalysisResult;
using AiExtractedEntities = Sprk.Bff.Api.Models.Ai.ExtractedEntities;

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
    private readonly IRagService _ragService;
    private readonly IPlaybookService _playbookService;
    private readonly IToolHandlerRegistry _toolHandlerRegistry;
    private readonly INodeService _nodeService;
    private readonly RagQueryBuilder _ragQueryBuilder;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IStorageRetryPolicy _storageRetryPolicy;
    private readonly AnalysisOptions _options;
    private readonly ILogger<AnalysisOrchestrationService> _logger;
    private readonly AiTelemetry? _telemetry;
    private readonly JobSubmissionService? _jobSubmissionService;

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
        IRagService ragService,
        RagQueryBuilder ragQueryBuilder,
        IPlaybookService playbookService,
        IToolHandlerRegistry toolHandlerRegistry,
        INodeService nodeService,
        IHttpContextAccessor httpContextAccessor,
        IStorageRetryPolicy storageRetryPolicy,
        IOptions<AnalysisOptions> options,
        ILogger<AnalysisOrchestrationService> logger,
        AiTelemetry? telemetry = null,
        JobSubmissionService? jobSubmissionService = null)
    {
        _dataverseService = dataverseService;
        _speFileStore = speFileStore;
        _textExtractor = textExtractor;
        _openAiClient = openAiClient;
        _scopeResolver = scopeResolver;
        _contextBuilder = contextBuilder;
        _workingDocumentService = workingDocumentService;
        _exportRegistry = exportRegistry;
        _ragService = ragService;
        _ragQueryBuilder = ragQueryBuilder;
        _playbookService = playbookService;
        _toolHandlerRegistry = toolHandlerRegistry;
        _nodeService = nodeService;
        _httpContextAccessor = httpContextAccessor;
        _storageRetryPolicy = storageRetryPolicy;
        _options = options.Value;
        _logger = logger;
        _telemetry = telemetry;
        _jobSubmissionService = jobSubmissionService;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAnalysisAsync(
        AnalysisExecuteRequest request,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // CRITICAL FIX: If PlaybookId is provided, delegate to ExecutePlaybookAsync
        // which executes each tool in the playbook (SummaryHandler, EntityExtractorHandler, etc.)
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
        var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Document {documentId} not found");

        // Log document details for debugging extraction issues
        _logger.LogInformation(
            "Document retrieved from Dataverse: Id={DocumentId}, Name={Name}, HasFile={HasFile}, " +
            "FileName={FileName}, GraphDriveId={GraphDriveId}, GraphItemId={GraphItemId}",
            document.Id, document.Name, document.HasFile,
            document.FileName, document.GraphDriveId ?? "(null)", document.GraphItemId ?? "(null)");

        // 2. Use existing analysis record ID from Dataverse (if provided) or generate a new one.
        // When the Code Page passes AnalysisId, we reuse it so WorkingDocumentService
        // persists content to the correct Dataverse record (sprk_workingdocument).
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
        _analysisStore[analysisId] = analysis;

        _logger.LogDebug("Created analysis record {AnalysisId} with session {SessionId}", analysisId, sessionId);

        // Emit metadata chunk
        yield return AnalysisStreamChunk.Metadata(analysisId, document.Name ?? "Unknown");

        // 3. Resolve scopes (Skills, Knowledge, Tools) and action
        // Note: Playbook-based analysis is handled via early delegation to ExecutePlaybookAsync above.
        // This code path is for action-based analysis only (no playbook).
        if (!request.ActionId.HasValue)
        {
            throw new ArgumentException("ActionId is required when not using a playbook");
        }

        var actionId = request.ActionId.Value;
        var scopes = await _scopeResolver.ResolveScopesAsync(
            request.SkillIds ?? [],
            request.KnowledgeIds ?? [],
            request.ToolIds ?? [],
            cancellationToken);

        // Update analysis record with resolved action ID
        analysis.ActionId = actionId;

        // 4. Get action definition
        var action = await _scopeResolver.GetActionAsync(actionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Action {actionId} not found");

        // 5. Extract document text from SPE via TextExtractor (uses OBO auth)
        var documentText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);

        // Log extraction result for debugging
        var textPreview = documentText.Length > 200 ? documentText[..200] + "..." : documentText;
        _logger.LogInformation(
            "Document text extracted: Length={TextLength}, Preview={Preview}",
            documentText.Length, textPreview);

        // Check for extraction failure indicators
        if (documentText.Contains("No file content available") ||
            documentText.Contains("not configured") ||
            documentText.Contains("not supported") ||
            documentText.Contains("Failed to download"))
        {
            _logger.LogWarning("Document extraction returned fallback message: {Message}", textPreview);
        }

        // 6. Process RAG knowledge sources (query Azure AI Search)
        // Wrap the raw document text in a minimal AnalysisDocumentResult for RagQueryBuilder.
        // The Summary field is used as the semantic anchor for vector search.
        // Entity/keyword enrichment occurs later in the pipeline (tool handlers); for
        // action-based analysis without a playbook, use document text as the summary.
        var ragAnalysisContext = new AnalysisDocumentResult
        {
            Summary = documentText.Length > 500 ? documentText[..500] : documentText,
            Keywords = string.Empty,
            Entities = new AiExtractedEntities()
        };
        var processedKnowledge = await ProcessRagKnowledgeAsync(
            scopes.Knowledge, ragAnalysisContext, cancellationToken);

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

        // Persist final content to Dataverse (sprk_workingdocument)
        await _workingDocumentService.UpdateWorkingDocumentAsync(analysisId, finalOutput, cancellationToken);
        await _workingDocumentService.FinalizeAnalysisAsync(analysisId, inputTokens, outputTokens, cancellationToken);

        _logger.LogInformation("Analysis {AnalysisId} completed: {InputTokens} input, {OutputTokens} output tokens",
            analysisId, inputTokens, outputTokens);

        // Enqueue RAG indexing job after analysis completes (ADR-001 / ADR-004).
        // The job runs as a background Service Bus task so it does not block the streaming response.
        // Idempotency key: "{tenantId}:{documentId}" — prevents duplicate indexing (ADR-004).
        var analysisTenantId = GetTenantIdFromClaims() ?? "unknown";
        await EnqueueRagIndexingJobAsync(
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

        // Get analysis from in-memory store, or reload from Dataverse if not found
        if (!_analysisStore.TryGetValue(analysisId, out var analysis))
        {
            _logger.LogInformation("Analysis {AnalysisId} not in memory, reloading from Dataverse", analysisId);
            analysis = await ReloadAnalysisFromDataverseAsync(analysisId, httpContext, cancellationToken);
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
    public async Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        AnalysisSaveRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Saving working document for analysis {AnalysisId}", analysisId);

        var analysis = await GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

        if (string.IsNullOrWhiteSpace(analysis.WorkingDocument))
        {
            throw new InvalidOperationException("Analysis has no working document to save");
        }

        // Convert working document to requested format
        var (content, contentType) = ConvertToFormat(analysis.WorkingDocument, request.Format);

        // Save to SPE via working document service
        return await _workingDocumentService.SaveToSpeAsync(
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

        var analysis = await GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

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
    public async Task<AnalysisDetailResult> GetAnalysisAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Retrieving analysis {AnalysisId}", analysisId);

        var analysis = await GetOrReloadFromDataverseAsync(analysisId, cancellationToken);

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
                    documentText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);

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
    /// Process knowledge sources, replacing RAG types with inline content from search results.
    /// This implements RAG (Retrieval-Augmented Generation) by querying Azure AI Search.
    /// Uses <see cref="RagQueryBuilder"/> to construct metadata-aware queries from the
    /// DocumentAnalysisResult rather than the naive first-500-characters approach.
    /// </summary>
    /// <param name="knowledge">Original knowledge sources from scope resolution.</param>
    /// <param name="analysisResult">
    /// Document analysis result containing entities, key phrases, and document type metadata.
    /// Used by RagQueryBuilder to build targeted search queries.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processed knowledge array with RAG sources replaced by inline content.</returns>
    private async Task<AnalysisKnowledge[]> ProcessRagKnowledgeAsync(
        AnalysisKnowledge[] knowledge,
        AnalysisDocumentResult analysisResult,
        CancellationToken cancellationToken)
    {
        if (knowledge.Length == 0)
        {
            return knowledge;
        }

        var ragSources = knowledge.Where(k => k.Type == KnowledgeType.RagIndex).ToArray();
        if (ragSources.Length == 0)
        {
            _logger.LogDebug("No RAG knowledge sources to process");
            return knowledge;
        }

        // Get tenant ID from HttpContext claims
        var tenantId = GetTenantIdFromClaims();
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("Cannot process RAG knowledge: TenantId not found in claims");
            // Return knowledge as-is; the context builder will just skip RAG sources
            return knowledge;
        }

        _logger.LogInformation("Processing {RagCount} RAG knowledge sources for tenant {TenantId}",
            ragSources.Length, tenantId);

        // Build metadata-aware query from analysis result using RagQueryBuilder.
        // This replaces the naive first-500-characters approach with a structured query
        // derived from entities, key phrases, document type, and summary.
        var ragQuery = _ragQueryBuilder.BuildQuery(analysisResult, tenantId);

        _logger.LogDebug(
            "Built RAG query: searchText length={SearchTextLength}, filter={Filter}",
            ragQuery.SearchText.Length, ragQuery.FilterExpression);

        var processedKnowledge = new List<AnalysisKnowledge>();

        foreach (var source in knowledge)
        {
            if (source.Type != KnowledgeType.RagIndex)
            {
                // Keep non-RAG sources as-is
                processedKnowledge.Add(source);
                continue;
            }

            try
            {
                // Search RAG index using the structured RagQuery.
                // KnowledgeSourceId filtering is applied via the existing RagSearchOptions path
                // (the RagQuery overload handles tenant + doc type; source scoping uses options).
                var searchOptions = new RagSearchOptions
                {
                    TenantId = tenantId,
                    DeploymentId = source.DeploymentId,
                    KnowledgeSourceId = source.Id.ToString(),
                    TopK = _options.MaxKnowledgeResults,
                    MinScore = _options.MinRelevanceScore,
                    UseSemanticRanking = true,
                    UseVectorSearch = true,
                    UseKeywordSearch = true
                };

                _logger.LogDebug("Searching RAG index for knowledge source {SourceId}: {SourceName}",
                    source.Id, source.Name);

                var searchResult = await _ragService.SearchAsync(ragQuery.SearchText, searchOptions, cancellationToken);

                if (searchResult.Results.Count == 0)
                {
                    _logger.LogDebug("No RAG results found for knowledge source {SourceId}", source.Id);
                    continue; // Skip this source if no results
                }

                // Convert RAG results to inline knowledge content
                var ragContent = new StringBuilder();
                ragContent.AppendLine($"Retrieved from knowledge base: {source.Name}");
                ragContent.AppendLine();

                foreach (var result in searchResult.Results)
                {
                    ragContent.AppendLine($"### {result.DocumentName} (Relevance: {result.Score:P0})");
                    ragContent.AppendLine(result.Content);
                    ragContent.AppendLine();
                }

                // Replace RAG source with inline source containing search results
                var inlineSource = source with
                {
                    Type = KnowledgeType.Inline,
                    Content = ragContent.ToString()
                };

                processedKnowledge.Add(inlineSource);

                _logger.LogInformation(
                    "RAG search for {SourceName} returned {ResultCount} results in {Duration}ms",
                    source.Name, searchResult.Results.Count, searchResult.SearchDurationMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search RAG index for knowledge source {SourceId}: {SourceName}",
                    source.Id, source.Name);
                // Continue without this knowledge source rather than failing the analysis
            }
        }

        _logger.LogDebug("Processed {OriginalCount} knowledge sources into {ProcessedCount} sources",
            knowledge.Length, processedKnowledge.Count);

        return processedKnowledge.ToArray();
    }

    /// <summary>
    /// Gets the tenant ID from the current HTTP context claims.
    /// </summary>
    private string? GetTenantIdFromClaims()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return null;
        }

        // Try common claim types for tenant ID
        return user.FindFirstValue("tid") ??
               user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid") ??
               user.FindFirstValue("tenant_id");
    }

    /// <summary>
    /// Extract text from a document stored in SharePoint Embedded.
    /// Downloads the file using OBO authentication and uses TextExtractor to extract readable text.
    /// </summary>
    private async Task<string> ExtractDocumentTextAsync(
        DocumentEntity document,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Check if document has SPE file reference
        // Presence of valid Graph IDs is sufficient - HasFile flag may not always be set
        var hasValidGraphIds = !string.IsNullOrEmpty(document.GraphDriveId) && !string.IsNullOrEmpty(document.GraphItemId);
        if (!hasValidGraphIds)
        {
            _logger.LogWarning(
                "Document {DocumentId} has no SPE file reference (HasFile={HasFile}, DriveId={DriveId}, ItemId={ItemId})",
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
            _logger.LogInformation(
                "Downloading document {DocumentId} from SPE (Drive={DriveId}, Item={ItemId}, FileName={FileName})",
                document.Id, document.GraphDriveId, document.GraphItemId, fileName);

            // Download file from SharePoint Embedded using OBO authentication
            // This ensures the user's token is used for file access (fixes "Access denied" errors)
            // Graph IDs validated as non-empty above, safe to use null-forgiving operator
            using var fileStream = await _speFileStore.DownloadFileAsUserAsync(
                httpContext,
                document.GraphDriveId!,
                document.GraphItemId!,
                cancellationToken);

            if (fileStream == null)
            {
                _logger.LogWarning("Failed to download document {DocumentId} from SPE - stream is null", document.Id);
                return $"[Document: {document.Name}]\n\nFailed to download file from storage.";
            }

            // Check if stream has content
            var streamLength = fileStream.CanSeek ? fileStream.Length : -1;
            _logger.LogInformation(
                "Downloaded document {DocumentId}, stream length: {StreamLength} bytes, extracting text from {FileName}",
                document.Id, streamLength, fileName);

            // Extract text using TextExtractor
            var extractionResult = await _textExtractor.ExtractAsync(fileStream, fileName, cancellationToken);

            _logger.LogInformation(
                "Extraction result for {DocumentId}: Success={Success}, Method={Method}, TextLength={TextLength}, Error={Error}",
                document.Id, extractionResult.Success, extractionResult.Method,
                extractionResult.Text?.Length ?? 0, extractionResult.ErrorMessage ?? "none");

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
    /// Get an analysis from the in-memory store, or reload it from Dataverse if not found.
    /// This is the "lite" version that does NOT extract document text (no HttpContext needed).
    /// Suitable for GET, save, and export operations that only need the persisted analysis data.
    /// For chat continuation (which needs document text), use ReloadAnalysisFromDataverseAsync instead.
    /// </summary>
    private async Task<AnalysisInternalModel> GetOrReloadFromDataverseAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        if (_analysisStore.TryGetValue(analysisId, out var existing))
        {
            return existing;
        }

        _logger.LogInformation("Analysis {AnalysisId} not in memory, loading from Dataverse (lite)", analysisId);

        // 1. Get analysis record from Dataverse
        var record = await _dataverseService.GetAnalysisAsync(analysisId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Analysis {analysisId} not found in Dataverse");

        // 2. Parse chat history
        var chatHistory = Array.Empty<ChatMessageModel>();
        if (!string.IsNullOrWhiteSpace(record.ChatHistory))
        {
            try
            {
                var messages = System.Text.Json.JsonSerializer.Deserialize<ChatMessageModel[]>(
                    record.ChatHistory,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (messages != null)
                {
                    chatHistory = messages;
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize chat history for analysis {AnalysisId}", analysisId);
            }
        }

        // 3. Build internal model (no document text — only needed for chat continuation)
        var analysis = new AnalysisInternalModel
        {
            Id = analysisId,
            DocumentId = record.DocumentId,
            DocumentName = record.Name ?? "Analysis",
            ActionId = Guid.Empty,
            Status = "Completed",
            WorkingDocument = record.WorkingDocument,
            ChatHistory = chatHistory,
            StartedOn = record.CreatedOn,
            CompletedOn = record.ModifiedOn,
        };

        _analysisStore[analysisId] = analysis;

        _logger.LogInformation(
            "Loaded analysis {AnalysisId} from Dataverse (lite): {DocChars} chars, {ChatCount} messages",
            analysisId, record.WorkingDocument?.Length ?? 0, chatHistory.Length);

        return analysis;
    }

    /// <summary>
    /// Reload analysis context from Dataverse when not found in memory.
    /// Extracts document text so chat continuations have context.
    /// </summary>
    private async Task<AnalysisInternalModel> ReloadAnalysisFromDataverseAsync(
        Guid analysisId,
        HttpContext httpContext,
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
                    documentText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);
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

    /// <summary>
    /// Store Document Profile outputs in Dataverse with dual storage and soft failure handling.
    /// Stores outputs in both sprk_analysisoutput (always) and sprk_document fields (with retry).
    /// </summary>
    /// <param name="analysisId">The analysis ID.</param>
    /// <param name="documentId">The document ID.</param>
    /// <param name="playbookName">The playbook name.</param>
    /// <param name="toolResults">The tool execution results.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Storage result indicating success, partial success, or failure.</returns>
    private async Task<DocumentProfileResult> StoreDocumentProfileOutputsAsync(
        Guid analysisId,
        Guid documentId,
        string playbookName,
        Dictionary<string, string?> toolResults,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Use existing analysis record if analysisId was provided (user-initiated via Code Page),
            // otherwise create a new one (background worker / app-only path).
            // This prevents duplicate records when the user triggers analysis from an existing Draft record.
            Guid dataverseAnalysisId;

            if (analysisId != Guid.Empty)
            {
                _logger.LogInformation(
                    "Using existing analysis record for Document Profile: AnalysisId={AnalysisId}, DocumentId={DocumentId}",
                    analysisId, documentId);
                dataverseAnalysisId = analysisId;
            }
            else
            {
                _logger.LogInformation(
                    "Creating new analysis record for Document Profile: DocumentId={DocumentId}",
                    documentId);
                dataverseAnalysisId = await _dataverseService.CreateAnalysisAsync(
                    documentId,
                    $"Document Profile - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    playbookId: null,
                    cancellationToken);
            }

            // Step 2: Store outputs in sprk_analysisoutput (critical path)
            _logger.LogInformation(
                "Storing {OutputCount} outputs in sprk_analysisoutput for analysis {AnalysisId}",
                toolResults.Count, dataverseAnalysisId);

            var sortOrder = 0;
            foreach (var (outputTypeName, value) in toolResults)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    _logger.LogDebug("Skipping empty output for type {OutputType}", outputTypeName);
                    continue;
                }

                var output = new AnalysisOutputEntity
                {
                    Name = outputTypeName,
                    Value = value,
                    AnalysisId = dataverseAnalysisId,
                    OutputTypeId = null, // Output type lookup optional for Phase 1
                    SortOrder = sortOrder++
                };

                await _dataverseService.CreateAnalysisOutputAsync(output, cancellationToken);
                _logger.LogDebug("Stored output {OutputType} in sprk_analysisoutput", outputTypeName);
            }

            // Step 3: Map outputs to sprk_document fields (optional path, with retry)
            // Only for Document Profile playbook
            if (playbookName.Equals("Document Profile", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _logger.LogInformation(
                        "Mapping Document Profile outputs to sprk_document fields for document {DocumentId}",
                        documentId);

                    // Create field mapping using DocumentProfileFieldMapper
                    var fieldMapping = DocumentProfileFieldMapper.CreateFieldMapping(toolResults);

                    if (fieldMapping.Count == 0)
                    {
                        _logger.LogWarning(
                            "No mappable outputs found for Document Profile. Skipping document field update.");
                        return DocumentProfileResult.FullSuccess(dataverseAnalysisId);
                    }

                    // Update document fields with retry policy
                    await _storageRetryPolicy.ExecuteAsync(async ct =>
                    {
                        await _dataverseService.UpdateDocumentFieldsAsync(
                            documentId.ToString(),
                            fieldMapping,
                            ct);

                        _logger.LogInformation(
                            "Successfully mapped {FieldCount} outputs to sprk_document fields",
                            fieldMapping.Count);

                    }, cancellationToken);

                    return DocumentProfileResult.FullSuccess(dataverseAnalysisId);
                }
                catch (Exception ex)
                {
                    // Soft failure: Outputs are preserved in sprk_analysisoutput
                    // User can still access results via Analysis Workspace
                    _logger.LogWarning(ex,
                        "[STORAGE-SOFT-FAIL] Failed to map outputs to sprk_document fields after retries. " +
                        "Outputs preserved in sprk_analysisoutput for analysis {AnalysisId}",
                        dataverseAnalysisId);

                    return DocumentProfileResult.PartialSuccess(
                        dataverseAnalysisId,
                        "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.");
                }
            }

            // Non-Document-Profile playbooks: full success (only sprk_analysisoutput storage required)
            return DocumentProfileResult.FullSuccess(dataverseAnalysisId);
        }
        catch (Exception ex)
        {
            // Complete failure: Could not store outputs at all
            _logger.LogError(ex,
                "Failed to store Document Profile outputs for analysis {AnalysisId}",
                analysisId);

            return DocumentProfileResult.Failure(
                $"Failed to store analysis outputs: {ex.Message}",
                analysisId);
        }
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
        var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), cancellationToken)
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
        _analysisStore[analysisId] = analysis;

        // Emit metadata chunk
        yield return AnalysisStreamChunk.Metadata(analysisId, document.Name ?? "Unknown");

        // 3b. Node-based execution detection
        // If the playbook has sprk_playbooknode records, delegate to PlaybookOrchestrationService
        // which executes nodes in topological order with per-node scope resolution.
        var nodes = await _nodeService.GetNodesAsync(request.PlaybookId, cancellationToken);
        if (nodes.Length > 0)
        {
            _logger.LogInformation(
                "[PLAYBOOK-EXEC] Node-based mode: {NodeCount} nodes found for playbook {PlaybookId}, delegating to PlaybookOrchestrationService",
                nodes.Length, request.PlaybookId);
            yield return AnalysisStreamChunk.TextChunk(
                $"[Node-based execution: {nodes.Length} nodes detected]\n");

            // Load document text before delegating — nodes need extracted text for AI analysis.
            // Uses the existing SpeFileStore facade (ADR-007) and ITextExtractor pattern.
            yield return AnalysisStreamChunk.TextChunk("[Extracting document text...]\n");
            var nodeDocText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);
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
                    ["PlaybookName"] = playbook.Name
                }
            };

            // Resolve PlaybookOrchestrationService from request services to avoid circular DI.
            // PlaybookOrchestrationService depends on IAnalysisOrchestrationService (for legacy fallback),
            // so we cannot inject it via constructor without creating a circular dependency.
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
                // The Deliver Output node aggregates all previous results into a final document.
                if (evt.Type == PlaybookEventType.NodeCompleted && evt.NodeOutput != null)
                {
                    // Identify Deliver Output by output variable name or node name convention
                    var outputVar = evt.NodeOutput.OutputVariable;
                    if (outputVar != null &&
                        (outputVar.Contains("output", StringComparison.OrdinalIgnoreCase) ||
                         outputVar.Contains("deliver", StringComparison.OrdinalIgnoreCase)))
                    {
                        deliverOutputContent = evt.NodeOutput.TextContent;
                    }
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
                        await _workingDocumentService.UpdateWorkingDocumentAsync(
                            analysisId, partialContent, cancellationToken);
                    }

                    analysis = analysis with { Status = "Failed", CompletedOn = DateTime.UtcNow };
                    _analysisStore[analysisId] = analysis;

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

            // Determine status: "Completed with warnings" if some nodes failed but we have output
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
                await _workingDocumentService.UpdateWorkingDocumentAsync(
                    analysisId, finalContent, cancellationToken);
            }

            await _workingDocumentService.FinalizeAnalysisAsync(
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
            _analysisStore[analysisId] = analysis;

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

        // Legacy path: No nodes — execute tools sequentially using playbook-level scopes
        _logger.LogInformation(
            "[PLAYBOOK-EXEC] Legacy mode: No nodes found for playbook {PlaybookId}, using sequential tool execution",
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
                SystemPrompt = BuildDefaultSystemPrompt()
            };
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 5 OK: Action '{ActionName}'", action.Name);

        // 6. Extract document text from SPE
        _logger.LogInformation("[PLAYBOOK-EXEC] Step 6: Extracting document text");
        yield return AnalysisStreamChunk.TextChunk("[Extracting document text...]\n");
        var documentText = await ExtractDocumentTextAsync(document, httpContext, cancellationToken);

        _logger.LogInformation("[PLAYBOOK-EXEC] Step 6 OK: Extracted {CharCount} characters from document {DocumentId}",
            documentText.Length, documentId);
        yield return AnalysisStreamChunk.TextChunk(
            $"[Document text: {documentText.Length} chars extracted]\n");

        // 7. Process RAG knowledge sources
        // Build a minimal AnalysisDocumentResult for RagQueryBuilder.
        // In playbook execution, full entity extraction runs as a tool step (EntityExtractorHandler).
        // Since that tool hasn't run yet at this stage, we use document text as the summary.
        // Future enhancement: run entity extraction before RAG if available in scope.
        var ragAnalysisContext = new AnalysisDocumentResult
        {
            Summary = documentText.Length > 500 ? documentText[..500] : documentText,
            Keywords = string.Empty,
            Entities = new AiExtractedEntities()
        };
        var processedKnowledge = await ProcessRagKnowledgeAsync(scopes.Knowledge, ragAnalysisContext, cancellationToken);

        // Get tenant ID from claims
        var tenantId = GetTenantIdFromClaims() ?? "default";

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
        _analysisStore[analysisId] = analysis;

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
            // Priority: 1) HandlerClass (if specified), 2) Type-based lookup, 3) GenericAnalysisHandler fallback
            IAnalysisToolHandler? handler = null;

            if (!string.IsNullOrWhiteSpace(tool.HandlerClass))
            {
                // Try to get specific handler by class name
                handler = _toolHandlerRegistry.GetHandler(tool.HandlerClass);

                if (handler == null)
                {
                    // Handler not found - log available handlers and fall back to generic
                    var availableHandlers = _toolHandlerRegistry.GetRegisteredHandlerIds();
                    _logger.LogWarning(
                        "Custom handler '{HandlerClass}' not found for tool '{ToolName}'. " +
                        "Available handlers: [{AvailableHandlers}]. Falling back to GenericAnalysisHandler.",
                        tool.HandlerClass, tool.Name, string.Join(", ", availableHandlers));

                    // Fall back to GenericAnalysisHandler
                    handler = _toolHandlerRegistry.GetHandler("GenericAnalysisHandler");

                    yield return AnalysisStreamChunk.TextChunk(
                        $"[Tool {tool.Name}: Handler '{tool.HandlerClass}' not found, using generic handler]\n");
                }
            }
            else
            {
                // No HandlerClass specified - use type-based lookup
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
            var validation = handler.Validate(executionContext, tool);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Tool validation failed for {ToolName}: {Errors}",
                    tool.Name, string.Join(", ", validation.Errors));
                yield return AnalysisStreamChunk.TextChunk(
                    $"[Tool {tool.Name}: Validation FAILED - {string.Join("; ", validation.Errors)}]\n");
                continue;
            }

            // Execute with error handling — streaming-aware dispatch
            // Mirrors the per-token pattern from ExecuteAnalysisAsync (action-based path)
            var streamed = false;

            if (handler is IStreamingAnalysisToolHandler streamingHandler)
            {
                // Per-token streaming path: emit heading + tokens as they arrive.
                // No try-catch here because C# forbids yield inside try-catch.
                // StreamExecuteAsync handles errors internally (yields Completed with error result).
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

                // Persist accumulated content (same pattern as action-based path)
                await _workingDocumentService.UpdateWorkingDocumentAsync(
                    analysisId, toolResults.ToString(), cancellationToken);
            }
            else
            {
                // Non-streaming fallback — current blocking behavior
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
                    // Non-streaming: emit full result as single chunk (existing behavior)
                    var summary = toolResult.Summary ?? "No summary available";
                    toolResults.AppendLine($"### {tool.Name}");
                    toolResults.AppendLine(summary);
                    toolResults.AppendLine();

                    yield return AnalysisStreamChunk.TextChunk($"### {tool.Name}\n{summary}\n\n");
                }

                totalInputTokens += toolResult.Execution.InputTokens.GetValueOrDefault();
                totalOutputTokens += toolResult.Execution.OutputTokens.GetValueOrDefault();

                // Collect successful tool results for output extraction
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
        // Extract structured outputs from tool results for Document Profile storage
        var structuredOutputs = new Dictionary<string, string?>();

        // Map output keys from tool results to output type names expected by DocumentProfileFieldMapper
        // These keys match the playbook outputMapping configuration
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

        // Extract outputs from collected tool results
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

            // Parse the Data JSON and extract outputs based on tool handler type
            try
            {
                using var dataDoc = JsonDocument.Parse(toolResult.Data.Value.GetRawText());
                var root = dataDoc.RootElement;

                // Map tool result structures to output type names based on handler ID
                // EntityExtractorHandler → Entities output
                if (toolResult.HandlerId.Equals("EntityExtractorHandler", StringComparison.OrdinalIgnoreCase))
                {
                    // EntityExtractionResult has "entities" array property
                    if (root.TryGetProperty("entities", out var entitiesValue) ||
                        root.TryGetProperty("Entities", out entitiesValue))
                    {
                        // Serialize entities array as JSON for storage
                        var entitiesJson = JsonSerializer.Serialize(entitiesValue);
                        structuredOutputs["Entities"] = entitiesJson;

                        _logger.LogDebug(
                            "Extracted Entities output from EntityExtractorHandler: {Length} characters",
                            entitiesJson.Length);
                    }
                }
                // SummaryHandler → TL;DR, Summary, Keywords outputs
                else if (toolResult.HandlerId.Equals("SummaryHandler", StringComparison.OrdinalIgnoreCase))
                {
                    // SummaryResult has "fullText" property containing the complete summary
                    if (root.TryGetProperty("fullText", out var fullTextValue) ||
                        root.TryGetProperty("FullText", out fullTextValue))
                    {
                        var summaryText = fullTextValue.GetString();
                        if (!string.IsNullOrWhiteSpace(summaryText))
                        {
                            // Extract TL;DR (first paragraph or first 1-2 sentences)
                            var tldr = ExtractTldr(summaryText);
                            if (!string.IsNullOrWhiteSpace(tldr))
                            {
                                structuredOutputs["TL;DR"] = tldr;
                                _logger.LogDebug("Extracted TL;DR output: {Length} characters", tldr.Length);
                            }

                            // Use full summary text for Summary output
                            structuredOutputs["Summary"] = summaryText;
                            _logger.LogDebug("Extracted Summary output: {Length} characters", summaryText.Length);

                            // Extract keywords from sections if available
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
                // DocumentClassifierHandler → Document Type output
                else if (toolResult.HandlerId.Equals("DocumentClassifierHandler", StringComparison.OrdinalIgnoreCase))
                {
                    // Look for "documentType" or "classification" property
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
                // Generic fallback: Try to find output keys directly in the data
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
            storageResult = await StoreDocumentProfileOutputsAsync(
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
        _analysisStore[analysisId] = analysis;

        // Persist final content to Dataverse (sprk_workingdocument) — matches action-based path
        await _workingDocumentService.UpdateWorkingDocumentAsync(analysisId, finalOutput, cancellationToken);
        await _workingDocumentService.FinalizeAnalysisAsync(analysisId, totalInputTokens, totalOutputTokens, cancellationToken);

        _logger.LogInformation("Playbook execution completed: Analysis {AnalysisId}, {ToolCount} tools executed, output {OutputLength} chars",
            analysisId, scopes.Tools.Length, finalOutput.Length);
        yield return AnalysisStreamChunk.TextChunk(
            $"\n[Execution complete: {executedToolResults.Count}/{scopes.Tools.Length} tools succeeded, {finalOutput.Length} chars output]\n");

        // Enqueue RAG indexing job after playbook execution completes (ADR-001 / ADR-004).
        // Idempotency key: "{tenantId}:{documentId}" — prevents duplicate indexing (ADR-004).
        await EnqueueRagIndexingJobAsync(
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

    /// <summary>
    /// Enqueues a <see cref="RagIndexingJobPayload"/> to the Service Bus queue so the document
    /// is indexed into Azure AI Search in the background after analysis completes.
    ///
    /// Implements ADR-001 (BackgroundService pattern) and ADR-004 (idempotent job contract).
    /// Idempotency key: "{tenantId}:{documentId}" — guarantees the same document is not indexed twice.
    ///
    /// The method is a soft-failure path: if <see cref="_jobSubmissionService"/> is not registered
    /// (e.g. in test or minimal startup) or enqueueing throws, the analysis result is still returned
    /// to the caller — indexing will be caught by the scheduled backfill (ScheduledRagIndexingService).
    /// </summary>
    private async Task EnqueueRagIndexingJobAsync(
        string analysisId,
        string documentId,
        string tenantId,
        string? driveId,
        string? itemId,
        CancellationToken cancellationToken)
    {
        if (_jobSubmissionService is null)
        {
            _logger.LogDebug(
                "JobSubmissionService not available — skipping RAG indexing job enqueue for analysis {AnalysisId}",
                analysisId);
            return;
        }

        // Only enqueue if we have the SPE file reference needed to download the document.
        if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
        {
            _logger.LogWarning(
                "Cannot enqueue RAG indexing job for analysis {AnalysisId}: missing DriveId or ItemId",
                analysisId);
            return;
        }

        try
        {
            // Idempotency key per ADR-004: "{tenantId}:{documentId}"
            var idempotencyKey = $"{tenantId}:{documentId}";

            var payload = new RagIndexingJobPayload
            {
                TenantId = tenantId,
                DriveId = driveId,
                ItemId = itemId,
                DocumentId = documentId,
                FileName = string.Empty, // Resolved by handler from Dataverse if needed
                Source = "AnalysisOrchestration",
                EnqueuedAt = DateTimeOffset.UtcNow
            };

            var job = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = documentId,
                CorrelationId = analysisId,
                IdempotencyKey = idempotencyKey,
                Attempt = 1,
                MaxAttempts = 3,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(payload)),
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _jobSubmissionService.SubmitJobAsync(job, cancellationToken);

            _logger.LogInformation(
                "Enqueued RAG indexing job {JobId} for document {DocumentId}, analysis {AnalysisId}, " +
                "idempotency key {IdempotencyKey}",
                job.JobId, documentId, analysisId, idempotencyKey);
        }
        catch (Exception ex)
        {
            // Soft failure — analysis result is already returned to the caller.
            // Scheduled backfill (ScheduledRagIndexingService) will catch up unindexed documents.
            _logger.LogWarning(ex,
                "Failed to enqueue RAG indexing job for analysis {AnalysisId}, document {DocumentId}. " +
                "Indexing will be retried by scheduled backfill.",
                analysisId, documentId);
        }
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
