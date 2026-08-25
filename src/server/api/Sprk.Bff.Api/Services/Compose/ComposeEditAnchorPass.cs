namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-C01/C02/C03/C04 (spaarkeai-compose-r8 tasks 051 + 052) — the batch-level edit validation pass:
/// the Compose-owned validation path the ADR-043/041 assessment (§7, C-7) designates as the home for
/// closed-set validation. It has exactly ONE leg:
///
/// <list type="bullet">
///   <item><b>DETERMINISTIC</b> — an edit carrying <c>target_para_id</c> and/or <c>target_ref</c> is
///   resolved by <see cref="ComposeAnchorResolver"/> against the supplied reference map. It resolves to a
///   paragraph, or it is REFUSED with a structured <see cref="EditValidationError"/>.</item>
///   <item><b>NO ANCHOR ⇒ REFUSAL</b> — an edit that names no target at all yields
///   <see cref="EditErrorKind.NoAnchor"/>. It is NOT searched for.</item>
/// </list>
///
/// <para>
/// <b>The achieved contract (task 052, FR-C04).</b> Task 051 left a second leg in place — un-anchored
/// edits fell through to <c>IComposeEditValidator</c>'s whole-document <c>target_text</c> search — because
/// deleting it before every anchor source was live would have broken whichever source was missed. All
/// sources are live (tasks 051/054/055), so that leg and the validator behind it are DELETED. The
/// guarantee that used to need a throwing test double is now enforced by the TYPE SYSTEM: this pass takes
/// no document text and no text validator, so there is no document prose here to search and no collaborator
/// to search it with (ADR-049 I-7).
/// </para>
///
/// <para><b>Pure.</b> No I/O, no DI, no state. Total — every edit takes exactly one branch below.</para>
/// </summary>
public static class ComposeEditAnchorPass
{
    /// <summary>
    /// Validates <paramref name="edits"/> against the document's anchor set. See the type remarks for the
    /// contract: every edit resolves to a paragraph or is refused, and nothing here reads document prose.
    /// </summary>
    /// <param name="edits">The proposed batch, in request order. Verdict indices refer to this order.</param>
    /// <param name="referenceMap">The closed set + numbering data. Null/empty refuses every anchored edit
    /// (<see cref="EditErrorKind.NoReferenceMap"/>) rather than degrading it to a guess.</param>
    public static BatchValidationResult Validate(
        IReadOnlyList<ProposedEdit> edits,
        IReadOnlyList<ParaIdMapEntry>? referenceMap)
    {
        ArgumentNullException.ThrowIfNull(edits);

        var verdicts = new List<EditVerdict>(edits.Count);

        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            var anchor = ComposeAnchorResolver.Resolve(edit.TargetParaId, edit.TargetRef, referenceMap);

            verdicts.Add(anchor.Status == ComposeAnchorStatus.Resolved
                ? new EditVerdict(
                    EditIndex: i,
                    IsValid: true,
                    Matches: Array.Empty<ResolvedMatch>(),
                    Error: null,
                    ResolvedParaId: anchor.ParaId)
                : new EditVerdict(
                    EditIndex: i,
                    IsValid: false,
                    Matches: Array.Empty<ResolvedMatch>(),
                    Error: BuildAnchorError(i, anchor)));
        }

        // No batch-level errors are possible here: an anchored verdict carries no span, so two edits have
        // nothing to collide over. Cross-edit span overlap belongs to the apply side (ComposeEditBatch).
        return new BatchValidationResult(verdicts, Array.Empty<EditValidationError>());
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
            ComposeAnchorStatus.NoAnchor => (
                EditErrorKind.NoAnchor,
                "This edit named no target: supply target_para_id (a w14:paraId drawn from the paragraph "
                + "set supplied with the request) or target_ref (a clause number that exists in this "
                + "document). Quoting the text to be replaced is no longer a way to place an edit."),

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
                "Include the document's referenceMap with the request so the anchor can be validated "
                + "against the paragraph set it names."),
        };

        return new EditValidationError(
            kind,
            $"Edit {index + 1}: {anchor.Reason}",
            MatchCount: 0,
            Examples: Array.Empty<MatchExample>(),
            ResolutionHint: hint);
    }
}
