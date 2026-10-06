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
//
// RUN-CONTEXT CONTRACT (task 245a, G25): every property carries exactly one of
// [ProducedBy(handler)] or [NoProducer(reason)]. A handler that needs a value
// another handler produces reads it HERE — never from run.Parameters.NonSecret,
// which only intake writes. RunContextContractTests proves each required
// reader runs after the producer (Reconciler/HandlerRunInputs.cs declares the
// readers). Evidence for why: notes/run-context-dataflow-gap.md.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Handlers;
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
    /// <summary>Entra app registration (client) ID for the customer's BFF API app (H3 output).</summary>
    [JsonPropertyName("bffAppRegId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H3)]
    public string? BffAppRegId { get; set; }

    /// <summary>Entra app registration ID for the customer's S2S app if applicable (legacy — Model-2 dedicated only; may remain null in Model 1).</summary>
    [JsonPropertyName("s2sAppRegId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [NoProducer("Legacy S2S app registration, dropped by r3 task 060. H3 reads it only to refuse a run that still carries one; H4's leak scan reads it if present.")]
    public string? S2SAppRegId { get; set; }

    /// <summary>UAMI object ID for the customer's App Service managed identity (H2a output).</summary>
    [JsonPropertyName("miObjectId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? MiObjectId { get; set; }

    /// <summary>UAMI client ID for the customer's App Service managed identity (H2a output).</summary>
    [JsonPropertyName("miClientId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? MiClientId { get; set; }

    /// <summary>
    /// SharePoint Embedded container-type ID. NOT written by any handler: the container type is a
    /// pre-existing per-environment value supplied at intake (run parameter <c>containerTypeId</c>,
    /// from spaarke-constants.yaml). Readers use the intake value.
    /// </summary>
    [JsonPropertyName("containerTypeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [NoProducer("The container type is an intake value (run parameter containerTypeId); no handler writes this property. H4's leak scan reads it if present.")]
    public string? ContainerTypeId { get; set; }

    /// <summary>Dataverse environment URL (e.g. https://spaarke-acme.crm.dynamics.com) — H5 output.</summary>
    [JsonPropertyName("dataverseEnvUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H5)]
    public string? DataverseEnvUrl { get; set; }

    /// <summary>Azure OpenAI endpoint URI for the customer's deployment (H2a output).</summary>
    [JsonPropertyName("openAiEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? OpenAiEndpoint { get; set; }

    /// <summary>Azure AI Search endpoint URI for the customer's search service (H2a output).</summary>
    [JsonPropertyName("aiSearchEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? AiSearchEndpoint { get; set; }

    /// <summary>Cosmos DB account endpoint URI for the customer's runtime data (H2a output).</summary>
    [JsonPropertyName("cosmosEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? CosmosEndpoint { get; set; }

    /// <summary>
    /// Resource group the customer stack deployed into (H2a output — ARM output
    /// <c>resourceGroupName</c>, e.g. <c>rg-spaarke-acme-prod</c>).
    /// </summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25) — previously captured by H2a and dropped.</remarks>
    [JsonPropertyName("resourceGroupName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? ResourceGroupName { get; set; }

    /// <summary>The customer BFF App Service name (H2a output — ARM output <c>appServiceName</c>).</summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25).</remarks>
    [JsonPropertyName("appServiceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? AppServiceName { get; set; }

    /// <summary>The BFF App Service's staging slot name (H2a output — ARM output <c>appServiceStagingSlotName</c>).</summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25).</remarks>
    [JsonPropertyName("appServiceStagingSlotName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? AppServiceStagingSlotName { get; set; }

    /// <summary>The customer Key Vault name (H2a output — ARM output <c>keyVaultName</c>).</summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25). The CUSTOMER vault — not the Spaarke platform (L2) vault.</remarks>
    [JsonPropertyName("keyVaultName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? KeyVaultName { get; set; }

    /// <summary>The customer Key Vault URI (H2a output — ARM output <c>keyVaultUri</c>).</summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25).</remarks>
    [JsonPropertyName("keyVaultUri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? KeyVaultUri { get; set; }

    /// <summary>Resource id of the customer stamp's UAMI (H2a output — ARM output <c>userAssignedIdentityResourceId</c>).</summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25).</remarks>
    [JsonPropertyName("miResourceId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? MiResourceId { get; set; }

    /// <summary>
    /// The customer Service Bus fully-qualified namespace, <c>{namespace}.servicebus.windows.net</c>
    /// (H2a output — the host of ARM output <c>serviceBusEndpoint</c>).
    /// </summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 245a, G25).</remarks>
    [JsonPropertyName("serviceBusFullyQualifiedNamespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? ServiceBusFullyQualifiedNamespace { get; set; }

    /// <summary>
    /// The customer Azure Managed Redis endpoint, <c>{host}:10000</c> (H2a output — ARM output <c>redisEndpoint</c>).
    /// H4b sets it as the BFF's <c>Redis__Endpoint</c>; the BFF connects with the stamp UAMI (no key exists).
    /// </summary>
    /// <remarks>CONTROLLED SCHEMA EXTENSION (task 242, owner D12/D13).</remarks>
    [JsonPropertyName("redisEndpoint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H2a)]
    public string? RedisEndpoint { get; set; }

    /// <summary>Dataverse `systemuser` GUID for the MI/UAMI Dataverse App User (H10 output; T2 trap subject).</summary>
    [JsonPropertyName("systemUserId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H10)]
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
    [ProducedBy(HandlerIds.H10)]
    public string? BffAppRegSystemUserId { get; set; }

    /// <summary>Correlation ID for the SPE consent flow (design intent: H0.5 output). No handler writes it today.</summary>
    [JsonPropertyName("speConsentCorrelationId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [NoProducer("Model 2 H0.5 consent-flow correlation id (Model 2 is out of scope, D3); H0.5 does not write it. H4's leak scan reads it if present.")]
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
    [ProducedBy(HandlerIds.H3)]
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
    [ProducedBy(HandlerIds.H6)]
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
    [ProducedBy(HandlerIds.H8)]
    public string? SpeContainerId { get; set; }

    /// <summary>
    /// H8's CREATION RECORD (H8 output; read only by H8): the root container H8 created and has not finished binding,
    /// the further containers it must bind, and whether a container creation got no authoritative answer. Every H8
    /// re-entry resumes from it, so nothing is created twice and nothing is left unbound. The container type is an
    /// intake value (run parameter <c>containerTypeId</c>), not part of the record.
    /// </summary>
    /// <remarks>
    /// CONTROLLED SCHEMA EXTENSION (unified-access-control-r2 task 165, owner round 49 item 2: "the resume record is
    /// persisted as typed fields — never as JsonElement evidence"). Round 41 kept the root container in the
    /// <c>h8-t6-verified</c> gate's evidence, which the Cosmos SDK's Newtonsoft serializer wrote as
    /// <c>{"valueKind":1}</c>; every resume then created a second root container. Same enumerated-keys discipline as
    /// <see cref="ImportedSolutions"/> / <see cref="SpeContainerId"/>: a deliberate type extension, not a dictionary
    /// insert.
    /// </remarks>
    [JsonPropertyName("speContainerCreation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H8)]
    public SpeContainerCreationRecord? SpeContainerCreation { get; set; }

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
    [ProducedBy(HandlerIds.H11)]
    public IList<ProvisionedUserRecord>? ProvisionedUsers { get; set; }

    /// <summary>
    /// The stamp's own BFF base URL — the production slot H9 deployed to and health-probed
    /// (<c>https://{appServiceName}.azurewebsites.net</c>, no <c>/api</c> suffix). Task 245b: H7 writes it
    /// as <c>sprk_BffApiBaseUrl</c>, H13 probes it, H14 derives its webhook receiver URLs from it.
    /// Before T245b those read run parameters nothing wrote, and H7 fell back to the PLATFORM BFF.
    /// </summary>
    [JsonPropertyName("bffApiUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H9)]
    public string? BffApiUrl { get; set; }

    /// <summary>
    /// The BFF build H9 deployed (the CI artifact manifest's <c>buildId</c>, e.g. <c>2026.09.30-123</c>).
    /// Task 245b: H13's idempotency key (<c>validate-{customerId}-{buildId}</c>) and the registry column
    /// <c>sprk_bffversion</c>.
    /// </summary>
    [JsonPropertyName("bffBuildId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [ProducedBy(HandlerIds.H9)]
    public string? BffBuildId { get; set; }
}
