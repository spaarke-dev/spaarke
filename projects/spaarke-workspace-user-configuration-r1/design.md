# Configurable Workspace — User-Personalized Dashboard Layouts

> **Project**: spaarke-workspace-user-configuration-r1
> **Status**: Design
> **Priority**: Medium
> **Last Updated**: March 28, 2026

---

## Executive Summary

Allow users to personalize their workspace dashboard by selecting a layout template, choosing which sections to display, and arranging sections via drag-and-drop. Users can create multiple named workspaces, switch between them via a header dropdown, and set a default. Layout configurations are stored per-user in a Dataverse table (`sprk_workspacelayout`).

This builds on the existing `WorkspaceShell` shared component and `WorkspaceConfig` type system, which already support declarative, data-driven layouts. The key change is making that configuration **user-editable at runtime** instead of hardcoded in a factory function.

---

## Problem Statement

Today the workspace layout is hardcoded in `workspaceConfig.tsx`:

- **Fixed layout**: 3 rows, 5 sections, predetermined column ratios — every user sees the same dashboard
- **Fixed sections**: Get Started, Quick Summary, Latest Updates, To Do, Documents — no way to add/remove/reorder
- **Single workspace**: No concept of named workspaces or switching between views
- **No personalization**: Users cannot prioritize what matters to them (e.g., a user who primarily works with documents sees the same layout as one who focuses on tasks)

The `WorkspaceShell` component already accepts a declarative `WorkspaceConfig` — the architecture supports dynamic configuration, but no mechanism exists for users to create or modify that configuration.

---

## Goals

1. **Layout wizard** — 3-step wizard Code Page for workspace configuration (layout → sections → arrange)
2. **Named workspaces** — users create multiple workspaces with distinct names and purposes
3. **Workspace switcher** — dropdown in workspace header to change active workspace
4. **Default workspace** — user selects which workspace loads on navigation
5. **Per-user storage** — layout configs stored in `sprk_workspacelayout` Dataverse entity
6. **Section registry** — code-side registry mapping section IDs to component factories
7. **Preserve existing sections** — all current workspace sections (Get Started, Quick Summary, Activity Feed, To Do, Documents) become registry entries with no behavior changes
8. **Extensible** — new sections can be added by registering in the section registry (future: SprkChat panel, Calendar, KPI charts)

---

## Design Principles

1. **Store what and where, not how** — Dataverse stores section IDs and grid positions (JSON). Rendering logic stays in code via the Section Registry. No React components or JSX serialized to the database.
2. **Section Registry is the source of truth for available sections** — the wizard reads the registry to know what's available; the workspace reads it to know how to render.
3. **WorkspaceShell unchanged** — the existing shared component already consumes `WorkspaceConfig`. This project adds a dynamic config builder that merges stored layout JSON with registry factories to produce `WorkspaceConfig` objects.
4. **Graceful defaults** — if a user has no saved layout, the current LegalWorkspace layout is the default. If a stored section ID is missing from the registry (removed in a future release), skip it gracefully.
5. **No admin-level layout management in R1** — this is per-user personalization only. Org-wide default layouts are future scope.

---

## Architecture

### Current Flow (hardcoded)

```
workspaceConfig.tsx
  └── buildWorkspaceConfig(handlers, state)
        └── returns WorkspaceConfig (hardcoded 3 rows, 5 sections)
              └── <WorkspaceShell config={config} />
```

### Target Flow (user-configurable)

```
User opens workspace
  │
  ├── BFF: GET /api/workspace/layouts?default=true
  │   └── Returns user's default layout JSON (or system default)
  │
  ├── Section Registry (code-side)
  │   └── Maps section IDs → { label, icon, factory, description }
  │
  └── Dynamic Config Builder
      └── Merges: layout JSON × registry factories → WorkspaceConfig
            └── <WorkspaceShell config={config} />

User clicks ⚙️ Settings
  │
  └── Opens Layout Wizard (Code Page)
      ├── Step 1: Choose layout template (visual thumbnails)
      ├── Step 2: Select sections (checklist from registry)
      ├── Step 3: Arrange sections in layout (drag/drop)
      │           + Name the workspace
      │           + Set as default checkbox
      └── Save: POST /api/workspace/layouts
```

