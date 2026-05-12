/**
 * FieldMappingService - Core engine for the Field Mapping Framework
 *
 * This service provides field-to-field mapping functionality between parent and child
 * entities in Dataverse. It queries mapping configurations and applies them to records.
 *
 * Key features:
 * - Query active field mapping profiles from Dataverse
 * - Get mapping rules for a specific profile
 * - Apply mappings from source record to target record/form
 * - Support three sync modes: One-time, Manual Refresh, Update Related
 * - Cascading logic with TWO-PASS LIMIT to prevent infinite loops
 * - Type compatibility validation (Strict mode)
 *
 * @see spec.md - Field Mapping Framework section
 * @see FieldMappingTypes.ts for type definitions
 */
import { IFieldMappingProfile, IFieldMappingRule, IMappingResult, FieldType, CompatibilityMode, ITypeCompatibilityResult, IApplyMappingsOptions, SourceFieldValues, IGetProfilesOptions, IFieldMappingServiceConfig, IProfileValidationResult } from '../types/FieldMappingTypes';
/**
 * FieldMappingService - Core service for field mapping operations.
 *
 * This service is stateless by design - each method call queries Dataverse fresh
 * unless caching is explicitly enabled.
 *
 * @example
 * ```typescript
 * const service = new FieldMappingService({ webApi: context.webAPI });
 *
 * // Get profile for entity pair
 * const profile = await service.getProfileForEntityPair("sprk_matter", "sprk_event");
 *
 * // Apply mappings to form
 * if (profile) {
 *   const result = await service.applyMappings(sourceRecordId, targetRecord, profile);
 *   if (!result.success) {
 *     console.error("Mapping errors:", result.errors);
 *   }
 * }
 * ```
 */
