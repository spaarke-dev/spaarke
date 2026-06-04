# Iframe-Wizard Pattern Enhancement — Design Document

> **Created**: 2026-05-27
> **Source**: R4 task 043 (W-5) discovery + operator decision 2026-05-27
> **Status**: Design (pre-planning)
> **Predecessor**: R4 [`notes/context-workspace-mount-pattern.md`](../spaarke-ai-platform-unification-r4/notes/context-workspace-mount-pattern.md) captured the initial finding; this document is the comprehensive write-up.

---

## 1. Background

SpaarkeAi is a React 19 code page deployed as a Dynamics 365 web resource (`sprk_spaarkeai`). It runs inside a Dynamics iframe and renders a three-pane shell (`ThreePaneShell`) with an Assistant pane, a Workspace pane, and a Context pane.

The Workspace pane mounts tabs ("widgets") in response to `widget_load` events dispatched on the `workspace` channel of a typed multi-subscriber bus (`PaneEventBus`, codified in [ADR-030](../../.claude/adr/ADR-030-pane-event-bus.md)). Dispatchers call:

```typescript
const dispatch = useDispatchPaneEvent();
dispatch('workspace', { type: 'widget_load', widgetId: '...', payload: { ... } });
```

`useDispatchPaneEvent()` reads the bus from React context. **The context is only available inside the `<PaneEventBusProvider>` tree** — any code outside that tree (different iframe, different window, different react root) will get a no-op dispatcher and the call will silently fail.

## 2. The cross-cutting problem

Many features need to mount a workspace tab from **outside** the SpaarkeAi React tree:

1. **Web-resource iframe wizards** — multi-step wizards (CreateProjectWizard, WorkAssignmentWizardDialog, DocumentEmailWizard, WorkspaceLayoutWizard) deploy as their own web resources and run in their own iframes inside Dynamics. They are siblings of SpaarkeAi, not children.

2. **Model-driven-app main forms** — when a user saves a Matter record on the standard MDA form (no React at all), there is currently no path to "open this Matter as a workspace tab in SpaarkeAi". The form code runs in the MDA iframe; SpaarkeAi runs in a different iframe (or a different window entirely if the user navigated away).

3. **Background job completions** — `Sprk.Bff.Api` background workers complete (analysis done, document triage done, RAG index built). The user is currently in SpaarkeAi but no signal arrives to mount the result.

4. **External SPAs** — Power Pages portals, partner-branded SPAs (e.g., `MatterMate`) are entirely separate React applications served from different origins. They cannot share React context with SpaarkeAi.

5. **Office Add-ins** — Outlook and Word add-ins run in a completely different window family (the Outlook/Word task pane), have their own React tree, and need to coordinate with SpaarkeAi running in the user's browser.

Today: every one of these surfaces either (a) silently fails the dispatch, (b) requires the user to manually navigate, or (c) doesn't exist yet because the integration path is unclear.

## 3. Use case surfaces — detailed

### Surface 1: Web-resource iframe wizards

**Today**: a wizard like `CreateProjectWizard` deploys as `sprk_createprojectwizard.html` (a small Vite SPA). It is opened via `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_createprojectwizard' })`. On completion the wizard `Xrm.Navigation.navigateTo({...})` back to the original page — but the result (e.g., the just-created Matter ID) is lost, and there's no mechanism to surface the new record in SpaarkeAi's workspace.

**Desired**: the final wizard step shows "Matter created. [Add to Workspace]" — clicking it mounts a Matter-detail workspace tab in SpaarkeAi.

**The transport problem**: the wizard is in its own iframe (`window.top.frames[wizardFrame]`). SpaarkeAi is in a different iframe (`window.top.frames[spaarkeAiFrame]`). React context is not shared.

### Surface 2: Model-driven-app main forms

**Today**: users save a Matter on the standard MDA form. There's currently no in-product affordance to "open this Matter in SpaarkeAi as a workspace tab". The user must navigate manually.

