# Spaarke UX Management

> **Last Updated**: March 19, 2026
>
> **Purpose**: Documents the Spaarke UX architecture including the three-layer UI model, service abstractions, shell selection, Code Page wrappers, and workspace layout.

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified (accurate)
>
> Verified against `src/client/shared/Spaarke.UI.Components/src/components/` — `WizardShell`, `CreateRecordWizard`, `PlaybookLibraryShell`, `SidePaneShell`, `WorkspaceShell`, and all listed `Create*Wizard` components exist. Service adapters (`createXrmDataService`, `createBffDataService`, `createXrmUploadService`, `createBffUploadService`) verified in `src/client/shared/Spaarke.UI.Components/src/utils/adapters/`. `parseDataParams.ts` verified in `src/utils/`. Code Page wrappers verified in `src/solutions/Create*Wizard/`.

---

## UI Architecture: Three-Layer Model

All Spaarke UI dialogs and wizards follow a three-layer architecture that separates shared components from deployment wrappers and consumers.

```
Layer 1: @spaarke/ui-components (shared library)
├── types/        → IDataService, IUploadService, INavigationService
├── utils/        → resolveCodePageTheme(), parseDataParams(), adapters/
└── components/   → WizardShell, CreateRecordWizard, PlaybookLibraryShell,
                    CreateMatterWizard, CreateProjectWizard, CreateEventWizard,
                    CreateTodoWizard, CreateWorkAssignmentWizard,
                    SummarizeFilesWizard, FindSimilarDialog

Layer 2: Code Page wrappers (~30-50 LOC each)
└── src/solutions/{WizardName}/   → sprk_{wizardname} (web resource)

Layer 3: Consumers
├── Corporate Workspace (navigateTo calls)
├── Entity form command bars (ribbon → sprk_wizard_commands.js → navigateTo)
└── Power Pages SPA (BFF adapters, direct component import)
```

### Why Three Layers?

| Layer | Responsibility | Changes When... |
|-------|---------------|-----------------|
| **Layer 1** (shared library) | All domain logic, UI rendering, service contracts | Business rules change, new fields, new step logic |
| **Layer 2** (Code Page wrapper) | Mount React, resolve theme, create adapters, pass props | Never (boilerplate is stable) |
| **Layer 3** (consumer) | Trigger `navigateTo` with entity context | New entry points, new button placements |

A bug fix in a wizard component is made once in Layer 1 and automatically available to all consumers without modification.

---

## Service Abstraction (IDataService Pattern)

Shared components accept service interfaces instead of platform-specific APIs. This makes components portable across Dataverse Code Pages, Power Pages SPAs, and test harnesses.

### Why Abstraction?

Direct use of `Xrm.WebApi` or `window.Xrm` in shared components would make them untestable and non-portable. The adapter pattern allows the same component to run in Dataverse (using Xrm adapters), Power Pages (using BFF adapters), and unit tests (using mock adapters) without modification.

### Service Interfaces

| Interface | Purpose |
|-----------|---------|
| `IDataService` | CRUD operations against Dataverse entities |
| `IUploadService` | File upload to SharePoint Embedded via BFF |
| `INavigationService` | Navigate to records, open dialogs, close dialogs |

### Adapter Selection Guide

| Context | Data Adapter | Upload Adapter | Navigation Adapter |
|---------|-------------|----------------|--------------------|
| **Dataverse Code Page** | `createXrmDataService()` | `createXrmUploadService(bffBaseUrl)` | `createXrmNavigationService()` |
| **Power Pages SPA** | `createBffDataService(authFetch, bffBaseUrl)` | `createBffUploadService(authFetch, bffBaseUrl)` | `createBffNavigationService(navigate)` |
| **Unit Tests** | `createMockDataService()` | `createMockUploadService()` | `createMockNavigationService()` |

---

## Shell Selection Decision Tree

| Need | Use | Why |
|------|-----|-----|
| Multi-step wizard with file upload + entity creation | **CreateRecordWizard** | Standardized 4-step flow (Add Files, Details, Next Steps, follow-on steps) |
| Multi-step wizard with custom step sequence | **WizardShell** directly | Full control over steps (e.g., CreateWorkAssignment has non-standard flow) |
| Playbook browse + execute | **PlaybookLibraryShell** | Tab UI with browse/custom scope tabs, intent mode for pre-selected playbooks |
| Single-step dialog | **Fluent UI Dialog** | No wizard overhead needed |
| Slide-in side panel | **SidePaneShell** | Used by CalendarSidePane, EventDetailSidePane, TodoDetailSidePane |

---

## WizardShell Design Principles

WizardShell is a domain-free, reusable wizard dialog shell. It owns layout (sidebar stepper + content area + footer), navigation state, and the finish/success flow.

- **Zero domain imports** — all domain content is injected via callbacks
- **Consumer-owned domain state** — WizardShell tracks only navigation state; uploaded files, form values, API results are managed by the consumer's reducer
- **Dynamic steps** — steps can be added/removed at runtime via imperative `IWizardShellHandle` (e.g., a "Next Steps" selection screen injects follow-on steps)
- **`embedded` prop** — when `true`, renders as full-page layout without the Fluent UI `<Dialog>` overlay. Use when hosted inside a Dataverse dialog that provides its own chrome

