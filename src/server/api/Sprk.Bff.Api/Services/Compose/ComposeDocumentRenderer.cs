using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-01a / FR-27 (task 026, E1 born-in-editor) — the from-scratch, high-fidelity OOXML AUTHORING engine.
/// Materializes a well-formed, Word-openable <c>.docx</c> from a server-received editor
/// <see cref="ComposeContentModel"/> (paragraphs / clause headings / lists / tables + inline runs, each
/// paragraph paraId-keyed). This is the server-side replacement for the removed client <c>docx.js</c>
/// exporter and the reason the client can stop authoring bytes entirely (task 027): the SERVER becomes the
/// single authority for ALL <c>.docx</c> authoring — delta-onto-original for LOADED docs
/// (<see cref="ComposeParagraphRedlineSynthesizer"/>, task 022), full render for BORN-IN-EDITOR docs (this).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: a document born in the editor — AI-drafted (a <c>initialHtml</c> seed), blank, or
/// browse-local — has NO retained load-time original to delta against. Before R3 it was saved by the lossy
/// client <c>tipTapToDocxBytes</c>, which flattened true multi-level numbering, real styles, and firm
/// formatting into a stripped approximation — degrading an AI-drafted LEGAL document the moment it was first
/// saved. This engine authors the OOXML deterministically instead. Research-validated: this is how Harvey
/// authors documents — deterministic backend code owns the OOXML; the LLM/editor only supplies text +
/// structure (the "adeu asymmetry"). Client JS exporters are generators, not fidelity round-trippers.
/// </para>
/// <para>
/// <b>The keystone — style-linked multi-level numbering (FR-27)</b>: legal clause schemes (1 / 1.1 / 1.1.1)
/// are authored as ONE multi-level <c>w:abstractNum</c> (ilvl 0-8, <c>%N</c> <c>lvlText</c> cascade)
/// STYLE-LINKED to the <c>Heading1..6</c> paragraph styles — the heading STYLE carries the <c>w:numPr</c>,
/// and each abstract level back-references its style via <c>w:pStyle</c>. A clause paragraph then uses ONLY
/// <c>w:pStyle</c> (no DIRECT paragraph <c>numId</c>), so Word numbers it through the style — the single
/// mechanism that avoids the double/ghost numbering a direct-numId-alongside-a-style-numId produces. This
/// mirrors the real firm-template idiom (Common Paper CSA fixture <c>word/numbering.xml</c>). Ordered/bullet
/// LISTS are distinct: <c>ListParagraph</c> carries no style numbering, so a list item legitimately gets a
/// DIRECT <c>numPr</c> (not double-numbering); an ordered list that restarts at 1 gets a fresh <c>w:num</c>
/// instance with a <c>lvlOverride/startOverride</c>. Numbering MUST be instance-clean + style-linked AT
/// BIRTH — a malformed or double numId stays invisible until a later tracked-change edit (task 022) layers
/// on and corrupts the redline; it cannot be cheaply retrofit.
/// </para>
/// <para>
/// <b>E2 substrate</b>: every emitted <c>w:p</c> (incl. table-cell + nested-table paragraphs) carries a
/// unique, OOXML-valid <c>w14:paraId</c> — carried verbatim from the client model when present, else minted
/// using the SAME <c>ST_LongHexNumber</c> scheme as <see cref="ParaIdPreParser"/> (<c>0 &lt; x &lt;
/// 0x80000000</c>, 8-hex uppercase, dedup). So the rendered doc is a first-class substrate: the NEXT edit's
/// <see cref="ComposeParagraphRedlineSynthesizer"/> finds its paragraphs by those ids.
/// </para>
/// <para>
/// <b>Pure — <c>ComposeContentModel</c> in / <c>byte[]</c> out (ADR-007 / ADR-013 / NFR-05 / NFR-01)</b>: no
/// I/O, no AI, no <c>Microsoft.Graph</c> type, no routing type (Tier-1 NetArchTest <c>ADR013_ComposeFacade</c>
/// enforces); the SPE write stays behind the <c>SpeFileStore</c> facade (the renderer produces bytes;
/// <see cref="ComposeService.SaveAsync"/> persists them). No external NuGet — <c>DocumentFormat.OpenXml</c>
/// 3.5.1 is already referenced (Server-side render-from-a-content-model is deterministic OOXML authoring, NOT
/// an AI dispatch — design §11, ADR-039 complied). Thread-safe stateless — a shared singleton (ADR-010).
/// </para>
/// </remarks>
public sealed partial class ComposeDocumentRenderer
{
    // ── style ids ────────────────────────────────────────────────────────────────────────────────
    private const string NormalStyleId = "Normal";
    private const string ListParagraphStyleId = "ListParagraph";
    private const int MaxHeadingLevel = 6;               // Heading1..6 (TipTap heading levels)

    // ── numbering ids (see NumberingPlan) ────────────────────────────────────────────────────────
    private const int HeadingAbstractNumId = 0;          // style-linked clause scheme (ilvl 0-8)
    private const int OrderedAbstractNumId = 1;           // decimal list scheme (direct numPr)
    private const int BulletAbstractNumId = 2;            // bullet list scheme (direct numPr)
    private const int HeadingNumInstanceId = 1;           // the ONE num instance the Heading styles reference
    private const int FirstListNumInstanceId = 2;         // list num instances are allocated from here up

    // The largest permitted w14:paraId is 0x7FFFFFFF (ST_LongHexNumber, 0 < x < 0x80000000) — mirrors
    // ParaIdPreParser (task 010), the canonical mint scheme. Kept in lockstep so a rendered doc and a
    // load-time pre-parse produce interchangeable ids.
    private const uint MaxParaId = 0x80000000u;
    private const int MintRetryLimit = 1000;

    private readonly Func<uint> _mint;

    /// <summary>Production constructor — mints paraIds from a cryptographic RNG.</summary>
    public ComposeDocumentRenderer() : this(DefaultMint) { }

    /// <summary>
    /// Test seam: inject a deterministic id generator so a golden-file test can assert stable ids. Internal —
    /// exposed to the test assembly via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ComposeDocumentRenderer(Func<uint> mint) => _mint = mint;

