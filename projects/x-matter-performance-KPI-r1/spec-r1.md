# Matter Performance Assessment - R1 MVP Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-12
> **Project**: matter-performance-KPI-r1
> **Scope**: R1 MVP - Manual KPI Entry + Basic Visualization

---

## Executive Summary

**R1 MVP** provides manual KPI assessment entry with automated grade calculation and visualization on matter records. Users can quickly assess matter performance across three areas (Guidelines, Budget, Outcomes) using a Quick Create form, and immediately see updated grades displayed via VisualHost metric cards on the main form and trend analysis on the Report Card tab.

**Key Characteristics**:
- **Manual Entry**: Users enter KPI assessments via Quick Create form (no automation)
- **Instant Visualization**: VisualHost metric cards show current grades on main tab
- **Trend Analysis**: Report Card tab shows historical averages and trend graphs (last 5 updates)
- **Automated Calculation**: JavaScript web resource triggers API to recalculate grades on save

---

## Scope

### In Scope (R1 MVP)

#### **Data Model**
- **1 new entity**: `sprk_kpiassessment` (KPI Assessment)
- **6 new fields on Matter**: 3 current grade fields + 3 historical average grade fields

#### **User Interface**
- **Quick Create Form**: Add KPI assessment with 5 fields (Performance Area, KPI Name, Criteria, Grade, Notes)
- **Main Tab - VisualHost Cards**: 3 Report Card metric cards showing current grades (color-coded: blue/yellow/red)
- **Report Card Tab**:
  - 3 trend cards with historical averages + sparkline graphs (last 5 updates)
  - 1 subgrid showing all KPI assessments for the matter

#### **Calculator Logic**
- **API Endpoint**: `POST /api/matters/{matterId}/recalculate-grades`
- **Calculation**:
  - **Current Grade**: Latest assessment grade (by `createdon DESC`)
  - **Historical Average**: Average of all assessment grades for the area
  - **Trend Direction**: Linear regression on last 5 updates (↑ ↓ →)

#### **Trigger Mechanism**
- **JavaScript Web Resource**: OnSave event handler on Quick Create form
- **Action**: Calls calculator API after KPI Assessment save completes
- **Effect**: Updates matter's 6 grade fields, refreshes parent form

#### **VisualHost Enhancement**
- **New or Modified Card Type**: Report Card metric card with:
  - Area icon and name
  - Large letter grade (A+, B, C, etc.)
  - Color coding (Blue A-B, Yellow C, Red D-F)
  - Contextual text: "You have an X% in [Area] compliance"

---

### Out of Scope (Future R2+)

- ❌ Automated system-calculated inputs (from invoice data)
- ❌ Assessment generation infrastructure (triggers, scheduled jobs)
- ❌ Outlook adaptive card delivery
- ❌ In-app assessment PCF panel (using standard Quick Create instead)
- ❌ AI-derived inputs (playbook integration)
- ❌ Organization/person rollups (firm-level, attorney-level aggregation)
- ❌ Scheduled batch processing (nightly rollups, monthly snapshots)
- ❌ Dataverse plugins (using web resource trigger instead)
- ❌ Power Automate flows
- ❌ Advanced formula evaluation
- ❌ KPI versioning
- ❌ Profile template synchronization

**Note**: Full solution specification retained in `spec-full.md` for future reference.

---

## Requirements

### Functional Requirements

#### **Data Model (FR-01 to FR-02)**

**FR-01: KPI Assessment Entity**
**Description**: Create `sprk_kpiassessment` entity to store manual KPI assessments.
**Acceptance**: Entity deployed with fields:
- `sprk_matter` (Lookup to Matter) - Parent matter
- `sprk_performancearea` (Choice) - Guidelines, Budget, Outcomes
- `sprk_kpiname` (Text or Choice) - KPI name (e.g., "Invoice Compliance")
- `sprk_assessmentcriteria` (Multiline Text) - Description of KPI assessment criteria
- `sprk_grade` (Choice) - A+ (1.00), A (0.95), B+ (0.90), B (0.85), C+ (0.80), C (0.75), D+ (0.70), D (0.65), F (0.60), No Grade (0.00)
- `sprk_assessmentnotes` (Multiline Text) - Narrative feedback
- `createdon` (DateTime) - Auto-populated

