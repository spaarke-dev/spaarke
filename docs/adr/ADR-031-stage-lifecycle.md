# ADR-031: Stage Lifecycle Pattern (Pane / Code Page Shell)

> **Status**: Accepted
> **Date**: 2026-05-26
> **Domain**: Client Architecture — SpaarkeAi shell + widget lifecycle
> **Supersedes**: None
> **Related**: ADR-012 (shared components), ADR-021 (Fluent v9), ADR-022 (React 19 for Code Pages), ADR-030 (PaneEventBus pattern), ADR-028 (Spaarke Auth v2 — session restore contract)
> **Renumbering note**: Originally drafted as ADR-031 in the R4 project plan/spec. Renumbered to ADR-031 on 2026-05-26 after operator decision because ADR-031 is taken by `ADR-031-full-page-custom-page-standard.md` (Code Page Build Standard — Vite + singlefile + React 19; active, widely cross-referenced). Sister ADR (PaneEventBus pattern) renumbered to ADR-030 in the same operation. See `projects/spaarke-ai-platform-unification-r4/notes/adr-025-numbering-collision-2026-05-26.md` for the analysis + decision trail.

---

## Context

The SpaarkeAi three-pane Code Page and its embedded widgets share a four-stage lifecycle that governs which content each pane surfaces, which transitions are valid, and how state is restored across page reloads. This pattern was invented in R2 (multi-pane shell refactor) and exercised heavily in R3 across the Calendar widget, Daily Briefing, workspace tabs, and session restore (R3 D-08 — data-refreshed restore).

Before this ADR, the pattern lived only in code and in `design.md` of individual R2/R3 projects. Each new widget author had to re-derive the rules by reading `ThreePaneShell.tsx` and `StageTransitionRules.ts`, which led to:

- Widgets dispatching events on the wrong channel
- Widgets inventing new "ready / refresh / error" sub-states inconsistent with the four canonical stages
- Confusion about whether stage state should be persisted, recomputed, or restored

R4 DR-04 (governance docs round) requires codifying the pattern so future widget authors load one binding source.

The pattern is purely client-side and lives in `@spaarke/ai-widgets`. The Code Page shell (`ThreePaneShell.tsx` in `src/solutions/SpaarkeAi/`) consumes it and propagates the current stage to every pane via `ShellStageContext`.

---

## Decision

The SpaarkeAi shell and its embedded widgets operate under a **four-stage lifecycle** with deterministic transitions computed from a single `SessionState` snapshot. Stages are named `welcome | loading | active-chat | review` (R2 originals; preserved verbatim in code as `PaneStage`/`ShellStage`):

| Stage | Code value | Meaning | Pane content (high level) |
|-------|------------|---------|---------------------------|
| Stage 1 | `welcome` | Landing — no session, no playbook | Conv: welcome + prompt buttons. Workspace: "What would you like to work on?" + recent work. Context: playbook gallery + Get Started cards. |
| Stage 2 | `loading` | Playbook selected — gathering context | Conv: chat initialized with chosen agent, awaiting entity/document. Workspace: Upload / Browse / Recent. Context: entity info or loading spinner. |
| Stage 3 | `active-chat` | Active work — first document/widget loaded | Conv: SprkChat with live exchange. Workspace: single active widget. Context: findings, citations, sources. |
| Stage 4 | `review` | Multi-task — second workspace tab opened | Conv: stable. Workspace: tabbed widget view (`WorkspaceTabManagerComponent`). Context: adapts to active tab via `tab_change` events. |

### Stage determination (canonical algorithm)

Stage is **always** computed by the pure function `determineStage(state: SessionState): PaneStage` (in `src/client/shared/Spaarke.AI.Widgets/src/interactions/StageTransitionRules.ts`). The function is side-effect-free, React-independent, and unit-testable without a DOM.

```typescript
export interface SessionState {
  hasSession: boolean;   // session active OR playbook selected
  hasWidget:  boolean;   // at least one workspace tab resolved
  tabCount:   number;    // open workspace tabs
  hasEntity:  boolean;   // entity context (logicalName + id) resolved
}

// Priority order — first match wins:
//   1. !hasSession                  → 'welcome'
//   2. tabCount >= 2                → 'review'
//   3. hasWidget || hasEntity       → 'active-chat'
//   4. (hasSession, no widget yet)  → 'loading'
```

