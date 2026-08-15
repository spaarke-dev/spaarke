# Spaarke Side-Pane Navigation (the "Navigator")

> **Audience**: Developers adding the Navigator to a new entity or a new code page, or maintaining the Navigator itself.
> **Companion doc**: [`DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](../guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md) (the sibling code-page host contract for `<DataGrid>`).
> **Project**: `spaarke-side-pane-navigation-history-r1`
> **Last Updated**: 2026-08-14

---

## 1. Overview

The **Navigator** is a docked, always-available Dataverse UCI side pane that gives every user a persistent, cross-record navigation history — the "recently viewed" experience browsers give you natively, which model-driven apps do not.

It has four tabs plus a persistent search bar:

| Tab | Content |
|---|---|
| **Recent** | Records the user has viewed, captured passively (no click required), most-recent first |
| **Bookmarks** | Records + saved views + raw weblinks the user has explicitly starred/pinned ("Pin this page") |
| **Monitored** | Records the user is following (owner-scoped "assigned to me" view — Path A; a BFF membership resolver for Path B remains an open, non-blocking decision) |
| **Views** | The user's personal (`userquery`) saved views, grouped by entity, via `ViewService` |
| **Search (QuickSwitcher)** | A top-of-pane search bar: local fuzzy-match over the Recent/Bookmarks/Views entries first, escalating to a live `Xrm.WebApi`/`ViewService` lookup on no local hit |

**Data model**: one entity, `sprk_navitem` (per-user, `UserOwned`, deployed unmanaged into the `SpaarkeCore` solution). A `sprk_type` option-set (`History` / `Pin`) distinguishes passively captured rows from explicit pins; `sprk_source` (`Captured` / `Manual`) and `sprk_pagetype` (`EntityRecord` / `EntityList` / `Custom` / `WebLink`) round out the shape. No new tables beyond `sprk_navitem`.

**Access pattern**: **host-context `Xrm.WebApi` only**, under the signed-in user. There is **no BFF endpoint and no Dataverse plugin** for the Navigator — reads and writes are both a browser-side `Xrm.WebApi` call, same as `Spaarke.UI.Components`'s `XrmDataverseClient` pattern elsewhere. This is a project HARD constraint (`projects/spaarke-side-pane-navigation-history-r1/CLAUDE.md`), not an oversight — see §3 for why the capture mechanism has to be zero-plugin/zero-form-handler too.

---

## 2. Architecture / components

```
┌─────────────────────────────────────────────────────────────────┐
│ Dataverse UCI shell                                              │
│                                                                    │
│   Xrm.App.sidePanes (app-level, persists across in-app nav)      │
│     └── pane "sprk-navigator" (canClose:false, alwaysRender:true)│
│           └── webresource: sprk_NavigatorPane.html                │
│                 └── App.tsx  (root FluentProvider)                 │
│                       └── NavigatorBody.tsx  (tabs + search)      │
│                             ├── RecentTab / BookmarksTab /        │
│                             │   MonitoredTab / ViewsTab            │
│                             ├── QuickSwitcher (search)             │
│                             └── startNavigatorCapture()  (poll)    │
│                                                                    │
│   sprk_SidePaneManager (JScript web resource)                     │
│     Spaarke.SidePaneManager.initialize / .openPane                │
│     → createPane + navigate (mirrors the code-page registrar)     │
└─────────────────────────────────────────────────────────────────┘
```

| Component | File | Role |
|---|---|---|
| Navigator code page | `src/solutions/NavigatorPane/` (Vite single-file bundle → `sprk_NavigatorPane.html`) | The pane's content |
| Root mount | `src/solutions/NavigatorPane/src/App.tsx` | Root `FluentProvider` (theme + `--sprk-ui-scale`) wrapping `NavigatorBody` **directly** |
| Pane body | `src/solutions/NavigatorPane/src/NavigatorBody.tsx` | 4-tab layout + `QuickSwitcher` search bar; owns starting the capture poll |
| Bootstrap JS | `src/solutions/NavigatorPane/bootstrap/sprk_SidePaneManager.js` | Ribbon-invoked `Spaarke.SidePaneManager.initialize`/`.openPane` — the Path B app-startup registrar |
| Rail icon | `src/solutions/NavigatorPane/bootstrap/sprk_navigatorstar.svg` | The pane's collapsed-rail launcher icon (a star) |
| Capture poll | `src/client/shared/Spaarke.UI.Components/src/services/navigator/navigatorCaptureService.ts` | 1.5s poll of `Xrm.Utility.getPageContext()`, upserts `sprk_navitem` history rows; started from `NavigatorBody`'s mount |
| Data access | `src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts` | `Xrm.WebApi` CRUD for `sprk_navitem` (mirrors `Notepad/src/hooks/useSprkMemoRepository.ts`) |
| Security trim | `src/solutions/NavigatorPane/src/services/securityTrimService.ts` | Read-time re-validation of cached record names (see below) |
| Views | `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts` | Reused (not reimplemented) for the Views tab and QuickSwitcher's live fallback |

### Why `App.tsx` mounts `NavigatorBody` directly, not `SprkSidePaneHost`

`Spaarke.UI.Components` also ships a general multi-contributor side-pane framework (`SprkSidePaneHost` + `sidePaneRegistry`, task 011/085) for pages that need to host *several* independent pane contributors behind one rail. The Navigator does **not** use it. During task-086 UAT the deployed pane rendered blank in the live app (while rendering fine headlessly) — root cause was mounting the full multi-contributor `SprkSidePaneHost` (pane-lifecycle orchestrator + async lazy contributor resolution + rail) inside a webresource that only ever has ONE contributor. The fix (commit `cd86c1323`) mirrors the already-proven `CalendarSidePane` pattern: a plain root `FluentProvider` wrapping the pane body directly. **Lesson, stated for future reuse**: a single-contributor side-pane code page mounts its body directly under a root `FluentProvider` — reach for `SprkSidePaneHost` only when a pane genuinely needs to host multiple independent contributors behind one rail.

### Capture poll

`startNavigatorCapture()` is a plain start/stop function (not a React hook) polling `Xrm.Utility.getPageContext()` every `DEFAULT_CAPTURE_POLL_INTERVAL_MS` (1.5s). It re-acquires `Xrm` fresh via `getXrm()` on every tick — **never caches the `Xrm` reference** (a task-001 spike lesson: a cached reference goes stale across MDA navigations). Only `pageType==='entityrecord'` pages with both an entity name and id are written as history; `entitylist`/`dashboard`/`custom` pages are skipped (no single resolvable record target). The loop has no dependency on pane visibility — `alwaysRender:true` on the pane keeps it running while the pane is collapsed.

### Security trim

The Recent/Bookmarks tabs render **cached** display names off `sprk_navitem`. If the signed-in user subsequently loses access to the underlying record (ethical-wall revocation, matter unassignment, etc.), the cached name must never render again — a confidentiality requirement, not a UX nicety. `securityTrimService.ts` re-validates each row with a lightweight `Xrm.WebApi.retrieveRecord` call (host-context, signed-in user) and classifies each row `accessible` / `denied` (drop the row) / `transient` (network blip — keep the row; never throws). Only `EntityRecord`-typed rows are checked; `WebLink`/`EntityList` rows carry no leakable cached record identity and are exempt.

### Retention

History rows older than `HISTORY_RETENTION_DAYS` (**30 days**) are pruned inline, on-write, after every successful capture upsert (`navigatorCaptureService.ts` → `navItemRepository.deleteHistoryItemsOlderThan`) — no scheduled job, no plugin. **Pins never auto-expire.**

---

## 3. The launch / auto-load model (the important part)

Modern UCI has **no supported global "run JS on every page load" hook**. Form and grid `OnLoad`/`OnChange` events are per-entity; the AppModule-level `onload` some legacy add-ins relied on is unsupported; and Spaarke's own retired side-pane platform's global ribbon enable-rule had regressed (gone dormant) by the time this project started. Because of this, the Navigator uses **two separate insertions** to reach "auto-dock everywhere," not one:

### (a) Entity command-bar enable rule — silent auto-dock on that entity's grid + form

A Navigator button is added to an entity's ribbon (both the homepage grid and the record form). Its `EnableRule` is a `CustomRule` that calls `Spaarke.SidePaneManager.initialize` with `Default="true"` — this rule **fires silently every time the entity's command bar is evaluated** (grid load or form load), and its side effect is registering the app-level pane. The button itself is a normal, clickable "open/focus the Navigator" affordance; the auto-registration rides along on the enable-rule evaluation regardless of whether the user ever clicks it.

**Working proof**: `projects/spaarke-side-pane-navigation-history-r1/deploy/ribbon/MatterRibbons.customizations.xml` (the `sprk_Matter` entity). The four RibbonDiffXml pieces, verbatim:

```xml
<!-- 1. Grid button -->
<CustomAction Id="sprk.Navigator.Matter.Grid.Button.CustomAction"
              Location="Mscrm.HomepageGrid.sprk_matter.MainTab.Actions.Controls._children" Sequence="1">
  <CommandUIDefinition>
    <Button Id="sprk.Navigator.Matter.Grid.Button" Command="sprk.Navigator.Open.Command"
            LabelText="$LocLabels:sprk.Navigator.Button.LabelText"
            Alt="$LocLabels:sprk.Navigator.Button.Alt"
            ToolTipTitle="$LocLabels:sprk.Navigator.Button.LabelText"
            ToolTipDescription="$LocLabels:sprk.Navigator.Button.ToolTipDescription"
            TemplateAlias="o1" ModernImage="$webresource:sprk_navigatorstar.svg" />
  </CommandUIDefinition>
</CustomAction>

<!-- 2. Form button -->
<CustomAction Id="sprk.Navigator.Matter.Form.Button.CustomAction"
              Location="Mscrm.Form.sprk_matter.MainTab.Actions.Controls._children" Sequence="1">
  <CommandUIDefinition>
    <Button Id="sprk.Navigator.Matter.Form.Button" Command="sprk.Navigator.Open.Command"
            LabelText="$LocLabels:sprk.Navigator.Button.LabelText"
            Alt="$LocLabels:sprk.Navigator.Button.Alt"
            ToolTipTitle="$LocLabels:sprk.Navigator.Button.LabelText"
            ToolTipDescription="$LocLabels:sprk.Navigator.Button.ToolTipDescription"
            TemplateAlias="o1" ModernImage="$webresource:sprk_navigatorstar.svg" />
  </CommandUIDefinition>
</CustomAction>

<!-- 3. CommandDefinition — click action (open/focus the pane) -->
<CommandDefinition Id="sprk.Navigator.Open.Command">
  <EnableRules><EnableRule Id="sprk.Navigator.Open.EnableRule" /></EnableRules>
  <DisplayRules />
  <Actions>
    <JavaScriptFunction Library="$webresource:sprk_SidePaneManager" FunctionName="Spaarke.SidePaneManager.openPane" />
  </Actions>
</CommandDefinition>

<!-- 4. EnableRule — the auto-load side effect. Fires SILENTLY on every command-bar evaluation. -->
<EnableRule Id="sprk.Navigator.Open.EnableRule">
  <CustomRule Library="$webresource:sprk_SidePaneManager" FunctionName="Spaarke.SidePaneManager.initialize" Default="true" />
</EnableRule>
```

Plus the matching `LocLabels` (`sprk.Navigator.Button.LabelText` / `.Alt` / `.ToolTipDescription`).

**How-to** (via the `/ribbon-edit` skill):
1. Export the entity's ribbon solution unmanaged (e.g. `{Entity}Ribbons`).
2. Insert the 4 blocks above into `customizations.xml`, substituting the entity's schema name for `sprk_matter` in the two `Location` attributes and the two `CustomAction Id`s.
3. `pac solution import --publish-changes`.

`ApplicationRibbon.customizations.xml` carries the equivalent **global** wiring (`Mscrm.GlobalTab.MainTab.Actions.Controls._children`, `sprk.Global.SidePaneManager.*`) — the same `Spaarke.SidePaneManager.initialize` `EnableRule` pinned once at the application-ribbon level, next to the existing `SprkChat` button. Prefer the global insertion when you want the Navigator on *every* entity's grid/form inside an app without touching each entity's own ribbon; use the per-entity insertion when only specific entities should carry the button (e.g. as a secondary, explicit trigger alongside the global one).

### (b) Code-page registrar — auto-dock on code pages

Code pages (Vite webresource bundles like SpaarkeAi, LegalWorkspace, EventsPage) have no ribbon to hang an `EnableRule` off. For these, the insertion is a **one-line import + a mount-time call**:

```tsx
import { ensureNavigatorSidePane } from "@spaarke/ui-components";

React.useEffect(() => {
  ensureNavigatorSidePane();
}, []);
```

**Reference implementation**: `src/solutions/SpaarkeAi/src/App.tsx` (mount `useEffect`, calling the thin wrapper `src/solutions/SpaarkeAi/src/ensureNavigatorPane.ts`, which now delegates to the shared `ensureNavigatorSidePane()` in `@spaarke/ui-components` — see §5 for the full shared-module reference).

**This should be part of the standard code-page build**: every new Spaarke code page adds this one line in its root mount `useEffect`, the same way it already wires `resolveCodePageTheme`/`setupCodePageThemeListener`. There is nothing else to configure — the function is idempotent and safe to call unconditionally on every mount.

### Why both insertions matter

`Xrm.App.sidePanes` panes are **app-level**, not page-scoped: once `createPane` succeeds, the pane (and its `alwaysRender:true` capture poll) persists across every subsequent **in-app** navigation — clicking between records, grids, and code pages inside the same UCI app session does not tear it down. What it does NOT survive is a **full browser refresh / new tab / fresh app load** — `Xrm.App.sidePanes` is reset with the page. That is exactly why two independent triggers exist: whichever surface the user's session happens to *start* on (an entity grid/form via the ribbon rule, or a code page via the registrar) re-establishes the pane for that fresh session; every other surface visited afterward inherits it for free from the app-level persistence.

**Known gap**: OOB Dataverse dashboards have no ribbon `EnableRule` insertion point and are not a code page — a session that starts on a dashboard (and never visits a Navigator-wired entity or code page first) will not see the pane until the user navigates somewhere that DOES carry one of the two triggers. This is a known, accepted gap (no dashboard-specific mechanism exists in modern UCI to close it).

---

## 4. Known modern-UCI constraints/caveats

- **No global load hook.** Confirmed by this project's own task-001 spike: the AppModule-level `onload` some legacy customizations relied on is unsupported on current UCI, and a global ribbon enable-rule without a rendered/pinned control never evaluates (see §7 "blank button" incident below).
- **`navigateTo` view identifiers must be STRING, not numeric.** `Xrm.Navigation.navigateTo`'s `PageInputEntityList.viewType` must be the string `'4230'` (userquery) or `'1039'` (savedquery) — passing a JS number silently falls back to the entity's default view instead of disambiguating. The Navigator's `ViewsTab.tsx` always passes `'4230'` since it only lists `userquery` views. See `xrmContext.ts`'s `PageInput.viewType` doc comment for the full contract.
- **A per-table "sticky" view selector can override the requested view.** Even with a correct `viewId`/`viewType`, some MDA table configurations remember the user's last-selected grid view and can visually override the one `navigateTo` requested. This is a platform behavior, not a bug in the Navigator's navigation call.
- **UCI renders rail `imageSrc` icons with its own styling** (sizing/coloring/hover states applied by the platform chrome) — a pane's `imageSrc` (e.g. `WebResources/sprk_navigatorstar.svg`) is a plain SVG reference, not a component; do not expect CSS control over how the platform renders it in the collapsed rail.

---

## 5. How to add the Navigator to a new surface

### New entity (grid + form auto-dock)

1. Confirm the three web resources below already exist in the target environment (they are global, not per-entity — deploy once via §6, not per new entity).
2. Export the entity's ribbon solution unmanaged (`/ribbon-edit` skill).
3. Insert the 4 RibbonDiffXml blocks from §3(a), substituting the entity's schema name.
4. `pac solution import --publish-changes`.
5. Hard-refresh the app; the enable rule fires on the entity's grid/form load and silently registers the pane — no click required.

### New code page

1. `npm install`/depend on `@spaarke/ui-components` (already a workspace dependency of every Spaarke code page).
2. Add, in the root mount component's `useEffect` (once, on mount):
   ```tsx
   import { ensureNavigatorSidePane } from "@spaarke/ui-components";
   React.useEffect(() => { ensureNavigatorSidePane(); }, []);
   ```
3. Nothing else — no props, no config, no cleanup needed (the function never throws and is a safe no-op on repeat calls/remounts).

This should be treated as a **standard step in the code-page build checklist**, alongside theme detection and `--sprk-ui-scale` wiring — every new code page should carry this line by default.

---

## 6. Web resources + deploy

| Web resource | Type | Source |
|---|---|---|
| `sprk_NavigatorPane.html` | 1 (HTML) | `src/solutions/NavigatorPane/dist/index.html` (Vite single-file bundle) |
| `sprk_SidePaneManager` | 3 (JScript) | `src/solutions/NavigatorPane/bootstrap/sprk_SidePaneManager.js` |
| `sprk_navigatorstar.svg` | 11 (SVG) | `src/solutions/NavigatorPane/bootstrap/sprk_navigatorstar.svg` |

Deployed by `src/solutions/NavigatorPane/Deploy-NavigatorPane.ps1` — an UPSERT script (create-if-missing, else `PATCH` content) using `az account get-access-token` + raw Web API calls, followed by `PublishXml` for all three. The `sprk_SidePaneManager` web resource name is load-bearing **exact** — the still-live application-ribbon `EnableRule` references `Library="$webresource:sprk_SidePaneManager"` / `FunctionName="Spaarke.SidePaneManager.initialize"` by that literal name; recreating the web resource under that exact name reactivates the rule with no ribbon re-import needed for the base bootstrap wiring.

**PS 5.1 gap for the SVG create**: creating a **new** (not yet existing) web resource and reading its generated id back from the response needs `-ResponseHeadersVariable` (to read `OData-EntityId` from the POST response headers), which is a PowerShell 7+ (`pwsh`) feature — Windows PowerShell 5.1 does not support it. The script falls back to a follow-up `GET` by name when the header isn't available, but running under `pwsh` (or issuing the create via a plain Web API call outside PS 5.1) avoids the extra round-trip. This mirrors `notes/086-deploy-evidence.md`'s "Blank-render root cause + fix" investigation.

Entity schema (`sprk_navitem`) is deployed via raw Web API + PowerShell (NOT `pac`), into the `SpaarkeCore` unmanaged solution, `UserOwned`, global option sets created first, then published — per `projects/spaarke-side-pane-navigation-history-r1/CLAUDE.md`.

---

## 7. Reference: recovering a dormant global rule (context for §3's design)

This project's Navigator reused a **pre-existing but dormant** application-ribbon `EnableRule` (`sprk.Global.SidePaneManager.Command` / `Spaarke.SidePaneManager.initialize`) left over from a retired side-pane platform. The rule had gone dormant for two independent reasons, both fixed as part of task 086/087 (see `projects/spaarke-side-pane-navigation-history-r1/notes/086-deploy-evidence.md` for the full incident writeup):

1. The `sprk_SidePaneManager` web resource it referenced had been deleted — recreating it under the exact same name (§6) reactivated the reference.
2. The button carrying the rule was rendering **blank** (`LabelText=""`, no icon) and had `Default="false"` — a blank/hidden control's enable rule never evaluates. Fixing the label/icon/`ModernImage`, setting `Default="true"`, and wiring the click `Command` to `Spaarke.SidePaneManager.openPane` (via `/ribbon-edit` on the exported `ApplicationRibbon` solution) made the rule fire on every app load.

The lesson generalizes: an `EnableRule` on a control that never renders (blank label, no icon, `Default="false"`) will never fire, silently. If you add a new command-bar-driven auto-load trigger anywhere in Spaarke, verify the carrying control actually renders visibly (or is deliberately made minimal-but-visible) before relying on its enable rule as a load-bearing side effect.
