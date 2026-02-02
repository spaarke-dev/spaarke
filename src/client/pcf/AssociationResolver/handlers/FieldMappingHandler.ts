/**
 * FieldMappingHandler - Integrates AssociationResolver with FieldMappingService
 *
 * After a regarding record is selected, this handler:
 * 1. Queries for an active field mapping profile (source entity -> sprk_event)
 * 2. If profile exists, fetches source record values
 * 3. Applies mappings to the Event form fields
 * 4. Returns mapping results for UI feedback
 *
 * ADR Compliance:
 * - ADR-012: Uses FieldMappingService from @spaarke/ui-components
 * - ADR-006: Logic in PCF control code, not Business Rules
 *
 * @see spec.md - Field Mapping Framework section
 */

// Local type definitions (inlined from @spaarke/ui-components to avoid build dependency issues)
export enum SyncMode {
    OneTime = 0,
    ManualRefresh = 1,
}

export interface IFieldMappingProfile {
    id: string;
    name: string;
    sourceEntity: string;
    targetEntity: string;
    syncMode: SyncMode;
    isActive: boolean;
    rules: IFieldMappingRule[];
}

export interface IFieldMappingRule {
    id: string;
    sourceField: string;
    targetField: string;
    sourceFieldType: number;
    targetFieldType: number;
    executionOrder: number;
}

export interface IMappingResult {
    success: boolean;
    appliedRules: number;
    skippedRules: number;
    errors: Array<{ message: string }>;
    mappedValues: Record<string, unknown>;
}

export interface IFieldMappingServiceConfig {
    webApi: ComponentFramework.WebApi;
    enableCache?: boolean;
}

/**
 * FieldMappingService - Queries Dataverse for mapping profiles and applies mappings
 *
 * This is a local implementation that queries sprk_fieldmappingprofile directly
 * via Dataverse WebAPI, avoiding the React 18 dependency in @spaarke/ui-components.
 *
 * When the React migration project fixes @spaarke/ui-components for React 16,
 * this can be replaced with an import from the shared library.
 *
 * @see DEPLOYMENT-ISSUES.md for context on why this local implementation exists
 */
class FieldMappingService {
    private webApi: ComponentFramework.WebApi;
    private enableCache: boolean;
    private profileCache: Map<string, { profile: IFieldMappingProfile | null; timestamp: number }> = new Map();
    private readonly CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

    constructor(config: IFieldMappingServiceConfig) {
        this.webApi = config.webApi;
        this.enableCache = config.enableCache ?? false;
    }