### Transition rules (event-driven, all routed through PaneEventBus)

| From | To | Trigger event | Notes |
|------|-----|---------------|-------|
| Stage 1 → Stage 2 | `welcome` → `loading` | `conversation/playbook_change` OR `conversation/first_message` | First user intent recorded |
| Stage 2 → Stage 3 | `loading` → `active-chat` | `workspace/widget_load` (first resolved tab) OR `workspace/entity_resolved` | First content available |
| Stage 3 → Stage 4 | `active-chat` → `review` | `workspace/tab_count_change` with `tabCount >= 2` | Multi-task entered |
| Stage 4 → Stage 3 | `review` → `active-chat` | `workspace/tab_count_change` with `tabCount === 1` | Back to single tab |
| Any → Stage 1 | `* → welcome` | `workspace/session_reset` (session cleared/deleted) | Hard reset; `shouldReset()` predicate covers this case |

### State manager (canonical wiring)

The transitions are wired exactly once per shell mount by **`ShellStageManager`** (a private component inside `ThreePaneShell.tsx`). `ShellStageManager`:

1. Holds the `SessionState` snapshot in a `useRef` (mutable; not React state — avoids re-render storms during high-frequency bus events).
2. Subscribes to relevant PaneEventBus channels (`workspace`, `conversation`).
3. On each event, mutates the relevant `SessionState` field and calls `recompute()`.
4. `recompute()` invokes `determineStage(sessionRef.current)` and updates the `currentStage` React state **only if** the value changed (referential-equality guard).
5. Exposes `currentStage` + explicit transition callbacks (`toLoading()`, `toActiveChat()`, `toReview()`, `toActiveWork()`, `reset()`) via `ShellStageContext`.

Child panes consume the stage via `useShellStage()` and adapt their content without prop-drilling.

### Persistence across reload

Per ADR-028 (Spaarke Auth v2) §H-4 session-restore contract, when a Code Page reloads with `?sessionId={id}` in the URL, `useSessionRestore` calls `GET /api/ai/chat/sessions/{sessionId}/restore` and receives a `SessionRestoreSpec` that includes:

- `stage: string` — the persisted stage at last save
- `widgetStates: Record<string, string>` — per-widget serialized state

`ThreePaneShell` applies the restore spec by:

1. Hydrating `AiSessionProvider` with the playbook + messages.
2. Re-fetching live widget data (per R2 D-08 "data-refreshed restore" — do NOT replay stale snapshots).
3. Letting `ShellStageManager` recompute the stage from the resulting `SessionState` rather than blindly trusting the persisted `stage` string.

Recomputed stage may differ from persisted stage if the underlying data shifted (e.g. tabs closed in another session). The canonical algorithm always wins; the persisted string is informational.

---

## Consequences

### Positive

- **Single source of truth.** `determineStage()` is the only place stage logic lives. Every pane reads the same value, eliminating race conditions and divergence.
- **Pure-function testable.** `StageTransitionRules.ts` is React-free; unit tests cover all five transition paths without DOM.
- **Event-driven.** Transitions are triggered by PaneEventBus events (ADR-030), so any pane can drive a transition without coupling to the shell.
- **Restoreable.** ADR-028 session restore + R2 D-08 data-refreshed pattern guarantees the stage after reload matches what the user left.
- **Bounded surface.** Four stages — not five, not a free-form state machine. Widget authors do not invent stages.

### Negative / trade-offs

- **No explicit error stage.** Errors are surfaced inside the active stage (e.g. an error banner in the workspace pane while still in `active-chat`). The lifecycle does not branch to a dedicated `error` stage. Rationale: errors are usually recoverable per-widget, not shell-wide; introducing an error stage would force every pane to handle a state with no meaningful content for it.
- **No explicit refresh stage.** A widget refreshing its data does NOT change the shell stage. The widget owns its own internal loading state. Rationale: shell stage is about content availability at the shell level, not at the widget level — otherwise every async fetch would trigger a stage flap.
- **Stage 4 (`review`) is fragile around tab churn.** Rapid open-then-close of a second tab can briefly trigger `active-chat → review → active-chat` round trips. The referential-equality guard in `recompute()` suppresses no-op renders, but external subscribers to `currentStage` may still observe transient values. Widget authors should not rely on stage transitions being debounced.
- **Persisted `stage` field is advisory only.** Client-side recompute always wins. This is intentional (data may have changed between save and restore) but means the BFF must NOT trust the persisted string as canonical.

