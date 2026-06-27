# ADR-030: PaneEventBus — Typed Multi-Subscriber Cross-Pane Communication

| Field | Value |
|-------|-------|
| Status | **Accepted (v2 — amendment 2026-06-21 adds `memory` channel)** |
| Date | 2026-05-26 (codification of R2-shipped, R3-extended pattern); amended 2026-06-21 |
| Authors | Spaarke Engineering |
| Originating project | `spaarke-ai-platform-unification-r4` (task A-2a) |
| Amending project | `spaarke-ai-platform-chat-routing-redesign-r1` — 6-tier memory subsystem introduces memory-domain events (matter-memory promotion approval, pin lifecycle); see Amendment History section |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-030 Concise](../../.claude/adr/ADR-030-pane-event-bus.md) — ~140 lines, MUST/MUST NOT rules + channel/event enumeration + code pointers

**When to load this full ADR**: designing a new PaneEventBus channel, adding a new event type to an existing channel, debugging a missed/duplicated event delivery, replacing the bus with a different communication primitive, evaluating whether a new feature should use the bus vs. a direct prop drill, or auditing existing cross-pane wiring.

**Related ADRs:**
- [ADR-012 Shared Component Library](./ADR-012-shared-component-library.md) — PaneEventBus lives in `@spaarke/ai-widgets` per ADR-012's shared-lib placement rules
- [ADR-021 Fluent UI v9 Design System](./ADR-021-fluent-ui-design-system.md) — All consumers of the bus are Fluent v9 React components
- [ADR-022 PCF Platform Libraries](./ADR-022-pcf-platform-libraries.md) — Bus is consumed by Code Pages (React 19); not used in PCF

**Code pointers:**
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` — bus implementation
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — typed channel + event definitions
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx` — React context provider
- `src/client/shared/Spaarke.AI.Widgets/src/events/usePaneEvent.ts` — React subscription hook
- `src/client/shared/Spaarke.AI.Widgets/src/events/useDispatchPaneEvent.ts` — React dispatch hook
- `src/client/shared/Spaarke.AI.Widgets/src/events/index.ts` — barrel export (public surface)

---

## Context

### R1 baseline (and its limitations)

The first generation of the SpaarkeAi three-pane shell (R1, `cross-pane-events.ts`) used **DOM `CustomEvent`** as the cross-pane communication primitive. Each pane attached an `addEventListener` to `window` (or a designated element) and dispatched via `window.dispatchEvent(new CustomEvent(...))`. This shipped and worked, but accumulated three structural problems that became blocking in R2:

1. **Single-listener semantics** — When two components on the same pane needed to react to the same event (e.g. a tab bar AND a tab content area both responding to `tab_change`), the second `addEventListener` either silently shadowed the first (if the wrong removal pattern was used) or both fired but in an unpredictable order driven by DOM listener registration timing. Multi-subscriber fan-out was not first-class; subscribers had to coordinate manually.
2. **Untyped payloads** — `CustomEvent.detail` was typed `any`. Every subscriber had to perform runtime type checks or cast, and refactors that renamed a payload field broke subscribers silently at runtime instead of at compile time. Several R1 production bugs (incorrect citation IDs, dropped wizard step transitions) traced to payload-shape drift between dispatcher and subscriber.
3. **DOM dependency** — Unit-testing components that subscribed to or dispatched events required either jsdom or a brittle mock of the DOM event APIs. Pure-logic tests of, say, the playbook selection flow could not be written without spinning up a DOM.

R2 introduced the **PaneEventBus** as a clean replacement: pure TypeScript, no DOM, typed channels with discriminated-union event payloads, and Set-based multi-subscriber storage with O(1) subscribe/unsubscribe.

### R2 → R3 evolution

R2 established four channels (`workspace`, `context`, `conversation`, `safety`) and a small initial event vocabulary (`widget_load`, `widget_action`, `tab_change`, `context_update`, `context_highlight`, `suggestion`, `playbook_change`, `safety_annotation`, `capability_change`). R3 extended the vocabulary substantially:

