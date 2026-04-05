# Workspace Architecture

> **Last Updated**: April 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Describes the declarative workspace layout system, panel composition, section registration, and the Layout Wizard.

---

## Overview

The Workspace system provides a declarative, personalized dashboard experience for Dataverse model-driven app users. At its core, `WorkspaceShell` (shared library) renders a CSS grid layout from a `WorkspaceConfig` object that describes rows, sections, and their content. Individual sections self-register via a `SectionRegistration` interface, providing a factory function that produces section configuration from a standardized `SectionFactoryContext`. Users personalize their workspace layout through the `WorkspaceLayoutWizard`, which saves layout configurations (template + section assignments) to the BFF API.

The architecture follows two key ADRs: ADR-012 (shared component library, reusable across solutions) and ADR-021 (Fluent UI v9 with dark mode support via semantic tokens).

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| WorkspaceShell | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/WorkspaceShell.tsx` | Renders multi-row grid layout from declarative `WorkspaceConfig` |
| SectionPanel | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/SectionPanel.tsx` | Bordered card wrapper with title bar, badge count, toolbar, collapsible content |
| layoutTemplates | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/layoutTemplates.ts` | 9 predefined CSS grid templates (2-col, 3-row, sidebar, hero, etc.) |
| types | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/types.ts` | All type definitions: WorkspaceConfig, SectionConfig, SectionRegistration, SectionFactoryContext |
| ActionCardRow | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCardRow.tsx` | Renders action card grid for "Get Started" sections |
| MetricCardRow | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/MetricCardRow.tsx` | Renders metric card grid for "Quick Summary" sections |
| WorkspaceLayoutWizard | `src/solutions/WorkspaceLayoutWizard/src/App.tsx` | 3-step wizard (Choose Layout, Select Components, Arrange Sections) for layout personalization |
| LegalWorkspace config | `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` | Factory function that builds WorkspaceConfig for the Legal workspace |
| Section registrations | `src/solutions/LegalWorkspace/src/sections/*.registration.ts` | Self-contained section definitions with factory functions |

## Declarative Layout System

WorkspaceShell supports two layout variants:

- **`"single-column"`**: All sections stacked vertically in declaration order
- **`"rows"`**: Sections grouped into rows with configurable `gridTemplateColumns` per row

Each row specifies which section IDs to include and a CSS grid-template-columns value. At viewport widths below 768px, rows collapse to single-column (`1fr`) automatically.

### Layout Templates

Nine predefined templates are available, with stable IDs stored in Dataverse user configuration:

| Template ID | Name | Rows | Slots |
|------------|------|------|-------|
| `2-col-equal` | Two Column | 2 rows of 2 equal columns | 4 |
| `3-row-mixed` | Three Row (default) | 2-col, full-width, 2-col | 5 |
| `sidebar-main` | Sidebar + Main | 3 rows of 1:2 ratio | 6 |
| `single-column` | Single Column | 4 stacked rows | 4 |
| `single-column-5` | Single Column (5) | 5 stacked rows | 5 |
| `3-col-equal` | Three Column | 2 rows of 3 equal columns | 6 |
| `3-col-3-row` | Three Column, Three Row | 3 rows of 3 columns | 9 |
| `hero-grid` | Hero + Grid | Full-width hero + 3-col row | 4 |
| `hero-2x2` | Hero + 2x2 Grid | Full-width hero + two 2-col rows | 5 |

## Section Registration Pattern

Each workspace section implements the `SectionRegistration` interface with metadata (id, label, description, icon, category) and a `factory` function that receives `SectionFactoryContext` and returns a `SectionConfig`.

The `SectionFactoryContext` provides standardized dependencies:
- `webApi` for Dataverse queries
- `userId` for ownership filtering
- `service` for document operations
- `bffBaseUrl` for BFF API calls
- `onNavigate`, `onOpenWizard` for navigation
- `onBadgeCountChange`, `onRefetchReady` for cross-section communication
- `scope` ("my" or "all") and `businessUnitId` for record filtering

### Section Types (Discriminated Union)

| Type | Config Interface | Body Rendering |
|------|-----------------|---------------|
| `action-cards` | `ActionCardSectionConfig` | ActionCardRow with click handlers |
| `metric-cards` | `MetricCardSectionConfig` | MetricCardRow with trend indicators |
| `content` | `ContentSectionConfig` | Arbitrary content via `renderContent()` |

### Registered Sections

