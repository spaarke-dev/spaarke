/**
 * AnalysisWorkspace Code Page Launcher Script
 *
 * Opens the AnalysisWorkspace Code Page as a near-full-screen dialog via
 * Xrm.Navigation.navigateTo({ pageType: "webresource" }). Replaces the
 * legacy Custom Page navigation in sprk_analysis_commands.js.
 *
 * Deployment: Dataverse web resource sprk_/scripts/analysisWorkspaceLauncher.js
 * Web Resource Name: sprk_AnalysisWorkspace (HTML Code Page)
 *
 * URL parameters passed to Code Page:
 *   analysisId  - GUID of the sprk_analysis record (required)
 *   documentId  - GUID of the source sprk_document record (optional)
 *   tenantId    - Azure AD Tenant ID from Xrm.Utility.getGlobalContext() (optional)
 *
 * Security:
 *   - NO auth tokens or sensitive data in URL parameters (ADR-008)
 *   - Code Page acquires its own Bearer tokens via Xrm.Utility.getGlobalContext()
 *   - Only record GUIDs and tenant ID are passed
 *
 * @version 1.0.0
 * @namespace Spaarke.AnalysisWorkspace
 * @see ADR-006 - navigateTo webresource for standalone pages (not custom pages)
 * @see ADR-008 - Independent auth per Code Page
 */

// ============================================================================
// Namespace Setup
// ============================================================================

var Spaarke = window.Spaarke || {};
Spaarke.AnalysisWorkspace = Spaarke.AnalysisWorkspace || {};

