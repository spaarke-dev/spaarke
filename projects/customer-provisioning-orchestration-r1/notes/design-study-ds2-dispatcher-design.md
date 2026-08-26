# Design Study DS-2 — L2 Provisioning Dispatcher (closes gap C1.1 / C1.2 / C1.5 / C1.6)

> **Produced by**: design-study agent, 2026-08-18. Research + design only — no source edits, no deployment.
> **Input**: [`r1-gap-analysis-2026-08-18.md`](./r1-gap-analysis-2026-08-18.md) §C-1.1/1.2/1.5/1.6, §B.4; spec.md FR-22/FR-27/FR-24; design.md §4.1/§4.2/§4C/§4D; ADR-036; ADR-028.
> **Every claim below is grep/read-verified**; file:line cites throughout.
> **Scope boundary**: the *execution environment* question (pwsh/az/pac availability where handlers run — gap C1.3) is DS-1's deliverable. This design assumes the dispatcher runs **in-process in the L2 App Service** per the owner's 2026-08-18 clarification ("BFF has no role in the provisioning execution path"); if DS-1 lands on a different host (container/worker), everything here ports unchanged except the DI host.

---

## 1. Reference pattern — BFF's `ServiceBusJobProcessor` (read in full)

Source: `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` (265 lines), `IdempotencyService.cs` (119 lines), `IJobHandler.cs`, `JobContract.cs`, `JobOutcome.cs`.

### 1.1 Shape

- **`BackgroundService`** holding a `ServiceBusProcessor` (NOT session-aware) created in `ExecuteAsync` (`ServiceBusJobProcessor.cs:50-56`):
  ```csharp
  _processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
  {
      MaxConcurrentCalls = _maxConcurrentCalls,        // ServiceBusOptions.MaxConcurrentCalls, default 5
      AutoCompleteMessages = false,
      MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
      ReceiveMode = ServiceBusReceiveMode.PeekLock
  });
  _processor.ProcessMessageAsync += ProcessMessageAsync;
  _processor.ProcessErrorAsync += ProcessErrorAsync;
  await _processor.StartProcessingAsync(stoppingToken);
  await Task.Delay(Timeout.Infinite, stoppingToken);   // park until shutdown
  ```
- **Per-message DI scope** (`:113` `using var scope = _serviceProvider.CreateScope();`) so Scoped handler dependencies resolve under the singleton BackgroundService.

### 1.2 Handler resolution

