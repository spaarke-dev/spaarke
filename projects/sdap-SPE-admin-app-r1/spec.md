# SDAP SPE Admin App - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-13
> **Source**: design.md

## Executive Summary

Build a comprehensive SharePoint Embedded (SPE) administration application delivered as a Dataverse Code Page HTML web resource. The app provides a single-pane-of-glass admin experience for managing all SPE resources — container types, containers, files, permissions, custom properties, columns, search, recycle bin, and security — directly from within the Dataverse model-driven app. Admin selects a Dataverse Business Unit to scope operations to the correct container type and containers for that BU. Configuration data (Azure app registration GUIDs, tenant IDs, container type IDs, endpoints, Key Vault secret references) is stored in Dataverse tables, enabling multi-BU, multi-environment, and multi-container-type support with full audit trails.

## Scope

### In Scope

- **Code Page HTML** — Single self-contained HTML file (React 18, Fluent UI v9, Vite + viteSingleFile) deployed as `sprk_speadmin` Dataverse web resource
- **BFF API Endpoints** — ~55 new endpoints under `/api/spe/*` proxying Microsoft Graph SPE APIs through existing Sprk.Bff.Api
- **Business Unit Scoping** — BU picker cascade: BU → Container Type Config → Environment, resolving auth credentials per selection
- **3 Dataverse Tables** — `sprk_speenvironment`, `sprk_specontainertypeconfig`, `sprk_speauditlog`
- **Container Type Management** — CRUD, registration, settings, permissions assignment for container types
- **Container Management** — CRUD, activate, lock/unlock, permissions, columns, custom properties for containers
- **File Browser** — Browse files/folders within containers, upload, download, preview, manage metadata, versions, sharing links
- **Search** — Cross-container and cross-item search with actionable result grids (bulk delete, export CSV, manage permissions)
- **Recycle Bin** — View/restore/permanently-delete deleted containers
- **Security Dashboard** — Security alerts, secure scores (read-only)
- **eDiscovery Dashboard** — Cases, custodians, data sources (Phase 3, read-only)
- **Audit Logging** — All mutating operations logged to `sprk_speauditlog` with BU and config context
- **Dashboard** — Summary metrics (container counts, storage usage, recent activity) via background sync with manual refresh
- **Configuration UI** — CRUD for environments and container type configs
- **Multi-App Credential Resolution** — `SpeAdminGraphService` resolves credentials per-config from Dataverse + Key Vault
- **Shared Library Reuse** — Heavy reuse of `@spaarke/ui-components`, `@spaarke/auth`, `@spaarke/sdap-client`

### Out of Scope

- Billing/payment management (handled in Azure portal)
- Multi-tenant consuming tenant management (Phase 3)
- SharePoint site administration (not SPE)
- Power Platform solution deployment automation
- Custom page or PCF wrapper — this is a Code Page (ADR-006)
- Creating new Azure App Registrations from within the app (done in Azure portal)

### Affected Areas

