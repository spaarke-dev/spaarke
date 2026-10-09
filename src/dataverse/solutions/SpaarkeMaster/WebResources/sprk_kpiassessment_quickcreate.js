/**
 * KPI Assessment Quick Create Form - Post-Save Trigger
 *
 * Web resource registered on the KPI Assessment Quick Create form.
 * After a KPI Assessment record is saved, calls the calculator API
 * to recalculate grades and refreshes the parent Matter or Project form.
 *
 * Web Resource Name: sprk_/scripts/kpiassessment_quickcreate.js
 *
 * Form Events:
 * - OnLoad: Registers the OnPostSave handler via addOnPostSave
 * - OnPostSave: Extracts Matter or Project ID, calls calculator API, refreshes parent
 *
 * Supports both matter-linked and project-linked KPI assessments.
 * Detects which lookup (sprk_matter or sprk_project) is populated and
 * calls the appropriate API endpoint.
 *
 * Constraints:
 * - MUST use addOnPostSave (not addOnSave) to ensure record save completes first
 * - MUST be async/non-blocking: form save MUST NOT be blocked by API failure
 * - MUST NOT throw exceptions that would prevent form close
 * - Retry logic: 3 attempts with exponential backoff (0s, 1s, 2s)
 * - User-friendly error dialog via Xrm.Navigation.openAlertDialog on final failure
 *
 * @see projects/matter-performance-KPI-r1/spec-r1.md (FR-07)
 * @see projects/matter-performance-KPI-r1/tasks/014-create-web-resource-trigger.poml
 */

/* eslint-disable no-undef */
"use strict";

// Namespace for Spaarke KPI Assessment web resource
var Spaarke = Spaarke || {};
Spaarke.KpiAssessment = Spaarke.KpiAssessment || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/**
 * API configuration for the calculator endpoint.
 * Uses environment-based URL detection consistent with DocumentOperations.js pattern.
 */
Spaarke.KpiAssessment.Config = {
    /** @type {string|null} Resolved API base URL (set during onLoad via env var query) */
    apiBaseUrl: null,

    /**
     * Build the recalculate-grades endpoint URL for a given entity.
     * @param {string} entityType - "matter" or "project"
     * @param {string} entityId - The record GUID (without braces)
     * @returns {string} Relative API endpoint path
     */
    recalculateGradesEndpoint: function (entityType, entityId) {
        var basePath = entityType === "project" ? "/api/projects/" : "/api/matters/";
        return basePath + entityId + "/recalculate-grades";
    },

    /** Maximum retry attempts for API calls */
    maxRetryAttempts: 3,

    /** Base delay in milliseconds for exponential backoff */
    retryBaseDelay: 1000,

    /** Version for console logging */
    version: "1.2.0"
};

// =============================================================================
// ENVIRONMENT VARIABLE RESOLUTION
// =============================================================================

/**
 * Cached BFF API base URL (module-level, set after first env var resolution).
 * @type {string|null}
 */
Spaarke.KpiAssessment._cachedApiBaseUrl = null;

/**
 * Resolve the BFF API base URL from Dataverse Environment Variables.
 * Queries environmentvariabledefinition + environmentvariablevalue for
 * "sprk_BffApiBaseUrl". Caches the result module-level so subsequent calls
 * skip the query.
 *
 * @returns {Promise<string>} BFF API base URL
 * @throws {Error} If the environment variable is not configured
 */
