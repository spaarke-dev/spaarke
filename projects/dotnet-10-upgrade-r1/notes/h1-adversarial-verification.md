# H1 — Adversarial verification of the `BackgroundService.ExecuteAsync` threading audit

> **Task**: 011 (NFR-07 non-author adversarial verification) · **Spec**: FR-07 / NFR-07 · **Design**: §5 H1
> **Reviewer**: task-011 agent (Opus 4.8, FULL rigor, effort xhigh) — **NOT the author of task 010** (non-author requirement satisfied; task 010 was authored by a separate task-execute agent)
> **Artifact under review**: `projects/dotnet-10-upgrade-r1/notes/h1-backgroundservice-audit.md` (task 010)
> **Method**: independent grep-derived closed set + per-worker source re-read + empirical reader trace for the two residual items. READ-ONLY; no source edits; no build run (P0 green baseline stands).
> **FINAL VERDICT: PASS** — the audit's conclusions are sound. Independent count matches (28); every SAFE verdict independently confirmed; both residual items (§5, §6) empirically verified SAFE. **0 REMEDIATE, 0 MISSED.**

---

## 1. The .NET 10 change (what breaks a worker)

`BackgroundService.ExecuteAsync` now runs on a background thread. Only two things break a worker:
- **(a)** pre-`await` synchronous init that another component observes as complete before traffic;
- **(b)** a pre-`await` `throw` *intended* as a startup fail-fast (no longer aborts host startup synchronously; instead faults the background task → default `StopHost`).

The change is scoped to `BackgroundService.ExecuteAsync` **only**. `IHostedService.StartAsync` is still awaited synchronously; a `throw` there still aborts startup. This scoping is the load-bearing fact for the 5 `IHostedService` verdicts.

---

## 2. Independent closed-set re-derivation — **MATCH (28)**

I did NOT trust the author's list. I ran my own greps across `src/server`:

- Single-line declaration grep (`class X : … BackgroundService|IHostedService|IHostedLifecycleService`): **28 classes**.
- Multiline declaration grep (base class on a wrapped line, up to 200 chars): **28 files, 28 occurrences** — no additional multi-line declarations.
- Broad mention grep: **48 files** mention these terms. I inspected all 20 extras that are NOT in the 28. Every one is either a comment reference (e.g. `PlaybookSchedulerJob`, `InsightsOrchestrator`, `AnalysisResultPersistence`, `PlaybookInvocationService`, the DI `*Module.cs` registrars, `Program.cs`) or implements `IJobHandler` / `IScheduledJob` (`InsightsIngestJobHandler`, `ProfileSummaryJobHandler`, `BulkRagIndexingJobHandler`, `PlaybookSchedulerJob`) — **job handlers/scheduled jobs invoked BY a processor, not hosted services themselves**. No indirect base class, no nested hosted-service class, no expression-bodied hidden implementer.
- **`IHostedLifecycleService`**: 0 implementers (confirmed independently).
- **Archived-file exclusion is legitimate**: `Services/BackgroundServices/_archive/JobProcessor.cs.archived-2025-10-03` has extension `.archived-2025-10-03`, NOT `.cs`. `Sprk.Bff.Api.csproj` declares no explicit `<Compile>` items → SDK default glob (`**/*.cs`) is in effect → the file is not part of the compilation. (Two other `.cs.archived-*` files exist under `_archive/` dirs for the same reason.) Excluding it is correct; it is dead code.

**Result: 28 live implementers (23 `BackgroundService` + 5 `IHostedService`). MATCHES the audit exactly. No worker missed.**

---

## 3. Per-worker independent verdict — all **CONFIRMED (AGREE)**

Legend: **CONFIRMED** = I independently re-read the ctor + `ExecuteAsync`/`StartAsync` and agree with SAFE.

### 3.1 `BackgroundService` — periodic / timer / delay loops (read openings to first `await`)

