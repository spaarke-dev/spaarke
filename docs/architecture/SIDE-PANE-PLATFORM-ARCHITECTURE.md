# Spaarke Side Pane Platform Architecture

> **Version**: 1.0
> **Last Updated**: March 4, 2026
> **Audience**: Developers, AI agents, architects
> **Purpose**: Architecture and implementation guide for persistent, context-aware side panes in Spaarke model-driven apps
> **Related ADRs**: ADR-006 (PCF + Code Pages), ADR-012 (Shared library), ADR-013 (AI Architecture), ADR-021 (Fluent UI v9)
> **Related Docs**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md), [sdap-auth-patterns.md](sdap-auth-patterns.md)

---

## Executive Summary

The Spaarke Side Pane Platform provides always-available side panes in model-driven apps — similar to Microsoft Copilot. It uses a **configuration-driven SidePaneManager** that auto-registers panes on every page load via a supported hidden ribbon trigger pattern.

**Key features**:
- Panes are always available in the side pane launcher (left icon bar)
- Context-aware: detects current entity/record via Xrm polling
- Works on both OOB Dataverse forms AND custom Code Pages
- Multi-pane support: SprkChat, future Actions pane, Notifications, etc.
- Pro-code implementation: TypeScript, React Code Pages, BroadcastChannel

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
│  │         ↓                                                      │ │
│  │  SidePaneManager reads PaneRegistry                            │ │
│  │    ↓ For each configured pane:                                 │ │
│  │    ↓   Xrm.App.sidePanes.getPane(paneId)                     │ │
│  │    ↓   If not exists → createPane() + navigate()              │ │
│  │    ↓   Returns false (button hidden)                          │ │
│  └────────────────────────────────────────────────────────────────┘ │
│                                                                      │
│  ┌─ Main Content Area ───────────┐  ┌─ Side Pane Launcher ────────┐│
│  │                               │  │                              ││
│  │  OOB Form (Matter, Contact)   │  │  [Chat icon] SprkChat       ││
│  │      OR                       │  │  [Actions icon] Actions*     ││
│  │  Code Page (navigateTo)       │  │  [Notify icon] Alerts*      ││
│  │      OR                       │  │                              ││
│  │  Dashboard / Grid View        │  │  * = future panes            ││
│  │                               │  │                              ││
│  └───────────────────────────────┘  └──────────────────────────────┘│
│                                                                      │
│  ┌─ Communication Layer ─────────────────────────────────────────┐  │
│  │  BroadcastChannel: sprk-workspace-{context}                   │  │
│  │  Events: context_changed, document_stream_*, selection_changed │  │
│  │  Auth: Each pane acquires own tokens (no tokens cross bridge)  │  │
│  └───────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Core Components

### 1. SidePaneManager (Loader Script)

**File**: `src/client/side-pane-manager/SidePaneManager.ts`
**Deployed as**: `sprk_SidePaneManager` (Dataverse JS web resource)

The SidePaneManager is a standalone TypeScript module compiled to a plain JS file (no bundler, `--module None` for Dataverse global namespace compatibility). It has one job: register all configured side panes. It auto-initializes when loaded (calls `initialize()` at script end).

```typescript
// Core data structure
interface PaneConfig {
  paneId: string;           // Unique ID for singleton behavior
  title: string;            // Display title in pane header + tooltip
  icon: string;             // Web resource path for side pane launcher icon
  webResource: string;      // Code Page web resource name
  width: number;            // Panel width in pixels (min 300)
  canClose: boolean;        // false = always present (like Copilot)
  alwaysRender: boolean;    // true = keeps React state when switching tabs
  contextAware: boolean;    // true = passes entity context via URL params
}
```

**Lifecycle**:

```
Code Page loads (e.g., workspace landing page)
  → Injection snippet in <head> injects <script> into parent Dataverse shell
  → Parent frame loads /WebResources/sprk_SidePaneManager
  → Script auto-calls initialize() on load
  → For each PaneConfig in registry:
      → getPane(paneId) — check if already registered
      → If not: createPane() → navigate() → pane icon appears in launcher
  → Guard flag prevents duplicate registration on subsequent loads
  → Panes persist across all page navigations within the session
```

**Loading mechanism — Code Page injection** (primary):

Any Code Page that serves as a landing page (e.g., workspace) includes an injection snippet in its HTML `<head>`:

