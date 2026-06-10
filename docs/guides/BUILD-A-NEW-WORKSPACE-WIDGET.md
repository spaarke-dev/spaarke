# Build a New Workspace Widget

> **Purpose**: Operator-side decision guide + step-by-step tutorial for adding a new widget to the SpaarkeAi shell. Walks through the **two-wrapper decision tree first**, then the five archetypes that fall out of it, then the file edits + Dataverse seed + deploy sequence. The canonical worked example is the **Calendar widget** (Pattern D — dual-use, shipped R3 tasks 114 + 115, polished through R13).
>
> **Last reviewed**: 2026-05-26 (R4 task 011 / W-2). Rewritten around the two-wrapper model now codified in [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md). Previous version (Task 123, R13) catalogued Patterns A–D but did not lead with a single decision tree; this rewrite makes the wrapper choice the first 5 minutes of the read.
>
> **Audience**: A developer who has never built a SpaarkeAi widget. After §1 you should know which wrapper / archetype you need; the rest of the doc is implementation detail per archetype.
>
> **Required reading before this guide**:
> - [`../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — **the model this guide implements**. Read §1 (three surfaces), §2 (two wrappers), §3 (four mount sources), §4 (dual-use pattern).
> - [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline.
> - [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) §2A — Calendar canonical Pattern D reference.
> - [`../../CLAUDE.md`](../../CLAUDE.md) §10 — **binding** rules if you add ANY BFF code.
> - [`../../.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — required pre-merge checklist for BFF additions.

---

## 1. Decision tree — which wrapper / archetype do I need?

**Two wrappers exist** (operator-finalized OC-R4-06, model doc §2). Pick one. Everything else follows from this choice.

```
START
  │
  │  Q1. Does my widget compose MULTIPLE sections inside ONE Workspace tab,
  │      and should users be able to add/remove/re-order the sections via
  │      the WorkspaceLayoutWizard (i.e. is the unit of mount a Dataverse
  │      sprk_workspacelayout record)?
  │
  ├── YES ──▶ Dashboard wrapper (the 'workspace' widget registration)
  │            │
  │            │  Q2. Is the section logic LegalWorkspace-internal (reaches
  │            │      into FeedTodoSyncContext, LW DataverseService, LW hooks)?
  │            │
  │            ├── YES ──▶ ARCHETYPE 1: Composable section (Pattern A, LW-internal)
  │            │            §2 — the 5 original sections shape
  │            │
  │            └── NO  ──▶ ARCHETYPE 3: Dual-use section (Pattern D)
  │                         §4 — Calendar shape (shared-lib widget + thin LW shim)
  │
  └── NO ──▶ Direct widget wrapper (a per-widget-type registration in WorkspaceWidgetRegistry)
              │
              │  Q3. What is the widget's body?
              │
              ├── A sophisticated single-purpose React component (its own data, UX, chrome)
              │   that owns the whole tab and is NOT composable with other sections.
              │     ──▶ ARCHETYPE 2: Sophisticated single-purpose direct widget
              │          §3 — Patterns B (Code Page dispatcher) and C (AI output)
              │
              ├── A thin wrapper that calls Xrm.Navigation.navigateTo to open a
              │   Code Page wizard / form / page as a MODAL — the widget itself
              │   just launches and re-launches the modal.
              │     ──▶ ARCHETYPE 5: Modal-launcher (Pattern B variant)
              │          §6
              │
              └── A widget that originates IN THE CONTEXT PANE (a wizard, a card,
                  a gallery picker) and dispatches a separate widget_load to the
                  Workspace pane on completion.
                    ──▶ ARCHETYPE 4: Context-pane widget (with §3.3 dispatcher)
                         §5
```

### 1.1 The five archetypes at a glance

| # | Archetype | Wrapper | Pattern (legacy name) | Canonical example | When to use |
|---|---|---|---|---|---|
| 1 | Composable section (LW-internal) | Dashboard | A | Quick Summary, Documents, To Do, Latest Updates | New section that genuinely depends on LegalWorkspace context (FeedTodoSyncContext, LW DataverseService) and won't ship in a non-LW host. |
| 2 | Sophisticated single-purpose direct widget | Direct | B (Code Page dispatcher) or C (AI output) | RedlineViewer, AnalysisEditor, future DocumentViewer | One sophisticated component owns the whole tab. Not composable. Mounted from Assistant or Context (or future surfaces). |
| 3 | Dual-use section (shared-lib widget + thin LW shim) | Dashboard (Direct optional) | D | **Calendar** (R3 task 115 — canonical) | New widget that could ship in **both** a Dashboard layout AND as a standalone Direct tab. Default for new widgets going forward. |
| 4 | Context-pane widget (with workspace dispatch) | (Context-surface UI; dispatches to Direct wrapper) | (Pattern C, but originating in Context) | Create Project wizard, Create Matter wizard final step | Widget body lives in the Context pane; on completion, dispatches `widget_load` to mount a result widget in the Workspace pane. R4 W-5 (FR-03) ships first such dispatch. |
| 5 | Modal-launcher | Direct (thin) | B (canonical) | create-project-wizard, find-similar-wizard | The "widget" is conceptually a launcher button — it calls `Xrm.Navigation.navigateTo` to open a wizard / Code Page / form as a modal at 80% × 80%. The tab content is small (launch / relaunch / status). |

### 1.2 Two checks before you write any code

1. **Pick the wrapper before the language.** If you can't yet explain *out loud* why your widget is Dashboard vs. Direct, re-read [`SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) §2.3. Choosing wrong is the most expensive mistake in this surface — a Direct widget that turns out to need composition has to be rewritten as a Dashboard section (and vice versa).
2. **If in doubt, build dual-use (Pattern D).** Put the actual component in a shared lib (`@spaarke/<lib>-components`) from day one and write a thin LW section shim. You can always register it as a Direct widget later without rewriting. Calendar's whole point is that the same `CalendarWorkspaceWidget` works in both wrappers because it has zero host coupling.

### 1.3 What the wrapper choice locks in

| Wrapper | What you can do | What you can't do (without changing wrappers) |
|---|---|---|
| **Dashboard** | Compose your widget with other sections in a user-customizable layout; ship it as a system layout via `system-layouts.json`; let users add it to custom layouts via WorkspaceLayoutWizard; persist layouts as `sprk_workspacelayout` rows. | Mount as a single sophisticated standalone tab without going through `LegalWorkspaceApp` (cost: full embedded LW shell loads). |
| **Direct** | Mount a single sophisticated component as its own tab; pass widget-instance-scoped data (`{ documentId, ... }`); have per-widget `allowMultiple`. | Be re-composed by users alongside other sections (would require refactor into a section + Dashboard wrapper). |

---

## 2. Archetype 1 — Composable section (Pattern A, LW-internal)

**Use when**: New section that legitimately depends on LegalWorkspace context (`FeedTodoSyncContext` cross-section badge counts, LW `DataverseService`, LW-local hooks) and you do not expect to ship it in a non-LegalWorkspace host. The 5 original sections (`getStarted`, `quickSummary`, `latestUpdates`, `todo`, `documents`) are this shape; **for NEW work prefer Archetype 3 (Pattern D) unless you have a specific reason to stay LW-internal.**

### 2.1 Files you'll edit

1. **Create** `src/solutions/LegalWorkspace/src/components/<SectionName>/<SectionName>Section.tsx` — the component.
2. **(Optional) Create** `src/solutions/LegalWorkspace/src/hooks/use<SectionName>Data.ts` — Xrm.WebApi fetch hook.
3. **Create** `src/solutions/LegalWorkspace/src/sections/<sectionName>.registration.ts` — `SectionRegistration` factory.
4. **Edit** `src/solutions/LegalWorkspace/src/sectionRegistry.ts` — add to `SECTION_REGISTRY`.
5. **Edit** `scripts/system-layouts.json` — add the system-layout entry pointing at the new `sectionId`.
6. **Run** `scripts/Deploy-SystemWorkspaceLayouts.ps1` — seeds the `sprk_workspacelayout` row.
7. **Deploy** LegalWorkspace via `code-page-deploy` (the new lib code lands in the LW bundle; SpaarkeAi consumes LW via dependency, so the standalone SpaarkeAi bundle picks it up at next SpaarkeAi deploy).

### 2.2 Section component rules

- Use the `webApi` + `userId` props passed via `SectionFactoryContext` — **never** reach for `Xrm` global directly. This keeps the section data-correct in both embedded (SpaarkeAi tab) and standalone-LW historical contexts.
- Use `scope` + `businessUnitId` from context if the section needs "my records vs. all in my BU" filtering.
- Honor `context.onNavigate`, `context.onOpenWizard`, `context.onBadgeCountChange`, `context.onRefetchReady` callbacks for cross-section UX.
- Reference: `quickSummary.registration.ts` (single content section with 6-card grid) or `documents.registration.ts` (single content section with view picker).

### 2.3 Registration file template

```ts
import * as React from "react";
import { CalendarRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { MySection } from "../components/MySection/MySection";

export const mySectionRegistration: SectionRegistration = {
  id: "my-section",                        // UNIQUE — clash = dev-mode console.error
  label: "My Section",                     // shown in WorkspaceLayoutWizard step 2
  description: "What it does",
  icon: CalendarRegular,
  category: "data",                        // overview | data | ai | productivity
  defaultHeight: "440px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "my-section",
      type: "content",
      title: "My Section",
      style: {},
      renderContent: () =>
        React.createElement(MySection, {
          webApi: context.webApi as any,
          userId: context.userId,
          scope: context.scope,
          businessUnitId: context.businessUnitId,
        }),
    };
  },
};

export default mySectionRegistration;
```

### 2.4 System-layout seed entry

```json
{
  "name": "My Section",
  "sectionId": "my-section",
  "layoutTemplateId": "single-column",
  "sortOrder": 6,
  "isDefault": false,
  "description": "Single-section layout exposing my-section. Operator request 2026-MM-DD."
}
```

- `sortOrder` is dropdown position; existing entries 0..5 are reserved. Pick the next free integer.
- `isDefault: true` on exactly **one** layout system-wide. The BFF's `GetDefaultLayoutAsync` Step 2 orders by `sortOrder ASC` if multiple are flagged.

### 2.5 Verify cold load

1. Run `Deploy-SystemWorkspaceLayouts.ps1`.
2. Restart SpaarkeAi (or `localStorage.removeItem('lw-layout-cache')` + refresh).
3. Workspaces dropdown shows the new entry; clicking it opens a tab; the tab renders `MySection`.
4. (Optional) Add the section to a multi-section custom layout via WorkspaceLayoutWizard to verify it composes.

---

## 3. Archetype 2 — Sophisticated single-purpose direct widget (Patterns B + C)

**Use when**: One React component owns the entire Workspace tab. It is not composable with other sections. It is launched from a non-dropdown mount source — typically the Assistant pane (chat result → AI output widget, R4 W-4 / FR-02) or a Code Page dispatcher.

### 3.1 Files you'll edit

1. **Create** the widget component in `@spaarke/ai-widgets` or a more specific shared lib (e.g. `@spaarke/legal-domain-widgets` for matter-specific widgets).
2. **Edit** `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` — add a `registerWorkspaceWidget(...)` call.
3. **(If needed)** the dispatching surface (`ConversationPane.tsx` for Assistant, `ContextPaneController.tsx` for Context) calls `bus.publish('workspace', { type: 'widget_load', widgetType: 'my-widget', widgetData: {...} })`.

### 3.2 Registration

```ts
registerWorkspaceWidget(
  'pdf-viewer',                          // type string — MUST be unique
  {
    displayName: 'PDF Viewer',
    category: 'document',                // overview | document | ai | wizard | other
    icon: 'DocumentPdfRegular',
    allowMultiple: true,
    defaultOrder: 150,                   // append after existing entries
  },
  () => import('./PdfViewerWidget').then((m) => ({
    default: m.PdfViewerWidget as WorkspaceWidgetComponent,
  })),
);
```

### 3.3 Component template

```tsx
import type { WorkspaceWidgetComponent } from '@spaarke/ai-widgets';

interface PdfViewerData {
  documentId: string;
  blobUrl: string;
  mimeType: string;
}

export const PdfViewerWidget: WorkspaceWidgetComponent<PdfViewerData> = ({ data }) => {
  // data is the widgetData payload from widget_load dispatch
  return (
    <div style={{ width: '100%', height: '100%' }}>
      {/* render */}
    </div>
  );
};
PdfViewerWidget.displayName = 'PdfViewerWidget';
```

### 3.4 The two flavors (B vs C)

- **Pattern B — Code Page dispatcher**: The widget body is mostly a launch button. On render (or on user click), it calls `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_mywizard', ... })`. The tab itself stays small (launch / relaunch / status display). See Archetype 5 (§6) for the modal-launcher variant.
- **Pattern C — AI output**: The widget body renders the AI tool's output payload (passed via `data`). The result of an Assistant-pane orchestration: tool runs server-side, payload comes back, widget visualizes it. Examples: `BudgetDashboard`, `ContractComparison`, `AnalysisEditor`, `RedlineViewer`.

### 3.5 No BFF changes? Don't add them.

For most direct widgets, the data comes from the dispatching surface (Assistant orchestrator payload, Context wizard result). You usually do NOT need a new BFF endpoint. If you think you do, **STOP** and read [`../../CLAUDE.md`](../../CLAUDE.md) §10 + [`../../.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing the addition.

---

## 4. Archetype 3 — Dual-use widget (Pattern D, shared-lib widget + thin LW shim) — RECOMMENDED DEFAULT

**Use when**: New widget that COULD ship as either a section inside a Dashboard layout OR a standalone Direct tab — now or in the future. This is the recommended default for new widgets going forward; Calendar (R3 task 115) proved the pattern.

The defining trait: the widget proper lives in a shared lib with **zero LegalWorkspace coupling**, and BOTH wrapper paths render the same component.

### 4.1 The Calendar canonical worked example (R3 tasks 114 + 115, polished R10–R13)

**Operator goal (Round 9, 2026-05-22)**: A Calendar system workspace that surfaces all events + tasks the user has access to (matching the standalone EventsPage Code Page), with a full Events toolbar, a horizontal month strip, and event detail opens as a modal via `Xrm.Navigation.navigateTo`. Architectural unity required: the Calendar workspace and the EventsPage must share their components.

**It shipped in two tasks**:

- **Task 114** (`53e3323e`) — created the shared library `@spaarke/events-components` (folder `src/client/shared/Spaarke.Events.Components/`) by hoisting EventsPage components: `CalendarSection`, `CalendarDrawer`, `GridSection`, `AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`, `ColumnFilterHeader`, `ColumnHeaderMenu`, `ViewSelectorDropdown`, the Xrm.WebApi FetchXML query services, view + filter hooks, `EventsPageContext` provider, type unions, date utils.
- **Task 115** (`cc83a68a`) — built `CalendarWorkspaceWidget` in the new lib, wrote the 62-line LegalWorkspace section shim, added the Dataverse-system layout entry, deployed.

**Six polish rounds R10–R13** (tasks 116, 118, 120, 121, 122) all landed in `@spaarke/events-components` — the shared lib — and propagated automatically to both the Calendar workspace AND the standalone EventsPage. The LW shim was untouched after the initial ship. **That's the dual-use payoff.**

### 4.2 Step-by-step (pattern that you copy for new dual-use widgets)

#### Step 1 — Decide the shared-lib home

For Calendar it was a NEW lib (`@spaarke/events-components`) because there was a parallel Code Page consumer (EventsPage). For a new widget without an existing parallel consumer, you can put it in `@spaarke/ai-widgets` (general-purpose AI widget home) or `@spaarke/ui-components` (general UI). Avoid putting it in `@spaarke/legal-workspace` — that's the dashboard engine; widgets should not be coupled to it.

Confirm the closure is portable: only `Xrm.WebApi` + shared services + `authenticatedFetch` (from `@spaarke/auth`). No `FeedTodoSyncContext`. No LW `DataverseService`. No LW-local hooks.

#### Step 2 — Build the widget proper

```
src/client/shared/Spaarke.<Lib>.Components/src/widgets/<Name>WorkspaceWidget/
├── <Name>WorkspaceWidget.tsx       — main component
├── <Name>WorkspaceWidget.css       — Griffel via makeStyles, ADR-021 tokens only
└── (any internal subcomponents)
```

Compose existing shared components from the same lib + Fluent v9. Treat the widget as if it has no host: all data via `Xrm.WebApi` or `authenticatedFetch` from `@spaarke/auth`. No host-specific props (no `webApi` passed in, no `userId` injected) — read them yourself if needed.

Calendar's widget is ~1100 LOC. It composes `CalendarSection layout="horizontal"`, a Fluent v9 Toolbar with 9 CRUD buttons, `ViewSelectorDropdown` defaulted to "Active Events", and `GridSection` auto-bound to `EventsPageContext.filters`. It uses `Xrm.Navigation.navigateTo({pageType:'entitylist', entityName:'sprk_event'}, {target:2, width:80%, height:80%})` for the Open click target — NOT `Xrm.App.sidePanes` (operator decision: matches task 111 Documents Expand UX).

#### Step 3 — Add the LegalWorkspace section shim

`src/solutions/LegalWorkspace/src/sections/<sectionName>.registration.ts` (Calendar's version is 62 lines):

```ts
import * as React from "react";
import { CalendarLtr24Regular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { CalendarWorkspaceWidget } from "@spaarke/events-components";

export const calendarRegistration: SectionRegistration = {
  id: "calendar",
  label: "Calendar",
  description: "All events + tasks you have access to",
  icon: CalendarLtr24Regular,
  category: "data",
  defaultHeight: "720px",
  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "calendar",
      type: "content",
      title: "Calendar",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(CalendarWorkspaceWidget),
    };
  },
};

export default calendarRegistration;
```

**Note the discriminator**: `factory` does NOT forward `SectionFactoryContext` props to the widget. The widget is self-contained. This is the defining trait of Pattern D versus Pattern A.

#### Step 4 — Register in `sectionRegistry.ts`

```ts
import { calendarRegistration } from "./sections/calendar.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,        // dual-use entry
] as const;
```

The dev-mode guard at the bottom of `sectionRegistry.ts` warns on duplicate IDs.

#### Step 5 — Add the system layout seed entry

`scripts/system-layouts.json`:

```json
{
  "name": "Calendar",
  "sectionId": "calendar",
  "layoutTemplateId": "single-column",
  "sortOrder": 5,
  "isDefault": false
}
```

Run `pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <url>`.

#### Step 6 — (Optional) Register as a Direct widget too

If you want the SAME widget to ALSO be mountable as a standalone tab dispatched from the Assistant or Context surface, add a `WorkspaceWidgetRegistry` registration:

```ts
// src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts
registerWorkspaceWidget(
  'calendar',                          // distinct type — NOT 'workspace'
  {
    displayName: 'Calendar',
    category: 'data',
    icon: 'CalendarLtr24Regular',
    allowMultiple: false,
    defaultOrder: 200,
  },
  () => import('@spaarke/events-components').then((m) => ({
    default: m.CalendarWorkspaceWidget as WorkspaceWidgetComponent,
  })),
);
```

Now `widget_load { widgetType: 'calendar' }` dispatched from Assistant or Context mounts the widget directly (no `LegalWorkspaceApp` wrapper, no layout fetch). Same component, two wrappers. Calendar today is registered only on the section side; the direct registration is a future option.

#### Step 7 — Deploy

```pwsh
# Shared lib lands in BOTH consumers' bundles via dependency.
# LegalWorkspace consumes @spaarke/<lib>-components; SpaarkeAi consumes LW.

