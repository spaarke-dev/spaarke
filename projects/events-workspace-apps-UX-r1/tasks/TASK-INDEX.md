# Task Index - Events Workspace Apps UX R1

> **Last Updated**: 2026-02-04
> **Total Tasks**: 67
> **Status**: 🔲 = Pending, 🔄 = In Progress, ✅ = Complete, ⏸️ = Blocked

---

## Execution Configuration

```bash
# Run with skip-permissions for uninterrupted execution
claude --dangerously-skip-permissions

# Or set in .claude/settings.json:
# "permissions": { "allow": ["Bash(*)", "Edit(*)", "Write(*)", ...] }
```

---

## Parallel Execution Groups

| Group | Tasks | Prerequisites | Notes |
|-------|-------|---------------|-------|
| **A** | 001-009 + 020-025 | None | Phase 1 (Calendar) + Phase 3 (EventTypeService) - NO file conflicts |
| **B** | 010-019 | Group A complete | Phase 2 (Grid) - depends on calendar filter format |
| **C** | 030-044 | Task 025 complete | Phase 4 (Side Pane) - depends on EventTypeService |
| **D** | 050-058 + 060-068 | Task 044 complete | Phase 5 (Widget) + Phase 6 (Page) - NO file conflicts |
| **E** | 070-079 | Groups A-D complete | Phase 7 (Integration) |

**To run parallel tasks**: Send single message with multiple Task tool invocations.

---

## Phase 1: EventCalendarFilter PCF (Foundation)

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 001 | Scaffold EventCalendarFilter PCF control | None | Group A |
| 🔲 | 002 | Implement multi-month vertical stack calendar | 001 | Group A |
| 🔲 | 003 | Add single date selection | 002 | Group A |
| 🔲 | 004 | Add range selection (Shift+click) | 003 | Group A |
| 🔲 | 005 | Add event indicators (dots on dates) | 002 | Group A |
| 🔲 | 006 | Implement filter output JSON format | 003, 004 | Group A |
| 🔲 | 007 | Add dark mode and theme support | 002 | Group A |
| 🔲 | 008 | Add unit tests | 006 | Group A |
| 🔲 | 009 | Add Storybook stories and deploy Phase 1 | 008 | Group A |

---

## Phase 2: UniversalDatasetGrid Enhancement

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 010 | Add calendar filter input property | 006 | Group B |
| 🔲 | 011 | Implement date filtering on dataset | 010 | Group B |
| 🔲 | 012 | Add bi-directional sync (row → calendar) | 011 | Group B |
| 🔲 | 013 | Add hyperlink column with side pane action | 010 | Group B |
| 🔲 | 014 | Add checkbox column for bulk actions | 010 | Group B |
| 🔲 | 015 | Implement optimistic row update callback | 013 | Group B |
| 🔲 | 016 | Add column/field filters | 010 | Group B |
| 🔲 | 017 | Match Power Apps grid styling exactly | 011, 016 | Group B |
| 🔲 | 018 | Add unit tests for grid enhancements | 017 | Group B |
| 🔲 | 019 | Deploy and test Phase 2 | 018 | Group B |

---

## Phase 3: EventTypeService Extraction

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 020 | Create EventTypeService in shared library | None | Group A |
| 🔲 | 021 | Implement getEventTypeFieldConfig interface | 020 | Group A |
| 🔲 | 022 | Add sprk_fieldconfigjson parsing | 021 | Group A |
| 🔲 | 023 | Add unit tests for EventTypeService | 022 | Group A |
| 🔲 | 024 | Update EventFormController to use shared service | 023 | Group A |
| 🔲 | 025 | Verify EventFormController still works | 024 | Group A |

---

