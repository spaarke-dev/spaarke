using Spaarke.Dataverse;

namespace Spaarke.Core.Auth;

/// <summary>
/// Defines 1:1 mapping between SharePoint Embedded/Microsoft Graph API operations and required Dataverse AccessRights.
/// Ensures complete coverage of all SPE container and DriveItem operations.
/// </summary>
/// <remarks>
/// **Design Principle**: Tight integration with SPE/Graph API
/// - Operation names match Graph API methods (e.g., "driveitem.get", "container.create")
/// - Every SPE operation has explicit permission requirements
/// - Dataverse permissions control SPE access (Read/Write/Delete/Create/Append/AppendTo/Share)
///
/// **Business Rule**: Download requires Write access (not just Read) for security compliance
///
/// All methods are static for performance. Thread-safe for concurrent access.
/// </remarks>
public static class OperationAccessPolicy
{
    /// <summary>
    /// Complete mapping of SharePoint Embedded/Graph API operations to required Dataverse AccessRights.
    /// Covers all DriveItem operations, Container operations, and collaboration features.
    /// </summary>
    private static readonly Dictionary<string, AccessRights> _operationRequirements = new(StringComparer.OrdinalIgnoreCase)
    {
        // ========================================================================
        // DRIVEITEM METADATA OPERATIONS
        // ========================================================================
        ["driveitem.get"] = AccessRights.Read,                           // Retrieve DriveItem metadata
        ["driveitem.update"] = AccessRights.Write,                       // Update DriveItem properties
        ["driveitem.list.children"] = AccessRights.Read,                 // List child items in folder

        // ========================================================================
        // DRIVEITEM CONTENT OPERATIONS
        // ========================================================================
        ["driveitem.content.download"] = AccessRights.Write,             // CRITICAL: Download requires Write (security policy)
        ["driveitem.content.upload"] = AccessRights.Write | AccessRights.Create,  // Upload new file
        ["driveitem.content.replace"] = AccessRights.Write,              // Replace existing file content
        ["driveitem.preview"] = AccessRights.Read,                       // Generate preview URL (read-only)

        // ========================================================================
        // DRIVEITEM FILE MANAGEMENT
        // ========================================================================
        ["driveitem.create.folder"] = AccessRights.Create | AccessRights.Write,  // Create new folder
        ["driveitem.move"] = AccessRights.Write | AccessRights.Delete,   // Move item (delete from source, write to destination)
        ["driveitem.copy"] = AccessRights.Read | AccessRights.Create,    // Copy item (read source, create destination)
        ["driveitem.delete"] = AccessRights.Delete,                      // Delete item (soft delete to recycle bin)
        ["driveitem.permanentdelete"] = AccessRights.Delete,             // Permanently delete (bypass recycle bin)

        // ========================================================================
        // DRIVEITEM SHARING & PERMISSIONS
        // ========================================================================
        ["driveitem.createlink"] = AccessRights.Share,                   // Create sharing link
        ["driveitem.permissions.add"] = AccessRights.Share,              // Invite users to access
        ["driveitem.permissions.list"] = AccessRights.Read,              // View current permissions
        ["driveitem.permissions.delete"] = AccessRights.Share,           // Remove specific permission

        // ========================================================================
        // DRIVEITEM VERSIONING
        // ========================================================================
        ["driveitem.versions.list"] = AccessRights.Read,                 // View file version history
        ["driveitem.versions.restore"] = AccessRights.Write,             // Restore previous version

        // ========================================================================
        // DRIVEITEM ADVANCED OPERATIONS
        // ========================================================================
        ["driveitem.search"] = AccessRights.Read,                        // Search for items
        ["driveitem.delta"] = AccessRights.Read,                         // Track changes (sync)
        ["driveitem.follow"] = AccessRights.Read,                        // Follow item for updates
        ["driveitem.unfollow"] = AccessRights.Read,                      // Unfollow item
        ["driveitem.thumbnails.get"] = AccessRights.Read,                // Get thumbnail images
        ["driveitem.createuploadsession"] = AccessRights.Write | AccessRights.Create,  // Large file upload

        // ========================================================================
        // DRIVEITEM COMPLIANCE & GOVERNANCE
        // ========================================================================
        ["driveitem.sensitivitylabel.get"] = AccessRights.Read,          // Get sensitivity label
        ["driveitem.sensitivitylabel.assign"] = AccessRights.Write,      // Assign sensitivity label
        ["driveitem.retentionlabel.get"] = AccessRights.Read,            // Get retention label
        ["driveitem.retentionlabel.set"] = AccessRights.Write,           // Set retention label
        ["driveitem.lock"] = AccessRights.Write,                         // Lock as record
        ["driveitem.unlock"] = AccessRights.Write,                       // Unlock record

        // ========================================================================
        // DRIVEITEM COLLABORATION
        // ========================================================================
        ["driveitem.checkin"] = AccessRights.Write,                      // Check in file
        ["driveitem.checkout"] = AccessRights.Write,                     // Check out file

        // ========================================================================
        // CONTAINER CRUD OPERATIONS
        // ========================================================================
        ["container.list"] = AccessRights.Read,                          // List all containers
        ["container.create"] = AccessRights.Create | AccessRights.Write, // Create new container
        ["container.get"] = AccessRights.Read,                           // Get container details
        ["container.update"] = AccessRights.Write,                       // Update container properties
        ["container.delete"] = AccessRights.Delete,                      // Soft delete container

        // ========================================================================
        // CONTAINER LIFECYCLE
        // ========================================================================
        ["container.activate"] = AccessRights.Write,                     // Activate container
        ["container.restore"] = AccessRights.Write | AccessRights.Create,// Restore deleted container
        ["container.permanentdelete"] = AccessRights.Delete,             // Permanently delete container
        ["container.lock"] = AccessRights.Write,                         // Lock container
        ["container.unlock"] = AccessRights.Write,                       // Unlock container

        // ========================================================================
        // CONTAINER PERMISSIONS
        // ========================================================================
        ["container.permissions.list"] = AccessRights.Read,              // List container permissions
        ["container.permissions.add"] = AccessRights.Share,              // Add container permissions
        ["container.permissions.update"] = AccessRights.Share,           // Update container permissions
        ["container.permissions.delete"] = AccessRights.Share,           // Delete container permissions

        // ========================================================================
        // CONTAINER CUSTOM PROPERTIES
        // ========================================================================
        ["container.customproperties.list"] = AccessRights.Read,         // List custom properties
        ["container.customproperties.add"] = AccessRights.Write,         // Add custom property
        ["container.customproperties.update"] = AccessRights.Write,      // Update custom property
        ["container.customproperties.delete"] = AccessRights.Write,      // Delete custom property

        // ========================================================================
        // CONTAINER ADDITIONAL OPERATIONS
        // ========================================================================
        ["container.drive.get"] = AccessRights.Read,                     // Get associated drive
        ["container.columns.list"] = AccessRights.Read,                  // List columns (metadata schema)
        ["container.recyclebin.settings.update"] = AccessRights.Write,   // Update recycle bin settings
        ["container.recyclebin.items.list"] = AccessRights.Read,         // List recycle bin items
        ["container.recyclebin.items.restore"] = AccessRights.Write,     // Restore from recycle bin
        ["container.recyclebin.items.delete"] = AccessRights.Delete,     // Delete from recycle bin

        // ========================================================================
        // LEGACY/COMPATIBILITY OPERATIONS (Business-level names)
        // These map to the new Graph API operations above but use friendlier names
        // ========================================================================
        ["preview_file"] = AccessRights.Read,                            // → driveitem.preview
        ["download_file"] = AccessRights.Write,                          // → driveitem.content.download
        ["upload_file"] = AccessRights.Write | AccessRights.Create,      // → driveitem.content.upload
        ["replace_file"] = AccessRights.Write,                           // → driveitem.content.replace
        ["delete_file"] = AccessRights.Delete,                           // → driveitem.delete
        ["read_metadata"] = AccessRights.Read,                           // → driveitem.get
        ["update_metadata"] = AccessRights.Write,                        // → driveitem.update
        ["share_document"] = AccessRights.Share,                         // → driveitem.createlink
        ["create_container"] = AccessRights.Create | AccessRights.Write, // → container.create
        ["delete_container"] = AccessRights.Delete                       // → container.delete
    };

