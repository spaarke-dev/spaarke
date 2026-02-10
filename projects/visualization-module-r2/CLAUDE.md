# CLAUDE.md - Visualization Framework R2

> **Status**: In Progress
> **Priority**: High
> **Created**: 2026-02-08

---

## 🚨 MANDATORY: Task Execution Protocol

**When working on tasks in this project, Claude Code MUST invoke the `task-execute` skill.**

DO NOT read POML files directly and implement manually. The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Quality gates run (code-review + adr-check)
- ✅ Progress is recoverable after compaction

**Trigger phrases**: "work on task X", "continue", "next task", "resume task X"

---

## Project Context

This project enhances the VisualHost PCF control to support:
1. **Configuration-driven click actions** for all visual types
2. **New visual types**: `duedatecard` (single) and `duedatecardlist` (list)
3. **EventDueDateCard** shared component in `@spaarke/ui-components`
4. **View-driven data fetching** with context filtering
5. **Custom FetchXML support** with parameter substitution

### Origin

This work originated from Events Workspace Apps UX R1 project's DueDateWidget requirements but is being implemented strategically as a framework enhancement to benefit all visualization use cases.

---

## Applicable ADRs

| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) | PCF over webresources | All UI must be PCF, not JS webresources |
| [ADR-012](../../.claude/adr/ADR-012-shared-components.md) | Shared component library | Reusable components go in `@spaarke/ui-components` |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 | Use design tokens, support dark mode, WCAG 2.1 AA |
| [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | PCF Platform Libraries | **React 16 APIs only** - use `ReactDOM.render()` |

### Key Constraints

```
✅ MUST use React 16 APIs (ReactDOM.render(), unmountComponentAtNode())
✅ MUST use Fluent UI v9 design tokens (no hard-coded colors)
✅ MUST support light and dark themes
✅ MUST place shared components in @spaarke/ui-components
✅ MUST maintain backward compatibility with existing VisualHost configurations

❌ MUST NOT use React 18 APIs (createRoot, concurrent features)
❌ MUST NOT hard-code entity names in shared components
❌ MUST NOT bundle React in PCF output
```

---

## File Locations

### Primary Implementation Areas

```
src/client/pcf/VisualHost/
├── control/
│   ├── index.ts                  # PCF entry point (React 16 pattern)
│   ├── components/
│   │   ├── VisualHostRoot.tsx    # Main component - add click action handler
│   │   ├── ChartRenderer.tsx     # Visual type routing - add due date cards
│   │   ├── DueDateCard.tsx       # NEW: Single card visual
│   │   └── DueDateCardList.tsx   # NEW: Card list visual
│   ├── services/
│   │   ├── ConfigurationLoader.ts # Extend for new fields
│   │   └── ViewDataService.ts     # NEW: View-driven data fetching
│   └── types/
│       └── index.ts               # IChartDefinition interface - extend

src/client/shared/Spaarke.UI.Components/
├── src/components/
│   └── EventDueDateCard/          # NEW: Shared component
│       ├── EventDueDateCard.tsx
│       ├── EventDueDateCard.test.tsx
│       └── index.ts
└── src/index.ts                   # Export new component
```

### Existing Patterns to Follow

| Pattern | Location | Use For |
|---------|----------|---------|
| CardView | `src/client/shared/.../DatasetGrid/CardView.tsx` | Card layout pattern |
| VisualHostRoot | `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` | Click handler pattern |
| ConfigurationLoader | `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` | Field loading pattern |

---

## Schema Changes (Dataverse)

### New Fields on `sprk_chartdefinition`

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_onclickaction` | Choice | Click action type (none, openrecordform, opensidepane, navigatetopage, opendatasetgrid) |
| `sprk_onclicktarget` | Text (200) | Target for click action |
| `sprk_onclickrecordfield` | Text (100) | Field containing record ID |
| `sprk_contextfieldname` | Text (100) | Lookup field for context filtering |
| `sprk_viewlisttabname` | Text (100) | Tab name for "View List" navigation |
| `sprk_maxdisplayitems` | Whole Number | Maximum items to display (default 10) |

### New Option Set Values

Add to `sprk_visualtype`:
- `DueDateCard` = 100000008
- `DueDateCardList` = 100000009

### New PCF Property

| Property | Type | Purpose |
|----------|------|---------|
| `fetchXmlOverride` | SingleLine.Text | Per-deployment FetchXML override |

---

## Decisions Made

| Decision | Rationale | Date |
|----------|-----------|------|
| Integrate into VisualHost | Reusable framework vs. standalone PCF | 2026-02-08 |
| React 16 for PCF | Platform constraint (ADR-022) | 2026-02-08 |
| Use existing FetchXML fields | `sprk_fetchxmlquery` and `sprk_fetchxmlparams` already exist | 2026-02-08 |
| Query priority: PCF → FetchXML → View → Entity | Clear precedence for data resolution | 2026-02-08 |

---

## Quick Commands

```bash
# Build VisualHost PCF
cd src/client/pcf/VisualHost && npm run build

# Run VisualHost tests
cd src/client/pcf/VisualHost && npm test

# Build shared components
cd src/client/shared/Spaarke.UI.Components && npm run build

# Deploy VisualHost to Dataverse
scripts/Deploy-PCFWebResources.ps1 -ControlName "VisualHost" -Environment "dev"
```

---

## Dependencies

### Blocked By
- None - this project can start immediately

### Blocks
- events-workspace-apps-UX-r1 → DueDateWidget visual refresh
- events-workspace-apps-UX-r1 → "View List" navigation

---

*Last Updated: 2026-02-08*
