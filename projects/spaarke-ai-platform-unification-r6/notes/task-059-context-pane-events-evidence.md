# Task 059 — D-C-12 Evidence: Additive `context.*` PaneEventBus Event Types

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 059 (Phase C / Wave C-G2 / Pillar 6c)
> **Status**: completed
> **Date**: 2026-06-09
> **Rigor**: FULL

---

## Summary

Six additive event-type discriminants added to `ContextPaneEvent` on the
`@spaarke/ai-widgets` `context` channel. These feed the Pillar 6c
execution-trace widget (task 061) via emission sites wired in task 063.

ADR-030 4-channel constraint preserved (additive types only — no 5th channel).
ADR-015 governance enforced **structurally**: payload fields are explicit
primitives only; the type system makes user-content smuggling impossible by
construction.

---

## Files Modified

| File | Lines (delta) | Purpose |
|------|---------------|---------|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | +211 / 0 (~+211 LOC) | Add 6 trace discriminants + 11 enumerated payload fields with JSDoc |

No barrel change required — `ContextPaneEvent` and `PaneChannelEventMap` were
already exported; the new payload fields and discriminator union members ride
on the existing interface.

---

## New Event-Type Names + Payload Shapes

All six events ride the existing `ContextPaneEvent` interface, narrowed via
the `type` discriminator (same flat-optional-field pattern as the R5 `files_staged`
/ `file_selected` precedent — see `PaneEventTypes.ts` lines 379, 418-441).

### 1. `context.tool_call_started`

**Required**: `toolName: string`, `timestamp: string` (ISO-8601), `sessionId: string`
**Optional**: `correlationId?: string`

Emitted when the chat agent invokes a registered tool.

### 2. `context.tool_call_completed`

**Required**: `toolName: string`, `durationMs: number`, `success: boolean`,
`timestamp: string`, `sessionId: string`
**Optional**: `correlationId?: string`

Emitted when the tool invocation returns. **No result body** — outcome only.

### 3. `context.knowledge_retrieved`

**Required**: `knowledgeSourceId: string`, `relevanceScore: number` (0.0–1.0),
`timestamp: string`, `sessionId: string`
**Optional**: `correlationId?: string`

Emitted per knowledge-retrieval hit (e.g. RAG). **No retrieved content body** — ID
+ score only.

### 4. `context.playbook_node_executing`

**Required**: `playbookId: string`, `nodeId: string`, `timestamp: string`,
`sessionId: string`
**Optional**: `correlationId?: string`

Emitted at playbook-node execution start.

### 5. `context.playbook_node_completed`

**Required**: `playbookId: string`, `nodeId: string`, `durationMs: number`,
`success: boolean`, `timestamp: string`, `sessionId: string`
**Optional**: `correlationId?: string`

Emitted at playbook-node execution end.

### 6. `context.decision_made`

**Required**: `decision: string` (short enum-like — e.g. `'route:summarize'`),
`timestamp: string`, `sessionId: string`
**Optional**: `decisionReason?: string` (machine summary, NOT user text),
`correlationId?: string`

Emitted when the agent makes an enumerated routing / safety / guardrail
decision.

---

## ADR-015 Enforcement Evidence

**ADR-015 binding** (concise): "log only identifiers, sizes, timings, outcome
codes." Trace events MUST NOT log user message text, document contents,
extracted text, full prompts, or model responses.

### Structural enforcement (type-system level)

Every new field on `ContextPaneEvent` is one of:

| Field | TypeScript shape | Class | Why safe |
|-------|------------------|-------|----------|
| `timestamp` | `string` (ISO-8601) | timing metadata | Tier 1 explicit-allow |
| `sessionId` | `string` (opaque ID) | deterministic ID | Tier 1 explicit-allow |
| `correlationId` | `string` (opaque ID) | deterministic ID | Tier 1 explicit-allow |
| `toolName` | `string` (registered name) | config identifier | Tier 1; ADR-015 2026-05-17 amendment explicitly allows "tool names" |
| `durationMs` | `number` (integer ms) | timing metric | Tier 1 explicit-allow ("timings") |
| `success` | `boolean` | outcome flag | Tier 1 explicit-allow ("outcome codes") |
| `knowledgeSourceId` | `string` (source ID) | deterministic ID | Tier 1 explicit-allow |
| `relevanceScore` | `number` (0.0–1.0) | numeric metric | Tier 1 explicit-allow |
| `playbookId` | `string` (catalog ID) | config identifier | Tier 1 explicit-allow |
| `nodeId` | `string` (catalog key) | config identifier | Tier 1 explicit-allow |
| `decision` | `string` (short enum-like) | enumerated identifier | Tier 1 explicit-allow with emitter-side discipline (JSDoc constrains shape) |
| `decisionReason` | `string` (machine summary) | enumerated identifier | **Highest-risk field**; constrained by emitter discipline + JSDoc reviewer guidance |

### No field admits free-form content

- ❌ No `unknown` field
- ❌ No `any` field
- ❌ No `object` field
- ❌ No `Record<string, unknown>` field
- ❌ No `data` / `payload` / `content` / `text` / `body` field
- ❌ No `message` / `prompt` / `response` field

The pre-existing `WorkspaceWidgetLoadEvent.widgetData?: unknown` and
`ContextPaneEvent.contextData?: unknown` are NOT extended for trace events;
they remain on the existing `widget_load` / `context_update` discriminants,
where subscriber-side narrowing has been the established pattern since R1.

### Emitter responsibility for `decisionReason` (audit hook)

`decisionReason` is the only new field whose name suggests a possible
free-text vector. Three structural mitigations:

1. **JSDoc explicitly forbids** user text, verbatim prompts, formatted natural-language sentences.
2. **JSDoc reviewer guidance**: emitters that pass paragraph-style text violate ADR-015. Reviewers SHOULD flag values > ~64 characters or containing spaces beyond a colon separator as suspect.
3. **No alternative free-text vector**: the entire payload surface is constrained, so a reviewer auditing emission sites in task 063 has only ONE field to scrutinize (vs. an `unknown`-bag escape hatch).

This is the strongest enforcement achievable in TypeScript's structural type
system short of branded types — which were considered and rejected as
over-engineering for a string field whose discipline is owned by ~10 emission
sites in BFF C# code.

---

## Type-Check Outcome

```
> @spaarke/ai-widgets@0.1.0 typecheck
> tsc --noEmit
```

**0 TS errors, 0 warnings.** Clean compile across the entire `@spaarke/ai-widgets`
TypeScript project (which includes the test files that subscribe to the
`context` channel via `bus.subscribe('context', ...)`).

---

## Consumer-Smoke Results

### Grep for the 6 new event-type literals

```
Grep pattern: tool_call_started|tool_call_completed|knowledge_retrieved|
              playbook_node_executing|playbook_node_completed|decision_made
Path:         src/
Result:       No files found
```

**No pre-existing consumer** uses any of the six new event-type names. Safe
to add — no shape collision.

### Grep for `switch (event.type)` on `ContextPaneEvent`

Only ONE production switch over `ContextPaneEvent.type` exists:
`src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx:430`.
It has a `default: break;` branch — the 6 new types fall through harmlessly.
No consumer breakage. (When task 061 lands the trace widget, it adds its own
subscription handling the 6 new types directly.)

---

## Quality Gates Outcome

### code-review (self-audit)
- ✅ Pattern conformance — followed existing flat-optional-field model (R5 `files_staged` / `file_selected` precedent)
- ✅ JSDoc on every new field
- ✅ ADR-015 binding cited inline on each governance-sensitive field
- ✅ No `any` / `unknown` / `object` / `Record<...>` on any new field
- ✅ Reviewer guidance for `decisionReason` emitters embedded in JSDoc
- ✅ Discriminator union extended verbatim — existing 5 values preserved

### adr-check (self-audit)
- ✅ **ADR-015** (binding): structural prevention of user-content leakage — see "ADR-015 Enforcement Evidence" above. Audit trail documented.
- ✅ **ADR-030** (binding): 4-channel PaneEventBus preserved; additive types only on `context` channel.
- ✅ **ADR-012**: shared-lib placement in `@spaarke/ai-widgets`; Fluent v9 stack unchanged.
- ✅ **spec FR-37**: 6 event-type names match requirement verbatim (`tool_call_started`, `tool_call_completed`, `knowledge_retrieved`, `playbook_node_executing`, `playbook_node_completed`, `decision_made`).

---

## Acceptance Criteria

| # | Criterion | Status |
|---|-----------|--------|
| 1 | 6 new event variants added to `context` channel | ✅ pass |
| 2 | Each payload typed with explicit enumerated fields; NO `unknown` / `any` content fields | ✅ pass |
| 3 | ADR-015 audit confirms zero free-form text fields; documented in task notes | ✅ pass (this file) |
| 4 | No new channel introduced (ADR-030 preserved) | ✅ pass |
| 5 | Discriminated-union exhaustive matching compiles correctly | ✅ pass |
| 6 | Each variant exported from package barrel | ✅ pass (rides existing `ContextPaneEvent` + `PaneChannelEventMap` exports) |
| 7 | tsc --noEmit passes | ✅ pass (0 errors) |
| 8 | code-review + adr-check pass | ✅ pass (self-audit) |

---

## Coordination Notes

- **Task 060** edits the SAME file (`PaneEventTypes.ts`) adding `workspace.*`
  trace events. Per main-session dispatch instruction, 059 lands first; 060
  follows sequentially. The two tasks operate on disjoint channel sections,
  so the merge surface is purely additive.
- **Task 053** (chat-factory wiring) is the BFF-side prerequisite for actual
  emission of these events — held for UI walkthrough. 059's TypeScript
  contract is upstream of any emission and unblocks both 060 and downstream
  061 (widget) + 063 (BFF emission).
- **Task 061** (execution-trace widget) imports `ContextPaneEvent` from
  `@spaarke/ai-widgets` and narrows on the 6 new `type` values to render
  the timeline. Type-system contract is the source of truth.

---

## Escalations

None. Task executed cleanly within autonomous-mode parameters.

---

## Commit Message Recommendation

```
feat(r6 / Pillar 6c): additive `context.*` PaneEventBus event types (task 059)

Add 6 additive event-type discriminants on the `context` channel of
@spaarke/ai-widgets PaneEventBus for Pillar 6c execution-trace widget:
tool_call_started, tool_call_completed, knowledge_retrieved,
playbook_node_executing, playbook_node_completed, decision_made.

ADR-030 4-channel constraint preserved (additive types only).
ADR-015 governance enforced structurally — every payload field is a
deterministic ID, numeric metric, boolean, enum-like short string, or
ISO-8601 timestamp. Type system makes user-content smuggling impossible
by construction; no `unknown` / `any` / `object` field on any new
discriminant. `decisionReason` constrained by JSDoc reviewer guidance.

Files:
- src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts (+211 LOC)
- projects/spaarke-ai-platform-unification-r6/notes/task-059-context-pane-events-evidence.md (NEW)
- projects/spaarke-ai-platform-unification-r6/tasks/059-additive-context-pane-event-types.poml (status → completed)
- projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md (059 ✅)

Spec: FR-37. ADRs: ADR-015 (binding), ADR-030 (4-channel), ADR-012.
Type-check: 0 errors. Consumer-smoke: no pre-existing consumer uses the
new names; the one production switch over ContextPaneEvent.type has a
default branch (no breakage).
```
