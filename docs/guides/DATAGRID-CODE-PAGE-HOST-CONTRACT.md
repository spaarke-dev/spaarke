# DataGrid Code-Page Host Contract

> **Audience**: Developers building a new Spaarke Custom Page that hosts the `<DataGrid>` framework, OR migrating an existing one.
> **Companion doc**: [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) (what the framework does) and [`DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md) (how to author `sprk_gridconfiguration`).
> **Last Updated**: 2026-06-04 (post task 035 UAT iteration 5)

---

## The promise

If a developer wants to add a DataGrid Code Page to a NEW surface, the scaffolding for column sort, column filter, command bar, formatting, dark mode, parent-context drill-through, and side-pane-driven filtering should already exist. The developer authors **the configjson** and **a thin shell**, period.

This document is the SHELL contract. If you follow it, your Custom Page will render and behave identically to every other DataGrid Custom Page in Spaarke (InvoicesPage, KPI Assessments, EventsPage, …). If you DON'T follow it, expect subtle divergences (the kind that ate task 035's UAT cycle).

---

## TL;DR — use `DataGridPageShell`

```tsx
import { DataGridPageShell } from '@spaarke/ui-components';

export const App = () => (
  <DataGridPageShell
    configId="…sprk_gridconfiguration record GUID…"
    useUrlParentContext={{ key: 'matterId' }}      // optional — parse the URL data= envelope
    onBack={() => window.close()}                  // optional — adds the back arrow
    sidePaneFilter={{                              // optional — wires a side pane to hostFilters
      paneId: 'my-filter-pane',
      translator: payload => /* HostFilterCondition[] */
    }}
  />
);
```

The shell wraps `<DataGrid>` with the canonical FluentProvider + theme listener + box-sizing reset injection + URL parsing + side-pane filter subscription. The host code shrinks to ~10 lines.

**For pages that need additional custom logic** (event-detail side panes, custom command handlers, etc.), you still mount `<DataGrid>` directly — but follow §3 below carefully.

---

## 1. Host shell anatomy (minimum)

A Custom Page hosting `<DataGrid>` is a Vite + React single-file bundle. The minimum file set:

```
src/solutions/<your-page-name>/
├── package.json         # vite + react + @spaarke/ui-components workspace dep
├── tsconfig.json
├── vite.config.ts       # rollup singlefile plugin
├── index.html           # ← CRITICAL — see §2
└── src/
    ├── main.tsx         # createRoot mount
    └── App.tsx          # 10-line shell using DataGridPageShell
```

Use [`templates/spaarke-codepage-with-datagrid/`](../../templates/spaarke-codepage-with-datagrid/) as the copy-paste starting point.

---

## 2. The `index.html` CSS contract (NON-NEGOTIABLE)

The DataGrid component depends on the host page's CSS reset. The minimum required `<style>` in `index.html`:

```html
<style>
  /* 1) box-sizing: border-box on every element. Without this, the DataGrid's
        internal padding ADDS to the 100%-of-viewport width/height and the
        grid overflows the modal frame on the right + bottom. This was the
        root cause of task 035 UAT iterations 1-3. */
  *, *::before, *::after {
    box-sizing: border-box;
  }

  /* 2) Pin html/body/#root to the viewport. `height: 100%` requires every
        ancestor to have explicit height — this chain establishes it. */
  html, body, #root {
    margin: 0;
    padding: 0;
    width: 100%;
    height: 100%;
    overflow: hidden;
  }

  /* 3) Cap to 100vh as a safety in case the iframe parent gives us auto-height
        (some MDA form-tab embeds do this — caused task 035 iteration 5's
        "outer scrollbar on entire page section"). */
  html, body, #root {
    max-height: 100vh;
  }

  /* 4) Ensure FluentProvider's wrapper div fills its container. FluentProvider
        renders a <div> as #root's first child; this rule keeps the height
        chain unbroken into the DataGrid root. */
  #root > div {
    height: 100%;
  }
