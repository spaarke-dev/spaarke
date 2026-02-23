# SDAP Entity Configuration Enhancement

## Design Document

> **Author**: Ralph Schroeder (with Claude Code)
> **Date**: February 22, 2026
> **Status**: Draft
> **Priority**: Medium (quality-of-life improvement, not blocking)

---

## Problem Statement

The SharePoint Document Access Platform (SDAP) currently requires hardcoded configuration in **three separate locations** to onboard a new entity for document upload support. Adding `sprk_communication` to SDAP exposed this pain:

1. **PCF Control** (`EntityDocumentConfig.ts`) — TypeScript config map with 7 fields per entity
2. **Web Resource** (`sprk_subgrid_commands.js`) — JavaScript config object with 4 fields per entity
3. **Ribbon Button** — Must be manually deployed to each entity form

Each location has different field subsets, different update mechanisms (PCF redeploy vs. manual web resource update vs. Dataverse UI), and no validation that they're in sync. Forgetting any one of them causes runtime errors that are difficult to diagnose:

- Missing from PCF config → `"Unsupported parent entity"` error on control init
- Missing from web resource config → Ribbon button silently fails
- Missing from ribbon → No upload button appears on form

**Current onboarding cost**: Rebuild PCF → deploy solution ZIP → manually update web resource in Dataverse → deploy ribbon button. A process that should be a 2-minute admin task requires a full development cycle.

---

## Proposed Solution

Replace the three hardcoded configuration locations with a single **Dataverse configuration table** (`sprk_sdapentityconfig`) that serves as the single source of truth. All consumers (PCF control, web resource, BFF API) query this table at runtime.

### New Dataverse Table: `sprk_sdapentityconfig`

**Display Name**: SDAP Entity Configuration
**Logical Name**: `sprk_sdapentityconfig`
**Ownership**: Organization-owned
**Description**: Defines which Dataverse entities support SDAP document uploads and how they integrate with the document management system.

#### Schema

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Single Line (Primary) | Yes | Human-readable name (e.g., "Matter", "Project", "Communication") |
| Entity Logical Name | `sprk_entitylogicalname` | Single Line | Yes | Dataverse entity logical name (e.g., "sprk_matter") |
| Entity Set Name | `sprk_entitysetname` | Single Line | Yes | OData entity set name for API queries (e.g., "sprk_matters") |
| Lookup Field Name | `sprk_lookupfieldname` | Single Line | Yes | Field on `sprk_document` that links back to this entity (e.g., "sprk_matter") |
| Relationship Schema Name | `sprk_relationshipschemaname` | Single Line | Yes | 1:N relationship name for metadata queries (e.g., "sprk_matter_document") |
| Container ID Field | `sprk_containeridfieldname` | Single Line | Yes | Field storing the SPE container ID on the parent entity (e.g., "sprk_containerid") |
| Display Name Fields | `sprk_displaynamefields` | Single Line | Yes | Comma-separated field names for display (e.g., "sprk_matternumber,sprk_name") |
| Active | `sprk_active` | Yes/No | Yes | Enable/disable without deleting configuration |
| Status | `statuscode` | Status | Yes | Active/Inactive (standard Dataverse status) |

#### Notes on Schema Design

- **`sprk_displaynamefields`** uses comma-separated values rather than a child table because the data is simple (1-3 field names) and a child table would be over-engineering.
- **`sprk_containeridfieldname`** defaults to `"sprk_containerid"` for most entities but allows flexibility if an entity uses a different field name.
- **`sprk_active`** provides a soft-disable mechanism so admins can temporarily remove an entity from SDAP without deleting the config row.
- **No navigation property column**: The `navigationPropertyName` in the current PCF config is deprecated — `MetadataService.getLookupNavProp()` dynamically resolves this from Dataverse metadata at runtime, so it doesn't need to be stored.

---

## Current State: Three Hardcoded Locations

### Location 1: PCF Control — `EntityDocumentConfig.ts`

**Path**: `src/client/pcf/UniversalQuickCreate/control/config/EntityDocumentConfig.ts`

```typescript
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': {
        entityName: 'sprk_matter',
        lookupFieldName: 'sprk_matter',
        relationshipSchemaName: 'sprk_matter_document',
        navigationPropertyName: 'sprk_Matter',  // DEPRECATED
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_matternumber',
        entitySetName: 'sprk_matters'
    },
    // ... 5 more entities
};
```

**Fields used by PCF**: `entityName`, `lookupFieldName`, `relationshipSchemaName`, `containerIdField`, `displayNameField`, `entitySetName`

**Consumed by**:
- `index.ts` — `validateParentEntity()` checks if entity is supported
- `DocumentRecordService.ts` — Uses config for document record creation and OData queries
- `MetadataService.ts` — Uses `relationshipSchemaName` to discover navigation properties

### Location 2: Web Resource — `sprk_subgrid_commands.js`

**Path**: `src/client/pcf/UniversalQuickCreate/Solution/src/WebResources/sprk_subgrid_commands.js`

