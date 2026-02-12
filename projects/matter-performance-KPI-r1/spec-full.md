# Performance Assessment & Matter Report Card — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-12
> **Source**: performance-assessment-design.md

---

## Executive Summary

The Performance Assessment module provides a multi-modal intelligence system for evaluating legal matter performance. It produces **Matter Report Cards** with composite scores across three performance areas (OCG Compliance, Budget Compliance, Outcome Success), and aggregates those scores into Organization (law firm) and Person (attorney) Report Cards for portfolio-level performance visibility.

**Key Characteristics**: Each matter has **3-6 KPIs total** (typically 1-2 per area). Assessments deliver **1-3 questions maximum** per respondent. Triggers are **infrequent** (monthly at most). The system supports three input modalities: system-calculated (from invoice/budget data), practitioner assessment (human input via Outlook adaptive cards), and AI-derived (playbook-based evaluation).

---

## Scope

### In Scope (MVP)

#### **Data Model**
- 6 core Dataverse entities: KPI Definition, Performance Profile, Profile KPI Config, Performance Assessment, Scorecard KPI Input, Matter Scorecard
- 1 rollup entity: Performance Rollup (for organizations and persons)
- 1 extension to existing entity: Add `sprk_scorecardchanged` field to `sprk_matter`

#### **KPI Catalog**
- **6 KPI definitions** (2 per area):
  - **OCG Compliance**: Invoice Line-Item Compliance Rate (system), Responsiveness (in-house assessment)
  - **Budget Compliance**: Overall Budget Variance (system), Early Warning Effectiveness (in-house assessment)
  - **Outcome Success**: Outcome vs. Target (bilateral assessment), Cycle Time vs. Expected Duration (system)

#### **Profile Templates**
- 2-3 profile templates (Litigation, Transaction, General) with 3-4 KPIs each
- Profile assignment to matters
- KPI weighting and responsibility configuration

#### **Services**
- **4 consolidated services** (DI minimalism per ADR-010):
  1. `IScorecardService` — scoring engine + data resolver
  2. `IAssessmentService` — generation + delivery + AI trigger
  3. `IScorecardInputService` — system-calculated input production
  4. `IScorecardRollupService` — organization/person aggregation

#### **Assessment Infrastructure**
- Assessment generation with **4 trigger types**:
  - Invoice approval → `InvoiceApprovalPlugin` → Service Bus job
  - Matter status change → `MatterStatusChangePlugin` → Service Bus job
  - Scheduled (daily check for cadence) → `ScorecardAssessmentScheduleJob`
  - Manual (direct API call)
- Assessment delivery via **Outlook actionable messages** with adaptive cards (1-3 questions)
- In-app assessment completion with **draft save** and **explicit Submit button**
- Assessment responsibility filtering (InHouse / OutsideCounsel / Both / SystemCalculated)
- Error handling: adaptive card delivery failures → in-app fallback

#### **System-Calculated Inputs**
- Invoice approval triggers system input production for Budget Variance and Invoice Compliance KPIs
- Data resolver implementations for MVP paths:
  - `matter.invoices.total`, `matter.invoices.complianceRate`
  - `matter.budget.approved`, `profile.expectedBudget`, `profile.expectedDurationDays`

#### **AI Assessment**
- Scorecard Assessment playbook (single consolidated call per assessment)
- AI evaluation for Responsiveness KPI (OCG-1.3.3)
- AI status tracking on assessment records
- Graceful degradation: AI failures → assessment proceeds without AI inputs

#### **Scoring Engine**
- Input resolution rules (system > assessment > AI, with confidence weighting)
- Normalization bands from KPI calculation definitions
- Composite scoring with area weights
- Grade assignment (A-F scale)
- Confidence level calculation (High/Medium/Low based on data richness)

#### **Rollups & History**
- **Nightly rollup job** (2 AM) recalculates organization/person rollups for matters with `sprk_scorecardchanged = true`
- Priority-weighted averaging (High=1.5×, Normal=1.0×, Low=0.5×)
- **Monthly snapshot job** (1st of month, 3 AM) creates delta snapshots for changed scorecards only
- 24-month default time window for rollups

#### **API Endpoints**
- Scorecard endpoints: get, history, KPI detail, recalculate
- Assessment endpoints: create, list, draft save, submit, pending
- Profile endpoints: templates, assignment, KPI config update
- Rollup endpoints: organization, person, history, summary
- KPI catalog endpoints: list, detail

#### **UI Components**
- VisualHost scorecard card (compact form view with 3 area scores + overall grade)
- VisualHost scorecard detail view (full KPI breakdown with input provenance)
- VisualHost organization/person rollup view with matter breakdown
- In-app assessment panel with draft save and Submit button

#### **Caching**
- Redis caching for scorecard results (`scorecard:{matterId}`)
- Redis caching for rollup results (`rollup:org:{orgId}`, `rollup:person:{personId}`)
- Cache invalidation on recalculation

#### **Error Handling**
- Retry logic for Service Bus jobs (3 retries with exponential backoff)
- Retry logic for external calls (Microsoft Graph: 3 retries, circuit breaker after 5 failures)
- Retry logic for AI playbook calls (2 retries, 60s timeout)
- Graceful degradation for all failure scenarios
- Structured logging to Application Insights
- Error tracking fields on assessment entity (`sprk_errorlog`, `sprk_lasterror`)

