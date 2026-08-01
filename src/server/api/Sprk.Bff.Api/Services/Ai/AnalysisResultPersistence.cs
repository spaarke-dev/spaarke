using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Handles output storage, RAG indexing enqueue, working document finalization,
/// export execution, and export telemetry for the analysis pipeline.
/// Extracted from AnalysisOrchestrationService to reduce constructor dependency count (ADR-010).
/// </summary>
public class AnalysisResultPersistence
{
    private readonly IAnalysisDataverseService _analysisService;
    private readonly IDocumentDataverseService _documentService;
    private readonly IWorkingDocumentService _workingDocumentService;
    private readonly IStorageRetryPolicy _storageRetryPolicy;
    private readonly ExportServiceRegistry _exportRegistry;
    private readonly AiTelemetry? _telemetry;
    private readonly JobSubmissionService? _jobSubmissionService;
    private readonly IPostUploadIndexingEnqueuer? _postUploadIndexingEnqueuer;
    private readonly ILogger<AnalysisResultPersistence> _logger;

    public AnalysisResultPersistence(
        IAnalysisDataverseService analysisService,
        IDocumentDataverseService documentService,
        IWorkingDocumentService workingDocumentService,
        IStorageRetryPolicy storageRetryPolicy,
        ExportServiceRegistry exportRegistry,
        ILogger<AnalysisResultPersistence> logger,
        AiTelemetry? telemetry = null,
        JobSubmissionService? jobSubmissionService = null,
        IPostUploadIndexingEnqueuer? postUploadIndexingEnqueuer = null)
    {
        _analysisService = analysisService;
        _documentService = documentService;
        _workingDocumentService = workingDocumentService;
        _storageRetryPolicy = storageRetryPolicy;
        _exportRegistry = exportRegistry;
        _logger = logger;
        _telemetry = telemetry;
        _jobSubmissionService = jobSubmissionService;
        _postUploadIndexingEnqueuer = postUploadIndexingEnqueuer;
    }

    /// <summary>
    /// Get the export service for the requested format.
    /// </summary>
    public IExportService? GetExportService(ExportFormat format)
    {
        return _exportRegistry.GetService(format);
    }

    /// <summary>
    /// Record an export operation for telemetry tracking.
    /// </summary>
    public void RecordExport(string format, double elapsedMs, bool success,
        string? errorCode = null, long? fileSizeBytes = null)
    {
        _telemetry?.RecordExport(format, elapsedMs, success, errorCode: errorCode, fileSizeBytes: fileSizeBytes);
    }

    /// <summary>
    /// Update the working document content in Dataverse.
    /// </summary>
    public Task UpdateWorkingDocumentAsync(Guid analysisId, string content, CancellationToken cancellationToken)
    {
        return _workingDocumentService.UpdateWorkingDocumentAsync(analysisId, content, cancellationToken);
    }

    /// <summary>
    /// Finalize analysis record in Dataverse with token counts.
    /// </summary>
    public Task FinalizeAnalysisAsync(Guid analysisId, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        return _workingDocumentService.FinalizeAnalysisAsync(analysisId, inputTokens, outputTokens, cancellationToken);
    }

    /// <summary>
    /// Save working document to SPE via working document service.
    /// </summary>
    public Task<SavedDocumentResult> SaveToSpeAsync(
        Guid analysisId,
        string fileName,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        return _workingDocumentService.SaveToSpeAsync(analysisId, fileName, content, contentType, cancellationToken);
    }

