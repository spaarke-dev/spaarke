# Current Task State - Visualization Framework R2

> **Purpose**: Track active task state for context recovery after compaction

---

## Active Task

**Task ID:** 044
**Task File:** tasks/044-query-support-tests.poml (not yet created)
**Title:** Unit tests for query support features
**Phase:** 5
**Status:** not-started
**Started:** —

---

## Quick Recovery

If resuming after compaction or new session:

1. Read this file for current state
2. Read `tasks/TASK-INDEX.md` for task statuses
3. Read `CLAUDE.md` for project context
4. Say "continue" to resume

**Completed through Phase 5 (tasks 040-043)**. Phase 5 Advanced Query Support is complete. Task 044 (unit tests) is next.

---

## Completed Steps

- [x] Phase 1: Schema changes (tasks 001-003) - created manually in Dataverse
- [x] Task 010: Extended IChartDefinition interface with new fields + OnClickAction enum
- [x] Task 011: Updated ConfigurationLoader to fetch and map all new fields
- [x] Task 012: Created ClickActionHandler service
- [x] Task 013: Wired click handler to ChartRenderer and VisualHostRoot
- [x] Task 020: Created EventDueDateCard shared component with Fluent v9 tokens
- [x] Task 021: Styling with dark mode support (included in 020)
- [x] Task 023: Exported component from @spaarke/ui-components
- [x] Task 030: Created DueDateCardVisual (single card visual)
- [x] Task 031: Created DueDateCardListVisual (card list visual)
- [x] Task 032: Updated ChartRenderer with new visual type routing
- [x] Task 033: Implemented "View List" navigation in VisualHostRoot
- [x] Task 040: Created ViewDataService (view FetchXML retrieval, context filter injection, event mapping)
- [x] Task 041: Implemented parameter substitution engine ({contextRecordId}, {currentUserId}, {currentDate}, {currentDateTime})
- [x] Task 042: Implemented query priority resolution (pcfOverride → customFetchXml → view → directEntity)
- [x] Task 043: Added fetchXmlOverride PCF property to ControlManifest + wired through VisualHostRoot → ChartRenderer → DueDateCardList

---

## Modified Files

| File | Purpose |
|------|---------|
| `src/client/pcf/VisualHost/control/types/index.ts` | Added VisualType.DueDateCard/List, OnClickAction enum, IChartDefinition new fields |
| `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts` | Added new FIELDS, SELECT_COLUMNS, parseOnClickAction, updated mapToChartDefinition |
| `src/client/pcf/VisualHost/control/services/ClickActionHandler.ts` | **NEW** - Click action service |
| `src/client/pcf/VisualHost/control/services/ViewDataService.ts` | **NEW** - View data service (FetchXML retrieval, context filter injection, parameter substitution, query priority resolution) |
| `src/client/pcf/VisualHost/control/components/DueDateCard.tsx` | **NEW** - Single card visual component |
| `src/client/pcf/VisualHost/control/components/DueDateCardList.tsx` | **NEW** - Card list visual with query priority resolution |
| `src/client/pcf/VisualHost/control/components/ChartRenderer.tsx` | Added DueDateCard/List cases, new props (webApi, onClickAction, fetchXmlOverride, etc.) |
| `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` | Added handleClickAction, handleViewListClick, fetchXmlOverride prop, passes new props to ChartRenderer |
| `src/client/pcf/VisualHost/control/ControlManifest.Input.xml` | Added fetchXmlOverride property |
| `src/client/shared/Spaarke.UI.Components/src/components/EventDueDateCard/EventDueDateCard.tsx` | **NEW** - Shared EventDueDateCard component |
| `src/client/shared/Spaarke.UI.Components/src/components/EventDueDateCard/index.ts` | **NEW** - Barrel export |
| `src/client/shared/Spaarke.UI.Components/src/components/index.ts` | Added EventDueDateCard export |

---

## Decisions Made

- Combined tasks 020/021/023: EventDueDateCard component created with full Fluent v9 styling and dark mode in single pass
- Combined tasks 030/031/032/033: DueDateCard visuals, ChartRenderer routing, and View List navigation implemented together
- Combined tasks 040/041/042: ViewDataService, parameter substitution, and query priority resolution all in ViewDataService.ts
- Task 004 (solution export) deferred - schema changes done manually, export can happen anytime via PAC CLI
- DueDateCard visuals fetch their own data via WebAPI (different pattern from chart data aggregation)
- FetchXML context filter injection uses string manipulation (not DOMParser) for PCF environment compatibility
- Query priority resolution: pcfOverride → customFetchXml → view → directEntity

---

## Next Actions

- Task 044: Unit tests for query support features (optional - POML not created)
- Task 004 (solution export) still pending - can be done anytime
- Phase 6: Testing & deployment (tasks 050-052, 090)

---

*Last Updated: 2026-02-08*