## Phase 4: EventDetailSidePane Custom Page

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 030 | Scaffold EventDetailSidePane Custom Page | 025 | Group C |
| 🔲 | 031 | Implement side pane opening via Xrm.App.sidePanes | 030 | Group C |
| 🔲 | 032 | Create header section (name, type, parent link) | 031 | Group C |
| 🔲 | 033 | Create status section with segmented buttons | 031 | Group C |
| 🔲 | 034 | Create key fields section (Due Date, Priority, Owner) | 031 | Group C |
| 🔲 | 035 | Create collapsible Dates section | 031 | Group C |
| 🔲 | 036 | Create collapsible Related Event section | 031 | Group C |
| 🔲 | 037 | Create collapsible Description section | 031 | Group C |
| 🔲 | 038 | Create collapsible History section | 031 | Group C |
| 🔲 | 039 | Integrate EventTypeService for field visibility | 032-038, 025 | Group C |
| 🔲 | 040 | Implement save via WebAPI | 034 | Group C |
| 🔲 | 041 | Add optimistic UI with error rollback | 040 | Group C |
| 🔲 | 042 | Add security role awareness (read-only mode) | 041 | Group C |
| 🔲 | 043 | Add unsaved changes prompt | 041 | Group C |
| 🔲 | 044 | Add dark mode support and deploy Phase 4 | 042, 043 | Group C |

---

## Phase 5: DueDatesWidget PCF

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 050 | Scaffold DueDatesWidget PCF control | 044 | Group D |
| 🔲 | 051 | Implement filter logic (actionable events) | 050 | Group D |
| 🔲 | 052 | Implement list layout (per mockup) | 050 | Group D |
| 🔲 | 053 | Implement event type badges + days-until-due indicator | 052 | Group D |
| 🔲 | 054 | Implement click card → Events tab + Side Pane | 052, 044 | Group D |
| 🔲 | 055 | Add "All Events" link | 052 | Group D |
| 🔲 | 056 | Add dark mode support | 053 | Group D |
| 🔲 | 057 | Add unit tests | 056 | Group D |
| 🔲 | 058 | Add Storybook stories and deploy Phase 5 | 057 | Group D |

---

## Phase 6: Events Custom Page (System-Level)

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 060 | Scaffold Events Custom Page | 044 | Group D |
| 🔲 | 061 | Integrate Calendar + Grid components | 060, 009, 019 | Group D |
| 🔲 | 062 | Add "Regarding" column with parent link | 061 | Group D |
| 🔲 | 063 | Add Assigned To filter | 061 | Group D |
| 🔲 | 064 | Add Record Type filter | 061 | Group D |
| 🔲 | 065 | Add Status filter | 061 | Group D |
| 🔲 | 066 | Add Date Range filter | 061 | Group D |
| 🔲 | 067 | Configure sitemap (replace OOB Events view) | 066 | Group D |
| 🔲 | 068 | Add dark mode support and deploy Phase 6 | 067 | Group D |

---

## Phase 7: Integration & Testing

| Status | Task | Title | Dependencies | Parallel |
|--------|------|-------|--------------|----------|
| 🔲 | 070 | Form integration testing (Matter/Project) | 058, 068 | Group E |
| 🔲 | 071 | Custom Page integration testing | 068 | Group E |
| 🔲 | 072 | Cross-browser testing (Edge, Chrome) | 070, 071 | Group E |
| 🔲 | 073 | Dark mode verification (all components) | 070, 071 | Group E |
| 🔲 | 074 | Performance testing (calendar query < 500ms) | 070, 071 | Group E |
| 🔲 | 075 | Accessibility audit (WCAG 2.1 AA) | 073 | Group E |
| 🔲 | 076 | Final deployment to dev environment | 072, 073, 074, 075 | Group E |
| 🔲 | 077 | UAT support + **Verify PLACEHOLDER-TRACKER.md is empty** | 076 | Group E |
| 🔲 | 078 | Project wrap-up and documentation | 077 | Group E |

---

## Critical Path

```
001 → 002 → 003 → 006 → 010 → 011 → 017 → 019
                              ↓
020 → 021 → 022 → 023 → 024 → 025 → 030 → 031 → 039 → 044
                                                      ↓
                                          050/060 → 070 → 079
```

**Longest path**: ~35 sequential tasks (with parallelization: ~25 effective)

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: EventCalendarFilter | 001-009 (9) | 🔲 0/9 |
| Phase 2: Grid Enhancement | 010-019 (10) | 🔲 0/10 |
| Phase 3: EventTypeService | 020-025 (6) | 🔲 0/6 |
| Phase 4: Side Pane | 030-044 (15) | 🔲 0/15 |
| Phase 5: DueDatesWidget | 050-058 (9) | 🔲 0/9 |
| Phase 6: Events Page | 060-068 (9) | 🔲 0/9 |
| Phase 7: Integration | 070-078 (9) | 🔲 0/9 |
| **Total** | **67** | **🔲 0/67** |

---

*Updated by task-execute skill during execution*
