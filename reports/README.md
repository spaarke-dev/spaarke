# reports/ — Power BI Report Templates

This folder contains versioned Power BI report templates (.pbix files) for the Spaarke
Reporting module.

## Folder Structure

```
reports/
├── README.md           — This file
├── CHANGELOG.md        — Version history of all report changes
└── v1.0.0/             — Report templates for release 1.0.0
    ├── .gitkeep        — Placeholder until .pbix files are added
    └── *.pbix          — Power BI Desktop report files
```

## Versioning Convention

Each version folder maps to a Reporting module release:

| Folder | Corresponds To | Contents |
|--------|---------------|----------|
| `v1.0.0/` | R1 initial release | Seed report templates for standard categories |
| `v1.1.0/` | Future patch | Updated templates or new reports |

When adding or modifying report files:
1. Place `.pbix` files in the appropriate version folder
2. Update `CHANGELOG.md` with what was added/changed and why
3. After publishing to Power BI Service, update the `sprk_report` Dataverse entity with the
   report GUID, workspace GUID, and dataset GUID

## Git Handling of .pbix Files

Power BI `.pbix` files are binary files. They are tracked via Git LFS (configured in
`.gitattributes`) to avoid inflating the repository with large binary diffs.

```
*.pbix filter=lfs diff=lfs merge=lfs -text
```

To work with these files:
- Ensure Git LFS is installed: `git lfs install`
- Pull LFS objects: `git lfs pull`
- Add a new report: `git add reports/v1.0.0/MyReport.pbix` (LFS handles it automatically)

## Deployment Process

Report templates are NOT automatically deployed by the solution import. After importing
SpaarkeCore, an administrator must:

1. Open each `.pbix` file in Power BI Desktop
2. Publish to the Power BI workspace configured for the environment
3. Note the report GUID, workspace GUID, and dataset GUID from the Power BI Service URL
4. Create or update the corresponding `sprk_report` record in Dataverse with those GUIDs

See `scripts/Deploy-ReportingReports.ps1` for the automated deployment script (created in
a later task).

## Constraints

- MUST use Import mode — DirectQuery and Direct Lake are not supported in R1
- MUST NOT hardcode connection strings or environment URLs in the .pbix file
- Dataset credentials are configured per-environment in the Power BI Service after publish
- Report names in `.pbix` must match the `sprk_name` value in the `sprk_report` catalog entry

---

*Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