```html
<script>
(function() {
    try {
        var parentDoc = window.parent && window.parent !== window && window.parent.document;
        if (!parentDoc) return;
        if (parentDoc.querySelector('script[data-sprk-sidepane]')) return;
        var s = parentDoc.createElement('script');
        s.src = '/WebResources/sprk_SidePaneManager';
        s.setAttribute('data-sprk-sidepane', 'true');
        parentDoc.head.appendChild(s);
    } catch (e) {
        console.warn('[SidePane] Could not inject into parent frame:', e);
    }
})();
</script>
```

This injects the SidePaneManager script into the parent Dataverse shell frame where `Xrm.App.sidePanes` is available. The `data-sprk-sidepane` attribute prevents duplicate injection. The script auto-initializes on load.

**Why Code Page injection, not ribbon enable rules**: Testing confirmed that `Mscrm.GlobalTab` enable rules do NOT reliably fire in current UCI (2026). The community-documented hidden button pattern does not trigger script loading in practice. Code Page injection is reliable because Code Pages execute JavaScript in a same-origin iframe with full access to the parent Dataverse shell via `window.parent`.

### 2. Pane Registry (Configuration)

The registry is a static array compiled into the SidePaneManager script. No Dataverse round-trip is needed at registration time — the registry defines which panes exist and how they're configured.

```typescript
const PANE_REGISTRY: PaneConfig[] = [
  {
    paneId: "sprk-chat",
    title: "SprkChat",
    icon: "WebResources/sprk_SprkChatIcon16.svg",
    webResource: "sprk_SprkChatPane",
    width: 400,
    canClose: false,       // Always present
    alwaysRender: true,    // Preserves chat history when switching panes
    contextAware: true,    // Detects entity/record
  },
  // Future panes added here:
  // { paneId: "sprk-actions", title: "Actions", webResource: "sprk_ActionPane", ... },
  // { paneId: "sprk-alerts", title: "Alerts", webResource: "sprk_AlertPane", ... },
];
```

**Adding a new pane** requires only:
1. Build the Code Page web resource (see Code Page pattern)
2. Add an entry to `PANE_REGISTRY`
3. Recompile and upload `sprk_SidePaneManager`
4. Upload the Code Page web resource
5. Upload the pane icon SVG web resource

### 3. Context Service (Shared Library)

**File**: `src/client/shared/Spaarke.UI.Components/src/services/contextService.ts`
**Consumed by**: Any Code Page loaded in a side pane

The Context Service provides entity/record context to side pane content. It handles two fundamentally different hosting scenarios with a unified API.

#### Context Detection Priority

```
1. URL Parameters (from SidePaneManager navigate() data string)
   ├── entityType, entityId, playbookId, sessionId
   └── Set at pane creation and updated on context change

2. Xrm.Page.data.entity (parent frame walk)
   ├── getEntityName() → entity logical name
   ├── getId() → record GUID (normalized, no braces)
   └── Available when user is on a Dataverse form

3. Xrm.Utility.getPageContext() (parent frame walk)
   ├── input.entityName, input.entityId
   └── Available on forms, grids, dashboards

4. Graceful fallback
   └── Empty context (dashboard, grid without selection)
```

#### Context Change Detection (Polling)

Dataverse provides **no event** when the user navigates to a different record while a side pane is open. The platform does not call `navigate()` on the pane or fire any callback. This is a platform limitation that affects all third-party side panes (Microsoft Copilot, being first-party, has access to internal navigation events).

**Solution**: Poll `Xrm.Page.data.entity` and `Xrm.Utility.getPageContext()` every 2 seconds. When a change is detected, the pane can:
- Silently update context (for non-disruptive scenarios)
- Show a Context Switch Dialog (for scenarios with active sessions, e.g., chat)

```typescript
// From contextService.ts
export function startContextChangeDetection(
    currentContext: DetectedContext,
    onChange: ContextChangeCallback,
    intervalMs: number = 2_000  // 2 second poll
): () => void {
    // Returns cleanup function to stop polling
}
```

#### Frame Walking (Xrm Access from Iframes)

Code Pages run in iframes within the Dataverse shell. To access the Xrm SDK:

```typescript
function findXrm(): XrmNamespace | null {
    // Try: window → window.parent → window.top
    // Stop at first frame with Xrm.Utility.getGlobalContext
    // Handles cross-origin errors gracefully
}
```

This is necessary because:
- `window.Xrm` — not available in the Code Page's own iframe
- `window.parent.Xrm` — typically available (same Dataverse origin)
- `window.top.Xrm` — fallback for deeply nested frames

