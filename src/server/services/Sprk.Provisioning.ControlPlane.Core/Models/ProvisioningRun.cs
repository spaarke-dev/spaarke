// -----------------------------------------------------------------------------
// ProvisioningRun.cs
//
// Cosmos document POCO for the L2 control-plane's `spaarke-provisioning/runs`
// container. One document per pipeline execution (initial provision, re-provision,
// or repair) against a customer environment. Partition key = /customerId.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2
//   - projects/customer-provisioning-orchestration-r1/design.md §4C (rollback +
//     quarantine state transitions)
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-27 + FR-30
//
// SCOPE (THIS TASK — 024):
//   Types only. NO DI registration, NO Cosmos client instantiation, NO CosmosSDK
//   PackageReference. The .csproj is scaffolded by task 036; the client is wired
//   into L2 DI by task 037. This file exists in a NOT-YET-COMPILED project folder
//   so the compilation gate is intentionally deferred to task 036.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
// NOTE (2026-08-18 Phase F acceptance): Cosmos SDK defaults to Newtonsoft.Json;
// STJ [JsonPropertyName] alone is ignored on write path. Below uses fully-
// qualified `Newtonsoft.Json.JsonProperty` on `id` to avoid namespace ambiguity
// with STJ's [JsonIgnore] used elsewhere in this file.

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>
/// Represents one execution of the provisioning pipeline against a customer
/// environment. Persisted as a single Cosmos document; partitioned by
/// <see cref="CustomerId"/>.
/// </summary>
public sealed class ProvisioningRun
{
    /// <summary>
    /// Cosmos-required document identity. Value is a lowercase GUID string and
    /// MUST equal <see cref="RunId"/>. Cosmos SDK requires the JSON property to
    /// be named <c>id</c> (lowercase); the C# property name reflects the semantic
    /// role of a run identifier — see notes/task-024-provisioning-schema-deviations.md
    /// for the reconciliation of §6.2 field names vs the task POML acceptance
    /// criterion.
    /// </summary>
    [JsonPropertyName("id")]
    [Newtonsoft.Json.JsonProperty("id")]  // Newtonsoft attribute needed for Cosmos SDK write path (default serializer). Discovered Phase F 2026-08-18.
    public string RunId { get; set; } = default!;

    /// <summary>
    /// Partition key. Customer short-id (matches <c>sprk_dataverseenvironment.
    /// sprk_customerid</c>). Non-empty MUST — Cosmos partition-key predicate on
    /// every read/write depends on this (FR-30 / §4D I3).
    /// </summary>
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = default!;

    /// <summary>
    /// Foreign key (as GUID string) to <c>sprk_dataverseenvironment</c>. NOT a
    /// Cosmos relational FK — a cross-store reference resolved by the reconciler
    /// via Dataverse.
    /// </summary>
    [JsonPropertyName("environmentId")]
    public string EnvironmentId { get; set; } = default!;

    /// <summary>
    /// Tenancy model at run time (mirrors <c>sprk_dataverseenvironment.
    /// sprk_tenancymodel</c>). Values: <c>Model1Shared</c> | <c>Model2Dedicated</c>.
    /// </summary>
    [JsonPropertyName("tenancyModel")]
    public string TenancyModel { get; set; } = default!;

    /// <summary>
    /// Overall run status. See <see cref="RunStatus"/> for allowed values +
    /// §4C for the state-transition contract.
    /// </summary>
    [JsonPropertyName("status")]
    public RunStatus Status { get; set; } = RunStatus.NotStarted;

