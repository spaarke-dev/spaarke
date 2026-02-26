/**
 * SprkChatPane Side Pane Launcher Script
 *
 * Opens the SprkChatPane Code Page as a Dataverse side pane using
 * Xrm.App.sidePanes.createPane(). Called from ribbon buttons or form events.
 *
 * Deployment: Dataverse web resource sprk_/scripts/openSprkChatPane.js
 * Web Resource Name: sprk_SprkChatPane (HTML Code Page)
 *
 * Source: src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts
 *
 * @version 1.0.0
 * @namespace Spaarke.SprkChat
 *
 * Ribbon Configuration:
 *   Library: sprk_/scripts/openSprkChatPane.js
 *   Function: Spaarke.SprkChat.openPane
 *   CrmParameter: PrimaryControl
 *
 * PH-015-A: Icon uses placeholder data URI - replace when designer provides
 *           final sprk_ai_icon asset.
 */

// ============================================================================
// Namespace Setup
// ============================================================================

var Spaarke = window.Spaarke || {};
Spaarke.SprkChat = Spaarke.SprkChat || {};

(function (ns) {
    "use strict";

    // ========================================================================
    // Constants
    // ========================================================================

    /** Deterministic pane ID for singleton behavior */
    var PANE_ID = "sprk-chat-pane";

    /** Title displayed in the side pane header */
    var PANE_TITLE = "SprkChat";

    /** Web resource name for the SprkChatPane Code Page */
    var WEB_RESOURCE_NAME = "sprk_SprkChatPane";

    /** Default pane width in pixels */
    var PANE_WIDTH = 400;

    /** Log prefix for console output */
    var LOG_PREFIX = "[Spaarke.SprkChat]";

    /**
     * PH-015-A: Placeholder AI icon (SVG data URI).
     * Replace with final designer-provided sprk_ai_icon web resource reference.
     */
    var ICON_SRC =
        "data:image/svg+xml;utf8," +
        encodeURIComponent(
            '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="%230078d4" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
            '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>' +
            '<circle cx="9" cy="10" r="1" fill="%230078d4"/>' +
            '<circle cx="12" cy="10" r="1" fill="%230078d4"/>' +
            '<circle cx="15" cy="10" r="1" fill="%230078d4"/>' +
            "</svg>"
        );

    // ========================================================================
    // Helper Functions
    // ========================================================================

    /**
     * Get the Xrm.App.sidePanes API.
     * Checks current window and parent window for iframe contexts.
     *
     * @returns {object|null} sidePanes API or null if unavailable
     */
    function getSidePanesApi() {
        try {
            // Current window
            if (typeof Xrm !== "undefined" && Xrm && Xrm.App && Xrm.App.sidePanes) {
                return Xrm.App.sidePanes;
            }

            // Parent window (ribbon scripts may run in iframe)
            if (window.parent && window.parent !== window && window.parent.Xrm &&
                window.parent.Xrm.App && window.parent.Xrm.App.sidePanes) {
                return window.parent.Xrm.App.sidePanes;
            }
        } catch (e) {
            console.warn(LOG_PREFIX, "Error accessing Xrm.App.sidePanes:", e);
        }
        return null;
    }

    /**
     * Strip curly braces from a Dataverse GUID and lowercase.
     *
     * @param {string} guid - GUID string, possibly wrapped in braces
     * @returns {string} Clean GUID
     */
    function cleanGuid(guid) {
        if (!guid) return "";
        return guid.replace(/[{}]/g, "").toLowerCase();
    }

    /**
     * Get entity type and record ID from the current form context.
     *
     * @param {object} [primaryControl] - Form context from ribbon CrmParameter
     * @returns {{ entityType: string, entityId: string }}
     */
    function getFormContext(primaryControl) {
        var result = { entityType: "", entityId: "" };

        try {
            // Strategy 1: primaryControl (passed by ribbon as PrimaryControl)
            if (primaryControl && primaryControl.data && primaryControl.data.entity) {
                result.entityType = primaryControl.data.entity.getEntityName() || "";
                result.entityId = cleanGuid(primaryControl.data.entity.getId() || "");
                if (result.entityType || result.entityId) {
                    console.log(LOG_PREFIX, "Context from primaryControl:", result.entityType, result.entityId);
                    return result;
                }
            }

            // Strategy 2: Xrm.Page (legacy but widely available)
            var xrm = (typeof Xrm !== "undefined") ? Xrm : (window.parent ? window.parent.Xrm : null);
            if (xrm && xrm.Page && xrm.Page.data && xrm.Page.data.entity) {
                result.entityType = xrm.Page.data.entity.getEntityName() || "";
                result.entityId = cleanGuid(xrm.Page.data.entity.getId() || "");
                if (result.entityType || result.entityId) {
                    console.log(LOG_PREFIX, "Context from Xrm.Page:", result.entityType, result.entityId);
                    return result;
                }
            }
        } catch (e) {
            console.warn(LOG_PREFIX, "Error reading form context:", e);
        }

        console.log(LOG_PREFIX, "No form context available, opening without record context");
        return result;
    }

    /**
     * Build data query string for the Code Page web resource.
     *
     * @param {string} entityType
     * @param {string} entityId
     * @param {string} [playbookId]
     * @param {string} [sessionId]
     * @returns {string} URL-encoded query string
     */
    function buildDataParams(entityType, entityId, playbookId, sessionId) {
        var parts = [];
        if (entityType) parts.push("entityType=" + encodeURIComponent(entityType));
        if (entityId) parts.push("entityId=" + encodeURIComponent(entityId));
        if (playbookId) parts.push("playbookId=" + encodeURIComponent(playbookId));
        if (sessionId) parts.push("sessionId=" + encodeURIComponent(sessionId));
        return parts.join("&");
    }

    /**
     * Show an error alert dialog to the user.
     *
     * @param {string} message - Error message
     */
    function showErrorAlert(message) {
        try {
            var xrm = (typeof Xrm !== "undefined") ? Xrm : (window.parent ? window.parent.Xrm : null);
            if (xrm && xrm.Navigation && xrm.Navigation.openAlertDialog) {
                xrm.Navigation.openAlertDialog({
                    title: "SprkChat",
                    text: message
                });
            }
        } catch (e) {
            console.error(LOG_PREFIX, "Could not show alert:", e);
        }
    }

    // ========================================================================
    // Main Launcher Function
    // ========================================================================

    /**
     * Open the SprkChatPane as a Dataverse side pane.
     *
     * Singleton behavior: If a pane with ID "sprk-chat-pane" already exists,
     * it is selected (brought to focus) and navigated to the current record
     * context. Otherwise a new pane is created.
     *
     * @param {object} [primaryControl] - Form context from ribbon CrmParameter.
     *   Configure ribbon: CrmParameter = PrimaryControl
     * @param {string} [playbookId] - Optional playbook ID for guided interaction
     * @param {string} [sessionId] - Optional session ID to resume chat
     */
    ns.openPane = function (primaryControl, playbookId, sessionId) {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "openPane: Starting v1.0.0");
        console.log(LOG_PREFIX, "========================================");

        // -----------------------------------------------------------------
        // Step 1: Check if sidePanes API is available
        // -----------------------------------------------------------------
        var sidePanes = getSidePanesApi();
        if (!sidePanes) {
            var errorMsg =
                "The SprkChat side pane requires Xrm.App.sidePanes API, " +
                "which is not available in this context. " +
                "Please ensure you are using a supported model-driven app.";
            console.error(LOG_PREFIX, errorMsg);
            showErrorAlert(errorMsg);
            return;
        }

        // -----------------------------------------------------------------
        // Step 2: Get current form context
        // -----------------------------------------------------------------
        var ctx = getFormContext(primaryControl);
        var dataParams = buildDataParams(ctx.entityType, ctx.entityId, playbookId, sessionId);

        console.log(LOG_PREFIX, "entityType:", ctx.entityType);
        console.log(LOG_PREFIX, "entityId:", ctx.entityId);
        console.log(LOG_PREFIX, "dataParams:", dataParams);

        // -----------------------------------------------------------------
        // Step 3: Check for existing pane (singleton)
        // -----------------------------------------------------------------
        try {
            var existingPane = sidePanes.getPane(PANE_ID);

            if (existingPane) {
                console.log(LOG_PREFIX, "Existing pane found, reusing");

                existingPane.navigate({
                    pageType: "webresource",
                    webresourceName: WEB_RESOURCE_NAME,
                    data: dataParams
                }).then(function () {
                    existingPane.select();
                    console.log(LOG_PREFIX, "Existing pane navigated and selected");
                }, function (navError) {
                    console.error(LOG_PREFIX, "Navigation error on existing pane:", navError);
                    // Pane may be stale; try selecting it anyway
                    try { existingPane.select(); } catch (e) { /* ignore */ }
                });
                return;
            }

            // -----------------------------------------------------------------
            // Step 4: Create new pane
            // -----------------------------------------------------------------
            console.log(LOG_PREFIX, "No existing pane, creating new one");

            sidePanes.createPane({
                paneId: PANE_ID,
                title: PANE_TITLE,
                imageSrc: ICON_SRC, // PH-015-A: placeholder icon
                canClose: true,
                width: PANE_WIDTH,
                isSelected: true
            }).then(function (newPane) {
                newPane.navigate({
                    pageType: "webresource",
                    webresourceName: WEB_RESOURCE_NAME,
                    data: dataParams
                }).then(function () {
                    console.log(LOG_PREFIX, "New pane created and navigated successfully");
                }, function (navError) {
                    console.error(LOG_PREFIX, "Navigation error on new pane:", navError);
                });
            }, function (createError) {
                var msg = createError && createError.message ? createError.message : String(createError);
                console.error(LOG_PREFIX, "Error creating pane:", msg);
                showErrorAlert("Unable to open SprkChat: " + msg);
            });

        } catch (error) {
            var errorMessage = error && error.message ? error.message : String(error);
            console.error(LOG_PREFIX, "Error opening SprkChat pane:", errorMessage);
            showErrorAlert("Unable to open SprkChat: " + errorMessage);
        }
    };

    // ========================================================================
    // Enable / Visibility Rules (for ribbon configuration)
    // ========================================================================

    /**
     * Enable rule: SprkChat button is enabled when sidePanes API is available.
     *
     * @returns {boolean} true if button should be enabled
     */
    ns.enable = function () {
        return getSidePanesApi() !== null;
    };

    /**
     * Visibility rule: Always show the SprkChat button.
     *
     * @returns {boolean} true to show button
     */
    ns.show = function () {
        return true;
    };

})(Spaarke.SprkChat);

