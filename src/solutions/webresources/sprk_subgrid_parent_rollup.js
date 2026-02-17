/**
 * Generic Subgrid Parent Rollup - Configuration-driven
 *
 * Web Resource Name: sprk_/scripts/subgrid_parent_rollup.js
 *
 * A reusable web resource for any parent-child rollup scenario.
 * Register on any parent entity form's OnLoad event with a JSON
 * configuration string to define the subgrid name, API endpoint,
 * and entity type mapping.
 *
 * Supports multiple subgrids on the same form (each gets its own
 * instance keyed by subgridName).
 *
 * Form Events:
 *   Event: OnLoad
 *   Library: sprk_/scripts/subgrid_parent_rollup.js
 *   Function: Spaarke.SubgridRollup.onLoad
 *   Pass execution context: Yes
 *   Parameters: JSON config string (see below)
 *
 * Config parameter format:
 * {
 *   "subgridName": "subgrid_kpiassessments",
 *   "apiPathTemplate": "/api/{entityPath}/{entityId}/recalculate-grades",
 *   "entityPathMap": { "sprk_matter": "matters", "sprk_project": "projects" },
 *   "refreshDelayMs": 1500
 * }
 *
 * @see .claude/patterns/webresource/subgrid-parent-rollup.md
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.SubgridRollup = Spaarke.SubgridRollup || {};

// =============================================================================
// CONFIGURATION
// =============================================================================

/** Per-subgrid instance state. Keyed by subgridName. */
Spaarke.SubgridRollup._instances = {};

/** Version for console logging. */
Spaarke.SubgridRollup._version = "1.0.0";

// =============================================================================
// ENVIRONMENT DETECTION
// =============================================================================

/**
 * Determine the BFF API base URL based on the current Dataverse environment.
 * @returns {string} BFF API base URL
 */
Spaarke.SubgridRollup._getApiBaseUrl = function () {
    try {
        var clientUrl = Xrm.Utility.getGlobalContext().getClientUrl();
        if (clientUrl.indexOf("spaarkedev1.crm.dynamics.com") !== -1) {
            return "https://spe-api-dev-67e2xz.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeuat.crm.dynamics.com") !== -1) {
            return "https://spaarke-bff-uat.azurewebsites.net";
        } else if (clientUrl.indexOf("spaarkeprod.crm.dynamics.com") !== -1) {
            return "https://spaarke-bff-prod.azurewebsites.net";
        }
        return "https://localhost:5001";
    } catch (e) {
        return "https://spe-api-dev-67e2xz.azurewebsites.net";
    }
};

// =============================================================================
// FORM EVENT HANDLER
// =============================================================================

/**
 * OnLoad event handler. Register on any parent entity form.
 *
 * @param {Object} executionContext - Execution context (pass execution context = yes)
 * @param {string} configJson - JSON configuration string passed as event handler parameter
 */
Spaarke.SubgridRollup.onLoad = function (executionContext, configJson) {
    try {
        var formContext = executionContext.getFormContext();

        // Parse configuration
        var config;
        try {
            config = JSON.parse(configJson || "{}");
        } catch (parseError) {
            console.error("[SubgridRollup] Invalid config JSON:", configJson);
            return;
        }

        // Validate required fields
        if (!config.subgridName || !config.apiPathTemplate) {
            console.error("[SubgridRollup] Config must include subgridName and apiPathTemplate");
            return;
        }

        // Apply defaults
        config.refreshDelayMs = config.refreshDelayMs || 1500;
        config.entityPathMap = config.entityPathMap || {};

        // Resolve entity path for API URL
        var entityName = formContext.data.entity.getEntityName();
        var entityPath = config.entityPathMap[entityName] || entityName;

        // Create instance state (supports multiple subgrids per form)
        var key = config.subgridName;
        Spaarke.SubgridRollup._instances[key] = {
            lastRowCount: -1,
            refreshTimer: null,
            config: config,
            entityPath: entityPath,
            apiBaseUrl: Spaarke.SubgridRollup._getApiBaseUrl()
        };

        // Wait for subgrid to render, then attach listener
        Spaarke.SubgridRollup._waitForSubgrid(formContext, key, 0);

        console.log("[SubgridRollup:" + key + "] v" + Spaarke.SubgridRollup._version +
            " initialized for " + entityName + " (" + entityPath + ")");
    } catch (error) {
        console.error("[SubgridRollup] Error in onLoad:", error);
    }
};

