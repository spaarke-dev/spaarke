using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Sprk.Bff.Api.Services.Compose.Operations;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-04 / FR-11 / D5 (spaarkeai-compose-r4 task 030) — the SINGLE unified byte-author of the Shadow
/// Document Architecture. Applies an ID-anchored <see cref="ComposeOperationLog"/> (task 003) surgically
/// onto retained OOXML: <c>(retained bytes, operation log) -&gt; patched bytes</c>. Every text edit /
/// redline / comment lands at its <c>w14:paraId</c> (resolved O(1)) + run-local offset with <b>ZERO
/// write-path text-search</b> (invariant I-7). This engine consolidates the two retiring writers
/// (<c>DocxAnnotationWriter</c> — the text-search 422 root cause — and
/// <see cref="ComposeParagraphRedlineSynthesizer"/> — the paragraph-diff path) into ONE operational
/// applier; task 032 retires those, task 031 adds the structural paragraph ops.
/// </summary>
/// <remarks>
/// <para>
/// <b>Anchor model (D2, task 003).</b> Each op carries <c>(paraId, runIndex, run-local-offset)</c>.
/// The paragraph is resolved by <c>w14:paraId</c> dictionary lookup (never text-search, never absolute
/// position); the fine anchor addresses the <c>runIndex</c>-th run in the paragraph's <b>editor-visible
/// run flatten</b> — the SAME flatten <see cref="ComposeDocxProjectionBuilder.BuildParaOffsetMap"/>
/// produces (descends into <c>w:hyperlink</c>/<c>w:ins</c>/<c>w:del</c>/<c>w:sdt</c>, counts fields /
/// complex objects / special content controls as single opaque atom slots). <see cref="FlattenEditorRuns"/>
/// here MIRRORS <see cref="ComposeDocxProjectionBuilder"/>'s <c>CollectRunBoundaries</c> exactly (the
/// codebase's established "two mirrored walks stay consistent by construction" pattern) so
/// <c>runIndex</c>/offset mean the same thing the client measured over the projection
/// (<c>notes/patch-engine-ab-decision.md</c> finding #2 — index the editor-visible flatten, NOT raw
/// <c>Elements&lt;Run&gt;()</c>).
/// </para>
/// <para>
/// <b>Sequential application (finding #1 — intra-paragraph op drift).</b> Operations are applied in log
/// order; each op's anchor addresses the paragraph state <i>after</i> all prior ops (the client rebases
/// the log, ProseMirror-<c>Mapping</c>-style, per task 020 / <c>bridge-prior-art.md</c> #1). The engine
/// re-derives the run flatten <b>per operation</b>, so a run split by an earlier op never leaves a later
/// op reading a stale index. <c>w:ins</c> is a sibling (not a <c>w:r</c>) so it does not shift a run
/// flatten by itself, but a split does — re-flattening is what keeps this correct (finding #2).
/// </para>
/// <para>
/// <b>Native OOXML emission (EDGE-1…4, migrated from <c>DocxAnnotationWriter</c>).</b>
/// Insertions wrap a fresh run in <see cref="InsertedRun"/> (<c>w:ins</c>); deletions replace runs with
/// <see cref="DeletedRun"/> (<c>w:del</c>) whose text is <see cref="DeletedText"/> (<c>w:delText</c>,
/// EDGE-4 — <c>w:t</c> inside <c>w:del</c> yields a file Word treats as corrupt); comments (EDGE-1) are
/// emitted BEFORE track changes so their anchors land on still-live runs; revision ids are monotonic,
/// seeded past any id already in the document (EDGE-3). Run splits <see cref="SplitRun"/> preserve
/// <see cref="RunProperties"/> on both halves.
/// </para>
/// <para>
/// <b>Byte-surgical (NFR-01, I-4).</b> Only <c>MainDocumentPart.Document</c> is opened + saved, so every
/// OTHER package part (styles, numbering, headers/footers, theme, media, ...) is copied verbatim by the
/// SDK; untouched paragraph subtrees within <c>document.xml</c> stay structurally faithful. The DOM is
/// MUTATED — <c>document.xml</c> is NEVER string-edited. A no-op (empty log + no comments) returns the
/// retained bytes unchanged.
/// </para>
/// <para>
/// <b>Purity (ADR-007 / ADR-013 / NFR-05).</b> <c>byte[]</c>-in / <c>byte[]</c>-out; no
/// <c>Microsoft.Graph</c> type (the SPE hop stays above <c>SpeFileStore</c>), no <c>IOpenAiClient</c> /
/// node executor / routing type (Tier-1 NetArchTest <see cref="Spaarke.ArchTests"/> enforces).
/// <b>Zero package delta (ADR-029)</b>: <c>DocumentFormat.OpenXml</c> 3.5.1 is already referenced —
/// the task-005 A/B rejected Docxodus on fit + ~+13 MB size (<c>notes/patch-engine-ab-decision.md</c>).
/// <b>Stateless singleton (ADR-010)</b>: the public surface holds no per-request state — all mutable
/// per-call state lives in a per-invocation <see cref="PatchSession"/>, so the DI singleton is
/// concurrency-safe.
/// </para>
/// <para>
/// <b>Scope (task 030 + task 031).</b> Task 030 implemented the text + mark ops and the comment machinery
/// plus the core resolve-split-emit spine. Task 031 adds — SEQUENCED LAST (after every text/mark op so a
/// structural node change never shifts an earlier text-op run-local-offset anchor) — the four STRUCTURAL
/// paragraph ops (<c>splitParagraph</c>, <c>mergeParagraph</c>, <c>insertParagraph</c>, <c>deleteParagraph</c>)
/// as tracked paragraph-MARK revisions (<c>w:ins</c>/<c>w:del</c> inside <c>w:pPr/w:rPr</c>; a merge = <c>w:del</c>
/// on the para-mark glyph, bridge-prior-art #6 / design §5.6(b) — NOT a naive <c>w:p</c> removal). An edit that
/// cannot be represented as valid tracked OOXML (merge across a table/section boundary, colliding <c>newParaId</c>,
/// split that would break a field) is REFUSED (<see cref="ComposePatchErrorKind.StructuralOperationRefused"/>),
/// so a package Word would repair never escapes. <c>setBlockAttr</c> stays outside these four and routes to its
/// own later <see cref="ComposePatchErrorKind.StructuralOpNotYetImplemented"/> seam.
/// <b>Escalation boundary (task POML <c>&lt;escalation&gt;</c>, root §6.5).</b> Reconciling a NEW
/// edit that resolves onto a run INSIDE a pre-existing <c>w:ins</c>/<c>w:del</c> region is genuinely
/// ambiguous (does deleting already-inserted text become a plain removal or a del-of-ins?). Rather than
/// guess a split that could mis-place bytes, the engine REFUSES such an op
/// (<see cref="ComposePatchErrorKind.TrackedChangeReconciliationUnsupported"/>) — deterministic, never
/// mis-placed. No corpus doc forces this case (the CIPO worst-offender is track-changes-clean per the
/// corpus manifest), so the settled-run path is fully corpus-proven; the tracked-change reconciliation
/// semantic is surfaced for a later decision, not silently approximated.
/// </para>
/// </remarks>
public sealed class ComposeShadowPatchEngine
{
    private const string DefaultAuthor = "Spaarke Compose";