**Desired**: a Ribbon button "Open in Workspace" on the Matter form sends a mount signal to SpaarkeAi.

**The transport problem**: MDA forms are not React. They run inside the MDA's iframe chain with `Xrm.WebApi` as the only general-purpose API. If SpaarkeAi is already open in another browser tab (common when users navigate), there's no shared scope at all.

### Surface 3: Background job completions

**Today**: when a BFF worker finishes (e.g., 25-minute document analysis job), the result lands in Dataverse. The user might still be in SpaarkeAi but receives no notification.

**Desired**: SpaarkeAi shows a toast "Analysis complete. [View in Workspace]" — clicking mounts the analysis result.

**The transport problem**: the worker runs in a Container App, not in any browser. It has no way to "push" to a specific user's SpaarkeAi session except via SignalR/SSE/WebSocket — and no Power Automate / plugin trigger may participate.

### Surface 4: External SPAs

**Today**: a partner portal (`mattermate.example.com`) shows a list of matters. There's currently no "open in Spaarke" action.

**Desired**: an action that opens SpaarkeAi (in a new tab) AND mounts a specific tab.

**The transport problem**: cross-origin. `postMessage` requires the SpaarkeAi window to exist in the same window family OR a coordinator (BFF or signed URL).

### Surface 5: Office Add-ins

**Today**: an Outlook add-in lets users associate emails with matters. No path to "show this matter in Spaarke" from inside Outlook.

**Desired**: action triggers SpaarkeAi (in a browser tab) to mount the matter.

**The transport problem**: Outlook add-ins live in `chrome-extension://...` or `office.com` origins, completely separate from Dynamics. Direct `postMessage` not viable; coordination via BFF required.

## 4. Critical constraints (core product)

This is a **core product** project. Solution mechanisms are restricted:

### EXPLICITLY OUT OF SCOPE

- **Power Automate** — not part of the core product. Any solution that requires a Power Automate cloud flow is **rejected**.
- **Dataverse plugins** — not part of the core product. Any solution that requires a Dataverse plugin (pre-event, post-event, pipeline) is **rejected**.
- **Plugin-adjacent mechanisms**:
  - ServiceBus pushes triggered FROM a plugin → rejected
  - Custom workflow assemblies → rejected
  - Plugin Step registrations → rejected

### ALLOWED mechanisms

- **Web platform APIs**:
  - `window.postMessage` with origin allowlist
  - `BroadcastChannel` for same-origin tab/window coordination
  - `CustomEvent` for in-document coordination
  - `sessionStorage` / `localStorage` change events for cross-tab same-origin signaling
- **BFF API**:
  - HTTP polling against `Sprk.Bff.Api` endpoints
  - WebSockets (existing pattern in BFF)
  - Server-Sent Events (existing pattern in BFF for chat streaming)
  - Signed callback URLs (e.g., redirect a wizard completion through a URL that contains the mount signal)
- **Dataverse Web API**:
  - Client-side polling via `Xrm.WebApi.retrieveMultipleRecords` against a "pending mounts" entity
  - `Xrm.WebApi` change-tracking (with throttling) — feasible but expensive
- **React composition**:
  - Migrate iframe wizards to in-process React components inside the SpaarkeAi tree
  - The component then uses `useDispatchPaneEvent()` directly (no transport needed)

### Why these constraints

Per operator direction 2026-05-27: "As part of the core product we don't use Power Automate or plugins; let's ensure that we don't allow those to be part of the solution."

Power Automate and Dataverse plugins are **product extension surfaces** that customers can configure but they are not the core product's build-time components. Spaarke must function entirely without them.

## 5. Solution options

Five options considered; each is evaluated against the 5 surfaces above and the constraints.

### Option 1 — postMessage bridge

**How it works**: iframe wizard finishes and calls `window.parent.postMessage({type: 'spaarke:mount', widgetId, payload}, targetOrigin)`. SpaarkeAi listens on `window.addEventListener('message', ...)` and validates origin + payload, then dispatches on the bus.

