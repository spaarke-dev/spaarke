using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for creating or updating a playbook.
/// </summary>
public record SavePlaybookRequest
{
    /// <summary>
    /// Playbook name.
    /// </summary>
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Playbook description.
    /// </summary>
    [StringLength(4000)]
    public string? Description { get; init; }

    /// <summary>
    /// Output type for analysis results.
    /// References sprk_aioutputtype entity.
    /// </summary>
    public Guid? OutputTypeId { get; init; }

    /// <summary>
    /// Whether the playbook is public (shared) or private.
    /// </summary>
    public bool IsPublic { get; init; } = false;

    /// <summary>
    /// Whether this playbook is a template that can be cloned.
    /// Templates are managed by administrators and provide standard starting points.
    /// </summary>
    public bool IsTemplate { get; init; } = false;

    /// <summary>
    /// Action IDs to associate with this playbook.
    /// N:N relationship with sprk_analysisaction entity.
    /// </summary>
    public Guid[]? ActionIds { get; init; }

    /// <summary>
    /// Skill IDs to associate with this playbook.
    /// N:N relationship with sprk_analysisskill entity.
    /// </summary>
    public Guid[]? SkillIds { get; init; }

    /// <summary>
    /// Knowledge source IDs to associate with this playbook.
    /// N:N relationship with sprk_analysisknowledge entity.
    /// </summary>
    public Guid[]? KnowledgeIds { get; init; }

    /// <summary>
    /// Tool IDs to associate with this playbook.
    /// N:N relationship with sprk_analysistool entity.
    /// </summary>
    public Guid[]? ToolIds { get; init; }
}

/// <summary>
/// Response model for playbook operations.
/// </summary>
public record PlaybookResponse
{
    /// <summary>
    /// Playbook ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Playbook name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Playbook description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Output type ID.
    /// </summary>
    public Guid? OutputTypeId { get; init; }

    /// <summary>
    /// Whether the playbook is public.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// Whether this playbook is a template.
    /// </summary>
    public bool IsTemplate { get; init; }

    /// <summary>
    /// Owner user ID.
    /// </summary>
    public Guid OwnerId { get; init; }

    /// <summary>
    /// Associated action IDs.
    /// </summary>
    public Guid[] ActionIds { get; init; } = [];

    /// <summary>
    /// Associated skill IDs.
    /// </summary>
    public Guid[] SkillIds { get; init; } = [];

    /// <summary>
    /// Associated knowledge IDs.
    /// </summary>
    public Guid[] KnowledgeIds { get; init; } = [];

    /// <summary>
    /// Associated tool IDs.
    /// </summary>
    public Guid[] ToolIds { get; init; } = [];

    /// <summary>
    /// Record creation timestamp.
    /// </summary>
    public DateTime CreatedOn { get; init; }

    /// <summary>
    /// Record modification timestamp.
    /// </summary>
    public DateTime ModifiedOn { get; init; }
}

/// <summary>
/// Validation result for playbook configuration.
/// </summary>
public record PlaybookValidationResult
{
    /// <summary>
    /// Whether the playbook configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors if any.
    /// </summary>
    public string[] Errors { get; init; } = [];

    /// <summary>
    /// Validation warnings (non-blocking).
    /// </summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>
    /// Create a successful validation result.
    /// </summary>
    public static PlaybookValidationResult Success(string[]? warnings = null) =>
        new() { IsValid = true, Warnings = warnings ?? [] };

    /// <summary>
    /// Create a failed validation result with errors.
    /// </summary>
    public static PlaybookValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Query parameters for listing playbooks.
/// </summary>
public record PlaybookQueryParameters
{
    /// <summary>
    /// Page number (1-based). Default is 1.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Page size. Default is 20, max is 100.
    /// </summary>
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Filter by name (contains search).
    /// </summary>
    public string? NameFilter { get; init; }

    /// <summary>
    /// Filter by output type ID.
    /// </summary>
    public Guid? OutputTypeId { get; init; }

    /// <summary>
    /// Include only public playbooks (for public endpoint).
    /// </summary>
    public bool PublicOnly { get; init; } = false;

    /// <summary>
    /// Sort field. Default is "modifiedon".
    /// </summary>
    public string SortBy { get; init; } = "modifiedon";

    /// <summary>
    /// Sort direction. Default is descending.
    /// </summary>
    public bool SortDescending { get; init; } = true;

    /// <summary>
    /// Normalize page size to valid range (1-100).
    /// </summary>
    public int GetNormalizedPageSize() => Math.Clamp(PageSize, 1, 100);

    /// <summary>
    /// Get skip count for pagination.
    /// </summary>
    public int GetSkip() => (Math.Max(1, Page) - 1) * GetNormalizedPageSize();
}

/// <summary>
/// Summary response for playbook list items (without full relationship details).
/// </summary>
public record PlaybookSummary
{
    /// <summary>
    /// Playbook ID.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Playbook name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Playbook description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Output type ID.
    /// </summary>
    public Guid? OutputTypeId { get; init; }

