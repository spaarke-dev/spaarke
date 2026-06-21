using System.Collections.Concurrent;

namespace Spaarke.Scheduling;

/// <summary>
/// In-memory <see cref="IBackgroundJobStore"/> implementation used (1) by unit tests in
/// <c>Spaarke.Scheduling.Tests</c> and (2) by early-wave BFF deployments before the
/// <c>sprk_backgroundjob</c> / <c>sprk_backgroundjobrun</c> Dataverse entities land
/// (tasks 015 / 016).
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe. Run records are kept in process memory and lost on host restart — intentional
/// for the test / bootstrap use case. The Dataverse-backed implementation will replace this
/// in production (task 023 onward).
/// </para>
/// <para>
/// Job definitions can be seeded via <see cref="AddOrReplaceJob"/> (typical pattern: feature
/// modules call this from DI registration). Tests can also use <see cref="RunRecords"/>
/// to assert run-history side effects.
/// </para>
/// </remarks>
public sealed class InMemoryBackgroundJobStore : IBackgroundJobStore
{
    private readonly ConcurrentDictionary<string, BackgroundJobDefinition> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, RunRecord> _runs = new();

    /// <summary>Seed or replace a job definition by <see cref="BackgroundJobDefinition.JobId"/>.</summary>
    public void AddOrReplaceJob(BackgroundJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _jobs[definition.JobId] = definition;
    }

    /// <summary>Remove a job definition by id. Returns <c>true</c> if removed.</summary>
    public bool RemoveJob(string jobId) => _jobs.TryRemove(jobId, out _);

    /// <summary>All recorded runs (test inspection surface). Snapshot — not live.</summary>
    public IReadOnlyCollection<RunRecord> RunRecords => _runs.Values.ToArray();

    /// <inheritdoc />
    public Task<IReadOnlyList<BackgroundJobDefinition>> LoadJobsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<BackgroundJobDefinition> snapshot = _jobs.Values.ToArray();
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<Guid> RecordRunStartAsync(
        string jobId,
        JobRunTrigger trigger,
        string correlationId,
        DateTimeOffset? scheduledFireUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        ArgumentException.ThrowIfNullOrEmpty(correlationId);
        cancellationToken.ThrowIfCancellationRequested();

        var runId = Guid.NewGuid();
        _runs[runId] = new RunRecord(
            runId,
            jobId,
            trigger,
            correlationId,
            ScheduledFireUtc: scheduledFireUtc,
            StartedAtUtc: DateTimeOffset.UtcNow,
            CompletedAtUtc: null,
            Result: null);
        return Task.FromResult(runId);
    }

    /// <inheritdoc />
    public Task RecordRunCompleteAsync(
        Guid runId,
        JobRunResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        cancellationToken.ThrowIfCancellationRequested();

        _runs.AddOrUpdate(
            runId,
            // Run start was never recorded (defensive — produce a free-standing terminal record)
            _ => new RunRecord(
                runId,
                JobId: "(unknown)",
                Trigger: JobRunTrigger.Scheduled,
                CorrelationId: "(unknown)",
                ScheduledFireUtc: null,
                StartedAtUtc: DateTimeOffset.UtcNow - result.Duration,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Result: result),
            (_, existing) => existing with
            {
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Result = result
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasRunForScheduledTimeAsync(
        string jobId,
        DateTimeOffset scheduledFireUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(jobId);
        cancellationToken.ThrowIfCancellationRequested();

        // Snapshot to avoid races with concurrent RecordRunStartAsync. Match on (jobId,
        // scheduledFireUtc) — any prior run (in-progress OR completed) is a duplicate.
        // UTC equality is exact here because the host derives scheduledFireUtc from the same
        // CronExpression.GetNextOccurrence call (which produces deterministic UTC DateTime
        // values at minute or second precision).
        foreach (var record in _runs.Values)
        {
            if (record.ScheduledFireUtc is { } recorded
                && string.Equals(record.JobId, jobId, StringComparison.Ordinal)
                && recorded == scheduledFireUtc)
            {
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    /// <summary>Seed a run record directly — TEST-ONLY surface for simulating host-restart scenarios
    /// where a prior run already exists in the store before the host first dispatches the job.</summary>
    public void SeedRunRecord(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _runs[record.RunId] = record;
    }

    /// <summary>One run-history row (in-memory parallel of <c>sprk_backgroundjobrun</c>).</summary>
    public sealed record RunRecord(
        Guid RunId,
        string JobId,
        JobRunTrigger Trigger,
        string CorrelationId,
        DateTimeOffset? ScheduledFireUtc,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        JobRunResult? Result);
}
