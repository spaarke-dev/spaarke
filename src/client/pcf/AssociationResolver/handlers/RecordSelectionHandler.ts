/**
 * RecordSelectionHandler - Manages regarding field population
 *
 * When a user selects a related record in AssociationResolver:
 * 1. Sets the entity-specific lookup field (e.g., sprk_regardingmatter for Matter)
 * 2. Sets denormalized fields (sprk_regardingrecordname, sprk_regardingrecordtype)
 * 3. Clears the other 7 entity-specific lookup fields
 *
 * Uses Xrm.Page.getAttribute() for form field access.
 *
 * ADR Compliance:
 * - ADR-006: Logic in PCF control code, not Business Rules
 * - ADR-021: No UI changes here (Fluent UI v9 in components)
 *
 * STUB: [CONFIG] - S021-01: Lookup field names should come from configuration, not hardcoded
 */

/**
 * Selection data passed when user picks a record
 */
export interface IRecordSelection {
    entityType: string;      // Logical name (e.g., "sprk_matter")
    recordId: string;        // GUID of selected record
    recordName: string;      // Display name of selected record
}

/**
 * Entity configuration mapping
 */
interface EntityLookupConfig {
    logicalName: string;
    displayName: string;
    regardingField: string;
    regardingRecordTypeValue: number;
}

// STUB: [CONFIG] - S021-01: Lookup field names should come from configuration, not hardcoded
// These must match ENTITY_CONFIGS in AssociationResolverApp.tsx and spec.md
const ENTITY_LOOKUP_CONFIGS: EntityLookupConfig[] = [
    { logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter", regardingRecordTypeValue: 1 },
    { logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject", regardingRecordTypeValue: 0 },
    { logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice", regardingRecordTypeValue: 2 },
    { logicalName: "sprk_analysis", displayName: "Analysis", regardingField: "sprk_regardinganalysis", regardingRecordTypeValue: 3 },
    { logicalName: "account", displayName: "Account", regardingField: "sprk_regardingaccount", regardingRecordTypeValue: 4 },
    { logicalName: "contact", displayName: "Contact", regardingField: "sprk_regardingcontact", regardingRecordTypeValue: 5 },
    { logicalName: "sprk_workassignment", displayName: "Work Assignment", regardingField: "sprk_regardingworkassignment", regardingRecordTypeValue: 6 },
    { logicalName: "sprk_budget", displayName: "Budget", regardingField: "sprk_regardingbudget", regardingRecordTypeValue: 7 }
];

// Denormalized field names for unified views
const DENORMALIZED_FIELDS = {
    recordName: "sprk_regardingrecordname",
    recordId: "sprk_regardingrecordid",
    recordType: "sprk_regardingrecordtype"
};

/**
 * Get Xrm.Page from parent window (PCF runs in iframe)
 */
function getXrmPage(): Xrm.Page | null {
    try {
        const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;
        return xrm?.Page || null;
    } catch (error) {
        console.warn("[RecordSelectionHandler] Unable to access Xrm.Page:", error);
        return null;
    }
}

/**
 * Set a lookup field value
 * Handles gracefully if field doesn't exist on form
 */
function setLookupValue(
    fieldName: string,
    entityType: string,
    id: string,
    name: string
): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.warn("[RecordSelectionHandler] Xrm.Page not available");
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            // Format GUID without braces for lookup value
            const formattedId = id.replace(/[{}]/g, '');
            attr.setValue([{
                id: formattedId,
                name: name,
                entityType: entityType
            }]);
            console.log(`[RecordSelectionHandler] Set ${fieldName} to ${name} (${formattedId})`);
            return true;
        } else {
            console.warn(`[RecordSelectionHandler] Field ${fieldName} not found on form`);
            return false;
        }
    } catch (error) {
        console.error(`[RecordSelectionHandler] Error setting ${fieldName}:`, error);
        return false;
    }
}

/**
 * Clear a lookup field value
 * Handles gracefully if field doesn't exist on form
 */
function clearLookupValue(fieldName: string): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.warn("[RecordSelectionHandler] Xrm.Page not available");
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            attr.setValue(null);
            console.log(`[RecordSelectionHandler] Cleared ${fieldName}`);
            return true;
        } else {
            // Field not on form - not an error, just skip
            return true;
        }
    } catch (error) {
        console.error(`[RecordSelectionHandler] Error clearing ${fieldName}:`, error);
        return false;
    }
}

/**
 * Set a text field value
 * Handles gracefully if field doesn't exist on form
 */
function setTextValue(fieldName: string, value: string | null): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.warn("[RecordSelectionHandler] Xrm.Page not available");
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            attr.setValue(value);
            console.log(`[RecordSelectionHandler] Set ${fieldName} to "${value}"`);
            return true;
        } else {
            console.warn(`[RecordSelectionHandler] Field ${fieldName} not found on form`);
            return false;
        }
    } catch (error) {
        console.error(`[RecordSelectionHandler] Error setting ${fieldName}:`, error);
        return false;
    }
}