(function (ns) {
    "use strict";

    // ========================================================================
    // Constants
    // ========================================================================

    /** Web resource name for the AnalysisWorkspace Code Page */
    var WEB_RESOURCE_NAME = "sprk_AnalysisWorkspace";

    /** Log prefix for console output */
    var LOG_PREFIX = "[Spaarke.AnalysisWorkspace]";

    /** Empty GUID constant for new/unsaved record detection */
    var EMPTY_GUID = "00000000-0000-0000-0000-000000000000";

    /** Dialog dimensions -- 95% for near-full-screen experience */
    var DIALOG_WIDTH = { value: 95, unit: "%" };
    var DIALOG_HEIGHT = { value: 95, unit: "%" };

    /**
     * Logical name of the source document lookup field on the sprk_analysis form.
     * This is the lookup to sprk_document that links an analysis to its parent document.
     */
    var SOURCE_DOCUMENT_FIELD = "sprk_sourcedocumentid";

    // ========================================================================
    // Helper Functions
    // ========================================================================

    /**
     * Strip curly braces from a Dataverse GUID and lowercase.
     *
     * @param {string} guid - GUID string, possibly wrapped in braces
     * @returns {string} Clean GUID without braces, lowercased
     */
    function cleanGuid(guid) {
        if (!guid) return "";
        return guid.replace(/[{}]/g, "").toLowerCase();
    }

    /**
     * Check if a GUID represents an unsaved/new record.
     *
     * @param {string} guid - GUID to check (already cleaned)
     * @returns {boolean} True if GUID is empty or represents a new record
     */
    function isNewRecord(guid) {
        return !guid || guid === "" || guid === EMPTY_GUID;
    }

    /**
     * Get a form attribute value safely.
     *
     * @param {object} formContext - Dataverse form context
     * @param {string} attributeName - Logical name of the attribute
     * @returns {*} Attribute value or null
     */
    function getAttributeValue(formContext, attributeName) {
        try {
            var attribute = formContext.getAttribute(attributeName);
            if (attribute) {
                var value = attribute.getValue();
                if (value !== null && value !== undefined) {
                    // Handle lookup fields (array of { id, name, entityType })
                    if (Array.isArray(value) && value.length > 0) {
                        return value[0].id || null;
                    }
                    return value;
                }
            }
        } catch (e) {
            console.warn(LOG_PREFIX, "Could not get attribute '" + attributeName + "':", e.message);
        }
        return null;
    }

    /**
     * Get the Azure AD Tenant ID from the Xrm global context.
     *
     * Reads organizationSettings.tenantId, which is available in all
     * Dataverse model-driven app contexts.
     *
     * @returns {string} Tenant ID or empty string if unavailable
     */
    function getTenantId() {
        try {
            var globalContext = Xrm.Utility.getGlobalContext();
            if (globalContext && globalContext.organizationSettings &&
                globalContext.organizationSettings.tenantId) {
                return cleanGuid(globalContext.organizationSettings.tenantId);
            }
        } catch (e) {
            console.warn(LOG_PREFIX, "Could not get tenantId from global context:", e.message);
        }
        return "";
    }

    /**
     * Show an alert dialog to the user via Xrm.Navigation.openAlertDialog.
     *
     * @param {string} title - Dialog title
     * @param {string} message - Dialog message text
     */
    function showAlert(title, message) {
        try {
            Xrm.Navigation.openAlertDialog({
                title: title,
                text: message
            });
        } catch (e) {
            // Last resort: browser alert
            console.error(LOG_PREFIX, "Could not show Xrm alert, falling back:", e);
            alert(title + "\n\n" + message); // eslint-disable-line no-alert
        }
    }

    /**
     * Build the data query string for the Code Page web resource.
     * Only includes non-empty values.
     *
     * @param {string} analysisId - Analysis record GUID
     * @param {string} documentId - Source document GUID (may be empty)
     * @param {string} tenantId - Azure AD Tenant ID (may be empty)
     * @returns {string} URL-encoded query string for navigateTo data parameter
     */
    function buildDataParams(analysisId, documentId, tenantId) {
        var parts = [];
        if (analysisId) parts.push("analysisId=" + encodeURIComponent(analysisId));
        if (documentId) parts.push("documentId=" + encodeURIComponent(documentId));
        if (tenantId) parts.push("tenantId=" + encodeURIComponent(tenantId));
        return parts.join("&");
    }

    // ========================================================================
    // Main Launcher Function
    // ========================================================================

    /**
     * Open the AnalysisWorkspace Code Page as a near-full-screen dialog.
     *
     * This function is designed to be called from:
     *   - A form onLoad event handler on the sprk_analysis entity form
     *   - A ribbon/command bar button on the sprk_analysis entity form
     *   - Programmatic invocation from other form scripts
     *
     * Flow:
     *   1. Extract analysisId from the current form record
     *   2. If record is unsaved, prompt user to save first
     *   3. Extract documentId from the source document lookup field
     *   4. Extract tenantId from Xrm global context
     *   5. Open Code Page dialog via navigateTo
     *
     * @param {object} executionContext - Execution context from form event or
     *   primaryControl from ribbon CrmParameter
     */
    ns.openAnalysisWorkspace = function (executionContext) {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "openAnalysisWorkspace: Starting v1.0.0");
        console.log(LOG_PREFIX, "========================================");

        try {
            // -----------------------------------------------------------------
            // Step 1: Get form context
            // -----------------------------------------------------------------
            var formContext;
            if (executionContext && typeof executionContext.getFormContext === "function") {
                // Called from form event (onLoad, onChange) -- executionContext wraps formContext
                formContext = executionContext.getFormContext();
            } else if (executionContext && executionContext.data && executionContext.data.entity) {
                // Called from ribbon with PrimaryControl -- executionContext IS formContext
                formContext = executionContext;
            } else {
                console.error(LOG_PREFIX, "Invalid execution context:", executionContext);
                showAlert(
                    "Analysis Workspace",
                    "Unable to access form context. Please refresh the page and try again."
                );
                return;
            }

            // -----------------------------------------------------------------
            // Step 2: Extract and validate analysisId
            // -----------------------------------------------------------------
            var rawAnalysisId = formContext.data.entity.getId();
            var analysisId = cleanGuid(rawAnalysisId);

            console.log(LOG_PREFIX, "analysisId:", analysisId);

            if (isNewRecord(analysisId)) {
                console.warn(LOG_PREFIX, "Record is unsaved, prompting user to save first");
                showAlert(
                    "Analysis Workspace",
                    "Please save the record before opening the Analysis Workspace.\n\n" +
                    "The analysis record must be saved so the workspace can load its content."
                );
                return;
            }

            // -----------------------------------------------------------------
            // Step 3: Extract documentId from source document lookup
            // -----------------------------------------------------------------
            var rawDocumentId = getAttributeValue(formContext, SOURCE_DOCUMENT_FIELD);
            var documentId = cleanGuid(rawDocumentId);

            if (!documentId) {
                console.info(LOG_PREFIX, "No source document linked -- opening without viewer context");
            } else {
                console.log(LOG_PREFIX, "documentId:", documentId);
            }

            // -----------------------------------------------------------------
            // Step 4: Extract tenantId from global context
            // -----------------------------------------------------------------
            var tenantId = getTenantId();
            console.log(LOG_PREFIX, "tenantId:", tenantId);

            // -----------------------------------------------------------------
            // Step 5: Navigate to Code Page dialog
            // -----------------------------------------------------------------
            var dataParams = buildDataParams(analysisId, documentId, tenantId);
            console.log(LOG_PREFIX, "dataParams:", dataParams);

            var pageInput = {
                pageType: "webresource",
                webresourceName: WEB_RESOURCE_NAME,
                data: dataParams
            };

            var navigationOptions = {
                target: 2,  // Dialog (not full page)
                width: DIALOG_WIDTH,
                height: DIALOG_HEIGHT
            };

            console.log(LOG_PREFIX, "Navigating to:", pageInput);
            console.log(LOG_PREFIX, "Options:", navigationOptions);

            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function success() {
                    console.log(LOG_PREFIX, "Dialog closed successfully");
                    // Refresh the form to pick up any changes made in the workspace
                    try {
                        if (formContext.data && typeof formContext.data.refresh === "function") {
                            formContext.data.refresh(false); // false = don't save before refresh
                            console.log(LOG_PREFIX, "Form data refreshed");
                        }
                    } catch (refreshError) {
                        console.warn(LOG_PREFIX, "Form refresh failed (non-critical):", refreshError.message);
                    }
                },
                function error(err) {
                    // errorCode 2 means user closed the dialog -- not a real error
                    if (err && err.errorCode === 2) {
                        console.log(LOG_PREFIX, "Dialog closed by user");
                        return;
                    }

                    var errorMessage = (err && err.message) ? err.message : "Unknown error";
                    console.error(LOG_PREFIX, "navigateTo error:", err);
                    showAlert(
                        "Analysis Workspace",
                        "Unable to open the Analysis Workspace.\n\n" +
                        "Error: " + errorMessage + "\n\n" +
                        "Please try again. If the problem persists, contact your administrator."
                    );
                }
            );

        } catch (error) {
            var msg = (error && error.message) ? error.message : String(error);
            console.error(LOG_PREFIX, "Unexpected error:", error);
            showAlert(
                "Analysis Workspace",
                "An unexpected error occurred while opening the Analysis Workspace.\n\n" +
                "Error: " + msg
            );
        }
    };

    // ========================================================================
    // Alternative Entry Point: Open by Analysis ID (programmatic)
    // ========================================================================

    /**
     * Open the AnalysisWorkspace Code Page by providing IDs directly.
     *
     * Use this when you already have the analysis and document IDs
     * (e.g., from a subgrid selection or another script).
     *
     * @param {string} analysisId - Analysis record GUID (required)
     * @param {string} [documentId] - Source document GUID (optional)
     */
    ns.openById = function (analysisId, documentId) {
        console.log(LOG_PREFIX, "openById: analysisId=" + analysisId + ", documentId=" + documentId);

        if (!analysisId || isNewRecord(cleanGuid(analysisId))) {
            showAlert(
                "Analysis Workspace",
                "Please select a valid analysis record to open."
            );
            return;
        }

        var cleanAnalysisId = cleanGuid(analysisId);
        var cleanDocumentId = cleanGuid(documentId);
        var tenantId = getTenantId();
        var dataParams = buildDataParams(cleanAnalysisId, cleanDocumentId, tenantId);

        var pageInput = {
            pageType: "webresource",
            webresourceName: WEB_RESOURCE_NAME,
            data: dataParams
        };

        var navigationOptions = {
            target: 2,
            width: DIALOG_WIDTH,
            height: DIALOG_HEIGHT
        };

        console.log(LOG_PREFIX, "openById navigating:", pageInput);

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success() {
                console.log(LOG_PREFIX, "openById dialog closed successfully");
            },
            function error(err) {
                if (err && err.errorCode === 2) {
                    console.log(LOG_PREFIX, "openById dialog closed by user");
                    return;
                }
                var errorMessage = (err && err.message) ? err.message : "Unknown error";
                console.error(LOG_PREFIX, "openById navigateTo error:", err);
                showAlert(
                    "Analysis Workspace",
                    "Unable to open the Analysis Workspace.\n\nError: " + errorMessage
                );
            }
        );
    };

    // ========================================================================
    // Enable / Visibility Rules (for ribbon configuration)
    // ========================================================================

    /**
     * Enable rule: Only enable the "Open Workspace" button when the
     * analysis record is saved (has a valid ID).
     *
     * @param {object} primaryControl - Form context from ribbon CrmParameter
     * @returns {boolean} True if the button should be enabled
     */
    ns.enableOpenWorkspace = function (primaryControl) {
        try {
            if (!primaryControl || !primaryControl.data || !primaryControl.data.entity) {
                return false;
            }

            var recordId = cleanGuid(primaryControl.data.entity.getId());
            return !isNewRecord(recordId);
        } catch (error) {
            console.error(LOG_PREFIX, "enableOpenWorkspace error:", error);
            return false;
        }
    };

    /**
     * Visibility rule: Always show the "Open Workspace" button on Analysis forms.
     *
     * @returns {boolean} True to show the button
     */
    ns.showOpenWorkspace = function () {
        return true;
    };

})(Spaarke.AnalysisWorkspace);

