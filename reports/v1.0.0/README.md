# reports/v1.0.0 — Standard Report Templates

> **Version**: 1.0.0
> **Release**: Spaarke Reporting Module R1
> **Status**: PLACEHOLDER — awaiting human-authored .pbix files
> **Created**: 2026-03-31

This folder contains the five standard Power BI report templates shipped with Spaarke R1.
Each `.pbix` file must be created in Power BI Desktop by a Spaarke developer, then stored here.
Actual `.pbix` files are tracked via Git LFS (see `.gitattributes`).

---

## Reports in This Release

| File | Report Name | Category | Status |
|------|-------------|----------|--------|
| `MatterPipeline.pbix` | Matter Pipeline | Operational | PLACEHOLDER |
| `FinancialSummary.pbix` | Financial Summary | Financial | PLACEHOLDER |
| `DocumentActivity.pbix` | Document Activity | Documents | PLACEHOLDER |
| `TaskOverview.pbix` | Task Overview | Operational | PLACEHOLDER |
| `ComplianceDashboard.pbix` | Compliance Dashboard | Compliance | PLACEHOLDER |

> Each `.pbix.md` companion file in this folder documents the exact build instructions for the
> corresponding report. Follow those instructions in Power BI Desktop to produce the `.pbix`.

---

## Prerequisites (Power BI Desktop Setup)

Before creating any report, complete these setup steps once:

1. **Install Power BI Desktop** (free, Windows only)
   - Download from: https://powerbi.microsoft.com/en-us/desktop/
   - Version: latest stable

2. **Connect to the Dev Dataverse Environment**
   - Data source: `Power Platform → Dataverse`
   - Server URL: `https://spaarkedev1.crm.dynamics.com`
   - Authentication: Microsoft account (your work login)
   - Connection mode: **Import** (NOT DirectQuery — MUST use Import mode per spec constraint)

3. **Configure Dataset Credentials**
   - After publishing to Power BI Service, reconfigure dataset credentials in the Service UI
   - Use an account with read access to the Dataverse entities listed per-report

4. **Apply the Spaarke Theme**
   - In Power BI Desktop: **View → Themes → Browse for themes**
   - Load the theme JSON from the block below

---

## Spaarke Color Palette

All reports MUST use this palette for consistent Spaarke branding. Apply it as a custom theme.

### Brand Colors

| Name | Hex | Usage |
|------|-----|-------|
| **Spaarke Navy** | `#1E3A5F` | Primary headers, axis labels, key text |
| **Spaarke Blue** | `#2563EB` | Primary interactive elements, links, key metrics |
| **Spaarke Light Blue** | `#93C5FD` | Secondary data series, light backgrounds |
| **Spaarke Teal** | `#0D9488` | Positive indicators, completion metrics |
| **Spaarke Amber** | `#F59E0B` | Warnings, at-risk indicators |
| **Spaarke Red** | `#DC2626` | Alerts, overdue items, critical status |
| **Spaarke Gray 900** | `#111827` | Body text (light theme) |
| **Spaarke Gray 100** | `#F3F4F6` | Card backgrounds, surface (light theme) |
| **Spaarke White** | `#FFFFFF` | Canvas background (light theme) |

### Dark Mode Colors (Transparent Background)

The Spaarke Reporting Code Page uses dark mode via Fluent v9. Set the report background to
**transparent** so it inherits the page theme:

- In Power BI Desktop: **Format page → Canvas background → Transparency = 100%**
- Keep all text in **Spaarke Gray 100** (`#F3F4F6`) for dark mode readability

### Power BI Theme JSON

Save this as `SpaarkeTheme.json` and load it in Power BI Desktop:

```json
{
  "name": "Spaarke",
  "dataColors": [
    "#2563EB",
    "#0D9488",
    "#F59E0B",
    "#DC2626",
    "#93C5FD",
    "#6EE7B7",
    "#FCD34D",
    "#FCA5A5"
  ],
  "background": "#FFFFFF",
  "foreground": "#111827",
  "tableAccent": "#2563EB",
  "visualStyles": {
    "*": {
      "*": {
        "fontFamily": [{ "value": "Segoe UI" }],
        "fontSize": [{ "value": 11 }]
      }
    },
    "title": {
      "*": {
        "color": [{ "solid": { "color": "#1E3A5F" } }],
        "fontSize": [{ "value": 14 }],
        "fontFamily": [{ "value": "Segoe UI Semibold" }]
      }
    }
  }
}
```

---

## BU RLS Role — Required in ALL Reports

Every report MUST include a Row-Level Security role named **`BusinessUnitFilter`** with a DAX
filter that restricts data to the signed-in user's business unit hierarchy.

### Steps to Add RLS in Power BI Desktop

1. In the **Modeling** ribbon, click **Manage roles**
2. Click **Create** and name the role: `BusinessUnitFilter`
3. For the **systemuser** table (or the primary entity table), add this DAX filter expression:

```dax
[domainname] = USERNAME()
```

> `USERNAME()` returns the UPN of the user whose identity is passed in the embed token
> `EffectiveIdentity` field. The BFF API sets this to the Dataverse user's `domainname`
> (UPN) filtered by their business unit.

4. For related tables that should also be filtered by BU, add equivalent DAX filters
5. Click **Save**

### Important Notes

- The role name `BusinessUnitFilter` is the value the BFF sends in `EffectiveIdentity.Roles`
- Do NOT rename this role — the BFF API hardcodes this name in token generation
- The DAX filter above applies to Import mode — the data is filtered at token generation time,
  not at query time (unlike DirectQuery)
- Test the role in Power BI Desktop: **Modeling → View as → BusinessUnitFilter** with a test UPN

---

## Publishing Workflow

After creating each `.pbix` file:

1. **Save locally**: `reports/v1.0.0/<ReportName>.pbix`
2. **Open in Power BI Desktop** and verify the `BusinessUnitFilter` RLS role is present
3. **Publish to Dev workspace** via File → Publish → Publish to Power BI
4. **Note the IDs** from the Power BI Service URL:
   - Workspace GUID: from the URL `/groups/<workspace-guid>/reports/...`
   - Report GUID: from the URL `.../reports/<report-guid>`
   - Dataset GUID: from the dataset settings URL
5. **Create a `sprk_report` record** in Dataverse dev environment with those GUIDs
6. **Verify in the Reporting Code Page** that the report appears in the dropdown and renders

See `scripts/Deploy-ReportingReports.ps1` for the automated version of steps 3-6.

---

## Git LFS

`.pbix` files are binary. They are committed via Git LFS:

```bash
git lfs install
git add reports/v1.0.0/MatterPipeline.pbix
git commit -m "feat(reports): add MatterPipeline.pbix v1.0.0"
git push
```

Ensure your Git LFS is configured before adding `.pbix` files:
```bash
git lfs track "*.pbix"
# Verify .gitattributes contains: *.pbix filter=lfs diff=lfs merge=lfs -text
```

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
