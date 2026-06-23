# Scheduled Jobs Migration — Design

> **Project**: `scheduled-jobs-migration`
> **Status**: Design (draft for owner review)
> **Created**: 2026-06-22
> **Predecessor**: [`spaarke-platform-foundations-r3`](../spaarke-platform-foundations-r3/) (delivered the framework + 2 reference consumers)
> **Author**: Scaffolded by Claude Code, design pending owner sharpening

---

## Problem Statement

### What's broken / suboptimal today

R3 delivered the `Spaarke.Scheduling` framework (`IScheduledJob` contract + `ScheduledJobHost` runner + `ScheduledJobRegistry` + `IBackgroundJobStore` persistence + `sprk_backgroundjob` / `sprk_backgroundjobrun` Dataverse entities + `/api/admin/jobs/*` admin endpoints) and proved the pattern with two reference consumers (`PlaybookSchedulerJob` migrated in R3 task 023; `MembershipReconciliationJob` family in R3 task 085).

Despite this, **17 schedule-driven `BackgroundService` implementations remain bespoke** in `src/server/api/Sprk.Bff.Api/`. Each invents its own:

- `PeriodicTimer` (or hand-rolled `Task.Delay` loop) for cadence
- `IOptions<XOptions>` + `appsettings.json` for schedule configuration
- Logging conventions (some structured, some not; no shared correlation ID)
- Retry / backoff behavior (most have none)
- Idempotency story (most rely on "good enough" no-op detection)

### The cost of leaving them as-is

| Cost | Concrete impact |
|---|---|
| **No operator visibility** | An operator who wants to know "did the dashboard sync run last night?" has to grep App Service logs. Each job logs differently. |
| **No manual trigger** | A failed run cannot be re-driven without restarting the App Service or waiting for the next cadence tick. R3 reference consumers can be triggered via `POST /api/admin/jobs/{jobId}/trigger`. |
| **No run history audit** | `sprk_backgroundjobrun` rows give a queryable / reportable history; ad-hoc `BackgroundService` impls leave only Application Insights traces. |
| **Inconsistent retry behavior** | Each impl decides whether to retry, how long to wait, whether to escalate. Most have no retry — a one-off transient Graph 503 silently kills the cadence tick. |
| **Cadence drift over time** | New operators inherit the codebase + pattern; without a framework anchor, new schedule-driven services will continue to be authored as bespoke `BackgroundService` impls. |
| **Configuration sprawl** | 17 distinct `IOptions<XOptions>` blocks in `appsettings.json` for cadence — should be one unified mechanism. |
| **Testing burden** | Each impl needs bespoke unit tests for cadence + idempotency. The framework's standard `IScheduledJob` contract is testable as a unit (no host needed). |

### Why this is the right time

- Framework is **proven** (2 reference consumers running in production since R3 merge)
- Per-job migration cost is **bounded** (R3 task 023 took ~3h; task 085 was the Service Bus variant — kept as Tier 2 boundary case)
- Architecture doc (`docs/architecture/background-workers-architecture.md`) and operator guide (`docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`) already exist — no doc-debt overhang
- Behavioral migration risk is **per-job-bounded** (one BackgroundService at a time; each ships in its own PR or wave PR)

---

## Discovery (current BackgroundService inventory)

A `Grep ": BackgroundService"` in `src/server/api/Sprk.Bff.Api/` returned 28 hits. One is archived (`Services/BackgroundServices/_archive/JobProcessor.cs.archived-2025-10-03`). The remaining 27 are classified below.

> **Note on missing/already-migrated jobs**: The R3 reference consumers (`PlaybookSchedulerJob`, `MembershipReconciliationJob`) no longer extend `BackgroundService` (they implement `IScheduledJob` and run inside `ScheduledJobHost`), so they don't appear in this grep — exactly the post-migration state we want for everything else in Tier 1.

### Tier 1 — Schedule-driven candidates (IN SCOPE — 17 jobs)

These use `PeriodicTimer` or hand-rolled `Task.Delay` loops with a fixed cadence and are candidates to become `IScheduledJob` implementations.

