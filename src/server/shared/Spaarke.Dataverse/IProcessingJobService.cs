namespace Spaarke.Dataverse;

/// <summary>
/// Processing job lifecycle and artifact operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IProcessingJobService
{
    Task<Guid> CreateProcessingJobAsync(object request, CancellationToken ct = default);
    Task UpdateProcessingJobAsync(Guid id, object request, CancellationToken ct = default);
    Task<object?> GetProcessingJobAsync(Guid id, CancellationToken ct = default);
    Task<object?> GetProcessingJobByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<Guid> CreateEmailArtifactAsync(object request, CancellationToken ct = default);
    Task<object?> GetEmailArtifactAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAttachmentArtifactAsync(object request, CancellationToken ct = default);
    Task<object?> GetAttachmentArtifactAsync(Guid id, CancellationToken ct = default);
}
