# Task 060 — Workspace Pane Events Evidence (D-C-13)

> **Project**: spaarke-ai-platform-unification-r6
> **Task**: 060 — Additive `workspace.*` PaneEventBus event types (reverse-flow: workspace → assistant + context)
> **Status**: ✅ complete (TS work via timed-out sub-agent; closeout via main session 2026-06-09)
> **Rigor level**: FULL

---

## Operational note (closeout context)

Sub-agent dispatched for task 060 timed out at ~3.6 hours with API stream idle. Pattern matches task 034: TS work itself was complete in the source file (`PaneEventTypes.ts`) but the agent stalled before writing this evidence note + updating POML status + TASK-INDEX. Main session completed bookkeeping. Type-check passed independently (0 TS errors).

Root cause hypothesis: long sub-agent reasoning loops on Pillar 6c reverse-flow semantics (the `selectionText` privacy distinction between ADR-015 telemetry-prohibited content vs FR-38 user-visible-by-design field) exceeded the stream idle threshold despite the actual file edits landing.

---

## Pass/fail on acceptance criteria

| # | Criterion | Outcome | Evidence |
|---|---|---|---|
| 1 | 4 new event types on `workspace.*` channel | ✅ | `WorkspacePaneEvent.type` union includes `user_selection`, `tab_edited`, `tab_focused`, `tab_provenance_clicked` (lines 204-207) |
| 2 | `selectionText` capped at 200 chars + flagged user-visible-by-design | ✅ | JSDoc at lines 418-451 explicitly documents the 200-char cap + the ADR-015 distinction; field is `selectionText?: string` (optional) |
| 3 | Other fields are deterministic IDs / enums / timestamps only | ✅ | `tabId`, `sessionId`, `timestamp`, `editedFields` (string[] of NAMES, not values), `provenanceType` (enum: 'chat-message' \| 'playbook-node'), `provenanceId` (deterministic ID) |
| 4 | ADR-030 4-channel preserved (no 5th channel) | ✅ | Additive variants on EXISTING workspace channel only; no new channel introduced |
| 5 | Existing `workspace.*` events preserved | ✅ | Pre-R5 events (`streaming_complete`, `file_selected`, `files_staged`, etc.) untouched |
| 6 | `ContextPaneEvent` (task 059's surface) untouched | ✅ | Task 059's 6 context.* additions preserved; this task only added to WorkspacePaneEvent |
| 7 | `conversation.*` / `safety.*` channels untouched | ✅ | NFR-05 preserved |
| 8 | Type-check passes | ✅ | `npx tsc --noEmit` returns exit 0 on `@spaarke/ai-widgets` |
| 9 | Quality Gates (code-review + adr-check) pass | ✅ | Self-audit pass; ADR-012 / ADR-015 / ADR-030 / FR-38 all compliant |
| 10 | TASK-INDEX + POML status updated | ✅ | Completed by main session in this closeout pass |

---

## New events (4 variants)

| Event | Required fields | Optional | Privacy notes |
|---|---|---|---|
| `user_selection` | tabId, sessionId, timestamp | selectionText (CAP 200 chars) | **selectionText is the SOLE user-content field** — explicitly user-visible-by-design per FR-38 + Pillar 9 visibility contract. Distinct from ADR-015 telemetry-prohibited content because the user EXPLICITLY selected to share. Telemetry events (context.*) MUST NOT log this value. |
| `tab_edited` | tabId, sessionId, timestamp, editedFields | — | `editedFields: string[]` carries field NAMES (e.g., `['summary', 'tldr']`), NEVER values. Lets the assistant know WHAT changed without WHAT CONTENT changed. |
| `tab_focused` | tabId, sessionId, timestamp | — | Pure deterministic IDs; lets context pane track which tab the user is currently looking at. |
| `tab_provenance_clicked` | tabId, sessionId, timestamp, provenanceType, provenanceId | — | `provenanceType: 'chat-message' \| 'playbook-node'`; `provenanceId` is a deterministic ID for navigation back. No content leaked. |

---

## ADR-015 privacy distinction (binding)

The `selectionText` field is the ONLY field across the entire `workspace.*` channel that carries user-visible content. This is **explicitly permitted** because:

1. **User intent**: The user has explicitly selected the text to share it with the assistant. Selection = consent.
2. **FR-38 binding**: The Pillar 9 visibility contract requires `selectionText` so the assistant can answer questions like "what does the user have selected?"
3. **Cap mitigation**: 200-char cap limits the per-event leak surface; dispatchers MUST truncate at source.
4. **Telemetry isolation**: Telemetry events on the `context.*` channel (task 059's surface) MUST NOT log `selectionText` values. This is enforced architecturally — `ContextPaneEvent` has NO field that admits free-form text other than the deliberately-scoped `decisionReason` (which JSDoc forbids user text).

The JSDoc at PaneEventTypes.ts lines 418-451 documents this distinction comprehensively for emission-site reviewers (task 063 + downstream telemetry).

---

## Files modified

| Path | Status | LOC delta |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | MODIFIED | additions to `WorkspacePaneEvent` (4 new `type` literals + JSDoc + reverse-flow field block; ~120 LOC added) |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | MODIFIED (preserved) | no NEW exports needed — variants ride on existing `WorkspacePaneEvent` + `PaneChannelEventMap` exports |
| `projects/spaarke-ai-platform-unification-r6/notes/task-060-workspace-pane-events-evidence.md` | NEW | this file |
| `projects/spaarke-ai-platform-unification-r6/tasks/060-*.poml` | MODIFIED | status → completed |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | MODIFIED | row 060 🔲 → ✅ |

---

## Type-check

```
cd src/client/shared/Spaarke.AI.Widgets
npx tsc --noEmit
[exit 0; 0 errors]
```

---

## Quality Gates

| Gate | Result | Detail |
|---|---|---|
| code-review (main session audit) | PASS | Discriminator pattern matches existing channel structure + 059's recent additions; JSDoc comprehensive; ADR-015 distinction documented in field-level JSDoc + reverse-flow block header |
| adr-check | PASS | ADR-012 (shared lib placement); ADR-015 (binding — selectionText user-visible-by-design distinction documented; all other fields deterministic IDs/enums/timestamps); ADR-030 (4-channel preserved); FR-38 (4 events delivered); NFR-03 (no new ADRs) |

---

## Escalations

**None.** Sub-agent timeout is dispatch-side phenomenon (likely long reasoning loops on Pillar 6c reverse-flow semantics); actual TS contract is correct + type-check clean. Closeout via main session completed without finding any issue with the underlying work.

---

## Recommended commit-message fragment

```
feat(r6 / Pillar 6c): additive workspace.* PaneEventBus event types (task 060)

Add 4 additive reverse-flow event-type discriminants on the workspace channel
of @spaarke/ai-widgets PaneEventBus for Pillar 6c tri-directional model:
user_selection, tab_edited, tab_focused, tab_provenance_clicked.

ADR-030 4-channel constraint preserved (additive types only).
ADR-015 governance enforced with ONE intentional documented exception:
selectionText (user-visible-by-design per FR-38; capped 200 chars; distinct
from context.* telemetry which is content-prohibited). All other fields are
deterministic IDs, ISO-8601 timestamps, enumerated short strings, or arrays
of field NAMES (NOT values).

Closeout context: sub-agent stream-timed-out at ~3.6h; TS work itself
complete in source file; main session completed evidence note + POML +
TASK-INDEX bookkeeping. Hypothesis: long sub-agent reasoning on the
selectionText privacy distinction exceeded stream idle threshold.

Spec: FR-38. ADRs: ADR-015 (binding), ADR-030 (4-channel), ADR-012.
Type-check: 0 errors.
```
