// -----------------------------------------------------------------------------
// BicepInfraDeployOptions.cs
//
// Bound options for the H2a handler's collaborators (runner + probes +
// inspector). Loaded from the "BicepInfraDeploy" configuration section by
// <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/> —
// runtime-configurable so the linux-x64 App Service publish layout can be
// honored without recompiling.
//
// TASK 123 (Wave G-2, Option D hybrid): ProvisioningArtifactsContainerUri +
// ArmManifestBlobName added for <see cref="ArmDeploymentRunner"/> — the SDK
// port no longer shells out to Provision-Customer.ps1 / az CLI (those two
// legacy fields + PwshExecutable/AzCliExecutable stay ONLY for
// <see cref="FileBicepTemplateInspector"/>'s on-disk template reads and any
// residual scaffold consumers; ArmDeploymentRunner, ArmKeyVaultRefProbe, and
// ArmWhatIfDriftDetector consume none of them). See task 117's
// .github/workflows/publish-provisioning-arm-artifacts.yml for the artifact
// shape this options class points at.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Bound options for <see cref="H2aBicepInfraDeployHandler"/> collaborators.
/// Configuration key: <c>BicepInfraDeploy</c>.
/// </summary>
public sealed class BicepInfraDeployOptions
{
    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="Preflight.PreflightModuleOptions.PwshExecutable"/>.
    /// Retained only for legacy/scaffold consumers — <see cref="ArmDeploymentRunner"/>
    /// (task 123) does not shell out.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Path to the <c>az</c> CLI executable. Defaults to <c>az</c> (resolved
    /// via PATH). On Linux App Service the operator install path is
    /// <c>/usr/bin/az</c>. Retained only for legacy/scaffold consumers —
    /// <see cref="ArmKeyVaultRefProbe"/> / <see cref="ArmWhatIfDriftDetector"/>
    /// (task 123) do not shell out.
    /// </summary>
    public string AzCliExecutable { get; set; } = "az";

    /// <summary>
    /// Absolute path to <c>scripts/Provision-Customer.ps1</c>. Defaults to
    /// <c>scripts/Provision-Customer.ps1</c> relative to
    /// <see cref="AppContext.BaseDirectory"/>; production deployments should
    /// override via app-setting so the linux-x64 publish layout is honored.
    /// Retained only for legacy/scaffold consumers (task 123 deleted the
    /// shell-out runner that consumed this).
    /// </summary>
    public string ProvisionCustomerScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Provision-Customer.ps1");

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
    /// <c>PostConfigure&lt;BicepInfraDeployOptions&gt;</c> at composition-root
    /// registration time (Worker/Program.cs) so a misconfigured deploy fails
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
    /// Absolute path to the <c>infrastructure/bicep/</c> tree — used by
    /// <see cref="FileBicepTemplateInspector"/> to walk template + module
    /// files. Defaults relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string BicepDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "infrastructure", "bicep");

    /// <summary>
    /// Absolute path to the runNotes directory used for upgrade-mode drift
    /// reports (spec.md FR-34: <c>runNotes/drift-{customerId}-{timestamp}.md</c>).
    /// Defaults relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string RunNotesDirectory { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "runNotes");

    /// <summary>
    /// Maximum time to wait for a single Bicep deploy invocation. Defaults
    /// to 45 minutes — H2a's Bicep deploys are documented at 10–20 min per
    /// design.md §4.2 / FR-22; the ceiling absorbs cold-provisioning of
    /// Cosmos + OpenAI + AI Search without truncating a slow-but-progressing
    /// deploy.
    /// </summary>
    public TimeSpan DeployTimeout { get; set; } = TimeSpan.FromMinutes(45);

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

    /// <summary>
    /// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27) — F5 verbatim.
    /// Policy for the OpenAI model-deployment set on a fresh subscription
    /// with mixed model-tier TPM grants.
    ///   - <c>Strict</c> (default) — the customer.bicep template deploys
    ///     the full canonical model set; H2a fails if any pin has zero
    ///     auto-granted TPM (matching pre-Wave-2 behavior; the frontier-
    ///     tier customer waits for a support ticket).
    ///   - <c>AutoRecompose</c> — H2a inspects auto-granted TPM per model
    ///     and drops zero-TPM models from the deploy set with an operator-
    ///     visible warning. Trades frontier-tier coverage for immediate
    ///     H2a success on fresh subs where only mini + embedding TPM was
    ///     auto-granted.
    /// </summary>
    public OpenAiDeploymentSetPolicy OpenAiDeploymentSetPolicy { get; set; }
        = OpenAiDeploymentSetPolicy.Strict;
}

/// <summary>
/// HANDLER-13 (Wave 2 pre-dispatch remediation 2026-08-27) — F5 verbatim.
/// Deployment-set policy for the customer OpenAI account.
/// </summary>
public enum OpenAiDeploymentSetPolicy
{
    /// <summary>
    /// Deploy the full canonical model set unconditionally. Fresh subs
    /// without frontier-tier TPM (gpt-5.4 / gpt-5-pro) fail H2a's OpenAI
    /// step and require a support ticket for TPM before re-running.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Read auto-granted TPM per model and DROP zero-TPM models from the
    /// deployment set before H2a fires. Records an operator-visible
    /// warning in the run's Cosmos notes. Trades frontier coverage for
    /// fresh-sub deploy success.
    /// </summary>
    AutoRecompose = 1,
}
