# Power BI Embedded Analytics R1

> **Project**: spaarke-powerbi-embedded-r1
> **Status**: Design
> **Priority**: High
> **Last Updated**: March 31, 2026

---

## Executive Summary

Embed Power BI reports and dashboards into Spaarke's Model-Driven App via a full-page React Code Page, supporting all three deployment models (Spaarke multi-customer, Spaarke dedicated, customer tenant). Uses "App Owns Data" pattern with service principal profiles, Fabric Lakehouse as data source via Direct Lake mode, and embedded report authoring for customer-created reports.

---

## Problem Statement

Spaarke currently has no analytics/reporting capability within the MDA experience. Users must leave the app to view dashboards or export data to Excel for analysis. Legal operations teams need:
- Standard product dashboards (matter pipeline, financial summaries, document activity)
- Custom reports authored by each customer's analysts
- Real-time data from Dataverse via Fabric Lakehouse
- Analytics that work across all deployment models without per-user Power BI licensing

---

## Design

### Deployment Model Support

| Model | Capacity | Workspace | Service Principal | Data Source |
|-------|----------|-----------|-------------------|-------------|
| **Spaarke multi-customer** | Shared F-SKU pool (F32/F64) with per-customer capacity assignment | Workspace per customer | Spaarke SP + profile per customer | Shared Lakehouse with per-customer schema/tables |
| **Spaarke dedicated** | Dedicated F-SKU per customer (F8-F32) | Workspace per customer | Spaarke SP + profile per customer | Customer-dedicated Lakehouse |
| **Customer tenant** | Customer provisions own F-SKU | Customer-managed workspace | Customer SP (Spaarke provides templates) | Customer-managed Lakehouse |

### Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     Fabric / OneLake                          │
│                                                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐         │
│  │ Lakehouse A │  │ Lakehouse B │  │ Lakehouse C │  ...     │
│  │ (Customer A)│  │ (Customer B)│  │ (Customer C)│         │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘         │
│         │                │                │                  │
│  ┌──────┴──────┐  ┌──────┴──────┐  ┌──────┴──────┐         │
│  │Direct Lake  │  │Direct Lake  │  │Direct Lake  │         │
│  │Semantic     │  │Semantic     │  │Semantic     │         │
│  │Model A      │  │Model B      │  │Model C      │         │
│  └──────┬──────┘  └──────┴──────┘  └──────┬──────┘         │
│         │                │                │                  │
│  ┌──────┴──────┐  ┌──────┴──────┐  ┌──────┴──────┐         │
│  │ Workspace A │  │ Workspace B │  │ Workspace C │         │
│  │ - Reports   │  │ - Reports   │  │ - Reports   │         │
│  │ - Dashboards│  │ - Dashboards│  │ - Dashboards│         │
│  └─────────────┘  └─────────────┘  └─────────────┘         │
│                                                              │
│  ┌──────────────────────────────────┐                       │
│  │ F-SKU Capacity (shared or per-   │                       │
│  │ customer based on tier)          │                       │
│  └──────────────────────────────────┘                       │
└──────────────────────────────────────────────────────────────┘
           │
           │  Power BI REST API
           │  (GenerateToken, Workspaces, Reports)
           │
┌──────────┴───────────────────────────────────────────────────┐
│                     BFF API (Sprk.Bff.Api)                    │
│                                                              │
│  ┌──────────────────────────────────────┐                   │
│  │ PowerBiEmbedService                  │                   │
│  │ - Authenticates as service principal │                   │
│  │ - Manages SP profiles per customer   │                   │
│  │ - Generates embed tokens             │                   │
│  │ - Caches tokens in Redis             │                   │
│  │ - Manages report catalog             │                   │
│  └──────────────────────────────────────┘                   │
│                                                              │
│  Endpoints:                                                  │
│  GET  /api/powerbi/reports              (report catalog)     │
│  POST /api/powerbi/embed-token          (generate token)     │
│  POST /api/powerbi/reports/create       (new blank report)   │
│  GET  /api/powerbi/reports/{id}/export  (export to PDF/PPTX) │
└──────────────────────────────────────────────────────────────┘
           │
           │  Embed URL + Token
           │
┌──────────┴───────────────────────────────────────────────────┐
│                sprk_analytics Code Page                       │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Header: Report Selector Dropdown  [Edit] [New] [Export]  ││
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

### Report Catalog (Dataverse Entity)

