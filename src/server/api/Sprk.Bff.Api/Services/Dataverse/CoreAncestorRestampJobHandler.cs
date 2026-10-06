using System.Text.Json;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// ADR-004 handler for <see cref="JobTypeName"/>: re-stamps ONE child from its source's current root
/// (<see cref="CoreAncestorRestamper.RestampChildAsync"/>), cascading to its own children when it changed. Idempotent: a
/// child already equal to its root is a no-op, so redelivery and an overlapping reconciliation run are both safe.
/// </summary>
public sealed class CoreAncestorRestampJobHandler : IJobHandler
{
    /// <summary>The job type the queue routes on.</summary>
    public const string JobTypeName = "CoreAncestorRestamp";

    private readonly CoreAncestorRestamper _restamper;
    private readonly ILogger<CoreAncestorRestampJobHandler> _logger;

    public CoreAncestorRestampJobHandler(CoreAncestorRestamper restamper, ILogger<CoreAncestorRestampJobHandler> logger)
    {
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var started = TimeProvider.System.GetTimestamp();

        CoreAncestorRestampPayload? payload;
        try
        {
            payload = job.Payload?.Deserialize<CoreAncestorRestampPayload>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "{Prefix} Invalid re-stamp payload for job {JobId}; dead-lettering.",
                CoreAncestorRestamper.LogPrefix, job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid CoreAncestorRestamp payload.", job.Attempt,
                TimeProvider.System.GetElapsedTime(started));
        }

        if (payload is null || payload.Id == Guid.Empty || string.IsNullOrWhiteSpace(payload.Entity)
            || !CoreAncestorResolver.IsStampedChildEntity(payload.Entity))
        {
            return JobOutcome.Poisoned(job.JobId, JobType,
                "CoreAncestorRestamp payload names no record whose stamp can move.",
                job.Attempt, TimeProvider.System.GetElapsedTime(started));
        }

        var report = await _restamper.RestampChildAsync(payload.Entity, payload.Id, ct).ConfigureAwait(false);
        var elapsed = TimeProvider.System.GetElapsedTime(started);

        if (report.Complete)
        {
            return JobOutcome.Success(job.JobId, JobType, elapsed);
        }

        if (report.Failures.Count == 0)
        {
            // TRUNCATED only (verifier round 2 item 13): a bound stopped the cascade, and nothing failed. A retry re-lists
            // the same children from the first page and stops at the same bound, so it can never get further — retrying
            // would only end in a dead letter. The reconciliation job finishes the rest (it continues a stopped scan
            // across runs), and the storage resolver refuses a stale copy until it does. Done, with a warning.
            _logger.LogWarning(
                "{Prefix} Job {JobId}: the re-stamp of {Entity} {Id} stopped at a bound (examined={Examined} changed={Changed}); "
                + "the rest is left to the reconciliation job, not retried.",
                CoreAncestorRestamper.LogPrefix, job.JobId, payload.Entity, payload.Id, report.Examined, report.Changed);
            return JobOutcome.Success(job.JobId, JobType, elapsed);
        }

        var reason = string.Join("; ", report.Failures.Select(f => $"{f.Entity} {f.Id}: {f.Reason}"))
                     + (report.Truncated ? " (truncated)" : string.Empty);

        // Retried until the queue's max attempts, then dead-lettered (visible) — and the reconciliation job repairs it
        // regardless, so a dead letter here is never the only record of the stale copy.
        return job.IsAtMaxAttempts
            ? JobOutcome.Poisoned(job.JobId, JobType, reason, job.Attempt, elapsed)
            : JobOutcome.Failure(job.JobId, JobType, reason, job.Attempt, elapsed);
    }
}
