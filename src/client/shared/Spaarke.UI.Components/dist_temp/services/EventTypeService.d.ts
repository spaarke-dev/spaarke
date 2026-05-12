/**
 * EventTypeService - Shared service for Event Type field configuration
 *
 * Provides context-agnostic field visibility and requirement logic that can be
 * consumed by both EventFormController (PCF) and EventDetailSidePane (Custom Page).
 *
 * This service does NOT reference any PCF-specific APIs - it only provides
 * configuration logic. The actual form manipulation is done by the consumers.
 *
 * @see ADR-012 - Shared Component Library (no PCF-specific dependencies)
 * @see EventTypeConfig.ts - Type definitions
 * @see EventFormController/handlers/FieldVisibilityHandler.ts - Consumer implementation
 */
import { IEventTypeFieldConfig, IFieldDefaultStates, IFieldDefaultState, IComputedFieldState, IComputedFieldStates, ISectionDefaults, RequiredLevel, IEventTypeServiceOptions } from '../types/EventTypeConfig';
import { IWebApiLike } from '../types/WebApiLike';
/**
 * Default field states for Event form
 * These are the baseline states when no Event Type is selected
 */
export declare const DEFAULT_EVENT_FIELD_STATES: IFieldDefaultStates;
/**
 * All controllable event fields
 */
export declare const ALL_EVENT_FIELDS: string[];
/**
 * Default section collapse states
 */
export declare const DEFAULT_SECTION_STATES: ISectionDefaults;
/**
 * All controllable section names
 */
export declare const ALL_SECTION_NAMES: readonly ["dates", "relatedEvent", "description", "history"];
/**
 * Type for section names
 */
export type SectionName = (typeof ALL_SECTION_NAMES)[number];
/**
 * EventTypeService - Core service for Event Type field configuration
 *
 * This service computes field visibility and requirement states based on
 * Event Type configuration. It does NOT interact with forms directly -
 * consumers are responsible for applying the computed states.
 *
 * @example
 * ```typescript
 * const service = new EventTypeService();
 *
 * // Parse configuration from Dataverse JSON
 * const config = service.parseFieldConfigJson(eventType.sprk_fieldconfigjson);
 *
 * // Compute field states
 * const computed = service.computeFieldStates(config);
 *
 * // Apply to form (consumer responsibility)
 * for (const [fieldName, state] of computed.fields) {
 *   formContext.getControl(fieldName)?.setVisible(state.isVisible);
 *   formContext.getAttribute(fieldName)?.setRequiredLevel(state.requiredLevel);
 * }
 * ```
 */