export declare class FieldMappingService {
    private webApi;
    private cacheTtlMs;
    private enableCache;
    private profileCache;
    private ruleCache;
    /**
     * Creates a new FieldMappingService instance.
     *
     * @param config - Service configuration including WebAPI instance
     */
    constructor(config: IFieldMappingServiceConfig);
    /**
     * Get all active field mapping profiles from Dataverse.
     *
     * @param options - Query options (filters, include rules)
     * @returns Array of field mapping profiles
     */
    getProfiles(options?: IGetProfilesOptions): Promise<IFieldMappingProfile[]>;
    /**
     * Get the active profile for a specific source/target entity pair.
     *
     * @param sourceEntity - Source entity logical name (e.g., "sprk_matter")
     * @param targetEntity - Target entity logical name (e.g., "sprk_event")
     * @returns Matching profile or null if none found
     */
    getProfileForEntityPair(sourceEntity: string, targetEntity: string): Promise<IFieldMappingProfile | null>;
    /**
     * Get all profiles where the specified entity is the source.
     * Used for "Update Related" functionality on parent forms.
     *
     * @param sourceEntity - Source entity logical name
     * @returns Array of profiles where entity is the source
     */
    getProfilesForSource(sourceEntity: string): Promise<IFieldMappingProfile[]>;
    /**
     * Get all active rules for a specific profile.
     *
     * @param profileId - Profile record ID (GUID)
     * @returns Array of mapping rules sorted by execution order
     */
    getRulesForProfile(profileId: string): Promise<IFieldMappingRule[]>;
    /**
     * Fetch field values from a source record.
     *
     * @param sourceEntity - Source entity logical name
     * @param recordId - Source record ID (GUID)
     * @param fields - Array of field schema names to retrieve
     * @returns Record of field names to values
     */
    getSourceValues(sourceEntity: string, recordId: string, fields: string[]): Promise<SourceFieldValues>;
    /**
     * Apply field mappings from source record to target record.
     *
     * This method:
     * 1. Loads rules for the profile (if not already loaded)
     * 2. Fetches source record values for mapped fields
     * 3. Validates type compatibility
     * 4. Applies mappings to target record
     * 5. Executes cascading rules (second pass) if any fields trigger secondary mappings
     *
     * @param sourceRecordId - Source record ID (GUID)
     * @param targetRecord - Target record to update (mutable)
     * @param profile - Field mapping profile to apply
     * @param options - Apply options (dry-run, max passes, etc.)
     * @returns Mapping result with success status and any errors
     */
    applyMappings(sourceRecordId: string, targetRecord: Record<string, unknown>, profile: IFieldMappingProfile, options?: IApplyMappingsOptions): Promise<IMappingResult>;
    /**
     * Execute a single pass of mapping application.
     *
     * @param rules - Rules to apply
     * @param sourceValues - Source field values
     * @param targetRecord - Target record to update
     * @param profile - Parent profile
     * @param passNumber - Current pass number (1 or 2)
     * @param skipValidation - Whether to skip type validation
     * @param dryRun - Whether to skip actual updates
     * @returns Pass result
     */
    private executePass;
    /**
     * Get rules that should execute in a cascading pass.
     *
     * A rule is a cascading rule if:
     * 1. Its source field was mapped in the previous pass
     * 2. It is marked as a cascading source
     *
     * @param allRules - All rules in the profile
     * @param mappedFields - Fields that were mapped in the previous pass
     * @returns Rules to execute in the cascading pass
     */
    private getCascadingRules;
    /**
     * Validate type compatibility between source and target field types.
     *
     * @param sourceType - Source field type
     * @param targetType - Target field type
     * @param mode - Compatibility mode (Strict or Resolve)
     * @returns Validation result
     */
    validateTypeCompatibility(sourceType: FieldType, targetType: FieldType, mode?: CompatibilityMode): ITypeCompatibilityResult;
    /**
     * Validate a mapping rule's type compatibility (for configuration-time validation).
     *
     * @param rule - Rule to validate
     * @returns Validation result
     */
    validateMappingRule(rule: IFieldMappingRule): ITypeCompatibilityResult;
    /**
     * Check if a source type is compatible with a target type (simple boolean helper).
     *
     * @param sourceType - Source field type
     * @param targetType - Target field type
     * @returns True if compatible in Strict mode
     */
    isTypeCompatible(sourceType: FieldType, targetType: FieldType): boolean;
    /**
     * Get all compatible target types for a given source type.
     * Useful for UI dropdowns to show valid options.
     *
     * @param sourceType - Source field type
     * @returns Array of compatible target field types (includes the source type itself)
     */
    getCompatibleTargetTypes(sourceType: FieldType): FieldType[];
    /**
     * Validate an entire profile's rules for type compatibility.
     * Used for admin configuration validation before saving a profile.
     *
     * @param profile - Profile to validate
     * @param rules - Rules to validate (if not included in profile)
     * @returns Validation result with all incompatible mappings listed
     */
    validateProfile(profile: IFieldMappingProfile, rules?: IFieldMappingRule[]): IProfileValidationResult;
    /**
     * Convert a value from source type to target type.
     *
     * @param value - Value to convert
     * @param targetType - Target field type
     * @param sourceType - Optional source field type (for special handling)
     * @returns Converted value
     */
    private convertValue;
    /**
     * Convert any value to text representation.
     *
     * @param value - Value to convert
     * @param sourceType - Source field type (for formatting)
     * @returns Text representation
     */
    private convertToText;
    /**
     * Clear all cached data.
     */
    clearCache(): void;
    /**
     * Clear cache for a specific profile.
     *
     * @param profileId - Profile ID to clear
     */
    clearProfileCache(profileId?: string): void;
    /**
     * Get a cached entry if valid.
     */
    private getCachedEntry;
    /**
     * Set a cache entry.
     */
    private setCacheEntry;
    /**
     * Map a Dataverse profile entity to IFieldMappingProfile.
     * STUB: [API] - S010-05: Implement when entity exists (Task 001)
     */
    private mapProfileEntity;
    /**
     * Map a Dataverse rule entity to IFieldMappingRule.
     * STUB: [API] - S010-06: Implement when entity exists (Task 002)
     */
    private mapRuleEntity;
    /**
     * Load rules for multiple profiles.
     */
    private loadRulesForProfiles;
}
//# sourceMappingURL=FieldMappingService.d.ts.map