| # | Class | File | What it does (1-line guess) | Cadence mechanism | Priority |
|---|---|---|---|---|---|
| 1 | `ScheduledRagIndexingService` | `Services/Jobs/ScheduledRagIndexingService.cs` | Periodically indexes content into RAG store | PeriodicTimer | P1 |
| 2 | `RecordSyncJob` | `Services/Jobs/RecordSyncJob.cs` | Syncs records between systems on a cadence | PeriodicTimer | P1 |
| 3 | `DocumentVectorBackfillService` | `Services/Jobs/DocumentVectorBackfillService.cs` | Backfills vector embeddings for documents missing them | PeriodicTimer | P1 |
| 4 | `TodoGenerationService` | `Services/Workspace/TodoGenerationService.cs` | Generates daily To Do items per ADR-001 | PeriodicTimer (24h per WorkspaceModule.cs:112) | P2 |
| 5 | `ManifestRefreshService` | `Services/Ai/Capabilities/ManifestRefreshService.cs` | Refreshes capability manifest cache | PeriodicTimer | P1 |
| 6 | `SpeDashboardSyncService` | `Services/SpeAdmin/SpeDashboardSyncService.cs` | Periodically syncs SPE dashboard data | PeriodicTimer | P1 |
| 7 | `BulkOperationService` | `Services/SpeAdmin/BulkOperationService.cs` | Drives bulk operations as a long-running background pump | PeriodicTimer (verify — could be Tier 3) | P2 |
| 8 | `SessionFilesCleanupJob` | `Services/Ai/Chat/SessionFilesCleanupJob.cs` | Cleans up expired session files on a cadence | PeriodicTimer | P1 |
| 9 | `PlaybookIndexingBackgroundService` | `Services/Ai/PlaybookEmbedding/PlaybookIndexingBackgroundService.cs` | Indexes playbook embeddings into search store | PeriodicTimer | P1 |
| 10 | `DemoExpirationService` | `Services/Registration/DemoExpirationService.cs` | Daily sweep for expired demo registrations (delay-to-midnight pattern) | `Task.Delay(delayUntilMidnightUtc)` (daily cadence; per-day variant of cadence) | P1 |
| 11 | `EmbeddingMigrationService` | `Services/Ai/Jobs/EmbeddingMigrationService.cs` | One-time-ish migration of embeddings; may be Tier 3 (one-shot) — verify | PeriodicTimer (verify) | P3 (low priority — possibly retire after one run) |
| 12 | `DailySendCountResetService` | `Services/Communication/DailySendCountResetService.cs` | Resets per-account daily send-count counter at midnight UTC | Delay-to-midnight pattern | P2 |
| 13 | `InboundPollingBackupService` | `Services/Communication/InboundPollingBackupService.cs` | Backup polling for inbound emails when Graph webhooks miss | PeriodicTimer | P1 |
| 14 | `GraphSubscriptionManager` | `Services/Communication/GraphSubscriptionManager.cs` | Manages Graph webhook subscription lifecycle (renew before expiry) | PeriodicTimer (30 min interval per xmldoc) | P2 (sensitive: webhook continuity) |
| 15 | `UploadFinalizationWorker` | `Workers/Office/UploadFinalizationWorker.cs` | **Borderline**: implements `IOfficeJobHandler` (handler pattern); BackgroundService form may be vestigial — investigate whether this is currently a Tier 2 consumer registered as a BackgroundService for legacy reasons | Mixed (ServiceBusClient injected) | INVESTIGATE — likely Tier 2 |
| 16 | `ProfileSummaryWorker` | `Workers/Office/ProfileSummaryWorker.cs` | Same shape as #15 — IOfficeJobHandler + BackgroundService | Mixed | INVESTIGATE — likely Tier 2 |
| 17 | `IndexingWorkerHostedService` | `Workers/Office/IndexingWorkerHostedService.cs` | Same shape — Office handler family | Mixed | INVESTIGATE — likely Tier 2 |

> **Investigation needed on #15-17**: The `Workers/Office/*Worker` family looks like Service Bus consumers wearing BackgroundService clothing (they inject `ServiceBusClient` and implement `IOfficeJobHandler`). Phase 0 audit MUST classify each definitively before scope is locked. If they prove Tier 2, the in-scope Tier 1 count drops to **14**, not 17.

### Tier 2 — Event-driven (queue/subscription consumers — OUT OF SCOPE per ADR-036)

These respond to Service Bus messages, not schedule ticks. They don't fit the `IScheduledJob` contract (no cadence; idempotency model is per-message, not per-run).