Spaarke.KpiAssessment.getApiBaseUrl = function () {
    if (Spaarke.KpiAssessment._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.KpiAssessment._cachedApiBaseUrl);
    }

    var schemaName = "sprk_BffApiBaseUrl";

    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq '" + schemaName + "'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (definitionResult) {
        if (!definitionResult.entities || definitionResult.entities.length === 0) {
            throw new Error(
                '[KPI Assessment] Required environment variable "' + schemaName + '" not found in Dataverse. ' +
                'Ensure it is defined as an Environment Variable Definition in the solution.'
            );
        }

        var definition = definitionResult.entities[0];
        var definitionId = definition.environmentvariabledefinitionid;
        var defaultValue = definition.defaultvalue || null;

        return Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            "?$filter=_environmentvariabledefinitionid_value eq '" + definitionId + "'&$select=value"
        ).then(function (valueResult) {
            var finalValue = null;
            if (valueResult.entities && valueResult.entities.length > 0) {
                finalValue = valueResult.entities[0].value;
            } else {
                finalValue = defaultValue;
            }

            if (!finalValue) {
                throw new Error(
                    '[KPI Assessment] Environment variable "' + schemaName + '" has no value. ' +
                    'Set a default value on the definition or create an Environment Variable Value override.'
                );
            }

            Spaarke.KpiAssessment._cachedApiBaseUrl = finalValue;
            console.log("[KPI Assessment] Resolved BFF URL from env var: " + finalValue);
            return finalValue;
        });
    });
};

// =============================================================================
// FORM EVENT HANDLERS
// =============================================================================

/**
 * OnLoad event handler - registered on Quick Create form load.
 * Registers the OnPostSave handler so it fires after each successful save.
 *
 * Registration in Dataverse:
 *   Event: OnLoad
 *   Library: sprk_/scripts/kpiassessment_quickcreate.js
 *   Function: Spaarke.KpiAssessment.onLoad
 *   Pass execution context: Yes
 *
 * @param {Object} executionContext - The execution context passed by the form
 */
Spaarke.KpiAssessment.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();

        // Resolve API base URL from Dataverse Environment Variables (async)
        Spaarke.KpiAssessment.getApiBaseUrl().then(function (apiBaseUrl) {
            Spaarke.KpiAssessment.Config.apiBaseUrl = apiBaseUrl;

            // Register OnPostSave handler - fires AFTER save completes successfully
            formContext.data.entity.addOnPostSave(Spaarke.KpiAssessment.onPostSave);

            console.log(
                "[KPI Assessment] v" + Spaarke.KpiAssessment.Config.version +
                " loaded. API: " + Spaarke.KpiAssessment.Config.apiBaseUrl
            );
        }).catch(function (error) {
            console.error("[KPI Assessment] Failed to resolve BFF URL from environment variables:", error);
        });
    } catch (error) {
        // Log but do not throw - must not prevent form from loading
        console.error("[KPI Assessment] Error in onLoad:", error);
    }
};

/**
 * OnPostSave event handler - fires after the KPI Assessment record is saved.
 * Detects whether the assessment is linked to a Matter or Project, then calls
 * the appropriate calculator API endpoint to recalculate grades and refreshes
 * the parent form.
 *
 * This function is intentionally fire-and-forget. It MUST NOT block
 * the form close or throw exceptions that prevent the Quick Create
 * form from completing its save lifecycle.
 *
 * @param {Object} executionContext - The execution context passed by the form
 */
Spaarke.KpiAssessment.onPostSave = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();

        // Detect parent entity: check matter first, then project
        var parentInfo = Spaarke.KpiAssessment._getParentInfo(formContext);
        if (!parentInfo) {
            console.warn("[KPI Assessment] No matter or project lookup value found. Skipping recalculation.");
            return;
        }

        console.log("[KPI Assessment] Post-save triggered for " + parentInfo.entityType + ": " + parentInfo.entityId);

        // Fire-and-forget: call the calculator API asynchronously
        // Do NOT await - form close must not be blocked
        Spaarke.KpiAssessment._callCalculatorApi(parentInfo.entityType, parentInfo.entityId);

    } catch (error) {
        // Log but do not throw - must not prevent form from closing
        console.error("[KPI Assessment] Error in onPostSave:", error);
    }
};

/**
 * Determine whether this KPI assessment is linked to a Matter or a Project.
 * Checks sprk_matter first, then sprk_project.
 *
 * @param {Object} formContext - The form context
 * @returns {{ entityType: string, entityId: string }|null} Parent info or null if neither is set
 */
