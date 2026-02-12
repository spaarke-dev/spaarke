# Performance Assessment & Matter Report Card — Design Specification

> **Version**: 0.1 (Draft)
> **Date**: February 11, 2026
> **Module**: Legal Operations Intelligence — Performance Assessment
> **Status**: Design Review
> **Authors**: Spaarke Engineering

---

## Table of Contents

1. [Overview](#1-overview)
2. [Scope & MVP Boundaries](#2-scope--mvp-boundaries)
3. [End-to-End Flow](#3-end-to-end-flow)
4. [Data Model](#4-data-model)
5. [KPI Catalog & Calculation Definitions](#5-kpi-catalog--calculation-definitions)
6. [Input Collection Architecture](#6-input-collection-architecture)
7. [Assessment Triggers & Lifecycle](#7-assessment-triggers--lifecycle)
8. [Assessment Delivery & Collection](#8-assessment-delivery--collection)
9. [Scoring Engine](#9-scoring-engine)
10. [Organization & Person Rollup](#10-organization--person-rollup)
11. [AI Integration](#11-ai-integration)
12. [Error Handling & Resilience](#12-error-handling--resilience)
13. [API Endpoints](#13-api-endpoints)
14. [UI & Visualization](#14-ui--visualization)
15. [ADR Compliance Matrix](#15-adr-compliance-matrix)
16. [Phased Implementation Plan](#16-phased-implementation-plan)
17. [Testing Strategy](#17-testing-strategy)

---

## 1. Overview

### 1.1 Purpose

The Performance Assessment module provides a multi-modal intelligence system for evaluating legal matter performance. It produces **Matter Report Cards** — composite scores across three performance areas — and aggregates those scores into **Organization (law firm) and Person (attorney) Report Cards** for portfolio-level performance visibility.

**Simplified Model**: Each matter has **3-6 KPIs total** assigned at setup (typically 1-2 per area). These KPIs remain fixed for the matter lifecycle. Assessment triggers are infrequent (monthly at most). Assessments deliver **1-3 questions maximum** per respondent.

### 1.2 Three Performance Areas

| Area | What It Measures | Primary Signal Sources |
|------|------------------|----------------------|
| **Outside Counsel Guideline (OCG) Compliance** | Adherence to billing, staffing, communication, and operational expectations | Invoice data, practitioner assessment, AI analysis |
| **Budget Compliance** | Financial discipline — tracking to approved budget, forecast quality, spend efficiency | Invoice/budget data, practitioner assessment |
| **Outcome Success** | Effectiveness of legal work — results vs. expectations, resolution efficiency, strategic value | Practitioner assessment, AI analysis, matter lifecycle data |

### 1.3 Three Input Modalities

The system collects performance data through three complementary channels:

1. **System-Calculated** — Quantitative KPIs automatically derived from platform data (invoices, budgets, matter lifecycle events)
2. **Practitioner Assessment** — Qualitative and hybrid KPIs collected from in-house counsel and outside counsel via periodic assessments delivered through Outlook adaptive cards or in-app forms
3. **AI-Derived** — KPIs evaluated by AI through the existing playbook infrastructure, analyzing documents, correspondence, and billing patterns

### 1.4 Input-Agnostic Scoring

A foundational design principle: the scoring engine does not care *how* data arrives. Every KPI can be fed by multiple input sources. A customer with full invoicing integration gets system-calculated budget scores automatically. A customer with no invoicing module gets equivalent scores via practitioner assessment questions. Both produce comparable report cards with transparent confidence indicators showing data richness.

### 1.6 Simplified Assessment Model

**Each matter has 3-6 KPIs total** (not dozens) assigned at setup. These KPIs are fixed for the matter lifecycle. In-house counsel and outside counsel assess **different KPIs** (non-overlapping) based on each KPI's `assessmentresponsibility` configuration. Assessments deliver **1-3 questions maximum** per respondent. Triggers are infrequent (monthly at most).

### 1.5 Design Principles

| Principle | Implication |
|-----------|-------------|
| **Configuration over code** | KPI calculations, assessment questions, and scoring rules are defined in JSON on the KPI definition record — no code deployment to add or modify KPIs |
| **Selective tracking** | Not every matter tracks all KPIs. A performance profile assigns a curated subset (typically 8-15) from the full catalog |
| **Bidirectional** | Both in-house counsel and outside counsel provide input, enabling 360° assessment aligned with Spaarke's two-sided marketplace |
| **Playbook-native AI** | AI assessment uses the existing playbook infrastructure — no parallel prompt management system |
| **MVP minimalism** | Ship 13 core KPIs, 2-3 profile templates, and the essential scoring/assessment infrastructure. Expand the catalog and capabilities iteratively |

---

## 2. Scope & MVP Boundaries

### 2.1 MVP Includes

- Six core Dataverse entities (profile, profile KPI config, KPI definition, KPI input, matter scorecard, performance rollup)
- **6 KPI definitions** (2 per area) seeded with calculation definitions, assessment question templates, and AI evaluation hints
- 2-3 profile templates (Litigation, Transaction, General) with 3-4 KPIs each
- Profile assignment and configuration on matters
- **4 consolidated services** (Scorecard, Assessment, Input, Rollup)
- Assessment generation with four trigger types (invoice, status change, scheduled, manual)
- Assessment delivery via Outlook actionable messages with adaptive cards (1-3 questions max)
- In-app assessment completion with draft-save and explicit submit
- System-calculated inputs for budget and OCG KPIs (when invoicing module is active)
- AI assessment via Scorecard Assessment playbook (single consolidated call per assessment cycle)
- Score history (monthly snapshot job for changed scorecards only)
- Organization and person rollup with priority weighting (scheduled nightly batch)
- Score visualization via VisualHost components
- Scorecard, assessment, and rollup API endpoints

### 2.2 Deferred to Post-MVP

- Teams adaptive card delivery (separate from Outlook)
- Integration-synced input pattern (external e-billing connectors)
- Benchmark analytics (cross-firm/industry comparisons)
- Assessment completion tracking and automated reminders
- Full 59-KPI catalog expansion (ship with 6, add over time)
- Customer-created custom KPIs
- Score-triggered workflow automation (e.g., auto-alert on score drop)
- Advanced formula expression evaluator (MVP uses predefined formula types)
- Profile template versioning and synchronization
- KPI definition versioning
- Security model beyond Dataverse business units/roles
- GDPR/data retention policies

---

## 3. End-to-End Flow

### 3.1 Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                       TRIGGER EVENTS                                │
│                                                                     │
│  ┌──────────────┐ ┌──────────────┐ ┌───────────┐ ┌──────────────┐  │
│  │   Invoice    │ │    Matter    │ │ Scheduled │ │   Manual     │  │
│  │   Received   │ │   Status    │ │   Timer   │ │   Request    │  │
│  │              │ │   Changed   │ │ (Monthly/ │ │  (In-App /   │  │
│  │  (email or   │ │ (Close,     │ │ Quarterly)│ │   API)       │  │
│  │  module)     │ │  Milestone) │ │           │ │              │  │
│  └──────┬───────┘ └──────┬──────┘ └─────┬─────┘ └──────┬───────┘  │
│         └────────────────┼──────────────┼──────────────┘           │
│                          ▼              ▼                           │
└──────────────────────────┼──────────────┼───────────────────────────┘
                           ▼              ▼
              ┌────────────────────────────────────┐
              │   Assessment Generation Service    │
              │                                    │
              │  1. Load matter performance profile │
              │  2. Load profile KPI configs       │
              │  3. Determine assessment scope     │
              │     (which areas/KPIs per trigger) │
              │  4. Gap analysis: which KPIs need  │
              │     human input vs. system-calc    │
              │     vs. AI evaluation              │
              │  5. Create assessment record       │
              │  6. Fan out to input channels      │
              └────────────┬───────────────────────┘
                           │
            ┌──────────────┼──────────────────┐
            ▼              ▼                  ▼
  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐
  │ System-Calc  │  │ Practitioner │  │ AI Playbook      │
  │ Input        │  │ Assessment   │  │ Execution        │
  │ Producer     │  │              │  │                  │
  │              │  │ Outlook      │  │ Scorecard        │
  │ Invoice /    │  │ adaptive     │  │ Assessment       │
  │ budget data  │  │ card sent    │  │ playbook         │
  │ → auto-write │  │ to in-house  │  │ triggered via    │
  │ KPI inputs   │  │ & outside    │  │ AnalysisOrch-    │
  │              │  │ counsel      │  │ estrationService │
  │              │  │              │  │                  │
  │              │  │   ┌─── OR ───┐  │ KPI evaluation   │
  │              │  │   │ In-app   │  │ hints assembled  │
  │              │  │   │ form via │  │ as playbook      │
  │              │  │   │ PCF      │  │ context          │
  │              │  │   └──────────┘  │                  │
  └──────┬───────┘  └──────┬───────┘  └────────┬─────────┘
         │                 │                    │
         ▼                 ▼                    ▼
  ┌───────────────────────────────────────────────────────┐
  │              sprk_scorecardkpiinput                   │
  │                                                       │
  │  All inputs land in same entity with source type tag  │
  │  (system_calculated | assessment_inhouse |            │
  │   assessment_outsidecounsel | ai_derived)             │
  └───────────────────────┬───────────────────────────────┘
                          │
                          ▼
  ┌───────────────────────────────────────────────────────┐
  │              Scoring Engine                           │
  │                                                       │
  │  1. Retrieve all inputs for matter, grouped by KPI   │
  │  2. Resolve: prefer system > assessment > AI         │
  │     For bilateral: weighted avg (60/40 IH/OC)        │
  │  3. Apply normalization bands from KPI calc def      │
  │  4. Apply profile weights → area composite scores    │
  │  5. Compute overall composite + confidence level     │
  │  6. Write/update sprk_matterscorecard                │
  │  7. Queue rollup recalculation for affected          │
  │     organizations and persons                        │
  └───────────────────────┬───────────────────────────────┘
                          │
                          ▼
  ┌───────────────────────────────────────────────────────┐
  │              Rollup Service (Background Job)          │
  │                                                       │
  │  1. Identify affected orgs (Account) / persons       │
  │     (Contact) from matter relationships               │
  │  2. Query all matter scorecards in time window        │
  │  3. Weighted average per area:                        │
  │     Score = Σ(matter_score × priority_weight)         │
  │            / Σ(priority_weight)                       │
  │     WHERE matter has non-null score for area          │
  │  4. Write/update sprk_performancerollup              │
  └───────────────────────────────────────────────────────┘
```

### 3.2 Assessment Lifecycle States

```
                ┌──────────┐
                │ Created  │
                └────┬─────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
  ┌──────────┐ ┌──────────┐ ┌──────────┐
  │ System   │ │ Sent     │ │ AI       │
  │ Inputs   │ │ (await   │ │ Pending  │
  │ Written  │ │ response)│ │          │
  └────┬─────┘ └────┬─────┘ └────┬─────┘
       │             │            │
       │        ┌────┴─────┐     │
       │        │ Responded│     │
       │        └────┬─────┘     │
       │             │      ┌────┴─────┐
       │             │      │ AI       │
       │             │      │ Complete │
       │             │      └────┬─────┘
       └─────────────┼───────────┘
                     ▼
              ┌────────────┐
              │ Scoring    │
              │ Triggered  │
              └────┬───────┘
                   ▼
              ┌────────────┐
              │ Complete   │
              └────────────┘
```

---

## 4. Data Model

### 4.1 Entity Relationship Diagram

```
sprk_scorecardkpidefinition (KPI Catalog)
  │
  │ 1:N
  ▼
sprk_profilekpiconfig (Profile Line — junction)
  │                          │
  │ N:1                      │ N:1
  ▼                          ▼
sprk_performanceprofile ◄── sprk_matter
  │                          │
  │                          │ 1:N
  │                          ▼
  │                    sprk_performanceassessment
  │                          │
  │                          │ 1:N (via assessment lookup)
  │                          ▼
  │                    sprk_scorecardkpiinput
  │                          │
  │                          │ N:1 (via matter lookup)
  │                          ▼
  │                    sprk_matterscorecard
  │
  │
sprk_performancerollup ──► account (Organization)
                       ──► contact (Person/Attorney)
```

### 4.2 Entity Definitions

#### 4.2.1 `sprk_scorecardkpidefinition` — KPI Catalog

The master catalog of all available KPIs. Seeded per tenant, managed as reference data.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| KPI Definition Id | `sprk_scorecardkpidefinitionid` | GUID | Primary key |
| Name | `sprk_name` | Text (100) | Display name (e.g., "Invoice Line-Item Compliance Rate") |
| Code | `sprk_code` | Text (20) | Unique identifier (e.g., "OCG-1.1.1") |
| Scorecard Area | `sprk_scorecardarea` | Choice | `OCG_Compliance (100000000)`, `Budget_Compliance (100000001)`, `Outcome_Success (100000002)` |
| Measurement Type | `sprk_measurementtype` | Choice | `Quantitative (100000000)`, `Qualitative (100000001)`, `Hybrid (100000002)` |
| Description | `sprk_description` | Multiline Text | Human-readable description of what this KPI measures |
| Calculation Definition | `sprk_calculationdefinition` | Multiline Text | JSON — formula, normalization bands, assessment questions, score mappings (see §5) |
| AI Evaluation Hint | `sprk_aievaluationhint` | Multiline Text | Natural language guidance for the AI playbook when evaluating this KPI |
| Supported Input Sources | `sprk_supportedinputsources` | Multi-Select Choice | Which input channels can feed this KPI: `System_Calculated`, `Assessment_InHouse`, `Assessment_OutsideCounsel`, `AI_Derived` |
| Default Weight | `sprk_defaultweight` | Decimal | Default weight when added to a profile (0.0 to 1.0) |
| Is Active | `sprk_isactive` | Boolean | Whether this KPI is available for assignment to profiles |
| Category | `sprk_category` | Text (50) | Sub-grouping within the area (e.g., "Billing & Invoice", "Staffing & Resourcing") |
| Sort Order | `sprk_sortorder` | Whole Number | Display ordering within area/category |

#### 4.2.2 `sprk_performanceprofile` — Performance Profile

Defines the scoring configuration for a matter or serves as a reusable template.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Performance Profile Id | `sprk_performanceprofileid` | GUID | Primary key |
| Name | `sprk_name` | Text (100) | Profile name (e.g., "Litigation Default", "Matter #2024-0892 Profile") |
| Is Template | `sprk_istemplate` | Boolean | `true` = reusable template; `false` = matter-specific instance |
| Source Template | `sprk_sourcetemplate` | Lookup → `sprk_performanceprofile` | The template this profile was cloned from (null for templates) |
| Matter | `sprk_matter` | Lookup → `sprk_matter` | The matter this profile is assigned to (null for templates) |
| Performance Priority | `sprk_performancepriority` | Choice | `High (100000000)` = 1.5× rollup weight, `Normal (100000001)` = 1.0×, `Low (100000002)` = 0.5× |
| OCG Area Weight | `sprk_ocgareaweight` | Decimal | Weight of OCG area in overall composite (default 0.33) |
| Budget Area Weight | `sprk_budgetareaweight` | Decimal | Weight of Budget area in overall composite (default 0.34) |
| Outcome Area Weight | `sprk_outcomeareaweight` | Decimal | Weight of Outcome area in overall composite (default 0.33) |
| Assessment Cadence | `sprk_assessmentcadence` | Choice | `Monthly (100000000)`, `Quarterly (100000001)`, `SemiAnnual (100000002)`, `None (100000003)` |
| Trigger on Status Change | `sprk_triggeronstatuschange` | Multi-Select Choice | Which matter status transitions trigger an assessment |
| Trigger on Invoice Receipt | `sprk_triggeronInvoice` | Boolean | Whether invoice processing triggers a focused assessment |
| Expected Duration Days | `sprk_expecteddurationdays` | Whole Number | Expected matter duration for cycle time KPIs |
| Expected Budget | `sprk_expectedbudget` | Currency | Target budget for budget variance KPIs |
| Outcome Target Description | `sprk_outcometarget` | Multiline Text | Natural language description of what constitutes a successful outcome for this matter |
| Status | `sprk_status` | Choice | `Draft (100000000)`, `Active (100000001)`, `Archived (100000002)` |

#### 4.2.3 `sprk_profilekpiconfig` — Profile KPI Configuration (Junction)

Links a specific KPI to a profile with per-profile weight and threshold overrides.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Profile KPI Config Id | `sprk_profilekpiconfigid` | GUID | Primary key |
| Performance Profile | `sprk_performanceprofile` | Lookup → `sprk_performanceprofile` | Parent profile |
| KPI Definition | `sprk_scorecardkpidefinition` | Lookup → `sprk_scorecardkpidefinition` | Which KPI is included |
| Weight | `sprk_weight` | Decimal | Weight of this KPI within its area (overrides KPI default) |
| Is Active | `sprk_isactive` | Boolean | Can be deactivated without removing the record |
| Assessment Responsibility | `sprk_assessmentresponsibility` | Choice | Who provides input: `InHouse (100000000)`, `OutsideCounsel (100000001)`, `Both (100000002)`, `SystemCalculated (100000003)` |
| Target Value | `sprk_targetvalue` | Decimal | Optional matter-specific target that overrides the KPI definition's normalization bands |
| Notes | `sprk_notes` | Multiline Text | Context for why this KPI was included or how the target was set |

#### 4.2.4 `sprk_performanceassessment` — Assessment Record

Tracks a single assessment cycle for a matter — the event that triggers practitioner input and AI evaluation.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Performance Assessment Id | `sprk_performanceassessmentid` | GUID | Primary key |
| Name | `sprk_name` | Text (100) | Auto-generated (e.g., "Q1 2026 Assessment — Matter #2024-0892") |
| Matter | `sprk_matter` | Lookup → `sprk_matter` | Which matter is being assessed |
| Performance Profile | `sprk_performanceprofile` | Lookup → `sprk_performanceprofile` | Active profile at time of assessment |
| Trigger Type | `sprk_triggertype` | Choice | `Invoice (100000000)`, `StatusChange (100000001)`, `Scheduled (100000002)`, `Manual (100000003)` |
| Assessment Scope | `sprk_assessmentscope` | Multi-Select Choice | Which areas are included: `OCG_Compliance`, `Budget_Compliance`, `Outcome_Success` |
| Status | `sprk_assessmentstatus` | Choice | `Created (100000000)`, `Sent (100000001)`, `PartialResponse (100000002)`, `Complete (100000003)`, `Expired (100000004)` |
| InHouse Respondent | `sprk_inhouserespondent` | Lookup → `systemuser` | Assigned in-house counsel |
| OutsideCounsel Respondent | `sprk_outsidecounselrespondent` | Lookup → `contact` | Assigned outside counsel attorney |
| InHouse Response Status | `sprk_inhouseresponsestatus` | Choice | `Pending (100000000)`, `Complete (100000001)`, `Skipped (100000002)` |
| OutsideCounsel Response Status | `sprk_outsidecounselresponsestatus` | Choice | `Pending (100000000)`, `Complete (100000001)`, `Skipped (100000002)`, `NotApplicable (100000003)` |
| AI Assessment Status | `sprk_aiassessmentstatus` | Choice | `Pending (100000000)`, `Running (100000001)`, `Complete (100000002)`, `Failed (100000003)`, `NotApplicable (100000004)` |
| AI Job Id | `sprk_aijobid` | Text (50) | Reference to the async job for AI playbook execution |
| Created On | `createdon` | DateTime | System-managed |
| Completed On | `sprk_completedon` | DateTime | When all inputs were received and scoring triggered |
| Assessment Period Start | `sprk_periodstart` | DateTime | Start of the period being assessed |
| Assessment Period End | `sprk_periodend` | DateTime | End of the period being assessed |
| Expires On | `sprk_expireson` | DateTime | Deadline for practitioner responses (not enforced in MVP) |
| Is Draft | `sprk_isdraft` | Boolean | `true` = in-progress with auto-save; `false` = explicitly submitted |
| Submitted By | `sprk_submittedby` | Lookup → `systemuser` | In-house user who clicked Submit (null for OC assessments) |
| Submitted By Contact | `sprk_submittedbycontact` | Lookup → `contact` | Outside counsel who clicked Submit |
| Submitted On | `sprk_submittedon` | DateTime | Timestamp when Submit button was clicked |
| Error Log | `sprk_errorlog` | Multiline Text | JSON array of errors encountered during lifecycle (delivery failures, AI errors, etc.) |
| Last Error | `sprk_lasterror` | Text (500) | Most recent error message (user-facing) |

#### 4.2.5 `sprk_scorecardkpiinput` — KPI Input Record

A single data point contributed toward a KPI score. All input modalities write to this entity.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Scorecard KPI Input Id | `sprk_scorecardkpiinputid` | GUID | Primary key |
| Matter | `sprk_matter` | Lookup → `sprk_matter` | Always populated |
| KPI Definition | `sprk_scorecardkpidefinition` | Lookup → `sprk_scorecardkpidefinition` | Which KPI this input feeds |
| Assessment | `sprk_assessment` | Lookup → `sprk_performanceassessment` | Parent assessment (null for system-calculated inputs outside an assessment cycle) |
| Input Source Type | `sprk_inputsourcetype` | Choice | `System_Calculated (100000000)`, `Assessment_InHouse (100000001)`, `Assessment_OutsideCounsel (100000002)`, `AI_Derived (100000003)` |
| Raw Value | `sprk_rawvalue` | Text (500) | The original value before normalization (e.g., "42 days", "3 out of 5", "$45,200 variance") |
| Normalized Score | `sprk_normalizedscore` | Decimal | The 0-100 score after applying normalization from the calculation definition |
| Confidence Weight | `sprk_confidenceweight` | Decimal | Input source confidence (default: system_calculated=1.0, assessment=0.8, ai_derived=0.7) |
| Contributor | `sprk_contributor` | Lookup → `systemuser` | User who provided the input (null for system/AI) |
| Contributor Contact | `sprk_contributorcontact` | Lookup → `contact` | Outside counsel who provided the input |
| Justification | `sprk_justification` | Multiline Text | Optional narrative explaining the input (especially for AI-derived inputs) |
| Input Date | `sprk_inputdate` | DateTime | When this input was captured |
| Period Start | `sprk_periodstart` | DateTime | Start of the period this input covers |
| Period End | `sprk_periodend` | DateTime | End of the period this input covers |
| Is Superseded | `sprk_issuperseded` | Boolean | Marked true when a newer input for the same KPI/source replaces this one |

#### 4.2.6 `sprk_matterscorecard` — Matter Scorecard (Computed)

The computed report card for a matter. Updated by the scoring engine whenever new inputs arrive.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Matter Scorecard Id | `sprk_matterscorecardid` | GUID | Primary key |
| Matter | `sprk_matter` | Lookup → `sprk_matter` | One-to-one with matter |
| OCG Compliance Score | `sprk_ocgscore` | Decimal (nullable) | 0-100 composite for OCG area (null = no KPIs tracked in this area) |
| Budget Compliance Score | `sprk_budgetscore` | Decimal (nullable) | 0-100 composite for Budget area |
| Outcome Success Score | `sprk_outcomescore` | Decimal (nullable) | 0-100 composite for Outcome area |
| Overall Score | `sprk_overallscore` | Decimal (nullable) | Weighted composite of three areas |
| Grade | `sprk_grade` | Choice | `A (100000000)` 90-100, `B (100000001)` 75-89, `C (100000002)` 60-74, `D (100000003)` 40-59, `F (100000004)` 0-39 |
| Confidence Level | `sprk_confidencelevel` | Choice | `High (100000000)` ≥80% KPIs have inputs, `Medium (100000001)` 50-79%, `Low (100000002)` <50% |
| Data Richness Pct | `sprk_datarichnesspct` | Decimal | % of active KPIs that have at least one input |
| Score Status | `sprk_scorestatus` | Choice | `Provisional (100000000)` (active matter), `Final (100000001)` (matter closed) |
| KPI Count | `sprk_kpicount` | Whole Number | Total KPIs configured on profile |
| KPIs With Input | `sprk_kpiswithinput` | Whole Number | KPIs that have at least one input |
| Last Calculated | `sprk_lastcalculated` | DateTime | Timestamp of most recent scoring engine run |
| Last Assessment Date | `sprk_lastassessmentdate` | DateTime | Date of most recent completed assessment |
| Is Snapshot | `sprk_issnapshot` | Boolean | `true` for monthly history snapshots; `false` for the live record |
| Snapshot Date | `sprk_snapshotdate` | DateTime | Month/period the snapshot represents (null for live record) |

#### 4.2.7 `sprk_performancerollup` — Organization & Person Rollup

Pre-computed aggregate scores at the firm or attorney level.

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Performance Rollup Id | `sprk_performancerollupid` | GUID | Primary key |
| Name | `sprk_name` | Text (100) | Auto-generated (e.g., "Baker McKenzie — Trailing 24 Months") |
| Rollup Type | `sprk_rolluptype` | Choice | `Organization (100000000)`, `Person (100000001)` |
| Organization | `sprk_organization` | Lookup → `account` | Law firm (populated when rollup type = Organization) |
| Person | `sprk_person` | Lookup → `contact` | Attorney (populated when rollup type = Person) |
| OCG Score | `sprk_ocgscore` | Decimal (nullable) | Weighted average across matters with OCG scores |
| Budget Score | `sprk_budgetscore` | Decimal (nullable) | Weighted average across matters with Budget scores |
| Outcome Score | `sprk_outcomescore` | Decimal (nullable) | Weighted average across matters with Outcome scores |
| Overall Score | `sprk_overallscore` | Decimal (nullable) | Composite |
| Grade | `sprk_grade` | Choice | Same grade scale as matter scorecard |
| Matter Count | `sprk_mattercount` | Whole Number | Number of matters included in the rollup |
| Time Window Months | `sprk_timewindowmonths` | Whole Number | Trailing months included (default 24) |
| Last Calculated | `sprk_lastcalculated` | DateTime | Timestamp of most recent rollup run |
| Is Snapshot | `sprk_issnapshot` | Boolean | For rollup history |
| Snapshot Date | `sprk_snapshotdate` | DateTime | Month/period |

#### 4.2.8 Extensions to Existing Entities

**`sprk_matter` — Additional Field for Scorecard Module**

| Field | Logical Name | Type | Description |
|-------|-------------|------|-------------|
| Scorecard Changed | `sprk_scorecardchanged` | Boolean | Flag indicating scorecard has been updated and rollup recalculation is needed. Set by scoring engine, cleared by nightly rollup job. |

---

## 5. KPI Catalog & Calculation Definitions

### 5.1 Calculation Definition JSON Schema

Each `sprk_scorecardkpidefinition` record carries a `sprk_calculationdefinition` field containing a JSON object that defines how the KPI is scored. The scoring engine and assessment generator interpret this JSON at runtime.

#### 5.1.1 Schema Types

**`formula`** — Quantitative, system-calculated KPIs:

```json
{
  "calculationType": "formula",
  "formulaType": "variance_percentage",
  "inputs": [
    {
      "key": "actualSpend",
      "source": "matter.invoices.total",
      "type": "currency",
      "label": "Total Actual Spend"
    },
    {
      "key": "approvedBudget",
      "source": "profile.expectedBudget",
      "type": "currency",
      "label": "Approved Budget"
    }
  ],
  "normalization": {
    "direction": "lower_is_better",
    "bands": [
      { "max": 5, "score": 100 },
      { "max": 10, "score": 85 },
      { "max": 20, "score": 65 },
      { "max": 30, "score": 40 },
      { "min": 30, "score": 20 }
    ]
  }
}
```

**`assessment`** — Qualitative, practitioner-assessed KPIs:

```json
{
  "calculationType": "assessment",
  "questions": [
    {
      "key": "responsiveness_rating",
      "text": "How responsive has outside counsel been to inquiries and requests?",
      "format": "scale_5",
      "labels": {
        "1": "Very Slow / Unresponsive",
        "2": "Below Expectations",
        "3": "Meets Expectations",
        "4": "Exceeds Expectations",
        "5": "Exceptional"
      },
      "audience": ["inhouse", "outsidecounsel"],
      "scoreMapping": { "1": 20, "2": 40, "3": 60, "4": 80, "5": 100 }
    }
  ]
}
```

**`hybrid`** — System-calculated when data exists, assessment fallback when not:

```json
{
  "calculationType": "hybrid",
  "systemCalculation": {
    "formulaType": "days_average",
    "inputs": [
      {
        "key": "avgDaysToInvoice",
        "source": "matter.invoices.avgDaysFromWorkToSubmission",
        "type": "number"
      }
    ],
    "normalization": {
      "direction": "lower_is_better",
      "bands": [
        { "max": 30, "score": 100 },
        { "max": 45, "score": 80 },
        { "max": 60, "score": 60 },
        { "max": 90, "score": 40 },
        { "min": 90, "score": 20 }
      ]
    }
  },
  "assessmentFallback": {
    "questions": [
      {
        "key": "billing_timeliness",
        "text": "How timely has outside counsel been with invoice submission?",
        "format": "range_select",
        "options": [
          { "label": "Within 30 days", "score": 100 },
          { "label": "30-45 days", "score": 80 },
          { "label": "45-60 days", "score": 60 },
          { "label": "60-90 days", "score": 40 },
          { "label": "90+ days", "score": 20 }
        ],
        "audience": ["inhouse"]
      }
    ]
  }
}
```

#### 5.1.2 Supported Formula Types (MVP)

| Formula Type | Description | Example KPIs |
|-------------|-------------|-------------|
| `variance_percentage` | `abs((actual - target) / target) × 100` | Budget variance, forecast accuracy |
| `ratio` | `numerator / denominator × 100` | Compliance rate, staffing ratios |
| `days_average` | Average duration in days | Billing timeliness, cycle time |
| `days_between` | Days between two date fields | Matter duration vs. expected |
| `count` | Count of matching records | Rejection count, amendment frequency |
| `threshold_check` | Binary pass/fail against a threshold | Rate compliance, guideline acknowledgment |

Custom formulas beyond these types require a code-level calculation handler registered in the scoring engine. This is the extensibility point for post-MVP complex calculations.

#### 5.1.3 Supported Question Formats

| Format | UI Rendering | Score Mapping |
|--------|-------------|---------------|
| `scale_5` | 5-point Likert scale with labels | Direct mapping (1→20, 2→40, etc.) |
| `scale_10` | 10-point scale | Direct mapping (1→10, 2→20, etc.) |
| `range_select` | Radio/dropdown with labeled ranges | Per-option score defined in JSON |
| `traffic_light` | Three-option status selector (🟢🟡🔴) | Green=100, Yellow=60, Red=20 |
| `yes_no` | Binary with optional follow-up text | Yes=100, No=0 (or configurable) |
| `comparative` | "Better / Same / Worse" relative assessment | Better=90, Same=60, Worse=30 |

#### 5.1.4 Data Source Paths

The `inputs[].source` field references a known data access path resolved by the `ScorecardDataResolver` service.

**MVP Supported Paths:**

| Path | Resolves To |
|------|-------------|
| `matter.invoices.total` | Sum of approved invoice amounts for the matter |
| `matter.invoices.count` | Count of invoices submitted |
| `matter.invoices.avgDaysFromWorkToSubmission` | Average days between work date and invoice date |
| `matter.invoices.complianceRate` | % of line items passing OCG checks |
| `matter.invoices.rejectionRate` | % of line items rejected or reduced |
| `matter.invoices.blendedRate` | Effective blended hourly rate |
| `matter.budget.approved` | Current approved budget amount |
| `matter.budget.amendmentCount` | Number of budget revisions |
| `matter.budget.consumptionPct` | % of budget consumed to date |
| `matter.lifecycle.durationDays` | Days from matter open to current/close |
| `matter.lifecycle.statusChangeCount` | Number of status transitions |
| `profile.expectedBudget` | Expected budget from performance profile |
| `profile.expectedDurationDays` | Expected duration from performance profile |

Additional paths are added by registering new resolvers in the `ScorecardDataResolver` — no schema change required, just a new resolver implementation.

### 5.2 MVP KPI Definitions

The following **6 KPIs** (2 per area) are seeded as the initial catalog. Full calculation definition JSON is provided for each.

#### Area 1: OCG Compliance (2 KPIs)

**OCG-1.1.1: Invoice Line-Item Compliance Rate**
- Type: Quantitative
- Calculation: `ratio` — compliant_lines / total_lines × 100
- Source: `matter.invoices.complianceRate`
- AI Hint: "Review invoice line items for adherence to outside counsel guidelines including proper task coding, rate compliance, and prohibited charge avoidance."
- Default Weight: 0.50
- Assessment Responsibility: `SystemCalculated`

**OCG-1.3.3: Responsiveness**
- Type: Qualitative
- Assessment: scale_5 ("How responsive has outside counsel been to inquiries and requests?")
- Audience: `InHouse`
- AI Hint: "Analyze correspondence patterns for response times and communication frequency."
- Default Weight: 0.50
- Assessment Responsibility: `InHouse`

#### Area 2: Budget Compliance (2 KPIs)

**BUD-2.1.1: Overall Budget Variance**
- Type: Quantitative
- Calculation: `variance_percentage` — abs((actualSpend - approvedBudget) / approvedBudget) × 100
- Sources: `matter.invoices.total`, `profile.expectedBudget`
- Normalization: ±5%=100, ±10%=85, ±20%=65, ±30%=40, >30%=20
- AI Hint: null (fully system-calculated)
- Default Weight: 0.50
- Assessment Responsibility: `SystemCalculated`

**BUD-2.2.5: Early Warning Effectiveness**
- Type: Qualitative
- Assessment: scale_5 ("Did outside counsel proactively flag budget risks before overruns materialized?")
- Audience: `InHouse`
- AI Hint: "Review correspondence for instances where outside counsel alerted the client to budget risks before they materialized as invoice surprises."
- Default Weight: 0.50
- Assessment Responsibility: `InHouse`

#### Area 3: Outcome Success (2 KPIs)

**OUT-3.1.1: Outcome vs. Target**
- Type: Qualitative (with quantitative elements for litigation)
- Assessment: scale_5 ("To what extent did the matter outcome meet the defined success criteria?")
- Audience: `Both` (in-house and outside counsel)
- AI Hint: "Compare the matter resolution against the stated objectives and success criteria in the performance profile."
- Default Weight: 0.50
- Assessment Responsibility: `Both`

**OUT-3.2.1: Cycle Time vs. Expected Duration**
- Type: Quantitative
- Calculation: `days_between` — actual duration vs. profile.expectedDurationDays
- Normalization: ±10%=100, ±20%=80, ±30%=60, ±50%=40, >50%=20
- AI Hint: null
- Default Weight: 0.50
- Assessment Responsibility: `SystemCalculated`

---

## 6. Input Collection Architecture

### 6.1 Input Source Types

| Source Type | Choice Value | Confidence Weight | Trigger | Description |
|------------|:------------:|:-----------------:|---------|-------------|
| `System_Calculated` | 100000000 | 1.0 | Invoice processing, matter lifecycle events | Auto-computed from platform data |
| `Assessment_InHouse` | 100000001 | 0.8 | Assessment submission by in-house counsel | Practitioner judgment from managing attorney |
| `Assessment_OutsideCounsel` | 100000002 | 0.8 | Assessment submission by outside counsel | Practitioner judgment from assigned firm |
| `AI_Derived` | 100000003 | 0.7 | AI playbook execution as part of assessment | AI evaluation using matter documents and data |

### 6.2 Pattern A: System-Calculated Inputs

**Producer**: `IScorecardInputService`

**Integration Point**: `sprk_invoice` entity (Invoice Approval trigger)

**Architecture**: Thin Dataverse plugin → Service Bus background job

**Flow:**

```csharp
// 1. Dataverse Plugin (thin, <50ms, ADR-002 compliant)
public class InvoiceApprovalPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var invoice = GetInvoiceFromContext();

        // Only trigger if invoice approved
        if (invoice.StatusCode != ApprovedStatus) return;

        var matter = invoice.Matter;
        if (matter.PerformanceProfile == null) return;

        // Queue background job via Service Bus
        QueueJob(new JobContract
        {
            JobType = "scorecard-input-production",
            SubjectId = matter.Id,
            Metadata = new { InvoiceId = invoice.Id }
        });
    }
}

// 2. Background Job in BFF API
public class ScorecardInputProductionJob : IBackgroundJob
{
    public async Task ExecuteAsync(JobContract job)
    {
        var matterId = job.SubjectId;

        // 1. Load matter's active profile KPI configs
        // 2. Identify quantitative KPIs with SystemCalculated responsibility
        // 3. Use ScorecardDataResolver to retrieve current values
        // 4. Evaluate formulas and apply normalization bands
        // 5. Create sprk_scorecardkpiinput records with source type System_Calculated
        await _scorecardInputService.ProduceSystemInputsAsync(matterId);

        // 6. Trigger scoring recalculation
        await _scorecardService.RecalculateScorecardAsync(matterId);

        // 7. Set matter flag for nightly rollup
        await SetScorecardChangedFlagAsync(matterId);
    }
}
```

**Service Bus Registration:**
- Job Type: `scorecard-input-production`
- Handler: `ScorecardInputProductionJob`

### 6.3 Pattern B: Practitioner Assessment Inputs

**Producer**: `IAssessmentService`

When an assessment is generated (see §7 for triggers), the service:

1. Loads the matter's active profile KPI configs
2. Filters KPIs by `sprk_assessmentresponsibility`:
   - **In-house assessment**: Include KPIs where responsibility = `InHouse` OR `Both`
   - **Outside counsel assessment**: Include KPIs where responsibility = `OutsideCounsel` OR `Both`
   - **System-calculated**: Excluded from questionnaires (auto-produced)
3. Builds separate questionnaires for in-house and outside counsel (typically 1-3 questions each)
4. Delivers questionnaires via Outlook adaptive card and/or in-app form (see §8)
5. On response submission:
   - User clicks **Submit** button (not auto-submit)
   - System validates all required questions answered
   - Maps each answer to the corresponding KPI's `scoreMapping` or `options[].score`
   - Creates `sprk_scorecardkpiinput` records with appropriate source type
   - Sets `sprk_isdraft = false`, `sprk_submittedon = now`, `sprk_submittedby = user`
6. Triggers scoring engine recalculation

**Draft Save**: Assessments support auto-save as drafts (`sprk_isdraft = true`). Scoring only triggers after explicit Submit.

**Example:**
```
Matter #2024-0892 has 4 KPIs:
├─ OCG-1.1.1: Invoice Compliance (SystemCalculated) → not in questionnaire
├─ OCG-1.3.3: Responsiveness (InHouse) → in-house assessment only
├─ BUD-2.2.5: Early Warning (InHouse) → in-house assessment only
└─ OUT-3.1.1: Outcome vs Target (Both) → both assessments

Result:
- In-house receives 3 questions (Responsiveness, Early Warning, Outcome)
- Outside counsel receives 1 question (Outcome)
- System auto-produces 1 input (Invoice Compliance)
```

### 6.4 Pattern C: AI-Derived Inputs

**Producer**: `ScorecardAiAssessmentService`

Triggered as part of the assessment lifecycle (after the assessment record is created):

1. Collects the `sprk_aievaluationhint` values from all AI-evaluable KPIs active on the matter's profile
2. Assembles them as structured context for the Scorecard Assessment playbook
3. Gathers matter context: recent documents (via SPE/SpeFileStore), recent correspondence, invoice summaries, matter metadata
4. Submits the playbook execution request to `AnalysisOrchestrationService` with a single consolidated call
5. Parses the structured playbook output (JSON with per-KPI scores and justifications)
6. Creates `sprk_scorecardkpiinput` records with source type `AI_Derived`

This follows the existing playbook execution pattern from ADR-013. No new AI endpoints or handlers — the scorecard module is a consumer of the existing AI infrastructure.

---

## 7. Assessment Triggers & Lifecycle

### 7.1 Trigger Configuration

Assessment triggers are configured on the performance profile. Each trigger type produces an assessment scoped to specific areas:

| Trigger | Configuration Field | Default Scope | Assessment Audience |
|---------|-------------------|---------------|-------------------|
| **Invoice Receipt** | `sprk_triggeronInvoice` (boolean) | OCG + Budget areas | In-house only (system data supplements automatically) |
| **Matter Status Change** | `sprk_triggeronstatuschange` (multi-select) | Outcome area (+ all areas on Close) | In-house + Outside Counsel |
| **Scheduled** | `sprk_assessmentcadence` (choice) | All three areas | In-house + Outside Counsel |
| **Manual** | Always available via API/UI | Configurable at request time | Configurable at request time |

### 7.2 Trigger Processing

#### Invoice Receipt Trigger

```
Invoice Processed (Financial Intelligence module)
  │
  ├─ Check: Does matter have active performance profile
  │  with sprk_triggeronInvoice = true?
  │  │
  │  NO → Stop
  │  │
  │  YES → ScorecardInputProducerService writes
  │         system-calculated inputs for budget/OCG KPIs
  │         │
  │         └─ Check: Are there qualitative KPIs in
  │            OCG/Budget areas needing human input AND
  │            last assessment > 30 days ago?
  │            │
  │            NO → Trigger scoring engine only
  │            │
  │            YES → Create focused assessment
  │                  (OCG + Budget scope only)
```

#### Status Change Trigger

**Integration Point**: `sprk_matter.sprk_status` field (Matter Status Change)

**Architecture**: Thin Dataverse plugin → Service Bus background job

```csharp
// Dataverse Plugin on sprk_matter (Post-Update)
public class MatterStatusChangePlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var matter = GetMatterFromContext();
        var oldStatus = GetPreImageStatus();
        var newStatus = matter.Status;

        if (oldStatus == newStatus) return; // No status change

        var profile = matter.PerformanceProfile;
        if (profile == null) return; // No performance tracking

        // Check if this status change is a trigger
        if (!profile.TriggerOnStatusChange.Contains(newStatus)) return;

        // Queue assessment generation
        QueueJob(new JobContract
        {
            JobType = "assessment-generation",
            SubjectId = matter.Id,
            Metadata = new
            {
                TriggerType = "StatusChange",
                OldStatus = oldStatus,
                NewStatus = newStatus
            }
        });
    }
}
```

**Assessment Scope Logic:**
```
Is new status "Closed" or equivalent?
  │
  YES → Create comprehensive final assessment
  │     (all three areas, score status → Final)
  │
  NO → Create milestone assessment
        (Outcome leading indicators only)
```

**Service Bus Registration:**
- Job Type: `assessment-generation`
- Handler: `AssessmentGenerationJob`

#### Scheduled Trigger

```
Scheduled Job (runs daily, checks cadence per profile)
  │
  ├─ Query: Active matters with performance profiles
  │  where assessment is due based on cadence
  │  │
  └─ For each: Create comprehensive assessment
               (all three areas)
```

### 7.3 Assessment Generation Service

`IAssessmentGenerationService` responsibilities:

1. **Scope determination** — Based on trigger type and profile config, determine which scorecard areas and specific KPIs are in scope
2. **Gap analysis** — For each in-scope KPI, determine the input channel: Does system data exist? If yes, write system inputs and exclude from questionnaire. If no, include in questionnaire.
3. **Question assembly** — Extract `questions` blocks from calculation definitions of KPIs requiring human input. Group by audience (in-house vs. outside counsel).
4. **Assessment creation** — Create the `sprk_performanceassessment` record with scope, respondents, and expiration
5. **Delivery dispatch** — Send questionnaires via configured channels (Outlook adaptive card, in-app notification)
6. **AI trigger** — If any KPIs have AI evaluation hints, submit the Scorecard Assessment playbook via `AnalysisOrchestrationService`

---

## 8. Assessment Delivery & Collection

### 8.1 Outlook Actionable Messages (Primary Channel)

Assessments are delivered as Outlook Actionable Messages containing adaptive cards. This enables practitioners to complete assessments entirely within their email client.

**Flow:**

```
Assessment Created
  │
  ▼
AssessmentDeliveryService
  │
  ├─ Build adaptive card JSON from assessment questions
  │  (dynamically generated from KPI calculation definitions)
  │
  ├─ Wrap in Outlook Actionable Message format
  │  with Action.Http pointing to callback endpoint
  │
  ├─ Include signed token for authentication
  │  (time-limited, scoped to this assessment)
  │
  └─ Send via Microsoft Graph
     (separate messages to in-house and outside counsel)
```

**Adaptive Card Structure:**

```
┌─────────────────────────────────────────┐
│  SPAARKE PERFORMANCE ASSESSMENT         │
│  Matter: Smith v. Acme Corp             │
│  Period: Q4 2025                        │
│─────────────────────────────────────────│
│                                         │
│  OCG COMPLIANCE                         │
│                                         │
│  How responsive has outside counsel     │
│  been to inquiries and requests?        │
│  ○ Exceptional                          │
│  ○ Exceeds Expectations                 │
│  ○ Meets Expectations                   │
│  ○ Below Expectations                   │
│  ○ Very Slow / Unresponsive             │
│                                         │
│  BUDGET COMPLIANCE                      │
│                                         │
│  How is budget consumption trending?    │
│  🟢 On Track  🟡 Needs Attention  🔴 At Risk│
│                                         │
│  Did outside counsel proactively flag   │
│  budget risks?                          │
│  ○ Exceptional  ○ Exceeds  ○ Meets     │
│  ○ Below        ○ Very Poor            │
│                                         │
│  [Submit Assessment]                    │
│                                         │
└─────────────────────────────────────────┘
```

**Callback endpoint**: `POST /api/v1/assessments/{assessmentId}/responses`

The callback receives the adaptive card submission payload, validates the signed token, maps responses to KPI input records, and triggers the scoring engine.

### 8.2 In-App Assessment (Secondary Channel)

For users working within Spaarke, assessments are surfaced via:

1. **Matter form notification** — A banner or indicator on the matter main form showing pending assessments
2. **Assessment panel** — A VisualHost-rendered panel or dialog displaying the same questions as the adaptive card, with the same response options
3. **Assessment dashboard** — A list view (PCF dataset control) showing all pending assessments for the current user across matters

The in-app channel uses the same question assembly logic and writes to the same `sprk_scorecardkpiinput` entity.

### 8.3 Outside Counsel Access

Outside counsel may not have direct access to Spaarke. Assessment delivery to outside counsel uses:

1. **Primary**: Outlook adaptive card (works regardless of Spaarke access)
2. **Fallback**: Email with a deep-link to a lightweight response form (authenticated via signed token, no Spaarke login required)

---

## 9. Scoring Engine

### 9.1 Service Interface

```
IScorecardScoringService
  ├── ScoreKpi(matterId, kpiDefinitionId) → ScoredKpiResult
  ├── ScoreMatter(matterId) → MatterScorecardResult
  └── RecalculateScorecard(matterId) → void (writes to Dataverse)
```

### 9.2 Input Resolution Rules

When multiple inputs exist for the same KPI on the same matter, the scoring engine resolves them using these rules (applied in order):

| Priority | Rule | Rationale |
|----------|------|-----------|
| 1 | Prefer `system_calculated` when available | Highest confidence, data-driven |
| 2 | When system and assessment inputs both exist, use system as primary score; flag divergence >20 points for review | Assessment validates system data |
| 3 | **When bilateral assessments exist** (both in-house AND outside counsel for KPIs with `assessmentresponsibility = Both`), compute weighted average: 60% in-house / 40% outside counsel | Client perspective weighted higher |
| 4 | When multiple same-source inputs exist, use most recent non-superseded input | Recency preferred |
| 5 | Apply `confidenceWeight` from input source type to the resolved score | Propagates confidence to composite |
| 6 | Mark older inputs as `sprk_issuperseded = true` | Maintains audit trail |

**Note**: Most KPIs have distinct responsibility (InHouse OR OutsideCounsel OR SystemCalculated). Only KPIs with `assessmentresponsibility = Both` produce bilateral inputs requiring weighted averaging.

### 9.3 Scoring Calculation Flow

```
RecalculateScorecard(matterId):
  │
  ├─ 1. Load performance profile + profile KPI configs
  │
  ├─ 2. Load all non-superseded inputs for this matter
  │     grouped by KPI definition
  │
  ├─ 3. For each active KPI:
  │     ├─ Apply resolution rules (§9.2)
  │     ├─ Result: resolved score (0-100) + effective confidence
  │     └─ If no inputs exist: KPI score = null (not zero)
  │
  ├─ 4. For each scorecard area (OCG, Budget, Outcome):
  │     ├─ Collect resolved scores for KPIs in this area
  │     ├─ Exclude KPIs with null scores
  │     ├─ Compute: area_score = Σ(kpi_score × kpi_weight) / Σ(kpi_weight)
  │     │  WHERE kpi_score IS NOT NULL
  │     └─ If no KPIs have scores: area_score = null
  │
  ├─ 5. Compute overall composite:
  │     overall = Σ(area_score × area_weight) / Σ(area_weight)
  │     WHERE area_score IS NOT NULL
  │
  ├─ 6. Compute confidence/data richness:
  │     data_richness = KPIs_with_inputs / total_active_KPIs × 100
  │     confidence_level = High (≥80%) | Medium (50-79%) | Low (<50%)
  │
  ├─ 7. Determine grade:
  │     A (90-100) | B (75-89) | C (60-74) | D (40-59) | F (0-39)
  │
  ├─ 8. Write/update sprk_matterscorecard (live record)
  │
  └─ 9. Queue rollup recalculation for affected orgs/persons
        via Service Bus (JobType: "scorecard-rollup")
```

### 9.4 Trigger Points

The scoring engine is invoked (in-process, not via Service Bus) when:

- A new `sprk_scorecardkpiinput` record is created
- An assessment is submitted (after all input records are written)
- An AI playbook assessment completes
- A manual recalculation is requested via API

### 9.5 Score History

A scheduled background job (`ScorecardSnapshotJob`) runs monthly to capture score trends.

**Optimization**: Delta snapshots only (not all matters)

```csharp
// Monthly Scheduled Job (1st of month, 3 AM)
public class ScorecardSnapshotJob : IScheduledJob
{
    public async Task ExecuteAsync()
    {
        var lastSnapshotDate = GetLastSnapshotDate(); // e.g., 2026-01-01

        // 1. Query matters with scorecards modified since last snapshot
        var changedScorecards = await GetScorecardsWhere(
            s => s.ModifiedOn > lastSnapshotDate && !s.IsSnapshot
        );

        // 2. Clone each as snapshot
        foreach (var scorecard in changedScorecards)
        {
            var snapshot = scorecard.Clone();
            snapshot.IsSnapshot = true;
            snapshot.SnapshotDate = DateTime.UtcNow.Date; // 2026-02-01
            await CreateAsync(snapshot);
        }

        // 3. Similarly snapshot changed performance rollups
        var changedRollups = await GetRollupsWhere(
            r => r.ModifiedOn > lastSnapshotDate && !r.IsSnapshot
        );

        foreach (var rollup in changedRollups)
        {
            var snapshot = rollup.Clone();
            snapshot.IsSnapshot = true;
            snapshot.SnapshotDate = DateTime.UtcNow.Date;
            await CreateAsync(snapshot);
        }

        // 4. Update last snapshot date
        SetLastSnapshotDate(DateTime.UtcNow.Date);
    }
}
```

**Benefits**: If a matter scorecard hasn't changed since last snapshot, skip the clone (reduces DB writes at scale).

---

## 10. Organization & Person Rollup

### 10.1 Rollup Calculation

**Trigger**: `ScorecardRollupScheduledJob` — a nightly scheduled job (runs at 2 AM) that recalculates all dirty rollups.

**Architecture**: Scheduled background job (not reactive per-matter triggers)

**Process:**

```csharp
// Nightly Scheduled Job (2 AM)
public class ScorecardRollupScheduledJob : IScheduledJob
{
    public async Task ExecuteAsync()
    {
        // 1. Find all matters with changed scorecards
        var changedMatters = await GetMattersWhere(m => m.ScorecardChanged == true);

        // 2. Get affected organizations and persons from matter relationships
        var affectedOrgs = changedMatters.SelectMany(m => m.Organizations).Distinct();
        var affectedPersons = changedMatters.SelectMany(m => m.Persons).Distinct();

        // 3. Recalculate rollups for each affected entity
        foreach (var org in affectedOrgs)
        {
            await RecalculateOrgRollup(org.Id);
        }

        foreach (var person in affectedPersons)
        {
            await RecalculatePersonRollup(person.Id);
        }

        // 4. Clear the changed flags
        foreach (var matter in changedMatters)
        {
            matter.ScorecardChanged = false;
        }
        await SaveChangesAsync();
    }

    private async Task RecalculateOrgRollup(Guid orgId)
    {
        // 1. Query all matters for this org within time window (24 months)
        //    that have active (non-snapshot) scorecards
        // 2. Load each matter's scorecard and performance profile (for priority)
        // 3. Map priority to weight: High=1.5, Normal=1.0, Low=0.5
        // 4. For each area: weighted average = Σ(score × weight) / Σ(weight)
        // 5. Compute overall, grade, matter count
        // 6. Write/update sprk_performancerollup
    }
}
```

**Benefits:**
- One invoice approval → 1 DB write (set flag), not N Service Bus jobs
- Rollups refresh during low-traffic hours
- Reduced Service Bus load
- On-demand API can force-refresh if needed (bypass flag check)

### 10.2 Rollup Relationships

The rollup job determines which organizations and persons to update by querying the matter's relationships:

- **Organization rollup**: The matter's associated law firm (via the outside counsel relationship on the matter)
- **Person rollup**: Individual attorneys associated with the matter (via timekeeper/team member records)

A single scorecard update can trigger multiple rollup recalculations (one per associated org and person).

### 10.3 Rollup Time Windows

| Window | Use Case |
|--------|----------|
| Trailing 12 months | Recent performance focus |
| Trailing 24 months (default) | Balanced view with sufficient sample |
| All time | Complete historical picture |
| Custom range | Ad-hoc analysis |

The time window is configurable per rollup query (API parameter), with the stored rollup using the default 24-month window.

---

## 11. AI Integration

### 11.1 Playbook-Based Architecture

AI assessment is implemented as a playbook within the existing `AnalysisOrchestrationService` infrastructure (ADR-013). No new AI endpoints, handlers, or prompt management surfaces are introduced.

### 11.2 Scorecard Assessment Playbook

A new playbook record is created in Dataverse with:

- **Name**: "Scorecard Assessment"
- **Type**: Structured analysis with JSON output
- **Scope**: Matter-level (operates on a matter's documents, correspondence, and data)

The playbook's system prompt instructs the model to:
1. Receive a list of KPIs to evaluate, each with an evaluation hint
2. Receive matter context (document summaries, correspondence excerpts, billing data summaries)
3. Produce a structured JSON response with a score (1-5 scale) and brief justification for each KPI

### 11.3 AI Assessment Flow

```
ScorecardAiAssessmentService.ExecuteAsync(assessmentId):
  │
  ├─ 1. Load assessment and profile
  │
  ├─ 2. Collect AI-evaluable KPIs:
  │     SELECT kpi FROM profileKpiConfigs
  │     WHERE kpi.sprk_aievaluationhint IS NOT NULL
  │     AND kpi.sprk_isactive = true
  │
  ├─ 3. If none → mark AI status as NotApplicable, return
  │
  ├─ 4. Build playbook context:
  │     ├─ Matter metadata (type, status, duration, parties)
  │     ├─ Recent documents via SpeFileStore (last N docs)
  │     ├─ Invoice summary (if available)
  │     ├─ KPI evaluation list:
  │     │   [{ code, name, hint, responseFormat }]
  │     └─ Performance profile targets
  │
  ├─ 5. Submit to AnalysisOrchestrationService:
  │     ├─ PlaybookId: "scorecard-assessment"
  │     ├─ Context: assembled above
  │     ├─ JobType: "ai-analyze" (existing)
  │     └─ SubjectId: matterId
  │
  ├─ 6. On completion, parse structured response:
  │     {
  │       "evaluations": [
  │         {
  │           "kpiCode": "OCG-1.3.3",
  │           "score": 4,
  │           "justification": "Counsel responded to..."
  │         }
  │       ]
  │     }
  │
  ├─ 7. Map each evaluation to sprk_scorecardkpiinput:
  │     ├─ source type: AI_Derived
  │     ├─ normalized score: apply scoreMapping from calc def
  │     ├─ justification: from AI response
  │     └─ confidence: 0.7
  │
  └─ 8. Update assessment: sprk_aiassessmentstatus = Complete
        Trigger scoring engine recalculation
```

### 11.4 AI Cost Management

Per ADR-016, the AI assessment uses a single consolidated Azure OpenAI call per assessment cycle (not one call per KPI). For a typical assessment with 3-5 AI-evaluable KPIs, this is one GPT-4o call with matter context. Estimated token usage per assessment: 2,000-4,000 input tokens (context) + 500-1,000 output tokens (evaluations).

---

## 12. Error Handling & Resilience

### 12.1 Error Handling Strategy

All scorecard operations implement graceful degradation — failures in one component should not block the entire workflow.

| Scenario | Behavior | User Experience |
|----------|----------|-----------------|
| **AI assessment fails** | Log error with full context; create assessment without AI inputs; continue with human assessment | Assessment proceeds with system + human inputs only; admin receives error notification; AI inputs show as "not available" in scorecard detail |
| **Data resolver path not found** | Log warning; skip system input for this KPI; include KPI in assessment questionnaire as fallback | KPI score comes from practitioner assessment instead of system calculation |
| **Adaptive card delivery fails** | Retry 3× with exponential backoff (1s, 2s, 4s); if still fails, log error and create in-app notification | Assessment banner appears in Spaarke UI with "Email delivery failed" message; user can complete in-app |
| **Assessment submission error** | Display verbose error dialog with correlation ID and actionable guidance; preserve draft state | User retains their answers; can retry submit; error includes "Contact support with correlation ID: {guid}" |
| **Scoring engine calculation error** | Log error with full KPI context; preserve previous scorecard state; flag scorecard as "partial" | Scorecard shows last successful calculation with warning indicator; admin notification sent |
| **Rollup job failure** | Log error; retry on next nightly run; flag affected rollups as "stale" | API returns stale rollup with `lastCalculated` timestamp; UI shows "Last updated X days ago" |
| **Plugin timeout (>10s)** | Abort plugin; log timeout; queue background job instead | User operation completes; background job processes asynchronously |

### 12.2 Retry Logic

**Service Bus Jobs** (invoice processing, assessment generation, rollups):
- Max retries: 3
- Backoff: Exponential (1 min, 5 min, 15 min)
- Dead-letter queue: After 3 failures, message moves to DLQ for manual investigation

**External Calls** (Microsoft Graph for adaptive cards):
- Max retries: 3
- Backoff: Exponential (1s, 2s, 4s)
- Circuit breaker: After 5 consecutive failures, pause email delivery for 10 minutes; use in-app fallback only

**AI Playbook Calls**:
- Max retries: 2
- Backoff: Exponential (5s, 10s)
- Timeout: 60 seconds per call
- Fallback: If AI fails, assessment proceeds without AI-derived inputs

### 12.3 Logging & Diagnostics

**Structured Logging** (Application Insights):
```csharp
_logger.LogError(
    "Scoring engine failed for matter {MatterId}, KPI {KpiCode}",
    matterId,
    kpiCode,
    new
    {
        CorrelationId = correlationId,
        InputCount = inputs.Count,
        ProfileId = profileId,
        Exception = ex
    }
);
```

**Error Fields on Assessment Entity**:
- `sprk_errorlog`: JSON array of errors (full history)
- `sprk_lasterror`: User-facing message for most recent error

**Admin Dashboard Alerts**:
- Failed assessments (delivery or AI errors) → Daily digest email
- Scoring engine errors → Immediate alert
- Rollup staleness >7 days → Weekly report

---

## 13. API Endpoints

All endpoints follow ADR-001 Minimal API patterns with ADR-008 endpoint filter authorization.

### 12.1 Scorecard Endpoints (`ScorecardEndpoints.cs`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/api/v1/matters/{matterId}/scorecard` | Get current report card for a matter | Matter access |
| `GET` | `/api/v1/matters/{matterId}/scorecard/history` | Score history (monthly snapshots) | Matter access |
| `GET` | `/api/v1/matters/{matterId}/scorecard/kpis` | Detailed KPI breakdown with input provenance | Matter access |
| `POST` | `/api/v1/matters/{matterId}/scorecard/recalculate` | Trigger manual recalculation | Matter access |

### 12.2 Assessment Endpoints (`AssessmentEndpoints.cs`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `POST` | `/api/v1/matters/{matterId}/assessments` | Create and send a new assessment | Matter access |
| `GET` | `/api/v1/assessments/{assessmentId}` | Get assessment with questions | Assessment access |
| `PATCH` | `/api/v1/assessments/{assessmentId}/draft` | Save assessment draft (auto-save, sprk_isdraft=true) | Assessment access |
| `POST` | `/api/v1/assessments/{assessmentId}/submit` | Explicit submit (sets sprk_isdraft=false, triggers scoring) | Assessment access |
| `POST` | `/api/v1/assessments/{assessmentId}/responses` | Submit responses (Outlook adaptive card callback) | Signed token |
| `GET` | `/api/v1/assessments/pending` | List pending assessments for current user | User context |
| `GET` | `/api/v1/matters/{matterId}/assessments` | List all assessments for a matter | Matter access |

### 12.3 Performance Profile Endpoints (`PerformanceProfileEndpoints.cs`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/api/v1/performance-profiles/templates` | List available profile templates | Tenant access |
| `GET` | `/api/v1/performance-profiles/{profileId}` | Get profile with KPI configs | Profile access |
| `POST` | `/api/v1/matters/{matterId}/performance-profile` | Assign profile to matter (clone from template) | Matter access |
| `PUT` | `/api/v1/performance-profiles/{profileId}/kpis` | Update KPI selection and weights on a profile | Profile access |

### 12.4 Rollup Endpoints (`RollupEndpoints.cs`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/api/v1/rollups/organizations/{accountId}` | Organization (firm) rollup scorecard | Org access |
| `GET` | `/api/v1/rollups/persons/{contactId}` | Person (attorney) rollup scorecard | Person access |
| `GET` | `/api/v1/rollups/organizations/{accountId}/history` | Org rollup history | Org access |
| `GET` | `/api/v1/rollups/summary` | Dashboard summary — distributions, alerts | Tenant access |

### 12.5 KPI Catalog Endpoints (`KpiCatalogEndpoints.cs`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `GET` | `/api/v1/kpi-definitions` | List all KPI definitions (filterable by area, type) | Tenant access |
| `GET` | `/api/v1/kpi-definitions/{kpiId}` | Get KPI definition with calculation definition | Tenant access |

---

## 13. UI & Visualization

### 13.1 VisualHost Components

Score visualization is rendered via the existing VisualHost module, which provides Fluent UI v9 components. The design specifies data contracts that VisualHost consumes.

#### Matter Scorecard Card

A compact card component for the matter main form displaying the three area scores and overall grade.

**Data Contract:**

```typescript
interface MatterScorecardCardData {
  overallScore: number | null;
  grade: 'A' | 'B' | 'C' | 'D' | 'F' | null;
  ocgScore: number | null;
  budgetScore: number | null;
  outcomeScore: number | null;
  confidenceLevel: 'High' | 'Medium' | 'Low';
  dataRichnessPct: number;
  scoreStatus: 'Provisional' | 'Final';
  lastCalculated: string; // ISO datetime
  lastAssessmentDate: string | null;
  pendingAssessmentCount: number;
}
```

**Visual Elements:**
- Overall grade badge (letter grade with color)
- Three area score indicators (progress rings, gauges, or horizontal bars)
- Confidence indicator (data richness meter)
- "Pending assessment" call-to-action when assessments are due
- Drill-through to detailed view

#### Scorecard Detail View

Full report card view showing every KPI with scores and provenance.

**Data Contract:**

```typescript
interface ScorecardDetailData {
  matter: { id: string; name: string };
  scorecard: MatterScorecardCardData;
  areas: ScorecardAreaDetail[];
}

interface ScorecardAreaDetail {
  area: 'OCG_Compliance' | 'Budget_Compliance' | 'Outcome_Success';
  areaScore: number | null;
  areaWeight: number;
  kpis: KpiDetail[];
}

interface KpiDetail {
  kpiCode: string;
  kpiName: string;
  category: string;
  score: number | null;
  weight: number;
  inputSources: InputSourceIndicator[];
  trend: 'improving' | 'stable' | 'declining' | null;
  lastUpdated: string | null;
}

interface InputSourceIndicator {
  sourceType: 'system_calculated' | 'assessment_inhouse'
    | 'assessment_outsidecounsel' | 'ai_derived';
  recency: string; // ISO datetime
  confidence: number;
}
```

**Visual Elements:**
- Area-level score cards with grade and weight
- KPI table within each area: name, score bar, input source icons, trend arrow
- Input provenance indicators (icons showing which sources contributed)
- Score history sparkline (from monthly snapshots)

#### Organization / Person Rollup View

**Data Contract:**

```typescript
interface RollupViewData {
  subject: { type: 'Organization' | 'Person'; id: string; name: string };
  rollup: {
    overallScore: number | null;
    grade: string | null;
    ocgScore: number | null;
    budgetScore: number | null;
    outcomeScore: number | null;
    matterCount: number;
    timeWindowMonths: number;
  };
  matterBreakdown: MatterScoreSummary[];
}

interface MatterScoreSummary {
  matterId: string;
  matterName: string;
  priority: 'High' | 'Normal' | 'Low';
  overallScore: number | null;
  grade: string | null;
  lastAssessmentDate: string | null;
}
```

### 13.2 Assessment Completion UI

Assessment completion within the app is handled as a form-based experience (not VisualHost score rendering). This would be implemented as a dialog or side panel using standard Fluent UI v9 form components within a PCF control, presenting the dynamically generated questions from the KPI calculation definitions.

---

## 14. ADR Compliance Matrix

| ADR | Requirement | Performance Assessment Implementation |
|-----|-------------|---------------------------------------|
| ADR-001 | Minimal API + BackgroundService | Scorecard/assessment endpoints as Minimal API. Rollup and snapshot jobs as BackgroundService via Service Bus. |
| ADR-002 | No heavy plugins | No Dataverse plugins. Scoring engine runs in BFF API, triggered by API calls. |
| ADR-003 | Lean authorization seams | Authorization via endpoint filters; matter-level access checks on scorecard/assessment endpoints. |
| ADR-004 | Async job contract | Rollup recalculation and monthly snapshots use `JobContract` with `JobType: "scorecard-rollup"` and `"scorecard-snapshot"`. |
| ADR-005 | Flat storage in SPE | No SPE involvement — scorecard data lives in Dataverse. AI assessment reads documents via existing `SpeFileStore`. |
| ADR-008 | Authorization endpoint filters | All scorecard and assessment endpoints use `Add*AuthorizationFilter()` helpers. |
| ADR-009 | Redis-first caching | Matter scorecard results cached in Redis (key: `scorecard:{matterId}`) with invalidation on recalculation. Rollup results cached similarly. |
| ADR-010 | DI minimalism | **4 consolidated services**: `IScorecardService` (scoring + data resolver), `IAssessmentService` (generation + delivery + AI), `IScorecardInputService` (system-calculated production), `IScorecardRollupService` (aggregation). |
| ADR-011 | Dataset PCF over subgrids | Assessment list and KPI detail views use dataset PCF controls. |
| ADR-012 | Shared component library | Score visualizations (gauges, grade badges, trend indicators) added to shared component library for reuse. |
| ADR-013 | AI architecture | AI assessment uses existing `AnalysisOrchestrationService` with new Scorecard Assessment playbook. |
| ADR-017 | Async job status | Rollup and snapshot jobs persist status via standard `JobOutcome` pattern. |
| ADR-019 | ProblemDetails errors | All error responses use `ProblemDetails` with stable error codes. |
| ADR-021 | Fluent UI design system | All UI components use Fluent UI v9 via VisualHost. |

---

## 15. Phased Implementation Plan

### Phase 1: Foundation — Data Model & Scoring Engine

**Goal**: Establish the entity model, seed the KPI catalog, and build the scoring engine.

**Deliverables:**
- Dataverse solution with all six entities + Matter extension (§4.2)
- KPI catalog seeded with **6 MVP definitions** (2 per area) including calculation definition JSON
- 2-3 profile templates (Litigation, Transaction, General) with 3-4 KPIs each
- **`IScorecardService`** — scoring engine with input resolution, normalization, and data resolver
- `ScorecardEndpoints` — scorecard read and recalculate API endpoints
- `PerformanceProfileEndpoints` — profile template listing and matter assignment
- `KpiCatalogEndpoints` — KPI definition listing
- Unit tests for scoring engine logic (normalization, resolution, weighting)

**Dependencies**: Dataverse solution deployment.

**Validation**: Profiles can be assigned to matters. Manual KPI input via API produces correct scored output.

**Acceptance Criteria**: See §17.4 Phase 1

### Phase 2: Assessment Infrastructure

**Goal**: Build the assessment generation, delivery, and collection pipeline.

**Deliverables:**
- **`IAssessmentService`** — generation (with responsibility filtering), delivery (Outlook + in-app), and AI trigger
- `AssessmentEndpoints` — create, list, draft save, explicit submit
- Assessment callback endpoint (signed token validation, response-to-input mapping)
- Trigger processing for all four trigger types:
  - Invoice: `InvoiceApprovalPlugin` → Service Bus → `assessment-generation` job
  - Status change: `MatterStatusChangePlugin` → Service Bus → `assessment-generation` job
  - Scheduled: `ScorecardAssessmentScheduleJob` (daily check for cadence)
  - Manual: Direct API call
- Dataverse plugins deployed (`InvoiceApprovalPlugin`, `MatterStatusChangePlugin`)
- Service Bus job handler: `AssessmentGenerationJob`
- In-app assessment notification and completion form (PCF control) with draft save and explicit Submit button
- Error handling: adaptive card delivery failures → in-app fallback

**Dependencies**: Phase 1 complete. Microsoft Graph email permissions for Actionable Message delivery.

**Validation**: End-to-end flow — trigger creates assessment → adaptive card delivered to Outlook with 1-3 questions → practitioner responds in Outlook → inputs recorded → scoring engine produces updated scorecard.

**Acceptance Criteria**: See §17.4 Phase 2

### Phase 3: System-Calculated Inputs & Visualization

**Goal**: Connect the invoicing pipeline to auto-produce KPI inputs. Build the visual report card.

**Deliverables:**
- **`IScorecardInputService`** — system-calculated input production
- `InvoiceApprovalPlugin` deployed (triggers `scorecard-input-production` Service Bus job)
- Service Bus job handler: `ScorecardInputProductionJob`
- System-calculated input production for Budget Variance (BUD-2.1.1) and Invoice Compliance (OCG-1.1.1) KPIs
- Data resolver implementations for MVP paths
- VisualHost data contracts and component configurations for:
  - Matter Scorecard Card (compact form view with 3 area scores + overall grade)
  - Scorecard Detail View (full KPI breakdown with input provenance)
- Redis caching for scorecard results (`scorecard:{matterId}` keys)
- Cache invalidation on scorecard recalculation

**Dependencies**: Phase 2 complete. Financial Intelligence module (`sprk_invoice` entity) availability.

**Validation**: Invoice approved → system inputs auto-produced → scorecard updated without human intervention. Scorecard card renders on matter form with live scores.

**Acceptance Criteria**: See §17.4 Phase 3

### Phase 4: Rollup & History

**Goal**: Aggregate scores to org/person level. Enable score trending over time.

**Deliverables:**
- **`IScorecardRollupService`** — weighted average computation with priority
- `ScorecardRollupScheduledJob` — **nightly scheduled job** (2 AM) that recalculates rollups for matters with `sprk_scorecardchanged = true`
- `ScorecardSnapshotJob` — **monthly scheduled job** (1st of month, 3 AM) that creates delta snapshots for changed scorecards only
- `RollupEndpoints` — org and person rollup queries
- VisualHost components for organization/person rollup view with matter breakdown
- Score history/trend visualization (sparklines from snapshot data)
- Redis caching for rollup results (`rollup:org:{orgId}`, `rollup:person:{personId}`)

**Dependencies**: Phase 3 complete. Matter-to-organization and matter-to-person relationships in Dataverse.

**Validation**: Matter scorecard updated → flag set → nightly job recalculates org rollup within 24 hours. Firm-level report card correctly aggregates across 10+ matters with priority weighting. Score history shows monthly trend (3+ snapshots).

**Acceptance Criteria**: See §17.4 Phase 4

### Phase 5: AI Assessment

**Goal**: Integrate AI evaluation into the assessment lifecycle.

**Deliverables:**
- Scorecard Assessment playbook created in Dataverse
- AI trigger integrated into `IAssessmentService`
- AI evaluation hints populated on applicable KPI definitions (OCG-1.1.1, OCG-1.3.3, BUD-2.2.5)
- AI status tracking on assessment records (`sprk_aiassessmentstatus`, `sprk_aijobid`)
- Integration with existing `AnalysisOrchestrationService` (single consolidated call per assessment)
- Error handling: AI failures gracefully degrade (assessment proceeds without AI inputs)
- AI input provenance display in VisualHost scorecard detail view

**Dependencies**: Phase 2 complete. AI infrastructure (ADR-013) operational.

**Validation**: Assessment triggered → AI playbook execution queued → AI evaluates Responsiveness KPI (OCG-1.3.3) → AI-derived input recorded with justification → scorecard updated with AI contribution visible in detail view. AI failure → assessment completes without AI inputs, error logged.

**Acceptance Criteria**: See §17.4 Phase 5

---

## 17. Testing Strategy

### 17.1 Unit Tests

**Scoring Engine** (`IScorecardService`):
- All formula types × normalization bands
  - `variance_percentage`: Test ±5%, ±10%, ±20%, ±30%, >30% bands
  - `ratio`: Test 0%, 25%, 50%, 75%, 100%, >100% ranges
  - `days_average`: Test boundary values for all bands
- Input resolution rules
  - System preference over assessment
  - Bilateral weighting (60/40 for `Both` responsibility KPIs)
  - Most recent when multiple same-source inputs
  - Confidence weight propagation
- Composite scoring
  - Null handling (missing area scores)
  - Weight normalization (weights sum to 1.0)
  - Grade assignment (A-F thresholds)
- Edge cases
  - All KPIs null → overall score null
  - Single area has score, others null → overall = weighted average
  - Zero weight KPIs → excluded from composite

**Data Resolver** (`IScorecardService`):
- All MVP paths resolve correctly
  - `matter.invoices.total`
  - `matter.invoices.complianceRate`
  - `matter.budget.approved`
  - `profile.expectedBudget`
  - `profile.expectedDurationDays`
- Error cases
  - Path not found → returns null, logs warning
  - Matter has no invoices → returns 0
  - Profile has no budget → returns null

**Assessment Question Assembly** (`IAssessmentService`):
- Responsibility filtering
  - `InHouse` KPIs → in-house questionnaire only
  - `OutsideCounsel` KPIs → OC questionnaire only
  - `Both` KPIs → both questionnaires
  - `SystemCalculated` → neither questionnaire
- Question format rendering
  - `scale_5` → 5 radio buttons with labels
  - `traffic_light` → 3 options (green/yellow/red)
  - All formats map to 0-100 score correctly

### 17.2 Integration Tests

**Invoice Trigger End-to-End**:
```
sprk_invoice created with StatusCode=Approved
  → Plugin fires
  → Service Bus job queued
  → ScorecardInputProductionJob executes
  → sprk_scorecardkpiinput created (System_Calculated)
  → Scoring engine recalculates
  → sprk_matterscorecard updated
  → sprk_scorecardchanged flag set on matter
```

**Status Change Trigger End-to-End**:
```
sprk_matter.sprk_status changed to "Closed"
  → Plugin fires
  → Service Bus job queued
  → AssessmentGenerationJob executes
  → sprk_performanceassessment created
  → Adaptive cards sent to in-house and OC
  → Assessment records created with isdraft=true
```

**Assessment Submission Flow**:
```
User opens assessment (GET /api/v1/assessments/{id})
  → Auto-save draft (PATCH /api/v1/assessments/{id}/draft)
  → User edits answers (PATCH draft again)
  → User clicks Submit (POST /api/v1/assessments/{id}/submit)
  → Validation: all required questions answered
  → sprk_scorecardkpiinput records created
  → sprk_isdraft = false, sprk_submittedon = now
  → Scoring engine triggered
  → sprk_matterscorecard updated
```

**Nightly Rollup Job**:
```
10 matters have sprk_scorecardchanged = true
  → ScorecardRollupScheduledJob executes at 2 AM
  → Affected orgs/persons identified (5 orgs, 12 persons)
  → Rollups recalculated with weighted averages
  → sprk_performancerollup records updated
  → Flags cleared on matters
```

### 17.3 Performance Tests

**Nightly Rollup Job at Scale**:
- **Scenario**: 1,000 matters changed in one day
- **Expected**: Rollup recalculation completes in <10 minutes
- **Metrics**: Query time per org, write time per rollup

**Monthly Snapshot Job**:
- **Scenario**: 5,000 active matters, 40% changed since last snapshot (2,000 clones)
- **Expected**: Snapshot creation completes in <15 minutes
- **Metrics**: Query time, clone operation time

**Concurrent Assessment Submissions**:
- **Scenario**: 20 users submit assessments simultaneously
- **Expected**: No deadlocks, all submissions succeed, scorecards update correctly
- **Metrics**: Response time p50/p95/p99, error rate

**Data Resolver Performance**:
- **Scenario**: Scorecard with 4 KPIs, each requiring invoice aggregation
- **Expected**: All data resolver calls complete in <500ms total
- **Metrics**: Individual path resolution time, cache hit rate

### 17.4 Acceptance Criteria

#### **Phase 1: Foundation**
- ✅ Profile template created with 4 KPIs (1 OCG, 2 Budget, 1 Outcome)
- ✅ Profile assigned to matter via API
- ✅ Manual KPI input created via API → scorecard calculates correct composite scores
- ✅ Scorecard API returns matter report card with area scores, overall score, and grade
- ✅ Grade thresholds correct (A=90-100, B=75-89, C=60-74, D=40-59, F=0-39)

#### **Phase 2: Assessments**
- ✅ Invoice approved → assessment generated for in-house counsel
- ✅ Adaptive card delivered to Outlook with 2 questions
- ✅ User opens adaptive card, answers questions, clicks Submit
- ✅ sprk_scorecardkpiinput records created with source type Assessment_InHouse
- ✅ Scorecard updated with new scores from assessment
- ✅ Draft assessment saved → reopened in-app → edited → submitted
- ✅ sprk_submittedby, sprk_submittedon populated correctly

#### **Phase 3: System Inputs & Visualization**
- ✅ Invoice approved → system inputs auto-created for Budget Variance KPI
- ✅ No assessment questions sent for SystemCalculated KPIs
- ✅ Scorecard updated without human intervention
- ✅ VisualHost scorecard card renders on matter form with live scores
- ✅ Drill-through to detail view shows KPI breakdown with input provenance

#### **Phase 4: Rollups & History**
- ✅ Matter scorecard updated → sprk_scorecardchanged flag set
- ✅ Nightly rollup job recalculates org rollup within 24 hours
- ✅ Firm-level report card shows weighted average across 10 matters
- ✅ Priority weighting applied correctly (High=1.5×, Normal=1.0×, Low=0.5×)
- ✅ Monthly snapshot job creates history records for changed scorecards only
- ✅ Score history visualized as trend (3+ monthly snapshots)

#### **Phase 5: AI Assessment**
- ✅ Assessment triggered → AI playbook execution queued
- ✅ AI evaluates Responsiveness KPI (OCG-1.3.3)
- ✅ AI-derived input created with normalized score and justification
- ✅ Scorecard detail view shows AI contribution with provenance icon
- ✅ AI failure → assessment proceeds without AI inputs, error logged

---

## Appendix A: Glossary

| Term | Definition |
|------|-----------|
| **KPI Catalog** | The full library of available KPI definitions (`sprk_scorecardkpidefinition`) |
| **Performance Profile** | A configured set of KPIs with weights, assigned to a matter or used as a reusable template |
| **Profile KPI Config** | A junction record linking a specific KPI to a profile with weight and threshold overrides |
| **Assessment** | A single evaluation cycle for a matter that collects practitioner and AI inputs |
| **KPI Input** | A single data point contributing to a KPI score, tagged with its source type |
| **Matter Scorecard** | The computed report card for a matter — three area scores plus overall composite |
| **Performance Rollup** | Aggregated scores at the organization (law firm) or person (attorney) level |
| **Calculation Definition** | JSON on a KPI definition record that specifies how the KPI is scored — formula, normalization, assessment questions |
| **AI Evaluation Hint** | Natural language guidance on a KPI definition that instructs the AI playbook how to evaluate this KPI |
| **Normalization Band** | A mapping from raw metric values to the 0-100 score scale |

---

## Appendix B: Configuration Checklist for New Deployments

1. ☐ Deploy Dataverse solution with all entities (6 core + Matter extension)
2. ☐ Seed KPI catalog (6 MVP definitions with calculation definitions)
3. ☐ Create profile templates (Litigation, Transaction, General) with 3-4 KPIs each
4. ☐ Configure Outlook Actionable Message provider registration
5. ☐ Register Scorecard Assessment playbook in Dataverse
6. ☐ Configure scheduled jobs:
   - Nightly rollup job (2 AM daily): `ScorecardRollupScheduledJob`
   - Monthly snapshot job (1st of month, 3 AM): `ScorecardSnapshotJob`
7. ☐ Set default rollup time window (24 months)
8. ☐ Configure signed token secret for assessment callbacks
9. ☐ Register Service Bus job types and handlers:
   - `scorecard-input-production` → `ScorecardInputProductionJob`
   - `assessment-generation` → `AssessmentGenerationJob`
10. ☐ Deploy Dataverse plugins:
    - `InvoiceApprovalPlugin` on `sprk_invoice` (Post-Update)
    - `MatterStatusChangePlugin` on `sprk_matter` (Post-Update)
11. ☐ Verify Redis cache configuration for scorecard keys (`scorecard:{matterId}`, `rollup:org:{orgId}`, `rollup:person:{personId}`)
12. ☐ Configure Application Insights logging for error tracking

---

*This document is a design specification. Implementation details (exact C# service interfaces, Dataverse solution XML, adaptive card JSON templates) will be developed during project task execution following the project pipeline workflow defined in CLAUDE.md.*