| # | Class | File | Why excluded |
|---|---|---|---|
| 1 | `ServiceBusJobProcessor` | `Services/Jobs/ServiceBusJobProcessor.cs` | Generic Service Bus job dispatcher (ADR-004 job contract); no cadence |
| 2 | `CommunicationJobProcessor` | `Services/Communication/CommunicationJobProcessor.cs` | `ServiceBusProcessor` consumer (verified line 25) |
| 3 | `MembershipJunctionUpdaterHost` | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | Topic subscription consumer for `sprk-membership-changes` (verified xmldoc line 7) — R3 task 084 deliberately kept as Tier 2; companion `MembershipReconciliationJob` (Tier 1, already migrated) handles the scheduled reconciliation |
| 4 | `UploadFinalizationWorker` | `Workers/Office/UploadFinalizationWorker.cs` | **Pending Phase 0 confirm** — likely Tier 2 per shape |
| 5 | `ProfileSummaryWorker` | `Workers/Office/ProfileSummaryWorker.cs` | **Pending Phase 0 confirm** — likely Tier 2 per shape |
| 6 | `IndexingWorkerHostedService` | `Workers/Office/IndexingWorkerHostedService.cs` | **Pending Phase 0 confirm** — likely Tier 2 per shape |

ADR-036 explicitly excludes event-driven workers from the `IScheduledJob` framework. A future companion framework (`Spaarke.Messaging` or similar) could unify queue consumers; that is **not** in this project's scope.

### Tier 3 — Long-lived workers / null-objects (NOT migration candidates)

| # | Class | File | Why not migrating |
|---|---|---|---|
| 1 | `NullMembershipJunctionUpdaterHost` | `Services/Ai/Membership/NullMembershipJunctionUpdaterHost.cs` | ADR-032 null-object kill-switch impl — replaces `MembershipJunctionUpdaterHost` when feature-flagged off. Migrate together with #3 in Tier 2 (or not at all). |

### Per-tier counts (locked at Phase 0)

- **Tier 1 (in scope)**: 14 confirmed + 3 pending Phase 0 investigation = **14–17 jobs**
- **Tier 2 (out of scope)**: 3 confirmed + 3 pending Phase 0 investigation = **3–6 jobs**
- **Tier 3 (not migration candidates)**: 1 job
- **Already migrated (R3)**: 2 (`PlaybookSchedulerJob`, `MembershipReconciliationJob`) — these are the templates

---

## Migration Strategy

### Phasing

| Phase | Scope | Job count | Rationale |
|---|---|---|---|
| **Phase 0 — Audit** | Confirm Tier 1 vs Tier 2 classification for the 3 pending Office workers; verify cadence + idempotency story for each Tier 1 job; identify any cross-cutting concerns (shared state, ordering dependencies) | 0 migrations | Locks scope before any code change |
| **Phase 1 — Quick wins** | Simple PeriodicTimer-based, low business risk, no cross-cutting concerns | 6–8 jobs | Builds confidence; reuses R3 task 023 template directly |
| **Phase 2 — Medium complexity** | Jobs with custom state, daily cadence (delay-to-midnight), or feature flags | 4–6 jobs | Requires more thought per migration but pattern is established |
| **Phase 3 — Hard cases** | Jobs with cross-cutting concerns (webhook continuity, business-critical timing) | 2–3 jobs | Highest behavioral-regression risk; ship last with extra acceptance tests |
| **Phase 4 — Wrap-up** | Remove vestigial `BackgroundService` registrations + update architecture doc inventory | 0 migrations | Cleanup |

### Per-job task pattern

Each Tier 1 migration is **~1 task, ~2–4h of effort**, following the R3 PlaybookSchedulerJob template (R3 task 023). Tasks within a wave run in parallel unless they share a file (rare for these mostly-isolated services).

**Wave sizing**: 3–5 jobs per wave. With 14–17 total Tier 1 jobs, that's **3–6 waves**.

### Backwards compatibility

Each migration MUST be functionally equivalent to the pre-migration impl unless an intentional deviation is documented. The default migration removes the bespoke `BackgroundService` registration and adds an `IScheduledJob` registration — the new impl is invoked by `ScheduledJobHost` on the same cadence.