    /// <summary>
    /// Store Document Profile outputs in Dataverse with dual storage and soft failure handling.
    /// Stores outputs in both sprk_analysisoutput (always) and sprk_document fields (with retry).
    /// </summary>
    public async Task<DocumentProfileResult> StoreDocumentProfileOutputsAsync(
        Guid analysisId,
        Guid documentId,
        string playbookName,
        Dictionary<string, string?> toolResults,
        CancellationToken cancellationToken)
    {
        try
        {
            // Step 1: Use existing analysis record if analysisId was provided, otherwise create a new one.
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
                dataverseAnalysisId = await _analysisService.CreateAnalysisAsync(
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
                    OutputTypeId = null,
                    SortOrder = sortOrder++
                };

                await _analysisService.CreateAnalysisOutputAsync(output, cancellationToken);
                _logger.LogDebug("Stored output {OutputType} in sprk_analysisoutput", outputTypeName);
            }

            // Step 3: Map outputs to sprk_document fields (optional path, with retry)
            if (playbookName.Equals("Document Profile", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _logger.LogInformation(
                        "Mapping Document Profile outputs to sprk_document fields for document {DocumentId}",
                        documentId);

                    var fieldMapping = DocumentProfileFieldMapper.CreateFieldMapping(toolResults);

                    if (fieldMapping.Count == 0)
                    {
                        _logger.LogWarning(
                            "No mappable outputs found for Document Profile. Skipping document field update.");
                        return DocumentProfileResult.FullSuccess(dataverseAnalysisId);
                    }

                    await _storageRetryPolicy.ExecuteAsync(async ct =>
                    {
                        await _documentService.UpdateDocumentFieldsAsync(
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
                    _logger.LogWarning(ex,
                        "[STORAGE-SOFT-FAIL] Failed to map outputs to sprk_document fields after retries. " +
                        "Outputs preserved in sprk_analysisoutput for analysis {AnalysisId}",
                        dataverseAnalysisId);

                    return DocumentProfileResult.PartialSuccess(
                        dataverseAnalysisId,
                        "Document Profile completed. Some fields could not be updated. View full results in the Analysis tab.");
                }
            }

            return DocumentProfileResult.FullSuccess(dataverseAnalysisId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to store Document Profile outputs for analysis {AnalysisId}",
                analysisId);

            return DocumentProfileResult.Failure(
                $"Failed to store analysis outputs: {ex.Message}",
                analysisId);
        }
    }

    /// <summary>
    /// Enqueues a RAG indexing job to the Service Bus queue so the document
    /// is indexed into Azure AI Search in the background after analysis completes.
    /// Implements ADR-001 (BackgroundService pattern) and ADR-004 (idempotent job contract).
    /// </summary>
    public async Task EnqueueRagIndexingJobAsync(
        string analysisId,
        string documentId,
        string tenantId,
        string? driveId,
        string? itemId,
        CancellationToken cancellationToken)
    {
        if (_postUploadIndexingEnqueuer is null)
        {
            _logger.LogDebug(
                "IPostUploadIndexingEnqueuer not available -- skipping RAG indexing job enqueue for analysis {AnalysisId}",
                analysisId);
            return;
        }

        if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
        {
            _logger.LogWarning(
                "Cannot enqueue RAG indexing job for analysis {AnalysisId}: missing DriveId or ItemId",
                analysisId);
            return;
        }

        // Phase 2 refactor (upload-indexing centralization): delegate to centralized helper.
        // Original idempotency key was tenant:document scoped; helper's standard is
        // rag-index-{driveId}-{itemId} which is the wider, drive+item canonical form.
        // Same non-fatal try/catch + WARN logging is enforced inside the helper.
        var request = new PostUploadIndexingRequest(
            TenantId: tenantId,
            DriveId: driveId,
            ItemId: itemId,
            FileName: string.Empty,
            FileSizeBytes: null,
            ContentType: null,
            DocumentId: documentId,
            ParentEntity: null,
            SearchIndexName: null, // handler runs ISearchIndexNameResolver chain
            Source: "AnalysisOrchestration",
            CorrelationId: analysisId);

        // App-only path: re-indexing after AI analysis. Originally an MI-uploaded file path
        // (writer-identity rule per sdap-auth-patterns.md Pattern 4).
        await _postUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync(request, cancellationToken);
    }

    /// <summary>
    /// FR-13 (ai-advanced-capabilities-agreements-r1 task 050) — persists the assembled Review Summary
    /// Memo to <c>sprk_analysisoutput</c> via the SAME <see cref="IAnalysisDataverseService.CreateAnalysisOutputAsync"/>
    /// KEEP-list path <see cref="StoreDocumentProfileOutputsAsync"/> already uses. No new entity: the
    /// memo's structured header (<see cref="ReviewMemoDocument.SchemaVersion"/>/<see cref="ReviewMemoDocument.OverallRisk"/>/
    /// <see cref="ReviewMemoDocument.SectionCount"/>) travels alongside the section array in ONE JSON
    /// body (<c>sprk_value</c>) — everything a later reader needs, self-contained per ADR-015 (the memo
    /// is the ONLY review artifact that survives <c>DELETE /sessions</c>; it carries no Cosmos/ledger
    /// back-reference of any kind).
    /// </summary>
    /// <remarks>
    /// <b>Schema note</b>: <c>sprk_analysisoutput.sprk_value</c> (Multiline Text) was ADDED by this task
    /// (2026-07-31, via Dataverse Web API + <c>PublishXml</c>) — the column did not previously exist on
    /// the live table even though this class's <see cref="AnalysisOutputEntity.Value"/> →
    /// <c>sprk_value</c> write path predates this task. Both existing callers
    /// (<see cref="StoreDocumentProfileOutputsAsync"/> here and <c>AppOnlyAnalysisService</c>) wrap the
    /// create in a best-effort try/catch that SWALLOWED the resulting Dataverse fault as a warning, so
    /// the gap was latent rather than a visible failure. Adding the column is a pre-existing-bug fix
    /// that benefits those callers too, not scope introduced by the memo feature. See task 050 execution
    /// notes §Audit for the full trace (CLAUDE.md §6.5 Path C — completing an already-coded contract,
    /// not an ADR conflict).
    /// </remarks>
    /// <param name="analysisId">The <c>sprk_analysis</c> GUID to persist the memo under (sentinel-aware
    /// resolution happens in the caller — <c>ReviewMemoEndpoints.GenerateReviewMemo</c>).</param>
    /// <param name="memo">The assembled, self-contained memo (see <see cref="ReviewMemoAssembler"/>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new <c>sprk_analysisoutputid</c>.</returns>
    public async Task<Guid> PersistReviewMemoAsync(
        Guid analysisId,
        ReviewMemoDocument memo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memo);

        var json = JsonSerializer.Serialize(memo);

        var output = new AnalysisOutputEntity
        {
            Name = ReviewMemoOutputName,
            Value = json,
            AnalysisId = analysisId,
            // OutputTypeId intentionally left null: a "Review Summary Memo" sprk_aioutputtype row
            // was seeded (task 050 notes), but its GUID is environment-specific data, not portable
            // C# — hardcoding it here would 400 in any org that doesn't happen to share the same
            // GUID. Categorization by sprk_name (below) is env-portable; a future task can wire the
            // lookup by sprk_outputtypecode ("REVMEMO") once IAnalysisDataverseService exposes a
            // by-code resolver.
            OutputTypeId = null,
        };

        var outputId = await _analysisService.CreateAnalysisOutputAsync(output, cancellationToken);

        _logger.LogInformation(
            "Persisted Review Summary Memo output {OutputId} for analysis {AnalysisId} ({SectionCount} sections, overallRisk={OverallRisk})",
            outputId, analysisId, memo.SectionCount, memo.OverallRisk);

        return outputId;
    }

    /// <summary>Display name for the persisted memo row — the categorization signal (see <see cref="PersistReviewMemoAsync"/> remarks on <c>OutputTypeId</c>).</summary>
    private const string ReviewMemoOutputName = "Review Summary Memo";

    /// <summary>
    /// FR-14 (ai-advanced-capabilities-agreements-r1 task 051) — the Review Summary Memo READ path.
    /// Reads the MOST RECENT persisted memo for <paramref name="analysisId"/> (via the new
    /// <see cref="IAnalysisDataverseService.GetLatestAnalysisOutputByNameAsync"/> — the smallest read
    /// extension of 050's endpoint family, per project CLAUDE.md §10) plus the analysis/document display
    /// names, so BOTH the "Generate memo" (.docx) and "Email memo" toolbar actions render from the SAME
    /// persisted record (render-from-persisted, the binding project constraint — exports ≡ the durable
    /// artifact). Returns <c>null</c> when no memo has been generated yet (a malformed/undeserializable
    /// persisted value is also treated as absent, logged as a warning — never surfaced as a 500).
    /// </summary>
    public async Task<ReviewMemoReadResult?> GetReviewMemoWithMetadataAsync(
        Guid analysisId,
        CancellationToken cancellationToken)
    {
        var output = await _analysisService.GetLatestAnalysisOutputByNameAsync(analysisId, ReviewMemoOutputName, cancellationToken);
        if (string.IsNullOrEmpty(output?.Value))
        {
            return null;
        }

        ReviewMemoDocument? memo;
        try
        {
            memo = JsonSerializer.Deserialize<ReviewMemoDocument>(output.Value);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to deserialize the persisted Review Summary Memo for analysis {AnalysisId} — treating as not-yet-generated.",
                analysisId);
            return null;
        }

        if (memo is null)
        {
            return null;
        }

        var analysis = await _analysisService.GetAnalysisAsync(analysisId.ToString(), cancellationToken);
        string? documentName = null;
        if (analysis is not null && analysis.DocumentId != Guid.Empty)
        {
            var document = await _documentService.GetDocumentAsync(analysis.DocumentId.ToString(), cancellationToken);
            documentName = document?.Name;
        }

        return new ReviewMemoReadResult(memo, analysis?.Name, documentName);
    }
}