| # | Worker | Author | My verdict | Independent evidence |
|---|--------|--------|-----------|----------------------|
| 1 | `ScheduledJobHost` | SAFE | **CONFIRMED** | ctor null-checks only (`ScheduledJobHost.cs:72-76`); `ExecuteAsync` logs → `await RefreshDefinitionsAsync` (`:89`) with explicit self-healing comment ("empty registry = valid steady state"). No pre-await throw. |
| 2 | `TodoGenerationService` | SAFE | **CONFIRMED** | See §4. ctor null-checks only (`:172-176`); Dataverse resolution post-await (`:213`), try→log→return. |
| 3 | `SpeWebhookRenewalHostedService` | SAFE | **CONFIRMED** | Log → `while` → `await RenewDueAsync` (`:70`); per-iteration try/catch. |
| 4 | `StaleCheckoutSweeperHostedService` | SAFE | **CONFIRMED** | Log → `while` → `await ScanAndReleaseStaleAsync` (`:105`); per-iteration try/catch. |
| 5 | `SpeDashboardSyncService` | SAFE (note §5) | **CONFIRMED** | See §5 — reader tolerates cold cache (empirically confirmed). |
| 6 | `ScheduledRagIndexingService` | SAFE | **CONFIRMED** | Disabled/no-TenantId paths use graceful `return` (`:83`, `:90`), NOT throw. |
| 7 | `RecordSyncJob` | SAFE | **CONFIRMED** | Disabled/no-endpoint use `return` (`:281`, `:288`); `BuildSearchClient()` (`:291`) constructs own field (sync object build, no I/O, endpoint pre-validated non-empty). See §6 note (b). |
| 8 | `SessionFilesCleanupJob` | SAFE | **CONFIRMED** | Log + `PeriodicTimer`; first await `Task.WhenAny` (`:143`). |
| 9 | `EmbeddingMigrationService` | SAFE | **CONFIRMED** | Disabled → `return` (`:146`); first await `Task.Delay(15s)` (`:157`). |
| 10 | `DailySendCountResetService` | SAFE | **CONFIRMED** | Log → `while` → trivial calc → `await Task.Delay` (`:37`). |
| 11 | `DemoExpirationService` | SAFE | **CONFIRMED** | Log → `while` → trivial calc → `await Task.Delay` (`:57`). |
| 12 | `GraphSubscriptionManager` | SAFE | **CONFIRMED** | Log → first await `Task.Delay(10s)` warm-up (`:75`). |
| 13 | `InboundPollingBackupService` | SAFE | **CONFIRMED** | Log → first await `Task.Delay(15s)` warm-up (`:65`). |
| 14 | `MailboxDeltaReconciliationService` | SAFE | **CONFIRMED** | Log → first await `Task.Delay(StartupDelay)` (`:64`). |
| 15 | `MembershipReconcileSweepService` | SAFE | **CONFIRMED** | Options read → first await `Task.Delay(InitialDelay)` (`:44`). |

### 3.2 `BackgroundService` — Azure Service Bus processors

`ServiceBusClient.CreateProcessor(...)` is synchronous in-memory object construction (no network I/O; AMQP link opens on `StartProcessingAsync`); throws only on invalid arguments. Independently confirmed no worker treats its success as a depended-upon startup fail-fast gate.

| # | Worker | Author | My verdict | Independent evidence |
|---|--------|--------|-----------|----------------------|
| 16 | `ServiceBusJobProcessor` | SAFE | **CONFIRMED** | `CreateProcessor` INSIDE try (`:50`); `await StartProcessingAsync` (`:61`). |
| 17 | `UploadFinalizationWorker` | SAFE | **CONFIRMED** | `CreateProcessor` INSIDE try (`:118`), const `QueueName`; `await StartProcessingAsync` (`:132`). |
| 18 | `ProfileSummaryWorker` | SAFE (note §6) | **CONFIRMED** | `CreateProcessor` OUTSIDE try (`:82`), **const** `QueueName="office-profile"`, DI-injected+null-checked `_serviceBusClient`. See §6. |
| 19 | `IndexingWorkerHostedService` | SAFE (note §6) | **CONFIRMED** | `CreateProcessor` OUTSIDE try (`:76`), **const** `QueueName="office-indexing"`, DI-injected+null-checked client. See §6. |
| 20 | `CommunicationJobProcessor` | SAFE | **CONFIRMED** | `CreateProcessor` INSIDE try (`:54`); `await StartProcessingAsync` (`:65`). |
| 21 | `MembershipJunctionUpdaterHost` | SAFE | **CONFIRMED** | `_clientFactory(_options)` + `CreateProcessor` INSIDE try (`:135-136`); `await StartProcessingAsync` (`:150`). |

