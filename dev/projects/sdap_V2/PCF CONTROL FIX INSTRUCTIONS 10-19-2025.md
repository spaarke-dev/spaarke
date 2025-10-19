SDAP SPE Quick Create — Dataverse Binding (Claude-Ready)
========================================================

**Purpose:** Vibe-code ready instructions for fixing “undeclared property …” creation errors by using the correct Dataverse binding pattern from a Custom Page–hosted PCF.**Org:** https://spaarkedev1.crm.dynamics.com**Child table:** sprk\_document**Parent table:** sprk\_matter**Lookup attribute on sprk\_document (to Matter):** sprk\_matter**Relationship schema (Matter → Documents):** sprk\_matter\_document**Entity set names:** validate via step 2 (expected: sprk\_matters, sprk\_documents)

1) What We Are Fixing
---------------------

*   **Symptom:** Creating sprk\_document from PCF fails with “An undeclared property ‘…’ which only has property annotations in the payload…”.
    
*   **Cause:** The left-hand side of the binding used an incorrect name (e.g., relationship schema) instead of the **single-valued navigation property** corresponding to the **lookup attribute** on the child.
    
*   **Fix:** Use one of two supported patterns:
    
    *   **Option A (PCF default):** @odata.bind using the **lookup attribute’s navigation property** (here: sprk\_matter).
        
    *   **Option B (metadata-free):** **Relationship-URL POST** against the parent’s navigation collection (sprk\_matter\_document).
        

2) One-Time Validation (Authoritative in This Org)
--------------------------------------------------

Run these against https://spaarkedev1.crm.dynamics.com while authenticated.

*   **Query 1 - Entity Set Names:**

    *   GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk\_matter')?$select=EntitySetName
    *   GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk\_document')?$select=EntitySetName

    *   Expected: sprk\_matters, sprk\_documents. Use returned values in all payloads.

*   **Query 2 - Lookup Navigation Property (Child → Parent):**

    *   GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk\_document')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;$filter=SchemaName eq 'sprk\_matter\_document')

    *   Expected: ReferencingEntityNavigationPropertyName: sprk\_matter (this is the property used in @odata.bind)

    *   Use ReferencingEntityNavigationPropertyName if present; otherwise the lookup attribute logical name (sprk\_matter) typically binds correctly.

*   **Query 3 - Relationship Metadata:**

    *   GET /api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$select=SchemaName,ReferencingEntity,ReferencedEntity&$filter=SchemaName eq 'sprk\_matter\_document'

    *   Expected: SchemaName: sprk\_matter\_document, ReferencingEntity: sprk\_document, ReferencedEntity: sprk\_matter.


> **CRITICAL:** If any values differ, update the code/config below to match the responses.
>
> **Multi-Parent Support:** When adding support for additional parent entities (Account, Contact, Project, Invoice), you MUST run these queries for each parent/table combination. Each relationship requires its own metadata validation. Do not assume navigation property names follow a pattern—always query and cache them.

3) Binding Patterns (Use Either)
--------------------------------

### Option A — @odata.bind via Lookup Attribute’s Navigation Property (PCF default)

*   { "sprk\_documentname": "A", "sprk\_filename": "A.pdf", "sprk\_graphitemid": "01K...", "sprk\_graphdriveid": "drive-1", "sprk\_filesize": 12345, "sprk\_matter@odata.bind": "/sprk\_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"}
    
*   const result = await context.webAPI.createRecord("sprk\_document", payload);
    
*   **Notes:**
    
    *   Left-hand property is the **child lookup attribute’s nav property**. In this org, that is sprk\_matter (confirmed above).
        
    *   Right-hand side uses the **parent entity set** (plural), expected sprk\_matters.
        

### Option B — Relationship-URL POST (No @odata.bind)

*   POST /api/data/v9.2/sprk\_matters()/sprk\_matter\_document

*   { "sprk\_documentname": "A", "sprk\_filename": "A.pdf", "sprk\_graphitemid": "01K...", "sprk\_graphdriveid": "drive-1", "sprk\_filesize": 12345}

