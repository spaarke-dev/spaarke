# SDAP SPE Admin App - Design Document

> **Project**: SharePoint Embedded Administrative Application
> **Type**: Code Page HTML (Dataverse Web Resource)
> **Date**: March 13, 2026
> **Author**: Ralph Schroeder

---

## 1. Executive Summary

Build a comprehensive SharePoint Embedded (SPE) administration application delivered as a Dataverse Code Page HTML web resource. The app provides a single-pane-of-glass admin experience for managing all SPE resources — container types, containers, files, permissions, custom properties, columns, search, recycle bin, and security — directly from within the Dataverse model-driven app. The admin selects a **Dataverse Business Unit** to scope operations to the correct container type and containers for that BU. Configuration data (Azure app registration GUIDs, tenant IDs, container type IDs, owning app IDs, endpoints, and Key Vault secret references) is stored and managed in Dataverse tables, enabling multi-BU, multi-environment, and multi-container-type support with full audit trails.

---

## 2. Goals & Objectives

1. **Centralized SPE Administration** — Replace Postman/PowerShell workflows with a purpose-built UI accessible to authorized admins within Dataverse.
2. **Business Unit Scoping** — Admin selects a Dataverse Business Unit to scope all SPE operations to the correct container type, owning app, and containers for that BU. Supports organizations with multiple BUs each having their own SPE container types.
3. **Configuration-Driven** — Store all Azure/SPE parameters (app registration IDs, tenant IDs, container type IDs, owning app IDs, Key Vault references, endpoints, Graph permission sets) in Dataverse tables so environments can be managed without code changes.
4. **Full SPE API Coverage** — Expose all operations from the SPE Postman collection (Delegated + Application) plus additional operations discovered from the latest Graph API documentation.
5. **Secure by Design** — All Graph API calls flow through the existing BFF API (Sprk.Bff.Api), leveraging existing auth infrastructure (`GraphClientFactory` app-only + OBO flows). No client-side secrets.
6. **Audit & Traceability** — Log admin operations to Dataverse for compliance.

---

## 3. Scope

### 3.1 In Scope

| Category | Operations |
|----------|-----------|
| **Container Types** | Create, list, get, update, delete (trial), register on consuming tenant, manage settings, billing configuration |
| **Containers** | List, create, get, update, activate, delete (soft), restore, permanently delete, list deleted, lock/unlock |
| **Container Permissions** | List, create (assign roles: reader/writer/manager/owner), update, delete |
| **Container Columns** | List, create (text/boolean/dateTime/currency/choice/number/personOrGroup/hyperlinkOrPicture), get, delete |
| **Container Custom Properties** | Get, set (with isSearchable flag), delete |
| **Files & Folders** | List items, upload, get, update, delete, create folder, search drive, list with field filter |
| **File Metadata** | List/set file fields, list thumbnails, list versions, create preview |
| **File Sharing** | List file permissions, create invite, create sharing link, get/delete file permission |
| **Search** | Search containers (by type, title, description, custom properties), search container items (by content, metadata, file type) |
| **Recycle Bin** | List recycled items, restore items, permanently delete items |
| **Security/eDiscovery** | Case management, custodians, data sources, searches, review sets (view-only dashboard) |
| **Security Alerts** | List alerts with severity/category/status filters, secure scores |
| **Retention Labels** | List available retention labels |
| **Business Unit Context** | Select a Dataverse BU to resolve the correct container type, owning app, auth credentials, and default containers for that BU. BU picker persists across navigation. |
| **Configuration** | Manage SPE environment and BU-level configurations (Azure app IDs, owning app IDs, tenant IDs, container type IDs, Key Vault refs, Graph permission grants, endpoints) in Dataverse |
| **Audit Log** | Record admin operations with timestamp, user, operation, target resource, BU context |

### 3.2 Out of Scope

- Azure app registration creation (done in Azure Portal/CLI)
- Billing profile setup (requires SPO Management Shell — documented but not automated)
- eDiscovery case creation/modification (read-only dashboard; full eDiscovery workflows are complex legal processes)
- Multi-tenant consuming tenant management (Phase 2)

---

## 4. Architecture