Spaarke.KpiAssessment._getParentInfo = function (formContext) {
    // Check matter lookup first
    var matterAttr = formContext.getAttribute("sprk_matter");
    if (matterAttr) {
        var matterLookup = matterAttr.getValue();
        if (matterLookup && matterLookup[0]) {
            return {
                entityType: "matter",
                entityId: matterLookup[0].id.replace(/[{}]/g, "")
            };
        }
    }

    // Check project lookup
    var projectAttr = formContext.getAttribute("sprk_project");
    if (projectAttr) {
        var projectLookup = projectAttr.getValue();
        if (projectLookup && projectLookup[0]) {
            return {
                entityType: "project",
                entityId: projectLookup[0].id.replace(/[{}]/g, "")
            };
        }
    }

    return null;
};

// =============================================================================
// API CALL
// =============================================================================

/**
 * Call the calculator API to recalculate grades with retry logic.
 * This is intentionally async and fire-and-forget from the caller's perspective.
 * On success, refreshes the parent form to display updated grades.
 * On failure after all retries, shows a user-friendly error dialog.
 *
 * Retry strategy: 3 attempts with exponential backoff (0s, 1s, 2s).
 *
 * @param {string} entityType - "matter" or "project"
 * @param {string} entityId - The record GUID (without braces)
 * @returns {Promise<void>} Resolves when API call completes (success or failure)
 */
Spaarke.KpiAssessment._callCalculatorApi = async function (entityType, entityId) {
    var apiUrl = Spaarke.KpiAssessment.Config.apiBaseUrl +
        Spaarke.KpiAssessment.Config.recalculateGradesEndpoint(entityType, entityId);
    var maxAttempts = Spaarke.KpiAssessment.Config.maxRetryAttempts;
    var baseDelay = Spaarke.KpiAssessment.Config.retryBaseDelay;

    console.log("[KPI Assessment] Calling calculator API: POST " + apiUrl);

    for (var attempt = 1; attempt <= maxAttempts; attempt++) {
        try {
            var response = await fetch(apiUrl, {
                method: "POST",
                credentials: "include",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json"
                }
            });

            if (response.ok) {
                console.log("[KPI Assessment] Calculator API succeeded for " + entityType + ": " + entityId);

                // Refresh the parent form to show updated grades
                Spaarke.KpiAssessment._refreshParentForm(entityType, entityId);
                return;
            }

            // Non-ok response - log details and retry
            var errorBody = null;
            try {
                errorBody = await response.json();
            } catch (parseError) {
                // Response body may not be JSON
                errorBody = { status: response.status, statusText: response.statusText };
            }
            console.warn(
                "[KPI Assessment] Calculator API returned error " + response.status +
                " (attempt " + attempt + " of " + maxAttempts + ", " + entityType + ": " + entityId + "):",
                errorBody
            );

        } catch (error) {
            // Network error, CORS issue, or other fetch failure
            console.warn(
                "[KPI Assessment] Calculator API call failed" +
                " (attempt " + attempt + " of " + maxAttempts + ", " + entityType + ": " + entityId + "):",
                error
            );
        }

        // Wait before next attempt with exponential backoff: 0s, 1s, 2s
        if (attempt < maxAttempts) {
            var delay = baseDelay * (attempt - 1);
            if (delay > 0) {
                console.log("[KPI Assessment] Retrying in " + delay + "ms...");
                await new Promise(function (resolve) { setTimeout(resolve, delay); });
            }
        }
    }

    // All retries exhausted - show user-friendly error dialog
    console.error(
        "[KPI Assessment] All " + maxAttempts + " attempts failed for " + entityType + ": " + entityId +
        ". Showing error dialog."
    );
    Spaarke.KpiAssessment._showErrorDialog(entityId);
};

// =============================================================================
// ERROR DIALOG
// =============================================================================

/**
 * Show a user-friendly error dialog when all API retry attempts have failed.
 * Uses Xrm.Navigation.openAlertDialog to display a non-blocking alert.
 * The dialog is fire-and-forget - its result is not awaited.
 *
 * @param {string} matterId - The Matter record GUID (for logging context)
 */
