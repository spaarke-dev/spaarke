using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Compose.Operations;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 1 of the <c>ComposeService</c> decomposition (task 070): recovering a save whose anchors no
/// longer line up with the bytes we are about to write.
///
/// <para><b>Its reason to change</b> is the recovery policy for a mis-anchored save — which band is safe
/// to auto-apply, what the atomic retry unit is, and what surfaces to the user instead of being applied
/// or dropped. That is independent of how a save lands bytes, which is why it separates: <c>SaveAsync</c>
/// decides WHETHER recovery is needed; this type decides WHAT recovery does.</para>
///
/// <para><b>Two recovery paths, deliberately in one type.</b> They look separate and are often confused,
/// so the distinction is stated once here rather than re-derived at each call site:
/// <list type="bullet">
/// <item><see cref="ReanchorStaleSaveAsync"/> — the BASE MOVED (eTag mismatch). Re-download the live
///   bytes and re-anchor the whole op log across old→current paragraphs by content similarity.</item>
/// <item><see cref="ApplyBestEffortByParagraph"/> — the base is CURRENT; a single op just failed to
///   anchor. Apply the paragraph-units that resolve and surface the ones that do not.</item>
/// </list>
/// They share the same policy — <b>never a wrong edit, never a silent drop</b> — and the same collaborators
/// (patch engine, stamper, reanchor service), which is what makes them one cluster rather than two.</para>
///
/// <para><b>Two members are <c>internal static</c> because a caller outside the cluster needs them.</b>
/// <see cref="IsBatchLevelPatchRefusal"/> is called from <c>SaveAsync</c>'s <c>catch … when</c> filter —
/// it is the predicate that decides whether prong-1 recovery is even eligible, so the decision to enter
/// this type is necessarily made outside it. It travels here anyway (rather than staying on
/// <c>ComposeService</c>) because it defines the refusal taxonomy this recovery is built on. This follows
/// the call made for cluster 5b's signal factories: the helper lives with the code that explains it, and
/// the outside caller references it there.</para>
///
/// <para><b>The one dependency that points the other way.</b> <c>ReanchorStaleSaveAsync</c> needs the
/// tracked-change revision author, and <c>ComposeService.ResolveRevisionAuthor</c> owns that (cluster 9 —
/// it stays with the save path, where its other two callers are). Rather than duplicate it or thread an
/// extra argument through the call site, it is referenced as an <c>internal static</c> pure function of
/// its <c>HttpContext</c> argument. That is a shared helper, not a cycle: this type holds no reference to
/// <c>ComposeService</c> and cannot observe its state.</para>
///
/// <para>An <c>internal sealed</c> collaborator built from dependencies <c>ComposeService</c> already
/// holds — <b>no new DI registration</b> (ADR-010). Behaviour is unchanged; this is a move, not a
/// rewrite.</para>
/// </summary>
internal sealed class ComposeReanchorCoordinator
{
    private readonly ISpeFileOperations _spe;
    private readonly ComposeShadowPatchEngine _patchEngine;
    private readonly ComposeBaselineParaIdStamper _baselineParaIdStamper;

    /// <param name="reanchorService">
    /// The KEEP-asset fuzzy re-anchor engine. NULL is a supported state, not a defect: the host has no
    /// distributed cache (ADR-032 availability gate), so the re-anchor SUMMARY is simply not persisted.
    /// The re-anchoring itself runs regardless — <see cref="AnnotationReanchorService.Reanchor"/> is static.
    /// </param>
    private readonly AnnotationReanchorService? _reanchorService;
    private readonly ILogger _logger;

    internal ComposeReanchorCoordinator(
        ISpeFileOperations spe,
        ComposeShadowPatchEngine patchEngine,
        ComposeBaselineParaIdStamper baselineParaIdStamper,
        AnnotationReanchorService? reanchorService,
        ILogger logger)
    {
        _spe = spe;
        _patchEngine = patchEngine;
        _baselineParaIdStamper = baselineParaIdStamper;
        _reanchorService = reanchorService;
        _logger = logger;
    }

