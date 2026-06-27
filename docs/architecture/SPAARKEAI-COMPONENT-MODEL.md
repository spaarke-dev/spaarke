# SpaarkeAi Component Model

> **Purpose**: Inventory of the shared libraries (`@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`, `@spaarke/events-components`, `@spaarke/daily-briefing-components`) and solution-local components that compose the SpaarkeAi three-pane shell. Includes the PaneEventBus contract.
>
> **Last reviewed**: 2026-06-18 (project `spaarke-daily-update-service-r2` task 063 / SC14). Added `@spaarke/daily-briefing-components` (§7) as the third Pattern D dual-use shared lib alongside `@spaarke/events-components` and `@spaarke/smart-todo-components`. Previous review: 2026-05-22 (Task 123, Round 13).

---

## 1. Layering principles (ADR-012)

- **Shared libraries** in `src/client/shared/` are framework-agnostic-ish (React 19 + Fluent v9 only) and consumable by any Code Page or PCF host. They MUST NOT import from any `src/solutions/*` directory.
- **Solution-local components** live in `src/solutions/<page>/src/components/` and CAN import shared libs. They MUST NOT import from another solution's `src/solutions/*` directory — exception: `@spaarke/legal-workspace` is published as a workspace package and so is importable like any shared lib.
- **PCF-safe vs Code Page only**: anything in `@spaarke/ai-widgets` is React 19, NOT PCF-safe. `@spaarke/ui-components` is largely PCF-safe (verified by individual component docblocks).

---

## 2. `@spaarke/ui-components` inventory

Path: `src/client/shared/Spaarke.UI.Components/src/`. Barrel: `index.ts` → `* from './components'`. 41 component folders today:

### 2.1 Layout primitives

| Component | Path | Notes |
|---|---|---|
| `ThreePaneLayout` | `components/ThreePaneLayout/` | Three-pane shell with draggable splitters + per-pane collapse (Task 094 + 100) + custom collapsed-strip icons (Task 096) + percentage-of-viewport initial widths via `defaultLeftWidthFrac` / `defaultRightWidthFrac` (Task 117) + all-panes-collapsed empty-state overlay with "Welcome back" + Open button (Task 119). |
| `PaneHeader` | `components/PaneHeader/` | Shared pane title bar with icon, title, optional `rightSlot`, optional `onCollapse` (Task 010 + 094) |
| `PageChrome` | `components/PageChrome/` | Standalone Code Page chrome (Page header + ThemeToggle); used by LegalWorkspace standalone |
| `PanelSplitter` | `components/PanelSplitter/` | Generic horizontal/vertical splitter |
| `SidePane` | `components/SidePane/` | Slide-in side pane (used by `ManageWorkspacesPane`) |

#### 2.1.1 `ThreePaneLayout` API additions since task 113

Task 117 + 119 added the following to keep the layout primitive context-agnostic while still supporting SpaarkeAi's percentage-based defaults + recovery affordance:

- **Props** (`ThreePaneLayoutProps`):
  - `defaultLeftWidthFrac?: number` — e.g. `0.25` for 25% of `window.innerWidth` on cold mount when no sessionStorage value is stored. Falls back to `defaultLeftWidthPx` for SSR / non-browser. (Task 117.)
  - `defaultRightWidthFrac?: number` — same semantics for the right pane. (Task 117.)
- **Hook result** (`UseThreePaneLayoutResult`):
  - `resetToFracDefaults: () => void` — recomputes left/right widths from `frac × window.innerWidth` clamped to per-pane minimums, AND persists the new pixel values to sessionStorage so they OVERWRITE any user-dragged values. Invoked by the all-panes-collapsed Open button. Does NOT touch visibility — caller (the layout itself in the empty-state overlay) chains the toggle calls. (Task 119.)
