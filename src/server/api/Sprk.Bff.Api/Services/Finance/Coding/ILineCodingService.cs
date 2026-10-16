namespace Sprk.Bff.Api.Services.Finance.Coding;

/// <summary>
/// Codes the lines of one invoice with UTBMS task / activity / expense codes (ebilling-r1 task 047, FR-22): resolves the
/// invoice's code set, sends the lines to the <c>invoice-line-code</c> Action in chunks, and refuses any returned code
/// that is not in the set. Callers: the invoice pipeline's coding step (task 049) and the code-set suggestion flow (task 048).
/// </summary>
/// <remarks>
/// Failures are not swallowed: a missing code set throws <see cref="LineCodingException"/>; an AI failure surfaces unchanged as
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.InvoiceAiException"/> or
/// <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/>. A failure in any chunk fails the whole call (no partial
/// result), so the job retries the invoice as a unit.
/// </remarks>
public interface ILineCodingService
{
    /// <summary>Codes every line of <paramref name="request"/>; the result has one entry per line, in input order.</summary>
    Task<InvoiceCodingResult> CodeLinesAsync(LineCodingRequest request, CancellationToken cancellationToken = default);
}
