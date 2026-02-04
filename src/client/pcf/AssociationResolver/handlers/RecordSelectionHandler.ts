/**
 * RecordSelectionHandler - Manages regarding field population
 *
 * When a user selects a related record in AssociationResolver:
 * 1. Sets the entity-specific lookup field (e.g., sprk_regardingmatter for Matter)
 * 2. Sets denormalized fields (sprk_regardingrecordname, sprk_regardingrecordtype)
 * 3. Clears the other 7 entity-specific lookup fields
 *
 * Uses Xrm.Page.getAttribute() for form field access.
 * Uses WebAPI for Record Type lookup queries.
 *
 * ADR Compliance:
 * - ADR-006: Logic in PCF control code, not Business Rules
 * - ADR-021: No UI changes here (Fluent UI v9 in components)
 *
 * STUB: [CONFIG] - S021-01: Lookup field names should come from configuration, not hardcoded
 *
 * @version 1.1.0 - Updated to use Record Type lookup instead of OptionSet
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
export interface EntityLookupConfig {
    logicalName: string;
    displayName: string;
    regardingField: string;
    // Note: regardingRecordTypeValue removed - now using Record Type lookup
}

// STUB: [CONFIG] - S021-01: Lookup field names should come from configuration, not hardcoded
// These must match ENTITY_CONFIGS in AssociationResolverApp.tsx and spec.md
const ENTITY_LOOKUP_CONFIGS: EntityLookupConfig[] = [
    { logicalName: "sprk_matter", displayName: "Matter", regardingField: "sprk_regardingmatter" },
    { logicalName: "sprk_project", displayName: "Project", regardingField: "sprk_regardingproject" },
    { logicalName: "sprk_invoice", displayName: "Invoice", regardingField: "sprk_regardinginvoice" },
    { logicalName: "sprk_analysis", displayName: "Analysis", regardingField: "sprk_regardinganalysis" },
    { logicalName: "account", displayName: "Account", regardingField: "sprk_regardingaccount" },
    { logicalName: "contact", displayName: "Contact", regardingField: "sprk_regardingcontact" },
    { logicalName: "sprk_workassignment", displayName: "Work Assignment", regardingField: "sprk_regardingworkassignment" },
    { logicalName: "sprk_budget", displayName: "Budget", regardingField: "sprk_regardingbudget" }
];

// Denormalized field names for unified views
const DENORMALIZED_FIELDS = {
    recordName: "sprk_regardingrecordname",
    recordId: "sprk_regardingrecordid",
    recordType: "sprk_regardingrecordtype",  // Now a Lookup to sprk_recordtype_ref
    recordUrl: "sprk_regardingrecordurl"     // URL field - clickable link to parent record
};

/**
 * Build the Dataverse record URL for navigation
 * Generates a fully qualified URL that's portable between environments
 * Uses Xrm.Utility.getGlobalContext() for dynamic org URL and app ID
 */