- **Rendering**:
  - When `!isLeftVisible && !isCenterVisible && !isRightVisible`, the layout renders an empty-state overlay as a sibling of the three 48px collapsed strips: `flex: 1 1 auto` column with `EmojiSmileSlight24Regular`, "Welcome back" text, and an `autoFocus` primary `Button` labeled "Open" wrapped in `<div role="region" aria-label="All panes are collapsed">`. (Task 119.)

### 2.2 WorkspaceShell rendering pipeline

| Component | Path | Notes |
|---|---|---|
| `WorkspaceShell` | `components/WorkspaceShell/WorkspaceShell.tsx` | Renders `WorkspaceConfig` (rows + sections) |
| `SectionPanel` | `components/WorkspaceShell/SectionPanel.tsx` | Individual section card (header + body) |
| `ActionCard` + `ActionCardRow` | `components/WorkspaceShell/` | Hoisted from LegalWorkspace in Task 012 |
| `MetricCard` + `MetricCardRow` | `components/WorkspaceShell/` | Used by `quick-summary` section |
| `layoutTemplates.ts` | `components/WorkspaceShell/layoutTemplates.ts` | Template id → row config (e.g. `single-column`, `3-row-mixed`) |
| `buildDynamicWorkspaceConfig.ts` | `components/WorkspaceShell/` | Layout JSON → `WorkspaceConfig` (consumes `SectionRegistry`) |
| `wizardLaunchers.ts` | `components/WorkspaceShell/` | Hoisted `navigateTo` wizard launchers (Task 085) |
| `sections/dailyBriefing/` | `components/WorkspaceShell/sections/dailyBriefing/` | Hoisted Daily Briefing factory + hook + section component (Task 069, 086) |
| `types.ts` | `components/WorkspaceShell/types.ts` | `SectionRegistration`, `SectionFactoryContext`, `SectionConfig` discriminated union |

### 2.3 Chat + wizards

| Component | Path | Notes |
|---|---|---|
| `SprkChat` | `components/SprkChat/` | Reusable chat UI (used by ConversationPane); `useChatFileAttachment` hook for FR-07 attachments |
| `Wizard` | `components/Wizard/` | Generic multi-step wizard framework |
| `CreateMatterWizard`, `CreateProjectWizard`, `CreateEventWizard`, `CreateTodoWizard`, `CreateRecordWizard`, `CreateWorkAssignmentWizard`, `DocumentEmailWizard`, `SummarizeFilesWizard`, `FindSimilar` | `components/<Name>/` | Domain wizards |
| `PlaybookLibraryShell` | `components/PlaybookLibraryShell/` | Playbook gallery (Analysis Builder shell) |

### 2.4 Toolbars, dialogs, fields

| Component | Path | Notes |
|---|---|---|
| `Toolbar`, `DocumentToolbar`, `InlineAiToolbar` | `components/<Name>/` | Toolbars |
| `RecordCardShell`, `DocumentCard` (via DocumentsTab), `MiniGraph` | `components/<Name>/` | Card primitives |
| `FilePreview`, `FileUpload`, `RichTextEditor`, `DiffCompareView` | `components/<Name>/` | Editor + viewer primitives |
| `ChoiceDialog`, `SendEmailDialog`, `FindSimilarDialog` | `components/<Name>/` | Modal dialogs |
| `LookupField`, `EventDueDateCard`, `RelationshipCountCard`, `AiFieldTag`, `AiProgressStepper`, `AiSummaryPopover`, `AssociateToStep`, `SlashCommandMenu`, `ThemeToggle`, `EmailStep`, `TodoDetail`, `Playbook` | `components/<Name>/` | Misc form / AI / annotation primitives |
| `DatasetGrid` | `components/DatasetGrid/` | Used by PCF + Code Pages (PCF-safe) |

### 2.5 Cross-cutting

| Module | Path | Notes |
|---|---|---|
| `theme` | `theme/` | `resolveCodePageTheme`, `setupCodePageThemeListener`, `useTheme`, `syncThemeFromDataverse`, `persistThemeToDataverse` |
| `icons` | `icons/` | Central icon registry (Fluent v9 only, per ADR-025) |
| `hooks` | `hooks/` | Shared hooks (theme, dataverse helpers) |
| `services` | `services/` | DataverseService, telemetry, etc. |

