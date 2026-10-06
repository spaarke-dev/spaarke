using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// unified-access-control-r2 task 148 — what a provisioning or unsecure call did to the record's EXISTING related records
/// (its children): how many were re-owned, how many are still not where the ownership rule puts them, per table. Carried
/// on the success response (additive to the JSON contract) and, as the same numbers, on the
/// <c>children_incomplete</c> ProblemDetails.
/// </summary>
/// <param name="Status">The pass's status: <c>Completed</c> | <c>Incomplete</c> | <c>NotApplicable</c> | <c>Failed</c>.</param>
/// <param name="Reowned">Related records re-owned by this call.</param>
/// <param name="Remaining">Related records NOT yet where the rule puts them (refused, failed, or their shares not in line).</param>
/// <param name="Tables">Per-table counts.</param>
/// <param name="NeedsF3">Isolated related records the ownership rule would release, held isolated because only Unsecure
/// (a Full Access holder or the record's creator) releases one (owner round 24 item 2; task 148 r2). Not "remaining": no
/// repeat of this call moves them. Additive.</param>
public sealed record SecureChildPassSummary(
    string Status,
    int Reowned,
    int Remaining,
    IReadOnlyList<SecureChildPassTable> Tables,
    int NeedsF3)
{
    /// <summary>The summary of a reconciler report.</summary>
    public static SecureChildPassSummary From(SecureChildReconcileReport report) => new(
        report.Status.ToString(),
        report.ChildrenReowned,
        report.ChildrenRemaining,
        report.Tables
            .Select(t => new SecureChildPassTable(t.Table, t.Examined, t.AlreadyCorrect, t.Changed, t.Refused, t.Failed, t.NeedsF3))
            .ToList(),
        report.ChildrenNeedingF3);
}

/// <summary>
/// Round 26 item 3 (wired at the batch-4 integration) — what a Make Secure's file relocation did, for the provisioning
/// response and its <c>files_incomplete</c> refusal, and for the secure-child reconciliation's report. Counts are in full;
/// the document lists are capped at <see cref="MaxListed"/>.
/// </summary>
/// <param name="Examined">The documents handed to the relocator.</param>
/// <param name="Moved">Moved in this call (copied, verified, re-pointed), the rows moved along with them included.</param>
/// <param name="Incomplete">Not settled (a failed, pending, undecidable or planned move): the next call or run finishes them.</param>
/// <param name="Unresolvable">Rows that name no file the relocator can resolve (FileMissing): reported, never moved or deleted.</param>
/// <param name="Counts">Outcomes by relocation state.</param>
/// <param name="IncompleteDocuments">The incomplete documents (capped).</param>
/// <param name="UnresolvableDocuments">The unresolvable documents (capped).</param>
/// <param name="SourceChangedAfterMove">Sources edited after their move (round 45 item 4).</param>
/// <param name="VersionsTruncated">Histories the target's version limit truncated (round 45 item 1).</param>
/// <param name="SourceKeptForOtherRecords">Sources kept because rows of other records still use them (round 37 item 2).</param>
public sealed record MakeSecureFilesSummary(
    int Examined,
    int Moved,
    int Incomplete,
    int Unresolvable,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<Guid> IncompleteDocuments,
    IReadOnlyList<Guid> UnresolvableDocuments,
    int SourceChangedAfterMove,
    int VersionsTruncated,
    int SourceKeptForOtherRecords)
{
    /// <summary>How many document ids each list carries at most.</summary>
    public const int MaxListed = 50;

    /// <summary>Nothing to move: the record has no isolated document.</summary>
    public static MakeSecureFilesSummary None { get; } =
        new(0, 0, 0, 0, new Dictionary<string, int>(), [], [], 0, 0, 0);

    /// <summary>The summary of a relocation batch.</summary>
    public static MakeSecureFilesSummary From(Sprk.Bff.Api.Services.Documents.DocumentRelocationBatchResult batch)
    {
        var all = batch.Outcomes.Concat(batch.Outcomes.SelectMany(o => o.MovedAlong)).ToList();
        var unresolvable = batch.Incomplete.Where(IsUnresolvable).Select(o => o.DocumentId).Distinct().ToList();
        var incomplete = batch.Incomplete.Where(o => !IsUnresolvable(o)).Select(o => o.DocumentId).Distinct().ToList();
        return new MakeSecureFilesSummary(
            batch.Outcomes.Count,
            all.Count(o => o.State is Sprk.Bff.Api.Services.Documents.RelocationState.Relocated
                or Sprk.Bff.Api.Services.Documents.RelocationState.RelocatedSourceKeptForOtherRecords),
            incomplete.Count,
            unresolvable.Count,
            batch.Counts.ToDictionary(c => c.Key.ToString(), c => c.Value),
            incomplete.Take(MaxListed).ToList(),
            unresolvable.Take(MaxListed).ToList(),
            batch.SourceChangedAfterMove.Count,
            batch.VersionsTruncated.Count,
            batch.SourceKeptForOtherRecords.Count);
    }

    /// <summary>
    /// A row that names no file the relocator can resolve — the item is not in the drive the row names, or its item id is a
    /// document id the communication-archive path wrote (word-add-in-r1's archive bug). There is nothing to move, so it is
    /// reported and never blocks; anything it still owes from an earlier move keeps it incomplete.
    /// </summary>
    internal static bool IsUnresolvable(Sprk.Bff.Api.Services.Documents.DocumentRelocationOutcome outcome)
        => outcome.State == Sprk.Bff.Api.Services.Documents.RelocationState.FileMissing && outcome.Pending.Count == 0;
}

/// <summary>One table of a <see cref="SecureChildPassSummary"/>.</summary>
public sealed record SecureChildPassTable(
    string Table, int Examined, int AlreadyCorrect, int Reowned, int Refused, int Failed, int NeedsF3);