    /**
     * Get field mapping profile for an entity pair
     * Queries sprk_fieldmappingprofile from Dataverse
     */
    async getProfileForEntityPair(sourceEntity: string, targetEntity: string): Promise<IFieldMappingProfile | null> {
        const cacheKey = `${sourceEntity}:${targetEntity}`;
        console.log(`[FieldMappingService] Querying profile for ${sourceEntity} -> ${targetEntity}`);

        // Check cache if enabled
        if (this.enableCache) {
            const cached = this.profileCache.get(cacheKey);
            if (cached && (Date.now() - cached.timestamp) < this.CACHE_TTL_MS) {
                console.log(`[FieldMappingService] Returning cached profile`);
                return cached.profile;
            }
        }

        try {
            // Query for active profile matching source and target entities
            const query = `?$filter=sprk_sourceentity eq '${sourceEntity}' and sprk_targetentity eq '${targetEntity}' and statecode eq 0&$select=sprk_fieldmappingprofileid,sprk_name,sprk_sourceentity,sprk_targetentity,sprk_syncmode,statecode`;

            const result = await this.webApi.retrieveMultipleRecords("sprk_fieldmappingprofile", query);

            if (!result.entities || result.entities.length === 0) {
                console.log(`[FieldMappingService] No profile found for ${sourceEntity} -> ${targetEntity}`);
                if (this.enableCache) {
                    this.profileCache.set(cacheKey, { profile: null, timestamp: Date.now() });
                }
                return null;
            }

            const profileEntity = result.entities[0];
            const profileId = profileEntity.sprk_fieldmappingprofileid;

            // Fetch rules for this profile
            const rulesQuery = `?$filter=_sprk_fieldmappingprofileid_value eq '${profileId}' and statecode eq 0&$select=sprk_fieldmappingruleid,sprk_sourcefield,sprk_targetfield,sprk_sourcefieldtype,sprk_targetfieldtype,sprk_executionorder&$orderby=sprk_executionorder asc`;

            const rulesResult = await this.webApi.retrieveMultipleRecords("sprk_fieldmappingrule", rulesQuery);

            const rules: IFieldMappingRule[] = (rulesResult.entities || []).map((rule: Record<string, unknown>) => ({
                id: rule.sprk_fieldmappingruleid as string,
                sourceField: rule.sprk_sourcefield as string,
                targetField: rule.sprk_targetfield as string,
                sourceFieldType: rule.sprk_sourcefieldtype as number,
                targetFieldType: rule.sprk_targetfieldtype as number,
                executionOrder: rule.sprk_executionorder as number || 0
            }));

            const profile: IFieldMappingProfile = {
                id: profileId,
                name: profileEntity.sprk_name as string,
                sourceEntity: profileEntity.sprk_sourceentity as string,
                targetEntity: profileEntity.sprk_targetentity as string,
                syncMode: profileEntity.sprk_syncmode as SyncMode || SyncMode.OneTime,
                isActive: profileEntity.statecode === 0,
                rules
            };

            console.log(`[FieldMappingService] Found profile: ${profile.name} with ${rules.length} rules`);

            if (this.enableCache) {
                this.profileCache.set(cacheKey, { profile, timestamp: Date.now() });
            }

            return profile;

        } catch (error) {
            console.error(`[FieldMappingService] Error querying profile:`, error);
            return null;
        }
    }

    /**
     * Apply field mappings from source record to target record
     * Fetches source record values and maps them according to profile rules
     */
    async applyMappings(
        sourceRecordId: string,
        targetRecord: Record<string, unknown>,
        profile: IFieldMappingProfile
    ): Promise<IMappingResult> {
        console.log(`[FieldMappingService] Applying mappings from profile ${profile.name}`);

        const result: IMappingResult = {
            success: true,
            appliedRules: 0,
            skippedRules: 0,
            errors: [],
            mappedValues: {}
        };

        if (!profile.rules || profile.rules.length === 0) {
            console.log(`[FieldMappingService] No rules in profile`);
            return result;
        }

        try {
            // Build select string from source fields in rules
            const sourceFields = profile.rules.map(r => r.sourceField).join(',');

            // Fetch source record with required fields
            const sourceRecord = await this.webApi.retrieveRecord(
                profile.sourceEntity,
                sourceRecordId,
                `?$select=${sourceFields}`
            );

            // Apply each rule
            for (const rule of profile.rules) {
                try {
                    const sourceValue = sourceRecord[rule.sourceField];

                    if (sourceValue === undefined) {
                        console.log(`[FieldMappingService] Source field ${rule.sourceField} not found, skipping`);
                        result.skippedRules++;
                        continue;
                    }

                    // Map the value (type conversion could be added here for complex cases)
                    targetRecord[rule.targetField] = sourceValue;
                    result.mappedValues[rule.targetField] = sourceValue;
                    result.appliedRules++;

                    console.log(`[FieldMappingService] Mapped ${rule.sourceField} -> ${rule.targetField}`);

                } catch (ruleError) {
                    console.error(`[FieldMappingService] Error applying rule ${rule.sourceField} -> ${rule.targetField}:`, ruleError);
                    result.errors.push({
                        message: `Failed to apply mapping: ${rule.sourceField} -> ${rule.targetField}`
                    });
                    result.skippedRules++;
                }
            }

            console.log(`[FieldMappingService] Applied ${result.appliedRules} rules, skipped ${result.skippedRules}`);
            return result;

        } catch (error) {
            console.error(`[FieldMappingService] Error fetching source record:`, error);
            result.success = false;
            result.errors.push({
                message: error instanceof Error ? error.message : "Failed to fetch source record"
            });
            return result;
        }
    }
}

/**
 * Configuration for FieldMappingHandler
 */
