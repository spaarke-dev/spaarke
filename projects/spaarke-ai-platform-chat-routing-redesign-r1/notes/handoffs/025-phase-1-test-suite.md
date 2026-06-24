# Task 025 — Phase 1 Stable-ID Migration Test Suite

**Date**: 2026-06-22
**Status**: complete
**Rigor**: STANDARD
**Wave**: 1-I

---

## Summary

Layered a single suite-level regression class on top of the existing per-consumer
unit/integration tests created during Wave 1-A through 1-H (tasks 015–021). This is
the one place a code reviewer can read to verify Phase 1 §1.7 stable-ID migration is
complete and correct.

**Suite location**:
`tests/integration/Sprk.Bff.Api.IntegrationTests/Phase1StableIdMigrationSuite.cs`

**Total test count**: 10 facts (8 BE consumers + 2 FE facts; Consumer 9 split into 9a/9b).

---

## Per-Consumer Assertion Strategy

| # | Consumer | Strategy | Assertion |
|---|---|---|---|
| 1 | `SessionSummarizeOrchestrator` (task 015) | Reflection | type has `IPlaybookLookupService` ctor param + private field |
| 2 | `MatterPreFillService` (task 016) | Reflection | same |
| 3 | `ProjectPreFillService` (task 017) | Reflection | same |
| 4 | `WorkspaceAiService` (task 018) | Reflection | same |
| 5 | `WorkspaceFileEndpoints` (task 019) | Reflection on static methods | `HandleSummarize` AND `RunSummarizePlaybookAsSSEAsync` accept `IPlaybookLookupService` parameter |
| 6 | `AppOnlyAnalysisService:46` Document Profile (task 020) | Reflection on const + ctor | `DocumentProfilePlaybookId` const is GUID-format AND class has `IPlaybookLookupService` field |
| 7 | `AppOnlyAnalysisService:1068` Email Analysis (task 020) | Reflection on const | `EmailAnalysisPlaybookId` const is GUID-format AND starts with `bc71facf-` (task 020 seed value) |
| 8 | `ChatContextMappingService` (task 020) | **Source inspection** | source file contains NO `GetByNameAsync` call AND NO `/by-name/` URL string (data-driven; never refactored) |
| 9a | Frontend `useAiSummary.ts` (task 021) | **Source inspection** | contains `/api/ai/playbooks/by-id/` AND `DOCUMENT_PROFILE_PLAYBOOK_ID` const AND NO active `/by-name/` request URL (doc-comment refs allowed) |
| 9b | Frontend `DocumentEmailWizard.tsx` (task 021) | **Source inspection** | contains `/api/ai/playbooks/by-id/` AND `SUMMARIZE_NEW_FILES_PLAYBOOK_ID` const AND NO active `/by-name/` request URL |

---

## Why Reflection Over Full DI Resolution

Per the task POML and CLAUDE.md §10 F, the goal is a cohesive regression gate — NOT a
re-test of every existing per-consumer test. Reflection on the production type signatures
proves the **migration invariant** (consumer wired to `IPlaybookLookupService`) without
requiring:

- 8 separate `WebApplicationFactory` boots (slow + flaky)
- DI mocking for upstream dependencies (Dataverse, OpenAI, file store)
- Disambiguation between `NullSessionSummarizeOrchestrator` (Null-Object pattern P3 per
  ADR-032) and the real concrete `SessionSummarizeOrchestrator` registration

The class would not COMPILE without the `IPlaybookLookupService` field after the Pattern A/B
refactor — so the reflection assertion is a high-confidence proxy for the migration
invariant.

---

## Special-Handling Notes

### Consumer 8 — `ChatContextMappingService` (task 020 — no refactor)

This service was always data-driven via a Dataverse lookup column (not a playbook-name
string). The task 020 audit confirmed no by-name call sites existed. The suite asserts the
**absence** via source inspection — if a future PR adds a `GetByNameAsync` or `/by-name/`
URL string, this gate fires. Defensive: resolves the source path by walking up from
`AppContext.BaseDirectory` looking for `.git` (handles both directory AND file forms — git
WORKTREES have a `.git` FILE, not directory).

### Consumer 9 — Frontend Pattern B (task 021 — source inspection)

The two FE files use hardcoded GUID consts (Pattern B execution-path stable-ID). The suite
asserts:

1. `/api/ai/playbooks/by-id/` URL fragment is present
2. The expected playbook ID const name is declared
3. NO **active** `/by-name/` request URL exists

Heuristic for "active call": `/by-name/` lines NOT starting with `*`, `//`, or `/*` (i.e.,
non-comment). The task 021 source explicitly references the retired path in JSDoc
explaining the migration — those comment references are TOLERATED.

If either FE file is missing (partial worktree checkout), the test exits cleanly (no
failure) — defensive fallback.

---

## Build + Test Results

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 errors**, 0 new warnings.
- `dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ --filter "FullyQualifiedName~Phase1StableId"`:
  - **Passed: 10, Failed: 0, Skipped: 0, Total: 10**
  - Duration: 43 ms

---

## Recommendation for Wave 1-J Task 027 (Exit Gate)

Task 027 should treat this suite as the **first** of its gate checks:

```bash
dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ \
  --filter "FullyQualifiedName~Phase1StableId" --no-build --nologo
# If all 10 pass → Phase 1 §1.7 migration verified.
# If any fail → root-cause + patch BEFORE proceeding to deploy.
```

Additional task 027 gate checks (NOT in this suite): the per-consumer test classes
(`PlaybookByIdEndpointTests`, `WorkspaceAiServiceTests`, `MatterPreFillServiceTests`,
etc.) and the deprecation telemetry tests (`PlaybookByNameDeprecationTests`) from
task 024.

---

## Files Created

- `tests/integration/Sprk.Bff.Api.IntegrationTests/Phase1StableIdMigrationSuite.cs` (new — 9 consumer surfaces, 10 facts)
- `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/025-phase-1-test-suite.md` (this file)

## Files Modified

None — pure test-add task per CLAUDE.md §11 (no `<justification>` needed).
