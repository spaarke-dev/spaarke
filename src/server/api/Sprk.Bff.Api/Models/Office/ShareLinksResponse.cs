using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Response model for the share links endpoint.
/// Corresponds to POST /office/share/links response.
/// </summary>
/// <remarks>
/// <para>
/// Returns an array of shareable document links along with any external invitations
/// created. Links resolve through Spaarke access controls and can be inserted into
/// emails via the Outlook compose mode add-in.
/// </para>
/// <para>
/// Supports partial success scenarios where some documents may not be accessible.
/// Failed documents are returned in the Errors array.
/// </para>
/// </remarks>
public record ShareLinksResponse
{
    /// <summary>
    /// Generated share links for accessible documents.
    /// </summary>
    public required IReadOnlyList<DocumentLink> Links { get; init; }

    /// <summary>
    /// Invitations created for external recipients (SDAP-external-portal integration).
    /// </summary>
    /// <remarks>
    /// Only populated when grantAccess is true and recipients include external emails.
    /// </remarks>
    public IReadOnlyList<ShareInvitation>? Invitations { get; init; }

    /// <summary>
    /// Documents that could not be shared (partial success scenario).
    /// </summary>
    public IReadOnlyList<ShareLinkError>? Errors { get; init; }

    /// <summary>
    /// Whether all requested documents were successfully processed.
    /// </summary>
    public bool AllSucceeded => Errors == null || Errors.Count == 0;

    /// <summary>
    /// Correlation ID for tracking and support.
    /// </summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// A shareable link to a document.
/// </summary>
/// <remarks>
/// Links resolve through Spaarke access controls. The URL format is:
/// https://spaarke.app/doc/{documentId} (configurable via ShareLinkBaseUrl).
/// </remarks>
public record DocumentLink
{
    /// <summary>
    /// Document ID (sprk_document.sprk_documentid).
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Shareable URL that resolves through Spaarke access controls.
    /// </summary>
    /// <example>https://spaarke.app/doc/123e4567-e89b-12d3-a456-426614174000</example>
    public required string Url { get; init; }

    /// <summary>
    /// Document title for UI display.
    /// </summary>
    /// <remarks>
    /// Usually the document name (sprk_documentname) or filename (sprk_filename).
    /// </remarks>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Original filename with extension.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// MIME content type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long? Size { get; init; }

    /// <summary>
    /// Icon URL for the document type.
    /// </summary>
    public string? IconUrl { get; init; }
}

/// <summary>
/// External sharing invitation created for a recipient.
/// </summary>
/// <remarks>
/// Invitations are created when grantAccess is true and the recipient is external.
/// These integrate with SDAP-external-portal for actual access provisioning.
/// </remarks>
public record ShareInvitation
{
    /// <summary>
    /// Recipient email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Invitation status.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required InvitationStatus Status { get; init; }

    /// <summary>
    /// Invitation record ID (if created).
    /// </summary>
    public Guid? InvitationId { get; init; }

    /// <summary>
    /// Error message if invitation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Status of an external sharing invitation.
/// </summary>
public enum InvitationStatus
{
    /// <summary>
    /// Invitation was created successfully.
    /// </summary>
    Created,

    /// <summary>
    /// Invitation already exists for this recipient.
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// User already has access (internal user).
    /// </summary>
    AlreadyHasAccess,

    /// <summary>
    /// Failed to create invitation.
    /// </summary>
    Failed
}

/// <summary>
/// Error information for a document that could not be shared.
/// </summary>
public record ShareLinkError
{
    /// <summary>
    /// Document ID that failed.
    /// </summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    /// <remarks>
    /// Codes follow OFFICE_XXX format per ADR-019:
    /// - OFFICE_007: Document not found
    /// - OFFICE_009: Access denied (user lacks share permission)
    /// - OFFICE_014: Dataverse error
    /// </remarks>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; init; }
}
