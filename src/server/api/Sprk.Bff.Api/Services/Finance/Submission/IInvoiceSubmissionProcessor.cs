using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Processing Pipeline steps 1-5 for one submission (ebilling-r1 task 049): read (043), the D-91 invoice-number check, resolve (046),
/// code (047), evaluate (072) and decide (073), classified into a <see cref="SubmissionEvaluationOutcome"/>. Writes nothing: the
/// Processing status, the persistence (part C) and the after-commit work (part D) belong to the job.
/// </summary>
public interface IInvoiceSubmissionProcessor
{
    /// <summary>
    /// Runs steps 1-5 on <paramref name="submission"/> (a row retrieved with <see cref="SubmissionSnapshot.Columns"/>).
    /// <paramref name="loadDocument"/> fetches the submission's document (step 1a, the job's); it is called only after the flag and
    /// the row have passed, so nothing is downloaded for a submission that fails closed.
    /// </summary>
    /// <returns>A classified outcome; every non-retryable failure is an outcome, never an exception.</returns>
    /// <exception cref="InvoiceAiException">Retryable: the read service failed (<c>invoice.read.service_failed</c>) or line coding's
    /// AI call failed. The job retries, then marks the submission Failed with <see cref="InvoiceAiException.ReasonCode"/>.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    /// <remarks>Any other exception (a Dataverse fault in a reference read, a document download fault) is transient and propagates.</remarks>
    Task<SubmissionEvaluationOutcome> EvaluateAsync(
        Entity submission,
        Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>> loadDocument,
        CancellationToken ct = default);
}
