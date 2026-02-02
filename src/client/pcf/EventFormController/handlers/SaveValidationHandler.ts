/**
 * SaveValidationHandler
 *
 * Validates required fields before form save based on Event Type configuration.
 * Integrates with Xrm.Page save events to block save if required fields are empty.
 *
 * STUB: [UI] - S005: Uses legacy Xrm.Page API - acceptable for now, may need
 * update for modern formContext in future Dataverse releases.
 *
 * @version 1.0.0
 */

import { IFieldRule } from "./FieldVisibilityHandler";

/** Currently active field rules */
let currentRules: IFieldRule[] = [];

/** Flag indicating if save handler is registered */
let saveHandlerRegistered = false;

/** Unique notification ID for validation errors */
const VALIDATION_NOTIFICATION_ID = "requiredFieldsValidation";

/**
 * Gets the Xrm form context from the window or parent window
 * Uses legacy Xrm.Page API for compatibility with current Dataverse forms
 */
function getFormContext(): any | null {
    const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
    return xrm?.Page || null;
}

/**
 * Registers the save validation handler with the form
 * Updates the current rules to validate against
 *
 * @param rules - Array of field rules defining which fields are required/visible
 */
export function registerSaveHandler(rules: IFieldRule[]): void {
    currentRules = rules;

    if (!saveHandlerRegistered) {
        const formContext = getFormContext();
        if (formContext?.data?.entity) {
            try {
                formContext.data.entity.addOnSave(onSaveHandler);
                saveHandlerRegistered = true;
                console.log("[SaveValidationHandler] Save handler registered");
            } catch (err) {
                console.error("[SaveValidationHandler] Failed to register save handler:", err);
            }
        } else {
            console.warn("[SaveValidationHandler] Xrm.Page.data.entity not available");
        }
    } else {
        console.log("[SaveValidationHandler] Save handler rules updated");
    }
}

/**
 * Unregisters the save validation handler from the form
 * Called during control cleanup
 */
export function unregisterSaveHandler(): void {
    if (saveHandlerRegistered) {
        const formContext = getFormContext();
        if (formContext?.data?.entity) {
            try {
                formContext.data.entity.removeOnSave(onSaveHandler);
                saveHandlerRegistered = false;
                currentRules = [];
                console.log("[SaveValidationHandler] Save handler unregistered");
            } catch (err) {
                console.error("[SaveValidationHandler] Failed to unregister save handler:", err);
            }
        }
    }

    // Clear any validation notifications
    clearValidationNotification();
}

/**
 * Clears the validation error notification from the form
 */
export function clearValidationNotification(): void {
    const formContext = getFormContext();
    if (formContext?.ui) {
        try {
            formContext.ui.clearFormNotification(VALIDATION_NOTIFICATION_ID);
        } catch (err) {
            // Ignore errors clearing notifications
        }
    }
}

/**
 * Sets a field notification (inline error indicator)
 *
 * @param fieldName - The schema name of the field
 * @param message - The notification message
 */
function setFieldNotification(fieldName: string, message: string): void {
    const formContext = getFormContext();
    if (!formContext) return;

    const control = formContext.getControl(fieldName);
    if (control && typeof control.setNotification === "function") {
        try {
            control.setNotification(message, VALIDATION_NOTIFICATION_ID);
        } catch (err) {
            console.warn(`[SaveValidationHandler] Could not set notification on ${fieldName}:`, err);
        }
    }
}

/**
 * Clears a field notification (inline error indicator)
 *
 * @param fieldName - The schema name of the field
 */
function clearFieldNotification(fieldName: string): void {
    const formContext = getFormContext();
    if (!formContext) return;

    const control = formContext.getControl(fieldName);
    if (control && typeof control.clearNotification === "function") {
        try {
            control.clearNotification(VALIDATION_NOTIFICATION_ID);
        } catch (err) {
            // Ignore errors clearing notifications
        }
    }
}

/**
 * Checks if a field value is empty/null
 *
 * @param value - The field value to check
 * @returns true if the value is considered empty
 */
function isFieldValueEmpty(value: any): boolean {
    if (value === null || value === undefined) {
        return true;
    }
    if (typeof value === "string" && value.trim() === "") {
        return true;
    }
    if (Array.isArray(value) && value.length === 0) {
        return true;
    }
    return false;
}

