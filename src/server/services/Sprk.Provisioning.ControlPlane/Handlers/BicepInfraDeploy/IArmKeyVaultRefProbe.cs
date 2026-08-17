// -----------------------------------------------------------------------------
// IArmKeyVaultRefProbe.cs
//
// Reads App Service ARM state to verify silent-fail trap T1
// (design.md §4B): `keyVaultReferenceIdentity` on BOTH prod + staging slots
// MUST equal the deployed UAMI resource id, otherwise every @Microsoft.KeyVault(...)
// app-setting resolves as null at runtime and the BFF fails silently on
// first Dataverse call.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist:
//     - Production: <see cref="AzCliArmKeyVaultRefProbe"/> shells out to
//       `az webapp show` / `az webapp deployment slot show` (parity with the
//       preflight probe's shell-out pattern; reuses the operator's `az`
//       auth chain).
//     - Test: per-unit-test stubs that return canned
//       <see cref="ArmKeyVaultRefProbeResult"/> instances.
//   The interface earns its keep — no NIH.
//
// NOTE ON POST-D20:
//   r3 task 061 landed ValidateDataAnnotations().ValidateOnStart() on 24
//   Tier-1 IOptions classes → BFF /health probe fails visibly on missing
//   KV-resolved config, so T1 becomes a boot-time signal in addition to
//   H2a's structural post-check. H2a's probe stays as the primary defense
//   because /health only fires post-deploy; H2a fails the deploy BEFORE
//   downstream handlers depend on the KV refs being live.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Verifies the T1 silent-fail trap post-condition — App Service
/// <c>keyVaultReferenceIdentity</c> equals the UAMI resource id on BOTH
/// production + staging slots.
/// </summary>
public interface IArmKeyVaultRefProbe
{
    /// <summary>
    /// Reads current ARM state + returns a typed result. Domain outcomes
    /// (match / mismatch) never throw; only genuine infra faults (network,
    /// auth) throw so the handler can classify per §4C.
    /// </summary>
    /// <param name="input">Probe input — subscription id, resource group, app service + slot names, expected UAMI resource id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
        ArmKeyVaultRefProbeInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to <see cref="IArmKeyVaultRefProbe.VerifyKeyVaultReferenceIdentityAsync"/>.
/// Immutable record assembled by <see cref="H2aBicepInfraDeployHandler"/> from
/// <see cref="BicepDeployOutputs"/> + <see cref="BicepDeployRequest"/>.
/// </summary>
public sealed record ArmKeyVaultRefProbeInput(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    string StagingSlotName,
    string ExpectedUserAssignedIdentityResourceId);

/// <summary>
/// Discriminated result — <see cref="Match"/> means BOTH slots carry the
/// expected UAMI resource id; <see cref="Mismatch"/> carries the observed
/// values for the operator diagnostic.
/// </summary>
public abstract record ArmKeyVaultRefProbeResult
{
    private ArmKeyVaultRefProbeResult() { }

    /// <summary>Both slots carry <c>keyVaultReferenceIdentity == ExpectedUserAssignedIdentityResourceId</c>.</summary>
    public sealed record Match : ArmKeyVaultRefProbeResult;

    /// <summary>
    /// One or both slots do NOT match. Fields capture the exact observed
    /// values (may be <c>null</c> for a slot missing the property entirely,
    /// which is the classic T1 symptom — Bicep didn't PATCH the setting so
    /// the slot inherits the default System-Assigned identity behavior).
    /// </summary>
    public sealed record Mismatch(
        string? ObservedProductionSlotIdentity,
        string? ObservedStagingSlotIdentity) : ArmKeyVaultRefProbeResult;
}
