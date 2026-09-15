using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// Internal system-user shares on a record: the server half of the Manage Access "+ User" picker
// (unified-access-control-r2 task 063, spec FR-29; the picker is task 065). One file, because it is one contract.
//
// Every request names its record ONLY by recordType + recordId. There is no legacy projectId shorthand: the
// delegation check authorizes exactly the record whose shares are read or written.

/// <summary>Request body for <c>POST /api/v1/external-access/share-user</c>.</summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record to share. Required.</param>
/// <param name="SystemUserId">The Dataverse <c>systemuserid</c> of the person to share it with. Required.</param>
/// <param name="AccessLevel">
/// Required: <c>100000000</c> View Only, <c>100000001</c> Collaborate or <c>100000002</c> Full Access — the same values
/// as a contact grant's level. The share ends at EXACTLY this level: sharing again at another level replaces it, and
/// replaces any rights outside the three levels (such as a provisioning creator's Share right).
/// </param>
public record ShareRecordWithUserRequest(
    string? RecordType,
    Guid? RecordId,
    Guid? SystemUserId,
    ExternalAccessLevel? AccessLevel);

/// <summary>Response for a share confirmed at the requested level.</summary>
/// <param name="SystemUserId">The person the record is shared with.</param>
/// <param name="AccessLevel">The level the share is now at.</param>
/// <param name="AccessRightsMask">The rights mask Dataverse stored, read back after the write.</param>
/// <param name="Outcome">
/// <c>created</c> (the user held no share), <c>updated</c> (the level changed) or <c>unchanged</c> (the user already
/// held exactly this level; nothing was written).
/// </param>
public record ShareRecordWithUserResponse(
    Guid SystemUserId,
    ExternalAccessLevel AccessLevel,
    int AccessRightsMask,
    string Outcome);

/// <summary>Request body for <c>POST /api/v1/external-access/unshare-user</c>.</summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record whose share is removed. Required.</param>
/// <param name="SystemUserId">The Dataverse <c>systemuserid</c> whose share is removed. Required.</param>
public record UnshareRecordWithUserRequest(
    string? RecordType,
    Guid? RecordId,
    Guid? SystemUserId);

/// <summary>Response for an unshare.</summary>
/// <param name="SystemUserId">The person whose share was removed.</param>
/// <param name="Removed">
/// <c>true</c> when the user held a share and it is confirmed gone; <c>false</c> when they held none, in which case
/// nothing was written. Repeating an unshare is therefore safe.
/// </param>
public record UnshareRecordWithUserResponse(
    Guid SystemUserId,
    bool Removed);

/// <summary>Query string for <c>GET /api/v1/external-access/user-shares?recordType=&amp;recordId=</c>.</summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record whose shares are listed. Required.</param>
public record RecordUserSharesQuery(
    string? RecordType,
    Guid? RecordId);

/// <summary>Response for the share list: every system user holding a direct share on the record.</summary>
/// <param name="Shares">
/// Ordered by name. Excludes team shares and access inherited from a related record. Where the access came from is
/// task 064's read, not this one.
/// </param>
public record RecordUserSharesResponse(
    IReadOnlyList<RecordUserShare> Shares);

/// <summary>One system user's direct share on a record.</summary>
/// <param name="SystemUserId">The person holding the share.</param>
/// <param name="FullName">Their name, or <c>null</c> when it could not be read — the share is listed either way.</param>
/// <param name="AccessRightsMask">The rights mask Dataverse stores for the share.</param>
/// <param name="AccessLevel">
/// The level whose rights are EXACTLY <paramref name="AccessRightsMask"/>, or <c>null</c> when the share holds other
/// rights — for example a provisioning creator's Share right. Setting a level replaces those rights.
/// </param>
/// <param name="ModifiedOn">When the share last changed.</param>
public record RecordUserShare(
    Guid SystemUserId,
    string? FullName,
    int AccessRightsMask,
    ExternalAccessLevel? AccessLevel,
    DateTimeOffset ModifiedOn);
