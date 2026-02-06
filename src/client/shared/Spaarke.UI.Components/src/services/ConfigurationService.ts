/**
 * ConfigurationService
 *
 * Reads grid configurations from the sprk_gridconfiguration Dataverse table.
 * Provides caching and conversion to IViewDefinition format.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 */

import type { XrmContext } from "../utils/xrmContext";
import type { IViewDefinition } from "../types/FetchXmlTypes";
import type {
  IGridConfiguration,
  IGridConfigurationRecord,
  IGridConfigJson,
  GridConfigViewType,
} from "../types/ConfigurationTypes";

/**
 * Service for reading grid configurations from sprk_gridconfiguration.
 * Integrates with ViewService to provide custom views.
 */
export class ConfigurationService {
  private xrm: XrmContext;
  private cache: Map<string, { configs: IGridConfiguration[]; timestamp: number }> = new Map();
  private cacheTTL: number = 5 * 60 * 1000; // 5 minutes
  private entityExists: boolean | null = null;

  /**
   * Create a new ConfigurationService instance
   * @param xrm - XrmContext providing WebApi access
   */
  constructor(xrm: XrmContext) {
    this.xrm = xrm;
  }

  /**
   * Get all configurations for an entity
   * @param entityLogicalName - Entity to get configurations for
   * @returns Promise resolving to array of grid configurations
   */
  async getConfigurations(entityLogicalName: string): Promise<IGridConfiguration[]> {
    // Check if entity exists
    if (this.entityExists === false) {
      return [];
    }

    // Check cache
    const cached = this.cache.get(entityLogicalName);
    if (cached && Date.now() - cached.timestamp < this.cacheTTL) {
      return cached.configs;
    }

    try {
      const filter = [
        `sprk_entitylogicalname eq '${entityLogicalName}'`,
        "statecode eq 0", // Active only
      ].join(" and ");

      const select = [
        "sprk_gridconfigurationid",
        "sprk_name",
        "sprk_entitylogicalname",
        "sprk_viewtype",
        "sprk_savedviewid",
        "sprk_fetchxml",
        "sprk_layoutxml",
        "sprk_configjson",
        "sprk_isdefault",
        "sprk_sortorder",
        "sprk_iconname",
        "sprk_description",
        "statecode",
      ].join(",");

      const result = await this.xrm.WebApi.retrieveMultipleRecords(
        "sprk_gridconfiguration",
        `?$select=${select}&$filter=${filter}&$orderby=sprk_sortorder`
      );

      this.entityExists = true;

      const configs = (result.entities as IGridConfigurationRecord[]).map((record) =>
        this.mapRecordToConfiguration(record)
      );

      // Cache results
      this.cache.set(entityLogicalName, { configs, timestamp: Date.now() });

      return configs;
    } catch (error) {
      // Entity might not exist - handle gracefully
      if (this.isEntityNotFoundError(error)) {
        this.entityExists = false;
        console.debug("[ConfigurationService] sprk_gridconfiguration entity not found");
        return [];
      }
      console.error("[ConfigurationService] Failed to fetch configurations:", error);
      return [];
    }
  }

  /**
   * Get the default configuration for an entity
   * @param entityLogicalName - Entity to get default for
   * @returns Promise resolving to default configuration or undefined
   */
  async getDefaultConfiguration(entityLogicalName: string): Promise<IGridConfiguration | undefined> {
    const configs = await this.getConfigurations(entityLogicalName);

    // Find configuration marked as default
    const defaultConfig = configs.find((c) => c.isDefault);
    if (defaultConfig) {
      return defaultConfig;
    }

    // Return first configuration (sorted by sortOrder)
    return configs[0];
  }

