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
//   Property names mirror the output names in <c>customer.bicep</c> (the one
//   customer-stamp template since task 225a) + <c>modules/uami.bicep</c> +
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

    /// <summary>Customer Key Vault name (ARM output <c>keyVaultName</c>) — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.KeyVaultName"/> (task 245a).</summary>
    public required string KeyVaultName { get; init; }

    /// <summary>Customer Key Vault URI (ARM output <c>keyVaultUri</c>) — written to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.KeyVaultUri"/> (task 245a).</summary>
    public required string KeyVaultUri { get; init; }

    /// <summary>
    /// Customer Service Bus fully-qualified namespace (<c>{ns}.servicebus.windows.net</c>) — the HOST of ARM
    /// output <c>serviceBusEndpoint</c>; written to
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.ServiceBusFullyQualifiedNamespace"/> (task 245a).
    /// </summary>
    public required string ServiceBusFullyQualifiedNamespace { get; init; }

    /// <summary>
    /// Customer Azure Managed Redis endpoint, <c>{host}:10000</c> (ARM output <c>redisEndpoint</c>) — written to
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.RedisEndpoint"/> and set by H4b as the BFF's
    /// <c>Redis__Endpoint</c> (task 242). Not a secret: the cache has access keys disabled.
    /// </summary>
    public required string RedisEndpoint { get; init; }

    /// <summary>
    /// Customer Azure AI Content Safety endpoint (ARM output <c>contentSafetyEndpoint</c>) — written to
    /// <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.ContentSafetyEndpoint"/> and set by H4b as the
    /// BFF's <c>AiSafety__ContentSafety__Endpoint</c> (task 246). Not a secret: the account has local auth disabled.
    /// </summary>
    public required string ContentSafetyEndpoint { get; init; }

    /// <summary>Whether SignalR was deployed this run (mirrors <see cref="BicepDeployRequest.SignalREnabled"/>; downstream handlers may read to skip SignalR-touching steps when off).</summary>
    public required bool SignalRDeployed { get; init; }
}