---

### Out of Scope (Post-MVP)

- Teams adaptive card delivery (separate from Outlook)
- Integration-synced input pattern (external e-billing connectors via API)
- Benchmark analytics (cross-firm/industry comparisons)
- Assessment completion tracking and automated reminders
- Full 59-KPI catalog expansion (ship with 6, add over time)
- Customer-created custom KPIs
- Score-triggered workflow automation (e.g., auto-alert on score drop)
- Advanced formula expression evaluator (MVP uses 6 predefined formula types)
- Profile template versioning and synchronization
- KPI definition versioning
- Security model beyond Dataverse business units/roles
- GDPR/data retention policies (handled via separate organizational policy)

---

### Affected Areas

| Area | Files/Paths | Description |
|------|-------------|-------------|
| **Dataverse Solution** | `src/solutions/spaarke-performance/` | New solution with 6 entities + Matter extension |
| **BFF API** | `src/server/api/Sprk.Bff.Api/` | New endpoint groups, services, background jobs |
| **Dataverse Plugins** | `src/server/plugins/` | `InvoiceApprovalPlugin`, `MatterStatusChangePlugin` |
| **PCF Controls** | `src/client/pcf/` | In-app assessment panel (new control) |
| **VisualHost Components** | `src/client/shared/visual-host/` | Scorecard card, detail view, rollup view |
| **Shared Components** | `src/client/shared/ui-components/` | Reusable score visualization components |
| **Infrastructure** | `infrastructure/` | Service Bus job registration, Redis cache keys |

---

## Requirements

### Functional Requirements

#### **Data Model (FR-01 to FR-07)**

**FR-01: KPI Catalog Entity**
**Description**: Create `sprk_scorecardkpidefinition` entity to store the master catalog of all available KPIs.
**Acceptance**: Entity deployed with fields: code, name, scorecard area, measurement type, calculation definition (JSON), AI evaluation hint, supported input sources, default weight, category, sort order.

**FR-02: Performance Profile Entity**
**Description**: Create `sprk_performanceprofile` entity to define scoring configuration for matters or reusable templates.
**Acceptance**: Entity deployed with fields: name, is template, source template, matter lookup, performance priority, area weights, assessment cadence, trigger config, expected duration, expected budget, outcome target, status.

**FR-03: Profile KPI Config Entity**
**Description**: Create `sprk_profilekpiconfig` junction entity linking KPIs to profiles with weights and responsibility.
**Acceptance**: Entity deployed with fields: profile lookup, KPI definition lookup, weight, is active, **assessment responsibility** (InHouse/OutsideCounsel/Both/SystemCalculated), target value, notes.

**FR-04: Performance Assessment Entity**
**Description**: Create `sprk_performanceassessment` entity to track assessment cycles.
**Acceptance**: Entity deployed with fields: name, matter, profile, trigger type, assessment scope, status, respondents, response status, AI status, AI job ID, period dates, expiration, **is draft**, **submitted by**, **submitted on**, **error log**, **last error**.

**FR-05: Scorecard KPI Input Entity**
**Description**: Create `sprk_scorecardkpiinput` entity to store all input data points (system, assessment, AI).
**Acceptance**: Entity deployed with fields: matter, KPI definition, assessment, input source type, raw value, normalized score, confidence weight, contributor (user/contact), justification, input date, period dates, is superseded.

**FR-06: Matter Scorecard Entity**
**Description**: Create `sprk_matterscorecard` entity to store computed report cards.
**Acceptance**: Entity deployed with fields: matter, area scores (OCG/Budget/Outcome), overall score, grade, confidence level, data richness %, score status, KPI counts, last calculated, last assessment date, is snapshot, snapshot date.

**FR-07: Performance Rollup Entity**
**Description**: Create `sprk_performancerollup` entity for organization/person aggregate scores.
**Acceptance**: Entity deployed with fields: name, rollup type (org/person), organization/person lookups, area scores, overall score, grade, matter count, time window months, last calculated, is snapshot, snapshot date.

**FR-08: Matter Entity Extension**
**Description**: Add `sprk_scorecardchanged` boolean field to existing `sprk_matter` entity.
**Acceptance**: Field deployed and accessible via API. Used to flag matters needing rollup recalculation.

#### **KPI Catalog Seeding (FR-09)**

**FR-09: Seed MVP KPI Definitions**
**Description**: Seed 6 KPI definitions with complete calculation definition JSON, AI evaluation hints, and metadata.
**Acceptance**:
- OCG-1.1.1: Invoice Line-Item Compliance Rate (QN, SystemCalculated)
- OCG-1.3.3: Responsiveness (QL, InHouse)
- BUD-2.1.1: Overall Budget Variance (QN, SystemCalculated)
- BUD-2.2.5: Early Warning Effectiveness (QL, InHouse)
- OUT-3.1.1: Outcome vs. Target (QL, Both)
- OUT-3.2.1: Cycle Time vs. Expected Duration (QN, SystemCalculated)

#### **Profile Templates (FR-10)**

