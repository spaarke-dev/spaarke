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

### Added
- `reports/v1.0.0/` directory created as placeholder for R1 report templates

---

## [1.0.0] — TBD

### Added
- Initial report templates for the Reporting module R1 release

### Notes
- Reports use Import mode with scheduled refresh (ADR aligned)
- All reports are deployed to the Power BI workspace defined by `sprk_PbiWorkspaceId` environment variable
- Dataset IDs must be registered in the `sprk_report` Dataverse entity after workspace deployment

---

*See `reports/README.md` for folder structure conventions.*
