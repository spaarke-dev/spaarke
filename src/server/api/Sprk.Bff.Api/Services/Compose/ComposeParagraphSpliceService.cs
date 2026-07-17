using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-02 (task 020, E1) — the paraId-keyed edited-paragraph REBUILD + SPLICE. Given the retained
/// load-time original <c>.docx</c> and the set of editor paragraphs that changed (each carrying its
/// <c>w14:paraId</c> + rebuilt text), produces a spliced-edited <c>.docx</c>: a COPY of the original
/// in which exactly the K edited paragraphs are rebuilt at the positions identified by matching
/// <c>w14:paraId</c>, and the untouched N−K paragraphs pass through unchanged. This spliced-edited
/// document is the INPUT to task 021's Docxodus <c>WmlComparer</c>, which diffs it against the retained
/// original to synthesize the minimal <c>w:ins</c>/<c>w:del</c> redline (design §4.2 "model (b)").
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope (design §4.2 / §8)</b>: this task owns ONLY the "rebuild edited paragraphs + splice by
/// paraId" leg. It does NOT run the comparer (task 021) and does NOT invert <see cref="ComposeService"/>
/// <c>SaveAsync</c> to drive the pipeline (task 022) — it is a pure, in-memory, heavily unit-tested
/// <c>byte[]</c>→<c>byte[]</c> transform, exercised in isolation. The splice KEY (paraId → original
/// <c>w:p</c> index + match/unmatched resolution) is <see cref="ComposeParaIdSpliceMap"/> (task 012,
/// FR-12) — consumed here, not re-implemented.
/// </para>
/// <para>
/// <b>The adeu asymmetry (design §4.2)</b>: the editor produces typed TEXT edits; the engine produces
/// valid OOXML. So an edited paragraph is rebuilt as its original <c>w:p</c> with the run content
/// replaced by a single run carrying the new text — while its <c>w14:paraId</c> (the splice key + the
/// comparer's stable anchor), its <c>w:pPr</c> (style / numbering / justification), and the base run
/// <c>w:rPr</c> (font / size) are PRESERVED. We never ask a model or a diff layer to emit raw revision
/// XML — the comparer (task 021) synthesizes the redline from this rebuilt-vs-original pair.
/// </para>
/// <para>
/// <b>NFR-07 preservation</b>: untouched paragraphs are the LITERAL original XML (never rebuilt), so
/// their text, <c>w14:paraId</c>, styles, numbering, and every structural part (headers/footers,
/// footnotes, tables incl. nested) are preserved by construction — the splice only ever mutates the K
/// matched paragraphs. Table-cell + nested-table paragraphs splice correctly because
/// <see cref="ComposeParaIdSpliceMap.BuildParagraphIndex"/> indexes them (recursive
/// <c>Descendants&lt;Paragraph&gt;()</c>, S1b).
/// </para>
/// <para>
/// <b>Fail-fast on an unmatched paraId (FR-12)</b>: every edited paraId is resolved against the
/// original BEFORE any mutation. If ANY id matches no original paragraph (e.g. a client split minted an
/// id the original never had), the whole splice throws <see cref="ComposeSpliceException"/> and the
/// document is left untouched — never a silent no-op, never a write to the wrong paragraph.
/// </para>
/// <para>
/// <b>Pure — no I/O, no AI, no Graph (ADR-007 / ADR-013 / NFR-05)</b>: operates only on the in-memory
/// bytes the caller (task 022 <c>SaveAsync</c>) already fetched behind the <c>SpeFileStore</c> facade.
/// No <c>Microsoft.Graph</c> type, no <c>IOpenAiClient</c>, no routing type (Tier-1 NetArchTest
/// enforces). <b>Zero package delta (NFR-01)</b>: <c>DocumentFormat.OpenXml</c> is already referenced;
/// Docxodus (task 001) is consumed by task 021, not here. Thread-safe stateless — a shared singleton
/// (ADR-010).
/// </para>
/// </remarks>
public sealed class ComposeParagraphSpliceService
{
    /// <summary>
    /// Rebuilds the <paramref name="editedParagraphs"/> into a COPY of <paramref name="retainedOriginal"/>,
    /// keyed by <c>w14:paraId</c>, and returns the spliced-edited bytes. Exactly the supplied paragraphs
    /// are rebuilt (their run text replaced, paraId + pPr + base rPr preserved); every other paragraph
    /// passes through unchanged. An empty <paramref name="editedParagraphs"/> returns a byte copy of the
    /// original (no-op splice).
    /// </summary>
    /// <param name="retainedOriginal">The retained load-time original <c>.docx</c> bytes (baseline).</param>
    /// <param name="editedParagraphs">The editor paragraphs that changed, each with its paraId + new text.
    /// Duplicate paraIds are rejected (an editor paragraph maps to exactly one original).</param>
    /// <exception cref="ArgumentException"><paramref name="retainedOriginal"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="editedParagraphs"/> is null.</exception>
    /// <exception cref="ComposeSpliceException">The bytes are not a readable DOCX, an edited paraId
    /// matches no original paragraph, or the same paraId is supplied twice.</exception>
    public byte[] SpliceEditedParagraphs(
        ReadOnlyMemory<byte> retainedOriginal,
        IReadOnlyList<ComposeEditedParagraph> editedParagraphs)
    {
        if (retainedOriginal.IsEmpty)
        {
            throw new ArgumentException("retainedOriginal is required and must be non-empty.", nameof(retainedOriginal));
        }

        ArgumentNullException.ThrowIfNull(editedParagraphs);

        // Reject duplicate edited paraIds up-front (an editor paragraph maps to exactly one original —
        // two edits for the same paraId is a caller bug, not a last-writer-wins).
        var byParaId = new Dictionary<string, ComposeEditedParagraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in editedParagraphs)
        {
            if (string.IsNullOrEmpty(edit.ParaId))
            {
                throw new ComposeSpliceException("An edited paragraph carried no w14:paraId — every edit must be keyed by its paraId.");
            }

            if (!byParaId.TryAdd(edit.ParaId.ToUpperInvariant(), edit))
            {
                throw new ComposeSpliceException(
                    $"Duplicate edited w14:paraId '{edit.ParaId.ToUpperInvariant()}' — an editor paragraph maps to exactly one original paragraph.");
            }
        }

