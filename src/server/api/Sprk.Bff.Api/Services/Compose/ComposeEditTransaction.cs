namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-21 (task 022) — the snapshot/rollback layer wrapping <see cref="ComposeEditBatch"/>
/// (FR-20, task 021) per design.md §6.1 "atomic suggest-or-fail" and
/// <c>notes/spikes/spike-3-edit-batch.md</c> §5.2. This class does NOT re-implement any of
/// <see cref="ComposeEditBatch"/>'s own logic (Phase 1 fatal-validation-gate, Phase 3 non-fatal
/// within-batch overlap skip) — it is a thin wrapper that:
/// <list type="number">
/// <item>captures the pre-batch document as an explicit <see cref="ComposeEditTransactionResult.Snapshot"/>,</item>
/// <item>delegates entirely to <see cref="ComposeEditBatch.Apply"/>,</item>
/// <item>surfaces the SAME byte-identical guarantee at its own boundary when the batch's fatal
/// gate rolled back, and</item>
/// <item>adds a capability FR-20 does not have: an explicit, caller-requested
/// <see cref="Rollback"/> that discards an ALREADY-COMMITTED result and reverts to the
/// snapshot (e.g. a downstream business-rule veto after inspecting
/// <see cref="ComposeEditBatchResult.Skipped"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapshot cost (Spike 3 §5.2 note 2)</b>: <c>documentText</c> is a plain immutable
/// <see cref="string"/> at this layer (Phase 2/DOCX-shuttle offset-source work — tasks 025+ — is
/// out of scope here per Spike 3 §6). Holding a reference to the input string as
/// <see cref="ComposeEditTransactionResult.Snapshot"/> IS the snapshot: no copy, no clone, and it
/// can never be mutated by anything <see cref="ComposeEditBatch"/> does (which always builds a
/// NEW string via <see cref="System.Text.StringBuilder"/>, per <c>ComposeEditBatch.Apply</c>
/// Phase 4). This mirrors adeu's <c>takeSnapshot</c> intent (deep <c>cloneNode</c> of DOM parts)
/// at zero cost for the string-projection representation this pipeline currently uses.
/// </para>
/// <para>
/// <b>ADR-013 facade boundary</b>: pure, deterministic wrapper logic. Injects no
/// <c>IOpenAiClient</c>/executor/<c>IConsumerRoutingService</c> — this class (and its sole
/// dependency, <see cref="ComposeEditBatch"/>) never reaches the model.
/// </para>
/// <para>
/// <b>ADR-010 DI minimalism</b>: registered as a concrete singleton in
/// <c>ComposeModule.AddComposeModule</c>. It holds NO per-operation instance state — every
/// snapshot is a method-local value captured inside <see cref="Execute"/> and returned to the
/// caller as part of <see cref="ComposeEditTransactionResult"/>, never stored on <c>this</c>.
/// That is what makes a shared singleton instance safe under concurrent requests.
/// </para>
/// </remarks>
public sealed class ComposeEditTransaction
{
    private readonly ComposeEditBatch _batch;

    public ComposeEditTransaction(ComposeEditBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _batch = batch;
    }

    /// <summary>
    /// Snapshots <paramref name="documentText"/>, runs the FR-20 batch, and returns a
    /// <see cref="ComposeEditTransactionResult"/> that either commits the transformed document
    /// or (on the batch's own fatal validation gate) reports the untouched snapshot back.
    /// </summary>
    /// <param name="documentText">
    /// The pre-batch document (plaintext projection — same contract as
    /// <see cref="IComposeEditValidator.Validate"/> / <see cref="ComposeEditBatch.Apply"/>).
    /// </param>
    /// <param name="edits">The proposed edits, in caller order — passed through unchanged.</param>
    /// <param name="validation">
    /// The <see cref="BatchValidationResult"/> from validating <paramref name="edits"/> against
    /// <paramref name="documentText"/> (same contract as <see cref="ComposeEditBatch.Apply"/>).
    /// </param>
    public ComposeEditTransactionResult Execute(
        string documentText,
        IReadOnlyList<ProposedEdit> edits,
        BatchValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(validation);

        // The snapshot: a plain reference to the pre-batch string. See remarks — no clone needed.
        var snapshot = documentText;

        var batchResult = _batch.Apply(documentText, edits, validation);

        // EDGE-T1 (all-failing / single-failing batch — the FATAL path): ComposeEditBatch already
        // rolled back internally (Committed == false, its own DocumentText == the untouched
        // input). The transaction surfaces the identical guarantee at its own boundary: Snapshot
        // and DocumentText are the SAME string reference here, so "byte-identical" holds
        // trivially, and ValidationErrors (one per failing edit) flow through via Batch.
        if (!batchResult.Committed)
        {
            return new ComposeEditTransactionResult(
                Committed: false,
                DocumentText: snapshot,
                Snapshot: snapshot,
                Batch: batchResult);
        }

        // EDGE-T2 (empty batch — degenerate transaction): ComposeEditBatch's own EDGE-2 already
        // returns the document unchanged when there is nothing to apply; Committed is still
        // `true` here (a no-op batch is not a failure) and DocumentText == Snapshot by content.
        return new ComposeEditTransactionResult(
            Committed: true,
            DocumentText: batchResult.DocumentText,
            Snapshot: snapshot,
            Batch: batchResult);
    }

    /// <summary>
    /// EDGE-T3 — caller-requested abort. Discards an ALREADY-COMMITTED
    /// <see cref="ComposeEditTransactionResult"/> (from a prior <see cref="Execute"/> call) and
    /// reverts to its <see cref="ComposeEditTransactionResult.Snapshot"/>, byte-for-byte. This is
    /// the capability FR-20 alone does not provide: a batch can resolve and apply cleanly (no
    /// fatal validation failure) yet a caller may still decide, after inspecting
    /// <see cref="ComposeEditBatchResult.Skipped"/> or applying other business policy, that the
    /// result should not be kept.
    /// </summary>
    /// <param name="transaction">A result previously returned by <see cref="Execute"/>.</param>
    /// <returns>
    /// A rolled-back <see cref="ComposeEditTransactionResult"/> (<c>Committed == false</c>,
    /// <c>DocumentText == Snapshot</c>). Idempotent: rolling back a transaction that is already
    /// rolled back (<c>Committed == false</c>) returns it unchanged.
    /// </returns>
    public ComposeEditTransactionResult Rollback(ComposeEditTransactionResult transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!transaction.Committed)
        {
            return transaction;
        }

        return transaction with
        {
            Committed = false,
            DocumentText = transaction.Snapshot,
        };
    }
}
