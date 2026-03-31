# Power BI Embedded Reporting R1

> **Project**: spaarke-powerbi-embedded-r1
> **Status**: Design
> **Priority**: High
> **Module Name**: **Reporting** (not "Analytics" — avoids conflict with AI Analysis features)
> **Last Updated**: March 31, 2026

---

## Executive Summary

Embed Power BI reports and dashboards into Spaarke's Model-Driven App via a full-page "Reporting" Code Page, supporting all three deployment models (Spaarke multi-customer, Spaarke dedicated, customer tenant). Uses "App Owns Data" pattern with service principal profiles, Import mode with scheduled refresh from Dataverse, embedded report authoring for customer-created reports, and a `sprk_report` catalog entity. The Reporting module is gated by an environment variable (`sprk_ReportingModuleEnabled`) and secured via Dataverse security roles.

---

## Module Naming

| Term | Usage | Context |
|------|-------|---------|
| **Reporting** | This module — Power BI dashboards and reports | Business Intelligence, data visualization |
| **Analysis** | AI-powered document analysis (existing) | Playbooks, AI Tool Framework, Analysis Workspace |

All UI labels, Code Page names, endpoints, and entity names use "Reporting" not "Analytics/Analysis":
- Code Page: `sprk_reporting` (not `sprk_analytics`)
- Endpoints: `/api/reporting/*` (not `/api/powerbi/*`)
- Menu item: "Reporting" in workspace navigation
- Security role: `sprk_ReportingAccess`

---

## Problem Statement

Spaarke currently has no reporting/BI capability within the MDA experience. Users must leave the app to view dashboards or export data to Excel for analysis. Legal operations teams need:
- Standard product dashboards (matter pipeline, financial summaries, document activity)
- Custom reports authored by each customer's analysts (in-browser, no Power BI Desktop required)
- Reports that work across all deployment models without per-user Power BI licensing

---

## Design

### Data Source: Import Mode with Scheduled Refresh

**NOT Lakehouse/Direct Lake.** Import mode is the right choice for R1 because:
- Spaarke data is in Dataverse — single source, no need for a data lake
- Most customers have <50K matters, <500K events, <1M documents — small data
- Legal ops doesn't need real-time dashboards — hourly or 4x daily refresh is sufficient
- Import mode delivers the fastest query performance (in-memory VertiPaq engine)
- No Lakehouse infrastructure to provision, sync, monitor, or pay for

```
Power BI Desktop (.pbix)
  └── Connects to Dataverse OData endpoint (Import mode)
  └── Defines semantic model (tables, measures, relationships, RLS roles)
  └── Builds standard report visuals
  └── Published to Power BI workspace

Power BI Service
  ├── Semantic model (auto-created from .pbix publish)
  ├── Scheduled refresh (every 1-4 hours via Dataverse connector)
  ├── Workspace per customer
  └── Reports render from in-memory model (fast)
```

**Future (R2+):** If customers need real-time data, cross-source joins, or data volumes exceed Import limits (1GB+ compressed), migrate to Fabric Lakehouse + Direct Lake. The architecture supports this — only the data source binding changes.

### Deployment Model Support

| Model | Capacity | Workspace | Service Principal | Data Source |
|-------|----------|-----------|-------------------|-------------|
| **Spaarke multi-customer** | Shared F-SKU pool (F8/F32) | Workspace per customer | Spaarke SP + profile per customer | Import from shared Dataverse, RLS by org |
| **Spaarke dedicated** | Shared or dedicated F-SKU | Workspace per customer | Spaarke SP + profile per customer | Import from customer Dataverse |
| **Customer tenant** | Customer provisions own F-SKU | Customer-managed workspace | Customer SP (Spaarke provides .pbix templates) | Import from customer Dataverse |

### Capacity & Cost