**Fits**: Surface 1 (iframe wizards), Surface 2 (MDA forms — if SpaarkeAi is in the same window family).

**Pros**:
- Standards-based, near-zero latency
- Built into every browser
- Trivial to debug (DevTools listens to `message` events)
- No server round-trip

**Cons**:
- Sender must know SpaarkeAi exists (must hold a window reference or use `window.top.frames`)
- Cross-origin requires explicit `targetOrigin`
- Doesn't work if SpaarkeAi is in a different browser tab (different window family)

**Ruling**: PRIMARY mechanism for Surfaces 1 + 2.

### Option 2 — BroadcastChannel

**How it works**: SpaarkeAi creates `new BroadcastChannel('spaarke')`. Any same-origin tab/window can post to it.

**Fits**: Surface 1 (if same-origin), Surface 2 (if same-origin and same tab family).

**Pros**:
- Works across same-origin tabs, not just iframe parent
- Survives iframe boundary
- No origin allowlist required (same-origin only)

**Cons**:
- Same-origin only
- Sender still needs to know SpaarkeAi is listening (or accept fire-and-forget semantics)
- Not supported in older Edge legacy (n/a for Dynamics)

**Ruling**: SECONDARY mechanism for same-origin cross-tab scenarios (e.g., SpaarkeAi open in tab A, MDA form open in tab B, both `*.crm.dynamics.com`).

### Option 3 — HTTP polling against BFF queue

**How it works**: BFF maintains a per-user "pending mounts" queue (Cosmos or in-memory). External code (any surface) POSTs to `BFF /api/workspace/mount-pending`. SpaarkeAi polls `GET /api/workspace/mount-pending` every 5–10s and processes any pending mounts.

**Fits**: ALL 5 surfaces. Universal.

**Pros**:
- Works across any boundary (cross-origin, cross-tab, cross-process)
- Survives SpaarkeAi reload (queue persists)
- Standard HTTP — no special browser features

**Cons**:
- Latency = poll interval
- Storage cost (per-user queue in Cosmos)
- BFF API surface grows (one new endpoint family)
- BFF placement justification required per CLAUDE.md §10

**Ruling**: PRIMARY mechanism for Surfaces 3, 4, 5 (background jobs, external SPAs, Office Add-ins). Optional fallback for Surfaces 1 + 2 if postMessage is unreachable.

### Option 4 — SSE/WebSocket push from BFF

**How it works**: SpaarkeAi opens an SSE connection (already used for chat) to a "user events" channel. BFF pushes mount events as they arrive.

**Fits**: Surfaces 3, 4, 5 (where the sender can reach BFF).

**Pros**:
- Real-time (no polling latency)
- Reuses existing SSE pattern in BFF
- Single connection, low overhead

**Cons**:
- Connection management complexity (reconnect on network drop, scale across BFF instances)
- BFF must hold per-user connection state (or use Azure SignalR backplane)
- More BFF code than polling

**Ruling**: OPTIONAL upgrade path from Option 3 if poll latency becomes a UX problem. Not in the initial scope.

### Option 5 — In-process migration

**How it works**: rewrite iframe wizards as in-process React components rendered inside SpaarkeAi's tree. They then use `useDispatchPaneEvent()` directly. No transport needed.

**Fits**: Surface 1 (wizards only). Doesn't help with the other surfaces.

**Pros**:
- No transport at all — eliminates the problem for the migrated wizard
- Tighter UX integration (no iframe chrome)
- Easier testing (no cross-frame mocking)

**Cons**:
- Bigger refactor per wizard
- Some wizards (CreateProjectWizard) deeply integrated with Dynamics modal navigation — migration cost is high
- Doesn't help Surfaces 2–5 at all

**Ruling**: APPLY SELECTIVELY where migration cost is low. Not a universal answer.

## 6. Recommended architecture

A **layered approach** that addresses each surface with the simplest mechanism that works:

