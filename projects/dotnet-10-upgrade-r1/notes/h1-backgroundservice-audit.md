# H1 — `BackgroundService.ExecuteAsync` threading audit (closed per-worker verdict list)

> **Task**: 010 (P1 hit-site remediation) · **Spec**: FR-07 · **Design**: §5 H1
> **Author**: task-execute (Opus 4.8, FULL rigor, effort xhigh) — 2026-08-12
> **Adversarial verification**: task 011 (non-author) — NFR-07
> **Outcome**: **28 hosted-service implementers audited · 28 SAFE · 0 REMEDIATE · 0 code changes**

---

## 1. The .NET 10 behavioral change (what we are auditing against)

In **.NET 10**, `Microsoft.Extensions.Hosting.BackgroundService` changed how `ExecuteAsync` is invoked:

| | .NET 8 (old) | .NET 10 (new) |
|---|---|---|
| Where `ExecuteAsync` runs | The base `StartAsync` calls `ExecuteAsync(stoppingToken)` and the **synchronous prefix up to the first suspending `await` runs inline on the host startup thread**. | The base `StartAsync` schedules `ExecuteAsync` onto a **background thread**; `StartAsync` returns immediately without running any of `ExecuteAsync`'s body inline. |
| Pre-`await` synchronous init | Completes **before** `StartAsync` returns → before the host proceeds to serve traffic. | Runs later, on a thread-pool thread; **not** guaranteed complete before traffic is served. |
| Pre-`await` `throw` | Propagates **synchronously** out of `StartAsync` → **crashes host startup** (HTTP 500.30 on App Service). | Faults the background task → handled by `BackgroundServiceExceptionBehavior` (default `StopHost`) → host stops **asynchronously**, not as a synchronous startup abort. |

Source: [BackgroundService.ExecuteAsync breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).

**Two — and only two — things break a worker under this change:**

- **(a) Pre-`await` init assumed complete before traffic** — heavy synchronous initialization before the first `await` that another component (an endpoint, another service) observes as done before the app serves requests.
- **(b) Pre-`await` fail-fast** — a synchronous `throw` before the first `await` that is *intended* to abort host startup.

**Crucially, the change is scoped to `BackgroundService.ExecuteAsync` only.** `IHostedService.StartAsync` (and `IHostedLifecycleService`) are still awaited synchronously by `IHost.StartAsync()` in registration order, and a `throw` from `StartAsync` still aborts startup exactly as before. This is why the design §5 H1 fix guidance is "move ordering-sensitive / fail-fast code to ctor / `StartAsync` / `IHostedLifecycleService`."

---

## 2. Discovery method (closed-set construction — spec FR-07)

The spec's 8-name list is explicitly **not** asserted complete. The closed set was built by grep, not from the spec:

```
grep -rn "class \w+ : ... (BackgroundService|IHostedService|IHostedLifecycleService)" src/server
```

- Matched every class declaration extending `BackgroundService` or implementing `IHostedService` across `src/server/**` (BFF + `Spaarke.Scheduling`).
- **Excluded** `Services/BackgroundServices/_archive/JobProcessor.cs.archived-2025-10-03` — the `.archived-*` extension means it is not part of the compilation (verified: not a `.cs` file). It is dead code, not a live worker.
- No `IHostedLifecycleService` implementers exist in the tree (0 matches) — the codebase uses `IHostedService` + `BackgroundService` only.

**Result: 28 live hosted-service implementers** (23 `BackgroundService`, 5 `IHostedService`).

---

## 3. Per-worker verdict table (CLOSED SET — 28)

Legend: **BG** = `BackgroundService` · **IHS** = `IHostedService` · first-await = the first suspending `await` in `ExecuteAsync`/`StartAsync`.

### 3.1 `BackgroundService` — periodic / timer / delay loops