export declare class EventTypeService {
    private options;
    private cache;
    /**
     * Creates a new EventTypeService instance.
     *
     * @param options - Service configuration options
     */
    constructor(options?: IEventTypeServiceOptions);
    /**
     * Parse the sprk_fieldconfigjson field from an Event Type record.
     *
     * This method safely parses the JSON configuration and validates its structure.
     * Invalid JSON or malformed structures return null without throwing errors.
     *
     * ## Expected JSON Schema
     *
     * ```json
     * {
     *   "visibleFields": ["sprk_duedate", "sprk_priority"],
     *   "hiddenFields": ["sprk_completeddate"],
     *   "requiredFields": ["sprk_duedate"],
     *   "optionalFields": ["sprk_description"],
     *   "sectionDefaults": {
     *     "dates": "expanded" | "collapsed",
     *     "relatedEvent": "expanded" | "collapsed",
     *     "description": "expanded" | "collapsed",
     *     "history": "expanded" | "collapsed"
     *   }
     * }
     * ```
     *
     * ## Known Field Names (sprk_event entity)
     *
     * - `sprk_eventname` - Event Name (required by default)
     * - `sprk_description` - Description
     * - `sprk_basedate` - Base Date
     * - `sprk_duedate` - Due Date
     * - `sprk_completeddate` - Completed Date
     * - `scheduledstart` - Scheduled Start
     * - `scheduledend` - Scheduled End
     * - `sprk_location` - Location
     * - `sprk_remindat` - Remind At
     * - `statecode` - Status
     * - `statuscode` - Status Reason
     * - `sprk_priority` - Priority
     * - `sprk_source` - Source
     * - `sprk_relatedevent` - Related Event
     * - `sprk_relatedeventtype` - Related Event Type
     * - `sprk_relatedeventoffsettype` - Related Event Offset Type
     *
     * ## Priority Order
     *
     * When merging configuration (via `computeFieldStates`):
     * 1. `requiredFields` - highest priority, also makes field visible
     * 2. `visibleFields` - overrides hiddenFields
     * 3. `optionalFields` - removes requirement
     * 4. `hiddenFields` - lowest priority for visibility
     *
     * @param jsonString - Raw JSON string from Dataverse sprk_fieldconfigjson field
     * @returns Parsed configuration or null if input is invalid, empty, or malformed
     *
     * @example Valid configuration
     * ```typescript
     * const json = '{"requiredFields": ["sprk_duedate"], "sectionDefaults": {"dates": "expanded"}}';
     * const config = service.parseFieldConfigJson(json);
     * // Returns: { requiredFields: ["sprk_duedate"], sectionDefaults: { dates: "expanded" } }
     * ```
     *
     * @example Invalid JSON handling
     * ```typescript
     * const config = service.parseFieldConfigJson("{invalid json}");
     * // Returns: null (logs warning to console)
     * ```
     *
     * @example Empty input handling
     * ```typescript
     * const config = service.parseFieldConfigJson(null);
     * // Returns: null (no error logged)
     * ```
     */
    parseFieldConfigJson(jsonString: string | null | undefined): IEventTypeFieldConfig | null;
    /**
     * Parse section defaults from configuration.
     *
     * @param defaults - Raw section defaults object
     * @returns Validated section defaults
     */
    private parseSectionDefaults;
    /**
     * Compute field states from Event Type configuration.
     *
     * This merges the configuration with default field states to produce
     * the final visibility and requirement levels for each field.
     *
     * @param config - Event Type field configuration (null for defaults)
     * @param customDefaults - Optional custom default states (overrides built-in defaults)
     * @returns Computed field states for all fields
     */
    computeFieldStates(config: IEventTypeFieldConfig | null, customDefaults?: IFieldDefaultStates): IComputedFieldStates;
    /**
     * Get computed state for a single field.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @param customDefaults - Optional custom default states
     * @returns Computed state for the field, or null if field not in defaults
     */
    getFieldState(config: IEventTypeFieldConfig | null, fieldName: string, customDefaults?: IFieldDefaultStates): IComputedFieldState | null;
    /**
     * Check if a field should be visible given the configuration.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns True if field should be visible
     */
    isFieldVisible(config: IEventTypeFieldConfig | null, fieldName: string): boolean;
    /**
     * Check if a field is required given the configuration.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns True if field is required
     */
    isFieldRequired(config: IEventTypeFieldConfig | null, fieldName: string): boolean;
    /**
     * Get the required level for a field.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns Required level ("required", "recommended", "none")
     */
    getFieldRequiredLevel(config: IEventTypeFieldConfig | null, fieldName: string): RequiredLevel;
    /**
     * Get the default state for a field.
     *
     * @param fieldName - Field schema name
     * @returns Default state or null if field not in defaults
     */
    getDefaultFieldState(fieldName: string): IFieldDefaultState | null;
    /**
     * Get all default field states.
     *
     * @returns Copy of default field states
     */
    getDefaultFieldStates(): IFieldDefaultStates;
    /**
     * Get all field names.
     *
     * @returns Array of all controllable field names
     */
    getAllFieldNames(): string[];
    /**
     * Get default section collapse states.
     *
     * @returns Copy of default section states
     */
    getDefaultSectionStates(): ISectionDefaults;
    /**
     * Validate a field configuration.
     *
     * @param config - Configuration to validate
     * @returns Validation result with any issues found
     */
    validateConfig(config: IEventTypeFieldConfig): {
        isValid: boolean;
        warnings: string[];
        errors: string[];
    };
    /**
     * Get cached configuration for an Event Type ID.
     *
     * @param eventTypeId - Event Type record ID
     * @returns Cached configuration or null if not cached/expired
     */
    getCachedConfig(eventTypeId: string): IEventTypeFieldConfig | null;
    /**
     * Cache a configuration for an Event Type ID.
     *
     * @param eventTypeId - Event Type record ID
     * @param config - Configuration to cache
     */
    setCachedConfig(eventTypeId: string, config: IEventTypeFieldConfig): void;
    /**
     * Clear all cached configurations.
     */
    clearCache(): void;
    /**
     * Clear cached configuration for a specific Event Type.
     *
     * @param eventTypeId - Event Type record ID
     */
    clearCacheForEventType(eventTypeId: string): void;
}
/**
 * Result of getEventTypeFieldConfig operation
 */
