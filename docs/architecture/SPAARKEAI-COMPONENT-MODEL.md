# SpaarkeAi Component Model

> **Purpose**: Inventory of the shared libraries (`@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`) and solution-local components that compose the SpaarkeAi three-pane shell. Includes the PaneEventBus contract.
>
> **Last reviewed**: 2026-05-22 (Task 113, Round 8). Periodic review required.

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
| `ThreePaneLayout` | `components/ThreePaneLayout/` | Three-pane shell with draggable splitters + per-pane collapse (Task 094 + 100) + custom collapsed-strip icons (Task 096) |
| `PaneHeader` | `components/PaneHeader/` | Shared pane title bar with icon, title, optional `rightSlot`, optional `onCollapse` (Task 010 + 094) |
| `PageChrome` | `components/PageChrome/` | Standalone Code Page chrome (Page header + ThemeToggle); used by LegalWorkspace standalone |
| `PanelSplitter` | `components/PanelSplitter/` | Generic horizontal/vertical splitter |
| `SidePane` | `components/SidePane/` | Slide-in side pane (used by `ManageWorkspacesPane`) |

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

## 6. Solution-local components — SpaarkeAi

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

## 7. Solution-local components — LegalWorkspace

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

## 8. PaneEventBus contract

### 8.1 Bus instance lifetime

A SINGLE `PaneEventBus` instance per shell mount, provided via `PaneEventBusProvider` (lives at the top of `ThreePaneShell`). Every component below it that calls `usePaneEvent` or `useDispatchPaneEvent` shares the same bus.

### 8.2 Channel summary

| Channel | Type union | Notes |
|---|---|---|
| `workspace` | `WorkspacePaneEvent` (11 event types) | Primary cross-pane signal channel |
| `context` | `ContextPaneEvent` (3 event types) | Document context + citation highlights |
| `conversation` | `ConversationPaneEvent` (5 event types) | Chat / playbook events |
| `safety` | `SafetyPaneEvent` (2 event types) | Groundedness annotations |

### 8.3 Subscribe / dispatch hooks

```ts
// Subscribe (registered as a useEffect internally)
usePaneEvent('workspace', (event) => {
  if (event.type === 'widget_load') { ... }
});

// Dispatch
const dispatch = useDispatchPaneEvent();
dispatch('workspace', { type: 'widget_load', widgetType: 'workspace', widgetData: {...} });
```

### 8.4 Important dispatch patterns

1. **Subscription-race** — dispatching from a `useEffect` declared BEFORE the subscriber's `useEffect` lands on a zero-subscriber channel. Fix: defer to a macrotask via `setTimeout(..., 0)`. See `WorkspacePane.tsx:340-540` block comments.
2. **`widget_load` dispatch contract**:
   - Server-initiated (or menu-initiated): `widgetType + widgetData + displayName`, NO `tabId`.
   - WorkspacePane acks after resolution: `widgetType + tabId + tabCount`.
   - ShellStageManager only advances stage on the ACK (has `tabId`), not the initial dispatch.
3. **Multi-subscriber semantics**: every subscriber receives every event. Use Set iteration semantics — subscribers added during dispatch are NOT called for the in-flight event.

---

## 9. Cross-references

- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — end-to-end pipeline diagram + storage contract
- [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest reuse audit; calls out the dual `useWorkspaceLayouts` and section-factory coupling
- [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — adding a new system workspace widget
- [`../guides/SHARED-UI-COMPONENTS-GUIDE.md`](../guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption guide