### Component Diagram

```
┌─────────────────────────────────────────────────────┐
│ Workspace Header                                     │
│ ┌──────────────────────┐  ┌──────────┐              │
│ │ My Legal Dashboard ▾ │  │ ⚙️ Edit  │              │
│ └──────────────────────┘  └──────────┘              │
├─────────────────────────────────────────────────────┤
│                                                     │
│  <WorkspaceShell config={dynamicConfig} />           │
│                                                     │
│  ┌─────────────────┐  ┌─────────────────┐          │
│  │  Section A       │  │  Section B       │          │
│  │  (from registry) │  │  (from registry) │          │
│  └─────────────────┘  └─────────────────┘          │
│  ┌──────────────────────────────────────┐          │
│  │  Section C (full width)              │          │
│  └──────────────────────────────────────┘          │
│  ┌─────────────────┐  ┌─────────────────┐          │
│  │  Section D       │  │  Section E       │          │
│  └─────────────────┘  └─────────────────┘          │
│                                                     │
└─────────────────────────────────────────────────────┘
```

---

## Dataverse Storage

### New Entity: `sprk_workspacelayout`

| Column | Type | Required | Purpose |
|--------|------|----------|---------|
| `sprk_workspacelayoutid` | GUID | PK | Primary key |
| `sprk_name` | String (100) | Yes | Workspace display name (e.g., "My Legal Dashboard") |
| `sprk_layouttemplateid` | String (50) | Yes | Preset template identifier (e.g., "2x2", "sidebar-main", "3-row") |
| `sprk_sectionsjson` | Multiline (max) | Yes | JSON: section placements within the template |
| `sprk_isdefault` | Boolean | Yes | User's default workspace (only one per user) |
| `sprk_sortorder` | Integer | No | Display order in workspace dropdown |
| `sprk_ownerid` | Owner (User) | Yes | Standard Dataverse owner field — per-user config |

### `sprk_sectionsjson` Schema

```json
{
  "rows": [
    {
      "id": "row-1",
      "columns": "1fr 1fr",
      "columnsSmall": "1fr",
      "sections": ["get-started", "quick-summary"]
    },
    {
      "id": "row-2",
      "columns": "1fr",
      "sections": ["latest-updates"]
    },
    {
      "id": "row-3",
      "columns": "1fr 1fr",
      "sections": ["todo", "documents"]
    }
  ]
}
```

This maps directly to `WorkspaceRowConfig[]` — the existing type system.

### Layout Templates (predefined)

| ID | Name | Grid | Visual |
|----|------|------|--------|
| `2-col-equal` | Two Column | 2 rows × 2 cols (1fr 1fr) | `[A][B]` / `[C][D]` |
| `3-row-mixed` | Three Row (current default) | Row 1: 1fr 1fr, Row 2: 1fr, Row 3: 1fr 1fr | `[A][B]` / `[C──]` / `[D][E]` |
| `sidebar-main` | Sidebar + Main | 1fr 2fr | `[A][B──]` / `[A][C──]` |
| `single-column` | Single Column | 1fr per row | `[A]` / `[B]` / `[C]` |
| `3-col-equal` | Three Column | 1fr 1fr 1fr | `[A][B][C]` / `[D][E][F]` |
| `hero-grid` | Hero + Grid | Row 1: 1fr, Row 2: 1fr 1fr 1fr | `[A────]` / `[B][C][D]` |

Templates define **slot count and grid proportions**. The user fills slots with sections from the registry.

---

## Workspace Section Contract

### The Problem with Current Sections

Today each section is wired manually in `workspaceConfig.tsx` with bespoke callback props:

```
buildWorkspaceConfig(p: WorkspaceConfigParams)
  └── 26 parameters (webApi, userId, service, 7 count/refetch callbacks,
      7 action handlers, cardClickHandlers map)
  └── Returns hardcoded WorkspaceConfig with 5 sections
```

Each section receives a different subset of these 26 parameters. There's no standard contract — adding a new section means modifying `WorkspaceConfigParams`, `WorkspaceGrid`, and the factory function.

### The Standard Section Contract

Every workspace section implements one pattern:

