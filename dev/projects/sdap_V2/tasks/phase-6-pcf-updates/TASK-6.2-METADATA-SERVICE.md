# Task 6.2: Implement MetadataService

## Task Prompt (For AI Agent)

```
You are working on Task 6.2 of Phase 6: Implement MetadataService

BEFORE starting work:
1. Read this entire task document carefully
2. Review Task 6.1 results - you MUST have validated metadata values before proceeding
3. Check if MetadataService.ts already exists in the services folder
4. Review current codebase structure to understand import patterns
5. Update status section with current state (Not Started/In Progress/Blocked/Complete)

DURING work:
1. Create the new file: src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MetadataService.ts
2. Implement all three methods: getLookupNavProp, getEntitySetName, getCollectionNavProp
3. Add comprehensive error handling with friendly messages
4. Implement caching with environment-aware keys
5. Test TypeScript compilation: npm run build
6. Update testing checklist as you complete each test

AFTER completing work:
1. Verify no TypeScript errors or linting warnings
2. Complete the success criteria checklist
3. Commit changes with provided commit message template
4. Fill in task owner, completion date, and status
5. Mark task as Complete and proceed to Task 6.3

Your goal: Create a robust MetadataService that dynamically resolves navigation properties and entity set names from Dataverse metadata, with caching for performance.
```

---

## Overview

**Task ID:** 6.2
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 1 day
**Dependencies:** Task 6.1 (Metadata Validation)
**Status:** Ready to Start

---

## Objective

Create a new `MetadataService` class that dynamically queries Dataverse metadata to resolve navigation property names and entity set names. This service will support multi-parent scenarios and cache results for performance.

---

## File Location

**New File:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MetadataService.ts
```

---

## Implementation

### Full MetadataService Code

```typescript
/**
 * MetadataService
 *
 * Provides metadata resolution for Dataverse entities and relationships.
 * Caches results per environment to minimize API calls and improve performance.
 *
 * Key Methods:
 * - getLookupNavProp: Get navigation property for child → parent lookup (@odata.bind)
 * - getEntitySetName: Get plural entity set name for OData URLs
 * - getCollectionNavProp: Get collection navigation property for parent → child (relationship URL)
 *
 * Performance:
 * - 1 metadata call per relationship per session
 * - Cache persists for PCF lifecycle (browser session)
 * - Cache key includes environment URL to prevent cross-org pollution
 */

export class MetadataService {
  /**
   * In-memory cache for metadata queries
   * Key format: {environmentUrl}::{cacheType}::{identifier}
   */
  private static cache = new Map<string, string>();

  /**
   * Get the environment URL for cache key isolation
   * Supports multiple PCF hosts: Custom Pages, Model-Driven Apps, Canvas Apps
   *
   * @param context - PCF context
   * @returns Environment URL or 'default' if unavailable
   */
  private static getEnvironmentUrl(context: ComponentFramework.Context<any>): string {
    // Try multiple methods to get environment URL
    // context.page.getClientUrl is available in Custom Pages and Model-Driven Apps
    const clientUrl = (context as any).page?.getClientUrl?.();

    if (clientUrl) {
      return clientUrl;
    }

    // Fallback to webAPI internal method (less reliable but available)
    const webApiUrl = (context.webAPI as any).getClientUrl?.();

    if (webApiUrl) {
      return webApiUrl;
    }

    // Last resort: use 'default' (all queries in same org will share cache)
    console.warn('MetadataService: Unable to determine environment URL, using default cache key');
    return 'default';
  }

  /**
   * Build cache key for metadata queries
   * Format: {environmentUrl}::{cacheType}::{identifier}
   *
   * @param env - Environment URL
   * @param cacheType - Type of cache entry (lookup, entityset, collection)
   * @param identifier - Unique identifier (relationship name, entity name, etc.)
   * @returns Cache key string
   */
  private static buildCacheKey(
    env: string,
    cacheType: string,
    identifier: string
  ): string {
    return `${env}::${cacheType}::${identifier}`;
  }