// =============================================================================
// SUBGRID LISTENER
// =============================================================================

/**
 * Wait for the subgrid control to become available, then attach the listener.
 * Retries up to 10 times with 500ms intervals.
 */
Spaarke.SubgridRollup._waitForSubgrid = function (formContext, key, attempt) {
    var inst = Spaarke.SubgridRollup._instances[key];
    var subgrid = formContext.getControl(inst.config.subgridName);

    if (subgrid) {
        // Capture initial row count
        try {
            var grid = subgrid.getGrid();
            if (grid) {
                inst.lastRowCount = grid.getTotalRecordCount();
            }
        } catch (e) {
            // Grid data may not be loaded yet
        }

        // Attach subgrid OnLoad listener
        subgrid.addOnLoad(function () {
            Spaarke.SubgridRollup._onSubgridChange(formContext, subgrid, key);
        });

        console.log("[SubgridRollup:" + key + "] Listener attached. Rows: " + inst.lastRowCount);
        return;
    }

    if (attempt < 10) {
        setTimeout(function () {
            Spaarke.SubgridRollup._waitForSubgrid(formContext, key, attempt + 1);
        }, 500);
    } else {
        console.warn("[SubgridRollup:" + key + "] Subgrid not found after 10 attempts.");
    }
};

/**
 * Called when the subgrid refreshes. Checks if the row count changed
 * and triggers the API call + form refresh if so.
 */
Spaarke.SubgridRollup._onSubgridChange = function (formContext, subgrid, key) {
    var inst = Spaarke.SubgridRollup._instances[key];
    try {
        var currentCount = -1;
        try {
            currentCount = subgrid.getGrid().getTotalRecordCount();
        } catch (e) {
            return; // Grid in loading state
        }

        // Only act on actual data changes (prevents infinite refresh loops)
        if (currentCount !== inst.lastRowCount && currentCount >= 0) {
            console.log("[SubgridRollup:" + key + "] Rows: " +
                inst.lastRowCount + " → " + currentCount);
            inst.lastRowCount = currentCount;

            var entityId = formContext.data.entity.getId().replace(/[{}]/g, "");
            Spaarke.SubgridRollup._callApiAndRefresh(formContext, key, entityId);
        }
    } catch (error) {
        console.warn("[SubgridRollup:" + key + "] Error in subgrid handler:", error);
    }
};

// =============================================================================
// API CALL + FORM REFRESH
// =============================================================================

/**
 * Call the BFF calculator API, then refresh the form data after a delay.
 * Debounces rapid subgrid events.
 */
Spaarke.SubgridRollup._callApiAndRefresh = function (formContext, key, entityId) {
    var inst = Spaarke.SubgridRollup._instances[key];

    // Debounce: cancel any pending operation
    if (inst.refreshTimer) {
        clearTimeout(inst.refreshTimer);
        inst.refreshTimer = null;
    }

    // Build API URL from template
    var apiUrl = inst.apiBaseUrl +
        inst.config.apiPathTemplate
            .replace("{entityPath}", inst.entityPath)
            .replace("{entityId}", entityId);

    console.log("[SubgridRollup:" + key + "] POST " + apiUrl);

    fetch(apiUrl, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "Accept": "application/json"
        }
    }).then(function (response) {
        if (response.ok) {
            console.log("[SubgridRollup:" + key + "] API succeeded. Refreshing in " +
                inst.config.refreshDelayMs + "ms...");

            // Wait for Dataverse to commit, then refresh form data
            inst.refreshTimer = setTimeout(function () {
                inst.refreshTimer = null;
                formContext.data.refresh(false).then(
                    function () {
                        console.log("[SubgridRollup:" + key + "] Form refreshed.");
                    },
                    function (err) {
                        console.warn("[SubgridRollup:" + key + "] Refresh failed:", err);
                    }
                );
            }, inst.config.refreshDelayMs);
        } else {
            console.warn("[SubgridRollup:" + key + "] API returned " + response.status);
        }
    }).catch(function (error) {
        console.warn("[SubgridRollup:" + key + "] API call failed:", error);
    });
};

/* eslint-enable no-undef */
