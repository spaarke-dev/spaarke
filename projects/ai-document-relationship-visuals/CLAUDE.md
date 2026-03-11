# CLAUDE.md - AI Document Relationship Visuals

> **Status**: In Progress
> **Priority**: High
> **Created**: 2026-03-10

---

## MANDATORY: Task Execution Protocol

**When working on tasks in this project, Claude Code MUST invoke the `task-execute` skill.**

DO NOT read POML files directly and implement manually. The task-execute skill ensures:
- Knowledge files are loaded (ADRs, constraints, patterns)
- Context is properly tracked in current-task.md
- Quality gates run (code-review + adr-check)
- Progress is recoverable after compaction

**Trigger phrases**: "work on task X", "continue", "next task", "resume task X"

---

## Project Context

This project enhances the DocumentRelationshipViewer Code Page and establishes a standardized graph visualization pattern for the Spaarke platform:

1. **RelatedDocumentCount PCF** — Count card on Document Main Form with drill-through to viewer
2. **Shared `useForceSimulation` hook** — Sync d3-force pre-computation (hub-spoke + peer-mesh)
3. **Shared `RelationshipCountCard`** — Callback-based count display component
4. **BFF API `countOnly` parameter** — Fast path for count-only requests (~50-200ms)
5. **Code Page enhancements** — CSV export, quick search, graph migration to shared hook

### Standardization Decision

All document relationship graphs will use `@xyflow/react` v12 for rendering with `d3-force` v3 synchronous pre-computation for layout. This eliminates the "Calculating layout..." spinner.

---

## Applicable ADRs

| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| [ADR-001](../../.claude/adr/ADR-001.md) | Minimal API + BackgroundService | BFF API uses Minimal API; return ProblemDetails on errors |
| [ADR-006](../../.claude/adr/ADR-006.md) | PCF over webresources | PCF for form-bound count card; Code Page for viewer dialog |
| [ADR-008](../../.claude/adr/ADR-008.md) | Endpoint filters | Use endpoint filters for auth, not global middleware |
| [ADR-010](../../.claude/adr/ADR-010.md) | DI minimalism | Register concretes; ≤15 non-framework DI lines |
| [ADR-012](../../.claude/adr/ADR-012.md) | Shared component library | Shared components in `@spaarke/ui-components`; callback props; 90%+ coverage |
| [ADR-021](../../.claude/adr/ADR-021.md) | Fluent UI v9 | Design tokens; dark mode required; WCAG 2.1 AA |
| [ADR-022](../../.claude/adr/ADR-022.md) | PCF Platform Libraries | PCF: React 16 `ReactDOM.render()`; Code Page: React 19 `createRoot()` |

### Key Constraints

```
MUST use React 16 APIs in PCF (ReactDOM.render(), unmountComponentAtNode())
MUST use React 19 createRoot() in Code Page (bundled)
MUST use Fluent UI v9 design tokens (no hard-coded colors)
MUST support light and dark themes on all surfaces
MUST use callback-based props in shared components (zero service deps)
MUST use deep imports in PCF: @spaarke/ui-components/dist/components/{Name}
MUST use only useMemo in shared hooks (no useTransition, useDeferredValue)
MUST use endpoint filters for BFF API authorization (not global middleware)
MUST return ProblemDetails for all API errors
MUST achieve 90%+ test coverage on shared components

MUST NOT use React 18+ APIs in PCF or shared hooks
MUST NOT bundle React in PCF output
MUST NOT hard-code entity names in shared components
MUST NOT use Fluent UI v8 or alternative UI libraries
MUST NOT use global middleware for resource authorization
```

---

## File Locations

### Primary Implementation Areas

```
src/client/pcf/RelatedDocumentCount/           # NEW PCF control
├── RelatedDocumentCount/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                               # PCF entry point (ReactControl)
│   ├── RelatedDocumentCount.tsx               # Main component
│   ├── hooks/useRelatedDocumentCount.ts       # API call (countOnly=true)
│   └── types/index.ts

src/client/shared/Spaarke.UI.Components/       # Shared library
├── src/components/
│   └── RelationshipCountCard/                 # NEW shared component
│       ├── RelationshipCountCard.tsx
│       ├── RelationshipCountCard.test.tsx
│       └── index.ts
├── src/hooks/
│   ├── useForceSimulation.ts                  # NEW shared hook
│   └── useForceSimulation.test.ts
└── src/index.ts                               # Barrel export

src/client/code-pages/DocumentRelationshipViewer/  # MODIFIED Code Page
├── src/
│   ├── App.tsx                                # Add Export button, quick search
│   ├── components/DocumentGraph.tsx           # Migrate to useForceSimulation
│   ├── components/RelationshipGrid.tsx        # Add search filter, export support
│   └── services/CsvExportService.ts           # NEW export service

src/server/api/Sprk.Bff.Api/                   # BFF API
└── (visualization endpoint handler)           # Add countOnly parameter
```

### Existing Patterns to Follow

| Pattern | Location | Use For |
|---------|----------|---------|
| FindSimilarDialog | `src/client/shared/.../FindSimilarDialog/` | Callback-based shared component |
| SemanticSearch sync simulation | `src/client/code-pages/SemanticSearch/src/components/SearchResultsMap.tsx` | `sim.tick(300)` pattern |
| Current useForceLayout | `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` | Async pattern to replace |
| PCF ReactControl | `src/client/pcf/SemanticSearchControl/` | PCF with shared components |

---

## Decisions Made

| Decision | Rationale | Date |
|----------|-----------|------|
| @xyflow/react + sync d3-force as standard | Rich React nodes, instant layout, proven pattern | 2026-03-10 |
| countOnly API fast path | Separate count (~50-200ms) from full graph (~300-800ms) | 2026-03-10 |
| Shared hook in ui-components | Reusable; React 16 compatible via useMemo only | 2026-03-10 |
| SemanticSearch migration deferred | Future project; this establishes the shared hook | 2026-03-10 |
| PCF for count card, Code Page for viewer | ADR-006: form-bound vs standalone dialog | 2026-03-10 |

---

## Quick Commands

```bash
# Build shared library
cd src/client/shared/Spaarke.UI.Components && npm run build

# Build Code Page
cd src/client/code-pages/DocumentRelationshipViewer && npm run build

# Build RelatedDocumentCount PCF
cd src/client/pcf/RelatedDocumentCount && npm run build

# Run shared lib tests
cd src/client/shared/Spaarke.UI.Components && npm test

# Build BFF API
dotnet build src/server/api/Sprk.Bff.Api/

# Run BFF API tests
dotnet test tests/unit/Sprk.Bff.Api.Tests/
```

---

## Dependencies

### Blocked By
- None — this project can start immediately

### Blocks
- SemanticSearch @xyflow migration (future project) — will adopt `useForceSimulation` hook

---

*Last Updated: 2026-03-10*
