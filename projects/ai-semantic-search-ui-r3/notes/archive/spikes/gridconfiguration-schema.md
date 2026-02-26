# Investigation: sprk_gridconfiguration Schema for Saved Search Storage

> **Task**: R3-004
> **Date**: 2026-02-24
> **Status**: Complete
> **Blocks**: Tasks 041 (useSavedSearches hook), 042 (SavedSearchSelector component)

---

## 1. Complete Field Inventory of sprk_gridconfiguration

Source: `src/solutions/SpaarkeCore/entities/sprk_gridconfiguration/entity-schema.md`

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|---|---|---|---|---|---|
| sprk_gridconfigurationid | Grid Configuration | Uniqueidentifier | Auto | - | Primary key (GUID) |
| sprk_name | Name | String | **Yes** | **100** | Configuration display name |

### Core Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|---|---|---|---|---|---|
| sprk_entitylogicalname | Entity Logical Name | String | **Yes** | 100 | Target entity for this config (e.g., "sprk_event") |
| sprk_viewtype | View Type | Choice | **Yes** | - | 1=SavedView, 2=CustomFetchXML, 3=LinkedView |
| sprk_savedviewid | Saved View ID | String | No | 36 | GUID reference to savedquery |
| sprk_fetchxml | FetchXML | Multiline | No | **1,048,576** | Custom FetchXML query |
| sprk_layoutxml | Layout XML | Multiline | No | **1,048,576** | Column layout definition |
| sprk_configjson | Configuration JSON | Multiline | No | **1,048,576** | Additional JSON config (filters, formatting, etc.) |

### Display Fields

| Logical Name | Display Name | Type | Required | Default | Max Length | Description |
|---|---|---|---|---|---|---|
| sprk_isdefault | Is Default | Boolean | No | false | - | Whether this is the default view |
| sprk_sortorder | Sort Order | Integer | No | 100 | - | Display order (lower = first) |
| sprk_iconname | Icon Name | String | No | - | 50 | Fluent UI icon name |
| sprk_description | Description | Multiline | No | - | 2,000 | Admin notes |

### System Fields

| Logical Name | Type | Description |
|---|---|---|
| statecode | State | Active/Inactive |
| statuscode | Status | Status reason |
| createdon | DateTime | Created timestamp |
| modifiedon | DateTime | Modified timestamp |
| createdby | Lookup | Creator |
| modifiedby | Lookup | Last modifier |

### Choice Values for sprk_viewtype

| Value | Label | Description |
|---|---|---|
| 1 | SavedView | Reference to existing savedquery |
| 2 | CustomFetchXML | Inline FetchXML and layout |
| 3 | LinkedView | Reference to another configuration |

### Security

| Role | Create | Read | Write | Delete |
|---|---|---|---|---|
| System Administrator | Yes | Yes | Yes | Yes |
| System Customizer | Yes | Yes | Yes | Yes |
| Basic User | No | **Yes** | No | No |

**Key observation**: Basic Users can READ but cannot CREATE, WRITE, or DELETE. This is a problem for saved searches, which require per-user write access. See Section 6 (Recommendations).

---

## 2. ViewService.ts API Summary

Source: `src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts`

### Public Methods

| Method | Signature | Description |
|---|---|---|
| `getViews` | `(entityLogicalName: string, options?: IGetViewsOptions) => Promise<IViewDefinition[]>` | Fetches all views (savedquery + userquery + custom), merged and sorted by sortOrder then name |
| `getDefaultView` | `(entityLogicalName: string, options?: IGetViewsOptions) => Promise<IViewDefinition \| undefined>` | Returns first `isDefault` view, or first view if none is default |
| `getViewById` | `(viewId: string, entityLogicalName: string) => Promise<IViewDefinition \| undefined>` | Lookup by ID; checks cache first, then direct savedquery fetch |
| `clearCache` | `(entityLogicalName?: string) => void` | Clears 5-minute in-memory cache |

### IGetViewsOptions

```typescript
interface IGetViewsOptions {
  includeCustom?: boolean;    // Include sprk_gridconfiguration records (default: false)
  includePersonal?: boolean;  // Include userquery records (default: false)
  viewTypes?: ViewType[];     // Filter by type
}
```

### Key Behaviors

