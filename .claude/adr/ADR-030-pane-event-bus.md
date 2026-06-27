# ADR-030: PaneEventBus — Typed Multi-Subscriber Cross-Pane Communication (Concise)

> **Status**: Accepted (v2 — amendment 2026-06-21 adds `memory` channel)
> **Domain**: Client-side cross-pane communication (SpaarkeAi three-pane shell + future N-pane shells)
> **Last Updated**: 2026-06-21
> **Originating project**: `spaarke-ai-platform-unification-r4` (task A-2a) — codifies R2-shipped, R3-extended pattern (no behavior change)
> **Amendment (2026-06-21)**: `spaarke-ai-platform-chat-routing-redesign-r1` adds `memory` channel as the 5th member to support the 6-tier memory subsystem (matter-memory promotion approval, future memory-domain events). Closed-union size revised from 4 → 5. Amendment record at the bottom of this document.
> **Renumbering note**: Drafted as ADR-025 in R4 plan; renumbered to ADR-030 on 2026-05-26 (ADR-025 in use by `ADR-025-icon-library-and-deployment.md`).

---

## Decision

The **PaneEventBus** is the single authorized cross-pane communication primitive for the SpaarkeAi shell. Five typed channels (`workspace`, `context`, `conversation`, `safety`, `memory`) carry discriminated-union event payloads. Multi-subscriber delivery via Set-based registry; no DOM dependency; React-integrated via one provider (`<PaneEventBusProvider>`) + two hooks (`usePaneEvent`, `useDispatchPaneEvent`).

**Invariants** (the bus has these properties and they MUST be preserved by any future implementation):

- Channels are a **closed union of exactly five members** — adding a sixth requires a successor ADR
- Event payloads are **typed via `PaneChannelEventMap`** — `dispatch('workspace', { safety_annotation })` is a compile error
- Multi-subscriber, insertion-order, **synchronous** delivery — no last-write-wins; subscribing the same handler twice is a no-op (Set dedup)
- Dispatch iterates a **snapshot** so handlers may unsubscribe during dispatch without mutating the set mid-iteration
- The bus instance is **stable for the provider's lifetime** (held in `useRef`)
- `usePaneEventBus()` (internal context-reader) is **NOT exported** from the package barrel — public surface is the two hooks only

---

## Channels (closed union — exactly five)

| Channel | Purpose | Source-file event interface |
|---|---|---|
| `workspace` | Widget lifecycle, tab navigation, wizard step control, stage transitions | `WorkspacePaneEvent` |
| `context` | Document/data context updates, citation highlights, extraction stage advance | `ContextPaneEvent` |
| `conversation` | User input signals, playbook selection, refinement requests | `ConversationPaneEvent` |
| `safety` | Groundedness annotations, capability availability changes | `SafetyPaneEvent` |
| `memory` | **(added 2026-06-21)** Memory-domain signals — matter-memory promotion approval requests, fact-promotion completion, pin lifecycle. Targeted by ContextPane approval UI subscribers and memory-aware widgets. | `MemoryPaneEvent` |

## Event-type discriminants (per channel — additive only)

- **`workspace`**: `widget_load`, `widget_update`, `widget_action`, `tab_change`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset`, `active_widget_changed`
- **`context`**: `context_update`, `context_highlight`, `stage_change`
- **`conversation`**: `suggestion`, `playbook_change`, `playbook-selected`, `refine_request`, `first_message`
- **`safety`**: `safety_annotation`, `capability_change`
- **`memory` (added 2026-06-21)**: `promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`

New event types are added by extending the channel's `type` union and adding optional payload fields. Existing subscribers MUST continue to compile and run.

### `memory` channel payload reference (initial)

```typescript
type MemoryPaneEvent =
  | { type: 'promotion_pending';   promotionId: string; factSummary: string; matterId: string; sessionId: string }
  | { type: 'promotion_resolved';  promotionId: string; decision: 'approved' | 'rejected'; factId?: string }
  | { type: 'fact_promoted';       factId: string; matterId: string; source: string }
  | { type: 'pin_added';           pinId: string; pinType: 'user-preference' | 'system-rule' | 'matter-fact' }
  | { type: 'pin_removed';         pinId: string };
