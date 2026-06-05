# Task 016 — D2-06 PaneEventBus Additive Event Types — Evidence

**Status**: complete
**Date**: 2026-06-04
**Effort**: ~1h (sub-agent, code-authoring scope only)
**Rigor**: FULL (frontend type-union change consumed by 4 downstream R5 tasks)
**Scope note**: This evidence file is produced by the parallel-wave sub-agent.
The main session is responsible for npm builds, code-review / adr-check quality
gates, commits, and pushes — not this sub-agent.

---

## 1. File modified

| File | LOC delta (approx.) | Nature of change |
|---|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | +110 / 0 | Additive only: 3 new workspace literals + 2 new context literals + new optional fields under dedicated comment blocks |

**No other files modified.** The exhaustive-switch audit (§3 below) found zero
sites needing widening, so the file count remains 1.

### Diff summary

#### `WorkspacePaneEvent.type` union — 11 → 14 literals

Added (verbatim per R5 CLAUDE.md §3.4 / spec NFR-09):

- `'streaming_started'`
- `'field_delta'`
- `'streaming_complete'`

All 11 existing literals preserved unchanged. JSDoc on the union got 3 new
per-literal description lines matching the existing `—` formatting.

#### `WorkspacePaneEvent` new optional fields (under `// ── streaming fields ──`)

| Field | Type | Required when | Purpose |
|---|---|---|---|
| `streamId` | `string` (optional) | `streaming_started \| field_delta \| streaming_complete` | Correlates the three lifecycle events of one stream |
| `fieldPath` | `string` (optional) | `field_delta` | JSON path of target field (e.g. `"summary"`, `"parties[0].name"`) |
| `fieldContent` | `string` (optional) | `field_delta` | Delta content; appended in `sequence` order |
| `sequence` | `number` (optional) | `field_delta` | Monotonic ordering key per `streamId` |
| `completionStatus` | `'complete' \| 'declined' \| 'empty'` (optional) | `streaming_complete` | Terminal status |

Naming + types reconciled with task 017 (`StructuredOutputStreamWidget`) and
task 005 (BFF SSE `FieldDelta` variant of `AnalysisChunk`) per task 016 POML
Step 3 guidance.

#### `ContextPaneEvent.type` union — 3 → 5 literals

Added (verbatim per R5 CLAUDE.md §3.4 / spec NFR-09):

- `'files_staged'`
- `'file_selected'`

All 3 existing literals preserved unchanged. JSDoc on the union got 2 new
per-literal description lines matching the existing `—` formatting. Type union
reformatted from single-line to one-literal-per-line to match
`WorkspacePaneEvent` style now that there are 5 literals.

#### `ContextPaneEvent` new optional fields (under `// ── files_staged / file_selected fields ──`)

| Field | Type | Required when | Purpose |
|---|---|---|---|
| `stagedFileIds` | `string[]` (optional) | `files_staged` | Session-scoped IDs from `ChatSession.UploadedFiles[]` (max 20 per NFR-02) |
| `selectedFileId` | `string` (optional) | `file_selected` | The single file the user activated for preview / focused action |
| `selectionSource` | `'chip' \| 'context-card' \| 'preview'` (optional) | `file_selected` | UX hint for receiving widget |

Naming reconciled with task 018 (`FilePreviewContextWidget`), task 020
(chat-pane file UX), and task 021 (per-file "Summarize this only" affordance).

---

## 2. Channel-count confirmation (ADR-030 compliance)

Verified UNCHANGED at exactly 4:

- `PaneChannel` literal union: `'workspace' | 'context' | 'conversation' | 'safety'` (PaneEventTypes.ts:30)
- `PaneChannelEventMap` interface: maps exactly those 4 channels
  (PaneEventTypes.ts:450–455)
- `PaneEventBus.ts` constructor: `_channels` contains exactly 4 `Set` instances
  (`workspace`, `context`, `conversation`, `safety` at lines 69–74)

**Zero edits to `PaneEventBus.ts`.** Zero edits to `PaneChannel` or
`PaneChannelEventMap`. ADR-030 channel closure preserved.

---

## 3. Exhaustive-switch audit

### 3.1 Methodology

Repo-wide search across `src/` for:

- `switch (event.type)` / `switch(event.type)` over PaneEvent-typed callbacks
- `: never =` exhaustiveness sentinels in proximity to those switches
- `bus.subscribe('workspace' | 'context', …)` callsites
- `usePaneEvent('workspace' | 'context', …)` callsites

### 3.2 Findings — `switch (event.type)` over PaneEvent unions

| File | Line | Channel | Has `never` exhaustiveness? | Action required |
|---|---|---|---|---|
| `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` | 430 | `context` (`ContextPaneEvent`) | **NO** — uses `default: break;` | **None.** New literals fall through harmlessly to the default branch. Existing 3 cases unchanged. |

**That is the only `switch (event.type)` over a PaneEvent-typed callback in
the entire repo.**

### 3.3 Findings — other PaneEvent subscribers (if/else pattern, not switch)

Spot-checked 5 known subscribers (per POML Step 7 obligation):

