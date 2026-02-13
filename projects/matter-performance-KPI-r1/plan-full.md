# Project Plan: Matter Performance Assessment & KPI R1

> **Last Updated**: 2026-02-12
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build a Performance Assessment module that produces Matter Report Cards with composite scores across three performance areas (OCG Compliance, Budget Compliance, Outcome Success), aggregating to Organization and Person Report Cards for portfolio analytics. The system combines system-calculated inputs, practitioner assessments (1-3 questions via Outlook adaptive cards), and AI-derived evaluations.

**Scope**: MVP includes 6 KPI definitions, 6 Dataverse entities, 4 consolidated services, assessment infrastructure with non-overlapping responsibility model (in-house vs outside counsel get different KPIs), scoring engine, scheduled batch rollups, and visualization components.

**Timeline**: 5 phases | **Estimated Effort**: 30-40 days (implementation + testing)

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Minimal API + BackgroundService pattern required (no Azure Functions)
- **ADR-002**: Plugins must be thin (<50ms, no HTTP calls) → Use Service Bus for heavy work
- **ADR-004**: Async jobs use JobContract pattern with specific JobTypes
- **ADR-008**: Authorization via endpoint filters (not global middleware)
- **ADR-009**: Redis-first caching (no L1 cache unless profiling proves need)
- **ADR-010**: DI minimalism (≤15 non-framework registrations) → 4 services for this module
- **ADR-011**: Dataset PCF controls over legacy subgrids
- **ADR-012**: Reuse `@spaarke/ui-components` shared component library
- **ADR-013**: AI via playbook infrastructure (no parallel prompt management)
- **ADR-017**: Async job status via JobOutcome pattern
- **ADR-019**: All API errors return ProblemDetails with stable error codes
- **ADR-021**: All UI must use Fluent UI v9 (no hard-coded colors, dark mode required)

**From Spec**:
- Each matter has 3-6 KPIs total (typically 1-2 per area)
- Assessments deliver 1-3 questions maximum per respondent
- Non-overlapping assessment responsibility (InHouse / OutsideCounsel / Both / SystemCalculated)
- Scheduled batch processing (nightly rollup at 2 AM, monthly snapshots at 3 AM on 1st)
- Graceful degradation for all failure scenarios (AI, adaptive card delivery, data resolver)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| 4 Consolidated Services | Comply with ADR-010 DI minimalism | IScorecardService, IAssessmentService, IScorecardInputService, IScorecardRollupService |
| Non-overlapping Assessment Model | Reduce assessment fatigue, clarify responsibility | `sprk_assessmentresponsibility` field determines which KPIs each role assesses |
| Scheduled Batch Rollups | Performance and scale optimization | Nightly job processes only changed matters (sprk_scorecardchanged = true) |
| Delta Snapshots | Storage optimization | Monthly job creates snapshots only for changed scorecards |
| Single Consolidated AI Call | Comply with ADR-013, reduce latency | One playbook call per assessment evaluates all AI-applicable KPIs |
| Submit Tracking Fields | Capture submission metadata for audit | `sprk_isdraft`, `sprk_submittedby`, `sprk_submittedon` on assessment entity |

### Discovered Resources

**Applicable Skills** (auto-discovered):
- `.claude/skills/dataverse-deploy/` - Deploy solutions, PCF controls, web resources
- `.claude/skills/adr-aware/` - Proactively load ADRs based on resource types
- `.claude/skills/script-aware/` - Discover and reuse scripts from library

**Knowledge Articles**:
- `.claude/adr/ADR-001-minimal-api.md` - Minimal API + BackgroundService pattern
- `.claude/adr/ADR-002-thin-plugins.md` - Thin plugin constraints
- `.claude/adr/ADR-004-job-contract.md` - Job Contract schema and idempotency
- `.claude/adr/ADR-008-endpoint-filters.md` - Authorization filter implementation
- `.claude/adr/ADR-009-redis-caching.md` - Distributed cache patterns
- `.claude/adr/ADR-010-di-minimalism.md` - DI registration limits
- `.claude/adr/ADR-013-ai-architecture.md` - AI playbook integration
- `.claude/adr/ADR-021-fluent-design-system.md` - Fluent UI v9 requirements
- `.claude/patterns/api/endpoint-definition.md` - Minimal API endpoint patterns
- `.claude/patterns/api/background-workers.md` - BackgroundService + Service Bus patterns
- `.claude/patterns/dataverse/plugin-structure.md` - Thin plugin implementation
- `.claude/patterns/caching/distributed-cache.md` - Redis caching patterns

