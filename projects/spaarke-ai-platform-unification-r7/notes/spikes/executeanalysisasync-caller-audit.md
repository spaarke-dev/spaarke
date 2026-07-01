# ExecuteAnalysisAsync Caller Audit — Wave 4 task 040 deliverable

> **Author**: task-execute (R7-040)
> **Date**: 2026-06-28
> **Status**: Complete
> **Source POML**: [`tasks/040-audit-executeanalysisasync-callers.poml`](../../tasks/040-audit-executeanalysisasync-callers.poml)
> **Consumers**: Wave 4 task 041 (non-chat caller migration), Wave 4 task 042 (deletion), Wave 9 task 091 (chat-summarize migration)
> **Spec basis**: FR-11 (delete legacy direct-invocation path), NFR-06 (no backward-compat shim)

---

## Audit method

```
Grep pattern="ExecuteAnalysisAsync" glob="**/*.cs" output_mode=content -n=true
```

All 20 grep hits are accounted for below. **No further grep needed** for downstream task 041 / 042.

---

## Caller inventory table

| # | File | Line | Hit type | Calling context | Disposition | Replacement target | Risk |
|---|---|---:|---|---|---|---|---|
| 1 | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisOrchestrationService.cs` | 33 | **Interface declaration** | Contract surface for the legacy method | DELETE with method (task 042) | n/a (deleted) | none |
| 2 | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | 83 | **Method definition** | The legacy method itself (FR-11 deletion target) | DELETE (task 042) | n/a (deleted) | none |
| 3 | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | 95 | Self-reference (log message string) | Log line inside the method body | DELETE with method (task 042) | n/a (deleted) | none |
| 4 | `src/server/api/Sprk.Bff.Api/Services/Ai/IStreamingAnalysisToolHandler.cs` | 11 | XML-doc cref | Doc comment refers to `AnalysisOrchestrationService.ExecuteAnalysisAsync` as the pattern reference | DELETE/UPDATE doc-cref in task 042 wrap-up | rewrite cref to point at `PlaybookOrchestrationService.ExecuteNodeAsync` OR delete sentence | LOW — broken XML cref produces a warning, not a build break |
| 5 | `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | 261 | **Production call site** | `POST /api/ai/analysis/execute` — SSE streaming analysis execution. Only production caller of the method. | **Wave 4 task 041** | Replace endpoint handler body with a call into `IInvokePlaybookAi` (consumer-routing dispatch via `IConsumerRoutingService` Path A.5) OR a `PlaybookOrchestrationService.ExecuteAsync` invocation against a degenerate 3-node playbook (Start → AiAnalysis → ReturnResponse). | **MEDIUM** — endpoint returns SSE `AnalysisStreamChunk` (`metadata`, `progress`, `chunk`, `done`); `PlaybookOrchestrationService.ExecuteAsync` returns `PlaybookStreamEvent`. Stream-chunk shape mapping required. See Risk Register below. |
| 6 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 134 | Test region marker | `#region ExecuteAnalysisAsync Tests` — comment header | DELETE region (task 042) | n/a (deleted) | none |
| 7 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 137 | Test method name | `ExecuteAnalysisAsync_Success_YieldsMetadataChunksAndCompleted` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 8 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 151 | Test invocation | Inside test #1 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 9 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 166 | Test method name | `ExecuteAnalysisAsync_DocumentNotFound_ThrowsKeyNotFoundException` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 10 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 178 | Test invocation | Inside test #2 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 11 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 189 | Test method name | `ExecuteAnalysisAsync_ActionNotFound_ThrowsKeyNotFoundException` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 12 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 212 | Test invocation | Inside test #3 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 13 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 223 | Test method name | `ExecuteAnalysisAsync_WithPlaybook_ResolvesPlaybookScopes` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 14 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 257 | Test invocation | Inside test #4 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 15 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 269 | Test method name | `ExecuteAnalysisAsync_StreamsContentChunks` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 16 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 283 | Test invocation | Inside test #5 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 17 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 297 | Test method name | `ExecuteAnalysisAsync_CompletedChunk_ContainsTokenUsage` | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 18 | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | 311 | Test invocation | Inside test #6 | DELETE (test cleanup, task 042) | n/a (deleted) | none |
| 19 | `tests/integration/Spe.Integration.Tests/AnalysisEndpointsIntegrationTests.cs` | 853 | **Mock impl** of legacy interface | `MockAnalysisOrchestrationService` implements `IAnalysisOrchestrationService` for endpoint integration tests. Tests `/api/ai/analysis/execute` SSE contract: Unauthorized, PlaybookNotFound, DocumentNotFound, PartialAuthorization scenarios. | **REWRITE in task 041** to mock the replacement service the endpoint will call (either `IInvokePlaybookAi` or `IPlaybookOrchestrationService` depending on path 041 chooses). | If task 041 chooses consumer-routing Path A.5: rewire mock to `IConsumerRoutingService` + `IInvokePlaybookAi`. If task 041 chooses degenerate 3-node playbook path: rewire mock to `IPlaybookOrchestrationService`. | **MEDIUM** — integration test verifies endpoint-level SSE shape; the replacement code path must continue to produce the same SSE chunks. Run these tests post-041 to verify contract preservation (or update the assertions if the SSE chunk schema changes intentionally). |

