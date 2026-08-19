// -----------------------------------------------------------------------------
// DataverseRegistrySetupStatusUpdater.cs
//
// Wave-C4 production placeholder <see cref="IRegistrySetupStatusUpdater"/>
// impl. Real Dataverse Web API PATCH wiring against the Spaarke-internal
// registry environment lands in Wave C5 when the L2 Dataverse client seam
// lands (parity with the H0.5 handler's <see cref="NullDataverseEnvironmentRegistryClient"/>
// placeholder-then-real evolution).
//
// PLACEHOLDER SEMANTIC:
//   Returns <see cref="RegistrySetupStatusUpdateOutcome.Success"/> WITHOUT
//   issuing any Dataverse call. Logs prominently so operator-run scaffolds
//   see the "no-op registry write" state before the real impl lands. This is
//   the SAFE default: a placeholder Success lets H13 exercise its full green-
//   path unit-test surface (registry transition observed via the returned
//   outcome, NOT a live Dataverse round-trip) while a null-registered updater
//   would break the DI graph. The real Wave-C5 impl swaps via DI registration
//   change only — the H13 handler + its tests are unchanged.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls (this Wave-C4 placeholder + Wave-C5 real Dataverse Web API impl +
//   test fakes per unit test). Interface earns its keep.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Wave-C4 placeholder <see cref="IRegistrySetupStatusUpdater"/> — returns
/// Success WITHOUT issuing a real Dataverse write. Wave C5 swaps for a real
/// Dataverse Web API PATCH impl once the L2 Dataverse client wiring lands.
/// </summary>
public sealed class DataverseRegistrySetupStatusUpdater : IRegistrySetupStatusUpdater
{
    private readonly ILogger<DataverseRegistrySetupStatusUpdater> _logger;

    public DataverseRegistrySetupStatusUpdater(
        ILogger<DataverseRegistrySetupStatusUpdater> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<RegistrySetupStatusUpdateOutcome> TransitionToReadyAsync(
        RegistrySetupStatusUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogWarning(
            "DataverseRegistrySetupStatusUpdater in use (Wave-C4 placeholder) — " +
            "NOT issuing a real Dataverse PATCH. customerId={CustomerId} runId={RunId} environmentId={EnvironmentId}. " +
            "Real Wave-C5 impl wires the Dataverse Web API PATCH against sprk_dataverseenvironment.SetupStatus.",
            request.CustomerId, request.RunId, request.EnvironmentId);

        return Task.FromResult<RegistrySetupStatusUpdateOutcome>(
            new RegistrySetupStatusUpdateOutcome.Success());
    }
}