function buildRecordUrl(entityLogicalName: string, recordId: string): string {
    const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();

    try {
        const xrm = (window as any).Xrm || (window.parent as any)?.Xrm;

        if (xrm?.Utility?.getGlobalContext) {
            const globalContext = xrm.Utility.getGlobalContext();

            // Get the org URL (e.g., https://spaarkedev1.crm.dynamics.com)
            const clientUrl = globalContext.getClientUrl?.() || "";

            // Get the current app ID
            let appId = "";
            if (globalContext.getCurrentAppId) {
                // getCurrentAppId returns a promise in some versions
                const appIdResult = globalContext.getCurrentAppId();
                if (typeof appIdResult === "string") {
                    appId = appIdResult;
                } else if (appIdResult && typeof appIdResult.then === "function") {
                    // It's a promise - we can't await here, so use sync fallback
                    // Try to get from URL instead
                    appId = getAppIdFromUrl();
                }
            }

            // Fallback: try to get app ID from current URL
            if (!appId) {
                appId = getAppIdFromUrl();
            }

            if (clientUrl) {
                // Build fully qualified URL
                const url = new URL("/main.aspx", clientUrl);
                if (appId) {
                    url.searchParams.set("appid", appId.replace(/[{}]/g, '').toLowerCase());
                }
                url.searchParams.set("pagetype", "entityrecord");
                url.searchParams.set("etn", entityLogicalName);
                url.searchParams.set("id", cleanId);

                console.log(`[RecordSelectionHandler] Built record URL: ${url.toString()}`);
                return url.toString();
            }
        }
    } catch (error) {
        console.warn("[RecordSelectionHandler] Error building record URL, using fallback:", error);
    }

    // Fallback to relative URL if context not available
    return `/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
}

/**
 * Extract app ID from current page URL
 * Fallback when Xrm.Utility.getGlobalContext().getCurrentAppId() is not available
 */
function getAppIdFromUrl(): string {
    try {
        // Check both current window and parent window URLs
        const urls = [window.location.href, window.parent?.location?.href].filter(Boolean);

        for (const urlStr of urls) {
            const url = new URL(urlStr as string);
            const appId = url.searchParams.get("appid");
            if (appId) {
                return appId.replace(/[{}]/g, '').toLowerCase();
            }
        }
    } catch (error) {
        // Cross-origin or other error - ignore
    }
    return "";
}

// Cache for Record Type lookups to avoid repeated queries
const recordTypeCache: Map<string, { id: string; name: string }> = new Map();

// Dynamic entity config cache - loaded from sprk_recordtype_ref
let dynamicEntityConfigs: EntityLookupConfig[] | null = null;
let entityConfigsLoading = false;
const entityConfigsLoadPromise: { promise: Promise<EntityLookupConfig[]> | null } = { promise: null };

/**
 * Load entity configurations dynamically from sprk_recordtype_ref
 * This replaces the hardcoded ENTITY_LOOKUP_CONFIGS
 */
export async function loadEntityConfigs(webApi: ComponentFramework.WebApi): Promise<EntityLookupConfig[]> {
    // Return cached if available
    if (dynamicEntityConfigs) {
        return dynamicEntityConfigs;
    }

    // If already loading, wait for that promise
    if (entityConfigsLoading && entityConfigsLoadPromise.promise) {
        return entityConfigsLoadPromise.promise;
    }

    entityConfigsLoading = true;
    entityConfigsLoadPromise.promise = (async () => {
        try {
            console.log("[RecordSelectionHandler] Loading entity configs from sprk_recordtype_ref...");
            const query = `?$filter=statecode eq 0&$select=sprk_recordtype_refid,sprk_recordlogicalname,sprk_recorddisplayname,sprk_regardingfield&$orderby=sprk_recorddisplayname`;
            const result = await webApi.retrieveMultipleRecords("sprk_recordtype_ref", query);

            if (result.entities && result.entities.length > 0) {
                dynamicEntityConfigs = result.entities
                    .filter((e: Record<string, unknown>) => e.sprk_recordlogicalname && e.sprk_regardingfield)
                    .map((e: Record<string, unknown>) => ({
                        logicalName: e.sprk_recordlogicalname as string,
                        displayName: (e.sprk_recorddisplayname || e.sprk_recordlogicalname) as string,
                        regardingField: e.sprk_regardingfield as string
                    }));

                console.log(`[RecordSelectionHandler] Loaded ${dynamicEntityConfigs.length} entity configs:`,
                    dynamicEntityConfigs.map(c => c.logicalName));
                return dynamicEntityConfigs;
            } else {
                console.warn("[RecordSelectionHandler] No Record Types found, falling back to hardcoded configs");
                dynamicEntityConfigs = [...ENTITY_LOOKUP_CONFIGS];
                return dynamicEntityConfigs;
            }
        } catch (error) {
            console.error("[RecordSelectionHandler] Error loading entity configs, using fallback:", error);
            dynamicEntityConfigs = [...ENTITY_LOOKUP_CONFIGS];
            return dynamicEntityConfigs;
        } finally {
            entityConfigsLoading = false;
        }
    })();

    return entityConfigsLoadPromise.promise;
}

/**
 * Get entity configs (sync version - returns cached or fallback)
 */
export function getEntityConfigs(): EntityLookupConfig[] {
    return dynamicEntityConfigs || ENTITY_LOOKUP_CONFIGS;
}

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
 * Query Record Type entity by entity logical name
 * Returns the Record Type record ID and name for the given entity
 */
async function getRecordTypeByEntityLogicalName(
    webApi: ComponentFramework.WebApi,
    entityLogicalName: string
): Promise<{ id: string; name: string } | null> {
    // Check cache first
    const cached = recordTypeCache.get(entityLogicalName);
    if (cached) {
        console.log(`[RecordSelectionHandler] Record Type cache hit for ${entityLogicalName}: ${cached.id}`);
        return cached;
    }

    try {
        // Query sprk_recordtype_ref by sprk_recordlogicalname
        const query = `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0&$select=sprk_recordtype_refid,sprk_recorddisplayname`;
        const result = await webApi.retrieveMultipleRecords("sprk_recordtype_ref", query);

        if (result.entities && result.entities.length > 0) {
            const recordType = result.entities[0];
            const id = recordType.sprk_recordtype_refid as string;
            const name = recordType.sprk_recorddisplayname as string;

            // Cache the result
            recordTypeCache.set(entityLogicalName, { id, name });

            console.log(`[RecordSelectionHandler] Found Record Type for ${entityLogicalName}: ${name} (${id})`);
            return { id, name };
        } else {
            console.warn(`[RecordSelectionHandler] No Record Type found for entity: ${entityLogicalName}`);
            return null;
        }
    } catch (error) {
        console.error(`[RecordSelectionHandler] Error querying Record Type for ${entityLogicalName}:`, error);
        return null;
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
 * Handle record selection - main entry point (async)
 *
 * When a record is selected:
 * 1. Clear all 8 entity-specific lookup fields
 * 2. Set the selected entity's lookup field
 * 3. Set denormalized fields (name, id, type)
 * 4. Query Record Type and set as lookup
 *
 * @param selection - The selected record details
 * @param webApi - WebAPI for Record Type queries
 */
export async function handleRecordSelection(
    selection: IRecordSelection,
    webApi: ComponentFramework.WebApi
): Promise<IRecordSelectionResult> {
    const result: IRecordSelectionResult = {
        success: false,
        lookupFieldSet: false,
        denormalizedFieldsSet: false,
        otherLookupsCleared: 0,
        errors: []
    };

    console.log(`[RecordSelectionHandler] Processing selection: ${selection.entityType} - ${selection.recordName}`);

    // Find the config for the selected entity type
    const selectedConfig = getEntityConfigs().find(c => c.logicalName === selection.entityType);
    if (!selectedConfig) {
        const error = `Unknown entity type: ${selection.entityType}`;
        console.error(`[RecordSelectionHandler] ${error}`);
        result.errors.push(error);
        return result;
    }

    // Step 1: Clear ALL entity-specific lookup fields first (including the one we'll set)
    for (const config of getEntityConfigs()) {
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

    // Set record URL (clickable link to parent record)
    const recordUrl = buildRecordUrl(selection.entityType, selection.recordId);
    if (!setTextValue(DENORMALIZED_FIELDS.recordUrl, recordUrl)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordUrl}`);
    }

    // Step 4: Query Record Type and set as lookup (async operation)
    const recordType = await getRecordTypeByEntityLogicalName(webApi, selection.entityType);
    if (recordType) {
        // Set record type as a lookup value to sprk_recordtype_ref entity
        if (!setLookupValue(DENORMALIZED_FIELDS.recordType, "sprk_recordtype_ref", recordType.id, recordType.name)) {
            denormalizedSuccess = false;
            result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordType}`);
        }
    } else {
        denormalizedSuccess = false;
        result.errors.push(`Record Type not found for entity: ${selection.entityType}`);
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
    for (const config of getEntityConfigs()) {
        clearLookupValue(config.regardingField);
    }

    // Clear denormalized fields
    setTextValue(DENORMALIZED_FIELDS.recordName, null);
    setTextValue(DENORMALIZED_FIELDS.recordId, null);
    setTextValue(DENORMALIZED_FIELDS.recordUrl, null);  // Clear the URL field
    clearLookupValue(DENORMALIZED_FIELDS.recordType);   // Now a lookup, use clearLookupValue
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

/**
 * Clear the Record Type cache (useful for testing or when data changes)
 */
export function clearRecordTypeCache(): void {
    recordTypeCache.clear();
    console.log("[RecordSelectionHandler] Record Type cache cleared");
}

/**
 * Result of auto-detecting a pre-populated regarding field
 */
export interface IDetectedParentContext {
    entityType: string;      // Logical name (e.g., "sprk_matter")
    entityDisplayName: string; // Display name (e.g., "Matter")
    recordId: string;        // GUID of the parent record
    recordName: string;      // Display name of the parent record
    regardingField: string;  // The field that was populated (e.g., "sprk_regardingmatter")
}

/**
 * Detect if any regarding lookup field is pre-populated (from subgrid context)
 *
 * When creating an Event from a parent's subgrid, Dataverse can auto-populate
 * the lookup field via relationship mapping. This function checks all 8
 * entity-specific lookup fields to find which one (if any) is populated.
 *
 * @returns The detected parent context, or null if no parent is detected
 */
export function detectPrePopulatedParent(): IDetectedParentContext | null {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.log("[RecordSelectionHandler] Xrm.Page not available for parent detection");
        return null;
    }

    console.log("[RecordSelectionHandler] Checking for pre-populated regarding fields...");

    // Check each entity-specific lookup field
    for (const config of getEntityConfigs()) {
        try {
            const attr = xrmPage.getAttribute(config.regardingField);
            if (attr) {
                const value = attr.getValue() as Xrm.LookupValue[] | null;
                // Lookup values are arrays with entity references
                if (value && Array.isArray(value) && value.length > 0) {
                    const lookupValue = value[0] as Xrm.LookupValue;
                    if (lookupValue && lookupValue.id) {
                        const detected: IDetectedParentContext = {
                            entityType: config.logicalName,
                            entityDisplayName: config.displayName,
                            recordId: lookupValue.id.replace(/[{}]/g, ''),
                            recordName: lookupValue.name || "",
                            regardingField: config.regardingField
                        };
                        console.log(`[RecordSelectionHandler] Detected pre-populated parent: ${config.displayName} - ${detected.recordName} (${detected.recordId})`);
                        return detected;
                    }
                }
            }
        } catch (error) {
            console.warn(`[RecordSelectionHandler] Error checking ${config.regardingField}:`, error);
        }
    }

    console.log("[RecordSelectionHandler] No pre-populated regarding field detected");
    return null;
}

/**
 * Complete the association for a detected parent context
 *
 * When a parent is auto-detected from a subgrid, this function:
 * 1. Sets the denormalized fields (sprk_regardingrecordtype, sprk_regardingrecordid, sprk_regardingrecordname)
 * 2. Does NOT clear other lookups (only one should be populated from subgrid)
 *
 * @param detectedParent - The detected parent context from detectPrePopulatedParent()
 * @param webApi - WebAPI for Record Type queries
 * @returns Result of the operation
 */
export async function completeAutoDetectedAssociation(
    detectedParent: IDetectedParentContext,
    webApi: ComponentFramework.WebApi
): Promise<IRecordSelectionResult> {
    const result: IRecordSelectionResult = {
        success: false,
        lookupFieldSet: true, // Already set by Dataverse
        denormalizedFieldsSet: false,
        otherLookupsCleared: 0,
        errors: []
    };

    console.log(`[RecordSelectionHandler] Completing auto-detected association: ${detectedParent.entityDisplayName} - ${detectedParent.recordName}`);

    // Set denormalized fields
    let denormalizedSuccess = true;

    // Set record name
    if (!setTextValue(DENORMALIZED_FIELDS.recordName, detectedParent.recordName)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordName}`);
    }

    // Set record ID (GUID without braces)
    if (!setTextValue(DENORMALIZED_FIELDS.recordId, detectedParent.recordId)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordId}`);
    }

    // Set record URL (clickable link to parent record)
    const recordUrl = buildRecordUrl(detectedParent.entityType, detectedParent.recordId);
    if (!setTextValue(DENORMALIZED_FIELDS.recordUrl, recordUrl)) {
        denormalizedSuccess = false;
        result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordUrl}`);
    }

    // Query Record Type and set as lookup
    const recordType = await getRecordTypeByEntityLogicalName(webApi, detectedParent.entityType);
    if (recordType) {
        if (!setLookupValue(DENORMALIZED_FIELDS.recordType, "sprk_recordtype_ref", recordType.id, recordType.name)) {
            denormalizedSuccess = false;
            result.errors.push(`Failed to set ${DENORMALIZED_FIELDS.recordType}`);
        }
    } else {
        denormalizedSuccess = false;
        result.errors.push(`Record Type not found for entity: ${detectedParent.entityType}`);
    }

    result.denormalizedFieldsSet = denormalizedSuccess;
    result.success = denormalizedSuccess;

    console.log(`[RecordSelectionHandler] Auto-detection completion result: success=${result.success}, denormalized=${result.denormalizedFieldsSet}`);

    return result;
}
