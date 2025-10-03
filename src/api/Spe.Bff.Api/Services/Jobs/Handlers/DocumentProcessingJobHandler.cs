using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Sample job handler for document processing operations.
/// Demonstrates the job handling pattern following ADR-004.
/// </summary>
public class DocumentProcessingJobHandler : IJobHandler
{
    private readonly ILogger<DocumentProcessingJobHandler> _logger;
    private readonly SpeFileStore _speFileStore;

    public DocumentProcessingJobHandler(
        ILogger<DocumentProcessingJobHandler> logger,
        SpeFileStore speFileStore)
    {
        _logger = logger;
        _speFileStore = speFileStore;
    }

    public string JobType => "DocumentProcessing";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Processing document job {JobId} for subject {SubjectId}",
                job.JobId, job.SubjectId);

            // Parse job payload
            var payload = ParsePayload(job.Payload);
            if (payload == null)
            {
                throw new ArgumentException("Invalid payload for document processing job");
            }

            // Perform idempotent document processing
            await ProcessDocumentAsync(payload, ct);

            stopwatch.Stop();

            _logger.LogInformation("Document processing completed for job {JobId} in {Duration}ms",
                job.JobId, stopwatch.ElapsedMilliseconds);

            return JobOutcome.Success(job.JobId, job.JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Document processing failed for job {JobId}", job.JobId);
            throw;
        }
    }

    private DocumentProcessingPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<DocumentProcessingPayload>(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse document processing payload");
            return null;
        }
    }

    private async Task ProcessDocumentAsync(DocumentProcessingPayload payload, CancellationToken ct)
    {
        // Idempotent document processing logic
        // This is a placeholder - real implementation would:
        // 1. Check if processing already completed using IdempotencyKey
        // 2. Perform the actual document operation via SpeFileStore
        // 3. Record completion status

        await Task.Delay(100, ct); // Simulate processing time

        _logger.LogInformation("Processed document {DocumentId} with operation {Operation}",
            payload.DocumentId, payload.Operation);
    }
}

/// <summary>
/// Payload structure for document processing jobs.
/// </summary>
public class DocumentProcessingPayload
{
    public string DocumentId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}
