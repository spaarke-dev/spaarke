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
    // Navigation Mode Detection
    // ========================================================================

    /**
     * Session storage key prefix for the back-navigation guard.
     *
     * When opening the Code Page as full page (target: 1) from Form OnLoad,
     * pressing browser Back returns to the form, which fires OnLoad again.
     * The guard prevents this infinite redirect loop by tracking whether
     * we already redirected for a given analysis record.
     *
     * Flow:
     *   1. OnLoad fires → guard key NOT set → set key → navigate to Code Page (full page)
     *   2. User presses Back → form loads → OnLoad fires → guard key IS set → clear key → stay on form
     *   3. User presses Back again → returns to entity list (or Document form)
     */
    var REDIRECT_GUARD_PREFIX = "sprk_aw_redirected_";

    /**
     * Determine whether to open as full page (target: 1) or dialog (target: 2).
     *
     * Heuristic: if the form was opened from a parent context (e.g., subgrid on
     * a Document form), Dataverse includes an "etn" (entity type name) parameter
     * in the URL representing the referring entity. When present, the user navigated
     * from another form's subgrid, so a dialog is appropriate (preserves parent
     * context). When absent, the user came from the entity list or direct navigation,
     * so full page is appropriate (matches standard entity-open behavior).
     *
     * @returns {number} 1 for full page, 2 for dialog
     */
    function detectNavigationTarget() {
        try {
            var search = window.location.search || "";
            var params = new URLSearchParams(search);

            // "etn" = referring entity type name, set by Dataverse when navigating
            // from a subgrid on another entity's form
            if (params.get("etn") || params.get("parentrecordid")) {
                console.log(LOG_PREFIX, "Detected parent context (etn/parentrecordid) → dialog mode");
                return 2; // Dialog — opened from another form's subgrid
            }
        } catch (e) {
            console.warn(LOG_PREFIX, "Could not detect navigation context:", e.message);
        }

        console.log(LOG_PREFIX, "No parent context detected → full page mode");
        return 1; // Full page — opened from entity list or direct navigation
    }

    /**
     * Check and manage the back-navigation redirect guard.
     *
     * @param {string} analysisId - Clean analysis GUID
     * @returns {boolean} True if we should SKIP the redirect (user pressed Back)
     */
    function checkRedirectGuard(analysisId) {
        var guardKey = REDIRECT_GUARD_PREFIX + analysisId;

        if (sessionStorage.getItem(guardKey)) {
            // Guard is set — user pressed Back from the Code Page.
            // Clear the guard and let the form load normally.
            sessionStorage.removeItem(guardKey);
            console.log(LOG_PREFIX, "Back-navigation detected for", analysisId, "→ staying on form");
            return true; // Skip redirect
        }

        return false; // OK to redirect
    }

    /**
     * Set the redirect guard before navigating to the Code Page (full page mode only).
     *
     * @param {string} analysisId - Clean analysis GUID
     */
    function setRedirectGuard(analysisId) {
        var guardKey = REDIRECT_GUARD_PREFIX + analysisId;
        sessionStorage.setItem(guardKey, "1");
    }

    // ========================================================================
    // Main Launcher Function
    // ========================================================================

    /**
     * Open the AnalysisWorkspace Code Page from the sprk_analysis form.
     *
     * Navigation mode is auto-detected:
     *   - From entity list / direct URL → full page (target: 1), matching standard
     *     entity-open behavior. A sessionStorage guard prevents infinite OnLoad loops.
     *   - From a parent form's subgrid → dialog (target: 2) at 95%, preserving
     *     the parent form context underneath.
     *
     * Designed to be called from:
     *   - Form OnLoad event handler on the sprk_analysis entity form (primary)
     *   - Ribbon/command bar button on the sprk_analysis entity form
     *   - Programmatic invocation from other form scripts
     *
     * @param {object} executionContext - Execution context from form event or
     *   primaryControl from ribbon CrmParameter
     */
    ns.openAnalysisWorkspace = function (executionContext) {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "openAnalysisWorkspace: Starting v1.1.0");
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
            // Step 3: Detect navigation mode and check redirect guard
            // -----------------------------------------------------------------
            var target = detectNavigationTarget();

            if (target === 1 && checkRedirectGuard(analysisId)) {
                // User pressed Back from Code Page → stay on form, don't redirect
                console.log(LOG_PREFIX, "Allowing form to load normally (back-navigation)");
                return;
            }

            // -----------------------------------------------------------------
            // Step 4: Extract documentId from source document lookup
            // -----------------------------------------------------------------
            var rawDocumentId = getAttributeValue(formContext, SOURCE_DOCUMENT_FIELD);
            var documentId = cleanGuid(rawDocumentId);

            if (!documentId) {
                console.info(LOG_PREFIX, "No source document linked -- opening without viewer context");
            } else {
                console.log(LOG_PREFIX, "documentId:", documentId);
            }

            // -----------------------------------------------------------------
            // Step 5: Extract tenantId from global context
            // -----------------------------------------------------------------
            var tenantId = getTenantId();
            console.log(LOG_PREFIX, "tenantId:", tenantId);

            // -----------------------------------------------------------------
            // Step 6: Navigate to Code Page
            // -----------------------------------------------------------------
            var dataParams = buildDataParams(analysisId, documentId, tenantId);
            console.log(LOG_PREFIX, "dataParams:", dataParams);

            var pageInput = {
                pageType: "webresource",
                webresourceName: WEB_RESOURCE_NAME,
                data: dataParams
            };

            var navigationOptions;

            if (target === 1) {
                // Full page — from entity list or direct navigation
                console.log(LOG_PREFIX, "Opening as FULL PAGE (target: 1)");
                setRedirectGuard(analysisId);
                navigationOptions = { target: 1 };
            } else {
                // Dialog — from parent form subgrid
                console.log(LOG_PREFIX, "Opening as DIALOG (target: 2, 95%)");
                navigationOptions = {
                    target: 2,
                    width: DIALOG_WIDTH,
                    height: DIALOG_HEIGHT
                };
            }

            console.log(LOG_PREFIX, "Navigating to:", pageInput);
            console.log(LOG_PREFIX, "Options:", navigationOptions);

            Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
                function success() {
                    console.log(LOG_PREFIX, "Navigation completed successfully");
                    // Refresh the form to pick up any changes made in the workspace
                    // (only relevant for dialog mode — full page replaces the form)
                    if (target === 2) {
                        try {
                            if (formContext.data && typeof formContext.data.refresh === "function") {
                                formContext.data.refresh(false);
                                console.log(LOG_PREFIX, "Form data refreshed");
                            }
                        } catch (refreshError) {
                            console.warn(LOG_PREFIX, "Form refresh failed (non-critical):", refreshError.message);
                        }
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
================================================================================
SETUP: FORM ONLOAD EVENT (RECOMMENDED — auto-opens workspace)
================================================================================

Entity: sprk_analysis
Form: Main Form (Information)
Event: OnLoad

  - Library: sprk_/scripts/analysisWorkspaceLauncher.js
  - Function: Spaarke.AnalysisWorkspace.openAnalysisWorkspace
  - Pass execution context: Yes (checkbox)

NAVIGATION MODE IS AUTO-DETECTED:

  From Entity List (Analyses view):
    - User clicks a row → Analysis form loads → OnLoad fires
    - No parent context detected (no "etn" URL param)
    - Opens Code Page as FULL PAGE (target: 1)
    - SessionStorage guard prevents infinite loop on Back navigation
    - Back button flow: Code Page → form (guard stops redirect) → entity list

  From Document Subgrid (sprk_analysis subgrid on Document form):
    - User clicks Analysis Name link → Analysis form loads → OnLoad fires
    - Parent context detected ("etn=sprk_document" in URL)
    - Opens Code Page as DIALOG (target: 2, 95%)
    - Close dialog → back on Analysis form → Back → Document form

================================================================================
OPTIONAL: FORM COMMAND BAR BUTTON (manual trigger)
================================================================================

Not needed when using OnLoad (auto-open). Only add if you want a manual
"Open Workspace" button as an alternative:

Location: Mscrm.Form.sprk_analysis.MainTab.Actions.Controls._children

Button: "Open Workspace"
  - Command ID: Spaarke.AnalysisWorkspace.Open.Command
  - Function: Spaarke.AnalysisWorkspace.openAnalysisWorkspace
  - Library: $webresource:sprk_/scripts/analysisWorkspaceLauncher.js
  - CrmParameter: PrimaryControl
  - Enable Rule: Spaarke.AnalysisWorkspace.enableOpenWorkspace (same library)

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

With the Form OnLoad approach, the subgrid ribbon command for "Open Workspace"
(Spaarke.Analysis.OpenWorkspace.Command) is no longer needed — users click the
Name link to open the Analysis form, which auto-opens the workspace.

The existing sprk_analysis_commands.js remains for:
  - NewAnalysis / NewAnalysisFromSubgrid (Analysis Builder dialog)

================================================================================
WEB RESOURCE REGISTRATION
================================================================================

Name: sprk_/scripts/analysisWorkspaceLauncher.js
Display Name: Analysis Workspace Code Page Launcher
Type: Script (JScript)
Description: Opens the AnalysisWorkspace Code Page via navigateTo webresource.
             Auto-detects context: full page from entity list, dialog from subgrid.
             Replaces legacy Custom Page navigation for the Analysis Workspace.

================================================================================
VERSION HISTORY
================================================================================
- 1.1.0: Dual-mode navigation — full page from entity list, dialog from subgrid.
         Back-navigation guard prevents OnLoad redirect loop in full page mode.
- 1.0.0: Initial release - dialog-only navigateTo webresource launcher
*/