| Customer Tier | Capacity | Monthly Cost | Concurrent Viewers |
|---------------|----------|-------------|-------------------|
| **Dev/test** | F2 (pause when not in use) | ~$130/mo (paused 50%) | 5 |
| **Small** (shared pool) | F8 shared across 5-10 customers | ~$73-146/customer/mo | 5 per customer |
| **Medium** (shared pool) | F32 shared across 5-10 customers | ~$290-580/customer/mo | 25 per customer |
| **Large/dedicated** | F64 dedicated | ~$5,800/customer/mo | 250 |

Most customers land in the shared F8 or F32 pool at **$100-500/month** — reasonable as part of a platform subscription.

### Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    Power BI Service                           │
│                                                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │ Workspace A │  │ Workspace B │  │ Workspace C │  ...     │
│  │ (Customer A)│  │ (Customer B)│  │ (Customer C)│         │
│  │             │  │             │  │             │         │
│  │ Semantic    │  │ Semantic    │  │ Semantic    │         │
│  │ Model       │  │ Model       │  │ Model       │         │
│  │ (Import)    │  │ (Import)    │  │ (Import)    │         │
│  │             │  │             │  │             │         │
│  │ Standard    │  │ Standard    │  │ Standard    │         │
│  │ Reports     │  │ Reports     │  │ Reports     │         │
│  │ + Custom    │  │ + Custom    │  │ + Custom    │         │
│  └─────────────┘  └─────────────┘  └─────────────┘         │
│                                                              │
│  ┌──────────────────────────────────┐                       │
│  │ F-SKU Capacity (shared or per-   │                       │
│  │ customer based on tier)          │                       │
│  └──────────────────────────────────┘                       │
└──────────────────────────────────────────────────────────────┘
           │
           │  Power BI REST API
           │  (GenerateToken, Workspaces, Reports, Imports)
           │
┌──────────┴───────────────────────────────────────────────────┐
│                     BFF API (Sprk.Bff.Api)                    │
│                                                              │
│  ┌──────────────────────────────────────┐                   │
│  │ ReportingEmbedService               │                   │
│  │ - Authenticates as service principal │                   │
│  │ - Manages SP profiles per customer   │                   │
│  │ - Generates embed tokens (cached)    │                   │
│  │ - Manages report catalog             │                   │
│  │ - Checks module enabled + user roles │                   │
│  └──────────────────────────────────────┘                   │
│                                                              │
│  Endpoints:                                                  │
│  GET  /api/reporting/reports           (report catalog)      │
│  POST /api/reporting/embed-token       (generate token)      │
│  POST /api/reporting/reports/create    (new blank report)    │
│  PUT  /api/reporting/reports/{id}      (update metadata)     │
│  DELETE /api/reporting/reports/{id}    (delete custom report)│
│  POST /api/reporting/reports/{id}/export (PDF/PPTX)         │
└──────────────────────────────────────────────────────────────┘
           │
           │  Embed URL + Token
           │
