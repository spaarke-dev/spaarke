# Configurable Workspace — User-Personalized Dashboard Layouts - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-28
> **Source**: design.md

## Executive Summary

Allow users to personalize their workspace dashboard by selecting a layout template, choosing which sections to display, and arranging sections via drag-and-drop. Users can create multiple named workspaces (max 10), switch between them via a header dropdown, and set a default. Layout configurations are stored per-user in a Dataverse table (`sprk_workspacelayout`). This builds on the existing `WorkspaceShell` shared component and `WorkspaceConfig` type system — making configuration **user-editable at runtime** instead of hardcoded in a factory function.

## Scope

### In Scope

- `sprk_workspacelayout` Dataverse entity (GUID PK, name, template ID, sections JSON, isDefault, sortOrder, owner)
- BFF CRUD endpoints for layouts (8 endpoints, OBO auth, max 10 user layouts enforced)
- Section Registry — code-side registry mapping section IDs to `SectionRegistration` (metadata + factory)
- Standard section contract: `SectionRegistration` interface + `SectionFactoryContext`
- 5 initial registry entries: Get Started, Quick Summary, Latest Updates, My To Do List, My Documents
- Layout Wizard Code Page (`sprk_workspacelayoutwizard`) — 3-step wizard (layout → sections → arrange)
- 6 predefined layout templates (2-col-equal, 3-row-mixed, sidebar-main, single-column, 3-col-equal, hero-grid)
- Workspace Header with dropdown switcher + settings button (system/user workspace distinction)
- Dynamic config builder — merges stored layout JSON × registry factories → `WorkspaceConfig`
- Default workspace per user (single default enforced)
- Drag-and-drop section arrangement in wizard Step 3
- `useFeedTodoSync` no-op fix for SmartToDo independence
- Schema versioning (`schemaVersion: 1`) in `sprk_sectionsjson` JSON
- Loading states: skeleton shimmer (returning user), "Personalize your workspace" banner (first visit), inline toast (fetch failure)
- URL deep-linking via `workspaceId` query parameter
- System workspace "Save As" flow (⚙️ on system workspace → wizard in Save As mode)
- `sessionStorage` caching for instant same-session render (invalidated on wizard save)
- Slot overflow handling (auto-append `1fr` rows for overflow sections)

### Out of Scope

- Org-wide default layouts (admin-defined)
- Section-level configuration (e.g., "show 6 vs 10 documents")
- Section-level data filtering (e.g., "show only my matters' documents")
- Custom sections (user-defined content)
- Layout sharing between users
- Role-based default layouts
- Mobile-specific layouts
- Dashboard analytics/reporting sections
- User-adjustable section heights
- SmartToDo inline detail panel consolidation (R2 — see `projects/events-smart-todo-kanban-r2/design.md`)
- Keyboard-accessible drag-and-drop alternative (not required for R1)
- Cross-tab cache synchronization

### Affected Areas

- `src/solutions/WorkspaceLayoutWizard/` — **NEW** standalone Code Page (React 19, Vite, single-file build)
- `src/solutions/LegalWorkspace/` — modify to use dynamic config builder instead of hardcoded `buildWorkspaceConfig`
- `src/client/shared/` — Section Registry types (`SectionRegistration`, `SectionFactoryContext`), dynamic config builder
- `src/server/api/Sprk.Bff.Api/` — 8 new workspace layout endpoints
- `src/client/shared/` — `useFeedTodoSync` no-op fix
- `src/client/shared/` — Workspace Header component (dropdown switcher)

## Requirements

### Functional Requirements

1. **FR-01**: Section Registry — Code-side array of `SectionRegistration` objects mapping section IDs to metadata (label, icon, category, description) and factory functions. Registry is the single source of truth for available sections. — Acceptance: New sections can be added by appending one registration object; no other files need changes.

2. **FR-02**: Standard Section Contract — All 5 existing sections migrated to `SectionRegistration` pattern. Each factory receives `SectionFactoryContext` (webApi, userId, service, bffBaseUrl, onNavigate, onOpenWizard, onBadgeCountChange, onRefetchReady). Sections own their own toolbar and click handlers. — Acceptance: All 5 sections render identically to current behavior when using registry factories.

3. **FR-03**: Dynamic Config Builder — Function that takes stored layout JSON + section registry → produces `WorkspaceConfig` for `WorkspaceShell`. Checks `schemaVersion`, skips unknown section IDs with console warning, maps template grid + section placements to `WorkspaceRowConfig[]`. — Acceptance: Given a valid `sprk_sectionsjson` blob and registry, produces a `WorkspaceConfig` that renders correctly in `WorkspaceShell`.

