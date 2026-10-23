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

using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
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

    /// <summary>
    /// The re-file was refused (<see cref="InvoiceDocumentRefileResult.ReasonCode"/>): by the ownership resolver, or because the document is gone,
    /// linked to another invoice (<see cref="InvoiceSubmissionErrors.DocumentAlreadyInvoiced"/>, 050a review #1) or rejected as not an invoice
    /// (<c>document_rejected</c>, review round 2 item 3). Nothing was written. The invoice is committed without its document: the after-steps
    /// stop and the administrator is told (<see cref="SubmissionAfterStepsResult.DocumentNotFiled"/>).
    /// </summary>
    Refused,
}

/// <summary>The answer of <see cref="InvoiceDocumentRefile.RefileAsync"/>.</summary>
public sealed record InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus Status, string? ReasonCode = null);

/// <summary>
/// D-134 (3): when a submission points at an EXISTING document (the review-confirm route,
/// <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>), the job files that document under the invoice it created. This step is
/// the ONE writer of the document's <c>sprk_invoice</c> lookup for such a submission (050a review #1, uac-r2's design: the persister's
/// transaction does not write it), so the reparent reads the document's TRUE prior parents. In this order, each attempt:
/// <list type="number">
/// <item>The live row: its link, its review decision and its row version. Gone = <see cref="InvoiceDocumentRefileStatus.Refused"/>; linked
/// to an invoice other than <c>invoiceId</c> = Refused <see cref="InvoiceSubmissionErrors.DocumentAlreadyInvoiced"/> (never overwritten);
/// rejected as not an invoice = Refused <c>ebilling.submission.document_rejected</c> (the reject stands). Nothing written.</item>
/// <item><see cref="RefileIsolationRead.BeforeWriteAsync"/>: whether the document is isolated, read BEFORE its owner can move (once more after
/// a fault). Both reads faulting = <see cref="InvoiceDocumentRefileStatus.IsolationUnreadable"/>, nothing written.</item>
/// <item><see cref="IRecordOwnershipResolver.ReparentAsync"/> with <c>sprk_invoice</c> set to the invoice (<see cref="ReparentRequestFor"/>):
/// the resolver reads the row's current parents, re-derives the owner over them plus the invoice, refuses a move out of a Secure record
/// without F3 (the job acts for nobody, so any such move is refused), and only then writes the link, CONDITIONAL on the row version read
/// in 1. A version mismatch (a filing that landed in between) is read again and decided again, <see cref="MaxAttempts"/> in total; the
/// owner is reassigned (a separate write, read back) when it differs. A refusal = Refused, nothing written; a fault propagates.</item>
/// <item><see cref="SecureChildReconciler.AfterRefileAsync"/>, unless the reparent reports
/// <see cref="RecordOwnerResolution.ParentsUnchanged"/>: the document's secure-child mirror and the pass over what is filed under it, with
/// the reparent's answer (#2159 slice 2: a release it permits is recorded first) and <see cref="CancellationToken.None"/> (the re-file has
/// landed). It never throws.</item>
/// </list>
/// Every step is idempotent: a redelivery that finds the link already written skips the write, re-derives the owner (the invoice is also
/// passed as an inherited parent, so the owner is decided even when the link holds) and runs the step after the re-file again.
/// </summary>
public sealed class InvoiceDocumentRefile
{
    internal const string DocumentEntity = "sprk_document";
    internal const string InvoiceEntity = "sprk_invoice";

    /// <summary>Reason code of <see cref="InvoiceDocumentRefileStatus.IsolationUnreadable"/> (a Retry of the job).</summary>
    public const string IsolationUnreadableCode = "ebilling.submission.document_isolation_unreadable";

    /// <summary>Reason code of a refusal the resolver gave no code for.</summary>
    public const string RefileRefusedCode = "ebilling.submission.document_refile_refused";

    /// <summary>Attempts, in total, of the conditional link write when the document keeps changing under it.</summary>
    internal const int MaxAttempts = 3;

    private static readonly string[] DocumentColumns =
        [InvoiceReviewService.DocInvoiceLinkValue, InvoiceReviewService.DocInvoiceReviewStatus, InvoiceReviewService.VersionNumber];

    private readonly IRecordOwnershipResolver _ownership;
    private readonly SecureChildReconciler _children;
    private readonly IFieldMappingDataverseService _records;
    private readonly ILogger<InvoiceDocumentRefile> _logger;

    public InvoiceDocumentRefile(
        IRecordOwnershipResolver ownership, SecureChildReconciler children, IFieldMappingDataverseService records, ILogger<InvoiceDocumentRefile> logger)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when the submission is the confirm submission of <paramref name="documentId"/> (its id is derived from the document).</summary>
    public static bool AppliesTo(Guid submissionId, Guid? documentId) =>
        documentId is { } document && document != Guid.Empty && submissionId == InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(document);

    /// <summary>
    /// The reparent this step sends: <c>sprk_invoice</c> set to the invoice (the change the resolver weighs against the row's current
    /// parents), the invoice as an inherited parent too (so a redelivery that finds the link already written still re-derives the owner),
    /// and no caller for a move out of a Secure record (the job acts for nobody: such a move is refused).
    /// </summary>
    internal static RecordReparent ReparentRequestFor(Guid documentId, Guid invoiceId) => new()
    {
        EntityLogicalName = DocumentEntity,
        RecordId = documentId,
        ParentChanges = new Dictionary<string, EntityReference?>
        {
            [InvoiceSubmissionPersister.DocumentInvoice] = new EntityReference(InvoiceEntity, invoiceId),
        },
        InheritedParents = new[] { new RecordOwnershipParent(InvoiceEntity, invoiceId) },
    };