/**
 * Gets a user-friendly display name for a field
 *
 * @param fieldName - The schema name of the field
 * @param displayName - Optional display name from rule
 * @returns User-friendly field name
 */
function getFieldDisplayName(fieldName: string, displayName?: string): string {
    if (displayName) {
        return displayName;
    }

    // Try to get display name from form control
    const formContext = getFormContext();
    if (formContext) {
        const control = formContext.getControl(fieldName);
        if (control && typeof control.getLabel === "function") {
            try {
                const label = control.getLabel();
                if (label) return label;
            } catch {
                // Fall through to default
            }
        }
    }

    // Format schema name as display name (sprk_fieldname -> Fieldname)
    return fieldName
        .replace(/^sprk_/, "")
        .replace(/_/g, " ")
        .replace(/\b\w/g, (c) => c.toUpperCase());
}

/**
 * Save event handler - validates required fields and blocks save if empty
 *
 * @param context - The save event context
 */
function onSaveHandler(context: any): void {
    // Only validate required fields that are visible
    const requiredFields = currentRules.filter((r) => r.isRequired && r.isVisible);

    if (requiredFields.length === 0) {
        // No required fields to validate - allow save
        clearValidationNotification();
        return;
    }

    const formContext = getFormContext();
    if (!formContext) {
        console.warn("[SaveValidationHandler] Xrm.Page not available during save");
        return;
    }

    const emptyRequiredFields: string[] = [];

    for (const field of requiredFields) {
        const attr = formContext.getAttribute(field.fieldName);

        // Clear any previous field notification first
        clearFieldNotification(field.fieldName);

        if (attr) {
            const value = attr.getValue();
            if (isFieldValueEmpty(value)) {
                const displayName = getFieldDisplayName(field.fieldName, field.displayName);
                emptyRequiredFields.push(displayName);

                // Set inline notification on the field
                setFieldNotification(field.fieldName, `${displayName} is required`);
            }
        }
    }

    if (emptyRequiredFields.length > 0) {
        // Prevent save
        if (context?.getEventArgs && typeof context.getEventArgs === "function") {
            const eventArgs = context.getEventArgs();
            if (eventArgs && typeof eventArgs.preventDefault === "function") {
                eventArgs.preventDefault();
            }
        }

        // Show form notification with all missing fields
        const message =
            emptyRequiredFields.length === 1
                ? `Please fill in required field: ${emptyRequiredFields[0]}`
                : `Please fill in required fields: ${emptyRequiredFields.join(", ")}`;

        if (formContext.ui && typeof formContext.ui.setFormNotification === "function") {
            try {
                formContext.ui.setFormNotification(message, "ERROR", VALIDATION_NOTIFICATION_ID);
            } catch (err) {
                console.error("[SaveValidationHandler] Failed to set form notification:", err);
            }
        }

        console.log(`[SaveValidationHandler] Save blocked - missing required fields: ${emptyRequiredFields.join(", ")}`);
    } else {
        // Clear any previous validation notification - all required fields are filled
        clearValidationNotification();
        console.log("[SaveValidationHandler] Validation passed - save proceeding");
    }
}

/**
 * Validates a single field and updates its notification state
 * Can be called on field change to provide immediate feedback
 *
 * @param fieldName - The schema name of the field to validate
 * @returns true if field is valid, false if required and empty
 */
export function validateField(fieldName: string): boolean {
    const rule = currentRules.find((r) => r.fieldName === fieldName);

    // If no rule or not required, field is valid
    if (!rule || !rule.isRequired || !rule.isVisible) {
        clearFieldNotification(fieldName);
        return true;
    }

    const formContext = getFormContext();
    if (!formContext) return true;

    const attr = formContext.getAttribute(fieldName);
    if (!attr) return true;

    const value = attr.getValue();
    if (isFieldValueEmpty(value)) {
        const displayName = getFieldDisplayName(fieldName, rule.displayName);
        setFieldNotification(fieldName, `${displayName} is required`);
        return false;
    }

    clearFieldNotification(fieldName);
    return true;
}

/**
 * Checks if save handler is currently registered
 *
 * @returns true if save handler is registered
 */
export function isSaveHandlerRegistered(): boolean {
    return saveHandlerRegistered;
}

/**
 * Gets the current validation rules
 *
 * @returns Array of current field rules
 */
export function getCurrentRules(): IFieldRule[] {
    return [...currentRules];
}
