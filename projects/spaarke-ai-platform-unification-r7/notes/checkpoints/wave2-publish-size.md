# Wave 2 Publish-Hygiene Checkpoint

> **Project**: spaarke-ai-platform-unification-r7
> **Task**: 029 (Wave 2 closeout — BFF publish + size + CVE scan)
> **Date**: 2026-06-28
> **Commit SHA at measurement**: `79761c4b4` (Wave 2 task 026 close — Action override branch deletion; last code commit before this checkpoint)
> **Author**: Claude Code (task-execute, FULL rigor)
> **Spec coverage**: NFR-01 (publish-size ≤ +2 MB cumulative), NFR-02 (no new HIGH-severity CVE), ADR-029 (BFF publish hygiene)

---

## Wave 2 Outcome Summary

Wave 2 (Dispatch refactor + enum rename, tasks 020–029) is **COMPLETE**. Net effect on the BFF publish artifact: **zero compressed-size delta** vs Wave 1 baseline (46.71 MB → 46.71 MB; precise byte delta −181 bytes, lost in 0.01 MB rounding). The IL footprint of `Sprk.Bff.Api.dll` is functionally unchanged after rename + dispatch refactor + ~250 LOC of source deletions — exactly the expected outcome from a symbol-renaming refactor plus dead-code excisions that the linker had already partly elided. Zero new HIGH-severity CVE. R4 graduation gate (FR-15) still gated on Wave 5 backfill.

---

## 1. Build & Publish

**Command**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-wave2/`

| Metric | Value |
|---|---|
| Errors | 0 |
| Warnings (Sprk.Bff.Api project) | 19 (all pre-existing — 0 new from R7 Wave 2 code) |
| Output projects | `Spaarke.Dataverse`, `Spaarke.Core`, `Spaarke.Scheduling`, `Sprk.Bff.Api` |
| Publish output directory | `deploy/api-publish-wave2/` |
| Status | ✅ Clean publish |

The 19 pre-existing warnings (CS0618 obsolete `DemoProvisioningOptions`, CS8625/CS8601/CS8604 null-reference, CS1998 async-without-await) all live in code R7 Wave 2 did not modify (`RegistrationEndpoints`, `AnalysisActionService.cs:120`, `PlaybookInvocationService`, `AgentEndpoints`, `ChatEndpoints`, `DemoExpirationService`, `NullSessionSummarizeOrchestrator`). Wave 2 modified ~85 source files (77 rename + 8 dispatch refactor) and added zero new warnings — confirms the rename was IL-neutral and the dispatch refactor introduced no nullability/async regressions.

---

## 2. Publish Size — NFR-01 Compliance

**Pre-R7 baseline** (CLAUDE.md §10, post-Phase 5 Outcome A of `sdap-bff-api-remediation-fix`, 2026-05-26): **45.65 MB compressed**.
**Wave 1 measurement** (task 010, 2026-06-28): **46.71 MB compressed** (+1.06 MB vs pre-R7 baseline).

| Measurement | Value |
|---|---|
| Uncompressed publish (`du -sm deploy/api-publish-wave2/`) | **142 MB** (identical to Wave 1) |
| File count | **269** (identical to Wave 1) |
| Compressed archive (PowerShell `Compress-Archive -CompressionLevel Optimal`, `C:\tmp\sprk-bff-wave2.zip`) | **46.71 MB** (48,976,770 bytes) |
| **Single-wave delta** (Wave 2 vs Wave 1's 48,976,951 bytes) | **−181 bytes / −0.00 MB** (effectively FLAT — IL-neutral) |
| **Cumulative R7 delta** (Wave 2 vs 45.65 MB pre-R7 baseline) | **+1.06 MB** (+2.3%) — unchanged from Wave 1 |
| NFR-01 ceiling (≤ +2 MB cumulative for R7) | ✅ **PASS** (1.06 MB used of 2 MB budget; 0.94 MB headroom remaining for Waves 3-10) |
| 60 MB hard ceiling (CLAUDE.md §10) | ✅ **PASS** (13.29 MB headroom) |
| ADR-029 §4 Phase 5 documented ceiling (50 MB) | ✅ **PASS** (3.29 MB headroom) |
| ≥+5 MB single-wave escalation threshold | ✅ NOT TRIPPED |

### Reading

Wave 2 produced a **flat** publish-size outcome — exactly as predicted by the task POML and CLAUDE.md §10 commentary. The contributing forces:

- **`ActionType` → `ExecutorType` rename (task 022, 460 word-boundary renames across 77 files)**: IL-neutral by construction. Same enum identity, same integer values, same metadata-token count. Confirmed: file count unchanged (269 → 269), uncompressed size unchanged (142 MB → 142 MB).
- **Single-hop dispatch refactor (task 024)**: replaced 3-layer lookup chain with single read; ~30 LOC of dispatch code in `PlaybookOrchestrationService.ExecuteNodeAsync` collapsed; ~15 LOC of new payload-shell synthesis added. Net source delta ≈ −16 LOC (per task 024 notes).
- **Structural fallback ladder deleted (task 025)**: 3 helpers (`IsDeployedStartNode`, `IsDeployedLoadKnowledgeNode`, `IsDeployedReturnResponseNode`) and their XML doc blocks removed; ~190 LOC of source deleted from `PlaybookOrchestrationService.cs`.
- **Action override branch deleted (task 026)**: ~40 LOC of override branch inline-deleted.
- **AnalysisActionService read path simplified (task 028)**: removed `$expand=sprk_ActionTypeId` chain + dispatch-derived projection; +64/−50 lines (net +14 LOC of TODO comment blocks pointing to Wave 4 task 046 for the field-drop cleanup).

Total Wave 2 source delta: roughly **−250 LOC net** in production code. The IL impact of that source change is genuinely sub-KB after JIT-friendly compilation + zip compression — well below the rounding threshold of the `Compress-Archive` measurement.

**Trajectory for Waves 3–10**: 0.94 MB remains of the +2 MB R7 budget. Wave 3 (typed config schemas) adds 33 small `GetConfigSchema()` overrides + one new endpoint — expected delta low single-digit KB. Wave 4 (legacy direct path deletion) is expected to SHRINK by ~50–150 KB (`ExecuteAnalysisAsync` + cascading dead-code removal + Dataverse field drops cascading to model trimming). Net R7 publish-size impact at Wave 10 close should land at or under +1.06 MB cumulative — comfortably within NFR-01.

---

## 3. CVE Scan — NFR-02 Compliance

**Command**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive`