**Total hits**: 20 (with `replace_all` semantics, ~20 lines of grep output).
**Production call sites**: **1** — `AnalysisEndpoints.cs:261`.
**Test references**: 13 hits in `AnalysisOrchestrationServiceTests.cs` (6 test methods + 6 invocations + 1 region marker) + 1 mock impl in `AnalysisEndpointsIntegrationTests.cs`.
**Interface + method-definition + self-log + xml-doc**: 4 hits in the legacy service files themselves.

---

## SessionSummarizeOrchestrator deep-dive (Wave 9 task 091 handoff)

**Critical finding (contradicts POML expected-callers assumption + spec FR-17 wording)**:

> `SessionSummarizeOrchestrator` **does NOT call** `AnalysisOrchestrationService.ExecuteAnalysisAsync`. It is not in the grep hit list.

**Independent confirmation**: separately grepped `SessionSummarizeOrchestrator.cs` for `ExecuteAnalysisAsync | ExecutePlaybookAsync | orchestrationService\. | IAnalysisOrchestrationService` — **no matches**. SessionSummarizeOrchestrator uses different orchestration (likely a direct `IOpenAiClient` call or a dedicated path discoverable separately).

### Implication for Wave 9 task 090 / 091

Wave 9 task 091 (FR-17 migrate SessionSummarizeOrchestrator to `IConsumerRoutingService` + `IInvokePlaybookAi`) is still required — but **the dependency in plan.md TASK-INDEX.md row "041 ← needs 091" can be re-evaluated**. The original ordering was: chat-summarize must migrate before `ExecuteAnalysisAsync` can be deleted, because SessionSummarizeOrchestrator was assumed to use it. **That assumption is false.**

**Revised ordering** (recommended):
- Wave 4 task 041 (migrate the ONE non-chat caller — `AnalysisEndpoints.ExecuteAnalysis` endpoint) **CAN proceed independently of Wave 9 task 091**.
- Wave 4 task 042 (delete the method) then proceeds after 041.
- Wave 9 task 091 (migrate SessionSummarizeOrchestrator) is on its own track per FR-17 and does not gate Wave 4.

**Action for plan.md/TASK-INDEX.md update**: Wave 4 row "blocked on Wave 9 + Wave 2" → "blocked on Wave 2 only". Defer this update to task 041 kickoff (not in scope here per audit-only rule).

### Why FR-17 still applies separately

FR-17 = "consumer routing for chat-summarize". SessionSummarizeOrchestrator's existing path (whatever it is) still bypasses `IConsumerRoutingService` and `IInvokePlaybookAi`. The Wave 9 audit task 090 should produce its own caller graph for SessionSummarizeOrchestrator to confirm the actual code path and Path A.5 migration shape — that work is **not duplicated** by this audit.

---

## Wave 4 task 041 work list (non-chat callers)

**Exactly ONE production caller to migrate**:

| File | Line | Surface |
|---|---:|---|
| `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | 261 | `POST /api/ai/analysis/execute` (SSE) |

### Replacement-design notes for task 041

**Option A — Degenerate 3-node playbook**:
- Construct an in-memory playbook with `Start → AiAnalysis → ReturnResponse` nodes; configure the AiAnalysis node from `AnalysisExecuteRequest.{ActionId, SkillIds, KnowledgeIds, ToolIds, PlaybookId}`.
- Call `IPlaybookOrchestrationService.ExecuteAsync(PlaybookRunRequest, HttpContext, CancellationToken)`.
- Map `PlaybookStreamEvent` → `AnalysisStreamChunk` for SSE output. (See Risk Register below for chunk-shape diffs.)
- Pro: zero new code paths, reuses the canonical orchestrator.
- Con: requires PlaybookId handling for the existing "if request.PlaybookId.HasValue" branch (which is already delegated to `ExecutePlaybookAsync` — that delegation can drop too).

**Option B — Consumer-routing Path A.5** (via `IConsumerRoutingService` + `IInvokePlaybookAi`):
- Define an `"analysis-execute"` consumer slot in `sprk_playbookconsumer` (similar to Wave 9 task 092 for chat-summarize).
- Endpoint calls `IInvokePlaybookAi.InvokeAsync(consumerKey: "analysis-execute", request, httpContext, ct)`.
- Pro: makes the endpoint a thin façade with all routing externalized.
- Con: requires a Dataverse row + schema work; arguably overengineered for a single caller of a feature-gated endpoint.

**Recommendation for task 041**: **Option A** (degenerate 3-node playbook). The endpoint is feature-gated (`AnalysisOptions.Enabled`), authorized via `AnalysisExecuteAuthorizationFilter`, and used solely by the analysis Code Page consumer. Adding a consumer-routing slot for one caller does not pay rent. Path A.5 reserved for chat-surface consumers per FR-17/FR-18.

**Note**: existing code already delegates to `ExecutePlaybookAsync` when `request.PlaybookId.HasValue` (lines 92-113 of `AnalysisOrchestrationService.cs`). The "raw OpenAI call" path (lines 115+) is the only path that truly needs replacement.

---

## Test cleanup list (not blocking — happens in task 042)

| File | Action |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | **DELETE** the entire `#region ExecuteAnalysisAsync Tests` (lines 134-330, ~196 LOC, 6 test methods). Replacement coverage exists in `PlaybookOrchestrationService` tests + new task 041 endpoint tests. Per ADR-038 §7 (build-vs-maintain), these tests are scaffolding for a deleted method — they CANNOT defend any behavior after task 042. |
| `tests/integration/Spe.Integration.Tests/AnalysisEndpointsIntegrationTests.cs` (`MockAnalysisOrchestrationService` line 853+) | **REWRITE** in task 041 to mock the replacement service. Preserve the 4 scenario assertions (Unauthorized, PlaybookNotFound, DocumentNotFound, PartialAuthorization) — these protect the endpoint-level SSE contract per ADR-038 §1 (integration-heavy pyramid). |

**ADR-038 alignment**: the unit-test class (`AnalysisOrchestrationServiceTests.cs`) is mostly B7-class scaffolding (all-mocks + verify shape). Most of its 6 tests would fail the `/test-diet` defend-at-close gate anyway. Their natural disposition is DELETE — no rewrite needed. The integration test is maintain-class (endpoint contract test) and the mock rewrite preserves it.

---

## Risk register (subtle contract mismatches)

