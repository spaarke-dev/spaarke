# Task 103 — Scheduled jobs run exactly once: lease, slot guard, `AddScheduledJob<TJob>`

> **Status**: ✅ **COMPLETE 2026-09-14** (session 11) — `a9c538c72` + Step 9.5 fixes `0f1414c37`. Verification in §6; quality gates in §7.
> **Rules implemented**: ADR-036 A1 rules 1 (one dispatch per schedule), 2 (slots) and 6 (registration), plus the
> new **A1.1** owner decision.

## 1. The problem, as found in code

`ScheduledJobHost` started on every App Service instance and every deployment slot, and its only duplicate check
(`HasRunForScheduledTimeAsync`) was process-local. So each tick of each job ran once **per instance**:
- `model2-full` autoscales from 2 instances, so the hourly notification playbook scheduler sent duplicates on any
  stamp that scaled out.
- Task 100's reminders could duplicate the same way.

Each job also had its own copy-pasted bootstrap hosted service to register itself and seed its schedule. The first of
these was inserted at index 0 of the hosted-services list, because otherwise the host's first tick raced the seeding.

## 2. Escalation and owner decisions (2026-09-14)

| # | Question | Decision |
|---|---|---|
| Q1 (task's `<escalation>`) | Lease store **configured but unavailable** at tick time | Retry the acquire with the job retry backoff (~15 s), then **do not dispatch**. Record the tick **failed**, not skipped — every instance loses the store together, so nobody ran it — and log an error. Recorded as **ADR-036 A1.1** |
| Q2 (task 100) | Expiry reminders fire on exact days — catch up a missed one? | **Catch up**: send the most urgent unsent threshold whose day has passed, once per threshold. Folded into task 100 fix 8 |
| Q3 (carried from 102) | 8 not-started POMLs still carry the flat Functions ban | **Fix + widen the drift guard** — done by a sub-agent in an isolated worktree, cherry-picked as `0cf6f0c92` (9 POMLs; guard scans not-started POMLs of active projects; ArchTests 316) |
| Q4 (carried from 102) | Keep ADR-052's "a Function MUST NOT impersonate a Dataverse user"? | The owner asked for "the correct approach that allows the functions to work". Researched, then drafted as a proposal: [`decisions/function-impersonation-proposal.md`](decisions/function-impersonation-proposal.md) — ✅ **accepted by the owner 2026-09-15**: applied to ADR-052 §5/§6 (concise + full), ADR-028 A5 and the ArchTest; prerequisites filed as #988 (Service Bus still accepts SAS), #989 (typed requester field), #990 (fail-closed helper) |

## 3. Design

- **`IScheduledJobLease`** (Spaarke.Scheduling), which the host takes around **every** dispatch, scheduled or manual:
  - **Redis implementation** (`RedisScheduledJobLease`, BFF): `SET NX PX` via `LockTakeAsync`; only the holder can
    extend or release it (`LockExtendAsync` / `LockReleaseAsync`).
  - **Process-local implementation** (`ProcessLocalScheduledJobLease`): used when Redis is off, which is only
    Development and Testing, because `CacheModule` refuses to start a deployed environment without Redis. The host
    dispatches and warns once.
- **Occurrence marker — beyond the POML.** A lease released at the end of a run cannot, on its own, give one
  dispatch per schedule. A short run finishes and releases, then a slower instance wakes for the same occurrence and
  runs it again. So the lease also records the **last dispatched occurrence** and refuses any occurrence earlier than
  or equal to it. The Azure Functions timer trigger uses the same device (its "Last" schedule status).
- **Lifetime (`ScheduledJobLeaseHold`).** The lease is renewed every third of `LeaseDuration` (2 min), so it spans
  every retry and backoff, and the duration bounds only how long a **dead** holder blocks the job.
  - **Renewal refused:** re-take the lease if it is free; otherwise cancel the run, because another holder has it.
  - **Store unreachable during renewal:** keep running while the lease is still live; once renewal has failed for a
    full `LeaseDuration`, cancel the run — the lease has expired, and only this instance may have lost Redis (Step 9.5
    M2).
  - **Run too long:** past `MaxRunDuration` (2 h) the run is cancelled and renewal stops, so a hung run blocks the job
    for at most that plus one lease duration (M3). Host shutdown cancels manual runs too (M4).
  - **End of run:** the lease is released **before** the completion record is written, so "completed" means the job
    is free.
- **Outcomes.**
  - **Not granted:** the tick is recorded `Skipped` (`JobRunResult.Skipped`, status "Skipped"), not failed.
  - **Store down after the retries:** recorded failed (A1.1).
  - **Manual trigger while the job runs:** HTTP 409 (`ScheduledJobBusyException`). A store that is down gives 503.
- **Slot guard.** `ScheduledJobHostOptions.RunScheduledJobs` reads `Scheduling:RunScheduledJobs`; when it is false,
  `ExecuteAsync` logs and returns. `scripts/Deploy-BffApi.ps1 -UseSlotDeploy` sets `Scheduling__RunScheduledJobs=false`
  as a **slot setting** (`--slot-settings`), so it never swaps into production. After Step 9.5 (H1) the production
  workflow `.github/workflows/deploy-bff-api.yml` sets it too, and `deployment-slot.bicep` (no callers) lists it as
  sticky with `false` on the slot. The L2 H9 provisioning deploy does not yet — **#987**. The active slot Bicep
  (`app-service-slot.bicep`) was deliberately not changed: it sets no app settings, and declaring slot `appSettings`
  there would replace **all** of the slot's settings on every deploy.
- **Registration.** `services.AddScheduledJob<TJob>(cron, enabled)` adds a `ScheduledJobRegistration`.
  `ScheduledJobRegistry` and `InMemoryBackgroundJobStore` read every registration in their DI constructors. So there
  is **no bootstrap hosted service at all** — stronger than the POML's "one shared bootstrap", and it removes the
  start-order dependency and the `Insert(0)` hack. An unparseable cron fails at startup; a duplicate JobId fails when
  the registry is built.
- **Host-neutrality kept.** `IScheduledJob` is unchanged. The lease and registration types joined the ArchTest's
  `SchedulerHostTypes`, so no job can depend on them.

### POML premise gaps (the project tally was 13)

- **14.** "`IConnectionMultiplexer` is registered when Redis is enabled" — it is **always** registered: a
  Null-Object `NullConnectionMultiplexer` (every command throws) when Redis is off. So the lease must be chosen from
  `RedisOptions.Enabled`, not from the multiplexer being present.
- **15.** The acceptance criterion "two hosts … dispatch the job once" reads as if a lease alone delivers it. It
  does not for short runs — hence the occurrence marker above.

## 4. Placement Justification (root CLAUDE.md §10; `bff-extensions.md` §A)

- **Host = the BFF** (ADR-052): the three jobs are low volume, share the BFF's identity and release cadence (B3), and
  use BFF domain code (B2).
  - **F3** ("one dispatch per schedule without building coordination") was the signal favouring a Functions timer.
    Its cost here is small: a lease on the Redis every stamp already runs.
  - A Functions app per stamp would add a deployable, host storage, provisioning and CI/CD.
  - **Tie-breaker, fewer moving parts:** the BFF.
- **Packages:**
  - production: none new (StackExchange.Redis was already a transitive dependency of the BFF);
  - test-only: `Microsoft.Extensions.DependencyInjection` 10.0.11 in `Spaarke.Scheduling.Tests`, so the helper's
    DI-constructor selection can be proven.
- **Endpoints**: none new. `POST /api/admin/jobs/{jobId}/trigger` gained 409 and 503.
- **DI**:
  - **+** `IScheduledJobLease`: a factory, unconditional and symmetric (Redis or process-local), per ADR-032 / §F.1.
  - **−** three bootstrap hosted services, and three forwarded `IScheduledJob` registrations that nothing consumed.
- **Publish size / CVE**: §6.

### Component justification (root CLAUDE.md §11)

| New surface | Existing it overlaps | Why not extend it | Cost of doing nothing |
|---|---|---|---|
| `IScheduledJobLease` + `RedisScheduledJobLease` | `IIdempotencyService` lock | Check-then-set and fails open (#984), with no holder token and no renewal; changing its semantics changes every consumer | Duplicate hourly notifications on any multi-instance stamp |
| `ProcessLocalScheduledJobLease` | — | It is the host's default. Without it, every dispatch path needs a null check, and dev loses the manual/scheduled exclusion | Host null-branches; overlapping runs in dev |
| `ScheduledJobLeaseHold` | — | Renewal + loss + release is one lifetime; inlined, it would be duplicated across the scheduled and manual paths of an already large host | Duplicated renewal logic |
| `ScheduledJobRegistration` + `AddScheduledJob` | Three per-job bootstrap classes | It replaces them (ADR-036 A1-6 MUST) | Start-order race; copy-paste per job |

## 5. Files

- **Library** (`src/server/shared/Spaarke.Scheduling/`):
  - new: `IScheduledJobLease.cs`, `ProcessLocalScheduledJobLease.cs`, `ScheduledJobLeaseHold.cs`,
    `ScheduledJobRegistration.cs`;
  - changed: `ScheduledJobHost.cs` (rewrite), `ScheduledJobRegistry.cs`, `InMemoryBackgroundJobStore.cs`,
    `JobRunResult.cs`, `ScheduledJobHostOptions.cs`.
- **BFF**: new `Infrastructure/Scheduling/RedisScheduledJobLease.cs`; `Infrastructure/DI/SchedulingModule.cs`
  (rewrite); `MembershipModule.cs` and `ExternalAccessModule.cs` (bootstraps deleted); `Api/Admin/JobsEndpoints.cs`.
- **Script**: `scripts/Deploy-BffApi.ps1`.
- **Tests**:
  - new: `Spaarke.Scheduling.Tests/{ScheduledJobLeaseTests (12), AddScheduledJobTests (3), FakeDistributedLease,
    ListLogger}`, `Sprk.Bff.Api.Tests/Infrastructure/Scheduling/RedisScheduledJobLeaseTests (8)`;
  - a 409 test in `Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests`;
  - ArchTest rule 2b plus controls in `WorkloadPlacementGuardTests`;
  - two back-to-back-trigger tests now wait for the first run to finish.
- **Docs**: ADR-036 (concise + full: §5 implemented, A1.1), `.claude/patterns/api/scheduled-jobs.md`,
  `.claude/constraints/{jobs,bff-extensions}.md`, `docs/guides/BACKGROUND-JOBS-ADMIN-GUIDE.md`,
  `docs/architecture/spaarke-scheduling-architecture.md`, `.claude/CHANGELOG.md`.
- **Task 100 carry-over.** Fix 7 is **done** here: the job is registered on the helper, its nested bootstrap is
  deleted, and its doc comment no longer claims operators can pause it durably.

## 6. Verification

| Check | Result |
|---|---|
| Spaarke.Scheduling.Tests | **71/71** at `a9c538c72`; **74/74 three runs in a row** after the Step 9.5 fixes (`0f1414c37`) — two host tests that raced on which host won a later tick were rewritten to assert on the lease's grant times |
| Sprk.Bff.Api.Tests — lease + admin jobs (filtered) | **41/41** (at both commits) |
| Integration — admin jobs (filtered) | **18/18** |
| ArchTests | **319/319** (drift guard green over the doc edits) |
| Full suite (`Spaarke.sln`, at `0f1414c37`) | **14,540 passed / 0 failed / 86 skipped** across 8 test assemblies |
| Publish size | master `e0a6f87c4` **45.35 MB** vs task `a9c538c72` **45.43 MB** — **214 files each side** (Compress-Archive Optimal; fresh short-path worktrees `C:\wt103m`, `C:\wt103b`). The +0.08 MB is project-cumulative: the branch also carries tasks 096–102 and task 100's job, which measured 45.42 MB at `e7bd02189`, so task 103's own increment is ≈ **+0.01 MB**. Far under the 60 MB ceiling |
| CVE (`--vulnerable --include-transitive`) | **No vulnerable packages** — `Sprk.Bff.Api` and `Spaarke.Scheduling.Tests` (the test-only DI package; ADR-check W11) |
| Perturbations | PENDING (§6.1) |

### 6.1 Perturbations (`/tmp/p103.sh` — each applied, confirmed to have changed the file, tested, restored)

| # | Perturbation | Result |
|---|---|---|
| P1 | Redis acquire as check-then-set (`StringGet` + `StringSet`) instead of `LockTakeAsync` | ✅ caught — 5 `RedisScheduledJobLeaseTests` fail |
| P2 | Lease removed (every host gets its own process-local lease) | ⚠️ **first run INVALID** (`CS0006` metadata-not-found from a concurrent build — not a result); re-run in §6.2 |
| P3 | Store-down tick recorded as skipped | ✅ caught — `LeaseStoreUnavailable_…` |
| P4 | Slot guard ignored | ✅ caught — `SlotGuard_…` (+1) |
| P5 | Lease never renewed in time | ✅ caught — `LeaseSpansEveryRetry…`, `LeaseTakenByAnotherHolder…` |
| P6 | Occurrence not passed to the lease | ✅ caught — `OccurrenceAnotherInstanceAlreadyDispatched…` (+1) |
| P7 | Registry ignores registrations | ✅ caught — both `AddScheduledJobTests` DI tests |
| P8 | Manual trigger ignores a held lease | ✅ caught — `ManualTrigger_WhileAScheduledRunIsInFlight_IsRefusedAsBusy` |

### 6.2 After the Step 9.5 fixes — all eight re-run against `0f1414c37`, nothing else building

| # | Result |
|---|---|
| P1 | ✅ caught — 6 `RedisScheduledJobLeaseTests` fail (now including the post-take-failure test) |
| P2 | ✅ **caught — valid this time**: 6 host tests fail, including `TwoHostsSharingALease_…`, `LeaseSpansEveryRetryAndBackoff_…` and `LeaseStoreUnavailable_…` |
| P3 | ✅ caught — `LeaseStoreUnavailable_…` |
| P4 | ✅ caught — `SlotGuard_…` |
| P5 | ✅ caught — 5 tests, including the new re-take and hung-run tests |
| P6 | ✅ caught — `OccurrenceAnotherInstanceAlreadyDispatched_…` |
| P7 | ✅ caught — both `AddScheduledJobTests` DI tests |
| P8 | ✅ caught — `ManualTrigger_WhileAScheduledRunIsInFlight_IsRefusedAsBusy` |

The tree was clean after the run (every perturbation restored). ArchTests **319/319** against `0f1414c37`.

## 7. Step 9.5 quality gates

Both gates ran as sub-agents that read the **commit** via `git show` (the working tree was being perturbed).

### adr-check — 12 compliant · 13 warnings · 4 violations (ADR-036 A1-1/A1.1/2/6/7 met in code)

| Finding | Path | Resolution |
|---|---|---|
| **V1** No Placement Justification | C | This note (§4) — it did not exist at `a9c538c72` |
| **V2** Scheduler Redis keys not on the ADR-009 system-key allow-list | C | `SystemCacheKeys.SchedulerLease` + `SchedulerLastFire` (14 of 20) |
| **V3** New tests outside the KEEP paths (`tests/unit/Spaarke.Scheduling.Tests`, `tests/unit/Sprk.Bff.Api.Tests/...`) | **A** — **ratified by the owner 2026-09-15** | The POML names `tests/unit/Spaarke.Scheduling.Tests` as the output. The 58 existing scheduler tests and the admin-jobs tests already live there, and splitting the lease tests from the host tests they extend would scatter one component's tests over three projects. Recorded for owner visibility |
| **V4** `Stopwatch`/`Task.Delay` via the existing `WaitUntilAsync` helper in two new call sites | **A** — **ratified by the owner 2026-09-15** | The helper waits for a background `Task.Run` to finish writing its record; nothing asserts on elapsed time. The new lease tests themselves use `FakeTimeProvider` |
| W1 residual overlap window | fixed | Renewal failure for a full lease duration now cancels the run; the remaining window is documented (full ADR §5) |
| W5–W7 stale / lagging docs | fixed | ADR-036 rules 6–7, the ADR-010 row, ADR-052 concise, the ArchTest message, the architecture doc |
| W9 hot-path comment | fixed | `design.md` |
| W10 B3 container assertion; B7/B9 wrapper tests | fixed | Assertion removed; `Renew_`/`Release_` tests replaced by post-take-failure and bad-marker tests |
| W11 test-only package CVE | done | No vulnerable packages |
| W3 ProblemDetails media type · W4 Options pattern · W12 rule 2b scans the BFF only · W13 commit scope | accepted | They match the file's existing pattern, or are noted |

### code-review — sound design; 1 High, 7 Medium, 14 Low

| Finding | Resolution |
|---|---|
| **H1** Only one of three slot-deploy paths set the guard | **Fixed**: `.github/workflows/deploy-bff-api.yml` now sets it before its staging deploy, and `deployment-slot.bicep` lists it as sticky. **Filed #987** for the L2 H9 provisioning handler (another project's code) |
| M2 A holder that cannot renew ran past its lease's expiry | **Fixed**: cancelled once renewal has failed for a full lease duration; test `HolderThatCannotRenew_…` |
| M3 A hung run held the lease forever | **Fixed**: `ScheduledJobHostOptions.MaxRunDuration` (2 h) cancels the run and stops renewal; test `HungRunPastMaxRunDuration_…` |
| M4 Host shutdown did not cancel manual runs, so a lease outlived the drain | **Fixed**: manual runs are linked to a host-stopping token; test `ManualRun_HostShutdown_…` |
| M5 A bad membership cron in config now fails startup | **Kept, deliberately** — a deployment error surfaces at the slot health check, as `CacheModule` does for Redis; commented in `MembershipModule` |
| M6 Two BFF deployments sharing one Redis and `InstanceName` would suppress each other | **Documented** on `RedisScheduledJobLease` (one scheduler fleet per key prefix); no such topology today |
| M7 The swap is safe only while the sticky flag survives | Mitigated: `deployment-slot.bicep` lists it; Bicep remains otherwise unchanged (§3) |
| M8 `acquiredAt` read on the test thread | **Fixed**: the fake records each grant's virtual time |
| L9 Lease leaked on a post-take failure; `(long)` cast on a garbage marker | **Fixed** (release on any failure; `TryParse`) + tests |
| L11 Dispatch is at-most-once per occurrence | Documented (full ADR §5) |
| L12 `Cancel()` callback exceptions mislogged | **Fixed** (`Lose()` wraps it) |
| L13 Manual runs got the raw `LostToken` | Fixed by M4 (linked source) |
| L17 Status docs omitted "Skipped" | **Fixed** (`IBackgroundJobStore`, `JobRunDetail`, `JobStatusSummary`) |
| L20 Missing tests (re-take branch, distributed-lease negative warning); stray `<summary>` | **Fixed** |
| L14–L16, L18, L19, L22 | Accepted / noted: `TryAdd` semantics; eager job construction; `Skipped` public; a guarded slot still shows `NextScheduledOn`; malformed guard value fails startup (fail-fast); commit scope noted |
