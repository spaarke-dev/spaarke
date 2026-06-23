namespace Sprk.Bff.Api.Api.Admin.Models;

/// <summary>
/// Per-run detail DTO included in <see cref="JobStatusDetail.RecentRuns"/> by
/// <c>GET /api/admin/jobs/{jobId}/status</c>. Maps from one row of
/// <c>sprk_backgroundjobrun</c> (Dataverse) or one in-memory
/// <see cref="Spaarke.Scheduling.InMemoryBackgroundJobStore.RunRecord"/> (early-wave).
/// </summary>
/// <remarks>
/// Per R3 spec.md FR-2.6. <see cref="ProcessedItems"/> is nullable because not every job
/// reports a meaningful item count (per <see cref="Spaarke.Scheduling.JobRunResult.ProcessedItems"/>).
/// </remarks>
/// <param name="RunId">Persistent run identifier from the run-history store.</param>
/// <param name="Trigger">What caused this run (<c>"Scheduled"</c>, <c>"ManualAdmin"</c>, <c>"OnStartup"</c>) per NFR-08.</param>
/// <param name="CorrelationId">Distributed-trace correlation id flowed to downstream telemetry per NFR-08.</param>
/// <param name="StartedOn">When the run started executing.</param>
/// <param name="CompletedOn">When the run completed, or <c>null</c> if still running.</param>
/// <param name="Status">Run outcome (<c>"Succeeded"</c>, <c>"Failed"</c>, or <c>"InProgress"</c>).</param>
/// <param name="ErrorMessage">Final failure message from <see cref="Spaarke.Scheduling.JobRunResult.ErrorMessage"/>, or <c>null</c> on success or while in-progress.</param>
/// <param name="ProcessedItems">Optional item-processing count from <see cref="Spaarke.Scheduling.JobRunResult.ProcessedItems"/>.</param>
/// <param name="DurationMs">Run duration in milliseconds (<c>null</c> while in-progress).</param>
public sealed record JobRunDetail(
    Guid RunId,
    string Trigger,
    string CorrelationId,
    DateTimeOffset StartedOn,
    DateTimeOffset? CompletedOn,
    string Status,
    string? ErrorMessage,
    int? ProcessedItems,
    long? DurationMs);