```
┌─────────────────────────────────────────────────────────────────────┐
│ SpaarkeAi (React 19 Code Page)                                      │
│                                                                     │
│   <MountSourceProvider>          ← NEW: layer that sits OUTSIDE     │
│      ↓                              <PaneEventBusProvider> and      │
│      ↓                              forwards external signals into  │
│      ↓                              the bus.                        │
│      ↓                                                              │
│   ┌────────────────────────────────────────────────────────────┐   │
│   │  postMessage listener      → forward to bus                 │   │
│   │  BroadcastChannel listener → forward to bus                 │   │
│   │  BFF polling client        → poll /mount-pending; forward   │   │
│   │  (SSE optional upgrade)    → subscribe; forward             │   │
│   └────────────────────────────────────────────────────────────┘   │
│      ↓ all paths converge ↓                                         │
│   <PaneEventBusProvider> dispatches widget_load                     │
│   <WorkspaceShell> mounts the tab                                   │
└─────────────────────────────────────────────────────────────────────┘
        ↑                       ↑                        ↑
        │ postMessage           │ BroadcastChannel       │ HTTP POST
        │                       │                        │
┌───────────────┐      ┌────────────────┐      ┌──────────────────────┐
│ Iframe wizard │      │ Same-origin    │      │ External SPA /       │
│ (Surface 1)   │      │ tab (Surf 2)   │      │ Office Add-in /      │
│               │      │ MDA form       │      │ BFF worker (3, 4, 5) │
└───────────────┘      └────────────────┘      └──────────────────────┘
```

### Design principles

1. **One canonical event shape**. Every transport carries the same `MountSignal` payload:
   ```typescript
   interface MountSignal {
     type: 'spaarke:mount';
     widgetId: string;       // matches a registered widget
     payload: unknown;       // widget-specific
     correlationId: string;  // for idempotency + dedup
     origin: 'wizard' | 'mda' | 'job' | 'spa' | 'addin';
   }
   ```

2. **Dedup by correlationId**. All listeners feed into a single dispatcher that tracks recently-seen IDs (5-minute window) to handle cases where the same signal arrives via multiple transports (e.g., postMessage AND polling).

3. **Origin allowlist**. postMessage and BroadcastChannel listeners validate origin against a fixed list (`*.crm.dynamics.com`, `*.spaarke.com` for partner portals).

4. **BFF-side queue is per-user, time-bounded**. Mount-pending records expire after 30 minutes. Cosmos partition on `userObjectId`.

5. **Polling cadence is adaptive**. SpaarkeAi polls every 10s when idle, 3s when the user just interacted (heuristic: window focus + recent activity). Reduce server load.

6. **No core-product PA / plugins**. The "pending mounts" queue is populated EXCLUSIVELY via BFF HTTP POST. The BFF can be called from any client (wizard, MDA Ribbon JS, external SPA, Office Add-in). No Power Automate or Dataverse plugin participates.

## 7. Implementation phases (preliminary)

Final phasing to be set during `/design-to-spec`. Preliminary:

### Phase 1 — Core SpaarkeAi infrastructure
- Implement `<MountSourceProvider>` in `@spaarke/ai-widgets` or `@spaarke/ui-components/workspace`
- Wire postMessage + BroadcastChannel listeners
- Add dedup logic (correlationId tracking)
- Define `MountSignal` type
- Update [ADR-030](../../.claude/adr/ADR-030-pane-event-bus.md) to reference the new external-source pattern