- **`workspace`** gained `widget_update`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset`, `active_widget_changed` (R3 Round 4 Fix 4)
- **`conversation`** gained `playbook-selected` (full config, replaces legacy `playbook_change`), `refine_request`, `first_message`
- **`context`** gained `stage_change` (extraction/analysis stage advance)
- **`safety`** stayed at two event types but tightened its payload (groundedness, citations, confidence tier)

Critically, all R3 extensions followed the R2 pattern: new event types are added to the existing channel's discriminated union; new channels are NOT invented. By the end of R3 (task 140) the four channels were carrying multiple feature areas (workspace tabs, context wizards, conversation streaming, safety annotation, stage lifecycle) and the pattern had proven itself.

### Why an ADR now

The pattern has shipped, has been used by multiple parallel projects (R2 widget shell, R3 stage lifecycle, R3 Calendar widget pattern D, upcoming R4 W-4 Assistant mount source + W-5 Context mount source), and has been refined to a known-good shape. Without an ADR, every new widget author re-invents channels, event shapes, and subscriber semantics, which would either fragment the surface back into R1-style ad-hoc CustomEvent dispatch or accumulate channel proliferation that defeats the discriminated-union typing.

R4 task A-2a (this ADR) **codifies the existing pattern** so future widget authors load one ADR and inherit the constraints. **No behavior change.** The ADR's job is to make the constraints explicit, not to evolve the primitive.

---

## Decision

The PaneEventBus pattern is the **single authorized cross-pane communication primitive** for the SpaarkeAi three-pane shell and any future N-pane shell built on the same architecture. The bus has the following binding shape:

### 1. Five typed channels — `PaneChannel = 'workspace' | 'context' | 'conversation' | 'safety' | 'memory'`

The five channels correspond to the three structural panes (workspace, context, conversation), a cross-cutting **safety** channel, and a cross-cutting **memory** channel (added 2026-06-21 by the chat-routing-redesign-r1 project to support the 6-tier memory subsystem). Any pane may subscribe to or dispatch on the cross-cutting channels. New channels are **not added** without a successor ADR; new feature areas extend existing channels' event-type discriminants.

The current channel-purpose mapping:

| Channel | Purpose | Typical dispatchers | Typical subscribers |
|---|---|---|---|
| `workspace` | Widget lifecycle, tab navigation, wizard step control, stage 2→3 entity resolution, stage 3↔4 transitions | WorkspacePane, AssistantPane (wizard control), ContextPane (entity resolution) | WorkspacePane tab bar, embedded widgets, ShellStageManager, AssistantPane (active context awareness) |
| `context` | Document/data context updates, citation highlights, extraction stage advance | ContextPane, AssistantPane (citation links) | ContextPane document viewer, ShellStageManager |
| `conversation` | User input signals, playbook selection, refinement requests, first-message stage transition | ConversationPane (PlaybookGallery, prompt buttons) | WorkspacePane (playbook-driven tab seeding), ShellStageManager |
| `safety` | Groundedness annotations, capability availability changes | Safety perimeter service | AssistantPane (annotation overlay), capability-gated UI |

### 2. Discriminated-union event payloads per channel

Each channel's event interface declares a `type: 'a' | 'b' | 'c'` field as the discriminant. Subscribers narrow on `event.type` to access the type-specific fields. The map from channel to event-payload type is:

```typescript
export interface PaneChannelEventMap {
  workspace: WorkspacePaneEvent;
  context: ContextPaneEvent;
  conversation: ConversationPaneEvent;
  safety: SafetyPaneEvent;
}
```

TypeScript's mapped-type indexing ensures `dispatch('workspace', ...)` requires a `WorkspacePaneEvent` payload — `safety_annotation` on `workspace` is a compile error, not a runtime bug.

### 3. Multi-subscriber, Set-based delivery, no last-write-wins

Internally each channel holds a `Set<PaneEventHandler<C>>`. Every dispatched event is delivered to every subscriber synchronously, in insertion order. Subscribing the same function reference twice is a no-op (Set deduplication). Dispatching to a channel with no subscribers is safe — no error, no work done. This explicitly fixes R1's single-listener limitation.

### 4. Dispatch iterates a snapshot of subscribers

The implementation does `for (const handler of Array.from(set))` so handlers that unsubscribe themselves during dispatch (a common React unmount pattern) do not mutate the set mid-iteration. This is correct behavior; future implementations of the bus MUST preserve this snapshot semantics.

### 5. React integration via a single context provider + two hooks

- **`<PaneEventBusProvider>`** — wraps the three-pane shell root, holds a stable bus instance in a `useRef` so it survives re-renders. The provider may accept an optional `bus` prop (test scenarios), otherwise it creates the bus once on mount.
- **`usePaneEvent(channel, handler)`** — subscribe hook. Stores the handler in a ref so inline arrow functions don't tear down the subscription each render. Subscribes in a `useEffect` keyed on `[bus, channel]`.
- **`useDispatchPaneEvent()`** — dispatch hook. Returns a `useCallback`-stabilized dispatch function. Type inference at the call site: `dispatch('context', { type: 'context_highlight', citationId: 'ref-1' })` is fully typed.

The internal `usePaneEventBus()` hook (which reads the context value) is **intentionally not exported from the package barrel**. Consumers MUST use the public hooks. This keeps the bus instance encapsulated and prevents calling code from holding a direct reference that could outlive the provider.

### 6. Bus instance ownership: single provider at shell root

The canonical pattern is **one** `<PaneEventBusProvider>` at the SpaarkeAi shell root. Nested providers create isolated event scopes (rare, e.g. for an embedded sub-shell in an iframe-equivalent) and are permitted only when the isolation is intentional and documented in the consuming component's JSDoc.

---

## Consequences

### Positive

- **Typed channels prevent shape drift between dispatcher and subscriber.** TypeScript compile errors catch payload-mismatch bugs that R1 caught only at runtime (or in production).
- **Multi-subscriber semantics enable composition.** WorkspacePane tab bar, embedded widgets, and ShellStageManager can all subscribe to the same `workspace` channel; each handles the event types it cares about and ignores the rest. No coordination needed.
- **No DOM dependency makes the bus unit-testable.** `PaneEventBus.test.ts` runs in pure Node — no jsdom, no event polyfills. Hook tests run with `@testing-library/react` minimal setup.
- **Stable React API.** `usePaneEvent` accepts inline arrow functions safely (handler-ref pattern) and `useDispatchPaneEvent` returns a stable function — neither causes spurious re-renders or subscription churn.
- **Future-proof for non-React consumers.** Because `PaneEventBus` is plain TypeScript, a future non-React surface (Web Component, vanilla JS, headless agent) could share the same bus instance with React consumers if needed.

### Negative

- **Rigid contract for new channels.** Adding a sixth channel is intentionally hard: it requires a successor ADR, an update to `PaneChannelEventMap`, and decisions about default-subscriber semantics. This is a feature, not a bug, but it does mean teams must justify why a new feature does not fit into one of the existing five channels. (Original wording said "fifth channel… four channels"; revised 2026-06-21 with `memory` amendment.)
- **No automatic event replay or persistence.** The bus is purely in-memory and fire-and-forget. Subscribers that mount after a dispatch do not receive prior events. Features requiring "state-on-mount" (e.g. show the current playbook on a newly-mounted widget) must read state from a different primitive (props, context, query result) — the bus is for events, not state.
- **No cross-tab / cross-iframe delivery.** The bus is per-provider, per-React-tree. Multiple browser tabs / popups / Power Apps host iframes each have their own bus. Cross-surface communication requires a different primitive (e.g. `BroadcastChannel`, parent-window `postMessage`).
- **Discriminated union grows over time.** As R3 demonstrated, the `WorkspacePaneEvent` `type` discriminant has grown from ~4 values to 11. The union remains tractable but reviewers must pay attention during PR review to ensure new event types are genuinely additive and not subtly redefining existing types.

### Neutral

- **Subscriber ordering is insertion-order.** This is deterministic but not configurable. If two subscribers have an ordering dependency (rare), the dependent one must subscribe after the prerequisite. In practice this has not been a problem in R2/R3; the multi-subscriber pattern is used for independent reactions, not ordered pipelines.

---

## Alternatives Considered

| Alternative | Rejection reason |
|---|---|
| **Keep R1's DOM `CustomEvent` bus** | Three structural problems documented in Context: single-listener semantics, untyped payloads, DOM dependency in tests. Persisting with R1 would have blocked R2's stage lifecycle work and made the R3 Calendar widget (canonical Pattern D) impossible to test cleanly. |
| **Redux / Zustand / other state-management library** | Mismatch with the use case. The cross-pane primitive is for **events** (fire-and-forget), not **state** (last-write-wins). Forcing events into a state store adds reducer boilerplate, time-travel debugging overhead, and a single-tree-of-state mental model that the four-channel decomposition explicitly rejects. State management is still used inside each pane for its own state (component state, React Query); the bus is the seam between panes. |
| **React Context for each event type** | Would multiply context providers (one per event type, ~15+), each with a `useState` or `useReducer` that re-renders every consumer on every change. The bus's Set-based delivery does NOT cause React re-renders; only the explicit subscriber handler runs. Context-per-event-type would have catastrophic perf characteristics for high-frequency events (selection_changed during text drag). |
| **Plain pub/sub library (mitt, nanoevents, EventEmitter3)** | Equivalent runtime semantics, but those libraries are typed as `EventEmitter<{ [eventName: string]: any }>` or similar weakly-typed surfaces. Our `PaneChannel + PaneChannelEventMap` design gives stronger compile-time guarantees. The PaneEventBus implementation is ~80 LOC; the maintenance cost of an in-house bus is negligible compared to the typing-precision gain. |
| **Direct prop drilling between panes** | The three panes are sibling components under the shell root. Lifting state to the shell root and drilling down works for ~3 events, but the current event vocabulary (20+ events across 4 channels) would require drilling 20+ callback props through every layer. The bus collapses this to one provider and two hooks. |
| **External message bus (Service Bus, SignalR, etc.)** | Out of scope — this is a client-side pane-to-pane primitive. Server-pushed events arrive via a different mechanism (SSE in the AI streaming endpoints) and are translated into bus events at the pane boundary if needed. |

---

## Operationalization

### What enforces this ADR

| Mechanism | Scope |
|---|---|
| TypeScript type system (`PaneChannelEventMap`) | Compile-time enforcement of channel/payload typing |
| `code-review` skill | PR review enforcement of "no `any` in event payloads" + "no new channels without successor ADR" |
| `adr-check` skill | Loaded when task tags include `widget`, `paneeventbus`, `cross-pane`, `widget-mount` |
| `.claude/adr/ADR-030-pane-event-bus.md` (concise) | Loaded by `task-execute` for any task touching the bus or widget mounts |
| Future widget mount tasks (R4 W-4, W-5; future widget tasks) | Inherit ADR-030 via task POML `<constraint source="ADR-030">` declaration |

### Verification commands

```bash
# Confirm no `any` payloads in the bus code
cd src/client/shared/Spaarke.AI.Widgets/src/events
grep -n ":\s*any" *.ts *.tsx
# Expected: no matches (intersect with .gitignore is fine if matches are in test fixtures)

