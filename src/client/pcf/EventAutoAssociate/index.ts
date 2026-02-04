/**
 * EventAutoAssociate PCF Control
 *
 * Headless control that auto-populates Event denormalized fields when
 * a regarding lookup is pre-populated from a parent entity's subgrid.
 *
 * When creating an Event from a parent (Matter, Project, etc.) subgrid:
 * 1. Dataverse auto-populates the entity-specific lookup (e.g., sprk_regardingmatter)
 * 2. This control detects that population on init
 * 3. Sets the denormalized fields (sprk_regardingrecordtype, recordname, recordid, recordurl)
 *
 * No UI rendered - fully headless operation.
 *
 * ADR Compliance:
 * - ADR-006: Logic in PCF control code, not Business Rules
 * - ADR-022: No React/Fluent UI dependencies (minimal bundle)
 *
 * @version 1.0.0
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";

// Control version for debugging
const CONTROL_VERSION = "1.0.0";

/**
 * Entity configuration mapping - matches RecordSelectionHandler
 * Loaded dynamically from sprk_recordtype_ref with hardcoded fallback
 */
interface EntityLookupConfig {
    logicalName: string;
    displayName: string;
    regardingField: string;
}

// Hardcoded fallback configs - used only if sprk_recordtype_ref query fails
const FALLBACK_ENTITY_CONFIGS: EntityLookupConfig[] = [
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
    recordType: "sprk_regardingrecordtype",
    recordUrl: "sprk_regardingrecordurl"
};

// Cache for loaded entity configs
let entityConfigs: EntityLookupConfig[] | null = null;

// Cache for Record Type lookups
const recordTypeCache: Map<string, { id: string; name: string }> = new Map();

/**
 * Get Xrm.Page from parent window (PCF runs in iframe)
 */
function getXrmPage(): Xrm.Page | null {
    try {
        const xrm = (window as unknown as { Xrm?: { Page?: Xrm.Page } }).Xrm ||
            (window.parent as unknown as { Xrm?: { Page?: Xrm.Page } })?.Xrm;
        return xrm?.Page || null;
    } catch (error) {
        console.warn("[EventAutoAssociate] Unable to access Xrm.Page:", error);
        return null;
    }
}

/**
 * Build the Dataverse record URL for navigation
 * Generates a fully qualified URL that's portable between environments
 */
function buildRecordUrl(entityLogicalName: string, recordId: string): string {
    const cleanId = recordId.replace(/[{}]/g, '').toLowerCase();

    try {
        const xrm = (window as unknown as { Xrm?: { Utility?: { getGlobalContext?: () => {
            getClientUrl?: () => string;
            getCurrentAppId?: () => string | Promise<string>;
        } } } }).Xrm ||
            (window.parent as unknown as { Xrm?: { Utility?: { getGlobalContext?: () => {
                getClientUrl?: () => string;
                getCurrentAppId?: () => string | Promise<string>;
            } } } })?.Xrm;

        if (xrm?.Utility?.getGlobalContext) {
            const globalContext = xrm.Utility.getGlobalContext();
            const clientUrl = globalContext.getClientUrl?.() || "";

            let appId = "";
            if (globalContext.getCurrentAppId) {
                const appIdResult = globalContext.getCurrentAppId();
                if (typeof appIdResult === "string") {
                    appId = appIdResult;
                } else {
                    appId = getAppIdFromUrl();
                }
            }

            if (!appId) {
                appId = getAppIdFromUrl();
            }

            if (clientUrl) {
                const url = new URL("/main.aspx", clientUrl);
                if (appId) {
                    url.searchParams.set("appid", appId.replace(/[{}]/g, '').toLowerCase());
                }
                url.searchParams.set("pagetype", "entityrecord");
                url.searchParams.set("etn", entityLogicalName);
                url.searchParams.set("id", cleanId);

                return url.toString();
            }
        }
    } catch (error) {
        console.warn("[EventAutoAssociate] Error building record URL, using fallback:", error);
    }

    return `/main.aspx?pagetype=entityrecord&etn=${entityLogicalName}&id=${cleanId}`;
}

/**
 * Extract app ID from current page URL
 */
function getAppIdFromUrl(): string {
    try {
        const urls = [window.location.href];
        try {
            if (window.parent?.location?.href) {
                urls.push(window.parent.location.href);
            }
        } catch {
            // Cross-origin - ignore
        }

        for (const urlStr of urls) {
            const url = new URL(urlStr);
            const appId = url.searchParams.get("appid");
            if (appId) {
                return appId.replace(/[{}]/g, '').toLowerCase();
            }
        }
    } catch {
        // Error parsing URL - ignore
    }
    return "";
}

/**
 * Load entity configurations from sprk_recordtype_ref
 */
