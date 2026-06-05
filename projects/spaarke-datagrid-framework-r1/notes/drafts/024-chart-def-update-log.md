# Task 024 — Chart-Def Drillthrough Target Update Log

> **Task**: 024-visualhost-chart-def-updates
> **Date**: 2026-06-01
> **Executor**: Claude Code (task-execute, MINIMAL rigor)
> **Scope**: Update `sprk_drillthroughtarget` on two `sprk_chartdefinition` records to point at the new Custom Page web resources scaffolded in task 023.

---

## Pre-conditions

- Task 023 (commit `f1f10bf9`) scaffolded two Custom Page web resources:
  - `sprk_kpiassessmentspage.html` (built from `src/solutions/sprk_kpiassessmentspage/`)
  - `sprk_invoicespage.html` (built from `src/solutions/sprk_invoicespage/`)
- Task 025 (next) will deploy these pages to Dataverse. This task pre-stages the chart-def references so CardChrome opens the new pages once deployed.
- VisualHost PCF code untouched (NFR-06).

---

## Before / After Table

| Chart | `sprk_chartdefinitionid` | Before `sprk_drillthroughtarget` | After `sprk_drillthroughtarget` |
|---|---|---|---|
| Matter Health | `a8b8df8b-f359-f111-a825-3833c5d9bcab` | `sprk_kpiassessment` | `sprk_kpiassessmentspage.html` |
| Budget Performance | `7bf5b79e-f359-f111-a825-3833c5d9bcab` | `sprk_invoice` | `sprk_invoicespage.html` |

---

## Execution Timeline (UTC)

| Step | Action | Result |
|---|---|---|
| 1 | `read_query` Matter Health pre-update | Before-state captured: `sprk_kpiassessment` |
| 1 | `read_query` Budget Performance pre-update | Before-state captured: `sprk_invoice` |
| 2 | `update_record` Matter Health → `sprk_kpiassessmentspage.html` | "Record updated successfully." |
| 3 | `update_record` Budget Performance → `sprk_invoicespage.html` | "Record updated successfully." |
| 4 | `read_query` Matter Health post-update | Verified: `sprk_kpiassessmentspage.html` |
| 4 | `read_query` Budget Performance post-update | Verified: `sprk_invoicespage.html` |

---

## Anomalies

None. Both records existed at expected IDs. Pre-update values were the legacy table-name targets (`sprk_kpiassessment`, `sprk_invoice`) which is consistent with the historical VisualHost behavior of opening table records. New values are Custom Page web resource names, aligning with the task 023 scaffold + task 025 deploy plan.

> Note: The original before-state values (`sprk_kpiassessment`, `sprk_invoice` — no `.html` suffix) suggest the prior CardChrome flow resolved targets as table logical names, not web resources. After this update, CardChrome will treat the target as a web resource URL (the `.html` suffix is the marker). This is the intended behavior change per FR-CON-05.

---

## Verification

Both round-trip reads confirm the new values are persisted in Dataverse. Functional verification (CardChrome expand opens the new page) is deferred to UAT in task 026, after task 025 deploys the web resources.

---

## Acceptance Criteria — Status

- [x] **Criterion 1**: Given chart-def records retrieved post-update, when reviewed, then `sprk_drillthroughtarget` shows the new web resource names. — VERIFIED via post-update `read_query`.
- [x] **Criterion 2**: Given the audit log, when reviewed, then both updates are recorded with before/after values. — THIS FILE.