```
Project `Sprk.Bff.Api` has the following vulnerable packages
   [net8.0]:
   Top-level Package                   Requested   Resolved   Severity   Advisory URL
   > Microsoft.Kiota.Abstractions      1.21.2      1.21.2     High       https://github.com/advisories/GHSA-7j59-v9qr-6fq9
```

| Metric | Value |
|---|---|
| Total HIGH-severity entries | 1 (identical to Wave 1) |
| Total Critical-severity entries | 0 |
| HIGH entries introduced by R7 Wave 2 | **0** |
| HIGH entries pre-existing + carried forward | 1 (`Microsoft.Kiota.Abstractions 1.21.2`) |
| NFR-02 (no new HIGH-severity CVE from R7) | ✅ **PASS** |

### Pre-existing HIGH — carry-forward rationale (unchanged from Wave 1)

`Microsoft.Kiota.Abstractions 1.21.2` (GHSA-7j59-v9qr-6fq9, HIGH) is a transitive dependency of `Microsoft.Graph` 5.x. ADR-029 §4 (Publish-Size Baseline Ratchet table) explicitly notes "Kiota HIGH explicitly accepted-risk pending Graph SDK 6.x upgrade". Spec NFR-02 codifies this carry-forward: pre-existing transitive CVEs at Wave 1 start carry forward unchanged. The proper remediation is a Graph SDK 6.x upgrade as a separate project (not in R7 scope).

R7 Wave 2 added **zero new NuGet package references** and **zero NuGet version changes**. The CVE surface is functionally unchanged from pre-Wave-2.

---

## 4. Test Verification