async function loadEntityConfigs(webApi: ComponentFramework.WebApi): Promise<EntityLookupConfig[]> {
    if (entityConfigs) {
        return entityConfigs;
    }

    try {
        console.log("[EventAutoAssociate] Loading entity configs from sprk_recordtype_ref...");
        const query = `?$filter=statecode eq 0&$select=sprk_recordtype_refid,sprk_recordlogicalname,sprk_recorddisplayname,sprk_regardingfield&$orderby=sprk_recorddisplayname`;
        const result = await webApi.retrieveMultipleRecords("sprk_recordtype_ref", query);

        if (result.entities && result.entities.length > 0) {
            entityConfigs = result.entities
                .filter((e: Record<string, unknown>) => e.sprk_recordlogicalname && e.sprk_regardingfield)
                .map((e: Record<string, unknown>) => ({
                    logicalName: e.sprk_recordlogicalname as string,
                    displayName: (e.sprk_recorddisplayname || e.sprk_recordlogicalname) as string,
                    regardingField: e.sprk_regardingfield as string
                }));

            console.log(`[EventAutoAssociate] Loaded ${entityConfigs.length} entity configs`);
            return entityConfigs;
        }
    } catch (error) {
        console.warn("[EventAutoAssociate] Error loading entity configs, using fallback:", error);
    }

    entityConfigs = [...FALLBACK_ENTITY_CONFIGS];
    return entityConfigs;
}

/**
 * Get entity configs (sync version - returns cached or fallback)
 */
function getEntityConfigs(): EntityLookupConfig[] {
    return entityConfigs || FALLBACK_ENTITY_CONFIGS;
}

/**
 * Query Record Type entity by entity logical name
 */
async function getRecordTypeByEntityLogicalName(
    webApi: ComponentFramework.WebApi,
    entityLogicalName: string
): Promise<{ id: string; name: string } | null> {
    const cached = recordTypeCache.get(entityLogicalName);
    if (cached) {
        return cached;
    }

    try {
        const query = `?$filter=sprk_recordlogicalname eq '${entityLogicalName}' and statecode eq 0&$select=sprk_recordtype_refid,sprk_recorddisplayname`;
        const result = await webApi.retrieveMultipleRecords("sprk_recordtype_ref", query);

        if (result.entities && result.entities.length > 0) {
            const recordType = result.entities[0];
            const id = recordType.sprk_recordtype_refid as string;
            const name = recordType.sprk_recorddisplayname as string;

            recordTypeCache.set(entityLogicalName, { id, name });
            console.log(`[EventAutoAssociate] Found Record Type for ${entityLogicalName}: ${name}`);
            return { id, name };
        }
    } catch (error) {
        console.error(`[EventAutoAssociate] Error querying Record Type for ${entityLogicalName}:`, error);
    }

    return null;
}

/**
 * Set a lookup field value
 */
function setLookupValue(
    fieldName: string,
    entityType: string,
    id: string,
    name: string
): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            const formattedId = id.replace(/[{}]/g, '');
            attr.setValue([{
                id: formattedId,
                name: name,
                entityType: entityType
            }]);
            console.log(`[EventAutoAssociate] Set ${fieldName} to ${name}`);
            return true;
        }
    } catch (error) {
        console.error(`[EventAutoAssociate] Error setting ${fieldName}:`, error);
    }
    return false;
}

/**
 * Set a text field value
 */
function setTextValue(fieldName: string, value: string | null): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        return false;
    }

    try {
        const attr = xrmPage.getAttribute(fieldName);
        if (attr) {
            attr.setValue(value);
            console.log(`[EventAutoAssociate] Set ${fieldName} to "${value}"`);
            return true;
        }
    } catch (error) {
        console.error(`[EventAutoAssociate] Error setting ${fieldName}:`, error);
    }
    return false;
}

/**
 * Detected parent context
 */
interface DetectedParentContext {
    entityType: string;
    entityDisplayName: string;
    recordId: string;
    recordName: string;
    regardingField: string;
}

/**
 * Detect if any regarding lookup field is pre-populated
 */
function detectPrePopulatedParent(): DetectedParentContext | null {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        console.log("[EventAutoAssociate] Xrm.Page not available for parent detection");
        return null;
    }

    console.log("[EventAutoAssociate] Checking for pre-populated regarding fields...");

    for (const config of getEntityConfigs()) {
        try {
            const attr = xrmPage.getAttribute(config.regardingField);
            if (attr) {
                const value = attr.getValue() as Xrm.LookupValue[] | null;
                if (value && Array.isArray(value) && value.length > 0) {
                    const lookupValue = value[0];
                    if (lookupValue && lookupValue.id) {
                        const detected: DetectedParentContext = {
                            entityType: config.logicalName,
                            entityDisplayName: config.displayName,
                            recordId: lookupValue.id.replace(/[{}]/g, ''),
                            recordName: lookupValue.name || "",
                            regardingField: config.regardingField
                        };
                        console.log(`[EventAutoAssociate] Detected parent: ${config.displayName} - ${detected.recordName}`);
                        return detected;
                    }
                }
            }
        } catch (error) {
            console.warn(`[EventAutoAssociate] Error checking ${config.regardingField}:`, error);
        }
    }

    console.log("[EventAutoAssociate] No pre-populated regarding field detected");
    return null;
}