### 3.3 / 3.4 Channel reader + null-object

| # | Worker | Author | My verdict | Independent evidence |
|---|--------|--------|-----------|----------------------|
| 22 | `BulkOperationService` | SAFE | **CONFIRMED** | Log → `await foreach (_queue.Reader.ReadAllAsync)` (`:151`). Producer endpoint writes to the channel; delayed reader start only queues messages (no loss, no ordering dependency). |
| 23 | `NullMembershipJunctionUpdaterHost` | SAFE | **CONFIRMED** | Non-async override: log + `return Task.CompletedTask` (`:57-63`). No await, no init, no throw. |

### 3.5 `IHostedService` — `StartAsync` (unaffected by the H1 change)

| # | Worker | Author | My verdict | Independent evidence |
|---|--------|--------|-----------|----------------------|
| 24 | `StartupValidationService` | SAFE | **CONFIRMED** | The ONLY intended startup fail-fast: `IOptions.Value` validation → on `OptionsValidationException` logs critical + **`throw`** (`:63`). Lives in `StartAsync` → net10 preserves synchronous abort. Correctly placed. |
| 25 | `SchedulingBootstrapHostedService` | SAFE | **CONFIRMED** | `StartAsync` registers job (idempotent; duplicate `InvalidOperationException` caught, `:186`) + seeds store; returns `Task.CompletedTask`. Consumer `ScheduledJobHost` is self-healing. See §7. |
| 26 | `MembershipReconciliationBootstrapHostedService` | SAFE | **CONFIRMED** | Same idempotent-register + duplicate-catch pattern (`:356-366`). |
| 27 | `MembershipCacheInvalidationSubscriber` | SAFE | **CONFIRMED** | `StartAsync` awaits Redis `SubscribeAsync` (`:124`) — **fail-soft**: catch → log + continue (`:135-143`), explicitly "we do NOT crash the host"; then `_ = Task.Run(ProcessMessagesAsync)`. IHostedService — unaffected. |
| 28 | `RoutingConsumerTypeHealthCheck` | SAFE | **CONFIRMED** | `StartAsync`: enabled-check → graceful `return` (`:100`, `:109`); drift only **logs Error** (`:125`), does not throw; surfaced as Unhealthy via `CheckHealthAsync`. IHostedService — unaffected. |

**Every one of the 28 is independently CONFIRMED. 0 DISAGREE, 0 REFUTED, 0 MISSED.**

---

## 4. `TodoGenerationService` 500.30 guard (spec FR-07 acceptance criterion) — CONFIRMED

Independently re-read `TodoGenerationService.cs`:
- **Constructor (`:168-176`) does NOT touch Dataverse** — only three null-checks. The 500.30 concern (`DataverseServiceClientImpl` connects eagerly in its ctor → throw → 500.30 during `IHost.StartAsync()`) is genuinely a **constructor / DI-instantiation** concern.
- The risky resolution is deferred: `_serviceProvider.GetRequiredService<IDataverseService>()` at **`:213`**, which is **after** the first `await Task.Delay(initialDelay, …)` at `:201`, wrapped in try/catch that **logs + `return`s** ("This does not affect app startup.", `:222`).
- Constructor/DI semantics are entirely unchanged by the net10 `ExecuteAsync` threading change. The Dataverse touch was already post-await (already a continuation) on net8. net10 moves only the log + trivial delay calc, which nothing observes.

