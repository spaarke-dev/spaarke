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
    /// Tenancy model at run time (mirrors <c>sprk_dataverseenvironment.sprk_tenancymodel</c>).
    /// Stays a <c>string</c> field for Cosmos on-disk stability (task 223 D-12 preservation
    /// constraint — the enum <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel"/>
    /// is a computed helper for handler branching, NOT a serialization type; introducing a
    /// <c>[JsonConverter]</c> would risk breaking existing rows). Post-T224 (INCOMING §5 Item 3
    /// rename) values MUST round-trip to <c>"Model1"</c> or <c>"Model2"</c> because H12c's
    /// idempotency-key format embeds this string verbatim (the pre-T224 labels
    /// <c>"Model1Shared"</c> / <c>"Model2Dedicated"</c> were retired). Handlers parse at entry
    /// via <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModelParser.TryParse"/>.
    /// </summary>
    [JsonPropertyName("tenancyModel")]
    public string TenancyModel { get; set; } = default!;

    // Task 223 (D-12) code-review S1: `TryGetParsedTenancyModel()` helper method was removed —
    // every handler that needs a parsed enum already calls
    // Sprk.Provisioning.ControlPlane.Core.Models.TenancyModelParser.TryParse(run.TenancyModel, out var m)
    // directly + emits a handler-specific rejection code on failure. A helper method with
    // zero callers is YAGNI surface; delete-until-needed. If a future refactor genuinely
    // benefits from a `run.TryGetParsedTenancyModel()` shorthand, reintroduce it at that time.

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
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftVerbatimKeysDictionaryConverter<GateEntry>))] // gate ids persist verbatim (task 165)
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
    /// Environment profile used for the run: <c>spaarke-hosted-model2</c> (pairs with
    /// tenancyModel <c>Model1</c>) or <c>customer-owned-model2</c> (pairs with <c>Model2</c>) —
    /// the pairing POST /api/runs enforces (task 225b, D-12).
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
    /// <remarks>
    /// Keys persist VERBATIM (unified-access-control-r2 task 165): the Cosmos SDK's camelCase option lowered them ("H9" ->
    /// "h9"), so the next lookup missed and every retry re-used attempt 1 — the same MessageId, dropped by Service Bus
    /// duplicate detection. Case-insensitive so a run persisted before the fix ("h9") still counts.
    /// </remarks>
    [JsonPropertyName("handlerRetryAttempts")]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftVerbatimKeysDictionaryConverter<int>), true)]
    public IDictionary<string, int> HandlerRetryAttempts { get; set; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// Every handler whose last attempt failed and has not since succeeded, in failure order.
    /// <para>
    /// T186 L41: a run records ONE failure (<see cref="CurrentPhase"/> / <see cref="ErrorDetail"/>), so a second
    /// failure hid the first. Live, H8 failed, then H6 (dispatched earlier) failed and became the run's failure;
    /// resume re-ran only H6, and the reconciler kept re-sending H8's already-processed attempt, which the dispatcher
    /// completes without invoking — the run showed Running with H8, H4b, H9 and H7 waiting forever. Resume re-dispatches
    /// every handler listed here, and the reconciler does not re-send them (<c>DagAdvancer.ComputeReadyHandlers</c>).
    /// </para>
    /// </summary>
    [JsonPropertyName("failedHandlers")]
    public IList<string> FailedHandlers { get; set; } = new List<string>();

    /// <summary>
    /// Automatic (RetryableWithCleanup) re-dispatches each handler has used since it last succeeded or an operator
    /// last re-dispatched it. T186 L44: the auto-retry re-enqueued with no limit — live, H9 failing at its slot guard
    /// ran ~370 times in 12 minutes. <c>HandlerOutcomeApplier</c> stops re-enqueueing at
    /// <c>RollbackTransitions.MaxAutoRetries</c> and leaves the run Failed for a resume. Unlike
    /// <see cref="HandlerRetryAttempts"/> (which only ever grows, because it feeds the MessageId), this count is reset.
    /// </summary>
    [JsonPropertyName("handlerAutoRetries")]
    [Newtonsoft.Json.JsonConverter(typeof(NewtonsoftVerbatimKeysDictionaryConverter<int>), true)]
    public IDictionary<string, int> HandlerAutoRetries { get; set; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>An operator re-dispatch (resume, gate advance) gives the handler a fresh auto-retry budget (T186 L44).</summary>
    public void ResetAutoRetries(string handlerId) => HandlerAutoRetries.Remove(handlerId);

    /// <summary>
    /// The run-status transition for a handler failure the run does not retry by itself: the run takes
    /// <paramref name="status"/> (<see cref="RunStatus.Failed"/> or <see cref="RunStatus.Quarantined"/>) with
    /// <paramref name="handlerId"/> as its current phase, and the handler is recorded in <see cref="FailedHandlers"/>
    /// so a later failure of another handler cannot hide it (T186 L41).
    /// <para>
    /// A Quarantined run is never downgraded to Failed by another handler's failure (quarantine is cleared explicitly),
    /// and a Cancelled run keeps its status; the failure is still recorded.
    /// </para>
    /// </summary>
    public void MarkPhaseFailed(string handlerId, RunStatus status, string errorDetail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        if (status is not (RunStatus.Failed or RunStatus.Quarantined))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A handler failure leaves the run Failed or Quarantined.");
        }

        if (!FailedHandlers.Contains(handlerId, StringComparer.Ordinal))
        {
            FailedHandlers.Add(handlerId);
        }

        if (Status == RunStatus.Cancelled
            || (Status == RunStatus.Quarantined && status == RunStatus.Failed
                && !string.Equals(CurrentPhase, handlerId, StringComparison.Ordinal)))
        {
            return;
        }

        Status = status;
        CurrentPhase = handlerId;
        ErrorDetail = errorDetail;
    }

    private void RemoveFailedHandler(string handlerId)
    {
        for (var i = FailedHandlers.Count - 1; i >= 0; i--)
        {
            if (string.Equals(FailedHandlers[i], handlerId, StringComparison.Ordinal))
            {
                FailedHandlers.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// The run-status transition every handler applies when it succeeds: the run is <see cref="RunStatus.Running"/>
    /// with <paramref name="handlerId"/> as its current phase, so the reconciler fans out.
    /// <para>
    /// T186 L37: a handler must not overwrite ANOTHER handler's stop. Per-customer messages run one at a time, so a
    /// sibling dispatched earlier can finish after a branch has parked the run — live, H5 succeeded right after H3 put
    /// the run in <see cref="RunStatus.WaitingOnGate"/> (admin consent), wrote Running/H5, and the run stalled with no
    /// gate to advance. A WaitingOnGate, Failed or Quarantined run whose current phase is another handler keeps that
    /// status and phase (the success is still recorded in CompletedPhases by the caller); the handler that owns the stop
    /// (its own gate re-verified) moves the run on. A Cancelled run is never revived.
    /// </para>
    /// </summary>
    public void MarkPhaseSucceeded(string handlerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);

        RemoveFailedHandler(handlerId);
        ResetAutoRetries(handlerId);

        if (Status == RunStatus.Cancelled)
        {
            return;
        }

        if (Status is RunStatus.WaitingOnGate or RunStatus.Failed or RunStatus.Quarantined
            && !string.Equals(CurrentPhase, handlerId, StringComparison.Ordinal))
        {
            return;
        }

        Status = RunStatus.Running;
        CurrentPhase = handlerId;
    }

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

    /// <summary>
    /// UTC timestamp of the first resume after the clear (T186 L55). That resume also re-dispatches every ready
    /// handler, because the dispatcher completed their envelopes without running them while the run was quarantined;
    /// later resumes do not.
    /// </summary>
    [JsonPropertyName("resumedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ResumedAt { get; set; }
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