```

Payloads carry deterministic IDs + summaries only (per ADR-015 tier-1 safety) — NEVER raw fact text, user message content, or recall results. `factSummary` is the first 80 chars of the candidate fact for UI display purposes only.

---

## Constraints

### ✅ MUST

- **MUST** keep all event payloads **typed** via the `PaneChannelEventMap` interface — no `any` payloads, ever
- **MUST** use `PaneChannel = 'workspace' | 'context' | 'conversation' | 'safety' | 'memory'` as a closed union — no sixth channel without a successor ADR (5th `memory` added by v2 amendment 2026-06-21)
- **MUST** dispatch via `useDispatchPaneEvent()` from React; never instantiate `new PaneEventBus()` in widget code, never reach into context directly
- **MUST** subscribe via `usePaneEvent(channel, handler)` from React; never `useContext(PaneEventBusContext)` directly
- **MUST** have subscribers handle the event `type` values they care about and **ignore others** — discriminated-union narrowing per subscriber
- **MUST** add new event types as **additive** discriminants — existing subscribers must continue to compile and run after the addition
- **MUST** mount exactly one `<PaneEventBusProvider>` at the shell root unless intentional event-scope isolation is documented in JSDoc
- **MUST** preserve snapshot iteration during dispatch (`Array.from(set)`) so handlers unsubscribing during dispatch do not mutate the set mid-iteration

### ❌ MUST NOT

- **MUST NOT** use `any` in event payloads (use `unknown` if the field is genuinely polymorphic, with subscriber-side narrowing)
- **MUST NOT** add a sixth channel without a successor ADR amending this one (5th `memory` added by v2 amendment 2026-06-21)
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

---

## Amendment Record

### v2 (2026-06-21) — `memory` channel addition

**Context.** The `spaarke-ai-platform-chat-routing-redesign-r1` project introduces a 6-tier memory subsystem (Working / Session / Matter / User-Org / Retrieval / Audit per `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` §3). Memory-domain UI signals — most prominently the matter-memory promotion approval workflow (architecture §6.4) — do not fit semantically into any of the original four channels. The reconciliation review (spec.md FR-32 / project clarification 2026-06-21) initially namespaced these as `workspace.promotion_pending` inside the `workspace` channel, which forced workspace-channel subscribers to ignore non-workspace events. Audit (this ADR amendment) elevates the memory event taxonomy to a dedicated channel.

**Change.**
- Channel union expanded from 4 → 5: add `memory`.
- New event interface `MemoryPaneEvent` introduced (initial 5 discriminants: `promotion_pending`, `promotion_resolved`, `fact_promoted`, `pin_added`, `pin_removed`).
- `PaneChannel` closed-union now `'workspace' | 'context' | 'conversation' | 'safety' | 'memory'`. Adding a sixth still requires a successor ADR.

**Constraints preserved.**
- All original invariants (typed payloads, multi-subscriber sync delivery, snapshot iteration, single-provider lifetime, hidden context reader) continue to hold for the `memory` channel.
- ADR-015 tier-1 safety binding extends to `memory` payloads: deterministic IDs + summaries only; NEVER raw fact text, user message content, or recall results. `factSummary` is the 80-char preview for UI display.
- Memory payloads MUST be tenant-scoped via context (sessions / matters carry tenantId implicitly through subscriber context, NOT in payload).

**Required implementation updates** (out of this ADR's scope — owned by project tasks):
1. `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — add `MemoryPaneEvent` interface + extend `PaneChannel` union + extend `PaneChannelEventMap`.
2. `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` — verify channel-string switches accept `memory` (Set-keyed registry is channel-string-agnostic; no code change expected).
3. ContextPane approval UI subscriber wires `usePaneEvent('memory', handleMemoryEvent)`.
4. `MatterMemoryPromotionService` dispatch site uses the new channel via `useDispatchPaneEvent()` boundary.

**Out of scope for v2 amendment**: removing R6-style namespaced `workspace.promotion_pending` legacy references in code (if any exist) — done as part of FR-32 task.

**Reviewer**: Project owner (2026-06-21).

**Future additive event types** (per channel) on `memory` MAY include (NOT binding here — additive freedom granted by §"Event-type discriminants" rule):
- `recall_completed` — if tools want to signal completion to UI for citation highlighting
- `session_memory_written` — if Tier-2 writes need UI awareness
- `preference_changed` — if T4 user preferences gain mutability inside chat scope (not currently in scope per FR-04 + architecture §3.2 rule 3)
