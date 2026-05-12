/**
 * useEntityTypeConfig - Generic hook for reading configuration JSON from entity type records
 *
 * Provides a reusable pattern for reading a JSON configuration field from any
 * Dataverse "type" entity (sprk_eventtype, sprk_mattertype, sprk_projecttype, etc.)
 * and computing field/section visibility states.
 *
 * This is the generic foundation that entity-specific hooks (useEventTypeConfig,
 * useMatterTypeConfig, etc.) build upon.
 *
 * @see ADR-012 - Shared Component Library
 * @see Task 107 - Extract useEntityTypeConfig generic hook
 *
 * @example
 * ```tsx
 * // Event-specific usage
 * const config = useEntityTypeConfig<IEventTypeFieldConfig>({
 *   entityName: "sprk_eventtype",
 *   recordId: eventTypeId,
 *   configFieldName: "sprk_fieldconfigjson",
 *   selectFields: "sprk_eventtypeid,sprk_name,sprk_fieldconfigjson",
 * });
 *
 * // Matter-specific usage (future)
 * const config = useEntityTypeConfig<IMatterTypeConfig>({
 *   entityName: "sprk_mattertype",
 *   recordId: matterTypeId,
 *   configFieldName: "sprk_sidepaneconfigjson",
 *   selectFields: "sprk_mattertypeid,sprk_name,sprk_sidepaneconfigjson",
 * });
 * ```
 */
/**
 * Options for the useEntityTypeConfig hook
 */
export interface UseEntityTypeConfigOptions {
    /** Dataverse entity logical name (e.g., "sprk_eventtype") */
    entityName: string;
    /** Record ID (GUID) to retrieve config from. If undefined, returns defaults. */
    recordId: string | undefined;
    /** Field name containing the JSON configuration (e.g., "sprk_fieldconfigjson") */
    configFieldName: string;
    /** OData $select fields for the retrieveRecord call */
    selectFields: string;
}
/**
 * Result of the useEntityTypeConfig hook
 */
export interface UseEntityTypeConfigResult<TConfig> {
    /** Whether the config is currently loading */
    isLoading: boolean;
    /** Error message if loading failed */
    error: string | null;
    /** Raw parsed configuration object (null if not loaded or no config) */
    config: TConfig | null;
    /** The entity type record name (if available) */
    typeName: string | null;
}
/**
 * Generic hook for reading a JSON configuration field from a Dataverse entity type record.
 *
 * @param options - Configuration for which entity/field to read
 * @returns Loading state, error, and parsed config
 */
export declare function useEntityTypeConfig<TConfig>(options: UseEntityTypeConfigOptions): UseEntityTypeConfigResult<TConfig>;
//# sourceMappingURL=useEntityTypeConfig.d.ts.map