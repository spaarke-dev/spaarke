// ebilling-r1 task 095d-0 (owner rule EB-D-185, 2026-11-05, originally written D-148; EB-D-190 Reject; EB-D-197 adjustments and corrections;
// uac-r2 R3 marker; notes/095d0-firm-gate.md): the ONE answer to "may a substantive decision be
// made on this invoice yet?" - a firm comes first.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceFirmHold / InvoiceFirmNeed answer "no firm" and "held for a firm" (approve, export, Return to Firm, correction and the
//       adjustment rule options read them); AssignFirmStoredDecision.Read parses the stored Set-firm decision. Nothing answers "the post-Set-firm
//       re-run (095d-2) is still pending", and the line decisions and Override have no firm check at all.
//   (2) Extension — InvoiceFirmHold's contract is "why the invoice needs a firm" (seven callers); widening it to the re-run would change what each
//       of them refuses (Return to Firm must stay open). So this composes the two readers and adds the one new fact, instead of copying either.
//   (3) Cost of doing nothing — a reviewer can accept, reduce or disallow lines, or Override a blocked submission, on an invoice whose billing
//       guidelines and payee are not yet known (owner: "there always needs to be a firm: we have to know what billing guidelines apply, who to pay").
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure derivation and an app-only read; no DI registration, no endpoint, no package).

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>Whether an invoice may take a substantive decision (<see cref="InvoiceFirmGate"/>).</summary>
public enum InvoiceFirmGateResult
{
    /// <summary>The invoice has a firm, is not held for one, and no stored Set-firm re-run is pending: the decision may proceed.</summary>
    Open,

    /// <summary>The invoice names no firm (<see cref="InvoiceFirmNeed.NoFirm"/>).</summary>
    NoFirm,

    /// <summary>The invoice is held for its firm (<see cref="InvoiceFirmNeed.Held"/>).</summary>
    HeldForFirm,

    /// <summary>The firm was set (Set firm, 095d-1) and the billing-rules re-run for it (095d-2) has not completed. Also the answer when a stored
    /// <c>assignFirm</c> member cannot be read (fail closed).</summary>
    RerunPending,
}

/// <summary>
/// EB-D-185 (owner rule; EB-D-190 adds Reject, EB-D-197 adds adjustments and corrections): while an invoice has no firm, is held for one, or its
/// Set-firm re-run has not completed, no line decision, Override, approve, Reject, adjustment or correction is made on it. Return and Set firm
/// itself are not gated by this (Return has <see cref="CheckForReturnAsync"/>).
/// </summary>
/// <remarks>
/// <para><b>Reads.</b> App-only, through the <see cref="IGenericEntityService"/> the caller passes (as <see cref="InvoiceFirmHold"/> does): the
/// firm column and the invoice's own hold, then the stored hold and findings of every submission linked to the invoice. A read fault throws and
/// every caller refuses or answers unavailable; it never reads as <see cref="InvoiceFirmGateResult.Open"/>.</para>
/// <para><b>The order.</b> No firm; then the invoice's own field-secured hold (<see cref="InvoiceFirmHold.InvoiceHoldColumn"/>): exactly
/// <see cref="InvoiceFirmHold.InvoiceRerunPendingValue"/> is <see cref="InvoiceFirmGateResult.RerunPending"/> whatever the submission link says
/// (uac-r2 R3: re-pointing or removing the submission's invoice lookup cannot release the gate), and ANY other non-blank value is
/// <see cref="InvoiceFirmGateResult.HeldForFirm"/> (an unrecognised value fails closed; only blank passes). Only then the submissions: a pending
/// re-run (EB-D-185 R2: before the hold test, since a Set-firm row still stores the firm_missing reason until 095d-2 rewrites it), then the
/// firm_missing hold. This class only reads: nothing here clears or downgrades the marker; only the completed re-run (095d-2) does.</para>
/// <para><b>Fail closed.</b> A submission's findings that cannot be parsed at all are held (<see cref="InvoiceFirmGateResult.HeldForFirm"/>);
/// an <c>assignFirm</c> member that is present but cannot be read is <see cref="InvoiceFirmGateResult.RerunPending"/>. Blank findings, or
/// findings with no <c>assignFirm</c> member (an invoice whose firm came with the intake, or a row from before Set firm), carry no re-run.</para>
/// </remarks>
public static class InvoiceFirmGate
{
    /// <summary>The gate's answer for <paramref name="invoice"/> (which must have been read with <see cref="InvoiceSubmissionPersister.InvoiceVendorOrg"/>).
    /// The order is firm column, the invoice's own hold, then the submissions (see the remarks). Throws on a read fault.</summary>
    public static async Task<InvoiceFirmGateResult> CheckAsync(IGenericEntityService entities, Entity invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoice);
        if (InvoiceFirmHold.HasNoFirm(invoice))
        {
            return InvoiceFirmGateResult.NoFirm;
        }