**FR-10: Create Profile Templates**
**Description**: Seed 2-3 profile templates (Litigation, Transaction, General) with 3-4 KPIs each.
**Acceptance**: Templates created with `is_template = true`, KPI configs assigned with weights and responsibility, accessible via API.

#### **Scoring Engine (FR-11 to FR-15)**

**FR-11: Input Resolution**
**Description**: Implement input resolution rules when multiple inputs exist for same KPI.
**Acceptance**:
- System-calculated inputs preferred over assessment/AI
- Bilateral assessments (Both responsibility) weighted 60% in-house / 40% outside counsel
- Most recent non-superseded input used
- Confidence weights applied: system=1.0, assessment=0.8, AI=0.7
- Older inputs marked `sprk_issuperseded = true`

**FR-12: Normalization**
**Description**: Apply normalization bands from KPI calculation definitions to convert raw values to 0-100 scores.
**Acceptance**: All 6 formula types supported: `variance_percentage`, `ratio`, `days_average`, `days_between`, `count`, `threshold_check`. Boundary conditions tested (e.g., value exactly on band edge).

**FR-13: Composite Scoring**
**Description**: Calculate area scores and overall score using weighted averages.
**Acceptance**:
- Area score = Σ(kpi_score × kpi_weight) / Σ(kpi_weight) for non-null KPIs
- Overall score = Σ(area_score × area_weight) / Σ(area_weight) for non-null areas
- Null handling: missing KPIs/areas excluded from composite (not treated as zero)

**FR-14: Grade Assignment**
**Description**: Assign letter grade based on overall score.
**Acceptance**: A (90-100), B (75-89), C (60-74), D (40-59), F (0-39). Boundary values tested (e.g., 75.0 = B).

**FR-15: Confidence Calculation**
**Description**: Calculate data richness and confidence level.
**Acceptance**:
- Data richness % = (KPIs with inputs / total active KPIs) × 100
- Confidence level: High (≥80%), Medium (50-79%), Low (<50%)

#### **Assessment Generation (FR-16 to FR-20)**

**FR-16: Invoice Trigger**
**Description**: When invoice approved, trigger scorecard input production and optional assessment.
**Acceptance**:
- `InvoiceApprovalPlugin` on `sprk_invoice` (Post-Update) queues Service Bus job `scorecard-input-production`
- `ScorecardInputProductionJob` creates system inputs for SystemCalculated KPIs
- If qualitative KPIs exist in OCG/Budget areas AND last assessment >30 days, create focused assessment (OCG + Budget scope only)
- Scoring engine recalculates scorecard
- `sprk_scorecardchanged = true` set on matter

**FR-17: Status Change Trigger**
**Description**: When matter status changes to configured trigger status, generate assessment.
**Acceptance**:
- `MatterStatusChangePlugin` on `sprk_matter` (Post-Update) queues Service Bus job `assessment-generation`
- If new status = "Closed", create comprehensive final assessment (all three areas, score status → Final)
- If new status = milestone (not closed), create milestone assessment (Outcome leading indicators only)

**FR-18: Scheduled Trigger**
**Description**: Daily job checks matters for scheduled assessment cadence.
**Acceptance**:
- `ScorecardAssessmentScheduleJob` runs daily
- Queries active matters with performance profiles where next assessment is due based on cadence (Monthly/Quarterly/SemiAnnual)
- Creates comprehensive assessment (all three areas)

**FR-19: Manual Trigger**
**Description**: API endpoint to manually create assessment on demand.
**Acceptance**: `POST /api/v1/matters/{matterId}/assessments` creates assessment with configurable scope and audience.

**FR-20: Question Assembly with Responsibility Filtering**
**Description**: Build separate questionnaires for in-house and outside counsel based on KPI responsibility.
**Acceptance**:
- In-house questionnaire: Include KPIs where `sprk_assessmentresponsibility = InHouse OR Both`
- Outside counsel questionnaire: Include KPIs where `sprk_assessmentresponsibility = OutsideCounsel OR Both`
- SystemCalculated KPIs excluded from questionnaires
- Questions extracted from KPI calculation definitions (JSON)
- Typical result: 1-3 questions per questionnaire

#### **Assessment Delivery (FR-21 to FR-24)**

**FR-21: Outlook Adaptive Card Delivery**
**Description**: Deliver assessments as Outlook actionable messages with adaptive cards.
**Acceptance**:
- Adaptive card JSON dynamically generated from assessment questions
- Action.Http callback points to `/api/v1/assessments/{assessmentId}/responses`
- Signed token for authentication (time-limited, assessment-scoped)
- Separate messages sent to in-house and outside counsel with their respective questions
- Delivery via Microsoft Graph

**FR-22: In-App Assessment Panel**
**Description**: PCF control for completing assessments within Spaarke.
**Acceptance**:
- Panel displays same questions as adaptive card
- Draft auto-save on input change (`PATCH /api/v1/assessments/{assessmentId}/draft`, sets `sprk_isdraft = true`)
- Explicit Submit button (`POST /api/v1/assessments/{assessmentId}/submit`, sets `sprk_isdraft = false`, `sprk_submittedby`, `sprk_submittedon`)
- Validation: all required questions answered before submit allowed
- Error dialog with correlation ID on submission failure

