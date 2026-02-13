# Project Plan: Matter Performance Assessment - R1 MVP

> **Last Updated**: 2026-02-12
> **Status**: Complete
> **Spec**: [spec-r1.md](spec-r1.md)
> **Full Solution**: [plan-full.md](plan-full.md) (future reference)

---

## 1. Executive Summary

**Purpose**: Build R1 MVP for manual KPI assessment entry with automated grade calculation and visualization. Users enter KPI assessments via Quick Create form, grades are automatically calculated via API, and displayed via VisualHost metric cards on the main tab and trend cards on the Report Card tab.

**Scope**: Manual entry only (no automation, no assessment generation, no AI). Focus on core visualization and calculation.

**Timeline**: 1 sprint (3-4 days) | **Estimated Effort**: 22-29 tasks

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (applicable to R1):
- **ADR-001**: Minimal API pattern for calculator endpoint
- **ADR-008**: Endpoint filters for authorization
- **ADR-019**: ProblemDetails for API errors
- **ADR-021**: Fluent UI v9 for all UI components (no hard-coded colors, dark mode)

**From R1 Spec**:
- No Dataverse plugins (use JavaScript web resource trigger)
- No Power Automate (use web resource trigger)
- No background jobs (immediate calculation on save)
- No AI integration (manual entry only)
- No assessment generation (user-initiated only)

**Simplifications from Full Solution**:
- ❌ Removed: Assessment infrastructure (15-20 tasks saved)
- ❌ Removed: System-calculated inputs (5-7 tasks saved)
- ❌ Removed: AI integration (5-8 tasks saved)
- ❌ Removed: Scheduled rollup jobs (8-10 tasks saved)
- ❌ Removed: Organization/person rollups (8-10 tasks saved)
- **Total Saved**: ~41-55 tasks from full solution

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Web Resource Trigger | No plugins/Power Automate available | JavaScript OnSave event calls API |
| Manual Entry Only | MVP validation before automation | Quick Create form, no custom PCF panel |
| Dual Grades (Current + Average) | Show both latest and historical performance | 6 fields on Matter (not 3) |
| Last 5 Updates for Trend | Time-agnostic, consistent data points | Sparkline shows last 5 assessments |
| Linear Regression for Trend | Simple, interpretable trend direction | ↑ ↓ → indicators |
| VisualHost Cards | Leverage existing component library | May need new card type (research task) |

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Data Model (Day 1)
└─ Create KPI Assessment entity, extend Matter, configure Quick Create form

Phase 2: Calculator API (Day 1-2)
└─ Build API endpoint, calculation logic, web resource trigger

Phase 3: Main Tab - VisualHost Cards (Day 2)
└─ 3 Report Card metric cards with color coding and contextual text

Phase 4: Report Card Tab (Day 2-3)
└─ 3 trend cards with sparkline, subgrid, linear regression logic

Phase 5: VisualHost Enhancement (Day 3)
└─ Research + implement new/modified card type

Phase 6: Testing & Polish (Day 3-4)
└─ Unit tests, integration tests, performance validation, UI polish
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs entity schema)
- Phase 3 BLOCKED BY Phase 2 (needs calculator API functional)
- Phase 4 BLOCKED BY Phase 2 (needs API to return trend data)
- Phase 5 can run PARALLEL to Phase 4 (independent VisualHost work)

**High-Risk Items:**
- VisualHost card type may need significant customization (unknown until research phase)
- Web resource error handling must be robust (API failures should not block form save)
- Sparkline rendering performance with many data points

---

## 4. Phase Breakdown

### Phase 1: Data Model (5-6 tasks)

**Objectives:**
1. Create KPI Assessment entity in Dataverse
2. Extend Matter entity with 6 grade fields
3. Configure Quick Create form for KPI Assessment
4. Populate choice fields (Performance Area, Grade)

