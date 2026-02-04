/**
 * FieldVisibilityHandler
 *
 * Manages Dataverse form field visibility and requirement levels.
 * Extracted from EventFormControllerApp for cleaner separation of concerns.
 *
 * STUB: [UI] - S004: Uses legacy Xrm.Page API - acceptable for now, may need
 * update for modern formContext in future Dataverse releases.
 *
 * @version 1.0.0
 */

/**
 * Configuration for a field's visibility and requirement
 */
export interface FieldRule {
    fieldName: string;
    visible: boolean;
    required: boolean;
}

/**
 * Field rule for save validation - includes display name for user-friendly messages
 */
export interface IFieldRule {
    fieldName: string;
    displayName?: string;
    isVisible: boolean;
    isRequired: boolean;
}

/**
 * Aggregate configuration for all fields based on Event Type
 */
export interface EventTypeFieldConfig {
    requiredFields: string[];
    hiddenFields: string[];
    optionalFields?: string[];
}

/**
 * Default field states to restore when Event Type is cleared
 */
export interface FieldDefaultStates {
    [fieldName: string]: {
        visible: boolean;
        requiredLevel: "required" | "recommended" | "none";
    };
}

/**
 * Result of applying field rules
 */
export interface ApplyRulesResult {
    success: boolean;
    rulesApplied: number;
    skippedFields: string[];
    errors: string[];
}

/**
 * Gets the Xrm form context from the window or parent window
 * Uses legacy Xrm.Page API for compatibility with current Dataverse forms
 */
function getFormContext(): any | null {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
    return xrm?.Page || null;
}

/**
 * Shows a field on the form
 * Sets the control to visible
 *
 * @param fieldName - The schema name of the field
 * @returns true if successful, false if field not found
 */
export function showField(fieldName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - showField");
        return false;
    }

    const control = formContext.getControl(fieldName);
    if (!control) {
        console.warn(`[FieldVisibilityHandler] Field not found on form: ${fieldName}`);
        return false;
    }

    try {
        control.setVisible(true);
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error showing field ${fieldName}:`, err);
        return false;
    }
}

/**
 * Hides a field on the form
 * Sets the control to not visible
 *
 * @param fieldName - The schema name of the field
 * @returns true if successful, false if field not found
 */
export function hideField(fieldName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - hideField");
        return false;
    }

    const control = formContext.getControl(fieldName);
    if (!control) {
        console.warn(`[FieldVisibilityHandler] Field not found on form: ${fieldName}`);
        return false;
    }

    try {
        control.setVisible(false);
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error hiding field ${fieldName}:`, err);
        return false;
    }
}

/**
 * Sets a field as required
 * Uses setRequiredLevel("required") on the attribute
 * Also ensures the field is visible
 *
 * @param fieldName - The schema name of the field
 * @returns true if successful, false if field not found
 */
export function setRequired(fieldName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - setRequired");
        return false;
    }

    const attribute = formContext.getAttribute(fieldName);
    if (!attribute) {
        console.warn(`[FieldVisibilityHandler] Attribute not found: ${fieldName}`);
        return false;
    }

    try {
        attribute.setRequiredLevel("required");
        // Also ensure the field is visible when required
        showField(fieldName);
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error setting required ${fieldName}:`, err);
        return false;
    }
}

/**
 * Sets a field as optional (not required)
 * Uses setRequiredLevel("none") on the attribute
 *
 * @param fieldName - The schema name of the field
 * @returns true if successful, false if field not found
 */
export function setOptional(fieldName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - setOptional");
        return false;
    }

    const attribute = formContext.getAttribute(fieldName);
    if (!attribute) {
        console.warn(`[FieldVisibilityHandler] Attribute not found: ${fieldName}`);
        return false;
    }

    try {
        attribute.setRequiredLevel("none");
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error setting optional ${fieldName}:`, err);
        return false;
    }
}

/**
 * Sets a field as recommended (business recommended but not required)
 * Uses setRequiredLevel("recommended") on the attribute
 *
 * @param fieldName - The schema name of the field
 * @returns true if successful, false if field not found
 */
