using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
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
    public byte[] SynthesizeDocument(ComposeContentModel model, string author, ICollection<ComposeProjectionWarning>? degradations = null)
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
            // Task 026: the state is ALSO the render-side degradation sink (state.Warn), so every silent
            // render drop — filtered anchors, dropped format-change records, unresolvable hrefs — is
            // counted and surfaced through the optional `degradations` out-collection.
            var plan = new NumberingPlan();
            var state = new ListRenderState(plan);
            // Task 012: user-edit revision facts arrive author-less by design — attribute the saving user.
            state.DefaultRevisionAuthor = author;

            // Step-9.5 F2: anchors may only reference ids the AUTHORED part will contain (deduped model
            // ids) — unmatched/out-of-range anchors drop rather than dangle as corrupting references.
            var renderBlocks = model.Blocks;
            if (ModelContainsCommentAnchor(renderBlocks))
            {
                renderBlocks = FilterCommentAnchors(renderBlocks, new HashSet<int>(model.Comments.Select(c => c.Id)), state);
            }

            RenderBlocks(body, renderBlocks, state);

            // G5 (FR-05, task 033): swap each BuildRun href-sentinel for a real EXTERNAL hyperlink
            // relationship on the main part (the part is in scope here; BuildRun was not). Before save.
            ResolveHyperlinkRelationships(body, mainPart, state);

            // Word requires a trailing sectPr for a valid single-section document.
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));

            AddStyleDefinitions(mainPart);
            AddNumberingDefinitions(mainPart, plan);
            EnsureCommentsPart(mainPart, model.Comments, state);

            // Mint a unique w14:paraId on every paragraph lacking a valid one — AFTER the body is fully built
            // so the dedup pass sees every client-carried id (mirrors ParaIdPreParser's collect-then-mint).
            AssignParaIds(body);

            AddCoreProperties(document, creator);
            mainPart.Document.Save();

            state.CopyDegradationsTo(degradations);
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
            // Task 025 note (Step-9.5 F9): AppendSection takes NO revision-id seed — its callers (the
            // Summary Page generator) author settled AI content and never send revision-carrying blocks.
            // If a future caller appends revisions, thread ScanCarrierRevisionIdSeed here like
            // RenderIntoCarrier (ids would otherwise mint from 1 against the target's existing ids —
            // Word-tolerated but collision-unclean).
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

            // Step-9.5 F2: AppendSection manages no comments part — anchor marker runs (never sent by its
            // callers) are stripped rather than emitted as dangling references.
            var appendBlocks = ModelContainsCommentAnchor(blocks)
                ? FilterCommentAnchors(blocks, new HashSet<int>())
                : blocks;
            RenderBlocks(body, appendBlocks, state);

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

            // E2 substrate: mint a fresh w14:paraId for every appended paragraph. An existing UNIQUE id is
            // never touched; a SOURCE-DUPLICATED id (e.g. identical ids in a construct's mc:Choice and
            // mc:Fallback copies - the NDA class) keeps its FIRST occurrence and re-mints the later ones,
            // so every package this method returns is anchorable (no duplicate splice keys) - the
            // dedup invariant ComposeSummaryPageSeamTests' ids-stripped exemption documents.
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
    public byte[] RenderIntoCarrier(byte[] carrierBytes, ComposeContentModel model, string author, ICollection<ComposeProjectionWarning>? degradations = null, bool mergeUnchangedBlocks = true, ComposeMergeStats? mergeStats = null)
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

            // ═══════════════════════════════════════════════════════════════════════════════════
            // THE BASE SIDE (ADR-049 R8 third amendment · task 040).
            //
            // `body.RemoveAllChildren()` below is the single instruction that cost the project 82% of its
            // untouched blocks: every tab stop, indent, style, spacing rule and numbering association in the
            // carrier was discarded here, and the body rebuilt from a content model carrying justification,
            // bold and italic.
            //
            // The merge does not change that control flow (ADR-049 I-5: ONE body author, and this is it). It
            // adds the BASE side R6 never had — the baseline's own blocks, captured before the swap and
            // re-projected server-side, so a block the user never touched is put back VERBATIM instead of
            // re-authored from a lossy model.
            //
            // Captured BEFORE the removal because RemoveAllChildren detaches the nodes.
            //
            // `mergeUnchangedBlocks` defaults to TRUE and is a TEST SEAM, not a feature flag: it is bound to
            // no configuration and exists so the seam measurement can run a control arm through the same
            // renderer in the same test run. An instrument that reports two different answers for two inputs
            // is measuring something; that is the anti-vacuity evidence behind the gate.
            var mergeBaseline = mergeUnchangedBlocks ? ComposeBlockMerge.Capture(body, carrierBytes) : null;
            if (mergeUnchangedBlocks && mergeBaseline is null)
            {
                // Fail OPEN: a baseline we cannot re-project simply gets no merge and the render proceeds
                // exactly as R6 does. A save is never refused because the base side was unavailable
                // (ADR-049 invariant 1 — every save terminates in a defined outcome).
                mergeStats?.RecordBaselineUnavailable();
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
            // Task 025: revision ids in the re-authored body seed ABOVE the carrier's existing ids
            // (read-only scan; skipped entirely for a revision-free model).
            var revisionIdSeed = ModelContainsRevision(model.Blocks) ? ScanCarrierRevisionIdSeed(carrierBytes) : 0;
            var plan = new NumberingPlan();
            var state = new ListRenderState(plan, revisionIdSeed: revisionIdSeed);
            var maxExistingAbstractId = 0;
            if (ModelContainsListItem(model.Blocks))
            {
                var carrierNumbering = ScanCarrierNumbering(carrierBytes);
                maxExistingAbstractId = carrierNumbering.MaxAbstractNumId;
                plan = new NumberingPlan(Math.Max(FirstListNumInstanceId, carrierNumbering.MaxNumId + 1));
                state = new ListRenderState(plan, carrierNumbering, revisionIdSeed);
            }
            // Task 012: user-edit revision facts arrive author-less by design — attribute the saving user.
            state.DefaultRevisionAuthor = author;

            // Step-9.5 F2 + task 012: anchors may only reference ids the target will actually contain —
            // the carrier part's own ids (scanned READ-ONLY from the bytes) PLUS any NEW model comments
            // (ids the carrier part lacks — session/advisory comments the client mapper folded into the
            // model), which are APPENDED to the part after the body render below. When the carrier has no
            // part at all, EnsureCommentsPart authors it from the model (every model comment is "new").
            // Unmatched/out-of-range anchors still drop rather than dangle as corrupting references.
            var renderBlocks = model.Blocks;
            var carrierComments = model.Comments.Count > 0 || ModelContainsCommentAnchor(renderBlocks)
                ? ScanCarrierComments(carrierBytes)
                : new Dictionary<int, string>();
            var carrierCommentIds = new HashSet<int>(carrierComments.Keys);
            var newComments = model.Comments.Where(c => !carrierCommentIds.Contains(c.Id)).ToList();

            // Task 013 (012-review F6): a model comment whose id the carrier ALREADY holds is treated as
            // the loaded round-trip of that carrier comment (not appended). If its text does not match
            // the carrier's (the projection's clamp means the model text may be a PREFIX), the id was
            // client-allocated onto a carrier comment the loaded model never carried (e.g. one the
            // projection flattened) - the anchor would bind to the WRONG comment. Warn LOUDLY; the
            // anchor still renders against the carrier comment (behavior unchanged), but the collision
            // is wire-visible instead of silent.
            foreach (var modelComment in model.Comments)
            {
                if (carrierComments.TryGetValue(modelComment.Id, out var carrierText))
                {
                    var modelText = modelComment.Text.Trim();
                    var knownText = carrierText.Trim();
                    if (modelText.Length > 0 && !knownText.StartsWith(modelText, StringComparison.Ordinal))
                    {
                        // NOTE (review P4): this also fires when a model comment EXTENDS the carrier's text.
                        // Comment-text editing does not exist in the editor today (the carrier part is
                        // authoritative for existing comments); if editing ever lands, this check must learn
                        // an identity-diff re-authoring path (notes S14.1) instead of warn-and-discard.
                        state.Warn("comment-id-collision");
                    }
                }
            }
            if (ModelContainsCommentAnchor(renderBlocks))
            {
                var validCommentIds = new HashSet<int>(carrierCommentIds);
                foreach (var newComment in newComments)
                {
                    validCommentIds.Add(newComment.Id);
                }
                renderBlocks = FilterCommentAnchors(renderBlocks, validCommentIds, state);
            }

            if (mergeBaseline is not null)
            {
                RenderMergedBlocks(body, renderBlocks, mergeBaseline, state, mergeStats);
            }
            else
            {
                RenderBlocks(body, renderBlocks, state);
            }

            ResolveHyperlinkRelationships(body, mainPart, state);

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

            // Task 024 + task 012: the CARRIER's comments part is authoritative for the comments it
            // already contains; NEW model comments (session/advisory, folded in by the client mapper) are
            // APPENDED to it (append-only — existing comment elements are never edited or removed; note
            // whole-part byte-identity therefore narrows to saves that add no new comments). A carrier
            // with no part at all gets one authored from the model (the original task-024 behavior).
            if (mainPart.WordprocessingCommentsPart is { } carrierCommentsPart)
            {
                if (newComments.Count > 0)
                {
                    AppendCommentsToPart(carrierCommentsPart, newComments, carrierCommentIds, state);
                }
            }
            else
            {
                EnsureCommentsPart(mainPart, model.Comments, state);
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

            state.CopyDegradationsTo(degradations);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Task 024: authors <c>word/comments.xml</c> from the model's comments — ONLY when the target has no
    /// comments part (the carrier's part is authoritative and preserved byte-identically; blank-package
    /// synthesize authors it fresh). Ids match the body anchors' <see cref="ComposeCommentAnchor.Id"/>;
    /// the raw authored Date string is re-emitted verbatim.
    /// </summary>
    private static void EnsureCommentsPart(MainDocumentPart mainPart, IReadOnlyList<ComposeComment> comments, ListRenderState? state = null)
    {
        if (comments.Count == 0 || mainPart.WordprocessingCommentsPart is not null)
        {
            return;
        }

        var part = mainPart.AddNewPart<WordprocessingCommentsPart>();
        var container = new Comments();
        var emittedIds = new HashSet<int>();
        foreach (var model in comments)
        {
            // Step-9.5 F2: duplicate ids collapse to first-wins (a duplicate would make every anchor for
            // the id ambiguous). Step-9.5 F1 (the recurring client-input class): every client-controlled
            // string is SANITIZED like body text — XML-illegal control chars would make the part
            // unserializable and throw out of the live save path; a Date that does not parse as
            // xsd:dateTime is OMITTED (projection-sourced dates parse; only client junk is dropped).
            if (!emittedIds.Add(model.Id))
            {
                // Task 026 (review F7): a duplicate-id comment is user-visible content collapsing
                // first-wins — counted on the degradation sink (was render-silent since 024).
                state?.Warn("comment-duplicate-dropped");
                continue;
            }
            container.AppendChild(BuildCommentElement(model));
        }
        part.Comments = container;
        part.Comments.Save();
    }

    /// <summary>Task 012 (extracted from <see cref="EnsureCommentsPart"/> for the carrier-append seam):
    /// authors ONE sanitized <c>w:comment</c> element from a model comment. Step-9.5 F1 (the recurring
    /// client-input class): every client-controlled string is SANITIZED like body text — XML-illegal
    /// control chars would make the part unserializable and throw out of the live save path; a Date that
    /// does not pass the xsd:dateTime LEXICAL gate is OMITTED (a TryParse-able culture date like
    /// "08/01/2026" is still a schema-invalid <c>@w:date</c> — 025-F3 same-class posture; only client
    /// junk is dropped, projection-sourced dates parse).</summary>
    private static Comment BuildCommentElement(ComposeComment model)
    {
        var comment = new Comment
        {
            Id = model.Id.ToString(CultureInfo.InvariantCulture),
            Author = SanitizeText(model.Author),
        };
        if (!string.IsNullOrEmpty(model.Initials))
        {
            comment.Initials = SanitizeText(model.Initials);
        }
        if (NormalizeXsdDateTime(model.Date) is { } validCommentDate)
        {
            comment.Date = new DateTimeValue { InnerText = validCommentDate };
        }
        var text = model.Text.Replace("\r\n", "\n").Replace('\r', '\n'); // F10: normalize client CRLF
        foreach (var line in text.Split('\n'))
        {
            comment.AppendChild(new Paragraph(new Run(new Text(SanitizeText(line)) { Space = SpaceProcessingModeValues.Preserve })));
        }
        return comment;
    }

    /// <summary>
    /// Task 012 (the client cutover): APPENDS model comments the carrier part does not already contain —
    /// NEW session/advisory comments the client folded into the model — to the EXISTING carrier comments
    /// part. Append-only: the carrier's own comment elements are never edited or removed (the task-024
    /// byte-identity contract narrows to no-new-comments saves, since an append necessarily
    /// re-serializes the part; existing comment CONTENT is preserved either way). Duplicate ids in the
    /// new set collapse first-wins onto the degradation sink, mirroring <see cref="EnsureCommentsPart"/>.
    /// </summary>
    private static void AppendCommentsToPart(
        WordprocessingCommentsPart part,
        IReadOnlyList<ComposeComment> newComments,
        IReadOnlySet<int> existingIds,
        ListRenderState? state = null)
    {
        var container = part.Comments ??= new Comments();
        var emittedIds = new HashSet<int>(existingIds);
        var appended = false;
        foreach (var model in newComments)
        {
            if (!emittedIds.Add(model.Id))
            {
                state?.Warn("comment-duplicate-dropped");
                continue;
            }
            container.AppendChild(BuildCommentElement(model));
            appended = true;
        }
        if (appended)
        {
            container.Save();
        }
    }

    /// <summary>
    /// Step-9.5 F2: the set of comment ids a render may legitimately ANCHOR — the target's own part ids
    /// (carrier mode; scanned READ-ONLY from the carrier bytes, never the editable package) or the
    /// deduped model ids (blank-package mode, where <see cref="EnsureCommentsPart"/> authors the part).
    /// Anchors outside this set are DROPPED by <see cref="FilterCommentAnchors"/> — an orphan
    /// <c>w:commentReference</c> would corrupt the document (Word repair prompt). Comparison is by
    /// PARSED value (OOXML <c>w:id</c> is ST_DecimalNumber — integer value semantics; "01" == "1").
    /// </summary>
    /// <summary>Task 013 (012-review F6): the carrier comments scan also carries each comment's plain
    /// text (paragraphs joined by <c>\n</c> - the SAME join the projection uses for
    /// <c>ComposeComment.Text</c>) so the collision check can compare a model comment against the
    /// carrier comment its id points at. First-wins on duplicate ids (mirrors the anchor semantics).
    /// Unreadable part degrades to empty (anchors drop rather than dangle - unchanged posture).</summary>
    private static Dictionary<int, string> ScanCarrierComments(byte[] carrierBytes)
    {
        try
        {
            return ScanCarrierBytes(carrierBytes, doc =>
            {
                var byId = new Dictionary<int, string>();
                var comments = doc.MainDocumentPart?.WordprocessingCommentsPart?.Comments;
                if (comments is not null)
                {
                    foreach (var comment in comments.Elements<Comment>())
                    {
                        if (int.TryParse(comment.Id?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                            && !byId.ContainsKey(id))
                        {
                            byId[id] = string.Join("\n", comment.Elements<Paragraph>().Select(p => p.InnerText));
                        }
                    }
                }
                return byId;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static bool ModelContainsCommentAnchor(IReadOnlyList<ComposeBlock> blocks) =>
        blocks.Any(b => b.Runs.Any(r => r.CommentAnchor is not null)
            || (b.Table?.Rows.Any(row => row.Cells.Any(c => ModelContainsCommentAnchor(c.Blocks))) ?? false));

    /// <summary>Step-9.5 F2: drops anchor marker runs whose id is not in <paramref name="validIds"/> or
    /// whose Kind is out of range (JsonStringEnumConverter accepts raw integers). Returns the SAME
    /// instances when nothing needs filtering. Task 026: every dropped anchor is COUNTED on
    /// <paramref name="state"/> (<c>comment-anchor-dropped</c> — the 024-routed loud counter); null state
    /// (AppendSection's by-design strip of anchors its callers never send) records nothing.</summary>
    private static IReadOnlyList<ComposeBlock> FilterCommentAnchors(IReadOnlyList<ComposeBlock> blocks, IReadOnlySet<int> validIds, ListRenderState? state = null)
    {
        static bool IsInvalid(ComposeInlineRun run, IReadOnlySet<int> valid) =>
            run.CommentAnchor is { } anchor
            && (anchor.Kind is not (ComposeCommentAnchorKind.Start or ComposeCommentAnchorKind.End)
                || !valid.Contains(anchor.Id));

        List<ComposeBlock>? rebuilt = null;
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var filtered = block;
            var invalidCount = block.Runs.Count(r => IsInvalid(r, validIds));
            if (invalidCount > 0)
            {
                state?.Warn("comment-anchor-dropped", invalidCount);
                filtered = block with { Runs = block.Runs.Where(r => !IsInvalid(r, validIds)).ToList() };
            }
            if (block.Table is { } table)
            {
                List<ComposeTableRow>? newRows = null;
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = table.Rows[rowIndex];
                    List<ComposeTableCell>? newCells = null;
                    for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                    {
                        var cell = row.Cells[cellIndex];
                        var newBlocks = FilterCommentAnchors(cell.Blocks, validIds, state);
                        var newCell = ReferenceEquals(newBlocks, cell.Blocks) ? cell : cell with { Blocks = newBlocks };
                        if (!ReferenceEquals(newCell, cell) && newCells is null)
                        {
                            newCells = new List<ComposeTableCell>(row.Cells.Take(cellIndex));
                        }
                        newCells?.Add(newCell);
                    }
                    var newRow = newCells is null ? row : row with { Cells = newCells };
                    if (!ReferenceEquals(newRow, row) && newRows is null)
                    {
                        newRows = new List<ComposeTableRow>(table.Rows.Take(rowIndex));
                    }
                    newRows?.Add(newRow);
                }
                if (newRows is not null)
                {
                    filtered = filtered with { Table = table with { Rows = newRows } };
                }
            }
            if (!ReferenceEquals(filtered, block) && rebuilt is null)
            {
                rebuilt = new List<ComposeBlock>(blocks.Take(i));
            }
            rebuilt?.Add(filtered);
        }
        return rebuilt ?? blocks;
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

    private void RenderBlocks(OpenXmlElement container, IReadOnlyList<ComposeBlock> blocks, ListRenderState state, IDictionary<int, int>? runCursor = null)
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
        // Task 040: the merge owns ONE cursor for the whole body and passes it here, so ordered-list run
        // continuity spans cloned blocks and multiple calls. Every other caller passes null and gets the
        // previous per-call behaviour — a table-cell boundary still starts fresh, which is the contract.
        var orderedRunByLevel = runCursor ?? new Dictionary<int, int>();

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
                    container.AppendChild(BuildHeading(block, state));
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
                    container.AppendChild(BuildListItem(block, effectiveNumId, state));
                    break;

                case ComposeBlockKind.ListItem: // bullet
                    var bulletLevel = Math.Clamp(block.Level, 0, 8);
                    orderedRunByLevel.Remove(bulletLevel);
                    CloseRunsDeeperThan(bulletLevel);
                    container.AppendChild(BuildListItem(block, state.ResolveBullet(block.NumId, bulletLevel), state));
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
                    container.AppendChild(BuildParagraph(block, state));
                    break;
            }
        }
    }

    /// <summary>The nearest ACTIVE ordered run shallower than <paramref name="level"/> (a NumId-less
    /// nested ordered item joins its parent's instance at a deeper ilvl — Word's multi-level idiom).</summary>
    private static bool TryNearestShallowerRun(IDictionary<int, int> orderedRunByLevel, int level, out int numId)
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

    private static Paragraph BuildParagraph(ComposeBlock block, ListRenderState state)
    {
        var pPr = new ParagraphProperties();
        ApplyPageBreakBefore(pPr, block);
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block, state);
    }

    private static Paragraph BuildHeading(ComposeBlock block, ListRenderState state)
    {
        var level = Math.Clamp(block.Level <= 0 ? 1 : block.Level, 1, MaxHeadingLevel);
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = HeadingStyleId(level) });
        // NO w:numPr here — the number is supplied by the Heading{level} STYLE's numPr (style-linked). A
        // direct numId here would double-number (FR-27).
        ApplyPageBreakBefore(pPr, block);
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block, state);
    }

    private static Paragraph BuildListItem(ComposeBlock block, int numInstanceId, ListRenderState state)
    {
        var ilvl = Math.Clamp(block.Level, 0, 8);
        // CT_PPr order: pStyle < pageBreakBefore < numPr < jc — built sequentially in that order.
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = ListParagraphStyleId });
        ApplyPageBreakBefore(pPr, block);
        // DIRECT numPr: ListParagraph carries no style numbering, so this is not double-numbering.
        pPr.AppendChild(new NumberingProperties(
            new NumberingLevelReference { Val = ilvl },
            new NumberingId { Val = numInstanceId }));
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block, state);
    }

    /// <summary>Task 023: <c>w:pageBreakBefore</c> — appended in CT_PPr order (after <c>pStyle</c>, before
    /// <c>numPr</c>/<c>jc</c>; each builder calls this at exactly that point in its sequential build).</summary>
    private static void ApplyPageBreakBefore(ParagraphProperties pPr, ComposeBlock block)
    {
        if (block.PageBreakBefore)
        {
            pPr.AppendChild(new PageBreakBefore());
        }
    }

    /// <summary>Assembles a paragraph from its <paramref name="pPr"/> + the block's inline runs, carrying a
    /// client paraId when the model supplied a valid one (else left for <see cref="AssignParaIds"/>).
    /// Task 025: also authors the paragraph's tracked-change markup — the MARK revision
    /// (<c>w:pPr/w:rPr/w:ins|w:del</c>), the paragraph-formatting change (<c>w:pPrChange</c>, appended
    /// LAST in CT_PPr order), and run-level <c>w:ins</c>/<c>w:del</c> wrappers GROUPING consecutive runs
    /// with the same revision identity (one wrapper, one server-minted id). Comment anchors always emit
    /// at paragraph level, closing any open wrapper.</summary>
    private static Paragraph AssembleParagraph(ParagraphProperties pPr, ComposeBlock block, ListRenderState state)
    {
        // Mark revision: w:pPr/w:rPr/w:ins|w:del — CT_PPr puts rPr after jc and before sectPr/pPrChange,
        // which is exactly this append position (every builder appended its jc already).
        if (block.MarkRevision is { } markRev)
        {
            OpenXmlElement markChange = markRev.Kind == ComposeRevisionKind.Inserted
                ? new Inserted { Id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture), Author = ResolveRevisionAuthorValue(markRev.Author, state), Date = TryValidRevisionDate(markRev.Date) }
                : new Deleted { Id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture), Author = ResolveRevisionAuthorValue(markRev.Author, state), Date = TryValidRevisionDate(markRev.Date) };
            pPr.AppendChild(new ParagraphMarkRunProperties(markChange));
        }

        // Paragraph-formatting change: w:pPrChange (LAST in CT_PPr). Schema requires the previous-pPr
        // child, so the whole record is dropped when the opaque carry is missing or fails the SDK parse
        // gate (client junk never reaches the package; the current formatting simply stands) — counted
        // on the render degradation sink (task 026; was render-silent, 025-F4/F7 routing).
        if (block.PropertiesChange is { } propsChange)
        {
            if (TryParsePreviousProperties<ParagraphPropertiesExtended>(propsChange.PreviousPropertiesXml) is { } previousPPr)
            {
                pPr.AppendChild(new ParagraphPropertiesChange(previousPPr)
                {
                    Id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture),
                    Author = ResolveRevisionAuthorValue(propsChange.Author, state),
                    Date = TryValidRevisionDate(propsChange.Date),
                });
            }
            else
            {
                state.Warn("tracked-format-change-dropped");
            }
        }

        var paragraph = new Paragraph();
        CarryClientParaId(paragraph, block.ParaId);
        paragraph.AppendChild(pPr);

        if (block.Runs.Count == 0)
        {
            // An empty paragraph is valid (Word renders a blank line). No run needed.
            return paragraph;
        }

        // Task 025 revision grouping: consecutive runs with the same revision identity share ONE
        // w:ins/w:del wrapper (record value equality on kind+author+date — adjacent same-identity source
        // wrappers legitimately merge; Word renders them identically).
        OpenXmlElement? wrapper = null;
        ComposeRevision? wrapperRevision = null;

        void CloseWrapper()
        {
            wrapper = null;
            wrapperRevision = null;
        }

        foreach (var run in block.Runs)
        {
            // Task 024: a comment-anchor marker run emits the anchor markup instead of a text run —
            // rangeStart, or rangeEnd IMMEDIATELY followed by the folded commentReference run (Word's
            // canonical adjacency; the reference is what makes the comment visible). Anchors never join a
            // revision wrapper (they are emitted at paragraph level; ComposeInlineRun.Revision contract).
            if (run.CommentAnchor is { } anchor)
            {
                CloseWrapper();
                var anchorId = anchor.Id.ToString(CultureInfo.InvariantCulture);
                if (anchor.Kind == ComposeCommentAnchorKind.Start)
                {
                    paragraph.AppendChild(new CommentRangeStart { Id = anchorId });
                }
                else
                {
                    paragraph.AppendChild(new CommentRangeEnd { Id = anchorId });
                    paragraph.AppendChild(new Run(new CommentReference { Id = anchorId }));
                }
                continue;
            }

            if (run.Revision is not { } revision)
            {
                CloseWrapper();
                paragraph.AppendChild(BuildRun(run, state));
                continue;
            }

            // Step-9.5 F1: a REVISED LINKED run authors Word's canonical nesting — w:hyperlink OUTSIDE,
            // w:ins/w:del INSIDE (CT_RunTrackChange does not admit w:hyperlink; the reverse nesting is
            // schema-invalid and risks Word's repair prompt). The hyperlink boundary always breaks
            // wrapper grouping.
            if (!string.IsNullOrWhiteSpace(run.Href))
            {
                CloseWrapper();
                OpenXmlElement linkedWrapper = NewRevisionWrapper(revision, state);
                linkedWrapper.AppendChild(BuildRun(run with { Href = null }, state, deleted: revision.Kind == ComposeRevisionKind.Deleted));
                paragraph.AppendChild(new Hyperlink(linkedWrapper) { Id = HyperlinkPendingIdPrefix + run.Href!.Trim() });
                continue;
            }

            if (wrapper is null || wrapperRevision != revision)
            {
                wrapper = NewRevisionWrapper(revision, state);
                wrapperRevision = revision;
                paragraph.AppendChild(wrapper);
            }

            // A Deleted run's text authors as w:delText (Word's requirement for pending-deleted content).
            wrapper.AppendChild(BuildRun(run, state, deleted: revision.Kind == ComposeRevisionKind.Deleted));
        }

        return paragraph;
    }

    /// <summary>Task 012: revision-author resolution — a fact that carries an author keeps it
    /// (imported revisions round-trip their true authors); an EMPTY author falls back to the save-time
    /// authenticated author (<see cref="ListRenderState.DefaultRevisionAuthor"/> — the client mapper
    /// omits the author on user-edit revisions), then to the sanitizer's "Unknown" floor.</summary>
    private static string ResolveRevisionAuthorValue(string? factAuthor, ListRenderState state)
    {
        // Sanitize FIRST, then decide: a control-chars-only author (hostile client input) must take the
        // fallback exactly like an absent one — checking IsNullOrWhiteSpace on the RAW value would let
        // it bypass the fallback and land on the "Unknown" floor instead of the saving user.
        var sanitized = SanitizeText(factAuthor ?? string.Empty).Trim();
        return SanitizeRevisionAuthor(sanitized.Length > 0 ? sanitized : state.DefaultRevisionAuthor);
    }

    private static OpenXmlElement NewRevisionWrapper(ComposeRevision revision, ListRenderState state)
    {
        var id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture);
        var author = ResolveRevisionAuthorValue(revision.Author, state);
        var date = TryValidRevisionDate(revision.Date);
        return revision.Kind == ComposeRevisionKind.Inserted
            ? new InsertedRun { Id = id, Author = author, Date = date }
            : new DeletedRun { Id = id, Author = author, Date = date };
    }

    // G5 (FR-05, task 033): sentinel prefix stashing a run's href on the temporary Hyperlink.Id during the
    // static body build (which has no MainDocumentPart in hand). ResolveHyperlinkRelationships replaces
    // each sentinel with a real EXTERNAL relationship id BEFORE the document is saved — the sentinel never
    // persists. A prefix that can never collide with a real OOXML relationship id (rId…).
    private const string HyperlinkPendingIdPrefix = "COMPOSE_PENDING_HREF:";

    private static OpenXmlElement BuildRun(ComposeInlineRun run, ListRenderState state, bool deleted = false)
    {
        // Task 023: a page-break run IS the break — every other field is ignored by contract
        // (ComposeInlineRun.IsPageBreak). Same markup AppendSection's page-broken section uses.
        // (Inside a w:ins/w:del wrapper the bare break run is schema-legal — no delText involved.)
        if (run.IsPageBreak)
        {
            return new Run(new Break { Type = BreakValues.Page });
        }

        var element = new Run();
        // Task 025: a tracked run-formatting change (w:rPrChange) forces an rPr even on an unmarked run —
        // the change record lives inside it (LAST in CT_RPr order). A record whose opaque carry fails the
        // parse gate drops — counted on the render degradation sink (task 026).
        var formatChange = run.FormatChange is { } change
            ? (Change: change, Previous: TryParsePreviousProperties<PreviousRunProperties>(change.PreviousPropertiesXml))
            : ((ComposeFormatChange Change, PreviousRunProperties? Previous)?)null;
        if (formatChange is { Previous: null })
        {
            state.Warn("tracked-format-change-dropped");
        }
        if (run.Bold || run.Italic || run.Underline || formatChange?.Previous is not null)
        {
            var rPr = new RunProperties();
            if (run.Bold) rPr.AppendChild(new Bold());
            if (run.Italic) rPr.AppendChild(new Italic());
            if (run.Underline) rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
            if (formatChange is { Previous: { } previousRPr } fc)
            {
                // Same drop-on-parse-failure posture as pPrChange: schema requires the previous-rPr
                // child, so an invalid opaque carry drops the whole record (formatting stands as-is).
                rPr.AppendChild(new RunPropertiesChange(previousRPr)
                {
                    Id = state.NextRevisionId().ToString(CultureInfo.InvariantCulture),
                    Author = ResolveRevisionAuthorValue(fc.Change.Author, state),
                    Date = TryValidRevisionDate(fc.Change.Date),
                });
            }
            element.AppendChild(rPr);
        }

        // Pending-deleted content authors as w:delText (Word rejects w:t inside w:del).
        //
        // Task 041 investigated emitting `xml:space="preserve"` only when the text NEEDS it (leading or
        // trailing whitespace, or empty). The `p/r/t` difference class on five corpus documents is exactly
        // this attribute: the text was character-identical and only the attribute had been added.
        //
        // REVERTED — the conditional rule was measurably WORSE. Word emits `xml:space="preserve"` far more
        // liberally than "the text has edge whitespace", so matching that narrow rule made the renderer
        // disagree with the source on 15 of 18 documents instead of 5. Emitting it unconditionally is safe
        // (it only ever suppresses whitespace trimming) and agrees with the corpus more often.
        //
        // The residual `p/r/t` differences are therefore attribute-PRESENCE, not text loss — verified by
        // inspecting the fixtures. Recorded on the loss list so the class is not re-investigated as if it
        // were content.
        OpenXmlElement textElement = deleted
            ? new DeletedText(SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve }
            : new Text(SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve };
        element.AppendChild(textElement);

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
    private static void ResolveHyperlinkRelationships(Body body, MainDocumentPart mainPart, ListRenderState? state = null)
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
                // replacing the hyperlink wrapper with its inline children, and drop only the link —
                // counted on the render degradation sink (task 026; was render-silent).
                state?.Warn("hyperlink-target-dropped");
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

        // Source-faithful vs born-in-editor cell chrome follows the table's own tri-state discriminator
        // (Borders — see BuildTableProperties): in source-faithful mode a vAlign-less cell emits NOTHING
        // (the table style chain governs, review 022-F4); in legacy mode it keeps the center chrome.
        var sourceFaithful = model.Borders is not null;

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
                tableRow.AppendChild(BuildTableCell(cell, state, sourceFaithful));
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
        // Review 022-F1: the model reaches this method from CLIENT JSON (ComposeService save path), so the
        // token must be validated — a null (System.Text.Json ignores the C# default under NRT) or garbage
        // token would emit schema-invalid XML or throw out of the save path. Coerce to the safe visible
        // default rather than fail the save.
        var val = string.IsNullOrEmpty(edge.Val) ? BorderValues.Single : new BorderValues(edge.Val);
        if (!((IEnumValue)val).IsValid)
        {
            val = BorderValues.Single;
        }
        var element = new TEdge { Val = val };
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

    private static TableWidthUnitValues MapWidthType(string? type) => type?.ToLowerInvariant() switch
    {
        "dxa" => TableWidthUnitValues.Dxa,
        "pct" => TableWidthUnitValues.Pct,
        "nil" => TableWidthUnitValues.Nil,
        _ => TableWidthUnitValues.Auto, // unknown/garbage client token coerces to auto (022-F8)
    };

    private TableCell BuildTableCell(ComposeTableCell cell, ListRenderState state, bool sourceFaithful = false)
    {
        var tableCell = new TableCell();
        tableCell.AppendChild(BuildCellProperties(cell, sourceFaithful));

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

    /// <summary>Task 022 — CT_TcPr schema order: tcW, gridSpan, vMerge, vAlign. vAlign emission (review
    /// 022-F4): an explicit model value is always emitted (case-insensitive, unknown → top); a NULL value
    /// emits NOTHING in source-faithful mode (the table style chain governs, exactly as in the source) and
    /// keeps the legacy <c>center</c> chrome in born-in-editor mode.</summary>
    private static TableCellProperties BuildCellProperties(ComposeTableCell cell, bool sourceFaithful)
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
        if (cell.VerticalAlignment is { } vAlign)
        {
            tcPr.AppendChild(new TableCellVerticalAlignment
            {
                Val = vAlign.ToLowerInvariant() switch
                {
                    "center" => TableVerticalAlignmentValues.Center,
                    "bottom" => TableVerticalAlignmentValues.Bottom,
                    _ => TableVerticalAlignmentValues.Top,
                },
            });
        }
        else if (!sourceFaithful)
        {
            tcPr.AppendChild(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
        }
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
        // Task 040 investigated excluding paragraphs inside opaque regions (`mc:AlternateContent`,
        // `w:txbxContent`) from this pass, because entering them MUTATES a block the merge cloned verbatim:
        // Word writes the same box twice (Choice + Fallback) carrying the SAME w14:paraId, pass 1 treats the
        // second copy as a malformed duplicate, and re-mints it. Measured cost at the STRICT comparison level:
        // alternate-content-duplicate-paraid.docx 66.67%, AppligentNDA_Signed.docx 95.92% (both 100% LENIENT
        // — content is preserved; only identity churns).
        //
        // REVERTED, deliberately. Excluding them breaks task 011's global-paraId-uniqueness guarantee, which
        // RenderOnSaveSeamTests pins by name on the NDA's 2BBF07C9/CA/CB class — duplicate anchors were part
        // of the production-422 failure chain. Strict is a no-regression RATCHET, not a gate (task 031 T5),
        // and both documents clear it by a wide margin; trading a safety invariant for a better number on a
        // non-gating metric is the exact move the ADR-049 paired-MUST exists to forbid. The residual is on the
        // task-045 loss list with this reasoning; resolving it properly means changing what the identity map
        // considers a block, which is not a rendering change.
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

    // ── task 025: tracked-change authoring hardening ─────────────────────────────────────────────
    // Client-posted revision attribution is CLIENT INPUT reaching OOXML authoring — the recurring
    // review-finding class (021-F1 / 022-F1 / 024-F1). Everything below is gated AT AUTHORING:
    // author sanitized + clamped (never empty — @w:author is schema-required), date parse-gated
    // (@w:date is optional; junk is omitted), previous-properties XML parsed through the typed SDK and
    // schema-validated (never string-injected), revision ids ALWAYS server-minted.

    private const int MaxRevisionAuthorChars = 255;
    private const int MaxPreviousPropertiesXmlChars = 32 * 1024;

    // Step-9.5 F3: the xsd:dateTime LEXICAL forms (K covers Z / ±hh:mm / no-zone). A merely
    // DateTime.TryParse-able string ("08/01/2026") is NOT a valid @w:date and must be dropped.
    private static readonly string[] XsdDateTimeFormats =
    {
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
    };

    /// <summary>Step-9.5 F3/F5: returns <paramref name="raw"/> when it is a valid <c>xsd:dateTime</c>
    /// lexical form (kept RAW for byte-faithful re-authoring), else null. Used at BOTH ends: the
    /// projection normalizes captured dates through this (so the model is canonical and the render
    /// fixed point holds for degenerate source attribution), and the render gate drops anything a
    /// client posted that would be a schema-invalid <c>@w:date</c>.</summary>
    internal static string? NormalizeXsdDateTime(string? raw) =>
        !string.IsNullOrEmpty(raw)
            && DateTime.TryParseExact(raw, XsdDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? raw
            : null;

    private static string SanitizeRevisionAuthor(string? author)
    {
        var sanitized = SanitizeText(author ?? string.Empty).Trim();
        if (sanitized.Length == 0)
        {
            return "Unknown";
        }
        return sanitized.Length <= MaxRevisionAuthorChars ? sanitized : sanitized[..MaxRevisionAuthorChars];
    }

    /// <summary>Emits the revision date only when the raw string is a valid <c>xsd:dateTime</c> lexical
    /// form (Step-9.5 F3 — strictly tighter than the 024 comments-part <c>TryParse</c> gate, which admits
    /// culture formats that are schema-invalid as <c>@w:date</c>); junk is omitted (the attribute is
    /// schema-optional). The RAW string is kept for byte-faithful re-authoring.</summary>
    private static DateTimeValue? TryValidRevisionDate(string? date) =>
        NormalizeXsdDateTime(date) is { } valid ? new DateTimeValue { InnerText = valid } : null;

    /// <summary>
    /// The <see cref="ComposeFormatChange.PreviousPropertiesXml"/> gate: parses the opaque carry through
    /// the TYPED SDK class — the generated ctor VALIDATES the root element (name + namespace; a
    /// wrong-root or malformed fragment throws <c>ArgumentException</c>, and DTDs are prohibited by the
    /// SDK reader) — then schema-validates the parsed subtree; any failure drops the whole change record
    /// (the current formatting simply stands; equivalent to accepting the formatting change). Never
    /// string-injection into the package. Size-clamped against hostile payloads. The validator is
    /// per-call — <c>OpenXmlValidator</c> instance thread-safety is not contractually guaranteed and
    /// this runs on concurrent request paths (Step-9.5 F10); a subtree validation is cheap.
    /// </summary>
    private static T? TryParsePreviousProperties<T>(string? xml) where T : OpenXmlElement
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxPreviousPropertiesXmlChars)
        {
            return null;
        }
        try
        {
            var element = (T)Activator.CreateInstance(typeof(T), xml)!;
            _ = element.OuterXml; // force the lazy parse before validation
            return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(element).Any() ? null : element;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>P-2 (020-review ledger, extracted at task 012): the ONE read-only side-open every
    /// carrier scan uses — never the editable package, whose parts would be marked for autoSave
    /// re-serialization by the mere read (the 011-T2 preserve-parts hazard). Exception POLICY deliberately
    /// stays with each caller (fallback-to-empty vs typed <see cref="ComposePatchException"/>): the
    /// duplication this removes is the open preamble, not the divergent failure semantics.</summary>
    private static T ScanCarrierBytes<T>(byte[] carrierBytes, Func<WordprocessingDocument, T> scan)
    {
        using var stream = new MemoryStream(carrierBytes, writable: false);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);
        return scan(doc);
    }

    /// <summary>Whether any block carries task-025 revision facts (triggers the carrier revision-id seed
    /// scan — a revision-free render skips it entirely).</summary>
    private static bool ModelContainsRevision(IReadOnlyList<ComposeBlock> blocks) =>
        blocks.Any(b => b.MarkRevision is not null || b.PropertiesChange is not null
            || b.Runs.Any(r => r.Revision is not null || r.FormatChange is not null)
            || (b.Table?.Rows.Any(row => row.Cells.Any(c => ModelContainsRevision(c.Blocks))) ?? false));

    /// <summary>
    /// Task 025: the collision base for re-authored revision ids — the max revision <c>w:id</c> across the
    /// carrier's parts (body included: preserved headers/footers/notes may carry revisions, and seeding
    /// above the old body's ids costs nothing). READ-ONLY side open of the bytes, same discipline as
    /// <see cref="ScanCarrierNumbering"/> / <see cref="ScanCarrierComments"/>. Mirrors the R5 engine's
    /// <c>SeedRevisionId</c>. Unreadable carrier → 0 (blank-package posture).
    /// </summary>
    private static int ScanCarrierRevisionIdSeed(byte[] carrierBytes)
    {
        try
        {
            return ScanCarrierBytes(carrierBytes, doc =>
            {
                var main = doc.MainDocumentPart;
                if (main is null)
                {
                    return 0;
                }

                var max = 0;
                void Scan(OpenXmlElement? root)
                {
                    if (root is null) return;
                    foreach (var element in root.Descendants())
                    {
                        var id = element switch
                        {
                            InsertedRun ins => ins.Id?.Value,
                            DeletedRun del => del.Id?.Value,
                            MoveFromRun mf => mf.Id?.Value,
                            MoveToRun mt => mt.Id?.Value,
                            Inserted i => i.Id?.Value,
                            Deleted d => d.Id?.Value,
                            RunPropertiesChange rc => rc.Id?.Value,
                            ParagraphPropertiesChange pc => pc.Id?.Value,
                            CellInsertion ci => ci.Id?.Value,
                            CellDeletion cd => cd.Id?.Value,
                            // Step-9.5 F8: the remaining *Change family preserved parts can carry.
                            SectionPropertiesChange sc => sc.Id?.Value,
                            TablePropertiesChange tpc => tpc.Id?.Value,
                            TableRowPropertiesChange trc => trc.Id?.Value,
                            TableCellPropertiesChange tcc => tcc.Id?.Value,
                            TableGridChange tgc => tgc.Id?.Value,
                            _ => null,
                        };
                        if (id is not null
                            && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                            && value > max)
                        {
                            max = value;
                        }
                    }
                }

                Scan(main.Document);
                foreach (var header in main.HeaderParts) Scan(header.Header);
                foreach (var footer in main.FooterParts) Scan(footer.Footer);
                Scan(main.FootnotesPart?.Footnotes);
                Scan(main.EndnotesPart?.Endnotes);
                // Step-9.5 F8: comment TEXT can itself carry tracked changes; the part is preserved
                // byte-identically (task 024), so its ids join the collision base too.
                Scan(main.WordprocessingCommentsPart?.Comments);
                return max;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }
    }

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
        private int _revisionId;

        public ListRenderState(NumberingPlan plan, CarrierNumberingScan? carrier = null, int revisionIdSeed = 0)
        {
            Plan = plan;
            _carrier = carrier;
            _revisionId = revisionIdSeed;
        }

        public NumberingPlan Plan { get; }

        /// <summary>Task 012: the save-time authenticated author — the FALLBACK identity for any
        /// revision/format-change fact whose Author is empty. The client mapper deliberately OMITS the
        /// author on user-edit revision facts so the server (never the client) attributes the saving
        /// user; a fact that CARRIES an author (imported revisions) keeps it. Raw — sanitized at the
        /// emission sites via <see cref="ResolveRevisionAuthorValue"/>.</summary>
        public string? DefaultRevisionAuthor { get; set; }

        /// <summary>Task 025: mints the next revision <c>w:id</c> — monotonic per render, seeded above the
        /// carrier's existing revision ids (<see cref="ScanCarrierRevisionIdSeed"/>) so re-authored body
        /// revisions never collide with ids in preserved parts (headers/footers). ALWAYS server-minted —
        /// the model deliberately carries no revision id (client input never reaches <c>w:id</c>).</summary>
        public int NextRevisionId() => ++_revisionId;

        // Task 026: the render-side DEGRADATION SINK — the renderer's analog of the projection's
        // ctx.AddWarning (codes + counts only; Tier-1 safe, no document content). Anything the render
        // DROPS (filtered anchors, failed format-change parse gates, unresolvable hrefs) is counted here
        // and surfaced through the public methods' optional out-collection, so a save can report
        // success-with-warnings instead of degrading silently.
        private readonly Dictionary<string, int> _degradations = new();

        public void Warn(string code, int count = 1)
        {
            _degradations[code] = _degradations.TryGetValue(code, out var existing) ? existing + count : count;
        }

        public void CopyDegradationsTo(ICollection<ComposeProjectionWarning>? sink)
        {
            if (sink is null) return;
            foreach (var (code, count) in _degradations)
            {
                sink.Add(new ComposeProjectionWarning(code, count));
            }
        }

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
            return ScanCarrierBytes(carrierBytes, doc =>
            {
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
            });
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

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // THE MERGE, EXECUTED (ADR-049 R8 third amendment · task 040).
    //
    // Not a second body author (ADR-049 I-5): this method lives in the renderer, appends into the same
    // `body`, and shares the same `ListRenderState` as `RenderBlocks`, which it delegates to for every block
    // it does not clone. `ComposeBlockMerge` decides; this executes. One component writes body children.
    //
    //   cloned  -> the baseline's own subtree, appended verbatim, with ZERO property logic. Nothing is
    //              re-derived, so nothing can be lost (invariant 7).
    //   rendered-> from the model, inheriting the base counterpart's unmodeled properties (FR-A04).
    //   no base -> from the model alone. An inserted block has no base side; that is not a failure.
    // ═══════════════════════════════════════════════════════════════════════════════════════════
    private void RenderMergedBlocks(
        Body body,
        IReadOnlyList<ComposeBlock> posted,
        ComposeMergeBaseline baseline,
        ListRenderState state,
        ComposeMergeStats? stats)
    {
        var steps = ComposeBlockMerge.Plan(posted, baseline, stats);

        // ONE ordered-list run cursor for the entire body, observed by cloned and rendered blocks alike.
        // The task-030 prototype batched renders and let each batch start a fresh cursor, so a rendered list
        // item following cloned list items restarted at 1 (its limitation 3). Sharing the cursor — and
        // recording every cloned block into it — is what makes a cloned list and a rendered continuation of
        // it number as one list.
        var runCursor = new Dictionary<int, int>();
        var single = new ComposeBlock[1];

        foreach (var step in steps)
        {
            if (step.Action == ComposeMergeAction.Clone)
            {
                var clone = baseline.Blocks[step.BaseIndex].CloneNode(true);
                body.AppendChild(clone);
                ComposeBlockMerge.ObserveClonedBlock(clone, runCursor);
                continue;
            }

            // Rendered one block at a time so the just-appended element can be identified for property
            // inheritance. Continuity is unaffected: the run cursor is external and persists across calls.
            var before = body.ChildElements.Count;
            single[0] = posted[step.PostedIndex];
            RenderBlocks(body, single, state, runCursor);

            if (step.BaseIndex < 0)
            {
                continue;
            }

            var baseElement = baseline.Blocks[step.BaseIndex];
            for (var i = before; i < body.ChildElements.Count; i++)
            {
                ComposeBlockMerge.InheritProperties(body.ChildElements[i], baseElement);
            }

            // FR-A05 (task 041): restore what the content model cannot represent — bookmarks (the target of
            // every REF field, so dropping one breaks cross-references ELSEWHERE in the document) and a
            // block-level content-control shell. Taken from the BASE block, never from a client payload.
            ComposeBlockMerge.CarryUnmodeledConstructs(body, before, baseElement, code => state.Warn(code));
        }
    }
}
