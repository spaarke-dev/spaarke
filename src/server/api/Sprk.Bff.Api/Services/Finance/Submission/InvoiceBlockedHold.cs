// ebilling-r1 task 165a (EB-D-200; uac-r2 option D, 2026-11-06): hold decisions read only server-written sources.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — FinanceBaseValues.HeldSubmissionConditions (the FR-20 column filter), read by a private IsHeldAsync in InvoiceApprovalService
//       and in InvoiceRejectReturnService. Both decide from the column state alone.
//   (2) Extension — HeldSubmissionConditions is ontology-r1's single definition and is also a FetchXML anti-join (pending spend), which cannot
//       read the field-secured findings; the two private readers are copies of one query. One static reader beside InvoiceFirmHold and
//       InvoiceProcedureHold, in their shape, replaces both copies and keeps the column filter as one of its sources.
//   (3) Cost of doing nothing — the approval hold is not decided from server-written sources, as EB-D-200 requires (a blocked invoice is
//       released only by an Override or Return with its Decision Record).
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure derivation and an app-only read; no DI registration, no endpoint,
// no package).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The FR-20 hold (D-101: a blocked internal submission holds its invoice from approval until Override or Return), decided only from
/// server-written sources (task 165a, EB-D-200).
/// </summary>
/// <remarks>
/// <para><b>Sources.</b> Every submission linked to the invoice (<c>sprk_invoicesubmission.sprk_invoice</c>), read app-only through the
/// <see cref="IGenericEntityService"/> the caller passes. Three checks; ANY one holds (fail closed: when the sources disagree, the invoice is held):</para>
/// <list type="number">
/// <item><b>The column state</b> (<see cref="FinanceBaseValues.HeldSubmissionConditions"/>, unchanged). It can only ADD to the held set here: the
/// release is decided only from the server-written sources below, never from these columns. When the column state differs from a release the
/// recorded outcome shows, a repeat of that Override or Return writes the released column state back (task 165a fix round 2).</item>
/// <item><b>The recorded outcome</b> in the field-secured <c>sprk_findingsjson</c> (Writers only; the BFF identity is a Writer): <c>kind</c> =
/// <c>Blocked</c> holds unless the findings carry a valid <c>override</c> or <c>return</c> stamp, which <see cref="SubmissionOverrideService"/>
/// writes in the same write that releases the submission. The 095d-2 re-run rewrites <c>kind</c> in its own commit and drops the Override
/// stamp, and the Return stamp unless the invoice is already Returned to Firm. Unreadable findings on another linked submission are left to <see cref="InvoiceFirmHold"/> and <see cref="InvoiceProcedureHold"/>,
/// which hold them (fail closed) under their own refusal codes; on the invoice's own submission check (3) holds first.</item>
/// <item><b>The invoice's own submission</b>: the submission whose id derives the invoice's
/// (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>; invoices are created only by the submission operation, FR-17) must be linked, and its
/// recorded outcome must be this invoice's. When the own submission is absent the invoice is held until an administrator restores it.</item>
/// </list>
/// <para>A read fault throws; every caller refuses or answers unavailable (held, never "not held").</para>
/// <para>Each hold carries a distinct reason code and the table and row id it was decided on (<see cref="Verdict"/>), which callers log; nothing
/// else (ADR-015).</para>
/// </remarks>
public static class InvoiceBlockedHold
{
    /// <summary>A linked submission's column state is the FR-20 hold (<see cref="FinanceBaseValues.HeldSubmissionConditions"/>).</summary>
    public const string HeldBySubmissionState = "ebilling.hold.blocked_submission";

    /// <summary>A linked submission's recorded outcome is Blocked with no release stamp, while its columns say it is not held: the sources disagree.</summary>
    public const string HeldByRecordedOutcome = "ebilling.hold.recorded_outcome_blocked";

    /// <summary>The invoice's own submission is absent, or its recorded outcome is not this invoice's.</summary>
    public const string HeldOwnSubmissionUnlinked = "ebilling.hold.own_submission_unlinked";