    /// <summary>
    /// Applies <paramref name="operationLog"/> (and any <paramref name="comments"/>) onto
    /// <paramref name="retainedBytes"/> and returns the patched <c>.docx</c> bytes. A no-op (empty log,
    /// no comments) returns <paramref name="retainedBytes"/> unchanged (byte-identical). Nothing is
    /// partially written — any refusal throws before bytes are returned.
    /// </summary>
    /// <param name="retainedBytes">The retained original <c>.docx</c> bytes (a valid WordprocessingML OPC package).</param>
    /// <param name="operationLog">The ordered, rebased operation log to apply (task 003 contract).</param>
    /// <param name="comments">Optional paraId+range-anchored comments to emit as native <c>w:comment</c>
    /// (the durable-anchored, text-search-free replacement for <see cref="DocxAnnotation"/> comments).
    /// Applied BEFORE track-change ops (EDGE-1).</param>
    /// <param name="author">Revision author stamped on emitted <c>w:ins</c>/<c>w:del</c> (defaults to
    /// <c>"Spaarke Compose"</c>). Comments carry their own author.</param>
    /// <param name="timestamp">Revision timestamp stamped on emitted <c>w:ins</c>/<c>w:del</c> (defaults
    /// to <see cref="DateTimeOffset.UtcNow"/>). Comments carry their own date.</param>
    /// <exception cref="ArgumentException"><paramref name="retainedBytes"/> is null/empty.</exception>
    /// <exception cref="ComposePatchException">The bytes are not a readable DOCX, the schema version is
    /// unsupported, a paraId/runIndex/offset does not resolve, an op targets an opaque atom or a
    /// pre-existing tracked change, or a structural op (task 031) was supplied.</exception>
    public byte[] Apply(
        byte[] retainedBytes,
        ComposeOperationLog operationLog,
        IReadOnlyList<ComposeAnchoredComment>? comments = null,
        string? author = null,
        DateTimeOffset? timestamp = null)
    {
        if (retainedBytes is null || retainedBytes.Length == 0)
        {
            throw new ArgumentException("retainedBytes is required and must be non-empty.", nameof(retainedBytes));
        }

        ArgumentNullException.ThrowIfNull(operationLog);

        if (!string.Equals(operationLog.SchemaVersion, ComposeOperationSchema.Version, StringComparison.Ordinal))
        {
            throw new ComposePatchException(
                ComposePatchErrorKind.UnsupportedSchemaVersion,
                $"Operation log schema version '{operationLog.SchemaVersion}' is not supported by this engine " +
                $"(expected '{ComposeOperationSchema.Version}'). Both ends validate the version they compile against.");
        }

        var ops = operationLog.Operations ?? Array.Empty<ComposeOperation>();
        var anchoredComments = comments ?? Array.Empty<ComposeAnchoredComment>();

        // I-4 / NFR-01: a no-op save is a byte-identical passthrough — never open+re-serialize the
        // package (the OpenXML SDK would normalize document.xml on save). Return the retained bytes.
        if (ops.Count == 0 && anchoredComments.Count == 0)
        {
            return retainedBytes;
        }

        // Own a private, resizable copy so WordprocessingDocument can edit in place and we hand back the
        // flushed result (never open over the caller's array).
        using var buffer = new MemoryStream();
        buffer.Write(retainedBytes, 0, retainedBytes.Length);
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

            var session = new PatchSession(mainPart, body, author ?? DefaultAuthor, (timestamp ?? DateTimeOffset.UtcNow).UtcDateTime);
            session.Execute(ops, anchoredComments);

            mainPart.Document!.Save();
        }