```javascript
const ENTITY_CONFIGURATIONS = {
    "sprk_matter": {
        entityLogicalName: "sprk_matter",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_matternumber", "sprk_name"],
        entityDisplayName: "Matter"
    },
    // ... 5 more entities
};
```

**Fields used by web resource**: `entityLogicalName`, `containerIdField`, `displayNameFields`, `entityDisplayName`

**Consumed by**:
- `openDocumentUploadDialog()` — Validates entity support, gets container ID, passes context to PCF control
- `getContainerId()` — Reads `containerIdField` from form, falls back to business unit query
- `getParentDisplayName()` — Iterates `displayNameFields` for dialog title

### Location 3: Ribbon Button Configuration

Each entity that supports SDAP document upload needs a ribbon command bar button configured in Dataverse. This is deployed via solution XML or the Dataverse UI. Not hardcoded in source, but must be manually configured per entity.

---

## Target State: Single Dataverse Table

### Architecture

```
┌──────────────────────────────────────────────────┐
│            sprk_sdapentityconfig                  │
│            (Dataverse Table)                      │
│                                                   │
│  sprk_matter    │ sprk_containerid │ sprk_matters │
│  sprk_project   │ sprk_containerid │ sprk_projects│
│  sprk_invoice   │ sprk_containerid │ sprk_invoices│
│  account        │ sprk_containerid │ accounts     │
│  contact        │ sprk_containerid │ contacts     │
│  sprk_communi...│ sprk_containerid │ sprk_commu...│
└─────────┬───────────────┬────────────────┬───────┘
          │               │                │
          ▼               ▼                ▼
   ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
   │ PCF Control │ │ Web Resource│ │  BFF API    │
   │ (index.ts)  │ │ (subgrid_   │ │ (future)    │
   │             │ │  commands)  │ │             │
   │ Fetches on  │ │ Fetches on  │ │ Fetches on  │
   │ init        │ │ button click│ │ request     │
   └─────────────┘ └─────────────┘ └─────────────┘
```

### Consumer Changes

#### PCF Control (`EntityDocumentConfig.ts` → `EntityConfigService.ts`)

Replace the static `ENTITY_DOCUMENT_CONFIGS` map with a service that queries `sprk_sdapentityconfig` at control initialization:

```typescript
// NEW: EntityConfigService.ts
export class EntityConfigService {
    private static cache: Map<string, EntityDocumentConfig> | null = null;

    /**
     * Load all active entity configs from Dataverse table.
     * Called once during PCF init, cached for session lifetime.
     */
    static async loadConfigs(webApi: ComponentFramework.WebApi): Promise<void> {
        if (this.cache) return; // Already loaded

        const result = await webApi.retrieveMultipleRecords(
            "sprk_sdapentityconfig",
            "?$filter=sprk_active eq true and statuscode eq 1" +
            "&$select=sprk_entitylogicalname,sprk_entitysetname," +
            "sprk_lookupfieldname,sprk_relationshipschemaname," +
            "sprk_containeridfieldname,sprk_displaynamefields"
        );

        this.cache = new Map();
        for (const record of result.entities) {
            const config: EntityDocumentConfig = {
                entityName: record["sprk_entitylogicalname"],
                lookupFieldName: record["sprk_lookupfieldname"],
                relationshipSchemaName: record["sprk_relationshipschemaname"],
                containerIdField: record["sprk_containeridfieldname"],
                displayNameField: record["sprk_displaynamefields"]?.split(",")[0] || "",
                entitySetName: record["sprk_entitysetname"]
            };
            this.cache.set(config.entityName, config);
        }
    }

    static getConfig(entityName: string): EntityDocumentConfig | null {
        return this.cache?.get(entityName) || null;
    }

    static isSupported(entityName: string): boolean {
        return this.cache?.has(entityName) ?? false;
    }

    static getSupportedEntities(): string[] {
        return this.cache ? Array.from(this.cache.keys()) : [];
    }
}
```

**Key change**: `validateParentEntity()` in `index.ts` would call `EntityConfigService.isSupported()` instead of the static `isEntitySupported()`. The static module `EntityDocumentConfig.ts` becomes a fallback/default that is overridden by Dataverse data.

#### Web Resource (`sprk_subgrid_commands.js`)

Replace `ENTITY_CONFIGURATIONS` with a Dataverse query:

```javascript
/**
 * Load entity configurations from sprk_sdapentityconfig table.
 * Cached after first load for session lifetime.
 */
var _entityConfigCache = null;

async function loadEntityConfigurations() {
    if (_entityConfigCache) return _entityConfigCache;

    try {
        var result = await Xrm.WebApi.retrieveMultipleRecords(
            "sprk_sdapentityconfig",
            "?$filter=sprk_active eq true and statuscode eq 1" +
            "&$select=sprk_entitylogicalname,sprk_name," +
            "sprk_containeridfieldname,sprk_displaynamefields"
        );

        _entityConfigCache = {};
        result.entities.forEach(function(record) {
            var entityName = record["sprk_entitylogicalname"];
            _entityConfigCache[entityName] = {
                entityLogicalName: entityName,
                containerIdField: record["sprk_containeridfieldname"] || "sprk_containerid",
                displayNameFields: (record["sprk_displaynamefields"] || "").split(","),
                entityDisplayName: record["sprk_name"]
            };
        });

        console.log("[Spaarke] Loaded " + Object.keys(_entityConfigCache).length +
                    " SDAP entity configurations from Dataverse");
        return _entityConfigCache;
    } catch (error) {
        console.error("[Spaarke] Failed to load entity configs, falling back to defaults:", error);
        return ENTITY_CONFIGURATIONS; // Fallback to hardcoded defaults
    }
}
```