---

## 3. `@spaarke/ai-widgets` inventory

Path: `src/client/shared/Spaarke.AI.Widgets/src/`. React 19, NOT PCF-safe.

### 3.1 Public exports (`index.ts`)

```
events:
  PaneEventBus, PaneEventBusProvider, usePaneEvent, useDispatchPaneEvent
  PaneChannel, WorkspacePaneEvent, ContextPaneEvent, ConversationPaneEvent, SafetyPaneEvent

providers:
  AiSessionProvider, useAiSession, AiSessionContextValue

registries:
  registerWorkspaceWidget, resolveWorkspaceWidget, getWorkspaceWidgetMetadata, getAllWorkspaceWidgetTypes
  registerContextWidget, resolveContextWidget, hasContextWidget, getAllContextWidgetTypes

stage:
  determineStage, shouldReset, SessionState, PaneStage

widgets:
  GenericTextWidget (fallback)
  RedlineViewerWidget, CreateMatterWizardWidget, DocumentUploadWizardWidget, SearchSelectWizardWidget,
  EmailComposeWidget, MeetingScheduleWidget, CreateProjectWizardWidget, FindSimilarWizardWidget,
  WorkspaceLayoutWidget (Round 4 — the embed)
  GetStartedCardsWidget (Context pane), PlaybookGalleryWidget (Context pane), various ContextWidget impls
```

### 3.2 Workspace widget registrations (`widgets/workspace/register-workspace-widgets.ts`)

| # | Widget type | Component | allowMultiple | Purpose |
|---|---|---|---|---|
| 1 | `BudgetDashboard` | (wrapped R1) | false | Legacy AI output (budget) |
| 2 | `SearchResults` | (wrapped R1) | true | AI search results |
| 3 | `AnalysisEditor` | (wrapped R1) | true | AI-generated analysis |
| 4 | `ContractComparison` | (wrapped R1) | true | Side-by-side contract diff |
| 5 | `StatusSummary` | (wrapped R1) | false | Status overview |
| 6 | `Recommendation` | (wrapped R1) | false | Ranked recommendations |
| 7 | `ActionPlan` | (wrapped R1) | false | Action plan checklist |
| 8 | `redline-viewer` | RedlineViewerWidget | true | Document comparison |
| 9 | `create-matter-wizard` | CreateMatterWizardWidget | false | Embedded matter wizard |
| 10 | `document-upload-wizard` | DocumentUploadWizardWidget | true | Embedded upload flow |
| 11 | `search-select-wizard` | SearchSelectWizardWidget | true | Embedded record picker |
| 12 | `email-compose` | EmailComposeWidget | true | Analysis Builder dispatcher (email) |
| 13 | `meeting-schedule` | MeetingScheduleWidget | true | Analysis Builder dispatcher (meeting) |
| 14 | `create-project-wizard` | CreateProjectWizardWidget | true | Code Page dispatcher (existing sprk_createprojectwizard) |
| 15 | `find-similar-wizard` | FindSimilarWizardWidget | true | Code Page dispatcher (existing sprk_findsimilar) |
| 16 | **`workspace`** | **WorkspaceLayoutWidget** | true | **Embedded LegalWorkspaceApp — ONE registration covers every Dataverse-defined workspace** |

---

## 4. `@spaarke/auth` inventory

Path: `src/client/shared/Spaarke.Auth/src/`. Per ADR-028 (Spaarke Auth v2):

