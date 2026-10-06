// -----------------------------------------------------------------------------
// IBicepDeployRunner.cs
//
// L2 abstraction over the per-customer infrastructure deploy H2a performs. The
// production implementation (<see cref="ArmDeploymentRunner"/>, task 123)
// deploys the CI-precompiled ARM JSON (task 117's
// publish-provisioning-arm-artifacts.yml) through Azure.ResourceManager — no
// shell, no bicep CLI in the L2 runtime. Unit tests inject stubs to avoid
// Azure round-trips.
//
// SEAM JUSTIFICATION (ADR-010): production <see cref="ArmDeploymentRunner"/> +
// the per-test stubs in <c>H2aBicepInfraDeployHandlerTests</c> that construct
// <see cref="BicepDeployOutcome"/> directly.
//
// TEMPLATE RESOLUTION (task 245b): <see cref="IBicepDeployRunner.ResolveTemplateAsync"/>
// is separate from <see cref="IBicepDeployRunner.DeployAsync"/> so H2a can derive
// its idempotency version from the template BEFORE deciding whether to deploy,
// and so every step (inspection, what-if, deploy) works on the same bytes.
//
// UPGRADE-MODE INTEGRATION:
//   H2a itself (NOT the runner) owns the upgrade-mode branch:
//     1. Call <see cref="IUpgradeDriftDetector"/> FIRST to `what-if` the
//        deploy.
//     2. If drift → REJECT + write drift report + return
//        Failure(QuarantineRequired, upgrade-mode-drift).
//     3. Otherwise call this runner to perform the actual deploy.
//   The runner is drift-agnostic — it either deploys or it doesn't.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Executes the per-customer Bicep deploy for handler H2a. Production impl
/// shells out to <c>scripts/Provision-Customer.ps1</c>; test impls return
/// canned <see cref="BicepDeployOutcome"/>s.
/// </summary>
public interface IBicepDeployRunner
{
    /// <summary>
    /// Task 245b: resolves the ARM template H2a will deploy for <paramref name="tenancyModel"/> — the
    /// CI-published artifact named by the manifest's mutable "latest" pointer — and returns its JSON
    /// together with its content version (<see cref="ArtifactVersion"/> of the downloaded bytes). H2a
    /// resolves ONCE per invocation and hands the result to the inspector, the upgrade what-if and
    /// <see cref="DeployAsync"/> on <see cref="BicepDeployRequest.Template"/>, so the bytes that are
    /// versioned, checked and deployed are the same bytes. An unreadable or corrupt artifact THROWS
    /// (infra fault — the handler classifies it).
    /// </summary>
    Task<ResolvedArmTemplate> ResolveTemplateAsync(
        Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel tenancyModel,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the deploy. Returns a typed outcome — success carries the outputs
    /// consumed by downstream handlers (UAMI resource id, endpoint URIs);
    /// failure carries a diagnostic. Domain failures do NOT throw (parity
    /// with <see cref="IPreflightQuotaProbe"/> — infra faults MAY throw).
    /// </summary>
    /// <param name="request">Deploy inputs (customerId, tenantId, subscription id, the resolved template, tenancy model, feature flags).</param>
    /// <param name="cancellationToken">Cancellation token — a long-running deploy MUST honor it.</param>
    Task<BicepDeployOutcome> DeployAsync(BicepDeployRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Bicep deploy invocation. Immutable record; the caller
/// (<see cref="H2aBicepInfraDeployHandler"/>) constructs one per run from
/// <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>
/// + <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.TenancyModel"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key (customerId standard: 3-8 lowercase letters/digits, starts with a letter).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — must be explicit, never default).</param>
/// <param name="SubscriptionId">Target subscription id (ADR-027 D4 — customer subscription, never platform).</param>
/// <param name="TenancyModel">
/// <c>Model1</c> or <c>Model2</c> per <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.TenancyModel"/>.
/// Model 2 deploys <c>customer.bicep</c>. Model 1 fails closed (ArmDeploymentRunner) until tasks 225b + 228
/// converge it onto the same dedicated stamp — task 225a retired <c>stacks/model1-shared.bicep</c> (D-12).
/// </param>
/// <param name="Template">
/// Task 245b: the ARM template this run deploys, resolved once by
/// <see cref="IBicepDeployRunner.ResolveTemplateAsync"/>. Its <see cref="ResolvedArmTemplate.Version"/>
/// is the <c>bicepVer</c> of the idempotency key <c>infra-{customerId}-{bicepVer}</c> (spec FR-04) —
/// formerly a run parameter nothing wrote.
/// </param>
/// <param name="EnvironmentName">Target environment (<c>dev</c> / <c>staging</c> / <c>prod</c>) — feeds naming per §7.1.</param>
/// <param name="Location">Azure region for all customer resources (default westus2 per <c>customer.bicep</c>).</param>
/// <param name="SignalREnabled">Feature-gate for the SignalR resource (ADR-032 Null-Object kill-switch — see §7.2 #13).</param>
/// <param name="OpenAiLocation">
/// ISH-08 (customer-provisioning-orchestration-r1 Wave 5 punchlist, 2026-08-27):
/// Optional Azure OpenAI region override. When non-empty, overrides customer.bicep's
/// <c>openAiLocation</c> parameter default (<c>westus3</c>). When empty / null, the
/// Bicep parameter default wins — bit-identical to pre-ISH-08 behavior so existing
/// callers that do not supply it are unaffected. Populated by
/// <see cref="H2aBicepInfraDeployHandler"/> from
/// <c>run.Parameters.NonSecret[H2aBicepInfraDeployHandler.OpenAiLocationParameterKey]</c>.
/// </param>
public sealed record BicepDeployRequest(
    string CustomerId,
    string TenantId,
    string SubscriptionId,
    string TenancyModel,
    ResolvedArmTemplate Template,
    string EnvironmentName,
    string Location,
    bool SignalREnabled,
    string? OpenAiLocation = null)
{
    /// <summary>The template's content version — the <c>bicepVer</c> of <c>infra-{customerId}-{bicepVer}</c>.</summary>
    public string BicepVersion => Template.Version;
}

/// <summary>
/// Task 245b: the ARM template H2a deploys — the compiled <c>customer.bicep</c> artifact CI publishes
/// (<c>publish-provisioning-arm-artifacts.yml</c>).
/// </summary>
/// <param name="TemplateKey">The manifest's template key (<c>customer</c> — the only key published since task 225a).</param>
/// <param name="ArmJsonBlobName">The immutable per-build blob the manifest pointed at.</param>
/// <param name="Json">The template JSON, exactly as downloaded.</param>
/// <param name="Version">
/// <see cref="ArtifactVersion"/> of the downloaded bytes (lowercase-hex SHA-256) — equal to the manifest's
/// <c>sha256</c> for an intact download. Same template ⇒ same version.
/// </param>
public sealed record ResolvedArmTemplate(string TemplateKey, string ArmJsonBlobName, string Json, string Version)
{
    // The template is ~200 KB — never let a record ToString() (logs, assertion messages) print it.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"TemplateKey = {TemplateKey}, ArmJsonBlobName = {ArmJsonBlobName}, Version = {Version}, Json = ({Json.Length} chars)");
        return true;
    }
}

/// <summary>
/// Discriminated result of <see cref="IBicepDeployRunner.DeployAsync"/>.
/// Success carries the deploy outputs; Failure carries a runner-side
/// diagnostic (later mapped to <see cref="BicepDeployRejectionCodes.BicepDeployFailed"/>
/// by the handler).
/// </summary>
public abstract record BicepDeployOutcome
{
    private BicepDeployOutcome() { }

    /// <summary>Deploy succeeded — outputs carry the resource IDs / endpoints the T1 probe + interStepState writes need.</summary>
    public sealed record Success(BicepDeployOutputs Outputs) : BicepDeployOutcome;

    /// <summary>
    /// Deploy failed. <paramref name="Diagnostic"/> is the operator-facing message
    /// (e.g. "az deployment sub create exit 1: Cognitive Services capacity 0/150
    /// for gpt-4o in eastus"). Handler wraps this in a
    /// <see cref="Handlers.FailureClass.QuarantineRequired"/> §4C classification —
    /// partial Bicep deploys can leave orphaned resources.
    /// </summary>
    public sealed record Failure(string Diagnostic) : BicepDeployOutcome;
}