- `src/solutions/SpeAdminApp/` — NEW Code Page solution (React 18, Vite)
- `src/server/api/Sprk.Bff.Api/Endpoints/SpeAdminEndpoints.cs` — NEW endpoint group
- `src/server/api/Sprk.Bff.Api/Services/SpeAdminGraphService.cs` — NEW multi-config Graph service
- `src/server/api/Sprk.Bff.Api/Services/SpeAuditService.cs` — NEW audit logging service
- `src/server/api/Sprk.Bff.Api/Services/SpeDashboardSyncService.cs` — NEW BackgroundService for dashboard metrics
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` — EXTEND (reference pattern, not modify)
- `src/client/shared/Spaarke.UI.Components/` — CONSUME (no modifications expected)
- `src/client/shared/Spaarke.Auth/` — CONSUME (no modifications expected)
- `src/client/shared/Spaarke.SdapClient/` — CONSUME (no modifications expected)
- Dataverse solution — NEW tables, sitemap entry, web resource

## Requirements

### Functional Requirements

1. **FR-01**: Admin can select a Dataverse Business Unit to scope all SPE operations to the correct container type config — Acceptance: BU picker populates container type configs; all subsequent operations use the selected config's credentials
2. **FR-02**: Admin can view, create, update, and delete SPE Environment configurations — Acceptance: CRUD operations on `sprk_speenvironment` table with form validation
3. **FR-03**: Admin can view, create, update, and delete Container Type Configurations per BU — Acceptance: CRUD operations on `sprk_specontainertypeconfig` with Key Vault secret reference (never stores actual secret)
4. **FR-04**: Admin can list, create, and manage container types via Graph API — Acceptance: Container type CRUD reflected in Graph API; billing classification, settings visible
5. **FR-05**: Admin can register container types on consuming tenant — Acceptance: PUT to `/_api/v2.1/storageContainerTypes/{id}/applicationPermissions` with selected delegated/application permissions succeeds
6. **FR-06**: Admin can list, create, activate, lock/unlock containers — Acceptance: Container lifecycle management via Graph API with status reflected in UI
7. **FR-07**: Admin can manage container permissions (list, add, update, remove users/groups with roles) — Acceptance: Permission changes reflected in Graph API; roles: reader, writer, manager, owner
8. **FR-08**: Admin can manage container columns (list, create, update, delete column definitions) — Acceptance: Custom column definitions synced via Graph API
9. **FR-09**: Admin can manage container custom properties (CRUD with searchable flag) — Acceptance: Custom properties visible in container detail view
10. **FR-10**: Admin can browse files/folders within a container, upload files, download files, preview files — Acceptance: File browser with breadcrumb navigation, drag-drop upload, inline preview
11. **FR-11**: Admin can manage file metadata (fields, versions, sharing links, thumbnails) — Acceptance: File detail panel shows all metadata; version history navigable
12. **FR-12**: Admin can search across containers and container items — Acceptance: Search query returns results in actionable DataGrid
13. **FR-13**: Search result grids support bulk actions (delete containers, delete files, manage permissions, export CSV) — Acceptance: Multi-row selection with toolbar actions; confirmation dialogs for destructive operations
14. **FR-14**: Admin can view and manage recycle bin (deleted containers — restore or permanent delete) — Acceptance: Recycle bin grid with restore/delete actions
15. **FR-15**: Dashboard shows summary metrics (container count, storage usage, recent activity) — Acceptance: Dashboard loads cached data with manual refresh button; BackgroundService syncs periodically
16. **FR-16**: All mutating operations are logged to `sprk_speauditlog` — Acceptance: Audit log grid shows operation, target, status, user, timestamp, BU, config context
17. **FR-17**: App supports dark mode and theme detection — Acceptance: Reads `data-theme` attribute; switches between spaarkeLight/spaarkeDark themes
18. **FR-18**: All Graph API calls proxy through BFF API — Acceptance: No direct Graph API calls from client; all through `/api/spe/*` endpoints

### Non-Functional Requirements

- **NFR-01**: Code Page loads in <3 seconds on standard connection (single HTML file, <2MB)
- **NFR-02**: BFF API endpoints return within 2 seconds (p95) for non-search operations
- **NFR-03**: Support SPE rate limits gracefully — show throttling indicator, retry with backoff for 429 responses
- **NFR-04**: WCAG 2.1 AA accessibility compliance (keyboard navigation, screen reader, contrast ratios)
- **NFR-05**: No client secrets in browser — all auth handled server-side via BFF
- **NFR-06**: Audit log retention follows Dataverse table retention policies

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern required — all BFF endpoints use Minimal API; no Azure Functions
- **ADR-006**: Code Page for standalone admin UI — not legacy JS webresource, not PCF + custom page wrapper
- **ADR-008**: Use endpoint filters for authorization — `SpeAdminAuthorizationFilter` on route group, not global middleware
- **ADR-010**: DI minimalism — ≤15 non-framework DI registrations; use concrete types; feature module `AddSpeAdminModule()`
- **ADR-012**: Shared component library — reuse `@spaarke/ui-components` (UniversalDatasetGrid, CommandToolbar, WizardShell, SidePaneShell, FileUploadZone, etc.)
- **ADR-021**: Fluent UI v9 exclusively — `@fluentui/react-components`, design tokens for all styling, dark mode required, `makeStyles` for custom CSS
- **ADR-022**: Code Pages bundle React 18 + Fluent v9 (not platform-provided); use `createRoot()`

### MUST Rules

- ✅ MUST use Minimal API for all `/api/spe/*` endpoints
- ✅ MUST return `ProblemDetails` for all API errors
- ✅ MUST use endpoint filters for authorization (not global middleware)
- ✅ MUST use Fluent UI v9 exclusively (no v8, no hard-coded colors)
- ✅ MUST support dark mode and high-contrast themes
- ✅ MUST use `createRoot()` entry point (React 18, bundled)
- ✅ MUST use `@spaarke/ui-components` for DataGrid, toolbar, wizard, side pane, file upload
- ✅ MUST use `@spaarke/auth` for `authenticatedFetch()` and `initAuth()`
- ✅ MUST use `makeStyles` (Griffel) for custom styling — Fluent tokens only
- ✅ MUST keep client secrets in Key Vault (referenced by name in Dataverse config, never stored directly)
- ✅ MUST log all mutating operations to audit table
- ✅ MUST use BackgroundService (ADR-001) for dashboard sync, not Azure Functions

### MUST NOT Rules

- ❌ MUST NOT create Azure Functions
- ❌ MUST NOT use global middleware for resource authorization
- ❌ MUST NOT use Fluent UI v8 or hard-code colors
- ❌ MUST NOT use legacy JS webresources (jQuery, ad hoc scripts)
- ❌ MUST NOT inject `GraphServiceClient` directly into endpoints
- ❌ MUST NOT store client secrets in Dataverse tables
- ❌ MUST NOT make direct Graph API calls from client-side code
- ❌ MUST NOT create interfaces without genuine seam requirement (ADR-010)

### Existing Patterns to Follow

- See `src/solutions/LegalWorkspace/` for Vite-based Code Page pattern (viteSingleFile, theme detection, deploy script)
- See `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` for Graph client creation pattern
- See `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/ContainerOperations.cs` for existing container operations
- See `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` for existing endpoint patterns
- See `src/client/shared/Spaarke.UI.Components/src/` for shared component inventory

### Shared Library Reuse Map

| UI Need | Shared Component | Import Path |
|---------|-----------------|-------------|
| Data grids (containers, files, search results, audit log) | `UniversalDatasetGrid` | `@spaarke/ui-components` |
| Toolbar actions (create, delete, refresh, export) | `CommandToolbar` | `@spaarke/ui-components` |
| File upload | `FileUploadZone` | `@spaarke/ui-components` |
| File preview | `FilePreview` | `@spaarke/ui-components` |
| Wizard flows (create container type, register CT) | `WizardShell` | `@spaarke/ui-components` |
| Side panels (container detail, file detail, permissions) | `SidePaneShell` | `@spaarke/ui-components` |
| Confirmation dialogs (delete, permanent delete) | `ChoiceDialog` | `@spaarke/ui-components` |
| Lookup fields (BU picker, environment picker) | `LookupField` | `@spaarke/ui-components` |
| Status badges (active/inactive, trial/standard) | `StatusBadge` | `@spaarke/ui-components` |
| Themes (light/dark) | `spaarkeLight`, `spaarkeDark` | `@spaarke/ui-components` |
| Auth token handling | `initAuth()`, `authenticatedFetch()` | `@spaarke/auth` |
| API client for file operations | `SdapApiClient` | `@spaarke/sdap-client` |
| FetchXml queries (BU list, config list) | `FetchXmlService` | `@spaarke/ui-components` |

## Success Criteria

1. [ ] Admin can create/manage container types from within Dataverse — Verify: Create a trial container type, update settings, register on tenant
2. [ ] Admin can create/activate/manage containers without Postman or PowerShell — Verify: Full container lifecycle from UI
3. [ ] Admin can browse files, upload, manage metadata and permissions in any container — Verify: Upload file, set custom property, add permission, preview file
4. [ ] Admin can search across containers and container items with actionable results — Verify: Search returns results; bulk delete/export works
5. [ ] All operations are audit-logged — Verify: Check `sprk_speauditlog` after operations
6. [ ] Environment and container type configuration is managed through Dataverse tables — Verify: CRUD all 3 tables via settings UI
7. [ ] App supports dark mode and is accessible — Verify: Toggle theme, keyboard navigate all sections, screen reader test
8. [ ] All Graph API calls proxy through BFF (no direct client-side Graph calls) — Verify: Network tab shows only `/api/spe/*` calls, no `graph.microsoft.com`
9. [ ] BU picker scopes all operations correctly — Verify: Select different BU, confirm containers and config change accordingly
10. [ ] Dashboard shows cached metrics with manual refresh — Verify: Dashboard loads instantly from cache; refresh button triggers fresh sync

## Dependencies

### Prerequisites

- Azure App Registration(s) with SPE Graph API permissions granted (`FileStorageContainerType.Manage.All`, `FileStorageContainer.Selected`, etc.)
- Client secrets stored in Azure Key Vault
- At least one SPE container type created (can be trial) for testing
- Existing `Sprk.Bff.Api` deployed and running
- Existing shared libraries published (`@spaarke/ui-components`, `@spaarke/auth`, `@spaarke/sdap-client`)

### External Dependencies

- Microsoft Graph API (v1.0 + beta) — SPE endpoints
- SharePoint REST API (`/_api/v2.1/storageContainerTypes/`) — Container type registration
- Azure Key Vault — Client secret retrieval
- Dataverse Web API — BU list, table CRUD

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Security Role | New custom role or reuse existing? | Reuse existing admin role (e.g., System Administrator) | No new Dataverse security role needed; BFF endpoint filter checks existing admin role membership |
| Token Flow (Phase 1) | OBO vs app-only for Phase 1? | BUs have own containers but NOT own apps in Phase 1. Accommodate OBO if not too complex. | Phase 1: single app registration, app-only tokens. Architect `SpeAdminGraphService` for OBO extensibility but don't implement OBO flow yet. |
| Phase 1 Scope | Include `sprk_specontainertypeconfig` in Phase 1? | Yes — it's foundational for BU scoping | All 3 Dataverse tables are Phase 1 scope |
| Dashboard Data | Live Graph calls vs background sync? | Background sync + manual refresh button | Implement `SpeDashboardSyncService` (BackgroundService) for periodic sync; dashboard serves cached data; "Refresh" button triggers on-demand sync |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Single app registration in Phase 1**: All BUs share one Azure app registration (from BFF env vars). `sprk_specontainertypeconfig.owningAppId` will initially be the same for all configs. Multi-app support is architecturally ready but Phase 1 uses one app.
- **Dashboard sync interval**: BackgroundService runs every 15 minutes. Configurable via appsettings.
- **Audit log cleanup**: No auto-purge. Follows Dataverse table retention policies. Admin can filter by date range in UI.
- **File upload size limit**: Follows existing BFF upload limits. Large file upload (>250MB) uses chunked upload via Graph API.
- **Search scope**: Search uses Microsoft Graph search API (`/search/query`). Does not implement custom search indexing.
- **Sitemap placement**: Admin app accessible from Administration area in model-driven app sitemap.
- **Container type limit**: Display warning when approaching 25 container types per tenant limit.

## Unresolved Questions

- [ ] **OBO configuration**: When OBO is needed (Phase 2+), will the owning app registration be configured for OBO trust? — Blocks: Phase 2 user-context file operations
- [ ] **Consuming app scenario**: When would a separate consuming app ID be needed vs. using the owning app for everything? — Blocks: Phase 3 multi-tenant consuming tenant management
- [ ] **eDiscovery permissions**: Does the BFF app registration have SecurityEvents.Read.All and eDiscovery permissions? — Blocks: Phase 3 eDiscovery dashboard

## Architecture Overview

```
┌─────────────────────────────────────────────┐
│  Dataverse Model-Driven App                 │
│  ┌───────────────────────────────────────┐  │
│  │  sprk_speadmin (Code Page HTML)       │  │
│  │  React 18 + Fluent v9 + viteSingleFile│  │
│  │  @spaarke/ui-components reuse         │  │
│  │  @spaarke/auth (authenticatedFetch)   │  │
│  └───────────────┬───────────────────────┘  │
│                  │ /api/spe/*               │
└──────────────────┼──────────────────────────┘
                   │
┌──────────────────┼──────────────────────────┐
│  Sprk.Bff.Api    │                          │
│  ┌───────────────┴───────────────────────┐  │
│  │  SpeAdminEndpoints.cs                 │  │
│  │  + SpeAdminAuthorizationFilter        │  │
│  └───────────────┬───────────────────────┘  │
│  ┌───────────────┴───────────────────────┐  │
│  │  SpeAdminGraphService.cs              │  │
│  │  (multi-config credential resolution) │  │
│  │  SpeAuditService.cs                   │  │
│  │  SpeDashboardSyncService.cs (BGSvc)   │  │
│  └───────────────┬───────────────────────┘  │
└──────────────────┼──────────────────────────┘
                   │
        ┌──────────┼──────────┐
        │          │          │
   Graph API   Key Vault   Dataverse
   (SPE ops)   (secrets)   (config/audit)
```

## Dataverse Tables

### sprk_speenvironment

| Column | Type | Purpose |
|--------|------|---------|
| sprk_speenvironmentid | PK GUID | Primary key |
| sprk_name | String | Display name (e.g., "Production", "Dev") |
| sprk_tenantid | String | Azure AD tenant ID |
| sprk_tenantname | String | Tenant display name |
| sprk_rootsiteurl | String | SharePoint root site URL |
| sprk_graphendpoint | String | Graph API endpoint |
| sprk_isdefault | Boolean | Default environment flag |
| sprk_status | OptionSet | active / inactive |

### sprk_specontainertypeconfig

| Column | Type | Purpose |
|--------|------|---------|
| sprk_specontainertypeconfigid | PK GUID | Primary key |
| sprk_name | String | Config display name |
| sprk_businessunitid | Lookup(BU) | Dataverse Business Unit |
| sprk_environmentid | Lookup(sprk_speenvironment) | Parent environment |
| sprk_containertypeid | String | SPE Container Type ID (GUID from Graph) |
| sprk_containertypename | String | Container type display name |
| sprk_billingclassification | OptionSet | trial / standard / directToCustomer |
| sprk_owningappid | String | Azure App Registration Client ID |
| sprk_owningappdisplayname | String | App display name |
| sprk_keyvaultsecretname | String | Key Vault secret reference (never the actual secret) |
| sprk_consumingappid | String (optional) | Secondary/guest app client ID |
| sprk_consumingappkeyvaultsecret | String (optional) | Consuming app Key Vault secret ref |
| sprk_isregistered | Boolean | Whether CT is registered on consuming tenant |
| sprk_registeredon | DateTime | Registration date |
| sprk_delegatedpermissions | String | Comma-separated delegated permissions |
| sprk_applicationpermissions | String | Comma-separated application permissions |
| sprk_defaultcontainerid | String (optional) | Default container for this CT |
| sprk_maxstorageperbytes | Whole Number | Max storage per container |
| sprk_sharingcapability | OptionSet | disabled / externalUserSharingOnly / etc. |
| sprk_isitemversioningenabled | Boolean | Version tracking enabled |
| sprk_itemmajorsversionlimit | Whole Number | Max major versions |
| sprk_status | OptionSet | active / inactive |
| sprk_notes | Multiline String | Admin notes |

### sprk_speauditlog

| Column | Type | Purpose |
|--------|------|---------|
| sprk_speauditlogid | PK GUID | Primary key |
| sprk_operation | String | Operation name (e.g., "CreateContainer") |
| sprk_category | OptionSet | ContainerType / Container / Permission / File / Search / Security |
| sprk_targetresourceid | String | ID of affected resource |
| sprk_targetresourcename | String | Name of affected resource |
| sprk_responsestatus | Whole Number | HTTP status code |
| sprk_responsesummary | String | Response summary or error message |
| sprk_environmentid | Lookup(sprk_speenvironment) | Environment context |
| sprk_containertypeconfigid | Lookup(sprk_specontainertypeconfig) | Config context |
| sprk_businessunitid | Lookup(BU) | Business Unit context |
| sprk_performedby | String | User who performed operation |
| sprk_performedon | DateTime | Timestamp |

## BFF API Endpoint Groups

All endpoints under `/api/spe/*` with `SpeAdminAuthorizationFilter`.

### Configuration Endpoints
- `GET /api/spe/environments` — List environments
- `POST /api/spe/environments` — Create environment
- `PUT /api/spe/environments/{id}` — Update environment
- `DELETE /api/spe/environments/{id}` — Delete environment
- `GET /api/spe/configs` — List container type configs (filter by BU, environment)
- `POST /api/spe/configs` — Create container type config
- `PUT /api/spe/configs/{id}` — Update container type config
- `DELETE /api/spe/configs/{id}` — Delete container type config
- `GET /api/spe/businessunits` — List Dataverse Business Units

### Container Type Endpoints
- `GET /api/spe/containertypes?configId={id}` — List container types
- `POST /api/spe/containertypes?configId={id}` — Create container type
- `GET /api/spe/containertypes/{typeId}?configId={id}` — Get container type details
- `PUT /api/spe/containertypes/{typeId}/settings?configId={id}` — Update settings
- `POST /api/spe/containertypes/{typeId}/register?configId={id}` — Register on consuming tenant
- `GET /api/spe/containertypes/{typeId}/permissions?configId={id}` — List app permissions

### Container Endpoints
- `GET /api/spe/containers?configId={id}` — List containers
- `POST /api/spe/containers?configId={id}` — Create container
- `GET /api/spe/containers/{containerId}?configId={id}` — Get container
- `PATCH /api/spe/containers/{containerId}?configId={id}` — Update container
- `POST /api/spe/containers/{containerId}/activate?configId={id}` — Activate
- `POST /api/spe/containers/{containerId}/lock?configId={id}` — Lock (read-only)
- `POST /api/spe/containers/{containerId}/unlock?configId={id}` — Unlock

### Container Permission Endpoints
- `GET /api/spe/containers/{containerId}/permissions?configId={id}` — List permissions
- `POST /api/spe/containers/{containerId}/permissions?configId={id}` — Add permission
- `PATCH /api/spe/containers/{containerId}/permissions/{permId}?configId={id}` — Update permission
- `DELETE /api/spe/containers/{containerId}/permissions/{permId}?configId={id}` — Remove permission

### Container Column Endpoints
- `GET /api/spe/containers/{containerId}/columns?configId={id}` — List columns
- `POST /api/spe/containers/{containerId}/columns?configId={id}` — Create column
- `PATCH /api/spe/containers/{containerId}/columns/{colId}?configId={id}` — Update column
- `DELETE /api/spe/containers/{containerId}/columns/{colId}?configId={id}` — Delete column

### Container Custom Property Endpoints
- `GET /api/spe/containers/{containerId}/customproperties?configId={id}` — List properties
- `PUT /api/spe/containers/{containerId}/customproperties?configId={id}` — Update properties

### File/Folder Endpoints
- `GET /api/spe/containers/{containerId}/items?configId={id}&folderId={folderId}` — List items in folder
- `POST /api/spe/containers/{containerId}/items/upload?configId={id}&folderId={folderId}` — Upload file
- `GET /api/spe/containers/{containerId}/items/{itemId}?configId={id}` — Get item details
- `DELETE /api/spe/containers/{containerId}/items/{itemId}?configId={id}` — Delete item
- `GET /api/spe/containers/{containerId}/items/{itemId}/content?configId={id}` — Download file
- `GET /api/spe/containers/{containerId}/items/{itemId}/preview?configId={id}` — Preview URL
- `GET /api/spe/containers/{containerId}/items/{itemId}/versions?configId={id}` — List versions
- `GET /api/spe/containers/{containerId}/items/{itemId}/thumbnails?configId={id}` — Get thumbnails
- `POST /api/spe/containers/{containerId}/items/{itemId}/sharing?configId={id}` — Create sharing link
- `POST /api/spe/containers/{containerId}/folders?configId={id}&parentId={parentId}` — Create folder

### Search Endpoints
- `POST /api/spe/search/containers?configId={id}` — Search containers
- `POST /api/spe/search/items?configId={id}` — Search items within containers

### Recycle Bin Endpoints
- `GET /api/spe/recyclebin?configId={id}` — List deleted containers
- `POST /api/spe/recyclebin/{containerId}/restore?configId={id}` — Restore container
- `DELETE /api/spe/recyclebin/{containerId}?configId={id}` — Permanent delete

### Security Endpoints
- `GET /api/spe/security/alerts?configId={id}` — List security alerts
- `GET /api/spe/security/score?configId={id}` — Get secure score

### Dashboard Endpoints
- `GET /api/spe/dashboard/metrics?configId={id}` — Get cached dashboard metrics
- `POST /api/spe/dashboard/refresh?configId={id}` — Trigger manual refresh

### Audit Endpoints
- `GET /api/spe/audit?configId={id}&from={date}&to={date}&category={cat}` — Query audit log

## UI Navigation Structure

| Section | Components | Key Features |
|---------|-----------|--------------|
| **Dashboard** | MetricsCards, RecentActivityGrid, RefreshButton | Cached stats, manual refresh, container/storage counts |
| **Container Types** | ContainerTypeGrid, ContainerTypeDetail, RegisterWizard | CRUD, settings editor, registration flow with permission selection |
| **Containers** | ContainerGrid, ContainerDetail, PermissionPanel, ColumnEditor, CustomPropertyEditor | Full lifecycle, inline permission/column/property management |
| **File Browser** | FileBrowserGrid, BreadcrumbNav, FileDetailPanel, FileUploadZone | Navigate folders, upload, preview, manage metadata/versions |
| **Search** | SearchPanel, ContainerResultsGrid, ItemResultsGrid | Cross-container search with bulk actions (delete, export, permissions) |
| **Recycle Bin** | RecycleBinGrid | Restore or permanently delete containers |
| **Security** | AlertsGrid, SecureScoreCard | Read-only security overview |
| **Audit Log** | AuditLogGrid with filters | Filter by date, category, BU, config; export |
| **Settings** | EnvironmentConfig, ContainerTypeConfig | CRUD for both config tables |

## Code Page File Structure

```
src/solutions/SpeAdminApp/
├── src/
│   ├── App.tsx                            # Main app with navigation
│   ├── main.tsx                           # Entry point (createRoot)
│   ├── components/
│   │   ├── layout/
│   │   │   ├── AppShell.tsx               # Nav + content layout
│   │   │   ├── BuContextPicker.tsx        # BU → CT Config → Env cascade
│   │   │   └── NavigationPanel.tsx        # Left nav sections
│   │   ├── dashboard/
│   │   │   ├── DashboardPage.tsx          # Summary metrics
│   │   │   └── MetricsCards.tsx           # Stat cards
│   │   ├── container-types/
│   │   │   ├── ContainerTypesPage.tsx     # List + CRUD
│   │   │   ├── ContainerTypeDetail.tsx    # Detail panel
│   │   │   └── RegisterWizard.tsx         # Registration flow
│   │   ├── containers/
│   │   │   ├── ContainersPage.tsx         # List + CRUD
│   │   │   ├── ContainerDetail.tsx        # Detail + tabs
│   │   │   ├── PermissionPanel.tsx        # Permission management
│   │   │   ├── ColumnEditor.tsx           # Column definitions
│   │   │   └── CustomPropertyEditor.tsx   # Custom properties
│   │   ├── files/
│   │   │   ├── FileBrowserPage.tsx        # File/folder browser
│   │   │   └── FileDetailPanel.tsx        # File metadata, versions
│   │   ├── search/
│   │   │   ├── SearchPage.tsx             # Search interface
│   │   │   ├── ContainerResultsGrid.tsx   # Actionable container results
│   │   │   └── ItemResultsGrid.tsx        # Actionable item results
│   │   ├── recycle-bin/
│   │   │   └── RecycleBinPage.tsx         # Deleted containers
│   │   ├── security/
│   │   │   └── SecurityPage.tsx           # Alerts + score
│   │   ├── audit/
│   │   │   └── AuditLogPage.tsx           # Audit log viewer
│   │   └── settings/
│   │       ├── SettingsPage.tsx           # Config management
│   │       ├── EnvironmentConfig.tsx      # Environment CRUD
│   │       └── ContainerTypeConfig.tsx    # CT Config CRUD
│   ├── contexts/
│   │   ├── BuContext.tsx                  # Selected BU + config state
│   │   └── ThemeContext.tsx               # Theme detection
│   ├── hooks/
│   │   ├── useSpeApi.ts                   # Typed API client wrapper
│   │   ├── useContainers.ts              # Container operations
│   │   ├── useContainerTypes.ts          # Container type operations
│   │   └── useSearch.ts                  # Search operations
│   ├── services/
│   │   └── speApiClient.ts              # authenticatedFetch wrapper for /api/spe/*
│   ├── types/
│   │   └── spe.ts                        # All SPE TypeScript interfaces
│   └── utils/
│       └── validators.ts                 # GUID validation, input validation
├── index.html                            # HTML template with theme detection
├── vite.config.ts                        # Vite + viteSingleFile + shared lib aliases
├── package.json
├── tsconfig.json
└── dist/
    └── speadmin.html                     # Deployable single-file output
```

## BFF Implementation Notes

### Multi-Config Credential Resolution

```
1. Client selects BU → loads configs for that BU
2. Client selects config → sends configId with every API call
3. BFF reads sprk_specontainertypeconfig by configId
4. BFF reads parent sprk_speenvironment for tenantId
5. BFF resolves client secret from Key Vault by keyVaultSecretName
6. BFF creates GraphServiceClient with resolved credentials
7. BFF executes Graph API call and returns result
```

### Token Strategy (Phase 1)

| Operation Category | Token Type | Notes |
|-------------------|------------|-------|
| Container Type CRUD | App-only (Client Credentials) | Uses owning app from config |
| Container Type Registration | App-only (Client Credentials) | PUT to SharePoint REST API |
| Container CRUD | App-only (Client Credentials) | Phase 1: all app-only |
| File operations | App-only (Client Credentials) | Phase 1: app-only; Phase 2: add OBO |
| Search | App-only (Client Credentials) | Phase 1: app-only |
| Security alerts | App-only (Client Credentials) | Uses BFF default app |
| Dashboard sync | App-only (Client Credentials) | BackgroundService, no user context |

### Key BFF Services

- `SpeAdminGraphService` — Creates Graph clients per-config, caches by configId + TTL, handles pagination, retry with exponential backoff for 429
- `SpeAuditService` — Logs all mutating operations to `sprk_speauditlog` via Dataverse Web API
- `SpeDashboardSyncService` — BackgroundService that syncs container counts, storage usage, recent activity to cache (Redis or in-memory)
- `SpeAdminAuthorizationFilter` — Endpoint filter checking user has admin role (reuses existing Dataverse role check)

## Phasing

### Phase 1 (MVP)

- All 3 Dataverse tables (`sprk_speenvironment`, `sprk_specontainertypeconfig`, `sprk_speauditlog`)
- BU picker + config cascade
- BFF endpoints: configuration, containers, permissions, files, audit, dashboard
- Code Page: Dashboard, Containers (active/deleted), File Browser, Settings (environments + configs)
- Audit logging for all operations
- Dark mode support
- Single app registration (app-only tokens)

### Phase 2

- Container Type management (create/register/settings)
- Container columns and custom properties editors
- Search panel (containers + items) with actionable grids
- Security dashboard (alerts, scores)
- Recycle bin management
- OBO token flow for user-context file operations

### Phase 3

- eDiscovery read-only dashboard
- Retention label management
- Multi-tenant consuming tenant management
- Bulk operations (batch delete, batch permission assignment)
- Multi-app registration support (different owning app per BU)

## Graph API Permissions Required

| Permission | Type | Purpose |
|-----------|------|---------|
| `FileStorageContainerType.Manage.All` | Application | Container type management |
| `FileStorageContainerTypeReg.Selected` | Application | Container type registration |
| `FileStorageContainer.Selected` | Application | Container operations (app context) |
| `FileStorageContainer.Selected` | Delegated | Container operations (Phase 2 OBO) |
| `Files.Read.All` | Delegated | Search operations (Phase 2) |
| `SecurityEvents.Read.All` | Delegated | Security alerts (Phase 3) |

## SPE Rate Limits (Reference)

| Scope | Limit |
|-------|-------|
| Per container | 3,000 resource units/min |
| Per app per tenant | 12,000 resource units/min |
| Per user | 600 resource units/min |
| Container creation (peak) | 5/second per consuming tenant |

BFF must implement throttling awareness: detect 429 responses, retry with exponential backoff, surface throttling indicator in UI.

## SPE Resource Limits (Reference)

| Resource | Limit |
|----------|-------|
| Container types per tenant | 25 (increasable) |
| Container types per app | 1 |
| Containers per type per tenant | 100,000 |
| Storage per container type per tenant | 100 TB |
| Files/folders per container | 30 million |
| Storage per container | 25 TB |
| File size | 250 GB |
| Version count per file | 500 (configurable) |

---

*AI-optimized specification. Original design: design.md*