**FR-23: Assessment Submission Processing**
**Description**: Map assessment responses to KPI inputs and trigger scoring.
**Acceptance**:
- On submit, map each answer to corresponding KPI's `scoreMapping` or `options[].score`
- Create `sprk_scorecardkpiinput` records with appropriate source type (Assessment_InHouse or Assessment_OutsideCounsel)
- Set `sprk_isdraft = false`, populate submitted by/on fields
- Trigger scoring engine recalculation
- Set `sprk_scorecardchanged = true` on matter

**FR-24: Adaptive Card Delivery Fallback**
**Description**: If adaptive card delivery fails after retries, create in-app notification.
**Acceptance**:
- Retry 3× with exponential backoff (1s, 2s, 4s)
- If still fails, log error to `sprk_errorlog`, set `sprk_lasterror`
- Create in-app notification banner: "Email delivery failed - please complete assessment in app"
- User can complete assessment via in-app panel

#### **System-Calculated Inputs (FR-25 to FR-26)**

**FR-25: Data Resolver Implementation**
**Description**: Implement data source path resolution for system-calculated KPIs.
**Acceptance**: All MVP paths resolve correctly:
- `matter.invoices.total` → Sum of approved invoice amounts
- `matter.invoices.complianceRate` → % of line items passing OCG checks
- `matter.budget.approved` → Current approved budget amount
- `profile.expectedBudget` → Expected budget from performance profile
- `profile.expectedDurationDays` → Expected duration from performance profile
- Error case: path not found → returns null, logs warning (no exception thrown)

**FR-26: System Input Production**
**Description**: Auto-produce system inputs for quantitative KPIs on invoice approval.
**Acceptance**:
- `ScorecardInputProductionJob` loads matter's active profile KPI configs
- Filters to KPIs where `sprk_assessmentresponsibility = SystemCalculated`
- Uses data resolver to retrieve current values for each input source path
- Evaluates formula (from calculation definition JSON) and applies normalization bands
- Creates `sprk_scorecardkpiinput` records with source type `System_Calculated`
- Raw value and normalized score both populated

#### **AI Assessment (FR-27 to FR-29)**

**FR-27: AI Playbook Integration**
**Description**: Trigger Scorecard Assessment playbook via existing `AnalysisOrchestrationService`.
**Acceptance**:
- Triggered as part of assessment lifecycle (after assessment record created)
- Collects AI-evaluable KPIs (where `sprk_aievaluationhint` is not null)
- Assembles playbook context: matter metadata, recent documents (via SpeFileStore), invoice summaries, KPI evaluation list
- Submits to `AnalysisOrchestrationService` with `PlaybookId = "scorecard-assessment"`
- Single consolidated call per assessment (not one call per KPI)

**FR-28: AI Response Processing**
**Description**: Parse AI playbook response and create AI-derived inputs.
**Acceptance**:
- Parses structured JSON response: `{ evaluations: [{ kpiCode, score, justification }] }`
- Maps each evaluation to `sprk_scorecardkpiinput` with source type `AI_Derived`
- Applies `scoreMapping` from KPI calculation definition to normalize score to 0-100
- Populates `sprk_justification` field with AI explanation
- Sets confidence weight = 0.7

**FR-29: AI Failure Handling**
**Description**: Gracefully handle AI assessment failures.
**Acceptance**:
- If AI call fails after 2 retries, log error to `sprk_errorlog` with full context
- Set `sprk_aiassessmentstatus = Failed`, `sprk_lasterror = "AI assessment failed - proceeding with human inputs only"`
- Assessment continues with system + human inputs (no AI inputs created)
- Admin notification sent (Application Insights alert)
- Scorecard still computes with available inputs (may have lower confidence level)

#### **Rollup Calculation (FR-30 to FR-32)**

**FR-30: Nightly Rollup Job**
**Description**: Scheduled job recalculates organization/person rollups for changed matters.
**Acceptance**:
- `ScorecardRollupScheduledJob` runs nightly at 2 AM
- Queries matters where `sprk_scorecardchanged = true`
- Gets affected organizations (from matter law firm relationship) and persons (from matter team members)
- For each affected org/person, recalculates rollup
- Clears `sprk_scorecardchanged` flag on all processed matters

**FR-31: Priority-Weighted Averaging**
**Description**: Rollup scores use priority weighting from performance profile.
**Acceptance**:
- Maps priority to weight multiplier: High = 1.5, Normal = 1.0, Low = 0.5
- For each area: `area_score = Σ(matter_area_score × priority_weight) / Σ(priority_weight)` where matter has non-null score for area
- Overall score computed from area scores using same weighted average
- Matter count = count of matters with non-null overall scores

**FR-32: Rollup Time Window**
**Description**: Rollups include only matters within trailing time window.
**Acceptance**:
- Default time window = 24 months (configurable)
- Query filters to matters created within window
- Only active (non-snapshot) scorecards included

#### **Score History (FR-33)**

