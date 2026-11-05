// ebilling-r1 082c-2 (notes/082c-brief.md C11, C14; uac-r2 and ontology-r1 D-205, 2026-11-04): the Procedure holds of an invoice, read app-only.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceFirmHold reads the firm_missing hold from the invoice's submissions; FinanceBaseValues.HeldSubmissionConditions is
//       the FR-20 Blocked hold (a column filter). Neither knows the two Procedure holds 082c-2 stores.
//   (2) Extension — InvoiceFirmHold's contract is "why the invoice needs a firm" (InvoiceFirmNeed), which seven callers read; widening it to the
//       Procedure reasons would change what each of them refuses. The FR-20 filter cannot see an SPA hold (that submission is Accepted). So one
//       static reader beside it, in the same shape, keeps one definition of the Procedure hold for approval and for the review detail.
//   (3) Cost of doing nothing — an SPA invoice held for a Procedure gap is Accepted and In Review; without this read it could be approved with
//       billing rules that never ran (brief C11).
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure read helper; no DI registration, no endpoint, no package).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The Procedure holds of 082c-2: <see cref="SubmissionReasonCodes.ProcedureGap"/> (a work date with no billing terms in force) and
/// <see cref="SubmissionReasonCodes.ProceduresUnresolved"/> (the terms could not be checked). Either refuses approval
/// (<c>ebilling.approve.procedure_held</c>) until a re-check clears it or a privileged Override releases it (task 114).
/// </summary>
/// <remarks>
/// <para><b>The hold is the stored submission state, never the invoice's <c>sprk_reviewdetail</c>.</b> The review detail is a derived label for
/// readers (<see cref="InvoiceReviewDetail"/>); the hold holds on an environment where that column does not exist yet.</para>
/// <para>A submission linked to the invoice (<c>sprk_invoicesubmission.sprk_invoice</c>) holds when any of these names a Procedure hold code:</para>
/// <list type="bullet">
/// <item><c>sprk_outcomereasoncode</c> (the SPA hold's terminal reason);</item>
/// <item>a <c>pipelineFindings[]</c> entry of its stored findings (both channels; Internal stores <c>Blocked</c> as its reason);</item>
/// <item>a <c>lineFindings[]</c> <c>rules.not_checked</c> entry's cause.</item>
/// </list>
/// <para>Stored findings that cannot be parsed hold as <see cref="SubmissionReasonCodes.ProceduresUnresolved"/> (fail closed: what they hold cannot
/// be known). A read fault throws, and every caller refuses or answers unavailable: held, never "not held".</para>
/// <para><b>Clearing (recorded for 114, 095d-2 and the ebilling-r2 re-check).</b> A Procedure hold lasts as long as the stored state names it.
/// <b>Task 114's Override release marks it released</b>: the submission's outcome reason code becomes
/// <see cref="SubmissionReasonCodes.Overridden"/> (and its status Accepted) and its findings gain the <c>override</c> stamp, in one write only
/// <c>SubmissionOverrideService</c> makes; a submission in that state (<see cref="IsReleased"/>: the code AND a valid stamp, because the code column
/// alone is user-writable) holds no Procedure reason, whatever its stored findings still name (an override never rewrites a finding, FR-37).
/// A re-check that re-holds a submission must remove the stamp.
/// The release recomputes the review detail in the same transaction (<see cref="InvoiceOpenHolds"/>). A future re-check (ebilling-r2) must
/// rewrite the stored state, or mark it released, in the transaction that recomputes the review detail.</para>
/// </remarks>
public static class InvoiceProcedureHold
{
    /// <summary>The Procedure hold codes, in the review-detail priority (unresolved before the gap).</summary>
    public static IReadOnlyList<string> HoldCodes { get; } = [SubmissionReasonCodes.ProceduresUnresolved, SubmissionReasonCodes.ProcedureGap];