| # | Worker | File | Pre-`await` region | Verdict |
|---|--------|------|--------------------|---------|
| 1 | `ScheduledJobHost` | `shared/Spaarke.Scheduling/ScheduledJobHost.cs:80` | Log only, then `await RefreshDefinitionsAsync`. Comment: initial-load failure is tolerated, host keeps running; empty registry = valid steady state. | **SAFE** |
| 2 | `TodoGenerationService` | `Services/Workspace/TodoGenerationService.cs:183` | Log + `CalculateInitialDelay()` (trivial), then `await Task.Delay`. Dataverse resolution is **post-await** (line 213) and swallowed on failure. See §4 for the 500.30-guard analysis. | **SAFE** |
| 3 | `SpeWebhookRenewalHostedService` | `Services/Compose/SpeWebhookRenewalHostedService.cs:58` | Log only, then `while` → `await RenewDueAsync`. Per-iteration try/catch: "never let a transient failure kill the host." | **SAFE** |
| 4 | `StaleCheckoutSweeperHostedService` | `Services/Compose/StaleCheckoutSweeperHostedService.cs:93` | Log only, then `while` → `await ScanAndReleaseStaleAsync`. Per-iteration try/catch. | **SAFE** |
| 5 | `SpeDashboardSyncService` | `Services/SpeAdmin/SpeDashboardSyncService.cs:203` | Options read + log, then `await RunSyncSafeAsync` ("initial sync … before first request"). See §5 — the "before first request" intent was already async-racing under net8 and is exception-swallowed; net10 changes nothing observable. | **SAFE** (note §5) |
| 6 | `ScheduledRagIndexingService` | `Services/Jobs/ScheduledRagIndexingService.cs:77` | `if(!Enabled){ log; return; }` + `if(no TenantId){ LogError; return; }` — graceful `return`, **not** `throw`. First await later in loop. | **SAFE** |
| 7 | `RecordSyncJob` | `Services/Jobs/RecordSyncJob.cs:275` | `if(!Enabled){return;}` + `if(no AiSearchEndpoint){ LogError "will not start"; return; }` + `BuildSearchClient()` (own field). Graceful `return`, not `throw`. | **SAFE** |
| 8 | `SessionFilesCleanupJob` | `Services/Ai/Chat/SessionFilesCleanupJob.cs:122` | Interval calc + log + `PeriodicTimer`, then `while` → `await`. | **SAFE** |
| 9 | `EmbeddingMigrationService` | `Services/Ai/Jobs/EmbeddingMigrationService.cs:141` | `if(!Enabled){ log; return; }` + log, then try → await. Graceful `return`. | **SAFE** |
| 10 | `DailySendCountResetService` | `Services/Communication/DailySendCountResetService.cs:23` | Log, then `while` → `CalculateDelayUntilMidnightUtc()` (trivial) → `await Task.Delay`. | **SAFE** |
| 11 | `DemoExpirationService` | `Services/Registration/DemoExpirationService.cs:43` | Log, then `while` → trivial calc → `await Task.Delay`. | **SAFE** |
| 12 | `GraphSubscriptionManager` | `Services/Communication/GraphSubscriptionManager.cs:67` | Log, then **`await Task.Delay(10s)`** ("let dependencies warm up during app startup") as first await; loop wrapped so "a startup failure doesn't kill the service." | **SAFE** |
| 13 | `InboundPollingBackupService` | `Services/Communication/InboundPollingBackupService.cs:58` | Log, then **`await Task.Delay(15s)`** warm-up; try-wrapped. | **SAFE** |
| 14 | `MailboxDeltaReconciliationService` | `Services/Communication/MailboxDeltaReconciliationService.cs:57` | Log, then **`await Task.Delay(StartupDelay)`** warm-up; "startup failure must not kill the service." | **SAFE** |
| 15 | `MembershipReconcileSweepService` | `Services/Communication/Membership/MembershipReconcileSweepService.cs:39` | Options read, then **`await Task.Delay(InitialDelay)`** as first await; loop try/catch. | **SAFE** |

### 3.2 `BackgroundService` — Azure Service Bus processor (log → `CreateProcessor` → `await StartProcessingAsync`)