New entity: `sprk_report`

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_reportid` | GUID (PK) | Dataverse record ID |
| `sprk_name` | String | Display name in dropdown |
| `sprk_description` | String | Report description |
| `sprk_powerbireportid` | String | Power BI report GUID |
| `sprk_powerbiworkspaceid` | String | Power BI workspace GUID |
| `sprk_reporttype` | Choice | Standard (product) / Custom (customer-authored) |
| `sprk_category` | Choice | Financial / Operational / Compliance / Documents / Custom |
| `sprk_isdefault` | Boolean | Default report shown on page load |
| `sprk_sortorder` | Integer | Order in dropdown |
| `sprk_allowedit` | Boolean | Whether this report can be opened in edit mode |
| `sprk_ownerid` | Lookup (systemuser) | Creator (for custom reports) |

Standard product reports are seeded during onboarding. Customer-authored reports are created via the "New Report" button and added to the catalog automatically.

### Code Page: `sprk_analytics`

**React 19 + Vite single-file build** (ADR-026)

```
sprk_analytics Code Page
├── App.tsx
│   ├── useReportCatalog(webApi) — fetches sprk_report records
│   ├── useEmbedToken(bffBaseUrl, reportId) — calls BFF for embed token
│   ├── useTokenRefresh(report, token) — auto-refresh at 80% TTL
│   │
│   ├── Header
│   │   ├── Report Dropdown (Fluent v9 Combobox, grouped by category)
│   │   ├── [Edit] button (if report.allowedit && user has permission)
│   │   ├── [New Report] button → creates blank report via BFF
│   │   ├── [Save] / [Save As] buttons (visible in edit mode)
│   │   ├── [Export] button → PDF or PPTX via Power BI export API
│   │   └── [Full Screen] button
│   │
│   ├── PowerBIEmbed component
│   │   ├── embedConfig from useEmbedToken
│   │   ├── viewMode: View or Edit (toggled by Edit button)
│   │   ├── tokenType: Embed (App Owns Data)
│   │   ├── settings: transparent background (dark mode support)
│   │   └── eventHandlers: loaded, tokenExpired, saved, error
│   │
│   └── EmptyState (when no reports configured)
│       └── "No reports available. Contact your administrator."
│
└── main.tsx
    └── Bootstrap: resolveRuntimeConfig → auth → render
```

### Customer Report Authoring Flow

```
Customer clicks [New Report]
  │
  ├── Code Page calls BFF: POST /api/powerbi/reports/create
  │   ├── BFF creates blank report in customer's workspace
  │   │   (via Power BI REST API: POST /v1.0/myorg/groups/{workspaceId}/reports)
  │   ├── Report is bound to customer's semantic model (datasetId)
  │   ├── BFF creates sprk_report record in Dataverse
  │   └── Returns { reportId, embedUrl, embedToken (with allowEdit) }
  │
  ├── Code Page opens PowerBIEmbed in Edit mode
  │   ├── Full authoring toolbar (same as Power BI Service)
  │   ├── Customer drags fields, creates visuals, adds pages
  │   └── Customer names the report
  │
  ├── Customer clicks [Save]
  │   ├── report.save() → saves to customer's workspace
  │   ├── Code Page calls BFF to update sprk_report name
  │   └── Report appears in dropdown for all users in that org
  │
  └── Customer clicks [Save As] (copy existing report)
      ├── report.saveAs({ name, targetWorkspaceId })
      ├── BFF creates new sprk_report record
      └── New report appears in dropdown
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
BFF: PowerBiEmbedService
  │
  ├── GetEmbedTokenAsync(customerId, reportId, allowEdit)
  │   ├── Check Redis cache: key = pbi:embed:{customerId}:{reportId}:{allowEdit}
  │   ├── If cached and TTL > 5 min → return cached token
  │   ├── If not cached or near expiry:
  │   │   ├── Get SP profile for customer
  │   │   ├── Acquire Entra ID token via MSAL (client credentials)
  │   │   ├── Call GenerateToken with effective identity (RLS if needed)
  │   │   ├── Cache in Redis with TTL = token.expiration - 5min
  │   │   └── Return token
  │   └── Token lifetime: ~60 minutes (inherits from Entra ID token)
  │
  └── Client-side auto-refresh:
      ├── Set timer at 80% of TTL (48 min)
      ├── Call BFF for fresh token
      ├── report.setAccessToken(newToken) — no page reload
      └── tokenExpired event as fallback safety net
