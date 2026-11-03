namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>
/// Processing Pipeline step 2 (FR-23, D-47): checks an extracted invoice's arithmetic, resolves its timekeepers against the firm's roster
/// and its rates against the rate card in force, and combines four signals into one confidence per field. Deterministic: the same input,
/// reference data and settings give the same output.
/// </summary>
public interface IInvoiceResolutionService
{
    /// <summary>
    /// Resolves one invoice. Reference data is read once for the invoice (a bounded number of Dataverse reads, however many lines it
    /// has), and the settings come from <see cref="IEbillingSettingsReader"/>.
    /// </summary>
    /// <returns>
    /// Per fee line: the roster match (an unmatched line also yields an <see cref="ApprovedTimekeeperFindingInput"/>) and the rate in
    /// force; per field: the combined confidence. A timekeeper below the reliability threshold is never mapped to another person.
    /// </returns>
    /// <exception cref="ArgumentException">The input has no firm.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    Task<InvoiceResolutionResult> ResolveAsync(InvoiceResolutionInput input, CancellationToken ct = default);
}
