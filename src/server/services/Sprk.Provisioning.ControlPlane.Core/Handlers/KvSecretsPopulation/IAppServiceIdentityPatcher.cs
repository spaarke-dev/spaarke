// -----------------------------------------------------------------------------
// IAppServiceIdentityPatcher.cs
//
// PATCH abstraction over App Service `keyVaultReferenceIdentity` — sets the
// property to the UAMI resource id on BOTH production + staging slots. This is
// the T1 silent-fail trap fix per design.md §4B T1 + spec.md FR-33.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="AzCliAppServiceIdentityPatcher"/> shells out
//       to `az webapp update --set` / `az webapp deployment slot update --set`.
//     - Test: per-unit-test stubs returning canned outcomes.
//   Interface earns its keep — no NIH.
//
// T1 PATCH vs T1 VERIFY:
//   H4 does BOTH: patch THEN verify. H2a (task 044) VERIFIES only — it
//   assumes Bicep already patched, and fails deploy if not. H4 makes the
//   PATCH explicit (the interim Bicep does not set keyVaultReferenceIdentity
//   because that property was previously coupled to the System-Assigned MI
//   the Bicep no longer creates post-Phase-C uami.bicep migration). H4
//   patches unconditionally + verifies via the existing
//   <see cref="BicepInfraDeploy.IArmKeyVaultRefProbe"/> seam.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// PATCHes App Service <c>keyVaultReferenceIdentity</c> to the UAMI resource
/// id on BOTH production + staging slots. Owns the T1 silent-fail trap fix
/// (design.md §4B T1 + spec.md FR-33).
/// </summary>
public interface IAppServiceIdentityPatcher
{
    /// <summary>
    /// PATCHes the keyVaultReferenceIdentity setting on both slots. Domain
    /// outcomes (patch succeeded / patch failed on one slot) never throw
    /// unless a genuine infra fault occurs.
    /// </summary>
    /// <param name="input">The subscription + resource group + app service + slot names + UAMI resource id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AppServiceIdentityPatchResult> PatchKeyVaultReferenceIdentityAsync(
        AppServiceIdentityPatchInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to <see cref="IAppServiceIdentityPatcher.PatchKeyVaultReferenceIdentityAsync"/>.
/// </summary>
/// <param name="SubscriptionId">Customer subscription id.</param>
/// <param name="ResourceGroupName">Resource group containing the App Service.</param>
/// <param name="AppServiceName">App Service name.</param>
/// <param name="StagingSlotName">Staging slot name (typically <c>staging</c>).</param>
/// <param name="UserAssignedIdentityResourceId">The UAMI resource id to patch into keyVaultReferenceIdentity.</param>
public sealed record AppServiceIdentityPatchInput(
    string SubscriptionId,
    string ResourceGroupName,
    string AppServiceName,
    string StagingSlotName,
    string UserAssignedIdentityResourceId);

/// <summary>
/// Discriminated result — <see cref="Success"/> means both slots patched OK;
/// <see cref="Failure"/> carries which slot(s) failed and a diagnostic.
/// </summary>
public abstract record AppServiceIdentityPatchResult
{
    private AppServiceIdentityPatchResult() { }

    /// <summary>Both prod + staging slots patched successfully.</summary>
    public sealed record Success : AppServiceIdentityPatchResult;

    /// <summary>
    /// One or both PATCHes failed. <paramref name="Diagnostic"/> carries the
    /// operator-facing detail (which slot, az CLI exit code, stderr tail).
    /// </summary>
    public sealed record Failure(string Diagnostic) : AppServiceIdentityPatchResult;
}