        using var buffer = new MemoryStream(retainedOriginal.Length);
        buffer.Write(retainedOriginal.Span);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            // Editable open — we splice into THIS copy (the caller's bytes are never mutated; we own `buffer`).
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new ComposeSpliceException("The supplied retained-original bytes are not a readable .docx (WordprocessingML) package.", ex);
        }

        using (doc)
        {
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new ComposeSpliceException("The retained-original document has no body to splice into.");

            // Task 012 FR-12 splice key: build the paraId → original w:p index, then resolve ALL edited
            // ids BEFORE any mutation. A single unmatched id fails the whole splice (fail-fast) so no
            // partial/wrong write can land.
            var index = ComposeParaIdSpliceMap.BuildParagraphIndex(body);
            var resolution = ComposeParaIdSpliceMap.Resolve(index, byParaId.Keys);
            if (!resolution.IsFullyMatched)
            {
                throw new ComposeSpliceException(
                    "One or more edited paragraphs have a w14:paraId that matches no paragraph in the retained original: " +
                    string.Join(", ", resolution.Unmatched) +
                    ". The splice was aborted — no paragraph was modified.");
            }

            foreach (var (paraIdKey, paragraph) in resolution.Matched)
            {
                RebuildParagraphText(paragraph, byParaId[paraIdKey].NewText);
            }

            doc.MainDocumentPart!.Document.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Rebuilds <paramref name="paragraph"/> in place: replaces its run content with a single run
    /// carrying <paramref name="newText"/>, PRESERVING the paragraph's <c>w14:paraId</c> (an attribute —
    /// untouched by child edits), its <c>w:pPr</c> (style / numbering / justification), and the paragraph's
    /// base run <c>w:rPr</c> (font / size, cloned from the first original run). The paraId's survival is
    /// load-bearing: it stays the splice key and the comparer's stable anchor across task 021.
    /// </summary>
    private static void RebuildParagraphText(Paragraph paragraph, string newText)
    {
        // Clone the pPr + first-run rPr BEFORE clearing (they are children about to be removed).
        var pPr = paragraph.GetFirstChild<ParagraphProperties>()?.CloneNode(true) as ParagraphProperties;
        var baseRunProps = paragraph.Descendants<Run>().FirstOrDefault()?.GetFirstChild<RunProperties>()?.CloneNode(true) as RunProperties;

        // Clear only child content — the w14:paraId is an ATTRIBUTE on the w:p and survives this.
        paragraph.RemoveAllChildren();

        if (pPr is not null)
        {
            paragraph.AppendChild(pPr);
        }

        var run = new Run();
        if (baseRunProps is not null)
        {
            run.AppendChild(baseRunProps);
        }
        run.AppendChild(new Text(newText ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(run);
    }
}

/// <summary>
/// FR-02 input: one editor paragraph that changed — its <paramref name="ParaId"/> (<c>w14:paraId</c>,
/// the E2 splice key mapping it to exactly one original OOXML paragraph) and its rebuilt
/// <paramref name="NewText"/> (the paragraph's new settled text; the "typed text edit" side of the adeu
/// asymmetry — the service produces the valid OOXML). Reuses the E2 paraId identity rather than a
/// parallel schema (task 020 POML step 1).
/// </summary>
public sealed record ComposeEditedParagraph(string ParaId, string NewText);

/// <summary>
/// Raised when the paraId-keyed splice cannot proceed: the retained-original bytes are unreadable, an
/// edited paraId matches no original paragraph, or a paraId was supplied twice. Distinct from
/// <see cref="DocxAnnotationException"/> (the annotation-write path) so the caller (task 022) can
/// surface a splice-specific failure. FR-12: an unmatched paraId is a HANDLED error, never a silent
/// no-op.
/// </summary>
public sealed class ComposeSpliceException : Exception
{
    public ComposeSpliceException(string message) : base(message) { }
    public ComposeSpliceException(string message, Exception innerException) : base(message, innerException) { }
}