# Deploy LegalWorkspace:
pac auth select --index N      # the dev environment
# (build LW + push)
# Use: code-page-deploy skill, target LegalWorkspace

# Deploy SpaarkeAi so the new lib lands in the SpaarkeAi bundle:
pwsh scripts/Deploy-SpaarkeAi.ps1 -EnvironmentUrl <url>
# Bundle delta typically <40 KB gzip for a single widget.

# Seed the system layout:
pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <url>
```

> **R4 NOTE** (forward-looking, per W-6): The standalone LegalWorkspace Code Page (`sprk_corporateworkspace`) is being retired in R4. After W-6 ships, the LegalWorkspace package remains as a library consumed by SpaarkeAi, but it is no longer self-deployed. Pattern D widgets continue to work because they live in a shared lib (`@spaarke/events-components`, etc.) — the shim just re-exports.

### 4.3 What Calendar did NOT need to change

- **NO** changes to SpaarkeAi source code (zero, across all 9 tasks 114–122) — the existing `workspace` widget type covers every new Dashboard layout.
- **NO** changes to BFF — `WorkspaceLayoutService.GetLayoutsAsync` already includes all `sprk_issystem=true` records. Zero BFF endpoints, services, DI, NuGet, or publish-size delta.
- **NO** changes to `@spaarke/ai-widgets` — `WorkspaceLayoutWidget` is unchanged.
- **NO** new auth wiring — `useAuth` + `authenticatedFetch` + `Xrm.WebApi` already in place.
- **NO** new ADRs — Pattern D extends the existing Dashboard wrapper pipeline.

### 4.4 What Calendar still has imperfect (honest answer)

- `CalendarSidePane` (separate web resource) carries its own legacy copy of `CalendarSection` divergent from `@spaarke/events-components`. R4 B-6 / B-7 / B-8 work targets this reconciliation.
- Bulk-action handlers are partially duplicated between the standalone EventsPage's `App.tsx` and `CalendarWorkspaceWidget`. Pending extraction to a shared `useEventsBulkActions` hook.
- `CalendarDrawer.eventDates: string[]` vs `IEventDateInfo[]` API drift — drawer still accepts strings.
- `Xrm.WebApi` vs BFF decision criteria still undocumented at `docs/standards/` level — Calendar reinforced the unwritten norm.

---

## 5. Archetype 4 — Context-pane widget (with workspace dispatch)

**Use when**: The widget's primary UX lives in the **Context pane** (right pane) — a wizard, a card, a gallery picker, a Get Started shortcut. On completion (or on demand), it dispatches `widget_load` to mount a result widget in the **Workspace pane**.

The Context pane is not a "wrapper" in the model-doc sense (model doc §1, §2). It is a separate **surface** that hosts pane-local UI **and** acts as a mount source (model doc §3.3). The result widget that lands in the Workspace pane is itself either a Dashboard wrapper (rare — only if the wizard produces a `sprk_workspacelayout`) or a Direct wrapper (typical — a single-purpose result widget).

### 5.1 The two halves

1. **The Context-pane UI itself** — lives in `src/solutions/SpaarkeAi/src/components/context/` (or a Context-controlled subtree). React components, possibly multi-step. Examples shipped today: Get Started cards, Create Project wizard (R4 W-5 target), Create Matter wizard.
2. **The dispatch** — on completion (or user click), publish a `widget_load` event on the `workspace` channel:

```ts
import { bus } from '@spaarke/ai-widgets';