4. **FR-04**: BFF CRUD Endpoints — 8 endpoints for workspace layout management:
   - `GET /api/workspace/layouts` — list user's layouts (system + user-created)
   - `GET /api/workspace/layouts/{id}` — get specific layout
   - `GET /api/workspace/layouts/default` — get user's default (or system default)
   - `POST /api/workspace/layouts` — create (enforce max 10 per user)
   - `PUT /api/workspace/layouts/{id}` — update (user-created only; reject system layouts)
   - `DELETE /api/workspace/layouts/{id}` — delete (user-created only)
   - `GET /api/workspace/sections` — list available sections from registry
   - `GET /api/workspace/templates` — list available layout templates
   All endpoints use OBO auth, scoped to current user's records. — Acceptance: All CRUD operations work; system layouts are read-only; max 10 enforced on POST.

5. **FR-05**: Layout Wizard Code Page — 3-step wizard (`sprk_workspacelayoutwizard`):
   - Step 1: Choose layout template (6 visual thumbnails with grid preview)
   - Step 2: Select sections (grouped checklist by category from registry, shows slot count vs selected count)
   - Step 3: Arrange sections in layout slots via drag-and-drop + name workspace + set as default checkbox
   Launched via `Xrm.Navigation.navigateTo` at 70% width × 80% height. Uses `WizardDialog` from `@spaarke/ui-components`. Supports create mode (blank) and edit mode (pre-populated). — Acceptance: User can create and edit workspaces through all 3 wizard steps.

6. **FR-06**: Workspace Header — Dropdown showing active workspace name. System workspaces shown first with lock icon, user workspaces after divider, "+ New Workspace" at bottom. ⚙️ Edit button: opens wizard in edit mode (user layout) or Save As mode (system layout, name pre-filled as "{name} (copy)"). — Acceptance: User can switch workspaces, create new, and edit/Save As existing via header.

7. **FR-07**: Default Workspace & Loading — On navigation, load user's default layout (or system default for first-time users). Cache last-used layout in `sessionStorage` for instant render on same-session navigation. Invalidate cache on wizard save (POST/PUT success). First-time users see system default with subtle "Personalize your workspace" banner. Fetch failure falls back to cached layout or system default with inline toast. — Acceptance: Returning users see their default instantly; first-time users see system default with banner; failures degrade gracefully.

8. **FR-08**: `useFeedTodoSync` No-Op Fix — Change `useFeedTodoSync()` hook to return no-op stubs when no `FeedTodoSyncProvider` exists (instead of throwing). SmartToDo works independently in any context: workspace section with or without ActivityFeed, standalone Code Page, or dialog. — Acceptance: SmartToDo renders without crash when ActivityFeed section is absent from workspace.

9. **FR-09**: URL Deep-Linking — Support optional `workspaceId` URL parameter: `sprk_corporateworkspace?data=workspaceId={guid}`. If present, load that layout instead of the default. — Acceptance: Bookmarked workspace URLs load the correct layout.

10. **FR-10**: Slot Overflow/Underflow — Fewer sections than slots: unfilled slots don't render, rows with zero sections are skipped. More sections than slots: auto-append `1fr` (full-width) rows. Zero sections: wizard disables "Save" with tooltip. — Acceptance: All overflow/underflow scenarios render correctly without errors.

11. **FR-11**: System vs User Workspaces — One system workspace in R1: "Corporate Workspace" (mirrors current `corporateworkspace.html` layout). System workspaces are read-only and non-deletable. ⚙️ button opens wizard in "Save As" mode. System workspaces don't count toward the 10-workspace limit. — Acceptance: System workspace is always available; cannot be edited or deleted; Save As creates user copy.

### Non-Functional Requirements

- **NFR-01**: Layout JSON fetch and render must not degrade perceived load time — use `sessionStorage` cache for instant render, fetch in background.
- **NFR-02**: All wizard and workspace header UI must use Fluent UI v9 exclusively with dark mode support via semantic tokens (ADR-021).
- **NFR-03**: Wizard must build as a single HTML file via Vite + `vite-plugin-singlefile` (ADR-026).
- **NFR-04**: All sections must render correctly at any grid width: 33%, 50%, 100% (flex-wrap, CSS Grid auto-fill, percentage-based layouts — no fixed outer widths).
- **NFR-05**: Unknown section IDs in stored JSON must be skipped gracefully (console warning, no crash).
- **NFR-06**: No React components or JSX serialized to Dataverse — store only section IDs and grid positions.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern — all BFF endpoints use Minimal API, return `ProblemDetails` for errors
- **ADR-006**: Layout Wizard is a Code Page (not PCF) — standalone dialog launched via `navigateTo`
- **ADR-008**: Endpoint authorization via filters — no global middleware for resource checks
- **ADR-012**: Sections in `@spaarke/ui-components`; use shared component library; Fluent v9 only; semantic tokens; no PCF-specific APIs in shared components
- **ADR-021**: Fluent UI v9 exclusively; `makeStyles` (Griffel) for custom styling; dark mode + high contrast; WCAG 2.1 AA; no hard-coded colors
- **ADR-026**: Code Page standard — Vite + `vite-plugin-singlefile`; React 19 `createRoot`; standalone at `src/solutions/WorkspaceLayoutWizard/`; web resource named `sprk_workspacelayoutwizard`; Xrm frame-walk for Dataverse API access

