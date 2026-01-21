using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for generating shareable links to documents.
/// Corresponds to POST /office/share/links endpoint.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint generates shareable URLs for selected documents that resolve
/// through Spaarke access controls. Links can be inserted into emails via
/// the Outlook compose mode add-in.
/// </para>
/// <para>
/// If grantAccess is true and recipients include external users, invitations
/// will be created for external sharing (SDAP-external-portal integration).
/// </para>
/// </remarks>
public record ShareLinksRequest
{
    /// <summary>
    /// Document IDs to generate share links for.
    /// </summary>
    /// <remarks>
    /// Limit: 50 documents per request.
    /// </remarks>
    [Required]
    [MinLength(1, ErrorMessage = "At least one document ID is required")]
    [MaxLength(50, ErrorMessage = "Maximum 50 documents per request")]
    public required IReadOnlyList<Guid> DocumentIds { get; init; }

    /// <summary>
    /// Optional list of recipient email addresses for access checking.
    /// </summary>
    /// <remarks>
    /// If provided and grantAccess is true, external recipients will have
    /// invitations created for them (SDAP-external-portal integration).
    /// </remarks>
    public IReadOnlyList<string>? Recipients { get; init; }

    /// <summary>
    /// Whether to grant access to recipients who don't currently have access.
    /// </summary>
    /// <remarks>
    /// When true:
    /// - Internal users: Access is granted immediately based on role
    /// - External users: Invitations are created (requires SDAP-external-portal)
    /// </remarks>
    public bool GrantAccess { get; init; }

    /// <summary>
    /// Access role to grant when grantAccess is true.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ShareLinkRole Role { get; init; } = ShareLinkRole.ViewOnly;

    /// <summary>
    /// Idempotency key to prevent duplicate processing.
    /// </summary>
    /// <remarks>
    /// Format: SHA256 hash of canonical request payload.
    /// When provided, duplicate requests within 24 hours return cached results.
    /// </remarks>
    [MaxLength(64)]
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// Access role for shared document links.
/// </summary>
public enum ShareLinkRole
{
    /// <summary>
    /// View only - user can view but not download.
    /// </summary>
    ViewOnly,

    /// <summary>
    /// Download - user can view and download.
    /// </summary>
    Download,

    /// <summary>
    /// Edit - user can view, download, and edit.
    /// </summary>
    Edit
}