**FR-33: Monthly Snapshot Job**
**Description**: Scheduled job creates delta snapshots for changed scorecards.
**Acceptance**:
- `ScorecardSnapshotJob` runs monthly on 1st of month at 3 AM
- Queries scorecards where `modifiedon > last_snapshot_date` and `is_snapshot = false`
- Clones each as snapshot: `sprk_issnapshot = true`, `sprk_snapshotdate = current_month`
- Similarly snapshots changed `sprk_performancerollup` records
- Updates `last_snapshot_date` configuration value
- If scorecard unchanged since last snapshot, skip (optimization)

#### **API Endpoints (FR-34 to FR-38)**

**FR-34: Scorecard Endpoints**
**Description**: Minimal API endpoints for scorecard operations.
**Acceptance**:
- `GET /api/v1/matters/{matterId}/scorecard` — Returns current report card (from Redis cache or DB)
- `GET /api/v1/matters/{matterId}/scorecard/history` — Returns monthly snapshots (paginated)
- `GET /api/v1/matters/{matterId}/scorecard/kpis` — Returns KPI detail breakdown with input provenance
- `POST /api/v1/matters/{matterId}/scorecard/recalculate` — Manual recalculation, invalidates cache
- All endpoints use matter-level authorization filter (ADR-008)

**FR-35: Assessment Endpoints**
**Description**: Minimal API endpoints for assessment operations.
**Acceptance**:
- `POST /api/v1/matters/{matterId}/assessments` — Create and send new assessment
- `GET /api/v1/assessments/{assessmentId}` — Get assessment with questions
- `PATCH /api/v1/assessments/{assessmentId}/draft` — Save draft (auto-save)
- `POST /api/v1/assessments/{assessmentId}/submit` — Explicit submit (triggers scoring)
- `POST /api/v1/assessments/{assessmentId}/responses` — Outlook adaptive card callback (signed token auth)
- `GET /api/v1/assessments/pending` — List pending assessments for current user
- `GET /api/v1/matters/{matterId}/assessments` — List all assessments for matter
- All endpoints use appropriate authorization filters

**FR-36: Profile Endpoints**
**Description**: Minimal API endpoints for profile operations.
**Acceptance**:
- `GET /api/v1/performance-profiles/templates` — List available profile templates
- `GET /api/v1/performance-profiles/{profileId}` — Get profile with KPI configs
- `POST /api/v1/matters/{matterId}/performance-profile` — Assign profile to matter (clone from template)
- `PUT /api/v1/performance-profiles/{profileId}/kpis` — Update KPI selection and weights

**FR-37: Rollup Endpoints**
**Description**: Minimal API endpoints for rollup operations.
**Acceptance**:
- `GET /api/v1/rollups/organizations/{accountId}` — Organization (firm) rollup scorecard
- `GET /api/v1/rollups/persons/{contactId}` — Person (attorney) rollup scorecard
- `GET /api/v1/rollups/organizations/{accountId}/history` — Org rollup history (monthly snapshots)
- `GET /api/v1/rollups/summary` — Dashboard summary with distributions and alerts

**FR-38: KPI Catalog Endpoints**
**Description**: Minimal API endpoints for KPI catalog operations.
**Acceptance**:
- `GET /api/v1/kpi-definitions` — List all KPI definitions (filterable by area, type)
- `GET /api/v1/kpi-definitions/{kpiId}` — Get KPI definition with calculation definition JSON

#### **Caching (FR-39)**

**FR-39: Redis Caching Strategy**
**Description**: Cache scorecard and rollup results in Redis for performance.
**Acceptance**:
- Scorecard results cached with key pattern `scorecard:{matterId}`, TTL = 24 hours
- Rollup results cached with key patterns `rollup:org:{orgId}`, `rollup:person:{personId}`, TTL = 24 hours
- Cache invalidated on recalculation (explicit or triggered by new inputs)
- Cache-aside pattern: check cache first, fetch from DB on miss, write to cache

#### **UI Components (FR-40 to FR-42)**

**FR-40: Matter Scorecard Card**
**Description**: VisualHost component for compact scorecard display on matter form.
**Acceptance**:
- Displays overall grade badge (letter grade with color)
- Shows 3 area score indicators (progress rings or horizontal bars)
- Displays confidence indicator (data richness meter)
- Shows "Pending assessment" call-to-action when assessments are due
- Drill-through to scorecard detail view

**FR-41: Scorecard Detail View**
**Description**: VisualHost component for full KPI breakdown.
**Acceptance**:
- Area-level score cards with grade and weight
- KPI table per area: name, score bar, input source icons, trend arrow
- Input provenance indicators (icons showing system/assessment/AI sources)
- Score history sparkline (from monthly snapshots)
- Click KPI to drill into input detail (see all inputs with timestamps)

**FR-42: Rollup View**
**Description**: VisualHost component for organization/person rollup scorecard.
**Acceptance**:
- Overall score, grade, area scores displayed
- Matter count and time window shown
- Matter breakdown table: matter name, priority, overall score, grade, last assessment date
- Score history trend visualization

---

### Non-Functional Requirements

**NFR-01: Performance — Nightly Rollup Job**
**Description**: Rollup recalculation must complete in reasonable time at scale.
**Target**: 1,000 changed matters → rollup recalc completes in <10 minutes
**Verification**: Performance test with synthetic data (1,000 matters, 50 orgs, 200 persons)

