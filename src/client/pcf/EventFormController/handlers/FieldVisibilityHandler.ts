/**
 * FieldVisibilityHandler
 *
 * Manages Dataverse form field visibility and requirement levels.
 * Uses the shared EventTypeService for field configuration and default states.
 *
 * STUB: [UI] - S004: Uses legacy Xrm.Page API - acceptable for now, may need
 * update for modern formContext in future Dataverse releases.
 *
 * @version 2.0.0 - Refactored to use shared EventTypeService (ADR-012)
 */

// Import types and default states from shared service
import {
    IFieldRule,
    IFieldDefaultStates,
    IEventTypeFieldConfig,
    IApplyRulesResult,
    IComputedFieldStates,
    RequiredLevel
} from "@spaarke/ui-components";

import {
    EventTypeService,
    eventTypeService,
    DEFAULT_EVENT_FIELD_STATES,
    ALL_EVENT_FIELDS,
    ALL_SECTION_NAMES,
    DEFAULT_SECTION_STATES
} from "@spaarke/ui-components";

// Re-export types for backward compatibility with existing code
export type { IFieldRule, IApplyRulesResult, RequiredLevel };

/**
 * Legacy type alias for backward compatibility
 * @deprecated Use IFieldRule from @spaarke/ui-components
 */
export interface FieldRule {
    fieldName: string;
    visible: boolean;
    required: boolean;
}

/**
 * Legacy type alias for backward compatibility
 * @deprecated Use IEventTypeFieldConfig from @spaarke/ui-components
 */
export interface EventTypeFieldConfig {
    requiredFields: string[];
    hiddenFields: string[];
    optionalFields?: string[];
}

/**
 * Legacy type alias for backward compatibility
 * @deprecated Use IFieldDefaultStates from @spaarke/ui-components
 */
export interface FieldDefaultStates {
    [fieldName: string]: {
        visible: boolean;
        requiredLevel: RequiredLevel;
    };
}

/**
 * Legacy type alias for backward compatibility
 * @deprecated Use IApplyRulesResult from @spaarke/ui-components
 */
export interface ApplyRulesResult {
    success: boolean;
    rulesApplied: number;
    skippedFields: string[];
    errors: string[];
}

// Re-export default field states from shared service for backward compatibility
export { DEFAULT_EVENT_FIELD_STATES as DEFAULT_FIELD_STATES, ALL_EVENT_FIELDS };

// Re-export the shared service for direct usage
export { eventTypeService, EventTypeService };

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
 * Sets the requirement level for a field
 *
 * @param fieldName - The schema name of the field
 * @param level - The requirement level to set
 * @returns true if successful, false if field not found
 */
export function setRequiredLevel(fieldName: string, level: RequiredLevel): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - setRequiredLevel");
        return false;
    }

    const attribute = formContext.getAttribute(fieldName);
    if (!attribute) {
        console.warn(`[FieldVisibilityHandler] Attribute not found: ${fieldName}`);
        return false;
    }

    try {
        attribute.setRequiredLevel(level);
        return true;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error setting required level ${fieldName}:`, err);
        return false;
    }
}

// =============================================================================
// Section Visibility Functions
// =============================================================================

/**
 * Shows a section on the form
 * Sets the section to visible using Dataverse section.setVisible() API
 *
 * @param sectionName - The name of the section (e.g., "dates", "relatedEvent")
 * @returns true if successful, false if section not found
 */
export function showSection(sectionName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - showSection");
        return false;
    }

    // Get all tabs and search for the section
    const tabs = formContext.ui?.tabs;
    if (!tabs) {
        console.warn("[FieldVisibilityHandler] Form tabs not available");
        return false;
    }

    try {
        let found = false;
        tabs.forEach((tab: any) => {
            const sections = tab.sections;
            if (sections) {
                sections.forEach((section: any) => {
                    if (section.getName() === sectionName) {
                        section.setVisible(true);
                        found = true;
                    }
                });
            }
        });

        if (!found) {
            console.warn(`[FieldVisibilityHandler] Section not found on form: ${sectionName}`);
        }
        return found;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error showing section ${sectionName}:`, err);
        return false;
    }
}

/**
 * Hides a section on the form
 * Sets the section to not visible using Dataverse section.setVisible() API
 *
 * @param sectionName - The name of the section (e.g., "dates", "relatedEvent")
 * @returns true if successful, false if section not found
 */