### Phase 2 — Wizard proof of concept
- Pick ONE wizard (recommend: CreateProjectWizard — Matter creation is the operator's primary use case)
- Add an "Add to Workspace" terminal step
- The button calls `window.parent.postMessage({type: 'spaarke:mount', widgetId: 'matter-detail', ...}, targetOrigin)`
- SpaarkeAi receives, dispatches, mounts the Matter tab

### Phase 3 — BFF polling endpoint
- Add `POST /api/workspace/mount-pending` (place a mount)
- Add `GET /api/workspace/mount-pending` (drain the user's queue)
- Cosmos-backed per-user queue with 30-minute TTL
- BFF placement justification per CLAUDE.md §10

### Phase 4 — Roll out to additional wizards
- WorkAssignmentWizardDialog, DocumentEmailWizard
- Same postMessage pattern; common helper module

### Phase 5 — MDA form Ribbon support
- "Open in Workspace" Ribbon button on Matter, Document entity forms
- Uses BroadcastChannel if SpaarkeAi same-origin tab exists, else BFF queue
- TypeScript-compiled Ribbon JS (no Power Automate)

### Phase 6 — Background job completion
- BFF worker enqueues mount-pending on job completion
- SpaarkeAi polling picks it up
- Toast UI: "Analysis complete. [View]"

### Phase 7 — External SPA + Office Add-in integration
- Document the integration pattern in `docs/guides/`
- Each external surface integrates via BFF queue
- Office Add-in MSAL-auths to BFF, places mount-pending

## 8. Security considerations

- **postMessage origin validation**: hard allowlist; reject unknown origins. Never use `'*'` as targetOrigin.
- **MountSignal validation**: every received signal is validated against a JSON Schema before dispatching. Reject malformed payloads.
- **widgetId allowlist**: only widget IDs that the workspace shell knows about can be mounted. Unknown IDs → log + drop.
- **CSRF on BFF endpoints**: the mount-pending endpoints follow standard BFF auth (ADR-028).
- **Replay attacks**: correlationId-based dedup; expired tokens rejected.
- **No PII in signals**: the payload references records by ID; SpaarkeAi fetches details via authenticated Xrm.WebApi calls.

## 9. Open questions (for `/design-to-spec` resolution)

- How does Office Add-in MSAL flow compose with BFF auth for placing mount-pending?
- For cross-tab BroadcastChannel: do we keep a sessionStorage flag so SpaarkeAi knows whether another tab might want to broadcast?
- Should we expose a small SDK (`@spaarke/mount-source`) that wizards / external apps import, or document the postMessage protocol directly?
- BFF Cosmos partition strategy for the per-user queue — performance + cost projection
- Should mounts that arrive while SpaarkeAi is closed be queued indefinitely (until 30min TTL), or dropped entirely?

## 10. Relationship to R4

- R4 task 043 (W-5) attempted to wire `CreateProjectWizard` as a Context → Workspace mount source. Discovered the iframe scope issue. Pivoted to in-process `SemanticSearchCriteriaTool` (which works because it's in the tree).
- R4 documented the problem in `notes/context-workspace-mount-pattern.md`.
- R4 captured it as R5-backlog item #1.
- 2026-05-27: operator decided to scope as its own project rather than a deferred R5 backlog item.

## 11. Anti-goals

This project does **NOT**:

- Introduce a generic "events from anywhere" framework. Mounts are specifically `widget_load` events on the `workspace` channel — narrow and bounded.
- Modify the `PaneEventBus` itself. The bus stays untouched; the new layer feeds INTO it.
- Add Power Automate or plugin support. **Explicitly excluded.**
- Replace existing in-process dispatch paths. Code inside the React tree continues to use `useDispatchPaneEvent()`.
- Build a chat or collaboration channel. Mount signals only.

---

## 12. Pre-planning discussion synopsis (2026-05-28)

Captured during the pre-planning review with the operator. Restates the design in a discussion-ready form and flags the architecturally hot spots to settle during `/design-to-spec`.

### One-line framing

How does code *outside* the SpaarkeAi React tree (other iframes, MDA forms, BFF workers, external SPAs, Office Add-ins) tell SpaarkeAi to mount a workspace tab — without using Power Automate or Dataverse plugins?

### Core problem

SpaarkeAi's `PaneEventBus` (ADR-030) is exposed via React context inside `<PaneEventBusProvider>`. `useDispatchPaneEvent()` is a no-op anywhere outside that tree. Today, ~5 surface categories all silently fail, force manual navigation, or just don't exist yet.

### The 5 surfaces

1. **Iframe wizards** (`CreateProjectWizard`, `WorkAssignmentWizardDialog`, etc.) — sibling iframes, not children of SpaarkeAi
2. **MDA main forms** — no React at all; only `Xrm.WebApi`
3. **BFF background jobs** — no browser context; need a push channel
4. **External SPAs** — cross-origin (`mattermate.example.com`, Power Pages)
5. **Office Add-ins** — entirely separate window family

### Hard constraint (operator-set 2026-05-27)

**No Power Automate. No Dataverse plugins. No plugin-triggered ServiceBus.** Solutions must use only web platform APIs (`postMessage`, `BroadcastChannel`, `CustomEvent`, storage events), BFF HTTP/SSE/WebSocket, `Xrm.WebApi`, or in-process React composition.

### Recommended architecture — layered, per-surface

A new `<MountSourceProvider>` sits **outside** `<PaneEventBusProvider>` and funnels external signals in:

| Transport | Primary use | Why |
|---|---|---|
| **postMessage** | Surfaces 1, 2 (same window family) | Standards, zero-latency, no server |
| **BroadcastChannel** | Same-origin cross-tab (Surf 2 variant) | Survives iframe + tab boundaries |
| **BFF HTTP queue** (`/api/workspace/mount-pending`, Cosmos per-user, 30-min TTL) | Surfaces 3, 4, 5 — and fallback for 1+2 | Universal, cross-origin, survives reload |
| **SSE/WebSocket push** | Optional upgrade from polling | Real-time; reuses existing chat SSE |
| **In-process React migration** | Selective for low-cost wizards | Eliminates transport entirely |

All transports carry one canonical `MountSignal` (typed: `widgetId`, `payload`, `correlationId`, `origin`); a single dispatcher dedups by `correlationId`, validates origin allowlist, validates widget-ID allowlist.

### Phased rollout (7 phases, preliminary)

1. Core `<MountSourceProvider>` + postMessage + BroadcastChannel + dedup + ADR-030 update
2. Wizard PoC — `CreateProjectWizard` "Add to Workspace"
3. BFF `mount-pending` queue (requires Placement Justification per CLAUDE.md §10)
4. Roll out to remaining wizards
5. MDA Ribbon "Open in Workspace"
6. BFF worker → SpaarkeAi toast for job completions
7. External SPA + Office Add-in patterns (documented in `docs/guides/`)

### Discussion-worthy points to resolve in `/design-to-spec`

1. **Phase 3 is the BFF-governance hot spot.** New endpoints + Cosmos partition + worker-enqueue path all touch [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) and the ≤60 MB publish-size ceiling (CLAUDE.md §10). Decide early whether the queue lives in `Sprk.Bff.Api` or a separate small service.
2. **Phase 7 (external SPAs / Office Add-ins) is the most architecturally distinct.** Auth model and origin-trust assumptions differ enough that it may deserve its own sub-spec rather than being treated as the tail of the main project.
3. **Phase 5 (MDA Ribbon)** depends on whether Ribbon JS counts as "core product". Ribbon customization is solution-shipped and is **not** PA/plugin, so it almost certainly qualifies — but worth confirming explicitly so the constraint isn't relitigated later.
4. **Option 5 (in-process migration)** is a quiet "do-nothing-fancy" alternative for Surface 1. Worth a sharper per-wizard cost/benefit before committing the full transport layer for all four existing wizards — some may be cheaper to migrate than to bridge.

### Open questions (also tracked in §9)

- Office Add-in MSAL ↔ BFF auth composition for placing mount-pending
- Whether to ship a small `@spaarke/mount-source` SDK or just document the postMessage protocol
- Cosmos partition strategy + cost projection for per-user queue
- Behavior when SpaarkeAi is closed (queue until TTL vs drop)
- Whether to keep a `sessionStorage` flag so SpaarkeAi knows another tab might broadcast

---

*End of design document. Move to `/design-to-spec` to advance to spec.md.*