**NFR-02: Performance — Monthly Snapshot Job**
**Description**: Snapshot creation must complete in reasonable time at scale.
**Target**: 5,000 active matters, 40% changed (2,000 snapshots) → completes in <15 minutes
**Verification**: Performance test with synthetic data

**NFR-03: Performance — Concurrent Assessments**
**Description**: System must handle concurrent assessment submissions without deadlocks.
**Target**: 20 users submit assessments simultaneously → all succeed, no errors
**Verification**: Load test with parallel API calls

**NFR-04: Performance — Data Resolver**
**Description**: Data resolver calls must complete quickly to avoid blocking scoring engine.
**Target**: All data resolver paths for 4 KPIs complete in <500ms total
**Verification**: Unit tests with instrumentation, cache hit rate monitoring

**NFR-05: Resilience — Graceful Degradation**
**Description**: Failures in one component must not block the entire workflow.
**Acceptance**:
- AI failure → assessment proceeds with system + human inputs
- Adaptive card delivery failure → in-app fallback notification
- Data resolver failure → KPI score comes from assessment instead
- Scoring engine error → previous scorecard preserved with error indicator

**NFR-06: Resilience — Retry Logic**
**Description**: Transient failures must be retried with appropriate backoff.
**Acceptance**:
- Service Bus jobs: 3 retries with exponential backoff (1 min, 5 min, 15 min), then dead-letter
- Microsoft Graph calls: 3 retries with exponential backoff (1s, 2s, 4s), circuit breaker after 5 consecutive failures
- AI playbook calls: 2 retries with exponential backoff (5s, 10s), 60s timeout

**NFR-07: Observability — Structured Logging**
**Description**: All errors and key events logged to Application Insights with full context.
**Acceptance**:
- Error logs include: correlation ID, matter ID, KPI code, input count, profile ID, exception details
- Error fields on assessment entity populated: `sprk_errorlog` (JSON array), `sprk_lasterror` (user-facing message)
- Admin alerts configured: failed assessments (daily digest), scoring engine errors (immediate), rollup staleness >7 days (weekly)

**NFR-08: Security — Dataverse Authorization**
**Description**: All data access controlled via Dataverse business units and roles.
**Acceptance**: Security configuration out of scope for this project (handled via Dataverse admin). API endpoints enforce matter/assessment/profile access checks.

**NFR-09: Security — API Authorization**
**Description**: All API endpoints use endpoint filters for authorization (ADR-008).
**Acceptance**: No global authorization middleware. Each endpoint has appropriate filter (matter access, user context, signed token, etc.)

**NFR-10: Accessibility — Fluent UI v9**
**Description**: All UI components use Fluent UI v9 with dark mode support (ADR-021).
**Acceptance**: No hard-coded colors. All components render correctly in light and dark themes.

---

## Technical Constraints

### Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| **ADR-001** | Minimal API + BackgroundService pattern required |
| **ADR-002** | Plugins must be thin (<50ms, no HTTP calls) → Use Service Bus for heavy work |
| **ADR-004** | Async jobs use `JobContract` pattern with specific JobTypes |
| **ADR-008** | Authorization via endpoint filters (not global middleware) |
| **ADR-009** | Redis-first caching (no L1 cache unless profiling proves need) |
| **ADR-010** | DI minimalism (≤15 non-framework registrations) → 4 services for this module |
| **ADR-011** | Dataset PCF controls over legacy subgrids |
| **ADR-012** | Reuse `@spaarke/ui-components` shared component library |
| **ADR-013** | AI via playbook infrastructure (no parallel prompt management) |
| **ADR-017** | Async job status via `JobOutcome` pattern |
| **ADR-019** | All API errors return `ProblemDetails` with stable error codes |
| **ADR-021** | All UI must use Fluent UI v9 (no hard-coded colors, dark mode required) |

### MUST Rules

#### **Architecture**
- ✅ **MUST** use Minimal API pattern for all endpoints (`MapGet`, `MapPost`, etc. in `ScorecardEndpoints.cs`, `AssessmentEndpoints.cs`, etc.)
- ✅ **MUST** implement background jobs as `IBackgroundJob` triggered via Service Bus
- ✅ **MUST** use thin Dataverse plugins (<50ms execution time, no HTTP calls, no Graph SDK)
- ✅ **MUST** queue Service Bus jobs from plugins for heavy processing
- ✅ **MUST** use endpoint filters for authorization (per-endpoint, not global middleware)

#### **Data Access**
- ✅ **MUST** cache scorecard results in Redis with `scorecard:{matterId}` key pattern
- ✅ **MUST** invalidate cache on scorecard recalculation
- ✅ **MUST** use cache-aside pattern (check cache → DB on miss → write to cache)

#### **AI Integration**
- ✅ **MUST** use playbook infrastructure for AI assessment (reuse existing `AnalysisOrchestrationService`)
- ✅ **MUST** make single consolidated AI call per assessment (not one call per KPI)
- ✅ **MUST** handle AI failures gracefully (assessment proceeds without AI inputs)

#### **Error Handling**
- ✅ **MUST** return `ProblemDetails` for all API errors with stable error codes
- ✅ **MUST** log all errors to Application Insights with structured context
- ✅ **MUST** implement retry logic for transient failures (Service Bus, Graph, AI)

