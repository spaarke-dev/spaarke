using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for inbound attachment-text extraction used as an Association Engine match signal
/// (email-r4 Phase 2, owner spec 2026-07-18). Bound from the <c>Communication:AttachmentMatch</c> section.
/// </summary>
/// <remarks>
/// When enabled, the inbound processor extracts plain text (via <c>ITextExtractor</c>: PDF/DOCX/TXT/…) from a
/// bounded set of the message's attachments and sets it on <c>NormalizedMessage.AttachmentText</c> BEFORE the
/// association rungs run — so an email whose matter/project name appears only in the attachment still matches.
/// Extraction is best-effort/non-fatal and bounded (attachment count, per-attachment size, total chars) to
/// contain cost on the inbound path.
/// </remarks>
public class AttachmentMatchOptions
{
    public const string SectionName = "Communication:AttachmentMatch";

    /// <summary>Operational kill-switch. <c>true</c> (default) extracts attachment text for matching; <c>false</c> skips it (no redeploy).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max attachments to extract text from per message (1–20, default 5). Guards inbound latency/cost.</summary>
    [Range(1, 20)]
    public int MaxAttachments { get; set; } = 5;

    /// <summary>Max size (bytes) of an attachment to attempt extraction on (default 10 MB). Larger attachments are skipped.</summary>
    [Range(1024, 104857600)]
    public long MaxAttachmentSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Max total characters of extracted attachment text retained for matching (256–20000, default 4000).</summary>
    [Range(256, 20000)]
    public int MaxTotalChars { get; set; } = 4000;
}
