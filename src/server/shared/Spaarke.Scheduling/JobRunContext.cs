namespace Spaarke.Scheduling;

/// <summary>
/// Per-run inputs supplied to <see cref="IScheduledJob.ExecuteAsync"/> by the
/// <c>ScheduledJobHost</c>. Carries the run identifier, distributed-trace correlation id,
/// trigger source, and arbitrary parameter bag (sourced from <c>sprk_backgroundjob.sprk_configjson</c>
/// or admin-trigger request body).
/// </summary>
/// <remarks>Per spec.md FR-2.1 (R3 Background-Job Infrastructure). NFR-08 requires
/// <see cref="CorrelationId"/> to flow through to downstream logs and events.</remarks>
public record JobRunContext(
    Guid RunId,
    string CorrelationId,
    JobRunTrigger Trigger,
    IDictionary<string, object> Parameters);
