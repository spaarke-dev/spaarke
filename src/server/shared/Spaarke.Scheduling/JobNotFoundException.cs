namespace Spaarke.Scheduling;

/// <summary>
/// Thrown by <see cref="ScheduledJobHost.TriggerNowAsync"/> when the supplied <c>jobId</c>
/// is not registered with <see cref="ScheduledJobRegistry"/>. The
/// <c>POST /api/admin/jobs/{jobId}/trigger</c> endpoint maps this to a 404 ProblemDetails
/// response (R3 task 021).
/// </summary>
/// <remarks>
/// Lives in <c>Spaarke.Scheduling</c> (not the BFF) so any future caller of
/// <see cref="ScheduledJobHost.TriggerNowAsync"/> — admin endpoints (task 021), CLI tools,
/// or other shared-lib consumers — can catch the same exception type without depending on
/// BFF-side code.
/// </remarks>
public sealed class JobNotFoundException : Exception
{
    /// <summary>The unregistered job id supplied to <see cref="ScheduledJobHost.TriggerNowAsync"/>.</summary>
    public string JobId { get; }

    /// <summary>Construct a <see cref="JobNotFoundException"/> with the standard message format.</summary>
    public JobNotFoundException(string jobId)
        : base($"No background job with jobId '{jobId}' is registered.")
    {
        JobId = jobId;
    }

    /// <summary>Construct a <see cref="JobNotFoundException"/> with a custom message and inner exception.</summary>
    public JobNotFoundException(string jobId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        JobId = jobId;
    }
}

/// <summary>
/// Outcome of a successful <see cref="ScheduledJobHost.TriggerNowAsync"/> call. Returned to
/// the <c>POST /api/admin/jobs/{jobId}/trigger</c> endpoint (R3 task 021), which forwards
/// the values verbatim to the admin client as the 202 Accepted body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Status"/> is always <c>"Running"</c> at the moment of dispatch — the host
/// kicks off the run on a tracked background task and returns immediately so the admin client
/// is not blocked on the job's actual duration (jobs may run for minutes/hours). Real-time
/// status can be polled via <c>GET /api/admin/jobs/{jobId}/status</c> (task 020).
/// </para>
/// <para>
/// <see cref="RunId"/> is the persistent run id written to the run-history store (matches
/// the value that will later appear in the run record); admin clients can use it as a stable
/// reference for that specific manual trigger.
/// </para>
/// </remarks>
/// <param name="RunId">Persistent run id assigned by <see cref="IBackgroundJobStore.RecordRunStartAsync"/>.</param>
/// <param name="Status">Canonical status at the moment of dispatch — always <c>"Running"</c>.</param>
/// <param name="StartedAt">UTC timestamp captured immediately before background dispatch.</param>
public sealed record TriggerResult(
    Guid RunId,
    string Status,
    DateTimeOffset StartedAt);
