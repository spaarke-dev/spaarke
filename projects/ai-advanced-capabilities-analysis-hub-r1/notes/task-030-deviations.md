# Task 030 — Analysis hub widget: deviations + scoping decisions

> Documented per task-execute Step 8 ("Document any deviation"). No `<escalation>` trigger
> fired — neither escalation condition in the task POML applies (a `sprk_gridconfiguration`
> row CAN be created later without bypassing the DataGrid framework, and the coming-soon
> cards do not require server gating to be correct per spec). These are scoping/
> interpretation decisions, mirroring the precedent `task-040-deviations.md` set.

## 1. Grid composed via `DataverseEntityViewWidget`, not a raw `<DataGrid configId=… />`

The task brief's step 2 literally says "render `<DataGrid configId="…" />`". `AnalysisHubWidget`
instead composes the existing `DataverseEntityViewWidget` (this same package), which itself
renders `<DataGrid configId=… />` plus the Xrm frame-walk, `XrmDataverseClient` construction, and
membership-resolver wiring that widget already owns. Per CLAUDE.md §11 (reuse-first) and the task's
own ADR-012 constraint ("do NOT hand-roll a bespoke table or fork the component"), duplicating that
Xrm/client plumbing inside a second widget would itself be a fork of `DataverseEntityViewWidget`'s
established "any future system widget wraps this" pattern (see that widget's own header doc). The
net effect is identical — the hub renders `<DataGrid configId=… />` — through the ALREADY-canonical
composition path instead of a second copy of the wiring.

## 2. View-by-type dropdown is the DataGrid framework's own `ViewSelector` — no bespoke control

The task constraint requires the view dropdown to use "the DataGrid framework's own view mechanism
(sprk_gridconfiguration views / ViewSelector)". `DataGrid.tsx` already renders `<ViewSelector>`
internally, populated from sibling saved queries for the config's entity (`activeSavedQueryId` /
`availableViewsAllowlist`). No separate dropdown component was built — the "view by type" affordance
is satisfied entirely by seeding the hub's `sprk_gridconfiguration` row with FOUR sibling saved
queries (All / Agreement Review / Legal Research / Patent Application), each filtering
`sprk_worktype`. See `notes/hub-grid-config-deployment.md` for the operator recipe. This is not
buildable/testable until that Dataverse row exists (task 071 deploy) — see item 4 below.

## 3. `sprk_gridconfiguration` GUID — placeholder constant, seeding handed to task 071

No hub-specific `sprk_gridconfiguration` row exists yet (only the 6 pre-existing rows baked into
`ENTITY_VIEW_CONFIG_IDS` for Documents/Matters/Projects/Invoices/Work-Assignments/Communications —
none of which is `sprk_analysis`). Per the task's own constraint ("reference the config-id via a
constant + document that the config record must be seeded"), `AnalysisHubWidget.tsx` declares
`ANALYSIS_HUB_GRID_CONFIG_ID` as a placeholder GUID (`00000000-0000-0000-0000-000000000000`) with a
deployment-requirement doc comment, mirroring the existing `ENTITY_VIEW_CONFIG_IDS` /
`entity-view-widget-deployment.md` precedent from `ai-spaarke-ai-workspace-UI-r1`. Full operator
steps (row creation, the 4 sibling saved queries, FetchXML filters) are in
`notes/hub-grid-config-deployment.md`. Until seeded, `DataGrid`'s own invalid-config guard renders a
clear empty state — no crash — matching the existing 6-widget precedent.

## 4. Agreement Review card launch — dispatch wiring only, NOT the deep service wiring

Per the task's own `<notes>`: "Card actionable-click wiring to the create flow is provided by the
wizard (task 040) and entry routing (task 050); this task ships the hub surface + cards." The
Agreement Review card's `onClick` dispatches the `widget_load` PaneEventBus event
(`widgetType: 'create-analysis-wizard'`, `widgetData: { workTypeValue: 100000000, workTypeLabel:
'Agreement Review' }`) — the literal "launches THIS" wiring named in the brief, using the SAME
mechanism `CreateAnalysisWizardWidget` itself uses to open `document-viewer`. It does NOT supply
`dataService` / `authenticatedFetch` / `navigationService` / `searchUsers` — those are live function
objects that task 050 (entry routing) is explicitly scoped to resolve and thread through (the hub's
own `AiSessionContext` access only exposes `authenticatedFetch` + `bffBaseUrl`, not a Dataverse
`IDataService`/`INavigationService` adapter — building that adapter is a bigger lift matching task
050's "entry routing" framing, not a hub-widget concern). Until task 050 lands, the opened
`create-analysis-wizard` tab shows its own existing "Connecting to workspace services…" placeholder
(a graceful, already-implemented `CreateAnalysisWizardWidget` render branch, not a crash).

## 5. `register-workspace-widgets.ts` + barrel exports — both new registrations added by this task

Per the task-040/030 parallel-execution handoff (`task-040-deviations.md` §6: "task 030 ... owns
register-workspace-widgets.ts"), this task adds BOTH registrations that file was missing:
`'analysis-hub'` (this widget) and `'create-analysis-wizard'` (task 040's widget, which deliberately
did not self-register). Both are also added to the package barrel (`index.ts`) — task 040 exported
`CreateAnalysisWizardWidget` from its own file but never wired it into the barrel; this task closes
that gap so task 050 (entry routing) and any test/consumer can import both by name from
`@spaarke/ai-widgets`.

## 6. `workTypeValue` restated locally — same ADR-012 precedent as task 040

`AnalysisHubWidget` lives in the shared `Spaarke.AI.Widgets` package and cannot import
`SprkAnalysisWorkType` from `src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts` (solution-owned,
depends on this shared package, not the reverse). The Agreement Review raw Choice value
(`100000000`) is restated locally as `WORK_TYPE_AGREEMENT_REVIEW`, mirroring
`CreateAnalysisWizardWidget`'s own `DEFAULT_WORK_TYPE_VALUE` restatement (see
`task-040-deviations.md` §5).