#### **UI**
- ✅ **MUST** use Fluent UI v9 for all components
- ✅ **MUST** support dark mode (no hard-coded colors)
- ✅ **MUST** reuse components from `@spaarke/ui-components` where applicable

#### **Dependency Injection**
- ✅ **MUST** limit DI registrations to ≤15 non-framework services for this module
- ✅ **MUST** use 4 consolidated services: `IScorecardService`, `IAssessmentService`, `IScorecardInputService`, `IScorecardRollupService`

### MUST NOT Rules

- ❌ **MUST NOT** create heavy plugins (no HTTP calls, no Graph SDK in plugins)
- ❌ **MUST NOT** use global authorization middleware (use endpoint filters per ADR-008)
- ❌ **MUST NOT** create separate AI infrastructure (reuse existing playbook system per ADR-013)
- ❌ **MUST NOT** hard-code colors or disable dark mode in UI (per ADR-021)
- ❌ **MUST NOT** exceed 15 DI service registrations for this module (per ADR-010)
- ❌ **MUST NOT** make multiple AI calls per assessment (single consolidated call per ADR-013)

### Existing Patterns to Follow

| Pattern | Reference | Description |
|---------|-----------|-------------|
| Minimal API Endpoints | `src/server/api/Sprk.Bff.Api/Endpoints/` | Standard endpoint group pattern |
| Background Jobs | `.claude/patterns/api/background-job-pattern.md` | `IBackgroundJob` implementation via Service Bus |
| Dataverse Plugins | `.claude/patterns/dataverse/thin-plugin-pattern.md` | Thin plugin (<50ms) → queue Service Bus job |
| Endpoint Filters | `.claude/constraints/api.md` | Authorization via `AddEndpointFilter<T>()` |
| Redis Caching | `.claude/patterns/api/caching-pattern.md` | Cache-aside with invalidation |
| VisualHost Components | `.claude/patterns/pcf/visualhost-pattern.md` | Data contracts and component config |
| Fluent UI v9 | `.claude/constraints/ui.md` | Dark mode support, shared components |

---

## Success Criteria

### Phase 1: Foundation — Data Model & Scoring Engine

- [ ] Dataverse solution deployed with 6 entities + Matter extension
- [ ] KPI catalog seeded with 6 definitions including calculation definition JSON
- [ ] 2-3 profile templates created with 3-4 KPIs each
- [ ] `IScorecardService` implemented with scoring engine and data resolver
- [ ] Scorecard API endpoints functional (`GET`, `POST /recalculate`)
- [ ] Profile API endpoints functional (templates, assignment, KPI config)
- [ ] KPI catalog API endpoints functional (list, detail)
- [ ] Unit tests: scoring engine (normalization, resolution, weighting, grade assignment)
- [ ] **Acceptance**: Profile assigned to matter → manual KPI input via API → scorecard shows correct composite scores and grade

### Phase 2: Assessments

- [ ] `IAssessmentService` implemented (generation with responsibility filtering, delivery, AI trigger)
- [ ] `InvoiceApprovalPlugin` deployed on `sprk_invoice`
- [ ] `MatterStatusChangePlugin` deployed on `sprk_matter`
- [ ] Service Bus job handlers: `AssessmentGenerationJob`
- [ ] Scheduled job: `ScorecardAssessmentScheduleJob`
- [ ] Assessment API endpoints functional (create, draft save, submit, list, pending)
- [ ] Outlook adaptive card delivery implemented
- [ ] In-app assessment PCF control with draft save and Submit button
- [ ] Error handling: adaptive card delivery failures → in-app fallback
- [ ] **Acceptance**: Invoice approved → assessment generated → adaptive card delivered with 2 questions → user submits in Outlook → inputs recorded → scorecard updated

### Phase 3: System Inputs & Visualization

- [ ] `IScorecardInputService` implemented
- [ ] `InvoiceApprovalPlugin` triggers `scorecard-input-production` Service Bus job
- [ ] Service Bus job handler: `ScorecardInputProductionJob`
- [ ] Data resolver implementations for all MVP paths
- [ ] System inputs auto-produced for Budget Variance and Invoice Compliance KPIs
- [ ] VisualHost scorecard card component configured and deployed
- [ ] VisualHost scorecard detail view configured and deployed
- [ ] Redis caching implemented for scorecard results
- [ ] **Acceptance**: Invoice approved → system inputs auto-created → scorecard updated without human intervention → scorecard card renders on matter form with live scores

### Phase 4: Rollups & History

- [ ] `IScorecardRollupService` implemented with priority-weighted averaging
- [ ] `ScorecardRollupScheduledJob` (nightly at 2 AM) implemented
- [ ] `ScorecardSnapshotJob` (monthly on 1st, 3 AM) implemented
- [ ] `sprk_scorecardchanged` flag usage implemented (set on scorecard update, cleared by rollup job)
- [ ] Rollup API endpoints functional (org, person, history, summary)
- [ ] VisualHost rollup view configured and deployed
- [ ] Redis caching implemented for rollup results
- [ ] Performance test: 1,000 changed matters → rollup recalc in <10 min
- [ ] Performance test: 5,000 matters, 40% changed → monthly snapshots in <15 min
- [ ] **Acceptance**: Matter scorecard updated → flag set → nightly job recalculates org rollup within 24 hours → firm report card shows weighted average across 10 matters → score history trend visualized