**Wave 2 deliverable tests** (AiCompletionNodeExecutor preserved across rename + dispatch refactor):

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --no-build --filter FullyQualifiedName~AiCompletionNodeExecutor
Passed!  - Failed: 0, Passed: 20, Skipped: 0, Total: 20
```

| Suite | Pass | Fail | Skipped | Status |
|---|---:|---:|---:|---|
| AiCompletionNodeExecutor (Wave 1 deliverable; preserved) | 20 | 0 | 0 | ✅ |
| NodeExecutorRegistry (task 027 dispatch alignment) | — | 0 | — | ✅ (included in Nodes filter) |
| Orchestration (tasks 024/025/026 dispatch refactor) | 60 | 0 | 3 | ✅ (3 pre-existing skips) |
| **Full BFF unit suite** | **7503** | **3** | **106** | ⚠️ (see below) |

### Full BFF unit suite — failure analysis

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --no-build
Failed!  - Failed: 3, Passed: 7503, Skipped: 106, Total: 7612, Duration: 1m 14s
```

3 failures, ALL pre-existing on master / NOT R7 Wave 2 regressions:

| # | Test | Status | Evidence |
|---|---|---|---|
| 1 | `KnowledgeDeploymentConfigTests.KnowledgeDeploymentConfig_DefaultValues_AreCorrect` | Pre-existing on master | Identical failure recorded in Wave 1 sign-off (`wave1-publish-size-cve.md` §4). `git log master..HEAD -- tests/unit/Sprk.Bff.Api.Tests/Services/Ai/KnowledgeDeploymentServiceTests.cs` returns 0 R7 commits. Asserts `IndexName == "spaarke-knowledge-index-v2"`; production default is `"spaarke-files-index"` — owned by multi-container-multi-index-r2 backlog. |
| 2 | `SessionFilesCleanupJobTests.RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set` | Pre-existing on master | Identical failure recorded in Wave 1 sign-off §4. `git log master..HEAD -- tests/unit/Sprk.Bff.Api.Tests/Services/Ai/SessionFilesCleanupJobTests.cs` returns 0 R7 commits. Moq `Verify(..., Times.Once)` expects 1 delete invocation; observes 2 in parallel-test runs (the cleanup runs twice when other tests in the same xunit collection trigger a refresh). |
| 3 | `R5SummarizeTelemetryTests.BothInvocationPaths_RecordViaSameCounter` | **Pre-existing parallel-test counter contention** (NEW SURFACING vs Wave 1 baseline, but NOT R7-caused) | `git diff master..HEAD --stat -- tests/unit/Sprk.Bff.Api.Tests/Telemetry/R5SummarizeTelemetryTests.cs` returns empty (zero R7 modifications to file). **Passes 8/8 in isolation**: `dotnet test --filter FullyQualifiedName~R5SummarizeTelemetryTests` → `Failed: 0, Passed: 8`. Failure is a static `Meter` / `Counter` aggregation race — the `r5.summarize.invocation` counter accumulates across parallel tests sharing the same in-process meter instance, so a test asserting `Sum == 2` observes `Sum == 3` when another test's contribution leaks into the capture window. Classic xunit "static state across parallel tests" failure, surfaced now by the rebuild but present in any concurrent run; R7 did not introduce nor address it (out of scope; spec FR-09/10/11 do not touch SessionSummarize telemetry counter design). |

**Zero Wave 2 regressions.** All 3 failures were either explicitly carried forward from Wave 1 (entries 1+2) or are flakes in test code R7 never touched (entry 3). The 3-failure count vs Wave 1's 2-failure count is explained by parallel-test scheduling non-determinism — the R5SummarizeTelemetry flake didn't surface on Wave 1's specific run but is intrinsically present.

---

## 5. Sign-Off Matrix

