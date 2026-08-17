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
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

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

    /// <summary>
    /// H6-authored manifest of the 8 authoritative Spaarke managed solutions
    /// imported by Package Deployer (spec.md §11.1a + FR-09). Populated once
    /// H6 completes successfully; each record carries the solution unique
    /// name + installed version + Dataverse-assigned solutionId + dependency
    /// tier. Consumed by H7 (env-var values — task 050) for option-set / config
    /// lookups keyed by solutionId, and by operator UI for solution-level
    /// status views. Null / empty until H6 succeeds.
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (task 049 / wave C4 Batch 3D):
    /// design.md §6.2 field <c>interStepState</c> was originally enumerated
    /// with 11 keys; this field is a POML-driven addition per task 049
    /// step 5 + acceptance criterion 6 ("Cosmos interStepState contains
    /// manifest of 8 imported solutions with name + version + solutionId").
    /// Follows the enumerated-keys discipline: adding this required a
    /// deliberate type extension (not an ad-hoc dictionary insert). The
    /// key <c>importedSolutions</c> is now part of the interStepState
    /// contract that downstream handlers rely on.
    /// </remarks>
    [JsonPropertyName("importedSolutions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<ImportedSolutionRecord>? ImportedSolutions { get; set; }
}
