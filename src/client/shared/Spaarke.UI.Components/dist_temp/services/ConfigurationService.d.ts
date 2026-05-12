/**
 * ConfigurationService
 *
 * Reads grid configurations from the sprk_gridconfiguration Dataverse table.
 * Provides caching and conversion to IViewDefinition format.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 */
import type { XrmContext } from '../utils/xrmContext';
import type { IViewDefinition } from '../types/FetchXmlTypes';
import type { IGridConfiguration } from '../types/ConfigurationTypes';
/**
 * Service for reading grid configurations from sprk_gridconfiguration.
 * Integrates with ViewService to provide custom views.
 */
export declare class ConfigurationService {
    private xrm;
    private cache;
    private cacheTTL;
    private entityExists;
    /**
     * Create a new ConfigurationService instance
     * @param xrm - XrmContext providing WebApi access
     */
    constructor(xrm: XrmContext);
    /**
     * Get all configurations for an entity
     * @param entityLogicalName - Entity to get configurations for
     * @returns Promise resolving to array of grid configurations
     */
    getConfigurations(entityLogicalName: string): Promise<IGridConfiguration[]>;
    /**
     * Get the default configuration for an entity
     * @param entityLogicalName - Entity to get default for
     * @returns Promise resolving to default configuration or undefined
     */
    getDefaultConfiguration(entityLogicalName: string): Promise<IGridConfiguration | undefined>;
    /**
     * Get a specific configuration by ID
     * @param configurationId - Configuration ID (sprk_gridconfigurationid)
     * @returns Promise resolving to configuration or undefined
     */
    getConfigurationById(configurationId: string): Promise<IGridConfiguration | undefined>;
    /**
     * Convert a configuration to IViewDefinition format
     * Used by ViewService when merging custom views
     * @param config - Grid configuration to convert
     * @returns View definition
     */
    toViewDefinition(config: IGridConfiguration): IViewDefinition;
    /**
     * Convert multiple configurations to view definitions
     * @param configs - Configurations to convert
     * @returns Array of view definitions
     */
    toViewDefinitions(configs: IGridConfiguration[]): IViewDefinition[];
    /**
     * Clear the configuration cache
     * @param entityLogicalName - Optional entity to clear (clears all if not specified)
     */
    clearCache(entityLogicalName?: string): void;
    /**
     * Check if the sprk_gridconfiguration entity exists
     * @returns Promise resolving to true if entity exists
     */
    checkEntityExists(): Promise<boolean>;
    /**
     * Map Dataverse record to IGridConfiguration
     */
    private mapRecordToConfiguration;
    /**
     * Check if error indicates entity not found
     */
    private isEntityNotFoundError;
}
export type { IGridConfiguration, IGridConfigJson, IColumnOverride, IDefaultFilter, IRowFormattingRule, IGridFeatures, GridConfigViewType, } from '../types/ConfigurationTypes';
//# sourceMappingURL=ConfigurationService.d.ts.map