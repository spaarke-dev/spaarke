# Shared UI Components Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Documents the `@spaarke/ui-components` shared library -- component structure, theming, composition patterns, and consumer integration.

---

## Overview

`@spaarke/ui-components` is the shared React component library consumed by PCF controls, Code Pages, and Office Add-ins. It exists because ADR-012 mandates reusable, context-agnostic UI components across all Spaarke front-end surfaces.

The library is built with TypeScript, targets ES2020, and declares React `>=16.14.0` as a peer dependency. This allows PCF controls (React 16/17, platform-provided) and Code Pages (React 19, bundled) to consume the same source. The library uses Fluent UI v9 exclusively (ADR-021) with selective per-package peer dependencies rather than the monolithic `@fluentui/react-components` meta-package.

The key architectural decision is **context-agnosticism**: no component may reference `ComponentFramework.Context` or assume a specific hosting environment. Host-specific concerns (Xrm access, auth tokens, data fetching) are injected via props or service interfaces.

## Component Structure

### Component Categories

| Category | Components | Responsibility |
|----------|-----------|---------------|
| **Dataset / Grid** | `UniversalDatasetGrid`, `GridView`, `CardView`, `ListView`, `VirtualizedGridView`, `VirtualizedListView`, `ViewSelector` | Tabular data display with OOB Dataverse parity |
| **Wizard Infrastructure** | `WizardShell`, `WizardStepper`, `WizardSuccessScreen`, `wizardShellReducer` | Generic multi-step dialog shell with reducer-driven state |
| **Domain Wizards** | `CreateMatterWizard`, `CreateProjectWizard`, `CreateEventWizard`, `CreateTodoWizard`, `CreateWorkAssignmentWizard`, `CreateRecordWizard`, `SummarizeFilesWizard` | Entity-specific creation flows built on WizardShell |
| **AI Components** | `SprkChat`, `AiFieldTag`, `AiProgressStepper`, `AiSummaryPopover`, `InlineAiToolbar`, `SlashCommandMenu`, `DiffCompareView` | AI-powered interaction, streaming, revision comparison |
| **Layout** | `PageChrome`, `SidePane`, `PanelSplitter`, `WorkspaceShell`, `Toolbar`, `RecordCardShell` | Page chrome, panels, toolbars, card shells |
| **Dialogs** | `ChoiceDialog`, `SendEmailDialog`, `FindSimilarDialog`, `FilePreviewDialog` | Modal dialog patterns |
| **Form Controls** | `LookupField`, `RichTextEditor`, `FileUpload`, `ThemeToggle`, `EmailStep`, `AssociateToStep` | Reusable form fields and wizard steps |
| **Data Visualization** | `MiniGraph`, `EventDueDateCard`, `RelationshipCountCard` | Small visual data displays |
| **Playbook** | `Playbook`, `PlaybookLibraryShell` | Playbook card grid, scope config, analysis services |

### Supporting Modules

| Module | Path | Responsibility |
|--------|------|---------------|
| Icons | `src/icons/SprkIcons.tsx` | Fluent UI v9 icon registry (central, no per-component imports) |
| Theme | `src/theme/brand.ts` | Spaarke brand palette (`spaarkeBrand`), `spaarkeLight` / `spaarkeDark` themes |
| Types | `src/types/` | 15 type definition files: Dataset, Command, Configuration, FetchXml, Lookup, WebApiLike, etc. |
| Hooks | `src/hooks/` | 19 shared hooks: `useDatasetMode`, `useVirtualization`, `useTheme`, `useSseStream`, `useAiSummary`, `useForceSimulation`, `useSlashCommands`, `useTwoPanelLayout`, etc. |
| Services | `src/services/` | `EntityCreationService`, `FetchXmlService`, `ViewService`, `ConfigurationService`, `CommandRegistry/Executor`, `SprkChatBridge`, `FieldMappingService`, `PolymorphicResolverService`, `renderMarkdown` |
| Utils | `src/utils/` | `xrmContext` (Xrm frame-walk), `themeDetection` (PCF theme bridging), `themeStorage` (localStorage + Dataverse sync), `parseDataParams`, `logger`, `lookupMatching`, `relationshipColors` |

## Data Flow

### Theming Flow (PCF Controls)

1. PCF `updateView` receives platform `context` with `fluentDesignLanguage.tokenTheme`
2. `detectTheme(context, themeMode)` resolves Fluent v9 Theme (Host, Spaarke brand, or Auto)
3. `getEffectiveDarkMode(context)` checks: localStorage preference -> platform context -> navbar DOM -> default light
4. Component renders inside `<FluentProvider theme={resolvedTheme}>`

### Theming Flow (Code Pages)