**FR-02: Matter Entity Extension**
**Description**: Add 6 grade fields to existing `sprk_matter` entity.
**Acceptance**: Fields deployed and accessible:
- `sprk_guidelinecompliancegrade_current` (Decimal) - Latest Guidelines assessment grade
- `sprk_guidelinecompliancegrade_average` (Decimal) - All-time average Guidelines grade
- `sprk_budgetcompliancegrade_current` (Decimal) - Latest Budget assessment grade
- `sprk_budgetcompliancegrade_average` (Decimal) - All-time average Budget grade
- `sprk_outcomecompliancegrade_current` (Decimal) - Latest Outcomes assessment grade
- `sprk_outcomecompliancegrade_average` (Decimal) - All-time average Outcomes grade

---

#### **Quick Create Form (FR-03)**

**FR-03: KPI Assessment Quick Create Form**
**Description**: Standard Dataverse Quick Create form for adding KPI assessments.
**Acceptance**: Form configured with 5 fields:
1. **Performance Area** (Choice dropdown): Guidelines, Budget, Outcomes
2. **KPI Assessment** (Choice or Text): Filtered list of KPI names based on selected area
3. **Assessment Criteria** (Multiline Text, read-only): Pre-populated description for selected KPI
4. **Grade** (Choice dropdown): A+ (1.00) through No Grade (0.00)
5. **Assessment Notes** (Multiline Text): User-entered narrative feedback

**Form Behavior**:
- Launched via "+ Add KPI" button on Report Card tab subgrid
- OnSave event triggers web resource to call calculator API
- Closes after save, parent form refreshes automatically

---

#### **Calculator API (FR-04 to FR-06)**

**FR-04: Recalculate Grades Endpoint**
**Description**: API endpoint to recalculate matter grades after new KPI assessment.
**Acceptance**:
- Endpoint: `POST /api/matters/{matterId}/recalculate-grades`
- Authentication: Dataverse user token (OBO)
- Response: Updated grades + trend data

**FR-05: Current Grade Calculation**
**Description**: Calculate current grade as latest assessment grade per area.
**Acceptance**:
- Query: `SELECT TOP 1 sprk_grade FROM sprk_kpiassessment WHERE sprk_matter = {matterId} AND sprk_performancearea = {area} ORDER BY createdon DESC`
- Update: `sprk_{area}compliancegrade_current = latest_grade`
- Example: Latest Guidelines assessment = A+ (1.00) → `sprk_guidelinecompliancegrade_current = 1.00`

**FR-06: Historical Average Calculation**
**Description**: Calculate historical average as mean of all assessment grades per area.
**Acceptance**:
- Query: `SELECT AVG(sprk_grade) FROM sprk_kpiassessment WHERE sprk_matter = {matterId} AND sprk_performancearea = {area}`
- Update: `sprk_{area}compliancegrade_average = avg_grade`
- Example: Guidelines assessments [1.00, 0.85, 0.90] → average = 0.92 → `sprk_guidelinecompliancegrade_average = 0.92`

---

#### **Trigger Mechanism (FR-07)**

**FR-07: JavaScript Web Resource Trigger**
**Description**: Web resource on Quick Create form that calls calculator API after save.
**Acceptance**:
- Web resource: `sprk_kpiassessment_quickcreate.js`
- Event: OnSave (using `addOnPostSave` to ensure save completes first)
- Action: `fetch('/api/matters/{matterId}/recalculate-grades', { method: 'POST' })`
- Error Handling: Log to console, show user-friendly error dialog if API call fails
- Parent Refresh: `window.parent.Xrm.Page.data.refresh(false)` to show updated grades

---

#### **VisualHost Metric Cards - Main Tab (FR-08)**

**FR-08: Report Card Metric Cards**
**Description**: 3 VisualHost cards on main matter form tab showing current grades.
**Acceptance**: Each card displays:
1. **Icon**: Area-specific icon (🏛️ Guidelines, 💰 Budget, 🎯 Outcomes)
2. **Area Name**: "Guidelines", "Budget", "Outcomes"
3. **Letter Grade**: Large font, center-aligned (e.g., "A+", "C", "D")
4. **Color Coding**:
   - **Blue** (A-B range: 0.85-1.00)
   - **Yellow** (C range: 0.70-0.84)
   - **Red** (D-F range: 0.00-0.69)
5. **Contextual Text**: Smaller font below grade
   - Template: "You have an {grade_percent}% in {area_name} compliance"
   - Example: "You have an 95% in Guideline compliance"

**Data Source**: `sprk_{area}compliancegrade_current` field on matter

