using System.Text;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-20 (task 021) — the deterministic 4-phase pipeline that applies a batch of PRE-VALIDATED
/// edits to a document without offset drift: resolve → sort descending → skip overlap → apply
/// bottom-up (design.md §6.1 "4-phase atomic batch pipeline"; validated by
/// <c>notes/spikes/spike-3-edit-batch.md</c> + <c>edit-batch-prototype.cs</c>, all 4 proofs
/// PASS). Ported faithfully from that prototype — the ordering/atomicity invariant is identical;
/// only the resolved-span SOURCE differs (production consumes <see cref="IComposeEditValidator"/>
/// output instead of re-resolving text itself, per the validator's contract: "edits enter the
/// batch pre-validated").
/// </summary>
/// <remarks>
/// <para>
/// <b>The single most important design fact (Spike 3 §2 — read this before changing anything)</b>:
/// there are TWO SEPARATE failure classes with OPPOSITE semantics. Do not conflate them.
/// <list type="bullet">
/// <item><b>Validation failure</b> — a per-edit <see cref="EditVerdict.IsValid"/> of <c>false</c>
/// (<c>not-found</c> / <c>ambiguous-strict</c> / <c>empty-target</c>, produced upstream by
/// <see cref="IComposeEditValidator"/>). This is FATAL: the WHOLE batch rolls back and NONE of
/// the edits apply — <see cref="ComposeEditBatchResult.DocumentText"/> is the untouched input,
/// byte-identical.</item>
/// <item><b>Within-batch span overlap</b> — two edits' resolved spans intersect. This is
/// NON-FATAL: the later-claimed overlapping span is skipped and reported
/// (<see cref="ComposeEditBatchResult.Skipped"/>), and the batch STILL COMMITS. Overlap is
/// detected and handled entirely inside THIS class (Phase 3) — it is intentionally NOT gated by
/// <see cref="BatchValidationResult.BatchErrors"/> (the validator's own <c>Overlap</c>-kind
/// batch errors exist for the client-facing <c>/edit-batch/validate</c> dry-run UX, a different
/// consumer; the apply pipeline's overlap semantics are this class's own contract).</item>
/// </list>
/// </para>
/// <para>
/// <b>ADR-013 facade boundary</b>: pure, deterministic text logic. Injects no
/// <c>IOpenAiClient</c>/executor/<c>IConsumerRoutingService</c> — nothing here reaches the model.
/// </para>
/// <para>
/// <b>ADR-010 DI minimalism</b>: stateless (no constructor dependencies) — intentionally NOT
/// registered in <c>ComposeModule</c> yet. No endpoint consumes it directly; FR-21
/// (<c>ComposeEditTransaction</c>, task 022) wraps it with document snapshot/apply semantics.
/// Register only when a direct consumer needs DI (single line:
/// <c>services.AddSingleton&lt;ComposeEditBatch&gt;();</c>).
/// </para>
/// <para>
/// <b>Batch-apply ordering is the WHOLE POINT (not this validator's job)</b>: every edit resolves
/// against the SAME original <c>documentText</c> (via <see cref="IComposeEditValidator.Validate"/>
/// upstream). Applying naively in list order (or any order that doesn't account for offset shift)
/// would corrupt the document as soon as an earlier-applied edit changes length. Sorting
/// descending and applying bottom-up means every remaining LOWER offset stays valid throughout
/// the splice sequence — the offset-stability guarantee this class exists to provide.
/// </para>
/// </remarks>
public sealed class ComposeEditBatch
{
    /// <summary>
    /// Applies <paramref name="edits"/> to <paramref name="documentText"/> using the pre-computed
    /// <paramref name="validation"/> result from <see cref="IComposeEditValidator.Validate"/>.
    /// </summary>
    /// <param name="documentText">
    /// The SAME plaintext projection <paramref name="validation"/> was resolved against (see
    /// <see cref="IComposeEditValidator"/> document-projection contract). Returned unchanged,
    /// byte-identical, when the batch rolls back.
    /// </param>
    /// <param name="edits">
    /// The proposed edits in caller order — <see cref="EditVerdict.EditIndex"/> in
    /// <paramref name="validation"/> indexes into this same list (used to recover
    /// <see cref="ProposedEdit.NewText"/> at apply time; the validator's resolved
    /// <see cref="ResolvedMatch"/> spans do not carry replacement text).
    /// </param>
    /// <param name="validation">
    /// The result of validating <paramref name="edits"/> against <paramref name="documentText"/>
    /// (one <see cref="EditVerdict"/> per edit, in the same order as <paramref name="edits"/>).
    /// </param>
    /// <returns>
    /// A <see cref="ComposeEditBatchResult"/> — see the two-failure-class remarks above for the
    /// <see cref="ComposeEditBatchResult.Committed"/> semantics.
    /// </returns>
    public ComposeEditBatchResult Apply(
        string documentText,
        IReadOnlyList<ProposedEdit> edits,
        BatchValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(validation);

        if (validation.Verdicts.Count != edits.Count)
        {
            throw new ArgumentException(
                $"validation.Verdicts.Count ({validation.Verdicts.Count}) must match edits.Count " +
                $"({edits.Count}) — every edit must have a corresponding verdict from the SAME " +
                "IComposeEditValidator.Validate call.",
                nameof(validation));
        }

        // PHASE 1 (continued from IComposeEditValidator) — the FATAL gate. Any per-edit
        // resolution failure (not-found / ambiguous-strict / empty-target) rolls back the WHOLE
        // batch: none of the edits apply, and the untouched document is returned verbatim.
        // EDGE-1: this is Proof 2's exact assertion (spike-3-edit-batch.md §4) — one invalid
        // edit among otherwise-valid ones still means zero edits apply.
        var validationErrors = validation.Verdicts
            .Where(v => !v.IsValid)
            .Select(v => v.Error!)
            .ToList();

        if (validationErrors.Count > 0)
        {
            return new ComposeEditBatchResult(
                Committed: false,
                DocumentText: documentText,
                Applied: Array.Empty<AppliedEdit>(),
                Skipped: Array.Empty<SkippedEdit>(),
                ValidationErrors: validationErrors);
        }

        // Flatten every resolved span across all (now guaranteed-valid) verdicts, tagged with its
        // origin edit index so we can recover NewText at apply time. match_mode:all fans one edit
        // out to N spans (IComposeEditValidator.Resolve) — each span is an independent apply
        // candidate below.
        var resolvedSpans = new List<(int EditIndex, ResolvedMatch Match)>();
        foreach (var verdict in validation.Verdicts)
        {
            foreach (var match in verdict.Matches)
            {
                resolvedSpans.Add((verdict.EditIndex, match));
            }
        }

        // EDGE-2: an empty edit list (or, degenerately, a non-empty edit list whose verdicts
        // resolved zero spans) returns the document completely unchanged — nothing applied,
        // nothing skipped, batch still commits.
        if (resolvedSpans.Count == 0)
        {
            return new ComposeEditBatchResult(
                Committed: true,
                DocumentText: documentText,
                Applied: Array.Empty<AppliedEdit>(),
                Skipped: Array.Empty<SkippedEdit>(),
                ValidationErrors: Array.Empty<EditValidationError>());
        }

        // PHASE 2 — SORT DESCENDING by resolved start offset (largest first). Tie-break by
        // longer span first so a fully-covering edit claims its range ahead of a nested one
        // (mirrors the prototype's ThenByDescending(End) tie-break).
        var ordered = resolvedSpans
            .OrderByDescending(r => r.Match.Offset)
            .ThenByDescending(r => r.Match.Offset + r.Match.Length)
            .ToList();

        // PHASE 3 — SKIP OVERLAP (NON-FATAL) via occupied ranges. First-claimed (highest offset,
        // or longer span on a tie) wins; a later-in-the-descending-order span that intersects an
        // already-claimed range is skipped and reported. THE BATCH STILL COMMITS — this is
        // deliberately NOT a validation failure (Spike 3 §2; Proof 4).
        // EDGE-3: two edits with genuinely overlapping spans — one applies, one is skipped.
        // EDGE-4: adjacent-but-not-overlapping spans (e.g. [10,20) and [20,30)) are NOT flagged —
        // half-open interval boundaries touch but do not intersect.
        // EDGE-5: identical target spans (same [Start,End) resolved twice) ARE flagged as
        // overlapping — the interval comparison treats an exact match as full intersection.
        var occupied = new List<(int Start, int End)>();
        var toApply = new List<(int EditIndex, ResolvedMatch Match)>();
        var skipped = new List<SkippedEdit>();
        foreach (var r in ordered)
        {
            int start = r.Match.Offset;
            int end = r.Match.Offset + r.Match.Length;
            bool overlaps = occupied.Any(o => start < o.End && o.Start < end);

            if (overlaps)
            {
                skipped.Add(new SkippedEdit(
                    r.EditIndex,
                    r.Match,
                    $"Edit {r.EditIndex + 1} [{start},{end}) overlaps an already-claimed span in this batch " +
                    "(first-resolved wins) — skipped, not applied."));
                continue;
            }

            occupied.Add((start, end));
            toApply.Add(r);
        }

        // PHASE 4 — APPLY BOTTOM-UP (already sorted descending). Splicing from the highest
        // offset down leaves every remaining LOWER offset untouched and therefore still valid —
        // this is the offset-stability guarantee (Proof 1). EDGE-6: NewText == "" is a pure
        // deletion (the matched span is removed and nothing is inserted in its place); an edit
        // resolved at offset 0 or at the document's tail applies identically to any other offset
        // because the splice only ever touches [Start,End).
        var sb = new StringBuilder(documentText);
        var applied = new List<AppliedEdit>();
        foreach (var r in toApply) // already sorted descending — bottom-up by construction
        {
            var newText = edits[r.EditIndex].NewText;
            sb.Remove(r.Match.Offset, r.Match.Length);
            sb.Insert(r.Match.Offset, newText);
            applied.Add(new AppliedEdit(r.EditIndex, r.Match, newText));
        }

        return new ComposeEditBatchResult(
            Committed: true,
            DocumentText: sb.ToString(),
            Applied: applied,
            Skipped: skipped,
            ValidationErrors: Array.Empty<EditValidationError>());
    }
}
