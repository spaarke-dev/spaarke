namespace Spaarke.Dataverse;

public interface IAccessDataSource
{
    Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default);
}

/// <summary>
/// Snapshot of user access permissions for a specific resource.
/// Captures granular Dataverse permissions and organizational context.
/// </summary>
public class AccessSnapshot
{
    public required string UserId { get; init; }
    public required string ResourceId { get; init; }

    /// <summary>
    /// Granular access rights mapped from Dataverse permissions.
    /// Uses [Flags] pattern to support multiple simultaneous permissions.
    /// </summary>
    public AccessRights AccessRights { get; init; }

    public IEnumerable<string> TeamMemberships { get; init; } = Array.Empty<string>();
    public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Granular access rights matching Dataverse permission model.
/// Uses [Flags] pattern for bitwise combination of permissions.
/// Maps directly to Dataverse RetrievePrincipalAccess response.
/// </summary>
/// <remarks>
/// Dataverse Permission Mapping:
/// - ReadAccess → Read (can view record)
/// - WriteAccess → Write (can update record)
/// - DeleteAccess → Delete (can delete record)
/// - CreateAccess → Create (can create new records)
/// - AppendAccess → Append (can attach to other records)
/// - AppendToAccess → AppendTo (other records can attach to this)
/// - ShareAccess → Share (can share with others)
///
/// Example: User with "ReadAccess,WriteAccess,DeleteAccess" gets:
/// AccessRights.Read | AccessRights.Write | AccessRights.Delete
/// </remarks>
[Flags]
public enum AccessRights
{
    /// <summary>No access permissions</summary>
    None         = 0,        // 0000000 - No access

    /// <summary>Can view/read the resource (preview only)</summary>
    Read         = 1 << 0,   // 0000001 - Bit 0

    /// <summary>Can update/modify the resource (includes download for files)</summary>
    Write        = 1 << 1,   // 0000010 - Bit 1

    /// <summary>Can delete the resource</summary>
    Delete       = 1 << 2,   // 0000100 - Bit 2

    /// <summary>Can create new records of this type</summary>
    Create       = 1 << 3,   // 0001000 - Bit 3

    /// <summary>Can attach this record to other records</summary>
    Append       = 1 << 4,   // 0010000 - Bit 4

    /// <summary>Other records can be attached to this record</summary>
    AppendTo     = 1 << 5,   // 0100000 - Bit 5

    /// <summary>Can share this record with other users</summary>
    Share        = 1 << 6    // 1000000 - Bit 6
}