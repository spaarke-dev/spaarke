namespace Sprk.Bff.Api.Models.FieldMapping;

/// <summary>
/// DTO representing a field mapping profile from Dataverse.
/// </summary>
public record FieldMappingProfileDto
{
    /// <summary>
    /// Unique identifier of the field mapping profile.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Display name of the profile.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the source entity (e.g., "account", "contact", "sprk_matter").
    /// </summary>
    public string SourceEntity { get; init; } = string.Empty;

    /// <summary>
    /// Logical name of the target entity (e.g., "sprk_event").
    /// </summary>
    public string TargetEntity { get; init; } = string.Empty;

    /// <summary>
    /// Synchronization mode: OneTime, ManualRefresh, or UpdateRelated.
    /// </summary>
    public string SyncMode { get; init; } = string.Empty;

    /// <summary>
    /// Whether this profile is currently active.
    /// </summary>
    public bool IsActive { get; init; }
}

/// <summary>
/// Response model for listing field mapping profiles.
/// </summary>
public record FieldMappingProfileListResponse
{
    /// <summary>
    /// List of field mapping profiles.
    /// </summary>
    public FieldMappingProfileDto[] Items { get; init; } = [];

    /// <summary>
    /// Total count of matching profiles.
    /// </summary>
    public int TotalCount { get; init; }
}

/// <summary>
/// Query parameters for filtering field mapping profiles.
/// </summary>
public record FieldMappingProfileQueryParams
{
    /// <summary>
    /// Filter by source entity logical name.
    /// </summary>
    public string? SourceEntity { get; init; }

    /// <summary>
    /// Filter by target entity logical name.
    /// </summary>
    public string? TargetEntity { get; init; }

    /// <summary>
    /// Filter by active status. Defaults to true (only active profiles).
    /// </summary>
    public bool? IsActive { get; init; } = true;
}

/// <summary>
/// Sync mode values for field mapping profiles.
/// </summary>
public static class FieldMappingSyncMode
{
    /// <summary>
    /// Values are copied once when the target record is created.
    /// </summary>
    public const string OneTime = "OneTime";

    /// <summary>
    /// User can manually refresh values from parent (pull).
    /// </summary>
    public const string ManualRefresh = "ManualRefresh";

    /// <summary>
    /// Changes to related fields can be pushed to target records (push).
    /// </summary>
    public const string UpdateRelated = "UpdateRelated";

    /// <summary>
    /// All valid sync mode values.
    /// </summary>
    public static readonly string[] AllModes = [OneTime, ManualRefresh, UpdateRelated];
}