| Export | Purpose |
|---|---|
| `initAuth(options)` | Initialize a single MSAL `PublicClientApplication`. Idempotent. |
| `getAuthProvider()` | Get the `SpaarkeAuthProvider` singleton (use sparingly — prefer hooks) |
| `useAuth()` | React hook — returns `{ isAuthenticated, getAccessToken, authenticatedFetch, tenantId, ... }` |
| `authenticatedFetch(url, init)` | Function-style fetch with auto Bearer + 401 retry |
| `buildBffApiUrl(baseUrl, path)` | URL normalization helper — REQUIRED for ALL BFF URL building |
| `resolveRuntimeConfig()` | Resolve `IRuntimeConfig` from Xrm env vars OR localStorage cache |
| `clearRuntimeConfigCache()` | Test helper |
| `resolveTenantIdSync()` | Synchronous tenant ID accessor for click handlers |
| `BrowserMsalStrategy`, `OfficeNaaStrategy` | Pluggable auth strategies (browser MSAL vs Office Add-in NAA) |
| `AuthError`, `ApiError` | Typed errors |

**Invariants** (INV-1..INV-8 from ADR-028):
- INV-1: Single MSAL instance per page.
- INV-2: Cache in localStorage (survives tab close).
- INV-3: NEVER snapshot token strings into React state.
- INV-4: Top-frame uses redirect; in-MDA uses popup; in-iframe MUST NOT use redirect.
- INV-5: Handle `handleRedirectPromise` on every load.
- INV-6: `authenticatedFetch` retries 401 once with exponential backoff.
- INV-7: All BFF URLs through `buildBffApiUrl`.
- INV-8: `VERSION` log on init surfaces bundling-reality drift.

---

## 5. `@spaarke/legal-workspace` (the `LegalWorkspaceApp` package)

LegalWorkspace is its own solution under `src/solutions/LegalWorkspace/` AND exposes its app shell as a workspace package (`@spaarke/legal-workspace`) so SpaarkeAi can embed it. Public exports include:

- `LegalWorkspaceApp` — the root component (props: `version`, `allocatedWidth`, `allocatedHeight`, `webApi`, `userId`, `initialWorkspaceId?`, `embedded?`)
- `setLegalWorkspaceRuntimeConfig(config)` — initializes LegalWorkspace's SEPARATE runtime-config singleton (see §4 of the audit)

The package is consumed by `WorkspaceLayoutWidget` (in `@spaarke/ai-widgets`) which is the SINGLE generic workspace-widget entry point.

---

## 6. `@spaarke/events-components` (Events + Tasks surface — task 114)

Path: `src/client/shared/Spaarke.Events.Components/src/`. Hoisted from the standalone EventsPage solution in **task 114 (Round 8, 2026-05-22)** so the same Events components could be reused inside the SpaarkeAi Calendar workspace widget (task 115). Pure data + UI; no BFF dependency; auth via `Xrm.WebApi` (ADR-028); Fluent v9 only (ADR-021); React 19 (ADR-022). Public exports:

### 6.1 Components

| Component | Path | Notes |
|---|---|---|
| `CalendarSection` | `components/CalendarSection/` | Month-grid calendar with `horizontal` and `vertical` layouts, From/To range, click-to-filter, event-day highlight, controlled `selectedDate` prop (task 116/118/120/122). |
| `CalendarDrawer` | `components/CalendarSection/` | Side drawer wrapper around `CalendarSection` used by the standalone EventsPage. |
| `GridSection` | `components/GridSection/` | Records grid with FetchXML / OData fetch paths + sort + filter + `onRecordsLoaded` callback (task 120) for event-date derivation. |
| `AssignedToFilter` | `components/AssignedToFilter/` | User-picker filter for the grid. |
| `RecordTypeFilter` | `components/RecordTypeFilter/` | Event-type (option-set) filter. |
| `StatusFilter` + `getStatusOptions` + `getActionableStatuses` | `components/StatusFilter/` | Status (option-set + state) filter. |
| `ColumnFilterHeader` | `components/ColumnFilterHeader/` | Column-header inline filter affordance. |
| `ColumnHeaderMenu` | `components/ColumnHeaderMenu/` | Column-header context menu (sort + filter + show/hide). |
| `ViewSelectorDropdown` + `useViewSelection` + `EVENT_VIEWS` + `DEFAULT_VIEW_ID` | `components/ViewSelectorDropdown/` | Saved-view selector + active-view hook. |