Enumerate-and-match, not keyed services (`:143,170`):
```csharp
handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();
var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);
```
`JobType` comes from the **deserialized body** (`JobContract`), not from `Subject`/`ApplicationProperties` (those are set by the enqueuer for observability but the processor routes off the body). Resolution failures are first-class dead-letter reasons: `HandlerResolutionFailed` (DI enumeration threw — a handler's dependency graph failed to construct, `:149-168`) and `NoHandler` (`:172-184`).

### 1.3 Idempotency

The BFF **processor itself performs no idempotency check** — `IdempotencyService` (Redis via `IDistributedCache`) is invoked *inside handler bodies* per ADR-004. Its contract (`IdempotencyService.cs`):

| Method | Key | TTL | Failure posture |
|---|---|---|---|
| `IsEventProcessedAsync` | `idempotency:processed:{eventId}` | 24 h | fail-open (return false) |
| `MarkEventAsProcessedAsync` | same | 24 h | swallow |
| `TryAcquireProcessingLockAsync` | `idempotency:lock:{eventId}` | 5 min default | fail-open (return true) |
| `ReleaseProcessingLockAsync` | same | — | swallow (lock self-expires) |

Note the get-then-set lock is **not atomic** (no `SETNX`); the BFF accepts the tiny race because Level 1 + Level 3 back it up.

### 1.4 Failure / dead-letter policy (`:190-241`)

| Condition | Action | DLQ reason |
|---|---|---|
| Body won't deserialize | DeadLetter | `InvalidFormat` |
| Handler enumeration throws | DeadLetter | `HandlerResolutionFailed` |
| No handler for JobType | DeadLetter | `NoHandler` |
| `outcome.Status == Completed` | `CompleteMessageAsync` | — |
| `Poisoned` OR `job.IsAtMaxAttempts` OR `DeliveryCount >= 5` | DeadLetter | `Poisoned` / `MaxRetriesExceeded` |
| Any other failed outcome | `AbandonMessageAsync` (SB redelivers) | — |
| Unhandled exception, `DeliveryCount >= 3` | DeadLetter | `ProcessingError` |
| Unhandled exception, `DeliveryCount < 3` | Abandon | — |

`ProcessErrorAsync` logs transport errors only. Shutdown: `StopAsync` override calls `StopProcessingAsync` + `DisposeAsync`; `ExecuteAsync`'s `finally` repeats both defensively with `ObjectDisposedException` tolerance (`:76-108`).

### 1.5 What L2 mirrors vs must diverge on

| Aspect | BFF | L2 dispatcher | Why diverge |
|---|---|---|---|
| Host shape | `BackgroundService` + processor + events | **same** | proven pattern |
| Processor type | `CreateProcessor` (no sessions) | **`CreateSessionProcessor`** | enqueuer sets `SessionId = CustomerId` (`ServiceBusHandlerEnqueuer.cs:133`); §4D I5 per-customer FIFO |
| Routing key | body `JobType` | body `HandlerId` (`HandlerEnvelope.cs:77`) | same idea, L2 contract |
| Resolution | enumerate + match | **keyed services** | instantiating all 19 handler graphs per message is wasteful; keyed DI is the option the code itself anticipates (`IProvisioningHandler.cs:22-23`, `HandlersModule.cs:35-38`) |
| Lock renewal | 10 min | **65 min** (configurable) | handlers run 10–30+ min (spec FR-22 acceptance; TTL table `ServiceBusModuleOptions.HandlerTimeToLive`) |
| Retry authority | SB Abandon/redeliver loop | **§4C `RollbackTransitions`** — dispatcher completes the message once the outcome is *applied*; re-dispatch is a fresh enqueue | L2 has an explicit rollback taxonomy (`RollbackTransitions.cs`); Abandon+§4C-re-enqueue would double-retry |
| Idempotency L2 location | in handler bodies | **in the dispatcher dequeue path** | gap analysis A14 target; handlers already own L3 |

---

## 2. L2 dispatcher — concrete class-level design

### 2.1 Files (all NEW, all in the L2 project — never BFF)

```
src/server/services/Sprk.Provisioning.ControlPlane/
├── Dispatch/
│   ├── ProvisioningHandlerDispatcher.cs    # the BackgroundService (this design's core)
│   ├── DispatcherOptions.cs                # bound from "Dispatcher" config section
│   ├── DispatchDecision.cs                 # enum-ish record: Complete | DeadLetter(reason,desc) | Abandon
│   ├── DispatchIdempotencyService.cs       # Level-2 Redis lock (mirror of BFF IdempotencyService)
│   ├── IDispatchIdempotencyService.cs
│   └── DispatchModule.cs                   # AddDispatchModule(services, config) per ADR-010
├── Handlers/
│   └── HandlerIds.cs                       # canonical string constants (see §3.2)
└── Reconciler/
    ├── IHandlerOutcomeApplier.cs           # NEW seam extracted from StateReconcilerService (see §5)
    └── HandlerOutcomeApplier.cs
```

### 2.2 Class

```csharp
namespace Sprk.Provisioning.ControlPlane.Dispatch;

/// <summary>
/// Drains the fleet-scoped session-enabled queue `sprk-provisioning-jobs`,
/// resolves the IProvisioningHandler by envelope HandlerId (keyed DI),
/// invokes it, and applies the outcome via IHandlerOutcomeApplier (§4C).
/// Mirror of BFF ServiceBusJobProcessor adapted for sessions + long handlers.
/// </summary>
public sealed class ProvisioningHandlerDispatcher : BackgroundService
{
    public ProvisioningHandlerDispatcher(
        ServiceBusClient serviceBusClient,          // singleton from ServiceBusModule (MI credential, ADR-028)
        IServiceScopeFactory scopeFactory,          // per-message scope, parity with reconciler
        IOptions<DispatcherOptions> options,
        IOptions<ServiceBusModuleOptions> sbOptions, // reuse QueueName — same queue the enqueuer targets
        TimeProvider timeProvider,                  // TEST-ARCHITECTURE §4 time discipline (parity with reconciler)
        ILogger<ProvisioningHandlerDispatcher> logger);

    protected override Task ExecuteAsync(CancellationToken stoppingToken);   // create + start session processor, park
    public override Task StopAsync(CancellationToken cancellationToken);     // StopProcessingAsync + DisposeAsync

    // Testable core: pure decision function over (envelope, scope). No ServiceBusReceivedMessage
    // dependency — the event wrapper translates DispatchDecision into Complete/DeadLetter/Abandon.
    internal Task<DispatchDecision> DispatchCoreAsync(
        HandlerEnvelope envelope, int deliveryCount, IServiceProvider scopedProvider, CancellationToken ct);
}
```

**Package**: `Azure.Messaging.ServiceBus` (already referenced by the L2 csproj for the enqueuer — **zero new NuGet for the processor itself**). The processor is created with:

```csharp
_processor = _serviceBusClient.CreateSessionProcessor(_queueName, new ServiceBusSessionProcessorOptions
{
    MaxConcurrentSessions        = _options.MaxConcurrentCustomers,   // default 4
    MaxConcurrentCallsPerSession = 1,                                 // HARD-CODED — §4D I5 FIFO (not configurable)
    AutoCompleteMessages         = false,
    ReceiveMode                  = ServiceBusReceiveMode.PeekLock,
    MaxAutoLockRenewalDuration   = _options.MaxHandlerDuration,       // default 01:05:00
    SessionIdleTimeout           = _options.SessionIdleTimeout,       // default 00:00:30
    PrefetchCount                = 0,                                 // long handlers — never prefetch
});
_processor.ProcessMessageAsync += OnSessionMessageAsync;
_processor.ProcessErrorAsync   += OnErrorAsync;
```

### 2.3 Why `ServiceBusSessionProcessor` (not `ServiceBusProcessor`)

1. `ServiceBusHandlerEnqueuer` **already sets `SessionId = envelope.CustomerId`** on every message (`ServiceBusHandlerEnqueuer.cs:130-133`), explicitly so "a future toggle to requiresSession=true works without code change".
2. §4D I5 (spec FR-32 family) mandates same-customer serialization; sessions give it at the transport layer.
3. **The decisive technical reason**: the DAG has parallel branches (H2b ∥ H4 ∥ H5 after H2a; H12a ∥ H12b — `DagAdvancer.cs` header diagram). Without sessions, two handlers of the SAME run execute concurrently and both do read-modify-`ReplaceRunAsync`(ETag) against the SAME Cosmos document to append `CompletedPhases` — a guaranteed ETag-conflict retry loop the current handler code has no retry-on-conflict machinery for. `MaxConcurrentCallsPerSession = 1` makes intra-run writes single-writer by construction, so the existing handler code is correct as-is.
4. Cost: one customer's parallel branches serialize (per-customer wall-clock grows; fleet throughput is preserved via `MaxConcurrentSessions`). Given handlers are 10–30 min and provisioning is a rare, hours-scale operation, correctness-by-construction wins. **This is the headline design decision — owner sign-off requested (see §7).**
5. **Hard prerequisite**: `requiresSession` is a **create-time-only** queue property, and the live queue was created via bare `az servicebus queue create` (gap C5.4 — sessions OFF, dedup OFF). The queue must be **recreated via Bicep** with `requiresSession: true` + `requiresDuplicateDetection: true` (fixes C4.6 in the same stroke). A session processor against a non-session queue fails at `StartProcessingAsync` — loudly, at boot, which is the correct NFR-05 posture.

### 2.4 Concurrency configuration (`DispatcherOptions`, section `"Dispatcher"`)

| Option | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | parity with `ReconcilerOptions.Enabled`; clean-exit when false (ADR-032-style kill switch without Null-Object — the service simply returns, exactly like `StateReconcilerService.cs:148-153`) |
| `MaxConcurrentCustomers` | 4 | `MaxConcurrentSessions` — global cap = customers provisioning in parallel |
| `MaxHandlerDuration` | `01:05:00` | `MaxAutoLockRenewalDuration` AND the Level-2 Redis lock TTL; must exceed the largest `ServiceBus:HandlerTimeToLive` entry (H9 BFF build is the long pole) |
| `SessionIdleTimeout` | `00:00:30` | release an idle session so a waiting customer's session is picked up promptly (between-handler gaps are reconciler-tick-driven, ~5 s, so 30 s is generous) |
| `MaxDeliveryCount` | 5 | dead-letter threshold for infrastructure-fault redeliveries (mirrors BFF `DeliveryCount >= 5`) |

Per-handler concurrency caps are **not needed**: within a session it's 1 by construction; across customers, no handler contends on shared state (per-customer resources) except H0 quota probes, which are read-only.

### 2.5 Message-handling flow (`OnSessionMessageAsync` → `DispatchCoreAsync`)

```
1  Deserialize HandlerEnvelope (camelCase, parity with enqueuer BodySerializerOptions)
       null/exception → DeadLetter("InvalidFormat")                          [BFF :127-133 mirror]
2  Resolve handler: scopedProvider.GetKeyedService<IProvisioningHandler>(envelope.HandlerId)
       null → DeadLetter("NoHandler", "no keyed registration for '{HandlerId}'")   [BFF :172-184 mirror]
       DI construction throws → DeadLetter("HandlerResolutionFailed")        [BFF :149-168 mirror]
3  LEVEL-2 idempotency gate (messageId = deterministic ComputeMessageId — recompute from envelope,
   identical algorithm, so the gate works even if L1 queue-dedup is off):
       IsProcessed(messageId)?          → Complete (duplicate already done; log + drop)
       !TryAcquireLock(messageId, ttl = MaxHandlerDuration) → Abandon (another instance mid-flight;
                                          SB redelivers after lock expiry; DeliveryCount cap backstops)
4  Load run: repository.ReadRunAsync(envelope.RunId, partitionKey: envelope.CustomerId)
       not found → DeadLetter("OrphanRun") — HandlerEnvelope.cs:19-20 contract: "Handlers must NOT
                   mutate anything if runs/{RunId} is absent — treat as a lost message"
       run.Status ∈ {Cancelled, Quarantined, Completed} → Complete (stale dispatch; log, no invoke)
5  Invoke: outcome = await handler.HandleAsync(envelope, ct)
       unhandled exception → outcome = HandlerResult.Failure(classifier.ClassifyException(ex), ...)
                   (FailureClassifier.cs:39 exists for exactly this; handler contract says handlers
                    SHOULD NOT throw — IProvisioningHandler.cs:63-68 — but the dispatcher must not trust that)
6  Re-read run (fresh ETag — the handler mutated the doc on its success path), then
   applied = await outcomeApplier.ApplyHandlerOutcomeAsync(run, etag, outcome, envelope.HandlerId, ct)
       — the C2.1 wiring hook, §4C taxonomy + quarantine + auto-re-enqueue all live there (see §5)
       Cosmos unreachable here → Abandon (state transition NOT landed; redelivery retries the whole
       unit; handler idempotency L3 makes the re-invoke safe); DeliveryCount >= MaxDeliveryCount
       → DeadLetter("OutcomeApplyFailed")
7  MarkProcessed(messageId, 24h); ReleaseLock(messageId)
8  Complete the message — for BOTH Success and applied-Failure. The §4C transition has landed in
   Cosmos; retry authority now belongs to RollbackTransitions.ShouldReEnqueue (auto) or the operator
   resume/clear-quarantine endpoints (manual). Abandon is reserved for steps 3/6 infrastructure faults.
```

### 2.6 Cancellation + shutdown semantics

- `stoppingToken` → `StopProcessingAsync()` (stops new receives; in-flight `ProcessMessageAsync` invocations get their `args.CancellationToken` signalled) → `DisposeAsync()`. Mirror the BFF's defensive `finally` (`ServiceBusJobProcessor.cs:76-108`) including `ObjectDisposedException` tolerance.
- **Reality check**: App Service graceful shutdown (~30 s, extendable via `WEBSITES_CONTAINER_STOP_TIME_LIMIT`) can NEVER drain a 30-min handler. The design therefore does not pretend to: an interrupted handler's message lock expires → SB redelivers → Level-2 lock has expired too → re-invoke → Level-3 `CompletedPhases` dedup no-ops the completed portion. `CrashRecoveryStartupService` (I6) independently re-enqueues `currentPhase` for orphaned Running runs. Handlers honour `ct` at their shell-out boundaries (existing `ProcessStartInfo` wrappers take tokens).
- Recommend setting `WEBSITES_CONTAINER_STOP_TIME_LIMIT=00:05:00` on the L2 App Service (slot-swap hygiene), documented in the Bicep app-settings (C5.1 fix wave).

### 2.7 Failure classification wiring

Already-shipped surfaces the dispatcher composes — **no new taxonomy code**:
- `Rollback/IFailureClassifier` / `FailureClassifier` (`Classify(Failure)` at `:32`, `ClassifyException(Exception)` at `:39`) — used in step 5 for the throw path; `ApplyHandlerOutcomeAsync` calls `Classify` internally for the returned-Failure path.
- `Rollback/RollbackTransitions` — exhaustive-switch §4C policy (`MapToRunStatus` / `ShouldReEnqueue` / `ShouldReleaseCustomerGuard`); consumed inside the outcome applier, never re-implemented in the dispatcher.
- `ShouldReleaseCustomerGuard` → the outcome applier is also the natural home to call `ICustomerRunGuard.ReleaseAsync` on guard-releasing transitions (currently only wired in endpoints; note for the implementation task).

### 2.8 Dead-letter policy summary

| DLQ reason | Trigger | Retryable by |
|---|---|---|
| `InvalidFormat` | body deserialization failure | operator (fix + re-send) |
| `NoHandler` | no keyed registration for HandlerId | deploy fix; completeness test (§6) makes this near-impossible |
| `HandlerResolutionFailed` | handler DI graph threw | deploy/config fix |
| `OrphanRun` | `runs/{RunId}` absent | none — lost-message contract |
| `OutcomeApplyFailed` | Cosmos write of §4C transition failed `MaxDeliveryCount` times | operator + I6 crash recovery |

Handler *domain* failures never DLQ — they land as §4C Cosmos transitions with full operator visibility via `GET /api/runs/{id}` + the logs endpoint. **Alerting**: App Insights alert on `deadletteredMessages > 0` metric for the queue (operator runbook item, Phase F); the `ProcessErrorAsync` log line carries `ErrorSource` for transport faults. Gap C1.6 closes with this table + the runbook entry.

---

## 3. Handler-resolution surface (closes C1.2)

### 3.1 Current state (grep-verified)

All 22 handler classes **already implement `IProvisioningHandler`** with a `public string HandlerId => HandlerIdentifier;` const-backed property (grep: 22 hits `: IProvisioningHandler`, 22 `HandlerId =>` implementations). The gap is purely **registration**: only H0 is registered against the interface (`HandlersModule.cs:103`); the other 18 top-level handlers + 3 H14 sub-handlers are concrete-type-only (`Program.cs:126–865`, module files). Zero `AddKeyedScoped` usage exists repo-wide (grep: 1 hit, and it's the HandlersModule comment anticipating it).

### 3.2 Design: keyed services over the existing concrete registrations

**`HandlerId` contract**: string constants, values verbatim from design.md §4.1 (`"H0"`, `"H0.5"`, `"H1"`, `"H2a"` … `"H14"`) — already the shape of every handler's `HandlerIdentifier` const and `DagAdvancer`'s `HandlerH*` consts. Consolidate into ONE static class so the dispatcher, DAG, and registrations share a single source:

```csharp
namespace Sprk.Provisioning.ControlPlane.Handlers;

/// <summary>Canonical HandlerId catalog. DagAdvancer consts + per-handler
/// HandlerIdentifier consts re-point here (mechanical refactor, no behavior).</summary>
public static class HandlerIds
{
    public const string H0 = "H0";  public const string H05 = "H0.5";
    public const string H1 = "H1";  public const string H2a = "H2a";  /* … through H14 */

    /// <summary>The 20 envelope-dispatchable ids. H14a/b/c are EXCLUDED —
    /// sub-handlers are orchestrated by H14 in-process, never enqueued.</summary>
    public static readonly IReadOnlyList<string> Dispatchable = [ H0, H05, H1, /* … */ H14 ];
}
```

An enum was rejected: `"H0.5"` and `"H2a"` are not valid enum identifiers without attribute mapping, the wire format is already string, and the DAG map is string-keyed.

**Registration pattern** — one line per handler, added beside each existing concrete registration (which stays, because tests and H14's sub-handler injection resolve concretes):

```csharp
// In each handler's own module / Program.cs block (ownership parity with current layout):
services.AddKeyedScoped<IProvisioningHandler>(
    HandlerIds.H1, (sp, _) => sp.GetRequiredService<H1SubscriptionReadinessHandler>());
```

The factory-forwarding form (rather than `AddKeyedScoped<IProvisioningHandler, H1SubscriptionReadinessHandler>(key)`) guarantees the keyed resolution and the concrete resolution return the **same scoped instance** — no double-construction of heavy dependency graphs. .NET 10 keyed DI (`GetKeyedService<T>(key)`) is available natively; no package. H0's existing `AddScoped<IProvisioningHandler, H0PreflightHandler>()` line is *replaced* by its keyed equivalent (nothing consumes the non-keyed interface registration today — grep-verified: the only `GetServices<IProvisioningHandler>` consumers would be new code).

**Where the mapping lives**: distributed one-liners in the modules that own each handler (matches the codebase's per-wave module ownership), with **centralized enforcement** via `HandlerIds.Dispatchable` + a registration-completeness test (§6.1) that fails the build if any dispatchable id lacks a keyed registration or any keyed registration returns a handler whose `HandlerId` property mismatches its key. This gives assembly-scan-level safety with explicit-registration-level reviewability (the same rationale `HandlersModule.cs:80-84` documents for the probe list).

**Rejected alternative — BFF-style `GetServices<IProvisioningHandler>() + FirstOrDefault`**: works (all classes implement the interface), but constructs all ~20 handler dependency graphs (hundreds of collaborators, incl. `IOptions` validation) on every message, and a single broken handler graph poisons dispatch of every other handler (`HandlerResolutionFailed` for all — the exact failure mode BFF's `:149-168` diagnostics exist to debug). Keyed resolution constructs exactly one graph and isolates faults per handler.

---

## 4. Three-level idempotency (ADR-036 / spec FR-22 / FR-27, gap A14)

### Level 1 — Service Bus MessageId dedup (exists; currently INERT)

- Enqueuer side is done and correct: deterministic `MessageId = SHA256(HandlerId|RunId|CustomerId|SHA256(ParametersJson))` (`ServiceBusHandlerEnqueuer.cs:185-192`).
- **Queue-config gap (C4.6/C5.4, confirmed)**: live queue was hand-created with `az` defaults → `requiresDuplicateDetection: false`; `infrastructure/bicep/modules/service-bus.bicep:39` also sets `false` for the queues it manages, and the provisioning queue is in **no** Bicep at all. Both `requiresDuplicateDetection` and `requiresSession` are **create-time-only** → the §2.3 queue recreation fixes both. Recommended: `duplicateDetectionHistoryTimeWindow: PT1H` (≥ the longest handler TTL so a slow first delivery can't race its own duplicate).
- **⚠ Interaction defect this design surfaces (NEW)**: once L1 dedup is ON, the §4C `RetryableWithCleanup` auto-retry breaks — `StateReconcilerService.ApplyHandlerOutcomeAsync` re-enqueues via `BuildEnvelope`, whose `ReconcilerEnqueuePayload` is deliberately byte-stable and `EnqueuedAt` is NOT in the hash → the retry message carries the **identical MessageId** as the just-consumed original and SB dedup silently drops it within the 1 h window. **Fix (small, must ship with the queue recreation)**: add an `attempt` field to `ReconcilerEnqueuePayload`, populated ONLY on the re-enqueue path (first-enqueue stays attempt-absent so reconciler-tick duplicate-suppression — the actual purpose of L1 — is preserved). paramHash changes → fresh MessageId → retry survives dedup.

### Level 2 — Redis per-handler-run lock (NEW; closes C1.5)

Mirror `IdempotencyService` into `Dispatch/DispatchIdempotencyService.cs` (L2 cannot reference the BFF assembly — same isolation rule as `IProvisioningHandler.cs:8-13`; a ~100-line intentional copy, consistent with the existing enqueuer/handler-contract copies):

- **Keys**: `provisioning:idempotency:processed:{messageId}` (TTL 24 h) and `provisioning:idempotency:lock:{messageId}` (TTL = `DispatcherOptions.MaxHandlerDuration`, NOT the BFF's 5-min default — a 30-min handler must hold the lock for its whole runtime since there is no mid-flight renewal on `IDistributedCache`).
- **`{messageId}`** = the recomputed deterministic `ComputeMessageId(envelope)` (make that helper `public` or expose via a small shared static) — this makes Level 2 effective even while L1 remains inert, and identical across L2 instances.
- **Retry semantics**: lock-held → `Abandon` (SB redelivers with backoff via delivery count; by then either processed-marker hits or the lock expired). Fail-open on Redis outage, exactly like the BFF (`IdempotencyService.cs:43,96`) — L1 + L3 backstop; provisioning must not hard-depend on cache availability.
- **Dependencies**: `Microsoft.Extensions.Caching.StackExchangeRedis` package + `Redis:ConnectionString` (KV-referenced app setting) against the existing **per-environment** Redis (spec MUST NOT provision per-customer Redis; `Deploy-RedisCache.ps1` is the per-env owner). This is the one new NuGet + one new config key the dispatcher introduces. If the owner prefers to defer Redis, the documented fallback is a §6.5 **Path A exception narrowing FR-22 to 2-level for L2** — the analysis in gap C1.5 already frames this; NOT recommended, because §2.6 showed lock-loss-under-renewal-failure is the dispatcher's realest duplicate-execution window and Level 2 is the guard sized for it.

### Level 3 — durable dedup in handler bodies (exists)

- **Universal**: every handler already scans `ProvisioningRun.CompletedPhases` for its `(Phase, IdempotencyKey)` and returns `HandlerResult.Success` no-op on hit (`IProvisioningHandler.cs:41-44`; gap analysis B.1-A14 "L3: implemented per handler — CompletedPhases scans present"). The dispatcher needs nothing here.
- **Dataverse alt-key upsert applies only to Dataverse-writing handlers**: H5 (env record), H7 (env-var values — upsert by schema name), H10 (app users), H11 (users by UPN), H12a/H12b/H12c (seed rows — additive/upsert per §14A), H13 (registry `sprk_setupstatus` PATCH by GUID, rides on the C1.4 real registry client), H14c (service endpoints). Handlers whose side effects are natively idempotent APIs need no alt-key: H1 (read-only probe), H2a (ARM deployment = idempotent by deployment name), H2b (index PUT), H3 (app-reg lookup-then-create), H4 (KV set = last-write-wins + never-delete guard), H8 (containerType existence check), H9 (slot swap w/ gates), H14a (policy set), H14b (subscription lookup-then-create).

---

## 5. Interaction with `StateReconcilerService` — dispatcher does NOT advance the DAG

**Answer: outcome-write + reconciler-advance (option A). The dispatcher never computes ready-sets.**

Evidence this is the shipped architecture's intent:
- `StateReconcilerService.cs:49-56` header: reconciler enqueue-side is deliberately Cosmos-write-free and ETag-race-free *because* "handlers own state-transition writes"; ready-set is a "pure function of CompletedPhases" (`DagAdvancer`), recomputed every 5 s tick.
- `ApplyHandlerOutcomeAsync` (`:331-476`) exists **precisely as the dispatcher's hook**: ":352-357 — the wiring hook lives here so the classifier + transition table are the SINGLE source of truth for any future consumer". It is Success-no-op (handler already wrote CompletedPhases), Failure-transition + quarantine + §4C auto-re-enqueue, ETag-conflict-tolerant (Conflict → no re-enqueue, next tick reconciles — `:439-445`).
- If the dispatcher advanced the DAG directly, ready-set computation would run in two places with different views of the run doc → duplicate-enqueue windows the deterministic-MessageId design (`:36-43`) was built to make harmless *only under the reconciler's single-algorithm assumption*.

**The 5 s handoff latency** between handler-complete and next-handler-enqueue (≤19 hops ≈ ≤95 s per run total) is noise against 10–30-min handlers. No dispatcher→reconciler signal path is warranted.

**One refactor required (small, mechanical)**: `ApplyHandlerOutcomeAsync` is an `internal` instance method on the `BackgroundService`. Extract it verbatim into a Scoped `HandlerOutcomeApplier : IHandlerOutcomeApplier` (namespace `Reconciler`, registered in `ReconcilerModule`), with `StateReconcilerService` keeping a thin delegating internal method so its existing tests stay green. The dispatcher consumes the interface from its per-message scope. `HandlerOutcomeApplied` record moves with it. This resolves gap C2.1 (zero production callers) as a byproduct.

**Adjacent blocker (not this design, must land before E2E)**: C4.5 — the Cosmos default serializer writes `status` as an **integer** while `CosmosActiveRunScanner.cs:46` queries string literals, so even with the dispatcher live the reconciler's scan returns zero rows. The dispatcher design assumes that fix ships in its wave.

---

## 6. Testing surface (closes C1.8; ADR-038 KEEP paths)

### 6.1 Unit (`Sprk.Provisioning.ControlPlane.Tests/Dispatch/`)

| Test | Verifies |
|---|---|
| `HandlerRegistrationCompletenessTests` | Build the real DI container (Program.cs composition via `WebApplicationFactory` or the module extensions): every `HandlerIds.Dispatchable` id resolves via `GetKeyedService<IProvisioningHandler>`, the resolved instance's `HandlerId` equals its key, and H14a/b/c are NOT keyed-registered. **This is the forcing function that makes `NoHandler` DLQ unreachable.** |
| `HandlerEnvelopeRoundTripTests` | Enqueuer `BodySerializerOptions` serialize → dispatcher deserialize → identical envelope (guards the camelCase contract both sides depend on) |
| `DispatchCoreDecisionTests` | Table-driven over `DispatchCoreAsync`: bad JSON → DeadLetter(InvalidFormat); unknown id → DeadLetter(NoHandler); processed-marker hit → Complete-without-invoke; lock-held → Abandon; orphan run → DeadLetter(OrphanRun); terminal-status run → Complete-without-invoke; Success → applier-called + Complete; Failure → applier-called + Complete; handler throws → `ClassifyException` path + applier-called; applier throws + deliveryCount < max → Abandon; ≥ max → DeadLetter(OutcomeApplyFailed). Fakes: keyed fake handler, in-memory `IProvisioningRunRepository`, recording `IHandlerOutcomeApplier`. |
| `DispatchIdempotencyServiceTests` | Mirror of BFF's coverage against in-memory `IDistributedCache` incl. fail-open branches; lock TTL = MaxHandlerDuration |
| `ReconcilerEnqueuePayloadAttemptTests` | §4-L1 fix: retry-path envelope MessageId ≠ original; first-enqueue MessageId stable across ticks |
| (existing) `StateReconcilerServiceTests` | unchanged — delegating shim keeps them compiling against the extracted applier |

### 6.2 Integration seam (`tests/integration/seam/**` — the ADR-038 dispatch-spine DoD category)

- **Primary seam (no live SB)**: real DI container + real JSON + real Cosmos **emulator/test container** (the L2 test project already has Cosmos smoke tests to pattern-match): enqueue-shape message → `DispatchCoreAsync` → real fake-scripted handler (a keyed test handler that writes `CompletedPhases`) → real `HandlerOutcomeApplier` → assert Cosmos transition; then drive one real `StateReconcilerService.RunTickAsync` and assert the NEXT handler's envelope reaches a recording enqueuer — **the full "message in → handler executed → Cosmos transitioned → DAG advanced" loop with only the AMQP wire faked**.
- **Wire-level**: the official **Service Bus emulator** (Docker) supports session-enabled queues; use it for `CreateSessionProcessor` behavior (session FIFO, `MaxConcurrentCallsPerSession=1`, Complete/Abandon/DeadLetter round-trips). ⚠ Verify emulator support for `requiresDuplicateDetection` before leaning on it for L1 tests (spotty historically); if unsupported, L1 dedup is covered by the existing live-dev SB smoke-test pattern instead.
- **FR-22 acceptance (30-min handler, lock renewal)**: emulator lock-renewal fidelity is not trustworthy — run against the **live dev namespace** with a configurable-delay test handler (compressed to ~6 min with `MaxAutoLockRenewalDuration=1min`-scale settings to prove renewal machinery, plus one full-length run in Phase F). Also re-run the task-062 no-duplicate-under-N-reconcilers load test WITH the consumer attached — completing the half of FR-22 acceptance the gap analysis marks untested.

### 6.3 E2E — Phase F (out of scope here)

Full `POST /api/runs → sprk_setupstatus = Ready` against a real customer stamp; owned by task 089's harness once C1.3/C1.4/C3.x land.

---

## 7. Decisions requiring owner sign-off (ranked)

1. **Session-serialized execution** (§2.3): adopt `ServiceBusSessionProcessor` with `MaxConcurrentCallsPerSession=1`, deliberately serializing the DAG's parallel branches per customer, in exchange for single-writer Cosmos semantics and transport-level I5 — and accept the **queue recreation** (sessions + dedup are create-time-only) via Bicep as its prerequisite. Alternative (plain processor, BFF-mirror) requires adding ETag-retry loops to every handler's CompletedPhases append — strictly more code and more failure modes.
2. **L1-dedup vs §4C-retry conflict** (§4-L1): approve the `attempt`-field fix to `ReconcilerEnqueuePayload` shipping in the same wave as queue recreation — without it, enabling dedup silently kills auto-retry.
3. **Redis Level 2 in L2** (§4-L2): add the StackExchangeRedis dependency + per-env Redis binding, vs a documented §6.5 Path-A exception narrowing to 2-level. Recommended: implement.
4. **FR-22/design-§4.2 text reconciliation**: this design places the consumer in L2, matching the MUST rules and the owner's clarification but contradicting FR-22's literal "handlers run in BFF's existing IJobHandler infrastructure" — the spec/design correction (gap analysis §D "Doc reconciliation") should cite this document.

---

## 8. Implementation-size accounting (for task creation)

| Piece | Size |
|---|---|
| `ProvisioningHandlerDispatcher` + options + decision record + module | ~450 LOC (BFF's is 265; sessions + run-load + outcome wiring add the rest) |
| `HandlerIds` + 20 keyed one-liners + const re-pointing | ~120 LOC mechanical |
| `HandlerOutcomeApplier` extraction | ~move-only + 30 LOC shim |
| `DispatchIdempotencyService` + Redis wiring | ~150 LOC + 1 package + 1 config key |
| `ReconcilerEnqueuePayload.attempt` | ~15 LOC |
| Bicep: queue (sessions+dedup) + SB Data Receiver RBAC (C5.5 — Receiver granted **nowhere** today) + Dispatcher/Redis app settings | 1 Bicep task |
| Tests per §6.1/6.2 | ~15 unit + 3 seam + 2 live-SB |

*Design study only. No source, config, Azure state, or `.claude/**` files modified.*
