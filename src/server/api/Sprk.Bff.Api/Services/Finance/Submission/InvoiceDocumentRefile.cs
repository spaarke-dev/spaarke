// ebilling-r1 task 050a (D-134 item 3): the re-file of a confirmed email attachment under the invoice the submission job created.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceReviewService.RefileDocumentUnderInvoiceAsync / ReadDocumentIsolationBeforeWriteAsync ran exactly these calls
//       (RefileIsolationRead.BeforeWriteAsync, IRecordOwnershipResolver.ReparentAsync, SecureChildReconciler.AfterRefileAsync) when confirm
//       created the invoice itself (#2095, task 146). D-134 moves the invoice create into the submission job, so the calls move with it and
//       the review service no longer re-files anything.
//   (2) Extension — the persister's one transaction is the all-or-nothing invoice write (049) and cannot hold an owner reassignment, and
//       the after-steps are a sequence of idempotent steps over the stored state; this is one more such step, kept in its own type so the
//       re-file seam and its after-re-file hook are held together by RefileWriterAfterRefileGuardTests and resolved with the reconciler
//       (RefileWriterReconcilerRegistrationTests).
//   (3) Cost of doing nothing — a document confirmed onto a Secure matter's invoice stays owned and shared as before (readable by its
//       business unit until the two-minute net), and one moved out of a Secure record releases nothing (#2095).
//
// Placement (bff-extensions.md): Services/Finance/Submission, an in-process step of the ADR-004 submission job in the BFF (ADR-052). No
// endpoint, no package, no AI call. DI: FinanceModule, beside the after-steps that call it.

using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What <see cref="InvoiceDocumentRefile.RefileAsync"/> did.</summary>
public enum InvoiceDocumentRefileStatus
{
    /// <summary>The submission's document was created by intake (an upload): it was filed under the core record from the start; nothing ran.</summary>
    NotApplicable,

    /// <summary>The document was re-filed under the invoice (its owner re-derived and, when it moved, reassigned) and the after-re-file step ran.</summary>
    Refiled,

    /// <summary>Whether the document is isolated could not be read (twice): nothing was written. Transient; the redelivery retries.</summary>
    IsolationUnreadable,

    /// <summary>The ownership resolver refused the re-file (<see cref="InvoiceDocumentRefileResult.ReasonCode"/>): nothing was written.</summary>
    Refused,
}

/// <summary>The answer of <see cref="InvoiceDocumentRefile.RefileAsync"/>.</summary>
public sealed record InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus Status, string? ReasonCode = null);

/// <summary>
/// D-134 (3): when a submission points at an EXISTING document (the review-confirm route,
/// <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>), linking that document to the invoice the job created files it under
/// the invoice, so the job re-files it with the same calls confirm made before the redirect (#2095, task 146), in this order:
/// <list type="number">
/// <item><see cref="RefileIsolationRead.BeforeWriteAsync"/>: whether the document is isolated, read BEFORE its owner can move (once more after
/// a fault). Both reads faulting = <see cref="InvoiceDocumentRefileStatus.IsolationUnreadable"/>, nothing written.</item>
/// <item><see cref="IRecordOwnershipResolver.ReparentAsync"/> with the invoice as the inherited parent and no column change (the link is
/// already written by the persister's transaction): the owner is re-derived over every parent the document has, and reassigned (a separate
/// write, read back) when it differs. A refusal = <see cref="InvoiceDocumentRefileStatus.Refused"/>, nothing written; a fault propagates.</item>
/// <item><see cref="SecureChildReconciler.AfterRefileAsync"/>, unless the reparent reports
/// <see cref="RecordOwnerResolution.ParentsUnchanged"/>: the document's secure-child mirror and the pass over what is filed under it, with
/// <see cref="CancellationToken.None"/> (the re-file has landed). It never throws.</item>
/// </list>
/// Every step is idempotent: a redelivery that finds the document already owned by the resolved team writes no owner and runs the step again.
/// </summary>
public sealed class InvoiceDocumentRefile
{
    internal const string DocumentEntity = "sprk_document";
    internal const string InvoiceEntity = "sprk_invoice";

    /// <summary>Reason code of <see cref="InvoiceDocumentRefileStatus.IsolationUnreadable"/> (a Retry of the job).</summary>
    public const string IsolationUnreadableCode = "ebilling.submission.document_isolation_unreadable";

    /// <summary>Reason code of a refusal the resolver gave no code for.</summary>
    public const string RefileRefusedCode = "ebilling.submission.document_refile_refused";

    private readonly IRecordOwnershipResolver _ownership;
    private readonly SecureChildReconciler _children;
    private readonly ILogger<InvoiceDocumentRefile> _logger;

    public InvoiceDocumentRefile(IRecordOwnershipResolver ownership, SecureChildReconciler children, ILogger<InvoiceDocumentRefile> logger)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when the submission is the confirm submission of <paramref name="documentId"/> (its id is derived from the document).</summary>
    public static bool AppliesTo(Guid submissionId, Guid? documentId) =>
        documentId is { } document && document != Guid.Empty && submissionId == InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document);

    /// <summary>Re-files the submission's existing document under <paramref name="invoiceId"/>; see the type's remarks for the order.</summary>
    /// <exception cref="Exception">A Dataverse fault of the reparent (its owner assignment included) propagates: the caller retries.</exception>
    public async Task<InvoiceDocumentRefileResult> RefileAsync(Guid submissionId, Guid? documentId, Guid invoiceId, CancellationToken ct)
    {
        if (!AppliesTo(submissionId, documentId))
        {
            return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.NotApplicable);
        }

        var document = documentId!.Value;

        // 1. Isolated before? Read before anything can move the owner; a read that cannot be answered writes nothing (fail closed).
        var answer = await RefileIsolationRead.BeforeWriteAsync(_children, DocumentEntity, document, ct).ConfigureAwait(false);
        bool wasIsolated;
        try
        {
            wasIsolated = await answer().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Invoice submission {SubmissionId}: document {DocumentId} was not re-filed under invoice {InvoiceId}: whether it is held in a "
                + "Secure record could not be read (twice). Nothing was written; the job retries.", submissionId, document, invoiceId);
            return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.IsolationUnreadable, IsolationUnreadableCode);
        }

        // 2. The reparent: the invoice is the parent the document now has; its owner is re-derived over all of them and moved when it differs.
        var reparent = await _ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = DocumentEntity,
                RecordId = document,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(),
                InheritedParents = new[] { new RecordOwnershipParent(InvoiceEntity, invoiceId) },
            },
            _ => Task.CompletedTask,
            ct).ConfigureAwait(false);

        if (reparent.IsRefused)
        {
            _logger.LogWarning(
                "Invoice submission {SubmissionId}: document {DocumentId} could not be re-filed under invoice {InvoiceId} ({Code}); nothing was written",
                submissionId, document, invoiceId, reparent.RefusalCode);
            return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, reparent.RefusalCode ?? RefileRefusedCode);
        }

        // 3. The ONE step after a re-file (#2091 / #2095). CancellationToken.None: the re-file has landed. It never throws.
        if (!reparent.ParentsUnchanged)
        {
            await _children.AfterRefileAsync(DocumentEntity, document, () => Task.FromResult(wasIsolated), CancellationToken.None)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: document {DocumentId} re-filed under invoice {InvoiceId}",
            submissionId, document, invoiceId);
        return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refiled);
    }
}