**The guard is a constructor-avoidance guard; net10 leaves it intact and fully effective. CONFIRMED. If anything net10 is strictly safer for the ctor-throw class, but that path is already avoided by design.**

---

## 5. Residual item §5 — `SpeDashboardSyncService` cold-cache reader — **VERIFIED SAFE**

I traced the actual reader, not just the writer.

- **Writer**: `SpeDashboardSyncService.ExecuteAsync` (`:203`) logs + reads options, then `await RunSyncSafeAsync` (`:212`). `RunSyncSafeAsync` → `FetchAndAggregateDashboardMetricsAsync` → `LoadContainerTypeConfigsAsync` → `await _dataverseClient.QueryAsync(...)` (`SpeDashboardSyncService.cs:383`) = real network I/O that **always suspends**. So even on net8 the "initial sync before first request" did NOT complete before `StartAsync` returned — the cache was already populated asynchronously, racing the first request. `RunSyncSafeAsync` swallows all non-cancellation exceptions (`:284-287`). net10 moves only the two trivial synchronous lines onto the background thread — **no new window**.
- **Reader (the load-bearing empirical check)**: `GET /api/spe/dashboard/metrics` → `GetDashboardMetricsAsync` at **`src/server/api/Sprk.Bff.Api/Endpoints/SpeAdmin/DashboardEndpoints.cs:63`**. It calls `syncService.ReadCachedMetricsAsync(ct)` and at **`DashboardEndpoints.cs:72-76`** does `if (metrics == null) { log; return Results.NoContent(); }`. `ReadCachedMetricsAsync` (`SpeDashboardSyncService.cs:456-471`) returns `null` when the cache key is unset and swallows read errors (returns null). The endpoint's own doc comment states: **"204 No Content — No metrics cached yet (service just started, hasn't completed first sync)."**

**The reader is explicitly designed for a cold/unpopulated cache and returns 204. There is no populate-before-traffic assumption anywhere. §5 is SAFE — no net10-specific regression. The on-demand `TriggerRefreshAsync` path (`:167`) also polls-then-returns-whatever-is-cached, tolerating a not-yet-populated cache.**

---

## 6. Residual item §6 — `CreateProcessor` pre-`await`-throw (#18, #19) — **VERIFIED SAFE**

- Confirmed `ProfileSummaryWorker.cs:82` and `IndexingWorkerHostedService.cs:76` call `CreateProcessor` **outside** the surrounding `try`, before the first `await`.
- `ServiceBusClient.CreateProcessor` is a synchronous factory that constructs an in-memory `ServiceBusProcessor`; the AMQP connection is established lazily on `StartProcessingAsync` (which IS inside the try in both). It performs no network I/O and throws only on invalid arguments.
- In BOTH #18 and #19 the queue name is a **compile-time `const`** (`"office-profile"` / `"office-indexing"`), the injected `ServiceBusClient` is null-checked in the ctor, and `ServiceBusProcessorOptions` are hardcoded valid. So `CreateProcessor` here effectively **cannot** throw — there is no invalid-argument path reachable at runtime.
- Even in the general case: a throw would be a deterministic misconfiguration that fails identically every boot; net8 → sync startup crash, net10 → background fault → `StopHost` (host stops async). No component observes the processor being started before HTTP traffic (it is a queue listener, not a config gate). It is **not** a depended-upon fail-fast. Genuine config fail-fast is centralized in `StartupValidationService` (§4).

**(b) note on #7 `RecordSyncJob`:** `BuildSearchClient()` at `RecordSyncJob.cs:291` is a second pre-`await` synchronous construction site of the same class (build an Azure `SearchClient` object; endpoint pre-validated non-empty at `:284`). Same reasoning: object construction, deterministic-on-misconfig, not a depended-upon fail-fast, out-of-band indexer. The audit accounts for it inline in the #7 row rather than in §6, but the SAFE verdict is unaffected. I note it here only for completeness — it does not change any conclusion.

