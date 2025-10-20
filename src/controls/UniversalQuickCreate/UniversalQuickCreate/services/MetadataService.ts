/**
 * MetadataService
 *
 * Provides metadata resolution for Dataverse entities and relationships.
 * Caches results per environment to minimize API calls and improve performance.
 *
 * CRITICAL: Navigation property names are CASE-SENSITIVE!
 * Example: sprk_Matter (capital M) vs sprk_matter (lowercase)
 * Always query metadata to get the actual property name - don't assume lowercase!
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
   * CRITICAL: Navigation property names are CASE-SENSITIVE!
   * Example: Returns "sprk_Matter" (capital M), not "sprk_matter" (lowercase)
   *
   * Example:
   * - Child: sprk_document
   * - Relationship: sprk_matter_document
   * - Returns: "sprk_Matter" (the ReferencingEntityNavigationPropertyName) ⚠️ Note capital M!
   * - Usage: ["sprk_Matter@odata.bind"]: "/sprk_matters(guid)"
   *
   * @param context - PCF context
   * @param childEntityLogicalName - Child entity logical name (e.g., "sprk_document")
   * @param relationshipSchemaName - Relationship schema name (e.g., "sprk_matter_document")
   * @returns Navigation property name for @odata.bind (EXACT case from metadata)
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
    // Use $filter to find entity by LogicalName, then $expand to get relationships
    const query =
      `?$filter=LogicalName eq '${childEntityLogicalName}'` +
      `&$expand=ManyToOneRelationships(` +
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

      // Cache and return THE EXACT VALUE from metadata (preserves case!)
      this.cache.set(cacheKey, navProp);
      console.log(`MetadataService: Resolved navigation property for ${relationshipSchemaName}: ${navProp}`);
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

    // Use $filter to find entity by LogicalName
    const query = `?$filter=LogicalName eq '${entityLogicalName}'&$select=EntitySetName`;

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
    // Use $filter to find entity by LogicalName, then $expand to get relationships
    const query =
      `?$filter=LogicalName eq '${parentEntityLogicalName}'` +
      `&$expand=OneToManyRelationships(` +
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
