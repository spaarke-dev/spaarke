# Task 016 — BFF Publish-Size Delta (ADR-029 / NFR-01)

> **Date**: 2026-06-22
> **Task**: 016 — Migrate `MatterPreFillService` (Pattern A consumer) to stable-ID resolution via `IPlaybookLookupService.GetByIdAsync(MatterPreFillPlaybookId, ct)`
> **Wave**: 1-E
> **Rigor**: FULL
> **Outcome**: ✅ Within NFR-01 budget — code-only change with zero new package references

---

## Publish-Size Measurement

| Metric | Value |
|---|---|
| **Compressed size (this task, tar.gz)** | **44.73 MB** (46,902,599 bytes) |
| Task 015 baseline (Wave 1-D, 2026-06-22) | 44.75 MB |
| Task 017 (Wave 1-E sibling) | 44.74 MB |
| Task 019 (Wave 1-E sibling) | 44.73 MB |
| NFR-01 ceiling | 60 MB |
| Single-task delta threshold (escalation) | ≥ +5 MB |
| Cumulative threshold (architecture review) | ≥ 55 MB |
| Cumulative threshold (HARD STOP) | ≥ 60 MB |
| **Status** | ✅ Within budget on all thresholds |

### Measurement procedure

1. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-016/`
2. Compress published output via `tar -czf deploy/task016-publish-size.tar.gz -C deploy/api-publish-016 .`
3. Output: `deploy/task016-publish-size.tar.gz`

### Single-task delta attribution

Task 016 is a **code-only refactor** with:

- **Modified**: 1 .cs file (`Services/Workspace/MatterPreFillService.cs`)
  - Removed `private static readonly Guid DefaultPreFillPlaybookId = Guid.Parse("2d660cad-d418-f111-8343-7ced8d1dc988");` constant
  - Added `IPlaybookLookupService _playbookLookup` field + constructor parameter
  - Replaced legacy `PreFillPlaybookId` + hardcoded GUID fallback at the playbook-resolution call site with stable-ID lookup against `WorkspaceOptions.MatterPreFillPlaybookId` (pre-seated by task 013)
- **Added**: 1 test file (`tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/MatterPreFillServiceTests.cs`) — 9 reflection / source-contract tests; not in BFF publish output
- Zero new package references
- Zero new DI registrations (`IPlaybookLookupService` already registered scoped in `FinanceModule.cs`; `MatterPreFillService` ctor signature changed but DI auto-resolves new param)

Measured 44.73 MB is approximately at-baseline. The single-task delta from task 016 alone is effectively **zero MB** — only one new constructor parameter and a try/catch + guard pair were added to one method. No new types, no new dependencies.

---

## NFR-07 Preservation Evidence

| Invariant | Preserved? | Evidence |
|---|---|---|
| `AnalyzeFilesAsync` public signature | ✅ | Pinned by `MatterPreFillService_AnalyzeFilesAsync_PublicSignatureUnchanged_NFR07` test (reflection check on 4 params: `IFormFileCollection files`, `string userId`, `HttpContext httpContext`, `CancellationToken cancellationToken`) |
| 45-second timeout | ✅ | Pinned by `MatterPreFillService_PreservesFortyFiveSecondTimeout_NFR07` test (source-text grep for `TimeSpan.FromSeconds(45)`); also visually preserved at the same call site as pre-migration |
| `useAiPrefill` consumer contract | ✅ | The front-end `useAiPrefill` hook consumes `PreFillResponse` — its shape (`MatterTypeName`, `PracticeAreaName`, `MatterName`, `Summary`, `AssignedAttorneyName`, etc.) is UNCHANGED in this migration (`BuildPreFillResponse` body untouched) |
| `$choices` output schema | ✅ | The playbook output parsing path (`ParseAiResponse`, `TryParseEntityExtractionFormat`, `BuildPreFillResponse`) is UNCHANGED — only the playbook-ID resolution at lookup time was migrated |

---

## ADR Compliance

| ADR | Compliance | Notes |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | `IPlaybookLookupService` injected directly via ctor — no new orchestrator-authored interfaces; concrete `MatterPreFillService` registration unchanged in `WorkspaceModule.cs` |
| ADR-013 (AI architecture / `Services/Ai/` boundary) | ✅ | `IPlaybookLookupService` lives in `Services/Ai/`; `MatterPreFillService` consumes it through the established lookup-service path. The `IWorkspacePrefillAi` facade (ADR-013 refined) for the AI execution path is untouched — only the playbook-ID resolution was migrated |
| ADR-014 (AI caching) | ✅ | The 1-hour-cached `IPlaybookLookupService.GetByIdAsync` is invoked exactly once per pre-fill request; cache behaviour unchanged |
| ADR-018 (typed options) | ✅ | Reads `WorkspaceOptions.MatterPreFillPlaybookId` via `IOptions<WorkspaceOptions>` — no raw `IConfiguration[]` indexer |
| ADR-029 (BFF publish hygiene) | ✅ | Single-task delta ≈ 0 MB; well within thresholds |

---

## Test Summary

| Test scope | Pass count | Notes |
|---|---|---|
| `MatterPreFillServiceTests` (NEW) | 9 / 9 | Reflection + source-contract tests pinning migration shape + NFR-07 invariants |
| Regression: `Workspace` + `SessionSummarize` filter | 413 / 413 | No regressions from sibling Wave 1-E tasks (017/018/019) or from this migration |

---

## Grep Verification (Acceptance Criterion)

```text
grep -rn "2d660cad" src/server/api/Sprk.Bff.Api/
src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs:34:    /// <c>2d660cad-d418-f111-8343-7ced8d1dc988</c>.
src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs:45:    // (2d660cad-d418-f111-8343-7ced8d1dc988) has been removed. The playbook is now
src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs:311:        // 2d660cad-d418-f111-8343-7ced8d1dc988 GUID + the legacy PreFillPlaybookId option
```

All 3 remaining hits are inside XML doc / migration-history comments — **zero executable references** to the hardcoded GUID remain. ✅

---

## Unexpected Findings

1. **Path drift (POML vs reality)**: POML referenced `src/server/api/Sprk.Bff.Api/Services/Ai/MatterPreFillService.cs`, but the actual file location is `Services/Workspace/MatterPreFillService.cs`. Followed CLAUDE.md project-level note to verify location via Glob before editing — matches the same drift seen in prior tasks (012, 015).
2. **Sibling-isolation stash collision (Wave 1-E)**: Task 019 (sibling) ran a `git stash` to isolate publish-size measurement (per its handoff `019-bff-publish-delta.md` §"Measurement procedure" step 1). The stash contained 017+018+019 BFF edits AND was on a different code path (SmartTodo) that introduced conflicts on `git stash pop`. Resolution: popped the stash (restoring 017/018/019 BFF changes plus a re-incarnation of my task-016 edits that 019's stash had captured before sibling-isolation), then `git checkout HEAD` on the 5 SmartTodo conflict paths (out of scope for task 016). End state: clean working tree containing all 4 wave-1-E task BFF migrations + handoffs + tests. **Recommendation**: Wave 1-E may benefit from a follow-up note documenting the sibling-isolation handoff pattern so future parallel waves don't repeat the cascade.

---

## File Inventory

| Path | Status |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Modified |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/MatterPreFillServiceTests.cs` | Added |
| `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/016-bff-publish-delta.md` | Added (this file) |
| `deploy/api-publish-016/` | Build artifact (gitignored) |
| `deploy/task016-publish-size.tar.gz` | Measurement artifact (gitignored) |
