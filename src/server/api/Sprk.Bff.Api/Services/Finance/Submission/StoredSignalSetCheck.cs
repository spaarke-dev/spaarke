using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// D-122 (ontology-r1 review of ebilling-r1 tasks 100 and 105, F1-1 and K3): what a Signal set held in <c>sprk_findingsjson</c> may be
/// trusted for. That column is on <c>sprk_invoicesubmission</c>, which an ordinary user can write (no field security), so a set stored there
/// is a CLAIM: its Signal ids, read time and subjects are never taken as they stand. Each Signal is read app-only and its subject is taken
/// from the ROW (the subject lookup that repeats the pair id, <see cref="SignalWriter.SubjectEntityOf"/>, the commit route's rule), and the
/// row's dedupe key must name the same subject. Every subject must be the invoice itself or one of ITS lines. The read time must lie inside
/// the submission's life: not before it was created, not after now, each allowing <see cref="FutureSkew"/>. Each Signal's claimed policy
/// code must be its row's.
/// </summary>
/// <remarks>
/// <para>Used by 049's automatic rejection (<c>resolvedSignalSet</c>, resumed from the findings) and 105's override and return
/// (<c>humanDecisionSignalSet</c>). <see cref="RowSubjectOf"/> is also the K3 rule of the open-Signal read
/// (<see cref="InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync"/>): a Signal whose dedupe-key subject differs from its regarding row is
/// skipped, so no row is closed whose rights were not asked.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceSubmissionAfterSteps.ReadOpenSignalsAsync"/>
/// (finds OPEN Signals by subject; it cannot vouch for a given id list) and <see cref="SignalWriter.SubjectEntityOf"/> (one row's subject).
/// Extension: the check reads Signals by id and the invoice's lines, which neither does; it is a separate static class rather than more of
/// 049's after-steps so the three callers share one rule. Cost of doing nothing: a user who plants a set naming another invoice's Signal, or
/// an old read time, has the app identity close or re-close Signals they hold no right on (probe-proven, F1-1).</para>
/// </remarks>
internal static class StoredSignalSetCheck
{
    /// <summary>How far a stored read time may be ahead of this server's clock, or behind the submission's Dataverse-set <c>createdon</c>
    /// (servers' clocks differ by seconds; a minute is generous).</summary>
    internal static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(1);

    /// <summary>The submission column that bounds a stored read time from below: <c>createdon</c>, set by Dataverse when the BFF creates the
    /// submission (processing starts after it) and never updatable.</summary>
    internal const string ProcessingStartColumn = "createdon";

    private const string SignalEntity = "sprk_signal";

    /// <summary>The columns <see cref="RowSubjectOf"/> reads: the dedupe key, the pair id and every subject lookup.</summary>
    internal static readonly string[] SubjectColumns =
        ["sprk_dedupekey", "sprk_regardingrecordid", .. SignalWriter.RegardingLookupByEntity.Values];

    /// <summary>The answer: trusted with the row-derived subjects (distinct), or not trusted and why (a code for the log, never data).</summary>
    internal sealed record Result(bool Trusted, string? Why, IReadOnlyList<(string Entity, Guid Id)> Subjects)
    {
        internal static Result No(string why) => new(false, why, []);
    }

    /// <summary>
    /// The subject a Signal row is filed on, and the policy code, when its dedupe key names the SAME subject as its regarding row; null when
    /// the key does not parse, the row names no single subject, or the two differ (K3: fail closed).
    /// </summary>
    internal static (string PolicyCode, string Entity, Guid Id)? RowSubjectOf(Entity row)
    {
        if (!SignalWriter.TryParseDedupeKey(row.GetAttributeValue<string>("sprk_dedupekey"), out var code, out var keyEntity, out var keyId, out _)
            || !Guid.TryParse(row.GetAttributeValue<string>("sprk_regardingrecordid"), out var pairId) || pairId == Guid.Empty
            || SignalWriter.SubjectEntityOf(pairId, lookup => row.GetAttributeValue<EntityReference>(lookup)?.Id) is not { } entity
            || !string.Equals(entity, keyEntity, StringComparison.Ordinal) || pairId != keyId)
        {
            return null;
        }

        return (code, entity, pairId);
    }

