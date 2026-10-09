namespace Spaarke.Dataverse;

/// <summary>
/// Processing job lifecycle and artifact operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IProcessingJobService
{
    Task<Guid> CreateProcessingJobAsync(object request, CancellationToken ct = default);
    Task UpdateProcessingJobAsync(Guid id, object request, CancellationToken ct = default);
    // Typed (task 060, #1084): these returned anonymous types that no other assembly could read through `dynamic`.
    Task<ProcessingJobRecord?> GetProcessingJobAsync(Guid id, CancellationToken ct = default);
    Task<ProcessingJobRecord?> GetProcessingJobByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// The NEWEST job with this idempotency key that the caller started — <c>sprk_initiatedby</c>'s systemuser has this
    /// Entra object id (spaarkeai-word-add-in-r1 task 121). An email key names the message by its RFC Message-ID, the same
    /// in every recipient's mailbox, so another user's newer job under the same key must not hide the caller's own.
    /// </summary>
    Task<ProcessingJobRecord?> GetCallersProcessingJobByIdempotencyKeyAsync(
        string idempotencyKey, string initiatorObjectId, CancellationToken ct = default);
    Task<Guid> CreateEmailArtifactAsync(object request, CancellationToken ct = default);
    Task<object?> GetEmailArtifactAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAttachmentArtifactAsync(object request, CancellationToken ct = default);
    Task<object?> GetAttachmentArtifactAsync(Guid id, CancellationToken ct = default);
}
