using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-12 (task 012) — the paraId SPLICE KEY. Builds a <c>w14:paraId → w:p</c> index over a document
/// body and resolves a set of edited-paragraph paraIds against it, so the E1 delta save
/// (<see cref="ComposeParagraphRedlineSynthesizer"/>, Option C — design §4.2) can map each edited editor
/// paragraph back to EXACTLY the original OOXML paragraph carrying the matching id — an edit to paragraph
/// P updates the one original paragraph whose paraId is P's, and an id that matches nothing is surfaced as
/// a handled miss rather than a silent no-op or a wrong write.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a shared key</b>: the redline synthesizer owns the OOXML surgery (rewrite each edited paragraph
/// in place as a word-diff redline of its old→new text); task 012 owns paraId IDENTITY — promoting it to
/// the primary anchor (<see cref="AnnotationReanchorService"/>, FR-11) AND to the splice key here (FR-12).
/// Both consume the SAME id. Factoring the index/resolution out as a pure helper means the synthesizer and
/// this task's FR-12 tests exercise ONE lookup, not two divergent copies. It is deliberately NOT a method
/// on <see cref="ComposeService"/> — that class is the SPE-lifecycle facade (SRP); a byte/OOXML lookup
/// keyed by paraId is a distinct, pure concern.
/// </para>
/// <para>
/// <b>Same walk as <see cref="ParaIdPreParser"/></b>: <c>Descendants&lt;Paragraph&gt;()</c> is recursive
/// and document-ordered — it already covers paragraphs inside table cells and nested tables (S1b), so a
/// table-cell paragraph is indexed and splices by its id with no special-case descent. Ids are compared
/// case-insensitively (upper-cased keys) so a Word-lowercased id still matches an anchor's canonical id.
/// </para>
/// <para>
/// <b>Pure — no I/O, no AI, no Graph</b>: operates only on the caller's already-open Open XML
/// <see cref="Body"/>. The SPE read/write hop stays behind the <c>SpeFileStore</c> facade in the caller
/// (ADR-007); this helper never sees a <c>Microsoft.Graph</c> type, an <c>IOpenAiClient</c>, or a routing
/// type (ADR-013 / NFR-05 — Tier-1 NetArchTest enforces). It does NOT mutate the body; the returned
/// <see cref="Paragraph"/> references are LIVE children of the caller's document, so the synthesizer
/// rewrites them in place within its own editable <c>using</c> scope.
/// </para>
/// </remarks>
public static class ComposeParaIdSpliceMap
{
    /// <summary>
    /// Builds the <c>w14:paraId → w:p</c> index over <paramref name="body"/> in document order (incl.
    /// table-cell + nested-table paragraphs). Paragraphs with no id are skipped — only id-bearing
    /// paragraphs are splice targets (task 010 ensures every body paragraph has one on Load). Keys are
    /// upper-cased for case-insensitive matching. A DUPLICATE id is a document-integrity violation
    /// (paraIds are unique within a document — task 010 guarantees it on Load) and throws rather than
    /// silently picking one occurrence, which would splice an edit into the wrong paragraph.
    /// </summary>
    /// <param name="body">The document body to index (the caller's open Open XML document).</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Two paragraphs share the same <c>w14:paraId</c>.</exception>
    public static IReadOnlyDictionary<string, Paragraph> BuildParagraphIndex(Body body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var index = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var id = paragraph.ParagraphId?.Value;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var key = id.ToUpperInvariant();
            if (!index.TryAdd(key, paragraph))
            {
                throw new InvalidOperationException(
                    $"Duplicate w14:paraId '{key}' in the document body — paraIds must be unique within a " +
                    "document for the splice key to map an edited paragraph to exactly one original.");
            }
        }

        return index;
    }

    /// <summary>
    /// Resolves <paramref name="editedParaIds"/> against <paramref name="index"/>, partitioning them into
    /// the matched original paragraphs (the redline targets) and the unmatched ids. FR-12: an unmatched id
    /// is a HANDLED miss — returned in <see cref="ParaIdSpliceResolution.Unmatched"/> for the caller
    /// (the synthesizer) to surface as an error — NEVER a silent no-op or a write to the wrong paragraph. A
    /// null/empty edited id is treated as unmatched (it can key nothing). Duplicate edited ids collapse
    /// to a single matched entry (the same original paragraph); the id still appears once as matched.
    /// </summary>
    /// <param name="index">The paraId → paragraph index from <see cref="BuildParagraphIndex"/>.</param>
    /// <param name="editedParaIds">The paraIds of the editor paragraphs the user changed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="index"/> or <paramref name="editedParaIds"/> is null.</exception>
    public static ParaIdSpliceResolution Resolve(
        IReadOnlyDictionary<string, Paragraph> index,
        IEnumerable<string> editedParaIds)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(editedParaIds);

        var matched = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();

        foreach (var editedId in editedParaIds)
        {
            if (string.IsNullOrEmpty(editedId))
            {
                unmatched.Add(editedId ?? string.Empty);
                continue;
            }

            var key = editedId.ToUpperInvariant();
            if (index.TryGetValue(key, out var paragraph))
            {
                matched[key] = paragraph;
            }
            else if (!unmatched.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                unmatched.Add(key);
            }
        }

        return new ParaIdSpliceResolution(matched, unmatched);
    }
}

/// <summary>
/// FR-12 splice-key resolution: the edited paraIds that mapped to an original paragraph
/// (<paramref name="Matched"/>, keyed by upper-cased paraId → the live original <c>w:p</c> to splice)
/// and those that mapped to nothing (<paramref name="Unmatched"/> — a handled error for the caller to
/// surface; never a silent no-op). <see cref="IsFullyMatched"/> is true when every edited id resolved.
/// </summary>
public sealed record ParaIdSpliceResolution(
    IReadOnlyDictionary<string, Paragraph> Matched,
    IReadOnlyList<string> Unmatched)
{
    /// <summary>True when no edited paraId was left unmatched (the splice can proceed for all edits).</summary>
    public bool IsFullyMatched => Unmatched.Count == 0;
}