export interface IGetEventTypeFieldConfigResult {
    /** Whether the operation was successful */
    success: boolean;
    /** The parsed field configuration (null if not found or invalid) */
    config: IEventTypeFieldConfig | null;
    /** Event Type name (for display/logging) */
    eventTypeName?: string;
    /** Event Type ID that was queried */
    eventTypeId: string;
    /** Error message if operation failed */
    error?: string;
    /** Whether the record was not found (vs. other error) */
    notFound?: boolean;
}
/**
 * Fetches Event Type field configuration from Dataverse.
 *
 * This is the main interface function for retrieving field configuration
 * for a specific Event Type. It accepts a generic WebApi interface,
 * allowing it to work with both PCF controls and Custom Pages.
 *
 * **Context-Agnostic Design (ADR-012)**:
 * - Accepts IWebApiLike interface, not PCF-specific ComponentFramework.WebApi
 * - Can be used with PCF context.webAPI, Xrm.WebApi, or mock implementations
 * - No direct coupling to Dataverse SDK or specific platform APIs
 *
 * @param webApi - WebAPI-like interface for Dataverse operations
 * @param eventTypeId - GUID of the Event Type record to fetch
 * @param options - Optional configuration (currently supports caching via service)
 * @returns Promise resolving to result with field configuration or error
 *
 * @example PCF Control usage:
 * ```typescript
 * // In a PCF control's updateView or init method
 * const result = await getEventTypeFieldConfig(context.webAPI, eventTypeId);
 * if (result.success && result.config) {
 *   const states = eventTypeService.computeFieldStates(result.config);
 *   // Apply states to form...
 * }
 * ```
 *
 * @example Custom Page usage:
 * ```typescript
 * // In a Custom Page component
 * import { createWebApiFromXrm } from "@spaarke/ui-components";
 *
 * const webApi = createWebApiFromXrm(Xrm.WebApi);
 * const result = await getEventTypeFieldConfig(webApi, eventTypeId);
 * if (result.success && result.config) {
 *   setFieldConfig(result.config);
 * }
 * ```
 *
 * @example Error handling:
 * ```typescript
 * const result = await getEventTypeFieldConfig(webApi, eventTypeId);
 * if (!result.success) {
 *   if (result.notFound) {
 *     console.warn(`Event Type ${eventTypeId} not found`);
 *     // Use default configuration
 *   } else {
 *     console.error(`Failed to fetch config: ${result.error}`);
 *   }
 * }
 * ```
 */
export declare function getEventTypeFieldConfig(webApi: IWebApiLike, eventTypeId: string, options?: {
    service?: EventTypeService;
}): Promise<IGetEventTypeFieldConfigResult>;
/**
 * Create a singleton instance for shared use.
 * Consumers can import this directly for simple cases.
 *
 * @example
 * ```typescript
 * import { eventTypeService } from "@spaarke/ui-components";
 *
 * const config = eventTypeService.parseFieldConfigJson(jsonString);
 * const states = eventTypeService.computeFieldStates(config);
 * ```
 */
export declare const eventTypeService: EventTypeService;
//# sourceMappingURL=EventTypeService.d.ts.map