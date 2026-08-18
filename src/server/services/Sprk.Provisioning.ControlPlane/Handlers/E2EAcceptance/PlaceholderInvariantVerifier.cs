// -----------------------------------------------------------------------------
// PlaceholderInvariantVerifier.cs
//
// Wave-C4 production placeholder <see cref="IE2EInvariantVerifier"/> impl —
// parity with <see cref="PlaceholderTrapVerifier"/>. Returns InfraFault for
// every invariant so H13 classifies Resumable when invoked against a live
// customer stamp WITHOUT the full invariant-probe wiring in place.
//
// Live-Azure probe implementations for I1–I5 belong in the Phase F acceptance
// suite (task 089). This placeholder satisfies the DI contract + surfaces the
// "verifier not yet exercisable from L2 host" state cleanly.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Placeholder invariant verifier — returns <see cref="InvariantVerificationOutcome.InfraFault"/>
/// for every invariant. Handler classifies Resumable per §4C. Real live-probe
/// impl lands in Phase F (task 089).
/// </summary>
public sealed class PlaceholderInvariantVerifier : IE2EInvariantVerifier
{
    private const string DeferralDiagnostic =
        "H13 invariant live-probe not yet wired in L2 (task 055 scope stops at handler orchestration + " +
        "test fakes; Phase F task 089 covers the live customer-stamp sample probe). Handler returns Resumable — " +
        "swap DI registration to a real verifier once the probe seam lands.";

    private readonly ILogger<PlaceholderInvariantVerifier> _logger;

    public PlaceholderInvariantVerifier(ILogger<PlaceholderInvariantVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<InvariantCatalogVerificationResult> VerifyAllAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogWarning(
            "PlaceholderInvariantVerifier in use — returning InfraFault for every invariant so H13 classifies Resumable. " +
            "customerId={CustomerId} runId={RunId}. Real probe wiring is a Phase F concern (task 089).",
            request.CustomerId, request.RunId);

        var outcomes = new InvariantVerificationOutcome[]
        {
            new InvariantVerificationOutcome.InfraFault(InvariantKind.I1NoHardcodedTenant, DeferralDiagnostic),
            new InvariantVerificationOutcome.InfraFault(InvariantKind.I2AiSearchTenantFilter, DeferralDiagnostic),
            new InvariantVerificationOutcome.InfraFault(InvariantKind.I3CosmosPartitionKey, DeferralDiagnostic),
            new InvariantVerificationOutcome.InfraFault(InvariantKind.I4SpeContainerResolver, DeferralDiagnostic),
            new InvariantVerificationOutcome.InfraFault(InvariantKind.I5GraphTokenTenant, DeferralDiagnostic),
        };

        return Task.FromResult(new InvariantCatalogVerificationResult(outcomes));
    }
}
