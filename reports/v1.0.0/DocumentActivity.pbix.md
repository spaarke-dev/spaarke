# DocumentActivity.pbix — Build Instructions

> **Report Name**: Document Activity
> **File**: `reports/v1.0.0/DocumentActivity.pbix`
> **Category**: Documents (sprk_category = 4)
> **sprk_name value**: `Document Activity`
> **Status**: PLACEHOLDER — requires Power BI Desktop to create
> **Created**: 2026-03-31

---

## Purpose

Tracks document lifecycle activity across matters: uploads, downloads, AI processing events,
storage consumption, and sharing. Helps operations teams monitor document health and identify
inactive or over-stored matters.

---

## Data Source

**Connection Type**: Dataverse (Import mode)
**Environment URL**: Configured per deployment — do NOT hardcode

### Dataverse Tables to Import

| Table Logical Name | Display Name | Purpose |
|-------------------|--------------|---------|
| `sprk_documentevent` | Document Event | Audit log of document operations (if exists) |
| `sprk_matter` | Matter | Parent matter context |
| `businessunit` | Business Unit | BU hierarchy for RLS |
| `systemuser` | User | Actor for each document operation |

> If `sprk_documentevent` does not exist, use `sprk_matter` with document count/size fields
> or Dataverse audit log tables (`audit`). Adapt to the actual schema.

### Key Columns to Include

From `sprk_documentevent` (or equivalent):
- `sprk_name` — Document name
- `sprk_operation` — Operation type (Upload, Download, Delete, AIProcess, Share)
- `sprk_matterlookup` — Related matter
- `sprk_filesize` — File size in bytes
- `sprk_timestamp` — When the event occurred
- `ownerid` — User who performed the action
- `businessunitid` — Business unit context

From `sprk_matter` (for matter-level aggregation):
- `sprk_name`, `sprk_documentcount`, `sprk_storageusedbytes`, `businessunitid`

---

## Relationships

- `sprk_documentevent[sprk_matterlookup]` → `sprk_matter[sprk_matterid]`
- `sprk_documentevent[ownerid]` → `systemuser[systemuserid]`
- `systemuser[businessunitid]` → `businessunit[businessunitid]`

---

## Calculated Measures (DAX)

```dax
-- Total Storage (GB)
Total Storage GB =
    DIVIDE(SUM(sprk_matter[sprk_storageusedbytes]), 1073741824, 0)

-- Documents Uploaded (Last 30 Days)
Uploads Last 30d =
    CALCULATE(
        COUNTROWS(sprk_documentevent),
        sprk_documentevent[sprk_operation] = "Upload",
        DATESINPERIOD(sprk_documentevent[sprk_timestamp], TODAY(), -30, DAY)
    )

-- AI Processing Rate
AI Processing Rate % =
    DIVIDE(
        CALCULATE(COUNTROWS(sprk_documentevent), sprk_documentevent[sprk_operation] = "AIProcess"),
        COUNTROWS(sprk_documentevent),
        0
    ) * 100
```

---

## BU RLS Role

| Role Name | Table | DAX Filter |
|-----------|-------|------------|
| `BusinessUnitFilter` | `systemuser` | `[domainname] = USERNAME()` |

Filters document events to those performed by users in the current user's BU hierarchy.

---

## Visualizations

### Page 1: Activity Overview

| Visual | Type | Data |
|--------|------|------|
| Operations Over Time | Line chart | Count per day/week by operation type |
| Operations by Type | Donut chart | Upload vs Download vs Delete vs AIProcess vs Share |
| Most Active Users | Bar chart | Top 10 users by operation count |
| Recent Activity Feed | Table | Latest 50 events: document, user, operation, timestamp |

### Page 2: Storage & Volume

| Visual | Type | Data |
|--------|------|------|
| Total Storage Used | KPI card | Sum of storage in GB vs previous period |
| Storage by Matter | Bar chart | Top 20 matters by storage consumed |
| Document Count Trend | Line chart | Total documents added over time |
| Large Files | Table | Documents over 10MB with matter and owner |

### Page 3: AI Processing

| Visual | Type | Data |
|--------|------|------|
| AI Processing Rate | Gauge | % of uploaded docs that went through AI pipeline |
| Processing by Matter Type | Bar chart | AI operations per matter category |
| Processing Errors | Card | Count of failed AI operations (if error field exists) |

---

## Formatting

- **Canvas background**: Transparent (100% transparency)
- **Colors**:
  - Upload / new: Spaarke Blue (`#2563EB`)
  - Download / read: Spaarke Light Blue (`#93C5FD`)
  - Delete / removal: Spaarke Red (`#DC2626`)
  - AI processing: Spaarke Teal (`#0D9488`)
  - Share: Spaarke Amber (`#F59E0B`)
- **Report title**: "Document Activity" — Spaarke Navy, 20pt Segoe UI Semibold

---

## Dataverse Record (After Publishing)

| Field | Value |
|-------|-------|
| `sprk_name` | `Document Activity` |
| `sprk_category` | `4` (Documents) |
| `sprk_iscustom` | `false` |
| `sprk_pbi_reportid` | `<GUID from PBI Service URL>` |
| `sprk_workspaceid` | `<workspace GUID>` |
| `sprk_datasetid` | `<dataset GUID>` |
| `sprk_description` | `Document lifecycle tracking: uploads, downloads, AI processing, and storage consumption.` |

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
