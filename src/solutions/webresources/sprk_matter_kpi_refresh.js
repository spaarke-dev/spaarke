/**
 * Matter KPI Refresh - Subgrid change listener + grade recalculation
 *
 * Web Resource Name: sprk_/scripts/matter_kpi_refresh.js
 *
 * Registered on the Matter main form OnLoad event.
 * Listens for KPI Assessments subgrid refresh events (which fire after
 * a Quick Create save), calls the BFF calculator API to recalculate
 * grades, then refreshes the form data so the Performance Areas
 * grade fields reflect the updated values.
 *
 * This is the ONLY web resource needed for the KPI rollup. It replaces
 * the Quick Create form trigger (sprk_kpiassessment_quickcreate.js)
 * by handling both the API call and the form refresh from the main
 * form context.
 *
 * Form Events:
 *   Event: OnLoad
 *   Library: sprk_/scripts/matter_kpi_refresh.js
 *   Function: Spaarke.MatterKpi.onLoad
 *   Pass execution context: Yes
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.MatterKpi = Spaarke.MatterKpi || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/**
 * BFF API base URL (resolved on form load based on Dataverse environment).
 * @type {string|null}
 */
Spaarke.MatterKpi._apiBaseUrl = null;

/**
 * Delay in ms after API success before refreshing form data.
 * Gives Dataverse time to commit the updated field values.
 */
Spaarke.MatterKpi._refreshDelayMs = 1500;

/**
 * Name of the KPI Assessments subgrid control on the Matter form.
 */
Spaarke.MatterKpi._subgridName = "subgrid_kpiassessments";

/**
 * Tracks the last known subgrid row count to detect actual data changes
 * vs. spurious OnLoad events.
 */
Spaarke.MatterKpi._lastRowCount = -1;

/**
 * Pending refresh timer ID (to debounce multiple rapid subgrid events).
 */
Spaarke.MatterKpi._refreshTimer = null;

/**
 * Version for console logging.
 */
Spaarke.MatterKpi._version = "1.0.0";

// =============================================================================
// ENVIRONMENT DETECTION
// =============================================================================

/**
 * Determine the BFF API base URL based on the current Dataverse environment.
 * @returns {string} BFF API base URL
 */
Spaarke.MatterKpi._getApiBaseUrl = function () {
    try {
        var globalContext = Xrm.Utility.getGlobalContext();
        var clientUrl = globalContext.getClientUrl();

        if (clientUrl.indexOf("spaarkedev1.crm.dynamics.com") !== -1) {
            return "https://spe-api-dev-67e2xz.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeuat.crm.dynamics.com") !== -1) {
            return "https://spaarke-bff-uat.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeprod.crm.dynamics.com") !== -1) {
            return "https://spaarke-bff-prod.azurewebsites.net";
        } else {
            return "https://localhost:5001";
        }
    } catch (error) {
        console.error("[Matter KPI] Error determining API base URL:", error);
        return "https://spe-api-dev-67e2xz.azurewebsites.net";
    }
};

// =============================================================================
// FORM EVENT HANDLER
// =============================================================================

/**
 * OnLoad event handler - registered on the Matter main form.
 *
 * Resolves the API base URL, then waits for the subgrid control to
 * become available and attaches an OnLoad listener that triggers
 * grade recalculation when the subgrid content changes.
 *
 * @param {Object} executionContext - The execution context passed by the form
 */