- **READ-ONLY**: ViewService has no create, update, or delete methods. It is purely a query/read service.
- **Caching**: 5-minute TTL in-memory Map cache, keyed by `{entity}_{includeCustom}_{includePersonal}`.
- **Graceful degradation**: If `sprk_gridconfiguration` entity does not exist, logs debug message and returns empty array (no error thrown).
- **Query filter**: Filters by `sprk_entitylogicalname eq '{entity}'` AND `statecode eq 0` (active only).
- **Selected fields**: `sprk_gridconfigurationid, sprk_name, sprk_entitylogicalname, sprk_viewtype, sprk_savedviewid, sprk_fetchxml, sprk_layoutxml, sprk_configjson, sprk_isdefault, sprk_sortorder, sprk_iconname`.

### IViewDefinition (output type)

```typescript
interface IViewDefinition {
  id: string;
  name: string;
  entityLogicalName: string;
  fetchXml: string;
  layoutXml: string;
  isDefault?: boolean;
  viewType: "savedquery" | "userquery" | "custom";
  sortOrder?: number;
  iconName?: string;
  columns?: IColumnDefinition[];
}
```

**Important gap**: `IViewDefinition` does not carry `configJson`. The JSON blob is fetched but not exposed through ViewService's mapping. Only `ConfigurationService` exposes `configJson`.

---

## 3. ConfigurationService.ts API Summary

Source: `src/client/shared/Spaarke.UI.Components/src/services/ConfigurationService.ts`

### Public Methods

| Method | Signature | Description |
|---|---|---|
| `getConfigurations` | `(entityLogicalName: string) => Promise<IGridConfiguration[]>` | Read all active configs for an entity |
| `getDefaultConfiguration` | `(entityLogicalName: string) => Promise<IGridConfiguration \| undefined>` | Find default or first config |
| `getConfigurationById` | `(configurationId: string) => Promise<IGridConfiguration \| undefined>` | Direct lookup by GUID |
| `toViewDefinition` | `(config: IGridConfiguration) => IViewDefinition` | Convert to ViewService format |
| `toViewDefinitions` | `(configs: IGridConfiguration[]) => IViewDefinition[]` | Batch convert |
| `clearCache` | `(entityLogicalName?: string) => void` | Clear cache |
| `checkEntityExists` | `() => Promise<boolean>` | Probe entity existence |

**Also READ-ONLY**: No create, update, or delete methods exist in ConfigurationService either.

### IGridConfiguration (typed output, includes configJson)

```typescript
interface IGridConfiguration {
  id: string;
  name: string;
  entityLogicalName: string;
  viewType: GridConfigViewType;
  savedViewId?: string;
  fetchXml?: string;
  layoutXml?: string;
  configJson?: IGridConfigJson;     // <-- Parsed JSON blob
  isDefault: boolean;
  sortOrder: number;
  iconName?: string;
  description?: string;
  stateCode: number;
}
```

### IGridConfigJson (current shape of configjson content)

```typescript
interface IGridConfigJson {
  columnOverrides?: IColumnOverride[];
  defaultFilters?: IDefaultFilter[];
  rowFormatting?: IRowFormattingRule[];
  features?: IGridFeatures;
  cssClasses?: string[];
}
```

This is the existing typed shape for grid-specific configuration. It does NOT contain any fields for semantic search (query, filters, viewMode, columns, sortColumn, etc.).

---

## 4. ViewSelector.tsx Usage Pattern