---

## Code Page Wrappers

Every Code Page wrapper follows the same pattern (~30-50 lines):

1. Resolve theme via `resolveCodePageTheme()` (4-level cascade: localStorage, URL flags, navbar DOM, system preference)
2. Parse URL parameters via `parseDataParams()` (handles both Xrm `data` envelope and raw query params)
3. Create **Xrm service adapters** (IDataService, IUploadService, INavigationService)
4. Mount shared wizard component with `embedded={true}` (so WizardShell skips its own Dialog overlay)
5. Listen for theme changes via `setupCodePageThemeListener()`

Key constraint: wrappers use React 18 `createRoot` (bundled). The shared component must NOT import any Xrm or PCF-specific types — all platform interaction goes through the service adapters.

---

## How to Add a New Wizard

1. Create the wizard component in `@spaarke/ui-components` — accept `IDataService`, `IUploadService`, `INavigationService` as props; pass `embedded` through to the shell
2. Create a Code Page wrapper in `src/solutions/{Name}/` following the standard pattern
3. Deploy via code-page-deploy skill
4. Wire `navigateTo` from consumers (workspace or ribbon command handler in `sprk_wizard_commands.js`)
5. If steps are dynamic, use `handle.addDynamicStep(config, canonicalOrder)` inside `renderContent`

---

## Dark Mode Theme Persistence

### Decision

Power Platform MDA dark mode is controlled via URL flag: `flags=themeOption%3Ddarkmode`. There is no API to set this programmatically on app load. The solution uses a **ribbon EnableRule side effect**: an enable rule fires on every entity grid load, checks the stored preference in localStorage against the current URL, and redirects if there is a mismatch.

### Why Ribbon EnableRule?

There is no app-level `OnLoad` API that fires reliably on every page navigation in UCI. The ribbon EnableRule pattern fires on entity grid loads, which covers the majority of navigation scenarios. Known limitation: dashboards and non-entity pages don't trigger the ribbon, so theme may not enforce until navigating to an entity.

### Theme Cascade for Code Pages

Code Pages do NOT use the ribbon-based mechanism. They resolve theme via `resolveCodePageTheme()` from `@spaarke/ui-components`:
1. localStorage (`spaarke-theme` key) — user's explicit preference
2. URL flags (`flags` param with `themeOption=dark|light`)
3. Navbar DOM — reads Dataverse navbar background-color luminance
4. Default: light (OS `prefers-color-scheme` is intentionally NOT consulted — ADR-021)

### Adding Theme Menu to a New Entity

Every main entity needs the Theme menu ribbon to ensure theme persistence works when users navigate to that entity. Add an entry to `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` and update `Solution.xml` with a `<RootComponent>` entry. The ribbon structure adds a FlyoutAnchor with Auto/Light/Dark buttons, commands calling `Spaarke.Theme.setTheme()`, and an EnableRule calling `Spaarke.Theme.isEnabled()`.

---

## WorkspaceShell Architecture

WorkspaceShell is a **full-page declarative layout container** for workspace dashboards. Unlike WizardShell (imperative dialog container), WorkspaceShell consumes a `WorkspaceConfig` object and renders a responsive multi-row grid of section panels.

### Why Declarative Config (Not Imperative Props)?

Workspace layouts are user-configurable and persisted in Dataverse (`sprk_sectionsjson`). A declarative config object (describing rows, columns, and section IDs) can be serialized/deserialized and edited without code changes. Dialog shells by contrast have ephemeral state (open/close) that doesn't need persistence.

### Key Design

- **Section Registry Pattern**: Sections self-register via `SectionRegistration` objects (id, label, factory function). `buildDynamicWorkspaceConfig()` reconciles the persisted `LayoutJson` with the registry at render time
- **Layout switching**: WorkspaceHeader provides a dropdown to switch between system and user layouts. The `SYSTEM_DEFAULT_LAYOUT_JSON` is used as fallback
- **Row overflow**: handled gracefully — unknown section IDs are warned and skipped; row overflow when more sections exist than column slots is managed by `buildDynamicWorkspaceConfig()`
- **Coexistence**: WorkspaceShell and dialog shells coexist in `@spaarke/ui-components` but serve different UX patterns. Workspace sections may themselves open dialog shells (e.g., "Add" button opens `sprk_createtodowizard`)

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Theme doesn't persist to new entity | Entity missing ribbon customization | Add theme menu ribbon |
| Dark mode flickers on load | EnableRule not triggering | Verify EnableRule references `Spaarke.Theme.isEnabled` |
| Code Page opens in wrong theme | localStorage not set | Check `resolveCodePageTheme()` cascade; verify `spaarke-theme` in localStorage |
| Wizard buttons missing on entity form | Ribbon solution not deployed | Import ribbon solution and publish `sprk_wizard_commands` |

---

## Related Documentation

- [ADR-006: PCF over Web Resources](../adr/ADR-006-pcf-over-webresources.md)
- [ADR-012: Shared Component Library](../adr/ADR-012-shared-component-library.md)
- [ADR-021: Fluent UI v9 Design System](../adr/ADR-021-fluent-ui-v9.md)
- [ADR-026: UI Dialog Shell Standardization](../adr/ADR-026-ui-dialog-shell-standardization.md)
