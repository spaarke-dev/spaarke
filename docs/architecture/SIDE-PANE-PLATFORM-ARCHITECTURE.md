# Spaarke Side Pane Platform Architecture

> **Version**: 1.0
> **Last Updated**: March 4, 2026
> **Audience**: Developers, AI agents, architects
> **Purpose**: Architecture decisions for persistent, context-aware side panes in Spaarke model-driven apps
> **Related ADRs**: ADR-006 (PCF + Code Pages), ADR-012 (Shared library), ADR-013 (AI Architecture), ADR-021 (Fluent UI v9)

---

## Executive Summary

The Spaarke Side Pane Platform provides always-available side panes in model-driven apps — similar to Microsoft Copilot. It uses a **configuration-driven SidePaneManager** that auto-registers panes on every page load via Code Page injection (primary) or a hidden ribbon trigger pattern (secondary).

**Key capabilities**:
- Panes are always available in the side pane launcher (left icon bar)
- Context-aware: detects current entity/record via Xrm polling
- Works on both OOB Dataverse forms AND custom Code Pages
- Multi-pane support: SprkChat, future Actions pane, Notifications, etc.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│  Model-Driven App Shell (UCI)                                        │
│                                                                      │
│  ┌─ Mscrm.GlobalTab ──────────────────────────────────────────────┐ │
│  │  Hidden Button (never rendered)                                 │ │
│  │    └─ EnableRule → Spaarke.SidePaneManager.initialize()        │ │
│  │         ↓ (fires on every page navigation)                     │ │
│  │  SidePaneManager reads PaneRegistry                            │ │
│  │    ↓ For each configured pane:                                 │ │
│  │    ↓   Xrm.App.sidePanes.getPane(paneId)                     │ │
│  │    ↓   If not exists → createPane() + navigate()              │ │
│  │    ↓   Returns false (button hidden)                          │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│  ┌─ Main Content Area ───────────┐  ┌─ Side Pane Launcher ────────┐│
│  │  OOB Form / Code Page /       │  │  [Chat icon] SprkChat       ││
│  │  Dashboard / Grid View        │  │  [Actions icon] Actions*     ││
│  │                               │  │  * = future panes            ││
│  └───────────────────────────────┘  └──────────────────────────────┘│
│                                                                      │
│  ┌─ Communication Layer ─────────────────────────────────────────┐  │
│  │  BroadcastChannel: sprk-workspace-{context}                   │  │
│  │  Auth: Each pane acquires own tokens (no tokens cross bridge)  │  │
│  └───────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Design Decisions

### Why Code Page Injection (Not Ribbon Enable Rules)?

Testing confirmed that `Mscrm.GlobalTab` enable rules do NOT reliably fire in current UCI (2026). The community-documented hidden button pattern does not trigger script loading in practice.

Code Page injection is reliable because Code Pages execute JavaScript in a same-origin iframe with full access to the parent Dataverse shell via `window.parent`. Any Code Page serving as a landing page injects the SidePaneManager script into the parent shell frame by creating a `<script>` element in `parentDoc.head`. The `data-sprk-sidepane` attribute prevents duplicate injection.

The hidden ribbon trigger remains in the solution as a secondary mechanism but is not the primary loading path.

### Why Polling (Not Events)?

Dataverse provides **no event** when the user navigates between records while a side pane is open. The `Xrm.App.sidePanes` API has no `onNavigate` or `onContextChange` callback. Microsoft Copilot, being a first-party feature, has access to internal UCI navigation events that are not exposed to third-party developers.

Polling `Xrm.Page.data.entity` every 2 seconds is the pragmatic solution. CPU cost is negligible (one property read per poll cycle). When a context change is detected while a session is active (e.g., chat), a Context Switch Dialog is shown.

### Why BroadcastChannel (Not postMessage)?

- **Same origin guaranteed**: All Dataverse web resources share `*.crm.dynamics.com`
- **Multi-pane**: BroadcastChannel broadcasts to all listeners; postMessage requires specific target window references
- **Decoupled**: Panes don't need references to each other's window objects
- SprkChatBridge auto-falls back to postMessage if BroadcastChannel is unavailable

### Why canClose: false for SprkChat?

A `canClose: false` pane cannot be dismissed by the user — always present in the launcher bar, exactly like Microsoft Copilot. Users can collapse the pane but cannot remove it.

### Why alwaysRender: true for SprkChat?

Without `alwaysRender`, the pane's web resource unmounts when the user switches to a different side pane tab, destroying React state (chat history, session context). With `alwaysRender: true`, the pane stays mounted even when hidden. Use sparingly — each always-rendered pane consumes memory.

### Auth Token Isolation

**Auth tokens MUST NEVER cross the BroadcastChannel bridge.** Each pane authenticates independently via `Xrm.Utility.getGlobalContext()`. Token A (main area) and Token B (side pane) are issued separately from the same SSO session. BFF API validates each independently.

### Context Detection Priority

Context is resolved in this order:
1. URL parameters (from SidePaneManager `navigate()` data string)
2. `Xrm.Page.data.entity` via parent frame walk
3. `Xrm.Utility.getPageContext()` via parent frame walk
4. Graceful fallback (empty context — dashboard, grid without selection)

### PaneRegistry (Static Configuration)

The registry is a static array compiled into the SidePaneManager script. No Dataverse round-trip needed at registration time. Adding a new pane requires: build a Code Page web resource, add an entry to `PANE_REGISTRY`, recompile and upload `sprk_SidePaneManager`, upload the Code Page and icon web resources.

---

## Core Components

| Component | Purpose | Deployed As |
|-----------|---------|-------------|
| `SidePaneManager.ts` | Registry + initialization — compiled to plain JS, no bundler (`--module None`) | `sprk_SidePaneManager` (JS web resource) |
| `contextService.ts` | Context detection + polling for side pane Code Pages | Shared library (`@spaarke/ui-components`) |
| `SprkChatBridge.ts` | Cross-pane typed events via BroadcastChannel | Shared library (`@spaarke/ui-components`) |

---

## Hosting Scenarios

### Scenario 1: OOB Dataverse Form

User opens a Matter record. SprkChat pane is already registered. Context polling detects `entityType: "sprk_matter"` and `entityId`. Auto-resolves playbook: "legal-analysis". **Context source**: Xrm.Page.data.entity (parent frame walk + polling).

### Scenario 2: Custom Code Page

User opens Analysis Workspace. Code Page emits `context_changed` via BroadcastChannel. SprkChat pane updates to document context. **Context source**: BroadcastChannel `context_changed` event.

### Scenario 3: Dashboard

No specific record context. SprkChat falls back to "general-assistant" playbook. **Context source**: Graceful fallback.

### Scenario 4: Navigation Between Records

Polling detects context change within ≤2 seconds. A Context Switch Dialog appears: "You navigated to Matter B. Switch chat context? [Switch] [Keep]"

---

## Future Extensions

| Extension | Priority |
|-----------|----------|
| **Actions Pane** — Quick-launch for standalone AI actions | High |
| **Notifications Pane** — Real-time alerts for background job completion | Medium |
| **Conditional Panes** — Show/hide based on entity type or security role | Medium |
| **Dynamic Registry** — Load pane config from Dataverse records | Low |

---

## References

- [Creating side panes using client API](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/create-app-side-panes)
- [Andrew Butenko: Side Panels like a Boss](https://butenko.pro/2024/06/03/creating-model-driven-apps-side-panels-like-a-boss/)
- [AI Architecture](AI-ARCHITECTURE.md)
- [SDAP Auth Patterns](sdap-auth-patterns.md)