    /// <summary>
    /// Whether the playbook is public.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// Whether this playbook is a template.
    /// </summary>
    public bool IsTemplate { get; init; }

    /// <summary>
    /// Owner user ID.
    /// </summary>
    public Guid OwnerId { get; init; }

    /// <summary>
    /// Record modification timestamp.
    /// </summary>
    public DateTime ModifiedOn { get; init; }
}

/// <summary>
/// Paginated list response for playbooks.
/// </summary>
public record PlaybookListResponse
{
    /// <summary>
    /// List of playbooks.
    /// </summary>
    public PlaybookSummary[] Items { get; init; } = [];

    /// <summary>
    /// Total count of matching playbooks.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(1, PageSize));

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Request to clone a playbook.
/// </summary>
public record ClonePlaybookRequest
{
    /// <summary>
    /// Optional new name for the cloned playbook.
    /// If not provided, defaults to "[SourceName] (Copy)".
    /// </summary>
    [StringLength(200)]
    public string? NewName { get; init; }
}

/// <summary>
/// Sharing level for playbooks.
/// </summary>
public enum SharingLevel
{
    /// <summary>Only the owner can access.</summary>
    Private = 0,

    /// <summary>Shared with specific teams.</summary>
    Team = 1,

    /// <summary>Available to all users in the organization.</summary>
    Organization = 2,

    /// <summary>Public template visible to all.</summary>
    Public = 3
}

/// <summary>
/// Access rights that can be granted when sharing.
/// </summary>
[Flags]
public enum PlaybookAccessRights
{
    /// <summary>No access.</summary>
    None = 0,

    /// <summary>Read access.</summary>
    Read = 1,

    /// <summary>Write access (includes read).</summary>
    Write = 2,

    /// <summary>Share access (can re-share).</summary>
    Share = 4,

    /// <summary>Full access (read, write, share).</summary>
    Full = Read | Write | Share
}

/// <summary>
/// Request to share a playbook with teams.
/// </summary>
public record SharePlaybookRequest
{
    /// <summary>
    /// Team IDs to share with.
    /// </summary>
    public Guid[] TeamIds { get; init; } = [];

    /// <summary>
    /// Access rights to grant.
    /// </summary>
    public PlaybookAccessRights AccessRights { get; init; } = PlaybookAccessRights.Read;

    /// <summary>
    /// Set the playbook as organization-wide.
    /// </summary>
    public bool OrganizationWide { get; init; } = false;
}

/// <summary>
/// Request to revoke sharing from a playbook.
/// </summary>
public record RevokeShareRequest
{
    /// <summary>
    /// Team IDs to revoke access from.
    /// </summary>
    public Guid[] TeamIds { get; init; } = [];

    /// <summary>
    /// Revoke organization-wide access.
    /// </summary>
    public bool RevokeOrganizationWide { get; init; } = false;
}

/// <summary>
/// Information about who a playbook is shared with.
/// </summary>
public record PlaybookSharingInfo
{
    /// <summary>
    /// Playbook ID.
    /// </summary>
    public Guid PlaybookId { get; init; }

    /// <summary>
    /// Current sharing level.
    /// </summary>
    public SharingLevel SharingLevel { get; init; }

    /// <summary>
    /// Whether the playbook is organization-wide.
    /// </summary>
    public bool IsOrganizationWide { get; init; }

    /// <summary>
    /// Whether the playbook is public.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>
    /// Teams the playbook is shared with.
    /// </summary>
    public SharedWithTeam[] SharedWithTeams { get; init; } = [];
}

/// <summary>
/// Team sharing information.
/// </summary>
public record SharedWithTeam
{
    /// <summary>
    /// Team ID.
    /// </summary>
    public Guid TeamId { get; init; }

    /// <summary>
    /// Team name.
    /// </summary>
    public string TeamName { get; init; } = string.Empty;

    /// <summary>
    /// Access rights granted.
    /// </summary>
    public PlaybookAccessRights AccessRights { get; init; }

    /// <summary>
    /// When sharing was granted.
    /// </summary>
    public DateTime SharedOn { get; init; }
}

/// <summary>
/// Result of a sharing operation.
/// </summary>
public record ShareOperationResult
{
    /// <summary>
    /// Whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Updated sharing info.
    /// </summary>
    public PlaybookSharingInfo? SharingInfo { get; init; }

    /// <summary>
    /// Create a successful result.
    /// </summary>
    public static ShareOperationResult Succeeded(PlaybookSharingInfo info) =>
        new() { Success = true, SharingInfo = info };

    /// <summary>
    /// Create a failed result.
    /// </summary>
    public static ShareOperationResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}