```

### Standard Product Reports (R1)

| Report | Category | Description |
|--------|----------|-------------|
| **Matter Pipeline** | Operational | Active matters by status, phase, practice area |
| **Financial Summary** | Financial | Invoice totals, budget burn, spend by matter |
| **Document Activity** | Documents | Upload volume, document types, storage usage |
| **Task Overview** | Operational | Open tasks by status, overdue items, assignment distribution |
| **Compliance Dashboard** | Compliance | Filing deadlines, audit status, policy compliance |

These are authored by Spaarke in Power BI Desktop, published as `.pbix` templates, and deployed to each customer workspace during onboarding.

### Export Capabilities

| Format | Method | Notes |
|--------|--------|-------|
| **PDF** | Power BI REST API: `POST /reports/{id}/ExportTo` | Server-side rendering, no client-side dependency |
| **PPTX** | Same API, format: `PPTX` | Each page becomes a slide |
| **PNG** | JS SDK: `report.print()` | Client-side, browser print dialog |
| **Data (CSV)** | JS SDK: visual-level export | User right-clicks visual → Export data |

---

## BFF API Endpoints

| Method | Path | Purpose | Auth |
|--------|------|---------|------|
| `GET` | `/api/powerbi/reports` | List reports from `sprk_report` catalog | OBO |
| `POST` | `/api/powerbi/embed-token` | Generate embed token for a report | OBO |
| `POST` | `/api/powerbi/reports/create` | Create new blank report in customer workspace | OBO + SP |
| `PUT` | `/api/powerbi/reports/{id}` | Update report metadata (name, category) | OBO |
| `DELETE` | `/api/powerbi/reports/{id}` | Delete custom report | OBO |
| `POST` | `/api/powerbi/reports/{id}/export` | Export report to PDF/PPTX | OBO + SP |

### BFF Service Registration

```csharp
// New services (ADR-010: DI minimalism)
services.AddSingleton<PowerBiEmbedService>();      // SP auth + token generation
services.AddSingleton<PowerBiProfileManager>();     // SP profile management