```typescript
/**
 * What the Section Registry knows about each section (metadata + factory).
 * This is the ONLY interface a section author needs to implement.
 */
export interface SectionRegistration {
  /** Unique section identifier (stored in Dataverse layout JSON). */
  id: string;
  /** Display name shown in wizard Step 2 checklist. */
  label: string;
  /** One-line description shown in wizard Step 2. */
  description: string;
  /** Fluent icon shown in wizard and section header. */
  icon: FluentIcon;
  /** Category for grouping in wizard Step 2. */
  category: "overview" | "data" | "ai" | "productivity";
  /** Suggested default height (e.g., "560px"). Undefined = auto. */
  defaultHeight?: string;
  /** Thumbnail preview for wizard Step 3 (optional static image or icon). */
  previewIcon?: FluentIcon;
  /**
   * Factory function that produces a SectionConfig for WorkspaceShell.
   * Receives a standardized context — no bespoke props.
   */
  factory: (context: SectionFactoryContext) => SectionConfig;
}

/**
 * Standard context passed to every section factory.
 * Sections must work with ONLY these dependencies.
 */
export interface SectionFactoryContext {
  /** Xrm.WebApi for Dataverse queries. */
  webApi: IWebApi;
  /** Current user's systemuserid GUID. */
  userId: string;
  /** DataverseService for document/entity operations. */
  service: DataverseService;
  /** BFF API base URL (environment variable, BYOK-safe). */
  bffBaseUrl: string;
  /** Navigate to a Dataverse URL or web resource. */
  onNavigate: (target: NavigateTarget) => void;
  /** Open a Code Page wizard dialog. */
  onOpenWizard: (webResourceName: string, data?: string, options?: DialogOptions) => void;
  /**
   * Register a badge count updater. The workspace header shows this
   * count on the section's tab. Call with updated count whenever data changes.
   */
  onBadgeCountChange: (count: number) => void;
  /**
   * Register a refetch function. The workspace calls this when the user
   * clicks a global refresh or when another section triggers a cross-refresh.
   */
  onRefetchReady: (refetch: () => void) => void;
}
```

### Key Design Decision: Sections Own Their Own Behavior

The current approach pushes behavior **up** to the parent (WorkspaceGrid creates handlers, passes them down). The new approach pushes behavior **down** to the section:

| Concern | Current (parent-driven) | Target (section-driven) |
|---------|------------------------|------------------------|
| Toolbar buttons | Parent builds JSX, passes as `toolbar` prop | Section factory builds its own toolbar |
| Click handlers | Parent creates handlers, passes as callbacks | Section factory creates handlers using `onNavigate`/`onOpenWizard` |
| Badge count | Parent holds state, child calls `onCountChange` | Same — child calls `onBadgeCountChange` from context |
| Refetch | Parent holds ref, child registers via callback | Same — child calls `onRefetchReady` from context |
| Data fetching | Child fetches, but parent provides webApi/userId | Same — context provides webApi/userId |

**What changes**: Toolbar creation and action handlers move INTO the section factory. The parent no longer needs to know what buttons a section has or what they do.

**What stays the same**: Badge count and refetch patterns are already callback-based and work as-is.

### How Each Current Section Migrates

#### Get Started (action-cards type) — minimal change

```typescript
// BEFORE: parent builds cards + passes cardClickHandlers map
{
  id: "get-started",
  type: "action-cards",
  cards: actionCards,
  onCardClick: p.cardClickHandlers,  // 7 handlers from parent
  toolbar: getStartedToolbar,        // parent-built JSX
}

// AFTER: section factory builds everything internally
const getStartedRegistration: SectionRegistration = {
  id: "get-started",
  label: "Get Started",
  description: "Quick action cards for common tasks",
  icon: RocketRegular,
  category: "overview",
  factory: (ctx) => ({
    id: "get-started",
    type: "action-cards",
    title: "Get Started",
    cards: ACTION_CARD_CONFIGS,
    onCardClick: {
      "create-new-matter": () => ctx.onOpenWizard("sprk_creatematterwizard"),
      "create-new-project": () => ctx.onOpenWizard("sprk_createprojectwizard"),
      "summarize-new-files": () => ctx.onOpenWizard("sprk_summarizefileswizard"),
      // ... each card wires itself using ctx helpers
    },
    toolbar: (
      <Button appearance="subtle" size="small" icon={<OpenRegular />}
        onClick={() => ctx.onOpenWizard("sprk_playbooklibrary")}
        aria-label="Open Playbook Library" />
    ),
    maxVisible: 4,
    style: { minHeight: "auto" },
  }),
};
```

