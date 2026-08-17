// -----------------------------------------------------------------------------
// InterStepState.cs
//
// Handler-authored, read-by-downstream state for ProvisioningRun.interStepState.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2 field `interStepState`:
//     "Enumerated keys: bffAppRegId, s2sAppRegId, miObjectId, miClientId, containerTypeId,
//      dataverseEnvUrl, openAiEndpoint, aiSearchEndpoint, cosmosEndpoint, systemUserId,
//      speConsentCorrelationId. Handlers write once; downstream handlers read."
//
// Modeled as a POCO (not IDictionary) because the keys are ENUMERATED — new keys
// require a design change + type extension, catching typos and unknown keys at
// compile time rather than at reconciler-runtime.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Handler-authored state that flows between phases of a single ProvisioningRun.
/// Each property is written exactly once by the owning handler and then read by
/// zero-or-more downstream handlers. Null indicates "not yet produced".
/// </summary>
/// <remarks>
/// Ordering, meaning, and enumeration of keys are LOCKED by design.md §6.2. Do
/// NOT add ad-hoc properties without amending the design first — the reconciler
/// treats this shape as contract.
/// </remarks>
public sealed class InterStepState
{
    /// <summary>Entra app registration ID for the customer's BFF API app (H3 output).</summary>
    [JsonPropertyName("bffAppRegId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BffAppRegId { get; set; }

    /// <summary>Entra app registration ID for the customer's S2S app if applicable (legacy — Model-2 dedicated only; may remain null in Model 1).</summary>
    [JsonPropertyName("s2sAppRegId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? S2SAppRegId { get; set; }

    /// <summary>UAMI object ID for the customer's App Service managed identity (H2a output).</summary>
    [JsonPropertyName("miObjectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MiObjectId { get; set; }

    /// <summary>UAMI client ID for the customer's App Service managed identity (H2a output).</summary>
    [JsonPropertyName("miClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MiClientId { get; set; }

    /// <summary>SharePoint Embedded container-type ID for the customer (H10 output).</summary>
    [JsonPropertyName("containerTypeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContainerTypeId { get; set; }

    /// <summary>Dataverse environment URL (e.g. https://spaarke-acme.crm.dynamics.com) — H5/H6 output.</summary>
    [JsonPropertyName("dataverseEnvUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataverseEnvUrl { get; set; }

    /// <summary>Azure OpenAI endpoint URI for the customer's deployment (H2a output).</summary>
    [JsonPropertyName("openAiEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OpenAiEndpoint { get; set; }

    /// <summary>Azure AI Search endpoint URI for the customer's search service (H2a output).</summary>
    [JsonPropertyName("aiSearchEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiSearchEndpoint { get; set; }

    /// <summary>Cosmos DB account endpoint URI for the customer's runtime data (H2a output).</summary>
    [JsonPropertyName("cosmosEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CosmosEndpoint { get; set; }

    /// <summary>Dataverse `systemuser` GUID for the MI-Dataverse App User (H10 output).</summary>
    [JsonPropertyName("systemUserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemUserId { get; set; }

    /// <summary>Correlation ID emitted by the SPE consent-callback (H0.5 output; used by H8/H10 to correlate the consent flow).</summary>
    [JsonPropertyName("speConsentCorrelationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpeConsentCorrelationId { get; set; }
}