**Deliverables:**
- [ ] `sprk_kpiassessment` entity created with all fields
- [ ] 6 grade fields added to `sprk_matter` entity
- [ ] Quick Create form configured with 5 fields (Performance Area, KPI Name, Criteria, Grade, Notes)
- [ ] Performance Area choice populated: Guidelines, Budget, Outcomes
- [ ] Grade choice populated: A+ (1.00) through No Grade (0.00)
- [ ] KPI Name populated (hardcoded or choice field - 6-10 common KPIs)

**Tasks:**
1. Create `sprk_kpiassessment` entity with schema
2. Add 6 decimal fields to `sprk_matter` entity (current + average × 3 areas)
3. Configure Quick Create form layout
4. Populate Performance Area choice field
5. Populate Grade choice field with numeric values
6. (Optional) Create KPI Name choice field or catalog entity

**Inputs**: `spec-r1.md` (FR-01, FR-02, FR-03)

**Outputs**: Dataverse entities and form ready for use

**Acceptance Criteria**:
User can open Quick Create form, see all 5 fields, and save a KPI assessment record

---

### Phase 2: Calculator API (5-6 tasks)

**Objectives:**
1. Build API endpoint for grade recalculation
2. Implement current grade logic (latest assessment)
3. Implement historical average logic (mean of all)
4. Build web resource trigger (JavaScript)
5. Add error handling and retry logic

**Deliverables:**
- [ ] API endpoint: `POST /api/matters/{matterId}/recalculate-grades`
- [ ] Current grade calculation: Query latest assessment per area, update matter fields
- [ ] Historical average calculation: Query all assessments per area, calculate mean, update matter fields
- [ ] Web resource: `sprk_kpiassessment_quickcreate.js` with OnSave event handler
- [ ] API response includes trend data (for sparkline)
- [ ] Error handling: Retry 3× on failure, log to console, show user dialog
- [ ] Performance: API responds in < 500ms

**Tasks:**
1. Create `ScorecardCalculatorEndpoints.cs` with POST endpoint
2. Implement current grade calculation logic (latest assessment query)
3. Implement historical average calculation logic (AVG query)
4. Build trend data response (query last 5 assessments per area)
5. Create `sprk_kpiassessment_quickcreate.js` web resource
6. Add error handling and retry logic to web resource

**Inputs**: Phase 1 complete (entity schema), `spec-r1.md` (FR-04 to FR-07)

**Outputs**: Functional calculator API + web resource trigger

**Acceptance Criteria**:
User saves KPI assessment → API called → matter grades updated → parent form refreshes automatically

---

### Phase 3: Main Tab - VisualHost Cards (4-5 tasks)

**Objectives:**
1. Create 3 VisualHost Report Card metric cards
2. Implement color coding (blue/yellow/red)
3. Add contextual text template
4. Configure cards on matter main form tab

**Deliverables:**
- [ ] Guidelines card: Icon, letter grade, color coding, contextual text
- [ ] Budget card: Icon, letter grade, color coding, contextual text
- [ ] Outcomes card: Icon, letter grade, color coding, contextual text
- [ ] Color rules: Blue (0.85-1.00), Yellow (0.70-0.84), Red (0.00-0.69)
- [ ] Contextual text: "You have an X% in [Area] compliance"
- [ ] Dark mode compatible (Fluent UI v9 design tokens)

**Tasks:**
1. Create Guidelines metric card component
2. Create Budget metric card component
3. Create Outcomes metric card component
4. Implement color coding logic (grade → color mapping)
5. Add contextual text template substitution

**Inputs**: Phase 2 complete (grades populated), `spec-r1.md` (FR-08)

**Outputs**: 3 VisualHost cards rendering on matter main form

**Acceptance Criteria**:
Main form displays 3 cards with correct grades, color coding, and contextual text; dark mode works

---

### Phase 4: Report Card Tab (6-8 tasks)

**Objectives:**
1. Create 3 trend cards with historical averages
2. Implement sparkline graphs (last 5 updates)
3. Calculate trend direction via linear regression
4. Configure subgrid for KPI assessments
5. Add "+ Add KPI" button