*   async function createViaRelationshipUrl( context: ComponentFramework.Context, parentId: string, childPayload: any) { const relationshipSchema = "sprk\_matter\_document"; const id = parentId.replace(/[{}]/g, "").toLowerCase(); const url = \`/api/data/v9.2/sprk\_matters(${id})/${relationshipSchema}\`; const req = { method: "POST", url, headers: { "Content-Type": "application/json" }, body: JSON.stringify(childPayload), getMetadata: () => ({ boundParameter: null, parameterTypes: {}, operationType: 0, operationName: "" }) }; return (context.webAPI as any).execute(req);}

*   **When to prefer:** On the server/BFF for $batch, or in PCF when you want a **metadata-free** path.

*   **Important:** `context.webAPI.execute` requires the `getMetadata()` function exactly as shown (returning bound parameter metadata). See [Microsoft Docs - executeRequest](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-webapi/execute) for details.

*   **Multi-Parent Support:** When supporting multiple parent entities, you must retrieve each parent's **collection navigation property** from the parent entity's `OneToManyRelationships` metadata and substitute it in the URL. Do not hard-code "sprk\_matter\_document"—query it dynamically per parent entity type.
    

4) Metadata Helper (Required for Multi-Parent Support)
-------------------------------------------------------

**Purpose:** Dynamically resolve navigation properties for lookup attributes when supporting multiple parent entities. This eliminates hard-coding and ensures correctness across different parent/child relationships.

**When to use:**
- **Single parent (Matter only):** Optional—you can hardcode `sprk_matter` based on Section 2 validation.
- **Multiple parents (Matter, Account, Contact, etc.):** **REQUIRED**—each parent/child relationship needs its own metadata lookup.

```typescript
export class MetadataService {
  private static cache = new Map<string, string>();

  // Get environment URL for cache key
  private static env(context: ComponentFramework.Context): string {
    return (context as any).page?.getClientUrl?.()
        || (context.webAPI as any).getClientUrl?.()
        || "default";
  }

  // Build cache key: environment::childEntity::lookupAttribute
  private static key(env: string, childEntity: string, lookupAttr: string): string {
    return `${env}::${childEntity}::${lookupAttr}`;
  }

  /**
   * Resolve the navigation property for a lookup attribute on the child entity.
   * Uses ManyToOneRelationships metadata to get ReferencingEntityNavigationPropertyName.
   *
   * @param context - PCF context (context.webAPI works across all PCF hosts)
   * @param childEntityLogicalName - Child entity (e.g., "sprk_document")
   * @param relationshipSchemaName - Relationship schema name (e.g., "sprk_matter_document")
   * @returns Navigation property name for @odata.bind (e.g., "sprk_matter")
   */
  static async getLookupNavProp(
    context: ComponentFramework.Context,
    childEntityLogicalName: string,
    relationshipSchemaName: string
  ): Promise<string> {
    const env = this.env(context);
    const k = this.key(env, childEntityLogicalName, relationshipSchemaName);

    // Check cache first
    const hit = this.cache.get(k);
    if (hit) return hit;

    // Query ManyToOneRelationships on child entity to get navigation property
    const query =
      `EntityDefinitions(LogicalName='${childEntityLogicalName}')` +
      `?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;` +
      `$filter=SchemaName eq '${relationshipSchemaName}')`;

    try {
      const result = await context.webAPI.retrieveMultipleRecords("EntityDefinitions", query);

      if (!result.entities || result.entities.length === 0) {
        throw new Error(`No metadata found for entity '${childEntityLogicalName}'`);
      }

      const entity = result.entities[0];
      const relationships = entity.ManyToOneRelationships as any[];

      if (!relationships || relationships.length === 0) {
        throw new Error(
          `No ManyToOneRelationship found for schema name '${relationshipSchemaName}' ` +
          `on entity '${childEntityLogicalName}'`
        );
      }

      const navProp = relationships[0].ReferencingEntityNavigationPropertyName;

      if (!navProp) {
        throw new Error(
          `ReferencingEntityNavigationPropertyName not found for relationship '${relationshipSchemaName}'`
        );
      }

      // Cache and return
      this.cache.set(k, navProp);
      return navProp;

    } catch (error: any) {
      throw new Error(
        `Metadata query failed for ${childEntityLogicalName}.${relationshipSchemaName}: ${error.message}`
      );
    }
  }

  /**
   * Get parent entity set name (plural form) for a given parent entity logical name.
   * Used in @odata.bind right-hand side: /sprk_matters(guid)
   */
  static async getEntitySetName(
    context: ComponentFramework.Context,
    entityLogicalName: string
  ): Promise<string> {
    const env = this.env(context);
    const cacheKey = `${env}::entityset::${entityLogicalName}`;

    const hit = this.cache.get(cacheKey);
    if (hit) return hit;

    const query = `EntityDefinitions(LogicalName='${entityLogicalName}')?$select=EntitySetName`;

    try {
      const result = await context.webAPI.retrieveMultipleRecords("EntityDefinitions", query);

      if (!result.entities || result.entities.length === 0) {
        throw new Error(`No metadata found for entity '${entityLogicalName}'`);
      }

      const entitySetName = result.entities[0].EntitySetName;

      if (!entitySetName) {
        throw new Error(`EntitySetName not found for '${entityLogicalName}'`);
      }

      this.cache.set(cacheKey, entitySetName);
      return entitySetName;

    } catch (error: any) {
      throw new Error(`Failed to get EntitySetName for '${entityLogicalName}': ${error.message}`);
    }
  }
}
```

**Cache keys:**
- Navigation property: `envUrl::sprk_document::sprk_matter_document` → returns `sprk_matter`
- Entity set name: `envUrl::entityset::sprk_matter` → returns `sprk_matters`

**Why `context.webAPI`?**
`context.webAPI` works across **all PCF hosts** (model-driven apps, canvas apps, custom pages, portals). The fallback to `Xrm.WebApi` is unnecessary and adds complexity without value. Use `context.webAPI` exclusively.

**Multi-Parent Pattern:**
When adding Account, Contact, or other parent entities, call `getLookupNavProp()` once per parent type and cache the result. For 100 files uploaded to the same parent, you make **1 metadata call + 100 creates**—not 100 metadata calls.

5) PCF Service (Create Documents) — Updated
-------------------------------------------

```typescript
export class DocumentRecordService {
  constructor(private context: ComponentFramework.Context<any>) {}

  /**
   * Create Document records for uploaded files using Option A (@odata.bind).
   * Supports multi-parent scenarios via metadata-driven navigation property resolution.
   *
   * @param files - Array of uploaded files from SPE
   * @param parent - Parent record info (Matter, Account, Contact, etc.)
   * @param config - Entity configuration for the parent type
   * @param formData - Optional form data (document name, description)
   */
  async createDocuments(
    files: Array<{ name: string; id: string; size: number }>,
    parent: { parentRecordId: string; containerId?: string },
    config: EntityDocumentConfig,
    formData?: { documentName?: string; description?: string }
  ): Promise<Array<{ success: boolean; fileName: string; recordId?: string; error?: string }>> {

    // Sanitize GUID: remove braces, lowercase
    const parentId = parent.parentRecordId.replace(/[{}]/g, "").toLowerCase();

    // Option A (default): Resolve navigation property from metadata
    // For single parent: this can be hardcoded to "sprk_matter"
    // For multi-parent: dynamically resolve via MetadataService
    const navProp = await MetadataService.getLookupNavProp(
      this.context,
      'sprk_document',
      config.relationshipSchemaName
    );

    const entitySetName = await MetadataService.getEntitySetName(
      this.context,
      config.parentEntity
    );

    const results: Array<{ success: boolean; fileName: string; recordId?: string; error?: string }> = [];

    for (const f of files) {
      // Build whitelist payload (never spread form data directly)
      const payload: any = {
        sprk_documentname: formData?.documentName || f.name.replace(/\.[^/.]+$/, ""),
        sprk_filename: f.name,
        sprk_graphitemid: f.id,
        sprk_graphdriveid: parent.containerId,
        sprk_filesize: f.size,
        // Correct left-hand binding property from metadata
        [`${navProp}@odata.bind`]: `/${entitySetName}(${parentId})`
      };

      // Add description if provided
      if (formData?.description) {
        payload.sprk_documentdescription = formData.description;
      }

      try {
        const r = await this.context.webAPI.createRecord("sprk_document", payload);
        results.push({ success: true, fileName: f.name, recordId: r.id });
      } catch (e: any) {
        results.push({ success: false, fileName: f.name, error: e.message });
      }
    }

    return results;
  }

  /**
   * Option B: Create via relationship URL (metadata-free).
   * Use this for server-side $batch operations or as fallback.
   *
   * @param parentId - Parent record GUID (sanitized)
   * @param childPayload - Document record payload (no lookup fields)
   * @param config - Entity configuration for parent type
   */
  async createViaRelationship(
    parentId: string,
    childPayload: any,
    config: EntityDocumentConfig
  ): Promise<any> {

    // Sanitize GUID: remove braces, lowercase
    const id = parentId.replace(/[{}]/g, "").toLowerCase();

    const entitySetName = await MetadataService.getEntitySetName(
      this.context,
      config.parentEntity
    );

    // Use relationship schema name in URL (e.g., "sprk_matter_document")
    const url = `/api/data/v9.2/${entitySetName}(${id})/${config.relationshipSchemaName}`;

    const req = {
      method: "POST",
      url,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(childPayload),
      // Required by context.webAPI.execute - returns metadata for bound parameter
      // See: https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-webapi/execute
      getMetadata: () => ({
        boundParameter: null,
        parameterTypes: {},
        operationType: 0,
        operationName: ""
      })
    };

    return (this.context.webAPI as any).execute(req);
  }
}
```

**Key improvements:**
1. **Added `formData` parameter** with `documentName` and `description` support
2. **Multi-parent support** via `MetadataService.getLookupNavProp()` and `getEntitySetName()`
3. **Whitelist payload** - explicit field assignment, no form data spreading
4. **Fixed regex** - `replace(/[{}]/g, "")` instead of typo `replace(/\[{}\]/g, "")`
5. **Added description field** when provided in formData
6. **Both Option A and Option B** implementations with proper metadata resolution
7. **Documentation comments** explaining parameters and purpose
8. **Exclusively uses `context.webAPI`** (works across all PCF hosts)

6) Configuration Snapshot (Use in Code)
---------------------------------------

```typescript
export interface EntityDocumentConfig {
  entityName: string;              // Parent entity logical name
  lookupFieldName: string;         // Lookup field on child (sprk_document)
  relationshipSchemaName: string;  // Relationship schema name (for metadata queries)
  containerIdField: string;        // Field on parent holding SPE Container ID
  displayNameField: string;        // Field on parent for display (e.g., matter number)
  entitySetName?: string;          // Optional: entity set name (can be queried dynamically)
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
  'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'  // Optional: can be resolved via MetadataService
  },
  // Add more parent entities as needed:
  // 'account': {
  //   entityName: 'account',
  //   lookupFieldName: 'sprk_account',
  //   relationshipSchemaName: 'sprk_account_document',
  //   containerIdField: 'sprk_containerid',
  //   displayNameField: 'name'
  // }
} as const;
```

**Multi-Parent Pattern:**
- Each parent entity type needs its own configuration
- `relationshipSchemaName` is used by `MetadataService` to query navigation properties
- `entitySetName` can be hardcoded (faster) or queried dynamically (more resilient)

7) Testing Matrix (This Org)
----------------------------

*   **Single create (Option A)**
    
    *   Payload contains sprk\_matter@odata.bind and /sprk\_matters().
        
    *   Expect 201 with an id.
        
*   **Multi-create (Option A)**
    
    *   Loop across 3–5 files; confirm all succeed.
        
    *   Ensure payloads are whitelist-only; no \_sprk\_matter\_value, no stray annotations.
        
*   **Relationship URL (Option B)**
    
    *   POST /sprk\_matters()/sprk\_matter\_document with no lookup fields in body.
        
    *   Expect 204/201 depending on headers.
        
*   **Negative**
    
    *   Wrong entity set (e.g., /sprk\_matter(...)) → should fail; fix to /sprk\_matters(...).
        
    *   Wrong left-hand property (e.g., sprk\_matter\_document@odata.bind) → should fail; fix to sprk\_matter@odata.bind.
        
*   **Performance**
    
    *   Ensure client does not make repeated metadata calls when using Option A hardcoded navProp.
        
    *   For high volume, prefer server-side $batch using **Option B**.
        

8) Practical Guidance
---------------------

*   **In PCF:**

    *   Use **`context.webAPI`** exclusively. It works across **all PCF hosts** (model-driven apps, canvas apps, custom pages, portals).

    *   **No need for `Xrm.WebApi` fallback**—it adds complexity without value since `context.webAPI` is universally available.

    *   Build **whitelist** payloads; never spread form data into the body (security best practice).

    *   Prefer **Option A** (@odata.bind) as primary method—it's standard and well-documented.

    *   Keep **Option B** (relationship URL) available as a robust fallback and for server-side $batch operations.

    *   **Multi-parent support:** Use `MetadataService` to dynamically resolve navigation properties. Don't hardcode values when supporting multiple parent entities.

*   **In Custom Page:**

    *   Pass `parentId` cleanly (no braces, lowercase).

    *   On success, close the side pane and refresh the originating subgrid.

    *   Handle errors gracefully—show user-friendly messages for create failures.

*   **Performance:**

    *   Metadata queries are cached per environment + relationship

    *   For 100 files to same parent: 1 metadata call + 100 creates

    *   Cache persists for PCF lifecycle (browser session)
        

9) Ready-to-Paste Snippets
--------------------------

**Option A payload build (PCF) - Matter example:**

```typescript
// Sanitize GUID: remove braces, lowercase
const parentId = parentRecordId.replace(/[{}]/g, "").toLowerCase();