---

## Alternatives Considered

### Alternative A — XState (full FSM library)

**Rejected**. The four-stage lifecycle is simple enough (4 stages, 5 transitions, single computation function) that XState's overhead (~30 KB minified, learning curve, additional indirection for hot paths) outweighed its benefits. The current `determineStage()` is ~30 lines of pure TypeScript and ships with the existing widget bundle.

### Alternative B — React `useState` per pane (no central manager)

**Rejected**. Each pane would have to subscribe to the same events and re-derive the stage independently. This was the R1 architecture and produced visible divergence bugs (workspace pane "ahead" of context pane during fast transitions). The R2 centralization via `ShellStageManager` fixed the bug; this ADR codifies that fix.

### Alternative C — Custom finite-state machine class

**Rejected**. Considered briefly. The pure function approach (`determineStage()` + `SessionState` snapshot) gives the same guarantees (deterministic, exhaustive) without the boilerplate of a class-based state machine. The `recompute()` pattern in `ShellStageManager` provides the React-state binding.

### Alternative D — Server-driven stage (BFF returns the stage on every event)

**Rejected**. Latency. Stage changes must propagate within one tick (~16 ms) to keep the UI responsive during rapid interactions (e.g. opening a tab while a playbook switches). A network round-trip would make the shell feel sluggish. Stage is a client concern.

---

## References

### Code

- `src/client/shared/Spaarke.AI.Widgets/src/interactions/StageTransitionRules.ts` — `determineStage()`, `shouldReset()`, `PaneStage`, `SessionState` (pure logic)
- `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — `ShellStageManager`, `ShellStageContext`, `useShellStage()` (React wiring)
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — event channel definitions (`workspace`, `conversation`, etc.)
- `src/solutions/SpaarkeAi/src/hooks/useSessionRestore.ts` — restore fetch + `SessionRestoreSpec`
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — consumer of `useShellStage()`
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — consumer of `useShellStage()`
- `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` — consumer of `useShellStage()`

### Related ADRs

- **ADR-012** — Shared component library. `@spaarke/ai-widgets` is the canonical home for `determineStage()` and stage types.
- **ADR-021** — Fluent v9. Pane content for each stage uses Fluent v9 tokens; no hex / no `rgba()`.
- **ADR-022** — React 19 for Code Pages. The shell uses `createRoot` and concurrent features; `ShellStageManager` relies on `useRef` for sub-tick mutation to avoid render flaps.
- **ADR-030** — PaneEventBus pattern. Every transition is driven by a typed event on a typed channel.
- **ADR-028** — Spaarke Auth v2 §H-4 session restore. Persisted stage state survives reload but is advisory; client-side recompute is canonical.

### Project history

- **R2** (multi-pane shell refactor) — invented the four-stage lifecycle and the `ShellStageManager` pattern. Original design captured in `projects/spaarke-ai-platform-unification-r2/design.md` Section 2.3.
- **R3 task AIPU2-105** — extracted `StageTransitionRules.ts` from `ThreePaneShell.tsx` and made it pure / unit-testable. Verified via the Calendar widget (R3 task 115) and Daily Briefing workspace.
- **R3 D-08** — "data-refreshed restore" principle. On session restore, re-fetch live widget data rather than replaying snapshots. This ADR inherits the principle.
- **R4 DR-04** — codified the pattern as an ADR (this document).

---

## Amendments

*Reserved for future amendments. Task R4-016 (D-2) will append a "Heavy library handling" subsection covering singlefile-vs-lazy-import incompatibility surfaced in the R3 bundle-size investigation.*

---

**Concise form**: [`.claude/adr/ADR-031-stage-lifecycle.md`](../../.claude/adr/ADR-031-stage-lifecycle.md)