    /// <summary>
    /// Gets the required AccessRights for a given SPE/Graph API operation.
    /// </summary>
    /// <param name="operation">Operation name (e.g., "driveitem.content.download", "container.create")</param>
    /// <returns>Required AccessRights flags</returns>
    /// <exception cref="ArgumentException">Thrown if operation is unknown</exception>
    public static AccessRights GetRequiredRights(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation, nameof(operation));

        if (_operationRequirements.TryGetValue(operation, out var rights))
        {
            return rights;
        }

        throw new ArgumentException($"Unknown operation '{operation}'. Valid operations: {string.Join(", ", _operationRequirements.Keys)}", nameof(operation));
    }

    /// <summary>
    /// Checks if a user has the required rights for an operation.
    /// Uses bitwise AND to check if all required flags are present.
    /// </summary>
    /// <param name="userRights">User's current AccessRights (from AccessSnapshot)</param>
    /// <param name="operation">Operation to check (e.g., "driveitem.content.download")</param>
    /// <returns>True if user has all required rights; false otherwise</returns>
    /// <example>
    /// <code>
    /// // User with Read only
    /// var userRights = AccessRights.Read;
    /// HasRequiredRights(userRights, "driveitem.preview");           // → true (Read is sufficient)
    /// HasRequiredRights(userRights, "driveitem.content.download");  // → false (needs Write)
    ///
    /// // User with Read + Write
    /// var userRights = AccessRights.Read | AccessRights.Write;
    /// HasRequiredRights(userRights, "driveitem.content.download");  // → true (has Write)
    /// HasRequiredRights(userRights, "driveitem.delete");            // → false (needs Delete)
    ///
    /// // User with Write + Create
    /// var userRights = AccessRights.Write | AccessRights.Create;
    /// HasRequiredRights(userRights, "driveitem.content.upload");    // → true (has both)
    /// </code>
    /// </example>
    public static bool HasRequiredRights(AccessRights userRights, string operation)
    {
        var required = GetRequiredRights(operation);

        // Bitwise AND check: (userRights & required) == required
        // This ensures ALL required flags are present in userRights
        return (userRights & required) == required;
    }

    /// <summary>
    /// Gets the missing AccessRights needed for an operation.
    /// Useful for generating detailed error messages.
    /// </summary>
    /// <param name="userRights">User's current AccessRights</param>
    /// <param name="operation">Operation to check</param>
    /// <returns>AccessRights flags that are missing; None if user has all required rights</returns>
    /// <example>
    /// <code>
    /// var userRights = AccessRights.Read;
    /// var missing = GetMissingRights(userRights, "driveitem.content.download");
    /// // → AccessRights.Write (user has Read but needs Write)
    ///
    /// var missing2 = GetMissingRights(userRights, "driveitem.content.upload");
    /// // → AccessRights.Write | AccessRights.Create (missing both)
    /// </code>
    /// </example>
    public static AccessRights GetMissingRights(AccessRights userRights, string operation)
    {
        var required = GetRequiredRights(operation);

        // XOR to find what's required but not present
        // Then AND with required to isolate only the missing required rights
        return required & ~userRights;
    }

    /// <summary>
    /// Gets all supported operations grouped by category.
    /// </summary>
    /// <returns>Dictionary of category → operation names</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GetOperationsByCategory()
    {
        return new Dictionary<string, IReadOnlyList<string>>
        {
            ["DriveItem - Metadata"] = _operationRequirements.Keys.Where(k => k.StartsWith("driveitem.") && (k.Contains(".get") || k.Contains(".update") || k.Contains(".list.children"))).ToList(),
            ["DriveItem - Content"] = _operationRequirements.Keys.Where(k => k.Contains(".content.") || k.Contains(".preview")).ToList(),
            ["DriveItem - File Management"] = _operationRequirements.Keys.Where(k => k.Contains(".create.") || k.Contains(".move") || k.Contains(".copy") || k.Contains(".delete")).ToList(),
            ["DriveItem - Sharing"] = _operationRequirements.Keys.Where(k => k.Contains(".createlink") || k.Contains(".permissions.")).ToList(),
            ["DriveItem - Versioning"] = _operationRequirements.Keys.Where(k => k.Contains(".versions.")).ToList(),
            ["DriveItem - Advanced"] = _operationRequirements.Keys.Where(k => k.Contains(".search") || k.Contains(".delta") || k.Contains(".follow") || k.Contains(".thumbnails") || k.Contains(".uploadsession")).ToList(),
            ["DriveItem - Compliance"] = _operationRequirements.Keys.Where(k => k.Contains("label") || k.Contains(".lock") || k.Contains(".unlock")).ToList(),
            ["DriveItem - Collaboration"] = _operationRequirements.Keys.Where(k => k.Contains(".checkin") || k.Contains(".checkout")).ToList(),
            ["Container - CRUD"] = _operationRequirements.Keys.Where(k => k.StartsWith("container.") && !k.Contains(".") || (k.Contains(".list") || k.Contains(".create") || k.Contains(".get") || k.Contains(".update") || k.Contains(".delete")) && !k.Contains("permissions") && !k.Contains("custom") && !k.Contains("recycle")).ToList(),
            ["Container - Lifecycle"] = _operationRequirements.Keys.Where(k => k.Contains(".activate") || k.Contains(".restore") || k.Contains(".permanentdelete") || k.Contains(".lock") || k.Contains(".unlock")).ToList(),
            ["Container - Permissions"] = _operationRequirements.Keys.Where(k => k.StartsWith("container.permissions.")).ToList(),
            ["Container - Custom Properties"] = _operationRequirements.Keys.Where(k => k.StartsWith("container.customproperties.")).ToList(),
            ["Container - Additional"] = _operationRequirements.Keys.Where(k => k.Contains(".drive.") || k.Contains(".columns.") || k.Contains(".recyclebin.")).ToList(),
            ["Legacy/Compatibility"] = _operationRequirements.Keys.Where(k => !k.Contains(".")).ToList()
        };
    }

    /// <summary>
    /// Gets all supported operations.
    /// </summary>
    /// <returns>Read-only collection of supported operation names</returns>
    public static IReadOnlyCollection<string> GetSupportedOperations()
    {
        return _operationRequirements.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks if an operation is supported.
    /// </summary>
    /// <param name="operation">Operation name to check</param>
    /// <returns>True if operation is defined; false otherwise</returns>
    public static bool IsOperationSupported(string operation)
    {
        return _operationRequirements.ContainsKey(operation);
    }

    /// <summary>
    /// Gets a human-readable description of the security requirement for an operation.
    /// </summary>
    /// <param name="operation">Operation name</param>
    /// <returns>Human-readable description (e.g., "Read", "Write and Create")</returns>
    public static string GetRequirementDescription(string operation)
    {
        var required = GetRequiredRights(operation);
        return GetAccessRightsDescription(required);
    }

    /// <summary>
    /// Converts AccessRights flags to human-readable string.
    /// </summary>
    private static string GetAccessRightsDescription(AccessRights rights)
    {
        if (rights == AccessRights.None)
        {
            return "None";
        }

        var parts = new List<string>();

        if (rights.HasFlag(AccessRights.Read)) parts.Add("Read");
        if (rights.HasFlag(AccessRights.Write)) parts.Add("Write");
        if (rights.HasFlag(AccessRights.Delete)) parts.Add("Delete");
        if (rights.HasFlag(AccessRights.Create)) parts.Add("Create");
        if (rights.HasFlag(AccessRights.Append)) parts.Add("Append");
        if (rights.HasFlag(AccessRights.AppendTo)) parts.Add("AppendTo");
        if (rights.HasFlag(AccessRights.Share)) parts.Add("Share");

        return string.Join(", ", parts);
    }
}