`ServiceBusClient.CreateProcessor(...)` is a synchronous object construction (no network I/O); it throws only on invalid arguments (a deterministic config error that fails identically every boot). No worker treats its success as a fail-fast startup gate; late attachment of a Service Bus listener does not affect HTTP traffic. See §6 for the pre-`await`-throw analysis.

| # | Worker | File | Notes | Verdict |
|---|--------|------|-------|---------|
| 16 | `ServiceBusJobProcessor` | `Services/Jobs/ServiceBusJobProcessor.cs:42` | `CreateProcessor` inside `try`; `await StartProcessingAsync`. | **SAFE** |
| 17 | `UploadFinalizationWorker` | `Workers/Office/UploadFinalizationWorker.cs:110` | `CreateProcessor` inside `try`; `await StartProcessingAsync`. | **SAFE** |
| 18 | `ProfileSummaryWorker` | `Workers/Office/ProfileSummaryWorker.cs:76` | `CreateProcessor` before `try`; `await StartProcessingAsync` + `await Task.Delay(Infinite)`. | **SAFE** (note §6) |
| 19 | `IndexingWorkerHostedService` | `Workers/Office/IndexingWorkerHostedService.cs:70` | `CreateProcessor` before `try`; `await StartProcessingAsync` + `await Task.Delay(Infinite)`. | **SAFE** (note §6) |
| 20 | `CommunicationJobProcessor` | `Services/Communication/CommunicationJobProcessor.cs:47` | `CreateProcessor` inside `try`; `await StartProcessingAsync`. | **SAFE** |
| 21 | `MembershipJunctionUpdaterHost` | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs:124` | `_clientFactory(_options)` + `CreateProcessor` inside `try`; `await StartProcessingAsync`. | **SAFE** |

### 3.3 `BackgroundService` — channel/queue reader

| # | Worker | File | Notes | Verdict |
|---|--------|------|-------|---------|
| 22 | `BulkOperationService` | `Services/SpeAdmin/BulkOperationService.cs:147` | Log, then `await foreach (var job in _queue.Reader.ReadAllAsync(...))` — first op is an await over a `Channel` reader. No pre-await init, no throw. | **SAFE** |

### 3.4 `BackgroundService` — null-object no-op (ADR-032 kill-switch)

| # | Worker | File | Notes | Verdict |
|---|--------|------|-------|---------|
| 23 | `NullMembershipJunctionUpdaterHost` | `Services/Ai/Membership/NullMembershipJunctionUpdaterHost.cs:57` | Non-`async` override: logs "disabled" and `return Task.CompletedTask`. No `await`, no init, no `throw`. | **SAFE** |

### 3.5 `IHostedService` — `StartAsync` (UNAFFECTED by the H1 change)

The H1 change is scoped to `BackgroundService.ExecuteAsync`. These five implement `IHostedService.StartAsync` directly; the host still awaits `StartAsync` synchronously in registration order under net10, and a `throw` still aborts startup. Their ordering/fail-fast semantics are **preserved unchanged**.

| # | Worker | File | Notes | Verdict |
|---|--------|------|-------|---------|
| 24 | `StartupValidationService` | `Infrastructure/Startup/StartupValidationService.cs:32` | **Reference fail-fast**: triggers `IOptions.Value` validation; on `OptionsValidationException` logs critical + **re-`throw`s** to abort startup (line 63). Because this lives in `StartAsync`, net10 preserves the abort. See §4. | **SAFE** |
| 25 | `SchedulingBootstrapHostedService` | `Infrastructure/DI/SchedulingModule.cs:176` | Registers `PlaybookSchedulerJob` into `ScheduledJobRegistry` (idempotent; duplicate caught). Consumer `ScheduledJobHost` is self-healing (periodic refresh; empty registry valid). See §7. | **SAFE** |
| 26 | `MembershipReconciliationBootstrapHostedService` | `Infrastructure/DI/MembershipModule.cs:351` | Same registry-bootstrap pattern as #25 (idempotent register + duplicate-catch). | **SAFE** |
| 27 | `MembershipCacheInvalidationSubscriber` | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs:119` | `StartAsync` `await`s Redis `SubscribeAsync`, then `_ = Task.Run(ProcessMessagesAsync)` (already-explicit background loop). `IHostedService` — unaffected. | **SAFE** |
| 28 | `RoutingConsumerTypeHealthCheck` | `Services/Ai/PublicContracts/RoutingConsumerTypeHealthCheck.cs:92` | `StartAsync`: `if(!AiEnabled) return;` then scoped warm probe. `IHostedService` — unaffected; graceful `return`. | **SAFE** |

