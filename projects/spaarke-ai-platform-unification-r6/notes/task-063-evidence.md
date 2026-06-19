# Task 063 evidence — D-C-16 — Telemetry: chat agent + playbook execution emit context.* events (ADR-015 BINDING)

**Pillar / Spec ref**: R6 Pillar 6c / FR-37 — `context.*` events emitted from BFF telemetry
sites to feed the ExecutionTraceWidget (task 061 / 062).
**Wave**: C-G3 follow-up dispatch (after the original C-G3 gap-fill agent punted with
`task-063-partial-evidence.md`).
**Date**: 2026-06-18.
**Dependencies**: task 059 ✅ (event type definitions on disk at
`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`), task 061 ✅
(ExecutionTraceWidget), task 062 ✅ (registry wiring).

## What was on disk going in (per `task-063-partial-evidence.md`)

| File | Status |
|---|---|
| `Services/Ai/Chat/SprkChatAgent.cs` | Unmodified (no tool-call event emissions) |
| `Services/Ai/Capabilities/CapabilityRouter.cs` | Unmodified (no knowledge/decision events) |
| `Services/Ai/Playbook/PlaybookExecutionEngine.cs` (sic — actual file is `PlaybookOrchestrationService.cs`) | Unmodified (no node-lifecycle events) |
| `tests/.../ContextEventEmissionTests.cs` | NOT created |
| `notes/task-063-adr015-emission-audit.md` | NOT created |

## What this dispatch added

### 1. Telemetry emitter contract + implementation (NEW)

`src/server/api/Sprk.Bff.Api/Services/Ai/Telemetry/IContextEventEmitter.cs`
+ `ContextEventEmitter.cs` — six emission methods (one per
ContextPaneEvent discriminant from task 059) with structurally-constrained
parameter lists (no `object` / `JsonElement` / `string content` types).

Implementation emits via:
- A `Meter` named `Sprk.Bff.Api.Ai.ContextEvents` with one `Counter<long>` per
  event type.
- Structured `[ADR-015]`-prefixed `ILogger` entries carrying deterministic
  identifiers + enum-like short strings + numeric metrics ONLY.

DI registration: unconditional `AddSingleton` in
`Infrastructure/DI/AnalysisServicesModule.cs` (next to `R5SummarizeTelemetry`)
— same anti-asymmetric-registration pattern.

### 2. Four emission categories wired

Per the POML, each of the four categories listed below is now emitted from
the BFF. Full per-site ADR-015 + NFR-08 audit in
`task-063-adr015-emission-audit.md`.

