# Matter Performance Assessment & KPI R1

> **Last Updated**: 2026-02-12
>
> **Status**: Ready for Implementation (R1 MVP)

## Project Versions

| Version | Scope | Status | Plan |
|---------|-------|--------|------|
| **R1 (Current)** | Manual KPI entry + basic visualization | Ready for Implementation | [plan-r1.md](plan-r1.md) |
| **R2-R5 (Future)** | Assessment infrastructure + AI + rollups | Planned (future) | [plan-full.md](plan-full.md) |

**This README describes the R1 MVP scope.** For full solution documentation, see [spec-full.md](spec-full.md) and [plan-full.md](plan-full.md).

---

## Overview

**R1 MVP** provides manual KPI assessment entry with automated grade calculation and visualization on matter records. Users can quickly assess matter performance across three areas (Guidelines, Budget, Outcomes) using a Quick Create form, and immediately see updated grades displayed via VisualHost metric cards.

**Full Solution** (future R2+): Multi-modal intelligence system with automated assessment generation, Outlook adaptive cards, system-calculated inputs from invoices, AI-derived evaluations, and organization/person rollups for portfolio-level analytics.

## Quick Links

| Document | Description |
|----------|-------------|
| **R1 MVP (Current)** | |
| [R1 Plan](./plan-r1.md) | Implementation plan for R1 MVP (22-29 tasks, 3-4 days) |
| [R1 Specification](./spec-r1.md) | AI-optimized R1 MVP specification |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status (to be generated) |
| [AI Context](./CLAUDE.md) | Claude Code context file |
| **Full Solution (Future)** | |
| [Full Plan](./plan-full.md) | Complete solution plan with 5 phases (archived for future) |
| [Full Specification](./spec-full.md) | Full solution specification (archived for future) |
| [Design Document](./performance-assessment-design.md) | Original human design document |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning |
| **Progress** | 0% |
| **Target Date** | TBD |
| **Completed Date** | — |
| **Owner** | Development Team |

## Problem Statement

Legal matter performance evaluation is currently manual, inconsistent, and lacks visibility. There is no systematic way to measure how outside counsel perform across key dimensions (OCG compliance, budget management, outcome achievement). This leads to:
- Inconsistent evaluation criteria across matters and attorneys
- Delayed feedback loops (assessments happen too late to affect outcomes)
- Limited data for portfolio-level insights and law firm benchmarking
- Heavy manual effort for performance reviews

## Solution Summary

Implement a Performance Assessment module that automatically generates Matter Report Cards with composite scores across three performance areas. The system combines system-calculated inputs (from invoice/budget data), practitioner assessments (human input via Outlook adaptive cards with 1-3 questions), and AI-derived evaluations (playbook-based). Scores roll up to Organization and Person Report Cards for portfolio analytics. The system uses scheduled batch processing for rollups (nightly at 2 AM) and delta snapshots (monthly) to track historical trends.

## Graduation Criteria

**R1 MVP** is considered **complete** when:

### Data Model
- [ ] KPI Assessment entity deployed to Dataverse
- [ ] 6 grade fields added to Matter entity (current + average × 3 areas)
- [ ] Quick Create form configured with 5 fields
- [ ] User can add KPI assessment in < 30 seconds

### Calculator & Trigger
- [ ] Calculator API functional: `POST /api/matters/{matterId}/recalculate-grades`
- [ ] Current grade calculation correct (latest assessment)
- [ ] Historical average calculation correct (mean of all)
- [ ] Web resource trigger calls API on save
- [ ] API response time < 500ms
- [ ] Parent form refreshes automatically after save

### Main Tab Visualization
- [ ] 3 VisualHost Report Card metric cards render on main tab
- [ ] Cards show current grades with correct color coding (blue/yellow/red)
- [ ] Contextual text displays: "You have an X% in [Area] compliance"
- [ ] Dark mode works (no hard-coded colors)

### Report Card Tab
- [ ] 3 trend cards with historical averages + sparkline graphs (last 5 updates)
- [ ] Trend indicators (↑ ↓ →) calculated via linear regression
- [ ] Subgrid shows all KPI assessments for matter
- [ ] "+ Add KPI" button launches Quick Create form

### Testing & Performance
- [ ] All unit tests pass (calculator, trend logic)
- [ ] Integration test passes (end-to-end flow)
- [ ] Performance targets met (API < 500ms, subgrid < 2s)
- [ ] Error handling works (API failure → user dialog, form saves)
- [ ] Accessibility validated (WCAG 2.1 AA)

