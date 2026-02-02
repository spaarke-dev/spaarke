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

import {
  IFieldMappingProfile,
  IFieldMappingRule,
  IMappingResult,
  IMappingError,
  MappingErrorCode,
  FieldType,
  CompatibilityMode,
  SyncMode,
  MappingDirection,
  ITypeCompatibilityResult,
  CompatibilityLevel,
  STRICT_TYPE_COMPATIBILITY,
  IApplyMappingsOptions,
  SourceFieldValues,
  IGetProfilesOptions,
  IFieldMappingServiceConfig,
  ICacheEntry,
  IProfileValidationResult,
  IRuleValidationResult,
} from "../types/FieldMappingTypes";

/**
 * Default cache TTL: 5 minutes
 */
const DEFAULT_CACHE_TTL_MS = 5 * 60 * 1000;

/**
 * Maximum passes for cascading mappings (prevents infinite loops)
 */
const MAX_CASCADING_PASSES = 2;

/**
 * Dataverse table names for field mapping entities
 */
const PROFILE_TABLE = "sprk_fieldmappingprofile";
const RULE_TABLE = "sprk_fieldmappingrule";

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
export class FieldMappingService {
  private webApi: ComponentFramework.WebApi;
  private cacheTtlMs: number;
  private enableCache: boolean;
  private profileCache: Map<string, ICacheEntry<IFieldMappingProfile[]>> = new Map();
  private ruleCache: Map<string, ICacheEntry<IFieldMappingRule[]>> = new Map();

  /**
   * Creates a new FieldMappingService instance.
   *
   * @param config - Service configuration including WebAPI instance
   */
  constructor(config: IFieldMappingServiceConfig) {
    this.webApi = config.webApi;
    this.cacheTtlMs = config.cacheTtlMs ?? DEFAULT_CACHE_TTL_MS;
    this.enableCache = config.enableCache ?? false;
  }

  // =========================================================================
  // Profile Query Methods
  // =========================================================================

  /**
   * Get all active field mapping profiles from Dataverse.
   *
   * @param options - Query options (filters, include rules)
   * @returns Array of field mapping profiles
   */
  async getProfiles(options: IGetProfilesOptions = {}): Promise<IFieldMappingProfile[]> {
    const { activeOnly = true, sourceEntity, targetEntity, includeRules = false } = options;

    // Build cache key
    const cacheKey = `profiles:${activeOnly}:${sourceEntity ?? "all"}:${targetEntity ?? "all"}`;

    // Check cache
    if (this.enableCache) {
      const cached = this.getCachedEntry(this.profileCache, cacheKey);
      if (cached) {
        return includeRules ? await this.loadRulesForProfiles(cached) : cached;
      }
    }

    // Build OData filter
    const filters: string[] = [];
    if (activeOnly) {
      filters.push("sprk_isactive eq true");
    }
    if (sourceEntity) {
      filters.push(`sprk_sourceentity eq '${sourceEntity}'`);
    }
    if (targetEntity) {
      filters.push(`sprk_targetentity eq '${targetEntity}'`);
    }

    const filterString = filters.length > 0 ? `$filter=${filters.join(" and ")}` : "";
    const selectString = "$select=sprk_fieldmappingprofileid,sprk_name,sprk_sourceentity,sprk_targetentity,sprk_mappingdirection,sprk_syncmode,sprk_isactive,sprk_description";
    const query = [filterString, selectString].filter(Boolean).join("&");

    // STUB: [API] - S010-01: Replace with actual Dataverse query when sprk_fieldmappingprofile entity exists (Task 001)
    // For now, return empty array - real implementation would be:
    // const result = await this.webApi.retrieveMultipleRecords(PROFILE_TABLE, `?${query}`);
    // const profiles = result.entities.map(this.mapProfileEntity);

    const profiles: IFieldMappingProfile[] = [];

    // Cache results
    if (this.enableCache) {
      this.setCacheEntry(this.profileCache, cacheKey, profiles);
    }

    return includeRules ? await this.loadRulesForProfiles(profiles) : profiles;
  }

