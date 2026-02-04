# Events Workspace Apps UX R1 - Implementation Plan

> **Status**: Active
> **Created**: 2026-02-04
> **Last Updated**: 2026-02-04

---

## Architecture Context

### Discovered Resources

**Applicable ADRs:**
| ADR | Summary | Key Constraint |
|-----|---------|----------------|
| [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) | PCF over webresources | All UI as PCF controls |
| [ADR-011](.claude/adr/ADR-011-dataset-pcf.md) | Dataset PCF over subgrids | Use UniversalDatasetGrid |
| [ADR-012](.claude/adr/ADR-012-shared-components.md) | Shared component library | Import via `@spaarke/ui-components` |
| [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 | Use tokens, support dark mode |
| [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) | React 16 APIs | `ReactDOM.render`, platform-library |

**Relevant Patterns:**
| Pattern | Location | Usage |
|---------|----------|-------|
| PCF Initialization | `.claude/patterns/pcf/control-initialization.md` | React 16 lifecycle |
| Theme Management | `.claude/patterns/pcf/theme-management.md` | Dark mode support |
| Error Handling | `.claude/patterns/pcf/error-handling.md` | ErrorBoundary pattern |
| Dataverse Queries | `.claude/patterns/pcf/dataverse-queries.md` | WebAPI calls |
| Web API Client | `.claude/patterns/dataverse/web-api-client.md` | CRUD operations |

**Knowledge Docs:**
| Document | Location | Purpose |
|----------|----------|---------|
| PCF Deployment Guide | `docs/guides/PCF-DEPLOYMENT-GUIDE.md` | Full deployment workflow |
| PCF Module CLAUDE.md | `src/client/pcf/CLAUDE.md` | Module-specific patterns |

**Deployment Scripts:**
| Script | Purpose |
|--------|---------|
| `scripts/Deploy-PCFWebResources.ps1` | PCF web resource deployment |
| `scripts/Deploy-CustomPage.ps1` | Custom Page deployment |
| `scripts/query-pcf-controls.ps1` | Verify PCF controls in environment |

**Canonical Code Examples:**
| Code | Location | Reference For |
|------|----------|---------------|
| UniversalDatasetGrid | `src/client/pcf/UniversalDatasetGrid/` | Grid enhancement base |
| EventFormController | `src/client/pcf/EventFormController/` | Field visibility logic |
| VisualHost CalendarVisual | `src/client/pcf/VisualHost/control/components/CalendarVisual.tsx` | Calendar design (React 18, reference only) |
| Shared Components | `src/client/shared/Spaarke.UI.Components/` | EventTypeService location |

---

## Phase Breakdown

### Phase 1: EventCalendarFilter PCF (Foundation)

**Goal:** Create standalone calendar PCF control with date selection and event indicators.

**Deliverables:**
1. EventCalendarFilter PCF control scaffolding
2. Fluent UI v9 Calendar component integration
3. Date selection (single and range)
4. Event indicator display (dots on dates with events)
5. Filter output (JSON to form field or callback)
6. Dark mode support
7. Unit tests and Storybook stories

**Dependencies:** None (foundation component)

**Estimated Tasks:** 8-10

---

### Phase 2: UniversalDatasetGrid Enhancement

**Goal:** Extend existing grid with calendar filter awareness and side pane integration.

**Deliverables:**
1. Calendar filter input property
2. Date range filtering on dataset
3. Bi-directional sync (row click → calendar highlight)
4. Hyperlink column with side pane action
5. Checkbox column for bulk actions
6. Optimistic row update on save callback
7. Column/field filters
8. Match Power Apps grid styling

**Dependencies:** Phase 1 (calendar filter format defined)

**Estimated Tasks:** 10-12

---

### Phase 3: EventTypeService Extraction

**Goal:** Extract field visibility logic from EventFormController into shared service.

**Deliverables:**
1. EventTypeService in `@spaarke/ui-components/services/`
2. Interface: `getEventTypeFieldConfig(webApi, eventTypeId)`
3. Support for `sprk_fieldconfigjson` parsing
4. Field visibility, required fields, section defaults
5. Unit tests with mocked WebAPI
6. Update EventFormController to use shared service

**Dependencies:** None (can run parallel with Phase 2)

**Estimated Tasks:** 5-6

---

### Phase 4: EventDetailSidePane Custom Page

**Goal:** Create Custom Page for context-preserving Event editing.

**Deliverables:**
1. Custom Page scaffolding
2. Side pane opening via Xrm.App.sidePanes
3. Header section (name, type, parent link)
4. Status section with radio/segmented buttons
5. Key fields section (Due Date, Priority, Owner)
6. Collapsible sections (Dates, Related Event, Description, History)
7. Event Type-aware field visibility (using EventTypeService)
8. Save via WebAPI with optimistic UI
9. Security role awareness (read-only mode)
10. Unsaved changes prompt
11. Dark mode support

**Dependencies:** Phase 3 (EventTypeService)

**Estimated Tasks:** 12-15

---

### Phase 5: DueDatesWidget PCF

**Goal:** Create card-based widget for Overview tabs.

**Deliverables:**
1. DueDatesWidget PCF control scaffolding
2. Filter logic (actionable events, overdue, 7-day window)
3. Card layout (date, name, type, countdown badge)
4. Color-coded urgency badges
5. Click card → navigate to Events tab + open Side Pane
6. "All Events" link
7. Dark mode support
8. Unit tests and Storybook stories

**Dependencies:** Phase 4 (Side Pane ready for integration)

**Estimated Tasks:** 8-10

---

### Phase 6: Events Custom Page (System-Level)

**Goal:** Replace OOB Events entity main view with custom implementation.

**Deliverables:**
1. Events Custom Page scaffolding
2. Integrate Calendar + Grid + Side Pane components
3. "Regarding" column with parent record link
4. Filters: Assigned To, Record Type, Status, Date Range
5. React context for component communication
6. Sitemap configuration (replace OOB view)
7. Dark mode support

**Dependencies:** Phases 1, 2, 4 (all components)

**Estimated Tasks:** 8-10

---

### Phase 7: Integration & Testing

**Goal:** End-to-end testing and deployment.

**Deliverables:**
1. Form integration testing (Matter/Project forms)
2. Custom Page testing
3. Cross-browser testing (Edge, Chrome)
4. Dark mode verification
5. Performance testing (calendar query < 500ms)
6. Accessibility audit (WCAG 2.1 AA)
7. Deployment to dev environment
8. User acceptance testing support

**Dependencies:** All previous phases

**Estimated Tasks:** 6-8

---

## Task Summary

| Phase | Component | Est. Tasks |
|-------|-----------|------------|
| 1 | EventCalendarFilter PCF | 8-10 |
| 2 | UniversalDatasetGrid Enhancement | 10-12 |
| 3 | EventTypeService Extraction | 5-6 |
| 4 | EventDetailSidePane Custom Page | 12-15 |
| 5 | DueDatesWidget PCF | 8-10 |
| 6 | Events Custom Page | 8-10 |
| 7 | Integration & Testing | 6-8 |
| **Total** | | **57-71** |

---

## Parallel Execution Opportunities

| Group | Tasks | Can Run After |
|-------|-------|---------------|
| A | Phase 1 + Phase 3 | Project start (independent) |
| B | Phase 2 (partial) | Phase 1 filter format defined |
| C | Phase 4 | Phase 3 complete |
| D | Phase 5 + Phase 6 | Phase 4 complete (can run in parallel) |

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Fluent UI v9 Calendar limitations | Early POC in Phase 1; fallback to custom implementation if needed |
| React 16 compatibility issues | Follow ADR-022 strictly; test in Dataverse early |
| VisualHost React 18 code temptation | Build fresh; reference only for design patterns |
| Side pane state management | Use React context for form, props for Custom Page |
| Grid performance with large datasets | Use Dataverse paging (50 records default) |

---

## References

- [spec.md](spec.md) - Full specification
- [design.md](design.md) - Original design document
- [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) - PCF architecture
- [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) - Fluent UI v9
- [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) - React 16 APIs
- [PCF Deployment Guide](docs/guides/PCF-DEPLOYMENT-GUIDE.md) - Deployment workflow

---

*Generated by project-pipeline skill*