    /// <summary>
    /// Identifier of the current handler (e.g. <c>H2a</c>, <c>H12b</c>). String
    /// (not int) because v3 introduces sub-handlers (H12a/b/c, H0.5). Null after
    /// a terminal transition or before the first handler dispatch.
    /// </summary>
    [JsonPropertyName("currentPhase")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentPhase { get; set; }

    /// <summary>
    /// Immutable append-only log of completed handler executions in this run.
    /// Each entry captures a handler's idempotency key + backing Service Bus
    /// job ID so the reconciler can crash-recover mid-run (§4C I6).
    /// </summary>
    [JsonPropertyName("completedPhases")]
    public IList<CompletedPhase> CompletedPhases { get; set; } = new List<CompletedPhase>();

    /// <summary>
    /// Per-gate state map keyed by <c>gateId</c> (e.g. <c>admin-consent</c>,
    /// <c>app-user-created</c>). See <see cref="GateEntry"/> + <see cref="GateState"/>.
    /// </summary>
    [JsonPropertyName("gateStates")]
    public IDictionary<string, GateEntry> GateStates { get; set; }
        = new Dictionary<string, GateEntry>(StringComparer.Ordinal);

    /// <summary>
    /// Handler-authored inter-step state (enumerated keys, one-write / many-read
    /// contract). See <see cref="Models.InterStepState"/>.
    /// </summary>
    [JsonPropertyName("interStepState")]
    public InterStepState InterStepState { get; set; } = new InterStepState();

    /// <summary>
    /// Operator-supplied run parameters. Secrets are structurally
    /// <see cref="KeyVaultSecretRef"/>-only (no cleartext values at the type level).
    /// </summary>
    [JsonPropertyName("parameters")]
    public RunParameters Parameters { get; set; } = new RunParameters();

    /// <summary>
    /// Environment profile used for the run (<c>spaarke-hosted-model2</c>,
    /// <c>customer-owned-model2</c>, <c>spaarke-hosted-model1-trial</c>).
    /// </summary>
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = default!;

    /// <summary>
    /// Number of resume attempts across the run's lifetime. Incremented by the
    /// reconciler on I6 crash recovery.
    /// </summary>
    [JsonPropertyName("attemptCount")]
    public int AttemptCount { get; set; }

    /// <summary>
    /// Per-handler §4C <c>RetryableWithCleanup</c> auto-retry counter, keyed
    /// by HandlerId (task 107 / DS-2 §4-L1). Incremented by
    /// <see cref="Reconciler.StateReconcilerService.ApplyHandlerOutcomeAsync"/>
    /// immediately before a re-enqueue and persisted as part of the SAME
    /// <see cref="Repositories.IProvisioningRunRepository.ReplaceRunAsync"/>
    /// write that records the failure transition (no extra Cosmos round
    /// trip). The value becomes the re-enqueued
    /// <see cref="Enqueue.HandlerEnvelope.Attempt"/>, which participates in
    /// <see cref="Enqueue.ServiceBusHandlerEnqueuer.ComputeMessageId"/> so
    /// the retry's MessageId differs from the original dispatch's and
    /// survives Service Bus level-1 duplicate detection. NOT read by the
    /// normal tick-driven ready-set enqueue path — that path always
    /// dispatches with Attempt=0 so tick-duplicate-suppression byte-
    /// stability is unaffected by this dictionary's contents. Deliberately
    /// distinct from <see cref="AttemptCount"/> (a whole-run I6
    /// crash-recovery counter, not per-handler).
    /// </summary>
    [JsonPropertyName("handlerRetryAttempts")]
    public IDictionary<string, int> HandlerRetryAttempts { get; set; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// UTC timestamp at which this run document was created. Named
    /// <c>createdOn</c> in JSON per this task's acceptance criterion + the
    /// composite-index shape in <c>cosmos-provisioning.bicep</c>.
    /// </summary>
    [JsonPropertyName("createdOn")]
    public DateTimeOffset CreatedOn { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp at which the run reached a terminal state (Completed,
    /// Failed, Cancelled, Quarantined). Null while running.
    /// </summary>
    [JsonPropertyName("completedOn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedOn { get; set; }

    /// <summary>Last error message (populated on failure paths).</summary>
    [JsonPropertyName("errorDetail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// Quarantine metadata per §4C (rollback semantics). Non-null when
    /// <see cref="Status"/> == <see cref="RunStatus.Quarantined"/>.
    /// </summary>
    [JsonPropertyName("quarantine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuarantineInfo? Quarantine { get; set; }

    // TTL: REMOVED 2026-08-18 (Phase F acceptance discovery). Cosmos SDK uses its
    // own default serializer (Newtonsoft-based) which ignores System.Text.Json
    // [JsonIgnore(WhenWritingNull)] attributes and emits `"ttl": null`. Cosmos
    // server rejects that with `BadRequest — The input ttl 'null' is invalid`.
    // Container-level TTL (365d per platform-controlplane.bicep cosmos-provisioning
    // module) applies globally; per-item override was never actively used per the
    // prior property's doc comment. If per-item TTL is ever needed, either
    // (a) register a CosmosSystemTextJsonSerializer to make attributes work, or
    // (b) set TTL via ItemRequestOptions on the CreateItem call, not via POCO.
}

/// <summary>
/// Overall run lifecycle state. Serialized as the enum name verbatim.
/// State-transition contract lives in design.md §4C.
/// </summary>
/// <remarks>
/// DUAL-ATTRIBUTED (2026-08-19, task 106 / DS-5 §C4.5): the Cosmos SDK's
/// default serializer is Newtonsoft-based (see CosmosModule.cs — no custom
/// STJ <c>CosmosSerializer</c> is registered), so the STJ
/// <see cref="JsonStringEnumConverter"/> below is silently ignored on the
/// actual write path and Newtonsoft would otherwise emit the enum as an
/// INTEGER. The Newtonsoft <c>StringEnumConverter</c> attribute is the
/// load-bearing one at runtime; the STJ attribute is kept for any future
/// STJ-based consumer (defensive, matches the already-landed #20 dual-
/// attribute precedent on <see cref="ProvisioningRun.RunId"/>).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum RunStatus
{
    /// <summary>Row exists but the first handler has not yet been dispatched.</summary>
    NotStarted,

    /// <summary>A handler is currently in flight (or the reconciler owns advancement).</summary>
    Running,

    /// <summary>Progress is paused pending an external gate (e.g. admin consent).</summary>
    WaitingOnGate,

    /// <summary>All handlers completed successfully; environment usable.</summary>
    Completed,

    /// <summary>Handler failure, resumable via <c>POST /api/runs/{id}/resume</c> per §4C.</summary>
    Failed,

    /// <summary>Operator explicitly abandoned the run; may precede decommission.</summary>
    Cancelled,

    /// <summary>
    /// Handler wrote un-recoverable partial state (§4C Quarantine-required class).
    /// <see cref="ProvisioningRun.Quarantine"/> MUST be non-null. Blocks new runs
    /// against the same customer until cleared via
    /// <c>POST /api/runs/{id}/clear-quarantine</c>.
    /// </summary>
    Quarantined,
}

/// <summary>
/// Single entry in <see cref="ProvisioningRun.CompletedPhases"/>. Captures the
/// idempotency key + Service Bus job ID so the reconciler can prove a handler
/// completed exactly-once across crashes.
/// </summary>
public sealed class CompletedPhase
{
    /// <summary>Handler identifier (e.g. <c>H2a</c>, <c>H12b</c>).</summary>
    [JsonPropertyName("phase")]
    public string Phase { get; set; } = default!;

    /// <summary>UTC timestamp handler execution started.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>UTC timestamp handler execution completed.</summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>
    /// Idempotency key the handler used (from Service Bus message metadata) —
    /// enables the reconciler to detect a duplicate mid-run advance.
    /// </summary>
    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; set; } = default!;

    /// <summary>
    /// Backing Service Bus job identifier for this handler execution. Cross-
    /// references the per-handler job status store (ADR-017 — different store per §5.3).
    /// </summary>
    [JsonPropertyName("jobId")]
    public string JobId { get; set; } = default!;
}

/// <summary>
/// Quarantine metadata per design.md §4C. Populated when the run transitions
/// into <see cref="RunStatus.Quarantined"/>.
/// </summary>
public sealed class QuarantineInfo
{
    /// <summary>
    /// Fine-grained quarantine lifecycle state (nested inside overall
    /// <see cref="RunStatus.Quarantined"/>).
    /// </summary>
    [JsonPropertyName("state")]
    public QuarantineState State { get; set; } = QuarantineState.Quarantined;

    /// <summary>Human-readable reason (operator-facing) for quarantine.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = default!;

    /// <summary>
    /// Identifier of the handler whose partial external side effect caused the
    /// quarantine (e.g. <c>H2a</c> Bicep failed after 12 of 16 resources).
    /// </summary>
    [JsonPropertyName("quarantinedByHandler")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QuarantinedByHandler { get; set; }

    /// <summary>UTC timestamp of quarantine transition.</summary>
    [JsonPropertyName("quarantinedAt")]
    public DateTimeOffset QuarantinedAt { get; set; }

    /// <summary>Operator identity (oid) that cleared the quarantine, if any.</summary>
    [JsonPropertyName("clearedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClearedBy { get; set; }

    /// <summary>UTC timestamp the quarantine was cleared, if any.</summary>
    [JsonPropertyName("clearedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ClearedAt { get; set; }
}

/// <summary>
/// Fine-grained quarantine lifecycle. Separate from <see cref="RunStatus"/>
/// because a quarantine may be cleared without changing the run's terminal
/// status (operator repair path).
/// </summary>
/// <remarks>
/// DUAL-ATTRIBUTED (2026-08-19, task 106 / DS-5 §C4.5) — see the identical
/// remark on <see cref="RunStatus"/>: the Newtonsoft <c>StringEnumConverter</c>
/// attribute is load-bearing at runtime (Cosmos SDK default serializer),
/// the STJ attribute is defensive-only.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
[Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum QuarantineState
{
    /// <summary>Handler wrote un-recoverable partial state; run blocked.</summary>
    Quarantined,

    /// <summary>Operator manually resolved the external state; run may resume.</summary>
    Cleared,
}
