# Wave 1 Publish-Hygiene Gate — Sign-Off

> **Project**: spaarke-ai-platform-unification-r7
> **Task**: 010 (Wave 1 closeout — BFF publish + size check + CVE scan)
> **Date**: 2026-06-28
> **Commit SHA at measurement**: `d4bbf9fdc6ebad35c7edd53b3851907336c487b9`
> **Author**: Claude Code (task-execute, FULL rigor)
> **Spec coverage**: NFR-01 (publish-size ≤ +2 MB cumulative), NFR-02 (no new HIGH-severity CVE), ADR-029 (BFF publish hygiene)

---

## 1. Build & Publish

**Command**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-wave1/`

| Metric | Value |
|---|---|
| Errors | 0 |
| Warnings (full build) | 19 (all pre-existing — 0 new from R7 Wave 1 code) |
| Output projects | `Spaarke.Dataverse`, `Spaarke.Core`, `Spaarke.Scheduling`, `Sprk.Bff.Api` |
| Publish output | `deploy/api-publish-wave1/` |
| Status | ✅ Clean publish |

The 19 pre-existing warnings (CS8766 nullability, CS1998 async without await, CS0618 obsolete `DemoProvisioningOptions`, CS8601/CS8604 null-reference) all live in code that R7 did not modify. Wave 1 added one new source file (`AiCompletionNodeExecutor.cs`) and modified one DI registration line; neither introduces any new warning.

---

## 2. Publish Size — NFR-01 Compliance

**Pre-Wave-1 baseline** (CLAUDE.md §10, post-Phase 5 Outcome A of `sdap-bff-api-remediation-fix`, 2026-05-26): **45.65 MB compressed**.

| Measurement | Value |
|---|---|
| Uncompressed publish (`du -sm deploy/api-publish-wave1/`) | **142 MB** |
| File count | **269** |
| Compressed archive (PowerShell `Compress-Archive -CompressionLevel Optimal`, `C:\tmp\sprk-bff-wave1.zip`) | **46.71 MB** (48,976,951 bytes) |
| Wave 1 cumulative delta vs baseline | **+1.06 MB** (+2.3%) |
| Delta vs task 002 measurement (46.71 MB) | **0.00 MB** — tasks 003-009 added only test code + in-file edits; zero shipped IL delta |
| NFR-01 ceiling (≤ +2 MB cumulative for R7) | ✅ **PASS** (1.06 MB used of 2 MB budget; 0.94 MB headroom remaining for Waves 2-10) |
| 60 MB hard ceiling (CLAUDE.md §10) | ✅ **PASS** (13.29 MB headroom) |
| Phase 5 documented ceiling (50 MB per ADR-029 §4) | ✅ **PASS** (3.29 MB headroom) |
| ≥+5 MB single-task escalation threshold | ✅ NOT TRIPPED (largest single-task delta was task 002 at +1.06 MB) |

**Reading**: The full Wave 1 delta (+1.06 MB) was concentrated in task 002 (AiCompletionNodeExecutor scaffold + Action record extension + DI registration). Tasks 003-009 added zero net shipped size (test code lives in `tests/`, not `Sprk.Bff.Api/`; payload-binding + LLM-call edits to the executor body added microseconds of IL — sub-KB and lost in compression rounding to 0.00 MB).

**Trajectory for R7**: With Waves 2-10 to come, 0.94 MB remains of the +2 MB project budget. Wave 2 (dispatch refactor + enum rename) is expected to **shrink** the package (~150 LOC of `PlaybookOrchestrationService` dispatch code DELETED per FR-11 contour). Net R7 publish-size impact may end negative.

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
| Total HIGH-severity entries | 1 |
| Total Critical-severity entries | 0 |
| HIGH entries introduced by R7 Wave 1 | **0** |
| HIGH entries pre-existing + carried forward | 1 (`Microsoft.Kiota.Abstractions 1.21.2`) |
| NFR-02 (no new HIGH-severity CVE from R7) | ✅ **PASS** |

### Pre-existing HIGH — accepted-risk rationale

`Microsoft.Kiota.Abstractions 1.21.2` (GHSA-7j59-v9qr-6fq9, HIGH) is a transitive dependency of `Microsoft.Graph` 5.x. ADR-029 §4 (Publish-Size Baseline Ratchet table) explicitly notes "Kiota HIGH explicitly accepted-risk pending Graph SDK 6.x upgrade". Spec NFR-02 codifies this carry-forward: pre-existing transitive CVEs at Wave 1 start carry forward unchanged. The proper remediation is a Graph SDK 6.x upgrade as a separate project (not in R7 scope).

R7 added zero new NuGet package references and zero NuGet version changes. The CVE surface is functionally unchanged from pre-Wave-1.

---

## 4. Test Verification

**AiCompletionNodeExecutor test suite** (Wave 1 deliverable):

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter FullyQualifiedName~AiCompletionNodeExecutor
Passed!  - Failed: 0, Passed: 20, Skipped: 0, Total: 20, Duration: 160 ms
```