  /**
   * Get the navigation property name for a child → parent lookup relationship.
   * This is used in Option A (@odata.bind) to create the left-hand property name.
   *
   * Example:
   * - Child: sprk_document
   * - Relationship: sprk_matter_document
   * - Returns: "sprk_matter" (the ReferencingEntityNavigationPropertyName)
   * - Usage: ["sprk_matter@odata.bind"]: "/sprk_matters(guid)"
   *
   * @param context - PCF context
   * @param childEntityLogicalName - Child entity logical name (e.g., "sprk_document")
   * @param relationshipSchemaName - Relationship schema name (e.g., "sprk_matter_document")
   * @returns Navigation property name for @odata.bind
   * @throws Error if metadata query fails or navigation property not found
   */
  static async getLookupNavProp(
    context: ComponentFramework.Context<any>,
    childEntityLogicalName: string,
    relationshipSchemaName: string
  ): Promise<string> {
    const env = this.getEnvironmentUrl(context);
    const cacheKey = this.buildCacheKey(env, 'lookup', `${childEntityLogicalName}::${relationshipSchemaName}`);

    // Check cache first
    const cached = this.cache.get(cacheKey);
    if (cached) {
      return cached;
    }

    // Query ManyToOneRelationships on child entity to get navigation property
    const query =
      `EntityDefinitions(LogicalName='${childEntityLogicalName}')` +
      `?$expand=ManyToOneRelationships(` +
      `$select=SchemaName,ReferencingEntityNavigationPropertyName;` +
      `$filter=SchemaName eq '${relationshipSchemaName}'` +
      `)`;

    try {
      const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);

      // Defensive check: ensure entities array exists and has data
      if (!result.entities || result.entities.length === 0) {
        throw new Error(
          `Metadata query returned no results for entity '${childEntityLogicalName}'. ` +
          `This may indicate insufficient permissions to access EntityDefinitions metadata.`
        );
      }

      const entity = result.entities[0];
      const relationships = entity.ManyToOneRelationships as any[];

      // Defensive check: ensure relationships array exists and has data
      if (!relationships || relationships.length === 0) {
        throw new Error(
          `No ManyToOneRelationship found for schema name '${relationshipSchemaName}' ` +
          `on entity '${childEntityLogicalName}'. Verify the relationship exists in Dataverse.`
        );
      }

      const navProp = relationships[0].ReferencingEntityNavigationPropertyName;

      // Defensive check: ensure navigation property value exists
      if (!navProp) {
        throw new Error(
          `ReferencingEntityNavigationPropertyName is null or empty for relationship '${relationshipSchemaName}'. ` +
          `This indicates a metadata configuration issue.`
        );
      }

      // Cache and return
      this.cache.set(cacheKey, navProp);
      return navProp;

    } catch (error: any) {
      // Provide friendly error message for common issues
      if (error.message?.includes('401') || error.message?.includes('Unauthorized')) {
        throw new Error(
          `Permission denied: Unable to access EntityDefinitions metadata. ` +
          `Please ensure your user account has 'Read' permission on Entity Definitions. ` +
          `Original error: ${error.message}`
        );
      }

      if (error.message?.includes('404') || error.message?.includes('Not Found')) {
        throw new Error(
          `Entity or relationship not found: Verify that entity '${childEntityLogicalName}' ` +
          `and relationship '${relationshipSchemaName}' exist in this Dataverse environment. ` +
          `Original error: ${error.message}`
        );
      }

      // Re-throw with context if not a friendly error already
      if (!error.message?.includes('Metadata query') && !error.message?.includes('Permission denied')) {
        throw new Error(
          `Metadata query failed for ${childEntityLogicalName}.${relationshipSchemaName}: ${error.message}`
        );
      }

      throw error;
    }
  }

  /**
   * Get the entity set name (plural form) for a given entity logical name.
   * This is used in OData URLs and @odata.bind right-hand side.
   *
   * Example:
   * - Entity: sprk_matter
   * - Returns: "sprk_matters"
   * - Usage: "/sprk_matters(guid)"
   *
   * @param context - PCF context
   * @param entityLogicalName - Entity logical name (e.g., "sprk_matter")
   * @returns Entity set name (plural)
   * @throws Error if metadata query fails or entity set name not found
   */
  static async getEntitySetName(
    context: ComponentFramework.Context<any>,
    entityLogicalName: string
  ): Promise<string> {
    const env = this.getEnvironmentUrl(context);
    const cacheKey = this.buildCacheKey(env, 'entityset', entityLogicalName);

    // Check cache first
    const cached = this.cache.get(cacheKey);
    if (cached) {
      return cached;
    }

    const query = `EntityDefinitions(LogicalName='${entityLogicalName}')?$select=EntitySetName`;

    try {
      const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);

      // Defensive check: ensure entities array exists and has data
      if (!result.entities || result.entities.length === 0) {
        throw new Error(
          `Metadata query returned no results for entity '${entityLogicalName}'. ` +
          `Verify the entity exists in Dataverse or check metadata access permissions.`
        );
      }

      const entitySetName = result.entities[0].EntitySetName;

      // Defensive check: ensure entity set name exists
      if (!entitySetName) {
        throw new Error(
          `EntitySetName is null or empty for entity '${entityLogicalName}'. ` +
          `This indicates a metadata configuration issue.`
        );
      }

      // Cache and return
      this.cache.set(cacheKey, entitySetName);
      return entitySetName;

    } catch (error: any) {
      // Provide friendly error message for common issues
      if (error.message?.includes('401') || error.message?.includes('Unauthorized')) {
        throw new Error(
          `Permission denied: Unable to access EntityDefinitions metadata. ` +
          `Please ensure your user account has 'Read' permission on Entity Definitions. ` +
          `Original error: ${error.message}`
        );
      }

      if (error.message?.includes('404') || error.message?.includes('Not Found')) {
        throw new Error(
          `Entity not found: Verify that entity '${entityLogicalName}' exists in this Dataverse environment. ` +
          `Original error: ${error.message}`
        );
      }

      // Re-throw with context if not a friendly error already
      if (!error.message?.includes('Metadata query') && !error.message?.includes('Permission denied')) {
        throw new Error(
          `Failed to get EntitySetName for '${entityLogicalName}': ${error.message}`
        );
      }

      throw error;
    }
  }

  /**
   * Get the collection navigation property name for a parent → child relationship.
   * This is used in Option B (relationship URL POST) to build the endpoint URL.
   *
   * Example:
   * - Parent: sprk_matter
   * - Relationship: sprk_matter_document
   * - Returns: "sprk_matter_document" (the ReferencedEntityNavigationPropertyName)
   * - Usage: POST /sprk_matters(guid)/sprk_matter_document
   *
   * @param context - PCF context
   * @param parentEntityLogicalName - Parent entity logical name (e.g., "sprk_matter")
   * @param relationshipSchemaName - Relationship schema name (e.g., "sprk_matter_document")
   * @returns Collection navigation property name
   * @throws Error if metadata query fails or navigation property not found
   */
  static async getCollectionNavProp(
    context: ComponentFramework.Context<any>,
    parentEntityLogicalName: string,
    relationshipSchemaName: string
  ): Promise<string> {
    const env = this.getEnvironmentUrl(context);
    const cacheKey = this.buildCacheKey(env, 'collection', `${parentEntityLogicalName}::${relationshipSchemaName}`);

    // Check cache first
    const cached = this.cache.get(cacheKey);
    if (cached) {
      return cached;
    }

    // Query OneToManyRelationships on parent entity to get collection navigation property
    const query =
      `EntityDefinitions(LogicalName='${parentEntityLogicalName}')` +
      `?$expand=OneToManyRelationships(` +
      `$select=SchemaName,ReferencedEntityNavigationPropertyName;` +
      `$filter=SchemaName eq '${relationshipSchemaName}'` +
      `)`;

    try {
      const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);

      // Defensive check: ensure entities array exists and has data
      if (!result.entities || result.entities.length === 0) {
        throw new Error(
          `Metadata query returned no results for entity '${parentEntityLogicalName}'. ` +
          `This may indicate insufficient permissions to access EntityDefinitions metadata.`
        );
      }

      const entity = result.entities[0];
      const relationships = entity.OneToManyRelationships as any[];

      // Defensive check: ensure relationships array exists and has data
      if (!relationships || relationships.length === 0) {
        throw new Error(
          `No OneToManyRelationship found for schema name '${relationshipSchemaName}' ` +
          `on entity '${parentEntityLogicalName}'. Verify the relationship exists in Dataverse.`
        );
      }

      const navProp = relationships[0].ReferencedEntityNavigationPropertyName;

      // Defensive check: ensure navigation property value exists
      if (!navProp) {
        throw new Error(
          `ReferencedEntityNavigationPropertyName is null or empty for relationship '${relationshipSchemaName}'. ` +
          `This indicates a metadata configuration issue.`
        );
      }

      // Cache and return
      this.cache.set(cacheKey, navProp);
      return navProp;

    } catch (error: any) {
      // Provide friendly error message for common issues
      if (error.message?.includes('401') || error.message?.includes('Unauthorized')) {
        throw new Error(
          `Permission denied: Unable to access EntityDefinitions metadata. ` +
          `Please ensure your user account has 'Read' permission on Entity Definitions. ` +
          `Original error: ${error.message}`
        );
      }

      if (error.message?.includes('404') || error.message?.includes('Not Found')) {
        throw new Error(
          `Entity or relationship not found: Verify that entity '${parentEntityLogicalName}' ` +
          `and relationship '${relationshipSchemaName}' exist in this Dataverse environment. ` +
          `Original error: ${error.message}`
        );
      }

      // Re-throw with context if not a friendly error already
      if (!error.message?.includes('Metadata query') && !error.message?.includes('Permission denied')) {
        throw new Error(
          `Metadata query failed for ${parentEntityLogicalName}.${relationshipSchemaName}: ${error.message}`
        );
      }

      throw error;
    }
  }

  /**
   * Clear the metadata cache
   * Useful for testing or if metadata changes during runtime
   */
  static clearCache(): void {
    this.cache.clear();
  }

  /**
   * Get cache statistics for debugging
   * @returns Object with cache size and keys
   */
  static getCacheStats(): { size: number; keys: string[] } {
    return {
      size: this.cache.size,
      keys: Array.from(this.cache.keys())
    };
  }
}
```

---

## Key Features

### 1. **Environment-Aware Caching**
- Cache key includes environment URL to prevent cross-org pollution
- Supports Custom Pages, Model-Driven Apps, and Canvas Apps
- Fallback to 'default' if environment URL unavailable

### 2. **Three Metadata Resolution Methods**

| Method | Purpose | Returns | Used In |
|--------|---------|---------|---------|
| `getLookupNavProp()` | Child → Parent navigation property | `sprk_matter` | Option A (@odata.bind) |
| `getEntitySetName()` | Plural entity set name | `sprk_matters` | Both Option A & B |
| `getCollectionNavProp()` | Parent → Child navigation property | `sprk_matter_document` | Option B (relationship URL) |

### 3. **Comprehensive Error Handling**
- Friendly error messages for permission issues (401)
- Clear guidance for missing entities/relationships (404)
- Defensive checks for null/empty results
- Detailed error context for troubleshooting

### 4. **Performance Optimized**
- In-memory cache persists for PCF lifecycle
- 1 metadata call per relationship per session
- 100 files uploaded = 1 metadata call + 100 creates

---

## Usage Examples

### Example 1: Get Lookup Navigation Property (Option A)

```typescript
import { MetadataService } from './MetadataService';

