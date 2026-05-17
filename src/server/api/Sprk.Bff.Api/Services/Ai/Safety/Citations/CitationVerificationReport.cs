namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Aggregated result of verifying all citations extracted from a single LLM response.
///
/// Results are partitioned into three buckets:
///   - <see cref="Verified"/>   — provider confirmed, <c>IsVerified = true</c>
///   - <see cref="Unverified"/> — no supporting provider, or provider returned false
///   - <see cref="Errors"/>     — provider threw an exception during verification
///
/// The union of all three lists equals the full set of extracted citations.
/// </summary>
/// <param name="Verified">
/// Citations where a provider returned <c>IsVerified = true</c>.
/// </param>
/// <param name="Unverified">
/// Citations where no provider is registered for the type, or the provider returned
/// <c>IsVerified = false</c> (citation not found / not accurate).
/// </param>
/// <param name="Errors">
/// Citations where the assigned provider threw an exception. These are returned as
/// <see cref="CitationVerificationResult.FromError"/> entries and should be treated
/// as unverified for UI purposes.
/// </param>
public sealed record CitationVerificationReport(
    IReadOnlyList<CitationVerificationResult> Verified,
    IReadOnlyList<CitationVerificationResult> Unverified,
    IReadOnlyList<CitationVerificationResult> Errors)
{
    /// <summary>All results (verified + unverified + errors) as a flat list.</summary>
    public IReadOnlyList<CitationVerificationResult> All =>
        [.. Verified, .. Unverified, .. Errors];

    /// <summary>Total number of citations that were extracted from the source text.</summary>
    public int TotalCitations => Verified.Count + Unverified.Count + Errors.Count;
}
