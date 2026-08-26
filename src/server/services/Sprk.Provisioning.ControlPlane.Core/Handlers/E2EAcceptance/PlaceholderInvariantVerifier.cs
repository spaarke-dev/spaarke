// -----------------------------------------------------------------------------
// PlaceholderInvariantVerifier.cs
//
// RETIRED (task 170, Wave G-7, 2026-08-20): the DI registration for
// <see cref="IE2EInvariantVerifier"/> was swapped in
// <see cref="E2EAcceptanceModule"/> from THIS class to
// <see cref="PackagedScriptTenantLiteralInvariantVerifier"/> — which returns
// real Pass/Fail for I1 (packaged-scripts on-disk grep) and preserves
// InfraFault for I2-I5 pending their own sibling probe tasks
// (173 / 174 / 176 / 179).
//
// KEPT ON DISK unregistered, per the uniform Wave-G6 retirement convention
// (task 125 AzCliKvSecretsWriter, task 160 AzCliKvSecretReader, task 161
// ExchangePolicyScriptApplier all followed the same "keep on disk with a
// retirement banner" posture). Rationale: preserves the audit trail of
// what was replaced + gives operators a one-file diff if the swap ever
// needs to be reverted (delete the swap line in E2EAcceptanceModule.cs, add
// back the AddSingleton<IE2EInvariantVerifier, PlaceholderInvariantVerifier>
// line — no other change needed).
//
// ORIGINAL SCOPE (Wave-C4, task 055):
//   Placeholder impl parity with <see cref="PlaceholderTrapVerifier"/>.
//   Returned InfraFault for every invariant so H13 classified Resumable
//   when invoked against a live customer stamp WITHOUT the full invariant-
//   probe wiring in place. Live-Azure probe implementations for I1-I5 were
//   originally deferred to Phase F acceptance suite (task 089); the deferral
//   for I1 has now landed (this task) and I2-I5 are individually deferred.
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