### Phase 5: AI Assessment

- [ ] Scorecard Assessment playbook created in Dataverse
- [ ] AI trigger integrated into `IAssessmentService`
- [ ] AI evaluation hints populated on applicable KPI definitions
- [ ] AI status tracking on assessment records (`sprk_aiassessmentstatus`, `sprk_aijobid`, error fields)
- [ ] Integration with `AnalysisOrchestrationService` (single consolidated call)
- [ ] Error handling: AI failures → graceful degradation, error logging, admin notification
- [ ] AI input provenance display in VisualHost scorecard detail view
- [ ] **Acceptance**: Assessment triggered → AI playbook execution → AI evaluates Responsiveness KPI → AI-derived input created with justification → scorecard updated → AI contribution visible in detail view with provenance icon

---

## Dependencies

### Prerequisites

- Dataverse environment configured and accessible
- Financial Intelligence module deployed with `sprk_invoice` entity
- Existing `sprk_matter` entity accessible for extension
- Azure OpenAI service configured (for AI assessment playbook)
- Microsoft Graph API permissions for actionable messages (Mail.Send)
- Service Bus namespace configured
- Redis cache instance configured
- Application Insights instance configured

### External Dependencies

- **Microsoft Graph**: For Outlook actionable message delivery
- **Azure OpenAI**: For AI playbook execution via `AnalysisOrchestrationService`
- **Service Bus**: For async job processing
- **Redis**: For caching scorecard and rollup results
- **Application Insights**: For logging and alerting

### Internal Dependencies

- **SpeFileStore**: For AI playbook document context retrieval
- **AnalysisOrchestrationService**: For AI playbook execution (ADR-013)
- **VisualHost module**: For score visualization components
- **Shared UI Components** (`@spaarke/ui-components`): For reusable score visualization elements

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| **Performance Targets** | Are 1,000 matters for nightly rollup and 5,000 for snapshots realistic? | Proceed with assumptions | Performance tests designed for these scale targets (1K/5K) |
| **Admin Notifications** | What notification channel for errors? Email? In-app? | Application Insights only (no custom notification in MVP) | Error handling configured for Application Insights alerts; no email digest infrastructure needed |
| **Outside Counsel Access** | Will any OC users have Dataverse access? | OC assessments are Outlook-only (no Dataverse access in MVP) | In-app assessment panel designed for in-house users only; OC uses adaptive cards exclusively |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Performance Scale**: 1,000 active matters with performance tracking, 5,000 total matters. This determines performance optimization strategy (indexing, caching, batch processing).
- **Admin Notifications**: Application Insights alerts only. Failed assessments generate daily digest email, scoring engine errors trigger immediate alerts, rollup staleness >7 days generates weekly report.
- **Outside Counsel Access**: 100% Outlook-only for external users. No Dataverse licenses or in-app access for outside counsel in MVP. Future: consider guest access or portal.
- **Assessment Cadence**: Typical cadence is quarterly (not monthly). Monthly option exists for high-priority matters only. This affects scheduled job load.
- **Rollup Time Window**: Default 24 months is sufficient. No need for configurable time windows in MVP. Future: add API parameter for custom windows.
- **Data Retention**: GDPR/retention policies handled via separate organizational policy, not in this module. Snapshots retained indefinitely unless purged by admin.
- **Profile Locking**: Profiles are locked once created. No template synchronization or versioning in MVP. Future: add "refresh from template" workflow.
- **KPI Catalog Expansion**: 6 KPIs sufficient for MVP validation. Post-MVP: expand to 15-20 KPIs based on customer feedback, then full 59-KPI catalog over time.

---

## Unresolved Questions

*These questions may arise during implementation and may require owner input:*

- [ ] **Invoice Entity Integration**: Exact field names on `sprk_invoice` for status code and approved status value — need to inspect existing schema. **Blocks**: `InvoiceApprovalPlugin` implementation.
- [ ] **Matter Status Values**: Which status code values should trigger assessments (e.g., "Closed", "Settled", "Dismissed")? **Blocks**: `MatterStatusChangePlugin` configuration.
- [ ] **Matter-to-Organization Relationship**: What is the exact relationship name between `sprk_matter` and `account` (law firm)? **Blocks**: Rollup query implementation.
- [ ] **Matter-to-Person Relationship**: What is the exact relationship name between `sprk_matter` and `contact` (team members)? **Blocks**: Rollup query implementation.
- [ ] **Adaptive Card Provider Registration**: Azure AD app registration for actionable messages — need tenant admin approval? **Blocks**: Outlook delivery implementation.
- [ ] **Scorecard Assessment Playbook Content**: Exact system prompt and response format for the playbook — need AI team input. **Blocks**: AI assessment implementation.

---

*AI-optimized specification. Original design: performance-assessment-design.md (89.7 KB, 1426 lines)*
*Generated: 2026-02-12 by design-to-spec skill*