**Migration effort**: Small — move `getStartedConfig` handlers into factory.

#### Quick Summary (content type) — minimal change

```typescript
const quickSummaryRegistration: SectionRegistration = {
  id: "quick-summary",
  label: "Quick Summary",
  description: "Key metrics with trend badges",
  icon: DataBarVerticalRegular,
  category: "overview",
  factory: (ctx) => ({
    id: "quick-summary",
    type: "content",
    title: "Quick Summary",
    toolbar: /* refresh + dashboard buttons using ctx.onNavigate */,
    style: { minHeight: "auto" },
    renderContent: () => (
      <QuickSummaryRow webApi={ctx.webApi} userId={ctx.userId} />
    ),
  }),
};
```

**Migration effort**: Minimal — already self-contained.

#### Latest Updates (content type) — medium refactor

```typescript
const latestUpdatesRegistration: SectionRegistration = {
  id: "latest-updates",
  label: "Latest Updates",
  description: "Activity feed with filters",
  icon: ClockRegular,
  category: "data",
  defaultHeight: "325px",
  factory: (ctx) => ({
    id: "latest-updates",
    type: "content",
    title: "Latest Updates",
    style: { minHeight: "325px" },
    renderContent: () => (
      <ActivityFeed
        embedded webApi={ctx.webApi} userId={ctx.userId}
        textOnlyFilter gridLayout hideOverflowMenu
        onCountChange={ctx.onBadgeCountChange}
        onRefetchReady={ctx.onRefetchReady}
        onOpenAll={() => ctx.onNavigate({ type: "view", entity: "sprk_event" })}
        onCreateNew={() => ctx.onOpenWizard("sprk_createeventwizard")}
      />
    ),
  }),
};
```

**Migration effort**: Medium — `onOpenAll` and `onCreateNew` were parent handlers; now use context helpers.

#### My To Do List + My Documents — same pattern as Latest Updates

Both follow the same migration: move toolbar construction and handlers into the factory, use `ctx.onBadgeCountChange` and `ctx.onRefetchReady`.

**Migration effort**: Medium each.

### Summary: Current Section Readiness

| Section | Ready for Registry? | Migration Effort | What Changes |
|---------|-------------------|------------------|--------------|
| Get Started | 80% | Small | Move card click handlers into factory |
| Quick Summary | 90% | Minimal | Wire toolbar buttons to context |
| Latest Updates | 70% | Medium | Move open/create handlers into factory |
| My To Do List | 70% | Medium | Move toolbar + handlers into factory |
| My Documents | 70% | Medium | Move toolbar + handlers into factory |

**Total migration**: ~1-2 tasks to refactor all 5 sections into registry-compatible factories.

---

## Building New Workspace Sections

### Section Author Guide (R1: Product-Built Only)

To add a new workspace section:

#### 1. Create the section component

Build a React component in `@spaarke/ui-components` (or solution-specific if not reusable). It must:

- Accept `webApi`, `userId`, and any other props from `SectionFactoryContext`
- Call `onCountChange(n)` to update the badge count in the section header
- Call `onRefetchReady(fn)` to register a refresh function
- Handle its own data fetching and error states
- Use Fluent UI v9 exclusively (ADR-021)
- Support dark mode via semantic tokens

```typescript
// Example: RecentAnalyses component
interface RecentAnalysesProps {
  webApi: IWebApi;
  userId: string;
  bffBaseUrl: string;
  onCountChange: (count: number) => void;
  onRefetchReady: (refetch: () => void) => void;
}

export const RecentAnalyses: React.FC<RecentAnalysesProps> = (props) => {
  const { data, refetch } = useRecentAnalyses(props.webApi, props.userId);

  React.useEffect(() => { props.onRefetchReady(refetch); }, [refetch]);
  React.useEffect(() => { props.onCountChange(data?.length ?? 0); }, [data]);

  return <AnalysisList items={data} />;
};
```

