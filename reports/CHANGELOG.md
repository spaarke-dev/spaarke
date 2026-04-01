# Reports CHANGELOG

This file tracks all changes to Power BI report templates (.pbix files) across versions.

## Format

Each version entry follows this structure:

```
## [version] — YYYY-MM-DD

### Added
- `<filename>.pbix` — <description of report>

### Modified
- `<filename>.pbix` — <what changed and why>

### Removed
- `<filename>.pbix` — <reason for removal>

### Notes
- Any relevant notes (data model changes, RLS changes, dataset configuration)
```

---

## [Unreleased]

No unreleased changes.

---

## [1.0.0] — 2026-03-31

### Added
- `reports/v1.0.0/MatterPipeline.pbix` — Matter pipeline overview: stages, throughput, and team performance metrics. Category: Operational.
- `reports/v1.0.0/FinancialSummary.pbix` — Financial KPIs, budget utilization, and revenue trends by matter and business unit. Category: Financial.
- `reports/v1.0.0/DocumentActivity.pbix` — Document lifecycle tracking: uploads, downloads, AI processing, and storage consumption. Category: Documents.
- `reports/v1.0.0/TaskOverview.pbix` — Task status, completion rates, SLA adherence, and workload distribution. Category: Operational.
- `reports/v1.0.0/ComplianceDashboard.pbix` — Regulatory compliance metrics, SLA adherence, risk indicators, and audit trail. Category: Compliance.
- `reports/v1.0.0/README.md` — Build instructions, Spaarke color palette, BU RLS setup, and publishing workflow.
- Per-report build instructions: `MatterPipeline.pbix.md`, `FinancialSummary.pbix.md`, `DocumentActivity.pbix.md`, `TaskOverview.pbix.md`, `ComplianceDashboard.pbix.md`

### Notes
- All reports use **Import mode** with scheduled refresh (spec constraint — DirectQuery not supported in R1)
- All reports include a `BusinessUnitFilter` RLS role with DAX filter `[domainname] = USERNAME()` on the `systemuser` table
- Reports are deployed to the Power BI workspace defined by `sprk_PbiWorkspaceId` environment variable
- After publishing, create a `sprk_report` Dataverse record for each report with the workspace/report/dataset GUIDs
- Dataset credentials must be reconfigured per environment in the Power BI Service after publish
- `.pbix` files are tracked via Git LFS (`*.pbix filter=lfs diff=lfs merge=lfs -text`)
- Spaarke color palette: Navy `#1E3A5F`, Blue `#2563EB`, Teal `#0D9488`, Amber `#F59E0B`, Red `#DC2626`
- Canvas background set to transparent (100%) for Fluent v9 dark mode compatibility
- Standard reports have `sprk_iscustom = false`; tenant-authored reports use `sprk_iscustom = true`

---

*See `reports/README.md` for folder structure conventions.*