| Gate | Result | Notes |
|---|---|---|
| `dotnet publish -c Release` runs cleanly (0 errors, 0 new warnings) | ✅ PASS | 0 errors, 19 pre-existing warnings, 0 new |
| Compressed publish-output size measured + recorded | ✅ 46.71 MB | `Compress-Archive -CompressionLevel Optimal` |
| Single-wave delta ≤ +5 MB (escalation threshold) | ✅ PASS | −181 bytes / −0.00 MB (FLAT vs Wave 1) |
| Cumulative R7 delta ≤ +2 MB (NFR-01) | ✅ PASS | +1.06 MB (0.94 MB headroom remaining) |
| Cumulative ceiling 60 MB NOT breached (CLAUDE.md §10) | ✅ PASS | 13.29 MB headroom |
| ADR-029 §4 Phase 5 ceiling 50 MB NOT breached | ✅ PASS | 3.29 MB headroom |
| `dotnet list package --vulnerable --include-transitive` output captured | ✅ PASS | 1 HIGH (pre-existing Kiota) |
| NO new HIGH-severity CVE introduced (NFR-02) | ✅ PASS | Zero new package refs in Wave 2 |
| AiCompletionNodeExecutor tests preserved 20/20 | ✅ PASS | Survived enum rename + dispatch refactor |
| Orchestration tests pass 60/63 (3 pre-existing skips) | ✅ PASS | Baseline preserved |
| Zero Wave 2 regressions in broader BFF suite | ✅ PASS | 3 failures all confirmed pre-existing / parallel-test flake |

**Wave 2 publish-hygiene gate: PASSED.**

**Wave 2 status: ✅ COMPLETE (10/10 tasks).**

R7 is now ready to begin Waves 3–9. The dispatch model is fully reformed: every executor (33 of them) is dispatched on `node.sprk_executortype` via single-hop dispatch in `PlaybookOrchestrationService.ExecuteNodeAsync`; the structural fallback ladder is gone; the Action override branch is gone; `AnalysisActionService` no longer drags the `sprk_ActionTypeId` lookup chain into read paths.

---

## 6. Wave 3-10 Unblock

| Wave | Status | Dependencies satisfied |
|---|---|---|
| Wave 3 (typed config schemas) | 🔲 Ready | Depends on Wave 2 enum rename ✅ |
| Wave 4 (`ExecuteAnalysisAsync` deletion + cascading cleanup) | 🔲 Ready (independent of Wave 9 per task 040 audit) | Depends on Wave 2 ✅ |
| Wave 5 (94-node backfill) | 🔲 Ready | Depends on Wave 2 single-hop dispatch ✅ |
| Wave 6 (doc rewrites) | 🔲 Ready | Depends on Wave 2 vocabulary ✅ |
| Wave 7 (skill rewrites — main-session sequential per Sub-Agent Write Boundary) | 🔲 Ready | Depends on Wave 6 vocabulary draft |
| Wave 8 (PlaybookBuilder UI updates) | 🔲 Ready | Depends on Wave 2 rename + Wave 3 schemas |
| Wave 9 (chat-summarize consumer migration → FR-17) | 🔲 Ready | Depends on Wave 5 backfill |
| Wave 10 (wrap-up + R4 graduation close) | 🔲 Pending | Depends on Waves 5 + 9 |

### R4 graduation gate (still pending — unchanged from Wave 1)

The R4 daily-update-service-r4 graduation gate (FR-15 — `/narrate` end-to-end via `AiCompletionNodeExecutor`) remains NOT closed. FR-15 requires (a) `AiCompletionNodeExecutor` end-to-end (Wave 1 ✅ delivered) AND (b) repointed Action rows to `sprk_executortype = AiCompletion` (Wave 5 backfill task 052 owner-checkpoint). Wave 2's dispatch refactor enables the integration but does not close it. R4 stays held until Wave 5 completes; R7 wrap-up (Wave 10 task 091/092) closes the gate.

---

## 7. Constraint Update Recommendation (defer — Sub-Agent Write Boundary)

`.claude/constraints/azure-deployment.md` currently records the baseline as "~45.65 MB (post-Phase 5 Outcome A, 2026-05-26)". Wave 2 cumulative delta is unchanged from Wave 1 (+1.06 MB). Recommendation unchanged from Wave 1 sign-off: defer baseline update until R7 merges to master at Wave 10 close, at which point the new baseline becomes the cumulative R7 measurement (expected at or below 46.71 MB given Wave 4's anticipated net-negative contribution).

No `.claude/` write needed from this task.

---

*Generated by task-execute Step 8 — task 029 of spaarke-ai-platform-unification-r7.*
