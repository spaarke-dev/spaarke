# Current Task — Spaarke Platform Foundations (R3)

> **Project**: `spaarke-platform-foundations-r3`
> **Last Updated**: 2026-06-20

---

## Active Task

**Status**: none (task 031 complete 2026-06-21)

**Next Task**: per TASK-INDEX next 🔲 — likely group **E** (015, 016 — `sprk_backgroundjob*` entities) or remaining **G** sibling task 030, or **H** orchestration tasks 033/034 (now unblocked by 030+031+032).

**To start**:
- Say "work on task <NNN>" or "continue"

---

## Task State (when active)

(populated by `task-execute` when a task starts)

- **Task ID**: —
- **Title**: —
- **Started**: —
- **Rigor Level**: —
- **Step**: —
- **Files Modified**: —
- **Decisions Made**: —
- **Blockers**: —

---

## Recently Completed (this session)

- **2026-06-21 — Task 031**: `IdentityNormalizationService` — systemuser → 6-path
  PersonIdentity (contactId via AAD-oid cross-ref per ADR-028, primary email, teamIds via
  teammembership, businessUnitId, accountId via contact.parentcustomerid, organizationIds
  via task-032 resolver seam). Redis 10-min TTL per ADR-009.
  - Files (new, src): `Services/Ai/Membership/IIdentityNormalizationService.cs`,
    `IdentityNormalizationService.cs`, `IIdentityOrganizationResolver.cs` (coordination
    seam consumed by IdentityNormalizationService; task 032's `OrganizationMembershipResolver`
    implements both this seam AND the canonical `IOrganizationMembershipResolver`).
  - Files (new, tests): `IdentityNormalizationServiceTests.cs` (10 tests — happy path 6-way,
    user-without-contact, contact-without-account, multi-team, system-user-query-failure
    isolation, org-resolver-failure isolation, no-resolvers empty list, cache-hit verification
    via FakeDistributedCache call-count probes, cancelled-token, empty-Guid guard).
  - Files (modified, src): `Services/Ai/Membership/Models/PersonIdentity.cs` (extended task
    032's SystemUserId-only placeholder to the full 7-field record; positional-with-defaults
    preserves backward-compat constructor); `Infrastructure/DI/MembershipModule.cs` (added
    `IIdentityNormalizationService` singleton registration).
  - **10 / 10 tests pass** (full Membership-namespace regression: **24/24** pass — task 012
    MembershipOptions + task 032 OrganizationResolver still green after PersonIdentity shape
    extension).
  - BFF build: 0 errors, 0 new warnings (16 pre-existing warnings unchanged).
  - Publish size: **46.16 MB** compressed (delta **+0.02 MB** vs 46.14 baseline; no new
    NuGet packages — uses existing `IDataverseService` + `IDistributedCache`).
  - CVE check: no new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions
    1.21.2` HIGH finding, tracked at project level — not introduced by 031).
  - **Design decisions**:
    - **Dataverse abstraction**: injected `IDataverseService` (composite, includes
      `IGenericEntityService` → `RetrieveAsync` + `RetrieveMultipleAsync(QueryExpression)`).
      No direct `IOrganizationService` use per task brief.
    - **Coordination with task 032**: defined `IIdentityOrganizationResolver` in this
      task (031). Task 032's `OrganizationMembershipResolver` implements both the canonical
      `IOrganizationMembershipResolver` (PersonIdentity-aware) AND the
      `IIdentityOrganizationResolver` (this task's seam). Module registers the concrete +
      both interfaces. `IdentityNormalizationService` consumes
      `IEnumerable<IIdentityOrganizationResolver>` so zero registered resolvers is acceptable
      → empty `OrganizationIds` (verified by dedicated test).
    - **Failure isolation per path**: each of the 6 identity paths is wrapped in try/catch.
      Cancellation re-thrown explicitly (not swallowed). Cache read/write failures fail-open
      → re-resolve from Dataverse (verified by FakeDistributedCache test). Other failures
      log Warning + null/empty for that field while letting other paths complete (verified
      by `SystemUserQueryThrows_OtherPathsStillResolve` test).
    - **Cache contract**: `IDistributedCache` with key `membership:identity:{systemUserId:D}`,
      `DistributedCacheEntryOptions.AbsoluteExpirationRelativeToNow = 10min`. Namespace
      prefix aligns with Phase 2 invalidation channel (FR-2P2.8 — task 086).
    - **PersonIdentity shape**: positional-with-defaults preserves task 032's placeholder
      constructor `new PersonIdentity(systemUserId)` so 032's resolver tests don't break;
      init-only collection overrides ensure consumers never observe null collections.
  - **AC-1A.4 integration test**: SKIPPED per task brief (deferred to P4 wrap-up manual UAT
    against spaarkedev1; needs real AAD object ids + multi-team test user).
  - adr-check (self): PASS — ADR-001 (in-process singleton, no new background process),
    ADR-009 (Redis 10-min TTL exactly per constraint), ADR-010 (interface as testing seam,
    concrete also resolvable), ADR-013 (lives under Services/Ai/Membership/),
    ADR-024 (per-path failure isolation pattern), ADR-028 (cross-ref via
    `azureactivedirectoryobjectid` equality query), ADR-032 N/A (service is unconditional).
  - bff-extensions §A/§F: PASS — no BFF csproj changes, no new conditional DI, no new HIGH
    CVE, 10 unit tests cover new behavior, asymmetric-registration §F.1 N/A (unconditional
    registration; future endpoints in task 035 must also be unconditional).
  - Not committed per task brief.
  - Notes: `notes/bff-publish-size-task031.md` (publish-size measurement + pre-merge checklist).

- **2026-06-21 — Task 014**: Retry/backoff + idempotency in `ScheduledJobHost`.
  - Files (new, src): `Spaarke.Scheduling/JobRetryPolicy.cs`.
  - Files (new, tests): `Spaarke.Scheduling.Tests/JobRetryPolicyTests.cs` (+7 tests),
    `RetryAndIdempotencyTests.cs` (+6 tests — transient retry, exhaustion, max-attempts
    cap-per-tick, cancellation interrupts retry-loop sleep, idempotency dedup via
    pre-seeded prior run, distinct ticks still execute).
  - Files (modified, src): `IBackgroundJobStore.cs` (added `scheduledFireUtc` param to
    `RecordRunStartAsync`; added `HasRunForScheduledTimeAsync`); `InMemoryBackgroundJobStore.cs`
    (matching impl + `SeedRunRecord` test surface + `ScheduledFireUtc` field on `RunRecord`);
    `ScheduledJobHostOptions.cs` (`RetryPolicy` property defaulting to `new JobRetryPolicy()`);
    `ScheduledJobHost.cs` (`DispatchAndAdvance` snapshots `scheduledFireUtc` BEFORE
    `AdvanceNextFire`; `RunJobAsync` idempotency probe via `HasRunForScheduledTimeAsync`
    BEFORE recording start; new `ExecuteWithRetryAsync` wraps `IScheduledJob.ExecuteAsync`
    in retry loop honoring cancellation throughout).
  - Files (modified, tests): `InMemoryBackgroundJobStoreTests.cs` (updated signature; +3
    new tests for `HasRunForScheduledTimeAsync`).
  - Files (modified, project mgmt): `tasks/014-...poml` status -> completed;
    `TASK-INDEX.md` row 014 -> ✅; `notes/bff-publish-size-task014.md` (new); this file.
  - **42 tests pass** in Spaarke.Scheduling.Tests (delta +17 from 25); solution build 0
    errors / 16 pre-existing BFF warnings (none from `Spaarke.Scheduling` which enforces
    TreatWarningsAsErrors).
  - Publish size 46.16 MB compressed (delta +0.02 MB / +16 KB vs 46.14 baseline; no new
    NuGet packages — JobRetryPolicy is in-house POCO, intentionally NOT Polly).
  - No new HIGH CVE (single pre-existing `Microsoft.Kiota.Abstractions` advisory unchanged).
  - **Design decisions**:
    - **Idempotency mechanism**: extended `IBackgroundJobStore` with explicit
      `HasRunForScheduledTimeAsync(jobId, scheduledFireUtc, ct)` probe + added
      `scheduledFireUtc` parameter on `RecordRunStartAsync`. Cleanest minimal-surface
      extension; tasks 015/016 (Dataverse entities) will add a corresponding
      `sprk_backgroundjobrun.sprk_scheduledfireon` column to back this probe.
    - **Retry policy default**: 3 attempts, 5s base delay, 2 min cap; exponential
      `BaseDelay * 2^(attempt-1)`. In-house POCO (`JobRetryPolicy`), NOT Polly — Polly's
      middleware shape is overkill for in-process direct invocation, no Retry-After
      semantics needed, no jitter needed (single in-process caller per tick). Rationale
      captured in XML doc on the class.
    - **Cancellation**: explicit `IsCancellationRequested` check at top of each retry
      iteration + cancellable `Task.Delay` for inter-attempt sleep, so a cancelled host
      short-circuits without sleeping through a multi-second retry delay (verified by
      `CancellationDuringRetryLoop_StopsImmediately_DoesNotSleepThroughToken` test).
  - adr-check (self): PASS — ADR-001 (in-process), ADR-010 (`JobRetryPolicy` POCO; no
    new interface added; `IBackgroundJobStore` extension justified by ≥2 impls remaining
    valid), ADR-012 (lives in shared lib), NFR-07 (cancellation propagates through
    retry-loop sleep AND ExecuteAsync), NFR-08 (final failure record carries
    `exception.Message` as `ErrorMessage`).
  - bff-extensions §A/§F: PASS — no BFF csproj changes, no new conditional DI, no new HIGH CVE.
  - Not committed per task brief.

- **2026-06-21 — Task 013**: `ScheduledJobHost : BackgroundService` (cron dispatch + run-record write).
  - Files (new, src): `Spaarke.Scheduling/ScheduledJobHost.cs`, `ScheduledJobRegistry.cs`,
    `ScheduledJobHostOptions.cs`, `IBackgroundJobStore.cs`, `InMemoryBackgroundJobStore.cs`.
  - Files (new, tests): test project `tests/unit/Spaarke.Scheduling.Tests/` + 5 test files (registry,
    in-memory store, Cronos parsing, host lifecycle, FakeScheduledJob helper) — 25 tests pass.
  - Files (modified): `Spaarke.sln` (added test project); `tasks/013-...poml` status → completed;
    `TASK-INDEX.md` row 013 → ✅; this file.
  - Solution build PASS (0 errors); test project PASS 25/25 in 10s; publish size 46.14 MB
    (delta +0.01 MB vs 46.13 baseline). No new HIGH CVE.
  - Design choices: `IBackgroundJobStore` abstraction + `InMemoryBackgroundJobStore` stub —
    decouples task 013 from tasks 015/016 (Dataverse entities) which land in a later wave.
    `ParseCron` transparently supports both 5-field (spec.md default) and 6-field (seconds-mode)
    Cron expressions so internal high-frequency jobs + sub-second tests are first-class.
  - adr-check (self): PASS — ADR-001 ✓ (in-process BackgroundService), ADR-010 ✓ (IBackgroundJobStore
    justified by ≥2 implementations from day 1), ADR-012 ✓ (lives in shared lib), NFR-07 ✓
    (30s drain timeout, linked CT to in-flight tasks), NFR-08 ✓ (fresh correlationId per run).
  - bff-extensions §A/§F: PASS — no BFF csproj changes, no new conditional DI, no new HIGH CVE.
  - Not committed per task brief.

- **2026-06-21 — Task 002**: `joinIds` Handlebars helper registered in `TemplateEngine.cs`.
  - Files: `TemplateEngine.cs` (+76 lines), `TemplateEngineTests.cs` (+243 lines, 8 new tests).
  - 33/33 tests pass. Publish size 44.78 MB compressed (delta -1.34 MB vs 46.12 baseline; no NuGet adds).
  - No new HIGH CVE. adr-check + code-review: PASS.
  - Not committed per task brief.

---

## Project Progress

- **Phase**: P1 not started
- **Completed Tasks**: 0 / ~55-65 (final count after task generation)
- **Critical Path**: P1 (001) → P2 (010) → P3 (020) → P4 (030) → P5 (040) → P6 (050) → P6.5 (054) → P7.5 (070) → P8 (080) → P9 (090) → P10 (100) → P11 (110)
- **Earliest parallel opportunity**: Phase 1 Group B (tasks 003+004 — playbook migrations after 002 lands)

---

## Recovery After Compaction

If context was lost:
1. Read [`CLAUDE.md`](CLAUDE.md) (project context)
2. Read [`spec.md`](spec.md) (requirements + owner clarifications)
3. Read this file ([`current-task.md`](current-task.md)) for active task state
4. Read [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) for next pending task (first 🔲)
5. Invoke `task-execute` with the active or next pending task's POML

---

*Initialized 2026-06-20 by `/project-pipeline`. Updated by `task-execute` per CLAUDE.md §5 checkpointing rules.*
