namespace Spaarke.Dataverse;

/// <summary>
/// A <c>sprk_processingjob</c> row as the two job reads return it. Typed and public, so callers in other assemblies can
/// read it.
/// </summary>
/// <remarks>
/// spaarkeai-word-add-in-r1 task 060 (GitHub #1084). Both reads used to return an anonymous type. Anonymous types are
/// <c>internal</c> to the assembly that compiles them, and the BFF read them through <c>dynamic</c>. The runtime binder
/// checks the CALL SITE's access, and <c>Sprk.Bff.Api</c> cannot see this assembly's internals, so every read threw and
/// was swallowed (<c>RuntimeBinderException</c> in production, 2026-08-25). The job-status poll answered 404, and the
/// idempotency check answered "not a duplicate". Tests passed because their double returned a public
/// <c>ExpandoObject</c>.
/// </remarks>
public sealed record ProcessingJobRecord
{
    /// <summary><c>sprk_processingjobid</c>.</summary>
    public required Guid Id { get; init; }

    /// <summary><c>sprk_name</c>.</summary>
    public string? Name { get; init; }

    /// <summary><c>sprk_jobtype</c> option value.</summary>
    public int? JobType { get; init; }

    /// <summary><c>sprk_status</c> option value: 0 Pending, 1 In Progress, 2 Completed, 3 Failed, 4 Cancelled.</summary>
    public int? Status { get; init; }

    /// <summary><c>sprk_progress</c>.</summary>
    public int? Progress { get; init; }

    /// <summary><c>sprk_currentstage</c>.</summary>
    public string? CurrentStage { get; init; }

    /// <summary><c>sprk_idempotencykey</c>.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary><c>sprk_correlationid</c>.</summary>
    public string? CorrelationId { get; init; }

    /// <summary><c>sprk_initiatedby</c>: the creator's <c>systemuserid</c> (task 067).</summary>
    public Guid? InitiatedBy { get; init; }

    /// <summary>
    /// The creator's Entra object id, joined from <c>sprk_initiatedby</c> → <c>systemuser.azureactivedirectoryobjectid</c>,
    /// canonical bare-lowercase (ADR-044). Null when no creator was recorded. Only <see cref="IProcessingJobService.GetProcessingJobAsync"/>
    /// fills it.
    /// </summary>
    public string? InitiatedByOid { get; init; }

    /// <summary><c>sprk_result</c>: the job's output, as JSON. Its schema belongs to the writer.</summary>
    public string? Result { get; init; }

    /// <summary><c>sprk_errorcode</c>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary><c>sprk_errormessage</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary><c>createdon</c>, UTC.</summary>
    public DateTime? CreatedOn { get; init; }

    /// <summary><c>sprk_completeddate</c>, UTC.</summary>
    public DateTime? CompletedDate { get; init; }
}