    private static uint DefaultMint() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    /// <summary>
    /// Authors a high-fidelity <c>.docx</c> from <paramref name="model"/>: a <c>StyleDefinitionsPart</c>
    /// catalog (Normal + Heading1-6 + ListParagraph), a <c>NumberingDefinitionsPart</c> with the style-linked
    /// multi-level clause scheme + list schemes, native <c>w:tbl</c> tables, inline <c>w:b</c>/<c>w:i</c>/
    /// <c>w:u</c> runs, and a unique <c>w14:paraId</c> on every <c>w:p</c>. Returns the <c>.docx</c> bytes.
    /// </summary>
    /// <param name="model">The editor content model (the born-in-editor authoring source).</param>
    /// <param name="author">Document author, written to core properties (<c>dc:creator</c>). A blank value
    /// falls back to a stable product label.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public byte[] SynthesizeDocument(ComposeContentModel model, string author)
    {
        ArgumentNullException.ThrowIfNull(model);
        var creator = string.IsNullOrWhiteSpace(author) ? "Spaarke Compose" : author.Trim();

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // A NumberingPlan accumulates the ordered/bullet num instances the body render allocates, so the
            // numbering part authored afterwards references exactly the ids the body used. (Blank package —
            // no carrier numbering exists, so source NumIds map to allocated instances; see ListRenderState.)
            var plan = new NumberingPlan();
            RenderBlocks(body, model.Blocks, new ListRenderState(plan));

            // G5 (FR-05, task 033): swap each BuildRun href-sentinel for a real EXTERNAL hyperlink
            // relationship on the main part (the part is in scope here; BuildRun was not). Before save.
            ResolveHyperlinkRelationships(body, mainPart);

            // Word requires a trailing sectPr for a valid single-section document.
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));

            AddStyleDefinitions(mainPart);
            AddNumberingDefinitions(mainPart, plan);

            // Mint a unique w14:paraId on every paragraph lacking a valid one — AFTER the body is fully built
            // so the dedup pass sees every client-carried id (mirrors ParaIdPreParser's collect-then-mint).
            AssignParaIds(body);

            AddCoreProperties(document, creator);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Task 041 (ai-advanced-capabilities-nda-r1, Phase 4): appends <paramref name="blocks"/> as a NEW,
    /// page-broken, NON-TRACKED section at the END of an EXISTING <c>.docx</c> package — <c>byte[]</c>-in /
    /// <c>byte[]</c>-out, purely additive. Used to materialize the NDA-REVIEW Summary Page (TL;DR +
    /// flagged-section overview + recommendations, built by <see cref="ComposeSummaryPageGenerator"/> from
    /// the ONE ledgered result — no second LLM call) without touching a single existing paragraph.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a ComposeShadowPatchEngine operation (deliberate, ADR-049)</b>: the Summary Page is
    /// SERVER-AUTHORED, FINAL content — not a proposed edit for the user to accept/reject. Routing it
    /// through the operation log would emit tracked <c>w:ins</c> revisions (a pending suggestion), the wrong
    /// semantic here. This method instead appends PLAIN (untracked) paragraphs directly to the body,
    /// mirroring how <see cref="SynthesizeDocument"/> authors a whole document from a
    /// <see cref="ComposeContentModel"/> — the SAME authoring engine (<see cref="RenderBlocks"/>), applied
    /// additively to an existing package instead of a blank one.
    /// </para>
    /// <para>
    /// <b>Page break, not a new OOXML section</b>: a manual page break (<c>w:br w:type="page"</c>) precedes
    /// the appended content. This is deliberately NOT a new <c>w:sectPr</c> section break — a true section
    /// break would fork page setup (headers/footers/margins) for the appendix, unnecessary here and a risk
    /// of clashing with the source document's own section scheme. <see cref="ComposeSummaryPageGenerator"/>
    /// contractually emits only <see cref="ComposeBlockKind.Paragraph"/> blocks with plain (unstyled,
    /// unnumbered) runs, so this method never needs to merge into — or collide with — the target's
    /// <c>StyleDefinitionsPart</c> / <c>NumberingDefinitionsPart</c>.
    /// </para>
    /// <para>
    /// <b>Trailing section properties preserved</b>: OOXML requires the FINAL section's <c>w:sectPr</c> to be
    /// the LAST direct child of <c>w:body</c> (true of every valid single- or multi-section document). This
    /// method detaches it, appends the page break + blocks, then re-attaches it — so the appended content
    /// lands INSIDE the same final section (same page setup) rather than after/outside it.
    /// </para>
    /// <para>
    /// <b>Idempotent paraId assignment</b>: <see cref="AssignParaIds"/> mints a fresh <c>w14:paraId</c> for
    /// every new paragraph (E2 substrate); every existing id is left untouched.
    /// </para>
    /// </remarks>
    /// <param name="docxBytes">The existing <c>.docx</c> package bytes (retained-original, patched, or
    /// just-synthesized). A valid WordprocessingML OPC package.</param>
    /// <param name="blocks">The content to append, in document order. SHOULD be
    /// <see cref="ComposeBlockKind.Paragraph"/> blocks only (style/numbering-independent, per
    /// <see cref="ComposeSummaryPageGenerator"/>'s contract). Heading/ListItem/Table blocks are supported
    /// defensively — this method adds the required Style/Numbering part ONLY when the target package does
    /// not already carry one — but are not exercised by the Summary Page generator. An empty
    /// <paramref name="blocks"/> is a no-op passthrough (mirrors <see cref="ComposeShadowPatchEngine.Apply"/>'s
    /// empty-log contract).</param>
    /// <exception cref="ArgumentException"><paramref name="docxBytes"/> is null/empty.</exception>
    /// <exception cref="ComposePatchException">The supplied bytes are not a readable <c>.docx</c> package, or
    /// the package has no main document part / body.</exception>
    public byte[] AppendSection(byte[] docxBytes, IReadOnlyList<ComposeBlock> blocks)
    {
        if (docxBytes is null || docxBytes.Length == 0)
        {
            throw new ArgumentException("docxBytes is required and must be non-empty.", nameof(docxBytes));
        }

        ArgumentNullException.ThrowIfNull(blocks);

        if (blocks.Count == 0)
        {
            return docxBytes;
        }

        using var buffer = new MemoryStream();
        buffer.Write(docxBytes, 0, docxBytes.Length);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new ComposePatchException(
                ComposePatchErrorKind.MalformedDocument,
                "The supplied bytes are not a readable .docx (WordprocessingML) package.",
                ex);
        }

        using (doc)
        {
            var mainPart = doc.MainDocumentPart
                ?? throw new ComposePatchException(ComposePatchErrorKind.MalformedDocument, "The .docx has no main document part.");
            var body = mainPart.Document?.Body
                ?? throw new ComposePatchException(ComposePatchErrorKind.MalformedDocument, "The .docx main document part has no body.");

            // The final section's sectPr is always the LAST direct child of body — detach it so the new
            // content lands INSIDE the same final section, then re-attach it as the new last child.
            var trailingSectPr = body.Elements<SectionProperties>().LastOrDefault();
            trailingSectPr?.Remove();

            // Manual page break — a dedicated paragraph containing only a <w:br w:type="page"/> run.
            // Deliberately NOT a new w:sectPr (see remarks).
            body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));

            // Defensive list path (Step-9.5 fix F5): mirror RenderIntoCarrier's collision-safe scan — the
            // old blank-package plan allocated from FirstListNumInstanceId regardless of the target's own
            // numbering, so an appended list could capture (or dangle against) an existing target instance.
            // Gated on list presence like RenderIntoCarrier (011-T2: list-free appends never touch the part).
            var plan = new NumberingPlan();
            var state = new ListRenderState(plan);
            var maxExistingAbstractId = 0;
            if (ModelContainsListItem(blocks))
            {
                var targetNumbering = ScanCarrierNumbering(docxBytes);
                maxExistingAbstractId = targetNumbering.MaxAbstractNumId;
                plan = new NumberingPlan(Math.Max(FirstListNumInstanceId, targetNumbering.MaxNumId + 1));
                state = new ListRenderState(plan, targetNumbering);
            }

            RenderBlocks(body, blocks, state);

            // G5 (FR-05, task 033): resolve any href-sentinels in the appended section too (parity with
            // SynthesizeDocument; the Summary Page generator emits none today, so this is a no-op there).
            ResolveHyperlinkRelationships(body, mainPart);

            // Defensive: only exercised if a future caller passes Heading/ListItem/Table blocks (the Summary
            // Page generator does not). Adds the required part ONLY when the target doesn't already carry
            // one — never a second StyleDefinitionsPart/NumberingDefinitionsPart (Word would not merge two;
            // an EXISTING part gets the same collision-safe merge as RenderIntoCarrier, F5).
            if (mainPart.StyleDefinitionsPart is null && blocks.Any(b => b.Kind == ComposeBlockKind.Heading))
            {
                AddStyleDefinitions(mainPart);
            }

            if (plan.OrderedInstanceIds.Count > 0 || plan.BulletInstanceId is not null)
            {
                if (mainPart.NumberingDefinitionsPart is null)
                {
                    AddNumberingDefinitions(mainPart, plan);
                }
                else
                {
                    MergeNumberingDefinitions(
                        mainPart.NumberingDefinitionsPart,
                        plan,
                        orderedAbstractId: maxExistingAbstractId + 1,
                        bulletAbstractId: maxExistingAbstractId + 2);
                }
            }

            if (trailingSectPr is not null)
            {
                body.AppendChild(trailingSectPr);
            }

            // E2 substrate: mint a fresh w14:paraId for every appended paragraph; every existing id untouched.
            AssignParaIds(body);

            mainPart.Document!.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Task 011 (spaarkeai-compose-r6, FR-03) — the CARRIER render: replaces an existing package's BODY with
    /// content rendered from <paramref name="model"/> while preserving every other part (styles, numbering,
    /// headers/footers, theme, settings, fonts — the document-level identity the thin model cannot carry).
    /// This is the render-on-save author for IMPORTED documents (the 010 cutover's callee): the canonical
    /// model (projected by <c>ComposeDocxProjectionBuilder.BuildContentModel</c>, edited by the client)
    /// renders back INTO the retained source package — the third authoring mode alongside
    /// <see cref="SynthesizeDocument"/> (blank package, born-in-editor) and <see cref="AppendSection"/>
    /// (additive, whose preserve-parts + trailing-sectPr discipline this generalizes).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No anchoring, ADR-049 Path-B by construction</b>: the body is replaced WHOLESALE from the model and
    /// carrier parts are preserved WHOLESALE — nothing is located, matched, or patched; no text-search; no
    /// <c>ComposeShadowPatchEngine</c>. The count-gate's mismatch condition cannot exist on this path.
    /// </para>
    /// <para>
    /// <b>Carrier styles WIN (the fidelity point)</b>: when the carrier has a StyleDefinitionsPart, its
    /// Heading1..6 / ListParagraph / Normal definitions govern the rendered look (a firm template's heading
    /// scheme survives the save). The renderer's own catalog is authored ONLY when the carrier has no styles
    /// part at all — the same defensive rule as <see cref="AppendSection"/> — and in that carrier case the
    /// catalog is authored WITHOUT the heading style-linked numPr (review 011-M1; the heading num instance
    /// is never merged, so a numbered Heading style would dangle or capture a carrier num definition). A
    /// carrier whose Heading styles carry no style-linked numbering yields unnumbered headings, and a
    /// carrier styles part that simply LACKS <c>Heading{n}</c> / <c>ListParagraph</c> leaves the rendered
    /// <c>pStyle</c> references dangling — Word falls back to Normal formatting (headings visually
    /// indistinct; list indent lost though direct-numPr numbering still works). All are the documented
    /// carrier-faithful degradation (review 011-P7).
    /// </para>
    /// <para>
    /// <b>Carrier-referenced numbering first; collision-safe merge second (task 021)</b>: a list item whose
    /// <see cref="ComposeBlock.NumId"/> exists in the carrier REFERENCES that instance directly — the
    /// carrier's own scheme + Word's per-instance counters reproduce the source labels exactly (golden-label
    /// parity, incl. interruption-continuity and multi-level composition), and a fully carrier-referencing
    /// render never touches the numbering part at all (numbering.xml stays byte-identical). Only items whose
    /// identity is unknown to the carrier (born-in-editor, foreign source) allocate: those instances
    /// allocate ABOVE the carrier's max <c>numId</c> and reference renderer abstracts appended ABOVE the
    /// carrier's max <c>abstractNumId</c> (<see cref="MergeNumberingDefinitions"/>) — a rendered list can
    /// never capture a carrier num definition and no carrier-owned abstract/instance is touched. The heading
    /// abstract/instance is NOT merged (headings follow carrier styles, see above).
    /// </para>
    /// <para>
    /// <b>Final section preserved; interior sections flatten</b>: the trailing body <c>sectPr</c> (page size /
    /// margins / header-footer references of the FINAL section) is detached and re-attached around the body
    /// swap, so the rendered content lands inside the same page setup and the carrier's header/footer parts
    /// stay referenced. INTERIOR section breaks live in paragraphs being replaced — their loss is a projection-
    /// side counted flatten (task 023 widens sections). A carrier body with no trailing sectPr gets
    /// <see cref="SynthesizeDocument"/>'s default single-section setup.
    /// </para>
    /// <para>
    /// <b>Carrier metadata preserved</b>: core properties (creator etc.) are left untouched when present;
    /// <paramref name="author"/> is used only when the carrier lacks a core-properties part entirely.
    /// Orphaned parts (images/footnotes referenced only by the replaced body) remain in the package as inert
    /// weight — harmless, and version history retains the original anyway (ADR-049 safety net). Two further
    /// documented degradations (review 011-P4/P9): a preserved header/footer whose <c>REF</c> field or
    /// anchor hyperlink targets a BODY bookmark loses its target with the body swap (the model does not
    /// carry bookmarks — Word shows its standard broken-reference text on field update); and a carrier
    /// instance referencing an UNDEFINED abstractNumId that happens to equal a remapped renderer abstract id
    /// would resolve to it — only observable from numbered paragraphs inside preserved parts, accepted.
    /// </para>
    /// </remarks>
    /// <param name="carrierBytes">The retained source package (a valid WordprocessingML OPC package).</param>
    /// <param name="model">The canonical content model to render as the new body.</param>
    /// <param name="author">Attribution used ONLY when the carrier has no core-properties part.</param>
    /// <exception cref="ArgumentException"><paramref name="carrierBytes"/> is null/empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ComposePatchException">The carrier is not a readable package or has no main part/body.</exception>
    public byte[] RenderIntoCarrier(byte[] carrierBytes, ComposeContentModel model, string author)
    {
        if (carrierBytes is null || carrierBytes.Length == 0)
        {
            throw new ArgumentException("carrierBytes is required and must be non-empty.", nameof(carrierBytes));
        }

        ArgumentNullException.ThrowIfNull(model);

        using var buffer = new MemoryStream();
        buffer.Write(carrierBytes, 0, carrierBytes.Length);
        buffer.Position = 0;

        WordprocessingDocument doc;
        try
        {
            doc = WordprocessingDocument.Open(buffer, isEditable: true);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            throw new ComposePatchException(
                ComposePatchErrorKind.MalformedDocument,
                "The supplied carrier bytes are not a readable .docx (WordprocessingML) package.",
                ex);
        }

        using (doc)
        {
            var mainPart = doc.MainDocumentPart
                ?? throw new ComposePatchException(ComposePatchErrorKind.MalformedDocument, "The carrier .docx has no main document part.");
            var body = mainPart.Document?.Body
                ?? throw new ComposePatchException(ComposePatchErrorKind.MalformedDocument, "The carrier .docx main document part has no body.");

            // Detach the FINAL section's sectPr before the swap; re-attached below (see remarks).
            var trailingSectPr = body.Elements<SectionProperties>().LastOrDefault();
            if (trailingSectPr is not null)
            {
                trailingSectPr.Remove();
            }
            else
            {
                // Review finding 011-P1: some third-party generators park the final section's sectPr
                // inside the LAST PARAGRAPH's pPr with no body-level sectPr (schema-nonconforming).
                // Promote a clone so the carrier's page setup + header/footer REFERENCES survive the
                // swap instead of being silently replaced by the default Letter setup (the UAT #1A
                // headers-vanish symptom, on this one shape).
                trailingSectPr = body.Elements<Paragraph>().LastOrDefault()
                    ?.ParagraphProperties?.SectionProperties?.CloneNode(true) as SectionProperties;
            }

            body.RemoveAllChildren();

            // Collision-safe allocation base (see remarks), computed BEFORE the render so the plan's ids
            // are correct in the paragraphs' direct numPr as they are built. Task 021: the carrier's
            // numbering is inspected via a SEPARATE READ-ONLY open of the carrier bytes — touching the
            // editable package's Numbering DOM would mark the part for autoSave re-serialization on
            // dispose, rewriting an otherwise-untouched numbering.xml and breaking the preserve-parts
            // byte-identity contract (caught by the 011-T2 hardened seam oracle). The scan also collects
            // the carrier's numId set, so a model item whose ComposeBlock.NumId exists in the carrier
            // REFERENCES it directly (golden-label parity by construction) — a fully carrier-referencing
            // render allocates nothing and the numbering part is never touched at all.
            var plan = new NumberingPlan();
            var state = new ListRenderState(plan);
            var maxExistingAbstractId = 0;
            if (ModelContainsListItem(model.Blocks))
            {
                var carrierNumbering = ScanCarrierNumbering(carrierBytes);
                maxExistingAbstractId = carrierNumbering.MaxAbstractNumId;
                plan = new NumberingPlan(Math.Max(FirstListNumInstanceId, carrierNumbering.MaxNumId + 1));
                state = new ListRenderState(plan, carrierNumbering);
            }

            RenderBlocks(body, model.Blocks, state);
            ResolveHyperlinkRelationships(body, mainPart);

            if (mainPart.StyleDefinitionsPart is null)
            {
                // Carrier has no styles at all — author the catalog WITHOUT the heading style-linked
                // numPr (review finding 011-M1): the heading abstract/instance is never merged in
                // carrier mode, so a numbered Heading style would either dangle (no numbering part) or
                // CAPTURE a carrier num instance at numId 1 — the exact collision class the merge
                // exists to prevent. Unnumbered headings are the documented carrier-faithful stance.
                AddStyleDefinitions(mainPart, includeHeadingNumbering: false);
            }

            if (plan.OrderedInstanceIds.Count > 0 || plan.BulletInstanceId is not null)
            {
                if (mainPart.NumberingDefinitionsPart is null)
                {
                    AddNumberingDefinitions(mainPart, plan);
                }
                else
                {
                    MergeNumberingDefinitions(
                        mainPart.NumberingDefinitionsPart,
                        plan,
                        orderedAbstractId: maxExistingAbstractId + 1,
                        bulletAbstractId: maxExistingAbstractId + 2);
                }
            }

            if (trailingSectPr is not null)
            {
                body.AppendChild(trailingSectPr);
            }
            else
            {
                // Carrier had no trailing sectPr (schema-degenerate): default single-section setup,
                // mirroring SynthesizeDocument.
                body.AppendChild(new SectionProperties(
                    new PageSize { Width = 12240, Height = 15840 },
                    new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));
            }

            AssignParaIds(body);

            if (doc.CoreFilePropertiesPart is null)
            {
                AddCoreProperties(doc, string.IsNullOrWhiteSpace(author) ? "Spaarke Compose" : author.Trim());
            }

            mainPart.Document!.Save();
        }

        return buffer.ToArray();
    }

    /// <summary>Whether <paramref name="blocks"/> contains any list item (recursing into table cells) —
    /// gates the carrier numbering inspection/merge so a list-free render never touches (and therefore
    /// never rewrites) the carrier's numbering part (011-T2 preserve-parts contract).</summary>
    private static bool ModelContainsListItem(IReadOnlyList<ComposeBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.Kind == ComposeBlockKind.ListItem)
            {
                return true;
            }
            if (block.Kind == ComposeBlockKind.Table && block.Table is not null
                && block.Table.Rows.Any(r => r.Cells.Any(c => ModelContainsListItem(c.Blocks))))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Task 011: merges the plan's list instances into an EXISTING carrier numbering part. Renderer abstracts
    /// are inserted with REMAPPED ids above the carrier's own (schema order: AbstractNum before Num — inserted
    /// before the first existing instance), and every plan instance references those remapped abstracts. No
    /// carrier-owned abstract or instance is modified. The heading abstract/instance is deliberately absent —
    /// carrier styles govern headings (see <see cref="RenderIntoCarrier"/> remarks).
    /// </summary>
    private static void MergeNumberingDefinitions(
        NumberingDefinitionsPart numberingPart, NumberingPlan plan, int orderedAbstractId, int bulletAbstractId)
    {
        var numbering = numberingPart.Numbering ??= new Numbering();

        // CT_Numbering order edges (review finding 011-P3): all abstractNum precede all num, and a
        // trailing w:numIdMacAtCleanup (Mac Word artifact) must stay LAST — new instances insert before
        // it, and new abstracts insert before the first existing instance (or before the cleanup marker
        // when the part has abstracts but no instances).
        var firstInstance = numbering.Elements<NumberingInstance>().FirstOrDefault();
        var macCleanup = numbering.GetFirstChild<NumberingIdMacAtCleanup>();

        void InsertAbstract(AbstractNum abstractNum)
        {
            var anchor = (OpenXmlElement?)firstInstance ?? macCleanup;
            if (anchor is not null)
            {
                numbering.InsertBefore(abstractNum, anchor);
            }
            else
            {
                numbering.AppendChild(abstractNum);
            }
        }

        void AppendInstance(NumberingInstance instance)
        {
            if (macCleanup is not null)
            {
                numbering.InsertBefore(instance, macCleanup);
            }
            else
            {
                numbering.AppendChild(instance);
            }
        }

        if (plan.OrderedInstanceIds.Count > 0)
        {
            InsertAbstract(BuildOrderedAbstractNum(orderedAbstractId));
            foreach (var orderedId in plan.OrderedInstanceIds)
            {
                var instance = new NumberingInstance(new AbstractNumId { Val = orderedAbstractId }) { NumberID = orderedId };
                instance.AppendChild(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 });
                AppendInstance(instance);
            }
        }

        if (plan.BulletInstanceId is { } bulletId)
        {
            InsertAbstract(BuildBulletAbstractNum(bulletAbstractId));
            AppendInstance(new NumberingInstance(new AbstractNumId { Val = bulletAbstractId }) { NumberID = bulletId });
        }

        numberingPart.Numbering.Save();
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Body render
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private void RenderBlocks(OpenXmlElement container, IReadOnlyList<ComposeBlock> blocks, ListRenderState state)
    {
        // Ordered-list continuity (task 021, review 020-R1 + Step-9.5 fix F1): the model contract — not
        // block adjacency — governs instance selection. An item carrying a source NumId resolves through
        // ListRenderState (carrier-direct or per-source-id mapped), so same source numId ⇒ same rendered
        // instance and an interrupted run CONTINUES exactly as Word's per-numId counters do.
        //
        // NumId-less (born-in-editor) items use PER-LEVEL run state, mirroring the TipTap nesting the
        // client mapper flattens from (docxBridge.buildContentModel: startsNewList=true only on a
        // top-level list's first item; nested lists are never flagged — their boundaries are conveyed by
        // LEVEL transitions):
        //   - an ordered item at level L continues the run AT ITS LEVEL; with no run at L it inherits the
        //     NEAREST SHALLOWER active ordered instance (Word's one-instance-deeper-ilvl idiom — a nested
        //     ordered list inside an ordered list is the same w:num, and Word's deeper-level reset makes a
        //     re-entered nested list restart at 1 by itself);
        //   - ANY list item closes runs DEEPER than its level, and a bullet also closes the run AT its
        //     level (in the flattened TipTap shape a same-level bullet means that ordered list node
        //     ended — so two nested ordered lists separated by a parent bullet get DISTINCT instances and
        //     each restarts at 1, matching the editor display);
        //   - a non-list block closes NESTED runs (level ≥ 1) but leaves the level-0 run continuable —
        //     StartsNewList=false continues across intervening prose (the 020-R1 contract; the live
        //     mapper flags every distinct top-level ordered list StartsNewList=true, so top-level
        //     restarts remain explicit);
        //   - StartsNewList=true always allocates a fresh restart-at-1 instance.
        // State is local per container: a table-cell boundary starts fresh (a NumId-less list never
        // continues across cells).
        var orderedRunByLevel = new Dictionary<int, int>();

        void CloseRunsDeeperThan(int level)
        {
            foreach (var key in orderedRunByLevel.Keys.Where(k => k > level).ToList())
            {
                orderedRunByLevel.Remove(key);
            }
        }

        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case ComposeBlockKind.Heading:
                    CloseRunsDeeperThan(0);
                    container.AppendChild(BuildHeading(block));
                    break;

                case ComposeBlockKind.ListItem when block.Ordered:
                    var level = Math.Clamp(block.Level, 0, 8);
                    int effectiveNumId;
                    if (block.NumId is int sourceNumId)
                    {
                        effectiveNumId = state.ResolveOrdered(sourceNumId, level);
                    }
                    else if (!block.StartsNewList && orderedRunByLevel.TryGetValue(level, out var currentRun))
                    {
                        effectiveNumId = currentRun;
                    }
                    else if (!block.StartsNewList && TryNearestShallowerRun(orderedRunByLevel, level, out var inherited))
                    {
                        effectiveNumId = inherited;
                    }
                    else
                    {
                        effectiveNumId = state.Plan.NewOrderedInstance();
                    }
                    // A following NumId-less continuation item at this level joins THIS list (natural
                    // editing: the user adds an item to an imported list and it numbers with it).
                    orderedRunByLevel[level] = effectiveNumId;
                    CloseRunsDeeperThan(level);
                    container.AppendChild(BuildListItem(block, effectiveNumId));
                    break;

                case ComposeBlockKind.ListItem: // bullet
                    var bulletLevel = Math.Clamp(block.Level, 0, 8);
                    orderedRunByLevel.Remove(bulletLevel);
                    CloseRunsDeeperThan(bulletLevel);
                    container.AppendChild(BuildListItem(block, state.ResolveBullet(block.NumId, bulletLevel)));
                    break;

                case ComposeBlockKind.Table:
                    CloseRunsDeeperThan(0);
                    if (block.Table is { Rows.Count: > 0 })
                    {
                        container.AppendChild(BuildTable(block.Table, state));
                    }
                    break;

                case ComposeBlockKind.Paragraph:
                default:
                    CloseRunsDeeperThan(0);
                    container.AppendChild(BuildParagraph(block));
                    break;
            }
        }
    }

    /// <summary>The nearest ACTIVE ordered run shallower than <paramref name="level"/> (a NumId-less
    /// nested ordered item joins its parent's instance at a deeper ilvl — Word's multi-level idiom).</summary>
    private static bool TryNearestShallowerRun(Dictionary<int, int> orderedRunByLevel, int level, out int numId)
    {
        for (var probe = level - 1; probe >= 0; probe--)
        {
            if (orderedRunByLevel.TryGetValue(probe, out numId))
            {
                return true;
            }
        }
        numId = 0;
        return false;
    }

    private static Paragraph BuildParagraph(ComposeBlock block)
    {
        var pPr = new ParagraphProperties();
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    private static Paragraph BuildHeading(ComposeBlock block)
    {
        var level = Math.Clamp(block.Level <= 0 ? 1 : block.Level, 1, MaxHeadingLevel);
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = HeadingStyleId(level) });
        // NO w:numPr here — the number is supplied by the Heading{level} STYLE's numPr (style-linked). A
        // direct numId here would double-number (FR-27).
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    private static Paragraph BuildListItem(ComposeBlock block, int numInstanceId)
    {
        var ilvl = Math.Clamp(block.Level, 0, 8);
        var pPr = new ParagraphProperties(
            new ParagraphStyleId { Val = ListParagraphStyleId },
            // DIRECT numPr: ListParagraph carries no style numbering, so this is not double-numbering.
            new NumberingProperties(
                new NumberingLevelReference { Val = ilvl },
                new NumberingId { Val = numInstanceId }));
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    /// <summary>Assembles a paragraph from its <paramref name="pPr"/> + the block's inline runs, carrying a
    /// client paraId when the model supplied a valid one (else left for <see cref="AssignParaIds"/>).</summary>
    private static Paragraph AssembleParagraph(ParagraphProperties pPr, ComposeBlock block)
    {
        var paragraph = new Paragraph();
        CarryClientParaId(paragraph, block.ParaId);
        paragraph.AppendChild(pPr);

        if (block.Runs.Count == 0)
        {
            // An empty paragraph is valid (Word renders a blank line). No run needed.
            return paragraph;
        }

        foreach (var run in block.Runs)
        {
            paragraph.AppendChild(BuildRun(run));
        }

        return paragraph;
    }

    // G5 (FR-05, task 033): sentinel prefix stashing a run's href on the temporary Hyperlink.Id during the
    // static body build (which has no MainDocumentPart in hand). ResolveHyperlinkRelationships replaces
    // each sentinel with a real EXTERNAL relationship id BEFORE the document is saved — the sentinel never
    // persists. A prefix that can never collide with a real OOXML relationship id (rId…).
    private const string HyperlinkPendingIdPrefix = "COMPOSE_PENDING_HREF:";

    private static OpenXmlElement BuildRun(ComposeInlineRun run)
    {
        var element = new Run();
        if (run.Bold || run.Italic || run.Underline)
        {
            var rPr = new RunProperties();
            if (run.Bold) rPr.AppendChild(new Bold());
            if (run.Italic) rPr.AppendChild(new Italic());
            if (run.Underline) rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
            element.AppendChild(rPr);
        }

        element.AppendChild(new Text(SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve });

        // G5: a run carrying an href renders as a clean w:hyperlink wrapping the run. The real external
        // relationship id can only be minted once the MainDocumentPart is in scope, so stash the href on a
        // sentinel Hyperlink.Id here; ResolveHyperlinkRelationships (called by both byte-authors after the
        // body is built) swaps it for the true rId. Zero text-search — the wrap is by the model's own run.
        if (!string.IsNullOrWhiteSpace(run.Href))
        {
            return new Hyperlink(element) { Id = HyperlinkPendingIdPrefix + run.Href!.Trim() };
        }

        return element;
    }

    /// <summary>
    /// G5 (FR-05, task 033): resolve every sentinel <see cref="HyperlinkPendingIdPrefix"/> id emitted by
    /// <see cref="BuildRun"/> into a real EXTERNAL hyperlink relationship on <paramref name="mainPart"/>
    /// (<c>TargetMode="External"</c>). Called by both authors (<see cref="SynthesizeDocument"/> +
    /// <see cref="AppendSection"/>) after the body is built and BEFORE save, so no sentinel ever persists.
    /// A malformed href that cannot form a Uri is unwrapped to its inner run (never a broken relationship,
    /// never a silent drop of the run's text — the text survives, only the link is dropped).
    /// </summary>
    private static void ResolveHyperlinkRelationships(Body body, MainDocumentPart mainPart)
    {
        foreach (var hyperlink in body.Descendants<Hyperlink>().ToList())
        {
            var id = hyperlink.Id?.Value;
            if (id is null || !id.StartsWith(HyperlinkPendingIdPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var href = id.Substring(HyperlinkPendingIdPrefix.Length);
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
            {
                hyperlink.Id = mainPart.AddHyperlinkRelationship(uri, isExternal: true).Id;
            }
            else
            {
                // Unrepresentable target (not an absolute Uri): keep the run's TEXT (no silent loss) by
                // replacing the hyperlink wrapper with its inline children, and drop only the link.
                var parent = hyperlink.Parent;
                if (parent is not null)
                {
                    foreach (var child in hyperlink.ChildElements.ToList())
                    {
                        child.Remove();
                        parent.InsertBefore(child, hyperlink);
                    }
                    hyperlink.Remove();
                }
            }
        }
    }

    // ── tables ───────────────────────────────────────────────────────────────────────────────────

    private Table BuildTable(ComposeTable model, ListRenderState state)
    {
        var table = new Table();
        table.AppendChild(BuildTableProperties(model));

        // A w:tblGrid (column definitions) MUST follow tblPr and precede the rows (schema order). Task 022:
        // explicit source widths when the model carries them; else width the grid to the widest row's TOTAL
        // SPAN (a gridSpan cell occupies multiple columns) so every column is defined.
        var grid = new TableGrid();
        if (model.GridColumnWidthsTwips is { Count: > 0 } gridWidths)
        {
            foreach (var width in gridWidths)
            {
                grid.AppendChild(new GridColumn { Width = width });
            }
        }
        else
        {
            var columnCount = model.Rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.GridSpan)));
            for (var c = 0; c < columnCount; c++)
            {
                grid.AppendChild(new GridColumn());
            }
        }
        table.AppendChild(grid);

        foreach (var row in model.Rows)
        {
            var tableRow = new TableRow();
            if (row.RepeatAsHeaderRow)
            {
                // CT_Row order: trPr precedes the cells.
                tableRow.AppendChild(new TableRowProperties(new TableHeader()));
            }
            foreach (var cell in row.Cells)
            {
                tableRow.AppendChild(BuildTableCell(cell, state));
            }
            table.AppendChild(tableRow);
        }

        return table;
    }

    /// <summary>
    /// Task 022 — the tri-state chrome contract (<see cref="ComposeTable.Borders"/>): a NULL Borders means
    /// born-in-editor → the legacy single-border/100% chrome, BIT-STABLE for the live client; non-null means
    /// source-faithful → ONLY the carried facts are emitted (an all-null-edge Borders reproduces a
    /// BORDERLESS table — legal signature-block layout tables must not grow borders on save).
    /// </summary>
    private static TableProperties BuildTableProperties(ComposeTable model)
    {
        if (model.Borders is null)
        {
            return new TableProperties(
                new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                // CT_TblBorders order: top, left, bottom, right, insideH, insideV.
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" }),
                new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = true, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true });
        }

        // Source-faithful — CT_TblPr schema order: tblStyle, tblW, tblBorders, tblLook.
        var tblPr = new TableProperties();
        if (!string.IsNullOrEmpty(model.StyleId))
        {
            tblPr.AppendChild(new TableStyle { Val = model.StyleId });
        }
        if (model.Width is { } width)
        {
            tblPr.AppendChild(new TableWidth { Width = width.Value, Type = MapWidthType(width.Type) });
        }
        if (BuildTableBorders(model.Borders) is { } borders)
        {
            tblPr.AppendChild(borders);
        }
        if (!string.IsNullOrEmpty(model.LookHex))
        {
            tblPr.AppendChild(new TableLook { Val = model.LookHex });
        }
        return tblPr;
    }

    /// <summary>Null when every edge is null — a borderless table omits <c>w:tblBorders</c> entirely.</summary>
    private static TableBorders? BuildTableBorders(ComposeTableBorders borders)
    {
        // CT_TblBorders order: top, left, bottom, right, insideH, insideV.
        var result = new TableBorders();
        AppendEdge<TopBorder>(result, borders.Top);
        AppendEdge<LeftBorder>(result, borders.Left);
        AppendEdge<BottomBorder>(result, borders.Bottom);
        AppendEdge<RightBorder>(result, borders.Right);
        AppendEdge<InsideHorizontalBorder>(result, borders.InsideHorizontal);
        AppendEdge<InsideVerticalBorder>(result, borders.InsideVertical);
        return result.HasChildren ? result : null;
    }

    private static void AppendEdge<TEdge>(TableBorders borders, ComposeTableBorderEdge? edge)
        where TEdge : BorderType, new()
    {
        if (edge is null)
        {
            return;
        }
        var element = new TEdge { Val = new BorderValues(edge.Val) };
        if (edge.Size is { } size)
        {
            element.Size = size;
        }
        if (!string.IsNullOrEmpty(edge.Color))
        {
            element.Color = edge.Color;
        }
        borders.AppendChild(element);
    }

    private static TableWidthUnitValues MapWidthType(string type) => type switch
    {
        "dxa" => TableWidthUnitValues.Dxa,
        "pct" => TableWidthUnitValues.Pct,
        "nil" => TableWidthUnitValues.Nil,
        _ => TableWidthUnitValues.Auto,
    };

    private TableCell BuildTableCell(ComposeTableCell cell, ListRenderState state)
    {
        var tableCell = new TableCell();
        tableCell.AppendChild(BuildCellProperties(cell));

        if (cell.Blocks.Count == 0)
        {
            // Word requires each cell to contain at least one paragraph.
            tableCell.AppendChild(new Paragraph());
            return tableCell;
        }

        // A header cell bolds its runs (cosmetic). Render the cell's nested blocks recursively so nested
        // tables + lists inside a cell are supported.
        var blocks = cell.IsHeader ? cell.Blocks.Select(EmphasizeBlock).ToList() : cell.Blocks;
        RenderBlocks(tableCell, blocks, state);
        return tableCell;
    }

    /// <summary>Task 022 — CT_TcPr schema order: tcW, gridSpan, vMerge, vAlign. A null
    /// <see cref="ComposeTableCell.VerticalAlignment"/> keeps the legacy born-in-editor <c>center</c>;
    /// projected cells always carry an explicit value (source, else Word's default <c>top</c>).</summary>
    private static TableCellProperties BuildCellProperties(ComposeTableCell cell)
    {
        var tcPr = new TableCellProperties();
        if (cell.Width is { } width)
        {
            tcPr.AppendChild(new TableCellWidth { Width = width.Value, Type = MapWidthType(width.Type) });
        }
        if (cell.GridSpan > 1)
        {
            tcPr.AppendChild(new GridSpan { Val = cell.GridSpan });
        }
        if (cell.VMerge == ComposeVerticalMerge.Restart)
        {
            tcPr.AppendChild(new VerticalMerge { Val = MergedCellValues.Restart });
        }
        else if (cell.VMerge == ComposeVerticalMerge.Continue)
        {
            tcPr.AppendChild(new VerticalMerge()); // no @w:val = continue (ECMA-376 default)
        }
        tcPr.AppendChild(new TableCellVerticalAlignment
        {
            Val = cell.VerticalAlignment switch
            {
                null or "center" => TableVerticalAlignmentValues.Center,
                "bottom" => TableVerticalAlignmentValues.Bottom,
                _ => TableVerticalAlignmentValues.Top,
            },
        });
        return tcPr;
    }

    private static ComposeBlock EmphasizeBlock(ComposeBlock block) =>
        block.Kind == ComposeBlockKind.Table
            ? block
            : block with { Runs = block.Runs.Select(r => r with { Bold = true }).ToList() };

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Style catalog (StyleDefinitionsPart)
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void AddStyleDefinitions(MainDocumentPart mainPart, bool includeHeadingNumbering = true)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Normal — the default paragraph style every other style is based on.
        styles.AppendChild(new Style(
            new StyleName { Val = "Normal" },
            new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = NormalStyleId,
            Default = true,
        });

        // Heading1..6 — each carries a w:numPr referencing the ONE heading num instance at its own ilvl
        // (the STYLE side of the style-link) + an outlineLvl so the doc has a navigable outline. Descending
        // sizes; all bold; keepNext so a heading stays with its following paragraph. Carrier mode (task 011)
        // passes includeHeadingNumbering=false — the heading num instance is never authored there, so a
        // style-linked numPr would dangle or capture a carrier num definition (review finding 011-M1).
        var headingSizes = new[] { "32", "28", "26", "24", "22", "22" }; // half-points: 16pt..11pt
        for (var level = 1; level <= MaxHeadingLevel; level++)
        {
            styles.AppendChild(BuildHeadingStyle(level, headingSizes[level - 1], includeHeadingNumbering));
        }

        // ListParagraph — indent only; NO numbering (list items supply a direct numPr).
        styles.AppendChild(new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 34 },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new Indentation { Left = "720" },
                new ContextualSpacing()))
        {
            Type = StyleValues.Paragraph,
            StyleId = ListParagraphStyleId,
        });

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style BuildHeadingStyle(int level, string sizeHalfPoints, bool includeNumbering = true)
    {
        var ilvl = level - 1;

        // CT_PPrBase child order: keepNext precedes numPr precedes spacing precedes outlineLvl.
        var pPr = new StyleParagraphProperties();
        pPr.AppendChild(new KeepNext());
        if (includeNumbering)
        {
            pPr.AppendChild(new NumberingProperties(
                new NumberingLevelReference { Val = ilvl },
                new NumberingId { Val = HeadingNumInstanceId }));
        }
        pPr.AppendChild(new SpacingBetweenLines { Before = "240", After = "120" });
        pPr.AppendChild(new OutlineLevel { Val = ilvl });

        return new Style(
            new StyleName { Val = $"heading {level}" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            pPr,
            new StyleRunProperties(
                new Bold(),
                new FontSize { Val = sizeHalfPoints },
                new FontSizeComplexScript { Val = sizeHalfPoints }))
        {
            Type = StyleValues.Paragraph,
            StyleId = HeadingStyleId(level),
        };
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Numbering (NumberingDefinitionsPart) — the keystone
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void AddNumberingDefinitions(MainDocumentPart mainPart, NumberingPlan plan)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        // AbstractNum elements MUST precede Num elements (schema order).
        numbering.AppendChild(BuildHeadingAbstractNum());
        numbering.AppendChild(BuildOrderedAbstractNum());
        numbering.AppendChild(BuildBulletAbstractNum());

        // The ONE heading num instance the Heading styles reference (numId 1 → heading abstract).
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = HeadingAbstractNumId }) { NumberID = HeadingNumInstanceId });

        // The shared bullet instance (allocated only if a bullet list was rendered).
        if (plan.BulletInstanceId is { } bulletId)
        {
            numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = BulletAbstractNumId }) { NumberID = bulletId });
        }

        // One ordered instance per restart-scoped ordered list, each with a startOverride so it restarts at 1.
        foreach (var orderedId in plan.OrderedInstanceIds)
        {
            var instance = new NumberingInstance(new AbstractNumId { Val = OrderedAbstractNumId }) { NumberID = orderedId };
            instance.AppendChild(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 });
            numbering.AppendChild(instance);
        }

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    /// <summary>
    /// The style-linked multi-level clause scheme (FR-27): ONE multilevel abstractNum, 9 levels (ilvl 0-8),
    /// each a decimal <c>%N</c> cascade (<c>%1</c> / <c>%1.%2</c> / <c>%1.%2.%3</c> …). Levels 0-5 back-link
    /// their <c>Heading1..6</c> style via <c>w:pStyle</c>; levels 6-8 are numbered (for completeness) but
    /// unlinked (headings only reach level 6). Each level restarts its counter after a higher level advances.
    /// </summary>
    private static AbstractNum BuildHeadingAbstractNum()
    {
        // CT_AbstractNum order: nsid precedes multiLevelType precedes the levels.
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0000" },
            new MultiLevelType { Val = MultiLevelValues.Multilevel })
        { AbstractNumberId = HeadingAbstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var cascade = string.Join(".", Enumerable.Range(1, ilvl + 1).Select(k => $"%{k}"));
            var level = new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = cascade },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            };

            // Style-link levels 0-5 → Heading1..6 (the abstract side of the link). Schema order places
            // w:pStyle after w:numFmt and BEFORE w:lvlText (matches the real CSA numbering.xml idiom).
            if (ilvl < MaxHeadingLevel)
            {
                level.InsertBefore(new ParagraphStyleIdInLevel { Val = HeadingStyleId(ilvl + 1) }, level.GetFirstChild<LevelText>());
            }

            abstractNum.AppendChild(level);
        }

        return abstractNum;
    }

    /// <summary>The ordered-list scheme: 9 decimal levels (<c>%N.</c>), consumed via a DIRECT numPr on
    /// ListParagraph items. No style link (lists are not styled-numbered). <paramref name="abstractNumId"/>
    /// defaults to the blank-package id; carrier mode (task 011) passes a remapped id above the carrier's own.</summary>
    private static AbstractNum BuildOrderedAbstractNum(int abstractNumId = OrderedAbstractNumId)
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0001" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = abstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = $"%{ilvl + 1}." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    /// <summary>The bullet-list scheme: 9 bullet levels (Symbol-font glyphs), consumed via a DIRECT numPr.
    /// <paramref name="abstractNumId"/> defaults to the blank-package id; carrier mode remaps (task 011).</summary>
    private static AbstractNum BuildBulletAbstractNum(int abstractNumId = BulletAbstractNumId)
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0002" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = abstractNumId };

        // Cycle the three classic Word bullet glyphs across depths.
        var glyphs = new[] { "", "o", "" }; // • (Symbol), o (Courier), ▪ (Wingdings)
        var fonts = new[] { "Symbol", "Courier New", "Wingdings" };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var pick = ilvl % 3;
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = glyphs[pick] },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }),
                new NumberingSymbolRunProperties(
                    new RunFonts { Ascii = fonts[pick], HighAnsi = fonts[pick], Hint = FontTypeHintValues.Default }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // paraId minting (E2 substrate) — mirrors ParaIdPreParser's ST_LongHexNumber scheme
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Carries a client-supplied paraId onto <paramref name="paragraph"/> when it is a valid
    /// <c>ST_LongHexNumber</c>; otherwise leaves it null for <see cref="AssignParaIds"/> to mint.</summary>
    private static void CarryClientParaId(Paragraph paragraph, string? clientParaId)
    {
        if (TryNormalizeParaId(clientParaId, out var normalized))
        {
            paragraph.ParagraphId = new HexBinaryValue(normalized);
        }
    }

    /// <summary>
    /// Post-pass: every body paragraph (incl. table-cell + nested-table paragraphs — <c>Descendants</c> is
    /// recursive) gets a UNIQUE <c>w14:paraId</c>. Two-pass, mirroring <see cref="ParaIdPreParser"/>: pass 1
    /// keeps the FIRST occurrence of each client-carried id and DROPS any later duplicate (so a malformed model
    /// that repeats a paraId can never emit a doc with a duplicate splice key); pass 2 mints for every
    /// paragraph left without one, collision-checked against the kept ids.
    /// </summary>
    private void AssignParaIds(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>().ToList();

        // Pass 1: keep the first occurrence of each client id; null out duplicates so pass 2 re-mints them.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paragraphs)
        {
            var existing = p.ParagraphId?.Value;
            if (string.IsNullOrEmpty(existing))
            {
                continue;
            }

            if (!seen.Add(existing!))
            {
                p.ParagraphId = null; // duplicate client-carried id — drop it, mint a fresh unique one below
            }
        }

        // Pass 2: mint for every paragraph without an id.
        foreach (var p in paragraphs)
        {
            if (!string.IsNullOrEmpty(p.ParagraphId?.Value))
            {
                continue;
            }

            var minted = MintUnique(seen);
            seen.Add(minted);
            p.ParagraphId = new HexBinaryValue(minted);
        }
    }

    private string MintUnique(HashSet<string> seen)
    {
        for (var attempt = 0; attempt < MintRetryLimit; attempt++)
        {
            var candidate = _mint();
            if (candidate == 0 || candidate >= MaxParaId)
            {
                continue;
            }

            var hex = candidate.ToString("X8");
            if (!seen.Contains(hex))
            {
                return hex;
            }
        }

        throw new InvalidOperationException($"Unable to mint a unique w14:paraId after {MintRetryLimit} attempts.");
    }

    private static bool TryNormalizeParaId(string? candidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (!ParaIdHexPattern().IsMatch(trimmed))
        {
            return false;
        }

        // Enforce the ST_LongHexNumber range (0 < x < 0x80000000).
        if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            || value == 0 || value >= MaxParaId)
        {
            return false;
        }

        normalized = value.ToString("X8");
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void ApplyAlignment(ParagraphProperties pPr, ComposeParagraphAlignment alignment)
    {
        var value = alignment switch
        {
            ComposeParagraphAlignment.Left => JustificationValues.Left,
            ComposeParagraphAlignment.Center => JustificationValues.Center,
            ComposeParagraphAlignment.Right => JustificationValues.Right,
            ComposeParagraphAlignment.Justify => JustificationValues.Both,
            _ => (JustificationValues?)null,
        };

        if (value is { } jc)
        {
            pPr.AppendChild(new Justification { Val = jc });
        }
    }

    private static string HeadingStyleId(int level) => $"Heading{level}";

    private static void AddCoreProperties(WordprocessingDocument document, string creator)
    {
        var props = document.AddCoreFilePropertiesPart();
        using var writer = new StreamWriter(props.GetStream(FileMode.Create));
        writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties""
                   xmlns:dc=""http://purl.org/dc/elements/1.1/"">
    <dc:creator>{EscapeXml(creator)}</dc:creator>
    <dc:description>Authored by Spaarke Compose</dc:description>
</cp:coreProperties>");
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    /// <summary>Strips XML-illegal control characters so Word never reports "unreadable content"
    /// (mirrors <c>DocxExportService.SanitizeText</c>; the content model text is already plain, so no HTML
    /// decode is needed). Tab / LF / CR are preserved (valid in XML).</summary>
    private static string SanitizeText(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : XmlInvalidCharPattern().Replace(value, string.Empty);

    [GeneratedRegex(@"^[0-9A-Fa-f]{1,8}$")]
    private static partial Regex ParaIdHexPattern();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex XmlInvalidCharPattern();

    /// <summary>
    /// Task 021 — document-scoped list-identity state threaded through one body render. Resolves each list
    /// item's EFFECTIVE <c>w:num</c> instance id from its <see cref="ComposeBlock.NumId"/> source identity:
    /// a source id present in <paramref name="carrierNumIds"/> is referenced DIRECTLY (the carrier's own
    /// scheme + Word's per-instance counters reproduce the source labels — golden parity by construction,
    /// and a fully carrier-referencing render never touches the numbering part); a source id unknown to the
    /// render target (blank-package synthesize, foreign carrier) maps per-DISTINCT-source-id to one
    /// allocated plan instance, preserving list identity — and therefore interruption-continuity — under
    /// the renderer's own scheme. Shared across table-cell recursion deliberately: the same source numId in
    /// body and cell is the same Word list. NumId-less (born-in-editor) items never reach this map — they
    /// use <c>RenderBlocks</c>' per-container current-instance + <see cref="ComposeBlock.StartsNewList"/> contract.
    /// </summary>
    private sealed class ListRenderState
    {
        private readonly CarrierNumberingScan? _carrier;
        private readonly Dictionary<int, int> _orderedBySourceId = new();

        public ListRenderState(NumberingPlan plan, CarrierNumberingScan? carrier = null)
        {
            Plan = plan;
            _carrier = carrier;
        }

        public NumberingPlan Plan { get; }

        /// <summary>Effective instance id for an ordered item carrying <paramref name="sourceNumId"/> at
        /// <paramref name="level"/>: carrier-direct when the id exists there AND its level classifies as
        /// ordered (Step-9.5 fix F2 — a coincident id in a FOREIGN carrier whose scheme is a bullet at
        /// this level would render a glyph where the source showed a number); else per-source-id mapped.</summary>
        public int ResolveOrdered(int sourceNumId, int level)
        {
            if (_carrier is not null && _carrier.ContainsNumId(sourceNumId)
                && _carrier.IsKindCompatible(sourceNumId, level, ordered: true))
            {
                return sourceNumId;
            }
            if (!_orderedBySourceId.TryGetValue(sourceNumId, out var allocated))
            {
                allocated = Plan.NewOrderedInstance();
                _orderedBySourceId.Add(sourceNumId, allocated);
            }
            return allocated;
        }

        /// <summary>Effective instance id for a bullet item: carrier-direct when the source id exists there
        /// AND classifies as a bullet at <paramref name="level"/> (preserves the carrier's glyph scheme);
        /// otherwise the shared renderer bullet instance (the glyph scheme is not model data — all fallback
        /// bullets share one instance).</summary>
        public int ResolveBullet(int? sourceNumId, int level) =>
            sourceNumId is int src && _carrier is not null && _carrier.ContainsNumId(src)
                && _carrier.IsKindCompatible(src, level, ordered: false)
                ? src
                : Plan.BulletInstance();
    }

    /// <summary>
    /// The carrier numbering facts <see cref="RenderIntoCarrier"/> needs BEFORE rendering: the referencable
    /// <c>w:num</c> id set, the collision-safe allocation base (max instance/abstract ids), and a
    /// per-(instance, level) ordered-vs-bullet classification for the F2 kind guard.
    /// </summary>
    private sealed class CarrierNumberingScan
    {
        private readonly HashSet<int> _numIds = new();
        private readonly Dictionary<int, int> _abstractByNumId = new();
        private readonly Dictionary<(int AbstractId, int Level), bool> _bulletByAbstractLevel = new();
        private readonly Dictionary<(int NumId, int Level), bool> _bulletByInstanceOverride = new();

        public int MaxNumId { get; private set; }
        public int MaxAbstractNumId { get; private set; }

        public bool ContainsNumId(int numId) => _numIds.Contains(numId);

        /// <summary>
        /// Whether the carrier instance's scheme at <paramref name="level"/> matches the item's kind.
        /// Tolerant probe (exact level, then nearer-lower, then higher — mirroring the projector's
        /// <c>ResolveOrderedFromModel</c> posture); an UNCLASSIFIABLE id/level returns compatible — the
        /// designed same-source carrier always matches, so unknown defaults to direct reference.
        /// </summary>
        public bool IsKindCompatible(int numId, int level, bool ordered)
        {
            var isBullet = ResolveBulletness(numId, level);
            return isBullet is null || isBullet.Value != ordered;
        }

        private bool? ResolveBulletness(int numId, int level)
        {
            if (_bulletByInstanceOverride.TryGetValue((numId, level), out var overridden))
            {
                return overridden;
            }
            if (!_abstractByNumId.TryGetValue(numId, out var abstractId))
            {
                return null;
            }
            if (_bulletByAbstractLevel.TryGetValue((abstractId, level), out var exact))
            {
                return exact;
            }
            for (var probe = level - 1; probe >= 0; probe--)
            {
                if (_bulletByAbstractLevel.TryGetValue((abstractId, probe), out var lower))
                {
                    return lower;
                }
            }
            for (var probe = level + 1; probe <= 8; probe++)
            {
                if (_bulletByAbstractLevel.TryGetValue((abstractId, probe), out var higher))
                {
                    return higher;
                }
            }
            return null;
        }

        public void RecordAbstract(AbstractNum abstractNum)
        {
            if (abstractNum.AbstractNumberId?.Value is not int abstractId)
            {
                return;
            }
            MaxAbstractNumId = Math.Max(MaxAbstractNumId, abstractId);
            foreach (var level in abstractNum.Elements<Level>())
            {
                if (level.LevelIndex?.Value is int ilvl && level.NumberingFormat?.Val is { } fmt)
                {
                    _bulletByAbstractLevel[(abstractId, ilvl)] = fmt.Value == NumberFormatValues.Bullet;
                }
            }
        }

        public void RecordInstance(NumberingInstance instance)
        {
            if (instance.NumberID?.Value is not int numId)
            {
                return;
            }
            _numIds.Add(numId);
            MaxNumId = Math.Max(MaxNumId, numId);
            if (instance.AbstractNumId?.Val?.Value is int abstractId)
            {
                _abstractByNumId[numId] = abstractId;
            }
            // A w:lvlOverride carrying a FULL w:lvl redefinition can change the level's numFmt for this
            // instance only — record it so the kind guard sees the instance-effective classification.
            foreach (var levelOverride in instance.Elements<LevelOverride>())
            {
                if (levelOverride.LevelIndex?.Value is int ilvl
                    && levelOverride.GetFirstChild<Level>()?.NumberingFormat?.Val is { } fmt)
                {
                    _bulletByInstanceOverride[(numId, ilvl)] = fmt.Value == NumberFormatValues.Bullet;
                }
            }
        }
    }

    /// <summary>
    /// Task 021: inspects the carrier's numbering part via a SEPARATE READ-ONLY open of the carrier bytes —
    /// never the editable package, whose Numbering DOM would be marked for autoSave re-serialization by the
    /// mere read (the 011-T2 preserve-parts hazard). Returns the carrier's <c>w:num</c> id set + kind
    /// classification (for direct reference) and max instance/abstract ids (the collision-safe allocation
    /// base). A malformed numbering part surfaces as <see cref="ComposePatchException"/> (Step-9.5 fix F4 —
    /// the package-level open is lazy, so bytes that passed the editable open can still fail the part parse
    /// here).
    /// </summary>
    private static CarrierNumberingScan ScanCarrierNumbering(byte[] carrierBytes)
    {
        try
        {
            using var stream = new MemoryStream(carrierBytes, writable: false);
            using var doc = WordprocessingDocument.Open(stream, isEditable: false);
            var scan = new CarrierNumberingScan();
            var numbering = doc.MainDocumentPart?.NumberingDefinitionsPart?.Numbering;
            if (numbering is null)
            {
                return scan;
            }

            foreach (var abstractNum in numbering.Elements<AbstractNum>())
            {
                scan.RecordAbstract(abstractNum);
            }
            foreach (var instance in numbering.Elements<NumberingInstance>())
            {
                scan.RecordInstance(instance);
            }
            return scan;
        }
        catch (Exception ex) when (ex is not ComposePatchException and not OutOfMemoryException)
        {
            throw new ComposePatchException(
                ComposePatchErrorKind.MalformedDocument,
                "The carrier .docx numbering part is not readable.",
                ex);
        }
    }

    /// <summary>
    /// Accumulates the list <c>w:num</c> instances a body render allocates: a single shared bullet instance
    /// (lazily) and one instance per restart-scoped ordered list. The heading instance (numId 1) is fixed and
    /// authored unconditionally, so it is not tracked here.
    /// </summary>
    private sealed class NumberingPlan
    {
        private int _nextNumId;

        /// <summary>Blank-package authoring — instances allocate from <see cref="FirstListNumInstanceId"/>.</summary>
        public NumberingPlan() : this(FirstListNumInstanceId) { }

        /// <summary>Task 011 (carrier mode): allocate instances from <paramref name="firstNumId"/> — set
        /// ABOVE the carrier's own max numId so a rendered list can never capture a carrier num definition.</summary>
        public NumberingPlan(int firstNumId) => _nextNumId = firstNumId;

        /// <summary>The allocated ordered-list instance ids, in allocation order (each restarts at 1).</summary>
        public List<int> OrderedInstanceIds { get; } = new();

        /// <summary>The shared bullet-list instance id, or null when no bullet list was rendered.</summary>
        public int? BulletInstanceId { get; private set; }

        /// <summary>Allocates a fresh ordered-list instance (a new numbered list that restarts at 1).</summary>
        public int NewOrderedInstance()
        {
            var id = _nextNumId++;
            OrderedInstanceIds.Add(id);
            return id;
        }

        /// <summary>Returns the shared bullet-list instance id, allocating it on first use.</summary>
        public int BulletInstance() => BulletInstanceId ??= _nextNumId++;
    }
}