### 4. Cross-Pane Communication (SprkChatBridge)

**File**: `src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts`
**Transport**: BroadcastChannel (primary) with window.postMessage fallback

The SprkChatBridge provides typed, event-driven communication between panes. All panes and Code Pages served from Dataverse share the same origin (`*.crm.dynamics.com`), satisfying BroadcastChannel's same-origin requirement.

#### Channel Naming

```
sprk-workspace-{context}
```

Where `{context}` is typically a session ID, analysis ID, or entity ID. This prevents cross-session interference when multiple tabs are open.

#### Event Types

| Event | Direction | Payload | Purpose |
|-------|-----------|---------|---------|
| `context_changed` | Pane → Pane | `{ entityType, entityId }` | Navigation detected |
| `document_stream_start` | Chat → Editor | `{ operationId, targetPosition, operationType }` | Begin streaming write |
| `document_stream_token` | Chat → Editor | `{ operationId, token, index }` | Individual token |
| `document_stream_end` | Chat → Editor | `{ operationId, cancelled, totalTokens }` | Stream complete |
| `document_replaced` | Chat → Editor | `{ operationId, html }` | Full replacement |
| `selection_changed` | Editor → Chat | `{ text, start, end }` | User selected text |
| `reanalysis_progress` | Chat → Editor | `{ percent, message }` | Progress bar |

#### Security Constraint

**Auth tokens MUST NEVER cross the bridge.** Each pane authenticates independently:

| Context | Auth Method |
|---------|------------|
| Dataverse Web API | `Xrm.Utility.getGlobalContext()` → OBO token |
| BFF API | `Xrm.Utility.getGlobalContext()` → Bearer token to BFF → BFF acquires OBO for Graph/OpenAI |
| Bridge messages | No auth needed (same-origin BroadcastChannel) |

---

## Hosting Scenarios

### Scenario 1: OOB Dataverse Form

User opens a Matter record. SprkChat pane is already registered in the launcher.

```
Matter Form (main area)          SprkChat Pane (side)
─────────────────────           ──────────────────────
Form loads normally              Already registered (from page load)
                                 Context polling detects:
                                   entityType: "sprk_matter"
                                   entityId: "{guid}"
                                 Auto-resolves playbook: "legal-analysis"
                                 Chat ready with matter context
```

**Context source**: Xrm.Page.data.entity (via parent frame walk + polling)

### Scenario 2: Custom Code Page (navigateTo Dialog)

User opens Analysis Workspace from a document viewer button.

```
Analysis Workspace (dialog)      SprkChat Pane (side)
────────────────────────         ──────────────────────
navigateTo webresource           Already registered
  data: "documentId={guid}"
App.tsx reads URL params         Receives context_changed via bridge
Creates SprkChatBridge           Updates to document context
Emits context_changed            Chat ready with document context
```

**Context source**: BroadcastChannel `context_changed` event from Code Page

### Scenario 3: Dashboard / Home Page

User is on the main dashboard. No specific record context.

```
Dashboard (main area)            SprkChat Pane (side)
──────────────────────           ──────────────────────
Dashboard loads                  Already registered
                                 Polling detects: no entity context
                                 Falls back to "general-assistant" playbook
                                 Chat available for general questions
```

**Context source**: Graceful fallback (empty entityType/entityId)

### Scenario 4: Navigation Between Records

User navigates from Matter A to Matter B while chat is active.

```
Time    Main Area                SprkChat Pane
─────   ──────────               ──────────────
T0      Matter A form            Chat active with Matter A context
T1      Navigate to Matter B     Polling detects context change (≤2s)
T2                               ContextSwitchDialog:
                                   "You navigated to Matter B.
                                    Switch chat context? [Switch] [Keep]"
T3a     (User clicks Switch)     New session, Matter B context
T3b     (User clicks Keep)       Continues Matter A session
```

---

## Authentication Architecture

### Token Flow for Side Panes

