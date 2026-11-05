// ebilling-r1 task 095c-4 (notes/095c-from-email-firm-optional.md section 14; uac-r2 2026-11-03); task 095c-5 (section 15; uac-r2
// 2026-11-04): the invoice's own field-secured hold, sprk_invoice.sprk_firmhold, read app-only.
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

    /// <summary>The invoice's own <see cref="InvoiceFirmHold.InvoiceHoldColumn"/> is set, or a submission of it stores the
    /// <see cref="SubmissionReasonCodes.FirmMissing"/> hold, whatever the firm column says now: the invoice is waiting for its firm to be assigned.</summary>
    Held,
}

/// <summary>
/// The "firm needed" hold of task 095c (owner 2026-11-03; uac-r2 2026-11-03): an invoice submitted without a firm cannot be approved,
/// exported, returned to a firm, corrected or adjusted under a cited rule until the firm is assigned through the pipeline (task 095d).
/// </summary>
/// <remarks>
/// <para><b>The hold is stored, not derived from the firm column.</b> A user with Write can still set <c>sprk_vendororg</c> through the Web
/// API (the form controls are read-only, a convenience only), and that write runs none of the pipeline: no resolution, no firm codes, no rule.
/// So a firm set natively must neither clear the hold nor unlock a step.</para>
/// <para><b>Task 095c-5: the invoice's own hold.</b> <see cref="InvoiceHoldColumn"/> (<c>sprk_invoice.sprk_firmhold</c>) is set by the persister in
/// the same transaction as the held outcome, and cleared only by 095d's assign-firm through its typed writer. It is field-secured ("Spaarke
/// BFF-Managed Field Writers" read / create / update, "Spaarke BFF-Managed Field Readers" read) and refused to the generic writers
/// (<c>InheritedAccessPermission.AccessColumns</c>), so unlinking, repointing or deleting a submission cannot lift the hold. It is read
/// <b>app-only</b> here (the <see cref="IGenericEntityService"/> the callers pass), never from the invoice row a caller hands in: a read on a
/// user's token returns null for a user outside the Readers profile, which would read as "no hold" (uac-r2 2026-11-04). Any non-blank value
/// holds. A read fault, or a column that does not exist in the environment yet, throws, and every caller refuses or answers unavailable:
/// held, never "not held".</para>
/// <para>The submission side (095c-4, unchanged) is any of these on a submission linked to the invoice
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

    /// <summary>Task 095c-5: <c>sprk_invoice.sprk_firmhold</c>, the invoice's own field-secured firm hold. It stores the reason code
    /// (<see cref="InvoiceHoldValue"/>); any non-blank value holds.</summary>
    public const string InvoiceHoldColumn = "sprk_firmhold";

    /// <summary>The value the persister writes to <see cref="InvoiceHoldColumn"/> with the held outcome.</summary>
    public const string InvoiceHoldValue = SubmissionReasonCodes.FirmMissing;

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

    /// <summary>True when the invoice's own hold is set, or a submission of it stores the firm_missing hold (see the remarks). Read app-only
    /// through <paramref name="entities"/>; throws on a read fault.</summary>
    public static async Task<bool> IsHeldAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct) =>
        (await HeldInvoicesAsync(entities, [invoiceId], ct).ConfigureAwait(false)).Contains(invoiceId);

    /// <summary>The invoices of <paramref name="invoiceIds"/> held for a firm: by their own <see cref="InvoiceHoldColumn"/> (task 095c-5) or by a
    /// linked submission's stored hold, read app-only in chunks of <see cref="FinanceBaseValues.ParentChunkSize"/>. Throws on a read fault.</summary>
    public static async Task<IReadOnlySet<Guid>> HeldInvoicesAsync(IGenericEntityService entities, IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoiceIds);
        var held = new HashSet<Guid>();
        foreach (var chunk in invoiceIds.Where(i => i != Guid.Empty).Distinct().Chunk(FinanceBaseValues.ParentChunkSize))
        {
            // Task 095c-5: the invoice's own hold. It survives a submission that is unlinked, repointed or deleted.
            var invoices = new QueryExpression(FinanceBaseValues.InvoiceEntity)
            {
                ColumnSet = new ColumnSet(InvoiceHoldColumn),
                Criteria = new FilterExpression(LogicalOperator.And),
            };
            invoices.Criteria.AddCondition(FinanceBaseValues.InvoiceKeyColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var invoice in (await entities.RetrieveMultipleAsync(invoices, ct).ConfigureAwait(false)).Entities)
            {
                if (IsInvoiceHeld(invoice))
                {
                    held.Add(invoice.Id);
                }
            }

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

    /// <summary>True when an invoice row read app-only carries its own hold (<see cref="InvoiceHoldColumn"/> non-blank; pure).</summary>
    public static bool IsInvoiceHeld(Entity invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.Contains(InvoiceHoldColumn) && invoice[InvoiceHoldColumn] switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true, // a value of another shape: what it holds cannot be known (fail closed)
        };
    }

    /// <summary>True when one submission row stores the firm_missing hold (pure; see the remarks).</summary>
    public static bool IsHeld(Entity submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return IsHeld(
            submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
            submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson),
            out _);
    }

    /// <summary>
    /// True when a submission's stored reason code and findings carry the firm_missing hold (pure; see the remarks). <paramref name="unreadable"/>
    /// is true only when the answer is "held" BECAUSE the stored findings could not be read (fail closed): nothing readable names the firm. The
    /// answer is the same as <see cref="IsHeld(Entity)"/>'s, which is this method with the flag dropped.
    /// </summary>
    public static bool IsHeld(string? reasonCode, string? findingsJson, out bool unreadable)
    {
        unreadable = false;
        if (string.Equals(reasonCode, SubmissionReasonCodes.FirmMissing, StringComparison.Ordinal))
        {
            return true;
        }

        var json = findingsJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        // 095c-4 review K3: the parse is lazy, so a document with a duplicated member can parse and then throw ArgumentException (or
        // InvalidOperationException) when a member is read. Each of these means "cannot be known": held (fail closed).
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root)
            {
                unreadable = true;
                return true;
            }

            var pipeline = (root["pipelineFindings"] as JsonArray ?? []).OfType<JsonObject>()
                .Any(p => Text(p, "reasonCode") == SubmissionReasonCodes.FirmMissing);
            var lines = (root[InvoiceSubmissionPersister.LineFindingsMember] as JsonArray ?? []).OfType<JsonObject>()
                .Any(e => Text(e, "code") == InvoiceSubmissionPersister.LineFindingRulesNotChecked && Text(e, LineFindingCauseMember) == SubmissionReasonCodes.FirmMissing);
            return pipeline || lines;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            unreadable = true;
            return true; // fail closed: what the stored findings hold cannot be known
        }
    }

    private static string? Text(JsonObject node, string member) =>
        node[member] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
