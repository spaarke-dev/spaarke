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

import {
    FieldMappingService,
    IMappingResult,
    IFieldMappingProfile,
    SyncMode
} from "@spaarke/ui-components";

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
                result.errors = mappingResult.errors.map(e => e.message);
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
                attr.setValue(value);
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
