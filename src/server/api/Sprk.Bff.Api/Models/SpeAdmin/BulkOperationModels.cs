namespace Sprk.Bff.Api.Models.SpeAdmin;

/// <summary>
/// Implemented by every SPE admin request BODY that names a container type config. The tenant-scope
/// filter (<c>SpeAdminTenantScopeFilter</c>) reads <see cref="ConfigId"/> from any bound argument that
/// implements this, so the business-unit boundary runs on body-carried configIds by contract.
/// </summary>
/// <remarks>
/// unified-access-control-r2 task 165 (sweep findings #44, #72). The two bulk request records carried
/// <c>ConfigId</c> in the body, where the filter never looked, so any SPE admin could bulk-delete or
/// grant owner on another business unit's containers. <c>SpeAdminConfigScopedBodyGuardTests</c> fails
/// the build when a request record under <c>Models/SpeAdmin</c> carries a <c>ConfigId</c> without
/// implementing this — the next such record must not bypass the boundary the way these two did.
/// </remarks>
public interface ISpeAdminConfigScopedRequest
{
    /// <summary>The config the request acts on, as the client sent it (validated by the endpoint).</summary>
    string? ConfigId { get; }
}

/// <summary>
/// Implemented by every SPE admin request BODY that names ONE container the request acts on. The tenant-scope
/// filter reads <see cref="ContainerId"/> from any bound argument that implements this and applies the
/// per-container business-unit rule to it (unified-access-control-r2 task 165, owner round 20 item 2), exactly as
/// it does for a <c>{containerId}</c> route value.
/// </summary>
/// <remarks>
/// <c>SpeAdminConfigScopedBodyGuardTests</c> fails the build when a body bound by any <c>/api/spe</c> handler carries
/// a <c>ContainerId</c> without implementing this. A body that names MANY containers (the bulk requests) does not:
/// those are authorized per item in the job.
/// </remarks>
public interface ISpeAdminContainerScopedRequest
{
    /// <summary>The container the request acts on, or null/empty when it names none.</summary>
    string? ContainerId { get; }
}

/// <summary>
/// Specifies the type of bulk operation to perform on a set of SPE containers.
/// </summary>
public enum BulkOperationType
{
    /// <summary>Soft-delete (recycle-bin) the specified containers.</summary>
    Delete,

    /// <summary>Grant or update a permission on each of the specified containers.</summary>
    AssignPermissions,
}

/// <summary>
/// Request body for initiating a bulk delete operation.
/// Soft-deletes (recycle bin) all specified containers.
///
/// POST /api/spe/bulk/delete
/// </summary>
/// <param name="ContainerIds">
/// Non-empty list of SPE container IDs to delete.
/// All containers must belong to the container type of the config named by <c>ConfigId</c>; the
/// background job refuses, per item, any container of another type (task 165).
/// </param>
/// <param name="ConfigId">
/// Dataverse ID of the SPE container type config that authenticates Graph API calls.
/// Must be a valid GUID string.
/// </param>
/// <remarks>
/// ADR-007: No Graph SDK types are exposed — callers receive only this domain model.
/// </remarks>
public sealed record BulkDeleteRequest(
    IReadOnlyList<string> ContainerIds,
    string ConfigId) : ISpeAdminConfigScopedRequest;

/// <summary>
/// Request body for initiating a bulk permission assignment operation.
/// Grants the specified role for the given user or group on each container.
///
/// POST /api/spe/bulk/permissions
/// </summary>
/// <param name="ContainerIds">
/// Non-empty list of SPE container IDs to assign the permission to.
/// </param>
/// <param name="ConfigId">
/// Dataverse ID of the SPE container type config that authenticates Graph API calls.
/// </param>
/// <param name="UserId">
/// Azure AD user object ID to grant access to. Mutually exclusive with <paramref name="GroupId"/>.
/// Exactly one of <c>UserId</c> or <c>GroupId</c> must be provided.
/// </param>
/// <param name="GroupId">
/// Azure AD group object ID to grant access to. Mutually exclusive with <paramref name="UserId"/>.
/// </param>
/// <param name="Role">
/// SPE permission role to assign. Must be one of: reader, writer, manager, owner.
/// </param>
/// <remarks>
/// ADR-007: No Graph SDK types are exposed — callers receive only this domain model.
/// </remarks>
public sealed record BulkPermissionsRequest(
    IReadOnlyList<string> ContainerIds,
    string ConfigId,
    string? UserId,
    string? GroupId,
    string Role) : ISpeAdminConfigScopedRequest;

/// <summary>
/// Tracks the live progress of a bulk operation.
///
/// Polled via GET /api/spe/bulk/{operationId}/status.
/// </summary>
/// <param name="OperationId">Unique identifier for this bulk operation.</param>
/// <param name="OperationType">The type of bulk operation being performed.</param>
/// <param name="Total">Total number of items to process.</param>
/// <param name="Completed">Number of items successfully processed so far.</param>
/// <param name="Failed">Number of items that failed processing.</param>
/// <param name="IsFinished">
/// <c>true</c> when all items have been processed (either successfully or with errors).
/// </param>
/// <param name="Errors">
/// Per-container error details for items that failed.
/// Empty when no failures have occurred.
/// </param>
/// <param name="StartedAt">UTC timestamp when the operation was enqueued.</param>
/// <param name="CompletedAt">UTC timestamp when the operation finished, or <c>null</c> if still running.</param>
public sealed record BulkOperationStatus(
    Guid OperationId,
    BulkOperationType OperationType,
    int Total,
    int Completed,
    int Failed,
    bool IsFinished,
    IReadOnlyList<BulkOperationItemError> Errors,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// Describes a single item failure within a bulk operation.
/// </summary>
/// <param name="ContainerId">The container ID that failed.</param>
/// <param name="ErrorMessage">Human-readable description of the failure reason.</param>
public sealed record BulkOperationItemError(
    string ContainerId,
    string ErrorMessage);

/// <summary>
/// Lightweight acknowledgement returned when a bulk operation is accepted.
///
/// Returned by POST /api/spe/bulk/delete and POST /api/spe/bulk/permissions.
/// </summary>
/// <param name="OperationId">
/// Unique identifier for the bulk operation. Use to poll status.
/// </param>
/// <param name="StatusUrl">
/// Relative URL to poll for progress: /api/spe/bulk/{operationId}/status
/// </param>
public sealed record BulkOperationAccepted(
    Guid OperationId,
    string StatusUrl);