```
┌────────────────────────────────────────────────────────────────┐
│  Browser Tab                                                    │
│                                                                 │
│  ┌─ Dataverse Shell ───────────────────────────────────────┐   │
│  │  Xrm.Utility.getGlobalContext().getCurrentAppUrl()      │   │
│  │  SSO token available via Xrm SDK                         │   │
│  │                                                          │   │
│  │  ┌─ Main Area (Form/Code Page) ──┐  ┌─ Side Pane ────┐ │   │
│  │  │  Auth: Xrm SDK → token A      │  │  Auth: Xrm SDK │ │   │
│  │  │  BFF calls: Bearer token A     │  │  → token B     │ │   │
│  │  │                                │  │  BFF: Bearer B │ │   │
│  │  │  Bridge: no tokens             │  │                │ │   │
│  │  │  ←──── BroadcastChannel ────→  │  │                │ │   │
│  │  └────────────────────────────────┘  └────────────────┘ │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Token A and Token B are independent.                            │
│  Both acquired via same SSO session but issued separately.       │
│  BFF API validates each independently.                           │
└──────────────────────────────────────────────────────────────────┘
```

### BFF API Auth from Side Panes

When a side pane Code Page needs to call the BFF API (e.g., SprkChat sending a message):

1. Code Page calls `Xrm.Utility.getGlobalContext()` via frame walk
2. Acquires Dataverse access token
3. Sends to BFF API as `Authorization: Bearer {token}`
4. BFF validates token and performs OBO exchange for downstream services (Graph, Azure OpenAI)

This is the same auth pattern used by all Code Pages and PCF controls — side panes are not special. The key constraint is that each pane must acquire its own token; tokens are never shared via BroadcastChannel.

---

## Ribbon Configuration (Hidden Trigger Pattern)

### How It Works

UCI evaluates `Mscrm.GlobalTab` enable rules on **every page navigation** — dashboards, grids, forms. The button is never rendered (the `CustomRule` returns `false`), but the JavaScript function executes as a side effect.

This is the same pattern used by:
- Andrew Butenko's "Side Panels like a Boss" (community best practice)
- Microsoft's internal Teams Calls integration
- Microsoft Copilot's pane registration

### Ribbon XML

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Hidden trigger — enable rule fires JS on every page load -->
    <CustomAction Id="sprk.Global.SidePaneManager.CustomAction"
                  Location="Mscrm.GlobalTab.ApplicationCommon.Controls._children"
                  Sequence="1">
      <CommandUIDefinition>
        <Button Id="sprk.Global.SidePaneManager.Button"
                Command="sprk.Global.SidePaneManager.Command"
                LabelText="" TemplateAlias="o1" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <Templates>
    <RibbonTemplates Id="Mscrm.Templates" />
  </Templates>

  <CommandDefinitions>
    <CommandDefinition Id="sprk.Global.SidePaneManager.Command">
      <EnableRules>
        <EnableRule Id="sprk.Global.SidePaneManager.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions />
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules>
      <EnableRule Id="sprk.Global.SidePaneManager.EnableRule">
        <CustomRule FunctionName="Spaarke.SidePaneManager.initialize"
                    Library="$webresource:sprk_SidePaneManager"
                    Default="false" />
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
  <LocLabels />
</RibbonDiffXml>
```

**Key details**:
- `Default="false"` — button defaults to hidden while script loads
- `LabelText=""` — no visible text (button never renders anyway)
- `Sequence="1"` — fires early in evaluation order
- `<Actions />` — empty, no click handler needed (enable rule is the trigger)
- The `initialize()` function returns `false` to keep the button permanently hidden

### Deployed Via

The ribbon XML is packaged in the `ApplicationRibbon` unmanaged solution:
- `solution.xml` — solution manifest (publisher: Spaarke, prefix: sprk)
- `customizations.xml` — ribbon definitions
- `[Content_Types].xml` — ZIP content types
- Packed via `pack.ps1` using System.IO.Compression (not Compress-Archive)
- Imported via `pac solution import --path ... --publish-changes`

---

## File Structure

```
src/client/
├── side-pane-manager/                    ← NEW: Core platform module
│   ├── SidePaneManager.ts                ← Registry + initialization logic
│   ├── types.ts                          ← PaneConfig, PaneState interfaces
│   ├── tsconfig.json                     ← Compile config (target: ES2020, module: None)
│   └── out/
│       └── SidePaneManager.js            ← Compiled output → sprk_SidePaneManager
│
├── shared/Spaarke.UI.Components/src/
│   ├── services/
│   │   ├── SprkChatBridge.ts             ← Cross-pane typed events (existing)
│   │   └── contextService.ts             ← MOVED from SprkChatPane (reusable)
│   └── types/
│       └── sidePaneTypes.ts              ← Shared types for pane consumers
│
├── code-pages/
│   ├── SprkChatPane/                     ← First pane consumer
│   │   ├── src/
│   │   │   ├── App.tsx                   ← Uses contextService for context detection
│   │   │   └── services/
│   │   │       └── contextService.ts     ← TO BE MOVED to shared library
│   │   └── launcher/
│   │       └── openSprkChatPane.ts       ← DEPRECATED (replaced by SidePaneManager)
│   │
│   └── AnalysisWorkspace/                ← Code Page that communicates with panes
│       └── src/
│           └── hooks/
│               ├── useDocumentStreaming.ts ← Receives bridge events
│               └── useSelectionBroadcast.ts← Sends bridge events
│
tests/integration/CrossPane/              ← Existing integration tests
    ├── SprkChatBridgeIntegration.test.ts
    ├── ContextSwitching.test.ts
    └── ...