**Key change**: `openDocumentUploadDialog()` calls `await loadEntityConfigurations()` before checking entity support. The hardcoded `ENTITY_CONFIGURATIONS` is retained as a **fallback** in case the Dataverse query fails.

#### BFF API (Future Enhancement)

The BFF currently receives container IDs as parameters and doesn't need entity config. However, if future features (e.g., server-side document listing per entity, automated container provisioning) need entity config, the BFF could query the same table via `IDataverseService`.

---

## Migration Strategy

### Phase 1: Create Table and Seed Data

1. Create `sprk_sdapentityconfig` entity in Dataverse
2. Seed with current 6 entity configurations (matter, project, invoice, account, contact, communication)
3. Create a Dataverse form for easy admin management
4. Create a view "Active SDAP Entity Configs" for quick reference

### Phase 2: Update PCF Control

1. Create `EntityConfigService.ts` with Dataverse query + caching
2. Update `index.ts` to call `EntityConfigService.loadConfigs()` during `initializeAsync()`
3. Keep `EntityDocumentConfig.ts` as compile-time fallback (graceful degradation)
4. Update `DocumentRecordService.ts` and `MetadataService.ts` to use `EntityConfigService`
5. Add loading state handling (config fetch is async, ~50-100ms)
6. Rebuild and deploy PCF

### Phase 3: Update Web Resource

1. Add `loadEntityConfigurations()` async function to `sprk_subgrid_commands.js`
2. Update `openDocumentUploadDialog()` to await config load
3. Keep `ENTITY_CONFIGURATIONS` as fallback
4. Update web resource in Dataverse

### Phase 4: Validation and Admin UX

1. Add business rule on `sprk_sdapentityconfig`: entity logical name must be unique
2. Add view columns for easy scanning
3. Add documentation for admins: "How to add a new entity to SDAP"
4. Test: Add a new entity config row → verify PCF and web resource pick it up without any code deployment

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Dataverse query fails on PCF init | Control shows "unsupported entity" error | Retain hardcoded fallback; log warning |
| Config query adds latency to control load | Slightly slower PCF init (~50-100ms) | Cache after first load; query is simple and fast |
| Admin misconfigures entity (wrong field names) | Runtime errors on document upload | Add validation business rules; test with bad data |
| Table not seeded after deployment | All entities appear unsupported | Include seed data in deployment solution; fallback to hardcoded defaults |
| Web resource async load timing | Button click before config loaded | Load config eagerly on form load, not on button click |

---

## Out of Scope

- **Ribbon button automation**: Automatically deploying ribbon buttons to new entities is complex (requires solution XML manipulation). Ribbon configuration remains manual.
- **BFF-side entity config**: The BFF doesn't currently need entity-level SDAP config. If it does in the future, it can query the same table.
- **MSAL/API endpoint configuration**: The `sprk_DocumentOperations.js` and `sprk_emailactions.js` files have hardcoded MSAL config and environment-based API URLs. These are authentication concerns, not SDAP entity config, and should be addressed separately.
- **Event type form mappings**: The `eventConfig.ts` file has hardcoded event type GUIDs → form mappings. This is a different feature area (Events, not SDAP) and should be its own project if table-driven config is desired.
- **VisualHost chart configurations**: Already migrated to Dataverse-driven (`sprk_chartdefinition`). No changes needed.

---

## Success Criteria

1. **Zero-code entity onboarding**: Adding a new entity to SDAP requires only creating a row in `sprk_sdapentityconfig` — no PCF rebuild, no web resource edit, no code deployment
2. **Backward compatible**: Existing hardcoded configs serve as fallback if Dataverse table is empty or query fails
3. **Single source of truth**: One table, queried by all consumers, replaces three independent config locations
4. **Admin-friendly**: Dataverse form and view allow non-developers to manage SDAP entity configuration
5. **No performance regression**: Config query cached after first load; PCF init time increase < 100ms

---

## Appendix: Current Hardcoded Locations Summary

| # | Location | File | Fields | Update Mechanism |
|---|----------|------|--------|------------------|
| 1 | PCF Control | `EntityDocumentConfig.ts` | 7 per entity | Rebuild PCF → deploy solution ZIP |
| 2 | Web Resource | `sprk_subgrid_commands.js` | 4 per entity | Manual edit in Dataverse UI |
| 3 | Ribbon Button | Dataverse ribbon XML | N/A | Solution import or Dataverse UI |

**After this enhancement**: Locations 1 and 2 become dynamic (queried from Dataverse). Location 3 (ribbon) remains manual but is a one-time setup per entity.
