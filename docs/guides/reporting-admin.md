# Reporting Module — Administrator Guide

> **Document Version**: 1.0
> **Last Updated**: 2026-04-01
> **Module**: Reporting R1
> **Audience**: Spaarke Administrators, DevOps Engineers

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Module Configuration](#module-configuration)
3. [Environment Variables](#environment-variables)
4. [Security Roles](#security-roles)
5. [Customer Onboarding](#customer-onboarding)
6. [Report Deployment](#report-deployment)
7. [Code Page Deployment](#code-page-deployment)
8. [Dataverse Schema](#dataverse-schema)
9. [Capacity Planning](#capacity-planning)
10. [Troubleshooting](#troubleshooting)

---

## Architecture Overview

The Reporting module embeds Power BI reports into Spaarke's Model-Driven App using the "App Owns Data" pattern. No end user requires a Power BI Pro or Premium Per User license.

```
Browser (sprk_reporting Code Page)
    |
    |-- GET /api/reporting/status         (module gate check)
    |-- GET /api/reporting/embed-token    (BFF generates embed token, cached in Redis)
    |-- POST /api/reporting/export        (server-side PDF/PPTX export)
    |
BFF API (ReportingEmbedService)
    |-- Service principal (client credentials) → Power BI REST API
    |-- SP profile header (X-PowerBI-Profile-Id) → per-customer workspace isolation
    |-- EffectiveIdentity + BU roles → Row-Level Security enforcement
    |-- Redis cache (ADR-009) → embed token TTL, auto-refresh at 80%
    |
Power BI Service
    |-- Import mode datasets (scheduled refresh from Dataverse)
    |-- Per-customer workspace: "{CustomerName} - Reporting"
    |-- Per-customer SP profile: "sprk-{customerId}"
    |
Dataverse (sprk_report entity)
    |-- Report catalog (name, category, workspace ID, dataset ID, isCustom flag)
    |-- Environment variable: sprk_ReportingModuleEnabled (module gate)
```

### Deployment Models

The module supports three deployment models:

| Model | Description | Capacity | SP Profiles |
|-------|-------------|----------|-------------|
| **Multi-customer (shared)** | Multiple customers share one Entra app registration; each has an isolated workspace via SP profiles | Shared F-SKU pool | One profile per customer |
| **Dedicated customer** | Large customer gets dedicated F-SKU capacity for performance isolation | Dedicated F-SKU | One profile per customer |
| **Customer tenant** | Reporting runs inside the customer's own Entra tenant with their own Power BI capacity | Customer-managed | Service principal in customer tenant |

### BFF Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/reporting/status` | Module gate + privilege level check |
| GET | `/api/reporting/embed-token` | Generate embed token (Redis-cached) |
| GET | `/api/reporting/reports` | List reports in workspace |
| GET | `/api/reporting/reports/{id}` | Get single report |
| POST | `/api/reporting/reports` | Create report (Author+) |
| PUT | `/api/reporting/reports/{id}` | Update report catalog entry (Author+) |
| DELETE | `/api/reporting/reports/{id}` | Delete report (Admin only) |
| POST | `/api/reporting/export` | Export to PDF or PPTX |

All endpoints require authentication and the `sprk_ReportingAccess` security role claim. The module gate (404) fires before any auth check.

### Token Flow

1. User opens the Reporting Code Page (`sprk_reporting` web resource).
2. Code Page authenticates via `@spaarke/auth` bootstrap, acquires a BFF API access token.
3. Code Page calls `GET /api/reporting/embed-token?workspaceId=...&reportId=...`.
4. BFF checks Redis for a cached token; if absent, calls Power BI REST API using the service principal with the customer's SP profile header.
5. BFF applies EffectiveIdentity (user UPN + BU role) for Row-Level Security.
6. Embed token returned to Code Page; `powerbi-client-react` renders the report.
7. At 80% of the token TTL, `report.setAccessToken()` refreshes the token seamlessly — no page reload.

---

## Module Configuration

### Enabling or Disabling the Module

The Reporting module is gated by the `sprk_ReportingModuleEnabled` Dataverse environment variable.

| Value | Effect |
|-------|--------|
| `Yes` | Module is active; navigation shows Reporting; API endpoints respond normally |
| `No` or missing | Module is hidden; all `/api/reporting/*` endpoints return 404; Code Page shows "Module not available" |

To set the value manually:
1. Navigate to your Dataverse environment: `https://{org}.crm.dynamics.com`.
2. Go to **Settings** > **Advanced Settings** > **System** > **Environment Variables**.
3. Find `sprk_ReportingModuleEnabled` and set the value to `Yes` or `No`.

Alternatively, use the PAC CLI:
```powershell
pac env update-settings --environment <url> --name sprk_ReportingModuleEnabled --value Yes
```

The `Initialize-ReportingCustomer.ps1` script sets this automatically during customer onboarding.

### BFF API Configuration

The BFF reads Power BI configuration from the `PowerBi` section (environment variables use `PowerBi__` prefix). See [Environment Variables](#environment-variables) for the full reference.

---

## Environment Variables

All Power BI configuration is provided via environment variables. No workspace IDs, capacity IDs, or tenant IDs are hardcoded in source.

### BFF API Variables (`PowerBi__*`)

| Variable | Required | Description |
|----------|----------|-------------|
| `PowerBi__TenantId` | Yes | Entra ID tenant ID for the service principal |
| `PowerBi__ClientId` | Yes | App (client) ID of the Entra ID app registration |
| `PowerBi__ClientSecret` | Yes | Client secret for the service principal. Store in Azure Key Vault for production. |
| `PowerBi__AuthorityUrl` | No | Override the OAuth authority URL. Default: `https://login.microsoftonline.com/{TenantId}` |
| `PowerBi__Scope` | No | OAuth scope. Default: `https://analysis.windows.net/.default` |
| `PowerBi__ApiUrl` | No | Power BI REST API base URL. Default: `https://api.powerbi.com` |
| `Reporting__ModuleEnabled` | Yes | Set to `true` to enable the module at the BFF level (mirrors `sprk_ReportingModuleEnabled` in Dataverse) |

### Deployment Script Variables

These variables are used by the PowerShell deployment scripts:

| Variable | Required By | Description |
|----------|-------------|-------------|
| `PBI_TENANT_ID` | All scripts | Entra ID tenant ID |
| `PBI_CLIENT_ID` | All scripts | Service principal app ID |
| `PBI_CLIENT_SECRET` | All scripts | Service principal client secret |
| `PBI_WORKSPACE_ID` | `Deploy-ReportingReports.ps1` | Target Power BI workspace ID |
| `PBI_DATAVERSE_DATASOURCE_URL` | All scripts | Dataverse OData endpoint for dataset rebinding (e.g., `https://org.crm.dynamics.com/api/data/v9.2/`) |
| `DATAVERSE_URL` | All scripts | Dataverse org URL (e.g., `https://org.crm.dynamics.com`) |
| `PBI_DATASET_ID` | `Deploy-ReportingReports.ps1` | Optional — override dataset ID for all imported reports |
| `PBI_REFRESH_ENABLED` | `Deploy-ReportingReports.ps1` | Set to `false` to skip scheduling data refresh (default: `true`) |

---

## Security Roles

### Role Hierarchy

The module uses three Dataverse security roles with increasing privilege:

| Role | Schema Name | Privilege Level | Capabilities |
|------|------------|-----------------|--------------|
| **Viewer** | `sprk_ReportingAccess` | Viewer | View and interact with reports; no editing or export |
| **Author** | `sprk_ReportingAuthor` | Author | All Viewer capabilities + create reports, edit reports, save, save-as, export |
| **Admin** | `sprk_ReportingAdmin` | Admin | All Author capabilities + delete reports, manage workspace catalog |

> **Note**: `sprk_ReportingAccess` is the minimum required role. A user without this role receives a 403 response from the BFF and sees an access-denied message in the Code Page. Users with `sprk_ReportingAuthor` or `sprk_ReportingAdmin` implicitly satisfy the `sprk_ReportingAccess` check.

### Assigning Roles

**Option A — Dataverse Admin Center (UI)**:
1. Navigate to `https://{org}.crm.dynamics.com`.
2. Go to **Settings** > **Security** > **Users**.
3. Select the user.
4. Click **Manage Security Roles**.
5. Assign the appropriate `sprk_Reporting*` role.

**Option B — PAC CLI**:
```powershell
pac auth create --url https://{org}.crm.dynamics.com

# Assign Viewer role
pac org assign-user --environment https://{org}.crm.dynamics.com --user user@domain.com --role sprk_ReportingAccess

# Assign Author role
pac org assign-user --environment https://{org}.crm.dynamics.com --user user@domain.com --role sprk_ReportingAuthor

# Assign Admin role
pac org assign-user --environment https://{org}.crm.dynamics.com --user user@domain.com --role sprk_ReportingAdmin
```

### Row-Level Security (Business Unit Filtering)

All embed tokens include an `EffectiveIdentity` with the user's UPN and a BU-based RLS role (`BU_{businessunit-id}`). The Power BI datasets contain an RLS role named `BusinessUnitFilter` that filters data to the user's business unit and its children via a DAX expression on `USERNAME()`.

This is enforced by the BFF — users cannot bypass BU filtering from the client side.

---

## Customer Onboarding

Use `scripts/Initialize-ReportingCustomer.ps1` to provision a new customer's Reporting environment end-to-end.

### What the Script Does

1. Validates prerequisites (environment variables, Azure CLI authentication)
2. Creates (or reuses) a Power BI workspace named `{CustomerName} - Reporting`
3. Assigns the workspace to a capacity (if `-CapacityId` is provided)
4. Creates (or reuses) a service principal profile named `sprk-{CustomerId}`
5. Adds the SP profile as workspace Admin
6. Deploys standard product reports via `Deploy-ReportingReports.ps1`
7. Enables the Reporting module in Dataverse (`sprk_ReportingModuleEnabled = Yes`)
8. Outputs security role assignment commands for manual execution

The script is **idempotent** — safe to re-run if it fails mid-way; already-created resources are reused.

### Prerequisites

Before running the script:
- Set the required environment variables (`PBI_TENANT_ID`, `PBI_CLIENT_ID`, `PBI_CLIENT_SECRET`, `DATAVERSE_URL`, `PBI_DATAVERSE_DATASOURCE_URL`)
- Run `az login` and ensure you are authenticated to the target Azure tenant
- Ensure the service principal has Power BI tenant-level permissions (see below)
- Ensure the Spaarke Reporting managed solution is imported into the Dataverse environment

### Required Service Principal Permissions

In the Power BI Admin portal (**Tenant settings**), the service principal needs:
- **Allow service principals to use Power BI APIs** — enabled (either globally or for the SP's security group)
- **Allow service principals to create and use profiles** — enabled
- **Allow service principals to create workspaces** — enabled (for onboarding only)

In Entra ID, the app registration needs these API permissions (granted by an admin):
- `Dataset.ReadWrite.All`
- `Content.Create`
- `Workspace.ReadWrite.All`

### Running the Script

```powershell
# Dry run — preview all steps without making changes
.\scripts\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com" `
    -WhatIf

# Full onboarding on shared capacity (dev / test)
.\scripts\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com"

# Full onboarding with dedicated F2 capacity (production)
.\scripts\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com" `
    -CapacityId "00000000-0000-0000-0000-000000000000"

# Onboarding with pre-populated security role commands
.\scripts\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com" `
    -SecurityRoleUsers @("alice@contoso.com", "bob@contoso.com")
```

### Post-Onboarding Manual Step

Security role assignment is not automated. After the script completes, assign the appropriate `sprk_Reporting*` roles to users (see [Security Roles](#security-roles)).

### Rollback

If onboarding fails and you want to fully revert:
1. Delete the Power BI workspace via the Power BI Admin portal.
2. Delete the SP profile via the Power BI REST API: `DELETE https://api.powerbi.com/v1.0/myorg/profiles/{profileId}`.
3. Set `sprk_ReportingModuleEnabled` back to `No` in Dataverse environment variables.

---

## Report Deployment

Use `scripts/Deploy-ReportingReports.ps1` to import `.pbix` report templates into a customer workspace and synchronize the Dataverse report catalog.

### What the Script Does

1. Authenticates to Power BI REST API (service principal)
2. Authenticates to Dataverse (Azure CLI)
3. Imports each `.pbix` file from the report folder into the target workspace
4. Rebinds each imported dataset to the customer's Dataverse OData endpoint
5. Sets a scheduled refresh (Monday–Friday at 06:00, 12:00, 18:00 UTC)
6. Creates or updates `sprk_report` records in Dataverse for the deployed reports

Default report folder: `reports/v1.0.0` (relative to repo root).

### Standard Product Reports

Five standard reports are included in `reports/v1.0.0/`:

| Report | Category |
|--------|----------|
| Matter Pipeline | Operational |
| Financial Summary | Financial |
| Document Activity | Documents |
| Task Overview | Operational |
| Compliance Dashboard | Compliance |

### Running the Script

```powershell
# Dry run — preview without making changes
.\scripts\Deploy-ReportingReports.ps1 `
    -WorkspaceId "00000000-0000-0000-0000-000000000000" `
    -WhatIf

# Deploy default reports to dev workspace
.\scripts\Deploy-ReportingReports.ps1

# Deploy a specific version to staging
.\scripts\Deploy-ReportingReports.ps1 `
    -ReportFolder "reports/v1.2.0" `
    -WorkspaceId "abc-def-..." `
    -Environment staging `
    -DataverseOrg "https://contoso-stg.crm.dynamics.com"
```

### Report Versioning

Report templates are stored in source control under `reports/` with version subdirectories:

```
reports/
├── v1.0.0/
│   ├── MatterPipeline.pbix
│   ├── FinancialSummary.pbix
│   ├── DocumentActivity.pbix
│   ├── TaskOverview.pbix
│   └── ComplianceDashboard.pbix
└── CHANGELOG.md
```

When releasing an updated report:
1. Update the `.pbix` file in a new version folder (e.g., `reports/v1.1.0/`).
2. Update `reports/CHANGELOG.md` with the change description.
3. Run `Deploy-ReportingReports.ps1` with `-ReportFolder "reports/v1.1.0"` for each customer workspace.

---

## Code Page Deployment

Use `scripts/Deploy-ReportingCodePage.ps1` to build and deploy the Reporting Code Page (`sprk_reporting` web resource).

This script:
1. Runs `npm install` in `src/solutions/Reporting/`
2. Runs `npm run build` (Vite + `vite-plugin-singlefile` — produces a single self-contained `dist/index.html`)
3. Uploads `dist/index.html` as the `sprk_reporting` web resource to Dataverse
4. Publishes customizations

```powershell
# Deploy to dev environment
.\scripts\Deploy-ReportingCodePage.ps1 -DataverseUrl https://contoso.crm.dynamics.com

# Dry run
.\scripts\Deploy-ReportingCodePage.ps1 -DataverseUrl https://contoso.crm.dynamics.com -WhatIf
```

The `DATAVERSE_URL` environment variable is used as a fallback if `-DataverseUrl` is not provided.

This deployment is needed after any frontend change to `src/solutions/Reporting/`. It does not affect the BFF API or Dataverse schema.

---

## Dataverse Schema

### `sprk_report` Entity

The report catalog is stored in the `sprk_report` Dataverse entity. Each record represents one report available in the dropdown.

| Attribute | Type | Description |
|-----------|------|-------------|
| `sprk_reportid` | Unique Identifier (PK) | Auto-generated record ID |
| `sprk_name` | Text | Report display name shown in the dropdown |
| `sprk_category` | Text | Category group: Financial, Operational, Compliance, Documents, Custom |
| `sprk_workspaceid` | Text | Power BI workspace GUID |
| `sprk_datasetid` | Text | Power BI dataset GUID used for authoring |
| `sprk_iscustom` | Boolean | `true` for customer-created reports; `false` for standard product reports |

Records are created and updated automatically by `Deploy-ReportingReports.ps1` and by the BFF API when a user creates a report via the in-browser authoring flow.

### Environment Variables

| Schema Name | Display Name | Type | Description |
|-------------|-------------|------|-------------|
| `sprk_ReportingModuleEnabled` | Reporting Module Enabled | Text (Yes/No) | Gates the module globally. Set to `Yes` to enable, `No` to disable. |

### Security Role Structure

| Role Schema Name | Description |
|-----------------|-------------|
| `sprk_ReportingAccess` | Minimum access — read-only report viewing |
| `sprk_ReportingAuthor` | Author access — create, edit, save, export reports |
| `sprk_ReportingAdmin` | Admin access — all author capabilities plus delete |

---

## Capacity Planning

### F-SKU Sizing

The Reporting module requires Power BI Embedded capacity (F-SKU or P-SKU). Shared capacity is only appropriate for development and testing.

| SKU | vCores | Memory | Recommended Use | Approx. Cost |
|-----|--------|--------|-----------------|-------------|
| F2 | 2 | 4 GB | Dev/test, small pilot (<5 concurrent users) | ~$100/mo |
| F4 | 4 | 8 GB | Small production (5–20 concurrent users) | ~$200/mo |
| F8 | 8 | 16 GB | Medium production (20–50 concurrent users, complex reports) | ~$400/mo |
| F16 | 16 | 32 GB | Large production (50+ concurrent users) | ~$800/mo |

> **Cost estimates are approximate** and vary by Azure region and current pricing. Check the [Azure pricing calculator](https://azure.microsoft.com/en-us/pricing/calculator/) for current rates.

### Shared vs. Dedicated Capacity

| Consideration | Shared Pool | Dedicated |
|--------------|------------|-----------|
| **Isolation** | Workloads compete for resources | Full isolation — not affected by other customers |
| **Cost model** | Split across customers | Per-customer charge |
| **Data refresh limit** | 8 refreshes/day | 48 refreshes/day |
| **Recommended for** | Small customers, dev/test | Large customers, SLA-sensitive workloads |
| **Provisioning** | No `-CapacityId` in onboarding script | Provide `-CapacityId` in onboarding script |

### Customer Workspace Assignment

Each customer workspace must be assigned to a capacity before going to production. Use the `-CapacityId` parameter in `Initialize-ReportingCustomer.ps1`, or assign manually in the Power BI Admin portal (**Workspaces** > select workspace > **Capacity**).

Shared capacity can be used temporarily during dev/test. The onboarding script warns if no capacity is provided.

### Data Volume Guidance

- Most customers have less than 1 GB of compressed data in the semantic model (Import mode). Standard F4 capacity handles this comfortably.
- If a customer's data exceeds 1 GB, evaluate migration to Direct Lake (planned for R2) rather than increasing F-SKU.
- The default report catalog assumes fewer than 50 custom reports per customer workspace. Beyond this, consider adding folder/category management.

---

## Troubleshooting

### Service Principal Permission Errors

**Symptom**: `Deploy-ReportingReports.ps1` fails with "Failed to acquire Power BI token" or "Failed to create workspace".

**Resolution**:
1. Verify `PBI_CLIENT_ID` and `PBI_CLIENT_SECRET` are correct.
2. In the Power BI Admin portal, confirm **Allow service principals to use Power BI APIs** is enabled for the service principal's security group.
3. For workspace creation errors, confirm **Allow service principals to create workspaces** is enabled.

### Capacity Assignment Errors

**Symptom**: Workspace cannot be assigned to capacity; script fails at step 4 with a 403 or 404.

**Resolution**:
1. Verify the `-CapacityId` GUID is correct (from Power BI Admin portal > **Capacity settings**).
2. Confirm the service principal has the **Capacity Admin** role on the target capacity.
3. Ensure the capacity is in the same region as the workspace.

### Dataset Refresh Failures

**Symptom**: Reports show stale data; Power BI service shows refresh failures in the workspace.

**Resolution**:
1. In the Power BI service, navigate to the workspace and check **Dataset settings** > **Scheduled refresh** for error details.
2. Verify the Dataverse OData endpoint (`PBI_DATAVERSE_DATASOURCE_URL`) is correct and accessible.
3. Confirm the service principal (or its managed identity) has read access to the Dataverse tables used in the semantic model.
4. If the Dataverse environment was moved or the URL changed, re-run `Deploy-ReportingReports.ps1` to rebind the datasets.

### Embed Token Errors (BFF Returns 502)

**Symptom**: The Code Page shows an error; BFF logs show `sdap.reporting.pbi.call_failed`.

**Resolution**:
1. Check BFF application logs for the `correlationId` in the error response.
2. Verify the BFF's `PowerBi__ClientId` and `PowerBi__ClientSecret` are current (secrets may have expired).
3. Confirm the SP profile for the customer still exists: `GET https://api.powerbi.com/v1.0/myorg/profiles`.
4. Confirm the SP profile is still a member of the customer's workspace.

### Module Gate Returns 404 Unexpectedly

**Symptom**: All `/api/reporting/*` calls return 404, but the Dataverse env var is set to `Yes`.

**Resolution**:
1. Confirm the BFF's `Reporting__ModuleEnabled` configuration is set to `true` (the BFF reads its own config, not Dataverse directly at runtime).
2. Restart the BFF API App Service to pick up configuration changes: `az webapp restart --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2`.

### User Sees "Access Denied" Despite Role Assignment

**Symptom**: User has `sprk_ReportingAccess` in Dataverse but the Code Page still shows access denied.

**Resolution**:
1. Confirm the security role is assigned in the correct Dataverse **environment** (not a different environment).
2. Have the user sign out and sign back in to refresh their token claims.
3. Check BFF logs — the filter reads the `sprk_ReportingAccess` claim from the JWT. If the claim is missing, the Dataverse role may not be reflected in the token yet (token cache). Wait up to 1 hour or clear the user's token cache.

### Onboarding Script Fails Mid-Way

**Resolution**: The script is idempotent. Re-run with the same parameters — already-created resources (workspace, SP profile) are detected and reused. The script will continue from the first incomplete step.

---

*Spaarke Reporting Module — Administrator Guide v1.0*