**Card Config** (example for Guidelines):
```json
{
  "type": "ReportCardMetric",
  "title": "Guidelines",
  "icon": "Guidelines",
  "gradeField": "sprk_guidelinecompliancegrade_current",
  "contextTemplate": "You have an {grade}% in Guideline compliance",
  "colorRules": [
    { "range": [0.85, 1.00], "color": "blue" },
    { "range": [0.70, 0.84], "color": "yellow" },
    { "range": [0.00, 0.69], "color": "red" }
  ]
}
```

---

#### **Report Card Tab (FR-09 to FR-10)**

**FR-09: Trend Cards with Sparkline**
**Description**: 3 cards above subgrid showing historical average + trend graph.
**Acceptance**: Each trend card displays:
1. **Area Name**: "Guidelines", "Budget", "Outcomes"
2. **Historical Average**: "Avg: 0.90 (A-)"
3. **Trend Indicator**: ↑ improving / ↓ declining / → stable (calculated via linear regression)
4. **Sparkline Graph**: Last 5 assessments plotted as mini line graph

**Data Source**:
- Historical average: `sprk_{area}compliancegrade_average` field
- Sparkline data: Query last 5 KPI assessments for this matter + area (`ORDER BY createdon DESC LIMIT 5`)

**Trend Calculation** (Linear Regression):
```python
assessments = [0.85, 0.90, 0.88, 0.92, 0.95]  # last 5 grades
slope = linear_regression_slope(assessments)

if slope > 0.02:
    trend = "↑ improving"
elif slope < -0.02:
    trend = "↓ declining"
else:
    trend = "→ stable"
```

**FR-10: KPI Assessments Subgrid**
**Description**: Subgrid below trend cards showing all KPI assessments for this matter.
**Acceptance**: Subgrid displays:
- Columns: Performance Area, KPI Name, Grade, Created On, Assessment Notes (preview)
- Sorted by: Created On descending (newest first)
- Action: "+ Add KPI" button launches Quick Create form
- Related Entity: `sprk_kpiassessment` where `sprk_matter = current matter`

---

#### **VisualHost Enhancement (FR-11)**

**FR-11: Report Card Metric Card Type**
**Description**: New or modified VisualHost card type to support Report Card metric cards.
**Acceptance**:
- Investigates existing card types (check if current metric card can be extended)
- If new type needed: Creates `ReportCardMetric` component
- Supports:
  - Custom icon per card
  - Large letter grade display
  - Color coding by grade range (blue/yellow/red)
  - Contextual text with template substitution
  - Responsive design (Fluent UI v9)

**Implementation Options**:
- **Option A**: Extend existing metric card with new display mode
- **Option B**: Create new card type inheriting from base metric card
- **Decision**: Task includes research phase to determine best approach

---

### Non-Functional Requirements

**NFR-01: Performance — API Response Time**
**Description**: Calculator API must respond quickly to avoid blocking form save.
**Target**: API response time < 500ms for recalculation
**Verification**: Load test with 100 concurrent requests

**NFR-02: Performance — Subgrid Load Time**
**Description**: Report Card tab subgrid must load quickly even with many assessments.
**Target**: Subgrid loads < 2 seconds for 100 assessment records
**Verification**: Test with synthetic data (100 assessments per matter)

**NFR-03: Reliability — Web Resource Error Handling**
**Description**: Web resource must handle API failures gracefully.
**Acceptance**:
- If API call fails after 3 retries, log error to console
- Show user-friendly error dialog: "Unable to recalculate grades. Please refresh the form."
- Form save still completes (API call is asynchronous, non-blocking)

**NFR-04: Usability — Quick Create Form**
**Description**: Quick Create form must be intuitive and fast to complete.
**Target**: Users can complete assessment in < 30 seconds
**Verification**: User testing with 5 users, measure completion time

**NFR-05: Accessibility — Fluent UI v9 Compliance**
**Description**: All UI components must use Fluent UI v9 with dark mode support.
**Acceptance**:
- No hard-coded colors (use design tokens)
- VisualHost cards render correctly in light and dark themes
- WCAG 2.1 AA compliance (color contrast, keyboard navigation)

**NFR-06: Maintainability — Code Simplicity**
**Description**: R1 code must be simple and maintainable for future enhancements.
**Target**: Calculator logic < 200 lines of code
**Verification**: Code review checklist

---

## Technical Constraints

### Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| **ADR-001** | MUST use Minimal API pattern (no Azure Functions) |
| **ADR-008** | MUST use endpoint filters for authorization |
| **ADR-009** | MUST use Redis caching (optional for R1 - can defer) |
| **ADR-019** | MUST return ProblemDetails for API errors |
| **ADR-021** | MUST use Fluent UI v9 (no hard-coded colors, dark mode required) |