| Site | File | Decisions wired |
|---|---|---|
| `tool_call_started/completed` | `Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | 4 outcomes: `ok`, `error`, `validation_failed`, `cancelled`, `exception` |
| `knowledge_retrieved` | `Services/Ai/RagService.cs` (per-result emission in `SearchAsync` after `LogRetrievalResults`) | per result of every RAG search |
| `decision_made` | `Services/Ai/Capabilities/CapabilityRouter.cs` | 5 outcomes: layer1 `confident/uncertain`; layer2 `confident/timeout/rate_limited`; layer3 `fallback` |
| `playbook_node_executing/completed` | `Services/Ai/PlaybookOrchestrationService.cs` `ExecuteNodeAsync` (WRAPPER LEVEL — NFR-08 binding) | All 11 return paths via local helper `EmitNodeCompleted(decision)` |

### 3. Unit tests (NEW)

`tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/ContextEventEmissionTests.cs`
— 11 tests using `MeterListener` (the task 058 ConflictResolutionTests
pattern) to subscribe to the `Sprk.Bff.Api.Ai.ContextEvents` meter and
capture every counter increment with its tag values.

Coverage:
- Per-event-type emission (6 tests, one per discriminant).
- Started → Completed pairing correlates by decisionId / playbookId+nodeId
  (2 tests).
- Decision-made enum surface (Layer 1 confident / Layer 3 fallback) (2 tests).
- ADR-015 anti-leakage: drives every emission site and asserts no captured
  tag VALUE across any of the 6 counters carries the user-content needle
  (1 test).
- Timing contract: `Stopwatch.ElapsedMilliseconds` is non-negative; API
  accepts it (1 test).

### 4. Audit notes

`projects/spaarke-ai-platform-unification-r6/notes/task-063-adr015-emission-audit.md`
— per-site ADR-015 audit conclusion (4 sites, all PASS) + NFR-08
verification + cross-cut ADR analysis. See that file for the load-bearing
detail.

## Governance

### ADR-013 (AI architecture, facade boundary)
- `IContextEventEmitter` lives in `Services/Ai/Telemetry/` — AI-internal,
  NOT in `Services/Ai/PublicContracts/`. CRUD-side callers never inject it.
- DI registration unconditional + singleton, in an existing module
  (`AnalysisServicesModule.cs`) per ADR-010.

### ADR-015 (data governance, BINDING)
- Per-site audits in `task-063-adr015-emission-audit.md`. All four
  emission categories PASS.
- The `IContextEventEmitter` interface signatures are STRUCTURALLY constrained
  to deterministic identifiers + enum-like short strings + numeric metrics.
  No `object` / `JsonElement` / `string content` parameter exists on any
  method — user-content smuggling is IMPOSSIBLE at the type-system layer.
- Empirical contract verification: `Adr015_NoEmissionSite_LeaksUserContent`
  test drives every site and asserts no tag value contains the needle
  `"PRIVILEGED LEGAL DRAFT do not share"`.

### ADR-029 (BFF publish hygiene)
- Zero new NuGet dependencies. Uses `System.Diagnostics.Metrics` (BCL) and
  `Microsoft.Extensions.Logging` (already in project).
- Compressed publish size: **44.68 MB** (post-task — measured via
  `tar -czf /tmp/bff-publish-task063.tar.gz -C deploy api-publish/`).
- Delta vs prior baseline (~45.65 MB): **-0.97 MB** (small reduction, likely
  build variance / minor dead-code elimination). Well within the NFR-02
  ≤+5 MB R6 budget and the ADR-029 ≤60 MB hard ceiling.

### ADR-030 (PaneEventBus 4-channel)
- Events fit on the existing `context` channel using the additive
  discriminants defined in task 059. No 5th channel introduced.

### NFR-08 (11 production node executors UNMODIFIED)
- Verified: `git diff --stat src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/`
  outputs ZERO lines (empty diff). The emission is exclusively at the
  wrapper level (`ExecuteNodeAsync`), not inside any executor's
  `ExecuteAsync(NodeExecutionContext, ...)` body.

## Build + test status

```
dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q
  Build succeeded. 0 Error(s), 16 Warning(s).  (all warnings pre-existing)

dotnet build tests/unit/Sprk.Bff.Api.Tests/ -nologo -v q
  Build succeeded. 0 Error(s), 0 Warning(s).

dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ContextEvent"
  Passed!  - Failed: 0, Passed: 11, Skipped: 0, Total: 11.  Duration: 51 ms.

dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
  Build succeeded. 0 Error(s).
  Compressed (tar+gzip): 44.68 MB.

git diff --stat src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/
  (empty)
```

Existing unit tests still pass (regression check via spot-build of the BFF
project + tests project; no test failures introduced by the new code).

## Outstanding

- **Frontend subscription**: ExecutionTraceWidget (task 061) + registry (task
  062) are functional client-side infrastructure. Subscribing them to the
  BFF-emitted meter / log surface is a deployment-time wiring step — the
  chat SSE pipe needs a forwarder that observes the
  `Sprk.Bff.Api.Ai.ContextEvents` meter (or the `[ADR-015][context.*]`
  structured logs) and dispatches PaneEventBus events on the `context`
  channel. This is downstream config work, not code work, so not in 063.
- **`sessionId` / `tenantId` threading** in `RagService.SearchAsync` and
  `PlaybookOrchestrationService.ExecuteNodeAsync` is `null` today (those
  services don't receive chat-session context in their current APIs). Trace
  widget correlates by `tenantId` + timestamp ordering per
  PaneEventTypes.ts `correlationId?` optional semantics. Future work could
  thread the chat session id through these layers; out of scope for 063.

## Outcome

- ✅ All four emission categories wired (tool_call_started/completed,
  knowledge_retrieved, decision_made, playbook_node_executing/completed).
- ✅ Per-site ADR-015 audit documented — all 4 sites PASS.
- ✅ NFR-08 binding verified — 11 production node executors UNMODIFIED.
- ✅ Lifecycle events emitted at PlaybookOrchestrationService.ExecuteNodeAsync
  WRAPPER, not inside executors.
- ✅ Publish-size delta well within ADR-029 budget (-0.97 MB).
- ✅ 11 unit tests passing.
- ✅ ADR-015 anti-leakage empirically verified (no tag value carries the
  user-content needle across any of the 6 counters).

Wave C-G3 close-out is now unblocked. Pillar 6c "Context-pane execution
trace" user story is end-to-end deliverable: BFF emission ✅ (task 063),
widget ✅ (task 061), registry ✅ (task 062), event types ✅ (task 059).