### MUST Rules (from ADRs + Design)

- ✅ MUST use Minimal API for all endpoints (ADR-001)
- ✅ MUST use endpoint filters for authorization (ADR-008)
- ✅ MUST use Fluent UI v9 exclusively, semantic tokens, dark mode (ADR-021)
- ✅ MUST build wizard as single HTML file via Vite + vite-plugin-singlefile (ADR-026)
- ✅ MUST use React 19 createRoot in wizard Code Page (ADR-026)
- ✅ MUST import shared components via `@spaarke/ui-components` (ADR-012)
- ✅ MUST store only section IDs and grid positions in Dataverse — no serialized JSX
- ✅ MUST gracefully handle missing section IDs (skip with console warning)
- ✅ MUST fall back to system default layout when user has no saved layouts
- ✅ MUST enforce single default per user (clear previous default on save)
- ✅ MUST enforce max 10 user workspaces per user
- ✅ MUST NOT allow editing or deleting system workspaces
- ✅ MUST include `schemaVersion` in all `sprk_sectionsjson` JSON
- ✅ MUST cache last-used layout in `sessionStorage`, invalidate on wizard save
- ✅ MUST use environment variables for BFF base URL (BYOK support)
- ❌ MUST NOT use Fluent v8, hard-coded colors, or alternative UI libraries
- ❌ MUST NOT use PCF for the wizard (React 16 limitation)
- ❌ MUST NOT use React Router in wizard — use Xrm.Navigation or postMessage

### Existing Patterns to Follow

- See `src/solutions/LegalWorkspace/` for existing `WorkspaceShell` usage and `buildWorkspaceConfig` pattern
- See `src/client/shared/` for `WorkspaceShell`, `WorkspaceConfig`, `WorkspaceRowConfig` types
- See `src/client/shared/` for `WizardDialog` component (reuse in wizard)
- See `.claude/patterns/api/` for BFF endpoint patterns
- See `.claude/patterns/` for Code Page build patterns

## Data Model

### New Entity: `sprk_workspacelayout`

| Column | Type | Required | Purpose |
|--------|------|----------|---------|
| `sprk_workspacelayoutid` | GUID | PK | Primary key |
| `sprk_name` | String (100) | Yes | Workspace display name |
| `sprk_layouttemplateid` | String (50) | Yes | Template identifier (e.g., "2x2", "sidebar-main") |
| `sprk_sectionsjson` | Multiline (max) | Yes | JSON: `{ schemaVersion: 1, rows: [...] }` |
| `sprk_isdefault` | Boolean | Yes | User's default workspace (one per user) |
| `sprk_sortorder` | Integer | No | Display order in dropdown |
| `sprk_ownerid` | Owner (User) | Yes | Standard Dataverse owner field |

### `sprk_sectionsjson` Schema

```json
{
  "schemaVersion": 1,
  "rows": [
    {
      "id": "row-1",
      "columns": "1fr 1fr",
      "columnsSmall": "1fr",
      "sections": ["get-started", "quick-summary"]
    }
  ]
}
```

### Layout Templates (6 predefined)

| ID | Name | Grid |
|----|------|------|
| `2-col-equal` | Two Column | 2 rows × 2 cols (1fr 1fr) |
| `3-row-mixed` | Three Row | Row 1: 1fr 1fr, Row 2: 1fr, Row 3: 1fr 1fr |
| `sidebar-main` | Sidebar + Main | 1fr 2fr |
| `single-column` | Single Column | 1fr per row |
| `3-col-equal` | Three Column | 1fr 1fr 1fr |
| `hero-grid` | Hero + Grid | Row 1: 1fr, Row 2: 1fr 1fr 1fr |

## Key Interfaces

### SectionRegistration

```typescript
interface SectionRegistration {
  id: string;
  label: string;
  description: string;
  icon: FluentIcon;
  category: "overview" | "data" | "ai" | "productivity";
  defaultHeight?: string;
  previewIcon?: FluentIcon;
  factory: (context: SectionFactoryContext) => SectionConfig;
}
```