| Risk ID | Caller | Risk | Mitigation in task 041 |
|---|---|---|---|
| R-040-1 | `AnalysisEndpoints.cs:261` | **SSE chunk-shape diff**: `ExecuteAnalysisAsync` yields `AnalysisStreamChunk` with types `metadata`, `progress`, `chunk`, `done`. `PlaybookOrchestrationService.ExecuteAsync` yields `PlaybookStreamEvent` (different schema). Code Page consumers parsing SSE expect `AnalysisStreamChunk` JSON shape. | Either (a) map `PlaybookStreamEvent` → `AnalysisStreamChunk` in the endpoint adapter layer, OR (b) update Code Page consumers to handle `PlaybookStreamEvent`. Prefer (a) — keeps client contract stable. Test via the migrated integration test (item 19 in caller table). |
| R-040-2 | `AnalysisEndpoints.cs:261` | Endpoint emits `[DONE]` SSE terminator + fires `SendAnalysisCompleteNotificationAsync` post-stream. Must preserve. | Keep the `try` block envelope unchanged; only swap the `await foreach` body. |
| R-040-3 | `AnalysisEndpoints.cs:261` | `AnalysisExecuteRequest` has `AnalysisId` field — used to resume/update an existing Dataverse record. `PlaybookRunRequest` must carry this through. | Confirm `PlaybookRunContext` allows pre-existing analysis-record-Id; if not, extend the request shape in task 041 (single-line addition, no breaking change). |
| R-040-4 | `IStreamingAnalysisToolHandler.cs:11` doc-cref | XML-doc cref points at the deleted method → build warning (not error). | Rewrite cref to `<see cref="PlaybookOrchestrationService.ExecuteNodeAsync"/>` in the same task 042 PR that deletes the method (one-line change). |
| R-040-5 | Wave 9 dependency assumption | plan.md TASK-INDEX.md says "Wave 4 blocked on Wave 9 + Wave 2". This audit finds that assumption stems from a false premise (SessionSummarizeOrchestrator does NOT call ExecuteAnalysisAsync). | Recommend updating plan.md at task 041 kickoff: Wave 4 blocked on **Wave 2 only**. Wave 9 still required for FR-17 but on its own track. |
| R-040-6 | Feature gating | The endpoint is gated by `AnalysisOptions.Enabled` (line 230). If currently disabled in production, the migration is even lower-risk (no live traffic to migrate). | Confirm `AnalysisOptions.Enabled` value in prod before task 042 deletion — if true, schedule deploy carefully; if false, deletion is risk-free. |

---

## Acceptance signal

- **Total grep hits** before deletion: **20** (1 method def + 1 interface decl + 1 self-log-string + 1 doc-cref + 1 prod caller + 13 unit-test references + 1 integration-test mock + 1 region marker — see caller inventory table above; 20 distinct lines).
- **Expected post-deletion grep hits** (after task 042 + test cleanup completes): **0**.
- **Production callers to migrate** (task 041): **1**.
- **Chat-related callers** for Wave 9 task 091: **0 via ExecuteAnalysisAsync** (SessionSummarizeOrchestrator uses a different code path — Wave 9 task 090 will audit separately).
- **Test files affected**: 2 (one full deletion, one rewrite).
- **Doc-cref repairs**: 1 (in `IStreamingAnalysisToolHandler.cs`).

---

## Handoff notes for downstream tasks

- **Task 041** (`migrate-non-chat-callers`): exactly ONE caller. Use Option A (degenerate 3-node playbook). Update `AnalysisEndpointsIntegrationTests.cs` mock concurrently. Preserve SSE chunk shape per R-040-1.
- **Task 042** (`delete-executeanalysisasync`): delete method (line 83) + interface row (line 33) + 6 unit tests (`AnalysisOrchestrationServiceTests.cs` lines 134-330) + fix doc-cref (`IStreamingAnalysisToolHandler.cs` line 11). Cascading dead-code grep recommended for any `_documentLoader`/`_ragProcessor`/`_resultPersistence` paths that become orphaned (note: `ExecutePlaybookAsync` still uses them — DO NOT delete the shared services).
- **Task 091** (`migrate-SessionSummarizeOrchestrator`): begin with task 090 audit. This audit found ZERO overlap with ExecuteAnalysisAsync; the migration is purely an FR-17 consumer-routing rewrite, independent of Wave 4 work.
- **Plan update**: Wave 4 row dependency in `TASK-INDEX.md` should change from "blocked on Wave 9 + Wave 2" to "blocked on Wave 2" — defer execution of this edit to task 041 kickoff to keep this task audit-only.

---

*Generated 2026-06-28 by task R7-040.*