// ============================================================================
// DEPLOYMENT NOTES
// ============================================================================

/*
RIBBON CONFIGURATION:

================================================================================
FORM COMMAND BAR (any entity form)
================================================================================

Location: Mscrm.Form.{entityname}.MainTab.Actions.Controls._children

Button: "SprkChat"
  - Command ID: Spaarke.SprkChat.Open.Command
  - Function: Spaarke.SprkChat.openPane
  - Library: $webresource:sprk_/scripts/openSprkChatPane.js
  - CrmParameter: PrimaryControl
  - Enable Rule: Spaarke.SprkChat.enable (same library)
  - Visibility Rule: Spaarke.SprkChat.show (same library)
  - Icon: sprk_ai_icon (PH-015-A: placeholder until designer delivers)

================================================================================
ENTITY-SPECIFIC FORMS (e.g., sprk_matter, sprk_document)
================================================================================

Same configuration as above, scoped to:
  Mscrm.Form.sprk_matter.MainTab.Actions.Controls._children
  Mscrm.Form.sprk_document.MainTab.Actions.Controls._children

================================================================================
PROGRAMMATIC USAGE
================================================================================

// From any Dataverse form script:
if (window.Spaarke && window.Spaarke.SprkChat) {
    window.Spaarke.SprkChat.openPane(primaryControl);
}

// With optional params:
window.Spaarke.SprkChat.openPane(primaryControl, playbookId, sessionId);

================================================================================
VERSION HISTORY
================================================================================
- 1.0.0: Initial release - side pane launcher for SprkChatPane Code Page

================================================================================
WEB RESOURCE
================================================================================
Name: sprk_/scripts/openSprkChatPane.js
Display Name: SprkChat Side Pane Launcher
Type: Script (JScript)
Source: src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts
*/
