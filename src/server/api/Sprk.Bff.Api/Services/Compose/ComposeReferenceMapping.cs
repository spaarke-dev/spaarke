using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 8 of the <c>ComposeService</c> decomposition (task 070): the projection coordinate
/// system — resolving a <c>w14:paraId</c> from a document-order paragraph index, and projecting the
/// Load-time paraId map onto the session-ledger shape.
///
/// <para><b>A static helper class, not a constructed collaborator.</b> These are pure functions over
/// their arguments with no instance state and no dependencies, so giving them a constructor and a
/// field on <c>ComposeService</c> would add ceremony without adding anything. The seam map offered a
/// partial-class split as the fallback for exactly this shape; a static class says the same thing
/// with a name attached.</para>
///
/// <para><b>One grouping worth revisiting.</b> <see cref="IsSameCrossVersionBinding"/> travels here
/// because the seam map lists it in cluster 8, and the map's groupings are being followed rather
/// than re-litigated mid-extraction. But it is honestly a different reason-to-change from its two
/// neighbours: they are about the paraId coordinate system, while it is about which prior session a
/// Load may resume. If a later cluster grows a natural home for session-resume matching (FR-29 /
/// FR-33 live in <c>LoadAsync</c>), this is the first thing that should move there. Recorded rather
/// than acted on, so the decision is visible instead of silently baked in.</para>
///
/// <para>Behaviour is unchanged from the methods it replaces; this is a move, not a rewrite.</para>
/// </summary>
internal static class ComposeReferenceMapping
{
    /// <summary>
    /// FR-24 (task 050): resolves the E2 <c>w14:paraId</c> for a recovered revision's document-order
    /// paragraph index (<see cref="RecoveredRevision.ParagraphHint"/>) from the Load-time
    /// <paramref name="paraIdMap"/>. Both the reader and <see cref="ParaIdPreParser"/> enumerate
    /// <c>body.Descendants&lt;Paragraph&gt;()</c> (recursive, document-ordered, incl. table-cell +
    /// nested-table paragraphs), so the hint index maps directly to <see cref="ParaIdMapEntry.Index"/>.
    /// Returns <c>null</c> when the hint is out of range (e.g. <c>-1</c> for a revision whose paragraph
    /// could not be located) — the client then falls back to fuzzy anchoring (anchorText + hint).
    /// </summary>
    internal static string? ResolveParaIdForHint(IReadOnlyList<ParaIdMapEntry> paraIdMap, int paragraphHint)
    {
        if (paragraphHint < 0)
        {
            return null;
        }

        foreach (var entry in paraIdMap)
        {
            if (entry.Index == paragraphHint)
            {
                return entry.ParaId;
            }
        }

        return null;
    }

    /// <summary>
    /// FR-17 (task 041, design.md §4 WS-4): projects a Load-time <see cref="ParaIdMapEntry"/> map
    /// (task 040's per-paragraph reference set) onto the session-ledger shape
    /// (<see cref="ParaReferenceMapEntry"/>) for persistence on <see cref="ChatSession.ReferenceMap"/>.
    /// Pure 1:1 field carry — no recomputation, no numbering logic: this method persists task 040's
    /// ALREADY-computed values, it does not derive new ones (ADR-013/007 purity: <c>Services/
    /// Compose/</c> stays <c>byte[]</c>-in/projection-out, no AI-internal type, no <c>Microsoft.Graph</c>
    /// above <c>SpeFileStore</c>).
    /// </summary>
    internal static IReadOnlyList<ParaReferenceMapEntry> BuildReferenceMap(IReadOnlyList<ParaIdMapEntry> paraIdMap)
    {
        if (paraIdMap.Count == 0)
        {
            return Array.Empty<ParaReferenceMapEntry>();
        }

        var entries = new List<ParaReferenceMapEntry>(paraIdMap.Count);
        foreach (var entry in paraIdMap)
        {
            entries.Add(new ParaReferenceMapEntry(
                ParaId: entry.ParaId,
                ComputedNumber: entry.ComputedNumber,
                NumberingLevel: entry.NumberingLevel,
                ListPath: entry.ListPath,
                HeadingLevel: entry.HeadingLevel));
        }

        return entries;
    }

    /// <summary>
    /// FR-33 (design.md §8) cross-version session-binding predicate: a resumed session must
    /// match the SAME <c>DocumentId</c> binding (<paramref name="bindingId"/> — version
    /// independent by construction; see <c>ComposeService.LoadAsync</c> remarks) AND, when the caller
    /// supplies a <paramref name="matterId"/>, the SAME Matter — read from the candidate
    /// session's <see cref="ChatHostContext"/> (canonical <c>EntityType == "matter"</c> per
    /// <see cref="Models.Ai.Chat.EntityTypeNormalizer"/>, <c>EntityId == matterId</c>). A
    /// <c>null</c>/whitespace <paramref name="matterId"/> preserves the FR-29 DocumentId-only
    /// match (backward compatible with callers that predate FR-33). This augments the EXISTING
    /// caller-supplied-SessionId resume path — no new lookup index, no parallel session cache
    /// (ADR-040).
    /// </summary>
    internal static bool IsSameCrossVersionBinding(ChatSession candidate, string bindingId, string? matterId)
    {
        if (!string.Equals(candidate.DocumentId, bindingId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(matterId))
        {
            return true;
        }

        return candidate.HostContext is { } hostContext
            && string.Equals(hostContext.EntityType, ParentEntityContext.EntityTypes.Matter, StringComparison.Ordinal)
            && string.Equals(hostContext.EntityId, matterId, StringComparison.Ordinal);
    }
}
