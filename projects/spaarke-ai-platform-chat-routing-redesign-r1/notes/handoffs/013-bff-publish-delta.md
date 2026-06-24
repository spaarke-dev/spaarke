# Task 013 BFF Publish Delta

> **Generated**: 2026-06-22
> **Task**: 013 — Extend WorkspaceOptions.cs with 4 typed code options (CRIT-1 race-condition fix)
> **Phase 0 baseline**: 44.75 MB compressed
> **Post-Wave 1-A baseline (per `011-bff-publish-delta.md`)**: 44.74 MB compressed

## Measurements

| Stage | Compressed (MB) | Compressed (bytes) | Delta vs Wave-1-A baseline |
|---|---|---|---|
| Post-task-011 (Wave 1-A baseline) | 44.74 | — | — |
| Post-task-013 (this task — 4 new typed string properties + 4 appsettings keys + tests) | 44.75 | 46,927,543 | **+0.01 MB (within measurement noise)** |

## NFR-01 status

- Cumulative delta after Wave 1-A + Wave 1-B (task 013): **+0.01 MB** vs post-Wave-1-A baseline (≈ **0 MB** vs Phase 0).
- Hard ceiling 60 MB → **44.75 MB measured**, **15.25 MB headroom**.
- Escalation threshold 55 MB → headroom 10.25 MB.
- Single-task escalation threshold (+5 MB) → not approached; delta is essentially measurement-noise.

## What changed in this task

CRIT-1 fix: pre-seat 4 typed `WorkspaceOptions` code properties so wave 1-E parallel consumer migrations (tasks 016, 017, 018, 019) don't collide on `WorkspaceOptions.cs`. **No consumer was migrated** — wave 1-E owns the consumer migrations.

### Files modified (exactly 3)

1. **`src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs`** (+61 lines)
   - Added `ChatSummarizePlaybookCode` (default `"summarize-document-chat"`, FR-05)
   - Added `MatterPreFillPlaybookCode` (default `"create-matter-prefill"`, FR-02 + NFR-07)
   - Added `ProjectPreFillPlaybookCode` (default `"create-project-prefill"`, FR-02 + NFR-07)
   - Added `AiSummaryPlaybookCode` (default `string.Empty`, FR-02; canonical value confirmed in task 018)
   - XML doc on every property references its FR + the CRIT-1 race-condition fix rationale.

2. **`src/server/api/Sprk.Bff.Api/appsettings.template.json`** (+10 lines, -1)
   - Extended the `Workspace` section with 4 matching config keys.
   - `AiSummaryPlaybookCode` left empty pending task 018.
   - Each key paired with a `_<Name>_comment` field explaining the migration rationale.

3. **`tests/unit/Sprk.Bff.Api.Tests/Configuration/WorkspaceOptionsTests.cs`** (+90 lines)
   - Added `Defaults_ForChatRoutingR1CodeProperties_MatchSpec_Section_1_7_3` (Fact) — asserts the 4 defaults match spec §1.7.3.
   - Added `ChatRoutingR1_CodeProperties_BindFromConfiguration` (Theory, 4 InlineData rows) — asserts each property binds from its config key.
   - Added `ChatRoutingR1_CodeProperties_RetainDefaults_WhenConfigKeysMissing` (Fact) — asserts defaults apply when config absent.
   - Added `ChatRoutingR1_AllFourCodeProperties_CoexistWithSummarizePlaybookCode` (Fact) — asserts post-task-013 appsettings template state binds all 5 code properties cleanly together.

## Test outcome

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 16 warnings (all pre-existing)**
- `dotnet test ... --filter "FullyQualifiedName~WorkspaceOptions"` → **13/13 passed, 0 failed, 0 skipped, 13 ms**
  - 6 from task 012 (preserved)
  - 7 new from task 013 (1 Defaults + 4 Theory rows + 1 RetainDefaults + 1 Coexist)
- All net-new tests fall within the existing `Sprk.Bff.Api.Tests.Configuration` namespace (no `Options/` directory created — preserves the namespace collision fix from task 012).

## Scope check

- `git diff --stat` on the 3 target files: `3 files changed, 160 insertions(+), 1 deletion(-)`
- No other Spaarke source files modified in this task. Pre-existing modifications visible in `git status` (`.husky/_/*`, `src/client/pcf/package-lock.json`, project POML files) are from prior tasks and not in scope for task 013.

## CRIT-1 invariant verification

After this commit, wave 1-E tasks (016, 017, 018, 019) MUST NOT modify `WorkspaceOptions.cs`. Each consumer migration only:
1. Reads its assigned typed property from `IOptions<WorkspaceOptions>`.
2. Resolves the code → GUID at runtime via `IPlaybookLookupService.GetByCodeAsync` (per ADR-018 + Pattern A).

If any wave 1-E task is found editing `WorkspaceOptions.cs`, that is a CRIT-1 regression and the task should be paused for re-scoping.

## Notes

- ADR-018 typed-options binding verified via in-memory `ConfigurationBuilder` + DI binding in the test helper.
- ADR-029 publish-size budget honored (+0.01 MB delta).
- ADR-010 DI minimalism preserved (no service registration changes; existing `services.Configure<WorkspaceOptions>()` from task 012 is the binding point).
- Backward-compat: existing nullable `*PlaybookId` properties from earlier projects unchanged; existing `SummarizePlaybookCode` from task 012 unchanged.