  /**
   * Get the active profile for a specific source/target entity pair.
   *
   * @param sourceEntity - Source entity logical name (e.g., "sprk_matter")
   * @param targetEntity - Target entity logical name (e.g., "sprk_event")
   * @returns Matching profile or null if none found
   */
  async getProfileForEntityPair(
    sourceEntity: string,
    targetEntity: string
  ): Promise<IFieldMappingProfile | null> {
    const profiles = await this.getProfiles({
      activeOnly: true,
      sourceEntity,
      targetEntity,
      includeRules: true,
    });

    // Return first matching active profile
    return profiles.length > 0 ? profiles[0] : null;
  }

  /**
   * Get all profiles where the specified entity is the source.
   * Used for "Update Related" functionality on parent forms.
   *
   * @param sourceEntity - Source entity logical name
   * @returns Array of profiles where entity is the source
   */
  async getProfilesForSource(sourceEntity: string): Promise<IFieldMappingProfile[]> {
    return this.getProfiles({
      activeOnly: true,
      sourceEntity,
      includeRules: true,
    });
  }

  // =========================================================================
  // Rule Query Methods
  // =========================================================================

  /**
   * Get all active rules for a specific profile.
   *
   * @param profileId - Profile record ID (GUID)
   * @returns Array of mapping rules sorted by execution order
   */
  async getRulesForProfile(profileId: string): Promise<IFieldMappingRule[]> {
    // Check cache
    const cacheKey = `rules:${profileId}`;
    if (this.enableCache) {
      const cached = this.getCachedEntry(this.ruleCache, cacheKey);
      if (cached) {
        return cached;
      }
    }

    const filterString = `$filter=_sprk_fieldmappingprofile_value eq '${profileId}' and sprk_isactive eq true`;
    const selectString = "$select=sprk_fieldmappingruleid,sprk_name,sprk_sourcefield,sprk_sourcefieldtype,sprk_targetfield,sprk_targetfieldtype,sprk_compatibilitymode,sprk_isrequired,sprk_defaultvalue,sprk_iscascadingsource,sprk_executionorder,sprk_isactive";
    const orderString = "$orderby=sprk_executionorder asc";
    const query = `?${filterString}&${selectString}&${orderString}`;

    // STUB: [API] - S010-02: Replace with actual Dataverse query when sprk_fieldmappingrule entity exists (Task 002)
    // For now, return empty array - real implementation would be:
    // const result = await this.webApi.retrieveMultipleRecords(RULE_TABLE, query);
    // const rules = result.entities.map(entity => this.mapRuleEntity(entity, profileId));

    const rules: IFieldMappingRule[] = [];

    // Cache results
    if (this.enableCache) {
      this.setCacheEntry(this.ruleCache, cacheKey, rules);
    }

    return rules;
  }

  // =========================================================================
  // Source Value Retrieval
  // =========================================================================

  /**
   * Fetch field values from a source record.
   *
   * @param sourceEntity - Source entity logical name
   * @param recordId - Source record ID (GUID)
   * @param fields - Array of field schema names to retrieve
   * @returns Record of field names to values
   */
  async getSourceValues(
    sourceEntity: string,
    recordId: string,
    fields: string[]
  ): Promise<SourceFieldValues> {
    if (fields.length === 0) {
      return {};
    }

    const selectString = fields.join(",");

    // STUB: [API] - S010-03: Replace with actual Dataverse query (Task 010)
    // const record = await this.webApi.retrieveRecord(sourceEntity, recordId, `?$select=${selectString}`);
    // return record;

    // Return empty object - real implementation queries Dataverse
    return {};
  }