        switch (await InvoiceFirmHold.InvoiceHoldStateAsync(entities, invoice.Id, ct).ConfigureAwait(false))
        {
            case InvoiceFirmHold.InvoiceHoldState.RerunPending:
                return InvoiceFirmGateResult.RerunPending;
            case InvoiceFirmHold.InvoiceHoldState.Held:
                return InvoiceFirmGateResult.HeldForFirm;
        }

        var submissions = await SubmissionsAsync(entities, invoice.Id, ct).ConfigureAwait(false);
        var pending = false;
        var held = false;
        foreach (var submission in submissions)
        {
            var findings = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
            if (InvoiceFirmHold.IsHeld(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode), findings, out var unreadable))
            {
                if (unreadable)
                {
                    return InvoiceFirmGateResult.HeldForFirm; // findings that cannot be parsed at all: nothing about a re-run can be known
                }

                held = true;
            }

            pending |= IsRerunPending(findings);
        }

        return pending ? InvoiceFirmGateResult.RerunPending : held ? InvoiceFirmGateResult.HeldForFirm : InvoiceFirmGateResult.Open;
    }

    /// <summary>
    /// Whether Return to Firm may hand the invoice to its firm (EB-D-185 R1): <see cref="InvoiceFirmNeed.NoFirm"/> when the invoice names no firm;
    /// <see cref="InvoiceFirmNeed.Held"/> when its own hold is anything but blank or the rerun-pending marker, or a linked submission is held for
    /// a firm and no stored <c>assignFirm</c> member of it names the invoice's CURRENT firm; else <see cref="InvoiceFirmNeed.None"/>. So after Set firm
    /// (marker set; the submission still stores the firm_missing reason until 095d-2) Return proceeds, while a firm written natively
    /// (no <c>assignFirm</c> naming it) or a different firm than Set firm stored does not. Throws on a read fault.
    /// </summary>
    public static async Task<InvoiceFirmNeed> CheckForReturnAsync(IGenericEntityService entities, Entity invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoice);
        if (InvoiceFirmHold.HasNoFirm(invoice))
        {
            return InvoiceFirmNeed.NoFirm;
        }

        if (await InvoiceFirmHold.InvoiceHoldStateAsync(entities, invoice.Id, ct).ConfigureAwait(false) == InvoiceFirmHold.InvoiceHoldState.Held)
        {
            return InvoiceFirmNeed.Held;
        }

        var firmId = invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)!.Id;
        foreach (var submission in await SubmissionsAsync(entities, invoice.Id, ct).ConfigureAwait(false))
        {
            var findings = submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson);
            if (InvoiceFirmHold.IsHeld(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode), findings, out var unreadable)
                && (unreadable || !StoredFirmIs(findings, firmId)))
            {
                return InvoiceFirmNeed.Held;
            }
        }

        return InvoiceFirmNeed.None;
    }

    private static async Task<IReadOnlyList<Entity>> SubmissionsAsync(IGenericEntityService entities, Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoiceId);
        return (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.ToList();
    }

    /// <summary>True when the stored findings carry an <c>assignFirm</c> member (readable) whose <c>firmId</c> is <paramref name="firmId"/> (pure; false when absent or unreadable).</summary>
    private static bool StoredFirmIs(string? findingsJson, Guid firmId)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return false;
        }

        try
        {
            return JsonNode.Parse(findingsJson) is JsonObject root && AssignFirmStoredDecision.Read(root) is { } stored && stored.FirmId == firmId;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>True when one submission's stored findings carry an <c>assignFirm</c> member whose <c>rulesRerun</c> is not
    /// <see cref="InvoiceAssignFirmService.RulesRerunCompleted"/>, or carry one that cannot be read (pure; fail closed).</summary>
    public static bool IsRerunPending(string? findingsJson)
    {
        if (string.IsNullOrWhiteSpace(findingsJson))
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(findingsJson) is not JsonObject root)
            {
                return false; // not an object: InvoiceFirmHold already holds it (fail closed there)
            }

            return AssignFirmStoredDecision.Read(root) is { } stored
                && !string.Equals(stored.RulesRerun, InvoiceAssignFirmService.RulesRerunCompleted, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException or InvalidOperationException)
        {
            return true; // a stored Set-firm decision that cannot be read: what was applied, and whether its re-run finished, cannot be known
        }
    }
}
