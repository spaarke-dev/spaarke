// ebilling-r1 082c-2 fix round 3 (ontology F1; access-control review: fail closed, 2026-11-05): the ONE derivation of an invoice's open hold
// reasons for the review detail.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceFirmHold (the firm hold, which approval reads) and InvoiceProcedureHold (the Procedure holds, which approval reads);
//       the supersede undo united them inline (SubmissionCommitGate) and Set firm read the Procedure holds alone (the F1 defect: the label went
//       blank while the firm hold still refused approval).
//   (2) Extension — neither reader can take the other's job: InvoiceFirmHold's contract is "why the invoice needs a firm" (seven callers), and
//       widening InvoiceProcedureHold would change what each Procedure caller refuses. So one static composition beside them, which only calls
//       them, replaces the inline union and gives the writers one place to ask.
//   (3) Cost of doing nothing — the F1 defect: after Set firm a firm-held invoice reads "normal review" to the partner while approval refuses it.
//
// Placement (bff-extensions.md): BFF, Services/Finance/Submission (a pure derivation and an app-only read; no DI registration, no endpoint, no package).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The open hold reasons of an invoice, as approval reads them: the firm hold (<see cref="InvoiceFirmHold"/>) united with the Procedure holds
/// (<see cref="InvoiceProcedureHold"/>), over the invoice's own firm column and hold column and every submission linked to it. A writer of
/// <c>sprk_invoice.sprk_reviewdetail</c> hands the result to <see cref="InvoiceReviewDetail.ReviewDetailOf"/>.
/// </summary>
/// <remarks>
/// <para><b>This is the only derivation future writers may use</b> (the re-check that clears a gap, 095d-2's re-run, 114's Override release,
/// and any other writer of the review detail): ask it for the reasons that remain AFTER the write (<see cref="ReasonsOf"/> over the state the
/// write leaves, or <see cref="ReasonsAsync"/> where the write changes nothing the holds read). Never derive the detail from one hold alone, and
/// never from the invoice's own <c>sprk_reviewdetail</c>: a label that is blank while approval refuses is the defect this exists to prevent.</para>
/// <para><b>Fail closed.</b> Stored findings that cannot be read are held (as <see cref="InvoiceFirmHold"/> and <see cref="InvoiceProcedureHold"/>
/// both rule), and they are reported here as <see cref="SubmissionReasonCodes.ProceduresUnresolved"/> ("billing terms could not be checked", the
/// same as <c>Get-SubmissionHoldReasons</c> in the column script): unreadable findings do not assert the firm, but they never read as no hold, so the
/// detail is never blank on their account. A read fault propagates from <see cref="ReasonsAsync"/>: the caller refuses or holds its write.</para>
/// <para><b>Not covered:</b> the FR-20 hold (a Blocked or Processing submission, <c>FinanceBaseValues.HeldSubmissionConditions</c>). It has no
/// review-detail label, and an Internal gap that is Blocked also stores its Procedure reason, which this does read.</para>
/// </remarks>
public static class InvoiceOpenHolds
{
    /// <summary>One linked submission's stored state: its outcome reason code and its findings document.</summary>
    public readonly record struct StoredSubmission(string? ReasonCode, string? FindingsJson);

    /// <summary>
    /// The open hold reasons of an invoice with these facts (pure): <see cref="SubmissionReasonCodes.FirmMissing"/> when the invoice names no firm or
    /// its own hold is set or a submission stores the firm hold; each Procedure reason a submission stores; and
    /// <see cref="SubmissionReasonCodes.ProceduresUnresolved"/> where a submission's findings could not be read (see the remarks).
    /// </summary>
    /// <param name="invoiceHasNoFirm">The invoice's <c>sprk_vendororg</c> is empty (<see cref="InvoiceFirmHold.HasNoFirm"/>), as the write leaves it.</param>
    /// <param name="invoiceHoldSet">The invoice's own <see cref="InvoiceFirmHold.InvoiceHoldColumn"/> is set (<see cref="InvoiceFirmHold.IsInvoiceHeld"/>), as the write leaves it.</param>
    /// <param name="submissions">Every submission linked to the invoice, as the write leaves it.</param>
    public static IReadOnlySet<string> ReasonsOf(bool invoiceHasNoFirm, bool invoiceHoldSet, IEnumerable<StoredSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        if (invoiceHasNoFirm || invoiceHoldSet)
        {
            reasons.Add(SubmissionReasonCodes.FirmMissing);
        }

        foreach (var submission in submissions)
        {
            if (InvoiceFirmHold.IsHeld(submission.ReasonCode, submission.FindingsJson, out var unreadable))
            {
                // Held, but unreadable findings name nothing: "could not be checked", never the firm and never blank.
                reasons.Add(unreadable ? SubmissionReasonCodes.ProceduresUnresolved : SubmissionReasonCodes.FirmMissing);
            }

            reasons.UnionWith(InvoiceProcedureHold.ReasonsOf(submission.ReasonCode, submission.FindingsJson));
        }

        return reasons;
    }

    /// <summary>
    /// The open hold reasons of <paramref name="invoice"/> as stored now, read app-only through <paramref name="entities"/> (the columns approval
    /// reads: the invoice's firm column and own hold column, and each linked submission's reason code and findings). The invoice row supplies its
    /// id and firm column, so it must have been read with <see cref="InvoiceSubmissionPersister.InvoiceVendorOrg"/>. Throws on a read fault.
    /// </summary>
    public static async Task<IReadOnlySet<string>> ReasonsAsync(IGenericEntityService entities, Entity invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Id == Guid.Empty)
        {
            throw new ArgumentException("The invoice has no id.", nameof(invoice));
        }

        // The invoice's own hold: read app-only (never from the row a caller hands in; a user-token read returns null for a user outside the
        // Readers profile, which would read as "no hold" - InvoiceFirmHold's remarks).
        var own = new QueryExpression(FinanceBaseValues.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceFirmHold.InvoiceHoldColumn),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        own.Criteria.AddCondition(FinanceBaseValues.InvoiceKeyColumn, ConditionOperator.Equal, invoice.Id);
        var invoiceHoldSet = (await entities.RetrieveMultipleAsync(own, ct).ConfigureAwait(false)).Entities.Any(InvoiceFirmHold.IsInvoiceHeld);

        var query = new QueryExpression(FinanceBaseValues.SubmissionEntity)
        {
            ColumnSet = new ColumnSet(
                InvoiceSubmissionPersister.SubmissionInvoice, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(InvoiceSubmissionPersister.SubmissionInvoice, ConditionOperator.Equal, invoice.Id);
        var submissions = (await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities
            .Select(s => new StoredSubmission(
                s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
                s.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionFindingsJson)))
            .ToList();

        return ReasonsOf(InvoiceFirmHold.HasNoFirm(invoice), invoiceHoldSet, submissions);
    }
}