Source: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx`

### Component Props

| Prop | Type | Default | Description |
|---|---|---|---|
| xrm | XrmContext | required | Xrm context for WebApi access |
| entityLogicalName | string | required | Entity to fetch views for |
| selectedViewId | string? | - | Currently selected view |
| onViewChange | (view: IViewDefinition) => void | - | Selection callback |
| includeCustomViews | boolean | false | Include sprk_gridconfiguration |
| includePersonalViews | boolean | false | Include userquery |
| groupByType | boolean | false | Group by type in dropdown |
| compact | boolean | false | Smaller size variant |

### Behavior

- Creates `ViewService` internally via `useMemo`.
- Loads views on mount or when `entityLogicalName` changes.
- Auto-selects default view if no `selectedViewId` provided.
- Groups views into "System Views", "Personal Views", "Custom Views" when `groupByType=true`.
- Renders Fluent UI v9 `Dropdown` with `Option` and `OptionGroup`.

### Relevance for Saved Searches

The SavedSearchSelector.tsx component (planned in spec.md) follows the same pattern as ViewSelector but will:
- Query `sprk_gridconfiguration` filtered differently (by a "search config" view type or context field).
- Support CRUD (save new, update, delete) — which ViewSelector does not.
- Display saved search names, not grid view names.

---

## 5. Field Mapping: Saved Search Schema to Entity Fields

### Saved Search JSON (from spec.md)

```json
{
  "name": "Active Litigation Contracts",
  "searchDomain": "Documents",
  "query": "contract amendments",
  "filters": {
    "documentTypes": ["Contract"],
    "fileTypes": ["pdf", "docx"],
    "matterTypes": ["Litigation"],
    "dateRange": { "from": "2025-01-01", "to": null },
    "threshold": 50,
    "searchMode": "hybrid"
  },
  "viewMode": "grid",
  "columns": ["name", "similarity", "documentType", "parentEntity", "modified"],
  "sortColumn": "similarity",
  "sortDirection": "desc",
  "graphClusterBy": "matterType"
}
```

### Mapping Table

| Saved Search Field | Entity Field | Compatible? | Notes |
|---|---|---|---|
| `name` (top-level) | `sprk_name` (String, 100 chars) | **YES** | Direct 1:1 mapping. 100 chars sufficient for search names. |
| Entire JSON blob | `sprk_configjson` (Multiline, 1MB) | **YES** | Store entire saved search JSON here. 1MB is vastly more than needed (~500 bytes typical). |
| `searchDomain` | `sprk_entitylogicalname` (String, 100) | **PARTIAL** | Could use this field but semantics differ: `searchDomain` is "Documents" (a search concept), while `sprk_entitylogicalname` is "sprk_document" (a Dataverse entity logical name). Recommendation: store `searchDomain` inside configjson and set `sprk_entitylogicalname` to a sentinel like `"semantic_search"` or to the mapped entity logical name. |
| `viewMode` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `query` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `filters.*` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `columns` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `sortColumn` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `sortDirection` | (no field) | **N/A** | Stored inside `sprk_configjson`. |
| `graphClusterBy` | (no field) | **N/A** | Stored inside `sprk_configjson`. |

### Auxiliary Field Mappings

| Entity Field | Proposed Usage for Saved Searches | Compatible? |
|---|---|---|
| `sprk_viewtype` | Use a **new choice value** (e.g., `4 = SemanticSearch`) to distinguish from grid configs | **NEEDS EXTENSION** |
| `sprk_entitylogicalname` | Set to mapped entity logical name (e.g., `"sprk_document"`, `"sprk_matter"`) or sentinel `"semantic_search"` for cross-domain | OK but semantic mismatch |
| `sprk_isdefault` | Mark system default searches (e.g., "All Documents") | **YES** |
| `sprk_sortorder` | Order in saved search dropdown | **YES** |
| `sprk_iconname` | Fluent icon for saved search (e.g., "SearchRegular") | **YES** |
| `sprk_description` | Admin notes about the saved search | **YES** |
| `sprk_fetchxml` | Not used for saved searches (search uses BFF API, not FetchXML) | N/A (leave null) |
| `sprk_layoutxml` | Not used (columns stored in configjson) | N/A (leave null) |
| `sprk_savedviewid` | Not used | N/A (leave null) |

---

## 6. Compatibility Assessment

### Verdict: AS-IS with conventions (preferred) or NEEDS EXTENSION (optional quality-of-life improvement)

### What Works AS-IS

The existing schema CAN store saved search data **without any Dataverse schema changes**:

1. **`sprk_name`** (100 chars) -- stores saved search display name. Sufficient.
2. **`sprk_configjson`** (1MB multiline) -- stores the entire saved search JSON blob. More than sufficient for the ~500 byte payloads.
3. **`sprk_isdefault`** -- marks system default searches.
4. **`sprk_sortorder`** -- controls dropdown ordering.
5. **`sprk_iconname`** -- Fluent icon for the dropdown entry.
6. **`sprk_description`** -- admin notes.
7. **`sprk_entitylogicalname`** -- can be used for domain scoping (map "Documents" to "sprk_document", etc.).
8. **`sprk_viewtype`** -- can use existing value `2` (CustomFetchXML) as a semantic category, since saved searches do not use FetchXML. Alternatively, convention-based filtering in configjson.

### What Would Benefit from Extension

| Enhancement | Type | Justification |
|---|---|---|
| Add `sprk_viewtype` value `4 = SemanticSearch` | Choice extension | Clean type discrimination. Avoids mixing grid configs and search configs. Makes queries more precise. |
| Add `sprk_ownerid` (Owner lookup) or change to User-owned | Entity property | Currently Organization-owned. All users share all records. Saved searches should be per-user. Without this, all users see all saved searches. |

### Critical Issue: Ownership Model

The entity is **Organization-owned** (not User-owned). This means:
- **All saved searches are visible to all users** in the organization.
- There is **no per-user ownership** -- you cannot filter "my saved searches" vs "shared searches" using standard Dataverse ownership patterns.
- Basic Users can READ but cannot CREATE/WRITE/DELETE.

**Impact on spec requirement FR-08**: Spec says "personal only (user sees their own saved searches). System-provided defaults visible to all." This CANNOT be achieved with Organization-owned entity without a workaround.

**Workarounds** (no schema change):
1. Add a `createdBy` filter in the query. The `createdby` system field exists and can filter by current user. However, Basic Users still cannot create/write records.
2. Store user ID inside `configjson` and filter client-side.

**Proper fix** (schema change):
- Change entity ownership to **User** (User/Team owned). This enables:
  - Per-user security automatically via Dataverse security model.
  - Basic Users can create/write/delete their own records.
  - System admins can create "shared" default searches visible to all via security role sharing.

---

## 7. Recommended Approach for Tasks 041/042

### Option A: AS-IS (No Schema Changes) -- RECOMMENDED for MVP

Use existing schema with conventions:

1. **`sprk_viewtype = 2`** (CustomFetchXML) for saved searches. Differentiate via a discriminator property inside `configjson`.
2. **`sprk_entitylogicalname`** = domain-mapped entity logical name (`"sprk_document"`, `"sprk_matter"`, `"sprk_project"`, `"sprk_invoice"`).
3. **`sprk_configjson`** = full saved search JSON with an added `"_type": "semantic-search"` discriminator.
4. **Filter query**: `sprk_entitylogicalname eq '{entity}' and statecode eq 0` + parse configjson client-side to filter by `_type === "semantic-search"`.
5. **Per-user filtering**: Use `createdby` system field with `_createdby_value eq '{currentUserId}'` OData filter.

**Saved search JSON (stored in `sprk_configjson`):**
```json
{
  "_type": "semantic-search",
  "_version": 1,
  "searchDomain": "Documents",
  "query": "contract amendments",
  "filters": {
    "documentTypes": ["Contract"],
    "fileTypes": ["pdf", "docx"],
    "matterTypes": ["Litigation"],
    "dateRange": { "from": "2025-01-01", "to": null },
    "threshold": 50,
    "searchMode": "hybrid"
  },
  "viewMode": "grid",
  "columns": ["name", "similarity", "documentType", "parentEntity", "modified"],
  "sortColumn": "similarity",
  "sortDirection": "desc",
  "graphClusterBy": "matterType"
}
```

**Pros:**
- Zero deployment friction (no solution import, no schema migration).
- Works today in any environment where `sprk_gridconfiguration` exists.
- Version field (`_version`) allows future schema evolution.

**Cons:**
- Security: Basic Users cannot create records (Organization-owned). May need elevated security role or BFF API proxy for CRUD.
- Type discrimination is convention-based (configjson `_type` field), not schema-enforced.
- `sprk_entitylogicalname` semantics are stretched (mixing entity logical names with search domain concepts).

### Option B: Minimal Schema Extension -- RECOMMENDED for production quality

Add two changes to `sprk_gridconfiguration`:

| Change | Details |
|---|---|
| 1. Add `sprk_viewtype` choice value `4 = SemanticSearch` | Clean type discrimination. Query: `sprk_viewtype eq 4`. No configjson parsing needed for type filtering. |
| 2. Modify entity to User/Team owned | Enables per-user saved searches with standard Dataverse security. Basic Users can create/own their records. |

**If ownership change is too disruptive** (affects existing grid configs), alternative:
- Keep Organization-owned.
- Add a `sprk_owneruserid` String(36) field to store the creating user's ID.
- Filter by this field for "my searches" vs "shared" (where field is null or matches system admin).
- Grant Basic User Create/Write privilege on the entity (scoped to own records if possible via security role adjustment).

### Option C: Proxy Through BFF API

If Dataverse security model is too restrictive:
- Create a BFF endpoint `POST /api/search/saved-searches` that performs CRUD on `sprk_gridconfiguration` server-side using the service account.
- Client calls BFF API instead of Xrm.WebApi directly.
- BFF can enforce per-user scoping and validation.

**This approach is the most aligned with ADR-013 (extend BFF)** but adds API development work.

---

## 8. CRUD Method Gap Analysis

### Current Services (READ-ONLY)

| Operation | ViewService | ConfigurationService | Exists? |
|---|---|---|---|
| List/Query | `getViews()` | `getConfigurations()` | YES |
| Get by ID | `getViewById()` | `getConfigurationById()` | YES |
| Get default | `getDefaultView()` | `getDefaultConfiguration()` | YES |
| **Create** | -- | -- | **NO** |
| **Update** | -- | -- | **NO** |
| **Delete** | -- | -- | **NO** |

### Required for useSavedSearches Hook (Task 041)

The `useSavedSearches` hook must implement:

| Operation | Method Needed |
|---|---|
| List saved searches for current user + domain | `getSavedSearches(domain, userId)` |
| Load a specific saved search | `getSavedSearchById(id)` |
| Save new search | `createSavedSearch(data)` -- **NEW** |
| Update existing search | `updateSavedSearch(id, data)` -- **NEW** |
| Delete a search | `deleteSavedSearch(id)` -- **NEW** |

### Implementation Recommendation

Create a new `SavedSearchService.ts` in the SemanticSearch code page (not in shared library) that:
1. Uses `Xrm.WebApi.createRecord("sprk_gridconfiguration", {...})` for create.
2. Uses `Xrm.WebApi.updateRecord("sprk_gridconfiguration", id, {...})` for update.
3. Uses `Xrm.WebApi.deleteRecord("sprk_gridconfiguration", id)` for delete.
4. Uses `Xrm.WebApi.retrieveMultipleRecords(...)` for list/query with appropriate filters.

This service is SemanticSearch-specific (not shared) because:
- It encodes saved-search-specific conventions (`_type`, domain mapping).
- CRUD for grid configs is a different concern than CRUD for saved searches.
- The shared ViewService/ConfigurationService remain read-only (no breaking changes).

---

## 9. Summary Decision Matrix

| Aspect | Status | Action Needed |
|---|---|---|
| Field for search name | `sprk_name` (100 chars) | None -- sufficient |
| Field for search JSON | `sprk_configjson` (1MB) | None -- sufficient |
| Field for domain scoping | `sprk_entitylogicalname` (100 chars) | Convention: map domain to entity logical name |
| Field for type discrimination | `sprk_viewtype` (Choice) | **Recommended**: add value 4=SemanticSearch. **Workaround**: use configjson `_type` field |
| Field for display order | `sprk_sortorder` (Integer) | None -- sufficient |
| Field for icon | `sprk_iconname` (50 chars) | None -- sufficient |
| Per-user ownership | Organization-owned entity | **Recommended**: change to User-owned. **Workaround**: filter by `createdby` system field |
| CRUD API | Read-only services exist | **Required**: new SavedSearchService with create/update/delete |
| Security (Basic User write) | Basic User = Read-only | **Required**: grant Create/Write for own records or use BFF proxy |

---

## 10. Recommendation for Task 041/042 Implementer

**Start with Option A (AS-IS with conventions)** to unblock development:
1. Use `sprk_viewtype = 2` with configjson `_type: "semantic-search"` discriminator.
2. Use `sprk_entitylogicalname` for domain scoping (mapped entity name).
3. Create `SavedSearchService.ts` in the SemanticSearch code page `src/client/code-pages/SemanticSearch/src/services/`.
4. Filter by `createdby` for per-user scoping.
5. Add `_version: 1` to configjson for future schema evolution.

**Follow up with Option B** (schema extension) as a separate task if/when:
- Security issues arise with Basic User access.
- Query performance suffers from client-side configjson parsing.
- Other features need clean type discrimination.

**Schema extension task (if needed)** would:
1. Add `sprk_viewtype` choice value `4 = SemanticSearch`.
2. Evaluate changing entity ownership to User/Team.
3. Update security roles to grant Basic User Create/Write (own records).
4. Export and re-import SpaarkeCore solution.

---

*Investigation complete. No code changes made.*