        return buffer.ToArray();
    }

    // =====================================================================================================
    // Per-invocation mutable state + mutation logic. Instantiated once per Apply() call so
    // ComposeShadowPatchEngine stays a thread-safe stateless singleton (ADR-010).
    // =====================================================================================================
    private sealed class PatchSession
    {
        private readonly MainDocumentPart _mainPart;
        private readonly Body _body;
        private readonly string _author;
        private readonly DateTime _date;
        private readonly Dictionary<string, Paragraph> _byParaId;
        private int _idCounter;
        private WordprocessingCommentsPart? _commentsPart;

        public PatchSession(MainDocumentPart mainPart, Body body, string author, DateTime date)
        {
            _mainPart = mainPart;
            _body = body;
            _author = author;
            _date = date;
            _byParaId = BuildParaIdIndex(body);
            _idCounter = SeedRevisionId(mainPart, body);
        }

        public void Execute(IReadOnlyList<ComposeOperation> ops, IReadOnlyList<ComposeAnchoredComment> comments)
        {
            // EDGE-1: comments FIRST — a comment anchors a still-live run range; if a deletion converted
            // that range to <w:del> first, the anchor would attach to now-deleted content (orphaned balloon).
            foreach (var comment in comments)
            {
                ApplyComment(comment);
            }

            _commentsPart?.Comments?.Save();

            // Sequential application in TWO passes (project ordering rule / FR-05): ALL text/mark ops FIRST
            // (log order), then ALL structural paragraph ops LAST (log order). Structural ops mutate paragraph
            // NODES (split/merge/insert/delete), which would invalidate an earlier text-op's run-local-offset
            // anchor if interleaved — so they run after every offset-anchored op has already landed. Within each
            // pass the flatten is re-derived per op (client rebases the log; a split by an earlier op never
            // leaves a later op reading a stale index).
            var structural = new List<ComposeOperation>();
            foreach (var op in ops)
            {
                switch (op)
                {
                    case InsertTextOperation ins:
                        ApplyInsertText(ins);
                        break;
                    case DeleteRangeOperation del:
                        ApplyDeleteRange(del);
                        break;
                    case ReplaceRangeOperation rep:
                        ApplyReplaceRange(rep);
                        break;
                    case SetMarkOperation setMark:
                        ApplyMarkOverRange(setMark.ParaId, setMark.Range, setMark.Mark, on: true);
                        break;
                    case ClearMarkOperation clearMark:
                        ApplyMarkOverRange(clearMark.ParaId, clearMark.Range, clearMark.Mark, on: false);
                        break;

                    // Task 031 — the four structural paragraph ops are collected and applied LAST (below).
                    case SplitParagraphOperation:
                    case MergeParagraphOperation:
                    case InsertParagraphOperation:
                    case DeleteParagraphOperation:
                        structural.Add(op);
                        break;

                    // setBlockAttr (alignment/style/list block application) is outside task 031's four-op scope.
                    // R5 task 010 (G3) fills the Alignment case; Style/ListOrdered/ListLevel remain deferred to
                    // task 011 (G3 heading/list — reuses NumberingComputationEngine).
                    case SetBlockAttrOperation setAttr when setAttr.Attr == ComposeBlockAttr.Alignment:
                        ApplySetBlockAttrAlignment(setAttr);
                        break;

                    case SetBlockAttrOperation setAttr:
                        throw new ComposePatchException(
                            ComposePatchErrorKind.StructuralOpNotYetImplemented,
                            $"Operation 'setBlockAttr' (paraId {op.ParaId}, attr {setAttr.Attr}) — paragraph " +
                            "block-attribute application (style/list) is not yet implemented (R5 task 011); only " +
                            "Alignment is implemented (R5 task 010).");

                    default:
                        throw new ComposePatchException(
                            ComposePatchErrorKind.StructuralOpNotYetImplemented,
                            $"Unhandled operation type '{op.GetType().Name}'.");
                }
            }

            // Structural pass — LAST, in log order.
            foreach (var op in structural)
            {
                switch (op)
                {
                    case SplitParagraphOperation sp:
                        ApplySplitParagraph(sp);
                        break;
                    case MergeParagraphOperation mp:
                        ApplyMergeParagraph(mp);
                        break;
                    case InsertParagraphOperation ip:
                        ApplyInsertParagraph(ip);
                        break;
                    case DeleteParagraphOperation dp:
                        ApplyDeleteParagraph(dp);
                        break;
                }
            }
        }

        // -- Node resolution by paraId (O(1); zero text-search) ---------------------------------------

        private static Dictionary<string, Paragraph> BuildParaIdIndex(Body body)
        {
            // OrdinalIgnoreCase — op paraIds mirror the projection's ParaIdMap, which uppercases; a
            // case-insensitive index makes resolution robust to either casing.
            var map = new Dictionary<string, Paragraph>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in body.Descendants<Paragraph>())
            {
                var id = p.ParagraphId?.Value;
                if (!string.IsNullOrEmpty(id) && !map.ContainsKey(id!))
                {
                    map[id!] = p;
                }
            }

            return map;
        }

        private Paragraph Resolve(string paraId) =>
            _byParaId.TryGetValue(paraId, out var p)
                ? p
                : throw new ComposePatchException(
                    ComposePatchErrorKind.ParagraphNotFound,
                    $"paraId '{paraId}' resolves to no paragraph in the retained document. The write path never " +
                    "text-searches for a target (invariant I-7) — an unresolvable anchor is refused, never applied " +
                    "to a neighbouring node.");

        // -- insertText -------------------------------------------------------------------------------

        private void ApplyInsertText(InsertTextOperation op)
        {
            var para = Resolve(op.ParaId);
            var absOffset = ToAbsoluteOffset(para, op.ParaId, op.At);

            // Split so a run boundary exists exactly at the insertion point, then anchor the <w:ins> there.
            SplitParagraphAtEditorOffset(para, absOffset, op.ParaId);
            var leftRun = LastRunEndingAt(para, absOffset);

            var ins = NewInsertedRun();
            ins.AppendChild(BuildRun(templateRunProperties: leftRun?.RunProperties, op.Text, op.Marks));
            InsertInsAt(para, ins, leftRun);
        }

        /// <summary>Places <paramref name="ins"/> immediately after <paramref name="leftRun"/> (the run
        /// ending at the insertion point). When <paramref name="leftRun"/> is null (offset 0) the tracked
        /// insertion goes before the paragraph's first inline element, or is appended to an empty paragraph.</summary>
        private static void InsertInsAt(Paragraph para, InsertedRun ins, Run? leftRun)
        {
            if (leftRun is not null)
            {
                leftRun.InsertAfterSelf(ins);
                return;
            }

            var firstInline = para.Elements().FirstOrDefault(IsInlineContainer);
            if (firstInline is not null)
            {
                firstInline.InsertBeforeSelf(ins);
            }
            else
            {
                para.AppendChild(ins);
            }
        }

        // -- deleteRange ------------------------------------------------------------------------------

        private void ApplyDeleteRange(DeleteRangeOperation op)
        {
            var covered = IsolateRangeRuns(op.ParaId, op.Range);
            foreach (var run in covered)
            {
                WrapRunAsDeleted(run);
            }
        }

        // -- replaceRange (delete + insert at range start, one redline) --------------------------------

        private void ApplyReplaceRange(ReplaceRangeOperation op)
        {
            var covered = IsolateRangeRuns(op.ParaId, op.Range);
            var first = covered.Count > 0 ? covered[0] : null;

            // Insertion renders BEFORE the deletion (Word convention: <w:ins> new … <w:del> old).
            var templateProps = first?.RunProperties;
            if (op.Text.Length > 0)
            {
                var ins = NewInsertedRun();
                ins.AppendChild(BuildRun(templateProps, op.Text, op.Marks));
                if (first is not null)
                {
                    first.InsertBeforeSelf(ins);
                }
                else
                {
                    // Empty range span (start == end) with no covered runs ⇒ behave as insertText at start.
                    var para = Resolve(op.ParaId);
                    var absStart = ToAbsoluteOffset(para, op.ParaId, op.Range.Start);
                    SplitParagraphAtEditorOffset(para, absStart, op.ParaId);
                    InsertInsAt(para, ins, LastRunEndingAt(para, absStart));
                }
            }

            foreach (var run in covered)
            {
                WrapRunAsDeleted(run);
            }
        }

        // -- setMark / clearMark ----------------------------------------------------------------------

        private void ApplyMarkOverRange(string paraId, ComposeRunRange range, ComposeMarkType mark, bool on)
        {
            // v1: a mark change is applied directly to the isolated runs' RunProperties (deterministic,
            // schema-safe via the SDK's typed properties). Tracking the format change as a native
            // <w:rPrChange> revision is a documented later refinement (not required by task 030's
            // text/mark scope); the run split + range isolation are the load-bearing mechanics here.
            var covered = IsolateRangeRuns(paraId, range);
            foreach (var run in covered)
            {
                ApplyMarkToRun(run, mark, on);
            }
        }

        // =============================================================================================
        // Structural paragraph ops (task 031) — applied LAST in the pass (see Execute). A tracked structural
        // edit is a paragraph-MARK revision (w:ins/w:del inside w:pPr/w:rPr), NOT a naive node add/remove
        // (bridge-prior-art #6 / design §5.6(b)). The DOM is MUTATED; document.xml is never string-edited.
        // =============================================================================================

        // -- splitParagraph ---------------------------------------------------------------------------

        /// <summary>
        /// Divides <see cref="SplitParagraphOperation.ParaId"/> at the run-local point
        /// <see cref="SplitParagraphOperation.At"/>: content BEFORE the point stays in the leading paragraph;
        /// content AT/AFTER moves into a NEW paragraph carrying <see cref="SplitParagraphOperation.NewParaId"/>
        /// (its block formatting cloned from the source's <c>w:pPr</c>), inserted immediately after. The NEW
        /// paragraph mark now terminating the leading paragraph is a tracked INSERTION (Word's
        /// Enter-with-track-changes = an inserted para-mark in <c>w:pPr/w:rPr</c>).
        /// </summary>
        private void ApplySplitParagraph(SplitParagraphOperation op)
        {
            var para = Resolve(op.ParaId);

            if (HasSectionBreak(para))
            {
                throw StructuralRefused(op.ParaId, "the paragraph carries a section break (w:sectPr); splitting it would strand section properties");
            }

            if (ContainsField(para))
            {
                throw StructuralRefused(op.ParaId, "the paragraph contains a field (w:fldChar/w:fldSimple); a split there could break the field code/result — refused rather than corrupt");
            }

            if (_byParaId.ContainsKey(op.NewParaId))
            {
                throw StructuralRefused(op.ParaId, $"newParaId '{op.NewParaId}' already exists in the document (a split must mint a fresh id)");
            }

            var absOffset = ToAbsoluteOffset(para, op.ParaId, op.At);
            SplitParagraphAtEditorOffset(para, absOffset, op.ParaId);

            var trailing = new Paragraph();
            if (para.ParagraphProperties is { } srcPpr)
            {
                // Clone block formatting (style/alignment/numbering) onto the trailing paragraph BEFORE the
                // leading paragraph's mark is stamped inserted — the clone must stay a clean original mark.
                trailing.AppendChild((ParagraphProperties)srcPpr.CloneNode(true));
            }

            trailing.ParagraphId = op.NewParaId;
            MoveTrailingInlineChildren(para, absOffset, op.ParaId, trailing);
            para.InsertAfterSelf(trailing);
            MarkParagraphMark(para, inserted: true);
            _byParaId[op.NewParaId] = trailing;
        }

        // -- mergeParagraph — the hardest edge (para-mark deletion, bridge-prior-art #6) ---------------

        /// <summary>
        /// Merges <see cref="MergeParagraphOperation.ParaId"/> into its immediate predecessor
        /// <see cref="MergeParagraphOperation.TargetParaId"/> by marking the TARGET's paragraph mark as a
        /// tracked DELETION (<c>w:del</c> on the para-mark glyph in <c>w:pPr/w:rPr</c>) — the boundary between
        /// the two paragraphs. Content stays physically in place; Word joins the paragraphs on accept. Refuses
        /// (never corrupts) when the two are not adjacent siblings (an intervening table), are in different
        /// containers (a table cell), or the target carries a section break.
        /// </summary>
        private void ApplyMergeParagraph(MergeParagraphOperation op)
        {
            var para = Resolve(op.ParaId);         // the paragraph being removed (its content joins the target)
            var target = Resolve(op.TargetParaId); // the surviving predecessor

            if (ReferenceEquals(para, target))
            {
                throw StructuralRefused(op.ParaId, "mergeParagraph target equals the source paragraph");
            }

            if (!ReferenceEquals(para.Parent, target.Parent))
            {
                throw StructuralRefused(op.ParaId, $"source and target '{op.TargetParaId}' are in different containers (e.g. one is inside a table cell) — the merge is not representable");
            }

            if (!ReferenceEquals(target.NextSibling(), para))
            {
                throw StructuralRefused(op.ParaId, $"target '{op.TargetParaId}' is not the immediate predecessor of the source — an intervening block (e.g. a table or a section boundary) makes the merge non-representable");
            }

            if (HasSectionBreak(target))
            {
                throw StructuralRefused(op.ParaId, $"target '{op.TargetParaId}' carries a section break (w:sectPr); deleting its paragraph mark would drop section properties");
            }

            // The boundary between target and para is the TARGET's paragraph mark. Deleting it (tracked) is the
            // paragraph-mark deletion the prior art flags as the hardest OOXML edge (finding #6 / §5.6(b)).
            MarkParagraphMark(target, inserted: false);
        }

        // -- insertParagraph --------------------------------------------------------------------------

        /// <summary>
        /// Inserts a NEW empty paragraph carrying <see cref="InsertParagraphOperation.NewParaId"/> at
        /// <see cref="InsertParagraphOperation.Position"/> relative to the reference paragraph
        /// <see cref="InsertParagraphOperation.ParaId"/>. A brand-new paragraph's mark is a tracked INSERTION.
        /// </summary>
        private void ApplyInsertParagraph(InsertParagraphOperation op)
        {
            var reference = Resolve(op.ParaId);

            if (_byParaId.ContainsKey(op.NewParaId))
            {
                throw StructuralRefused(op.ParaId, $"newParaId '{op.NewParaId}' already exists in the document");
            }

            var fresh = new Paragraph { ParagraphId = op.NewParaId };
            MarkParagraphMark(fresh, inserted: true);

            if (op.Position == ComposeParagraphPosition.Before)
            {
                reference.InsertBeforeSelf(fresh);
            }
            else
            {
                reference.InsertAfterSelf(fresh);
            }

            _byParaId[op.NewParaId] = fresh;
        }

        // -- deleteParagraph --------------------------------------------------------------------------

        /// <summary>
        /// Strikes the whole paragraph <see cref="DeleteParagraphOperation.ParaId"/>: every settled
        /// (non-tracked, non-atom) run becomes a tracked deletion (<c>w:del</c>/<c>w:delText</c>) AND the
        /// paragraph mark is marked deleted (<c>w:del</c> in <c>w:pPr/w:rPr</c>), so the entire paragraph
        /// vanishes on accept — the semantic the retired <c>{paraId,text:''}</c> paragraph-diff sentinel
        /// produced (closes task 023's coverage gap on the SERVER side). Refuses a paragraph carrying a section
        /// break (striking it would drop section properties).
        /// </summary>
        private void ApplyDeleteParagraph(DeleteParagraphOperation op)
        {
            var para = Resolve(op.ParaId);

            if (HasSectionBreak(para))
            {
                throw StructuralRefused(op.ParaId, "the paragraph carries a section break (w:sectPr); striking it would drop section properties");
            }

            // Snapshot the flatten once, then strike each settled physical run. Atoms / already-tracked runs are
            // left in place (the para-mark deletion still removes the whole paragraph on accept).
            foreach (var slot in FlattenEditorRuns(para))
            {
                if (!slot.IsAtom && slot.TrackChange == RunTrackChange.None && slot.PhysicalRun is { } run)
                {
                    WrapRunAsDeleted(run);
                }
            }

            MarkParagraphMark(para, inserted: false);
        }

        // -- setBlockAttr: Alignment (R5 G3, task 010) -------------------------------------------------

        /// <summary>
        /// Applies a <c>setBlockAttr</c> <see cref="ComposeBlockAttr.Alignment"/> op: resolves the target
        /// paragraph by <see cref="ComposeOperation.ParaId"/> ONLY (no run offset — paragraph-scoped, I-7
        /// compliant) and records the change as a tracked <c>w:pPrChange</c> — the paragraph-property analogue
        /// of <see cref="MarkParagraphMark"/>'s paragraph-MARK tracked change. The <c>w:pPrChange</c> snapshots
        /// the PRIOR <c>w:jc</c> (or none, if the paragraph inherited alignment from its style) into a nested
        /// <c>w:pPr</c> (modeled by <see cref="ParagraphPropertiesExtended"/> — the OpenXml SDK's
        /// schema-contextual type for a <c>w:pPrChange</c> child, confirmed by round-tripping through
        /// <c>WordprocessingDocument.Open</c> rather than assumed from the class name) BEFORE the new value is
        /// written, so Word's "Reject Formatting Change" restores exactly what was there.
        /// </summary>
        /// <remarks>
        /// This is the TRACKED path — the only path this engine has today (the imported/tracked path is the
        /// engine's sole caller; G2's clean-apply branch, task 021, is additive and does not change this
        /// method). A future <c>trackChanges:false</c> mode (R5-D2, notes/g2-clean-apply-decision.md) would add
        /// a sibling branch that sets <c>w:jc</c> directly with NO <see cref="ParagraphPropertiesChange"/> — this
        /// method is kept self-contained (its own resolve + its own mutation) precisely so that branch point is
        /// easy to add later without touching this tracked path.
        /// </remarks>
        private void ApplySetBlockAttrAlignment(SetBlockAttrOperation op)
        {
            var para = Resolve(op.ParaId);

            if (!TryParseAlignmentValue(op.Value, out var newJc))
            {
                throw new ComposePatchException(
                    ComposePatchErrorKind.InvalidBlockAttrValue,
                    $"setBlockAttr Alignment on paraId '{op.ParaId}' carries an unrecognized value '{op.Value}' " +
                    "— expected one of Default/Left/Center/Right/Justify (null/'Default' clears to the style default).");
            }

            var pPr = para.ParagraphProperties ??= new ParagraphProperties();

            // Snapshot the PRIOR alignment BEFORE mutating anything — this is what Word shows as the
            // "changed from" state on Reject Formatting Change. No prior <w:jc> (style-inherited alignment)
            // snapshots as an empty ParagraphPropertiesExtended, matching Word's own pPrChange shape.
            var previousJc = pPr.GetFirstChild<Justification>();
            var previousProps = new ParagraphPropertiesExtended();
            if (previousJc is not null)
            {
                previousProps.AppendChild((Justification)previousJc.CloneNode(true));
            }

            // w:pPrChange never stacks (mirrors MarkParagraphMark's "never stack two revisions" rule) — an
            // earlier change to THIS paragraph within the SAME Apply() call is superseded by recording the
            // state immediately before the newest value, not the doc's original on-disk state.
            pPr.RemoveAllChildren<ParagraphPropertiesChange>();

            // Replace the live <w:jc> with the new value; null/Default clears it (inherit from style).
            previousJc?.Remove();
            if (newJc is { } jcValue)
            {
                InsertJustificationInOrder(pPr, new Justification { Val = jcValue });
            }

            var pPrChange = new ParagraphPropertiesChange { Id = NextId(), Author = _author, Date = _date };
            pPrChange.AppendChild(previousProps);
            pPr.AppendChild(pPrChange); // w:pPrChange is always the LAST child of CT_PPr.
        }

        /// <summary>Maps a <see cref="SetBlockAttrOperation.Value"/> string (the <c>ComposeParagraphAlignment</c>
        /// member name) to the OOXML <see cref="JustificationValues"/> to write, or <c>null</c> to clear the
        /// paragraph's explicit alignment back to its style default. Mirrors
        /// <see cref="ComposeDocumentRenderer"/>'s <c>ApplyAlignment</c> value vocabulary exactly (both byte-authors
        /// agree on what each alignment name means). Returns <c>false</c> for a value outside the closed set.</summary>
        private static bool TryParseAlignmentValue(string? value, out JustificationValues? justification)
        {
            switch (value)
            {
                case null:
                case "Default":
                    justification = null;
                    return true;
                case "Left":
                    justification = JustificationValues.Left;
                    return true;
                case "Center":
                    justification = JustificationValues.Center;
                    return true;
                case "Right":
                    justification = JustificationValues.Right;
                    return true;
                case "Justify":
                    justification = JustificationValues.Both;
                    return true;
                default:
                    justification = null;
                    return false;
            }
        }

        /// <summary>Inserts <paramref name="justification"/> at its schema-correct CT_PPr position: immediately
        /// before <see cref="ParagraphMarkRunProperties"/> / <see cref="SectionProperties"/> when either is
        /// present (both sort AFTER <c>w:jc</c> in the CT_PPr sequence), otherwise appended at the end. Callers
        /// remove any prior <see cref="ParagraphPropertiesChange"/> before calling this (that element sorts LAST
        /// and is re-appended by the caller afterward, so it never becomes a stale insertion anchor here).</summary>
        private static void InsertJustificationInOrder(ParagraphProperties pPr, Justification justification)
        {
            OpenXmlElement? anchor = pPr.GetFirstChild<ParagraphMarkRunProperties>();
            anchor ??= pPr.GetFirstChild<SectionProperties>();

            if (anchor is not null)
            {
                pPr.InsertBefore(justification, anchor);
            }
            else
            {
                pPr.AppendChild(justification);
            }
        }

        // -- structural helpers -----------------------------------------------------------------------

        /// <summary>
        /// Stamps a tracked change on <paramref name="para"/>'s PARAGRAPH MARK — a <c>w:ins</c>
        /// (<see cref="Inserted"/>) or <c>w:del</c> (<see cref="Deleted"/>) inside <c>w:pPr/w:rPr</c>
        /// (<see cref="ParagraphMarkRunProperties"/>). Idempotent: never stacks two revisions on one mark.
        /// This is the representation prior art flags as the hardest structural edge (finding #6 / §5.6(b)).
        /// </summary>
        private void MarkParagraphMark(Paragraph para, bool inserted)
        {
            var pPr = para.ParagraphProperties ??= new ParagraphProperties();
            var markProps = pPr.ParagraphMarkRunProperties ??= new ParagraphMarkRunProperties();

            if (markProps.GetFirstChild<Inserted>() is not null || markProps.GetFirstChild<Deleted>() is not null)
            {
                return;
            }

            OpenXmlElement change = inserted
                ? new Inserted { Id = NextId(), Author = _author, Date = _date }
                : new Deleted { Id = NextId(), Author = _author, Date = _date };

            // w:ins/w:del are the FIRST elements of the CT_ParaRPr content model — prepend.
            markProps.InsertAt(change, 0);
        }

        /// <summary>Moves <paramref name="para"/>'s TOP-LEVEL inline children whose editor span starts at/after
        /// <paramref name="absOffset"/> into <paramref name="destination"/>, preserving order. Non-inline
        /// children (<c>w:pPr</c>, bookmarks) stay with the leading paragraph. Refuses if the split offset falls
        /// strictly inside a non-divisible top-level container (e.g. a hyperlink) — after the physical-run split
        /// this only happens for content the engine will not divide.</summary>
        private static void MoveTrailingInlineChildren(Paragraph para, int absOffset, string paraId, Paragraph destination)
        {
            var cum = 0;
            foreach (var child in para.Elements().ToList())
            {
                if (!IsInlineContainer(child))
                {
                    continue;
                }

                var len = TopLevelInlineEditorLength(child);
                var start = cum;
                cum += len;

                if (start >= absOffset)
                {
                    child.Remove();
                    destination.AppendChild(child);
                }
                else if (start + len > absOffset)
                {
                    throw new ComposePatchException(
                        ComposePatchErrorKind.OffsetUnsplittable,
                        $"splitParagraph on paraId '{paraId}' falls at editor offset {absOffset} strictly inside a non-divisible " +
                        "top-level inline container (e.g. a hyperlink) — refused rather than divide it incorrectly.");
                }
            }
        }

        /// <summary>Editor-visible length of ONE top-level inline child (mirrors the flatten's atom rules for the
        /// container kinds a paragraph's direct children can be).</summary>
        private static int TopLevelInlineEditorLength(OpenXmlElement child) => child switch
        {
            Run r => IsComplexObjectRun(r) ? 1 : RunEditorLength(r),
            SimpleField sf => ExtractRunsDisplayLength(sf.Descendants<Run>()),
            Hyperlink h => SumChildRunEditorLength(h),
            InsertedRun ins => SumChildRunEditorLength(ins),
            DeletedRun del => SumChildRunEditorLength(del),
            SdtRun sdt => ExtractRunsDisplayLength(sdt.Descendants<Run>()),
            _ => 0,
        };

        private static int SumChildRunEditorLength(OpenXmlElement container)
        {
            var sum = 0;
            foreach (var r in container.Elements<Run>())
            {
                sum += RunEditorLength(r);
            }

            return sum;
        }

        private static bool HasSectionBreak(Paragraph para) =>
            para.ParagraphProperties?.GetFirstChild<SectionProperties>() is not null;

        private static bool ContainsField(Paragraph para) =>
            para.Descendants<FieldChar>().Any() || para.Descendants<SimpleField>().Any();

        private static ComposePatchException StructuralRefused(string paraId, string why) =>
            new(
                ComposePatchErrorKind.StructuralOperationRefused,
                $"Structural op on paraId '{paraId}' refused: {why}. The engine refuses a structural edit it cannot " +
                "represent as valid tracked OOXML rather than emit a package Word would repair or corrupt (project " +
                "ordering rule / FR-05).");

        // -- comments (EDGE-1: emitted before track changes) ------------------------------------------

        private void ApplyComment(ComposeAnchoredComment comment)
        {
            comment.Validate();
            var covered = IsolateRangeRuns(comment.ParaId, comment.Range);
            if (covered.Count == 0)
            {
                throw new ComposePatchException(
                    ComposePatchErrorKind.EmptyRange,
                    $"Comment on paraId '{comment.ParaId}' resolves to an empty run range — a comment must anchor to at least one run.");
            }

            var first = covered[0];
            var last = covered[^1];
            var id = NextId();

            // Three-part comment invariant (migrated from DocxAnnotationWriter): matching w:id across
            // commentRangeStart/End/Reference in the body + a <w:comment> of the same id in comments.xml.
            first.InsertBeforeSelf(new CommentRangeStart { Id = id });
            var endMark = new CommentRangeEnd { Id = id };
            last.InsertAfterSelf(endMark);
            endMark.InsertAfterSelf(new Run(new CommentReference { Id = id }));

            var commentsPart = EnsureCommentsPart();
            var element = new Comment
            {
                Id = id,
                Author = comment.Author,
                Initials = comment.Initials ?? DeriveInitials(comment.Author),
                Date = comment.Date.UtcDateTime,
            };
            element.AppendChild(new Paragraph(new Run(new Text(comment.CommentText)
            {
                Space = SpaceProcessingModeValues.Preserve,
            })));
            commentsPart.Comments!.AppendChild(element);
        }

        // -- range isolation (editor-offset span → whole covered runs) --------------------------------

        /// <summary>
        /// Splits <paramref name="paraId"/>'s runs so the run-local range <paramref name="range"/> is
        /// covered by WHOLE runs, and returns those runs in document order. Converts the range's
        /// run-local endpoints to absolute paragraph editor-offsets via the flatten, then splits at the
        /// end offset first (keeps the start offset stable) and at the start offset, and collects the
        /// physical runs whose editor span ⊆ [start, end). Refuses an empty/inverted range and any range
        /// that would cover an opaque atom or a pre-existing tracked change (escalation boundary).
        /// </summary>
        private List<Run> IsolateRangeRuns(string paraId, ComposeRunRange range)
        {
            var para = Resolve(paraId);
            var startAbs = ToAbsoluteOffset(para, paraId, range.Start);
            var endAbs = ToAbsoluteOffset(para, paraId, range.End);

            if (endAbs <= startAbs)
            {
                throw new ComposePatchException(
                    ComposePatchErrorKind.EmptyRange,
                    $"Range on paraId '{paraId}' is empty or inverted (absolute editor offsets start={startAbs}, end={endAbs}).");
            }

            SplitParagraphAtEditorOffset(para, endAbs, paraId);
            SplitParagraphAtEditorOffset(para, startAbs, paraId);

            var covered = new List<Run>();
            foreach (var slot in FlattenEditorRuns(para))
            {
                if (slot.Length == 0)
                {
                    continue;
                }

                var slotStart = slot.StartOffset;
                var slotEnd = slot.StartOffset + slot.Length;

                // Any overlap with the range that is NOT a full containment means a boundary split failed
                // (should not happen after the two splits) or the slot is an unsplittable atom straddling
                // the range — refuse rather than partially delete.
                var overlaps = slotStart < endAbs && slotEnd > startAbs;
                if (!overlaps)
                {
                    continue;
                }

                if (slot.IsAtom || slot.PhysicalRun is null)
                {
                    throw new ComposePatchException(
                        ComposePatchErrorKind.AtomTargeted,
                        $"Range on paraId '{paraId}' covers an opaque atom (field / content control / complex object) " +
                        "at editor offset " + slotStart + " — operations may target atom boundaries only, never inside one.");
                }

                if (slot.TrackChange != RunTrackChange.None)
                {
                    throw TrackedChangeReconciliation(paraId, slotStart);
                }

                if (slotStart >= startAbs && slotEnd <= endAbs)
                {
                    covered.Add(slot.PhysicalRun);
                }
                else
                {
                    // Partial overlap after splitting — the split did not land on a boundary this slot
                    // expected (e.g. an offset inside a multi-glyph run we could not cleanly divide).
                    throw new ComposePatchException(
                        ComposePatchErrorKind.OffsetUnsplittable,
                        $"Range boundary on paraId '{paraId}' did not resolve to a clean run boundary at editor offset " +
                        $"[{startAbs},{endAbs}); slot [{slotStart},{slotEnd}) partially overlaps.");
                }
            }

            return covered;
        }

        /// <summary>
        /// Ensures a run boundary exists at absolute paragraph editor-offset <paramref name="absOffset"/>
        /// by splitting the run that strictly contains it (preserving <see cref="RunProperties"/>). A
        /// no-op when the offset already sits on a slot boundary. Refuses if the offset falls inside an
        /// opaque atom, or inside a run nested in a pre-existing tracked change (escalation boundary).
        /// </summary>
        private void SplitParagraphAtEditorOffset(Paragraph para, int absOffset, string paraId)
        {
            foreach (var slot in FlattenEditorRuns(para))
            {
                var slotStart = slot.StartOffset;
                var slotEnd = slot.StartOffset + slot.Length;

                if (slot.Length > 0 && absOffset > slotStart && absOffset < slotEnd)
                {
                    if (slot.IsAtom || slot.PhysicalRun is null)
                    {
                        throw new ComposePatchException(
                            ComposePatchErrorKind.AtomTargeted,
                            $"Editor offset {absOffset} on paraId '{paraId}' falls inside an opaque atom — " +
                            "operations may target an atom's boundaries only.");
                    }

                    if (slot.TrackChange != RunTrackChange.None)
                    {
                        throw TrackedChangeReconciliation(paraId, slotStart);
                    }

                    SplitRun(slot.PhysicalRun, absOffset - slotStart);
                    return;
                }
            }
            // Offset on a boundary (or paragraph end) — no split needed.
        }

        /// <summary>The last physical run whose editor span ENDS at <paramref name="absOffset"/> (the
        /// "left" side of an insertion point). Null when <paramref name="absOffset"/> is 0 (nothing to its
        /// left). Assumes <see cref="SplitParagraphAtEditorOffset"/> has already ensured a boundary there.</summary>
        private Run? LastRunEndingAt(Paragraph para, int absOffset)
        {
            Run? last = null;
            foreach (var slot in FlattenEditorRuns(para))
            {
                if (slot.Length == 0 || slot.PhysicalRun is null)
                {
                    continue;
                }

                if (slot.StartOffset + slot.Length <= absOffset)
                {
                    last = slot.PhysicalRun;
                }
                else
                {
                    break;
                }
            }

            return last;
        }

        private int ToAbsoluteOffset(Paragraph para, string paraId, ComposeRunPoint point)
        {
            var slots = FlattenEditorRuns(para);
            if (point.RunIndex < 0 || point.RunIndex >= slots.Count)
            {
                // A point.RunIndex == slots.Count with offset 0 could address the paragraph end; support
                // the common terminal case only when the paragraph has runs.
                if (point.RunIndex == slots.Count && point.Offset == 0 && slots.Count > 0)
                {
                    return slots[^1].StartOffset + slots[^1].Length;
                }

                throw new ComposePatchException(
                    ComposePatchErrorKind.RunIndexOutOfRange,
                    $"runIndex {point.RunIndex} is out of range for paraId '{paraId}' (paragraph has {slots.Count} editor-visible runs).");
            }

            var slot = slots[point.RunIndex];
            if (point.Offset < 0 || point.Offset > slot.Length)
            {
                throw new ComposePatchException(
                    ComposePatchErrorKind.OffsetOutOfRange,
                    $"run-local offset {point.Offset} is out of range for runIndex {point.RunIndex} on paraId '{paraId}' " +
                    $"(run editor length {slot.Length}).");
            }

            return slot.StartOffset + point.Offset;
        }

        // -- run surgery ------------------------------------------------------------------------------

        /// <summary>
        /// Splits <paramref name="run"/> at run-local editor offset <paramref name="offset"/>, preserving
        /// <see cref="RunProperties"/> on both halves (the right half is a <see cref="OpenXmlElement.CloneNode"/>
        /// of the run, then each side's content children are trimmed to its editor range). Handles a run
        /// whose content spans multiple children (text + <c>w:br</c>/<c>w:tab</c>/<c>w:noBreakHyphen</c>).
        /// offset==0 ⇒ (null, run); offset==editorLength ⇒ (run, null). Mutates the DOM in place.
        /// </summary>
        private static (Run? Left, Run? Right) SplitRun(Run run, int offset)
        {
            var editorLen = RunEditorLength(run);
            if (offset <= 0)
            {
                return (null, run);
            }

            if (offset >= editorLen)
            {
                return (run, null);
            }

            var rightRun = (Run)run.CloneNode(true);
            TrimRunContent(run, keepStart: 0, keepEnd: offset);          // left keeps [0, offset)
            TrimRunContent(rightRun, keepStart: offset, keepEnd: editorLen); // right keeps [offset, end)
            run.InsertAfterSelf(rightRun);
            return (run, rightRun);
        }

        /// <summary>
        /// Removes the content children of <paramref name="run"/> that fall outside the editor range
        /// <c>[keepStart, keepEnd)</c>, and trims a <see cref="Text"/>/<see cref="DeletedText"/> that
        /// straddles a boundary. Non-content children (e.g. <see cref="RunProperties"/>) are length-0 and
        /// always kept — that is how formatting is preserved on both split halves.
        /// </summary>
        private static void TrimRunContent(Run run, int keepStart, int keepEnd)
        {
            var cum = 0;
            foreach (var child in run.Elements().ToList())
            {
                var len = ChildEditorLength(child);
                if (len == 0)
                {
                    continue; // RunProperties and other non-content markup — keep as-is.
                }

                var childStart = cum;
                var childEnd = cum + len;
                cum = childEnd;

                if (childEnd <= keepStart || childStart >= keepEnd)
                {
                    child.Remove();
                    continue;
                }

                if (child is Text text)
                {
                    var from = Math.Max(0, keepStart - childStart);
                    var to = Math.Min(len, keepEnd - childStart);
                    text.Text = text.Text.Substring(from, to - from);
                    text.Space = SpaceProcessingModeValues.Preserve;
                }
                else if (child is DeletedText delText)
                {
                    var from = Math.Max(0, keepStart - childStart);
                    var to = Math.Min(len, keepEnd - childStart);
                    delText.Text = delText.Text.Substring(from, to - from);
                    delText.Space = SpaceProcessingModeValues.Preserve;
                }
                // Glyphs (Break/TabChar/NoBreakHyphen, len 1) fully inside the kept range — kept intact.
            }
        }

        private void WrapRunAsDeleted(Run run)
        {
            // A run may carry text (w:t), deleted text (already inside a tracked change — guarded upstream
            // so it will be TrackChange.None here), or glyphs. Convert every w:t to w:delText (EDGE-4).
            var inner = new Run();
            if (run.RunProperties is { } rpr)
            {
                inner.AppendChild((RunProperties)rpr.CloneNode(true));
            }

            foreach (var child in run.Elements().ToList())
            {
                switch (child)
                {
                    case RunProperties:
                        break; // already cloned onto inner
                    case Text t:
                        inner.AppendChild(new DeletedText(t.Text) { Space = SpaceProcessingModeValues.Preserve });
                        break;
                    case DeletedText dt:
                        inner.AppendChild(new DeletedText(dt.Text) { Space = SpaceProcessingModeValues.Preserve });
                        break;
                    default:
                        // Break / TabChar / NoBreakHyphen and other glyphs survive inside w:del unchanged.
                        inner.AppendChild((OpenXmlElement)child.CloneNode(true));
                        break;
                }
            }

            var del = NewDeletedRun();
            del.AppendChild(inner);
            run.Parent!.ReplaceChild(del, run);
        }

        private Run BuildRun(RunProperties? templateRunProperties, string text, IReadOnlyList<ComposeMarkType> marks)
        {
            var run = new Run();
            RunProperties? rpr = templateRunProperties is not null ? (RunProperties)templateRunProperties.CloneNode(true) : null;

            if (marks.Count > 0)
            {
                rpr ??= new RunProperties();
                foreach (var mark in marks)
                {
                    SetMarkOnProperties(rpr, mark, on: true);
                }
            }

            if (rpr is not null)
            {
                run.AppendChild(rpr);
            }

            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            return run;
        }

        private static void ApplyMarkToRun(Run run, ComposeMarkType mark, bool on)
        {
            var rpr = run.RunProperties;
            if (rpr is null)
            {
                if (!on)
                {
                    return; // nothing to clear
                }

                rpr = new RunProperties();
                run.PrependChild(rpr);
            }

            SetMarkOnProperties(rpr, mark, on);
        }

        private static void SetMarkOnProperties(RunProperties rpr, ComposeMarkType mark, bool on)
        {
            // The SDK's typed properties insert/remove at the schema-correct position in w:rPr.
            switch (mark)
            {
                case ComposeMarkType.Bold:
                    rpr.Bold = on ? new Bold() : null;
                    break;
                case ComposeMarkType.Italic:
                    rpr.Italic = on ? new Italic() : null;
                    break;
                case ComposeMarkType.Underline:
                    rpr.Underline = on ? new Underline { Val = UnderlineValues.Single } : null;
                    break;
            }
        }

        // -- editor-visible run flatten (mirrors ComposeDocxProjectionBuilder.CollectRunBoundaries) -----

        /// <summary>
        /// Produces the paragraph's ordered editor-visible run slots — the SAME flatten
        /// <see cref="ComposeDocxProjectionBuilder"/> measures offsets over (descends into
        /// <c>w:hyperlink</c>/<c>w:ins</c>/<c>w:del</c>/<c>w:sdt</c>; a field / complex object / special
        /// content control is one opaque atom slot). Each slot carries the PHYSICAL <see cref="Run"/> the
        /// engine splits (null for an atom) plus its editor <c>[StartOffset, StartOffset+Length)</c> span
        /// and pre-existing tracked-change context. Re-derived per operation so splits never leave a stale
        /// index (finding #1/#2).
        /// </summary>
        private static IReadOnlyList<EditorRunSlot> FlattenEditorRuns(Paragraph paragraph)
        {
            var slots = new List<EditorRunSlot>();
            var runIndex = 0;
            var cumOffset = 0;
            CollectSlots(paragraph, RunTrackChange.None, slots, ref runIndex, ref cumOffset);
            return slots;
        }

        private static void CollectSlots(
            OpenXmlElement container, RunTrackChange trackChange, List<EditorRunSlot> slots, ref int runIndex, ref int cumOffset)
        {
            var field = new FieldScanState();
            foreach (var child in container.Elements())
            {
                switch (child)
                {
                    case Run r:
                        if (TryAdvanceFieldScan(r, field, out var fieldClosed))
                        {
                            if (fieldClosed)
                            {
                                var atomLen = ExtractRunsDisplayLength(field.ResultRuns);
                                slots.Add(new EditorRunSlot(runIndex, cumOffset, atomLen, trackChange, IsAtom: true, PhysicalRun: null));
                                runIndex++;
                                cumOffset += atomLen;
                                field.Reset();
                            }

                            break;
                        }

                        if (IsComplexObjectRun(r))
                        {
                            slots.Add(new EditorRunSlot(runIndex, cumOffset, 1, trackChange, IsAtom: true, PhysicalRun: null));
                            runIndex++;
                            cumOffset += 1;
                            break;
                        }

                        var len = RunEditorLength(r);
                        slots.Add(new EditorRunSlot(runIndex, cumOffset, len, trackChange, IsAtom: false, PhysicalRun: r));
                        runIndex++;
                        cumOffset += len;
                        break;

                    case SimpleField sf:
                        var sfLen = ExtractRunsDisplayLength(sf.Descendants<Run>());
                        slots.Add(new EditorRunSlot(runIndex, cumOffset, sfLen, trackChange, IsAtom: true, PhysicalRun: null));
                        runIndex++;
                        cumOffset += sfLen;
                        break;

                    case Hyperlink h:
                        CollectSlots(h, trackChange, slots, ref runIndex, ref cumOffset);
                        break;

                    case InsertedRun ins:
                        CollectSlots(ins, RunTrackChange.Inserted, slots, ref runIndex, ref cumOffset);
                        break;

                    case DeletedRun del:
                        CollectSlots(del, RunTrackChange.Deleted, slots, ref runIndex, ref cumOffset);
                        break;

                    case SdtRun sdtRun:
                        if (IsSpecialSdtControl(sdtRun.SdtProperties))
                        {
                            var sdtLen = ExtractRunsDisplayLength(sdtRun.Descendants<Run>());
                            slots.Add(new EditorRunSlot(runIndex, cumOffset, sdtLen, trackChange, IsAtom: true, PhysicalRun: null));
                            runIndex++;
                            cumOffset += sdtLen;
                        }
                        else
                        {
                            var content = sdtRun.GetFirstChild<SdtContentRun>();
                            if (content is not null)
                            {
                                CollectSlots(content, trackChange, slots, ref runIndex, ref cumOffset);
                            }
                        }

                        break;

                    default:
                        // ParagraphProperties, bookmarks, proofErr, etc. — no editor-visible run.
                        break;
                }
            }
        }

        // Field-scan + atom helpers below MIRROR ComposeDocxProjectionBuilder (the canonical definitions
        // are private there; replicated here so the engine stays self-contained, byte[]-in/out, and free
        // of a projection-builder dependency — the codebase's "two mirrored walks" pattern).

        private enum FieldPhase { None, Code, Result }

        private sealed class FieldScanState
        {
            public int Depth;
            public FieldPhase Phase = FieldPhase.None;
            public readonly List<Run> ResultRuns = new();

            public void Reset()
            {
                Depth = 0;
                Phase = FieldPhase.None;
                ResultRuns.Clear();
            }
        }

        private static bool TryAdvanceFieldScan(Run run, FieldScanState field, out bool closed)
        {
            closed = false;
            var fldChar = run.GetFirstChild<FieldChar>();
            if (fldChar is not null)
            {
                var type = fldChar.FieldCharType?.Value;
                if (type == FieldCharValues.Begin)
                {
                    if (field.Depth == 0)
                    {
                        field.Phase = FieldPhase.Code;
                        field.ResultRuns.Clear();
                    }

                    field.Depth++;
                    return true;
                }

                if (type == FieldCharValues.Separate)
                {
                    if (field.Depth == 0) return false;
                    if (field.Phase == FieldPhase.Code) field.Phase = FieldPhase.Result;
                    return true;
                }

                if (type == FieldCharValues.End)
                {
                    if (field.Depth == 0) return false;
                    field.Depth--;
                    if (field.Depth == 0) closed = true;
                    return true;
                }

                return false;
            }

            if (field.Depth > 0)
            {
                if (field.Phase == FieldPhase.Result)
                {
                    field.ResultRuns.Add(run);
                }

                return true;
            }

            return false;
        }

        private static bool IsComplexObjectRun(Run run) =>
            run.GetFirstChild<Drawing>() is not null || run.GetFirstChild<EmbeddedObject>() is not null;

        private static bool IsSpecialSdtControl(SdtProperties? props)
        {
            if (props is null) return false;
            return props.GetFirstChild<SdtContentDate>() is not null
                || props.GetFirstChild<SdtContentDropDownList>() is not null
                || props.GetFirstChild<SdtContentComboBox>() is not null
                || props.GetFirstChild<SdtContentPicture>() is not null
                || props.GetFirstChild<SdtContentDocPartObject>() is not null
                || props.GetFirstChild<SdtContentDocPartList>() is not null
                || props.GetFirstChild<SdtContentEquation>() is not null
                || props.GetFirstChild<SdtContentCitation>() is not null
                || props.GetFirstChild<SdtContentBibliography>() is not null
                || props.GetFirstChild<SdtContentGroup>() is not null;
        }

        private static int ExtractRunsDisplayLength(IEnumerable<Run> runs)
        {
            var len = 0;
            foreach (var run in runs)
            {
                len += RunEditorLength(run);
            }

            return len;
        }

        private static int RunEditorLength(Run run)
        {
            var length = 0;
            foreach (var child in run.Elements())
            {
                length += ChildEditorLength(child);
            }

            return length;
        }

        private static int ChildEditorLength(OpenXmlElement child) => child switch
        {
            Text t => t.Text?.Length ?? 0,
            DeletedText dt => dt.Text?.Length ?? 0,
            Break or TabChar or NoBreakHyphen => 1,
            _ => 0,
        };

        private static bool IsInlineContainer(OpenXmlElement e) =>
            e is Run or InsertedRun or DeletedRun or Hyperlink or SdtRun or SimpleField;

        // -- revision ids + comments part (EDGE-3 monotonic seeding; migrated) -------------------------

        private InsertedRun NewInsertedRun() => new() { Id = NextId(), Author = _author, Date = _date };

        private DeletedRun NewDeletedRun() => new() { Id = NextId(), Author = _author, Date = _date };

        private string NextId() => (++_idCounter).ToString(CultureInfo.InvariantCulture);

        private WordprocessingCommentsPart EnsureCommentsPart()
        {
            if (_commentsPart is not null)
            {
                return _commentsPart;
            }

            _commentsPart = _mainPart.GetPartsOfType<WordprocessingCommentsPart>().FirstOrDefault();
            if (_commentsPart is null)
            {
                _commentsPart = _mainPart.AddNewPart<WordprocessingCommentsPart>();
                _commentsPart.Comments = new Comments();
            }
            else
            {
                _commentsPart.Comments ??= new Comments();
            }

            return _commentsPart;
        }

        private static int SeedRevisionId(MainDocumentPart mainPart, Body body)
        {
            var max = 0;
            foreach (var id in EnumerateExistingIds(mainPart, body))
            {
                if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > max)
                {
                    max = value;
                }
            }

            return max;
        }

        private static IEnumerable<string> EnumerateExistingIds(MainDocumentPart mainPart, Body body)
        {
            foreach (var ins in body.Descendants<InsertedRun>())
            {
                if (ins.Id?.Value is { } v) yield return v;
            }

            foreach (var del in body.Descendants<DeletedRun>())
            {
                if (del.Id?.Value is { } v) yield return v;
            }

            foreach (var crs in body.Descendants<CommentRangeStart>())
            {
                if (crs.Id?.Value is { } v) yield return v;
            }

            var commentsPart = mainPart.GetPartsOfType<WordprocessingCommentsPart>().FirstOrDefault();
            if (commentsPart?.Comments is { } comments)
            {
                foreach (var comment in comments.Elements<Comment>())
                {
                    if (comment.Id?.Value is { } v) yield return v;
                }
            }
        }

        private static string DeriveInitials(string author)
        {
            var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "?";
            }

            var initials = string.Concat(parts.Take(3).Select(p => char.ToUpperInvariant(p[0])));
            return initials.Length == 0 ? "?" : initials;
        }

        private static ComposePatchException TrackedChangeReconciliation(string paraId, int offset) =>
            new(
                ComposePatchErrorKind.TrackedChangeReconciliationUnsupported,
                $"An operation on paraId '{paraId}' resolves onto a run inside a pre-existing tracked change " +
                $"(w:ins/w:del) at editor offset {offset}. Reconciling a new edit against an existing revision is " +
                "the task-030 escalation boundary (POML <escalation>): the engine REFUSES rather than guess a split " +
                "that could mis-place bytes. This case is not exercised by the corpus (track-changes-clean).");

        /// <summary>One editor-visible run slot in a paragraph's flatten. <see cref="PhysicalRun"/> is the
        /// concrete <see cref="Run"/> the engine splits; null for an opaque atom (never split).</summary>
        private readonly record struct EditorRunSlot(
            int Index, int StartOffset, int Length, RunTrackChange TrackChange, bool IsAtom, Run? PhysicalRun);
    }
}

