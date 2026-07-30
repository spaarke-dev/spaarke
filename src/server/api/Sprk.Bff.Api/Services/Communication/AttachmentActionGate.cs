using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Pure, side-effect-free gate logic for attachment-grounded action extraction (email-communication-
/// intelligence-r1 task 041 / FR-13). Two responsibilities, both deterministic and independently testable
/// (mirrors the <see cref="CitationVerifier"/> precedent — orchestration lives in
/// <see cref="CommunicationEnrichmentService"/>, the trust-critical decisions live here):
/// <list type="number">
/// <item><see cref="IsLikelyActionTrigger"/> — the NFR-08 COST GATE: does an attachment's extracted text
/// carry any action-trigger signal? Only flagged attachments earn an LLM extraction pass.</item>
/// <item><see cref="VerifyAgainstAttachment"/> — the NFR-06 MACHINE-VERIFIED LOCATOR gate: is the cited span
/// verbatim-present in THIS attachment, and on which page? The page is CODE-DERIVED (located), never
/// model-asserted.</item>
/// </list>
/// </summary>
public static class AttachmentActionGate
{
    /// <summary>
    /// Deterministic action-trigger signals for the LLM cost gate (NFR-08). An attachment whose extracted
    /// text contains NONE of these is NOT sent for an LLM extraction pass. Lower-cased substring match;
    /// intentionally broad-but-bounded — a false-positive costs one skippable completion, a false-negative
    /// silently misses an action, so the gate errs toward inclusion while still skipping obviously-inert
    /// attachments (e.g. a logo or a read-only reference PDF with no imperative language).
    /// </summary>
    public static readonly IReadOnlyList<string> TriggerKeywords = new[]
    {
        "please ", "kindly ", "action required", "action item", "follow up", "follow-up",
        "deadline", "due date", "due by", "by no later", "no later than", "as soon as",
        "must ", "required to", "requested to", "need to", "respond", "reply by", "return ",
        "submit", "sign ", "signature", "countersign", "execute", "complete by", "review by",
        "confirm by", "provide by", "deliver by", "remit", "pay by", "payment due", "invoice",
        "schedule", "appointment", "hearing", "file by", "filing deadline", "expire", "expiration",
    };

    /// <summary>Cost-gate pre-filter (NFR-08): true when the text contains any action-trigger signal in
    /// <see cref="TriggerKeywords"/>. Attachments returning false are NOT sent for an LLM extraction pass.</summary>
    public static bool IsLikelyActionTrigger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var haystack = text.ToLowerInvariant();
        foreach (var keyword in TriggerKeywords)
        {
            if (haystack.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Machine-verified, code-derived locator gate (NFR-06). Returns <c>(Present, Page)</c>:
    /// <list type="bullet">
    /// <item><c>Present</c> is true ONLY when <paramref name="quotedText"/> is verbatim-locatable in this
    /// attachment's <see cref="AttachmentExtractedText.FullText"/> — an ATTACHMENT-SCOPED check, strictly
    /// stronger than the shipped merged subject+body+attachment <see cref="CitationVerifier"/> check (a span
    /// that only appears in the email body, not this attachment, is rejected).</item>
    /// <item><c>Page</c> is the 1-based number of the page whose text contains the span, or <c>null</c> when
    /// the span straddles a page boundary or the attachment has no page structure (still attachment-verified).
    /// The page is DERIVED by locating the span — never asserted by the model.</item>
    /// </list>
    /// Reuses <see cref="CitationVerifier"/>'s whitespace/case normalization for consistency with the shipped
    /// verify-cited-text gate.
    /// </summary>
    public static (bool Present, int? Page) VerifyAgainstAttachment(AttachmentExtractedText attachment, string? quotedText)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        if (!CitationVerifier.IsCitedTextPresent(attachment.FullText, quotedText))
        {
            return (false, null);
        }

        foreach (var page in attachment.Pages)
        {
            if (CitationVerifier.IsCitedTextPresent(page.Text, quotedText))
            {
                return (true, page.PageNumber);
            }
        }

        return (true, null);
    }
}
