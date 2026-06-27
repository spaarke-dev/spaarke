namespace Spaarke.Scheduling;

/// <summary>
/// Contract every Spaarke background job implements so the in-process <c>ScheduledJobHost</c>
/// can discover, schedule, and execute it uniformly. Implementations are registered in DI
/// and resolved by handler type recorded on the <c>sprk_backgroundjob</c> row.
/// </summary>
/// <remarks>Per spec.md FR-2.1 (R3 Background-Job Infrastructure). NFR-07 requires
/// implementations to honor the <see cref="CancellationToken"/> so host shutdown propagates
/// to running jobs within 30s. Permitted as an interface under ADR-010 as a testing seam.</remarks>
public interface IScheduledJob
{
    /// <summary>Unique key identifying this job (e.g., <c>"membership-reconciliation"</c>). Matches <c>sprk_backgroundjob.sprk_jobid</c>.</summary>
    string JobId { get; }

    /// <summary>Human-readable name for the admin UI.</summary>
    string DisplayName { get; }

    /// <summary>Short description of what the job does, surfaced in admin tooling.</summary>
    string Description { get; }

    /// <summary>
    /// Execute one run of the job. Implementations MUST observe <paramref name="cancellationToken"/>
    /// and return promptly on cancellation (NFR-07).
    /// </summary>
    Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken);
}
