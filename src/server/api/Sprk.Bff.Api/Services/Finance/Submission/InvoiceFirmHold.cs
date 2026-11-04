// ebilling-r1 task 095c-4 (notes/095c-from-email-firm-optional.md section 14; uac-r2 2026-11-03).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — FinanceBaseValues.HeldSubmissionConditions is the FR-20 hold (a Blocked or Processing submission); each decision service
//       (approve, reject / return, override) reads it with its own IsHeldAsync. Nothing reads the firm_missing hold that 095c-3 stores.
//   (2) Extension — the FR-20 conditions are a column filter the pending-spend anti-join also uses; firm_missing must also read the stored
//       findings (the pipeline finding and the line marker), which a FetchXML anti-join cannot. One static reader here, called by every
//       writer that needs a firm (approve, AP export, Return to Firm, correction, adjustment rule options), keeps one definition of the hold.
//   (3) Cost of doing nothing — an invoice with no firm, or whose firm was set natively while the hold is on, can be approved and exported
//       (095c-3 review K1, uac-r2 2026-11-03).
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure read helper; no DI registration, no endpoint, no package).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>Why an invoice needs a firm before a firm-dependent step (approve, export, Return to Firm, correction, a cited rule).</summary>
public enum InvoiceFirmNeed
{
    /// <summary>The invoice has a firm and no submission holds it for one.</summary>
    None,

    /// <summary>The invoice names no firm (<c>sprk_invoice.sprk_vendororg</c> is empty).</summary>
    NoFirm,

    /// <summary>A submission of the invoice stores the <see cref="SubmissionReasonCodes.FirmMissing"/> hold, whatever the firm column says now.</summary>
    Held,
}

/// <summary>
/// The "firm needed" hold of task 095c (owner 2026-11-03; uac-r2 2026-11-03): an invoice submitted without a firm cannot be approved,
/// exported, returned to a firm, corrected or adjusted under a cited rule until the firm is assigned through the pipeline (task 095d).
/// </summary>
/// <remarks>
/// <para><b>The hold is stored, not derived from the firm column.</b> A user with Write can still set <c>sprk_vendororg</c> through the Web
/// API (the form controls are read-only, a convenience only), and that write runs none of the pipeline: no resolution, no firm codes, no rule.
/// So a firm set natively must neither clear the hold nor unlock a step. The hold is any of these on a submission linked to the invoice
/// (<c>sprk_invoicesubmission.sprk_invoice</c>):</para>
/// <list type="bullet">
/// <item><c>sprk_outcomereasoncode</c> is <see cref="SubmissionReasonCodes.FirmMissing"/> (095c-3's terminal reason);</item>
/// <item>its stored findings carry the <c>firm_missing</c> pipeline finding;</item>
/// <item>a <c>lineFindings[]</c> entry is <c>rules.not_checked</c> with the cause <see cref="SubmissionReasonCodes.FirmMissing"/> (a line whose
/// rules never ran because the invoice had no firm). This one also covers an invoice with lines whose reason was rewritten.</item>
/// </list>
/// <para>Stored findings that cannot be parsed count as held (fail closed). The hold covers an invoice with no lines (095c-3 review K1:
/// I-2 alone allows it). Its writers are typed: the persister and the after-steps write it, 095d's assign-firm will clear it through the
/// pipeline; <c>sprk_outcomereasoncode</c> is refused to the app-only generic writers (<c>InheritedAccessPermission.AccessColumns</c>).</para>
/// </remarks>
public static class InvoiceFirmHold
{
    /// <summary>The member of a <c>lineFindings[]</c> entry that names why the line's rules did not run (095c-4).</summary>
    public const string LineFindingCauseMember = "cause";

    private static readonly string[] SubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson,
    ];

    /// <summary>True when the invoice row names no firm (an absent or empty <c>sprk_vendororg</c>).</summary>
    public static bool HasNoFirm(Entity invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg) is not { } firm || firm.Id == Guid.Empty;
    }

    /// <summary>Why <paramref name="invoice"/> needs a firm, or <see cref="InvoiceFirmNeed.None"/>. The firm column is checked first; then the stored hold.
    /// Throws on a read fault (every caller refuses or answers unavailable).</summary>
    public static async Task<InvoiceFirmNeed> CheckAsync(IGenericEntityService entities, Entity invoice, CancellationToken ct)
    {
        if (HasNoFirm(invoice))
        {
            return InvoiceFirmNeed.NoFirm;
        }

        return await IsHeldAsync(entities, invoice.Id, ct).ConfigureAwait(false) ? InvoiceFirmNeed.Held : InvoiceFirmNeed.None;
    }

    /// <summary>True when a submission of the invoice stores the firm_missing hold (see the remarks). Throws on a read fault.</summary>
    public static async Task<bool> IsHeldAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct) =>
        (await HeldInvoicesAsync(entities, [invoiceId], ct).ConfigureAwait(false)).Contains(invoiceId);

    /// <summary>The invoices of <paramref name="invoiceIds"/> that a submission holds for a firm, read in chunks of <see cref="FinanceBaseValues.ParentChunkSize"/>.</summary>
    public static async Task<IReadOnlySet<Guid>> HeldInvoicesAsync(IGenericEntityService entities, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        var held = new HashSet<Guid>();
        foreach (var chunk in invoiceIds.Where(i => i != Guid.Empty).Distinct().Chunk(FinanceBaseValues.ParentChunkSize))
        {
            var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
            {
                ColumnSet = new ColumnSet(SubmissionColumns),
                Criteria = new FilterExpression(LogicalOperator.And),
            };
            query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var submission in (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities)
            {
                if (submission.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id is { } invoiceId
                    && invoiceId != Guid.Empty && IsHeld(submission))
                {
                    held.Add(invoiceId);
                }
            }
        }

        return held;
    }

    /// <summary>True when one submission row stores the firm_missing hold (pure; see the remarks).</summary>
    public static bool IsHeld(Entity submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (string.Equals(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode), SubmissionReasonCodes.FirmMissing, StringComparison.Ordinal))
        {
            return true;
        }

        var json = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return true; // fail closed: what the stored findings hold cannot be known
        }

        if (root is null)
        {
            return true;
        }

        var pipeline = (root["pipelineFindings"] as JsonArray ?? []).OfType<JsonObject>()
            .Any(p => Text(p, "reasonCode") == SubmissionReasonCodes.FirmMissing);
        var lines = (root[InvoiceSubmissionPersister.LineFindingsMember] as JsonArray ?? []).OfType<JsonObject>()
            .Any(e => Text(e, "code") == InvoiceSubmissionPersister.LineFindingRulesNotChecked && Text(e, LineFindingCauseMember) == SubmissionReasonCodes.FirmMissing);
        return pipeline || lines;
    }

    private static string? Text(JsonObject node, string member) =>
        node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
