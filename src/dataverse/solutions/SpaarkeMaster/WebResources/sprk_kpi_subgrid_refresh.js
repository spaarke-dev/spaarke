/**
 * KPI Subgrid Refresh - Subgrid change listener + grade recalculation
 *
 * Web Resource Name: sprk_/scripts/kpi_subgrid_refresh.js
 *
 * Registered on the Matter AND/OR Project main form OnLoad event.
 * Listens for KPI Assessments subgrid refresh events (which fire after
 * a Quick Create save), calls the BFF calculator API to recalculate
 * grades, then refreshes the form data so the Performance Areas
 * grade fields reflect the updated values.
 *
 * Supports both entity types via auto-detection of the current entity name.
 *
 * This is the ONLY web resource needed for the KPI rollup on each form.
 * No Quick Create form web resource is required.
 *
 * Form Events (register on BOTH Matter and Project main forms):
 *   Event: OnLoad
 *   Library: sprk_/scripts/kpi_subgrid_refresh.js
 *   Function: Spaarke.KpiSubgrid.onLoad
 *   Pass execution context: Yes
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.KpiSubgrid = Spaarke.KpiSubgrid || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/**
 * BFF API base URL (resolved from Dataverse Environment Variables on form load).
 * @type {string|null}
 */
Spaarke.KpiSubgrid._apiBaseUrl = null;

/**
 * Cached BFF API base URL (module-level, set after first env var resolution).
 * @type {string|null}
 */
Spaarke.KpiSubgrid._cachedApiBaseUrl = null;

/**
 * Detected entity type: "matter" or "project".
 * @type {string|null}
 */
Spaarke.KpiSubgrid._entityType = null;

/**
 * Delay in ms after API success before refreshing form data.
 * Gives Dataverse time to commit the updated field values.
 */
Spaarke.KpiSubgrid._refreshDelayMs = 1500;

/**
 * Name of the KPI Assessments subgrid control on the form.
 */
Spaarke.KpiSubgrid._subgridName = "subgrid_kpiassessments";

/**
 * Tracks the last known subgrid row count to detect actual data changes
 * vs. spurious OnLoad events.
 */
Spaarke.KpiSubgrid._lastRowCount = -1;

/**
 * Pending refresh timer ID (to debounce multiple rapid subgrid events).
 */
Spaarke.KpiSubgrid._refreshTimer = null;

/**
 * Version for console logging.
 */
Spaarke.KpiSubgrid._version = "1.1.0";

// =============================================================================
// ENVIRONMENT VARIABLE RESOLUTION
// =============================================================================

/**
 * Resolve the BFF API base URL from Dataverse Environment Variables.
 * Queries environmentvariabledefinition + environmentvariablevalue for
 * "sprk_BffApiBaseUrl". Caches the result module-level so subsequent calls
 * skip the query.
 *
 * @returns {Promise<string>} BFF API base URL
 * @throws {Error} If the environment variable is not configured
 */
Spaarke.KpiSubgrid._getApiBaseUrl = function () {
    if (Spaarke.KpiSubgrid._cachedApiBaseUrl) {
        return Promise.resolve(Spaarke.KpiSubgrid._cachedApiBaseUrl);
    }

    var schemaName = "sprk_BffApiBaseUrl";

    return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariabledefinition",
        "?$filter=schemaname eq '" + schemaName + "'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (definitionResult) {
        if (!definitionResult.entities || definitionResult.entities.length === 0) {
            throw new Error(
                '[KPI Subgrid] Required environment variable "' + schemaName + '" not found in Dataverse. ' +
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
                    '[KPI Subgrid] Environment variable "' + schemaName + '" has no value. ' +
                    'Set a default value on the definition or create an Environment Variable Value override.'
                );
            }

            Spaarke.KpiSubgrid._cachedApiBaseUrl = finalValue;
            console.log("[KPI Subgrid] Resolved BFF URL from env var: " + finalValue);
            return finalValue;
        });
    });
};

/**
 * Detect whether this form is for a Matter or a Project.
 * @param {Object} formContext - The form context
 * @returns {string} "matter" or "project"
 */
Spaarke.KpiSubgrid._detectEntityType = function (formContext) {
    var entityName = formContext.data.entity.getEntityName();
    if (entityName === "sprk_project") {
        return "project";
    }
    return "matter";
};

// =============================================================================
// FORM EVENT HANDLER
// =============================================================================

/**
 * OnLoad event handler - registered on the Matter or Project main form.
 *
 * Resolves the API base URL, detects entity type, then waits for the
 * subgrid control to become available and attaches an OnLoad listener
 * that triggers grade recalculation when the subgrid content changes.
 *
 * @param {Object} executionContext - The execution context passed by the form
 */
Spaarke.KpiSubgrid.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();

        // Detect entity type synchronously
        Spaarke.KpiSubgrid._entityType = Spaarke.KpiSubgrid._detectEntityType(formContext);

        // Resolve API base URL from Dataverse Environment Variables (async)
        Spaarke.KpiSubgrid._getApiBaseUrl().then(function (apiBaseUrl) {
            Spaarke.KpiSubgrid._apiBaseUrl = apiBaseUrl;

            // Subgrid may not be rendered yet on form load - retry until available
            Spaarke.KpiSubgrid._waitForSubgrid(formContext, 0);

            console.log("[KPI Subgrid] v" + Spaarke.KpiSubgrid._version +
                " loaded. Entity: " + Spaarke.KpiSubgrid._entityType +
                ", API: " + Spaarke.KpiSubgrid._apiBaseUrl);
        }).catch(function (error) {
            console.error("[KPI Subgrid] Failed to resolve BFF URL from environment variables:", error);
        });
    } catch (error) {
        console.error("[KPI Subgrid] Error in onLoad:", error);
    }
};