### 4.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────┐
│  Dataverse Model-Driven App                         │
│  ┌───────────────────────────────────────────────┐  │
│  │  SPE Admin Code Page (sprk_speadmin.html)     │  │
│  │  React 18 + Fluent UI v9                      │  │
│  │  ┌─────────┐ ┌──────────┐ ┌───────────────┐  │  │
│  │  │Container│ │  Files   │ │ Permissions   │  │  │
│  │  │Types    │ │  Browser │ │ Manager       │  │  │
│  │  └────┬────┘ └────┬─────┘ └──────┬────────┘  │  │
│  │       │           │              │            │  │
│  │  ┌────▼───────────▼──────────────▼────────┐   │  │
│  │  │  SPE Admin Service Layer               │   │  │
│  │  │  (authenticatedFetch → BFF API)        │   │  │
│  │  └────────────────┬───────────────────────┘   │  │
│  └───────────────────┼───────────────────────────┘  │
│                      │                               │
│  ┌───────────────────▼───────────────────────────┐  │
│  │  Dataverse Tables                             │  │
│  │  sprk_speenvironment, sprk_speauditlog        │  │
│  └───────────────────────────────────────────────┘  │
└──────────────────────┬──────────────────────────────┘
                       │ HTTPS (Bearer token)
              ┌────────▼────────┐
              │  Sprk.Bff.Api   │
              │  /api/spe/*     │
              └────────┬────────┘
                       │ App-only or OBO token
              ┌────────▼────────┐
              │ Microsoft Graph │
              │ /v1.0 & /beta   │
              └─────────────────┘
```

### 4.2 Component Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| **UI** | React 18, Fluent UI v9, Vite + viteSingleFile | Single HTML web resource, dark mode support |
| **UI Components** | `@spaarke/ui-components` (shared library) | Reuse UniversalDatasetGrid, CommandToolbar, FileUploadZone, WizardShell, SidePaneShell, ChoiceDialog, LookupField, spaarkeLight/spaarkeDark themes |
| **Auth** | `@spaarke/auth` (BridgeStrategy → XrmStrategy → MSAL) | Existing auth library — `authenticatedFetch()`, `initAuth()`, no new auth flows |
| **SDAP Client** | `@spaarke/sdap-client` | Existing typed API client for file operations (DriveItem, Container, upload sessions) |
| **API Gateway** | Sprk.Bff.Api (.NET 8 Minimal API) | New `/api/spe/*` endpoint group, proxies to Graph. Extends existing `GraphClientFactory`, `ContainerOperations`, `SpeFileStore` patterns |
| **Data** | Dataverse custom tables | Configuration, audit logging |
| **Graph API** | Microsoft Graph v1.0 + beta | Container/file operations via BFF proxy |

### 4.3 Shared Library Reuse Strategy

**CRITICAL**: This app MUST maximize reuse of existing shared libraries. No new grid, toolbar, upload, dialog, or theme implementations — use what exists.

| Need | Existing Component to Reuse | Source |
|------|-----------------------------|--------|
| **All data grids** (containers, files, permissions, columns, search results, audit log) | `UniversalDatasetGrid` + `GridView` | `@spaarke/ui-components` |
| **Actionable grid toolbar** (bulk delete, export, permissions) | `CommandToolbar` + `CommandRegistry` | `@spaarke/ui-components` |
| **File upload** | `FileUploadZone` + `UploadedFileList` | `@spaarke/ui-components` |
| **Detail side panels** (container detail, file detail) | `SidePaneShell` | `@spaarke/ui-components` |
| **Confirmation dialogs** (delete, permanent delete) | `ChoiceDialog` | `@spaarke/ui-components` |
| **Multi-step wizards** (container creation, registration) | `WizardShell` + `WizardStepper` | `@spaarke/ui-components` |
| **BU/Entity lookup fields** | `LookupField` | `@spaarke/ui-components` |
| **File preview** | `FilePreview` | `@spaarke/ui-components` |
| **Theming** (light/dark brand themes) | `spaarkeLight` / `spaarkeDark` + `themeDetection` | `@spaarke/ui-components` |
| **Token acquisition & auth fetch** | `initAuth()`, `authenticatedFetch()` | `@spaarke/auth` |
| **File operations API client** | `SdapApiClient` (list, upload, download, DriveItem types) | `@spaarke/sdap-client` |
| **Dataverse queries** (BU list, config CRUD) | `FetchXmlService`, `ViewService` | `@spaarke/ui-components` |
| **Privilege checking** (admin role validation) | `PrivilegeService`, `FieldSecurityService` | `@spaarke/ui-components` |
| **Column rendering** (formatted cells in grids) | `ColumnRendererService` | `@spaarke/ui-components` |
| **Keyboard shortcuts** | `useKeyboardShortcuts` | `@spaarke/ui-components` |

**Reference Patterns from Existing Code:**

| Pattern | Reference Implementation | What to Reuse |
|---------|------------------------|---------------|
| Admin config editor | `src/client/pcf/ScopeConfigEditor/` | Multi-tab editor shell, JSON validation, dropdown population |
| Monitoring dashboard | `src/client/pcf/EmailProcessingMonitor/` | Status cards, metrics layout |
| File list with selection | `src/solutions/DocumentUploadWizard/src/components/DocumentPicker.tsx` | RadioGroup file selector, file metadata display |
| Full-page code page | `src/solutions/LegalWorkspace/` | Vite config, theme detection, BFF auth, 3-column layout |
| Dataset grid with commands | `src/client/pcf/UniversalDatasetGrid/` | CommandBar, ColumnFilter, ConfirmDialog, HyperlinkCell |
| File upload wizard | `src/solutions/DocumentUploadWizard/` | Upload progress, entity association, summary step |

### 4.3 Why BFF Proxy (Not Direct Graph Calls)

1. **No client-side secrets** — App-only tokens (for container type management, application-level operations) require client secrets that cannot be exposed in browser code.
2. **Consistent auth** — Reuse existing BFF auth infrastructure (endpoint filters, token acquisition).
3. **Rate limiting & retry** — BFF can implement Graph API rate limiting (3,000 RU/min per container, 12,000 RU/min per app).
4. **Audit logging** — BFF can log operations before proxying to Graph.
5. **ADR-001 compliance** — Extend BFF, not create separate service.

---

## 5. Dataverse Tables

### 5.1 SPE Environment Configuration (`sprk_speenvironment`)

Stores tenant-level connection/configuration data for each SPE environment (dev, test, prod). One record per Azure tenant.

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| `sprk_speenvironmentid` | Uniqueidentifier (PK) | Yes | Auto-generated |
| `sprk_name` | Text (100) | Yes | Environment name (e.g., "Development", "Production") |
| `sprk_tenantid` | Text (36) | Yes | Azure AD Tenant ID (GUID) — the consuming tenant |
| `sprk_tenantname` | Text (100) | Yes | Tenant name (e.g., "contoso") |
| `sprk_rootsiteurl` | Text (256) | Yes | SharePoint root site URL (e.g., `https://contoso.sharepoint.com`) |
| `sprk_graphendpoint` | Text (256) | No | Graph endpoint override (default: `https://graph.microsoft.com`) |
| `sprk_isdefault` | Boolean | No | Whether this is the default/active environment |
| `sprk_description` | Text (500) | No | Description/notes |
| `sprk_status` | OptionSet | Yes | Active / Inactive |

### 5.2 SPE Container Type Configuration (`sprk_specontainertypeconfig`)

Maps a **Dataverse Business Unit** to an **SPE Container Type** with all auth and app registration parameters needed to administer that container type. This is the core BU-scoping table — each BU can have one or more container types, and each container type has its own owning app registration and credentials.

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| `sprk_specontainertypeconfigid` | Uniqueidentifier (PK) | Yes | Auto-generated |
| `sprk_name` | Text (100) | Yes | Config name (e.g., "Legal Docs - Development") |
| `sprk_businessunitid` | Lookup (businessunit) | Yes | Dataverse Business Unit this config belongs to |
| `sprk_environment` | Lookup (sprk_speenvironment) | Yes | Which tenant/environment this config applies to |
| **Container Type Identity** | | | |
| `sprk_containertypeid` | Text (36) | Yes | SPE Container Type ID (GUID) |
| `sprk_containertypename` | Text (100) | No | Display name of the container type |
| `sprk_billingclassification` | OptionSet | No | Trial / Standard / DirectToCustomer |
| **Owning App Registration** | | | |
| `sprk_owningappid` | Text (36) | Yes | Azure App Registration (Client ID) that owns this container type. Each container type is coupled 1:1 with an owning app. |
| `sprk_owningappdisplayname` | Text (100) | No | App registration display name for reference |
| `sprk_keyvaultsecretname` | Text (100) | Yes | Key Vault secret name for the owning app's client secret (e.g., "spe-legal-app-secret"). BFF resolves this at runtime. |
| **Consuming App Registrations** | | | |
| `sprk_consumingappid` | Text (36) | No | Secondary/guest app ID that has been granted permissions on this container type (if different from owning app) |
| `sprk_consumingappkeyvaultsecret` | Text (100) | No | Key Vault secret name for consuming app (if applicable) |
| **Registration & Permissions** | | | |
| `sprk_isregistered` | Boolean | No | Whether this container type has been registered on the consuming tenant |
| `sprk_registeredon` | DateTime | No | When the container type was registered |
| `sprk_delegatedpermissions` | Text (500) | No | Comma-separated list of delegated permissions granted (e.g., "ReadContent,WriteContent,Create,Read,Write,EnumeratePermissions") |
| `sprk_applicationpermissions` | Text (500) | No | Comma-separated list of application permissions granted (e.g., "Full" or "ReadContent,WriteContent,Create,Delete,ManagePermissions") |
| **Defaults & Settings** | | | |
| `sprk_defaultcontainerid` | Text (36) | No | Default container ID for this BU's container type (quick-access) |
| `sprk_maxstorageperbytes` | Decimal | No | Max storage per container (from container type settings) |
| `sprk_sharingcapability` | OptionSet | No | Disabled / ExternalUserSharingOnly / ExternalUserAndGuestSharing / ExistingExternalUserSharingOnly |
| `sprk_isitemversioningenabled` | Boolean | No | Whether versioning is enabled for this container type |
| `sprk_itemmajorversionlimit` | Integer | No | Max versions per file |
| `sprk_status` | OptionSet | Yes | Active / Inactive |
| `sprk_notes` | Multiline Text | No | Admin notes |

> **Key Design Decisions:**
> - **BU → Container Type is the primary relationship**. When an admin selects a BU in the UI, the app loads all `sprk_specontainertypeconfig` records for that BU to populate the container type picker.
> - **Owning App ID is per-container-type** because SPE enforces a 1:1 relationship between app registration and container type. Different BUs may use different app registrations.
> - **Client secrets are NEVER stored in Dataverse**. Only the Key Vault secret name is stored. The BFF resolves `sprk_keyvaultsecretname` → actual secret from Azure Key Vault at runtime.
> - **Permission grants are recorded** so the admin app can display what operations are available for each container type without trial-and-error.

### 5.3 SPE Audit Log (`sprk_speauditlog`)

Records administrative operations for compliance and troubleshooting.

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| `sprk_speauditlogid` | Uniqueidentifier (PK) | Yes | Auto-generated |
| `sprk_operation` | Text (100) | Yes | Operation name (e.g., "CreateContainer", "DeletePermission") |
| `sprk_category` | OptionSet | Yes | ContainerType / Container / Permission / File / Search / Security |
| `sprk_targetresourceid` | Text (256) | No | ID of the affected resource (container ID, file ID, etc.) |
| `sprk_targetresourcename` | Text (256) | No | Display name of affected resource |
| `sprk_requestbody` | Multiline Text | No | Request payload (sanitized — no secrets) |
| `sprk_responsestatus` | Integer | Yes | HTTP status code from Graph API |
| `sprk_responsesummary` | Text (500) | No | Success/error summary |
| `sprk_environment` | Lookup (sprk_speenvironment) | Yes | Which environment was targeted |
| `sprk_containertypeconfig` | Lookup (sprk_specontainertypeconfig) | No | Which container type config was used |
| `sprk_businessunitid` | Lookup (businessunit) | No | BU context for the operation |
| `sprk_performedby` | Lookup (systemuser) | Yes | User who performed the operation |
| `sprk_performedon` | DateTime | Yes | Timestamp |

### 5.4 Table Relationships

```
businessunit (Dataverse system table)
  │
  └──< sprk_specontainertypeconfig (1:N — one BU can have multiple container types)
         │
         ├── sprk_speenvironment (N:1 — each config belongs to one environment)
         │
         └──< sprk_speauditlog (1:N — audit entries reference the config used)
                │
                └── sprk_speenvironment (N:1 — audit also references environment)
```

### 5.5 Auth Parameter Resolution Flow

When an admin selects a Business Unit and Container Type in the UI:

```
1. User selects BU in UI → "Legal Department"
2. UI queries: GET sprk_specontainertypeconfig?$filter=_sprk_businessunitid_value eq {buId}
3. Returns configs: [{containerTypeId: "abc-123", owningAppId: "def-456", keyVaultSecretName: "spe-legal-secret", ...}]
4. User selects container type → "Legal Document Store"
5. UI sends API call: POST /api/spe/containers?configId={configId}
6. BFF resolves:
   a. Reads sprk_specontainertypeconfig record
   b. Gets owningAppId + keyVaultSecretName
   c. Fetches client secret from Key Vault using keyVaultSecretName
   d. Creates GraphServiceClient with correct tenant/app/secret
   e. Calls Graph API with proper containerTypeId
   f. Logs operation to sprk_speauditlog
```

---

## 6. BFF API Endpoints

New endpoint group under `/api/spe/` in Sprk.Bff.Api. All endpoints require SPE admin role authorization via endpoint filter.

All endpoints accept a `configId` query parameter that identifies the `sprk_specontainertypeconfig` record. The BFF uses this to resolve the correct app registration, Key Vault secret, tenant, and container type for the Graph API call.

### 6.1 Container Type Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containertypes?configId={id}` | `GET /beta/storage/fileStorage/containerTypes` | List container types |
| POST | `/api/spe/containertypes?configId={id}` | `POST /beta/storage/fileStorage/containerTypes` | Create container type |
| GET | `/api/spe/containertypes/{id}?configId={id}` | `GET /beta/storage/fileStorage/containerTypes/{id}` | Get container type |
| PATCH | `/api/spe/containertypes/{id}?configId={id}` | `PATCH /beta/storage/fileStorage/containerTypes/{id}` | Update container type |
| DELETE | `/api/spe/containertypes/{id}?configId={id}` | `DELETE /beta/storage/fileStorage/containerTypes/{id}` | Delete container type (trial only) |
| PUT | `/api/spe/containertypes/{id}/register?configId={id}` | `PUT {RootSiteUrl}/_api/v2.1/storageContainerTypes/{id}/applicationPermissions` | Register on consuming tenant |

### 6.2 Container Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers` | `GET /beta/storage/fileStorage/containers?$filter=containerTypeId eq {id}` | List containers |
| POST | `/api/spe/containers` | `POST /beta/storage/fileStorage/containers` | Create container |
| GET | `/api/spe/containers/{id}` | `GET /beta/storage/fileStorage/containers/{id}` | Get container (with permissions expand) |
| PATCH | `/api/spe/containers/{id}` | `PATCH /beta/storage/fileStorage/containers/{id}` | Update container |
| POST | `/api/spe/containers/{id}/activate` | `POST /beta/storage/fileStorage/containers/{id}/activate` | Activate container |
| DELETE | `/api/spe/containers/{id}` | `DELETE /beta/storage/fileStorage/containers/{id}` | Soft-delete container |
| POST | `/api/spe/containers/{id}/restore` | `POST /beta/storage/fileStorage/containers/{id}/restore` | Restore deleted container |
| DELETE | `/api/spe/containers/{id}/permanent` | `DELETE /beta/storage/fileStorage/deletedContainers/{id}` | Permanently delete |
| POST | `/api/spe/containers/{id}/lock` | `POST /beta/storage/fileStorage/containers/{id}/lock` | Lock container |
| POST | `/api/spe/containers/{id}/unlock` | `POST /beta/storage/fileStorage/containers/{id}/unlock` | Unlock container |
| GET | `/api/spe/containers/deleted` | `GET /beta/storage/fileStorage/deletedContainers` | List deleted containers |

### 6.3 Container Permission Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/permissions` | `GET /beta/.../containers/{id}/permissions` | List permissions |
| POST | `/api/spe/containers/{id}/permissions` | `POST /beta/.../containers/{id}/permissions` | Add permission |
| PATCH | `/api/spe/containers/{id}/permissions/{permId}` | `PATCH /beta/.../permissions/{permId}` | Update permission |
| DELETE | `/api/spe/containers/{id}/permissions/{permId}` | `DELETE /beta/.../permissions/{permId}` | Remove permission |

### 6.4 Container Column Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/columns` | `GET /beta/.../containers/{id}/columns` | List columns |
| POST | `/api/spe/containers/{id}/columns` | `POST /beta/.../containers/{id}/columns` | Create column |
| GET | `/api/spe/containers/{id}/columns/{colId}` | `GET /beta/.../columns/{colId}` | Get column |
| DELETE | `/api/spe/containers/{id}/columns/{colId}` | `DELETE /beta/.../columns/{colId}` | Delete column |

### 6.5 Container Custom Properties Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/properties` | `GET /beta/.../containers/{id}/customProperties` | Get properties |
| PATCH | `/api/spe/containers/{id}/properties` | `PATCH /beta/.../containers/{id}/customProperties` | Set properties |

### 6.6 File & Folder Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/drive` | `GET /v1.0/drives/{id}` | Get drive info |
| GET | `/api/spe/containers/{id}/items` | `GET /v1.0/drives/{id}/items/root/children` | List root items |
| GET | `/api/spe/containers/{id}/items/{itemId}` | `GET /v1.0/drives/{id}/items/{itemId}` | Get item |
| GET | `/api/spe/containers/{id}/items/{itemId}/children` | `GET /v1.0/drives/{id}/items/{itemId}/children` | List folder children |
| POST | `/api/spe/containers/{id}/folders` | `POST /v1.0/drives/{id}/root/children` | Create folder |
| PUT | `/api/spe/containers/{id}/upload` | `PUT /v1.0/drives/{id}/root:/{filename}:/content` | Upload file |
| PATCH | `/api/spe/containers/{id}/items/{itemId}` | `PATCH /v1.0/drives/{id}/items/{itemId}` | Update item |
| DELETE | `/api/spe/containers/{id}/items/{itemId}` | `DELETE /v1.0/drives/{id}/items/{itemId}` | Delete item |
| GET | `/api/spe/containers/{id}/items/{itemId}/fields` | `GET /v1.0/drives/{id}/items/{itemId}/listitem/fields` | List file fields |
| PATCH | `/api/spe/containers/{id}/items/{itemId}/fields` | `PATCH /v1.0/drives/{id}/items/{itemId}/listitem/fields` | Set file fields |
| GET | `/api/spe/containers/{id}/items/{itemId}/versions` | `GET /v1.0/drives/{id}/items/{itemId}/versions` | List versions |
| GET | `/api/spe/containers/{id}/items/{itemId}/thumbnails` | `GET /v1.0/drives/{id}/items/{itemId}/thumbnails` | List thumbnails |
| POST | `/api/spe/containers/{id}/items/{itemId}/preview` | `POST /v1.0/drives/{id}/items/{itemId}/preview` | Create preview URL |
| GET | `/api/spe/containers/{id}/search` | `GET /v1.0/drives/{id}/root/search(q='{query}')` | Search within container |

### 6.7 File Sharing Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/items/{itemId}/permissions` | `GET /v1.0/drives/{id}/items/{itemId}/permissions` | List file permissions |
| POST | `/api/spe/containers/{id}/items/{itemId}/invite` | `POST /v1.0/drives/{id}/items/{itemId}/invite` | Create invite |
| POST | `/api/spe/containers/{id}/items/{itemId}/createLink` | `POST /v1.0/drives/{id}/items/{itemId}/createLink` | Create sharing link |
| GET | `/api/spe/containers/{id}/items/{itemId}/permissions/{permId}` | `GET /v1.0/.../permissions/{permId}` | Get file permission |
| DELETE | `/api/spe/containers/{id}/items/{itemId}/permissions/{permId}` | `DELETE /v1.0/.../permissions/{permId}` | Delete file permission |

### 6.8 Search Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| POST | `/api/spe/search/containers` | `POST /beta/search/query` (entityTypes: drive) | Search containers |
| POST | `/api/spe/search/items` | `POST /beta/search/query` (entityTypes: driveItem) | Search container items |

### 6.9 Search Results — Actionable Grid

Search results (containers and items) are rendered in a **selectable DataGrid** that supports both individual and bulk operations:

**Container Search Results Grid:**

| Column | Source | Description |
|--------|--------|-------------|
| (checkbox) | — | Row selection for bulk actions |
| Display Name | `resource.name` | Container display name (clickable → opens Container Detail) |
| Status | Container status | active / inactive / locked |
| Created | `resource.createdDateTime` | Creation timestamp |
| Storage Used | `resource.size` | Current storage consumption |
| Owner Count | permissions count | Number of assigned users |

**Toolbar Actions (on selected rows):**
- **Delete Containers** — Soft-delete selected containers (with confirmation dialog)
- **Permanently Delete** — For already-deleted containers (with double confirmation)
- **Lock / Unlock** — Toggle lock state on selected containers
- **Export to CSV** — Export selected (or all) search results

**Item Search Results Grid:**

| Column | Source | Description |
|--------|--------|-------------|
| (checkbox) | — | Row selection for bulk actions |
| File Name | `resource.name` | File/folder name (clickable → opens File Detail) |
| Container | `resource.parentReference` | Which container the item lives in |
| Type | `resource.file.mimeType` | File type / folder |
| Size | `resource.size` | File size |
| Modified | `resource.lastModifiedDateTime` | Last modified timestamp |
| Modified By | `resource.lastModifiedBy` | User who last modified |

**Toolbar Actions (on selected rows):**
- **Delete Files** — Delete selected files/folders (with confirmation)
- **Move Files** — Move selected files to a different folder/container
- **Update Permissions** — Add/remove permissions on selected files
- **Download** — Download selected files (single or ZIP for multiple)
- **Export to CSV** — Export result metadata

**Context Menu (right-click on individual row):**
- View Details → opens detail panel
- Copy ID → copies resource ID to clipboard
- Delete → single-item delete with confirmation
- Manage Permissions → opens permission editor for that resource
- Open in Browser → generates a preview URL

### 6.10 Recycle Bin Endpoints

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/containers/{id}/recyclebin` | `GET .../containers/{id}/recycleBin/items` | List recycled items |
| POST | `/api/spe/containers/{id}/recyclebin/restore` | `POST .../recycleBin/items/restore` | Restore items |
| DELETE | `/api/spe/containers/{id}/recyclebin/{itemId}` | `DELETE .../recycleBin/items/{itemId}` | Permanently delete |

### 6.11 Security Endpoints (Read-Only)

| Method | Path | Graph API | Description |
|--------|------|-----------|-------------|
| GET | `/api/spe/security/alerts` | `GET /v1.0/security/alerts` | List security alerts |
| GET | `/api/spe/security/securescores` | `GET /v1.0/security/secureScores` | Get secure scores |
| GET | `/api/spe/security/retentionlabels` | `GET /beta/security/labels/retentionLabels` | List retention labels |

### 6.12 Configuration Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/spe/config/businessunits` | List Dataverse Business Units |
| GET | `/api/spe/config/containertypeconfigs?buId={buId}&envId={envId}` | List container type configs for a BU (optionally filtered by environment) |
| GET | `/api/spe/config/containertypeconfigs/{id}` | Get a single container type config |
| POST | `/api/spe/config/containertypeconfigs` | Create a new BU → container type config |
| PATCH | `/api/spe/config/containertypeconfigs/{id}` | Update a config |
| DELETE | `/api/spe/config/containertypeconfigs/{id}` | Delete a config |
| GET | `/api/spe/config/environments` | List SPE environments from Dataverse |
| GET | `/api/spe/config/environments/{id}` | Get environment details |
| POST | `/api/spe/config/environments` | Create environment |
| PATCH | `/api/spe/config/environments/{id}` | Update environment |
| GET | `/api/spe/auditlog?buId={buId}&configId={id}&from={date}&to={date}` | Query audit log with BU, config, date filters |

---

## 7. UI Design

### 7.1 Layout

The app uses a **left navigation + main content area** layout, similar to Azure Portal or SharePoint Admin Center.

```
┌──────────────────────────────────────────────────────────────────────┐
│  SPE Admin  │  BU: [Legal Dept ▾]  CT: [Legal Docs ▾]  Env: [Dev ▾]│
│             │                                            🌙 [User] │
├─────────────┼────────────────────────────────────────────────────────┤
│             │                                                        │
│  Dashboard  │  ┌──────────────────────────────────────────────────┐  │
│             │  │  Content area changes per nav selection          │  │
│  Container  │  │                                                  │  │
│  Types      │  │  DataGrid / Detail panels / Forms                │  │
│             │  │                                                  │  │
│  Containers │  │  All operations scoped to selected BU +          │  │
│    ├ Active │  │  Container Type + Environment                    │  │
│    ├ Deleted│  │                                                  │  │
│             │  │                                                  │  │
│  Files      │  │                                                  │  │
│             │  │                                                  │  │
│  Search     │  └──────────────────────────────────────────────────┘  │
│             │                                                        │
│  Security   │                                                        │
│             │                                                        │
│  Settings   │                                                        │
│    ├ BU     │                                                        │
│      Config │                                                        │
│    ├ Envs   │                                                        │
│    ├ Audit  │                                                        │
│             │                                                        │
└─────────────┴────────────────────────────────────────────────────────┘
```

**Top Bar Context Pickers (cascade):**
1. **BU Picker** — Select a Dataverse Business Unit. Loads available container type configs for that BU.
2. **Container Type Picker** — Select a container type (populated from `sprk_specontainertypeconfig` records matching the selected BU). This resolves the owning app, auth credentials, and container type ID.
3. **Environment Picker** — Select environment (dev/test/prod). Filters container type configs to the selected environment.

All three selections persist across navigation within the session. Changing BU resets the Container Type picker. Changing Environment filters available Container Type configs.

### 7.2 Navigation Sections

1. **Dashboard** — Overview cards scoped to selected BU: container count, storage usage, recent operations, health status, registration status
2. **Container Types** — CRUD for container types, settings management, registration status on consuming tenant. Shows container types owned by the selected BU's app registration.
3. **Containers**
   - **Active** — List/grid of containers filtered to the selected BU's container type. Detail panel shows: properties, permissions, columns, custom properties, lock state.
   - **Deleted** — Soft-deleted containers with restore/permanent-delete actions
4. **File Browser** — Select a container (from the BU's containers) → tree/grid file browser with upload, create folder, manage metadata, sharing, versions, recycle bin
5. **Search** — Search containers and container items with KQL-style query builder, scoped to selected container type. **Results display in an actionable DataGrid** where admins can select rows and perform bulk operations (delete containers, delete files, update permissions, export results to CSV). Context menu on individual rows provides quick actions.
6. **Security** — Read-only dashboard: alerts, secure scores, retention labels, eDiscovery cases
7. **Settings**
   - **BU Config** — CRUD for `sprk_specontainertypeconfig` records: map Business Units to container types, owning app registrations, Key Vault references, permission grants
   - **Environments** — CRUD for `sprk_speenvironment` records (tenant-level config)
   - **Audit Log** — Searchable/filterable audit log with BU and container type filters, export capability

### 7.3 Key UI Components

| Component | Reuses From | Description |
|-----------|-------------|-------------|
| **BusinessUnitPicker** | `LookupField` (`@spaarke/ui-components`) | Top-bar BU selector with search-as-you-type. Cascades to Container Type picker. |
| **ContainerTypePicker** | `Dropdown` (Fluent v9) | Top-bar container type selector. Populated from `sprk_specontainertypeconfig` for selected BU + environment. |
| **EnvironmentPicker** | `Dropdown` (Fluent v9) | Top-bar environment selector, persists selection |
| **BUConfigEditor** | `WizardShell` (`@spaarke/ui-components`) | Multi-step config wizard: BU selection → app registration → permissions → settings |
| **ContainerList** | `UniversalDatasetGrid` + `CommandToolbar` (`@spaarke/ui-components`) | Sortable/filterable container grid with row selection, bulk actions toolbar, view toggle (grid/card/list) |
| **ContainerDetail** | `SidePaneShell` (`@spaarke/ui-components`) + `TabList` (Fluent v9) | Side panel with tabs: Info, Permissions, Columns, Properties |
| **PermissionEditor** | `ChoiceDialog` + `LookupField` (`@spaarke/ui-components`) | Add/edit/remove permissions with user lookup and role picker |
| **ColumnEditor** | `ChoiceDialog` (`@spaarke/ui-components`) | Create column with type-specific property editors |
| **FileBrowser** | `UniversalDatasetGrid` + `CommandToolbar` (`@spaarke/ui-components`) | File/folder grid with breadcrumb navigation, context menu, column filtering |
| **FileUpload** | `FileUploadZone` + `UploadedFileList` (`@spaarke/ui-components`) | Drag-and-drop upload with validation, progress, file type filtering |
| **FilePreviewPanel** | `FilePreview` (`@spaarke/ui-components`) | In-app file preview for supported types |
| **SearchPanel** | `UniversalDatasetGrid` + `CommandToolbar` (`@spaarke/ui-components`) | KQL query builder with actionable result grid — select results and perform bulk actions |
| **SearchResultsGrid** | `GridView` + `CommandToolbar` + `ColumnRendererService` (`@spaarke/ui-components`) | Selectable rows with formatted columns, context menu, bulk toolbar (delete, move, permissions, export CSV) |
| **AuditLogViewer** | `UniversalDatasetGrid` (`@spaarke/ui-components`) | Filterable audit log with column sorting, pagination, date range filtering |
| **ConfirmDialog** | `ChoiceDialog` (`@spaarke/ui-components`) | Confirmation for destructive operations (delete, permanent delete) — reuse existing pattern |
| **JsonViewer** | `pre` with syntax highlighting | View raw API responses for debugging |
| **RegistrationWizard** | `WizardShell` + `WizardStepper` (`@spaarke/ui-components`) | Multi-step wizard for container type registration on consuming tenant |

### 7.4 Dark Mode

Full dark mode support per ADR-021. Theme is resolved from:
1. URL parameter `?theme=dark`
2. Xrm theme detection (parent frame)
3. System preference fallback

Uses Fluent v9 semantic tokens (`webLightTheme` / `webDarkTheme`) — no hard-coded colors.

---

## 8. Code Page Structure

```
src/solutions/SpeAdminApp/
├── src/
│   ├── main.tsx                          # React 18 createRoot entry, theme detection, auth init
│   ├── App.tsx                           # Root shell: nav + content area + env picker
│   ├── components/
│   │   ├── Shell/
│   │   │   ├── AppShell.tsx              # Layout: left nav + main content
│   │   │   ├── NavMenu.tsx               # Left navigation
│   │   │   ├── BusinessUnitPicker.tsx    # BU selector (cascades to CT picker)
│   │   │   ├── ContainerTypePicker.tsx   # Container type selector (from BU config)
│   │   │   └── EnvironmentPicker.tsx     # Environment selector
│   │   ├── Dashboard/
│   │   │   └── DashboardPage.tsx         # Overview cards and stats
│   │   ├── ContainerTypes/
│   │   │   ├── ContainerTypesPage.tsx    # List + CRUD
│   │   │   ├── ContainerTypeDetail.tsx   # Detail panel with settings
│   │   │   └── ContainerTypeForm.tsx     # Create/edit form
│   │   ├── Containers/
│   │   │   ├── ContainersPage.tsx        # Active containers list
│   │   │   ├── DeletedContainersPage.tsx # Deleted containers list
│   │   │   ├── ContainerDetail.tsx       # Tabbed detail (info, permissions, columns, props)
│   │   │   ├── ContainerForm.tsx         # Create/edit form
│   │   │   ├── PermissionEditor.tsx      # Add/edit/remove permissions
│   │   │   ├── ColumnEditor.tsx          # Manage custom columns
│   │   │   └── PropertiesEditor.tsx      # Manage custom properties
│   │   ├── FileBrowser/
│   │   │   ├── FileBrowserPage.tsx       # Container selector + file tree/grid
│   │   │   ├── FileGrid.tsx             # File/folder listing
│   │   │   ├── FileDetail.tsx           # File metadata, versions, sharing
│   │   │   ├── FileUpload.tsx           # Upload with progress
│   │   │   ├── FolderCreate.tsx         # Create folder dialog
│   │   │   ├── SharingPanel.tsx         # File sharing management
│   │   │   └── RecycleBinPanel.tsx      # Recycle bin viewer
│   │   ├── Search/
│   │   │   ├── SearchPage.tsx           # Search containers + items with query builder
│   │   │   ├── SearchResultsGrid.tsx    # Actionable DataGrid with row selection + bulk actions
│   │   │   └── SearchResultActions.tsx  # Toolbar actions (delete, move, permissions, export)
│   │   ├── Security/
│   │   │   └── SecurityDashboard.tsx    # Alerts, scores, retention labels
│   │   └── Settings/
│   │       ├── BUConfigPage.tsx         # BU → Container Type config CRUD
│   │       ├── BUConfigForm.tsx         # Create/edit config (app IDs, KV refs, permissions)
│   │       ├── EnvironmentsPage.tsx     # Environment CRUD
│   │       └── AuditLogPage.tsx         # Audit log viewer
│   ├── services/
│   │   ├── speAdminService.ts           # All BFF API calls (typed fetch wrappers)
│   │   ├── configService.ts             # Environment config management
│   │   └── auditService.ts              # Audit log queries
│   ├── hooks/
│   │   ├── useBusinessUnits.ts          # BU list from Dataverse
│   │   ├── useContainerTypeConfigs.ts   # BU → container type configs
│   │   ├── useEnvironment.ts            # Current environment context
│   │   ├── useContainerTypes.ts         # Container type CRUD state
│   │   ├── useContainers.ts             # Container CRUD state
│   │   ├── useFileBrowser.ts            # File navigation state
│   │   └── useAuditLog.ts              # Audit log query state
│   ├── types/
│   │   ├── spe.ts                       # SPE domain types (Container, ContainerType, Permission, etc.)
│   │   ├── config.ts                    # Environment + BU config types
│   │   └── audit.ts                     # Audit log types
│   ├── contexts/
│   │   ├── AdminContext.tsx             # Combined BU + ContainerTypeConfig + Environment provider
│   │   └── EnvironmentContext.tsx       # Active environment provider (used by AdminContext)
│   └── utils/
│       ├── formatters.ts               # Date, size, status formatters
│       └── validators.ts              # GUID validation, input validation
├── index.html                          # HTML template with theme detection
├── vite.config.ts                      # Vite + viteSingleFile + shared lib aliases
├── package.json
├── tsconfig.json
└── dist/
    └── speadmin.html                   # DEPLOYABLE single-file output
```

---

## 9. TypeScript Types (Key Interfaces)

```typescript
// spe.ts — SPE domain types

interface BusinessUnit {
  id: string;
  name: string;
  parentBusinessUnitId?: string;
}

interface SpeEnvironment {
  id: string;
  name: string;
  tenantId: string;
  tenantName: string;
  rootSiteUrl: string;
  graphEndpoint: string;
  isDefault: boolean;
  status: "active" | "inactive";
}

interface SpeContainerTypeConfig {
  id: string;
  name: string;
  businessUnitId: string;
  businessUnitName: string;
  environmentId: string;
  environmentName: string;
  // Container Type Identity
  containerTypeId: string;
  containerTypeName?: string;
  billingClassification?: "trial" | "standard" | "directToCustomer";
  // Owning App Registration
  owningAppId: string;
  owningAppDisplayName?: string;
  keyVaultSecretName: string; // Key Vault reference (never the actual secret)
  // Consuming App (optional)
  consumingAppId?: string;
  consumingAppKeyVaultSecret?: string;
  // Registration & Permissions
  isRegistered: boolean;
  registeredOn?: string;
  delegatedPermissions?: string; // comma-separated: "ReadContent,WriteContent,Create,..."
  applicationPermissions?: string; // comma-separated: "Full" or individual permissions
  // Defaults & Settings
  defaultContainerId?: string;
  maxStoragePerBytes?: number;
  sharingCapability?: "disabled" | "externalUserSharingOnly" | "externalUserAndGuestSharing" | "existingExternalUserSharingOnly";
  isItemVersioningEnabled?: boolean;
  itemMajorVersionLimit?: number;
  status: "active" | "inactive";
  notes?: string;
}

// Available granular permissions for container type registration
type SpePermission =
  | "None" | "ReadContent" | "WriteContent" | "Create" | "Delete"
  | "Read" | "Write" | "EnumeratePermissions" | "AddPermissions"
  | "UpdatePermissions" | "DeletePermissions" | "DeleteOwnPermissions"
  | "ManagePermissions" | "ManageContent" | "Full";

interface ContainerType {
  id: string;
  name: string;
  owningAppId: string;
  billingClassification: "trial" | "standard" | "directToCustomer";
  settings: ContainerTypeSettings;
  createdDateTime: string;
}

interface ContainerTypeSettings {
  isDiscoverabilityEnabled: boolean;
  isSearchEnabled: boolean;
  isItemVersioningEnabled: boolean;
  itemMajorVersionLimit: number;
  maxStoragePerContainerInBytes: number;
  sharingCapability: "disabled" | "externalUserSharingOnly" | "externalUserAndGuestSharing" | "existingExternalUserSharingOnly";
  isSharingRestricted: boolean;
}

interface Container {
  id: string;
  displayName: string;
  description: string;
  containerTypeId: string;
  status: "inactive" | "active";
  createdDateTime: string;
  customProperties: Record<string, CustomProperty>;
  itemMajorVersionLimit: number;
  isItemVersioningEnabled: boolean;
  lockState?: "unlocked" | "lockedReadOnly";
  permissions?: ContainerPermission[];
}

interface CustomProperty {
  value: string;
  isSearchable: boolean;
}

interface ContainerPermission {
  id: string;
  roles: ("reader" | "writer" | "manager" | "owner")[];
  grantedToV2: {
    user?: { displayName: string; email: string; id: string };
    group?: { displayName: string; id: string };
  };
}

interface ColumnDefinition {
  id: string;
  name: string;
  displayName: string;
  description: string;
  enforceUniqueValues: boolean;
  hidden: boolean;
  indexed: boolean;
  // Column type (one of):
  text?: { allowMultipleLines: boolean; maxLength: number };
  boolean?: Record<string, never>;
  dateTime?: { displayAs: string; format: string };
  currency?: { locale: string };
  choice?: { allowTextEntry: boolean; choices: string[]; displayAs: string };
  number?: { decimalPlaces: string; displayAs: string; maximum: number; minimum: number };
  personOrGroup?: { allowMultipleSelection: boolean; chooseFromType: string };
  hyperlinkOrPicture?: { isPicture: boolean };
}

interface DriveItem {
  id: string;
  name: string;
  size: number;
  createdDateTime: string;
  lastModifiedDateTime: string;
  file?: { mimeType: string };
  folder?: { childCount: number };
  createdBy: { user: { displayName: string } };
  lastModifiedBy: { user: { displayName: string } };
}

interface AuditLogEntry {
  id: string;
  operation: string;
  category: "ContainerType" | "Container" | "Permission" | "File" | "Search" | "Security";
  targetResourceId?: string;
  targetResourceName?: string;
  responseStatus: number;
  responseSummary?: string;
  environmentId: string;
  environmentName: string;
  containerTypeConfigId?: string;
  containerTypeConfigName?: string;
  businessUnitId?: string;
  businessUnitName?: string;
  performedBy: string;
  performedOn: string;
}
```

---

## 10. BFF API Implementation

### 10.1 Endpoint Organization

New file: `src/server/api/Sprk.Bff.Api/Endpoints/SpeAdminEndpoints.cs`

```csharp
// Endpoint registration pattern (per ADR-008: endpoint filters, not global middleware)
public static class SpeAdminEndpoints
{
    public static void MapSpeAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/spe")
            .AddEndpointFilter<SpeAdminAuthorizationFilter>();

        // Container Types
        group.MapGet("/containertypes", ListContainerTypes);
        group.MapPost("/containertypes", CreateContainerType);
        // ... etc

        // Containers
        group.MapGet("/containers", ListContainers);
        group.MapPost("/containers", CreateContainer);
        // ... etc
    }
}
```

### 10.2 Graph API Proxy Service

New file: `src/server/api/Sprk.Bff.Api/Services/SpeAdminGraphService.cs`

- **Multi-app credential resolution**: Each API call includes a `configId` parameter. The service reads the `sprk_specontainertypeconfig` record to resolve:
  - `owningAppId` → the Azure App Registration to authenticate as
  - `keyVaultSecretName` → the Key Vault secret containing that app's client secret
  - `containerTypeId` → the SPE container type to operate on
  - `tenantId` (from parent `sprk_speenvironment`) → the Azure tenant
- **Dynamic `GraphServiceClient` creation**: Unlike the existing `GraphClientFactory` which uses a single app registration, this service creates Graph clients per-container-type-config, caching them by `configId` with TTL matching token expiry.
- Handles pagination, error mapping, retry with exponential backoff for Graph API throttling (429)
- Logs operations to audit table via `SpeAuditService` with BU and config context

```csharp
// Conceptual flow
public async Task<GraphServiceClient> GetClientForConfig(Guid configId)
{
    var config = await _dataverse.GetAsync<SpeContainerTypeConfig>(configId);
    var env = await _dataverse.GetAsync<SpeEnvironment>(config.EnvironmentId);
    var secret = await _keyVault.GetSecretAsync(config.KeyVaultSecretName);

    return new GraphServiceClient(
        new ClientSecretCredential(env.TenantId, config.OwningAppId, secret.Value),
        scopes: new[] { "https://graph.microsoft.com/.default" }
    );
}
```

### 10.3 Authorization

- `SpeAdminAuthorizationFilter` — Checks user has SPE admin security role in Dataverse
- Container type management operations require app-only tokens (client credentials) using the owning app
- Container/file operations can use delegated (OBO) or app-only based on context
- The selected `sprk_specontainertypeconfig` determines which app registration credentials are used

### 10.4 Token Acquisition Strategy

| Operation Category | Token Type | Azure Permission | App Registration Used |
|-------------------|------------|-----------------|----------------------|
| Container Type CRUD | App-only (Client Credentials) | `FileStorageContainerType.Manage.All` | Owning app (from config) |
| Container Type Registration | App-only (Client Credentials) | `FileStorageContainerTypeReg.Selected` | Owning app (from config) |
| Container CRUD | App-only or OBO | `FileStorageContainer.Selected` | Owning app (from config) |
| File operations | OBO (delegated) | `FileStorageContainer.Selected` (delegated) | Owning app (from config) — OBO exchange uses owning app's client secret |
| Search | OBO (delegated) | `Files.Read.All` (delegated) | Owning app (from config) |
| Security alerts | OBO (delegated) | `SecurityEvents.Read.All` (delegated) | BFF default app (not container-type-specific) |

### 10.5 Required Auth Parameters Per Container Type

The following parameters must be configured in `sprk_specontainertypeconfig` for each BU's container type:

| Parameter | Source | Required For |
|-----------|--------|-------------|
| **Container Type ID** | SPE (created via Graph or PowerShell) | All container operations — identifies which type of container to create/list |
| **Owning App ID** (Client ID) | Azure App Registration | Token acquisition — each container type is owned by exactly one app |
| **Key Vault Secret Name** | Azure Key Vault reference | Token acquisition — client secret for owning app (never stored in Dataverse) |
| **Tenant ID** | Azure AD (from parent `sprk_speenvironment`) | Token acquisition — identifies the Azure tenant |
| **Root Site URL** | SharePoint Admin | Container type registration (`/_api/v2.1/storageContainerTypes/{id}/applicationPermissions`) |
| **Delegated Permissions** | Set during registration | Determines which delegated operations are available (ReadContent, WriteContent, Create, Delete, etc.) |
| **Application Permissions** | Set during registration | Determines which app-only operations are available |
| **Billing Classification** | SPE (set at creation) | Informational — trial types have restrictions (5 containers, 1 GB each, 30-day expiry) |
| **Consuming App ID** (optional) | Azure App Registration | If a secondary/guest app also has permissions on this container type |
| **Sharing Capability** | Container type settings | Controls external sharing: disabled, externalUserSharingOnly, etc. |
| **Is Registered** | Set after registration call | Whether the container type is registered on the consuming tenant |

### 10.6 Relationship to Existing BFF Auth

The existing `GraphClientFactory` in the BFF uses a **single** app registration (from `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` env vars) for all Graph calls. The SPE Admin service extends this pattern:

| Existing BFF Pattern | SPE Admin Pattern |
|---------------------|-------------------|
| Single app registration (env vars) | Multiple app registrations (per container type config from Dataverse) |
| `GraphClientFactory.ForApp()` | `SpeAdminGraphService.GetClientForConfig(configId)` |
| `GraphClientFactory.ForUserAsync()` | `SpeAdminGraphService.GetOboClientForConfig(configId, httpContext)` |
| Client secret from env var | Client secret from Key Vault (resolved by `keyVaultSecretName`) |
| Token cached in Redis | Token cached in Redis (keyed by configId + tokenHash) |

The existing `SpeFileStore` facade and `ContainerOperations` service continue to work for normal document operations using the default app registration. The SPE Admin endpoints use a separate service that resolves credentials per-config.

---

## 11. Opening the Admin App

### 11.1 From Dataverse

The app is opened as a full-page web resource or modal dialog:

```typescript
// Full page (from sitemap or custom button)
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_speadmin",
        data: "theme=light"
    },
    { target: 1 }  // Full page (target: 1) or dialog (target: 2)
);
```

### 11.2 From Sitemap

Add a sitemap entry pointing to the `sprk_speadmin` web resource in the Administration area of the model-driven app.

---

## 12. Graph API Permissions Required

### App Registration: Spaarke BFF (existing)

Add the following permissions to the existing Spaarke BFF app registration:

| Permission | Type | Purpose |
|-----------|------|---------|
| `FileStorageContainerType.Manage.All` | Application | Container type management |
| `FileStorageContainerTypeReg.Selected` | Application | Container type registration |
| `FileStorageContainer.Selected` | Application | Container operations (app context) |
| `FileStorageContainer.Selected` | Delegated | Container operations (user context) |
| `Files.Read.All` | Delegated | Search operations |
| `SecurityEvents.Read.All` | Delegated | Security alerts (optional) |

---

## 13. Deployment

### 13.1 Code Page Build & Deploy

```bash
# Build
cd src/solutions/SpeAdminApp
npm install
npm run build
# Output: dist/speadmin.html

# Deploy to Dataverse
# Use scripts/Deploy-SpeAdmin.ps1 or code-page-deploy skill
```

### 13.2 Dataverse Solution

Include in the Spaarke solution:
- Web resource: `sprk_speadmin` (type: HTML)
- Tables: `sprk_speenvironment`, `sprk_speauditlog`
- Security role: `SPE Administrator` (controls access to admin app + tables)
- Sitemap entry: Administration → SPE Admin

### 13.3 BFF API

Deploy updated Sprk.Bff.Api with new `/api/spe/*` endpoints via existing `bff-deploy` skill.

---

## 14. Security Considerations

1. **No client secrets in browser** — All Graph API calls proxy through BFF API.
2. **Role-based access** — Only users with `SPE Administrator` Dataverse security role can access the app and API endpoints.
3. **Audit trail** — All mutating operations logged to `sprk_speauditlog`.
4. **Confirmation dialogs** — Destructive operations (delete container, permanent delete, remove permission) require explicit confirmation.
5. **Environment isolation** — Each SPE environment config is independent; no cross-environment operations.
6. **Key Vault secrets** — Client secrets stored in Azure Key Vault, referenced by client ID.

---

## 15. SPE API Rate Limits (Reference)

| Scope | Limit |
|-------|-------|
| Per container | 3,000 resource units/min |
| Per app per tenant | 12,000 resource units/min |
| Per user | 600 resource units/min |
| Container creation (peak) | 5/second per consuming tenant |

Resource unit costs: 1 unit (single-item GET), 2 units (list/create/update/delete), 5 units (permission operations).

The BFF API should implement throttling awareness and surface 429 responses gracefully in the UI.

---

## 16. SPE Limits (Reference)

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

## 17. Success Criteria

1. Admin can create/manage container types from within Dataverse
2. Admin can create/activate/manage containers without Postman or PowerShell
3. Admin can browse files, upload, manage metadata and permissions in any container
4. Admin can search across containers and container items
5. All operations are audit-logged
6. Environment configuration is managed through Dataverse tables
7. App supports dark mode and is accessible
8. All Graph API calls proxy through BFF (no direct client-side Graph calls)

---

## 18. Dependencies

| Dependency | Status | Reuse Level | Notes |
|------------|--------|-------------|-------|
| `@spaarke/ui-components` | Existing | **Heavy** | UniversalDatasetGrid, CommandToolbar, FileUploadZone, WizardShell, SidePaneShell, ChoiceDialog, LookupField, FilePreview, spaarkeLight/spaarkeDark themes, FetchXmlService, PrivilegeService, ColumnRendererService, hooks |
| `@spaarke/auth` | Existing | **Heavy** | `initAuth()`, `authenticatedFetch()`, token strategies, error types |
| `@spaarke/sdap-client` | Existing | **Moderate** | `SdapApiClient` for file list/upload/download, `DriveItem`/`Container` types |
| Sprk.Bff.Api | Existing | **Extend** | Needs new `/api/spe/*` endpoint group. Reuse `GraphClientFactory`, `ContainerOperations`, `SpeFileStore` patterns. Extend with multi-app credential resolution via `SpeAdminGraphService`. |
| LegalWorkspace pattern | Existing | **Template** | Vite config, viteSingleFile, shared lib aliases, theme detection, deploy script |
| DocumentUploadWizard pattern | Existing | **Reference** | File upload flow, entity association, progress tracking |
| ScopeConfigEditor pattern | Existing | **Reference** | Admin config editor with tabs, JSON validation, dropdown population |
| Azure Key Vault | Existing | **Integrate** | Store SPE app client secrets (one per container type owning app) |
| Dataverse environment | Existing | **Extend** | New tables: `sprk_speenvironment`, `sprk_specontainertypeconfig`, `sprk_speauditlog` |
| Graph API permissions | **New** | — | Add SPE permissions (`FileStorageContainerType.Manage.All`, `FileStorageContainer.Selected`, etc.) to app registrations |

---

## 19. Phasing

### Phase 1 (MVP)
- Dataverse tables (sprk_speenvironment, sprk_speauditlog)
- BFF endpoints for containers, permissions, files
- Code Page: Dashboard, Containers (active/deleted), File Browser, Settings (environments)
- Audit logging

### Phase 2
- Container Type management (create/register/settings)
- Container columns and custom properties editors
- Search panel (containers + items)
- Security dashboard (alerts, scores)
- Recycle bin management

### Phase 3
- eDiscovery read-only dashboard
- Retention label management
- Multi-tenant consuming tenant management
- Bulk operations (batch delete, batch permission assignment)