// Uses existing:
// - IDistributedCache (Redis, ADR-009)
// - IConfidentialClientApplication (MSAL, existing pattern)
```

---

## Azure / Fabric Resources Required

| Resource | Purpose | Provisioning |
|----------|---------|-------------|
| **Fabric capacity (F-SKU)** | Rendering engine for embedded reports | Per-customer or shared pool |
| **Fabric Lakehouse** | Data source (Direct Lake mode) | Per-customer |
| **Fabric Workspace** | Hosts reports, datasets, semantic models | Per-customer |
| **Entra ID App Registration** | Service principal for PBI REST API | One per Spaarke deployment |
| **Entra Security Group** | SP must be member of group with Fabric API access | One per deployment |

### Environment Variables

```
POWERBI_TENANT_ID={tenantId}
POWERBI_CLIENT_ID={spClientId}
POWERBI_CLIENT_SECRET={spClientSecret}  # → Key Vault
POWERBI_API_URL=https://api.powerbi.com/v1.0/myorg
```

No hardcoded environment parameters — all via environment variables per deployment model (BYOK-compatible).

---

## Onboarding: New Customer Setup

When a new customer is onboarded:

1. **Create Fabric workspace** for customer (via REST API or manual)
2. **Create service principal profile** mapped to customer
3. **Assign capacity** to workspace (shared pool or dedicated F-SKU)
4. **Create Lakehouse** and configure data pipeline from Dataverse
5. **Create Direct Lake semantic model** pointing to Lakehouse tables
6. **Deploy standard reports** (import `.pbix` templates to workspace)
7. **Seed `sprk_report` records** in Dataverse for report catalog
8. **Configure BFF** with customer's workspace ID and profile ID

Steps 1-6 can be automated via PowerShell/CLI scripts.

---

## Scope

### In Scope (R1)
- `sprk_analytics` Code Page (React 19, Vite single-file)
- BFF `PowerBiEmbedService` with SP profile management
- BFF embed token generation with Redis caching
- `sprk_report` Dataverse entity for report catalog
- Report selector dropdown (grouped by category)
- View mode: embedded report rendering
- Edit mode: embedded report authoring (create, edit, save, save-as)
- Token auto-refresh (80% TTL, no page reload)
- Export to PDF/PPTX
- 5 standard product reports (Matter Pipeline, Financial Summary, Document Activity, Task Overview, Compliance)
- Dark mode support (transparent background + Fluent v9 theme)
- Onboarding scripts for customer workspace/profile setup
- Environment variable-based config (BYOK-compatible)

### Out of Scope (R2+)
- Paginated reports (RDLC-style, for formal documents)
- Real-time streaming datasets
- Dashboard tiles embedded on entity forms (PCF)
- Power BI alerts and subscriptions
- Custom visuals marketplace integration
- Semantic model authoring by customers (requires Power BI Desktop)
- Row-Level Security within a customer (intra-org role-based filtering)
- Report scheduling (email delivery)
- Power BI REST API admin endpoints (capacity management UI)

---

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API pattern for embed token endpoints
- **ADR-006**: Code Page for standalone analytics page (not PCF)
- **ADR-008**: Endpoint filters for authorization on PBI endpoints
- **ADR-009**: Redis-first caching for embed tokens
- **ADR-010**: DI minimalism — PowerBiEmbedService + PowerBiProfileManager (2 registrations)
- **ADR-012**: Shared components from `@spaarke/ui-components` (header, dropdown)
- **ADR-021**: Fluent UI v9 exclusively; dark mode via transparent PBI background
- **ADR-026**: Vite + vite-plugin-singlefile build for Code Page

### MUST Rules
- MUST use "App Owns Data" pattern (service principal, not user auth)
- MUST use service principal profiles for multi-tenant isolation
- MUST use F-SKU capacity (required for Direct Lake mode)
- MUST cache embed tokens in Redis (ADR-009)
- MUST auto-refresh tokens at 80% TTL via `report.setAccessToken()`
- MUST use `powerbi-client-react` 2.0.2 (React 18+ only — Code Page, not PCF)
- MUST store report catalog in `sprk_report` Dataverse entity
- MUST use environment variables for all PBI configuration (BYOK-compatible)
- MUST NOT hardcode workspace IDs, capacity IDs, or tenant IDs
- MUST NOT require end users to have Power BI licenses

---

## Success Criteria

1. [ ] Analytics Code Page renders embedded Power BI report
2. [ ] Report dropdown shows catalog from `sprk_report` entity
3. [ ] Embed token generated via BFF with SP profile isolation
4. [ ] Token cached in Redis, auto-refreshes without page reload
5. [ ] Edit mode allows full report authoring (add visuals, save)
6. [ ] New Report creates blank report bound to customer's semantic model
7. [ ] Save/Save As persists to customer workspace and updates catalog
8. [ ] Export to PDF/PPTX works via REST API
9. [ ] 5 standard reports deployed and visible in dropdown
10. [ ] Dark mode renders correctly (transparent PBI background)
11. [ ] Works in all 3 deployment models (multi-customer, dedicated, customer tenant)
12. [ ] No per-user Power BI license required for viewers or editors
13. [ ] Onboarding script provisions workspace + profile + reports for new customer

---

## Dependencies

### Prerequisites
- Fabric capacity provisioned (at least F2 for dev)
- Fabric Lakehouse created with Dataverse data pipeline
- Entra ID app registration with Power BI API permissions
- Service principal added to Fabric workspace as Admin/Member

### Related Projects
- `spaarke-daily-update-service` — Daily Digest could include "new reports published" notifications
- `spaarke-workspace-user-configuration-r1` — Analytics could be a workspace section
- `ai-m365-copilot-integration-r1` — Copilot could suggest relevant reports

---

## References

- [Power BI Playground](https://playground.powerbi.com/) — Interactive embedding demo
- [Power BI Embedded capacity and SKUs](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embedded-capacity)
- [Service principal profiles for multi-tenant](https://learn.microsoft.com/en-us/power-bi/developer/embedded/embed-multi-tenancy)
- [Embedded report authoring](https://learn.microsoft.com/en-us/javascript/api/overview/powerbi/report-authoring-overview)
- [Direct Lake overview](https://learn.microsoft.com/en-us/fabric/fundamentals/direct-lake-overview)
- [powerbi-client-react npm](https://www.npmjs.com/package/powerbi-client-react)
- [AppOwnsDataMultiTenant sample](https://github.com/PowerBiDevCamp/AppOwnsDataMultiTenant)

---

*Last updated: March 31, 2026*