### SectionFactoryContext

```typescript
interface SectionFactoryContext {
  webApi: IWebApi;
  userId: string;
  service: DataverseService;
  bffBaseUrl: string;
  onNavigate: (target: NavigateTarget) => void;
  onOpenWizard: (webResourceName: string, data?: string, options?: DialogOptions) => void;
  onBadgeCountChange: (count: number) => void;
  onRefetchReady: (refetch: () => void) => void;
}
```

## Design Decisions (from design doc)

1. **Slot overflow/underflow** — unfilled slots skipped; overflow gets auto-appended `1fr` rows
2. **SmartToDo independence** — `useFeedTodoSync` returns no-op stubs instead of throwing
3. **Loading states** — first visit: system default + banner; returning: skeleton + sessionStorage cache; failure: fallback + toast
4. **Schema versioning** — `schemaVersion` field in JSON; config builder checks version and migrates
5. **No duplicate sections** — each section ID at most once per layout (checkboxes, not quantities)
6. **URL deep-linking** — `workspaceId` parameter overrides default
7. **Section height** — section declares `defaultHeight` in registry; not user-adjustable in R1
8. **System vs user workspaces** — system read-only, always available; ⚙️ opens Save As mode
9. **Multiple workspaces = same data** — workspaces are layout configs, not data filters
10. **Workspace limit** — 10 user workspaces; enforced in BFF POST; system layouts exempt
11. **No new cross-section sync** — existing `FeedTodoSyncContext` sufficient with no-op fix

## Success Criteria

1. [ ] User can create a named workspace with a chosen layout and selected sections — Verify: end-to-end wizard flow
2. [ ] User can switch between saved workspaces via header dropdown — Verify: dropdown switches rendered layout
3. [ ] User can set a default workspace that loads automatically — Verify: navigation loads default layout
4. [ ] User can drag-and-drop sections within the layout in the wizard — Verify: wizard Step 3 DnD works
5. [ ] Workspace renders correctly using dynamic config from Dataverse — Verify: layout matches stored JSON
6. [ ] System workspace ("Corporate Workspace") is preserved and read-only — Verify: no edit/delete on system workspace
7. [ ] User can "Save As" from a system workspace to create a customized copy — Verify: Save As creates new user workspace
8. [ ] SmartToDo section works independently (no crash when ActivityFeed absent) — Verify: workspace with only SmartToDo renders
9. [ ] New sections can be added by registering in the Section Registry — Verify: add test registration, appears in wizard
10. [ ] First-time users see system default with "Personalize" banner — Verify: new user experience flow
11. [ ] URL deep-linking with `workspaceId` parameter works — Verify: bookmarked URL loads correct workspace

## Dependencies

### Prerequisites

- `WorkspaceShell` shared component (exists)
- `WizardDialog` shared component (exists)
- `WorkspaceConfig` / `WorkspaceRowConfig` type system (exists)
- BFF API infrastructure (exists)
- `buildWorkspaceConfig` in `workspaceConfig.tsx` (exists — will be replaced by dynamic config builder)

### Related Projects

- `events-smart-todo-kanban-r2` — depends on this project's `useFeedTodoSync` no-op fix
- `ai-analysis-workspace-sprkchat-integration-r1` — establishes panel consolidation pattern reused by SmartToDo R2

### External

- None — self-contained feature using existing infrastructure

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| DnD accessibility | Keyboard-accessible alternative for drag-and-drop? | Not required for R1 | No need for move up/down buttons alongside DnD |
| Wizard location | Standalone or co-located with LegalWorkspace? | Standalone at `src/solutions/WorkspaceLayoutWizard/` | Follows ADR-026 convention, own build pipeline |
| System workspaces | How many ship in R1? | One — "Corporate Workspace" (current `corporateworkspace.html`) | Single system layout definition to code |
| Cache invalidation | When to invalidate sessionStorage cache? | On wizard save (POST/PUT success). No cross-tab sync in R1 | Simple invalidation strategy, no BroadcastChannel needed |

## Assumptions

- **Section categories**: The 4 categories (overview, data, ai, productivity) are sufficient for R1 registry entries. New categories can be added as string union members.
- **Wizard dialog return**: Wizard communicates save result back to workspace via `Xrm.Navigation.navigateTo` promise resolution (standard Code Page pattern).
- **System workspace definition**: Defined in code as a constant (not in Dataverse), returned by BFF alongside user layouts.

## Unresolved Questions

- None — all blocking questions resolved in owner clarification.

---

*AI-optimized specification. Original design: design.md*
