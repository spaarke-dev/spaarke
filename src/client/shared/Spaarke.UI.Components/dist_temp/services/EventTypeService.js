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
/**
 * Default cache TTL: 5 minutes
 */
const DEFAULT_CACHE_TTL_MS = 5 * 60 * 1000;
/**
 * Default field states for Event form
 * These are the baseline states when no Event Type is selected
 */
export const DEFAULT_EVENT_FIELD_STATES = {
    // Primary fields - always visible, event name is required
    sprk_eventname: { visible: true, requiredLevel: 'required' },
    sprk_description: { visible: true, requiredLevel: 'none' },
    // Date fields - visible but not required by default
    sprk_basedate: { visible: true, requiredLevel: 'none' },
    sprk_duedate: { visible: true, requiredLevel: 'none' },
    sprk_completeddate: { visible: true, requiredLevel: 'none' },
    scheduledstart: { visible: true, requiredLevel: 'none' },
    scheduledend: { visible: true, requiredLevel: 'none' },
    // Location - visible but optional
    sprk_location: { visible: true, requiredLevel: 'none' },
    // Reminder - visible but optional
    sprk_remindat: { visible: true, requiredLevel: 'none' },
    // Status fields - visible
    statecode: { visible: true, requiredLevel: 'none' },
    statuscode: { visible: true, requiredLevel: 'none' },
    sprk_priority: { visible: true, requiredLevel: 'none' },
    sprk_source: { visible: true, requiredLevel: 'none' },
    // Related event fields - visible but optional
    sprk_relatedevent: { visible: true, requiredLevel: 'none' },
    sprk_relatedeventtype: { visible: true, requiredLevel: 'none' },
    sprk_relatedeventoffsettype: { visible: true, requiredLevel: 'none' },
};
/**
 * All controllable event fields
 */
export const ALL_EVENT_FIELDS = Object.keys(DEFAULT_EVENT_FIELD_STATES);
/**
 * Default section collapse states
 */
export const DEFAULT_SECTION_STATES = {
    dates: 'expanded',
    relatedEvent: 'collapsed',
    description: 'expanded',
    history: 'collapsed',
};
/**
 * All controllable section names
 */