export function hideSection(sectionName: string): boolean {
    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[FieldVisibilityHandler] Xrm.Page not available - hideSection");
        return false;
    }

    // Get all tabs and search for the section
    const tabs = formContext.ui?.tabs;
    if (!tabs) {
        console.warn("[FieldVisibilityHandler] Form tabs not available");
        return false;
    }

    try {
        let found = false;
        tabs.forEach((tab: any) => {
            const sections = tab.sections;
            if (sections) {
                sections.forEach((section: any) => {
                    if (section.getName() === sectionName) {
                        section.setVisible(false);
                        found = true;
                    }
                });
            }
        });

        if (!found) {
            console.warn(`[FieldVisibilityHandler] Section not found on form: ${sectionName}`);
        }
        return found;
    } catch (err) {
        console.error(`[FieldVisibilityHandler] Error hiding section ${sectionName}:`, err);
        return false;
    }
}

/**
 * Sets section visibility
 *
 * @param sectionName - The name of the section
 * @param visible - Whether the section should be visible
 * @returns true if successful, false if section not found
 */
export function setSectionVisibility(sectionName: string, visible: boolean): boolean {
    return visible ? showSection(sectionName) : hideSection(sectionName);
}

/**
 * Gets the current visibility state of a section
 *
 * @param sectionName - The name of the section
 * @returns true if visible, false if hidden, undefined if section not found
 */
export function isSectionVisible(sectionName: string): boolean | undefined {
    const formContext = getFormContext();
    if (!formContext) return undefined;

    const tabs = formContext.ui?.tabs;
    if (!tabs) return undefined;

    try {
        let visible: boolean | undefined;
        tabs.forEach((tab: any) => {
            const sections = tab.sections;
            if (sections) {
                sections.forEach((section: any) => {
                    if (section.getName() === sectionName) {
                        visible = section.getVisible();
                    }
                });
            }
        });
        return visible;
    } catch {
        return undefined;
    }
}

// =============================================================================
// Reset and Apply Functions
// =============================================================================

/**
 * Resets all fields to their default visibility and requirement states
 * Called when Event Type is cleared. Uses DEFAULT_EVENT_FIELD_STATES from shared service.
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

    // Use default states from shared service
    for (const [fieldName, defaults] of Object.entries(DEFAULT_EVENT_FIELD_STATES)) {
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

    // Reset all sections to visible
    let sectionsReset = 0;
    for (const sectionName of ALL_SECTION_NAMES) {
        try {
            if (showSection(sectionName)) {
                sectionsReset++;
            }
        } catch (err) {
            const errorMsg = err instanceof Error ? err.message : String(err);
            result.errors.push(`Error resetting section ${sectionName}: ${errorMsg}`);
        }
    }

    if (result.errors.length > 0) {
        result.success = false;
    }

    console.log(`[FieldVisibilityHandler] Reset complete: ${result.rulesApplied} fields, ${sectionsReset} sections, ${result.skippedFields.length} skipped`);
    return result;
}

/**
 * Applies computed field states to the form.
 * This is the new approach using EventTypeService's computed states.
 *
 * @param computed - Computed field states from EventTypeService.computeFieldStates()
 * @returns Result of applying the rules
 */
export function applyComputedFieldStates(computed: IComputedFieldStates): ApplyRulesResult {
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

    // Apply each field's computed state
    for (const [fieldName, state] of computed.fields) {
        try {
            // Set visibility
            const control = formContext.getControl(fieldName);
            if (control) {
                control.setVisible(state.isVisible);
            } else {
                result.skippedFields.push(fieldName);
                continue; // Skip to next field
            }

            // Set requirement level
            const attribute = formContext.getAttribute(fieldName);
            if (attribute) {
                attribute.setRequiredLevel(state.requiredLevel);
            }

            result.rulesApplied++;
        } catch (err) {
            const errorMsg = err instanceof Error ? err.message : String(err);
            result.errors.push(`Error applying state to ${fieldName}: ${errorMsg}`);
        }
    }

    // Apply section visibility states
    let sectionsApplied = 0;
    const skippedSections: string[] = [];

    for (const [sectionName, state] of computed.sections) {
        try {
            const success = setSectionVisibility(sectionName, state.isVisible);
            if (success) {
                sectionsApplied++;
            } else {
                skippedSections.push(sectionName);
            }
        } catch (err) {
            const errorMsg = err instanceof Error ? err.message : String(err);
            result.errors.push(`Error applying section state to ${sectionName}: ${errorMsg}`);
        }
    }

    if (result.errors.length > 0) {
        result.success = false;
    }

    console.log(`[FieldVisibilityHandler] Applied computed states: ${result.rulesApplied} fields, ${sectionsApplied} sections, ${result.skippedFields.length} fields skipped, ${skippedSections.length} sections skipped`);
    return result;
}

/**
 * Applies field visibility and requirement rules based on Event Type configuration.
 * This is the legacy approach - consider using applyComputedFieldStates() with EventTypeService instead.
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
export function getFieldRequiredLevel(fieldName: string): RequiredLevel | undefined {
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