</style>
```

**Why this matters**: each Custom Page bundle is a separate iframe in MDA. The DataGrid manages its own internal scroll (the inner grid card's `gridScroll` overflows). If the OUTER HTML/body overflows because of missing reset, the user sees double scrollbars and incorrect chrome.

The canonical reference shells are:
- [`src/solutions/sprk_invoicespage/index.html`](../../src/solutions/sprk_invoicespage/index.html) (drill-through dialog)
- [`src/solutions/sprk_kpiassessmentspage/index.html`](../../src/solutions/sprk_kpiassessmentspage/index.html) (drill-through dialog)
- [`src/solutions/EventsPage/index.html`](../../src/solutions/EventsPage/index.html) (standalone + embedded + dialog)

If you author a new `index.html` by hand instead of copying the template, run the visual smoke test (§7) before believing it.

---

## 3. The `App.tsx` mount contract

The minimum mount via `DataGridPageShell` is the TL;DR above. If your page needs additional logic (a custom row-open behavior, a side pane lifecycle that does more than filter, etc.), mount `<DataGrid>` directly but follow this skeleton:

```tsx
import * as React from 'react';
import { FluentProvider } from '@fluentui/react-components';
import {
  DataGrid,
  XrmDataverseClient,
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from '@spaarke/ui-components';

const CONFIG_ID = '…sprk_gridconfiguration record GUID…';
const dataverseClient = new XrmDataverseClient();

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  React.useEffect(
    () => setupCodePageThemeListener(() => setTheme(resolveCodePageTheme())),
    []
  );

  // Parse URL params for parent context if your page is a drill-through.
  const parentContext = React.useMemo(() => /* parse */, []);

  // Your custom hostFilters source (if any) — e.g. side pane subscription.
  const [hostFilters, setHostFilters] = React.useState<HostFilterCondition[]>([]);

  return (
    <FluentProvider theme={theme} applyStylesToPortals={true} style={{ height: '100%' }}>
      <DataGrid
        configId={CONFIG_ID}
        dataverseClient={dataverseClient}
        theme={theme}
        parentContext={parentContext}
        hostFilters={hostFilters}
        onBack={() => window.close()}
        // ... other DataGrid props as needed
      />
    </FluentProvider>
  );
};
```

**Rules**:

| Rule | Why |
|---|---|
| FluentProvider wraps DataGrid | dark mode + token theming. `applyStylesToPortals={true}` is REQUIRED for popover surfaces (NFR-03) |
| `style={{ height: '100%' }}` on FluentProvider | keeps the height chain alive into the DataGrid root |
| `theme` prop to DataGrid AND theme prop to FluentProvider | passing it twice is intentional — FluentProvider colors the React tree; the DataGrid-level prop colors its CommandBar's dialog portals |
| ONE `XrmDataverseClient` instance, module-scope or useRef | prevents repeated Xrm context lookups |
| Do NOT wrap DataGrid in an extra `<div>` | the InvoicesPage shell has FluentProvider → DataGrid as direct parent/child. Any intermediate wrappers can break the height chain. Task 035 iter 2 hit this. |
| StrictMode at `main.tsx` level, not App | React 18 idiomatic; the shell handles strict-mode double-mounting (idempotent registration) |

---

## 4. Parent context (drill-through pages)

For Custom Pages opened from a VisualHost CardChrome expand (drill-through dialog) or a form-embedded iframe, the URL carries a `data=` envelope:

```
?data=mode%3Ddialog%26entityName%3Daccount%26filterValue%3D{matterGUID}
```

Parse it as:

```tsx
function parseParentContextFromUrl(): DataGridParentContext | undefined {
  const params = new URLSearchParams(window.location.search);
  const data = params.get('data');
  if (!data) return undefined;
  const inner = new URLSearchParams(data);
  const matterId = inner.get('filterValue') ?? inner.get('matterId');
  return matterId ? { entityType: 'sprk_matter', id: matterId, name: '' } : undefined;
}
```

Then the configjson's `behavior.parentContextFilter` declares how to overlay it:

```json
"behavior": {
  "parentContextFilter": {
    "attribute": "sprk_matter",
    "parentContextKey": "matterId",
    "operator": "eq"
  }
}
```

`DataGridPageShell`'s `useUrlParentContext` prop is shorthand for the common case (single matterId from the `data=` envelope).

For non-trivial parent contexts (multi-key, JSON envelope, fallback to `Xrm.Page`), parse manually and pass `parentContext` directly to `<DataGrid>` — see [`sprk_invoicespage/src/main.tsx`](../../src/solutions/sprk_invoicespage/src/main.tsx)'s `parseMatterId` as the canonical multi-fallback implementation.

---

## 5. Side panes that drive grid filtering — generalized

The pattern from EventsPage's calendar side pane is now **a first-class framework feature**. Any Custom Page can have one or more side panes that emit filter messages; the DataGrid's `hostFilters` prop consumes them via the framework's `useSidePaneFilter` hook.

### Architecture

```
┌────────────────────────────────┐         ┌─────────────────────────────────┐
│  Side-pane web resource         │         │  Hosting Custom Page             │
│  (e.g. sprk_calendarsidepane)   │         │  (e.g. sprk_eventspage)          │
│                                  │         │                                  │
│  Your UI (calendar, range,      │         │  <DataGridPageShell              │
│  saved filters, AI, map, …)     │         │    configId="…"                  │
│           │                      │         │    sidePaneFilter={{             │
│           ▼                      │         │      paneId: 'date-filter',     │
│  sendSidePaneFilter({             │ msg     │      translator: payload =>     │
│    paneId: 'date-filter',        ├────────►│        toHostFilters(payload)   │
│    payload: {…your shape…}        │         │    }}                            │
│  })                              │         │  />                              │
└────────────────────────────────┘         └─────────────────────────────────┘
                                                          │
                                                          ▼
                                              DataGrid's FetchXML composition:
                                              base → parentContext → hostFilters
                                                                       ↑
                                                                  (you are here)