┌──────────┴───────────────────────────────────────────────────┐
│                sprk_reporting Code Page                       │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Header: Report Dropdown  [Edit] [New Report] [Export]    ││
│  ├──────────────────────────────────────────────────────────┤│
│  │                                                          ││
│  │           <PowerBIEmbed />                               ││
│  │           (powerbi-client-react 2.0.2)                   ││
│  │                                                          ││
│  │  ┌────────────────────────────────────────────────────┐  ││
│  │  │                                                    │  ││
│  │  │          Embedded Power BI Report                  │  ││
│  │  │          (view or edit mode)                        │  ││
│  │  │                                                    │  ││
│  │  └────────────────────────────────────────────────────┘  ││
│  └──────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────┘
```

### Service Principal Profiles

One Entra ID app registration (service principal) with profiles for multi-tenant isolation:

```
Spaarke Service Principal (one per deployment)
├── Profile: customer-a-uuid → Workspace A access
├── Profile: customer-b-uuid → Workspace B access
├── Profile: customer-c-uuid → Workspace C access
└── ...up to 100,000 profiles
```

All API calls include `X-PowerBI-Profile-Id` header. Each profile has access only to its customer's workspace — no cross-tenant data leakage.

---

## Security Architecture

### Layer 1: Module Enablement (Environment Variable)

The Reporting module is gated at the environment level. If not enabled, all related UI, endpoints, and features are hidden.

| Variable | Type | Purpose |
|----------|------|---------|
| `sprk_ReportingModuleEnabled` | Dataverse environment variable (Boolean) | Gates entire Reporting module |

When `false`:
- Reporting menu item hidden in workspace navigation
- `/api/reporting/*` endpoints return `404` (not `403` — module doesn't exist)
- `sprk_reporting` Code Page shows "Module not available" if accessed directly
- Report catalog queries return empty

When `true`:
- Module is available, subject to Layer 2 security checks

### Layer 2: User Access Control (Dataverse Security Role)

Custom security role `sprk_ReportingAccess` with three privilege tiers:

| Privilege Level | Role | What They Can Do |
|----------------|------|-----------------|
| **Viewer** | `sprk_ReportingAccess` (Read on `sprk_report`) | View reports in Reporting Code Page |
| **Author** | `sprk_ReportingAccess` (Read + Write on `sprk_report`) | View + create/edit/save reports |
| **Admin** | `sprk_ReportingAccess` (Read + Write + Delete on `sprk_report`) | View + create/edit + delete reports |

BFF checks privileges before generating embed tokens:

```csharp
// Check module enabled
if (!await envVarService.GetBoolAsync("sprk_ReportingModuleEnabled"))
    return Results.NotFound();

// Check user has Reporting access
var hasAccess = await dataverseService.CheckUserPrivilege(userId, "sprk_report", "Read");
if (!hasAccess) return Results.Forbid();

// For edit/create: check Write privilege
var canEdit = await dataverseService.CheckUserPrivilege(userId, "sprk_report", "Write");
```

### Layer 3: Tenant Isolation (Workspace-per-Customer)

Service principal profiles ensure each customer can only access their own workspace. Cross-customer data leakage is impossible at this layer.

### Layer 4: Intra-Customer Data Security (Business Unit RLS)

Within a customer, users see only data for their business unit (and child BUs). This uses Spaarke's existing BU hierarchy model.

**RLS role defined in .pbix semantic model:**

```dax
// RLS Role: "BusinessUnitFilter"
// Applied to Matter table (cascades via relationships to Events, Documents, etc.)
[_businessunitid_value] IN
  PATHCONTAINS(
    LOOKUPVALUE(
      BusinessUnit[parentpath],
      BusinessUnit[businessunitid],
      USERNAME()
    ),
    [_businessunitid_value]
  )
```

**BFF passes user identity in embed token:**

```csharp
var tokenRequest = new GenerateTokenRequestV2 {
    Reports = new[] { new GenerateTokenRequestV2Report(reportId) },
    Datasets = new[] { new GenerateTokenRequestV2Dataset(datasetId) },
    Identities = new[] {
        new EffectiveIdentity(
            username: userId,              // Dataverse systemuserid
            roles: new[] { "BusinessUnitFilter" },
            datasets: new[] { datasetId }
        )
    }
};
```

Power BI evaluates the DAX filter using `USERNAME()` = the `userId` passed in the token. The semantic model includes a `BusinessUnit` table with the hierarchy, and the DAX walks the parent path to include child BUs.

### Complete Security Flow

```
User opens Reporting Code Page
  │
  ├── Is sprk_ReportingModuleEnabled = true?
  │   └── No → "Module not available"
  │
  ├── Code Page calls BFF: POST /api/reporting/embed-token
  │   ├── BFF validates OBO token (user is authenticated)
  │   ├── BFF checks: sprk_ReportingModuleEnabled = true
  │   │   └── If false → 404
  │   ├── BFF checks: user has Read privilege on sprk_report
  │   │   └── If no → 403 Forbidden
  │   ├── BFF resolves customer context (org ID → SP profile)
  │   ├── BFF generates embed token with:
  │   │   ├── SP profile (workspace isolation — Layer 3)
  │   │   ├── EffectiveIdentity (userId, BusinessUnitFilter — Layer 4)
  │   │   └── allowEdit based on Write privilege check (Layer 2)
  │   └── Returns token (cached in Redis)
  │
  ├── Code Page renders <PowerBIEmbed />
  │   ├── Report shows only data the user's BU hierarchy permits
  │   ├── [Edit] button visible only if allowEdit=true
  │   └── [New Report] button visible only if allowEdit=true
  │
  └── RLS enforced server-side by Power BI engine
      └── User cannot bypass by modifying client-side code
```

---

## Report Deployment Pipeline

### Development → Production Flow

```
Step 1: Author in Power BI Desktop
  └── Connect to Spaarke dev Dataverse (Import mode)
  └── Build semantic model (tables, measures, relationships)
  └── Define RLS roles (BusinessUnitFilter)
  └── Build report visuals
  └── Save as .pbix

Step 2: Publish to Dev Workspace
  └── Publish from Desktop → spaarke-pbi-dev workspace
  └── Test embedding with dev embed tokens
  └── Verify RLS works with test user identities

Step 3: Promote to Source Control
  └── Export .pbix to repo: reports/{version}/{report-name}.pbix
  └── Version: reports/v1.0/matter-pipeline.pbix
  └── Changelog in reports/CHANGELOG.md
  └── Commit to master (or feature branch)

Step 4: Deploy to Customer Workspace (automated)
  └── PowerShell script: Deploy-ReportingReports.ps1
      ├── For each customer workspace:
      │   ├── Import .pbix (POST /v1.0/myorg/groups/{workspaceId}/imports)
      │   ├── Rebind dataset to customer's Dataverse endpoint
      │   │   └── PATCH /datasets/{id}/Default.UpdateDatasources
      │   │       { "updateDetails": [{
      │   │           "connectionDetails": {
      │   │             "url": "https://{customerOrg}.crm.dynamics.com"
      │   │           }
      │   │       }]}
      │   ├── Set dataset credentials (SP auth for Dataverse connector)
      │   ├── Set refresh schedule (e.g., every 4 hours)
      │   ├── Trigger initial refresh
      │   └── Upsert sprk_report records in customer's Dataverse
      └── Log deployment results

Step 5: Verify
  └── Generate embed token for each customer
  └── Confirm report renders with customer data
  └── Confirm RLS filters correctly per BU
```

### Report Versioning Strategy

| Scenario | Approach |
|----------|----------|
| **New standard report** | Deploy .pbix to all customer workspaces; create `sprk_report` records |
| **Update standard report** | Re-import .pbix (overwrites report in workspace, preserves dataset bindings) |
| **Customer customized a standard report** | Don't overwrite — deploy as new report with "(Updated)" suffix; flag old version |
| **Semantic model change** (new table/measure) | Re-import .pbix (model + report together); triggers automatic refresh |
| **Rollback** | Re-import previous .pbix version from source control |
| **Customer-authored report** | Never touched by Spaarke pipeline — lives only in customer workspace |

---

## Report Catalog (Dataverse Entity)

New entity: `sprk_report`

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_reportid` | GUID (PK) | Dataverse record ID |
| `sprk_name` | String | Display name in dropdown |
| `sprk_description` | String | Report description |
| `sprk_powerbireportid` | String | Power BI report GUID |
| `sprk_powerbiworkspaceid` | String | Power BI workspace GUID |
| `sprk_powerbidatasetid` | String | Power BI dataset/semantic model GUID |
| `sprk_reporttype` | Choice | Standard (0) / Custom (1) |
| `sprk_category` | Choice | Financial (0) / Operational (1) / Compliance (2) / Documents (3) / Custom (4) |
| `sprk_isdefault` | Boolean | Default report shown on page load |
| `sprk_sortorder` | Integer | Order in dropdown |
| `sprk_allowedit` | Boolean | Whether this report can be opened in edit mode |
| `sprk_ownerid` | Lookup (systemuser) | Creator (for custom reports) |

Standard product reports are seeded by the deployment pipeline. Customer-authored reports are created via the [New Report] button.

---

## Code Page: `sprk_reporting`

**React 19 + Vite single-file build** (ADR-026)

```
sprk_reporting Code Page
├── App.tsx
│   ├── useReportCatalog(webApi) — fetches sprk_report records
│   ├── useEmbedToken(bffBaseUrl, reportId, allowEdit) — calls BFF
│   ├── useTokenRefresh(report, token) — auto-refresh at 80% TTL
│   ├── useUserPermissions(webApi) — checks Viewer/Author/Admin level
│   │
│   ├── Header
│   │   ├── Report Dropdown (Fluent v9 Combobox, grouped by category)
│   │   ├── [Edit] button (Author/Admin only, if report.allowedit)
│   │   ├── [New Report] button (Author/Admin only)
│   │   ├── [Save] / [Save As] buttons (visible in edit mode)
│   │   ├── [Delete] button (Admin only, custom reports only)
│   │   ├── [Export] button → PDF or PPTX via BFF
│   │   └── [Full Screen] button
│   │
│   ├── PowerBIEmbed component
│   │   ├── embedConfig from useEmbedToken
│   │   ├── viewMode: View or Edit (toggled by Edit button)
│   │   ├── tokenType: Embed (App Owns Data)
│   │   ├── settings: transparent background (dark mode support)
│   │   └── eventHandlers: loaded, tokenExpired, saved, error
│   │
│   ├── ModuleDisabledState (when env var is false)
│   │   └── "The Reporting module is not enabled for this environment."
│   │
│   └── EmptyState (when no reports configured)
│       └── "No reports available. Contact your administrator."
│
└── main.tsx
    └── Bootstrap: resolveRuntimeConfig → auth → render
```

### Customer Report Authoring Flow (In-Browser)

Customers build reports directly in the `sprk_reporting` Code Page — no Power BI Desktop needed, no PBI license needed:

```
Customer (with Author role) clicks [New Report]
  │
  ├── Code Page calls BFF: POST /api/reporting/reports/create
  │   ├── BFF creates blank report in customer's workspace
  │   │   (via Power BI REST API: POST /groups/{workspaceId}/reports)
  │   ├── Report is bound to customer's semantic model (datasetId)
  │   ├── BFF creates sprk_report record (type=Custom) in Dataverse
  │   └── Returns { reportId, embedUrl, embedToken (allowEdit=true) }
  │
  ├── Code Page opens PowerBIEmbed in Edit mode
  │   ├── Full authoring toolbar (same as Power BI Service)
  │   ├── Customer drags fields from semantic model onto canvas
  │   ├── Adds/removes/resizes visuals, changes chart types
  │   ├── Sets filters, slicers, drill-through
  │   ├── Adds pages
  │   └── Names the report
  │
  ├── Customer clicks [Save]
  │   ├── report.save() → saves to customer's workspace
  │   ├── Code Page calls BFF to update sprk_report name
  │   └── Report appears in dropdown for all users in that org
  │
  └── Customer clicks [Save As] (copy/customize existing report)
      ├── report.saveAs({ name, targetWorkspaceId })
      ├── BFF creates new sprk_report record (type=Custom)
      └── New report appears in dropdown

What customers CAN do (embedded edit mode):
  ✅ Full drag-and-drop report designer
  ✅ Add/remove/resize visuals
  ✅ Change visual types (bar, line, pie, table, matrix, etc.)
  ✅ Bind data fields from semantic model to visuals
  ✅ Set filters, slicers, drill-through
  ✅ Add pages
  ✅ Apply themes
  ✅ Save and Save As

What customers CANNOT do:
  ❌ Modify semantic model (add measures, calculated columns, relationships)
  ❌ Create new data connections
  ❌ Use Power Query / M transformations
  ❌ Install custom visuals from marketplace (ISV must pre-install)
```

### Edit Existing Report Flow

```
Customer selects report from dropdown → clicks [Edit]
  │
  ├── Code Page requests new embed token with allowEdit: true
  ├── PowerBIEmbed switches to Edit mode
  ├── Customer modifies visuals, filters, pages
  ├── [Save] persists changes
  └── [Cancel] reverts to view mode (unsaved changes discarded)
```

### Token Management

```
BFF: ReportingEmbedService
  │
  ├── GetEmbedTokenAsync(customerId, reportId, allowEdit)
  │   ├── Check Redis cache: key = reporting:embed:{customerId}:{reportId}:{allowEdit}
  │   ├── If cached and TTL > 5 min → return cached token
  │   ├── If not cached or near expiry:
  │   │   ├── Get SP profile for customer
  │   │   ├── Acquire Entra ID token via MSAL (client credentials)
  │   │   ├── Call GenerateToken with EffectiveIdentity (BU RLS)
  │   │   ├── Cache in Redis with TTL = token.expiration - 5min
  │   │   └── Return token
  │   └── Token lifetime: ~60 minutes
  │
  └── Client-side auto-refresh:
      ├── Timer at 80% of TTL (48 min)
      ├── Call BFF for fresh token
      ├── report.setAccessToken(newToken) — no page reload
      └── tokenExpired event as fallback
```

---

## Standard Product Reports (R1)

| Report | Category | Description |
|--------|----------|-------------|
| **Matter Pipeline** | Operational | Active matters by status, phase, practice area |
| **Financial Summary** | Financial | Invoice totals, budget utilization, spend by matter |
| **Document Activity** | Documents | Upload volume, document types, storage usage |
| **Task Overview** | Operational | Open tasks by status, overdue items, assignment distribution |
| **Compliance Dashboard** | Compliance | Filing deadlines, audit status, policy compliance |

Authored by Spaarke in Power BI Desktop, stored as `.pbix` in source control, deployed via pipeline.

---

## Export Capabilities

| Format | Method | Notes |
|--------|--------|-------|
| **PDF** | Power BI REST API: `POST /reports/{id}/ExportTo` | Server-side rendering |
| **PPTX** | Same API, format: `PPTX` | Each page becomes a slide |
| **PNG** | JS SDK: `report.print()` | Client-side, browser print dialog |
| **Data (CSV)** | JS SDK: visual-level export | User right-clicks visual → Export data |

---

## BFF API Endpoints

| Method | Path | Purpose | Auth | Privilege |
|--------|------|---------|------|-----------|
| `GET` | `/api/reporting/reports` | List reports from catalog | OBO | Read |
| `POST` | `/api/reporting/embed-token` | Generate embed token | OBO | Read |
| `POST` | `/api/reporting/reports/create` | Create new blank report | OBO + SP | Write |
| `PUT` | `/api/reporting/reports/{id}` | Update report metadata | OBO | Write |
| `DELETE` | `/api/reporting/reports/{id}` | Delete custom report | OBO | Delete |
| `POST` | `/api/reporting/reports/{id}/export` | Export to PDF/PPTX | OBO + SP | Read |

All endpoints check `sprk_ReportingModuleEnabled` first → `404` if disabled.

### BFF Service Registration

```csharp
// New services (ADR-010: DI minimalism)
services.AddSingleton<ReportingEmbedService>();     // SP auth + token generation + RLS
services.AddSingleton<ReportingProfileManager>();   // SP profile management

// Uses existing:
// - IDistributedCache (Redis, ADR-009)
// - IConfidentialClientApplication (MSAL, existing pattern)
// - DataverseWebApiService (privilege checks, env var reads)
```

---

## Azure Resources Required

| Resource | Purpose | Provisioning |
|----------|---------|-------------|
| **F-SKU Capacity** (F2 dev, F8/F32 prod) | Rendering engine for embedded reports | Per-customer or shared pool |
| **Power BI Workspace** | Hosts reports, datasets, semantic models | Per-customer |
| **Entra ID App Registration** | Service principal for PBI REST API | One per Spaarke deployment |
| **Entra Security Group** | SP must be member of group with PBI API access | One per deployment |

Note: **No Fabric Lakehouse** required for Import mode. Capacity is still F-SKU (needed for "embed for customers" without per-user licensing).

### Environment Variables

```
POWERBI_TENANT_ID={tenantId}
POWERBI_CLIENT_ID={spClientId}
POWERBI_CLIENT_SECRET={spClientSecret}  # → Key Vault
POWERBI_API_URL=https://api.powerbi.com/v1.0/myorg
```

Plus Dataverse environment variable:
```
sprk_ReportingModuleEnabled = true/false
```

No hardcoded environment parameters — all via environment variables (BYOK-compatible).

---

## Onboarding: New Customer Setup

1. **Create Power BI workspace** for customer (via REST API or manual)
2. **Create service principal profile** mapped to customer
3. **Assign F-SKU capacity** to workspace (shared pool or dedicated)
4. **Deploy .pbix templates** via `Deploy-ReportingReports.ps1`
   - Imports .pbix → creates semantic model + reports
   - Rebinds dataset to customer's Dataverse endpoint
   - Sets SP credentials for Dataverse connector
   - Configures refresh schedule (every 4 hours)
5. **Seed `sprk_report` records** in customer's Dataverse
6. **Set `sprk_ReportingModuleEnabled` = true** in customer environment
7. **Assign `sprk_ReportingAccess` security role** to appropriate users
8. **Trigger initial dataset refresh**

Steps 1-5 automated via deployment script. Steps 6-7 are admin configuration.

---

## Scope

### In Scope (R1)
- `sprk_reporting` Code Page (React 19, Vite single-file)
- BFF `ReportingEmbedService` with SP profile management
- BFF embed token generation with Redis caching and BU RLS
- `sprk_report` Dataverse entity for report catalog
- Report selector dropdown (grouped by category)
- View mode: embedded report rendering with BU RLS
- **Edit mode: in-browser report authoring (create, edit, save, save-as)**
- Module gating via `sprk_ReportingModuleEnabled` environment variable
- User access control via `sprk_ReportingAccess` security role (Viewer/Author/Admin)
- Business unit RLS in embed tokens
- Token auto-refresh (80% TTL, no page reload)
- Export to PDF/PPTX
- 5 standard product reports (.pbix templates)
- Report deployment pipeline (`Deploy-ReportingReports.ps1`)
- Report versioning in source control (`reports/` folder)
- Dark mode support (transparent PBI background + Fluent v9)
- Onboarding scripts for customer workspace/profile setup
- Environment variable-based config (BYOK-compatible)

### Out of Scope (R2+)
- Paginated reports (RDLC-style, for formal documents — planned for R2)
- Fabric Lakehouse / Direct Lake data source (if Import limits exceeded)
- Real-time streaming datasets
- Dashboard tiles embedded on entity forms (PCF)
- Power BI alerts and subscriptions
- Custom visuals marketplace integration
- Semantic model authoring by customers (requires Power BI Desktop)
- Report scheduling (email delivery)
- Capacity management admin UI
- Cross-customer analytics (Spaarke product analytics)

---

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API pattern for reporting endpoints
- **ADR-006**: Code Page for standalone reporting page (not PCF)
- **ADR-008**: Endpoint filters for authorization on reporting endpoints
- **ADR-009**: Redis-first caching for embed tokens
- **ADR-010**: DI minimalism — ReportingEmbedService + ReportingProfileManager (2 registrations)
- **ADR-012**: Shared components from `@spaarke/ui-components` (header, dropdown)
- **ADR-021**: Fluent UI v9 exclusively; dark mode via transparent PBI background
- **ADR-026**: Vite + vite-plugin-singlefile build for Code Page

### MUST Rules
- MUST use "App Owns Data" pattern (service principal, not user auth)
- MUST use service principal profiles for multi-tenant isolation
- MUST gate module via `sprk_ReportingModuleEnabled` environment variable
- MUST enforce user access via `sprk_ReportingAccess` security role privileges
- MUST enforce business unit RLS via EffectiveIdentity in embed tokens
- MUST cache embed tokens in Redis (ADR-009)
- MUST auto-refresh tokens at 80% TTL via `report.setAccessToken()`
- MUST use `powerbi-client-react` 2.0.2 (React 18+ only — Code Page)
- MUST store report catalog in `sprk_report` Dataverse entity
- MUST use environment variables for all PBI configuration (BYOK-compatible)
- MUST use Import mode for data source (not DirectQuery or Direct Lake in R1)
- MUST store .pbix templates in source control with version tracking
- MUST NOT hardcode workspace IDs, capacity IDs, or tenant IDs
- MUST NOT require end users to have Power BI licenses
- MUST NOT use "Analysis" or "Analytics" in module naming

---

## Success Criteria

1. [ ] Reporting Code Page renders embedded Power BI report
2. [ ] Report dropdown shows catalog from `sprk_report` entity
3. [ ] Embed token generated via BFF with SP profile isolation
4. [ ] Token cached in Redis, auto-refreshes without page reload
5. [ ] Business unit RLS filters data correctly per user
6. [ ] Edit mode allows full in-browser report authoring
7. [ ] New Report creates blank report bound to customer's semantic model
8. [ ] Save/Save As persists to customer workspace and updates catalog
9. [ ] Export to PDF/PPTX works via REST API
10. [ ] 5 standard reports deployed and visible in dropdown
11. [ ] Module hidden when `sprk_ReportingModuleEnabled` = false
12. [ ] Non-authorized users cannot access reporting features
13. [ ] Dark mode renders correctly (transparent PBI background)
14. [ ] Works in all 3 deployment models
15. [ ] No per-user Power BI license required for viewers or editors
16. [ ] Deployment pipeline deploys .pbix to customer workspaces
17. [ ] Onboarding script provisions workspace + profile + reports

---

## Dependencies

### Prerequisites
- F-SKU capacity provisioned (at least F2 for dev)
- Entra ID app registration with Power BI API permissions
- Service principal added to Power BI workspace as Admin/Member
- Spaarke Dataverse connector for Import mode refresh

### Related Projects
- `spaarke-daily-update-service` — Notifications could include "new reports published"
- `spaarke-workspace-user-configuration-r1` — Reporting could be a workspace section
- `ai-m365-copilot-integration-r1` — Copilot could suggest relevant reports

---

## References

- [Power BI Playground](https://playground.powerbi.com/) — Interactive embedding demo
- [Power BI Embedded capacity and SKUs](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embedded-capacity)
- [Service principal profiles for multi-tenant](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-multi-tenancy)
- [Embedded report authoring](https://learn.microsoft.com/en-us/javascript/api/overview/powerbi/report-authoring-overview)
- [Power BI REST API](https://learn.microsoft.com/en-us/rest/api/power-bi/)
- [powerbi-client-react npm](https://www.npmjs.com/package/powerbi-client-react)
- [powerbi-client JS SDK](https://www.npmjs.com/package/powerbi-client)
- [AppOwnsDataMultiTenant sample](https://github.com/PowerBiDevCamp/AppOwnsDataMultiTenant)
- [Row-Level Security with App Owns Data](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embedded-row-level-security)

---

*Last updated: March 31, 2026*