```

---

## Adding a New Side Pane (Developer Guide)

### Step 1: Create the Code Page

Follow the Code Page pattern (see `code-page-deploy` skill):

```
src/client/code-pages/{PaneName}/
├── src/index.tsx          ← React 18 entry point
├── index.html             ← HTML shell
├── webpack.config.js      ← Bundles everything
├── build-webresource.ps1  ← Inlines JS into HTML
├── package.json
└── tsconfig.json
```

### Step 2: Use Context Service

```typescript
// In your Code Page's App.tsx
import { detectContext, startContextChangeDetection } from "@spaarke/ui-components";

function App() {
  const [context, setContext] = useState<DetectedContext | null>(null);

  useEffect(() => {
    // Detect initial context from URL params or Xrm
    const ctx = detectContext();
    setContext(ctx);

    // Start polling for navigation changes
    const stopPolling = startContextChangeDetection(ctx, (newCtx) => {
      // Handle context change (show dialog, auto-switch, etc.)
      setContext(newCtx);
    });

    return stopPolling;
  }, []);

  return <MyPaneContent context={context} />;
}
```

### Step 3: Register in SidePaneManager

Add to `PANE_REGISTRY` in `SidePaneManager.ts`:

```typescript
{
  paneId: "sprk-my-pane",
  title: "My Pane",
  icon: "WebResources/sprk_MyPaneIcon16.svg",
  webResource: "sprk_MyPanePage",
  width: 350,
  canClose: true,
  alwaysRender: false,
  contextAware: true,
}
```

### Step 4: Deploy

1. Build Code Page: `npm run build && powershell -File build-webresource.ps1`
2. Upload Code Page HTML to Dataverse as `sprk_MyPanePage`
3. Upload icon SVG to Dataverse as `sprk_MyPaneIcon16.svg`
4. Recompile SidePaneManager: `npx tsc --skipLibCheck`
5. Upload `out/SidePaneManager.js` to Dataverse as `sprk_SidePaneManager`
6. Publish All Customizations

### Step 5: Cross-Pane Communication (Optional)

If your pane needs to communicate with other panes or Code Pages:

```typescript
import { SprkChatBridge } from "@spaarke/ui-components";

const bridge = new SprkChatBridge({ context: sessionId });

// Send events
bridge.emit("context_changed", { entityType: "sprk_matter", entityId: "..." });

