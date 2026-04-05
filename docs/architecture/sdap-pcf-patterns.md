# SDAP PCF Control Patterns

> **Last Updated**: April 5, 2026
> **Purpose**: Architecture of Power Apps Component Framework (PCF) controls in the Spaarke platform

---

## Overview

The Spaarke platform uses 14 PCF controls to embed custom TypeScript/React UI into Dataverse model-driven app forms. All PCF controls share a common architecture: field-bound or dataset-bound controls that render React components inside a `FluentProvider`, authenticate via MSAL.js against the BFF API, and follow ADR-006 (PCF for form controls, Code Pages for standalone dialogs), ADR-021 (Fluent UI v9 exclusively), and ADR-022 (React 16 platform-provided APIs for PCF, React 18 bundled for Code Pages).

The key architectural decision was ADR-006: **field-bound controls on a Dataverse form use PCF; standalone dialogs and pages use Code Pages (never Custom Page + PCF wrapper)**. The deprecated Custom Page wrapper pattern required two build steps and unnecessary complexity. Code Pages (`src/solutions/*/`) provide a single-build, single-deploy path via `Xrm.Navigation.navigateTo`.

---

## Control Catalog

### Document & File Controls

| Control | Path | Purpose | React API |
|---------|------|---------|-----------|
| UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/` | Form-embedded multi-file upload to SPE with Dataverse record creation | StandardControl (React 18 via createRoot) |
| UniversalDatasetGrid | `src/client/pcf/UniversalDatasetGrid/` | Custom dataset grid with actions, calendar filter, auth integration | StandardControl (ReactDOM.render) |
| DocumentRelationshipViewer | `src/client/pcf/DocumentRelationshipViewer/` | Interactive graph of document relationships via vector similarity | ReactControl |
| RelatedDocumentCount | `src/client/pcf/RelatedDocumentCount/` | Badge showing count of semantically related documents | ReactControl |

### AI & Search Controls

| Control | Path | Purpose | React API |
|---------|------|---------|-----------|
| SemanticSearchControl | `src/client/pcf/SemanticSearchControl/` | Natural language document search against BFF Semantic Search API | ReactControl |
| ScopeConfigEditor | `src/client/pcf/ScopeConfigEditor/` | Adaptive editor for 4 AI scope entity forms (Action, Skill, Knowledge, Tool) | ReactControl |
| AIMetadataExtractor | `src/client/pcf/AIMetadataExtractor/` | AI metadata extraction (placeholder — directory exists but empty) | — |

### Entity & Relationship Controls

| Control | Path | Purpose | React API |
|---------|------|---------|-----------|
| AssociationResolver | `src/client/pcf/AssociationResolver/` | Parent entity type/record selector for Events with field mapping | StandardControl (ReactDOM.render) |
| UpdateRelatedButton | `src/client/pcf/UpdateRelatedButton/` | Triggers BFF API field mapping rules on related records | StandardControl (ReactDOM.render) |
| SpaarkeGridCustomizer | `src/client/pcf/SpaarkeGridCustomizer/` | Custom cell renderers for Power Apps Grid Control (regarding links) | PAOneGridCustomizer |

### Visualization & Layout Controls

| Control | Path | Purpose | React API |
|---------|------|---------|-----------|
| VisualHost | `src/client/pcf/VisualHost/` | Configuration-driven chart/card/calendar rendering from `sprk_chartdefinition` | StandardControl (ReactDOM.render) |
| DrillThroughWorkspace | `src/client/pcf/DrillThroughWorkspace/` | Two-panel drill-through: chart (1/3) + dataset grid (2/3) | StandardControl (ReactDOM.render) |

### Platform & Admin Controls

| Control | Path | Purpose | React API |
|---------|------|---------|-----------|
| ThemeEnforcer | `src/client/pcf/ThemeEnforcer/` | Invisible control enforcing user dark mode preference on app load | StandardControl (no React rendering) |
| EmailProcessingMonitor | `src/client/pcf/EmailProcessingMonitor/` | Admin dashboard for email-to-document processing statistics | StandardControl (ReactDOM.render) |

---

## Shared Patterns

### Initialization Lifecycle

Every PCF control follows the same lifecycle:

1. `constructor()` — empty (no initialization here)
2. `init(context, notifyOutputChanged, state, container)` — store references, resolve theme, initialize MSAL if needed
3. `updateView(context)` — re-render React component tree inside `FluentProvider` with resolved theme
4. `destroy()` — unmount React, clean up theme listeners

Two rendering patterns exist:

| Pattern | Used By | Lifecycle |
|---------|---------|-----------|
| `StandardControl` with `ReactDOM.render()` | Most controls (AssociationResolver, VisualHost, etc.) | `render()` in `updateView()`, `unmountComponentAtNode()` in `destroy()` |
| `ReactControl` with `updateView()` returning `React.ReactElement` | RelatedDocumentCount, SemanticSearchControl, DocumentRelationshipViewer, ScopeConfigEditor | Framework manages mounting/unmounting |

### Theme Resolution

All controls resolve Fluent UI v9 theme at init time and listen for changes:

```
resolveThemeWithUserPreference() → webLightTheme | webDarkTheme
setupThemeListener(callback) → cleanup function stored for destroy()
```

Theme utilities are imported from `@spaarke/ui-components`.

### Authentication

Controls that call the BFF API authenticate via MSAL.js with runtime-resolved configuration from Dataverse environment variables (`sprk_BffApiBaseUrl`, `sprk_MsalClientId`, `sprk_BffApiAppId`, `sprk_TenantId`). No hardcoded client IDs in source.

### Shared Code

`src/client/pcf/shared/` provides cross-control utilities (types, helpers). Upload services (`MultiFileUploadService`, `DocumentRecordService`, `NavMapClient`) live in `@spaarke/ui-components` for reuse across PCF and Code Page surfaces.

---

## Component Structure

### UniversalQuickCreate (Upload)

**Purpose**: Form-embedded file upload for Matters, Projects, and other entities with SPE containers.

**Entity Configuration**: Each supported entity is configured via `EntityDocumentConfig.ts` with entity name, lookup field name, relationship schema name (exact Dataverse name — case matters), container ID field, display name field, and entity set name. Adding a new entity requires a config entry, a `sprk_containerid` field, and a 1:N relationship to `sprk_document`.

**Upload Flow**:
1. Get entity config from registry
2. Acquire auth token via MSAL.js (`api://...user_impersonation` scope)
3. Get navigation property name via NavMapClient (prevents case mismatches)
4. Upload file to SPE container via BFF API
5. Create `sprk_document` record in Dataverse with OData binding to parent entity