For jobs with **delay-to-midnight** patterns (Tier 1 #10 `DemoExpirationService`, #12 `DailySendCountResetService`), the framework's cron support handles `0 0 * * *` (midnight UTC); confirm the framework supports daily cron during Phase 0 (or extend it if not — would be a framework PR, not part of this project's scope).

---

## Per-Job Migration Recipe

For each Tier 1 BackgroundService, the task does:

### Step 1 — Pre-migration audit (~15 min)
- Read the existing `BackgroundService` impl end-to-end
- Document: current cadence, current retry behavior, current idempotency story, any cross-cutting concerns (e.g., reads `IOptionsMonitor`, depends on host startup ordering)
- Identify the "scoped work unit" — the chunk of work that needs to run once per cadence tick

### Step 2 — Implement IScheduledJob (~1–2h)
- Create new class `Sprk.Bff.Api.Services.{Area}.{Name}Job : IScheduledJob`
- Implement `JobId` (kebab-case, e.g., `"rag-indexing"`, `"document-vector-backfill"`)
- Implement `DefaultSchedule` (cron expression — e.g., `"0 */15 * * * *"` for every 15 min)
- Implement `RunAsync(JobRunContext ctx, CancellationToken ct)` — extract the body of the old `ExecuteAsync` loop, returning `JobRunResult.Success(...)` or `JobRunResult.Failure(...)`
- Inject scoped dependencies via `IServiceScopeFactory` (singleton job → scoped DI per run, per the framework contract)
- Preserve structured logging + correlation ID flow

### Step 3 — Register with ScheduledJobRegistry (~10 min)
- Replace the existing `services.AddHostedService<XService>()` registration with `services.AddSpaarkeScheduledJob<XJob>()` (or the equivalent registry call per the R3 task 023 template)
- Remove `IOptions<XServiceOptions>` if it's no longer used (or move cadence into the framework's `ScheduledJobHostOptions`)
- Update `appsettings.json` if needed

### Step 4 — Acceptance test (~30 min)
- Unit test: instantiate `XJob`, call `RunAsync(ctx, CT.None)`, assert behavior matches pre-migration impl
- Integration check: after deploy, verify `GET /api/admin/jobs` lists `XJob`'s `JobId`; trigger via `POST /api/admin/jobs/{JobId}/trigger`; assert a `sprk_backgroundjobrun` row is created

### Step 5 — Delete the old BackgroundService (~10 min)
- Remove the old `XService.cs` file
- Remove `XServiceOptions.cs` if obsolete
- Remove obsolete `appsettings.json` keys
- Update any tests / references

### Step 6 — Per-job PR checklist (per CLAUDE.md §10)
- ✅ Publish size: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, report delta (expected: ~0 MB change since framework already shipped in R3)
- ✅ Zero new HIGH-severity CVEs
- ✅ Test added/updated per bff-extensions.md § F Test update obligation
- ✅ No new AI-direct dependencies in CRUD code

### Worked examples to reference

- **R3 task 023** — `PlaybookSchedulerService` → `PlaybookSchedulerJob` (the canonical mechanical migration template)
- **R3 task 085** — `MembershipReconciliationJob` (a job that fans out work; demonstrates the `ResultJson` pattern)

---

## Risks

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| **Behavioral regression** — existing job's quirks (e.g., specific exception swallowing, off-hour skips) not preserved | High per job | Medium | Pre-migration audit (Step 1) explicitly documents quirks; per-job acceptance test verifies behavior |
| **Schedule drift** — cron string translation from `PeriodicTimer(TimeSpan.FromMinutes(N))` mis-translates | Medium | Low | Cron expressions documented in JobId xmldoc; reviewer cross-checks against pre-migration impl |
| **Webhook continuity loss** — Tier 1 #14 `GraphSubscriptionManager` has business-critical cadence (subscriptions auto-expire after 3 days; renewal threshold 24h); a mis-scheduled migration kills inbound email | HIGH | Low | Migrate in Phase 3 with extra scrutiny; staged rollout (deploy to dev → 48h soak → prod) |
| **Operator surprise** — jobs that were invisible become visible → operators investigate "new" failures that were always there | Low | High | Communicate the migration in deploy notes; document each migrated job's "expected behavior" in `BACKGROUND-JOBS-ADMIN-GUIDE.md` |
| **Office worker misclassification** — if Tier 1 #15-17 are actually Tier 2, the project scope is smaller than estimated; if they're Tier 1 with quirks, larger | Medium | Medium (depends on Phase 0 audit) | Phase 0 audit task locks classification before Phase 1 starts; OQ-1 below explicit |
| **Framework gap** — daily cron (`0 0 * * *`) or other expressions not supported by `ScheduledJobHost` | Medium | Low | Verify in Phase 0; if gap exists, file framework-extension task in `spaarke-platform-foundations-r3` follow-up rather than work around |
| **Dependency on R3 framework behavior** | Medium | Low | Framework is in production since R3 merge; PlaybookSchedulerJob run history is the canary for framework stability |

---

## Estimated Effort