    /// <summary>
    /// FR-08 (task 050): the base moved under the client since it was loaded (the persisted version stamp's
    /// eTag no longer matches the live SPE eTag). Re-anchors <paramref name="request"/>'s operation log +
    /// comments against the FRESHLY re-downloaded current bytes via <see cref="AnnotationReanchorService"/>
    /// (the KEEP asset, reused verbatim), applies ONLY the exact-paraId AUTO band through the Patch Engine,
    /// and returns the patched bytes alongside the full band summary. REVIEW/ORPHAN ops/comments are
    /// deliberately NOT applied — an op's anchor is never rewritten (I-7, no write-path text-search), so a
    /// fuzzy (non-exact-id) match is not safe to auto-apply; it surfaces in the summary instead, never
    /// silently applied and never silently dropped.
    /// </summary>
    internal async Task<(byte[] PatchedBytes, ReanchorSummary Summary)> ReanchorStaleSaveAsync(
        SaveComposeDocumentRequest request,
        byte[] originalBaseline,
        HttpContext httpContext,
        DateTimeOffset observedAt,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        // Re-download the CURRENT (live) bytes — the base the op log must now be checked against.
        //
        // FR-S07 (r8 task 014): a download miss REFUSES THE SAVE. It previously returned `originalBaseline`
        // — the LOAD-TIME bytes — and let the caller persist them. This method is only ever reached when
        // the base has already been observed to MOVE, so those bytes are by definition older than the
        // version they were about to replace: the fallback silently overwrote a newer document with
        // pre-edit content and reported HTTP 200. It was the only data-destroying path in Track S.
        //
        // Its comment claimed it "fails closed" because every op surfaced as ORPHAN — and that was true of
        // the OPS. It was the BYTES that were wrong. Surfacing the ops honestly while writing a stale
        // document is precisely the Half-A/Half-B confusion this project exists to remove.
        //
        // Deleted rather than guarded: a re-anchor with no current bytes cannot produce a correct save
        // under any condition, so there is no version of this fallback worth keeping. The throw is caught
        // at the endpoint and reported as `refused-stale` (FR-S06) — a defined terminal outcome, never an
        // HTTP 422 content refusal (ADR-049).
        Stream? stream;
        try
        {
            stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId!, request.DocumentSpeId!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor could not re-download the current bytes for driveItem={DocumentSpeId} — REFUSING the save; nothing written, stored version untouched.",
                request.DocumentSpeId);
            throw new ComposeStaleBaselineUnavailableException(request.DocumentSpeId!, "download-faulted", ex);
        }

        if (stream is null)
        {
            _logger.LogWarning(
                "Compose save: stale-base re-anchor got no content stream for driveItem={DocumentSpeId} — REFUSING the save; nothing written, stored version untouched.",
                request.DocumentSpeId);
            throw new ComposeStaleBaselineUnavailableException(request.DocumentSpeId!, "download-empty");
        }