export const ALL_SECTION_NAMES = ['dates', 'relatedEvent', 'description', 'history'];
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
export class EventTypeService {
    /**
     * Creates a new EventTypeService instance.
     *
     * @param options - Service configuration options
     */
    constructor(options = {}) {
        this.cache = new Map();
        this.options = {
            enableCache: options.enableCache ?? false,
            cacheTtlMs: options.cacheTtlMs ?? DEFAULT_CACHE_TTL_MS,
        };
    }
    // =========================================================================
    // Configuration Parsing
    // =========================================================================
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
    parseFieldConfigJson(jsonString) {
        if (!jsonString || jsonString.trim() === '') {
            return null;
        }
        try {
            const parsed = JSON.parse(jsonString);
            // Validate basic structure
            if (typeof parsed !== 'object' || parsed === null) {
                console.warn('[EventTypeService] Invalid config JSON - not an object');
                return null;
            }
            // Return validated config
            const config = {};
            if (Array.isArray(parsed.visibleFields)) {
                config.visibleFields = parsed.visibleFields.filter((f) => typeof f === 'string');
            }
            if (Array.isArray(parsed.hiddenFields)) {
                config.hiddenFields = parsed.hiddenFields.filter((f) => typeof f === 'string');
            }
            if (Array.isArray(parsed.requiredFields)) {
                config.requiredFields = parsed.requiredFields.filter((f) => typeof f === 'string');
            }
            if (Array.isArray(parsed.optionalFields)) {
                config.optionalFields = parsed.optionalFields.filter((f) => typeof f === 'string');
            }
            if (Array.isArray(parsed.hiddenSections)) {
                // Validate section names against known sections
                const validSections = new Set(ALL_SECTION_NAMES);
                config.hiddenSections = parsed.hiddenSections.filter((s) => typeof s === 'string' && validSections.has(s));
            }
            if (parsed.sectionDefaults && typeof parsed.sectionDefaults === 'object') {
                config.sectionDefaults = this.parseSectionDefaults(parsed.sectionDefaults);
            }
            return config;
        }
        catch (error) {
            console.warn('[EventTypeService] Failed to parse config JSON:', error);
            return null;
        }
    }
    /**
     * Parse section defaults from configuration.
     *
     * @param defaults - Raw section defaults object
     * @returns Validated section defaults
     */
    parseSectionDefaults(defaults) {
        const result = {};
        const validStates = ['expanded', 'collapsed'];
        for (const key of ['dates', 'relatedEvent', 'description', 'history']) {
            const value = defaults[key];
            if (typeof value === 'string' && validStates.includes(value)) {
                result[key] = value;
            }
        }
        return result;
    }
    // =========================================================================
    // Field State Computation
    // =========================================================================
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
    computeFieldStates(config, customDefaults) {
        const defaults = customDefaults ?? DEFAULT_EVENT_FIELD_STATES;
        const fields = new Map();
        // Start with defaults
        for (const [fieldName, defaultState] of Object.entries(defaults)) {
            fields.set(fieldName, {
                fieldName,
                isVisible: defaultState.visible,
                requiredLevel: defaultState.requiredLevel,
                isOverridden: false,
            });
        }
        // Apply configuration if provided
        if (config) {
            // Apply hidden fields first (lowest priority for visibility)
            if (config.hiddenFields) {
                for (const fieldName of config.hiddenFields) {
                    const state = fields.get(fieldName);
                    if (state) {
                        state.isVisible = false;
                        state.requiredLevel = 'none'; // Hidden fields cannot be required
                        state.isOverridden = true;
                    }
                }
            }
            // Apply visible fields (overrides hidden if both specified)
            if (config.visibleFields) {
                for (const fieldName of config.visibleFields) {
                    const state = fields.get(fieldName);
                    if (state) {
                        state.isVisible = true;
                        state.isOverridden = true;
                    }
                }
            }
            // Apply optional fields
            if (config.optionalFields) {
                for (const fieldName of config.optionalFields) {
                    const state = fields.get(fieldName);
                    if (state) {
                        state.requiredLevel = 'none';
                        state.isOverridden = true;
                    }
                }
            }
            // Apply required fields (highest priority - also makes visible)
            if (config.requiredFields) {
                for (const fieldName of config.requiredFields) {
                    const state = fields.get(fieldName);
                    if (state) {
                        state.isVisible = true;
                        state.requiredLevel = 'required';
                        state.isOverridden = true;
                    }
                }
            }
        }
        // Compute section defaults (legacy)
        const sectionDefaults = {
            ...DEFAULT_SECTION_STATES,
            ...config?.sectionDefaults,
        };
        // Compute section states (visibility + collapse)
        const sections = new Map();
        const hiddenSectionsSet = new Set(config?.hiddenSections ?? []);
        for (const sectionName of ALL_SECTION_NAMES) {
            sections.set(sectionName, {
                sectionName,
                isVisible: !hiddenSectionsSet.has(sectionName),
                collapseState: sectionDefaults[sectionName] ?? 'expanded',
            });
        }
        return {
            fields,
            sections,
            sectionDefaults,
            sourceConfig: config,
        };
    }
    /**
     * Get computed state for a single field.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @param customDefaults - Optional custom default states
     * @returns Computed state for the field, or null if field not in defaults
     */
    getFieldState(config, fieldName, customDefaults) {
        const computed = this.computeFieldStates(config, customDefaults);
        return computed.fields.get(fieldName) ?? null;
    }
    /**
     * Check if a field should be visible given the configuration.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns True if field should be visible
     */
    isFieldVisible(config, fieldName) {
        const state = this.getFieldState(config, fieldName);
        return state?.isVisible ?? true; // Default to visible if unknown field
    }
    /**
     * Check if a field is required given the configuration.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns True if field is required
     */
    isFieldRequired(config, fieldName) {
        const state = this.getFieldState(config, fieldName);
        return state?.requiredLevel === 'required';
    }
    /**
     * Get the required level for a field.
     *
     * @param config - Event Type field configuration
     * @param fieldName - Field schema name
     * @returns Required level ("required", "recommended", "none")
     */
    getFieldRequiredLevel(config, fieldName) {
        const state = this.getFieldState(config, fieldName);
        return state?.requiredLevel ?? 'none';
    }
    // =========================================================================
    // Default State Access
    // =========================================================================
    /**
     * Get the default state for a field.
     *
     * @param fieldName - Field schema name
     * @returns Default state or null if field not in defaults
     */
    getDefaultFieldState(fieldName) {
        return DEFAULT_EVENT_FIELD_STATES[fieldName] ?? null;
    }
    /**
     * Get all default field states.
     *
     * @returns Copy of default field states
     */
    getDefaultFieldStates() {
        return { ...DEFAULT_EVENT_FIELD_STATES };
    }
    /**
     * Get all field names.
     *
     * @returns Array of all controllable field names
     */
    getAllFieldNames() {
        return [...ALL_EVENT_FIELDS];
    }
    /**
     * Get default section collapse states.
     *
     * @returns Copy of default section states
     */
    getDefaultSectionStates() {
        return { ...DEFAULT_SECTION_STATES };
    }
    // =========================================================================
    // Validation
    // =========================================================================
    /**
     * Validate a field configuration.
     *
     * @param config - Configuration to validate
     * @returns Validation result with any issues found
     */
    validateConfig(config) {
        const warnings = [];
        const errors = [];
        // Check for unknown fields
        const allKnownFields = new Set(ALL_EVENT_FIELDS);
        const checkFields = (fieldNames, listName) => {
            if (!fieldNames)
                return;
            for (const field of fieldNames) {
                if (!allKnownFields.has(field)) {
                    warnings.push(`Unknown field '${field}' in ${listName}`);
                }
            }
        };
        checkFields(config.visibleFields, 'visibleFields');
        checkFields(config.hiddenFields, 'hiddenFields');
        checkFields(config.requiredFields, 'requiredFields');
        checkFields(config.optionalFields, 'optionalFields');
        // Check for conflicts
        if (config.hiddenFields && config.requiredFields) {
            const hiddenSet = new Set(config.hiddenFields);
            for (const field of config.requiredFields) {
                if (hiddenSet.has(field)) {
                    errors.push(`Field '${field}' is in both hiddenFields and requiredFields (required takes precedence)`);
                }
            }
        }
        return {
            isValid: errors.length === 0,
            warnings,
            errors,
        };
    }
    // =========================================================================
    // Cache Management
    // =========================================================================
    /**
     * Get cached configuration for an Event Type ID.
     *
     * @param eventTypeId - Event Type record ID
     * @returns Cached configuration or null if not cached/expired
     */
    getCachedConfig(eventTypeId) {
        if (!this.options.enableCache) {
            return null;
        }
        const entry = this.cache.get(eventTypeId);
        if (!entry) {
            return null;
        }
        if (Date.now() > entry.expiresAt) {
            this.cache.delete(eventTypeId);
            return null;
        }
        return entry.config;
    }
    /**
     * Cache a configuration for an Event Type ID.
     *
     * @param eventTypeId - Event Type record ID
     * @param config - Configuration to cache
     */
    setCachedConfig(eventTypeId, config) {
        if (!this.options.enableCache) {
            return;
        }
        const now = Date.now();
        this.cache.set(eventTypeId, {
            config,
            cachedAt: now,
            expiresAt: now + this.options.cacheTtlMs,
        });
    }
    /**
     * Clear all cached configurations.
     */
    clearCache() {
        this.cache.clear();
    }
    /**
     * Clear cached configuration for a specific Event Type.
     *
     * @param eventTypeId - Event Type record ID
     */
    clearCacheForEventType(eventTypeId) {
        this.cache.delete(eventTypeId);
    }
}
// =============================================================================
// Standalone Functions (Context-Agnostic API)
// =============================================================================
/**
 * Entity name for Event Type in Dataverse
 */