**Deliverables:**
- [ ] 3 trend cards above subgrid (Guidelines, Budget, Outcomes)
- [ ] Each card shows: Historical average, trend indicator (↑ ↓ →), sparkline graph
- [ ] Sparkline: Query last 5 assessments per area, render as mini line graph
- [ ] Linear regression: Calculate slope, determine trend direction
- [ ] Subgrid: All KPI assessments for matter (sorted by Created On DESC)
- [ ] "+ Add KPI" button: Launches Quick Create form

**Tasks:**
1. Create trend card component (reusable for 3 areas)
2. Implement sparkline graph rendering (using charting library)
3. Implement linear regression calculation (client-side or API?)
4. Configure Guidelines trend card
5. Configure Budget trend card
6. Configure Outcomes trend card
7. Configure KPI assessments subgrid
8. Add "+ Add KPI" button to subgrid ribbon

**Inputs**: Phase 2 complete (trend data API), `spec-r1.md` (FR-09, FR-10)

**Outputs**: Report Card tab with 3 trend cards + subgrid

**Acceptance Criteria**:
Report Card tab shows historical averages, sparkline graphs (last 5 updates), trend indicators, and subgrid with all assessments

---

### Phase 5: VisualHost Enhancement (2-4 tasks)

**Objectives:**
1. Research existing VisualHost card types
2. Determine if new card type needed or modify existing
3. Implement Report Card metric card type
4. Document card type for future use

**Deliverables:**
- [ ] Research report: Analysis of existing metric card types
- [ ] Decision: Extend existing or create new card type
- [ ] Implementation: Report Card metric card component
- [ ] Documentation: Card type API and configuration guide

**Tasks:**
1. Research existing VisualHost metric card implementations
2. Design Report Card metric card component (if new type needed)
3. Implement Report Card metric card (or extend existing)
4. Document card type configuration and usage

**Inputs**: Phase 3 requirements, VisualHost component library

**Outputs**: Report Card metric card type ready for use

**Acceptance Criteria**:
Report Card metric cards render with all specified features (icon, grade, color, contextual text, dark mode)

---

### Phase 6: Testing & Polish (4-6 tasks)

**Objectives:**
1. Unit tests for calculator logic
2. Integration test for end-to-end flow
3. Performance validation (API < 500ms, subgrid < 2s)
4. UI/UX polish (responsive design, accessibility)
5. Error scenario testing (API failures, network issues)

**Deliverables:**
- [ ] Unit tests: Current grade calculation, historical average, linear regression
- [ ] Integration test: Add KPI → grades update → UI refreshes
- [ ] Performance test: 100 concurrent API requests < 500ms
- [ ] Performance test: Subgrid with 100 assessments loads < 2s
- [ ] Error handling test: API failure → user-friendly dialog, form still saves
- [ ] Accessibility test: Keyboard navigation, WCAG 2.1 AA compliance
- [ ] Dark mode test: All components render correctly in dark theme

**Tasks:**
1. Write unit tests for calculator endpoint
2. Write unit tests for linear regression logic
3. Write integration test (end-to-end flow)
4. Perform load testing (API performance)
5. Test error scenarios (API failures, network issues)
6. Validate accessibility (keyboard, screen reader, color contrast)

**Inputs**: All phases complete

**Outputs**: Test suite passing, performance targets met

**Acceptance Criteria**:
- All unit tests pass (calculator, trend logic)
- Integration test passes (end-to-end)
- Performance targets met (API < 500ms, subgrid < 2s)
- Error handling works (graceful degradation)
- Accessibility validated (WCAG 2.1 AA)

---

## 5. Dependencies

### External Dependencies
| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft Dataverse | GA | Low | Standard platform |
| VisualHost Component Library | Internal | Medium | May need enhancement (Phase 5) |

### Internal Dependencies
| Dependency | Location | Status |
|------------|----------|--------|
| BFF API Infrastructure | `src/server/api/Sprk.Bff.Api/` | Production |
| VisualHost Module | `src/client/shared/visual-host/` | Production |
| Fluent UI v9 Components | `@spaarke/ui-components` | Production |

---

## 6. Testing Strategy