        byte[] currentBytes;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            currentBytes = buffer.ToArray();
        }

        // Seam A (UAT round-2 item #4 — advisory comments must survive a STALE save and bake as native
        // w:comment). The client's minted paraIds live only in request.ParaIdMap — physically absent from
        // BOTH the retained baseline AND the freshly-fetched current bytes. Without them, an advisory
        // comment's client-minted ParaId resolves to nothing in currentParaIds below → 0.0 re-anchor score
        // → ORPHAN band → surfaced-but-never-baked (the exact "comments don't survive Save to Word" bug the
        // non-stale path does NOT have, because it stamps first — see the sibling Stamp call in SaveAsync).
        // Stamp both corpora with the SAME fail-open, count-gated, text-verified stamper: where the version
        // bump was benign (unchanged paragraph structure + text — a metadata touch or an eTag-counter move
        // that did not really edit content), the client paraId becomes physically present in currentBytes →
        // ResolveByParaId hits confidence 1.0 → AUTO → the exact-paraId auto-apply gate below bakes the
        // comment. Where the current bytes genuinely diverged (different paragraph count, or the anchored
        // text changed), the stamper no-ops (count gate / text-verify) and the comment correctly stays
        // ORPHAN — never a wrong-paragraph stamp. Stamping originalBaseline too repopulates each comment's
        // TextPattern (via the IndexOfParaId hint below) so a text-drifted clause surfaces as REVIEW rather
        // than a blind ORPHAN in the re-anchor banner.
        if (request.ParaIdMap is { Count: > 0 })
        {
            originalBaseline = _baselineParaIdStamper.Stamp(originalBaseline, request.ParaIdMap);
            currentBytes = _baselineParaIdStamper.Stamp(currentBytes, request.ParaIdMap);
        }

        IReadOnlyList<string> oldParagraphs;
        IReadOnlyList<string?> oldParaIds;
        IReadOnlyList<string> currentParagraphs;
        IReadOnlyList<string?> currentParaIds;
        try
        {
            oldParagraphs = AnnotationReanchorService.ExtractParagraphTexts(originalBaseline);
            oldParaIds = AnnotationReanchorService.ExtractParaIds(originalBaseline);
            currentParagraphs = AnnotationReanchorService.ExtractParagraphTexts(currentBytes);
            currentParaIds = AnnotationReanchorService.ExtractParaIds(currentBytes);
        }
        catch (Exception ex) when (ex is DocxAnnotationException or ArgumentException)
        {
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor could not read the paragraph corpus for driveItem={DocumentSpeId} — every op/comment surfaces as ORPHAN.",
                request.DocumentSpeId);
            return (currentBytes, BuildAllOrphanSummary(request, observedAt));
        }

        var ops = request.OperationLog?.Operations ?? Array.Empty<ComposeOperation>();
        var comments = request.Comments ?? Array.Empty<ComposeAnchoredComment>();

        var priorAnchors = new List<PriorAnchor>(ops.Count + comments.Count);
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var hint = IndexOfParaId(oldParaIds, op.ParaId);
            priorAnchors.Add(new PriorAnchor(
                Id: $"op-{i}",
                Type: op.GetType().Name,
                TextPattern: hint >= 0 && hint < oldParagraphs.Count ? oldParagraphs[hint] : string.Empty,
                ParagraphHint: hint,
                Preview: null,
                ParaId: op.ParaId));
        }

        for (var i = 0; i < comments.Count; i++)
        {
            var c = comments[i];
            var hint = IndexOfParaId(oldParaIds, c.ParaId);
            priorAnchors.Add(new PriorAnchor(
                Id: $"comment-{i}",
                Type: "comment",
                TextPattern: hint >= 0 && hint < oldParagraphs.Count ? oldParagraphs[hint] : string.Empty,
                ParagraphHint: hint,
                Preview: c.CommentText,
                ParaId: c.ParaId));
        }

        var summary = AnnotationReanchorService.Reanchor(priorAnchors, currentParagraphs, request.DocumentSpeId, observedAt, currentParaIds);

        // Only an EXACT paraId match (confidence 1.0 — the paragraph's w14:paraId is still present,
        // unchanged, in the current document) is safe to auto-apply verbatim: the op's anchor is never
        // rewritten, so a fuzzy AUTO (a different paraId that merely scored well on content) would apply
        // the op against the WRONG paragraph id and fail to resolve (or worse, silently mis-anchor). Fuzzy
        // AUTO/REVIEW/ORPHAN all surface for review — never silently applied.
        var autoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in summary.Annotations)
        {
            if (r.Band == ReanchorBand.Auto && r.Confidence >= 1.0)
            {
                autoIds.Add(r.Id);
            }
        }

        var autoOps = new List<ComposeOperation>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (autoIds.TryGetValue($"op-{i}", out _))
            {
                autoOps.Add(ops[i]);
            }
        }

        var autoComments = new List<ComposeAnchoredComment>();
        for (var i = 0; i < comments.Count; i++)
        {
            if (autoIds.TryGetValue($"comment-{i}", out _))
            {
                autoComments.Add(comments[i]);
            }
        }

        byte[] patched;
        try
        {
            patched = (autoOps.Count == 0 && autoComments.Count == 0)
                ? currentBytes
                : _patchEngine.Apply(
                    currentBytes,
                    new ComposeOperationLog { SchemaVersion = request.OperationLog?.SchemaVersion ?? ComposeOperationSchema.Version, Operations = autoOps },
                    autoComments,
                    ComposeService.ResolveRevisionAuthor(httpContext),
                    observedAt,
                    trackChanges: trackChanges);
        }
        catch (ComposePatchException ex)
        {
            // An AUTO-band op that still fails to resolve at patch time (an edge case beyond the reanchor's
            // own exact-paraId check) is never silently applied — degrade the whole batch to ORPHAN rather
            // than guess a partial apply that could mis-place bytes.
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor's AUTO band failed to apply for driveItem={DocumentSpeId} ({Kind}) — degrading the whole batch to ORPHAN.",
                request.DocumentSpeId, ex.Kind);
            return (currentBytes, BuildAllOrphanSummary(request, observedAt));
        }

        if (_reanchorService is not null)
        {
            try
            {
                await _reanchorService.SaveSummaryAsync(request.DocumentSpeId!, summary, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose save: failed to persist the stale-base re-anchor summary for driveItem={DocumentSpeId} — the save itself succeeded.",
                    request.DocumentSpeId);
            }
        }

        return (patched, summary);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    // Prong 1 (task 055) — best-effort per-paragraph recovery for an OP-LEVEL patch refusal on the loaded-doc
    // save path. Distinct from ReanchorStaleSaveAsync above (which handles a STALE BASE — an eTag mismatch —
    // by content-similarity re-anchoring across old→current paragraphs). Here the base is CURRENT; a single op
    // just fails to anchor. Rather than lose the whole editing session (the pre-prong-1 behavior: any refusal
    // → 422), apply the resolvable paragraph-units and surface the unresolvable ops.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="ComposePatchErrorKind"/> that condemns the WHOLE batch (the docx itself is unreadable, or
    /// the op-log schema version is unsupported) rather than a single op's anchor — for these, best-effort
    /// partial apply is meaningless, so the save must still fail hard (mapped to a ProblemDetails by the
    /// endpoint). Every OTHER kind is an op-level anchoring/resolution refusal eligible for prong-1 recovery.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than private: <c>ComposeService.SaveAsync</c>'s <c>catch … when</c> filter is
    /// the caller that decides whether prong-1 recovery is entered at all, so the eligibility predicate is
    /// necessarily read from outside this type (see the class remarks).
    /// </remarks>
    internal static bool IsBatchLevelPatchRefusal(ComposePatchErrorKind kind) =>
        kind is ComposePatchErrorKind.MalformedDocument or ComposePatchErrorKind.UnsupportedSchemaVersion;

    /// <summary>
    /// True for a STRUCTURAL / whole-document op — one that splits/merges/inserts/deletes paragraphs, edits a
    /// table's structure, or reconciles EVERY tracked revision (scope=All). These span or renumber paragraphs
    /// (and mint child paraIds later ops depend on), so under the engine's intra-paragraph sequential rebasing
    /// they are NOT safe to apply piecemeal: prong-1 groups them into ONE all-or-nothing unit applied LAST
    /// (mirroring the engine's own structural-last pass). Inline ops (text/mark/setBlockAttr/single-revision)
    /// are grouped by paraId instead — the paragraph being the safe atomic unit.
    /// </summary>
    private static bool IsStructuralOrGlobalOp(ComposeOperation op) => op switch
    {
        SplitParagraphOperation or MergeParagraphOperation or InsertParagraphOperation
            or DeleteParagraphOperation or TableOperation => true,
        AcceptRevisionOperation { Scope: ComposeRevisionScope.All } => true,
        RejectRevisionOperation { Scope: ComposeRevisionScope.All } => true,
        _ => false,
    };

    /// <summary>
    /// Prong 1 (task 055). Applies <paramref name="log"/> onto <paramref name="baseline"/> in the LARGEST
    /// units provably safe under the engine's intra-paragraph sequential rebasing, applying every unit that
    /// resolves and surfacing every unit that refuses (never a wrong edit, never a silent drop):
    /// <list type="bullet">
    /// <item>Inline ops (text/mark/setBlockAttr/single-revision) grouped by <c>paraId</c> — the paragraph is
    ///   atomic (dropping one op would leave later same-paragraph ops mis-anchored), applied in first-seen
    ///   order; each paragraph's anchored comments ride its unit so the engine's comments-first-per-Apply
    ///   ordering (EDGE-1) is preserved per paragraph.</item>
    /// <item>Structural / All-revision ops as ONE all-or-nothing unit applied LAST (keeps minted-paraId
    ///   lineage intact).</item>
    /// </list>
    /// Each unit runs through the SAME <see cref="ComposeShadowPatchEngine.Apply"/> onto the cumulative bytes,
    /// so the byte result for the resolvable paragraphs matches the clean-batch path. A unit throwing a
    /// batch-level refusal (a malformed cumulative package — not expected mid-recovery) propagates.
    /// </summary>
    internal byte[] ApplyBestEffortByParagraph(
        byte[] baseline,
        ComposeOperationLog log,
        IReadOnlyList<ComposeAnchoredComment>? comments,
        string author,
        DateTimeOffset observedAt,
        bool trackChanges,
        out PartialApplySummary summary)
    {
        var ops = log.Operations ?? Array.Empty<ComposeOperation>();
        var schemaVersion = log.SchemaVersion;

        // Inline ops grouped by paraId (first-seen order preserved) + a single structural/global unit.
        var inlineOrder = new List<string>();
        var inlineOps = new Dictionary<string, List<ComposeOperation>>(StringComparer.OrdinalIgnoreCase);
        var structural = new List<ComposeOperation>();
        foreach (var op in ops)
        {
            if (IsStructuralOrGlobalOp(op))
            {
                structural.Add(op);
                continue;
            }
            if (!inlineOps.TryGetValue(op.ParaId, out var list))
            {
                list = new List<ComposeOperation>();
                inlineOps[op.ParaId] = list;
                inlineOrder.Add(op.ParaId);
            }
            list.Add(op);
        }

        // Distribute anchored comments into their paraId's unit; a comment whose paraId carries no inline op
        // gets its own comment-only unit (still applied in the inline pass, so it lands before any structural
        // change to that paragraph — mirrors the engine's comments-first ordering).
        var commentsByPara = new Dictionary<string, List<ComposeAnchoredComment>>(StringComparer.OrdinalIgnoreCase);
        var unbakeableComments = 0;
        foreach (var c in comments ?? Array.Empty<ComposeAnchoredComment>())
        {
            var key = c.ParaId ?? string.Empty;
            if (!commentsByPara.TryGetValue(key, out var list))
            {
                list = new List<ComposeAnchoredComment>();
                commentsByPara[key] = list;
                if (!inlineOps.ContainsKey(key))
                {
                    inlineOrder.Add(key); // comment-only paragraph unit
                    inlineOps[key] = new List<ComposeOperation>();
                }
            }
            list.Add(c);
        }

        var unresolved = new List<UnresolvedComposeOp>();
        var appliedCount = 0;
        // agreements-r1 UAT round-1 #4: a COMMENT is a NON-DESTRUCTIVE change, so a comments-only review
        // save (empty op-log — a review placed advisory notes but made no text edits) must degrade
        // gracefully, not lose the WHOLE save when one note can't anchor. Pre-fix the "did anything apply?"
        // signal counted only OPS, so a fully-bakeable comment contributed 0 and the caller's
        // `appliedCount == 0` guard re-threw the anchor refusal → 422, discarding even the notes that DID
        // anchor. Count baked comments as applied work (folded into the summary below) so the guard stays
        // correct AND surface the unbakeable notes on `unresolved` (skip-with-report, never a silent drop).
        // A failing TEXT op still lands in `unresolved` exactly as before — its honest contract is unchanged.
        var bakedCommentCount = 0;
        var current = baseline;

        // Inline paragraph units first, then the structural unit LAST.
        foreach (var paraId in inlineOrder)
        {
            var unitOps = inlineOps[paraId];
            commentsByPara.TryGetValue(paraId, out var unitComments);
            var (bytes, refusal) = TryApplyPatchUnit(current, schemaVersion, unitOps, unitComments, author, observedAt, trackChanges);
            if (refusal is null)
            {
                current = bytes;
                appliedCount += unitOps.Count;
                if (unitComments is not null)
                    bakedCommentCount += unitComments.Count;
            }
            else
            {
                foreach (var op in unitOps)
                    unresolved.Add(new UnresolvedComposeOp(op.ParaId, op.GetType().Name, refusal.Kind.ToString(), refusal.Message));
                if (unitComments is not null)
                {
                    unbakeableComments += unitComments.Count;
                    // Surface each un-anchorable advisory note so the client reports it (skip-with-report).
                    // OpType "AdvisoryComment" distinguishes a non-destructive note from a lost edit op.
                    foreach (var c in unitComments)
                        unresolved.Add(new UnresolvedComposeOp(c.ParaId ?? string.Empty, "AdvisoryComment", refusal.Kind.ToString(), refusal.Message));
                }
            }
        }

        if (structural.Count > 0)
        {
            var (bytes, refusal) = TryApplyPatchUnit(current, schemaVersion, structural, null, author, observedAt, trackChanges);
            if (refusal is null)
            {
                current = bytes;
                appliedCount += structural.Count;
            }
            else
            {
                foreach (var op in structural)
                    unresolved.Add(new UnresolvedComposeOp(op.ParaId, op.GetType().Name, refusal.Kind.ToString(), refusal.Message));
            }
        }

        if (unbakeableComments > 0)
        {
            _logger.LogWarning(
                "Compose save: best-effort recovery could not bake {UnbakeableComments} advisory comment(s) whose paragraph unit refused.",
                unbakeableComments);
        }

        // Comments are first-class items in the partial-apply accounting alongside ops: Total counts every
        // op + comment, AppliedCount counts applied ops + baked comments, and the invariant
        // Total == AppliedCount + UnresolvedCount holds. An op-only batch (the existing seam cases) has no
        // comments, so these reduce to ops.Count / appliedCount exactly as before (no behavior change).
        summary = new PartialApplySummary(
            Total: ops.Count + (comments?.Count ?? 0),
            AppliedCount: appliedCount + bakedCommentCount,
            UnresolvedCount: unresolved.Count,
            Unresolved: unresolved,
            ComputedAtUtc: observedAt);
        return current;
    }

    /// <summary>
    /// Applies ONE prong-1 unit through the patch engine, returning the patched bytes on success or the
    /// ORIGINAL bytes + the op-level <see cref="ComposePatchException"/> on refusal. A batch-level refusal
    /// (malformed / schema — see <see cref="IsBatchLevelPatchRefusal"/>) is rethrown (the cumulative bytes are
    /// unusable). A unit with no ops and no comments is a byte-identical no-op (the engine's passthrough).
    /// </summary>
    private (byte[] Bytes, ComposePatchException? Refusal) TryApplyPatchUnit(
        byte[] input,
        string schemaVersion,
        IReadOnlyList<ComposeOperation> unitOps,
        IReadOnlyList<ComposeAnchoredComment>? unitComments,
        string author,
        DateTimeOffset observedAt,
        bool trackChanges)
    {
        try
        {
            var bytes = _patchEngine.Apply(
                input,
                new ComposeOperationLog { SchemaVersion = schemaVersion, Operations = unitOps },
                unitComments,
                author,
                observedAt,
                trackChanges: trackChanges);
            return (bytes, null);
        }
        catch (ComposePatchException ex) when (!IsBatchLevelPatchRefusal(ex.Kind))
        {
            return (input, ex);
        }
    }

    /// <summary>0-based index of the FIRST current paraId equal to <paramref name="paraId"/> (case-sensitive
    /// — <see cref="AnnotationReanchorService.ExtractParaIds"/> already upper-cases every id), or -1 when
    /// absent/null.</summary>
    private static int IndexOfParaId(IReadOnlyList<string?> paraIds, string? paraId)
    {
        if (string.IsNullOrEmpty(paraId))
        {
            return -1;
        }

        for (var i = 0; i < paraIds.Count; i++)
        {
            if (string.Equals(paraIds[i], paraId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Fail-closed summary: every op/comment in <paramref name="request"/> surfaces as ORPHAN
    /// (never silently applied, never silently dropped) when the current base could not be re-downloaded or
    /// read.</summary>
    private static ReanchorSummary BuildAllOrphanSummary(SaveComposeDocumentRequest request, DateTimeOffset observedAt)
    {
        var opsCount = request.OperationLog?.Operations.Count ?? 0;
        var commentsCount = request.Comments?.Count ?? 0;
        var total = opsCount + commentsCount;

        var annotations = new List<ReanchoredAnnotation>(total);
        for (var i = 0; i < opsCount; i++)
        {
            annotations.Add(new ReanchoredAnnotation(
                Id: $"op-{i}", Type: request.OperationLog!.Operations[i].GetType().Name, Preview: null,
                Band: ReanchorBand.Orphan, Confidence: 0.0, MatchedParagraphIndex: -1,
                ContentSimilarity: 0.0, StructuralProximity: 0.0, Ambiguous: false, MatchedParagraphPreview: null));
        }
        for (var i = 0; i < commentsCount; i++)
        {
            annotations.Add(new ReanchoredAnnotation(
                Id: $"comment-{i}", Type: "comment", Preview: request.Comments![i].CommentText,
                Band: ReanchorBand.Orphan, Confidence: 0.0, MatchedParagraphIndex: -1,
                ContentSimilarity: 0.0, StructuralProximity: 0.0, Ambiguous: false, MatchedParagraphPreview: null));
        }

        return new ReanchorSummary(
            DocumentSpeId: request.DocumentSpeId,
            Total: total,
            AutoCount: 0,
            ReviewCount: 0,
            OrphanCount: total,
            Annotations: annotations,
            ComputedAtUtc: observedAt);
    }
}