export function setRecommended(fieldName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - setRecommended");
        return false;
    }

    const attribute = formContext.getAttribute(fieldName);
    if (!attribute) {
        console.warn(`[FieldVisibilityHandler] Attribute not found: ${fieldName}`);
        return false;
    }

    try {
        attribute.setRequiredLevel("recommended");
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error setting recommended ${fieldName}:`, err);
        return false;
    }
}

/**
 * Default field states for Event form
 * These are restored when Event Type is cleared
 */
export const DEFAULT_FIELD_STATES: FieldDefaultStates = {
    // Primary fields - always visible, subject is required
    "sprk_eventname": { visible: true, requiredLevel: "required" },
    "sprk_description": { visible: true, requiredLevel: "none" },

    // Date fields - visible but not required by default
    "sprk_basedate": { visible: true, requiredLevel: "none" },
    "sprk_duedate": { visible: true, requiredLevel: "none" },
    "sprk_completeddate": { visible: true, requiredLevel: "none" },
    "scheduledstart": { visible: true, requiredLevel: "none" },
    "scheduledend": { visible: true, requiredLevel: "none" },

    // Location - visible but optional
    "sprk_location": { visible: true, requiredLevel: "none" },

    // Reminder - visible but optional
    "sprk_remindat": { visible: true, requiredLevel: "none" },

    // Status fields - visible
    "statecode": { visible: true, requiredLevel: "none" },
    "statuscode": { visible: true, requiredLevel: "none" },
    "sprk_priority": { visible: true, requiredLevel: "none" },
    "sprk_source": { visible: true, requiredLevel: "none" },

    // Related event fields - visible but optional
    "sprk_relatedevent": { visible: true, requiredLevel: "none" },
    "sprk_relatedeventtype": { visible: true, requiredLevel: "none" },
    "sprk_relatedeventoffsettype": { visible: true, requiredLevel: "none" }
};

/**
 * List of all controllable fields on the Event form
 */
export const ALL_EVENT_FIELDS = Object.keys(DEFAULT_FIELD_STATES);

/**
 * Resets all fields to their default visibility and requirement states
 * Called when Event Type is cleared
 *
 * @returns Result of the reset operation
 */
export function resetToDefaults(): ApplyRulesResult {
    const result: ApplyRulesResult = {
        success: true,
        rulesApplied: 0,
        skippedFields: [],
        errors: []
    };

    const formContext = getFormContext();
    if (!formContext) {
        result.success = false;
        result.errors.push("Xrm.Page not available");
        return result;
    }

    for (const [fieldName, defaults] of Object.entries(DEFAULT_FIELD_STATES)) {
        try {
            // Reset visibility
            const control = formContext.getControl(fieldName);
            if (control) {
                control.setVisible(defaults.visible);
            } else {
                result.skippedFields.push(fieldName);
                continue; // Skip to next field
            }

            // Reset requirement level
            const attribute = formContext.getAttribute(fieldName);
            if (attribute) {
                attribute.setRequiredLevel(defaults.requiredLevel);
            }

            result.rulesApplied++;
        } catch (err) {
            const errorMsg = err instanceof Error ? err.message : String(err);
            result.errors.push(`Error resetting ${fieldName}: ${errorMsg}`);
        }
    }

    if (result.errors.length > 0) {
        result.success = false;
    }

    console.log(`[FieldVisibilityHandler] Reset complete: ${result.rulesApplied} fields, ${result.skippedFields.length} skipped`);
    return result;
}

/**
 * Applies field visibility and requirement rules based on Event Type configuration
 *
 * @param config - The Event Type field configuration
 * @returns Result of applying the rules
 */
export function applyFieldRules(config: EventTypeFieldConfig): ApplyRulesResult {
    const result: ApplyRulesResult = {
        success: true,
        rulesApplied: 0,
        skippedFields: [],
        errors: []
    };

    const formContext = getFormContext();
    if (!formContext) {
        result.success = false;
        result.errors.push("Xrm.Page not available");
        return result;
    }

    // First, reset optional fields to ensure clean state
    if (config.optionalFields) {
        for (const fieldName of config.optionalFields) {
            if (setOptional(fieldName)) {
                result.rulesApplied++;
            } else {
                result.skippedFields.push(fieldName);
            }
        }
    }

    // Set required fields - also makes them visible
    for (const fieldName of config.requiredFields) {
        if (setRequired(fieldName)) {
            result.rulesApplied++;
        } else {
            result.skippedFields.push(fieldName);
        }
    }

    // Hide fields
    for (const fieldName of config.hiddenFields) {
        if (hideField(fieldName)) {
            // Also set hidden fields to optional (can't require hidden fields)
            setOptional(fieldName);
            result.rulesApplied++;
        } else {
            result.skippedFields.push(fieldName);
        }
    }

    if (result.skippedFields.length > 0) {
        console.warn(`[FieldVisibilityHandler] Skipped fields not on form: ${result.skippedFields.join(", ")}`);
    }

    console.log(`[FieldVisibilityHandler] Applied ${result.rulesApplied} rules, skipped ${result.skippedFields.length}`);
    return result;
}

/**
 * Checks if Xrm form context is available
 * Useful for conditional rendering in PCF controls
 */
export function isFormContextAvailable(): boolean {
    return getFormContext() !== null;
}

/**
 * Gets the current visibility state of a field
 *
 * @param fieldName - The schema name of the field
 * @returns true if visible, false if hidden, undefined if field not found
 */
export function isFieldVisible(fieldName: string): boolean | undefined {
    const formContext = getFormContext();
    if (!formContext) return undefined;

    const control = formContext.getControl(fieldName);
    if (!control) return undefined;

    try {
        return control.getVisible();
    } catch {
        return undefined;
    }
}

/**
 * Gets the current requirement level of a field
 *
 * @param fieldName - The schema name of the field
 * @returns "required" | "recommended" | "none" | undefined if field not found
 */
export function getFieldRequiredLevel(fieldName: string): "required" | "recommended" | "none" | undefined {
    const formContext = getFormContext();
    if (!formContext) return undefined;

    const attribute = formContext.getAttribute(fieldName);
    if (!attribute) return undefined;

    try {
        return attribute.getRequiredLevel();
    } catch {
        return undefined;
    }
}
