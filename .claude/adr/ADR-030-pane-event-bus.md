# ADR-030: PaneEventBus — Typed Multi-Subscriber Cross-Pane Communication (Concise)

> **Status**: Accepted
> **Domain**: Client-side cross-pane communication (SpaarkeAi three-pane shell + future N-pane shells)
> **Last Updated**: 2026-05-26
> **Originating project**: `spaarke-ai-platform-unification-r4` (task A-2a) — codifies R2-shipped, R3-extended pattern (no behavior change)
> **Renumbering note**: Drafted as ADR-025 in R4 plan; renumbered to ADR-030 on 2026-05-26 (ADR-025 in use by `ADR-025-icon-library-and-deployment.md`).

---

## Decision

The **PaneEventBus** is the single authorized cross-pane communication primitive for the SpaarkeAi shell. Four typed channels (`workspace`, `context`, `conversation`, `safety`) carry discriminated-union event payloads. Multi-subscriber delivery via Set-based registry; no DOM dependency; React-integrated via one provider (`<PaneEventBusProvider>`) + two hooks (`usePaneEvent`, `useDispatchPaneEvent`).

**Invariants** (the bus has these properties and they MUST be preserved by any future implementation):

- Channels are a **closed union of exactly four members** — adding a fifth requires a successor ADR
- Event payloads are **typed via `PaneChannelEventMap`** — `dispatch('workspace', { safety_annotation })` is a compile error
- Multi-subscriber, insertion-order, **synchronous** delivery — no last-write-wins; subscribing the same handler twice is a no-op (Set dedup)
- Dispatch iterates a **snapshot** so handlers may unsubscribe during dispatch without mutating the set mid-iteration
- The bus instance is **stable for the provider's lifetime** (held in `useRef`)
- `usePaneEventBus()` (internal context-reader) is **NOT exported** from the package barrel — public surface is the two hooks only

---

## Channels (closed union — exactly four)

| Channel | Purpose | Source-file event interface |
|---|---|---|
| `workspace` | Widget lifecycle, tab navigation, wizard step control, stage transitions | `WorkspacePaneEvent` |
| `context` | Document/data context updates, citation highlights, extraction stage advance | `ContextPaneEvent` |
| `conversation` | User input signals, playbook selection, refinement requests | `ConversationPaneEvent` |
| `safety` | Groundedness annotations, capability availability changes | `SafetyPaneEvent` |

## Event-type discriminants (per channel — additive only)

- **`workspace`**: `widget_load`, `widget_update`, `widget_action`, `tab_change`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset`, `active_widget_changed`
- **`context`**: `context_update`, `context_highlight`, `stage_change`
- **`conversation`**: `suggestion`, `playbook_change`, `playbook-selected`, `refine_request`, `first_message`
- **`safety`**: `safety_annotation`, `capability_change`

New event types are added by extending the channel's `type` union and adding optional payload fields. Existing subscribers MUST continue to compile and run.

---

## Constraints

### ✅ MUST

- **MUST** keep all event payloads **typed** via the `PaneChannelEventMap` interface — no `any` payloads, ever
- **MUST** use `PaneChannel = 'workspace' | 'context' | 'conversation' | 'safety'` as a closed union — no fifth channel without a successor ADR
- **MUST** dispatch via `useDispatchPaneEvent()` from React; never instantiate `new PaneEventBus()` in widget code, never reach into context directly
- **MUST** subscribe via `usePaneEvent(channel, handler)` from React; never `useContext(PaneEventBusContext)` directly
- **MUST** have subscribers handle the event `type` values they care about and **ignore others** — discriminated-union narrowing per subscriber
- **MUST** add new event types as **additive** discriminants — existing subscribers must continue to compile and run after the addition
- **MUST** mount exactly one `<PaneEventBusProvider>` at the shell root unless intentional event-scope isolation is documented in JSDoc
- **MUST** preserve snapshot iteration during dispatch (`Array.from(set)`) so handlers unsubscribing during dispatch do not mutate the set mid-iteration

### ❌ MUST NOT

- **MUST NOT** use `any` in event payloads (use `unknown` if the field is genuinely polymorphic, with subscriber-side narrowing)
- **MUST NOT** add a fifth channel without a successor ADR amending this one
- **MUST NOT** instantiate `new PaneEventBus()` in component code (the provider owns the lifecycle)
- **MUST NOT** export `usePaneEventBus()` from the package barrel — it is encapsulation by design
- **MUST NOT** redefine an existing event-type discriminant's payload shape in a breaking way — payload changes are additive optional fields only
- **MUST NOT** use the bus for cross-tab / cross-iframe communication — use `BroadcastChannel` or `postMessage` for those
- **MUST NOT** use the bus for state (last-write-wins) — it is fire-and-forget event delivery; subscribers that mount after a dispatch do NOT receive prior events

---

## Code pointers

| File | Role |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` | Bus implementation (~80 LOC) |
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` | `PaneChannel` union + four `*PaneEvent` interfaces + `PaneChannelEventMap` |
| `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx` | React context + `<PaneEventBusProvider>` |
| `src/client/shared/Spaarke.AI.Widgets/src/events/usePaneEvent.ts` | Subscribe hook (handler-ref stabilization) |
| `src/client/shared/Spaarke.AI.Widgets/src/events/useDispatchPaneEvent.ts` | Dispatch hook (stable `useCallback` return) |
| `src/client/shared/Spaarke.AI.Widgets/src/events/index.ts` | Barrel export (public surface) |

## Canonical consumers (reference)

- **WorkspacePane tab bar** — R2 origin consumer of `workspace` channel
- **ShellStageManager** — consumes `workspace.tab_count_change`, `workspace.entity_resolved`, `workspace.session_reset`, `context.stage_change`, `conversation.first_message`, `conversation.playbook-selected` to drive Stage 1↔2↔3↔4 transitions
- **Calendar widget (R3 task 115)** — canonical Pattern D (shared-lib widget + thin LegalWorkspace shim) consumer of `workspace.widget_load`
- **R4 W-4 (AssistantPane mount source)** — upcoming consumer of `workspace.active_widget_changed` + `workspace.entity_resolved`
- **R4 W-5 (ContextPane mount source)** — upcoming consumer of `workspace.entity_resolved` + `context.context_update` + `context.context_highlight`

---

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-012](ADR-012-shared-components.md) | PaneEventBus lives in `@spaarke/ai-widgets` per ADR-012's shared-lib placement |
| [ADR-021](ADR-021-design-system.md) | All consumers are Fluent v9 React components |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | Bus is consumed by Code Pages (React 19); PCF does NOT consume the bus directly |
| ADR-031 (R4 task A-2b) | Stage lifecycle (ADR-031) consumes bus events as its primary signals |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-030-pane-event-bus.md](../../docs/adr/ADR-030-pane-event-bus.md) — Context (R1 → R2 → R3 evolution), Decision (binding shape), Consequences, Alternatives Considered (CustomEvent / Redux / Context / mitt / prop drill), Operationalization (verification commands), Canonical consumers, AI-Directed Coding Guidance.

---

**Lines**: ~120
