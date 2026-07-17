using Docxodus;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-03 / FR-05 (task 021, E1) — the paragraph-REDLINE SYNTHESIS adapter. Given the retained
/// load-time original <c>.docx</c> (the E1 baseline) and the spliced-edited <c>.docx</c> produced by
/// <see cref="ComposeParagraphSpliceService"/> (task 020), it runs the Docxodus <c>WmlComparer</c> to
/// synthesize the MINIMAL <c>w:ins</c>/<c>w:del</c> revision markup between the two documents — with
/// author attribution set on the emitted revisions — and returns the redline-marked bytes. Inline
/// run-formatting edits (bold / italic / font) inside a changed paragraph are represented via
/// WmlComparer Format-Change Detection (<c>rPr</c>/<c>pPrChange</c>), NOT a full-run delete+re-insert
/// (FR-05 / D4). This is the diff engine of design §4.2 "model (b)".
/// </summary>
/// <remarks>
/// <para>
/// <b>Off-the-shelf engine, thin adapter (design §11 Path C)</b>: R3 does NOT hand-write a
/// paragraph-diff algorithm (historically hard — nested tables, multi-level numbering, moves). It
/// adopts the maintained MIT <c>Docxodus.WmlComparer</c> (a fork of Open-Xml-PowerTools, added by task
/// 001). The only NEW code is this adapter, which wraps <see cref="WmlComparer.Compare(WmlDocument,
/// WmlDocument, WmlComparerSettings)"/>: build the two inputs as <see cref="WmlDocument"/>s, set the
/// author on <see cref="WmlComparerSettings"/>, compare, and hand back
/// <see cref="WmlDocument.DocumentByteArray"/>. Spikes S1 + S1b validated that the comparer preserves
/// <c>w14:paraId</c> on unchanged paragraphs, emits minimal ins/del, detects format changes, and does
/// not throw on nested tables / 3-level numbering / whole-paragraph delete / paragraph split.
/// </para>
/// <para>
/// <b>BINDING packaging invariant (design §12 bullet 3 / §13)</b>: the diff path uses
/// <c>WmlComparer</c> ONLY. This adapter MUST NOT call Docxodus <c>HtmlToWml</c> or
/// <c>FormattingAssembler</c> — either re-pulls SkiaSharp and breaks the publish-size exclusion task
/// 001 established (SkiaSharp assets are excluded; <c>libSkiaSharp</c> must not appear in the published
/// output). Spike S3 proved <c>WmlComparer</c> runs correctly with SkiaSharp fully removed (net add
/// ≈ 2.44 MB managed, ~1 MB compressed).
/// </para>
/// <para>
/// <b>Fidelity is structural/semantic, not byte-identity (S1 refinement)</b>: WmlComparer
/// re-serializes its output (cosmetic BOM / whitespace differences on untouched parts) but preserves
/// structure, styles, numbering, headers/footers, footnotes, and <c>w14:paraId</c> on unchanged
/// paragraphs. This is Approach A (design §4.2) — cosmetic-lossless, adopted for the MVP.
/// </para>
/// <para>
/// <b>Pure — no I/O, no AI, no Graph (ADR-007 / ADR-013 / NFR-05)</b>: operates only on the in-memory
/// bytes the caller (task 022 <c>SaveAsync</c>) already fetched behind the <c>SpeFileStore</c> facade.
/// No <c>Microsoft.Graph</c> type, no <c>IOpenAiClient</c>, no executor / routing type (Tier-1
/// NetArchTest <c>ADR013_ComposeFacade</c> enforces). Thread-safe stateless — a shared singleton
/// (ADR-010). It does NOT run the comparer against SPE or invert <c>SaveAsync</c>; wiring into the save
/// pipeline is task 022.
/// </para>
/// <para>
/// <b>6.4.0 API note (§6.5 Path-C carry-over from task 001)</b>: task 001 shipped Docxodus <b>6.4.0</b>
/// (net8) rather than the S1-validated 7.1.0 (net10-only). The 6.4.0 surface is the same fork lineage:
/// <c>WmlComparer.Compare</c> is identical; <see cref="WmlDocument"/>'s byte-in constructor is
/// <c>(string fileName, byte[] bytes)</c> (a synthetic <c>.docx</c> name satisfies its extension
/// check); output is <see cref="WmlDocument.DocumentByteArray"/>; format-change detection is the
/// explicit <see cref="WmlComparerSettings.DetectFormatChanges"/> flag (set to true for FR-05).
/// </para>
/// </remarks>
public sealed class ComposeRedlineComparerService
{
    // A synthetic in-package name for the two comparison inputs. WmlDocument's (string, byte[]) ctor
    // validates the extension is a WordprocessingML one (.docx) — the name is never persisted; the
    // comparison operates purely on the byte arrays in memory.
    private const string OriginalDocName = "original.docx";
    private const string EditedDocName = "edited.docx";