```

### Side-pane web resource side

In your side pane's React code:

```tsx
import { sendSidePaneFilter } from '@spaarke/ui-components/sidepane';

const handleApply = () => {
  sendSidePaneFilter({
    paneId: 'date-filter',
    payload: { dateField: 'sprk_duedate', from: '2026-06-01', to: '2026-06-30' },
  });
};
```

The send helper uses BroadcastChannel as the primary cross-iframe transport with `window.postMessage` + custom-event fallbacks (handles sandboxed iframes).

### Hosting Custom Page side

Easy mode (via `DataGridPageShell`):

```tsx
<DataGridPageShell
  configId="…"
  sidePaneFilter={{
    paneId: 'date-filter',
    translator: (payload: any): HostFilterCondition[] => {
      if (!payload?.from || !payload?.to) return [];
      return [{
        attribute: payload.dateField ?? 'sprk_duedate',
        operator: 'between',
        value: [payload.from, payload.to],
      }];
    },
  }}
/>
```

Manual mode (mounting `<DataGrid>` directly):

```tsx
import { useSidePaneFilter } from '@spaarke/ui-components';

const hostFilters = useSidePaneFilter('date-filter', payload => {
  // payload is whatever the side pane sent
  return [/* HostFilterCondition[] */];
});

return <DataGrid hostFilters={hostFilters} … />;
```

The translator is the ONLY page-specific code. Adding a new side-pane filter type (saved filters, AI assistant, geographic, etc.) is: write the pane, write the translator, done. The framework handles transport + subscription + lifecycle.

### Side-pane LIFECYCLE (open/close/mutual exclusivity)

If your Custom Page also OPENS the side pane (vs. the side pane being opened by the MDA shell), use `DataGridSidePaneOrchestrator`:

```tsx
import { DataGridSidePaneOrchestrator } from '@spaarke/ui-components';

const orchestrator = React.useMemo(() => new DataGridSidePaneOrchestrator(), []);

React.useEffect(() => {
  void orchestrator.registerPane({
    paneId: 'date-filter',
    title: 'Date Filter',
    webResourceName: 'sprk_calendarsidepane.html',
    width: 340,
    iconName: 'WebResources/sprk_calendarline_24',
  });
  return () => orchestrator.closeAll();
}, []);
```

The orchestrator handles:
- `Xrm.App.sidePanes.createPane` registration (idempotent)
- IntersectionObserver-based hide/show lifecycle (closes on form-tab navigation away, reopens on return)
- Mutual exclusivity between multiple panes (e.g. detail pane vs. filter pane — opening one closes the other)
- `beforeunload` + `pagehide` cleanup

For pages that don't manage their own pane registration (e.g. when MDA opens the pane through its sitemap), skip the orchestrator and use only `useSidePaneFilter`.

---

## 6. Theme

Always use the framework's theme helpers:

```tsx
import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';

