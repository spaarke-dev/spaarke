namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// Response DTO for <c>GET /api/communications/{id}/attachments/text</c>
/// (email-communication-intelligence-r2, reconciliation reader parity Batch 2 / B2.1).
///
/// The reconciliation browse reader folds each file attachment's EXTRACTED TEXT into the
/// same normalized surface as the email body (NFR-11 — one reader). The extracted text is a
/// transient server-side pipeline artifact never persisted on <c>sprk_communicationattachment</c>,
/// so this endpoint RE-EXTRACTS on demand from SharePoint Embedded via the shared
/// <c>ITextExtractor</c> (whose Redis cache, ADR-009, makes the re-extraction cheap after the
/// first hit). One item per file attachment that resolves to a governed <c>sprk_document</c>.
/// </summary>
public sealed record CommunicationAttachmentTextResponse
{
    /// <summary>Per-attachment extracted-text results (empty when the communication has no file attachments).</summary>
    public required IReadOnlyList<CommunicationAttachmentTextItem> Attachments { get; init; }
}

/// <summary>
/// One file attachment's extracted-text result. The client merges <see cref="Text"/> /
/// <see cref="Extractable"/> into its already-resolved reader folds by <see cref="AttachmentId"/>
/// (falling back to <see cref="DocumentId"/>).
/// </summary>
public sealed record CommunicationAttachmentTextItem
{
    /// <summary>The <c>sprk_communicationattachment</c> row id (the reader fold's join key).</summary>
    public required Guid AttachmentId { get; init; }

    /// <summary>The linked <c>sprk_document</c> id (secondary join key; null when the link is missing).</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>The attachment file name (extension drives extraction method + the reader fold heading).</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The extracted, normalized readable text — present only when <see cref="Extractable"/> is true.
    /// Null for image/scanned/unsupported/failed attachments (the reader then shows its
    /// "Content not available as text" note instead).
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// True when the attachment yielded readable text. False for image-only (vision-required),
    /// unsupported file types, a missing SPE reference, or an extraction failure — all rendered
    /// by the reader as the same non-fatal "not available as text" fold (never an error).
    /// </summary>
    public required bool Extractable { get; init; }

    /// <summary>The extraction method that produced the text (e.g. <c>Native</c>, <c>DocumentIntelligence</c>,
    /// <c>Email</c>) — diagnostic only; null when nothing was extracted.</summary>
    public string? Method { get; init; }
}
