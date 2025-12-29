# Project Plan: Spaarke Visuals Framework

> **Last Updated**: 2025-12-29
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build a configuration-driven visualization framework with Fluent v9 charting that provides operational, in-app visuals (cards, charts, calendars) with drill-through capabilities for Model-Driven Apps.

**Scope**:
- Visual Host PCF control (unified renderer for all visual types)
- 7 visual types using Fluent v9 charting
- Drill-through workspace with chart + dataset grid
- `sprk_chartdefinition` Dataverse entity for configuration
- Shared chart components in `@spaarke/ui-components`

**Estimated Effort**: 25-35 days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: MUST build new UI as PCF controls; MUST NOT create legacy webresources
- **ADR-011**: MUST use Dataset PCF for drill-through grid; MUST include Storybook stories
- **ADR-012**: MUST use `@spaarke/ui-components`; MUST NOT hard-code entity schemas
- **ADR-021**: MUST use `@fluentui/react-components` v9; MUST NOT use Fluent v8 or hard-coded colors

**From Spec**:
- MUST use `@fluentui/react-charting` for all chart visuals
- MUST support light, dark, and high-contrast modes
- MUST keep PCF bundle under 5MB
- MUST achieve 80%+ test coverage on PCF controls

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Single Visual Host PCF | Reduces deployment complexity, unified configuration | One control to maintain |
| Calendar may be separate | Not available in `@fluentui/react-charting` | May need Fluent v9 primitives |
| Drill-through as Custom Page | Opens as modal, maintains context | New Custom Page required |
| Theme from MDA app theme | Consistent with app appearance | Use fluentDesignLanguage context |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-006-pcf-over-webresources.md` - PCF control requirements
- `.claude/adr/ADR-011-dataset-pcf.md` - Dataset PCF patterns
- `.claude/adr/ADR-012-shared-components.md` - Shared library usage
- `.claude/adr/ADR-021-fluent-design-system.md` - Fluent v9 requirements

**Applicable Skills**:
- `.claude/skills/dataverse-deploy/` - Deploy PCF controls and solutions
- `.claude/skills/adr-aware/` - Proactive ADR compliance
- `.claude/skills/code-review/` - Quality validation
- `.claude/skills/spaarke-conventions/` - Naming and patterns

**Knowledge Articles**:
- `.claude/patterns/pcf/control-initialization.md` - PCF lifecycle pattern
- `.claude/patterns/pcf/theme-management.md` - Dark mode handling
- `.claude/patterns/pcf/dataverse-queries.md` - WebAPI patterns
- `.claude/patterns/pcf/dialog-patterns.md` - Modal/dialog patterns
- `src/client/pcf/CLAUDE.md` - PCF-specific instructions

**Reusable Code**:
- `src/client/pcf/UniversalDatasetGrid/` - Dataset PCF pattern
- `src/client/pcf/AnalysisWorkspace/` - Two-panel layout pattern
- `src/client/pcf/ThemeEnforcer/` - Theme management pattern

**Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` - PCF deployment
- `scripts/Deploy-CustomPage.ps1` - Custom Page deployment

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Infrastructure (Days 1-5)
└─ sprk_chartdefinition entity, project scaffolding, shared types

Phase 2: Core Chart Components (Days 6-15)
└─ Individual chart components (Bar, Line, Donut, etc.)

Phase 3: Visual Host PCF (Days 16-20)
└─ Unified PCF control, configuration binding, theme integration

Phase 4: Drill-Through Workspace (Days 21-28)
└─ Custom Page, interactive filtering, dataset integration

Phase 5: Testing & Documentation (Days 29-35)
└─ Unit tests, Storybook, integration testing, deployment
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (need types and entity schema)
- Phase 3 BLOCKED BY Phase 2 (need chart components)
- Phase 4 BLOCKED BY Phase 3 (need Visual Host PCF)

**High-Risk Items:**
- Calendar component not in Fluent charting - Mitigation: Build with Fluent v9 primitives
- Bundle size constraint - Mitigation: Use platform-library, monitor size throughout

---

## 4. Phase Breakdown

### Phase 1: Foundation & Infrastructure (Days 1-5)

**Objectives:**
1. Create Dataverse entity schema for `sprk_chartdefinition`
2. Establish shared TypeScript types and interfaces
3. Set up PCF control scaffolding
4. Configure build pipeline and Storybook

**Deliverables:**
- [ ] `sprk_chartdefinition` entity deployed to Dataverse
- [ ] Shared types package (`types/ChartDefinition.ts`, `types/DrillInteraction.ts`)
- [ ] Visual Host PCF project scaffold (`src/client/pcf/VisualHost/`)
- [ ] Storybook configuration for chart components
- [ ] `sprk_optionsjson` schema defined per visual type

**Critical Tasks:**
- Define entity schema first - all other work depends on this

**Inputs**: spec.md, existing PCF patterns

**Outputs**: Dataverse solution, TypeScript types, PCF scaffold

---

### Phase 2: Core Chart Components (Days 6-15)

**Objectives:**
1. Implement each chart type as a standalone React component
2. Integrate with `@fluentui/react-charting`
3. Support theme tokens and dark mode
4. Implement drill interaction callbacks

**Deliverables:**
- [ ] `MetricCard` component
- [ ] `BarChart` component (horizontal + vertical)
- [ ] `LineChart` component (with Area variant)
- [ ] `DonutChart` component
- [ ] `StatusDistributionBar` component
- [ ] `CalendarVisual` component (Fluent v9 primitives)
- [ ] `MiniTable` component (Top-N list)
- [ ] Storybook stories for all components

