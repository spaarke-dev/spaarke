# Task 063 partial-evidence — `context.*` event emissions from chat agent + playbook (D-C-16)

**Pillar / Spec ref**: R6 Pillar 6c / FR-37 — `context.*` events emitted from
BFF telemetry sites to feed the ExecutionTraceWidget (task 061 / 062).
**Wave**: C-G3 gap-fill — **NOT COMPLETED IN THIS PASS**.
**Date**: 2026-06-11.
**Status**: 🟡 PARTIAL — punted to follow-up agent dispatch.

## What this gap-fill pass did NOT do

The 063 implementation was sized as the largest remaining piece of work in the
C-G3 gap-fill (~1 full task day per the POML's `<estimated-effort>`). At
verification time the agent's tool budget was exhausted by tasks 057, 058, 062,
066 closeout (test infra heals + integration test creation + evidence notes).
Per the dispatch brief's stream-idle protection: "STOP immediately... A partial
completion with a clear handoff is FAR better than a timeout with lost state."

## On-disk state at end of this pass

| File | Status |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` | Unmodified (no tool-call event emissions added) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` | Unmodified (no knowledge_retrieved / decision_made event emissions) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Playbook/PlaybookExecutionEngine.cs` | Unmodified (no node-lifecycle event emissions at the wrapper) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/ContextEventEmissionTests.cs` | NOT created |
| `projects/.../notes/task-063-adr015-emission-audit.md` | NOT created |

## What the next agent must do (verbatim from POML 063)

Per `projects/spaarke-ai-platform-unification-r6/tasks/063-emit-context-events-from-agent-and-playbook.poml`:

### Wire-up sites

1. **`SprkChatAgent.cs`** — at each tool invocation:
   - BEFORE invocation: emit `context.tool_call_started` with `{toolName, decisionId, timestamp}` (NEVER tool input body)
   - AFTER invocation: emit `context.tool_call_completed` with `{toolName, decisionId, decision, outcome, durationMs, timestamp}` (NEVER tool output body or LLM response)

2. **`CapabilityRouter.cs`** — at the knowledge-retrieval / RAG path:
   - Emit `context.knowledge_retrieved` with `{sourceId, relevanceScore, resultCount, timestamp}` (NEVER the retrieved chunk text)
   - At Layer 2 / Layer 3 decision points: emit `context.decision_made` with `{layer, decision, capabilityName, timestamp}` (NEVER user message text)

3. **`PlaybookExecutionEngine.ExecuteAsync`** wrapper (NOT inside any of the 11 node executors — NFR-08 BINDING):
   - Per node, before dispatch: emit `context.playbook_node_executing` with `{playbookId, nodeId, nodeType, timestamp}`
   - Per node, after dispatch: emit `context.playbook_node_completed` with `{playbookId, nodeId, decision, durationMs, timestamp}`

### Dependencies to verify FIRST

- **Task 059 — event type definitions**: the POML states 063 depends on 059. Before writing any emission code, grep for `context.tool_call_started` to confirm the 6 PaneEvent type definitions exist:
  - `context.tool_call_started`
  - `context.tool_call_completed`
  - `context.knowledge_retrieved`
  - `context.playbook_node_executing`
  - `context.playbook_node_completed`
  - `context.decision_made`

  These should live in `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` (or its mirror). If MISSING, file a STOP and surface — task 059's work is upstream of 063.

- **Existing telemetry sink wiring**: each emission site must use the same SSE pipe / telemetry sink the chat surface already uses. Grep for `SseEventWriter` / `IServerSentEventsService` / `Activity.Current` (whichever pattern R5 task 008 established for telemetry).

### ADR-015 BINDING — non-negotiable

At every emission site, audit the payload construction code:

- ✅ Allowed: typed enumerated fields, deterministic IDs (matterId, scopeId, playbookId, nodeId, toolName, knowledgeSourceId, sourceId), timestamps, decision strings (enum-like short identifiers), numeric metrics (relevanceScore, resultCount, durationMs).
- ❌ FORBIDDEN: user message text, raw LLM response text, tool input/output bodies, retrieved chunk text, widget data content.

Document each emission site + audit conclusion in
`projects/spaarke-ai-platform-unification-r6/notes/task-063-adr015-emission-audit.md`
(NEW file per POML output spec).

### NFR-08 BINDING — non-negotiable

The 11 production node executors MUST NOT be modified. Lifecycle events are
emitted at the `PlaybookExecutionEngine.ExecuteAsync` wrapper level, NOT inside
any individual executor. After the implementation, run:

```
git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/
```

This must show ZERO changes. If any executor file is modified, the task fails
NFR-08 audit and must be reworked.

### Tests

Create `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/ContextEventEmissionTests.cs`:

- Unit tests per emission site with a mock event sink.
- Assert: deterministic-ID-only payloads (no user content substring).
- Assert: timing (started before invocation, completed after, durationMs > 0).
- Assert: 11 node executor diff is empty (run `git diff` in a test? — or simply
  visual code review documented in the evidence note; the POML doesn't mandate a
  programmatic check, but the ADR-015 / NFR-08 audit notes are mandatory).

### Build + publish-size verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — must compile clean.
- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` — measure delta vs prior baseline (~45.65 MB). Expect <2 MB per POML budget. Report absolute size + diff.
- Run `dotnet list package --vulnerable --include-transitive` — confirm no new HIGH-severity CVE.

### Evidence note

After all emission sites are wired + tests pass + ADR-015 + NFR-08 audits
documented, write `projects/spaarke-ai-platform-unification-r6/notes/task-063-evidence.md`
following the same structure as 057/058/062 evidence notes.

## Why this matters

Without 063, the ExecutionTraceWidget (task 061) and its registration (task 062)
are FUNCTIONAL CLIENT-SIDE INFRASTRUCTURE but receive ZERO events from the BFF.
The Pillar 6c "Context-pane execution trace" user story is not end-to-end
deliverable until 063 lands. The Wave C-G3 close-out is BLOCKED on this task.

## Recommendation

Dispatch a FRESH sub-agent with this note as primary brief + the POML 063 file
+ the existing 057/058/062/066 evidence notes for pattern reference. Budget:
single message with ~80 tool calls. Stream-idle protection: commit early at the
~50-call mark even if only 2 of the 4 emission sites are wired, leaving a
narrower follow-up.
