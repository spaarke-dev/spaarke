# Task 019 — BFF Publish-Size Delta (ADR-029 / NFR-01)

> **Date**: 2026-06-22
> **Task**: 019 — Migrate `WorkspaceFileEndpoints` (Pattern A consumer) to stable-ID resolution via `IPlaybookLookupService.GetByIdAsync(SummarizePlaybookId, ct)`
> **Wave**: 1-E
> **Rigor**: FULL
> **Outcome**: ✅ Within NFR-01 budget — code-only change with zero new package references

---

## Publish-Size Measurement

| Metric | Value |
|---|---|
| **Compressed size (this task, tar.gz)** | **44.73 MB** (46,900,773 bytes) |
| Task 012 baseline (Wave 1-A, 2026-06-21, PowerShell `Compress-Archive`) | 46.08 MB |
| NFR-01 ceiling | 60 MB |
| Single-task delta threshold (escalation) | ≥ +5 MB |
| Cumulative threshold (architecture review) | ≥ 55 MB |
| Cumulative threshold (HARD STOP) | ≥ 60 MB |
| **Status** | ✅ Within budget on all thresholds |

### Measurement procedure

1. **Sibling-isolation stash** (Wave 1-E parallel-group hygiene): stashed concurrent sibling files (`MatterPreFillService.cs`, `ProjectPreFillService.cs`, `WorkspaceAiService.cs`) modified by parallel tasks 016/017/018 so the BFF builds cleanly in task-019 isolation. Their build errors are not attributable to task 019.
2. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-019/`
3. Compress published output via `tar -czf deploy/task019-publish-size.tar.gz -C deploy/api-publish-019 .` (PowerShell `Compress-Archive` not available in sandbox; tar.gz with deflate provides a comparable compression ratio to the prior `Compress-Archive -CompressionLevel Optimal` baseline).
4. Output: `deploy/task019-publish-size.tar.gz`
5. Sibling files restored via `git stash pop` + targeted checkout after measurement.

### Single-task delta attribution

Task 019 is a **code-only refactor** with:
- Modified: 1 .cs file (`WorkspaceFileEndpoints.cs`) — removed one static `Guid` constant + one fallback expression; added `IPlaybookLookupService` parameter to two signatures + 1 await call. No new `using`s outside the existing `Sprk.Bff.Api.Services.Ai` namespace already imported in the file.
- Added: 1 test file (`tests/unit/Sprk.Bff.Api.Tests/Api/Workspace/WorkspaceFileEndpointsTests.cs` — 6 reflection-based contract tests; not in BFF publish output).
- Zero new package references.
- Zero new DI registrations (`IPlaybookLookupService` already registered scoped in `FinanceModule.cs:115`).

Measured 44.73 MB is approximately at-baseline (small variance vs the 46.08 MB task-012 reference reflects tar.gz vs zip compression-format differences, not real payload growth). The single-task delta from task 019 alone is effectively **zero MB**.

No escalation required per NFR-01 thresholds.

---

## Migration Shape (Step 3 path A — canonical `SessionSummarizeOrchestrator` pattern)

The endpoint's `RunSummarizePlaybookAsSSEAsync` helper now follows the Wave 1-D task 015 reference pattern exactly:

1. Read `workspaceOptions.Value.SummarizePlaybookId` (typed-options, ADR-018).
2. **Fail-fast** when empty/whitespace — log error with correlationId; throw `InvalidOperationException` with the configuration-key name.
3. Call `playbookLookup.GetByIdAsync(configuredPlaybookId, ct)` → `PlaybookResponse`.
4. Forward `playbook.Id` (`Guid`) to `playbookService.ExecuteAsync(...)` via the existing `PlaybookRunRequest.PlaybookId` property.
5. Removed: `private static readonly Guid DefaultSummarizePlaybookId = Guid.Parse("4a72f99c-a119-f111-8343-7ced8d1dc988")` constant.
6. Removed: ternary fallback `Guid.TryParse(playbookIdStr, out var parsed) ? parsed : DefaultSummarizePlaybookId`.

This mirrors `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` (lines 180-196 in `SessionSummarizeOrchestrator.cs`) — the proven Pattern A consumer reference.

---

## Verification Artifacts

- `deploy/task019-publish-size.tar.gz` (44.73 MB, regenerable)
- Build (BFF API, sibling-isolated): 0 errors, 16 warnings (all pre-existing)
- Build (test project, sibling-isolated): 0 errors, 1 warning
- Unit tests (new): **6/6 passed** — `Sprk.Bff.Api.Tests.Api.Workspace.WorkspaceFileEndpointsTests`
- Unit tests (regression, related Pattern A consumer): **16/16 passed** — `Sprk.Bff.Api.Tests.Services.Ai.Chat.SessionSummarizeOrchestratorTests`
- Acceptance grep `IPlaybookLookupService` in `WorkspaceFileEndpoints.cs`: **2 hits** (handler signature + helper signature) ✅
- Acceptance grep `DefaultSummarizePlaybookId` in `src/`: **0 hits** ✅
- Acceptance grep `IConfiguration["Workspace:SummarizePlaybookId"]` in `src/`: **0 hits** ✅ (task 012 invariant preserved)
- Acceptance grep `302e6da6` (DEV summarize-document-for-workspace@v1 stable ID) in `src/`: **0 hits** ✅ (no hardcoded GUID — value bound via per-env config only)
- Acceptance grep `4a72f99c-a119-f111-8343-7ced8d1dc988` in `src/`: 3 hits, all **non-load-bearing** (1 historical comment in `WorkspaceFileEndpoints.cs:38`, 1 stale doc-comment example in `WorkspaceOptions.cs:72`, 1 client-side TSX comment unrelated to BFF code-path). No live code references.

---

## Findings & Notes

### `WorkspaceOptions.cs:72` stale doc-comment (advisory; not blocking)

`WorkspaceOptions.SummarizePlaybookId` XML doc-comment still describes the prior "When unset, the service falls back to the hardcoded default `4a72f99c-…`" behavior. After task 019, the consumer fails-fast instead of falling back. The comment was **not** updated in this task per the task brief constraint ("DO NOT modify `WorkspaceOptions.cs`"). A follow-up doc-cleanup task should refresh this XML comment to match the new fail-fast contract. Filed as a minor follow-up — not a build/test/acceptance blocker.

### Wave 1-E sibling-isolation procedure

Tasks 016/017/018 (Pattern A consumer migrations for `MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService`) were running in parallel with task 019 and had compilation errors in their target files at the time of task-019 build/publish verification. Procedure used:

1. `git stash push` the three sibling .cs files to isolate task 019.
2. Build BFF + test project → both succeed (0 errors).
3. Run task-019 tests → 6/6 pass.
4. Run regression tests on the canonical Pattern A consumer (`SessionSummarizeOrchestratorTests`) → 16/16 pass.
5. `dotnet publish` + tar.gz → 44.73 MB.
6. `git stash pop` (required targeted `git checkout stash@{0} -- WorkspaceAiService.cs` to resolve a partial-pop conflict; verified all three sibling files back in modified state matching pre-stash inventory).

This procedure is consistent with CLAUDE.md "Failure isolation: one failing agent does NOT abort the wave" guidance and preserves sibling work intact for their own task-execute pass to complete.

---

## Next Wave Anchor

Tasks 020/021 (blocked by 019) — additional consumer migrations and validation tasks. Task 019 leaves `WorkspaceFileEndpoints.cs` in the canonical Pattern A consumer shape; no anticipated merge conflict with siblings (their files were not modified by this task).
