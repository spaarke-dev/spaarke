// -----------------------------------------------------------------------------
// PlaceholderTrapVerifier.cs
//
// Wave-C4 production placeholder <see cref="IE2ETrapVerifier"/> impl. Returns
// InfraFault for every trap so H13 classifies Resumable when invoked against
// a live customer stamp WITHOUT the full trap-probe wiring in place.
//
// SCOPE (task 055):
//   Live-Azure probe implementations for T1–T6 belong in the Phase F
//   acceptance suite (task 089). This placeholder satisfies the DI contract +
//   surfaces the "verifier not yet exercisable from L2 host" state cleanly
//   (Resumable — operator restarts the run once the probe seams land) so the
//   H13 orchestrator + all unit test surfaces can be authored + validated
//   without a live customer stamp. Once the real probe seams land (each with
//   its own POML task), the DI registration swaps this out without touching
//   the H13 handler or its tests.
//
// DEFERRAL (per POML escalation trigger): the escalation note in task 055
// POML says "Any invariant/trap verifier requires access to a live customer
// stamp that isn't reachable from the test host — CAN be documented as
// 'verified via integration test at Phase F acceptance (task 089)' if unit
// test can't exercise". This placeholder IS that documented deferral +
// preserves the H13 handler's ability to run end-to-end (returning Resumable
// where a live probe is required) without silently claiming Ready.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Placeholder trap verifier — returns <see cref="TrapVerificationOutcome.InfraFault"/>
/// for every trap. Handler classifies Resumable per §4C. Real live-probe impl
/// lands in Phase F (task 089).
/// </summary>
public sealed class PlaceholderTrapVerifier : IE2ETrapVerifier
{
    private const string DeferralDiagnostic =
        "H13 trap live-probe not yet wired in L2 (task 055 scope stops at handler orchestration + " +
        "test fakes; Phase F task 089 covers the live customer-stamp probe). Handler returns Resumable — " +
        "swap DI registration to a real verifier once the probe seam lands.";

    private readonly ILogger<PlaceholderTrapVerifier> _logger;

    public PlaceholderTrapVerifier(ILogger<PlaceholderTrapVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<TrapCatalogVerificationResult> VerifyAllAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogWarning(
            "PlaceholderTrapVerifier in use — returning InfraFault for every trap so H13 classifies Resumable. " +
            "customerId={CustomerId} runId={RunId}. Real probe wiring is a Phase F concern (task 089).",
            request.CustomerId, request.RunId);

        var outcomes = new TrapVerificationOutcome[]
        {
            new TrapVerificationOutcome.InfraFault(TrapKind.T1KeyVaultReferenceIdentity, DeferralDiagnostic),
            new TrapVerificationOutcome.InfraFault(TrapKind.T2DataverseAppUser, DeferralDiagnostic),
            new TrapVerificationOutcome.InfraFault(TrapKind.T3GraphAppRoleParity, DeferralDiagnostic),
            new TrapVerificationOutcome.InfraFault(TrapKind.T4ExchangePolicyCount, DeferralDiagnostic),
            new TrapVerificationOutcome.InfraFault(TrapKind.T5SlotMiKvRbac, DeferralDiagnostic),
            new TrapVerificationOutcome.InfraFault(TrapKind.T6SpeConfidentialClient, DeferralDiagnostic),
        };

        return Task.FromResult(new TrapCatalogVerificationResult(outcomes));
    }
}
