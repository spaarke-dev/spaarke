using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for batch processing historical emails.
/// Queries emails from Dataverse matching criteria, processes them with bounded concurrency,
/// and updates job status as progress is made.
///
/// Follows ADR-001 (BackgroundService pattern) and ADR-004 (async job contract).
/// </summary>
public class BatchProcessEmailsJobHandler : IJobHandler
{
    private readonly EmailToDocumentJobHandler _emailProcessor;
    private readonly BatchJobStatusStore _statusStore;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly EmailProcessingOptions _options;
    private readonly EmailTelemetry _telemetry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BatchProcessEmailsJobHandler> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _apiUrl;

    private AccessToken? _currentToken;

    /// <summary>
    /// Job type constant - matches the JobType used by the batch-process endpoint.
    /// </summary>
    public const string JobTypeName = "BatchProcessEmails";

    // Default batch processing settings
    private const int DefaultBatchSize = 50;
    private const int DefaultMaxConcurrency = 5;
    private const int ProgressUpdateInterval = 10; // Update status every N emails

    public BatchProcessEmailsJobHandler(
        EmailToDocumentJobHandler emailProcessor,
        BatchJobStatusStore statusStore,
        JobSubmissionService jobSubmissionService,
        IIdempotencyService idempotencyService,
        IOptions<EmailProcessingOptions> options,
        EmailTelemetry telemetry,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<BatchProcessEmailsJobHandler> logger)
    {
        _emailProcessor = emailProcessor ?? throw new ArgumentNullException(nameof(emailProcessor));
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = httpClientFactory.CreateClient("DataverseBatch");

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["Dataverse:ClientSecret"]
            ?? throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = _telemetry.RecordJobStart();
        var jobIdStr = job.JobId.ToString();

        try
        {
            _logger.LogInformation(
                "Starting batch email processing job {JobId}, CorrelationId {CorrelationId}",
                job.JobId, job.CorrelationId);

            // Parse batch job payload
            var payload = ParsePayload(job.Payload);
            if (payload == null)
            {
                _logger.LogError("Invalid payload for batch process job {JobId}", job.JobId);
                await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Failed, ct);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            await EnsureAuthenticatedAsync(ct);

            // Query matching emails from Dataverse
            _logger.LogInformation(
                "Querying emails for batch job {JobId}: {StartDate} to {EndDate}, MaxEmails={MaxEmails}",
                job.JobId, payload.StartDate, payload.EndDate, payload.MaxEmails);

            var emailIds = await QueryEmailsAsync(payload, ct);

            if (emailIds.Count == 0)
            {
                _logger.LogInformation("No emails found matching batch criteria for job {JobId}", job.JobId);
                await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Completed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "Found {Count} emails to process for batch job {JobId}",
                emailIds.Count, job.JobId);

            // Mark job as started with actual email count
            await _statusStore.MarkJobStartedAsync(jobIdStr, emailIds.Count, ct);

            // Process emails with bounded concurrency
            var result = await ProcessEmailsWithConcurrencyAsync(
                job, payload, emailIds, ct);

            // Determine final status
            var finalState = DetermineFinalState(result);
            await _statusStore.MarkJobCompletedAsync(jobIdStr, finalState, ct);

            _logger.LogInformation(
                "Batch job {JobId} completed: {Processed} processed, {Errors} errors, {Skipped} skipped. Final state: {State}",
                job.JobId, result.ProcessedCount, result.ErrorCount, result.SkippedCount, finalState);

            _telemetry.RecordBatchJobCompleted(
                result.ProcessedCount,
                result.ErrorCount,
                result.SkippedCount,
                stopwatch.Elapsed);

            return finalState == BatchJobState.Failed
                ? JobOutcome.Failure(job.JobId, JobType, $"Batch failed with {result.ErrorCount} errors", job.Attempt, stopwatch.Elapsed)
                : JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch process job {JobId} failed: {Error}", job.JobId, ex.Message);
            await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Failed, ct);
            return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private async Task<BatchProcessResult> ProcessEmailsWithConcurrencyAsync(
        JobContract batchJob,
        BatchProcessPayload payload,
        List<Guid> emailIds,
        CancellationToken ct)
    {
        var jobIdStr = batchJob.JobId.ToString();
        var maxConcurrency = _options.BatchMaxConcurrency > 0
            ? _options.BatchMaxConcurrency
            : DefaultMaxConcurrency;

        var result = new BatchProcessResult();
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var processingTasks = new List<Task>();
        var processedLock = new object();

        _logger.LogDebug(
            "Processing {Count} emails with max concurrency {MaxConcurrency}",
            emailIds.Count, maxConcurrency);

        foreach (var emailId in emailIds)
        {
            ct.ThrowIfCancellationRequested();

            await semaphore.WaitAsync(ct);

            var task = Task.Run(async () =>
            {
                try
                {
                    var outcome = await ProcessSingleEmailAsync(
                        batchJob, emailId, payload, ct);

                    lock (processedLock)
                    {
                        switch (outcome)
                        {
                            case EmailProcessOutcome.Success:
                                result.ProcessedCount++;
                                break;
                            case EmailProcessOutcome.Skipped:
                                result.SkippedCount++;
                                break;
                            case EmailProcessOutcome.Failed:
                                result.ErrorCount++;
                                break;
                        }

                        // Update status periodically
                        var totalHandled = result.ProcessedCount + result.ErrorCount + result.SkippedCount;
                        if (totalHandled % ProgressUpdateInterval == 0 || totalHandled == emailIds.Count)
                        {
                            _ = _statusStore.UpdateProgressAsync(
                                jobIdStr,
                                result.ProcessedCount,
                                result.ErrorCount,
                                result.SkippedCount,
                                ct);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct);

            processingTasks.Add(task);
        }

        // Wait for all tasks to complete
        await Task.WhenAll(processingTasks);

        // Final status update
        await _statusStore.UpdateProgressAsync(
            jobIdStr,
            result.ProcessedCount,
            result.ErrorCount,
            result.SkippedCount,
            ct);

        return result;
    }

    private async Task<EmailProcessOutcome> ProcessSingleEmailAsync(
        JobContract batchJob,
        Guid emailId,
        BatchProcessPayload payload,
        CancellationToken ct)
    {
        var jobIdStr = batchJob.JobId.ToString();
        var idempotencyKey = $"Email:{emailId}:Archive";

        try
        {
            // Check if already processed (skip duplicates)
            if (payload.SkipAlreadyConverted &&
                await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogDebug("Skipping already processed email {EmailId}", emailId);
                return EmailProcessOutcome.Skipped;
            }

            // Create a job contract for single email processing
            var emailJob = new JobContract
            {
                JobType = EmailToDocumentJobHandler.JobTypeName,
                SubjectId = emailId.ToString(),
                CorrelationId = batchJob.CorrelationId,
                IdempotencyKey = idempotencyKey,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    EmailId = emailId,
                    TriggerSource = "BatchProcess",
                    BatchJobId = batchJob.JobId
                })),
                MaxAttempts = 1, // No retries within batch
                Attempt = 0
            };

            // Process the email using the existing handler
            var outcome = await _emailProcessor.ProcessAsync(emailJob, ct);

            if (outcome.Status == JobStatus.Completed)
            {
                return EmailProcessOutcome.Success;
            }
            else
            {
                // Record error
                await _statusStore.RecordErrorAsync(
                    jobIdStr,
                    emailId.ToString(),
                    outcome.ErrorMessage ?? "Processing failed",
                    ct);
                return EmailProcessOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process email {EmailId} in batch", emailId);
            await _statusStore.RecordErrorAsync(
                jobIdStr,
                emailId.ToString(),
                ex.Message,
                ct);
            return EmailProcessOutcome.Failed;
        }
    }

    private async Task<List<Guid>> QueryEmailsAsync(
        BatchProcessPayload payload,
        CancellationToken ct)
    {
        var emailIds = new List<Guid>();

        // Build OData filter
        var filters = new List<string>
        {
            $"createdon ge {payload.StartDate:yyyy-MM-ddTHH:mm:ssZ}",
            $"createdon le {payload.EndDate:yyyy-MM-ddTHH:mm:ssZ}"
        };

        // Direction filter
        if (!string.IsNullOrEmpty(payload.DirectionFilter))
        {
            var directionCode = payload.DirectionFilter.Equals("Incoming", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false";
            filters.Add($"directioncode eq {directionCode}");
        }

        // Status filter (for completed emails - statecode = 1 means completed)
        if (payload.StatusFilter == "Completed")
        {
            filters.Add("statecode eq 1");
        }

        // Sender domain filter
        if (!string.IsNullOrEmpty(payload.SenderDomainFilter))
        {
            filters.Add($"contains(sender, '@{payload.SenderDomainFilter}')");
        }

        // Subject contains filter
        if (!string.IsNullOrEmpty(payload.SubjectContainsFilter))
        {
            filters.Add($"contains(subject, '{payload.SubjectContainsFilter}')");
        }

        var filterStr = string.Join(" and ", filters);
        var url = $"emails?$select=activityid&$filter={filterStr}&$top={payload.MaxEmails}&$orderby=createdon asc";

        _logger.LogDebug("Querying emails with URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        if (data.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("activityid", out var idProp) &&
                    Guid.TryParse(idProp.GetString(), out var emailId))
                {
                    emailIds.Add(emailId);
                }
            }
        }

        return emailIds;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/') + "/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope!]),
                ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse authentication token for batch processing");
        }
    }

    private BatchProcessPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<BatchProcessPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse batch process job payload");
            return null;
        }
    }

    private static BatchJobState DetermineFinalState(BatchProcessResult result)
    {
        if (result.ProcessedCount == 0 && result.ErrorCount > 0)
        {
            return BatchJobState.Failed;
        }
        if (result.ErrorCount > 0)
        {
            return BatchJobState.PartiallyCompleted;
        }
        return BatchJobState.Completed;
    }

    private enum EmailProcessOutcome
    {
        Success,
        Skipped,
        Failed
    }

    private class BatchProcessResult
    {
        public int ProcessedCount { get; set; }
        public int ErrorCount { get; set; }
        public int SkippedCount { get; set; }
    }
}

/// <summary>
/// Payload structure for batch process jobs.
/// </summary>
public class BatchProcessPayload
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? ContainerId { get; set; }
    public bool IncludeAttachments { get; set; } = true;
    public bool CreateAttachmentDocuments { get; set; } = true;
    public bool QueueForAiProcessing { get; set; }
    public string? DirectionFilter { get; set; }
    public string StatusFilter { get; set; } = "Completed";
    public bool SkipAlreadyConverted { get; set; } = true;
    public int MaxEmails { get; set; } = 1000;
    public string? MailboxFilter { get; set; }
    public string? SenderDomainFilter { get; set; }
    public string? SubjectContainsFilter { get; set; }
    public int Priority { get; set; } = 5;
}
