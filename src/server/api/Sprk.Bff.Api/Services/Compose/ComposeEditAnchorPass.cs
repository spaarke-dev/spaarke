namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-C01/C02/C03 (spaarkeai-compose-r8 task 051) — the batch-level, ANCHOR-FIRST edit validation pass:
/// the Compose-owned validation path the ADR-043/041 assessment (§7, C-7) designates as the home for
/// closed-set validation. One fixed ordering, never the reverse:
///
/// <list type="number">
///   <item><b>DETERMINISTIC</b> — an edit carrying <c>target_para_id</c> and/or <c>target_ref</c> is
///   resolved by <see cref="ComposeAnchorResolver"/> against the supplied reference map. It resolves to a
///   paragraph or it is REFUSED. It never falls through to step 2, so no text search runs for it — that is
///   the FR-C01/C02 guarantee, and it is asserted by test rather than assumed.</item>
///   <item><b>LEGACY TEXT</b> — only edits carrying NO anchor reach
///   <see cref="IComposeEditValidator.Validate"/> and its <c>target_text</c> search. Task 052 (FR-C04)
///   retires this leg; until every anchor source is live, deleting it early would break whichever source
///   was missed, so it stays and is simply unreachable for anchored edits.</item>
/// </list>
///
/// <para>
/// <b>Why the split matters mechanically.</b> The text validator indexes verdicts by position in the list
/// it was handed, so anchored edits cannot merely be ignored in its output — they must never be IN its
/// input, or <c>FindAll</c> runs over the whole document for an edit whose target was already known
/// exactly. This pass therefore hands the validator only the un-anchored subset and maps its verdict
/// indices back onto the original batch positions.
/// </para>
///
/// <para><b>Pure.</b> No I/O and no DI of its own; the text validator is passed in by the caller.</para>
/// </summary>
public static class ComposeEditAnchorPass
{
    /// <summary>
    /// Validates <paramref name="edits"/> anchor-first. See the type remarks for the ordering contract.
    /// </summary>
    /// <param name="documentText">The plaintext projection the legacy text leg searches. Only read for
    /// un-anchored edits; an all-anchored batch never touches it.</param>
    /// <param name="edits">The proposed batch, in request order. Verdict indices refer to this order.</param>
    /// <param name="referenceMap">The closed set + numbering data. Null/empty refuses every anchored edit
    /// (<see cref="EditErrorKind.NoReferenceMap"/>) rather than degrading them to a text search.</param>
    /// <param name="textValidator">The legacy text-search validator (task 052 removes this parameter).</param>
    public static BatchValidationResult Validate(
        string documentText,
        IReadOnlyList<ProposedEdit> edits,
        IReadOnlyList<ParaIdMapEntry>? referenceMap,
        IComposeEditValidator textValidator)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(textValidator);

        var verdicts = new EditVerdict?[edits.Count];

        // Pass 1 — anchors. Each anchored edit is decided here and removed from the text leg's input.
        var unanchored = new List<ProposedEdit>(edits.Count);
        var unanchoredOriginalIndex = new List<int>(edits.Count);

        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            var anchor = ComposeAnchorResolver.Resolve(edit.TargetParaId, edit.TargetRef, referenceMap);

            switch (anchor.Status)
            {
                case ComposeAnchorStatus.Resolved:
                    verdicts[i] = new EditVerdict(
                        EditIndex: i,
                        IsValid: true,
                        Matches: Array.Empty<ResolvedMatch>(),
                        Error: null,
                        ResolvedParaId: anchor.ParaId);
                    break;

                case ComposeAnchorStatus.NoAnchor:
                    unanchored.Add(edit);
                    unanchoredOriginalIndex.Add(i);
                    break;

                default:
                    verdicts[i] = new EditVerdict(
                        EditIndex: i,
                        IsValid: false,
                        Matches: Array.Empty<ResolvedMatch>(),
                        Error: BuildAnchorError(i, anchor));
                    break;
            }
        }

        // Pass 2 — legacy text search, over the un-anchored subset ONLY.
        IReadOnlyList<EditValidationError> batchErrors = Array.Empty<EditValidationError>();
        if (unanchored.Count > 0)
        {
            var textResult = textValidator.Validate(documentText, unanchored);
            batchErrors = textResult.BatchErrors;

            foreach (var verdict in textResult.Verdicts)
            {
                // Re-key onto the caller's batch positions; the validator numbered them 0..n over the subset.
                var originalIndex = unanchoredOriginalIndex[verdict.EditIndex];
                verdicts[originalIndex] = verdict with { EditIndex = originalIndex };
            }
        }

        // Every slot is filled by construction (each edit takes exactly one of the three branches above),
        // but materialize defensively rather than with a null-forgiving operator.
        var ordered = new List<EditVerdict>(edits.Count);
        for (var i = 0; i < verdicts.Length; i++)
        {
            ordered.Add(verdicts[i] ?? new EditVerdict(
                i, IsValid: false, Matches: Array.Empty<ResolvedMatch>(),
                Error: new EditValidationError(
                    EditErrorKind.NoMatch,
                    $"Edit {i + 1}: no verdict was produced.",
                    MatchCount: 0,
                    Examples: Array.Empty<MatchExample>(),
                    ResolutionHint: "Resubmit this edit.")));
        }

        return new BatchValidationResult(ordered, batchErrors);
    }

    /// <summary>
    /// Maps an anchor refusal onto the batch's existing structured-error shape. <c>MatchCount</c> is 0 and
    /// <c>Examples</c> is empty by construction: an anchor refusal has no text occurrences to show, and
    /// fabricating "nearest" examples would reintroduce exactly the guessing this pass removes.
    /// </summary>
    private static EditValidationError BuildAnchorError(int index, ComposeAnchorResolution anchor)
    {
        var (kind, hint) = anchor.Status switch
        {
            ComposeAnchorStatus.UnknownParaId => (
                EditErrorKind.UnknownParaId,
                "Re-issue the edit with a target_para_id drawn from the paragraph set supplied with the "
                + "request. Do not substitute a similar id."),

            ComposeAnchorStatus.UnresolvedReference => (
                EditErrorKind.UnresolvedReference,
                "Name a clause number that exists in this document, or supply target_para_id directly."),

            ComposeAnchorStatus.AmbiguousReference => (
                EditErrorKind.AmbiguousReference,
                anchor.Candidates.Count > 0
                    ? $"Split this into one edit per clause, or target one of: {string.Join(", ", anchor.Candidates.Take(5))}."
                    : "Split this into one edit per clause."),

            ComposeAnchorStatus.ConflictingAnchors => (
                EditErrorKind.ConflictingAnchors,
                "Supply target_para_id OR target_ref, not both — or make them agree."),

            _ => (
                EditErrorKind.NoReferenceMap,
                "Include the document's referenceMap with the request, or omit the anchor to use the "
                + "legacy text path."),
        };

        return new EditValidationError(
            kind,
            $"Edit {index + 1}: {anchor.Reason}",
            MatchCount: 0,
            Examples: Array.Empty<MatchExample>(),
            ResolutionHint: hint);
    }
}
