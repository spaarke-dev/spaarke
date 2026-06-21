namespace Spaarke.Scheduling;

/// <summary>
/// Outcome of a single <see cref="IScheduledJob"/> execution. Persisted to
/// <c>sprk_backgroundjobrun</c> by the host for run-history audit and admin inspection.
/// </summary>
/// <remarks>Per spec.md FR-2.1 (R3 Background-Job Infrastructure). <see cref="ProcessedItems"/>
/// is optional — jobs that don't have a meaningful item count return <c>null</c>.</remarks>
public record JobRunResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration);