#### 2. Create the section registration

```typescript
// sections/recentAnalyses.registration.ts
import { SectionRegistration } from "@spaarke/ui-components";
import { BrainCircuitRegular } from "@fluentui/react-icons";

export const recentAnalysesRegistration: SectionRegistration = {
  id: "recent-analyses",
  label: "Recent Analyses",
  description: "Your latest AI analysis results",
  icon: BrainCircuitRegular,
  category: "ai",
  defaultHeight: "400px",
  factory: (ctx) => ({
    id: "recent-analyses",
    type: "content",
    title: "Recent Analyses",
    toolbar: (
      <Button appearance="subtle" size="small" icon={<ArrowClockwiseRegular />}
        onClick={() => { /* refetch triggered via context */ }}
        aria-label="Refresh analyses" />
    ),
    style: { minHeight: "400px" },
    renderContent: () => (
      <RecentAnalyses
        webApi={ctx.webApi}
        userId={ctx.userId}
        bffBaseUrl={ctx.bffBaseUrl}
        onCountChange={ctx.onBadgeCountChange}
        onRefetchReady={ctx.onRefetchReady}
      />
    ),
  }),
};
```

#### 3. Register in the Section Registry

```typescript
// sectionRegistry.ts
import { getStartedRegistration } from "./sections/getStarted.registration";
import { quickSummaryRegistration } from "./sections/quickSummary.registration";
import { latestUpdatesRegistration } from "./sections/latestUpdates.registration";
import { todoRegistration } from "./sections/todo.registration";
import { documentsRegistration } from "./sections/documents.registration";
import { recentAnalysesRegistration } from "./sections/recentAnalyses.registration";

export const SECTION_REGISTRY: SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  recentAnalysesRegistration,  // ← new section, one line
];
```

**That's it.** The wizard automatically picks up the new section in Step 2. Users can add it to their workspace. No changes to WorkspaceShell, WorkspaceGrid, or workspaceConfig needed.

### Section Checklist

Before registering a new section, verify:

- [ ] Uses Fluent UI v9 only (ADR-021)
- [ ] Supports dark mode (semantic tokens, no hardcoded colors)
- [ ] Calls `onCountChange` for badge updates
- [ ] Calls `onRefetchReady` to support global refresh
- [ ] Handles loading states (spinner or skeleton)
- [ ] Handles empty states (meaningful message, not blank)
- [ ] Handles error states (inline error, not crash)
- [ ] Works at various sizes (section may be full-width or half-width)
- [ ] Uses only `SectionFactoryContext` dependencies (no bespoke parent wiring)
- [ ] Has `defaultHeight` set if content needs a fixed height (e.g., Kanban board)

---

## Section Registry

### Initial Registry Entries

| ID | Label | Category | Source Component |
|----|-------|----------|-----------------|
| `get-started` | Get Started | overview | ActionCardRow (7 cards) |
| `quick-summary` | Quick Summary | overview | QuickSummaryRow (4 metrics) |
| `latest-updates` | Latest Updates | data | ActivityFeed |
| `todo` | My To Do List | productivity | SmartToDo (Kanban) |
| `documents` | My Documents | data | DocumentsTab |

### Future Registry Entries (not in R1 scope)

| ID | Label | Category | Notes |
|----|-------|----------|-------|
| `sprkchat` | AI Assistant | ai | Post workspace+SprkChat integration |
| `calendar` | Calendar | productivity | CalendarSidePane adapted |
| `kpi-charts` | KPI Dashboard | overview | Chart visualizations |
| `recent-analyses` | Recent Analyses | ai | Analysis history list |
| `assignments` | Work Assignments | data | Filtered assignment grid |

---

## Layout Wizard (Code Page)

### Wizard Details

| Property | Value |
|----------|-------|
| Web resource | `sprk_workspacelayoutwizard` |
| Framework | React 19, Vite, single-file build (ADR-026) |
| Launch | `Xrm.Navigation.navigateTo` from workspace ⚙️ button |
| Dialog size | 70% width × 80% height |
| Shared components | `WizardDialog` from `@spaarke/ui-components` |

