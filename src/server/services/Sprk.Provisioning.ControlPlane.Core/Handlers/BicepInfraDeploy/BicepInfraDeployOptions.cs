// -----------------------------------------------------------------------------
// BicepInfraDeployOptions.cs
//
// Bound options for the H2a handler's collaborators (runner + probes +
// inspector). Loaded from the "BicepInfraDeployOptions" configuration section by
// Worker/Program.cs and validated at Worker startup (ValidateOnStart, task 249) —
// runtime-configurable so the linux-x64 App Service publish layout can be
// honored without recompiling.
//
// TASK 123 (Wave G-2, Option D hybrid): ProvisioningArtifactsContainerUri +
// ArmManifestBlobName added for <see cref="ArmDeploymentRunner"/> — the SDK
// port no longer shells out to Provision-Customer.ps1 / az CLI (task 245b
// removed BicepDirectory with the on-disk template inspector; task 253 removed
// the pwsh / az / script-path / deploy-timeout fields nothing read). See task 117's
// .github/workflows/publish-provisioning-arm-artifacts.yml for the artifact
// shape this options class points at.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Bound options for <see cref="H2aBicepInfraDeployHandler"/> collaborators.
/// Configuration section: <c>BicepInfraDeployOptions</c>.
/// </summary>
public sealed class BicepInfraDeployOptions
{
    // Task 253 (G38): PwshExecutable, AzCliExecutable, ProvisionCustomerScriptPath and DeployTimeout were DELETED —
    // nothing read them since task 123 ported H2a to the ARM SDK, and the Worker host has no pwsh or az.

    /// <summary>
    /// Full blob-container URI (e.g.
    /// <c>https://{account}.blob.core.windows.net/provisioning-artifacts</c>)
    /// task 117's CI workflow (<c>publish-provisioning-arm-artifacts.yml</c>)
    /// publishes the CI-precompiled ARM JSON artifacts + manifest to.
    /// <see cref="ArmDeploymentRunner"/> reads <see cref="ArmManifestBlobName"/>
    /// from this container to resolve the versioned ARM-JSON blob name for the
    /// requested <c>TenancyModel</c>, then downloads that blob and deploys its
    /// content verbatim — it does NOT compile .bicep at runtime (Option D
    /// zero-shell constraint). NFR-05: <see cref="Validate"/> fails fast at
    /// boot if unset so a live deploy does not silently no-op every H2a run.
    /// </summary>
    public string ProvisioningArtifactsContainerUri { get; set; } = string.Empty;

    /// <summary>
    /// Blob name of the mutable "latest" manifest task 117's workflow
    /// publishes on every successful compile (immutable per-buildId copies
    /// also exist but are not consulted by the default resolution path).
    /// Defaults to <c>provisioning-arm-latest.json</c> per the workflow's
    /// "Generate manifest" step.
    /// </summary>
    public string ArmManifestBlobName { get; set; } = "provisioning-arm-latest.json";

    /// <summary>
    /// Validates required fields are present. Called from
    /// <c>AddOptions&lt;BicepInfraDeployOptions&gt;().Validate(...).ValidateOnStart()</c>
    /// (Worker/Program.cs, task 249) so a misconfigured deploy fails
    /// at boot (NFR-05), not on the first customer's H2a dispatch.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProvisioningArtifactsContainerUri))
        {
            throw new InvalidOperationException(
                "BicepInfraDeployOptions:ProvisioningArtifactsContainerUri is required — " +
                "ArmDeploymentRunner (task 123) resolves the CI-precompiled ARM-JSON artifact " +
                "from this blob container. Verify infrastructure/bicep/modules/controlplane-worker-app-service.bicep " +
                "wires the app-setting to the provisioning-artifacts storage account task 117's " +
                "workflow publishes to.");
        }
        if (string.IsNullOrWhiteSpace(ArmManifestBlobName))
        {
            throw new InvalidOperationException(
                "BicepInfraDeployOptions:ArmManifestBlobName is required.");
        }
    }

    /// <summary>
    /// Absolute path to the runNotes directory used for upgrade-mode drift
    /// reports (spec.md FR-34: <c>runNotes/drift-{customerId}-{timestamp}.md</c>).
    /// Defaults relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string RunNotesDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "runNotes");

    /// <summary>
    /// Maximum time to wait for the <c>az deployment group what-if</c>
    /// upgrade-mode probe. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan WhatIfTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum time to wait for an ARM read of an App Service slot's
    /// <c>keyVaultReferenceIdentity</c>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan ArmProbeTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // Task 249: the L2 principal H2a sends as customer.bicep's controlPlaneUamiPrincipalId is
    // ControlPlaneIdentityOptions.PrincipalObjectId (shared with H4) — not a field of this class.
}