> **Migration Note (March 2026)**: The primary document upload experience migrated to the DocumentUploadWizard Code Page (`sprk_documentuploadwizard`) per ADR-006. The Custom Page + PCF wrapper pattern is deprecated. UniversalQuickCreate remains for form-embedded upload only.

### ScopeConfigEditor

Adaptive editor that detects the AI scope entity type from the form context and renders the appropriate editor: rich textarea with token counter (Action), compact textarea with injection preview (Skill), markdown textarea with file upload (Knowledge Source), or CodeMirror JSON editor with handler dropdown (Tool). Bundle target < 1 MB using CodeMirror (not Monaco).

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| PCF vs Code Page boundary | Field-bound → PCF; standalone dialog → Code Page | Eliminates Custom Page wrapper complexity | ADR-006 |
| Fluent UI version | v9 exclusively | Design system consistency, dark mode, WCAG 2.1 AA | ADR-021 |
| React version in PCF | React 16 platform-provided APIs | PCF platform only provides React 16/17; bundling React 18 in PCF causes conflicts | ADR-022 |
| React Flow version | `react-flow-renderer` v10 (not `@xyflow/react` v12+) | v12+ requires React 18; v10 is React 16-compatible | ADR-022 |
| Auth config source | Dataverse environment variables at runtime | No hardcoded client IDs; portable across environments | — |
| Upload services location | `@spaarke/ui-components` shared library | Reuse across PCF and Code Page surfaces | ADR-012 |

---

## Constraints

- **MUST** use Fluent UI v9 exclusively — no v8 components (ADR-021)
- **MUST** use React 16 APIs in PCF (`ReactDOM.render`, not `createRoot`) except UniversalQuickCreate which bundles its own React 18 (ADR-022)
- **MUST** include a version footer in every PCF control UI for deployment verification
- **MUST** update version in 4 locations on release: source manifest, UI footer, solution manifest, solution control manifest
- **MUST NOT** create standalone dialogs as Custom Page + PCF wrapper — use Code Pages instead (ADR-006)
- **MUST NOT** mix Fluent UI v8 and v9 in the same control
- **MUST NOT** hardcode Dataverse entity schemas or client IDs

---

## Known Pitfalls

| Pitfall | Symptom | Resolution |
|---------|---------|------------|
| React 16 vs 18 confusion | `createRoot` not available; runtime errors | PCF must use `ReactDOM.render()` (React 16). Only Code Pages and UniversalQuickCreate (which bundles its own React 18) use `createRoot()`. |
| `notifyOutputChanged` not triggering `updateView` | Control appears stuck after output property change | Ensure `notifyOutputChanged` is called AND the PCF manifest has matching `usage="output"` properties. Framework only calls `updateView` when bound input properties change. |
| Stale `@spaarke/ui-components` dist/ | Runtime errors from old shared library build | Run `npm run build` in `src/client/shared/Spaarke.UI.Components/` before building the PCF control. Shared library must be rebuilt after any change. |
| Wrong `relationshipSchemaName` casing | NavMap API returns 404, record create returns 400 | Schema names are case-sensitive in Dataverse. Always use NavMapClient to discover the correct PascalCase navigation property at runtime. |
| Custom Page republish forgotten | Old PCF version still shows after deploy | If PCF is embedded in a Custom Page, the Custom Page must be re-opened, saved, and published in Power Apps Maker after PCF solution import. |
| Theme not updating on preference change | Control stays in light mode when user switches to dark | Must call `setupThemeListener()` in `init()` and store the cleanup function for `destroy()`. |

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | BFF API | HTTPS endpoints with Bearer token | Upload, preview, search, analysis |
| Depends on | `@spaarke/ui-components` | Shared upload services, theme utils | Must be built before PCF build |
| Depends on | `@spaarke/auth` | MSAL initialization and token acquisition | Runtime config from Dataverse env vars |
| Depends on | Dataverse WebAPI | `context.webAPI` for record CRUD | Platform-provided in PCF context |
| Consumed by | Dataverse model-driven forms | Field-bound or dataset-bound control placement | Configured in form designer |

---

## Related

- [sdap-overview.md](sdap-overview.md) — System architecture
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — MSAL.js token handling
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — Backend endpoints
- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) — PCF vs Code Page boundary
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 design system
- [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — PCF platform libraries

---

*Last Updated: April 5, 2026*