### 6.2 Widgets

| Widget | Path | Notes |
|---|---|---|
| `CalendarWorkspaceWidget` | `widgets/CalendarWorkspaceWidget/` | The embedded Calendar widget. ~1100 LOC. Composes: date-range filter row (Dropdown + From/To inputs + Clear button + collapse caret), single `<CalendarSection layout="horizontal">` with responsive month count (1→5 by viewport breakpoints) and external ◀ ▶ arrow navigation, full Events toolbar (`<Toolbar>` with 9 CRUD buttons + flex-spacer + Open icon — task 120), `<ViewSelectorDropdown>` row, `<GridSection>` with `onRecordsLoaded={handleRecordsLoaded}` driving the event-day highlight (task 120). Side-pane behavior overridden: `Xrm.Navigation.navigateTo` modal at 80%×80% instead of `Xrm.App.sidePanes`. |

### 6.3 Shared services + hooks + context + types + utils

- `services/` — FetchXML query builder + Dataverse helpers (Xrm.WebApi only).
- `hooks/` — view + filter hooks.
- `context/` — `EventsPageContext` + `EventsPageProvider` (filters, calendarFilter, selectedDate, eventDates, callbacks like `onOpenEvent`).
- `types/` — `IEventRecord`, `IEventDateInfo`, filter type unions.
- `utils/` — date helpers (local-date `toIsoDateString` symmetric across CalendarSection + widget — task 120).

### 6.4 Consumers (architectural unity goal)