### Step 1: Choose Layout

```
┌─────────────────────────────────────────────────────┐
│ Configure Workspace                          Step 1/3│
│                                                     │
│ Choose a layout template:                           │
│                                                     │
│ ┌─────────┐  ┌─────────┐  ┌─────────┐             │
│ │ ┌──┬──┐ │  │ ┌──┬──┐ │  │ ┌─┬───┐ │             │
│ │ ├──┴──┤ │  │ ├──┼──┤ │  │ ├─┤   │ │             │
│ │ ├──┬──┤ │  │ └──┴──┘ │  │ ├─┤   │ │             │
│ │ └──┴──┘ │  │         │  │ └─┴───┘ │             │
│ │3-Row Mix│  │ 2×2 Grid│  │ Sidebar │             │
│ │  ✓ (sel)│  │         │  │ + Main  │             │
│ └─────────┘  └─────────┘  └─────────┘             │
│                                                     │
│ ┌─────────┐  ┌─────────┐  ┌─────────┐             │
│ │ ┌─────┐ │  │ ┌──┬──┬─┐│  │ ┌─────┐ │             │
│ │ ├─────┤ │  │ ├──┼──┼─┤│  │ ├──┬──┤ │             │
│ │ ├─────┤ │  │ └──┴──┴─┘│  │ ├──┼──┤ │             │
│ │ └─────┘ │  │          │  │ ├──┼──┤ │             │
│ │ Single  │  │ 3-Column │  │ Hero + │             │
│ │ Column  │  │          │  │ Grid   │             │
│ └─────────┘  └─────────┘  └─────────┘             │
│                                                     │
│                               [Cancel]  [Next →]    │
└─────────────────────────────────────────────────────┘
```

### Step 2: Select Sections

```
┌─────────────────────────────────────────────────────┐
│ Configure Workspace                          Step 2/3│
│                                                     │
│ Select sections to include:                         │
│                                                     │
│ Overview                                            │
│ ☑ Get Started — Quick action cards for common tasks │
│ ☑ Quick Summary — Key metrics with trend badges     │
│                                                     │
│ Data                                                │
│ ☑ Latest Updates — Activity feed with filters       │
│ ☑ My Documents — Recent documents list              │
│                                                     │
│ Productivity                                        │
│ ☑ My To Do List — Kanban board for tasks            │
│                                                     │
│ AI                                                  │
│ ☐ AI Assistant — SprkChat panel (coming soon)       │
│                                                     │
│ Selected: 5 sections │ Layout slots: 5              │
│                                                     │
│                          [← Back]  [Cancel]  [Next]  │
└─────────────────────────────────────────────────────┘
```

### Step 3: Arrange & Name

```
┌─────────────────────────────────────────────────────┐
│ Configure Workspace                          Step 3/3│
│                                                     │
│ Workspace name: [My Legal Dashboard          ]      │
│ ☑ Set as my default workspace                       │
│                                                     │
│ Drag sections to arrange:                           │
│                                                     │
│ ┌───────────────────┐  ┌───────────────────┐       │
│ │ 📋 Get Started    │  │ 📊 Quick Summary  │       │
│ │ (drag to reorder) │  │ (drag to reorder) │       │
│ └───────────────────┘  └───────────────────┘       │
│ ┌──────────────────────────────────────────┐       │
│ │ 🕐 Latest Updates                        │       │
│ │ (drag to reorder)                        │       │
│ └──────────────────────────────────────────┘       │
│ ┌───────────────────┐  ┌───────────────────┐       │
│ │ ✅ My To Do List  │  │ 📄 My Documents   │       │
│ │ (drag to reorder) │  │ (drag to reorder) │       │
│ └───────────────────┘  └───────────────────┘       │
│                                                     │
│                          [← Back]  [Cancel]  [Save]  │
└─────────────────────────────────────────────────────┘
```

Drag-and-drop: sections can be moved between slots within the chosen layout template. The grid structure (row count, column ratios) is fixed by the template; the user controls **which section goes in which slot**.

---