| File | Line | Channel | Pattern | New-literal handling |
|---|---|---|---|---|
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | 542 | `workspace` | `if (event.type === "widget_load" …) … else if (event.type === "widget_update") … else if (event.type === "widget_action") …` | Narrows on known literals only. New `streaming_*` literals are not matched — handler returns without side effect. ✅ |
| `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` | 354 | `workspace` | `if/else if` over 4 known literals (`widget_load`, `tab_count_change`, `entity_resolved`, `session_reset`) | New `streaming_*` literals not matched — handler returns. ✅ |
| `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` | 498 | `workspace` (cross-pane listener) | Early-return guard: `if (event.type !== "tab_change") return;` | New `streaming_*` literals fall into the early-return — no field access on unknown discriminant. ✅ |
| `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` | 430 | `context` | `switch` with `default: break;` (see §3.2) | New `files_staged` / `file_selected` literals fall through. ✅ |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/conversation/SprkChatWidget.tsx` (and other widget subscribers) | various | mixed | All observed patterns narrow on `event.type === '<known>'` before accessing discriminant-scoped fields | Same back-compat outcome. ✅ |

### 3.4 Conclusion

**ZERO sites required widening.** The repo uses `if-else narrow-on-known` and
`switch with non-exhaustive default` patterns universally for PaneEvent
subscribers. No `const _: never = event.type` exhaustiveness sentinel exists
over `WorkspacePaneEvent` / `ContextPaneEvent` anywhere in the tree. Therefore
the new literals are **strictly additive** — existing subscribers ignore them
as documented in spec NFR-09.

Note: `: never =` sentinels DO exist in the repo (e.g.
`ContextPaneController.tsx:615` over `GetStartedCardId`,
`RichFilePreviewDialog.tsx:650` over `FilePreviewAction`, multiple in
`SemanticSearchControl`) but **none over PaneEvent types**. All are over
unrelated discriminant unions and require zero changes.

---

## 4. ADR-030 + R5 CLAUDE.md compliance statement

| Constraint | Compliance |
|---|---|
| **ADR-030**: PaneEventBus channels permanently closed at 4 | ✅ `PaneChannel` unchanged; `PaneEventBus._channels` unchanged at 4 |
| **ADR-030**: Event-type discriminants additive only | ✅ All 5 new literals appended to existing unions; zero existing literals edited |
| **ADR-030**: All payloads typed (no `any`; `unknown` acceptable for polymorphic) | ✅ All new fields typed (`string`, `number`, `string[]`, string-literal unions). Zero `any`. No new `unknown` introduced. |
| **R5 CLAUDE.md §3.1** (reuse mandate, prohibited list): no new PaneEventBus channel | ✅ Zero channel changes |
| **R5 CLAUDE.md §3.2**: R5 introduces no new feature flags | ✅ New event types are unconditional members of the typed union; zero conditional logic, zero new flags |
| **R5 CLAUDE.md §3.4**: exactly these 5 event types, verbatim | ✅ `workspace.streaming_started`, `workspace.field_delta`, `workspace.streaming_complete`, `context.files_staged`, `context.file_selected` — names match character-for-character |
| **spec NFR-07** (BFF publish-size): pure frontend, zero BFF impact | ✅ No `.cs` files touched; no BFF binary delta; publish-size delta = 0 MB |
| **spec NFR-09**: existing subscribers ignore unknown discriminants | ✅ Exhaustive-switch audit (§3) found zero sites requiring widening |
| **spec NFR-10** (additive principle to SSE protocol): N/A this task (no SSE change) | ✅ Confirmed not applicable; additive discipline noted for downstream tasks 005/017 |
| **ADR-010** DI minimalism: no service registrations | ✅ No DI changes |
| **Project style**: JSDoc density + `// ── X fields ──` comment blocks preserved | ✅ New fields follow the existing `wizard_step` pattern; new comment blocks mirror existing format |

---

## 5. Downstream unblocking

Per task 016 POML acceptance criteria, the following downstream R5 tasks are
**unblocked from this pre-req perspective** (they may still have other
dependencies — consult their POMLs):

| Task | Title | Consumes |
|---|---|---|
| 017 (D2-07) | `StructuredOutputStreamWidget` | SUBSCRIBES to all 3 new workspace literals |
| 018 (D2-09) | `FilePreviewContextWidget` | SUBSCRIBES to both new context literals |
| 020 (D2-11) | Chat-pane orchestration UX | DISPATCHES `context.files_staged` + `workspace.streaming_started/complete` |
| 021 (D2-12) | "Summarize this only" per-file affordance | DISPATCHES `context.file_selected` |

---

## 6. Out-of-scope (per sub-agent task framing)

The following items are explicitly NOT performed by this sub-agent (deferred to
the main session per parallel-wave protocol):

- `npm run build` on `@spaarke/ai-widgets` / `@spaarke/ui-components` / downstream solutions
- `code-review` skill at Step 9.5
- `adr-check` skill at Step 9.5
- `git add` / `git commit` / `git push`
- `TASK-INDEX.md` status update (016 🔲 → ✅)
- `current-task.md` reset

The main session will run these and confirm acceptance criteria 5–11 from the
task POML before marking the task fully complete.

---

*Generated 2026-06-04 by R5 task 016 sub-agent. Source-of-truth file:
`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts`.*