const EVENT_TYPE_ENTITY = 'sprk_eventtype';
/**
 * Fields to retrieve from Event Type record
 */
const EVENT_TYPE_SELECT_FIELDS = 'sprk_eventtypeid,sprk_name,sprk_fieldconfigjson';
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
export async function getEventTypeFieldConfig(webApi, eventTypeId, options) {
    // Validate input
    if (!eventTypeId || eventTypeId.trim() === '') {
        return {
            success: false,
            config: null,
            eventTypeId: eventTypeId ?? '',
            error: 'Event Type ID is required',
            notFound: false,
        };
    }
    // Normalize GUID (remove braces if present)
    const normalizedId = eventTypeId.replace(/[{}]/g, '').toLowerCase();
    // Get service instance (use provided or singleton)
    const service = options?.service ?? eventTypeService;
    // Check cache first (if service has caching enabled)
    const cachedConfig = service.getCachedConfig(normalizedId);
    if (cachedConfig) {
        return {
            success: true,
            config: cachedConfig,
            eventTypeId: normalizedId,
        };
    }
    try {
        // Query Dataverse for Event Type record
        const record = await webApi.retrieveRecord(EVENT_TYPE_ENTITY, normalizedId, `?$select=${EVENT_TYPE_SELECT_FIELDS}`);
        // Extract fields from response
        const eventTypeName = record['sprk_name'] ?? undefined;
        const fieldConfigJson = record['sprk_fieldconfigjson'] ?? null;
        // Parse the field configuration JSON
        const config = service.parseFieldConfigJson(fieldConfigJson);
        // Cache the result if caching is enabled and config is valid
        if (config) {
            service.setCachedConfig(normalizedId, config);
        }
        return {
            success: true,
            config,
            eventTypeName,
            eventTypeId: normalizedId,
        };
    }
    catch (error) {
        // Handle specific error cases
        const errorMessage = error instanceof Error ? error.message : String(error);
        // Check for 404 Not Found (Dataverse returns this when record doesn't exist)
        // The error message format varies by API but typically includes "not found" or 404
        const isNotFound = errorMessage.includes('404') ||
            errorMessage.toLowerCase().includes('not found') ||
            errorMessage.toLowerCase().includes('does not exist');
        if (isNotFound) {
            console.warn(`[EventTypeService] Event Type not found: ${normalizedId}`);
            return {
                success: false,
                config: null,
                eventTypeId: normalizedId,
                error: `Event Type with ID '${normalizedId}' not found`,
                notFound: true,
            };
        }
        // Log and return generic error
        console.error(`[EventTypeService] Failed to fetch Event Type config: ${errorMessage}`);
        return {
            success: false,
            config: null,
            eventTypeId: normalizedId,
            error: errorMessage,
            notFound: false,
        };
    }
}
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
export const eventTypeService = new EventTypeService();
//# sourceMappingURL=EventTypeService.js.map