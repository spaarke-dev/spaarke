// -----------------------------------------------------------------------------
// BicepDeployOutputs.cs
//
// Typed projection of the Bicep deploy outputs H2a needs downstream:
//   1. UAMI resource ID + prod/staging App Service slot names → T1 probe input
//      (spec.md FR-33 acceptance).
//   2. Endpoint URIs (OpenAI, AI Search, Cosmos) + UAMI object/client ids →
//      written to ProvisioningRun.interStepState for downstream handlers
//      (H2b, H3, H4, H5, H12c per design.md §6.2).
//
// PARITY WITH BICEP OUTPUTS:
//   Property names mirror the output names in <c>customer.bicep</c> +
//   <c>stacks/model1-shared.bicep</c> + <c>modules/uami.bicep</c> +
//   <c>modules/openai.bicep</c> so the runner (shell-out to Provision-Customer.ps1
//   or SDK-based) can map by string key without a translation layer. Required
//   outputs (throw on missing) vs optional outputs (nullable) reflect the
//   deployment's minimum-viable output set per §7.2.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Deploy outputs H2a needs to (a) verify the T1 trap post-condition and (b)
/// populate <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState"/>
/// for downstream handlers. All string properties are REQUIRED — a null/blank
/// value on any field returns
/// <see cref="BicepDeployRejectionCodes.BicepDeployOutputsIncomplete"/>.
/// </summary>
public sealed class BicepDeployOutputs
{
    /// <summary>Resource group the customer stack deployed into (e.g. <c>rg-spaarke-acme-prod</c>).</summary>
    public required string ResourceGroupName { get; init; }

    /// <summary>UAMI resource id — the EXPECTED value for App Service <c>keyVaultReferenceIdentity</c> on both slots (T1 verification).</summary>
    public required string UserAssignedIdentityResourceId { get; init; }

    /// <summary>UAMI object id (principal id) — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.MiObjectId"/>.</summary>
    public required string UserAssignedIdentityObjectId { get; init; }

    /// <summary>UAMI client id (app id) — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.MiClientId"/>.</summary>
    public required string UserAssignedIdentityClientId { get; init; }

    /// <summary>App Service name of the customer's BFF (production slot). Used by <see cref="IArmKeyVaultRefProbe"/>.</summary>
    public required string AppServiceName { get; init; }

    /// <summary>Name of the staging deployment slot on the App Service — MUST also carry <c>keyVaultReferenceIdentity == UAMI</c> per T5 (structurally fixed by UAMI adoption).</summary>
    public required string AppServiceStagingSlotName { get; init; }

    /// <summary>Azure OpenAI endpoint URI — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.OpenAiEndpoint"/>.</summary>
    public required string OpenAiEndpoint { get; init; }

    /// <summary>Azure AI Search endpoint URI — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.AiSearchEndpoint"/>. Handler H2b consumes as its deploy target.</summary>
    public required string AiSearchEndpoint { get; init; }

    /// <summary>Cosmos DB account endpoint URI — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.CosmosEndpoint"/>. Prerequisite for BFF boot (R11).</summary>
    public required string CosmosEndpoint { get; init; }

    /// <summary>Whether SignalR was deployed this run (mirrors <see cref="BicepDeployRequest.SignalREnabled"/>; downstream handlers may read to skip SignalR-touching steps when off).</summary>
    public required bool SignalRDeployed { get; init; }
}
