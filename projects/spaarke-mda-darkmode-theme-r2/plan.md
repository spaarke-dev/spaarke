# Implementation Plan — Dark Mode Theme R2

## Architecture Context

### Discovered Resources

| Type | Resource | Purpose |
|------|----------|---------|
| ADR | ADR-021 | Fluent v9, dark mode, semantic tokens |
| ADR | ADR-012 | Shared component library protocol |
| ADR | ADR-006 | Web resource minimalism |
| ADR | ADR-022 | PCF platform libraries (React 16) |
| Pattern | `.claude/patterns/pcf/theme-management.md` | PCF theme listener pattern |
| Skill | `ribbon-edit` | Ribbon XML editing procedure |
| Skill | `dataverse-deploy` | Solution deployment |
| Constraint | `.claude/constraints/pcf.md` | PCF control constraints |
| Constraint | `.claude/constraints/webresource.md` | Web resource constraints |
| Constraint | `.claude/constraints/react-versioning.md` | React version split rules |

### Existing Code to Reuse

| Component | Location | Reuse |
|-----------|----------|-------|
| `getUserPreference` / `setUserPreference` | `src/solutions/LegalWorkspace/src/services/DataverseService.ts:545-618` | Dataverse theme persistence pattern |
| Per-entity ribbon XML | `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` | Template for new entity ribbons |
| Theme listener cleanup pattern | `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts` | Existing listener setup |

---

## Phase Breakdown

### Phase 1: Consolidate Theme Utilities (Core Fix)

**Goal**: Single authoritative theme module. No OS fallback. No duplicates.

| Task | Description | Parallel Group |
|------|-------------|----------------|
| 001 | Consolidate `codePageTheme.ts` into `themeStorage.ts` — add Code Page functions, remove OS fallback, delete `codePageTheme.ts` | — (serial, foundation) |
| 002 | Update barrel exports in shared library `index.ts` | — (serial, depends on 001) |
| 003 | Update unit tests — remove OS fallback expectations, add Code Page function tests | — (serial, depends on 001) |

### Phase 2: Migrate Consumers (Parallel — 3 groups)

**Goal**: All consumers import from unified `themeStorage.ts`. No inline duplicates.

| Task | Description | Parallel Group |
|------|-------------|----------------|
| 010 | Update 6 Code Page ThemeProvider wrappers (LegalWorkspace, EventsPage, CalendarSidePane, EventDetailSidePane, SpeAdminApp, WorkspaceLayoutWizard) | **Group A** |
| 011 | Update 11 Code Page App.tsx/main.tsx direct consumers (all wizard Code Pages + standalone pages) | **Group A** |
| 012 | Delete `useThemeDetection.ts` from AnalysisWorkspace and PlaybookBuilder; update their index.tsx | **Group A** |
| 013 | Replace SemanticSearch self-contained ThemeProvider with shared import | **Group A** |
| 014 | Replace inline theme code in UniversalQuickCreate (~160 lines) and EmailProcessingMonitor (~70 lines) with shared import | **Group B** |
| 015 | Replace VisualHost ThemeProvider.ts (~240 lines) with shared import | **Group B** |
| 016 | Remove OS listener from UniversalDatasetGrid, SemanticSearchControl, RelatedDocumentCount ThemeProviders/ThemeServices | **Group B** |
| 017 | Fix LegalWorkspace `useTheme.ts` — change storage key from `spaarke-workspace-theme` to `spaarke-theme`, remove OS fallback | **Group C** |
| 018 | Remove OS listener from `sprk_ThemeMenu.js` (lines 96-107) | **Group C** |
| 019 | Verify remaining PCF controls (AssociationResolver, UpdateRelatedButton, DrillThroughWorkspace, ScopeConfigEditor) use standard key | **Group C** |

### Phase 3: Ribbon Deployment

**Goal**: Theme flyout on every Spaarke entity form and grid.

| Task | Description | Parallel Group |
|------|-------------|----------------|
| 020 | Add theme flyout ribbon XML for 6 missing entities (sprk_workassignment, sprk_analysisplaybook, sprk_analysisoutput, sprk_communication, sprk_eventtodo, sprk_eventtype) | **Group D** |
| 021 | Add Form ribbon location for 3 existing entities (sprk_project, sprk_invoice, sprk_event) | **Group D** |
| 022 | Update "Auto (follows system)" label to "Auto (follows app)" in all ribbon XML | **Group D** |

### Phase 4: Dataverse Persistence

**Goal**: Cross-device theme sync via `sprk_userpreference`.

| Task | Description | Parallel Group |
|------|-------------|----------------|
| 030 | Add `ThemePreference` (100000001) to `sprk_preferencetype` option set in Dataverse | — (serial) |
| 031 | Add `syncThemeFromDataverse()` and `persistThemeToDataverse()` to unified `themeStorage.ts` | — (serial, depends on 030) |
| 032 | Wire Dataverse sync into `sprk_ThemeMenu.js` setTheme() and Code Page/PCF init flows | — (serial, depends on 031) |

### Phase 5: Protocol & Wrap-up

**Goal**: Document standards, verify, deploy.

| Task | Description | Parallel Group |
|------|-------------|----------------|
| 040 | Create `.claude/patterns/theme-consistency.md` — mandatory theme protocol for all new components | **Group E** |
| 041 | Integration testing — verify all surfaces render consistent theme | **Group E** |
| 042 | Deploy to dev environment — ribbon solutions, web resources, shared library | — (serial) |
| 090 | Project wrap-up — update README status, lessons learned, archive | — (serial) |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | File Scope | Notes |
|-------|-------|--------------|------------|-------|
| — | 001, 002, 003 | None | Shared library only | Serial — foundation changes |
| **A** | 010, 011, 012, 013 | 001-003 complete | Code Page solutions only | 4 agents: ThemeProviders, main.tsx files, useThemeDetection, SemanticSearch |
| **B** | 014, 015, 016 | 001-003 complete | PCF controls only | 3 agents: QuickCreate+EmailMonitor, VisualHost, other PCF ThemeServices |
| **C** | 017, 018, 019 | 001-003 complete | LegalWorkspace hook + web resource + PCF verify | 3 agents: useTheme fix, ThemeMenu.js, PCF audit |
| **D** | 020, 021, 022 | None (independent XML) | Ribbon XML only | 3 agents: new entities, Form locations, label update |
| **E** | 040, 041 | Phase 2-3 complete | Docs + testing | 2 agents: protocol doc, integration test |

**Maximum concurrency**: Groups A, B, C can run simultaneously (10 agents) after Phase 1.
Group D can run in parallel with everything (ribbon XML is independent).

---

## Critical Path

```
001 → 002 → 003 → [Groups A+B+C in parallel] → [Group E] → 042 → 090
                    [Group D in parallel with everything]
                    [030 → 031 → 032 can run alongside Groups A-C]
```

**Estimated total**: ~20 tasks, parallelizable to ~6 sequential steps with concurrent agents.
