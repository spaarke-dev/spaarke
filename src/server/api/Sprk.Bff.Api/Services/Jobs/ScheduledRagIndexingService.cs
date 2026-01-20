using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Configuration options for scheduled RAG document indexing.
/// </summary>
public class ScheduledRagIndexingOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "ScheduledRagIndexing";

    /// <summary>
    /// Whether the scheduled indexing service is enabled.
    /// Default: false (opt-in feature).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Interval between indexing runs in minutes.
    /// Default: 60 (hourly).
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum number of documents to index per scheduled run.
    /// Default: 100.
    /// </summary>
    public int MaxDocumentsPerRun { get; set; } = 100;

    /// <summary>
    /// Maximum concurrent document processing.
    /// Default: 5.
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    /// Tenant ID for multi-tenant filtering.
    /// Required if Enabled is true.
    /// </summary>
    public string? TenantId { get; set; }
}

/// <summary>
/// Background service that periodically indexes unindexed documents via bulk RAG indexing.
/// Acts as a catch-up mechanism for documents that weren't indexed during upload.
///
/// Implements ADR-001 BackgroundService pattern with PeriodicTimer.
/// Submits BulkRagIndexing jobs to the sdap-jobs queue for processing.
/// </summary>
/// <remarks>
/// Entry point: Scheduled timer (configurable interval, default 60 minutes)
/// Filter: Documents with sprk_hasfile=true and sprk_ragindexedon=null
/// Uses app-only authentication for Dataverse queries and SPE file access.
/// </remarks>
public class ScheduledRagIndexingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledRagIndexingService> _logger;
    private readonly ScheduledRagIndexingOptions _options;

    public ScheduledRagIndexingService(
        IServiceProvider serviceProvider,
        IOptions<ScheduledRagIndexingOptions> options,
        ILogger<ScheduledRagIndexingService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Scheduled RAG indexing service disabled. Set ScheduledRagIndexing:Enabled=true to enable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.TenantId))
        {
            _logger.LogError(
                "Scheduled RAG indexing service requires TenantId configuration. Service will not start.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.IntervalMinutes);
        _logger.LogInformation(
            "Scheduled RAG indexing service starting with {Interval} minute interval, " +
            "max {MaxDocs} documents per run, tenant {TenantId}",
            _options.IntervalMinutes,
            _options.MaxDocumentsPerRun,
            _options.TenantId);

        // Use PeriodicTimer for efficient periodic execution (ADR-001 pattern)
        using var timer = new PeriodicTimer(interval);

        // Initial delay to let the service fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SubmitScheduledIndexingJobAsync(stoppingToken);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Scheduled RAG indexing service stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled RAG indexing cycle, will retry on next interval");
                // Continue running - don't let one failure stop the service
            }
        }

        _logger.LogInformation("Scheduled RAG indexing service stopped");
    }

    /// <summary>
    /// Submits a bulk RAG indexing job for unindexed documents.
    /// The job handler will query Dataverse and process documents.
    /// </summary>
    private async Task SubmitScheduledIndexingJobAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var correlationId = $"scheduled-rag-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        _logger.LogDebug(
            "Starting scheduled RAG indexing cycle, correlation {CorrelationId}",
            correlationId);

        try
        {
            var jobSubmissionService = scope.ServiceProvider.GetRequiredService<JobSubmissionService>();

            // Build bulk indexing payload
            var payload = new BulkRagIndexingPayload
            {
                TenantId = _options.TenantId!,
                Filter = "unindexed", // Only documents not yet indexed
                MaxDocuments = _options.MaxDocumentsPerRun,
                MaxConcurrency = _options.MaxConcurrency,
                ForceReindex = false,
                Source = "Scheduled"
            };

            var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(payload));

            // Create job contract
            var job = new JobContract
            {
                JobType = BulkRagIndexingJobHandler.JobTypeName,
                SubjectId = _options.TenantId!,
                CorrelationId = correlationId,
                IdempotencyKey = $"scheduled-rag-{_options.TenantId}-{DateTime.UtcNow:yyyyMMddHH}",
                Payload = jobPayload,
                MaxAttempts = 1 // Scheduled jobs should not auto-retry
            };

            // Submit job to queue
            await jobSubmissionService.SubmitJobAsync(job, ct);

            _logger.LogInformation(
                "Submitted scheduled bulk RAG indexing job {JobId} for tenant {TenantId}",
                job.JobId, _options.TenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit scheduled RAG indexing job for tenant {TenantId}",
                _options.TenantId);
            throw;
        }
    }
}