**Unit Tests** (60% coverage target):
- Calculator logic: Current grade, historical average
- Linear regression: Trend direction calculation
- Web resource: Error handling, retry logic

**Integration Tests**:
- End-to-end flow: Add KPI → API called → grades updated → UI refreshed
- Subgrid refresh: Verify new assessment appears immediately

**Performance Tests**:
- API load test: 100 concurrent requests (target: < 500ms p95)
- Subgrid load test: 100 assessments (target: < 2s load time)

**Manual UI Tests**:
- Quick Create form usability (target: < 30 seconds to complete)
- VisualHost cards rendering (color coding, contextual text, dark mode)
- Trend cards + sparkline visualization

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] User can open "+ Add KPI" and see Quick Create form with 5 fields
- [ ] User can select Performance Area, KPI Name, Grade from dropdowns
- [ ] User can enter Assessment Notes and save record

**Phase 2:**
- [ ] Save KPI assessment → API called automatically
- [ ] Matter's 6 grade fields updated correctly (current + average)
- [ ] Parent form refreshes, new grades visible immediately
- [ ] API response time < 500ms

**Phase 3:**
- [ ] Main tab shows 3 Report Card metric cards
- [ ] Cards display correct letter grades with color coding (blue/yellow/red)
- [ ] Contextual text accurate: "You have an X% in [Area] compliance"
- [ ] Dark mode works (no hard-coded colors)

**Phase 4:**
- [ ] Report Card tab shows 3 trend cards with historical averages
- [ ] Sparkline graphs display last 5 assessments
- [ ] Trend indicators (↑ ↓ →) calculated via linear regression
- [ ] Subgrid shows all KPI assessments, sorted newest first

**Phase 5:**
- [ ] VisualHost Report Card metric card type functional
- [ ] Cards support icon, grade, color coding, contextual text

**Phase 6:**
- [ ] All unit tests pass (calculator, trend logic)
- [ ] Integration test passes (end-to-end flow)
- [ ] Performance targets met (API < 500ms, subgrid < 2s)
- [ ] Error handling works (API failure → user dialog, form saves)
- [ ] Accessibility validated (WCAG 2.1 AA)

### Business Acceptance

- [ ] User can add KPI assessment in < 30 seconds
- [ ] Grades visible immediately after save (no manual refresh)
- [ ] Historical trends understandable at a glance (sparkline + indicator)
- [ ] Visual design consistent with Spaarke design system

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | VisualHost card type requires significant customization | Medium | Medium | Research phase (Phase 5) determines scope early |
| R2 | Web resource API call fails silently | Low | High | Robust error handling with user dialog and logging |
| R3 | Calculator API slow at scale | Low | Medium | Performance testing in Phase 6, add caching if needed |
| R4 | Sparkline library adds bundle size | Low | Low | Evaluate lightweight charting libraries (Victory, Recharts) |
| R5 | Linear regression logic incorrect | Low | Medium | Unit tests validate trend calculation accuracy |

---

## 9. Next Steps

1. **Review this plan-r1.md** with team
2. **Run** `/task-create matter-performance-KPI-r1` to generate task files (22-29 tasks)
3. **Begin** Phase 1 implementation (Data Model)

---

**Status**: Ready for Tasks
**Next Action**: Run `/task-create` to decompose phases into executable task files

---

## 10. Future Enhancements (R2+)

This R1 MVP establishes the foundation for future enhancements:

**R2: Assessment Generation Infrastructure**
- Automated triggers (invoice approval, matter status change)
- Outlook adaptive card delivery
- In-app assessment PCF panel

**R3: System-Calculated Inputs**
- Auto-production from invoice data
- Data resolver framework
- Integration with Financial Intelligence module

**R4: AI-Derived Inputs**
- Playbook integration
- AI evaluation for specific KPIs
- Provenance tracking

**R5: Organization/Person Rollups**
- Firm-level aggregation
- Attorney-level aggregation
- Portfolio analytics

See [plan-full.md](plan-full.md) for complete future roadmap.

---

*For Claude Code: This plan provides R1 MVP implementation context. Load relevant sections when executing tasks.*