// Receive events
const unsubscribe = bridge.subscribe("document_stream_token", (payload) => {
  console.log(payload.token);
});
```

---

## Design Decisions

### Why Hidden Ribbon Button (Not App OnLoad)?

| Approach | Supported? | Fires On | Limitation |
|----------|-----------|----------|------------|
| **Hidden GlobalTab button** (chosen) | Yes (ribbon API is documented) | Every page navigation | Requires ribbon XML solution |
| App-level OnLoad XML | No (undocumented hack) | App start only | May break in future updates |
| Form-level OnLoad | Yes (documented) | Per-entity form load | Must add to every form; misses dashboards/grids |
| Power Fx Command Bar | Yes | Button click only | Cannot create side panes programmatically |

### Why Polling (Not Events)?

Dataverse provides **no event** when the user navigates between records while a side pane is open. The `Xrm.App.sidePanes` API has no `onNavigate` or `onContextChange` callback. Microsoft Copilot, being a first-party feature, has access to internal UCI navigation events that are not exposed to third-party developers.

Polling `Xrm.Page.data.entity` every 2 seconds is the pragmatic solution used by the community. The CPU cost is negligible (one property read per poll cycle).

### Why BroadcastChannel (Not postMessage)?

- **Same origin guaranteed**: All Dataverse web resources share `*.crm.dynamics.com`
- **Multi-pane**: BroadcastChannel broadcasts to all listeners; postMessage requires specific target window references
- **Decoupled**: Panes don't need references to each other's window objects
- **Fallback**: SprkChatBridge auto-falls back to postMessage if BroadcastChannel is unavailable

### Why canClose: false for SprkChat?

A `canClose: false` pane cannot be dismissed by the user — it's always present in the launcher bar, exactly like Microsoft Copilot. This ensures the chat assistant is always one click away. Users can collapse the pane (Xrm.App.sidePanes.state = 0) but cannot remove it.

### Why alwaysRender: true for SprkChat?

Without `alwaysRender`, the pane's web resource unmounts when the user switches to a different side pane tab. This destroys React state (chat history, session context, WebSocket connections). With `alwaysRender: true`, the pane stays mounted in the DOM even when hidden, preserving state across tab switches. Use sparingly — each always-rendered pane consumes memory.

---

## Dataverse Solution Components

### Web Resources Required

| Name | Type | Purpose |
|------|------|---------|
| `sprk_SidePaneManager` | Script (JS) | Core loader — registers all panes |
| `sprk_SprkChatPane` | Webpage (HTML) | SprkChat Code Page (React 18 app) |
| `sprk_SprkChatIcon16.svg` | SVG | SprkChat icon for side pane launcher |
| `sprk_analysisworkspace` | Webpage (HTML) | Analysis Workspace Code Page |

### Solution: ApplicationRibbon

Contains only the ribbon XML with the hidden SidePaneManager trigger. No web resources are included in this solution (they exist independently in the default solution or their own solutions).

| File | Purpose |
|------|---------|
| `solution.xml` | Manifest — publisher: Spaarke, prefix: sprk |
| `customizations.xml` | RibbonDiffXml with SidePaneManager trigger |
| `[Content_Types].xml` | ZIP content types |

---

## Testing

### Manual Verification

1. **Hard refresh** (`Ctrl+Shift+R`) after deploying
2. Open browser console (F12)
3. Look for: `[Spaarke.SidePaneManager] Registered {N} panes`
4. Verify pane icons appear in the left side pane launcher bar
5. Click SprkChat icon → pane expands with Code Page content
6. Navigate to a different record → verify context detection (console logs)

### Integration Tests

Existing tests in `tests/integration/CrossPane/`:
- `SprkChatBridgeIntegration.test.ts` — channel naming, message ordering
- `ContextSwitching.test.ts` — context change detection and dialog
- `SelectionRevisionFlow.test.ts` — selection → refine workflow
- `DocumentStreamingFlow.test.ts` — full streaming pipeline

### Console Logging

All components use prefixed logging:
- `[Spaarke.SidePaneManager]` — pane registration lifecycle
- `[SprkChatPane:ContextService]` — context detection and changes
- `[SprkChatBridge]` — cross-pane message events

---

## Future Extensions

| Extension | Description | Priority |
|-----------|-------------|----------|
| **Actions Pane** | Quick-launch for standalone AI actions (Analyze Contract, Write Email) | High |
| **Notifications Pane** | Real-time alerts for background job completion, mentions | Medium |
| **Dynamic Registry** | Load pane config from Dataverse records (user-configurable) | Low |
| **Conditional Panes** | Show/hide panes based on entity type or security role | Medium |
| **Deep Linking** | Open specific pane + context from URL parameters | Low |

---

## References

### Microsoft Documentation
- [Creating side panes using client API](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/create-app-side-panes)
- [createPane API reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-app/xrm-app-sidepanes/createpane)
- [Ribbons available in model-driven apps](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/ribbons-available)

### Community Patterns
- [Andrew Butenko: Side Panels like a Boss](https://butenko.pro/2024/06/03/creating-model-driven-apps-side-panels-like-a-boss/)
- [nijos.dev: Call JavaScript Methods Globally](https://nijos.dev/2020/12/08/call-javascript-methods-globally-over-dashboards-home-grids-and-record-forms/)

### Internal Documentation
- [AI Architecture](AI-ARCHITECTURE.md) — Four-tier AI framework, SprkChat agent factory
- [SDAP Auth Patterns](sdap-auth-patterns.md) — Token acquisition, OBO flow
- [SDAP PCF Patterns](sdap-pcf-patterns.md) — Code Page deployment pipeline

---

*This architecture document is the authoritative reference for the Spaarke Side Pane Platform. All implementations must follow the patterns described here.*