export interface IFieldMappingHandlerConfig {
    /** WebAPI instance from PCF context */
    webApi: ComponentFramework.WebApi;
    /** Target entity logical name (default: "sprk_event") */
    targetEntity?: string;
    /** Enable caching for profiles (default: false) */
    enableCache?: boolean;
}

/**
 * Result of applying field mappings after record selection
 */
export interface IFieldMappingApplicationResult {
    /** Whether mappings were applied successfully */
    success: boolean;
    /** Whether a profile was found for the entity pair */
    profileFound: boolean;
    /** Number of fields that were mapped */
    fieldsMapped: number;
    /** Number of rules that were skipped */
    rulesSkipped: number;
    /** Source entity display name (for UI messages) */
    sourceEntityName: string;
    /** Errors encountered during mapping */
    errors: string[];
    /** The profile that was applied (if any) */
    profile?: IFieldMappingProfile;
    /** Detailed mapping result */
    mappingResult?: IMappingResult;
}

/**
 * FieldMappingHandler - Manages field mapping operations for AssociationResolver
 *
 * @example
 * ```typescript
 * const handler = new FieldMappingHandler({ webApi: context.webAPI });
 * const result = await handler.applyMappingsForSelection("sprk_matter", recordId, {});
 * if (result.fieldsMapped > 0) {
 *   console.log(`Applied ${result.fieldsMapped} field mappings from ${result.sourceEntityName}`);
 * }
 * ```
 */
export class FieldMappingHandler {
    private mappingService: FieldMappingService;
    private targetEntity: string;

    /**
     * Creates a new FieldMappingHandler instance
     *
     * @param config - Configuration including webApi
     */
    constructor(config: IFieldMappingHandlerConfig) {
        this.mappingService = new FieldMappingService({
            webApi: config.webApi,
            enableCache: config.enableCache ?? false
        });
        this.targetEntity = config.targetEntity ?? "sprk_event";
    }

    /**
     * Apply field mappings after a record is selected.
     *
     * This method:
     * 1. Queries for an active profile matching sourceEntity -> targetEntity
     * 2. If found, applies the mappings to the target record
     * 3. Returns results for UI feedback
     *
     * @param sourceEntity - Source entity logical name (e.g., "sprk_matter")
     * @param sourceRecordId - GUID of the selected source record
     * @param targetRecord - Target record object to populate (mutated in place)
     * @returns Application result with success status and counts
     */
    async applyMappingsForSelection(
        sourceEntity: string,
        sourceRecordId: string,
        targetRecord: Record<string, unknown>
    ): Promise<IFieldMappingApplicationResult> {
        const result: IFieldMappingApplicationResult = {
            success: true,
            profileFound: false,
            fieldsMapped: 0,
            rulesSkipped: 0,
            sourceEntityName: sourceEntity,
            errors: []
        };

        console.log(`[FieldMappingHandler] Checking for profile: ${sourceEntity} -> ${this.targetEntity}`);

        try {
            // Step 1: Get profile for entity pair
            const profile = await this.mappingService.getProfileForEntityPair(
                sourceEntity,
                this.targetEntity
            );

            if (!profile) {
                console.log(`[FieldMappingHandler] No active profile found for ${sourceEntity} -> ${this.targetEntity}`);
                return result;
            }

            result.profileFound = true;
            result.profile = profile;

            console.log(`[FieldMappingHandler] Found profile: ${profile.name} (${profile.id})`);

            // Step 2: Check sync mode - only apply for one-time or manual refresh
            if (profile.syncMode !== SyncMode.OneTime && profile.syncMode !== SyncMode.ManualRefresh) {
                console.log(`[FieldMappingHandler] Profile sync mode (${profile.syncMode}) does not support auto-apply`);
                return result;
            }

            // Step 3: Apply mappings
            const mappingResult = await this.mappingService.applyMappings(
                sourceRecordId,
                targetRecord,
                profile
            );

            result.mappingResult = mappingResult;
            result.fieldsMapped = mappingResult.appliedRules;
            result.rulesSkipped = mappingResult.skippedRules;
            result.success = mappingResult.success;

            // Collect error messages
            if (mappingResult.errors.length > 0) {
                result.errors = mappingResult.errors.map((e: { message: string }) => e.message);
            }

            console.log(
                `[FieldMappingHandler] Applied mappings: ${result.fieldsMapped} applied, ` +
                `${result.rulesSkipped} skipped, ${result.errors.length} errors`
            );

            return result;

        } catch (error) {
            console.error("[FieldMappingHandler] Error applying mappings:", error);
            result.success = false;
            result.errors.push(error instanceof Error ? error.message : "Unknown error applying field mappings");
            return result;
        }
    }