// ============================================================================
// DEPLOYMENT NOTES
// ============================================================================

/*
RIBBON CONFIGURATION:

================================================================================
FORM COMMAND BAR (sprk_analysis entity)
================================================================================

Location: Mscrm.Form.sprk_analysis.MainTab.Actions.Controls._children

Button: "Open Workspace"
  - Command ID: Spaarke.AnalysisWorkspace.Open.Command
  - Function: Spaarke.AnalysisWorkspace.openAnalysisWorkspace
  - Library: $webresource:sprk_/scripts/analysisWorkspaceLauncher.js
  - CrmParameter: PrimaryControl
  - Enable Rule: Spaarke.AnalysisWorkspace.enableOpenWorkspace (same library)
    - CrmParameter: PrimaryControl
  - Visibility Rule: Spaarke.AnalysisWorkspace.showOpenWorkspace (same library)
  - Label: "Open Workspace"
  - Tooltip: "Open the Analysis Workspace to view and edit this analysis"

================================================================================
FORM EVENT (alternative to ribbon button)
================================================================================

Entity: sprk_analysis
Form: Main Form (Information)
Event: OnLoad

  - Library: sprk_/scripts/analysisWorkspaceLauncher.js
  - Function: Spaarke.AnalysisWorkspace.openAnalysisWorkspace
  - Pass execution context: Yes (checkbox)

Note: If using form OnLoad, the workspace dialog opens automatically when the
      form loads. This is useful for a "workspace-first" UX where the form
      itself is minimal and the real work happens in the Code Page dialog.

================================================================================
SUBGRID / EXTERNAL INVOCATION
================================================================================

From other scripts (e.g., sprk_analysis_commands.js subgrid row click):

  if (window.Spaarke && window.Spaarke.AnalysisWorkspace) {
      window.Spaarke.AnalysisWorkspace.openById(analysisId, documentId);
  }

================================================================================
MIGRATION FROM CUSTOM PAGE (sprk_analysis_commands.js)
================================================================================

The existing Spaarke_OpenAnalysisWorkspace() function in sprk_analysis_commands.js
navigates to a Custom Page (pageType: "custom", name: "sprk_analysisworkspace_8bc0b").

To migrate:
  1. Update ribbon command to call Spaarke.AnalysisWorkspace.openAnalysisWorkspace
     instead of Spaarke_OpenAnalysisWorkspace
  2. Update library reference to sprk_/scripts/analysisWorkspaceLauncher.js
  3. The existing sprk_analysis_commands.js can remain for other commands
     (NewAnalysis, NewAnalysisFromSubgrid) that use the Analysis Builder dialog

================================================================================
WEB RESOURCE REGISTRATION
================================================================================

Name: sprk_/scripts/analysisWorkspaceLauncher.js
Display Name: Analysis Workspace Code Page Launcher
Type: Script (JScript)
Description: Opens the AnalysisWorkspace Code Page dialog via navigateTo webresource.
             Replaces legacy Custom Page navigation for the Analysis Workspace.

================================================================================
VERSION HISTORY
================================================================================
- 1.0.0: Initial release - navigateTo webresource launcher (replaces Custom Page nav)
*/