  // =========================================================================
  // Mapping Application
  // =========================================================================

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
  async applyMappings(
    sourceRecordId: string,
    targetRecord: Record<string, unknown>,
    profile: IFieldMappingProfile,
    options: IApplyMappingsOptions = {}
  ): Promise<IMappingResult> {
    const { skipValidation = false, maxPasses = MAX_CASCADING_PASSES, dryRun = false } = options;

    const result: IMappingResult = {
      success: true,
      appliedRules: 0,
      skippedRules: 0,
      totalRules: 0,
      fieldsMapped: [],
      errors: [],
      sourceRecordId,
      profileId: profile.id,
    };

    // Load rules if not already present
    const rules = profile.rules ?? await this.getRulesForProfile(profile.id);
    result.totalRules = rules.length;

    if (rules.length === 0) {
      return result;
    }

    // Get source field names
    const sourceFields = rules.map((r) => r.sourceField);

    // Fetch source values
    const sourceValues = await this.getSourceValues(
      profile.sourceEntity,
      sourceRecordId,
      sourceFields
    );

    // First pass: apply direct mappings
    const firstPassResult = await this.executePass(
      rules,
      sourceValues,
      targetRecord,
      profile,
      1,
      skipValidation,
      dryRun
    );

    result.appliedRules += firstPassResult.appliedRules;
    result.skippedRules += firstPassResult.skippedRules;
    result.fieldsMapped.push(...firstPassResult.fieldsMapped);
    result.errors.push(...firstPassResult.errors);
    result.pass = 1;

    // Check for cascading rules
    if (maxPasses > 1) {
      const cascadingRules = this.getCascadingRules(rules, firstPassResult.fieldsMapped);

      if (cascadingRules.length > 0) {
        // Second pass: apply cascading mappings using updated target values as source
        const cascadingSourceValues = { ...sourceValues };

        // Add mapped target values as potential sources for cascading
        for (const field of firstPassResult.fieldsMapped) {
          if (targetRecord[field] !== undefined) {
            cascadingSourceValues[field] = targetRecord[field];
          }
        }

        const secondPassResult = await this.executePass(
          cascadingRules,
          cascadingSourceValues,
          targetRecord,
          profile,
          2,
          skipValidation,
          dryRun
        );

        result.appliedRules += secondPassResult.appliedRules;
        result.skippedRules += secondPassResult.skippedRules;
        result.fieldsMapped.push(...secondPassResult.fieldsMapped);
        result.errors.push(...secondPassResult.errors);
        result.pass = 2;
      }
    }

    // Set overall success based on required field errors
    result.success = !result.errors.some(
      (e) => e.code === MappingErrorCode.RequiredFieldEmpty || e.code === MappingErrorCode.TypeMismatch
    );

    return result;
  }

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
  private async executePass(
    rules: IFieldMappingRule[],
    sourceValues: SourceFieldValues,
    targetRecord: Record<string, unknown>,
    profile: IFieldMappingProfile,
    passNumber: number,
    skipValidation: boolean,
    dryRun: boolean
  ): Promise<IMappingResult> {
    const result: IMappingResult = {
      success: true,
      appliedRules: 0,
      skippedRules: 0,
      totalRules: rules.length,
      fieldsMapped: [],
      errors: [],
      pass: passNumber,
    };

    for (const rule of rules) {
      // Skip inactive rules
      if (!rule.isActive) {
        result.skippedRules++;
        continue;
      }

      // Validate type compatibility (unless skipped)
      if (!skipValidation) {
        const compatibility = this.validateTypeCompatibility(
          rule.sourceFieldType,
          rule.targetFieldType,
          rule.compatibilityMode
        );

        if (!compatibility.isCompatible) {
          result.errors.push({
            ruleId: rule.id,
            sourceField: rule.sourceField,
            targetField: rule.targetField,
            message: compatibility.errors.join("; "),
            code: MappingErrorCode.TypeMismatch,
          });
          result.skippedRules++;
          continue;
        }
      }

      // Get source value
      const sourceValue = sourceValues[rule.sourceField];

      // Handle empty source
      if (sourceValue === undefined || sourceValue === null || sourceValue === "") {
        if (rule.isRequired && !rule.defaultValue) {
          result.errors.push({
            ruleId: rule.id,
            sourceField: rule.sourceField,
            targetField: rule.targetField,
            message: `Required field '${rule.sourceField}' is empty and no default value is configured`,
            code: MappingErrorCode.RequiredFieldEmpty,
          });
          result.success = false;
          continue;
        }

        // Use default value or skip optional field
        if (rule.defaultValue) {
          if (!dryRun) {
            targetRecord[rule.targetField] = this.convertValue(
              rule.defaultValue,
              rule.targetFieldType
            );
          }
          result.fieldsMapped.push(rule.targetField);
          result.appliedRules++;
        } else {
          result.skippedRules++;
        }
        continue;
      }

      // Convert and apply value
      const convertedValue = this.convertValue(sourceValue, rule.targetFieldType, rule.sourceFieldType);

      if (!dryRun) {
        targetRecord[rule.targetField] = convertedValue;
      }

      result.fieldsMapped.push(rule.targetField);
      result.appliedRules++;
    }

    return result;
  }

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
  private getCascadingRules(
    allRules: IFieldMappingRule[],
    mappedFields: string[]
  ): IFieldMappingRule[] {
    return allRules.filter((rule) => {
      // Check if this rule's source field was mapped in a previous pass
      return mappedFields.includes(rule.sourceField) && rule.isCascadingSource;
    });
  }