---

## 4. `TodoGenerationService` — explicit 500.30 startup-crash guard analysis (spec FR-07 acceptance criterion 2)

`TodoGenerationService` carries explicit comments (lines 151–157) describing a "500.30" concern:

> *"Lazily resolved to avoid forcing Dataverse connection at host startup. `DataverseServiceClientImpl` connects eagerly in its constructor — if that connection fails … it throws, which crashes the host with HTTP 500.30 because BackgroundService resolution happens during `IHost.StartAsync()`."*

**What the guard actually protects against — and why net10 does not affect it:**

1. The 500.30 concern is a **constructor** concern, not an `ExecuteAsync` concern. The failure mode is: if `IDataverseService` were resolved in the worker's constructor, DI would instantiate it during `IHost.StartAsync()`, and `DataverseServiceClientImpl`'s eager-connecting constructor could throw → synchronous startup crash → 500.30. **Constructor / DI-instantiation semantics are entirely unchanged by the .NET 10 `ExecuteAsync` threading change.**
2. The guard's mechanism is: keep the risky resolution **out of the constructor** (the ctor only does null-checks, lines 172–176) and perform it **lazily inside `ExecuteAsync`, after the first `await`** (line 213, after `await Task.Delay` at line 201), wrapped in a try/catch that **logs and `return`s** — explicitly "This does not affect app startup."
3. Therefore the worker never relied on any pre-`await` synchronous behavior: its first `await` is reached after only a log + trivial delay calc, and its Dataverse touch was **already post-await** (i.e., already on a continuation, already off the startup thread) under net8. The net10 change (pre-await code now also on a background thread) moves only the log + delay-calc, which nothing observes.

**Conclusion**: the 500.30 guard is a constructor-avoidance guard. It remains **valid, necessary, and fully effective** under net10, unchanged. `TodoGenerationService` is **SAFE**; no remediation. (If anything, net10 is *strictly safer* for the constructor-throw class of failure, but that path is already avoided here by design.)

---

## 5. `SpeDashboardSyncService` — "initial sync before first request" (residual note for task 011)

`ExecuteAsync` comments "Run an initial sync on startup so the cache is populated before first request" then `await RunSyncSafeAsync(stoppingToken)`.

This reads like an ordering dependency but is **not** one, under either runtime:

- `RunSyncSafeAsync` performs Dataverse/Graph I/O — it **never completes synchronously**. Under net8, `ExecuteAsync`'s inline prefix reached `await RunSyncSafeAsync`, which suspended, returning control to `StartAsync` **before** the sync finished. So the cache was **already** populated asynchronously, racing the first request, on net8.
- The helper is `...SafeAsync` — it swallows exceptions. Any endpoint reading the dashboard cache therefore already had to tolerate a cold/empty cache (the sync may not have run, or may have failed) — this was true on net8.
- net10 moves only the two trivial synchronous lines (options read + log) onto the background thread. It introduces **no new window** that did not already exist.

**Verdict: SAFE.** Residual item for the adversarial verifier (011): empirically confirm the dashboard-cache reader tolerates an unpopulated cache (it must, since the populate was already async+swallowed on net8). No net10-specific regression exists.

---

## 6. Service-Bus-processor workers — pre-`await`-`throw` note (residual for task 011)

Workers #16–21 call `ServiceBusClient.CreateProcessor(...)` synchronously before `await StartProcessingAsync`. Two are outside a `try` (#18 `ProfileSummaryWorker`, #19 `IndexingWorkerHostedService`).

