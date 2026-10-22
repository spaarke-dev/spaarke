namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Result of an on-demand archive of an existing communication (POST /api/communications/{id}/archive,
/// task 044). The <c>.eml</c> Document + per-attachment Documents already get created automatically on
/// send/receive; this endpoint exposes the same archival on demand (e.g. for a draft, or when
/// outbound opt-in was disabled). Idempotent: a communication already archived returns
/// <see cref="AlreadyArchived"/> = true without creating duplicates.
/// </summary>
public sealed record ArchiveCommunicationResult
{
    /// <summary>The communication that was archived.</summary>
    public required Guid CommunicationId { get; init; }

    /// <summary>The sprk_document record for the archived .eml (null only if archival was skipped).</summary>
    public Guid? ArchiveDocumentId { get; init; }

    /// <summary>True when an email-archive Document already existed — no new archival was performed.</summary>
    public required bool AlreadyArchived { get; init; }

    /// <summary>Number of per-attachment sprk_document records created in this call.</summary>
    public required int AttachmentDocumentsCreated { get; init; }

    /// <summary>
    /// #2246: attachments NOT archived because their SharePoint Embedded item could not be verified as fit to bind to a
    /// new document of this communication. Logged per attachment with its reason code:
    /// <c>sdap.communication.attachment_item_unverified</c> (not verified as this communication's item in its container),
    /// or <c>sdap.communication.attachment_item_already_bound</c> (a document already names the item, PR #2248 fix
    /// round 1). Zero when every candidate was verified. Clients show it as "N attachment(s) could not be verified and
    /// were not saved."
    /// </summary>
    public int AttachmentsRefusedUnverified { get; init; }
}