// Build whitelist payload
const payload = {
  sprk_documentname: formData?.documentName || file.name.replace(/\.[^/.]+$/, ""),
  sprk_filename: file.name,
  sprk_graphitemid: speItemId,
  sprk_graphdriveid: driveId,
  sprk_filesize: file.size,
  sprk_documentdescription: formData?.description || null,
  // Use navigation property from metadata (e.g., "sprk_matter")
  ["sprk_matter@odata.bind"]: `/sprk_matters(${parentId})`
};

await context.webAPI.createRecord("sprk_document", payload);
```

**Option B relationship-URL POST (PCF/server):**

```typescript
// Sanitize GUID: remove braces, lowercase (fixed regex: /[{}]/g not /\[{}\]/g)
const id = parentRecordId.replace(/[{}]/g, "").toLowerCase();

await (context.webAPI as any).execute({
  method: "POST",
  url: `/api/data/v9.2/sprk_matters(${id})/sprk_matter_document`,
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    sprk_documentname: formData?.documentName || fileBase,
    sprk_filename: fileName,
    sprk_graphitemid: speItemId,
    sprk_graphdriveid: driveId,
    sprk_filesize: size,
    sprk_documentdescription: formData?.description || null
  }),
  // Required by context.webAPI.execute - see Microsoft docs
  getMetadata: () => ({
    boundParameter: null,
    parameterTypes: {},
    operationType: 0,
    operationName: ""
  })
});
```

**Code hygiene notes:**
- ✅ Correct: `replace(/[{}]/g, "")` - removes braces from GUID
- ❌ Incorrect: `replace(/\[{}\]/g, "")` - this is a typo (escapes brackets unnecessarily)
- Description field added to both examples
- FormData support for document name override

10) Definition of Done
----------------------

*   Creation succeeds with **Option A** using sprk\_matter@odata.bind → /sprk\_matters().

*   Relationship-URL **Option B** also succeeds for parity.

*   No "undeclared property …" errors in logs.

*   Custom Page closes and the launching **subgrid** refreshes deterministically.

*   **Multi-parent support verified:** Metadata queries work for Matter (and can be extended to Account, Contact, etc.)

*   Performance validated: 1 metadata call per relationship (not per file)

*   Code hygiene: No regex typos, whitelist payloads only, description field supported


---

## CRITICAL: Multi-Parent Deployment Checklist

When deploying this control across **multiple parent entity types** (Matter, Account, Contact, Project, Invoice, etc.), you **MUST**:

1. **Run Section 2 validation queries** for **EACH** parent/child relationship:
   - Query 1: Get entity set names for parent and child
   - Query 2: Get `ReferencingEntityNavigationPropertyName` from child's `ManyToOneRelationships`
   - Query 3: Validate relationship metadata

2. **Add configuration** to `ENTITY_DOCUMENT_CONFIGS` for each parent entity:
   ```typescript
   'account': {
     entityName: 'account',
     lookupFieldName: 'sprk_account',  // Lookup field on sprk_document
     relationshipSchemaName: 'sprk_account_document',
     containerIdField: 'sprk_containerid',
     displayNameField: 'name',
     entitySetName: 'accounts'
   }
   ```

3. **Do NOT assume navigation property names follow a pattern**—always query metadata

4. **Cache key structure** ensures no collisions between different environments or relationships:
   - `{environmentUrl}::sprk_document::{relationshipSchemaName}`

5. **Test with multiple parent types** to ensure metadata resolution works correctly

**Why this matters:**
- Navigation property names are **NOT** guaranteed to match lookup field names
- Different orgs may have different customizations
- Hard-coding values will fail when extending to new parent entities
- The `MetadataService` eliminates guesswork and ensures correctness