/**
 * Set an optionset field value
 * Handles gracefully if field doesn't exist on form
 */
function setOptionSetValue(fieldName: string, value: number | null): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.warn("[RecordSelectionHandler] Xrm.Page not available");
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            attr.setValue(value);
            console.log(`[RecordSelectionHandler] Set ${fieldName} to ${value}`);
            return true;
        } else {
            console.warn(`[RecordSelectionHandler] Field ${fieldName} not found on form`);
            return false;
        }
    } catch (error) {
        console.error(`[RecordSelectionHandler] Error setting ${fieldName}:`, error);
        return false;
    }
}

/**
 * Result of handling a record selection
 */
export interface IRecordSelectionResult {
    success: boolean;
    lookupFieldSet: boolean;
    denormalizedFieldsSet: boolean;
    otherLookupsCleared: number;
    errors: string[];
}

/**
 * Handle record selection - main entry point
 *
 * When a record is selected:
 * 1. Clear all 8 entity-specific lookup fields
 * 2. Set the selected entity's lookup field
 * 3. Set denormalized fields (name, id, type)
 */
export function handleRecordSelection(selection: IRecordSelection): IRecordSelectionResult {
    const result: IRecordSelectionResult = {
        success: false,
        lookupFieldSet: false,
        denormalizedFieldsSet: false,
        otherLookupsCleared: 0,
        errors: []
    };

    console.log(`[RecordSelectionHandler] Processing selection: ${selection.entityType} - ${selection.recordName}`);

    // Find the config for the selected entity type
    const selectedConfig = ENTITY_LOOKUP_CONFIGS.find(c => c.logicalName === selection.entityType);
    if (!selectedConfig) {
        const error = `Unknown entity type: ${selection.entityType}`;
        console.error(`[RecordSelectionHandler] ${error}`);
        result.errors.push(error);
        return result;
    }

    // Step 1: Clear ALL entity-specific lookup fields first (including the one we'll set)
    for (const config of ENTITY_LOOKUP_CONFIGS) {
        if (clearLookupValue(config.regardingField)) {
            result.otherLookupsCleared++;
        }
    }
    // Adjust count - we'll set one back
    result.otherLookupsCleared--;

    // Step 2: Set the selected entity's lookup field
    result.lookupFieldSet = setLookupValue(
        selectedConfig.regardingField,
        selection.entityType,
        selection.recordId,
        selection.recordName
    );

    if (!result.lookupFieldSet) {
        result.errors.push(`Failed to set ${selectedConfig.regardingField}`);
    }

    // Step 3: Set denormalized fields
    let denormalizedSuccess = true;

    // Set record name
    if (!setTextValue(DENORMALIZED_FIELDS.recordName, selection.recordName)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordName}`);
    }

    // Set record ID (GUID without braces)
    const formattedId = selection.recordId.replace(/[{}]/g, '');
    if (!setTextValue(DENORMALIZED_FIELDS.recordId, formattedId)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordId}`);
    }

    // Set record type (optionset value)
    if (!setOptionSetValue(DENORMALIZED_FIELDS.recordType, selectedConfig.regardingRecordTypeValue)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordType}`);
    }

    result.denormalizedFieldsSet = denormalizedSuccess;

    // Overall success if lookup was set (denormalized fields are secondary)
    result.success = result.lookupFieldSet;

    console.log(`[RecordSelectionHandler] Result: success=${result.success}, lookupSet=${result.lookupFieldSet}, denormalized=${result.denormalizedFieldsSet}, cleared=${result.otherLookupsCleared}`);

    return result;
}

/**
 * Clear all regarding fields (used when clearing selection)
 */
export function clearAllRegardingFields(): void {
    console.log("[RecordSelectionHandler] Clearing all regarding fields");

    // Clear all entity-specific lookups
    for (const config of ENTITY_LOOKUP_CONFIGS) {
        clearLookupValue(config.regardingField);
    }

    // Clear denormalized fields
    setTextValue(DENORMALIZED_FIELDS.recordName, null);
    setTextValue(DENORMALIZED_FIELDS.recordId, null);
    setOptionSetValue(DENORMALIZED_FIELDS.recordType, null);
}

/**
 * Get the entity config for a logical name
 */
export function getEntityConfig(logicalName: string): EntityLookupConfig | undefined {
    return ENTITY_LOOKUP_CONFIGS.find(c => c.logicalName === logicalName);
}

/**
 * Get all entity configs (for dropdown population)
 */
export function getAllEntityConfigs(): EntityLookupConfig[] {
    return [...ENTITY_LOOKUP_CONFIGS];
}