- **Tier 1 in-scope**: 14–17 jobs × 2–4h = **30–70h total developer time**
- **Phase 0 audit**: ~1 day
- **Wrap-up (Phase 4)**: ~0.5 day
- **With parallel waves of 3–5**: ~3–6 waves × 1 day per wave wall-clock = **5–10 days wall-clock** (assuming serial waves; could compress with multiple agents)

This is opportunistic infrastructure work; **does not block any feature delivery**.

---

## Acceptance Criteria

(For full graduation criteria, see [`README.md`](README.md). Reproduced here as the design contract.)

- All Tier-1 BackgroundService implementations confirmed by Phase 0 audit are migrated to `IScheduledJob`
- Each migrated job appears in `GET /api/admin/jobs`
- Each migrated job has a row in `sprk_backgroundjobrun` after a triggered or scheduled run
- Per-job acceptance test exists and passes
- Original BackgroundService implementations are removed
- `docs/architecture/background-workers-architecture.md` Tier-1 unmigrated inventory drops to zero
- Tier-2 / Tier-3 boundaries are explicitly documented in the architecture doc (so future contributors know not to migrate them)
- Publish size unchanged or smaller; zero new HIGH-severity CVEs

---

## Related ADRs / Docs

- **ADR-001** — BackgroundService pattern (foundational; this project migrates within the ADR-001 constraint, not against it)
- **ADR-010** — DI patterns (Singleton-with-Scoped pattern central to `IScheduledJob`)
- **ADR-012** — Shared component library (Spaarke.Scheduling is the shared lib)
- **ADR-032** — Null-Object Kill-Switch (relevant for feature-flagged jobs)
- **ADR-036** — Background Job Infrastructure (PRIMARY — framework spec)
- [`docs/architecture/spaarke-scheduling-architecture.md`](../../docs/architecture/spaarke-scheduling-architecture.md) — R3 Wave 27 framework architecture
- [`docs/architecture/background-workers-architecture.md`](../../docs/architecture/background-workers-architecture.md) — current inventory; this project drives the inventory down
- [`docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`](../../docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md) — operator-facing guide
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — pre-merge checklist applies to every task in this project
- **Reference implementations**: R3 tasks `023-*.poml` (PlaybookSchedulerJob) and `085-*.poml` (MembershipReconciliationJob)

---

## Open Questions for Owner

1. **OQ-1 — Office worker classification**: Are `UploadFinalizationWorker`, `ProfileSummaryWorker`, `IndexingWorkerHostedService` Tier 1 (schedule-driven) or Tier 2 (Service Bus consumers)? Phase 0 audit will determine empirically, but the owner may have prior context.
2. **OQ-2 — Phase 2 in scope?** The "Medium complexity" jobs (TodoGenerationService with 24h cadence, GraphSubscriptionManager with webhook continuity) are higher-risk. Is the project scoped for ALL Tier 1 jobs, or do we ship Phase 1 (quick wins) only and defer Phase 2/3 to a follow-up?
3. **OQ-3 — Service Bus + schedule hybrid jobs**: If a Tier 2 job has a scheduled component (e.g., "if the queue has been quiet for >1h, drain the overflow table"), should it be migrated? Or kept fully Tier 2 with the scheduled aspect as a separate Tier 1 companion job?
4. **OQ-4 — Acceptance bar**: Is the bar "**byte-equivalent behavior** verified by per-job acceptance test" OR "**good enough + add framework observability**" (i.e., behavior may drift in non-functional ways like timing, as long as the work happens)?
5. **OQ-5 — EmbeddingMigrationService disposition**: Tier 1 #11 may be a one-shot migration that's effectively done. Migrate, delete entirely, or leave as-is?
6. **OQ-6 — Framework cron gap audit**: Phase 0 should confirm `ScheduledJobHost` supports daily cron (`0 0 * * *`), PeriodicTimer-equivalent intervals (`*/15 * * * *`), and delay-to-midnight semantics. If not, is extending the framework in scope, or is it a hard dependency on a separate framework-PR?
7. **OQ-7 — Naming convention**: Should migrated jobs follow `XJob` (matches `PlaybookSchedulerJob`) or `XScheduledJob` (more explicit)? The R3 reference impls use `XJob`. Locking this avoids per-task bikeshedding.
8. **OQ-8 — JobId namespacing**: Should `JobId` strings be flat kebab-case (`rag-indexing`) or namespaced (`ai/rag-indexing`, `comms/send-count-reset`)? Affects admin UI grouping.

Answer these before running `/design-to-spec`.
