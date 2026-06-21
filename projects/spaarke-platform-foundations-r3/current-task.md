# Current Task — Spaarke Platform Foundations (R3)

> **Project**: `spaarke-platform-foundations-r3`
> **Last Updated**: 2026-06-20

---

## Active Task

**Status**: in-progress (task 025 — admin endpoints + scheduler integration tests)

---

## Task State (when active)

- **Task ID**: 025
- **Title**: Integration tests for admin endpoints + scheduler migration
- **Started**: 2026-06-21
- **Rigor Level**: FULL (BFF code, integration tests, dependencies on tasks 020-024)
- **Files Planned**:
  - tests/integration/Sprk.Bff.Api.IntegrationTests/Admin/JobsEndpointsIntegrationTests.cs (new)
  - tests/integration/Sprk.Bff.Api.IntegrationTests/Scheduling/PlaybookSchedulerJobIntegrationTests.cs (new)
- **Next Action**: Author the two integration-test classes; run them; verify regressions.

---

## Recently Completed (this session)

- **2026-06-21 — Task 023**: Migrated `PlaybookSchedulerService` → `PlaybookSchedulerJob : IScheduledJob` (FR-2.8 / D2 / Q1).
  - Files (new, src): `Services/Ai/PlaybookSchedulerJob.cs` (~470 lines; identical discovery + due-check + parallel
    user fan-out as legacy; per-child fresh correlationId; children recorded in `JobRunResult.ResultJson`).
  - Files (new, tests): `tests/.../Services/Ai/PlaybookSchedulerJobTests.cs` (27 tests),
    `tests/.../Services/InlineNotificationIntegrationPointsTests.cs` (5 tests — relocated from the deleted
    `PlaybookSchedulerServiceTests` "Inline Notification Integration Points" region).
  - Files (modified, src): `Spaarke.Scheduling/JobRunResult.cs` (+1 optional positional record param `ResultJson`);
    `Spaarke.Scheduling/IBackgroundJobStore.cs` (+1 optional positional record param on `BackgroundJobRunRecord`);
    `Spaarke.Scheduling/InMemoryBackgroundJobStore.cs` (+1 line in `ToPublicProjection` to flow `ResultJson`);
    `Infrastructure/DI/SchedulingModule.cs` (rewritten: hosted-service forwarder for `ScheduledJobHost`, new
    internal `SchedulingBootstrapHostedService` inserted at index 0 to seed
    `notification-playbook-scheduler` BackgroundJobDefinition + register handler in
    `ScheduledJobRegistry` before the cron loop's first tick); `Infrastructure/DI/AnalysisServicesModule.cs`
    (removed `AddHostedService<PlaybookSchedulerService>` line + added pointer comment to SchedulingModule).
  - Files (DELETED): `src/server/api/Sprk.Bff.Api/Services/PlaybookSchedulerService.cs` (~487 lines);
    `tests/unit/Sprk.Bff.Api.Tests/Services/PlaybookSchedulerServiceTests.cs` (split into two new test files).
  - **Tests**: 32 new pass (27 PlaybookSchedulerJob + 5 InlineNotification); Spaarke.Scheduling regression
    **57/57 pass** (zero impact from optional `ResultJson` additions); full BFF unit suite 7458 pass /
    110 skipped / 0 failed (no regressions).
  - BFF build: 0 errors, 16 pre-existing warnings unchanged. Spaarke.Scheduling TreatWarningsAsErrors honored.
  - Publish size: **46.20 MB** compressed (delta **+0.01 MB** vs 46.19 baseline; well under +1 MB ceiling).
  - CVE check: no new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions 1.21.2` HIGH).
  - **Design decisions**:
    - **JobId = "notification-playbook-scheduler"**: per D2 / FR-2.8; constant exposed as
      `PlaybookSchedulerJob.JobIdConstant` for cross-module reuse.
    - **Cron = `0 * * * *`** (every hour at minute 0): exactly matches the legacy
      `DefaultTickInterval = TimeSpan.FromHours(1)`. NFR-04 (preserve cadence) verified by inspection;
      the per-playbook elapsed-time due-check inside `IsPlaybookDue` is the final gate.
    - **PlaybookSchedulerService disposition**: REMOVED entirely (not an adapter). PlaybookSchedulerJob
      is the canonical replacement; cleaner architecture per task brief recommendation. Legacy class +
      its test file deleted; useful test coverage (inline notification integration points) relocated to
      a dedicated `InlineNotificationIntegrationPointsTests` file (5 tests preserved verbatim).
    - **Hosted-service registration**: `services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>())`
      pattern preserves the singleton identity so admin trigger (task 021) and the cron loop share
      `_inFlight` state. Task 021 had pre-marked this as the task-023 follow-up; verified via XML
      doc + the existing `JobsEndpointsTests` (admin tests still 17/17 pass).
    - **Seed mechanism**: new internal `SchedulingBootstrapHostedService` runs at startup (inserted at
      index 0 of hosted services via `services.Insert(0, ...)`) and (a) registers PlaybookSchedulerJob
      with `ScheduledJobRegistry` and (b) seeds the `notification-playbook-scheduler`
      BackgroundJobDefinition in `InMemoryBackgroundJobStore`. Idempotent on host restart. When task
      023+ swaps in Dataverse-backed store, the seed moves to a one-shot Dataverse upsert here.
    - **Children correlationId format**: `Guid.NewGuid().ToString("N")` (no dashes) — matches
      `ScheduledJobHost.DispatchAndAdvance` convention so admin tooling sees a consistent shape.
    - **ResultJson shape**: `{"Children": [{"PlaybookId": "...", "PlaybookName": "...", "CorrelationId": "...",
      "Status": "Succeeded|PartialFailure|Failed|Skipped|Cancelled", "UserCount": N, "SuccessCount": N,
      "FailureCount": N, "ErrorMessage": "..."}]}`. STJ default PascalCase keys (no naming policy override).
      Per-user errors NOT surfaced individually — kept in logs to bound payload size; per-playbook
      `ErrorMessage` only set on Status=Failed/Cancelled.
    - **`Success=true` even with per-playbook failures**: Children entries with Status=Failed/PartialFailure
      surface in `ResultJson`. Only an unhandled exception ABOVE the per-playbook try/catch turns the
      whole run into a failure (preserves legacy "continue with next playbook" semantics).
    - **Coordination with task 024** (`sprk_analysisplaybook` config migration): `ParseScheduleConfig`
      and `ScheduleConfig` record are flagged in XML docs as the stable surface task 024 must preserve
      — task 024 may replace the JSON parse with a column read, but `IsPlaybookDue(lastRun, schedule)`
      semantics + return shape MUST remain identical.
    - **JobRunResult / BackgroundJobRunRecord extensions**: added `ResultJson` as an OPTIONAL positional
      record parameter with default `null`. All 17 existing call sites continue to compile + run unchanged
      (positional records accept default-value parameters). Verified: Spaarke.Scheduling regression
      57/57 pass after the change.
    - **`PersistLastRunTimestampAsync` failure is non-fatal**: matches legacy semantics — next tick
      re-reads (still-stale) `sprk_lastrundate` and re-dispatches. Verified by
      `ExecuteAsync_ContinuesGracefully_WhenLastRunPersistenceFails`.
    - **Read `sprk_lastrundate` per-tick instead of in-memory dictionary**: the legacy service held a
      `ConcurrentDictionary<Guid, DateTimeOffset>` seeded once at startup. PlaybookSchedulerJob re-reads
      from Dataverse on every tick — small perf cost (1 query / ~7 rows / 1h cadence) but eliminates
      restart-window gaps where in-memory state diverged from the canonical row. Dataverse remains
      the source of truth.
  - **adr-check (self)**: PASS — ADR-001 (in-process via ScheduledJobHost; no external scheduler),
    ADR-010 (concrete singleton; IScheduledJob is framework seam; ResultJson additions are POCO record
    params), ADR-013 (lives under Services/Ai/), ADR-029 (publish-size +0.01 MB), ADR-032 N/A
    (handler unconditional). Spaarke.Scheduling TreatWarningsAsErrors honored.
  - **bff-extensions §A/§F/§F.1**: PASS — Placement Justification ✓ (PlaybookSchedulerJob lives next
    to AI deps); ADRs cited ✓; publish-size ✓; no new HIGH CVE ✓; test update obligation ✓ (+32 new);
    asymmetric-registration §F.1 ✓ (handler + host BOTH unconditional; static-scan: no new
    `if (flag) { … }` block); BFF csproj unchanged ✓.
  - **AC verification** (per task brief): AC ✓ PlaybookSchedulerJob registered as IScheduledJob;
    AC ✓ visible to GET /api/admin/jobs as "notification-playbook-scheduler" (via SchedulingBootstrap
    seed); AC ✓ all 7 active playbooks fan out (validated by 7-playbook cardinality test); AC ✓ each
    child gets its own correlationId (validated by unique-distinct + child≠parent tests); AC ✓ parent
    sprk_resultjson records children correlationIds (validated by ResultJson-records test); AC ✓ old
    PlaybookSchedulerService BackgroundService REMOVED (file deleted).
  - Not committed per task brief.
  - Notes: `notes/bff-publish-size-task023.md` (publish-size + pre-merge checklist).

- **2026-06-21 — Task 021**: `POST /api/admin/jobs/{jobId}/trigger` admin manual-trigger endpoint.
  - Files (new, src): `Spaarke.Scheduling/JobNotFoundException.cs` (JobNotFoundException +
    public `TriggerResult` record), `Api/Admin/Models/TriggerResponse.cs` (BFF-side wire DTO).
  - Files (new, tests): none (extended existing ScheduledJobHostTests.cs +5 tests; extended
    existing JobsEndpointsTests.cs +6 tests).
  - Files (modified, src):
    - `Spaarke.Scheduling/ScheduledJobHost.cs` — added public `TriggerNowAsync(jobId, parameters, ct)`;
      private `RunManualTriggerAsync` background-task body; private `ExecuteHandlerWithRetryAsync`
      (manual-trigger variant of scheduled retry envelope — no ScheduledJobState dep); private
      `BuildManualTriggerParameters` (merges definition.ConfigJson + caller overrides).
    - `Api/Admin/JobsEndpoints.cs` — `MapPost("/{jobId}/trigger", TriggerJobAsync)` inside the
      task-021 reserved comment block; private `TriggerJobAsync` handler (202 + Location header,
      404 via JobNotFoundException catch, 499 for client-cancellation). Did NOT modify task 020's
      GET handlers or shared helpers.
    - `Infrastructure/DI/SchedulingModule.cs` — registered `ScheduledJobHostOptions` (defaults POCO,
      spec-verbatim) + `ScheduledJobHost` as **singleton** (NOT HostedService) so admin trigger
      endpoint can inject it for out-of-band dispatch without spinning up the cron loop. Task 023+
      will add `AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>())` when first
      production cron job lands. bff-extensions §F.1 compliance: endpoint mapped unconditionally →
      host registered unconditionally.
  - **Tests**: Spaarke.Scheduling.Tests **47/47 pass** (delta +5: TriggerNowAsync registered/unknown/
    NFR-08 distinct-correlationIds / param-override-merge / pre-cancel). BFF Admin tests **17/17
    pass** (delta +6: 401/403/404 paths + 202+body shape + manual-admin trigger persistence + NFR-08
    distinct correlationIds + RunRecord with scheduledFireUtc=null).
  - BFF build: 0 errors, 16 pre-existing warnings unchanged. `Spaarke.Scheduling` TreatWarningsAsErrors
    honored (0 warnings in scheduling project).
  - Publish size: **44.86 MB** compressed (delta **-1.32 MB** vs 46.18 baseline; no NuGet adds —
    JobNotFoundException + TriggerResult are POCOs in existing project; the decrease is consistent
    with Release-mode publish-mode optimizations across other recent low-impact tasks).
  - CVE check: no new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions 1.21.2`
    HIGH advisory tracked at project level).
  - **Design decisions**:
    - **Fire-and-track via Task.Run + CancellationToken.None for background task body**:
      admin client cancellation cancels the dispatch path only (registry resolve + run-start write),
      NOT in-flight runs. Host shutdown drain (`_inFlight` tracking + StopAsync 30s ceiling per NFR-07)
      is the correct cancel surface for in-flight runs. Verified by `TriggerNowAsync_CancellationBeforeDispatch`.
    - **JobNotFoundException location**: `Spaarke.Scheduling` (NOT BFF). Any future caller of
      `TriggerNowAsync` (CLI tools, other shared-lib consumers) catches the same type without
      depending on BFF-side code.
    - **TriggerResult (shared lib) vs TriggerResponse (BFF DTO)**: dual-record split preserves the
      single-direction dependency (BFF → shared-lib, never reverse) and lets the BFF DTO live
      alongside sibling JobStatusSummary/JobStatusDetail/JobRunDetail DTOs in Api/Admin/Models/.
      Endpoint maps between them (zero-cost positional copy).
    - **ScheduledJobHost singleton (NOT hosted service) registration in SchedulingModule**: keeps
      P3 deployments lean (no cron loop spinning when no cron jobs exist yet) while satisfying
      §F.1 asymmetric-registration: endpoint mapped unconditionally → host registered unconditionally.
      First production cron job migration (task 023 PlaybookSchedulerService) adds the
      HostedService registration; same singleton instance.
    - **scheduledFireUtc=null for manual triggers**: per `IBackgroundJobStore.RecordRunStartAsync`
      contract; no idempotency dedup applied to manual triggers (admin double-click = 2 runs by
      design — admins explicitly chose to retrigger).
    - **Retry policy applies uniformly**: same `_options.RetryPolicy` (3 attempts, 5s base, 2min cap)
      wraps manual-trigger handler invocations. Transient failures retry without admin re-trigger.
    - **Caller-supplied parameter overrides merge ON TOP of definition's persisted ConfigJson**:
      verified by `TriggerNowAsync_OverrideParameters_MergedIntoRunContext`. Overrides win on key
      conflict. Endpoint passes `parameters: null` (R3 task 021 doesn't accept request body) —
      future tasks can extend with `[FromBody] Dictionary<string, object>` parameter.
    - **202 Accepted + Location header**: canonical "where to find this resource later" path is
      `/api/admin/jobs/{jobId}/runs/{runId}` (resource-style). Existing status surface lives at
      `/{jobId}/status` (task 020); the runs/{runId} shape is the location convention admin
      clients expect for a created resource per Microsoft Learn admin API guidance.
    - **499 Client Closed Request for caller-cancellation**: not 500 (no server fault); not 408
      (no request timeout — caller chose to cancel). 499 is the canonical nginx/admin-API convention.
  - **AC verification**: AC ✅ POST returns 202 + TriggerResponse (body shape verified); AC ✅
    sprk_backgroundjobrun row written with trigger=ManualAdmin (verified via
    InMemoryBackgroundJobStore.RunRecords + `RunRecord.Trigger`); AC ✅ 404 for unknown jobId
    (JobNotFoundException → ProblemDetails 404); AC ✅ 403 non-admin + 401 unauthenticated;
    AC ✅ NFR-08 fresh correlationId per run (verified via two-trigger distinct-count test).
  - adr-check (self): PASS — ADR-001 (in-process), ADR-008 (RequireAuthorization at MapGroup,
    no global middleware), ADR-010 (singleton concrete + JobNotFoundException is POCO; interface
    only where ≥2 impls warranted), ADR-029 (publish-size measured -1.32 MB), ADR-032 N/A (host
    is unconditional). Spaarke.Scheduling TreatWarningsAsErrors honored.
  - bff-extensions §A/§C/§F/§F.1: PASS — Placement Justification ✅ (admin trigger is thin BFF
    surface over shared-lib TriggerNowAsync; trigger logic correctly placed in Spaarke.Scheduling);
    Minimal API endpoint via extension method ✅; endpoint-filter auth ✅; ProblemDetails ✅;
    Producess annotations ✅; test-update obligation ✅ (+6 BFF + +5 Spaarke.Scheduling);
    asymmetric-registration ✅ (host registered unconditionally — static scan: TriggerJobAsync
    is the only consumer; ScheduledJobRegistry + IBackgroundJobStore + ScheduledJobHostOptions
    all unconditional in same module).
  - **Coordination with task 022**: pre-marked `// ===== Task 022 =====` comment block reserved
    in JobsEndpoints.cs (3 endpoints: GET /history, POST /enable, POST /disable). Task 022 adds
    handlers in that block without touching task 020 or 021 work.
  - Not committed per task brief.

- **2026-06-21 — Task 020**: `GET /api/admin/jobs` + `GET /api/admin/jobs/{jobId}/status` admin endpoints.
  - Files (new, src): `Api/Admin/JobsEndpoints.cs` (~280 lines), `Api/Admin/Models/JobStatusSummary.cs`,
    `JobStatusDetail.cs`, `JobRunDetail.cs`, `Infrastructure/DI/SchedulingModule.cs`.
  - Files (new, tests): `tests/.../Api/Admin/JobsEndpointsTests.cs` (11 tests), `AdminJobsTestFixture.cs`
    (WebApplicationFactory + admin/non-admin/unauthenticated client builders).
  - Files (modified, src): `Spaarke.Scheduling/IBackgroundJobStore.cs` (added `GetRecentRunsAsync`
    + public `BackgroundJobRunRecord` record with canonical Status), `InMemoryBackgroundJobStore.cs`
    (impl + status canonicalization helper), `Program.cs` (+`AddSchedulingModule`),
    `EndpointMappingExtensions.cs` (+`MapAdminJobsEndpoints` call at end of MapDomainEndpoints).
  - **11/11 new BFF tests pass** (133ms); Spaarke.Scheduling regression **42/42 pass**.
  - BFF build: 0 errors, 0 new warnings (16 pre-existing in unrelated files unchanged).
  - Publish size: **46.18 MB** compressed (delta **+0.02 MB** vs 46.16 baseline; no NuGet adds —
    Cronos reused from task 010).
  - CVE check: no new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions 1.21.2`
    HIGH, tracked at project level).
  - **Design decisions**:
    - **IBackgroundJobStore extension**: added `GetRecentRunsAsync(jobId, limit, ct)` returning
      new public record `BackgroundJobRunRecord` with canonical Status string
      (Succeeded/Failed/InProgress). Public projection of internal `InMemoryBackgroundJobStore.RunRecord`
      — Dataverse-backed store (task 023+) won't need to re-canonicalize from `sprk_backgroundjobrun`
      option-set values.
    - **SchedulingModule unconditional** per bff-extensions.md §F.1: registers `ScheduledJobRegistry`
      + `InMemoryBackgroundJobStore` (as `IBackgroundJobStore` + concrete) with no feature gate.
      Endpoints map unconditionally; dependencies must too. ADR-032 N/A (real services, not
      kill-switches). When task 023+ swaps to Dataverse-backed store, this module is the single
      registration site.
    - **NextScheduledOn computation**: local `ParseCron` helper in `JobsEndpoints.cs` mirrors
      `ScheduledJobHost.ParseCron` (5-field minute-precision OR 6-field seconds). Cron-format
      failures log Warning + omit `NextScheduledOn` rather than 500 the whole admin list.
    - **Orphan-handler tolerance**: jobs registered with `ScheduledJobRegistry` but missing a
      definition in `IBackgroundJobStore` surface with `Enabled=false` + empty cron +
      `NextScheduledOn=null`. Mirrors `ScheduledJobHost`'s same tolerance; lets operators spot
      "handler registered, definition missing" misconfigs via admin UI.
    - **Authorization (Q6)**: existing `SystemAdmin` policy at `AuthorizationModule.cs:241` —
      NOT a new `PlatformAdmin` policy. Precedent: `RagEndpoints.cs` admin group.
    - **Test fixture**: dedicated `AdminJobsTestFixture` (NOT shared with `CustomWebAppFactory`
      or `WorkspaceTestFixture`) because tests need per-call admin/non-admin/unauthenticated
      selection via `X-Test-Role` header — sibling fixtures hardcode SystemAdmin which would
      mask the 403 path.
    - **Task 021/022 coordination**: pre-marked `// ===== Task 021/022 ===== //` comment blocks
      in `JobsEndpoints.cs` reserve insertion points. Tasks 021/022 add their handlers after the
      task 020 GET handlers without touching shared helpers.
    - **Test reset via reflection** on `_runs`/`_jobs` ConcurrentDictionaries (production
      Clear() intentionally absent — single-host invariant; tests need symmetric teardown given
      shared `IClassFixture`).
  - adr-check (self): PASS — ADR-001 (in-process, no Azure Function), ADR-008 (`.RequireAuthorization`
    at MapGroup, no global middleware), ADR-010 (singletons + `IBackgroundJobStore` interface
    justified by ≥2 impls), ADR-029 (publish-size measured +0.02 MB), ADR-032 N/A. Spaarke.Scheduling
    TreatWarningsAsErrors honored.
  - bff-extensions §A/§F/§F.1: PASS — Placement Justification ✅, ADRs cited ✅, publish-size ✅,
    no new CRUD→AI dep ✅, feature-module DI ✅, unconditional registration ✅.
  - **AC verification**: AC-2.5 ✅ (403 non-admin + 401 unauthenticated tests pass), GET list returns
    seeded job summaries ✅, GET detail returns last 10 runs (capped from 12 seeded, newest-first) ✅,
    404 unknown jobId ✅, AC-2.7 failed-run ErrorMessage surfaces via `RecentRuns[0]` ✅.
  - Not committed per task brief.
  - Notes: `notes/bff-publish-size-task020.md` (publish-size + pre-merge checklist).

- **2026-06-21 — Task 034**: `MembershipResponse` DTO + nested-shape PersonIdentity JSON contract.
  - Files (new, src): `Services/Ai/Membership/Models/MembershipResponse.cs` — authoritative
    response shape per design.md Part 1 §"Endpoint contract": positional record
    (EntityType, PersonIdentity, Ids, ByRole, Count, CacheExpiresAt, ContinuationToken?)
    with [property: JsonPropertyName("camelCase")] on every parameter. Self-locked JSON
    contract independent of host JsonSerializerOptions.
  - Files (new, tests): `tests/.../MembershipResponseTests.cs` — 10 tests covering
    camelCase top-level keys, camelCase nested personIdentity keys, GUID D-form,
    ISO 8601 DateTimeOffset (+00:00 suffix), explicit `continuationToken: null` emission,
    preserved empty `byRole` arrays, full roundtrip (top-level + nested), continuation-
    token-present serialization, roundtrip with token.
  - Files (modified, src): `Services/Ai/Membership/Models/PersonIdentity.cs` — added
    [JsonPropertyName] attributes on all 7 fields (5 via `[property:]` on positional
    params for SystemUserId/ContactId/PrimaryEmail/BusinessUnitId/AccountId; 2 via body
    attributes on the TeamIds + OrganizationIds init-only redeclarations). Non-breaking:
    task 031's tests pass unchanged.
  - **10 / 10 new tests pass**; full Membership-namespace regression: **48/48 pass**
    (10 task 031 + 8 task 030 + 6 task 032 + 14 task 012 + 10 new from this task).
  - BFF build: 0 errors, 0 new warnings (16 pre-existing unchanged).
  - Publish size: **46.16 MB** compressed (delta **0.00 MB** vs 46.16 baseline; pure
    record + attribute additions, no NuGet adds).
  - CVE check: no new HIGH-severity CVE (only pre-existing `Microsoft.Kiota.Abstractions
    1.21.2` HIGH finding, tracked at project level).
  - **Coordination with task 033** (parallel group H): at authoring time (2026-06-21)
    task 033 had NOT created `MembershipResponse.cs` — both still 🔲. No reconciliation
    required. Task 033 consumes this DTO as the authoritative output shape.
  - **Design decisions**:
    - **`[property: JsonPropertyName(...)]` over default camelCase policy**: locks the
      contract at the type level so the DTO serializes identically regardless of host
      `JsonSerializerOptions` configuration (relevant for test contexts and internal
      logger contexts that don't run the BFF's configured options).
    - **Explicit `continuationToken: null` emission**: design example shows
      `"continuationToken": null` explicitly. We deliberately do NOT apply
      `[JsonIgnore(WhenWritingNull)]` so clients always see the key.
    - **PersonIdentity attribute placement**: body-declared properties (TeamIds,
      OrganizationIds) carry `[JsonPropertyName]` on the body declaration; positional
      params do NOT also carry `[property:]` for those two (would duplicate / shadow).
      The 5 non-overridden positional params carry `[property:]`.
    - **GUID format**: System.Text.Json default D-form (`aaaaaaaa-aaaa-aaaa-aaaa-...`),
      no braces. Test asserts both presence + absence-of-braces.
    - **DateTimeOffset format**: System.Text.Json default ISO 8601 round-trip with `+00:00`
      suffix for UTC (NOT `Z` — STJ preserves the offset). Test locks the runtime
      behavior exactly.
  - adr-check (self): PASS — ADR-013 (lives under Services/Ai/Membership/Models/),
    ADR-029 (zero publish-size impact), TreatWarningsAsErrors honored.
  - bff-extensions §A/§F: PASS — no BFF csproj changes, no new conditional DI, no new
    HIGH CVE, 10 unit tests cover the new behavior, asymmetric-registration §F.1 N/A
    (no service registration in this task).
  - Not committed per task brief.

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
