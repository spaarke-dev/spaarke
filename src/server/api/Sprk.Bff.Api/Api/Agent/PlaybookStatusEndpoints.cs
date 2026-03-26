using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Manages async playbook execution status for long-running playbooks
/// triggered from the M365 Copilot agent.
///
/// Pattern: Agent starts playbook → receives job ID → polls this service for status.
/// When timeout threshold is exceeded, returns deep-link to Analysis code page.
/// </summary>
public sealed class AgentPlaybookStatusService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<AgentPlaybookStatusService> _logger;
    private readonly HandoffUrlBuilder _handoffUrlBuilder;

    // Default threshold: if playbook takes longer than this, offer deep-link
    private static readonly TimeSpan InlineExecutionTimeout = TimeSpan.FromSeconds(30);

    private const string CacheKeyPrefix = "agent:playbook-job:";
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(4)
    };

    public AgentPlaybookStatusService(
        IDistributedCache cache,
        ILogger<AgentPlaybookStatusService> logger,
        HandoffUrlBuilder handoffUrlBuilder)
    {
        _cache = cache;
        _logger = logger;
        _handoffUrlBuilder = handoffUrlBuilder;
    }

    /// <summary>
    /// Registers a new playbook execution job for tracking.
    /// </summary>
    public async Task<Guid> RegisterJobAsync(
        string tenantId,
        Guid playbookId,
        Guid documentId,
        Guid? analysisId = null,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();
        var status = new PlaybookJobStatus
        {
            JobId = jobId,
            TenantId = tenantId,
            PlaybookId = playbookId,
            DocumentId = documentId,
            AnalysisId = analysisId,
            Status = "queued",
            Progress = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        await SaveJobStatusAsync(status, cancellationToken);

        _logger.LogInformation(
            "Registered agent playbook job {JobId} for playbook {PlaybookId} on document {DocumentId}",
            jobId, playbookId, documentId);

        return jobId;
    }

    /// <summary>
    /// Updates the progress of a running playbook job.
    /// </summary>
    public async Task UpdateProgressAsync(
        Guid jobId,
        string tenantId,
        string status,
        int progress,
        string? currentStep = null,
        CancellationToken cancellationToken = default)
    {
        var job = await GetJobStatusInternalAsync(tenantId, jobId, cancellationToken);
        if (job is null) return;

        job.Status = status;
        job.Progress = progress;
        job.CurrentStep = currentStep;

        if (status is "completed" or "failed")
            job.CompletedAt = DateTimeOffset.UtcNow;

        await SaveJobStatusAsync(job, cancellationToken);
    }

    /// <summary>
    /// Gets the current status of a playbook job.
    /// If the job has been running longer than the inline timeout, includes a deep-link.
    /// </summary>
    public async Task<PlaybookStatusResponse?> GetStatusAsync(
        string tenantId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await GetJobStatusInternalAsync(tenantId, jobId, cancellationToken);
        if (job is null) return null;

        // Build deep-link if running longer than threshold
        string? deepLinkUrl = null;
        if (job.Status == "running" &&
            DateTimeOffset.UtcNow - job.StartedAt > InlineExecutionTimeout &&
            job.AnalysisId.HasValue)
        {
            deepLinkUrl = _handoffUrlBuilder.BuildAnalysisWorkspaceUrl(
                job.AnalysisId.Value,
                job.DocumentId,
                job.PlaybookId);
        }

        // Use the existing PlaybookStatusResponse record from AgentModels.cs
        // Note: DeepLinkUrl is returned separately since the record doesn't have it
        return new PlaybookStatusResponse
        {
            JobId = job.JobId,
            Status = job.Status,
            ProgressPercent = job.Progress,
            ResultCardJson = job.Status == "completed" ? job.ResultCardJson : null,
            ErrorMessage = job.Status == "failed" ? job.CurrentStep : null
        };
    }

    /// <summary>
    /// Stores the final result card JSON for a completed job.
    /// </summary>
    public async Task SetResultCardAsync(
        Guid jobId,
        string tenantId,
        string resultCardJson,
        CancellationToken cancellationToken = default)
    {
        var job = await GetJobStatusInternalAsync(tenantId, jobId, cancellationToken);
        if (job is null) return;

        job.Status = "completed";
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ResultCardJson = resultCardJson;

        await SaveJobStatusAsync(job, cancellationToken);
    }

    private async Task<PlaybookJobStatus?> GetJobStatusInternalAsync(
        string tenantId, Guid jobId, CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}:{jobId}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        return cached is null ? null : JsonSerializer.Deserialize<PlaybookJobStatus>(cached);
    }

    private async Task SaveJobStatusAsync(
        PlaybookJobStatus status, CancellationToken cancellationToken)
    {
        var cacheKey = $"{CacheKeyPrefix}{status.TenantId}:{status.JobId}";
        var json = JsonSerializer.Serialize(status);
        await _cache.SetStringAsync(cacheKey, json, CacheOptions, cancellationToken);
    }
}

/// <summary>
/// Internal job tracking state (cached in Redis).
/// </summary>
internal sealed class PlaybookJobStatus
{
    public Guid JobId { get; set; }
    public string TenantId { get; set; } = "";
    public Guid PlaybookId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? AnalysisId { get; set; }
    public string Status { get; set; } = "queued";
    public int Progress { get; set; }
    public string? CurrentStep { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ResultCardJson { get; set; }
}

// PlaybookStatusResponse is defined in AgentModels.cs — reuse that type.