bus.publish('workspace', {
  type: 'widget_load',
  widgetType: 'project-summary',      // a Direct widget OR 'workspace' (Dashboard)
  widgetData: { projectId, ... },
});
```

The Workspace pane's `widget_load` subscriber resolves the widget type via `WorkspaceWidgetRegistry`, mounts the resulting React tree as a new tab, and acks (model doc §3.3).

### 5.2 R4 W-5 (FR-03) — first end-to-end Context → Workspace wiring

The R4 W-5 task wires the Create Project wizard's final step to dispatch `widget_load` on the `workspace` channel. Result: completing the wizard mounts a project-summary widget in the Workspace pane as a new tab. The result widget can be either:

- A new Direct widget (`'project-summary'`) registered in `WorkspaceWidgetRegistry` — typical case.
- A constructed Dashboard layout — only if the wizard builds a `sprk_workspacelayout` and dispatches `widgetType: 'workspace'`.

Code references:
- Dispatcher: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` + the per-wizard final-step handler.
- Wrapper: whatever widget type is dispatched (see §3 for Direct or §4 for Dashboard-via-layout).

### 5.3 Channels to use

- **`context` channel** — for events that stay in the Context pane (gallery selection, wizard step navigation).
- **`workspace` channel** — to mount a widget in the Workspace pane. ALWAYS use this for cross-pane dispatch.
- **`conversation` channel** — only if your Context widget also needs to inject a message into the Assistant chat (rare).