// In DocumentRecordService.createDocuments()
const navProp = await MetadataService.getLookupNavProp(
  this.context,
  'sprk_document',
  'sprk_matter_document'
);
// Returns: "sprk_matter"

const payload = {
  sprk_documentname: "Document A",
  [`${navProp}@odata.bind`]: "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
};
```

### Example 2: Get Entity Set Name

```typescript
const entitySetName = await MetadataService.getEntitySetName(
  this.context,
  'sprk_matter'
);
// Returns: "sprk_matters"

const url = `/${entitySetName}(${parentId})`;
// Result: "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
```

### Example 3: Get Collection Navigation Property (Option B)

```typescript
const collectionNavProp = await MetadataService.getCollectionNavProp(
  this.context,
  'sprk_matter',
  'sprk_matter_document'
);
// Returns: "sprk_matter_document"

const entitySetName = await MetadataService.getEntitySetName(
  this.context,
  'sprk_matter'
);

const url = `/api/data/v9.2/${entitySetName}(${parentId})/${collectionNavProp}`;
// Result: "/api/data/v9.2/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)/sprk_matter_document"
```

---

## Testing Checklist

### Unit Tests (Optional but Recommended)

- [ ] Test cache hit after first query
- [ ] Test cache isolation between environments
- [ ] Test error handling for 401 Unauthorized
- [ ] Test error handling for 404 Not Found
- [ ] Test error handling for empty results
- [ ] Test cache clearing
- [ ] Test cache statistics

### Integration Tests

- [ ] getLookupNavProp returns correct value for sprk_matter_document
- [ ] getEntitySetName returns correct value for sprk_matter
- [ ] getEntitySetName returns correct value for sprk_document
- [ ] getCollectionNavProp returns correct value for sprk_matter_document
- [ ] Cache works across multiple calls (verify network requests)
- [ ] Friendly error message displayed when permissions denied

---

## Deployment Steps

1. **Create the file:**
   ```bash
   # From src/controls/UniversalQuickCreate/UniversalQuickCreate/
   mkdir -p services
   # Create MetadataService.ts with code above
   ```

2. **Verify TypeScript compilation:**
   ```bash
   cd src/controls/UniversalQuickCreate
   npm run build
   ```

3. **Check for errors:**
   - No TypeScript compilation errors
   - No linting warnings
   - File properly exported from services folder (if using index.ts)

4. **Commit changes:**
   ```bash
   git add src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MetadataService.ts
   git commit -m "feat(pcf): Add MetadataService for dynamic navigation property resolution