/// <summary>
/// A durable, paraId+range-anchored comment for <see cref="ComposeShadowPatchEngine"/> — the
/// text-search-free (I-7) replacement for <see cref="DocxAnnotation"/>'s <c>target_text</c> comment
/// anchor. Emitted as native <c>w:comment</c> + comment range marks, BEFORE any track-change op (EDGE-1).
/// </summary>
public sealed record ComposeAnchoredComment
{
    /// <summary>The <c>w14:paraId</c> of the paragraph the comment anchors to.</summary>
    public required string ParaId { get; init; }

    /// <summary>The run-local range the comment brackets (intra-paragraph).</summary>
    public required ComposeRunRange Range { get; init; }

    /// <summary>The comment body.</summary>
    public required string CommentText { get; init; }

    /// <summary>Comment author (surfaces as Word's attribution).</summary>
    public required string Author { get; init; }

    /// <summary>Optional author initials for the balloon; derived from <see cref="Author"/> when omitted.</summary>
    public string? Initials { get; init; }

    /// <summary>Comment timestamp (serialized UTC in the <c>w:date</c> attribute).</summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>Guards internal consistency before any package mutation (fail-before-write).</summary>
    /// <exception cref="ArgumentException">A required field is missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrEmpty(ParaId))
        {
            throw new ArgumentException("A comment requires a non-empty ParaId.");
        }

