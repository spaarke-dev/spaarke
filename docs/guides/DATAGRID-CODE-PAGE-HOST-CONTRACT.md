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
| Workspace widget (inside SpaarkeAi/LegalWorkspace section) mounting `<DataGrid>` | ⚠️ See §9.1 | Different contract — workspace shell owns the FluentProvider + theme. See §9.1. |
| PCF control | ❌ No | PCFs have their own scaffolding (manifest, control class, lifecycle hooks). The framework's `<DataGrid>` does NOT target PCF hosting; the legacy `UniversalDatasetGrid` PCF retires in Phase F. |

---

## 9.1 Embedded workspace-widget hosts (NEW — iter-2 round 11, 2026-06-09)

When `<DataGrid>` is mounted **inside a workspace widget** (not as a standalone
Code Page), the host is the parent Code Page (SpaarkeAi, LegalWorkspace,
etc.), not the widget itself. The widget INHERITS the host's FluentProvider,
theme, and box-sizing reset. **Five additional contract rules** apply that
the standalone §1-§8 path does not need.

### 9.1.1 The host Code Page's `index.html` MUST have the box-sizing reset

The §2 rule (`*, *::before, *::after { box-sizing: border-box }`) is binding
for the host Code Page even when the host itself is not a DataGrid surface.
Without it every embedded `DataGridCell` renders **+24px** wider than its
declared width (`12px padding-left + 12px padding-right` adds to content-box),
which sums to ~100-150px of column overshoot over a typical 4-6 column grid
and produces a permanent horizontal scrollbar inside the widget.

Audit your host Code Page's `index.html`:

```bash
grep -l box-sizing src/solutions/<YourPage>/index.html || echo "MISSING — add the §2 reset"
```

Canonical reference: [`src/solutions/SpaarkeAi/index.html`](../../src/solutions/SpaarkeAi/index.html)
(added round 11). Verified failure mode: 17 of 23 Code Pages in this repo
were missing this as of 2026-06-09; see the audit note below the table.

### 9.1.2 The flex/grid `min-width: 0` chain MUST be unbroken

Every container between the workspace section card and the `<DataGrid>` root
MUST set `min-width: 0` (in addition to `min-height: 0` if it's a flex/grid
column). The default `min-width: auto` resolves to `max-content`, which lets
the FluentDataGrid's intrinsic column-sum width inflate every ancestor.

The known-required chain inside SpaarkeAi:

```
Workspace row (display: grid; grid-template-columns: 1fr)
  └── SectionPanel.card                       ← needs min-width: 0  (round 7)
       └── DataverseEntityViewWidget root     ← needs min-width: 0  (round 6)
            └── inner pixel-cap wrapper       ← width: ${measured}px (round 9 — see §9.1.3)
                 └── DataGrid root            ← needs min-width: 0  (round 6)
                      └── innerCard           ← needs min-width: 0  (round 6)
                           └── gridScroll     ← needs min-width: 0  (round 6)
                                └── FluentDataGrid (Fluent v9 internal)
```

If you build a new widget that wraps `<DataGrid>`, copy
[`DataverseEntityViewWidget`'s style chain](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx) verbatim
or risk introducing the same bug.

### 9.1.3 Widget root needs a ResizeObserver-driven pixel cap

The body-level `overflow: hidden` from §2 acts as the page-wide boundary for
a standalone Code Page. Embedded widgets have no such single boundary — the
section card can be any width depending on the workspace row's grid template
and the user's pane sizing.

The widget MUST measure its own outer width via a `ResizeObserver` and apply
that value as an explicit inline `width: ${px}px; maxWidth: ${px}px;` on a
wrapper that the `<DataGrid>` mounts into. This creates an explicit pixel
containing block that the inner FluentDataGrid's column layout cannot escape.

Reference: [`DataverseEntityViewWidget.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx)
(`useLayoutEffect` for synchronous initial measurement + `useEffect` +
`ResizeObserver` for live tracking).

### 9.1.4 Fluent v9's hardcoded `min-width: fit-content` must be overridden

`@fluentui/react-components` v9's `DataGrid` hardcodes
`min-width: fit-content` as an INLINE style on its root `<div role="grid">`.
CSS class selectors WITHOUT `!important` cannot beat an inline style. The
DataGrid component (`src/client/shared/Spaarke.UI.Components/src/components/
DataGrid/DataGrid.tsx`) already includes a Griffel `gridTableOverride` class
with `!important` that targets `& > [role="grid"]` to neutralize this — you
get it for free as long as you mount via `<DataGrid>`. Do not mount a raw
`<FluentDataGrid>` from Fluent v9 inside a workspace widget.

### 9.1.5 Column math: 2nd-pass redistribution + per-cell padding reserve

DataGrid's `columnSizingOptions` math already accounts for both:
- A 2nd pass that redistributes overshoot when some columns hit their
  `minWidth` floor and others have slack (round 10).
- A `visibleColumns.length × 24px` reserve in the `available` calculation
  so columns still fit if a host forgets the §9.1.1 box-sizing reset (round 11).

You do not need to do anything for this — it's framework-internal — but
DO NOT remove these passes when refactoring the DataGrid component; both
were added in response to operator-validated overshoot bugs.

### 9.1.6 Diagnostic script

If a future embedded DataGrid renders past its container, paste this into
DevTools Console (after switching the frame selector to the Code Page's
iframe) to walk the layout chain:

```js
(function(){
  const labels=['Active Documents','Active Projects','Active Matters','Active Events','All Active Events'];
  let g=null;
  for(const l of labels){g=document.querySelector(`div[role="grid"][aria-label="${l}"]`);if(g)break;}
  if(!g){g=document.querySelector('div[role="grid"]');}
  if(!g){console.log('no role="grid" on page');return;}
  console.log('FluentDataGrid cw=%d sw=%d inline-style=%s',g.clientWidth,g.scrollWidth,g.getAttribute('style'));
  g.querySelectorAll('[role="columnheader"]').forEach((c,i)=>console.log(`col[${i}] rendered=${c.offsetWidth}px style="${c.getAttribute('style')||''}"`));
  let n=g,d=0;
  while(n&&d<14){console.log(`[${d}] cw=${n.clientWidth} sw=${n.scrollWidth} inline="${(n.getAttribute('style')||'').slice(0,90)}"`);n=n.parentElement;d++;}
})()
```

Interpretation:
- If **column rendered width = declared width + 24px** consistently → §9.1.1
  reset missing in host index.html.
- If **table scrollWidth > container clientWidth but rendered widths match
  declared** → §9.1.5 column-math bug; file a regression issue.
- If **section card cw differs from workspace row track width** → §9.1.2
  min-width:0 chain broken somewhere; walk up the output until you find the
  first parent whose `cw` matches the table's `sw`.

### 9.1.7 The known reference implementations

| Widget | File | What it shows |
|---|---|---|
| Direct embed | [`DataverseEntityViewWidget.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx) | The canonical 9.1.2 + 9.1.3 + 9.1.4 reference. Copy this style chain for any new widget wrapping `<DataGrid>`. |
| Calendar (composite) | [`CalendarWorkspaceWidget.tsx`](../../src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx) | Same layout chain, plus a filter chip toolbar above the grid. |
| LegalWorkspace section shim | [`documents.registration.ts`](../../src/solutions/LegalWorkspace/src/sections/documents.registration.ts) | The SectionRegistration that mounts a widget. Just sets the configId — all the layout work lives in the widget. |

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