- Implement getLookupNavProp for child → parent lookups (Option A)
- Implement getEntitySetName for OData URLs
- Implement getCollectionNavProp for relationship URLs (Option B)
- Add environment-aware caching for performance
- Add comprehensive error handling with friendly messages

Task: 6.2 - Phase 6 PCF Updates"
   ```

---

## Troubleshooting

### Issue: TypeScript Error - "Cannot find module ComponentFramework"

**Solution:**
Ensure PCF types are installed:
```bash
cd src/controls/UniversalQuickCreate
npm install --save-dev @types/powerapps-component-framework
```

### Issue: "context.webAPI.retrieveMultipleRecords is not a function"

**Solution:**
Verify context is properly typed:
```typescript
constructor(private context: ComponentFramework.Context<any>) {}
```

### Issue: Cache not working (multiple network requests for same query)

**Solution:**
Check cache key generation - verify environment URL is consistent:
```typescript
const stats = MetadataService.getCacheStats();
console.log('Cache stats:', stats);
```

---

## Success Criteria

- [ ] MetadataService.ts file created in correct location
- [ ] All three methods implemented and documented
- [ ] TypeScript compilation successful
- [ ] No linting errors or warnings
- [ ] Cache implementation tested and working
- [ ] Error handling tested with invalid inputs
- [ ] Code committed to repository
- [ ] Ready for Task 6.3 (DocumentRecordService integration)

---

## Next Steps

Once MetadataService is complete and tested:

1. Proceed to [TASK-6.3-DOCUMENT-RECORD-SERVICE.md](./TASK-6.3-DOCUMENT-RECORD-SERVICE.md)
2. Integrate MetadataService into DocumentRecordService
3. Test end-to-end with actual Document creation

---

**Task Owner:** Claude (AI Agent)
**Completion Date:** 2025-10-19
**Reviewed By:** Pending
**Status:** ✅ Complete

**Key Implementation:** MetadataService handles case-sensitive navigation properties correctly (e.g., `sprk_Matter` with capital M)
