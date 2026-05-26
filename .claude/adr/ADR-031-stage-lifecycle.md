# ADR-031: Stage Lifecycle Pattern (Concise)

> **Status**: Accepted
> **Date**: 2026-05-26
> **Domain**: Client Architecture — SpaarkeAi shell + widget lifecycle
> **Related**: ADR-012, ADR-021, ADR-022, ADR-030 (PaneEventBus), ADR-028
> **Renumbering note**: Drafted as ADR-026 in R4 plan; renumbered to ADR-031 on 2026-05-26 (ADR-026 in use by `ADR-026-full-page-custom-page-standard.md` — Code Page Build Standard, widely cross-referenced).

---

## Decision

The SpaarkeAi three-pane shell and its embedded widgets operate under a **four-stage lifecycle** with deterministic transitions computed from a single `SessionState` snapshot. Stage is **always** derived by the pure function `determineStage()` in `@spaarke/ai-widgets`. Transitions are driven by `PaneEventBus` events (ADR-030). The `ShellStageManager` (private to `ThreePaneShell.tsx`) is the single state owner; child panes consume `currentStage` via `useShellStage()`.

## The Four Stages

| Stage | Code value | Trigger to enter | Pane content (summary) |
|-------|------------|------------------|------------------------|
| 1 | `welcome` | Initial mount; `session_reset` | Welcome + prompt buttons / playbook gallery |
| 2 | `loading` | `playbook_change` OR `first_message` | Agent initialized, awaiting entity/document |
| 3 | `active-chat` | `widget_load` (first tab) OR `entity_resolved` | Single active widget; live chat |
| 4 | `review` | `tab_count_change` with `tabCount >= 2` | Tabbed workspace; context follows active tab |

Reverse transitions: Stage 4 → Stage 3 on `tab_count_change` with `tabCount === 1`. Any stage → Stage 1 on `session_reset` (uses `shouldReset()` predicate).

## Canonical algorithm

```typescript
// src/client/shared/Spaarke.AI.Widgets/src/interactions/StageTransitionRules.ts
export function determineStage(state: SessionState): PaneStage {
  if (!state.hasSession)              return "welcome";       // priority 1
  if (state.tabCount >= 2)            return "review";        // priority 2
  if (state.hasWidget || state.hasEntity) return "active-chat"; // priority 3
  return "loading";                                           // priority 4
}
```

`SessionState` fields: `hasSession` (session OR playbook active), `hasWidget` (≥1 resolved tab), `tabCount` (open tabs), `hasEntity` (entity context resolved).

---

## Constraints

### ✅ MUST

- **MUST** use `determineStage()` from `@spaarke/ai-widgets` for any code that needs to know the current stage — do not re-implement the algorithm
- **MUST** drive transitions through `PaneEventBus` events on the documented channels (`workspace`, `conversation`) — do not mutate `currentStage` directly from a pane
- **MUST** consume stage via `useShellStage()` inside the `ThreePaneShell` subtree — do not pass `stage` as a prop chain
- **MUST** keep `SessionState` updates inside `ShellStageManager` only — one writer, many readers
- **MUST** re-fetch live widget data on session restore (R2 D-08 — "data-refreshed restore"); the persisted `stage` field is advisory and client-side recompute always wins
- **MUST** preserve the priority order in `determineStage()`: `welcome` first, `review` second, `active-chat` third, `loading` last
- **MUST** name stages exactly `welcome | loading | active-chat | review` — these strings appear in persisted `SessionRestoreSpec.stage` and in widget state serialization

### ❌ MUST NOT

- **MUST NOT** invent a new stage (e.g. `error`, `refresh`, `ready`) — errors are surfaced inside the active stage; widget refreshes own their internal loading state
- **MUST NOT** read `stage` from `SessionRestoreSpec` as authoritative — recompute via `determineStage(sessionState)` after applying the restore spec
- **MUST NOT** call `setCurrentStage()` from a child pane — go through `ShellStageContext` callbacks (`toLoading()`, `toActiveChat()`, etc.) so `ShellStageManager` stays the single writer
- **MUST NOT** subscribe to `PaneEventBus` events purely to track stage in a pane — read `useShellStage()` instead
- **MUST NOT** debounce stage transitions externally — the referential-equality guard in `recompute()` already suppresses no-op renders; rapid tab churn is expected to flap and consumers must tolerate it
- **MUST NOT** persist any widget-internal lifecycle ("loading data" / "error fetching") as a shell stage — that's a widget concern, not a shell concern

---

## Code pointers

- **Pure logic**: `src/client/shared/Spaarke.AI.Widgets/src/interactions/StageTransitionRules.ts` — `determineStage()`, `shouldReset()`, types `PaneStage` + `SessionState`
- **React wiring**: `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — `ShellStageManager`, `ShellStageContext`, `useShellStage()`
- **Event types**: `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — channel + event payload contracts
- **Restore fetch**: `src/solutions/SpaarkeAi/src/hooks/useSessionRestore.ts` — `SessionRestoreSpec` with `stage` + `widgetStates`
- **Consumers**: `WorkspacePane.tsx`, `ConversationPane.tsx`, `ContextPaneController.tsx`

---

## Quick decision guide

| Question | Answer |
|----------|--------|
| Where do I read the current stage? | `useShellStage().currentStage` |
| How do I move to the next stage? | Dispatch the appropriate `PaneEventBus` event from the pane that has the trigger (e.g. `dispatch('workspace', { type: 'widget_load', ... })`) |
| Can I add a new stage? | No — propose an amendment to this ADR first; the four stages are bounded by design |
| What if my widget fails to load? | Surface an error inside the current stage's pane; do NOT change the shell stage |
| Should I trust `SessionRestoreSpec.stage`? | No — recompute via `determineStage()` after applying the restore spec |

---

## Amendments

*Reserved for future amendments. Task R4-016 (D-2) will append a "Heavy library handling" subsection covering singlefile-vs-lazy-import incompatibility (R3 bundle-size investigation).*

---

**Full form**: [docs/adr/ADR-031-stage-lifecycle.md](../../docs/adr/ADR-031-stage-lifecycle.md)

**Lines**: ~115