| Section ID | Label | Category | Registration File |
|-----------|-------|----------|------------------|
| `get-started` | Get Started | overview | `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts` |
| `quick-summary` | Quick Summary | overview | `src/solutions/LegalWorkspace/src/sections/quickSummary.registration.ts` |
| `latest-updates` | Latest Updates | data | `src/solutions/LegalWorkspace/src/sections/latestUpdates.registration.ts` |
| `todo` | My To Do List | productivity | `src/solutions/LegalWorkspace/src/sections/todo.registration.ts` |
| `documents` | My Documents | data | `src/solutions/LegalWorkspace/src/sections/documents.registration.ts` |

## Data Flow

1. User opens the workspace; the solution loads saved layout configuration from the BFF API (`GET /api/workspace/layouts`)
2. Layout config specifies a `LayoutTemplateId` and a `sectionsJson` object mapping slot positions to section IDs
3. The solution resolves `SectionRegistration` objects for each section ID and calls `factory(context)` to produce `SectionConfig` objects
4. A `WorkspaceConfig` is assembled with the template's row definitions and the produced section configs
5. `WorkspaceShell` renders the grid: for each row, it looks up section configs by ID and renders them inside `SectionPanelWrapper`
6. `SectionPanel` renders the chrome (title bar, badge, toolbar, collapse toggle); section body content is rendered by the type-specific renderer (ActionCardRow, MetricCardRow, or renderContent)
7. Cross-section communication: sections register refetch callbacks via `onRefetchReady`; badge counts update via `onBadgeCountChange`

### Layout Wizard Flow

1. User opens the Layout Wizard (Code Page via `navigateTo`)
2. Step 1 (Choose Layout): user selects a grid template from thumbnails
3. Step 2 (Select Components): user toggles sections on/off from the catalog, selects scope (my/all)
4. Step 3 (Arrange Sections): user assigns sections to grid slots via drag-and-drop and enters a layout name
5. On Finish: wizard POSTs/PUTs layout config to BFF API; sets `window.__dialogResult` for the parent form

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | LegalWorkspace | `WorkspaceShell` + section registrations | Primary consumer |
| Consumed by | WorkspaceLayoutWizard | `WizardShell` (embedded) + `layoutTemplates` | Layout personalization |
| Depends on | BFF API | `GET/POST/PUT/DELETE /api/workspace/layouts` | Layout CRUD |
| Depends on | Fluent UI v9 | CSS grid, semantic tokens | ADR-021 |
| Depends on | WizardShell | Shared wizard framework | Layout Wizard uses WizardShell in embedded mode |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Declarative config | Data-driven `WorkspaceConfig` object | Enables personalization without code changes; config stored in Dataverse | ADR-012 |
| Section Registry pattern | `SectionRegistration` with factory function | Self-contained sections with no bespoke parent wiring; standardized context | — |
| CSS grid for layout | `gridTemplateColumns` per row | Native responsive behavior; rows collapse at 767px breakpoint | ADR-021 |
| Stable template IDs | String literals stored in Dataverse | Template IDs must never be renamed; layout config references them | — |
| Controller ref pattern | Mutable ref for title-body sibling communication | Documents section title (view picker) and body (card list) share state without lifting to workspace root | — |

## Constraints

- **MUST**: Use `WorkspaceShell` for all workspace-style dashboards (ADR-012)
- **MUST**: Implement `SectionRegistration` for new workspace sections
- **MUST**: Use `SectionFactoryContext` exclusively as the dependency contract; no bespoke props
- **MUST NOT**: Rename layout template IDs (stored in Dataverse user configuration)
- **MUST NOT**: Hardcode colors; use Fluent v9 semantic tokens (ADR-021)
- **MUST**: Support scope "my" and "all" in section factories that query records

## Known Pitfalls

- **Template ID stability**: Layout template IDs are persisted in Dataverse. Renaming or removing a template ID will break users' saved layouts. Add new templates; never modify existing IDs
- **Section ID stability**: Section IDs are stored in the `sectionsJson` layout configuration. Adding new sections is safe; removing a section ID means existing layouts referencing it will render empty slots
- **Max 10 workspaces**: The BFF API enforces a maximum of 10 saved workspace layouts per user (HTTP 409 on overflow)
- **Controller ref timing**: The Documents section uses a mutable controller ref pattern for title-body communication. The `onControllerReady` callback fires on every render (not just mount) to handle config memo re-evaluation

## Related

- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) — Shared Component Library
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 Design System
- [wizard-framework-architecture.md](wizard-framework-architecture.md) — WizardShell used by WorkspaceLayoutWizard
- [sdap-workspace-integration-patterns.md](sdap-workspace-integration-patterns.md) — Entity-agnostic workspace integration patterns