  /**
   * Get a specific configuration by ID
   * @param configurationId - Configuration ID (sprk_gridconfigurationid)
   * @returns Promise resolving to configuration or undefined
   */
  async getConfigurationById(configurationId: string): Promise<IGridConfiguration | undefined> {
    // Check cache first
    for (const cached of this.cache.values()) {
      const found = cached.configs.find((c) => c.id === configurationId);
      if (found) {
        return found;
      }
    }

    // Entity doesn't exist
    if (this.entityExists === false) {
      return undefined;
    }

    try {
      const select = [
        "sprk_gridconfigurationid",
        "sprk_name",
        "sprk_entitylogicalname",
        "sprk_viewtype",
        "sprk_savedviewid",
        "sprk_fetchxml",
        "sprk_layoutxml",
        "sprk_configjson",
        "sprk_isdefault",
        "sprk_sortorder",
        "sprk_iconname",
        "sprk_description",
        "statecode",
      ].join(",");

      const record = await this.xrm.WebApi.retrieveRecord(
        "sprk_gridconfiguration",
        configurationId,
        `?$select=${select}`
      );

      this.entityExists = true;
      return this.mapRecordToConfiguration(record as IGridConfigurationRecord);
    } catch (error) {
      if (this.isEntityNotFoundError(error)) {
        this.entityExists = false;
      }
      return undefined;
    }
  }

  /**
   * Convert a configuration to IViewDefinition format
   * Used by ViewService when merging custom views
   * @param config - Grid configuration to convert
   * @returns View definition
   */
  toViewDefinition(config: IGridConfiguration): IViewDefinition {
    return {
      id: config.id,
      name: config.name,
      entityLogicalName: config.entityLogicalName,
      fetchXml: config.fetchXml || "",
      layoutXml: config.layoutXml || "",
      isDefault: config.isDefault,
      viewType: "custom",
      sortOrder: config.sortOrder,
      iconName: config.iconName,
    };
  }

  /**
   * Convert multiple configurations to view definitions
   * @param configs - Configurations to convert
   * @returns Array of view definitions
   */
  toViewDefinitions(configs: IGridConfiguration[]): IViewDefinition[] {
    return configs.map((c) => this.toViewDefinition(c));
  }

  /**
   * Clear the configuration cache
   * @param entityLogicalName - Optional entity to clear (clears all if not specified)
   */
  clearCache(entityLogicalName?: string): void {
    if (entityLogicalName) {
      this.cache.delete(entityLogicalName);
    } else {
      this.cache.clear();
    }
  }

  /**
   * Check if the sprk_gridconfiguration entity exists
   * @returns Promise resolving to true if entity exists
   */
  async checkEntityExists(): Promise<boolean> {
    if (this.entityExists !== null) {
      return this.entityExists;
    }

    try {
      // Try to fetch a single record to check if entity exists
      await this.xrm.WebApi.retrieveMultipleRecords(
        "sprk_gridconfiguration",
        "?$top=1&$select=sprk_gridconfigurationid"
      );
      this.entityExists = true;
      return true;
    } catch (error) {
      if (this.isEntityNotFoundError(error)) {
        this.entityExists = false;
        return false;
      }
      // Other error - assume entity exists but query failed
      return true;
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Private methods
  // ─────────────────────────────────────────────────────────────────────────────

  /**
   * Map Dataverse record to IGridConfiguration
   */
  private mapRecordToConfiguration(record: IGridConfigurationRecord): IGridConfiguration {
    let configJson: IGridConfigJson | undefined;

    if (record.sprk_configjson) {
      try {
        configJson = JSON.parse(record.sprk_configjson) as IGridConfigJson;
      } catch {
        console.warn("[ConfigurationService] Failed to parse configjson for", record.sprk_name);
      }
    }

    return {
      id: record.sprk_gridconfigurationid,
      name: record.sprk_name,
      entityLogicalName: record.sprk_entitylogicalname,
      viewType: record.sprk_viewtype as GridConfigViewType,
      savedViewId: record.sprk_savedviewid,
      fetchXml: record.sprk_fetchxml,
      layoutXml: record.sprk_layoutxml,
      configJson,
      isDefault: record.sprk_isdefault ?? false,
      sortOrder: record.sprk_sortorder ?? 100,
      iconName: record.sprk_iconname,
      description: record.sprk_description,
      stateCode: record.statecode,
    };
  }

  /**
   * Check if error indicates entity not found
   */
  private isEntityNotFoundError(error: unknown): boolean {
    if (error instanceof Error) {
      const message = error.message.toLowerCase();
      return (
        message.includes("entity") &&
        (message.includes("not found") || message.includes("doesn't exist"))
      );
    }
    return false;
  }
}

// Re-export types for convenience
export type {
  IGridConfiguration,
  IGridConfigJson,
  IColumnOverride,
  IDefaultFilter,
  IRowFormattingRule,
  IGridFeatures,
  GridConfigViewType,
} from "../types/ConfigurationTypes";