- `CreateProcessor` constructs an in-memory processor object; it performs **no network I/O** and throws only on invalid arguments (e.g., null/empty queue name) — a deterministic misconfiguration that would fail identically on every boot regardless of runtime.
- If it *did* throw pre-`await`: net8 → synchronous startup crash; net10 → background-task fault → default `BackgroundServiceExceptionBehavior.StopHost` → host stops asynchronously. In both cases the host does not stay up in a healthy state; the observable operational outcome (host does not serve) is equivalent, only the timing/mechanism differs.
- No worker treats `CreateProcessor` success as an intended fail-fast startup gate (it is listener wiring, not config validation), and genuine config validation is centralized in `StartupValidationService` (§4). So there is **no depended-upon pre-`await` fail-fast** to lose.

**Verdict: SAFE** (NFR-01 zero-behavior-change holds in practice). Residual item for 011: if desired, `ProfileSummaryWorker`/`IndexingWorkerHostedService` could move `CreateProcessor` inside their existing `try` for symmetry with the others — this is a **cosmetic hardening, not an H1 requirement**, and is intentionally **not** applied here to honor NFR-01 (zero behavior change) + the escalation boundary (no functional change without a carve-out decision).

---

## 7. Startup ordering — job-registry bootstraps (#25, #26)

`SchedulingBootstrapHostedService` / `MembershipReconciliationBootstrapHostedService` register `IScheduledJob`s into `ScheduledJobRegistry` from `IHostedService.StartAsync`. The consumer `ScheduledJobHost` (BG #1) reads the registry in `RefreshDefinitionsAsync`. This is **not** a fragile startup ordering under net10:

- The bootstraps are `IHostedService.StartAsync` (awaited in registration order, unaffected by net10).
- `ScheduledJobHost` is deliberately **self-healing**: its own comment states an empty registry at boot is "a valid steady state," and it re-reads the registry on every periodic refresh tick. A job registered slightly later is picked up on the next refresh.
- Under net10, `ScheduledJobHost.ExecuteAsync` is scheduled onto a thread-pool thread and does not run until after all `StartAsync` calls have completed — which gives the bootstraps' `StartAsync` time to register regardless of relative registration order.

**Verdict: SAFE.** No ordering dependency is broken.

---

## 8. Summary & acceptance-criteria mapping

| Acceptance criterion (POML) | Status |
|---|---|
| Doc lists EVERY `BackgroundService`/`IHostedService` found by grep, each with an explicit SAFE/REMEDIATED verdict | ✅ §3 — closed set of 28, all verdicts explicit |
| `TodoGenerationService` 500.30 guard explicitly analyzed + fail-fast intent preserved under net10 | ✅ §4 |
| Every REMEDIATE worker moved fail-fast/ordering code to ctor/StartAsync/IHostedLifecycleService; BFF builds green | ✅ vacuously — **0 REMEDIATE**; BFF build green (Release, zero source changes; P0 baseline stands) |
| Negative: no worker's functional behavior changed — only where init/ordering is anchored | ✅ trivially — **0 code changes** |

**Why the whole set is SAFE (root cause):** the codebase already follows the ADR-001 worker discipline that the H1 fix guidance prescribes. The single fail-fast that must abort startup (config validation) already lives in `IHostedService.StartAsync` (`StartupValidationService`), which net10 leaves untouched. Every `BackgroundService` reaches its first `await` with only a trivial synchronous prefix and uses graceful `return` (never a pre-`await` `throw`) for disabled/misconfig paths; warm-up and initial-sync patterns were already asynchronous-and-tolerant under net8; ordering-sensitive registrations run in `StartAsync` against a self-healing registry.

**The escalation trigger (POML) did NOT fire** — no worker required a behavior change to preserve correctness under net10.

**Handoff to task 011 (adversarial verification, non-author):** two residual empirical checks are flagged for independent confirmation — §5 (dashboard-cache reader tolerates cold cache) and §6 (SB `CreateProcessor` pre-await throw is not a depended-upon fail-fast). Both are argued SAFE here because net10 introduces no window that did not already exist on net8.