**Note**: ADR-002 (thin plugins), ADR-004 (job contracts), ADR-013 (AI) do NOT apply to R1 MVP (no plugins, no background jobs, no AI).

### MUST Rules

#### **Architecture**
- ✅ MUST use Minimal API for calculator endpoint
- ✅ MUST use JavaScript web resource for trigger (no plugins, no Power Automate)
- ✅ MUST call API asynchronously (non-blocking form save)

#### **Data Model**
- ✅ MUST store grades as decimal (0.00 to 1.00) for precision
- ✅ MUST maintain both current and historical average fields
- ✅ MUST use standard Dataverse choice fields (no custom option sets)

#### **UI**
- ✅ MUST use Fluent UI v9 for all VisualHost components
- ✅ MUST support dark mode (no hard-coded colors)
- ✅ MUST color-code grades consistently (blue/yellow/red)

#### **API**
- ✅ MUST authenticate using Dataverse user token (OBO)
- ✅ MUST return ProblemDetails on errors
- ✅ MUST complete recalculation in < 500ms

### MUST NOT Rules

- ❌ MUST NOT use Dataverse plugins (use web resource trigger instead)
- ❌ MUST NOT use Power Automate (use web resource trigger instead)
- ❌ MUST NOT block form save on API failure (async, non-blocking)
- ❌ MUST NOT hard-code KPI list in web resource (use Dataverse choices)

---

## Success Criteria

### Phase 1: Data Model
- [ ] KPI Assessment entity deployed to Dataverse
- [ ] 6 grade fields added to Matter entity
- [ ] Quick Create form configured with 5 fields
- [ ] Choice fields populated (Performance Area, Grade)

### Phase 2: Calculator API
- [ ] API endpoint functional: `POST /api/matters/{matterId}/recalculate-grades`
- [ ] Current grade calculation correct (latest assessment)
- [ ] Historical average calculation correct (mean of all)
- [ ] API response time < 500ms

### Phase 3: Trigger Mechanism
- [ ] Web resource deployed: `sprk_kpiassessment_quickcreate.js`
- [ ] OnSave event triggers API call
- [ ] Parent form refreshes after recalculation
- [ ] Error handling functional (user-friendly dialog)

### Phase 4: Main Tab - VisualHost Cards
- [ ] 3 Report Card metric cards render on main tab
- [ ] Cards show current grades with correct color coding
- [ ] Contextual text displays correctly
- [ ] Dark mode works (no hard-coded colors)

### Phase 5: Report Card Tab
- [ ] 3 trend cards with historical average + sparkline
- [ ] Sparkline shows last 5 assessments
- [ ] Trend indicator (↑ ↓ →) calculated via linear regression
- [ ] Subgrid shows all KPI assessments for matter
- [ ] "+ Add KPI" button launches Quick Create form

### Phase 6: End-to-End Validation
- [ ] User can add KPI assessment via Quick Create form in < 30 seconds
- [ ] Grades update immediately on main tab after save
- [ ] Report Card tab shows updated average and trend
- [ ] Performance targets met (API < 500ms, subgrid < 2s)

---

## Dependencies

### External Dependencies
- Microsoft Dataverse (GA)
- Minimal API runtime (ASP.NET Core)
- VisualHost component library (internal)

### Internal Dependencies
- VisualHost module (`src/client/shared/visual-host/`)
- BFF API infrastructure (`src/server/api/Sprk.Bff.Api/`)

---

## Assumptions

- **Manual Entry is Acceptable**: Users are willing to manually enter KPI assessments for MVP
- **5 Updates Sufficient**: Last 5 assessments provide meaningful trend analysis
- **Linear Regression Appropriate**: Simple linear regression accurately represents trend direction
- **No Caching Needed**: Calculator API performance acceptable without Redis caching (can add later if needed)
- **Standard Quick Create Sufficient**: No need for custom PCF panel for R1 (future enhancement)

---

## Unresolved Questions

- [ ] **VisualHost Card Type**: Can existing metric card be extended, or do we need new card type? (Answer during implementation)
- [ ] **KPI Catalog**: Hardcoded choice field or separate entity? (Recommend: choice field for R1 simplicity)
- [ ] **Trend Graph Library**: Which charting library for sparkline? (Recommend: Recharts or Victory)

---

*AI-optimized specification for R1 MVP. Full solution spec: [spec-full.md](spec-full.md)*
*Generated: 2026-02-12 by project-pipeline skill*