    /// <summary>
    /// The reviewer-facing detail of a <see cref="HeldOwnSubmissionUnlinked"/> hold (approve, reject and return keep their refusal codes). The
    /// Override and Return of a submission cannot release it: they decide only a submission linked to the invoice it created.
    /// </summary>
    public const string OwnSubmissionUnlinkedDetail =
        "The invoice has no linked originating submission, so it cannot be approved, rejected or returned here. An administrator must restore the link.";

    /// <summary>The findings member that records the outcome kind (<see cref="InvoiceSubmissionPersister.BuildFindingsJson(SubmissionEvaluationOutcome, Guid)"/>).</summary>
    internal const string KindMember = "kind";

    /// <summary>The findings member that records the invoice the outcome created.</summary>
    internal const string InvoiceIdMember = "invoiceId";

    /// <summary>The columns read from each linked submission.</summary>
    internal static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode,
        InvoiceSubmissionPersister.SubmissionFindingsJson,
    ];

    /// <summary>Whether the invoice is held, and if so the reason code and the table and row it was decided on.</summary>
    public readonly record struct Verdict(bool Held, string? ReasonCode, string? Table, Guid RowId)
    {
        /// <summary>Not held.</summary>
        public static Verdict Open => default;

        internal static Verdict Hold(string reasonCode, string table, Guid rowId) => new(true, reasonCode, table, rowId);
    }

    /// <summary>The FR-20 hold of <paramref name="invoiceId"/>, read app-only through <paramref name="entities"/>. Throws on a read fault.</summary>
    public static async Task<Verdict> CheckAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SubmissionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        var linked = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        return Of(invoiceId, linked);
    }

    /// <summary>The FR-20 hold of <paramref name="invoiceId"/> over the submissions linked to it (pure; see the remarks).</summary>
    public static Verdict Of(Guid invoiceId, IEnumerable<Entity> linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        var rows = linked.ToList();

        // (1) The column state: a hold, never a release.
        foreach (var submission in rows)
        {
            if (MatchesHeldConditions(submission))
            {
                return Verdict.Hold(HeldBySubmissionState, FinanceBaseValues.SubmissionEntity, submission.Id);
            }
        }

        // (2) The recorded outcome (field-secured): Blocked and not released by a server-written stamp. Findings that cannot be read are
        //     not decided here. On another linked submission InvoiceFirmHold and InvoiceProcedureHold hold them (fail closed) with their
        //     own codes, which approval answers after this check. On the invoice's own submission check (3) below holds first (unreadable
        //     findings cannot show they are this invoice's), so the refusal is HeldOwnSubmissionUnlinked, not the firm or Procedure code.
        foreach (var submission in rows)
        {
            if (RecordedOutcomeOf(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)) == RecordedOutcome.BlockedOpen)
            {
                return Verdict.Hold(HeldByRecordedOutcome, FinanceBaseValues.SubmissionEntity, submission.Id);
            }
        }

        // (3) The invoice's own submission is present and its recorded outcome is this invoice's (unreadable findings fail this check).
        var own = OwnSubmissionOf(invoiceId, rows);
        if (own is null || !NamesInvoice(own.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson), invoiceId))
        {
            return Verdict.Hold(HeldOwnSubmissionUnlinked, FinanceBaseValues.InvoiceEntity, invoiceId);
        }

        return Verdict.Open;
    }

    /// <summary>
    /// The invoice's own submission among <paramref name="linked"/>: the one whose immutable id derives the invoice's
    /// (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>, <see cref="IsOwnSubmission"/>). Null when none of them is (pure). Every reader
    /// that takes "the invoice's submission" selects it here, never by the link alone or by creation order (task 165a).
    /// </summary>
    public static Entity? OwnSubmissionOf(Guid invoiceId, IEnumerable<Entity> linked)
    {
        ArgumentNullException.ThrowIfNull(linked);
        return invoiceId == Guid.Empty ? null : linked.FirstOrDefault(s => IsOwnSubmission(invoiceId, s.Id));
    }

    /// <summary>
    /// THE own-submission rule (pure): <paramref name="submissionId"/> is <paramref name="invoiceId"/>'s own submission when its immutable id
    /// derives the invoice's (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>). Every reader that selects or counts a submission of an
    /// invoice by its <c>sprk_invoice</c> link applies it here (<see cref="OwnSubmissionOf"/>, task 165a; external visibility, task 165c).
    /// </summary>
    public static bool IsOwnSubmission(Guid invoiceId, Guid submissionId) =>
        invoiceId != Guid.Empty && submissionId != Guid.Empty && InvoiceSubmissionPersister.InvoiceIdFor(submissionId) == invoiceId;

    /// <summary>
    /// Reads the invoice's own submission (<see cref="OwnSubmissionOf"/>) with <paramref name="columns"/>, app-only through
    /// <paramref name="entities"/>: linked to the invoice AND the one its id derives from. Another submission linked to the invoice is
    /// never returned. Null when the own submission is not linked (callers fail closed: no write, and a decision refuses or holds).
    /// Throws on a read fault.
    /// </summary>
    public static async Task<Entity?> ReadOwnSubmissionAsync(
        IGenericEntityService entities, Guid invoiceId, IReadOnlyCollection<string> columns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(columns);
        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(columns.ToArray()),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        var linked = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        return OwnSubmissionOf(invoiceId, linked);
    }

    /// <summary>What a submission's recorded findings say about the FR-20 hold.</summary>
    internal enum RecordedOutcome
    {
        /// <summary>No findings, or an outcome that is not Blocked, or a Blocked one released by a valid Override or Return stamp.</summary>
        NotHeld,

        /// <summary><c>kind</c> = Blocked and no valid release stamp.</summary>
        BlockedOpen,

        /// <summary>The findings cannot be read (fail closed).</summary>
        Unreadable,
    }

    /// <summary>The FR-20 reading of a submission's recorded findings (pure).</summary>
    internal static RecordedOutcome RecordedOutcomeOf(string? findingsJson)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return RecordedOutcome.NotHeld;
        }

        // As InvoiceFirmHold (095c-4 review K3): the parse is lazy, so a malformed document may throw when a member is read.
        try
        {
            if (JsonNode.Parse(findingsJson) is not JsonObject root)
            {
                return RecordedOutcome.Unreadable;
            }

            if (!string.Equals(Text(root, KindMember), nameof(SubmissionEvaluationKind.Blocked), StringComparison.Ordinal))
            {
                return RecordedOutcome.NotHeld;
            }

            return IsValidStamp(SubmissionOverrideService.ReadStamp(root, SubmissionOverrideService.OverrideMember))
                || IsValidStamp(SubmissionOverrideService.ReadStamp(root, SubmissionOverrideService.ReturnMember))
                ? RecordedOutcome.NotHeld
                : RecordedOutcome.BlockedOpen;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return RecordedOutcome.Unreadable;
        }
    }

    /// <summary>True when the recorded findings name <paramref name="invoiceId"/> as the invoice their outcome created (pure; unreadable is false).</summary>
    internal static bool NamesInvoice(string? findingsJson, Guid invoiceId)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(findingsJson) is JsonObject root
                && Guid.TryParse(Text(root, InvoiceIdMember), out var named)
                && named == invoiceId;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>True when the row's columns match every one of <see cref="FinanceBaseValues.HeldSubmissionConditions"/> (pure).</summary>
    internal static bool MatchesHeldConditions(Entity submission)
    {
        foreach (var (column, values) in FinanceBaseValues.HeldSubmissionConditions)
        {
            object? value = submission.Contains(column) ? submission[column] : null;
            value = value switch
            {
                OptionSetValue option => option.Value,
                EntityReference reference => reference.Id,
                _ => value,
            };
            if (value is null || !values.Any(v => Equals(v, value)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A release stamp the Override or Return wrote: a non-empty record id and decider.</summary>
    internal static bool IsValidStamp(BlockedDecisionStamp? stamp) =>
        stamp is { } s && s.RecordId != Guid.Empty && s.BySystemUserId != Guid.Empty;

    private static string? Text(JsonObject node, string member) =>
        node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
