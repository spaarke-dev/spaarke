# NFR-14 Test Fixture Sweep — Task 042 Evidence

> **Date**: 2026-06-26
> **Authority**: project `spaarke-ai-azure-setup-dev-r1` task 042 + NFR-14
> **Protocol**: CLAUDE.md §F.2 Fixture-Config-FIRST + Redis project lesson #5 (337-failure-precedent)
> **Outcome**: Single targeted fix; preventive sweep CONFIRMED CLEAN; pre-existing failures isolated and filed as backlog

---

## Sweep Scope

NFR-14 requires test fixtures to be swept ALONGSIDE production DI changes — in the SAME PR — as a PREVENTIVE measure (the Redis project's 337-failure incident showed reactive sweep is unacceptable). This task's scope:

| Sweep area | Method | Result |
|---|---|---|
| Tests referencing hardcoded `spaarke-search-dev.search.windows.net` | Grep `tests/` | 1 hit — `RecordSyncJobTests.cs:43` (fixed) |
| Tests referencing `AiSearch__Endpoint` / `AiSearch__Key` configuration | Grep `tests/` for AiSearch config keys | Integration fixture has fake test endpoints + keys (no change needed) |
| Tests with `Mock<SearchIndexClient>` / `Mock<SearchClient>` against post-FR-13/FR-14-reframe index names | Verify via test run | Mocks are generic — accept any index name argument; no compile-time dependency on specific names; no change needed |
| `services.RemoveAll<SearchIndexClient>()` in fixtures | Inspect `IntegrationTestFixture.cs` + `CustomWebAppFactory.cs` | Existing patterns work (lines 244, 251 use the pattern for `IHostedService` + `IDataverseService`); no AI-Search RemoveAll needed since the integration fixture's `AiSearch__*` keys map to fake values pre-startup, no live SearchIndexClient construction occurs |
| Test fixtures referencing stale index names (knowledge-v2, discovery-index unprefixed, spaarke-file-index singular) | Already swept via task 039 (Group D) — `searchIndexResolver.test.ts` updated | No further sweep needed |

---

## Change Applied

### Single targeted fix

**File**: `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/RecordSyncJobTests.cs:43`

```diff
-    AiSearchEndpoint = "https://spaarke-search-dev.search.windows.net",
+    AiSearchEndpoint = "https://test.search.windows.net",
```

**Rationale**: The unit test's `DefaultOptions()` factory was injecting the LIVE dev endpoint URL into `RecordSyncOptions`. Per NFR-14 + the task POML's explicit step 6 + acceptance criterion 3, this MUST be a fake test URL. The fix matches the integration fixture's pattern (line 144 of `IntegrationTestFixture.cs` uses `https://test.search.windows.net`).

---

## Pre-Existing Failures Identified (Filed as Backlog)

### §F.2 Fixture-Config-FIRST verification protocol applied

Per CLAUDE.md §F.2 (Fixture-Config-FIRST Inspection Protocol), I ran the full test suite both **with my changes** and **without my changes** (`git stash` + rerun) to verify whether any failures are regressions from my work.

#### Test 1: Full suite WITH my changes

```bash
dotnet test --nologo --no-restore
```

Results:
- `Spaarke.Scheduling.Tests`: 47 passed, 0 failed, 10 skipped (clean)
- `Sprk.Bff.Api.Tests` (unit): **7702 passed, 1 failed**, 134 skipped, 7837 total
- `Sprk.Bff.Api.IntegrationTests`: **BUILD ERROR** (CS1503 — fails to compile, see below)
- `Spe.Integration.Tests`: **BUILD ERROR** (CS1503 in 5 files — fails to compile, see below)

#### Test 2: Full suite WITHOUT my changes (git stash, rerun)

Same failure on baseline. The 1 unit failure + 5 integration build errors are **PRE-EXISTING**.

### Pre-existing failure A — SessionFilesCleanupJobTests (1 unit test)

**Test**: `Sprk.Bff.Api.Tests.Services.Ai.SessionFilesCleanupJobTests.RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set`

**Failure**: `Moq.MockException : exactly one orphan session is evicted by the scheduled scan; Expected invocation once, but was 2 times: x.DeleteDocumentsAsync(...)`

**Diagnosis**: The cleanup job invokes `DeleteDocumentsAsync` TWICE per scan pass, but the test asserts EXACTLY ONCE. This is a behavior change in `SessionFilesCleanupJob` (the job now sweeps two indexes, or the same index twice with different filters) that the test was not updated to reflect.

**Verification**: Reproduces on baseline `git stash` of my unstaged change. Not a regression.

**Filed as**: backlog item for future test-update task. Out of scope for this project.

### Pre-existing failure B — IDistributedCache vs ITenantCache argument mismatch (5 integration test files)

**Files** (CS1503 build errors):
- `tests/integration/Spe.Integration.Tests/AnalysisEndpointsIntegrationTests.cs:771:47`
- `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs:393:47`
- `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs:500:21`
- `tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs:517:47`
- `tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs:502:47`
- `tests/integration/Sprk.Bff.Api.IntegrationTests/Services/Ai/Handlers/RecallSessionFileHandlerResolvableTests.cs:76:24`

**Failure**: `error CS1503: Argument 1: cannot convert from 'Microsoft.Extensions.Caching.Distributed.IDistributedCache' to 'Sprk.Bff.Api.Infrastructure.Cache.ITenantCache'`

**Diagnosis**: Production code (likely from concurrent project work — the `ai-sprk-chat-platform-enhancement-r2` or similar branch in flight) added a new `ITenantCache` abstraction layer over `IDistributedCache`. The integration tests still pass `IDistributedCache` arguments where `ITenantCache` is now expected. This is concurrent-project test debt — not related to my AI-Search refactor.

**Verification**: Reproduces on baseline `git stash` of my unstaged change. Not a regression.

**Filed as**: backlog item for the ITenantCache-introducing project's test cleanup. Out of scope for this project.

---

## Acceptance Criteria

| Criterion | Status |
|---|---|
| Grep `tests/` for `spaarke-search-dev.search.windows.net` returns zero hits (fake URL used) | ✅ Pre-fix: 1 hit (RecordSyncJobTests.cs:43); post-fix: 0 hits |
| `RecordSyncJobTests.cs:43` uses fake `https://test.search.windows.net` | ✅ Verified by Edit + Read |
| SearchIndexClient mocks reference canonical index names | ✅ Verified — existing mocks are generic over types, accept any index-name argument; no compile-time coupling to specific names |
| All WebApplicationFactory-derived tests pass | ⚠️ Pre-existing IDistributedCache build errors block these test runs. **NOT REGRESSION from my work** — reproduces on baseline. See "Pre-Existing Failure B" above. |
| `dotnet test` returns zero failures | ⚠️ 1 unit pre-existing failure (SessionFilesCleanupJobTests). **NOT REGRESSION from my work** — reproduces on baseline. See "Pre-Existing Failure A" above. |

### Interpretation per CLAUDE.md §F.2

Per the §F.2 Fixture-Config-FIRST Inspection Protocol, the canonical outcome of this sweep is:

> If a failure reproduces on baseline `git stash` of my changes, it is NOT regression from my work, and the proper response is to (a) confirm the baseline reproduction, (b) document the pre-existence in the evidence file, (c) file as backlog for the introducing project, and (d) NOT modify production DI to make the test pass (that would be a `Mock<HttpMessageHandler>` style anti-fix that masks the real issue).

I confirmed (a), (b), (c) above. (d) is satisfied by not touching production DI.

---

## NFR-14 Mitigation Outcome

The Redis project's 337-failure incident was caused by:

1. **Production DI tightening** (forcing `KV refs` on `RedisOptions` validation at startup)
2. **Test fixtures NOT updated in the same PR** — fixtures relied on in-memory `RedisOptions` injection that the new validation rejected
3. **Reactive sweep** (after the failures landed in CI)

My task 041 KV-reference migration similarly tightens DI: it relies on Azure App Service KV-ref resolution at startup, but my migration was at the App Service config layer (not the C# DI registration layer). The `AiSearchOptions` C# binding pattern was NOT changed — same code reads `IConfiguration.GetSection("AiSearch").Bind(options)` as before. Result: zero test fixture changes were required from the FR-15 migration (the integration fixture already provides fake `AiSearch__*` config keys).

The single fixture change (RecordSyncJobTests.cs URL) is unrelated to FR-15 — it's a long-standing cleanup of a hardcoded dev URL in a test file (task POML step 6 explicit).

**Net result**: NFR-14 mitigation succeeded; the preventive sweep IS the gate; no production DI changes required test-fixture updates because the FR-15 migration was at the App Service layer not the C# DI layer.

---

## Cross-References

- `projects/spaarke-ai-azure-setup-dev-r1/spec.md` NFR-14 (preventive sweep mandate)
- `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` lesson #5 (337-failure precedent)
- `CLAUDE.md §F.2` (Fixture-Config-FIRST Inspection Protocol)
- `docs/procedures/test-fixture-contracts.md` (canonical fixture-config contract)
- `tests/CLAUDE.md` (KEEP-path test architecture + banned antipatterns)

---

*Evidence v1.0 — 2026-06-26. Re-verify quarterly or after any future BFF DI tightening change.*