Spaarke.KpiAssessment._showErrorDialog = function (matterId) {
    try {
        Xrm.Navigation.openAlertDialog({
            confirmButtonLabel: "OK",
            text: "Unable to recalculate grades. Your KPI assessment was saved successfully. Please refresh the form to see updated grades.",
            title: "Grade Recalculation"
        });
        // Note: openAlertDialog returns a Promise but we intentionally do not await it.
        // The dialog is non-blocking and fire-and-forget.
    } catch (error) {
        // If dialog fails (e.g., Xrm not available), log but do not throw
        console.error("[KPI Assessment] Could not show error dialog for matter " + matterId + ":", error);
    }
};

// =============================================================================
// PARENT FORM REFRESH
// =============================================================================

/**
 * Delay in milliseconds after API success before refreshing the parent form.
 * Gives Dataverse time to commit the updated field values.
 */
Spaarke.KpiAssessment._refreshDelayMs = 1500;

/**
 * Refresh the parent form (Matter or Project) to display updated grade values.
 * Waits briefly for Dataverse to commit the API-written values, then attempts
 * multiple refresh strategies in order of reliability.
 *
 * Strategies tried (in order):
 * 1. parent.Xrm.Page.data.refresh - legacy but widely supported in UCI
 * 2. top.Xrm.Page.data.refresh - fallback for nested iframes
 * 3. Xrm.Navigation.openForm re-open - last resort full form reload
 *
 * This is a best-effort operation - failures are logged, never thrown.
 *
 * @param {string} entityType - "matter" or "project" (for fallback reload)
 * @param {string} entityId - The record GUID (for fallback reload)
 */
Spaarke.KpiAssessment._refreshParentForm = function (entityType, entityId) {
    // Wait for Dataverse to commit the updated values before refreshing
    setTimeout(function () {
        Spaarke.KpiAssessment._doRefresh(entityType, entityId);
    }, Spaarke.KpiAssessment._refreshDelayMs);
};

/**
 * Internal: Execute the parent form refresh using available strategies.
 */
Spaarke.KpiAssessment._doRefresh = function (entityType, entityId) {
    try {
        // Strategy 1: parent window's Xrm.Page (legacy but widely supported in UCI)
        if (window.parent && window.parent.Xrm && window.parent.Xrm.Page &&
            window.parent.Xrm.Page.data) {
            window.parent.Xrm.Page.data.refresh(false);
            console.log("[KPI Assessment] Parent form refreshed via parent.Xrm.Page.");
            return;
        }

        // Strategy 2: top-level window (nested iframe scenarios)
        if (window.top && window.top !== window && window.top.Xrm &&
            window.top.Xrm.Page && window.top.Xrm.Page.data) {
            window.top.Xrm.Page.data.refresh(false);
            console.log("[KPI Assessment] Parent form refreshed via top.Xrm.Page.");
            return;
        }

        // Strategy 3: Use Xrm.Navigation.openForm to reload the parent record
        // This is a heavier approach but works when parent Xrm context is inaccessible
        var entityName = entityType === "project" ? "sprk_project" : "sprk_matter";
        var xrmContext = window.parent && window.parent.Xrm ? window.parent.Xrm :
                        (window.top && window.top.Xrm ? window.top.Xrm : null);
        if (xrmContext && xrmContext.Navigation) {
            xrmContext.Navigation.openForm({
                entityName: entityName,
                entityId: entityId,
                openInNewWindow: false
            });
            console.log("[KPI Assessment] Parent form reloaded via Xrm.Navigation.openForm.");
            return;
        }

        console.warn(
            "[KPI Assessment] Could not refresh parent form. " +
            "No Xrm context accessible. User may need to manually refresh."
        );
    } catch (error) {
        // Cross-origin or other access error - log but do not throw
        console.warn("[KPI Assessment] Parent form refresh failed:", error);
    }
};

/* eslint-enable no-undef */