**Future Enhancements** (R2+): See [Full Solution Graduation Criteria](plan-full.md#graduation-criteria)

## Scope

### In Scope (R1 MVP)

**Data Model**:
- 1 new entity: `sprk_kpiassessment` (KPI Assessment)
- 6 new fields on Matter: `sprk_{area}compliancegrade_current` and `sprk_{area}compliancegrade_average` × 3 areas

**User Interface**:
- Quick Create form for manual KPI assessment entry (5 fields)
- 3 VisualHost Report Card metric cards on main tab (current grades, color-coded)
- Report Card tab: 3 trend cards with sparkline graphs + subgrid

**Calculator Logic**:
- API endpoint: `POST /api/matters/{matterId}/recalculate-grades`
- Current grade: Latest assessment per area
- Historical average: Mean of all assessments per area
- Trend direction: Linear regression on last 5 updates

**Trigger Mechanism**:
- JavaScript web resource on Quick Create form
- OnSave event calls calculator API
- Parent form auto-refreshes

**VisualHost Enhancement**:
- New or modified Report Card metric card type
- Supports icon, grade, color coding, contextual text

### Out of Scope (R1) — Future R2+

**Automation** (R2):
- Assessment generation infrastructure (triggers, scheduled jobs)
- Outlook adaptive card delivery
- In-app assessment PCF panel (custom UI)
- System-calculated inputs (auto-production from invoices)
- Dataverse plugins
- Power Automate flows

**AI Integration** (R3):
- AI-derived inputs (playbook integration)
- AI evaluation for specific KPIs
- Provenance tracking

**Advanced Analytics** (R4):
- Organization/person rollups (firm-level, attorney-level)
- Nightly rollup jobs
- Monthly snapshot jobs
- Portfolio analytics

**Enterprise Features** (R5):
- Profile/KPI versioning
- Benchmark analytics (cross-firm comparisons)
- Custom KPIs (customer-created)
- Score-triggered workflow automation
- Advanced formula expression evaluator

**See [spec-full.md](spec-full.md) for complete future roadmap.**

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Simplified Model: 3-6 KPIs per matter | Reduces assessment fatigue, focus on actionable metrics | — |
| Non-overlapping Assessment Responsibility | In-house and outside counsel assess DIFFERENT KPIs based on `sprk_assessmentresponsibility` | — |
| Scheduled Batch Rollups | Changed from reactive to scheduled (2 AM nightly) for performance and scale | — |
| Delta Snapshots Only | Monthly snapshots capture only changed scorecards (optimization) | — |
| 4 Consolidated Services | Complies with ADR-010 DI minimalism (≤15 registrations) | [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) |
| No Versioning in MVP | Profiles locked once created, no template sync or KPI versioning | — |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| AI playbook failures block assessments | High | Medium | Graceful degradation - assessments proceed with system + human inputs only |
| Adaptive card delivery failures | Medium | Low | In-app fallback notification, user completes in app |
| Rollup performance at scale | High | Medium | Performance tests at 1K/5K scale, optimize queries, use indexed fields |
| Assessment fatigue (too many questions) | Medium | Low | Limit to 1-3 questions max per respondent, non-overlapping responsibility model |
| Outside counsel email spam complaints | Low | Low | Quarterly cadence default, skip if no new invoice activity |

## Dependencies

### External Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Microsoft Graph API | External | GA | For Outlook actionable message delivery |
| Azure OpenAI | External | GA | For AI playbook execution |
| Service Bus | Azure | GA | For async job processing |
| Redis | Azure | GA | For caching scorecard/rollup results |
| Application Insights | Azure | GA | For logging and alerting |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Financial Intelligence module | `src/` | Production | Depends on `sprk_invoice` entity |
| SpeFileStore | `src/server/shared/` | Production | For AI playbook document context |
| AnalysisOrchestrationService | `src/server/api/Services/Ai/` | Production | For AI playbook execution |
| VisualHost module | `src/client/` | Production | For score visualization components |
| Shared UI Components | `@spaarke/ui-components` | Production | For reusable score visualization |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Development Team | Overall accountability |
| Developer | Claude Code | Implementation |
| Reviewer | Development Team | Code review, ADR compliance |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-02-12 | 1.0 | Initial project setup, spec generated from design doc | Claude Code |

---

*For Claude Code: Always load [CLAUDE.md](./CLAUDE.md) first when working on tasks in this project.*
