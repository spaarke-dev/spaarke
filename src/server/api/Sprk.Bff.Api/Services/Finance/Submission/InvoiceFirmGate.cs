// ebilling-r1 task 095d-0 (owner decision EB-D-148 / D-148, 2026-11-05; notes/095d0-firm-gate.md): the ONE answer to "may a substantive decision be
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
/// D-148: while an invoice has no firm, is held for one, or has a linked submission whose stored <c>assignFirm</c> member has <c>rulesRerun</c>
/// other than <see cref="InvoiceAssignFirmService.RulesRerunCompleted"/>, no line decision, Override or approve is made on it. Return, Reject and
/// Set firm itself are not gated.
/// </summary>
/// <remarks>
/// <para><b>Reads.</b> App-only, through the <see cref="IGenericEntityService"/> the caller passes (as <see cref="InvoiceFirmHold"/> does): the
/// firm column and the invoice's own hold, then the stored findings of every submission linked to the invoice. A read fault throws and every
/// caller refuses or answers unavailable; it never reads as <see cref="InvoiceFirmGateResult.Open"/>.</para>
/// <para><b>Fail closed.</b> A submission's findings that cannot be parsed at all are held by <see cref="InvoiceFirmHold"/> (so
/// <see cref="InvoiceFirmGateResult.HeldForFirm"/>); an <c>assignFirm</c> member that is present but cannot be read is
/// <see cref="InvoiceFirmGateResult.RerunPending"/>. Blank findings, or findings with no <c>assignFirm</c> member (an invoice whose firm came
/// with the intake, or a row from before Set firm), carry no re-run and are open.</para>
/// </remarks>
public static class InvoiceFirmGate
{
    /// <summary>The gate's answer for <paramref name="invoice"/> (which must have been read with <see cref="InvoiceSubmissionPersister.InvoiceVendorOrg"/>).
    /// The order is firm column, stored hold, then the re-run. Throws on a read fault.</summary>
    public static async Task<InvoiceFirmGateResult> CheckAsync(IGenericEntityService entities, Entity invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoice);
        switch (await InvoiceFirmHold.CheckAsync(entities, invoice, ct).ConfigureAwait(false))
        {
            case InvoiceFirmNeed.NoFirm:
                return InvoiceFirmGateResult.NoFirm;
            case InvoiceFirmNeed.Held:
                return InvoiceFirmGateResult.HeldForFirm;
        }

        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(InvoiceSubmissionPersister.SubmissionFindingsJson),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoice.Id);
        var submissions = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities;
        return submissions.Any(s => IsRerunPending(s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)))
            ? InvoiceFirmGateResult.RerunPending
            : InvoiceFirmGateResult.Open;
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