# Confirm the channel union has exactly five members (4 + memory amendment 2026-06-21)
grep -A1 "^export type PaneChannel" PaneEventTypes.ts
# Expected: 'workspace' | 'context' | 'conversation' | 'safety' | 'memory'

# Confirm PaneChannelEventMap matches the channel union
grep -A6 "^export interface PaneChannelEventMap" PaneEventTypes.ts
# Expected: workspace + context + conversation + safety + memory mapped to their *PaneEvent interfaces

# Confirm the bus tests still pass
cd src/client/shared/Spaarke.AI.Widgets
npm test -- events/__tests__
# Expected: all tests pass
```

### Review cadence

- **Per-PR** when a PR adds a new event type to `WorkspacePaneEvent | ContextPaneEvent | ConversationPaneEvent | SafetyPaneEvent`: reviewer confirms the discriminant addition is genuinely additive (new event type, no behavior change for existing ones).
- **Per-project** when a project plan proposes a new PaneEventBus channel: project must include a successor ADR draft before the channel is added. The four-channel constraint is binding.
- **Annual** review (next: 2027-05-26): confirm the channel-purpose mapping in §1 still reflects how features are split. If a channel has accumulated unrelated event types (e.g. `safety` carrying both annotation events and unrelated capability-routing events), consider whether the channel should be split — but only via successor ADR.

---

## Canonical consumers (for reference)

| Consumer | Channel | Event types used | Notes |
|---|---|---|---|
| WorkspacePane tab bar | `workspace` | `widget_load`, `tab_change`, `tab_count_change`, `tabs_clear`, `active_widget_changed` | Original R2 consumer |
| ShellStageManager | `workspace`, `context`, `conversation` | `tab_count_change`, `entity_resolved`, `session_reset`, `stage_change`, `first_message`, `playbook-selected` | Stage 1↔2↔3↔4 transitions |
| Calendar widget (R3 task 115) | `workspace` | `widget_load` (mount signal) | Canonical Pattern D consumer: shared-lib widget + thin LegalWorkspace shim |
| AssistantPane (R4 W-4) | `workspace` | `active_widget_changed`, `entity_resolved` | Upcoming mount source per R4 Phase 4 |
| ContextPane (R4 W-5) | `workspace`, `context` | `entity_resolved`, `context_update`, `context_highlight` | Upcoming mount source per R4 Phase 4 |
| ConversationPane prompt buttons | `conversation` | `first_message`, `playbook-selected` | R3 Stage 1 → Stage 2 transition |
| Safety perimeter | `safety` | `safety_annotation`, `capability_change` | Retroactive annotation per R3 |

---

## References

| Source | Purpose |
|---|---|
| `projects/spaarke-ai-platform-unification-r4/spec.md` (DR-04) | Codification requirement |
| `projects/spaarke-ai-platform-unification-r4/plan.original.md` §4 Phase 1 A-2 | Task definition |
| `projects/spaarke-ai-platform-unification-r2/` | Origin project — bus replaced R1 CustomEvent surface |
| `projects/spaarke-ai-platform-unification-r3/` | Extended event vocabulary; Calendar widget Pattern D (task 115) |
| `src/client/shared/Spaarke.AI.Widgets/src/events/` | Implementation (source of truth) |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | PaneEventBus contract documented in the component-model architecture |

### Related ADRs

| ADR | Relationship |
|---|---|
| [ADR-012](ADR-012-shared-component-library.md) | PaneEventBus lives in `@spaarke/ai-widgets` per ADR-012's shared-lib placement; consumed by Code Pages and other consumers via package import |
| [ADR-021](ADR-021-fluent-ui-design-system.md) | All bus consumers are Fluent v9 React components; Fluent v9 + bus together define the SpaarkeAi UI primitive surface |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | Bus is consumed by Code Pages (React 19); PCF (React 16/17 platform-provided) does NOT consume the bus directly |
| ADR-031 (pending, R4 task A-2b) | Stage lifecycle uses bus events (`tab_count_change`, `entity_resolved`, `first_message`, `playbook-selected`) as its primary signals; ADR-031 codifies the stage state machine that consumes these events |
| ADR-013 (refined 2026-05-20) | Bus is the CLIENT-side primitive; server AI streaming (ADR-013) crosses the network and is translated into bus events at the pane boundary if pane-to-pane fan-out is needed |

---

## AI-Directed Coding Guidance

- When asked to **add cross-pane communication**, FIRST identify which of the five channels (`workspace`, `context`, `conversation`, `safety`, `memory`) the use case fits. If none fits cleanly, push back — adding a sixth channel requires a successor ADR.
- When asked to **add a new event type to an existing channel**, add the new value to the channel's `type` discriminant union and add any new payload fields as optional (`?:`) on the event interface. Existing subscribers MUST continue to compile and run; new subscribers handle the new `type` value via narrowing.
- When asked to **dispatch from inside a widget**, use `useDispatchPaneEvent()` — never instantiate `new PaneEventBus()` and never reach into context directly.
- When asked to **subscribe to events**, use `usePaneEvent(channel, handler)` — never `useContext(PaneEventBusContext)` directly. The provider context is encapsulated for a reason.
- When asked to **share a bus across iframes / tabs**, refuse — that is out of scope per the Negative consequences section. Direct the user to `BroadcastChannel` or `postMessage`.
- When asked to **add an `any` payload "just for this one case"**, refuse and cite this ADR's MUST rules. Find or add a typed shape.
- When asked to **dispatch memory-domain UI signals** (matter-memory promotion approval, pin lifecycle), use the `memory` channel (added 2026-06-21) — do NOT namespace inside `workspace.*`. The reconciliation review for `spaarke-ai-platform-chat-routing-redesign-r1` initially namespaced `promotion_pending` on `workspace`; the v2 amendment moves it to the dedicated `memory` channel.

---

## Amendment History

### v2 (2026-06-21) — `memory` channel addition

**Amending project**: `spaarke-ai-platform-chat-routing-redesign-r1`.

**Context.** The chat-routing-redesign project introduces a 6-tier memory subsystem (Working / Session / Matter / User-Org / Retrieval / Audit per `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` §3). Memory-domain UI signals — most prominently the matter-memory promotion approval workflow (architecture §6.4: pending record written → workspace UI notification → user approve/reject → durable T3 write) — do not fit semantically into any of the original four channels. The first-pass spec (`spec.md` FR-32) namespaced these as `workspace.promotion_pending`, which forced workspace-channel subscribers to ignore non-workspace events and created taxonomic confusion. The audit pass that produced this amendment elevated memory events to their own channel.

**Change.**
- Channel union expanded from 4 → 5: add `memory`.
- New event interface `MemoryPaneEvent` introduced with 5 initial discriminants:
  - `promotion_pending` — matter-memory promotion awaiting user approval; payload `{ promotionId, factSummary (80-char preview), matterId, sessionId }`
  - `promotion_resolved` — user approved or rejected the promotion; payload `{ promotionId, decision: 'approved' | 'rejected', factId? }`
  - `fact_promoted` — T3 durable matter-memory write completed; payload `{ factId, matterId, source }`
  - `pin_added` — Pinned context item added (T4); payload `{ pinId, pinType: 'user-preference' | 'system-rule' | 'matter-fact' }`
  - `pin_removed` — Pinned context item removed; payload `{ pinId }`
- `PaneChannel` closed-union now `'workspace' | 'context' | 'conversation' | 'safety' | 'memory'`.
- Adding a sixth still requires a successor ADR.

**Constraints preserved.**
- All original invariants (typed payloads, multi-subscriber sync delivery, snapshot iteration, single-provider lifetime, hidden context reader) continue to hold for the `memory` channel.
- ADR-015 tier-1 safety binding extends to `memory` payloads: deterministic IDs + summaries only; NEVER raw fact text, user message content, or recall results. `factSummary` is the 80-char preview for UI display.
- Tenant scope: memory payloads MUST be tenant-scoped through subscriber context (sessions / matters carry tenantId implicitly), NOT in payload.

**Required implementation updates** (out of this ADR's scope — owned by project tasks):
1. `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — add `MemoryPaneEvent` interface; extend `PaneChannel` union; extend `PaneChannelEventMap`.
2. `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` — verify channel-string switches accept `memory` (Set-keyed registry is channel-string-agnostic; no code change expected).
3. ContextPane approval UI subscriber wires `usePaneEvent('memory', handleMemoryEvent)`.
4. `MatterMemoryPromotionService` dispatch site uses the new channel via `useDispatchPaneEvent()` boundary.
5. Update spec / architecture references: spec.md FR-32 + architecture §6.4 (done as part of the amendment work).

**Out of scope for v2**: removing R6-style namespaced `workspace.promotion_pending` legacy references (none exist in shipped code; new feature).

**Reviewer**: Project owner (2026-06-21).

---

*Document Owner: Spaarke Engineering · Originating project: `spaarke-ai-platform-unification-r4` (Phase 1, task A-2a)*
