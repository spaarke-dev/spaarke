# MatterPipeline.pbix — Build Instructions

> **Report Name**: Matter Pipeline
> **File**: `reports/v1.0.0/MatterPipeline.pbix`
> **Category**: Operational (sprk_category = 2)
> **sprk_name value**: `Matter Pipeline`
> **Status**: PLACEHOLDER — requires Power BI Desktop to create
> **Created**: 2026-03-31

---

## Purpose

Provides a visual pipeline of matters (legal cases or work items) across their lifecycle stages.
Helps operations managers identify bottlenecks, track throughput, and monitor open matter health.

---

## Data Source

**Connection Type**: Dataverse (Import mode)
**Environment URL**: Configured per deployment — do NOT hardcode

### Dataverse Tables to Import

| Table Logical Name | Display Name | Purpose |
|-------------------|--------------|---------|
| `sprk_matter` | Matter | Primary pipeline entity (stages, status, dates) |
| `businessunit` | Business Unit | BU hierarchy for RLS filtering |
| `systemuser` | User | Owner lookup for `ownerid` on matters |
| `sprk_mattertype` | Matter Type | Category/type classification (if exists) |

### Key Columns to Include

From `sprk_matter`:
- `sprk_name` — Matter title/name
- `sprk_status` or `statecode` — Current stage / status
- `sprk_opendate` — Date matter was opened
- `sprk_targetclosedate` — Target close date
- `sprk_closedate` — Actual close date (null if open)
- `ownerid` — Assigned attorney/user
- `businessunitid` — Business unit (for RLS)
- `sprk_mattertype` — Matter category

> Adapt column names to match actual `sprk_matter` schema in the dev environment.

---

## Relationships

- `sprk_matter[ownerid]` → `systemuser[systemuserid]`
- `sprk_matter[businessunitid]` → `businessunit[businessunitid]`
- `systemuser[businessunitid]` → `businessunit[businessunitid]` (for BU hierarchy filter)

---

## BU RLS Role

| Role Name | Table | DAX Filter |
|-----------|-------|------------|
| `BusinessUnitFilter` | `systemuser` | `[domainname] = USERNAME()` |

This filters matters to those owned by users in the current user's business unit hierarchy.
The BFF API sets `EffectiveIdentity.Roles = ["BusinessUnitFilter"]` in all embed tokens.

---

## Visualizations

### Page 1: Pipeline Overview

| Visual | Type | Data |
|--------|------|------|
| Open Matters by Stage | Funnel chart | Count of `sprk_matter` grouped by status/stage |
| Matters Over Time | Line chart | Count opened per month (X: date, Y: count) |
| Average Time to Close | Card | Average of `sprk_closedate - sprk_opendate` in days |
| Top 10 Open Matters | Table | Name, owner, days open, target close date |

### Page 2: Team Performance

| Visual | Type | Data |
|--------|------|------|
| Matters by Owner | Bar chart | Count per assigned user |
| On-Time vs Overdue | Donut chart | Count where `closedate <= targetclosedate` vs overdue |
| BU Breakdown | Clustered bar | Count per business unit |

### Page 3: Matter Detail

| Visual | Type | Data |
|--------|------|------|
| Slicers | Dropdown | Matter type, status, assigned owner, date range |
| Matters List | Table | All columns with conditional formatting on overdue |

---

## Formatting

- **Canvas background**: Transparent (100% transparency) — inherits Fluent v9 dark mode
- **Font**: Segoe UI throughout
- **Colors**: Follow Spaarke palette from `README.md`
  - Open matters: Spaarke Blue (`#2563EB`)
  - Closed (on time): Spaarke Teal (`#0D9488`)
  - Overdue: Spaarke Red (`#DC2626`)
  - At-risk: Spaarke Amber (`#F59E0B`)
- **Report title**: "Matter Pipeline" — Spaarke Navy (`#1E3A5F`), 20pt Segoe UI Semibold

---

## Dataverse Record (After Publishing)

After publishing to Power BI Service, create a `sprk_report` record:

| Field | Value |
|-------|-------|
| `sprk_name` | `Matter Pipeline` |
| `sprk_category` | `2` (Operational) |
| `sprk_iscustom` | `false` |
| `sprk_pbi_reportid` | `<GUID from PBI Service URL>` |
| `sprk_workspaceid` | `<workspace GUID from PBI Service URL>` |
| `sprk_datasetid` | `<dataset GUID from PBI Service settings>` |
| `sprk_description` | `Matter pipeline overview: stages, throughput, and team performance.` |

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