**Reusable Code**:
- `src/server/api/Sprk.Bff.Api/Api/` - Endpoint definition patterns
- `src/server/api/Sprk.Bff.Api/Services/Jobs/` - Job Contract and handler patterns
- `src/client/shared/ui-components/` - Shared Fluent UI components

**Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` - Deploy PCF controls
- `scripts/Test-SdapBffApi.ps1` - API testing validation

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation — Data Model & Scoring Engine (Week 1-2)
└─ Create 6 entities, seed KPI catalog, implement scoring engine
└─ Deliverable: Scorecard API endpoints functional with manual KPI input

Phase 2: Assessments (Week 2-3)
└─ Build assessment generation, delivery (Outlook + in-app), and processing
└─ Deliverable: Invoice approved → assessment delivered → user submits → scorecard updates

Phase 3: System Inputs & Visualization (Week 3-4)
└─ Auto-produce system inputs on invoice approval, build VisualHost components
└─ Deliverable: Invoice approved → system inputs auto-created → scorecard card renders

Phase 4: Rollups & History (Week 4-5)
└─ Implement nightly rollup job, monthly snapshot job, rollup endpoints/views
└─ Deliverable: Org/person rollups with weighted averaging and score history

Phase 5: AI Assessment (Week 5-6)
└─ Create AI playbook, integrate with assessment lifecycle, add AI provenance UI
└─ Deliverable: AI evaluates KPIs, AI-derived inputs visible in detail view
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs scoring engine and KPI definitions)
- Phase 3 BLOCKED BY Phase 2 (needs assessment infrastructure for system input production)
- Phase 4 BLOCKED BY Phase 3 (needs scorecard calculation complete)
- Phase 5 can run PARALLEL to Phase 4 (independent AI integration)

**High-Risk Items:**
- Rollup performance at 1,000+ matters - Mitigation: Performance tests early, optimize queries, use indexed fields
- AI playbook failures - Mitigation: Graceful degradation, error handling, admin alerts
- Adaptive card delivery failures - Mitigation: In-app fallback notification

---

## 4. Phase Breakdown

### Phase 1: Foundation — Data Model & Scoring Engine

**Objectives:**
1. Create Dataverse entities for performance tracking
2. Seed KPI catalog with 6 MVP definitions
3. Implement scoring engine with normalization and composite scoring
4. Build API endpoints for scorecard operations

**Deliverables:**
- [ ] Dataverse solution deployed: `spaarke-performance` with 6 entities
- [ ] Matter entity extended with `sprk_scorecardchanged` field
- [ ] KPI catalog seeded: OCG-1.1.1, OCG-1.3.3, BUD-2.1.1, BUD-2.2.5, OUT-3.1.1, OUT-3.2.1
- [ ] 2-3 profile templates created (Litigation, Transaction, General)
- [ ] `IScorecardService` implemented with scoring engine
- [ ] Data resolver implementations for MVP paths
- [ ] Scorecard API endpoints: GET, POST /recalculate, GET /history, GET /kpis
- [ ] Profile API endpoints: GET templates, POST assignment, PUT KPI config
- [ ] KPI catalog API endpoints: GET list, GET detail
- [ ] Unit tests for scoring engine (normalization, resolution, weighting, grade assignment)

**Critical Tasks:**
- Entity creation MUST BE FIRST (all other work depends on schema)
- KPI catalog seeding MUST happen before profile templates
- Scoring engine MUST handle all 6 formula types correctly

**Inputs**:
- `spec.md` (FR-01 to FR-15, FR-34, FR-36, FR-38)
- ADR-010 (DI minimalism), ADR-019 (ProblemDetails)

**Outputs**:
- Dataverse solution with entities
- API endpoints functional
- Unit test suite

**Acceptance Criteria**:
Profile assigned to matter → manual KPI input via API → scorecard shows correct composite scores (OCG, Budget, Outcome) and overall grade (A-F)

---

### Phase 2: Assessments

**Objectives:**
1. Build assessment generation with 4 trigger types
2. Implement Outlook adaptive card delivery
3. Create in-app assessment PCF control
4. Process assessment submissions and update scorecards

**Deliverables:**
- [ ] `IAssessmentService` implemented (generation, delivery, AI trigger)
- [ ] `InvoiceApprovalPlugin` deployed on `sprk_invoice` entity
- [ ] `MatterStatusChangePlugin` deployed on `sprk_matter` entity
- [ ] Service Bus job handlers: `AssessmentGenerationJob`, `ScorecardInputProductionJob`
- [ ] Scheduled job: `ScorecardAssessmentScheduleJob` (daily check for cadence)
- [ ] Assessment API endpoints: POST create, PATCH draft, POST submit, GET list, GET pending, POST responses (callback)
- [ ] Outlook adaptive card delivery via Microsoft Graph
- [ ] In-app assessment PCF control with draft save and Submit button
- [ ] Assessment responsibility filtering (InHouse / OutsideCounsel / Both / SystemCalculated)
- [ ] Error handling: adaptive card delivery failures → in-app fallback
- [ ] Error tracking fields: `sprk_errorlog`, `sprk_lasterror` on assessment entity

**Critical Tasks:**
- Thin plugins MUST queue Service Bus jobs (no HTTP calls, <50ms execution)
- Question assembly MUST filter by `sprk_assessmentresponsibility` field
- Submit button MUST validate all required questions answered

**Inputs**:
- Phase 1 complete (scoring engine, KPI catalog)
- `spec.md` (FR-16 to FR-24, FR-35)
- ADR-002 (thin plugins), ADR-004 (job contract), ADR-021 (Fluent UI v9)

**Outputs**:
- Assessment infrastructure functional
- Dataverse plugins deployed
- PCF control deployed

**Acceptance Criteria**:
Invoice approved → assessment generated → adaptive card delivered with 2 questions → user submits in Outlook → inputs recorded → scorecard updated

---

### Phase 3: System Inputs & Visualization

**Objectives:**
1. Auto-produce system-calculated inputs on invoice approval
2. Build VisualHost scorecard visualization components
3. Implement Redis caching for scorecard results

**Deliverables:**
- [ ] `IScorecardInputService` implemented
- [ ] `InvoiceApprovalPlugin` triggers `scorecard-input-production` Service Bus job
- [ ] Service Bus job handler: `ScorecardInputProductionJob`
- [ ] Data resolver implementations: `matter.invoices.total`, `matter.invoices.complianceRate`, `matter.budget.approved`, `profile.expectedBudget`, `profile.expectedDurationDays`
- [ ] System inputs auto-produced for Budget Variance and Invoice Compliance KPIs
- [ ] VisualHost scorecard card component (compact form view with 3 area scores + overall grade)
- [ ] VisualHost scorecard detail view (full KPI breakdown with input provenance)
- [ ] Redis caching implemented: `scorecard:{matterId}` key pattern, 24-hour TTL
- [ ] Cache invalidation on recalculation

**Critical Tasks:**
- Data resolver MUST return null (not throw) if path not found
- System input production MUST be idempotent (safe under replay)
- VisualHost components MUST use Fluent UI v9 design tokens (no hard-coded colors)

**Inputs**:
- Phase 2 complete (assessment infrastructure)
- `spec.md` (FR-25, FR-26, FR-39, FR-40, FR-41)
- ADR-009 (Redis caching), ADR-021 (Fluent UI v9)

**Outputs**:
- System inputs auto-produced
- VisualHost components deployed
- Redis caching operational

**Acceptance Criteria**:
Invoice approved → system inputs auto-created → scorecard updated without human intervention → scorecard card renders on matter form with live scores

---

### Phase 4: Rollups & History

**Objectives:**
1. Implement nightly rollup job for organization/person aggregation
2. Create monthly snapshot job for score history
3. Build rollup API endpoints and visualization

**Deliverables:**
- [ ] `IScorecardRollupService` implemented with priority-weighted averaging
- [ ] `ScorecardRollupScheduledJob` (runs nightly at 2 AM)
- [ ] `ScorecardSnapshotJob` (runs monthly on 1st at 3 AM)
- [ ] `sprk_scorecardchanged` flag usage: set on scorecard update, cleared by rollup job
- [ ] Rollup API endpoints: GET org, GET person, GET history, GET summary
- [ ] VisualHost rollup view (org/person scorecard with matter breakdown)
- [ ] Redis caching for rollup results: `rollup:org:{orgId}`, `rollup:person:{personId}`
- [ ] Performance test: 1,000 changed matters → rollup recalc in <10 min
- [ ] Performance test: 5,000 matters, 40% changed → monthly snapshots in <15 min

**Critical Tasks:**
- Rollup job MUST process only matters with `sprk_scorecardchanged = true` (optimization)
- Monthly snapshot job MUST create delta snapshots only (skip unchanged scorecards)
- Priority weighting MUST apply correctly: High=1.5×, Normal=1.0×, Low=0.5×

**Inputs**:
- Phase 3 complete (scorecard calculation)
- `spec.md` (FR-30 to FR-33, FR-37, FR-42, NFR-01, NFR-02)
- ADR-001 (BackgroundService), ADR-009 (Redis caching)

**Outputs**:
- Nightly rollup job operational
- Monthly snapshot job operational
- Rollup views deployed

**Acceptance Criteria**:
Matter scorecard updated → flag set → nightly job recalculates org rollup within 24 hours → firm report card shows weighted average across 10 matters → score history trend visualized

---

### Phase 5: AI Assessment

**Objectives:**
1. Create Scorecard Assessment playbook in Dataverse
2. Integrate AI trigger into assessment lifecycle
3. Display AI contribution with provenance

**Deliverables:**
- [ ] Scorecard Assessment playbook created in Dataverse
- [ ] AI trigger integrated into `IAssessmentService`
- [ ] AI evaluation hints populated on KPI definitions
- [ ] AI status tracking: `sprk_aiassessmentstatus`, `sprk_aijobid`, error fields on assessment entity
- [ ] Integration with `AnalysisOrchestrationService` (single consolidated call per assessment)
- [ ] Error handling: AI failures → graceful degradation, error logging, admin notification
- [ ] AI input provenance display in VisualHost scorecard detail view

**Critical Tasks:**
- AI playbook call MUST be single consolidated call (not one per KPI) per ADR-013
- AI failures MUST NOT block assessment (graceful degradation)
- AI-derived inputs MUST have confidence weight = 0.7

**Inputs**:
- Phase 2 complete (assessment infrastructure)
- `spec.md` (FR-27 to FR-29, NFR-05, NFR-06)
- ADR-013 (AI architecture), ADR-017 (job status)

**Outputs**:
- AI playbook functional
- AI-derived inputs created
- AI provenance visible in UI

**Acceptance Criteria**:
Assessment triggered → AI playbook execution → AI evaluates Responsiveness KPI → AI-derived input created with justification → scorecard updated → AI contribution visible in detail view with provenance icon

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Microsoft Graph API (actionable messages) | GA | Low | In-app fallback if delivery fails |
| Azure OpenAI (playbook execution) | GA | Medium | Graceful degradation, error logging |
| Service Bus (async jobs) | GA | Low | Retry logic with exponential backoff |
| Redis (caching) | GA | Low | AbortOnConnectFail=false, in-memory fallback for dev |
| Application Insights (logging) | GA | Low | No mitigation needed |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Financial Intelligence module | `src/` | Production |
| `sprk_invoice` entity | Dataverse | Production |
| `sprk_matter` entity | Dataverse | Production |
| SpeFileStore facade | `src/server/shared/` | Production |
| AnalysisOrchestrationService | `src/server/api/Services/Ai/` | Production |
| VisualHost module | `src/client/` | Production |
| `@spaarke/ui-components` | `src/client/shared/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- Scoring engine: normalization, resolution rules, composite scoring, grade assignment
- Data resolver: all MVP paths return expected values or null
- Job handlers: idempotency, error handling, outcome reporting
- Assessment generation: responsibility filtering, question assembly