**Critical Tasks:**
- Verify `@fluentui/react-charting` supports all chart types before starting
- Calendar component likely requires custom implementation

**Inputs**: Shared types, Fluent v9 packages, Storybook

**Outputs**: Reusable chart components, Storybook documentation

---

### Phase 3: Visual Host PCF Control (Days 16-20)

**Objectives:**
1. Create unified Visual Host PCF that renders any visual type
2. Load configuration from `sprk_chartdefinition`
3. Implement data fetching via Dataverse WebAPI
4. Integrate theme management (MDA app theme + dark mode)

**Deliverables:**
- [ ] Visual Host PCF control (`src/client/pcf/VisualHost/`)
- [ ] Configuration loader service
- [ ] Data aggregation service (count, sum, avg, etc.)
- [ ] Theme provider integration
- [ ] Expand/View Details toolbar button
- [ ] PCF manifest with platform-library declarations

**Critical Tasks:**
- Theme resolution must follow established pattern (MDA theme → dark mode)
- Bundle size must stay under 5MB

**Inputs**: Chart components, `sprk_chartdefinition` entity, PCF patterns

**Outputs**: Deployable PCF control

---

### Phase 4: Drill-Through Workspace (Days 21-28)

**Objectives:**
1. Create Custom Page for drill-through workspace
2. Implement chart + dataset side-by-side layout
3. Add interactive filtering (chart selection → grid filter)
4. Support reset/clear selection action

**Deliverables:**
- [ ] Drill-Through Custom Page (`DrillThroughWorkspace.html`)
- [ ] Two-panel layout component (1/3 chart, 2/3 grid)
- [ ] Filter state context provider
- [ ] `DrillInteraction` contract implementation
- [ ] Dataset grid with dynamic filtering
- [ ] Reset/clear selection action

**Critical Tasks:**
- Custom Page must open as modal from Visual Host
- Filter state must be shared between chart and grid

**Inputs**: Visual Host PCF, Dataset PCF pattern, Custom Page scripts

**Outputs**: Complete drill-through experience

---

### Phase 5: Testing & Documentation (Days 29-35)

**Objectives:**
1. Achieve 80%+ test coverage on PCF controls
2. Complete Storybook documentation
3. Integration testing with Dataverse
4. Deploy to development environment

**Deliverables:**
- [ ] Unit tests for all chart components
- [ ] Unit tests for Visual Host PCF
- [ ] Integration tests for Dataverse queries
- [ ] Complete Storybook documentation
- [ ] PCF control deployed to dev environment
- [ ] Custom Page deployed
- [ ] Admin documentation for chart creation

**Critical Tasks:**
- Test with all 6 supported entities
- Verify dark mode works in all scenarios

**Inputs**: All components, test harness

**Outputs**: Tested, documented, deployed solution

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| `@fluentui/react-charting` | Available (npm) | Low | v5.x stable |
| `@fluentui/react-components` | Available (npm) | Low | v9.x stable |
| Power Platform PCF framework | GA | Low | Well-documented |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `@spaarke/ui-components` | `src/client/shared/` | Production |
| Existing PCF patterns | `src/client/pcf/UniversalDatasetGrid/` | Production |
| Theme management pattern | `.claude/patterns/pcf/theme-management.md` | Documented |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- Chart component rendering
- Configuration parsing
- Data aggregation logic
- DrillInteraction handler
- Theme resolution

**Integration Tests**:
- Dataverse WebAPI queries
- `sprk_chartdefinition` CRUD operations
- PCF lifecycle (init, updateView, destroy)

**E2E Tests**:
- Place Visual Host on form, verify renders
- Click chart element, verify drill-through opens
- Select segment, verify grid filters

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `sprk_chartdefinition` entity can be created/updated via model-driven app
- [ ] TypeScript compiles without errors

**Phase 2:**
- [ ] All 7 chart components render correctly in Storybook
- [ ] Each component responds to theme changes

**Phase 3:**
- [ ] Visual Host PCF renders chart based on configuration
- [ ] Bundle size under 5MB

**Phase 4:**
- [ ] Drill-through opens and filters dataset
- [ ] Reset action clears filter

**Phase 5:**
- [ ] 80%+ test coverage achieved
- [ ] Storybook deployed and accessible

### Business Acceptance

- [ ] Admin can create chart definitions
- [ ] Users see visuals on forms/dashboards
- [ ] Drill-through allows record investigation
- [ ] Dark mode works consistently

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Calendar not in Fluent charting | High | Medium | Build with Fluent v9 primitives |
| R2 | Bundle size exceeds 5MB | Medium | High | platform-library, tree-shaking, monitoring |
| R3 | Theme inconsistency | Medium | Medium | Follow established theme-management pattern |
| R4 | Performance with large datasets | Medium | Medium | Virtualization, pagination, aggregation |
| R5 | Custom Page modal issues | Low | Medium | Test early, follow dialog-patterns |

---

## 9. Next Steps

1. **Review this plan** - Confirm scope and approach
2. **Run task decomposition** - `/task-create` to generate task files
3. **Begin Phase 1** - Start with `sprk_chartdefinition` entity

---

**Status**: Ready for Tasks
**Next Action**: Generate task files with `/task-create`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