Spaarke.MatterKpi.onLoad = function (executionContext) {
    try {
        var formContext = executionContext.getFormContext();

        // Resolve API base URL
        Spaarke.MatterKpi._apiBaseUrl = Spaarke.MatterKpi._getApiBaseUrl();

        // Subgrid may not be rendered yet on form load - retry until available
        Spaarke.MatterKpi._waitForSubgrid(formContext, 0);

        console.log("[Matter KPI] v" + Spaarke.MatterKpi._version +
            " loaded. API: " + Spaarke.MatterKpi._apiBaseUrl);
    } catch (error) {
        console.error("[Matter KPI] Error in onLoad:", error);
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
Spaarke.MatterKpi._waitForSubgrid = function (formContext, attempt) {
    var maxAttempts = 10;
    var subgrid = formContext.getControl(Spaarke.MatterKpi._subgridName);

    if (subgrid) {
        // Capture initial row count
        try {
            var grid = subgrid.getGrid();
            if (grid) {
                Spaarke.MatterKpi._lastRowCount = grid.getTotalRecordCount();
            }
        } catch (e) {
            // Grid data may not be loaded yet
        }

        // Attach subgrid OnLoad listener
        subgrid.addOnLoad(function () {
            Spaarke.MatterKpi._onSubgridRefresh(formContext, subgrid);
        });

        console.log("[Matter KPI] Subgrid listener attached. Initial rows: " +
            Spaarke.MatterKpi._lastRowCount);
        return;
    }

    if (attempt < maxAttempts) {
        setTimeout(function () {
            Spaarke.MatterKpi._waitForSubgrid(formContext, attempt + 1);
        }, 500);
    } else {
        console.warn("[Matter KPI] Subgrid '" +
            Spaarke.MatterKpi._subgridName + "' not found after " + maxAttempts + " attempts.");
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
Spaarke.MatterKpi._onSubgridRefresh = function (formContext, subgrid) {
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
        if (currentCount !== Spaarke.MatterKpi._lastRowCount && currentCount >= 0) {
            console.log("[Matter KPI] Subgrid row count changed: " +
                Spaarke.MatterKpi._lastRowCount + " → " + currentCount);

            Spaarke.MatterKpi._lastRowCount = currentCount;

            // Get the matter ID from the current form
            var matterId = formContext.data.entity.getId().replace(/[{}]/g, "");

            // Call the calculator API, then refresh the form
            Spaarke.MatterKpi._recalculateAndRefresh(formContext, matterId);
        }
    } catch (error) {
        console.warn("[Matter KPI] Error in subgrid refresh handler:", error);
    }
};

// =============================================================================
// CALCULATOR API CALL
// =============================================================================

/**
 * Call the calculator API to recalculate grades, then refresh the form.
 * Debounces rapid calls (e.g., multiple subgrid events in quick succession).
 *
 * @param {Object} formContext - The main form context
 * @param {string} matterId - The Matter record GUID
 */
Spaarke.MatterKpi._recalculateAndRefresh = function (formContext, matterId) {
    // Debounce: cancel any pending operation
    if (Spaarke.MatterKpi._refreshTimer) {
        clearTimeout(Spaarke.MatterKpi._refreshTimer);
        Spaarke.MatterKpi._refreshTimer = null;
    }

    var apiUrl = Spaarke.MatterKpi._apiBaseUrl + "/api/matters/" + matterId + "/recalculate-grades";

    console.log("[Matter KPI] Calling calculator API: POST " + apiUrl);

    fetch(apiUrl, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/json"
        }
    }).then(function (response) {
        if (response.ok) {
            console.log("[Matter KPI] Calculator API succeeded. Refreshing form in " +
                Spaarke.MatterKpi._refreshDelayMs + "ms...");

            // Wait for Dataverse to commit, then refresh form data
            Spaarke.MatterKpi._refreshTimer = setTimeout(function () {
                Spaarke.MatterKpi._refreshTimer = null;
                Spaarke.MatterKpi._refreshFormData(formContext);
            }, Spaarke.MatterKpi._refreshDelayMs);
        } else {
            console.warn("[Matter KPI] Calculator API returned " + response.status);
        }
    }).catch(function (error) {
        console.warn("[Matter KPI] Calculator API call failed:", error);
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
Spaarke.MatterKpi._refreshFormData = function (formContext) {
    try {
        formContext.data.refresh(false).then(
            function () {
                console.log("[Matter KPI] Form data refreshed successfully.");
            },
            function (error) {
                console.warn("[Matter KPI] Form data refresh failed:", error);
            }
        );
    } catch (error) {
        console.warn("[Matter KPI] Error refreshing form data:", error);
    }
};

/* eslint-enable no-undef */