    private static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson,
    ];

    /// <summary>The open Procedure hold codes of <paramref name="invoiceId"/> (empty when none). Read app-only through
    /// <paramref name="entities"/>; throws on a read fault.</summary>
    public static async Task<IReadOnlySet<string>> ReasonsAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        if (invoiceId == Guid.Empty)
        {
            return reasons;
        }

        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(SubmissionColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        foreach (var submission in (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
        {
            reasons.UnionWith(ReasonsOf(submission));
        }

        return reasons;
    }

    /// <summary>The Procedure hold codes one submission row stores (pure; see the remarks).</summary>
    public static IReadOnlySet<string> ReasonsOf(Entity submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return ReasonsOf(
            submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
            submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson));
    }

    /// <summary>The Procedure hold codes of a submission's reason code and stored findings (pure; the findings' own <c>reasonCode</c> counts
    /// too). The 095d-1 Set firm apply reads the remaining reasons through this, from the findings it already holds.</summary>
    public static IReadOnlySet<string> ReasonsOf(string? reasonCode, string? json)
    {
        var reasons = new HashSet<string>(StringComparer.Ordinal);

        // Task 114: an Override released this submission's Procedure holds. Its findings still name them (they are kept as evidence and read
        // as Overridden), so the released state is the outcome reason AND the Override's stamp in the findings (IsReleased), not the findings alone.
        // A reason code with no valid stamp is not a release (F1 review round: sprk_outcomereasoncode is not field-secured, so any user with Write on
        // the submission could plant it): it falls through to the findings, which hold the submission. Findings that cannot be read are never
        // released (they hold as "could not be checked", as every other reason code).
        if (IsReleased(reasonCode, json))
        {
            return reasons;
        }

        if (reasonCode is { } reason && IsHoldCode(reason))
        {
            reasons.Add(reason);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return reasons;
        }

        // As InvoiceFirmHold (095c-4 review K3): the parse is lazy, so a malformed document may throw when a member is read. Each such
        // failure means "cannot be known": held as unresolved (fail closed).
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                reasons.Add(SubmissionReasonCodes.ProceduresUnresolved);
                return reasons;
            }

            if (Text(root, "reasonCode") is { } stored && IsHoldCode(stored))
            {
                reasons.Add(stored);
            }

            foreach (var pipeline in (root["pipelineFindings"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Text(pipeline, "reasonCode") is { } code && IsHoldCode(code))
                {
                    reasons.Add(code);
                }
            }

            foreach (var line in (root[InvoiceSubmissionPersister.LineFindingsMember] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Text(line, "code") == InvoiceSubmissionPersister.LineFindingRulesNotChecked
                    && Text(line, InvoiceFirmHold.LineFindingCauseMember) is { } cause && IsHoldCode(cause))
                {
                    reasons.Add(cause);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            reasons.Add(SubmissionReasonCodes.ProceduresUnresolved);
        }

        return reasons;
    }

    /// <summary>
    /// Task 114 (F1 review round): whether a submission's Procedure holds were released by a genuine Override. True ONLY when BOTH hold: the outcome
    /// reason code is <see cref="SubmissionReasonCodes.Overridden"/>, and the stored findings carry a valid <c>override</c> stamp
    /// (<see cref="SubmissionOverrideService.OverrideMember"/>: a non-empty GUID <c>recordId</c> and <c>bySystemUserId</c>, and a parseable <c>on</c>),
    /// which <c>SubmissionOverrideService</c> writes in the same transaction as the code. The code column is user-writable; the findings column is
    /// field-secured to the BFF app user, so the stamp is what makes the code trustworthy. Pure. The one definition of "released": the hold
    /// reader, the override service's classification and the review-detail column script (<c>Get-SubmissionHoldReasons</c>) all follow it.
    /// <b>A writer that re-holds a submission (a future re-check) must remove the <c>override</c> stamp.</b>
    /// </summary>
    public static bool IsReleased(string? reasonCode, string? json)
    {
        if (!string.Equals(reasonCode, SubmissionReasonCodes.Overridden, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(json) is JsonObject root
                && SubmissionOverrideService.ReadStamp(root, SubmissionOverrideService.OverrideMember) is { } stamp
                && stamp.RecordId != Guid.Empty
                && stamp.BySystemUserId != Guid.Empty;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool IsHoldCode(string code) =>
        code is SubmissionReasonCodes.ProcedureGap or SubmissionReasonCodes.ProceduresUnresolved;

    private static string? Text(JsonObject node, string member) =>
        node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
