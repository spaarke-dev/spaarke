# Visualization Framework R2 - Implementation Plan

> **Status**: Ready for Implementation
> **Created**: 2026-02-08
> **Source**: spec.md

---

## Architecture Context

### Discovered Resources

**Applicable ADRs:**
- ADR-006: PCF over webresources (all UI must be PCF)
- ADR-012: Shared component library (reusable components in `@spaarke/ui-components`)
- ADR-021: Fluent UI v9 design system (tokens, dark mode, WCAG 2.1 AA)
- ADR-022: PCF platform libraries (React 16 APIs only)

**Relevant Skills:**
- dataverse-deploy - For PCF and solution deployment
- code-review, adr-check - Quality gates during implementation

**Existing Patterns:**
- VisualHost PCF v1.1.17 - React 16 render pattern
- CardView component - Card layout with Fluent UI v9
- ConfigurationLoader - Dataverse WebAPI abstraction

**Available Scripts:**
- `Deploy-PCFWebResources.ps1` - Deploy PCF controls
- `Deploy-CustomPage.ps1` - Deploy custom pages

---

## Phase Breakdown

### Phase 1: Schema Changes (4-6 hrs)

**Objective:** Add new fields to `sprk_chartdefinition` entity in Dataverse

**Deliverables:**
1. Create click action fields:
   - `sprk_onclickaction` (Choice)
   - `sprk_onclicktarget` (Text 200)
   - `sprk_onclickrecordfield` (Text 100)
2. Create visual configuration fields:
   - `sprk_contextfieldname` (Text 100)
   - `sprk_viewlisttabname` (Text 100)
   - `sprk_maxdisplayitems` (Whole Number, default 10)
3. Add option set values to `sprk_visualtype`:
   - DueDateCard = 100000008
   - DueDateCardList = 100000009
4. Export and commit solution changes

**Dependencies:** None

**Risks:** Schema changes require Dataverse admin access

---

### Phase 2: Click Action Handler (6-8 hrs)

**Objective:** Implement configuration-driven click actions in VisualHostRoot

**Deliverables:**
1. Extend IChartDefinition interface with new fields
2. Update ConfigurationLoader to fetch new fields
3. Create ClickActionHandler service:
   - `none` - No action
   - `openrecordform` - Open record's main form
   - `opensidepane` - Open side pane with Custom Page
   - `navigatetopage` - Navigate to URL or Custom Page
   - `opendatasetgrid` - Open drill-through workspace
4. Wire click handler to ChartRenderer
5. Unit tests for ClickActionHandler

**Dependencies:** Phase 1 (schema fields exist)

**Risks:** Xrm.Navigation API availability in different contexts

---

### Phase 3: EventDueDateCard Shared Component (4-6 hrs)

**Objective:** Create reusable EventDueDateCard component in shared library

**Deliverables:**
1. Create EventDueDateCard component:
   - Props interface (generic, no PCF dependencies)
   - Date column with event type color
   - Event type + name header
   - Description text
   - Assigned to field
   - Days-until-due badge (color-coded)
2. Styling with Fluent UI v9 tokens
3. Dark mode support
4. Unit tests (90%+ coverage)
5. Storybook stories
6. Export from `@spaarke/ui-components`

**Dependencies:** None (can run parallel to Phase 1-2)

**Risks:** Event type color mapping accuracy

---

### Phase 4: Due Date Card Visual Types (6-8 hrs)

**Objective:** Add duedatecard and duedatecardlist visual types to VisualHost

**Deliverables:**
1. DueDateCard component (single card bound to lookup)
2. DueDateCardList component (card list from view)
3. Update ChartRenderer routing
4. View-driven data fetching with context filter
5. "View List" navigation link
6. Integration tests

**Dependencies:** Phase 2 (click handler), Phase 3 (shared component)

**Risks:** View FetchXML injection complexity

---

### Phase 5: Advanced Query Support (4-6 hrs)

**Objective:** Support custom FetchXML queries with parameter substitution

**Deliverables:**
1. Create ViewDataService:
   - Retrieve view FetchXML
   - Inject context filters
   - Execute and map results
2. Parameter substitution engine:
   - `{contextRecordId}` - Current record ID
   - `{currentUserId}` - Current user ID
   - `{currentDate}` - Today's date
   - `{currentDateTime}` - Current timestamp
3. Query resolution priority:
   - PCF override → Custom FetchXML → View → Direct entity
4. Add `fetchXmlOverride` PCF property
5. Unit tests for parameter substitution

**Dependencies:** Phase 4 (visual types exist)

**Risks:** FetchXML syntax edge cases

---

### Phase 6: Testing & Deployment (4-6 hrs)

**Objective:** Complete testing and deploy to development environment

**Deliverables:**
1. End-to-end testing in Dataverse:
   - Click action configuration for all types
   - Single due date card visual
   - Card list with view filtering
   - Context filtering on related forms
   - "View List" navigation
   - Custom FetchXML execution
   - Dark mode verification
2. Deploy VisualHost v1.2.0
3. Update Chart Definition records
4. Update DueDateWidget to use VisualHost (migration)
5. Documentation updates

**Dependencies:** All prior phases complete

**Risks:** Production deployment coordination

---

## Critical Path

```
Phase 1 (Schema) ─────┐
                      ├──► Phase 2 (Click Handler) ──► Phase 4 (Visual Types) ──► Phase 5 (Query) ──► Phase 6 (Deploy)
Phase 3 (Component) ──┘
```

**Parallel Opportunities:**
- Phase 1 and Phase 3 can run in parallel
- Phase 3 can complete before Phase 2 finishes

---

## Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| React 18 API usage | Medium | High | Code review against ADR-022 |
| Schema migration issues | Low | Medium | Test in sandbox first |
| View FetchXML complexity | Medium | Medium | Start with simple views, add complexity |
| Dark mode styling | Medium | Low | Use tokens exclusively, test early |

---

## Success Criteria

Per spec.md:

1. [ ] Click action configuration works for all visual types
2. [ ] Single due date card visual displays correctly
3. [ ] Card list visual fetches from view
4. [ ] Context filtering works
5. [ ] "View List" link navigates correctly
6. [ ] Custom FetchXML executes
7. [ ] PCF override takes precedence
8. [ ] Parameter substitution works
9. [ ] Dark mode support
10. [ ] Backward compatibility maintained

---

## References

- [spec.md](spec.md) - Full specification
- [design.md](design.md) - Original design document
- [VisualHost Source](../../src/client/pcf/VisualHost/)
- [Shared Components](../../src/client/shared/Spaarke.UI.Components/)
- [ADR-022 (React 16)](../../.claude/adr/ADR-022-pcf-platform-libraries.md)

---

*Generated: 2026-02-08*