  // =========================================================================
  // Type Compatibility Validation
  // =========================================================================

  /**
   * Validate type compatibility between source and target field types.
   *
   * @param sourceType - Source field type
   * @param targetType - Target field type
   * @param mode - Compatibility mode (Strict or Resolve)
   * @returns Validation result
   */
  validateTypeCompatibility(
    sourceType: FieldType,
    targetType: FieldType,
    mode: CompatibilityMode = CompatibilityMode.Strict
  ): ITypeCompatibilityResult {
    const result: ITypeCompatibilityResult = {
      isCompatible: false,
      level: CompatibilityLevel.Incompatible,
      warnings: [],
      errors: [],
    };

    // Exact match is always compatible
    if (sourceType === targetType) {
      result.isCompatible = true;
      result.level = CompatibilityLevel.Exact;
      return result;
    }

    // Check strict compatibility matrix
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];

    if (compatibleTypes.includes(targetType)) {
      result.isCompatible = true;

      // Determine compatibility level
      if (sourceType === FieldType.Text && targetType === FieldType.Memo) {
        result.level = CompatibilityLevel.Exact; // Text to Memo is exact
      } else if (targetType === FieldType.Text) {
        result.level = CompatibilityLevel.SafeConversion; // Any to Text is safe conversion
        result.warnings.push(`Converting ${FieldType[sourceType]} to Text - value will be formatted`);
      } else {
        result.level = CompatibilityLevel.SafeConversion;
      }

      return result;
    }

    // Check if Resolve mode would help (future feature)
    if (mode === CompatibilityMode.Resolve) {
      // STUB: [FEATURE] - S010-04: Implement Resolve mode type resolution (future enhancement)
      result.level = CompatibilityLevel.RequiresResolve;
      result.warnings.push(`Type resolution from ${FieldType[sourceType]} to ${FieldType[targetType]} is not yet implemented`);
      return result;
    }

    // Incompatible in Strict mode
    result.errors.push(
      `Cannot convert ${FieldType[sourceType]} to ${FieldType[targetType]} in Strict mode. ` +
      `Compatible types for ${FieldType[sourceType]}: ${compatibleTypes.map((t) => FieldType[t]).join(", ") || "none"}`
    );

