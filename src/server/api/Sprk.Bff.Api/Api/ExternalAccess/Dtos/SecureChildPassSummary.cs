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

/// <summary>One table of a <see cref="SecureChildPassSummary"/>.</summary>
public sealed record SecureChildPassTable(
    string Table, int Examined, int AlreadyCorrect, int Reowned, int Refused, int Failed, int NeedsF3);