    /// <summary>
    /// Synthesizes the minimal tracked-change redline between <paramref name="retainedOriginal"/> (the
    /// E1 baseline) and <paramref name="splicedEdited"/> (task 020 output) and returns the
    /// revision-marked <c>.docx</c> bytes. Emitted <c>w:ins</c>/<c>w:del</c> carry
    /// <paramref name="author"/> as attribution; inline run-format edits are emitted as
    /// <c>rPr</c>/<c>pPrChange</c> (Format-Change Detection), not del+ins.
    /// </summary>
    /// <param name="retainedOriginal">The retained load-time original <c>.docx</c> bytes (baseline).</param>
    /// <param name="splicedEdited">The spliced-edited <c>.docx</c> bytes (task 020 output).</param>
    /// <param name="author">The revision author attribution (surfaces as Word's revision author).</param>
    /// <param name="revisionTimestamp">Optional timestamp stamped on the emitted revisions
    /// (<c>w:date</c>). When null, Docxodus stamps its own wall-clock time — pass an explicit value for
    /// deterministic output (task 022 supplies the save timestamp; tests pin it).</param>
    /// <returns>The redline-marked <c>.docx</c> bytes.</returns>
    /// <exception cref="ArgumentException"><paramref name="retainedOriginal"/> or
    /// <paramref name="splicedEdited"/> is empty, or <paramref name="author"/> is null/whitespace.</exception>
    /// <exception cref="ComposeRedlineException">Either input is not a readable <c>.docx</c>, or the
    /// comparer failed to synthesize the redline. Nothing is partially returned — the throw happens
    /// before any bytes are handed back.</exception>
    public byte[] SynthesizeRedline(
        ReadOnlyMemory<byte> retainedOriginal,
        ReadOnlyMemory<byte> splicedEdited,
        string author,
        DateTimeOffset? revisionTimestamp = null)
    {
        if (retainedOriginal.IsEmpty)
        {
            throw new ArgumentException("retainedOriginal is required and must be non-empty.", nameof(retainedOriginal));
        }

        if (splicedEdited.IsEmpty)
        {
            throw new ArgumentException("splicedEdited is required and must be non-empty.", nameof(splicedEdited));
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("author is required for revision attribution.", nameof(author));
        }

        var settings = new WmlComparerSettings
        {
            AuthorForRevisions = author,

            // FR-05 / D4: represent inline run-format edits (bold/italic/font) as rPr/pPrChange, not a
            // full-run delete+re-insert. Set explicitly — do not rely on the field default.
            DetectFormatChanges = true,
        };

        if (revisionTimestamp is { } ts)
        {
            // Deterministic w:date attribution. Round-trip ("o") format is a valid XSD dateTime; UTC so
            // the emitted revision date is stable regardless of server locale.
            settings.DateTimeForRevisions = ts.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        }

        WmlDocument result;
        try
        {
            // WmlDocument's (string, byte[]) ctor eagerly opens the OPC package to sniff the document
            // type — so malformed bytes throw HERE, inside the try. Give it a private copy of the
            // bytes so the caller's buffers are never observed to change mid-compare.
            var original = new WmlDocument(OriginalDocName, retainedOriginal.ToArray());
            var edited = new WmlDocument(EditedDocName, splicedEdited.ToArray());

            // Diff path = WmlComparer ONLY (packaging invariant §12 bullet 3). No HtmlToWml /
            // FormattingAssembler on any branch here.
            result = WmlComparer.Compare(original, edited, settings);
        }
        catch (Exception ex) when (ex is not ComposeRedlineException)
        {
            // Malformed input package or an internal comparer failure — surface as a splice-adjacent
            // domain error so the caller (task 022) can map it instead of leaking a raw 500. This
            // method's own argument guards throw BEFORE the try, so any exception caught here
            // (incl. ArgumentNullException from the comparer's internals on an ill-formed package)
            // originates in Docxodus, not the caller's contract.
            throw new ComposeRedlineException(
                "The Docxodus WmlComparer failed to synthesize a redline from the supplied documents " +
                "(one of the inputs may not be a readable .docx package).",
                ex);
        }

        return result.DocumentByteArray
            ?? throw new ComposeRedlineException("The comparer returned no document bytes.");
    }
}

/// <summary>
/// Raised when the Docxodus <c>WmlComparer</c> redline synthesis cannot proceed: an input is not a
/// readable <c>.docx</c>, or the comparer failed to produce output. Distinct from
/// <see cref="ComposeSpliceException"/> (the paraId splice path) and
/// <see cref="DocxAnnotationException"/> (the annotation-write path) so the caller (task 022) can
/// surface a comparer-specific failure.
/// </summary>
public sealed class ComposeRedlineException : Exception
{
    public ComposeRedlineException(string message) : base(message) { }
    public ComposeRedlineException(string message, Exception innerException) : base(message, innerException) { }
}