    /// <summary>Re-files the submission's existing document under <paramref name="invoiceId"/>; see the type's remarks for the order.</summary>
    /// <exception cref="Exception">A Dataverse fault of a read, the link write or the reparent (its owner assignment included) propagates, as does
    /// a document that changed on every attempt (<see cref="DBConcurrencyException"/>): the caller retries.</exception>
    public async Task<InvoiceDocumentRefileResult> RefileAsync(Guid submissionId, Guid? documentId, Guid invoiceId, CancellationToken ct)
    {
        if (!AppliesTo(submissionId, documentId))
        {
            return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.NotApplicable);
        }

        var document = documentId!.Value;
        for (var attempt = 1; ; attempt++)
        {
            // 1. The live row: never overwrite another invoice's link (050a review #1).
            var row = await _records.RetrieveRecordFieldsAsync(DocumentEntity, document, DocumentColumns, ct).ConfigureAwait(false);
            if (row.Count == 0)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: document {DocumentId} no longer exists; it was not re-filed under invoice {InvoiceId}",
                    submissionId, document, invoiceId);
                return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentNotFound);
            }

            var linked = ReadGuid(row, InvoiceReviewService.DocInvoiceLinkValue);
            if (linked is { } other && other != invoiceId)
            {
                _logger.LogWarning(
                    "Invoice submission {SubmissionId}: document {DocumentId} is linked to invoice {LinkedId}, not {InvoiceId}; it was not re-filed",
                    submissionId, document, other, invoiceId);
                return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionErrors.DocumentAlreadyInvoiced);
            }

            // 050a review round 2, item 3: a document rejected as not an invoice is never filed under one (a stalled reject that landed after
            // the persister's check): the reject stands, and confirm's 409 ("will not create an invoice") is never contradicted by a link.
            if (ReadInt(row, InvoiceReviewService.DocInvoiceReviewStatus) == InvoiceReviewService.ReviewStatusRejectedNotInvoice)
            {
                _logger.LogWarning(
                    "Invoice submission {SubmissionId}: document {DocumentId} is rejected as not an invoice; it was not re-filed under invoice {InvoiceId}",
                    submissionId, document, invoiceId);
                return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, InvoiceSubmissionPersister.DocumentRejectedCode);
            }

            var version = ReadVersion(row);

            // 2. Isolated before? Read before anything can move the owner; a read that cannot be answered writes nothing (fail closed).
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

            // 3. The reparent: the resolver weighs the link against the row's CURRENT parents, then this step writes it, conditionally.
            RecordOwnerResolution reparent;
            try
            {
                reparent = await _ownership.ReparentAsync(
                    ReparentRequestFor(document, invoiceId),
                    token => linked == invoiceId
                        ? Task.CompletedTask // a redelivery: the link already holds
                        : _records.UpdateRecordFieldsIfUnchangedAsync(DocumentEntity, document, new Dictionary<string, object?>
                        {
                            [InvoiceReviewService.DocInvoiceLinkBind] = $"/sprk_invoices({invoiceId:D})",
                        }, version, token),
                    ct).ConfigureAwait(false);
            }
            catch (DBConcurrencyException changed) when (attempt < MaxAttempts)
            {
                _logger.LogInformation(changed,
                    "Invoice submission {SubmissionId}: document {DocumentId} changed before its link was written (attempt {Attempt} of {Max}); "
                    + "reading it again", submissionId, document, attempt, MaxAttempts);
                continue;
            }

            if (reparent.IsRefused)
            {
                _logger.LogWarning(
                    "Invoice submission {SubmissionId}: document {DocumentId} could not be re-filed under invoice {InvoiceId} ({Code}); nothing was written",
                    submissionId, document, invoiceId, reparent.RefusalCode);
                return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refused, reparent.RefusalCode ?? RefileRefusedCode);
            }

            // 4. The ONE step after a re-file (#2091 / #2095). CancellationToken.None: the re-file has landed. It never throws. With the
            // reparent's answer (#2159 slice 2), as every re-file writer passes it: a release the answer permits is recorded first.
            if (!reparent.ParentsUnchanged)
            {
                await _children.AfterRefileAsync(DocumentEntity, document, () => Task.FromResult(wasIsolated), reparent, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("Invoice submission {SubmissionId}: document {DocumentId} re-filed under invoice {InvoiceId}",
                submissionId, document, invoiceId);
            return new InvoiceDocumentRefileResult(InvoiceDocumentRefileStatus.Refiled);
        }
    }

    private static int? ReadInt(Dictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var raw) && raw is not null
        && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static Guid? ReadGuid(Dictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var raw) && raw is not null && Guid.TryParse(raw.ToString(), out var value) && value != Guid.Empty
            ? value
            : null;

    /// <summary>Every row has a version; without one the link cannot be written conditionally, so nothing is written (the job retries).</summary>
    private static long ReadVersion(Dictionary<string, object?> row) =>
        row.TryGetValue(InvoiceReviewService.VersionNumber, out var raw) && raw is not null
        && long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : throw new InvalidOperationException("The document's versionnumber could not be read, so its invoice link cannot be written safely.");
}