## BFF API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/workspace/layouts` | List user's workspace layouts |
| `GET` | `/api/workspace/layouts/{id}` | Get specific layout |
| `GET` | `/api/workspace/layouts/default` | Get user's default layout (or system default) |
| `POST` | `/api/workspace/layouts` | Create new layout |
| `PUT` | `/api/workspace/layouts/{id}` | Update layout (name, sections, default) |
| `DELETE` | `/api/workspace/layouts/{id}` | Delete layout |
| `GET` | `/api/workspace/sections` | List available sections from registry (for wizard) |
| `GET` | `/api/workspace/templates` | List available layout templates (for wizard) |

All endpoints use OBO auth, scoped to the current user's records.

---

## Workspace Header Component

```typescript
interface WorkspaceHeaderProps {
  activeLayout: WorkspaceLayoutDto;
  layouts: WorkspaceLayoutDto[];
  onLayoutChange: (layoutId: string) => void;
  onEditClick: () => void;
  onCreateClick: () => void;
}
```

Renders:
- Layout name as dropdown (Fluent `Dropdown`)
- Divider
- ⚙️ Edit button (opens wizard with current layout pre-filled)
- ➕ New button (opens wizard blank)

---

## Relationship to Grid Configuration

**Separate concerns — do not merge.**

| System | What It Configures | Storage |
|--------|-------------------|---------|
| `sprk_gridconfiguration` | Which **data columns and rows** appear in a grid | FetchXML + layout XML |
| `sprk_workspacelayout` | Which **sections** appear on the page and where | Section IDs + grid positions |

A workspace section may *contain* a grid that uses grid configuration. They're complementary layers:

```
Workspace Layout (sprk_workspacelayout)
  └── Section: "documents" (position: row 3, col 2)
        └── DocumentsTab component
              └── Uses UniversalDatasetGrid
                    └── Grid Configuration (sprk_gridconfiguration)
                          └── Columns, FetchXML, filters
```

---

## Scope

### In Scope (R1)
- `sprk_workspacelayout` Dataverse entity
- BFF CRUD endpoints for layouts
- Section Registry with 5 initial entries
- Layout Wizard Code Page (3-step)
- Workspace Header with dropdown + settings
- Dynamic config builder (merge JSON + registry → WorkspaceConfig)
- 6 predefined layout templates
- Default workspace per user
- Drag-and-drop arrangement in wizard Step 3

### Out of Scope (R2+)
- Org-wide default layouts (admin-defined)
- Section-level configuration (e.g., "show 6 vs 10 documents")
- Custom sections (user-defined content)
- Layout sharing between users
- Role-based default layouts
- Mobile-specific layouts
- Dashboard analytics/reporting sections

---

## Technical Constraints

### Applicable ADRs
- **ADR-006**: Layout Wizard is a Code Page (React 19, Vite single-file)
- **ADR-012**: Sections remain in `@spaarke/ui-components`; workspace shell is shared
- **ADR-021**: Fluent UI v9 exclusively; semantic tokens; dark mode support
- **ADR-026**: Code Page build standard (Vite + vite-plugin-singlefile)
- **ADR-001**: BFF API endpoints (Minimal API pattern)
- **ADR-008**: Endpoint authorization via filters

### MUST Rules
- MUST NOT serialize React components or JSX to Dataverse — store only IDs and positions
- MUST gracefully handle missing section IDs (section removed from registry after user saved layout)
- MUST fall back to system default layout when user has no saved layouts
- MUST enforce single default per user (clear previous default on save)
- MUST support dark mode in wizard and workspace header
- MUST use environment variables for BFF base URL (BYOK support)

---

## Success Criteria

1. [ ] User can create a named workspace with a chosen layout and selected sections
2. [ ] User can switch between saved workspaces via header dropdown
3. [ ] User can set a default workspace that loads automatically
4. [ ] User can drag-and-drop sections within the layout in the wizard
5. [ ] Workspace renders correctly using dynamic config from Dataverse
6. [ ] Existing LegalWorkspace layout is preserved as system default (no regression)
7. [ ] New sections can be added by registering in the Section Registry (no other changes needed)

---

## Dependencies

### Prerequisites
- WorkspaceShell shared component (exists)
- WizardDialog shared component (exists)
- BFF API infrastructure (exists)

### External
- None — this is a self-contained feature using existing infrastructure

---

*Last updated: March 28, 2026*
