/**
 * EntityConfigurationService - Loads and merges entity configurations
 */
import { IResolvedConfiguration, ICustomCommandConfiguration } from '../types/EntityConfigurationTypes';
export declare class EntityConfigurationService {
    private static schema;
    /**
     * Load configuration from JSON string (from manifest parameter)
     */
    static loadConfiguration(configJson: string | null | undefined): void;
    /**
     * Get configuration for a specific entity
     */
    static getEntityConfiguration(entityLogicalName: string): IResolvedConfiguration;
    /**
     * Get custom command configuration by key
     */
    static getCustomCommand(entityLogicalName: string, commandKey: string): ICustomCommandConfiguration | undefined;
    /**
     * Check if configuration is loaded
     */
    static isConfigurationLoaded(): boolean;
    /**
     * Get default schema (fallback when no configuration provided)
     */
    private static getDefaultSchema;
    /**
     * Validate configuration schema (optional, for development)
     */
    static validateConfiguration(configJson: string): {
        valid: boolean;
        errors: string[];
    };
}
//# sourceMappingURL=EntityConfigurationService.d.ts.map