    return result;
  }

  /**
   * Validate a mapping rule's type compatibility (for configuration-time validation).
   *
   * @param rule - Rule to validate
   * @returns Validation result
   */
  validateMappingRule(rule: IFieldMappingRule): ITypeCompatibilityResult {
    return this.validateTypeCompatibility(
      rule.sourceFieldType,
      rule.targetFieldType,
      rule.compatibilityMode
    );
  }

  /**
   * Check if a source type is compatible with a target type (simple boolean helper).
   *
   * @param sourceType - Source field type
   * @param targetType - Target field type
   * @returns True if compatible in Strict mode
   */
  isTypeCompatible(sourceType: FieldType, targetType: FieldType): boolean {
    // Exact match is always compatible
    if (sourceType === targetType) {
      return true;
    }

    // Check strict compatibility matrix
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];
    return compatibleTypes.includes(targetType);
  }

  /**
   * Get all compatible target types for a given source type.
   * Useful for UI dropdowns to show valid options.
   *
   * @param sourceType - Source field type
   * @returns Array of compatible target field types (includes the source type itself)
   */
  getCompatibleTargetTypes(sourceType: FieldType): FieldType[] {
    // Get compatible types from matrix
    const compatibleTypes = STRICT_TYPE_COMPATIBILITY[sourceType] ?? [];

    // Include the source type itself if not already included (exact match)
    if (!compatibleTypes.includes(sourceType)) {
      return [sourceType, ...compatibleTypes];
    }

    return compatibleTypes;
  }

  /**
   * Validate an entire profile's rules for type compatibility.
   * Used for admin configuration validation before saving a profile.
   *
   * @param profile - Profile to validate
   * @param rules - Rules to validate (if not included in profile)
   * @returns Validation result with all incompatible mappings listed
   */
  validateProfile(
    profile: IFieldMappingProfile,
    rules?: IFieldMappingRule[]
  ): IProfileValidationResult {
    const rulesToValidate = rules ?? profile.rules ?? [];

    const result: IProfileValidationResult = {
      isValid: true,
      profileId: profile.id,
      profileName: profile.name,
      totalRules: rulesToValidate.length,
      validRules: 0,
      invalidRules: 0,
      ruleResults: [],
    };

    for (const rule of rulesToValidate) {
      const ruleValidation = this.validateMappingRule(rule);

      const ruleResult: IRuleValidationResult = {
        ruleId: rule.id,
        ruleName: rule.name,
        sourceField: rule.sourceField,
        targetField: rule.targetField,
        sourceFieldType: rule.sourceFieldType,
        targetFieldType: rule.targetFieldType,
        isCompatible: ruleValidation.isCompatible,
        level: ruleValidation.level,
        warnings: ruleValidation.warnings,
        errors: ruleValidation.errors,
      };

      result.ruleResults.push(ruleResult);

      if (ruleValidation.isCompatible) {
        result.validRules++;
      } else {
        result.invalidRules++;
        result.isValid = false;
      }
    }

    return result;
  }

  // =========================================================================
  // Value Conversion
  // =========================================================================

  /**
   * Convert a value from source type to target type.
   *
   * @param value - Value to convert
   * @param targetType - Target field type
   * @param sourceType - Optional source field type (for special handling)
   * @returns Converted value
   */
  private convertValue(
    value: unknown,
    targetType: FieldType,
    sourceType?: FieldType
  ): unknown {
    if (value === null || value === undefined) {
      return null;
    }

    // Handle conversion based on target type
    switch (targetType) {
      case FieldType.Text:
        return this.convertToText(value, sourceType);

      case FieldType.Memo:
        return this.convertToText(value, sourceType);

      case FieldType.Number:
        return typeof value === "number" ? value : parseFloat(String(value)) || null;

      case FieldType.Boolean:
        if (typeof value === "boolean") return value;
        if (typeof value === "string") {
          return value.toLowerCase() === "true" || value === "1" || value.toLowerCase() === "yes";
        }
        return Boolean(value);

      case FieldType.DateTime:
        if (value instanceof Date) return value.toISOString();
        if (typeof value === "string") return value; // Assume ISO string
        return null;

      case FieldType.Lookup:
        // Lookup values are typically passed as-is (GUID references)
        return value;

      case FieldType.OptionSet:
        // OptionSet values are typically integers
        if (typeof value === "number") return value;
        if (typeof value === "string") {
          const parsed = parseInt(value, 10);
          return isNaN(parsed) ? null : parsed;
        }
        return null;

      default:
        return value;
    }
  }

  /**
   * Convert any value to text representation.
   *
   * @param value - Value to convert
   * @param sourceType - Source field type (for formatting)
   * @returns Text representation
   */
  private convertToText(value: unknown, sourceType?: FieldType): string {
    if (value === null || value === undefined) {
      return "";
    }

    if (typeof value === "string") {
      return value;
    }

    if (typeof value === "number") {
      return value.toString();
    }

    if (typeof value === "boolean") {
      return value ? "Yes" : "No";
    }

    if (value instanceof Date) {
      return value.toISOString();
    }

    // For lookup values, try to get the display name
    if (typeof value === "object" && value !== null) {
      // Dataverse lookup formatted value pattern
      const obj = value as Record<string, unknown>;
      if ("name" in obj) {
        return String(obj.name);
      }
      // OData formatted value pattern
      const keys = Object.keys(obj);
      const formattedKey = keys.find((k) => k.endsWith("@OData.Community.Display.V1.FormattedValue"));
      if (formattedKey) {
        return String(obj[formattedKey]);
      }
    }

    return String(value);
  }

  // =========================================================================
  // Cache Management
  // =========================================================================

  /**
   * Clear all cached data.
   */
  clearCache(): void {
    this.profileCache.clear();
    this.ruleCache.clear();
  }

  /**
   * Clear cache for a specific profile.
   *
   * @param profileId - Profile ID to clear
   */
  clearProfileCache(profileId?: string): void {
    if (profileId) {
      this.ruleCache.delete(`rules:${profileId}`);
    }
    // Clear all profile caches (they may contain the profile)
    this.profileCache.clear();
  }

  /**
   * Get a cached entry if valid.
   */
  private getCachedEntry<T>(cache: Map<string, ICacheEntry<T>>, key: string): T | null {
    const entry = cache.get(key);
    if (!entry) return null;

    if (Date.now() > entry.expiresAt) {
      cache.delete(key);
      return null;
    }

    return entry.data;
  }

  /**
   * Set a cache entry.
   */
  private setCacheEntry<T>(cache: Map<string, ICacheEntry<T>>, key: string, data: T): void {
    const now = Date.now();
    cache.set(key, {
      data,
      cachedAt: now,
      expiresAt: now + this.cacheTtlMs,
    });
  }

  // =========================================================================
  // Entity Mapping Helpers
  // =========================================================================

  /**
   * Map a Dataverse profile entity to IFieldMappingProfile.
   * STUB: [API] - S010-05: Implement when entity exists (Task 001)
   */
  private mapProfileEntity(entity: Record<string, unknown>): IFieldMappingProfile {
    return {
      id: String(entity.sprk_fieldmappingprofileid ?? ""),
      name: String(entity.sprk_name ?? ""),
      sourceEntity: String(entity.sprk_sourceentity ?? ""),
      targetEntity: String(entity.sprk_targetentity ?? ""),
      mappingDirection: (entity.sprk_mappingdirection as MappingDirection) ?? MappingDirection.ParentToChild,
      syncMode: (entity.sprk_syncmode as SyncMode) ?? SyncMode.OneTime,
      isActive: Boolean(entity.sprk_isactive),
      description: entity.sprk_description as string | undefined,
    };
  }

  /**
   * Map a Dataverse rule entity to IFieldMappingRule.
   * STUB: [API] - S010-06: Implement when entity exists (Task 002)
   */
  private mapRuleEntity(entity: Record<string, unknown>, profileId: string): IFieldMappingRule {
    return {
      id: String(entity.sprk_fieldmappingruleid ?? ""),
      profileId,
      name: entity.sprk_name as string | undefined,
      sourceField: String(entity.sprk_sourcefield ?? ""),
      sourceFieldType: (entity.sprk_sourcefieldtype as FieldType) ?? FieldType.Text,
      targetField: String(entity.sprk_targetfield ?? ""),
      targetFieldType: (entity.sprk_targetfieldtype as FieldType) ?? FieldType.Text,
      compatibilityMode: (entity.sprk_compatibilitymode as CompatibilityMode) ?? CompatibilityMode.Strict,
      isRequired: Boolean(entity.sprk_isrequired),
      defaultValue: entity.sprk_defaultvalue as string | undefined,
      isCascadingSource: Boolean(entity.sprk_iscascadingsource),
      executionOrder: (entity.sprk_executionorder as number) ?? 0,
      isActive: Boolean(entity.sprk_isactive),
    };
  }

  /**
   * Load rules for multiple profiles.
   */
  private async loadRulesForProfiles(
    profiles: IFieldMappingProfile[]
  ): Promise<IFieldMappingProfile[]> {
    const results = await Promise.all(
      profiles.map(async (profile) => {
        const rules = await this.getRulesForProfile(profile.id);
        return { ...profile, rules };
      })
    );
    return results;
  }
}