        if (string.IsNullOrEmpty(CommentText))
        {
            throw new ArgumentException("A comment requires non-empty CommentText.");
        }

        if (string.IsNullOrWhiteSpace(Author))
        {
            throw new ArgumentException("A comment requires a non-empty Author.");
        }
    }
}

/// <summary>The category of a <see cref="ComposePatchException"/> — lets the endpoint/service layer map a
/// refusal to the right ProblemDetails status instead of an opaque 500.</summary>
public enum ComposePatchErrorKind
{
    /// <summary>The supplied bytes are not a readable DOCX package → 400.</summary>
    MalformedDocument,

    /// <summary>The op-log schema version is not the one this engine compiles against → 400/409.</summary>
    UnsupportedSchemaVersion,

    /// <summary>An op's <c>w14:paraId</c> resolves to no paragraph (no text-search fallback) → 422.</summary>
    ParagraphNotFound,

    /// <summary>An op's <c>runIndex</c> is out of range for the paragraph's editor-visible run flatten → 422.</summary>
    RunIndexOutOfRange,

    /// <summary>An op's run-local offset is out of range for the addressed run → 422.</summary>
    OffsetOutOfRange,

    /// <summary>A range op resolved to an empty or inverted span → 422.</summary>
    EmptyRange,

    /// <summary>An op targeted the interior of an opaque atom (field/content control/complex object) → 422.</summary>
    AtomTargeted,