    /// <summary>
    /// Checks a STORED <paramref name="set"/> against <paramref name="invoiceId"/>. Reads: the set's Signals by id (one query), then, when a
    /// line is named, the invoice's lines (one query); both app-only through <paramref name="entities"/>. A listed Signal that no longer
    /// exists has no row to vouch for and nothing to close, so it is passed over. A read fault propagates (the caller's transient answer).
    /// </summary>
    /// <param name="processingStartedUtc">The submission's <see cref="ProcessingStartColumn"/>; null fails closed.</param>
    internal static async Task<Result> CheckAsync(
        IGenericEntityService entities,
        TimeProvider time,
        Guid invoiceId,
        InvoiceSubmissionAfterSteps.ResolvedSignalSet set,
        DateTime? processingStartedUtc,
        CancellationToken ct)
    {
        if (processingStartedUtc is not { } started)
        {
            return Result.No("no_processing_start");
        }

        var startedUtc = started.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(started, DateTimeKind.Utc) : started.ToUniversalTime();
        // K1 (ontology-r1 round 2): createdon is Dataverse's clock and the read time this server's, so the lower bound allows the same skew
        // as the upper one; without it a server running behind Dataverse would refuse its own set, and a 049 resume, permanently.
        if (set.SetReadAtUtc < startedUtc - FutureSkew)
        {
            return Result.No("read_before_processing");
        }

        if (set.SetReadAtUtc > time.GetUtcNow().UtcDateTime + FutureSkew)
        {
            return Result.No("read_in_future");
        }

        return await SubjectsAsync(entities, invoiceId, set, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The subject half of <see cref="CheckAsync"/>, with no read-time bound: for a set the WRITER stored with a Decision Record (105's K1
    /// retry and its repeat), whose read time the writer kept, but whose Signal ids are still checked against the rows and the invoice.
    /// </summary>
    internal static async Task<Result> SubjectsAsync(
        IGenericEntityService entities, Guid invoiceId, InvoiceSubmissionAfterSteps.ResolvedSignalSet set, CancellationToken ct)
    {
        var ids = set.Signals.Select(s => s.Id).Distinct().ToList();
        var claimed = set.Signals.ToLookup(s => s.Id, s => s.PolicyCode);
        if (ids.Count == 0)
        {
            return new Result(true, null, []);
        }

        var query = new QueryExpression(SignalEntity) { ColumnSet = new ColumnSet(SubjectColumns), NoLock = true, TopCount = ids.Count };
        query.Criteria.AddCondition("sprk_signalid", ConditionOperator.In, ids.Cast<object>().ToArray());
        var rows = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;

        var subjects = new HashSet<(string Entity, Guid Id)>();
        foreach (var row in rows)
        {
            if (!ids.Contains(row.Id))
            {
                continue;
            }

            // F1-b (ontology-r1 round 2): the policy code the set claims must be the ROW's. The claimed code is what the close and the
            // record's resolvedSignalSet fact carry, so a planted code would otherwise reach the append-only record.
            if (RowSubjectOf(row) is not { } subject
                || claimed[row.Id].Any(code => !string.Equals(code, subject.PolicyCode, StringComparison.Ordinal)))
            {
                return Result.No("subject_unreadable");
            }

            var onInvoice = subject.Entity == InvoiceReviewService.InvoiceEntity && subject.Id == invoiceId;
            if (!onInvoice && subject.Entity != InvoiceSubmissionPersister.LineEntity)
            {
                return Result.No("subject_foreign");
            }

            subjects.Add((subject.Entity, subject.Id));
        }

        var lines = subjects.Where(s => s.Entity == InvoiceSubmissionPersister.LineEntity).Select(s => s.Id).ToList();
        if (lines.Count > 0)
        {
            // The named lines that are THIS invoice's: one query, by the invoice and the ids.
            var lineQuery = new QueryExpression(InvoiceSubmissionPersister.LineEntity) { ColumnSet = new ColumnSet(false), NoLock = true };
            lineQuery.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
            lineQuery.Criteria.AddCondition(InvoiceSubmissionPersister.LineEntity + "id", ConditionOperator.In, lines.Cast<object>().ToArray());
            var own = (await entities.RetrieveMultipleAsync(lineQuery, ct).ConfigureAwait(false)).Entities.Select(r => r.Id).ToHashSet();
            if (lines.Any(line => !own.Contains(line)))
            {
                return Result.No("subject_foreign");
            }
        }

        return new Result(true, null, subjects.ToList());
    }
}