/**
 * Check if denormalized fields are already populated
 */
function areDenormalizedFieldsPopulated(): boolean {
    const xrmPage = getXrmPage();
    if (!xrmPage) {
        return false;
    }

    try {
        // Check if record name or record type is already set
        const recordNameAttr = xrmPage.getAttribute(DENORMALIZED_FIELDS.recordName);
        const recordTypeAttr = xrmPage.getAttribute(DENORMALIZED_FIELDS.recordType);

        if (recordNameAttr) {
            const value = recordNameAttr.getValue();
            if (value && typeof value === "string" && value.trim().length > 0) {
                console.log("[EventAutoAssociate] Denormalized fields already populated");
                return true;
            }
        }

        if (recordTypeAttr) {
            const value = recordTypeAttr.getValue() as Xrm.LookupValue[] | null;
            if (value && Array.isArray(value) && value.length > 0) {
                console.log("[EventAutoAssociate] Denormalized fields already populated (type lookup set)");
                return true;
            }
        }
    } catch (error) {
        console.warn("[EventAutoAssociate] Error checking denormalized fields:", error);
    }

    return false;
}

/**
 * Complete the association for a detected parent context
 */
async function completeAutoDetectedAssociation(
    detectedParent: DetectedParentContext,
    webApi: ComponentFramework.WebApi
): Promise<boolean> {
    console.log(`[EventAutoAssociate] Completing association for: ${detectedParent.entityDisplayName} - ${detectedParent.recordName}`);

    let success = true;

    // Set record name
    if (!setTextValue(DENORMALIZED_FIELDS.recordName, detectedParent.recordName)) {
        success = false;
    }

    // Set record ID
    if (!setTextValue(DENORMALIZED_FIELDS.recordId, detectedParent.recordId)) {
        success = false;
    }

    // Set record URL
    const recordUrl = buildRecordUrl(detectedParent.entityType, detectedParent.recordId);
    if (!setTextValue(DENORMALIZED_FIELDS.recordUrl, recordUrl)) {
        success = false;
    }

    // Query and set Record Type lookup
    const recordType = await getRecordTypeByEntityLogicalName(webApi, detectedParent.entityType);
    if (recordType) {
        if (!setLookupValue(DENORMALIZED_FIELDS.recordType, "sprk_recordtype_ref", recordType.id, recordType.name)) {
            success = false;
        }
    } else {
        console.warn(`[EventAutoAssociate] Record Type not found for entity: ${detectedParent.entityType}`);
        success = false;
    }

    console.log(`[EventAutoAssociate] Association completed: success=${success}`);
    return success;
}

/**
 * EventAutoAssociate PCF Control
 *
 * Headless control - renders nothing visible but runs auto-detection logic on init.
 */
export class EventAutoAssociate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement | null = null;
    private context: ComponentFramework.Context<IInputs> | null = null;
    private hasRun: boolean = false;

    constructor() {
        // Constructor
    }

    /**
     * Initialize the control - main entry point
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.context = context;
        this.container = container;

        // Render hidden container
        container.innerHTML = `<div style="display:none" data-control="EventAutoAssociate" data-version="${CONTROL_VERSION}"></div>`;

        console.log(`[EventAutoAssociate] v${CONTROL_VERSION} initialized`);

        // Run auto-detection asynchronously
        this.runAutoDetection();
    }

    /**
     * Run the auto-detection and field population logic
     */
    private async runAutoDetection(): Promise<void> {
        // Only run once per form load
        if (this.hasRun) {
            console.log("[EventAutoAssociate] Already ran, skipping");
            return;
        }
        this.hasRun = true;

        if (!this.context) {
            console.warn("[EventAutoAssociate] Context not available");
            return;
        }

        const webApi = this.context.webAPI;

        // Small delay to ensure form is fully loaded
        await new Promise(resolve => setTimeout(resolve, 100));

        // Check if denormalized fields are already populated
        if (areDenormalizedFieldsPopulated()) {
            console.log("[EventAutoAssociate] Denormalized fields already set, no action needed");
            return;
        }

        // Load entity configs (async)
        await loadEntityConfigs(webApi);

        // Detect pre-populated parent
        const detectedParent = detectPrePopulatedParent();
        if (detectedParent) {
            // Complete the association by setting denormalized fields
            await completeAutoDetectedAssociation(detectedParent, webApi);
        } else {
            console.log("[EventAutoAssociate] No parent detected, no action needed");
        }
    }

    /**
     * Update view - no-op for headless control
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        // No UI to update
    }

    /**
     * Get output values - none for this control
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Cleanup on destroy
     */
    public destroy(): void {
        if (this.container) {
            this.container.innerHTML = "";
            this.container = null;
        }
        this.context = null;
    }
}