See [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) §3.3 + ADR-025 (NEW in R4) for typed channel contracts. Do **not** invent new channels.

---

## 6. Archetype 5 — Modal-launcher widget (Pattern B canonical)

**Use when**: The "widget" is conceptually a launcher button. Its tab content is small — a launch button, status text, a relaunch affordance, maybe a thumbnail of last-result. The actual work happens inside a modal opened via `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_mywizard', ... })` at 80% × 80%.

Examples shipped today: `create-project-wizard`, `find-similar-wizard`, `email-compose`, `meeting-schedule`. These are all Direct widgets registered in `WorkspaceWidgetRegistry`; the body of each is a thin component that opens the corresponding Code Page wizard as a modal.

### 6.1 Why this is its own archetype

A modal-launcher is a Direct widget *implementation pattern*, but it's distinct enough to flag separately because:

1. The widget body has very little real UX — it's a launch chrome around a modal.
2. The actual feature lives in a separate Code Page web resource, not in the widget itself.
3. Decisions about modal vs side-pane vs in-tab are operator-bound (Calendar's Open chose modal navigateTo; some wizards choose side-pane; document tabs render in-tab).

### 6.2 Files you'll edit

1. **(One-time)** the wizard / form Code Page itself exists as a web resource (`sprk_mywizard`) deployed separately.
2. **Create** `src/client/shared/Spaarke.AI.Widgets/src/widgets/<MyLauncher>/<MyLauncherWidget>.tsx` — the launcher body.
3. **Edit** `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` — register the widget type.

### 6.3 Launcher body template

```tsx
import * as React from 'react';
import { Button } from '@fluentui/react-components';
import { OpenRegular } from '@fluentui/react-icons';
import type { WorkspaceWidgetComponent } from '@spaarke/ai-widgets';

interface MyWizardLauncherData {
  matterId?: string;
}

export const MyWizardLauncherWidget: WorkspaceWidgetComponent<MyWizardLauncherData> = ({ data }) => {
  const launch = React.useCallback(() => {
    // @ts-ignore — Xrm provided by host
    Xrm.Navigation.navigateTo(
      {
        pageType: 'webresource',
        webresourceName: 'sprk_mywizard',
        data: data ? JSON.stringify(data) : undefined,
      },
      { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } },
    );
  }, [data]);

  return (
    <div style={{ padding: 24 }}>
      <p>Open My Wizard to start the flow.</p>
      <Button icon={<OpenRegular />} onClick={launch}>
        Open My Wizard
      </Button>
    </div>
  );
};

MyWizardLauncherWidget.displayName = 'MyWizardLauncherWidget';
```

### 6.4 Registration

Same shape as §3.2. `category: 'wizard'` is appropriate for modal launchers.

### 6.5 Operator decision points

- **Side-pane vs modal navigateTo**: Calendar (task 115) explicitly rejected `Xrm.App.sidePanes` in favor of `Xrm.Navigation.navigateTo` at 80% × 80%. Operator preference; if your widget should live in a side-pane instead, document it in the design doc.
- **target: 2 means modal**. `target: 1` is in-line navigation; almost never what you want for a launcher widget.

---

## 7. Cheat sheet — file edits per archetype

| Archetype | Files created | Files edited | Scripts run | Deploys |
|---|---|---|---|---|
| 1. Composable section (Pattern A) | Section + hook + registration | `sectionRegistry.ts`, `system-layouts.json` | `Deploy-SystemWorkspaceLayouts.ps1` | LegalWorkspace (+ SpaarkeAi next deploy) |
| 2. Direct widget (Pattern B/C) | Widget component | `register-workspace-widgets.ts` (+ dispatching surface) | — | SpaarkeAi (or `@spaarke/ai-widgets` consumer) |
| 3. Dual-use (Pattern D) | Widget in shared lib + LW shim | `sectionRegistry.ts`, `system-layouts.json` (+ optional `register-workspace-widgets.ts`) | `Deploy-SystemWorkspaceLayouts.ps1` | LegalWorkspace + SpaarkeAi |
| 4. Context-pane widget | Context UI components | `ContextPaneController.tsx` (or equivalent) + the result widget per #2 or #3 | — | SpaarkeAi |
| 5. Modal-launcher | Launcher body | `register-workspace-widgets.ts` | — | SpaarkeAi (+ the modal's Code Page if new) |

---

## 7.1 Sizing & layout — the constraint chain (NEW 2026-06-09, post iter-2 round 11)

If your widget renders ANY component whose intrinsic width can exceed its
container — DataGrid, wide tables, side-by-side cards, image galleries,
horizontal scrollers — the workspace pane will be **forced to grow to fit
that content** unless every flex/grid ancestor in the chain explicitly opts
out of the default `min-width: auto` behavior.

This was the root cause of an 11-round debug cycle on the embedded
DataverseEntityViewWidget (Documents/Matters/Projects/Invoices/WorkAssignments
direct widgets). The single-row §9 of [`DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](DATAGRID-CODE-PAGE-HOST-CONTRACT.md)
that flagged "⚠️ Partial — workspace shell owns FluentProvider" understated
the host obligation enormously. The full requirement is now §9.1 of that
doc; below is the short version every workspace-widget author MUST follow.

### 7.1.1 The four-step contract

1. **Host Code Page's `index.html` MUST have the box-sizing reset.** If
   missing, every grid cell renders `+24px` wider than declared. Audit:
   ```bash
   grep -l "box-sizing" src/solutions/<YourHost>/index.html
   ```
   Add the §2 block from [`DATAGRID-CODE-PAGE-HOST-CONTRACT.md`](DATAGRID-CODE-PAGE-HOST-CONTRACT.md#2-the-indexhtml-css-contract-non-negotiable)
   if it's missing. As of 2026-06-09, 17 of 23 host Code Pages were missing
   this — assume nothing.

2. **Your widget's root container MUST set `min-width: 0` and `width: 100%`.**
   ```ts
   const useStyles = makeStyles({
     root: {
       flex: 1,
       minHeight: 0,
       minWidth: 0,   // ← required; default 'auto' = max-content
       width: '100%',
       display: 'flex',
       flexDirection: 'column',
       overflow: 'hidden',
     },
   });
   ```
   Reference: [`DataverseEntityViewWidget.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx)
   styles.root.

3. **Widget MUST measure its own outer width with ResizeObserver and apply
   it as an explicit pixel cap** on an inner wrapper, so the descendant
   content has an explicit containing block (mimics the
   `body { overflow: hidden }` boundary that a standalone Code Page gets
   for free).
   ```ts
   const rootRef = React.useRef<HTMLDivElement | null>(null);
   const [width, setWidth] = React.useState(0);
   React.useLayoutEffect(() => {
     if (rootRef.current) setWidth(rootRef.current.clientWidth);
   }, []);
   React.useEffect(() => {
     const el = rootRef.current;
     if (!el || typeof ResizeObserver === 'undefined') return;
     const ro = new ResizeObserver(es => es.forEach(e => setWidth(e.contentRect.width)));
     ro.observe(el);
     return () => ro.disconnect();
   }, []);
   return (
     <div ref={rootRef} className={styles.root}>
       <div style={{
         width: width > 0 ? `${width}px` : '100%',
         maxWidth: width > 0 ? `${width}px` : '100%',
         flex: 1,
         minHeight: 0,
         minWidth: 0,
         display: 'flex',
         flexDirection: 'column',
         overflow: 'hidden',
       }}>
         {/* your wide content here */}
       </div>
     </div>
   );
   ```
   Reference: [`DataverseEntityViewWidget.tsx`](../../src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx)
   (round 9 pattern).

4. **If your widget mounts `<DataGrid>`, you get the rest for free.** The
   shared `<DataGrid>` already includes:
   - Griffel `!important` override for Fluent v9's hardcoded
     `min-width: fit-content` (round 8).
   - 2-pass `columnSizingOptions` math with minWidth-floor-aware redistribution
     (round 10).
   - `visibleColumns.length * 24` padding reserve in the `available`
     calculation as defense against missed §7.1.1.1 resets (round 11).
   - `min-width: 0` on root, innerCard, gridScroll (rounds 6-7).

   If you build a non-DataGrid wide component, you'll need to replicate
   patterns 8/10/11 yourself or use the `<DataGrid>`-equivalent shared
   primitive (TBD as new wide components emerge).

### 7.1.2 Diagnostic when something looks wrong

Switch DevTools Console to the Code Page's iframe and run the script at
[`DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §9.1.6](DATAGRID-CODE-PAGE-HOST-CONTRACT.md#916-diagnostic-script).
The output tells you:
- `col[N] rendered = declared + 24` → §7.1.1.1 box-sizing reset missing.
- Section card `cw` differs from row track width → §7.1.1.2 min-width:0
  chain broken — walk up the parent list until you find the inflated layer.
- Table `sw > container cw` but rendered ≈ declared → DataGrid column math
  regression; file a bug, don't try to fix at the widget level.

### 7.1.3 Anti-patterns specific to sizing

- ❌ **Setting `width: 100%` without `min-width: 0`.** The flex item still
  refuses to shrink below its content's intrinsic width. The combination
  matters.
- ❌ **Using CSS class with `min-width: 0` (no `!important`) to override
  Fluent v9 inline styles.** Inline beats class in CSS specificity. Use
  the `!important` Griffel pattern from DataGrid's `gridTableOverride`
  style if you must override a Fluent inline style.
- ❌ **Measuring `gridScroll` clientWidth and assuming it equals available
  cell width.** The cell-padding reserve is real; either include the reset
  or subtract `cellCount * 24` from your math.
- ❌ **Relying on the workspace pane being narrow enough that overshoot is
  invisible.** The operator can resize panes; the widget must constrain
  itself regardless of current pane width.

---

## 8. Anti-patterns

- **❌ Do not invent a third wrapper.** The two wrappers (Dashboard + Direct) are intentionally retained per OC-R4-06 (model doc §2.3). If you find yourself wanting a third — "but my widget is sort-of-composable" or "but my widget needs Dataverse persistence too" — re-read the model doc. Dual-use (§4 here) is almost always the right answer.
- **❌ Do not duplicate section code in both LegalWorkspace and SpaarkeAi.** Either it's a Pattern A LW-internal section (lives in LW only, consumed via embedded `LegalWorkspaceApp`) or it's a Pattern D shared-lib widget (lives in a shared lib, consumed by BOTH). Never two divergent copies. The R3 `useWorkspaceLayouts` duplication is being remediated in R4 C-3 precisely because dual-source-of-truth bit us.
- **❌ Do not add Direct widgets that need composition.** If users will want to combine your widget with other content in one tab, you need a Dashboard wrapper (probably Pattern D). Adding a Direct widget and then trying to retro-fit composition requires rewriting it as a section + Dashboard wrapper.
- **❌ Do not create new Code Page entry points for LegalWorkspace functionality.** Per W-6 / OC-R4-05, the standalone LegalWorkspace Code Page is retired. Use the SpaarkeAi `sprk_spaarkeai` Code Page (the only deployed entry point) + the embedded LW engine. New LW deploys are not the path forward.
- **❌ Do not reach into `Xrm` globals from a shared-lib widget without an abstraction.** Pattern D widgets are portable because they only consume `@spaarke/auth` + `Xrm.WebApi` (via shared services in their own lib). If you have to invent direct `Xrm` access in the widget, encapsulate it in a service so the widget stays host-agnostic.
- **❌ Do not invent new PaneEventBus channels.** Use `workspace`, `context`, `conversation`, `safety` (the four ADR-025 channels). Channel proliferation is a coupling smell.
- **❌ Do not add BFF endpoints "just in case" for a new widget.** Almost every shipped widget reuses existing endpoints (or `Xrm.WebApi` directly). If you genuinely need a new endpoint, follow [`../../CLAUDE.md`](../../CLAUDE.md) §10 BFF Hygiene + [`../../.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md). Placement Justification is **mandatory** before adding code.
- **❌ Do not skip the decision tree (§1) "to save time".** Choosing the wrong wrapper is the most expensive mistake on this surface. Five minutes with §1 saves hours of rework.

---

## 9. Common pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Forgot to add to `SECTION_REGISTRY` | Tab opens but section doesn't render | Dev-mode console warns about unknown section ID — check `sectionRegistry.ts` |
| Duplicate section ID | Dev-mode `console.error` at load | Pick a unique ID |
| `sortOrder` collision | Layout appears in unexpected dropdown position | All existing `sortOrder` values (the 5 Dataverse-system layouts use 0..5) — pick the next free integer |
| Two layouts marked `isDefault: true` | Only one appears as default; the other behaves as non-default | Only ONE Dataverse-system layout can be the global default |
| Section needs auth-bearing BFF fetch | Uses `webApi` instead — works in MDA, fails in a future non-Xrm host | Use `authenticatedFetch` from `@spaarke/auth`; pass `bffBaseUrl` from `SectionFactoryContext` |
| Forgot to seed the layout | Workspaces dropdown doesn't show the new entry | Run `Deploy-SystemWorkspaceLayouts.ps1`; verify with the API query in §2.5 of the model doc |
| Section behaves differently in embedded vs. standalone | Section uses LW-internal context (`FeedTodoSyncContext`) that doesn't exist if its provider isn't mounted | Only depend on `SectionFactoryContext`; if you need more context, prefer Pattern D (shared-lib widget) which sidesteps the trap entirely |
| **Timezone-asymmetric date keys** (Calendar task 120 fix) | Event-day highlight doesn't appear on the expected calendar cell for users in positive UTC offsets | Any component that derives date-only keys from a `Date` must use LOCAL components (`getFullYear/Month/Date`) — never `date.toISOString().split('T')[0]`. The PRODUCING side and the CONSUMING side must use the same key derivation. |
| **Filter-state conflicts with passive indicators** (Calendar task 122 fix) | Passive visual indicators (event-day highlight, today, in-range) get clobbered by active filter-state indicators | Be explicit about which states are mutually exclusive vs. which can coexist. Passive indicators should remain visible whenever possible; user-action states win. |
| **Field-priority chain for date derivation** (Calendar task 121 fix) | Date used for highlighting / sorting doesn't match operator expectation | Use the SAME field-priority chain the user expects from the UI. For Events: `sprk_duedate → createdon` (Calendar operator decision — skipped `sprk_startdate` because events without a due date should anchor to creation date). Document the rationale. |
| **localStorage key drift between sessions** (Calendar task 116 fix) | Per-component collapse state doesn't persist | Use the `spaarke:<surface>:<feature>` pattern, matching existing keys (`spaarke:workspace:pinned-list`, `spaarke:panes:collapsed`). See `SPAARKEAI-WORKSPACE-ARCHITECTURE.md` §7. |
| **Pattern A vs. Pattern D not justified upfront** | Section ships as LW-internal (Pattern A) but later needs reuse in a non-LW host | Decide via §1 decision tree before writing code. New widgets default to Pattern D unless they genuinely depend on LW-internal context. The 5 original sections are Pattern A because they predate Pattern D — not because they're the model for new work. |

---

## 10. Verification walk-through (validate the decision tree)

Three hypothetical widgets, each walked through §1 to confirm the tree reaches the right archetype.

### 10.1 Hypothetical "Risk Dashboard" (composable, multi-section)

- **Q1**: Yes — combines Quick Summary + Latest Updates + To Do filtered to risk-tagged items. Multiple sections, user-customizable, layout is a `sprk_workspacelayout` row.
- **Q2**: Yes — reuses existing LW-internal sections (Quick Summary, Latest Updates, To Do).
- **Archetype**: **1 (Pattern A)** — composable section in a Dashboard wrapper layout. Add a system-layout entry to `system-layouts.json` pointing at the existing section IDs; configure section-config JSON with the risk filter criteria. No new widget code.
- ✅ Tree reaches the right answer.

### 10.2 Hypothetical "PDF Viewer" (single-purpose, AI-launched)

- **Q1**: No — single sophisticated viewer component, owns the whole tab, NOT composable.
- **Q3**: Sophisticated single-purpose React component (owns its data via the dispatching surface, owns its chrome).
- **Archetype**: **2 (Direct widget, Pattern C — AI output style)** — register `'pdf-viewer'` in `WorkspaceWidgetRegistry`, build `PdfViewerWidget`, dispatch `widget_load { widgetType: 'pdf-viewer', widgetData: { ... } }` from `ConversationPane.tsx`. This is the R4 W-4 (FR-02) demo scenario candidate.
- ✅ Tree reaches the right answer.

### 10.3 Hypothetical "Create Matter Wizard Result Card" (Context-pane → Workspace)

- **Surface**: Wizard body lives in the Context pane; on final step, mounts a result widget in the Workspace pane.
- **Q1**: For the Context wizard itself, N/A — it's not in the Workspace pane. For the result widget, no (single matter-summary card, not composable).
- **Q3**: Widget originates in the Context pane and dispatches a separate `widget_load` to the Workspace pane on completion.
- **Archetype**: **4 (Context-pane widget)** — wizard body in `src/solutions/SpaarkeAi/src/components/context/`; on completion, dispatch `bus.publish('workspace', { type: 'widget_load', widgetType: 'matter-summary', widgetData: { matterId } })`. The result widget itself is an Archetype 2 Direct widget.
- ✅ Tree reaches the right answer.

---

## 11. Related docs

- [`../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`](../architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md) — **the canonical model this guide implements** (three surfaces, two wrappers, four mount sources, dual-use pattern, LegalWorkspace-as-dashboard-engine).
- [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline.
- [`../architecture/SPAARKEAI-COMPONENT-MODEL.md`](../architecture/SPAARKEAI-COMPONENT-MODEL.md) — `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/events-components`, `@spaarke/legal-workspace`, `@spaarke/auth` inventory + PaneEventBus contract.
- [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest reuse assessment. §2A is the canonical Calendar Pattern D reference.
- [`SHARED-UI-COMPONENTS-GUIDE.md`](./SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption guide.
- [`PCF-DEPLOYMENT-GUIDE.md`](./PCF-DEPLOYMENT-GUIDE.md) — Code Page deploy reference (`Deploy-SpaarkeAi.ps1`, `Deploy-LegalWorkspace.ps1` retirement context per W-6).
- [`../../CLAUDE.md`](../../CLAUDE.md) §10 — BFF Hygiene; binding before any BFF additions.
- [`../../.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF additions pre-merge checklist.
- ADR-012 (shared component libraries), ADR-021 (Fluent v9 tokens), ADR-022 (React 19 Code Pages), ADR-025 (PaneEventBus — NEW in R4), ADR-026 (stage lifecycle — NEW in R4), ADR-028 (Spaarke Auth v2).
