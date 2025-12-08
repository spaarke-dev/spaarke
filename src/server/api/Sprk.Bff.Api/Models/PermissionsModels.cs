using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Models;

/// <summary>
/// Represents what a user can do with a specific Dataverse document.
/// Used by UI (PCF controls, Power Apps, React) to show/hide buttons and controls.
/// </summary>
/// <remarks>
/// This DTO exposes user capabilities based on Dataverse AccessRights.
/// Each boolean indicates if the user has permission for that operation.
///
/// Example usage in PCF control:
/// - if (capabilities.canPreview) { showPreviewButton(); }
/// - if (capabilities.canDownload) { showDownloadButton(); }
/// - if (capabilities.canDelete) { showDeleteButton(); }
/// </remarks>
public class DocumentCapabilities
{
    /// <summary>
    /// Dataverse document ID (sprk_documentid)
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// User ID (systemuserid) these capabilities apply to
    /// </summary>
    public required string UserId { get; init; }

    // ========================================================================
    // FILE CONTENT OPERATIONS
    // ========================================================================

    /// <summary>
    /// Can preview file in Office Online (read-only viewer)
    /// Requires: AccessRights.Read
    /// </summary>
    public bool CanPreview { get; init; }

    /// <summary>
    /// Can download file to local device
    /// Requires: AccessRights.Write (NOT just Read - security policy)
    /// </summary>
    public bool CanDownload { get; init; }

    /// <summary>
    /// Can upload new file to document
    /// Requires: AccessRights.Write + AccessRights.Create
    /// </summary>
    public bool CanUpload { get; init; }

    /// <summary>
    /// Can replace existing file with new version
    /// Requires: AccessRights.Write
    /// </summary>
    public bool CanReplace { get; init; }

    /// <summary>
    /// Can delete file (soft delete to recycle bin)
    /// Requires: AccessRights.Delete
    /// </summary>
    public bool CanDelete { get; init; }

    // ========================================================================
    // METADATA OPERATIONS
    // ========================================================================

    /// <summary>
    /// Can read document metadata from Dataverse
    /// Requires: AccessRights.Read
    /// </summary>
    public bool CanReadMetadata { get; init; }

    /// <summary>
    /// Can update document metadata in Dataverse
    /// Requires: AccessRights.Write
    /// </summary>
    public bool CanUpdateMetadata { get; init; }

    // ========================================================================
    // SHARING OPERATIONS
    // ========================================================================

    /// <summary>
    /// Can share document with other users
    /// Requires: AccessRights.Share
    /// </summary>
    public bool CanShare { get; init; }

    // ========================================================================
    // VERSIONING OPERATIONS
    // ========================================================================

    /// <summary>
    /// Can view file version history
    /// Requires: AccessRights.Read
    /// </summary>
    public bool CanViewVersions { get; init; }

    /// <summary>
    /// Can restore previous file version
    /// Requires: AccessRights.Write
    /// </summary>
    public bool CanRestoreVersion { get; init; }

    // ========================================================================
    // ADVANCED OPERATIONS
    // ========================================================================

    /// <summary>
    /// Can move file to different folder/container
    /// Requires: AccessRights.Write + AccessRights.Delete
    /// </summary>
    public bool CanMove { get; init; }

    /// <summary>
    /// Can copy file to different location
    /// Requires: AccessRights.Read + AccessRights.Create
    /// </summary>
    public bool CanCopy { get; init; }

    /// <summary>
    /// Can check out file for editing (locks file)
    /// Requires: AccessRights.Write
    /// </summary>
    public bool CanCheckOut { get; init; }

    /// <summary>
    /// Can check in file after editing (unlocks file)
    /// Requires: AccessRights.Write
    /// </summary>
    public bool CanCheckIn { get; init; }

    // ========================================================================
    // RAW ACCESS RIGHTS (for debugging/advanced scenarios)
    // ========================================================================

    /// <summary>
    /// Raw AccessRights flags from Dataverse
    /// Human-readable format: "Read, Write, Delete"
    /// </summary>
    public string AccessRights { get; init; } = string.Empty;

    /// <summary>
    /// When these capabilities were calculated (for caching)
    /// </summary>
    public DateTimeOffset CalculatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Request to get permissions for multiple documents in one call.
/// Used by UI galleries to avoid N+1 query problem.
/// </summary>
/// <example>
/// POST /api/documents/permissions/batch
/// {
///   "documentIds": ["doc-1", "doc-2", "doc-3"]
/// }
/// </example>
public class BatchPermissionsRequest
{
    /// <summary>
    /// List of Dataverse document IDs (sprk_documentid values)
    /// </summary>
    public required List<string> DocumentIds { get; init; }

    /// <summary>
    /// Optional: User ID to check permissions for
    /// If not provided, uses current authenticated user
    /// </summary>
    public string? UserId { get; init; }
}

/// <summary>
/// Response containing permissions for multiple documents.
/// </summary>
public class BatchPermissionsResponse
{
    /// <summary>
    /// Capabilities for each document requested
    /// Order matches request order
    /// </summary>
    public required List<DocumentCapabilities> Permissions { get; init; }

    /// <summary>
    /// Any document IDs that failed to retrieve permissions
    /// Includes error reason
    /// </summary>
    public List<PermissionError> Errors { get; init; } = new();

    /// <summary>
    /// Total number of documents processed
    /// </summary>
    public int TotalProcessed { get; init; }

    /// <summary>
    /// Number of successful permission checks
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed permission checks
    /// </summary>
    public int ErrorCount { get; init; }
}

/// <summary>
/// Represents an error when retrieving permissions for a document.
/// </summary>
public class PermissionError
{
    /// <summary>
    /// Document ID that failed
    /// </summary>
    public required string DocumentId { get; init; }

    /// <summary>
    /// Error reason code
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// Human-readable error message
    /// </summary>
    public required string Message { get; init; }
}
