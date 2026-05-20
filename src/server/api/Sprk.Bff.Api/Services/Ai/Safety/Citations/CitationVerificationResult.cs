namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// The result of verifying a single <see cref="Citation"/> against an
/// <see cref="IVerificationProvider"/>.
/// </summary>
/// <param name="Citation">The citation that was verified (or attempted).</param>
/// <param name="IsVerified">
/// <c>true</c> if the provider confirmed the citation exists and is accurate.
/// <c>false</c> for unverified, no-provider, or error outcomes.
/// </param>
/// <param name="ConfidenceScore">
/// Provider confidence in the verification, in the range [0.0, 1.0].
/// <c>0.0</c> when <paramref name="IsVerified"/> is <c>false</c> due to error or no provider.
/// </param>
/// <param name="SourceUrl">
/// Canonical URL at the verification provider where the citation can be viewed, if available.
/// </param>
/// <param name="VerifiedText">
/// An excerpt or snippet from the verified source, if the provider returned one.
/// </param>
/// <param name="VerificationProvider">
/// The <see cref="IVerificationProvider.ProviderName"/> that handled this citation,
/// or <c>"none"</c> if no registered provider supports the citation's type,
/// or <c>"error"</c> if the provider threw an exception.
/// </param>
/// <param name="LatencyMs">
/// Wall-clock time in milliseconds for the provider call. <c>0.0</c> when skipped.
/// </param>
/// <param name="ErrorMessage">
/// Human-readable error description when <paramref name="VerificationProvider"/> is <c>"error"</c>.
/// <c>null</c> on success or no-provider outcomes.
/// </param>
public sealed record CitationVerificationResult(
    Citation Citation,
    bool IsVerified,
    float ConfidenceScore,
    string? SourceUrl,
    string? VerifiedText,
    string VerificationProvider,
    double LatencyMs,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Creates an unverified result for a citation that has no registered provider.
    /// </summary>
    public static CitationVerificationResult NoProvider(Citation citation) =>
        new(
            Citation: citation,
            IsVerified: false,
            ConfidenceScore: 0f,
            SourceUrl: null,
            VerifiedText: null,
            VerificationProvider: "none",
            LatencyMs: 0.0);

    /// <summary>
    /// Creates an unverified result for a citation where the provider threw an exception.
    /// </summary>
    public static CitationVerificationResult FromError(
        Citation citation,
        string providerName,
        string errorMessage,
        double latencyMs) =>
        new(
            Citation: citation,
            IsVerified: false,
            ConfidenceScore: 0f,
            SourceUrl: null,
            VerifiedText: null,
            VerificationProvider: "error",
            LatencyMs: latencyMs,
            ErrorMessage: $"[{providerName}] {errorMessage}");
}
