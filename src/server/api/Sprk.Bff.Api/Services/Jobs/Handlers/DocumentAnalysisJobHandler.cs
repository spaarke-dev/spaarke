using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for ai-analyze background jobs.
/// Processes document analysis requests that were enqueued when client disconnects or for batch processing.
/// Follows ADR-004 for job contract patterns.
/// </summary>
public class DocumentAnalysisJobHandler : IJobHandler
{
    private readonly IDocumentIntelligenceService _documentIntelligenceService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DocumentAnalysisJobHandler> _logger;

    public DocumentAnalysisJobHandler(
        IDocumentIntelligenceService documentIntelligenceService,
        IIdempotencyService idempotencyService,
        IDataverseService dataverseService,
        ILogger<DocumentAnalysisJobHandler> logger)
    {
        _documentIntelligenceService = documentIntelligenceService;
        _idempotencyService = idempotencyService;
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public string JobType => "ai-analyze";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing ai-analyze job {JobId} for document {SubjectId}, Attempt {Attempt}",
                job.JobId, job.SubjectId, job.Attempt);

            // Check idempotency - prevent duplicate processing
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"analyze:{job.SubjectId}:{job.JobId}";
            }

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Job {JobId} already processed (idempotency key: {IdempotencyKey})",
                    job.JobId, idempotencyKey);

                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(5), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for job {JobId} (idempotency key: {IdempotencyKey})",
                    job.JobId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Parse job payload
                var payload = ParsePayload(job.Payload);
                if (payload == null)
                {
                    throw new ArgumentException("Invalid payload for ai-analyze job");
                }

                // Call DocumentIntelligenceService (non-streaming for background processing)
                var request = payload.ToRequest();
                var result = await _documentIntelligenceService.AnalyzeAsync(request, ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Analysis completed for document {DocumentId}, SummaryLength={Length}",
                        payload.DocumentId, result.Summary?.Length ?? 0);

                    // Update Dataverse with the analysis result
                    await UpdateDocumentAnalysisAsync(payload.DocumentId, result, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "Analysis failed for document {DocumentId}: {Error}",
                        payload.DocumentId, result.ErrorMessage);

                    // Update Dataverse with failure status
                    await UpdateDocumentAnalysisStatusAsync(payload.DocumentId, AnalysisStatus.Failed, ct);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                stopwatch.Stop();

                _logger.LogInformation(
                    "Job {JobId} completed in {Duration}ms",
                    job.JobId, stopwatch.ElapsedMilliseconds);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            finally
            {
                // Always release the lock
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, ct);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "ai-analyze job {JobId} failed", job.JobId);
            throw; // Let the job processor handle retry logic
        }
    }

    private DocumentAnalysisJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<DocumentAnalysisJobPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ai-analyze job payload");
            return null;
        }
    }

    private async Task UpdateDocumentAnalysisAsync(
        Guid documentId,
        AnalysisResult result,
        CancellationToken ct)
    {
        try
        {
            var updateRequest = new UpdateDocumentRequest
            {
                Summary = result.Summary,
                SummaryStatus = (int)AnalysisStatus.Completed
            };

            // If structured result is available, populate all entity fields
            if (result.StructuredResult != null)
            {
                var structured = result.StructuredResult;

                // TL;DR as newline-separated string
                if (structured.TlDr.Length > 0)
                {
                    updateRequest.TlDr = string.Join("\n", structured.TlDr);
                }

                // Keywords (already comma-separated from AI)
                if (!string.IsNullOrWhiteSpace(structured.Keywords))
                {
                    updateRequest.Keywords = structured.Keywords;
                }

                // Extracted entities - convert arrays to newline-separated strings
                var entities = structured.Entities;

                if (entities.Organizations.Length > 0)
                {
                    updateRequest.ExtractOrganization = string.Join("\n", entities.Organizations);
                }

                if (entities.People.Length > 0)
                {
                    updateRequest.ExtractPeople = string.Join("\n", entities.People);
                }

                if (entities.Amounts.Length > 0)
                {
                    updateRequest.ExtractFees = string.Join("\n", entities.Amounts);
                }

                if (entities.Dates.Length > 0)
                {
                    updateRequest.ExtractDates = string.Join("\n", entities.Dates);
                }

                if (entities.References.Length > 0)
                {
                    updateRequest.ExtractReference = string.Join("\n", entities.References);
                }

                // Document type - both raw text and mapped choice value
                if (!string.IsNullOrWhiteSpace(entities.DocumentType))
                {
                    updateRequest.ExtractDocumentType = entities.DocumentType;
                    updateRequest.DocumentType = DocumentTypeMapper.ToDataverseValue(entities.DocumentType);
                }

                _logger.LogDebug(
                    "Saving structured analysis for document {DocumentId}: Orgs={Orgs}, People={People}, DocType={DocType}",
                    documentId,
                    entities.Organizations.Length,
                    entities.People.Length,
                    entities.DocumentType);
            }

            await _dataverseService.UpdateDocumentAsync(documentId.ToString(), updateRequest, ct);

            _logger.LogDebug(
                "Updated Dataverse analysis for document {DocumentId}",
                documentId);
        }
        catch (Exception ex)
        {
            // Log but don't fail the job - the analysis was generated successfully
            _logger.LogWarning(ex,
                "Failed to update Dataverse analysis for document {DocumentId}",
                documentId);
        }
    }

    private async Task UpdateDocumentAnalysisStatusAsync(
        Guid documentId,
        AnalysisStatus status,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug(
                "Updating analysis status for document {DocumentId} to {Status}",
                documentId, status);

            await _dataverseService.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest { SummaryStatus = (int)status },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update analysis status for document {DocumentId}",
                documentId);
        }
    }
}

/// <summary>
/// Analysis status values matching Dataverse sprk_filesummarystatus choice.
/// </summary>
public enum AnalysisStatus
{
    None = 0,
    Pending = 1,
    Completed = 2,
    OptedOut = 3,
    Failed = 4
}
