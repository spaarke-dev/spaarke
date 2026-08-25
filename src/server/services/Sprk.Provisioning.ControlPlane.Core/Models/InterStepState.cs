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
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

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

    /// <summary>Dataverse `systemuser` GUID for the MI/UAMI Dataverse App User (H10 output; T2 trap subject).</summary>
    [JsonPropertyName("systemUserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemUserId { get; set; }

    /// <summary>
    /// Dataverse `systemuser` GUID for the BFF app-registration's Dataverse App
    /// User (H10 output).
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (task 053 / wave C4): design.md §6.2 names
    /// a single <c>systemUserId</c> key. H10 registers TWO App Users (BFF
    /// app-reg + UAMI); the pre-existing <c>systemUserId</c> field's doc
    /// comment already scoped it to the "MI-Dataverse App User" (the T2 trap
    /// subject), so this field is a deliberate, minimal addition for the
    /// second registration — parity with task 049's <c>ImportedSolutions</c>
    /// controlled-extension precedent (an explicit type extension, not an
    /// ad-hoc dictionary insert).
    /// </remarks>
    [JsonPropertyName("bffAppRegSystemUserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BffAppRegSystemUserId { get; set; }

    /// <summary>Correlation ID emitted by the SPE consent-callback (H0.5 output; used by H8/H10 to correlate the consent flow).</summary>
    [JsonPropertyName("speConsentCorrelationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpeConsentCorrelationId { get; set; }

    /// <summary>
    /// True when H3's Model 2 FIC creation completed with the <c>-FicOnly</c>
    /// script's exit-2 equivalent (H3 output; task 205b row A42, SF-8): the
    /// federated identity credential persisted and its (issuer, subject,
    /// audience) triple was structurally confirmed by an independent re-GET,
    /// but it could NOT be exchange-verified from L2 (L2's Worker cannot mint
    /// the BFF UAMI's assertion — GraphAppRegistrationProvisioner GOTCHA 2 /
    /// SF-4). This is the NORMAL creation-time result, and it is NEVER
    /// terminal success: H13/T4 (post-App-Service verification) MUST discharge
    /// it with a REAL token exchange, using FicExchangeOutcomeClassifier's
    /// parity semantics. Null = not applicable (Model 1 — zero FIC objects
    /// per I6 — or H3 not yet run).
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (task 205b / row A42). design.md §6.2's
    /// enumerated interStepState keys did not include a FIC-verification
    /// slot; auth-v4's §10 DELIVERED exit-code contract (0/1/2) +
    /// remediation-plan §5 item 2 ("run reports MUST distinguish
    /// persisted-verified from exchange-verified") require one. Follows the
    /// enumerated-keys discipline established by tasks 049/050/053/054: a
    /// deliberate type extension, not an ad-hoc dictionary insert. design.md
    /// §6.2 key-list refresh rides the S3 doc cascade (main session).
    /// </remarks>
    [JsonPropertyName("ficPendingPostAppServiceVerification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FicPendingPostAppServiceVerification { get; set; }

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

    /// <summary>
    /// SharePoint Embedded root container ID for the customer (H8 output).
    /// Distinct from <see cref="ContainerTypeId"/> — the container-TYPE is a
    /// template GUID; this is the actual per-customer root container/drive
    /// identifier (design.md §7.7 row 12 storage format —
    /// <c>b!...</c>-style Drive ID). Consumed by H7 (env-var values — task
    /// 050) as the value written to Dataverse env-var
    /// <c>sprk_SharePointEmbeddedContainerId</c> (design.md §10.3 row 7).
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (task 050 / wave 3E). design.md §6.2's
    /// enumerated interStepState keys did not include a slot for the SPE
    /// root container id (only <c>containerTypeId</c>, the type template).
    /// Follows the enumerated-keys discipline established by task 049's
    /// <see cref="ImportedSolutions"/> addition: a deliberate type extension,
    /// not an ad-hoc dictionary insert. H8 (task 051, wave C4 Batch 3E,
    /// parallel-authored alongside this field) is the intended writer of this
    /// slot — H8's own POML step 6 currently only documents writing the
    /// Dataverse env-var + KV secret directly, NOT this Cosmos field; H7
    /// treats a missing value here as <c>MissingUpstreamState</c> (Resumable)
    /// so the run naturally waits for H8 (or an operator patch) to populate
    /// it rather than guessing. See
    /// projects/customer-provisioning-orchestration-r1/notes/task-050-deviations.md
    /// for the cross-task coordination note.
    /// </remarks>
    [JsonPropertyName("speContainerId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpeContainerId { get; set; }

    /// <summary>
    /// H11-authored list of provisioned user identities (H11 output). One
    /// entry per user in run.Parameters.NonSecret["usersJson"], recording
    /// the Graph user object id + UPN (NativeAccount) or invited-guest
    /// object id + email (B2BGuest) + which D6 identity preset produced it.
    /// Populated on both the terminal-success write AND the B2BGuest
    /// WaitingOnGate write (so an operator can see who was invited while
    /// consent is pending). Null/empty until H11 first runs.
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (task 054 / wave C4 Batch 3F). design.md
    /// §6.2's enumerated interStepState keys did not include a user-
    /// provisioning slot. Follows the enumerated-keys discipline established
    /// by task 049's <see cref="ImportedSolutions"/> + task 050's
    /// <see cref="SpeContainerId"/> additions: a deliberate type extension,
    /// not an ad-hoc dictionary insert.
    /// </remarks>
    [JsonPropertyName("provisionedUsers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IList<ProvisionedUserRecord>? ProvisionedUsers { get; set; }
}