**Integration Tests**:
- End-to-end assessment flow: trigger → delivery → submission → scorecard update
- System input production: invoice approval → Service Bus job → inputs created
- Rollup calculation: scorecard changes → nightly job → rollup recalculated
- AI assessment: playbook call → response parsing → AI-derived inputs created

**Performance Tests**:
- Nightly rollup job: 1,000 changed matters → completes in <10 min
- Monthly snapshot job: 5,000 matters, 40% changed → completes in <15 min
- Concurrent assessments: 20 users submit simultaneously → all succeed

**E2E Tests**:
- User scenario: In-house user receives assessment email, opens card, submits responses, sees updated scorecard
- User scenario: Invoice approval triggers system inputs, scorecard updates automatically
- User scenario: View firm report card with matter breakdown, drill into score history

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] Profile assigned to matter → manual KPI input → correct composite scores and grade
- [ ] All 6 formula types normalize correctly (boundary conditions tested)
- [ ] Unit tests pass: 80%+ coverage for scoring engine

**Phase 2:**
- [ ] Invoice approved → assessment generated with correct questions for in-house vs outside counsel
- [ ] Adaptive card delivered → user submits → inputs recorded → scorecard updated
- [ ] Adaptive card delivery fails → in-app fallback notification created

**Phase 3:**
- [ ] Invoice approved → system inputs auto-created for Budget Variance and Invoice Compliance
- [ ] Scorecard card renders on matter form with 3 area scores + overall grade
- [ ] Dark mode works correctly (no hard-coded colors)