**§6 verdict SAFE holds.** The audit's decision to leave `CreateProcessor` outside the try (cosmetic-only hardening, NFR-01 zero-behavior-change) is correct and consistent with the escalation boundary.

---

## 7. Startup ordering — registry bootstraps (#25, #26 → #1) — CONFIRMED

`SchedulingBootstrapHostedService` / `MembershipReconciliationBootstrapHostedService` register jobs in `IHostedService.StartAsync` (awaited in registration order, unaffected by net10). The consumer `ScheduledJobHost` (BG #1) reads the registry in `ExecuteAsync`.

Adversarial angle checked: on net10, `ScheduledJobHost`'s base `StartAsync` schedules `ExecuteAsync` on a background thread and returns immediately, so `ExecuteAsync` could in principle run and read the registry before a later-registered bootstrap's `StartAsync` completes. **This does not break correctness** because `ScheduledJobHost` is deliberately self-healing: empty registry at boot is a valid steady state (`ScheduledJobHost.cs:86-88`) and the registry is re-read on every periodic refresh tick. A job registered slightly later is picked up next tick. The same race was already tolerable on net8 (the inline prefix reached `await RefreshDefinitionsAsync`, which suspends on Dataverse I/O). No new correctness issue. CONFIRMED.

---

## 8. Attempts to break the audit that FAILED (adversarial log)

1. **Missed implementer via indirect/nested/multiline base class** → multiline grep + inspection of all 20 non-declaration mentions found none. FAILED to break.
2. **Archived file actually compiled** → confirmed `.archived-*` extension excluded from `**/*.cs` default glob. FAILED to break.
3. **A pre-`await` `throw` used as fail-fast in some BackgroundService** → read every `ExecuteAsync` opening; all disabled/misconfig paths use graceful `return`; the only `throw`-to-fail-fast is in `StartupValidationService.StartAsync` (IHostedService, unaffected). FAILED to break.
4. **An endpoint that assumes a worker's pre-await init completed before traffic** → the only "before first request" claim (`SpeDashboardSyncService`) has a reader that returns 204 on cold cache; `BulkOperationService`'s producer just queues into a Channel. FAILED to break.
5. **`CreateProcessor` outside try as a depended-upon startup gate (#18/#19)** → const queue names + hardcoded valid options make it un-throwable at runtime; not observed before traffic. FAILED to break.
6. **500.30 guard rendered moot / newly-broken by net10** → it is a ctor concern; ctor/DI semantics unchanged; resolution already post-await on net8. FAILED to break.

---

## 9. Conclusion

- **Closed set**: independently re-derived = **28** (23 BG + 5 IHS). **MATCH** with the audit. **0 missed.**
- **Per-worker**: all **28 CONFIRMED (AGREE)**. **0 DISAGREE / 0 REFUTED.**
- **Findings requiring re-classification to REMEDIATE**: **NONE.**
- **§5 (dashboard cold-cache reader)**: **VERIFIED SAFE** — reader returns `204 No Content` on null cache (`DashboardEndpoints.cs:72-76`).
- **§6 (`CreateProcessor` pre-await throw #18/#19)**: **VERIFIED SAFE** — synchronous no-I/O construction, const queue names (un-throwable at runtime), not a depended-upon fail-fast.
- **TodoGenerationService 500.30 guard**: **CONFIRMED** intact under net10 (ctor-avoidance guard; resolution post-await).
- **Escalation trigger (POML)**: did NOT fire — no worker requires a behavior change to be correct under net10.

**FINAL: PASS.** The task-010 H1 audit is complete, its closed set is genuinely closed, and every SAFE verdict withstands adversarial scrutiny. Task 010 does NOT need to be reopened; P1 may close on the H1 dimension. (No `🔄` flag required in TASK-INDEX.)