    /**
     * Apply field mappings to form fields using Xrm.Page.
     *
     * This method takes the mapped values and sets them on the form.
     * It respects user-modified fields by checking dirty state.
     *
     * @param mappedValues - Record of field names to values from applyMappings
     * @param skipDirtyFields - Skip fields that user has already modified (default: true)
     * @returns Count of fields actually set on the form
     */
    applyToForm(
        mappedValues: Record<string, unknown>,
        skipDirtyFields: boolean = true
    ): number {
        const xrmPage = this.getXrmPage();
        if (!xrmPage) {
            console.warn("[FieldMappingHandler] Xrm.Page not available - cannot apply to form");
            return 0;
        }

        let appliedCount = 0;
        const fields = Object.keys(mappedValues);

        for (const fieldName of fields) {
            try {
                const attr = xrmPage.getAttribute(fieldName);
                if (!attr) {
                    console.warn(`[FieldMappingHandler] Field ${fieldName} not found on form`);
                    continue;
                }

                // Skip user-modified fields if requested
                if (skipDirtyFields && attr.getIsDirty()) {
                    console.log(`[FieldMappingHandler] Skipping dirty field: ${fieldName}`);
                    continue;
                }

                const value = mappedValues[fieldName];
                // eslint-disable-next-line @typescript-eslint/no-explicit-any
                attr.setValue(value as any);
                appliedCount++;
                console.log(`[FieldMappingHandler] Set ${fieldName} to:`, value);

            } catch (error) {
                console.error(`[FieldMappingHandler] Error setting field ${fieldName}:`, error);
            }
        }

        return appliedCount;
    }

    /**
     * Check if a field mapping profile exists for an entity pair.
     * Useful for determining whether to show "Refresh from Parent" button.
     *
     * @param sourceEntity - Source entity logical name
     * @returns True if an active profile exists
     */
    async hasProfileForEntity(sourceEntity: string): Promise<boolean> {
        try {
            const profile = await this.mappingService.getProfileForEntityPair(
                sourceEntity,
                this.targetEntity
            );
            return profile !== null && profile.isActive;
        } catch (error) {
            console.error("[FieldMappingHandler] Error checking profile:", error);
            return false;
        }
    }

    /**
     * Check if the profile supports manual refresh.
     *
     * @param sourceEntity - Source entity logical name
     * @returns True if profile exists and supports manual refresh
     */
    async supportsManualRefresh(sourceEntity: string): Promise<boolean> {
        try {
            const profile = await this.mappingService.getProfileForEntityPair(
                sourceEntity,
                this.targetEntity
            );
            return profile !== null &&
                   profile.isActive &&
                   profile.syncMode === SyncMode.ManualRefresh;
        } catch (error) {
            console.error("[FieldMappingHandler] Error checking refresh support:", error);
            return false;
        }
    }

    /**
     * Get Xrm.Page from parent window (PCF runs in iframe)
     */
    private getXrmPage(): Xrm.Page | null {
        try {
            const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
            return xrm?.Page || null;
        } catch (error) {
            console.warn("[FieldMappingHandler] Unable to access Xrm.Page:", error);
            return null;
        }
    }
}

/**
 * Create a field mapping handler for the AssociationResolver context
 *
 * @param webApi - WebAPI instance from PCF context
 * @returns Configured FieldMappingHandler instance
 */
export function createFieldMappingHandler(
    webApi: ComponentFramework.WebApi
): FieldMappingHandler {
    return new FieldMappingHandler({
        webApi,
        targetEntity: "sprk_event",
        enableCache: false // Disable cache for real-time profile updates
    });
}