// =============================================================================
// SUBGRID LISTENER
// =============================================================================

/**
 * Wait for the subgrid control to become available, then attach the listener.
 * Retries up to 10 times with 500ms intervals (5 seconds total).
 *
 * @param {Object} formContext - The form context
 * @param {number} attempt - Current retry attempt
 */
Spaarke.KpiSubgrid._waitForSubgrid = function (formContext, attempt) {
    var maxAttempts = 10;
    var subgrid = formContext.getControl(Spaarke.KpiSubgrid._subgridName);

    if (subgrid) {
        // Capture initial row count
        try {
            var grid = subgrid.getGrid();
            if (grid) {
                Spaarke.KpiSubgrid._lastRowCount = grid.getTotalRecordCount();
            }
        } catch (e) {
            // Grid data may not be loaded yet
        }

        // Attach subgrid OnLoad listener
        subgrid.addOnLoad(function () {
            Spaarke.KpiSubgrid._onSubgridRefresh(formContext, subgrid);
        });

        console.log("[KPI Subgrid] Subgrid listener attached. Initial rows: " +
            Spaarke.KpiSubgrid._lastRowCount);
        return;
    }

    if (attempt < maxAttempts) {
        setTimeout(function () {
            Spaarke.KpiSubgrid._waitForSubgrid(formContext, attempt + 1);
        }, 500);
    } else {
        console.warn("[KPI Subgrid] Subgrid '" +
            Spaarke.KpiSubgrid._subgridName + "' not found after " + maxAttempts + " attempts.");
    }
};

/**
 * Called when the KPI Assessments subgrid refreshes.
 * Checks if the row count changed (indicating a record was added or removed),
 * then calls the calculator API and schedules a debounced form data refresh.
 *
 * @param {Object} formContext - The main form context
 * @param {Object} subgrid - The subgrid control
 */
Spaarke.KpiSubgrid._onSubgridRefresh = function (formContext, subgrid) {
    try {
        var currentCount = -1;
        try {
            var grid = subgrid.getGrid();
            if (grid) {
                currentCount = grid.getTotalRecordCount();
            }
        } catch (e) {
            // Grid may be in a loading state
        }

        // Only recalculate if row count actually changed (skip spurious events)
        if (currentCount !== Spaarke.KpiSubgrid._lastRowCount && currentCount >= 0) {
            console.log("[KPI Subgrid] Row count changed: " +
                Spaarke.KpiSubgrid._lastRowCount + " → " + currentCount);

            Spaarke.KpiSubgrid._lastRowCount = currentCount;

            // Get the record ID from the current form
            var entityId = formContext.data.entity.getId().replace(/[{}]/g, "");

            // Call the calculator API, then refresh the form
            Spaarke.KpiSubgrid._recalculateAndRefresh(formContext, entityId);
        }
    } catch (error) {
        console.warn("[KPI Subgrid] Error in subgrid refresh handler:", error);
    }
};

// =============================================================================
// CALCULATOR API CALL
// =============================================================================

/**
 * Call the calculator API to recalculate grades, then refresh the form.
 * Debounces rapid calls (e.g., multiple subgrid events in quick succession).
 * Uses the detected entity type to call the correct API endpoint.
 *
 * @param {Object} formContext - The main form context
 * @param {string} entityId - The Matter or Project record GUID
 */
Spaarke.KpiSubgrid._recalculateAndRefresh = function (formContext, entityId) {
    // Debounce: cancel any pending operation
    if (Spaarke.KpiSubgrid._refreshTimer) {
        clearTimeout(Spaarke.KpiSubgrid._refreshTimer);
        Spaarke.KpiSubgrid._refreshTimer = null;
    }

    var entityPath = Spaarke.KpiSubgrid._entityType === "project" ? "projects" : "matters";
    var apiUrl = Spaarke.KpiSubgrid._apiBaseUrl + "/api/" + entityPath + "/" + entityId + "/recalculate-grades";

    console.log("[KPI Subgrid] Calling calculator API: POST " + apiUrl);

    fetch(apiUrl, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/json"
        }
    }).then(function (response) {
        if (response.ok) {
            console.log("[KPI Subgrid] Calculator API succeeded. Refreshing form in " +
                Spaarke.KpiSubgrid._refreshDelayMs + "ms...");

            // Wait for Dataverse to commit, then refresh form data
            Spaarke.KpiSubgrid._refreshTimer = setTimeout(function () {
                Spaarke.KpiSubgrid._refreshTimer = null;
                Spaarke.KpiSubgrid._refreshFormData(formContext);
            }, Spaarke.KpiSubgrid._refreshDelayMs);
        } else {
            console.warn("[KPI Subgrid] Calculator API returned " + response.status);
        }
    }).catch(function (error) {
        console.warn("[KPI Subgrid] Calculator API call failed:", error);
    });
};

// =============================================================================
// FORM REFRESH
// =============================================================================

/**
 * Refresh the form data (not a full page reload).
 * Re-reads all field values from Dataverse, including the
 * 6 grade fields updated by the calculator API.
 *
 * @param {Object} formContext - The main form context
 */
Spaarke.KpiSubgrid._refreshFormData = function (formContext) {
    try {
        formContext.data.refresh(false).then(
            function () {
                console.log("[KPI Subgrid] Form data refreshed successfully.");
            },
            function (error) {
                console.warn("[KPI Subgrid] Form data refresh failed:", error);
            }
        );
    } catch (error) {
        console.warn("[KPI Subgrid] Error refreshing form data:", error);
    }
};

/* eslint-enable no-undef */
