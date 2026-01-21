using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Office;

/// <summary>
/// Request model for the POST /office/share/attach endpoint.
/// Returns document content for attaching to Outlook compose emails.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint supports the Office Add-in "Share from Spaarke" feature,
/// enabling users to attach document copies to their compose emails.
/// </para>
/// <para>
/// The documentIds array allows batch retrieval of multiple attachments.
/// Each document is validated for user access and size limits before packaging.
/// </para>
/// </remarks>
public record ShareAttachRequest
{
    /// <summary>
    /// Array of document IDs to retrieve for attachment.
    /// Each ID must be a valid GUID referencing a Spaarke document.
    /// </summary>
    /// <remarks>
    /// Documents are validated for:
    /// - Existence in Dataverse
    /// - User share permission via UAC
    /// - Size limits (25MB per file, 100MB total per spec NFR-03)
    /// </remarks>
    [Required]
    [MinLength(1, ErrorMessage = "At least one document ID is required")]
    public required Guid[] DocumentIds { get; init; }

    /// <summary>
    /// Optional: Attachment delivery mode.
    /// Defaults to Url for better performance with large files.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><term>Url</term><description>Returns pre-signed download URLs with short TTL (default)</description></item>
    /// <item><term>Base64</term><description>Returns base64-encoded content in response (small files only)</description></item>
    /// </list>
    /// </remarks>
    public AttachmentDeliveryMode DeliveryMode { get; init; } = AttachmentDeliveryMode.Url;
}

/// <summary>
/// Attachment delivery mode for the share/attach endpoint.
/// </summary>
public enum AttachmentDeliveryMode
{
    /// <summary>
    /// URL-based delivery (primary). Returns pre-signed download URLs.
    /// Used with Office.context.mailbox.item.addFileAttachmentAsync(url, filename).
    /// </summary>
    Url = 0,

    /// <summary>
    /// Base64-encoded delivery (fallback for small files).
    /// Used with Office.context.mailbox.item.addFileAttachmentFromBase64Async(base64, filename).
    /// </summary>
    Base64 = 1
}