1. `resolveCodePageTheme()` checks: localStorage -> URL flags -> navbar DOM -> default light
2. `setupCodePageThemeListener(onChange)` subscribes to `storage` events and `spaarke-theme-change` custom events
3. Theme changes persist to Dataverse via `syncThemeFromDataverse()` / `persistThemeToDataverse()` for cross-device sync
4. `applyMdaTheme()` toggles the MDA dark mode URL flag and reloads the top-level frame

### Wizard Composition Flow

1. Consumer renders a domain wizard (e.g., `CreateMatterWizard`) passing `IDataService` + `onDismiss`
2. Domain wizard configures step definitions and passes them to `WizardShell`
3. `WizardShell` manages state via `wizardShellReducer` (step navigation, validation, completion)
4. Each step component receives shell handle (`IWizardShellHandle`) for navigation control
5. On completion, `WizardSuccessScreen` shows summary with optional follow-on actions

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | PCF Controls | `import { ... } from "@spaarke/ui-components"` | Workspace dependency (`file:` link), React 16 peer |
| Consumed by | Code Pages | `import { ... } from "@spaarke/ui-components"` | Workspace dependency, webpack alias to source for bundling |
| Consumed by | Office Add-ins | `import { ... } from "@spaarke/ui-components"` | Same pattern as Code Pages |
| Depends on | Fluent UI v9 | Selective peer deps (button, dialog, input, provider, etc.) | No monolithic `@fluentui/react-components` peer |
| Depends on | Lexical | `@lexical/*` peers | Rich text editing in `RichTextEditor` |
| Depends on | d3-force | Direct dependency | Force-directed graph layouts in `MiniGraph` |
| Depends on | react-window | Direct dependency | Virtualized list/grid rendering |
| Depends on | diff | Direct dependency | Text diffing for `DiffCompareView` |
| Depends on | DOMPurify + marked | Direct dependencies | Markdown rendering in `renderMarkdown` service |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Context-agnostic components | No PCF/Xrm imports in components | Components must work in PCF, Code Pages, and Add-ins | ADR-012 |
| Fluent UI v9 only | No v8 imports anywhere | Single design system, dark mode support | ADR-021 |
| Selective Fluent UI peers | Per-package peers, not monolithic | Reduces bundle size; consumers provide their own Fluent install | ADR-012 |
| React >=16.14 peer | Broad peer range | PCF provides React 16/17; Code Pages bundle React 19 | ADR-022 |
| TypeScript `tsc` build | No bundler (outputs `dist/`) | Library consumers bundle it; Code Pages webpack-alias to `src/` for tree-shaking | -- |
| IDataService abstraction | Domain wizards accept injected service interface | Decouples UI from data layer; testable with mocks | ADR-012 |
| Theme persistence in Dataverse | `sprk_userpreference` entity | Cross-device theme sync via `syncThemeFromDataverse` / `persistThemeToDataverse` | ADR-021 |

## Constraints

- **MUST**: Use Fluent UI v9 exclusively -- no v8 imports (ADR-021)
- **MUST**: Keep components context-agnostic -- no `ComponentFramework.Context` or `Xrm.*` in component props (ADR-012)
- **MUST**: Export TypeScript types alongside components via barrel files
- **MUST NOT**: Hard-code colors -- use Fluent theme tokens (ADR-021)
- **MUST NOT**: Import the monolithic `@fluentui/react-components` as a peer -- use selective per-package peers
- **MUST NOT**: Use `createRoot` (React 18+) anywhere in the library -- consumers control rendering lifecycle
- **MUST**: Wrap all tests in `<FluentProvider theme={...}>` to provide token context

## Known Pitfalls

- **React version mismatch**: Code Pages bundle React 19 but the shared library's `devDependencies` use React 16 types. Webpack aliases in Code Page configs force a single React instance to prevent "multiple React copies" errors.
- **Peer dependency resolution**: PCF controls rely on platform-provided React; the library must not bundle React. The `peerDependencies` declaration enforces this.
- **Theme token vs resolved value**: Inside `FluentProvider`, Fluent tokens are CSS variable references. Outside it (e.g., `document.body.style.backgroundColor`), you must read the resolved value from the theme object directly (e.g., `theme.colorNeutralBackground1`).
- **OS theme not consulted**: ADR-021 explicitly excludes `prefers-color-scheme` from the theme cascade. The Spaarke theme system (localStorage + Dataverse sync + navbar detection) controls all surfaces.

## Related

- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) -- Shared component library
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) -- Fluent UI v9 design system
- [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) -- PCF platform libraries
- [universal-dataset-grid-architecture.md](universal-dataset-grid-architecture.md) -- UniversalDatasetGrid deep-dive
- [ui-dialog-shell-architecture.md](ui-dialog-shell-architecture.md) -- Dialog shell patterns
- [SIDE-PANE-PLATFORM-ARCHITECTURE.md](SIDE-PANE-PLATFORM-ARCHITECTURE.md) -- Side pane integration