**Phase 4:**
- [ ] Matter scorecard updated → nightly job recalculates org rollup within 24 hours
- [ ] Performance tests pass: 1,000 matters in <10 min, 5,000 snapshots in <15 min
- [ ] Rollup view shows weighted average across matters with priority weighting

**Phase 5:**
- [ ] AI playbook evaluates Responsiveness KPI with justification
- [ ] AI failure → assessment proceeds with system + human inputs (graceful degradation)
- [ ] AI contribution visible in detail view with provenance icon

### Business Acceptance

- [ ] Matter report card displays with composite scores across 3 areas
- [ ] Organization report card aggregates scores from 10+ matters
- [ ] Assessment emails have 1-3 questions max (no assessment fatigue)
- [ ] Score history trends visualized over 6 months

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | AI playbook failures block assessments | Medium | High | Graceful degradation - assessments proceed without AI inputs, admin alerts |
| R2 | Rollup performance degrades at scale | Medium | High | Performance tests early, optimize queries, use indexed fields, delta snapshots |
| R3 | Adaptive card delivery failures | Low | Medium | In-app fallback notification, retry logic with exponential backoff |
| R4 | Assessment fatigue (too many questions) | Low | Medium | Limit to 1-3 questions max, non-overlapping responsibility model |
| R5 | Data resolver failures prevent scoring | Low | High | Null handling (no exceptions), scoring proceeds with available inputs |
| R6 | Outside counsel email spam complaints | Low | Low | Quarterly cadence default, skip if no new invoice activity |

---

## 9. Next Steps

1. **Review this PLAN.md** with team
2. **Run** `/task-create matter-performance-KPI-r1` to generate task files
3. **Begin** Phase 1 implementation

---

**Status**: Ready for Tasks
**Next Action**: Run `/task-create` to decompose phases into executable task files (50-200+ tasks)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