    /// <summary>A boundary did not resolve to a clean run split (unsplittable content) → 422.</summary>
    OffsetUnsplittable,

    /// <summary>The op resolves onto a run inside a pre-existing tracked change — the task-030 escalation
    /// boundary; refused rather than guessed (root §6.5) → 422.</summary>
    TrackedChangeReconciliationUnsupported,

    /// <summary>A structural paragraph op cannot be represented as valid tracked OOXML — a merge across a
    /// table/section boundary, a colliding <c>newParaId</c>, or a split that would break a field. Refused, not
    /// corrupted (project ordering rule / FR-05) → 422.</summary>
    StructuralOperationRefused,

    /// <summary>A block-attr op (<c>setBlockAttr</c>) was supplied — outside task 031's four structural-paragraph
    /// ops; routes to its own later applier extension → 501/422.</summary>
    StructuralOpNotYetImplemented,

    /// <summary>A <c>setBlockAttr</c> op's <c>Value</c> is not a recognized member of the attribute's closed
    /// value set (e.g. an Alignment value outside Default/Left/Center/Right/Justify) → 422.</summary>
    InvalidBlockAttrValue,
}

/// <summary>
/// A structured, mappable failure from <see cref="ComposeShadowPatchEngine.Apply"/>. Distinct from a bare
/// exception so the endpoint/service layer can select the right ProblemDetails status per
/// <see cref="Kind"/>. Nothing is partially written — a throw happens before any bytes are returned.
/// </summary>
public sealed class ComposePatchException : Exception
{
    public ComposePatchException(ComposePatchErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    /// <summary>The failure category, used to select the ProblemDetails status.</summary>
    public ComposePatchErrorKind Kind { get; }
}