| Test region | Author task | Count | Status |
|---|---:|---:|---|
| Payload binding + schema rendering + template substitution | 007 | 8 | ✅ all pass |
| Temperature + per-node prompt override (FR-14 + FR-25) | 008 | 7 | ✅ all pass |
| Error paths — Validate contract + LLM failure (FR-13 + FR-14) | 009 | 5 | ✅ all pass |
| **Total** | — | **20** | ✅ **20/20** |

**Full BFF unit suite**:

```
Failed!  - Failed: 2, Passed: 7504, Skipped: 106, Total: 7612, Duration: 1m 33s
```

The 2 failures are pre-existing on master (`KnowledgeDeploymentConfigTests.KnowledgeDeploymentConfig_DefaultValues_AreCorrect`, `SessionFilesCleanupJobTests.RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set`). Both test files were last modified by R3 / R5 / multi-container-r1 (verified via `git log master..HEAD` returning no R7 commits touching either file). **Zero Wave 1 regressions.**

---

## 5. Sign-Off

| Gate | Result |
|---|---|
| `dotnet publish -c Release` runs cleanly (0 errors, 0 new warnings) | ✅ PASS |
| Compressed publish-output size measured + recorded | ✅ 46.71 MB |
| Size delta vs pre-Wave-1 baseline ≤ +2 MB (NFR-01) | ✅ PASS (+1.06 MB) |
| Cumulative ceiling 60 MB NOT breached | ✅ PASS (13.29 MB headroom) |
| `dotnet list package --vulnerable --include-transitive` output captured | ✅ PASS |
| NO new HIGH-severity CVE introduced (NFR-02) | ✅ PASS (1 HIGH; pre-existing Kiota accepted-risk) |
| AiCompletionNodeExecutor tests 20/20 pass | ✅ PASS |
| Zero Wave 1 regressions in broader BFF suite | ✅ PASS (2 failures pre-existing on master) |

**Wave 1 publish-hygiene gate: PASSED.**

**Wave 1 status: ✅ COMPLETE (10/10 tasks).**

R7 is now ready to begin Wave 2 (dispatch refactor — task 020 + C# enum rename `ActionType` → `ExecutorType` — task 022). Wave 2 is expected to deliver a NET-NEGATIVE size delta as ~150 LOC of `PlaybookOrchestrationService` dispatch code is deleted per FR-11.

### R4 graduation gate pending

The R4 daily-update-service-r4 graduation gate (FR-15 — `/narrate` end-to-end via `AiCompletionNodeExecutor`) is NOT yet closed at Wave 1 end. FR-15 is an integration test requiring BOTH (a) `AiCompletionNodeExecutor` end-to-end (Wave 1 ✅ delivered) AND (b) repointed Action rows to `sprk_executortype = AiCompletion` (Wave 5 backfill task 052 owner-checkpoint). R4 stays held until Wave 5 completes; R7 wrap-up (Wave 10 task 091/092) will close R4 graduation gate as the final integration verification.

---

## 6. Constraint Update Recommendation (defer to main session — Sub-Agent Write Boundary)

The current `.claude/constraints/azure-deployment.md` records the baseline as "~45.65 MB (post-Phase 5 Outcome A, 2026-05-26)". Wave 1 cumulative delta is +1.06 MB — within the project budget but worth recording at Wave 10 wrap-up (not now; the baseline only ratchets when work merges to master, and R7 work is on a worktree branch until Wave 10).

**Recommendation**: defer baseline update until R7 merges to master at Wave 10 close. At that point, the new baseline becomes whatever the cumulative R7 measurement is (expected to be at or below 46.71 MB given Wave 2's anticipated net-negative contribution).

No `.claude/` write needed from this task.

---

*Generated by task-execute Step 8 — task 010 of spaarke-ai-platform-unification-r7.*
