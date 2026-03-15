using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Ai.Export;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Handles output storage, RAG indexing enqueue, working document finalization,
/// export execution, and export telemetry for the analysis pipeline.
/// Extracted from AnalysisOrchestrationService to reduce constructor dependency count (ADR-010).
/// </summary>
public class AnalysisResultPersistence
{
    private readonly IDataverseService _dataverseService;
    private readonly IWorkingDocumentService _workingDocumentService;
    private readonly IStorageRetryPolicy _storageRetryPolicy;
    private readonly ExportServiceRegistry _exportRegistry;
    private readonly AiTelemetry? _telemetry;
    private readonly JobSubmissionService? _jobSubmissionService;
    private readonly ILogger<AnalysisResultPersistence> _logger;

    public AnalysisResultPersistence(
        IDataverseService dataverseService,
        IWorkingDocumentService workingDocumentService,
        IStorageRetryPolicy storageRetryPolicy,
        ExportServiceRegistry exportRegistry,
        ILogger<AnalysisResultPersistence> logger,
        AiTelemetry? telemetry = null,
        JobSubmissionService? jobSubmissionService = null)
    {
        _dataverseService = dataverseService;
        _workingDocumentService = workingDocumentService;
        _storageRetryPolicy = storageRetryPolicy;
        _exportRegistry = exportRegistry;
        _logger = logger;
        _telemetry = telemetry;
        _jobSubmissionService = jobSubmissionService;
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
                    OutputTypeId = null,
                    SortOrder = sortOrder++
                };

                await _dataverseService.CreateAnalysisOutputAsync(output, cancellationToken);
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
        if (_jobSubmissionService is null)
        {
            _logger.LogDebug(
                "JobSubmissionService not available -- skipping RAG indexing job enqueue for analysis {AnalysisId}",
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

        try
        {
            var idempotencyKey = $"{tenantId}:{documentId}";

            var payload = new RagIndexingJobPayload
            {
                TenantId = tenantId,
                DriveId = driveId,
                ItemId = itemId,
                DocumentId = documentId,
                FileName = string.Empty,
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
            _logger.LogWarning(ex,
                "Failed to enqueue RAG indexing job for analysis {AnalysisId}, document {DocumentId}. " +
                "Indexing will be retried by scheduled backfill.",
                analysisId, documentId);
        }
    }
}