| Consumer | Purpose |
|---|---|
| `src/solutions/EventsPage/` | Standalone Code Page `sprk_eventspage` — full-page Events surface. |
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget` consumed by `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` | The Calendar system workspace inside SpaarkeAi (task 115). 62-line LW shim delegates entirely to the shared widget. |
| `src/solutions/CalendarSidePane/` (separate web resource) | Has its own legacy copy of `CalendarSection` divergent from the shared lib — flagged as a pre-existing follow-up across tasks 114–122. |

The presence of `@spaarke/events-components` is the first instance of "one components inventory, two consumers" since the original ADR-012 layering — see [componentization audit §9](./SPAARKEAI-COMPONENTIZATION-AUDIT.md).

### 6.5 `CalendarSection` controlled-mode props (tasks 116 + 118)

`CalendarSection` was extended in task 116/118 to support controlled-mode embedding inside a widget that owns the calendar's interaction state:

| Prop | Type | Default | Purpose |
|---|---|---|---|
| `viewDate?` | `Date` | uncontrolled internal | When provided, the calendar treats this as the anchor month. The widget owns "what month am I showing." |
| `monthsToShow?` | `number` | 3 | How many months to render. The widget recomputes this from viewport breakpoints via ResizeObserver (1 → 5 months at 480/768/1024/1280px). |
| `layout?` | `"vertical" \| "horizontal"` | `vertical` | Vertical = stacked (EventsPage CalendarDrawer); horizontal = strip (Calendar widget). In horizontal mode the internal header + selectionInfo + footer are suppressed (task 118) — the widget owns those affordances on its own filter / toolbar rows. |
| `selectedDate?` | `Date \| null` | `null` | Controlled-mode highlighted day. When `onSelectDate` is provided, parent owns selection. (Task 118.) |
| `onSelectDate?` | `(date: Date \| null) => void` | undefined | Day-click handler. Single-click emits via `onSelectDate`; Shift-click still hits `onFilterChange` for range selection. |

The widget wires these together: `selectedDate` widget state + `onDaySelect` callback that updates local state for visual highlight, dispatches `EventsPageContext.setCalendarFilter({type:'range', start:iso, end:iso, dateFields:[dateField]})` (single-day range — re-uses the existing range plumbing in `GridSection`), and clears on null toggle-off. A filter-divergence `useEffect` watches `filters.calendarFilter` so that if the active filter no longer matches local `selectedDate` (because user changed From/To or another component dispatched), `selectedDate` resets to `null` — the day highlight cannot lie about what's actually filtering.

This is the model for future "controlled component" patterns in the shared lib.

---

## 7. `@spaarke/daily-briefing-components` (Daily Briefing surface — R2 task 010)

Path: `src/client/shared/Spaarke.DailyBriefing.Components/src/`. Hoisted from the standalone DailyBriefing solution in **project `spaarke-daily-update-service-r2` (R2 task 010, 2026-06-18)** so the same Daily Briefing UI can be reused inside the SpaarkeAi Daily Briefing workspace widget (R2 task 018). Pure data + UI; no LegalWorkspace coupling; auth via `Xrm.WebApi` + `@spaarke/auth` `authenticatedFetch`; Fluent v9 only (ADR-021); React 19 (ADR-022). Third instance of the Pattern D "one components inventory, two consumers" model — follows the precedent set by `@spaarke/events-components` (Calendar) and `@spaarke/smart-todo-components` (Smart Todo).

### 7.1 Public exports

Barrel: `src/index.ts` re-exports the same six subpaths declared in `package.json` `exports`:

| Subpath | Contents |
|---|---|
| `./components` | `DailyBriefingApp` (top-level composer), `TldrSection`, `ActivityNotesSection`, `ChannelHeading` + `channelIcons`, `NarrativeBullet`, `PreferencesDropdown`, `CaughtUpFooter`, `DigestHeader`, `EmptyState` (per FR-04). |
| `./widgets` | Reserved for a future `DailyBriefingWorkspaceWidget` matching the Calendar widget shape; currently empty. The LegalWorkspace section shim mounts `DailyBriefingApp` directly. |
| `./hooks` | `useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions` (per FR-06 — the three split hooks that replaced the monolithic `useNotificationData` in R2), plus `useBriefingNarration` and `useInlineTodoCreate` (per FR-05). |
| `./services` | Notification + narration + preferences service modules (Xrm.WebApi + `authenticatedFetch`). |
| `./types` | `NotificationContext`, `NarrateRequest`, briefing payload types, preference types. |
| `./utils` | Pure helpers (date, channel-icon resolution). |

### 7.2 The `loadNotificationContext` factory option seam (FR-01)

`DailyBriefingApp` accepts an optional `loadNotificationContext?: () => Promise<NarrateRequest | null>` prop that allows the host to inject its own notification-context resolver. The seam decouples the shared component from any specific Xrm context source:

- **Standalone code page** (`src/solutions/DailyBriefing/`) omits the prop. Behavior matches the empty-payload contract — BFF `/narrate` returns empty bullets; UI renders the empty state.
- **SpaarkeAi workspace widget** (LegalWorkspace `daily-briefing.registration.ts` shim, R2 task 002 wires in after task 018) injects a resolver that pulls the active notification context from the SpaarkeAi shell; bullets populate accordingly.

This is the equivalent of the controlled-mode pattern in Calendar (§6.5): same shared component, host owns the data-source binding.

### 7.3 Consumers (architectural unity goal)

| Consumer | Purpose |
|---|---|
| `src/solutions/DailyBriefing/` | Standalone Code Page `sprk_dailyupdate` — thin host shell (R2 task 017 shrink). Mounts `<DailyBriefingApp />` with no `loadNotificationContext`. |
| `src/solutions/LegalWorkspace/src/sections/daily-briefing.registration.ts` | LegalWorkspace section shim consumed by SpaarkeAi via the embedded `LegalWorkspaceApp` pipeline. R2 task 018 rewires it to mount `<DailyBriefingApp loadNotificationContext={...} />` from the new package. Replaces the prior `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` shim (Task 069 / 086) which is retired in R2. |

The presence of `@spaarke/daily-briefing-components` makes Pattern D the codified default for new dual-use widgets — see [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §1.1 and §4.

---

## 8. Solution-local components — SpaarkeAi

Path: `src/solutions/SpaarkeAi/src/`. These are NOT shared. Reusable from other Code Pages only via copy-paste or future hoisting.

| Directory | Purpose | Reuse candidate? |
|---|---|---|
| `components/shell/ThreePaneShell.tsx` | Root shell — composes the 3 panes + provider tree. SpaarkeAi-specific because it knows about the 3 specific panes. | Partially — the provider stack pattern is generic; the pane composition is SpaarkeAi-specific |
| `components/conversation/ConversationPane.tsx` | Left pane — chat + history. | Possibly — depends on `ShellStage` context |
| `components/workspace/WorkspacePane.tsx` | Center pane — tab manager + auto-install + auto-open. | Tightly coupled to SpaarkeAi shell context |
| `components/workspace/WorkspaceTabManager.ts` | Tab state class. | YES — could be hoisted to `@spaarke/ai-widgets` if another host needs tabbed widgets |
| `components/workspace/WorkspaceTabManagerComponent.tsx` | Tab strip UI. | YES — candidate for hoisting alongside the manager class |
| `components/workspace/WorkspacePaneMenu.tsx` | Workspaces dropdown + pin + manage. | NO — too tightly coupled to the SpaarkeAi pipeline (dispatches `widget_load` to the local pane's bus) |
| `components/workspace/ManageWorkspacesPane.tsx` | Manage workspaces side pane (Task 093 / 104). | NO — local UI |
| `components/context/ContextPaneController.tsx` | Right pane — Get Started / playbook gallery / Semantic Search. | Possibly |
| `hooks/useWorkspaceLayouts.ts` | **DUPLICATE of LegalWorkspace's hook** — see audit §1 | Should be consolidated |
| `hooks/useSessionRestore.ts` | NFR-09 restore | Possibly |
| `hooks/usePaneCollapse.ts` | Per-pane collapse persistence | YES — generic |
| `hooks/useContextTool.ts` | Active Context tool persistence | Partially generic |
| `services/pinnedWorkspaces.ts` | Pinned-list localStorage contract | Partially generic |
| `services/contextToolPin.ts` | Context-tool localStorage contract | Same |
| `services/authInit.ts` | Calls `initAuth` with SpaarkeAi config | Boilerplate per Code Page |
| `services/runtimeConfig.ts` | SpaarkeAi's runtime-config singleton | Boilerplate per Code Page |
| `telemetry/errorTelemetry.ts` | Error telemetry helpers (Task 013) | YES — candidate for `@spaarke/ui-components/services` |

---

## 9. Solution-local components — LegalWorkspace

Path: `src/solutions/LegalWorkspace/src/`. The "monolithic" workspace surface that SpaarkeAi embeds.

| Directory | Purpose | Coupling notes |
|---|---|---|
| `LegalWorkspaceApp.tsx` | Root component; embedded-mode branch added Task 087 | Exposed via `@spaarke/legal-workspace` package |
| `components/Shell/WorkspaceGrid.tsx` | Renders the active layout via `WorkspaceShell` | Calls LegalWorkspace's own `useWorkspaceLayouts` |
| `components/Shell/PageHeader.tsx` | Standalone-mode header with workspace dropdown | Skipped in embedded mode |
| `contexts/FeedTodoSyncContext.tsx` | Shared flag state between ActivityFeed + SmartToDo blocks | LegalWorkspace-internal; would have to be reproduced if section factories were hoisted |
| `sections/*.registration.ts` | 6 section factory registrations | **All depend on local components in `src/solutions/LegalWorkspace/src/components/`** — see audit §2 |
| `sections/dailyBriefing/` | Daily Briefing shim re-exporting hoisted factory | See `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` for the real factory |
| `components/QuickSummary/*` | 6-card metric grid (Wave 3a added sprk_communication + sprk_invoices) | Self-contained, uses Xrm.WebApi directly |
| `components/DocumentsTab/*` | Documents grid (Wave 3b added gridMode 2x10) | Uses LegalWorkspace's DataverseService |
| `components/SmartToDo/*` | Smart To Do List kanban | Uses LegalWorkspace's DataverseService + FeedTodoSyncContext |
| `components/ActivityFeed/*` | Latest Updates feed | Uses LegalWorkspace's DataverseService + FeedTodoSyncContext |
| `components/GetStarted/*` | Get Started action cards | Hoisted wizard launchers from `wizardLaunchers.ts` |
| `hooks/useWorkspaceLayouts.ts` | LegalWorkspace's layouts hook — calls BFF, has SYSTEM_DEFAULT_LAYOUT fallback | See audit §1 (dual hook gap) |
| `hooks/useDailyBriefing.ts` | LegalWorkspace's Daily Briefing hook (TTL cache) | Re-exports hoisted version |
| `hooks/useQuickSummaryCounts.ts` | 6-card count queries via `webApi.retrieveMultipleRecords` | Xrm.WebApi-only |
| `services/authInit.ts` | LegalWorkspace's own `authenticatedFetch` instance | Separate from SpaarkeAi's; same `@spaarke/auth` plumbing |
| `config/runtimeConfig.ts` | LegalWorkspace's runtime-config singleton | See main.tsx §2.1 — initialized by SpaarkeAi too |
| `workspace/buildDynamicWorkspaceConfig.ts` | (Pre-hoist) layout JSON → config | Hoist target — partially completed (Task 067) |
| `workspace/layoutCache.ts` | sessionStorage layout cache | LegalWorkspace-local |

---

## 10. PaneEventBus contract

### 10.1 Bus instance lifetime

A SINGLE `PaneEventBus` instance per shell mount, provided via `PaneEventBusProvider` (lives at the top of `ThreePaneShell`). Every component below it that calls `usePaneEvent` or `useDispatchPaneEvent` shares the same bus.

### 10.2 Channel summary

| Channel | Type union | Notes |
|---|---|---|
| `workspace` | `WorkspacePaneEvent` (11 event types) | Primary cross-pane signal channel |
| `context` | `ContextPaneEvent` (3 event types) | Document context + citation highlights |
| `conversation` | `ConversationPaneEvent` (5 event types) | Chat / playbook events |
| `safety` | `SafetyPaneEvent` (2 event types) | Groundedness annotations |

### 10.3 Subscribe / dispatch hooks

```ts
// Subscribe (registered as a useEffect internally)
usePaneEvent('workspace', (event) => {
  if (event.type === 'widget_load') { ... }
});

// Dispatch
const dispatch = useDispatchPaneEvent();
dispatch('workspace', { type: 'widget_load', widgetType: 'workspace', widgetData: {...} });
```

### 10.4 Important dispatch patterns

1. **Subscription-race** — dispatching from a `useEffect` declared BEFORE the subscriber's `useEffect` lands on a zero-subscriber channel. Fix: defer to a macrotask via `setTimeout(..., 0)`. See `WorkspacePane.tsx:340-540` block comments.
2. **`widget_load` dispatch contract**:
   - Server-initiated (or menu-initiated): `widgetType + widgetData + displayName`, NO `tabId`.
   - WorkspacePane acks after resolution: `widgetType + tabId + tabCount`.
   - ShellStageManager only advances stage on the ACK (has `tabId`), not the initial dispatch.
3. **Multi-subscriber semantics**: every subscriber receives every event. Use Set iteration semantics — subscribers added during dispatch are NOT called for the in-flight event.

---

## 11. Cross-references

- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end pipeline diagram + storage contract
- [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest reuse audit; calls out the dual `useWorkspaceLayouts` and section-factory coupling
- [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — adding a new system workspace widget
- [`../guides/SHARED-UI-COMPONENTS-GUIDE.md`](../guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption guide