const [theme, setTheme] = React.useState(resolveCodePageTheme);
React.useEffect(
  () => setupCodePageThemeListener(() => setTheme(resolveCodePageTheme())),
  []
);
```

`resolveCodePageTheme` reads Dataverse's current theme (light/dark) and returns the matching Fluent v9 theme. `setupCodePageThemeListener` re-renders on theme change. Both handle the cases where Dataverse exposes the theme via `Xrm.Utility.getGlobalContext().userSettings.themeName` AND the cases where you have to fall back to `prefers-color-scheme`.

`DataGridPageShell` does this for you.

---

## 7. Visual smoke checklist (run before claiming "done")

Open your Custom Page in each of the contexts your design covers:

| Context | Smoke checks |
|---|---|
| Standalone modal (`Xrm.Navigation.navigateTo` `pageType: 'webresource'`) | Two-card chrome visible (header card + grid card with rounded borders + shadows + breathing room). Column-header chevrons work. Filter chevron opens a popover. Sort works (column header click). Filter chevron click DOES NOT trigger sort. |
| Dialog (drill-through from a VisualHost CardChrome expand) | All of the above. Plus: row click opens the configured `rowOpen.type` surface IN FRONT of the dialog (not behind — that was the EventsPage record-link bug). Parent-context filter applied if configjson `behavior.parentContextFilter` set. |
| Form-tab iframe embed (e.g. Matter form Calendar tab) | Chrome fits the iframe viewport. Only the inner grid card scrolls — NOT the outer matter form section. Page section does not show a second scrollbar. |
| Dark mode | All surfaces use `tokens.*` — no raw white/black panels. Popovers (filter chip + chevron menu) render with correct dark backgrounds (`applyStylesToPortals={true}` is doing its job). |

If any of these fail, do NOT ship until fixed — they're all framework-uniform behaviors.

---

## 8. Framework-change deploy contract

If you change ANY file under `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` (or transitive dependencies like `services/XrmDataverseClient`, `services/IDataverseClient`), the change must propagate to EVERY consumer's bundle. Use:

```powershell
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
.\scripts\Deploy-AllDataGridConsumers.ps1
```

The script's consumer registry (top of the file) is the source of truth — extend it when a new Custom Page mounts `<DataGrid>`.

Do NOT cherry-pick individual `Deploy-EventsPage.ps1` / `Deploy-CorporateWorkspace.ps1` runs after framework changes. That was the workflow that produced 5 iterations of task 035 UAT — each consumer drifting from the framework in slightly different ways.

---

## 9. When NOT to use this pattern

| Surface | Use this contract? | Use instead |
|---|---|---|
| Standalone Custom Page hosting `<DataGrid>` | ✅ Yes | — |
| Drill-through dialog Custom Page hosting `<DataGrid>` | ✅ Yes | — |
| Form-embedded iframe Custom Page hosting `<DataGrid>` | ✅ Yes | — |
| MDA sub-grid (system grid on a form) | ❌ No | OOB Power Platform grid; the framework targets Code Pages, not sub-grids |
| Workspace widget (inside SpaarkeAi/LegalWorkspace section) mounting `<DataGrid>` | ⚠️ Partial | Inherits FluentProvider from the workspace shell — do NOT add a second one. Use `<DataGrid>` directly without `DataGridPageShell`. The Calendar widget at [`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/`](../../src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/) is the canonical example. |
| PCF control | ❌ No | PCFs have their own scaffolding (manifest, control class, lifecycle hooks). The framework's `<DataGrid>` does NOT target PCF hosting; the legacy `UniversalDatasetGrid` PCF retires in Phase F. |

---

## 10. References

- Framework architecture: [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)
- Configjson schema: [`DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md)
- Parent-context pattern: [`projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md`](../../projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md)
- Deploy script: [`scripts/Deploy-AllDataGridConsumers.ps1`](../../scripts/Deploy-AllDataGridConsumers.ps1)
- Canonical thin shell: [`src/solutions/sprk_invoicespage/src/main.tsx`](../../src/solutions/sprk_invoicespage/src/main.tsx)
- Canonical complex shell (with side pane): [`src/solutions/EventsPage/src/App.tsx`](../../src/solutions/EventsPage/src/App.tsx)
- Canonical workspace-widget mount: [`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`](../../src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx)
- Code Page template: [`templates/spaarke-codepage-with-datagrid/`](../../templates/spaarke-codepage-with-datagrid/)

---

## Operator quote that drove this doc

> "we do not want to go through this same UI review and back/forth every time we deploy the dataset grid — this is always going to be part of the grid"
> "for the dataset grid my expectation is that if we want to add the dataset grid code page to a surface (any surface) that the shared and reusable components will make this very easy because none of the scaffolding and core functions (like column sorting, filtering; command bar; formatting/design) will have to be modified. there is one dataset grid component set"
> "we need to include the side pane context injection for the calendar type interaction — generalized so we can use this in other use cases"

— Task 035 UAT operator feedback, 2026-06-04.
