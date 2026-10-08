/**
 * Playbook Builder Command Script
 *
 * PURPOSE: Opens Playbook Builder Code Page in a near-full-screen dialog
 * WORKS WITH: sprk_analysisplaybook entity (Analysis Playbook)
 * DEPLOYMENT: Ribbon buttons on entity list view and form command bar
 * ARCHITECTURE: Code Page (web resource) dialog via Xrm.Navigation.navigateTo (ADR-006)
 *
 * @version 2.0.0
 * @namespace Spaarke.Commands.Playbook
 */

// ============================================================================
// CONSTANTS
// ============================================================================

var WEBRESOURCE_NAME = "sprk_playbookbuilder";
var LOG_PREFIX = "[Spaarke.Playbook]";

// ============================================================================
// MAIN COMMAND FUNCTIONS
// ============================================================================

/**
 * Opens Playbook Builder for an EXISTING playbook (from form)
 * Called by "Open" button in Analysis Builder menu on form
 *
 * @param {object} primaryControl - The form context (passed by ribbon via PrimaryControl)
 */
function Spaarke_OpenPlaybookBuilder(primaryControl) {
    try {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "OpenPlaybookBuilder: Starting v2.0.0");
        console.log(LOG_PREFIX, "========================================");

        // Get form context
        var formContext = primaryControl;
        if (!formContext || !formContext.data || !formContext.data.entity) {
            console.error(LOG_PREFIX, "Invalid form context");
            showPlaybookErrorDialog("Unable to access form context. Please refresh the page and try again.");
            return;
        }

        // Get playbook information
        var playbookId = formContext.data.entity.getId();
        var entityName = formContext.data.entity.getEntityName();

        console.log(LOG_PREFIX, "Playbook ID:", playbookId);
        console.log(LOG_PREFIX, "Entity Name:", entityName);

        // Validate playbook is saved
        if (!playbookId || playbookId === "" || playbookId === "{00000000-0000-0000-0000-000000000000}") {
            showPlaybookErrorDialog("Please save the playbook before opening the builder.");
            return;
        }

        // Clean GUID (remove braces)
        var cleanPlaybookId = playbookId.replace(/[{}]/g, '').toLowerCase();

        // Get playbook display name
        var playbookName = getPlaybookDisplayName(formContext);
        console.log(LOG_PREFIX, "Playbook Name:", playbookName);

        // Open Playbook Builder Custom Page for existing playbook
        openPlaybookBuilderDialog({
            playbookId: cleanPlaybookId,
            playbookName: playbookName,
            isNew: false
        }, formContext);

    } catch (error) {
        console.error(LOG_PREFIX, "OpenPlaybookBuilder Error:", error);
        showPlaybookErrorDialog("An unexpected error occurred: " + error.message);
    }
}

/**
 * Opens Playbook Builder for a NEW playbook (from form or list view)
 * Called by "+ New" button in Analysis Builder menu or list view
 *
 * @param {object} primaryControl - The form context or command bar context
 */
function Spaarke_NewPlaybookBuilder(primaryControl) {
    try {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "NewPlaybookBuilder: Starting v2.0.0");
        console.log(LOG_PREFIX, "========================================");

        // Open Playbook Builder Custom Page for new playbook
        openPlaybookBuilderDialog({
            playbookId: "",
            playbookName: "New Playbook",
            isNew: true
        }, primaryControl);

    } catch (error) {
        console.error(LOG_PREFIX, "NewPlaybookBuilder Error:", error);
        showPlaybookErrorDialog("An unexpected error occurred: " + error.message);
    }
}

/**
 * Opens Playbook Builder for a NEW playbook from list view
 * Called by "+ New" button on the entity list command bar
 *
 * @param {object} commandProperties - Command properties from ribbon
 */
function Spaarke_NewPlaybookFromList(commandProperties) {
    try {
        console.log(LOG_PREFIX, "========================================");
        console.log(LOG_PREFIX, "NewPlaybookFromList: Starting v2.0.0");
        console.log(LOG_PREFIX, "========================================");

        // Open Playbook Builder Custom Page for new playbook
        openPlaybookBuilderDialog({
            playbookId: "",
            playbookName: "New Playbook",
            isNew: true
        }, null);

    } catch (error) {
        console.error(LOG_PREFIX, "NewPlaybookFromList Error:", error);
        showPlaybookErrorDialog("An unexpected error occurred: " + error.message);
    }
}

// ============================================================================
// DIALOG FUNCTIONS
// ============================================================================

/**
 * Open Playbook Builder Code Page dialog in near-full-screen mode.
 *
 * Uses Xrm.Navigation.navigateTo with pageType "webresource" (ADR-006).
 * The Code Page reads playbookId from URLSearchParams.
 *
 * @param {object} params - Dialog parameters
 * @param {object} formContext - Form context for refresh after close (optional)
 */
function openPlaybookBuilderDialog(params, formContext) {
    console.log(LOG_PREFIX, "Opening Playbook Builder with parameters:", params);

    try {
        // Build data string for Code Page URL parameters
        var dataParams = "playbookId=" + encodeURIComponent(params.playbookId || "");
        if (params.playbookName) {
            dataParams += "&playbookName=" + encodeURIComponent(params.playbookName);
        }
        if (params.isNew) {
            dataParams += "&isNew=true";
        }

        // Navigate to Code Page web resource (not Custom Page)
        var pageInput = {
            pageType: "webresource",
            webresourceName: WEBRESOURCE_NAME,
            data: dataParams
        };

        // Dialog options for near-full-screen experience
        var navigationOptions = {
            target: 2,      // Dialog
            position: 1,    // Center
            width: { value: 95, unit: "%" },
            height: { value: 95, unit: "%" }
        };

        console.log(LOG_PREFIX, "Opening with pageInput:", pageInput);
        console.log(LOG_PREFIX, "Navigation options:", navigationOptions);

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success(result) {
                console.log(LOG_PREFIX, "Playbook Builder dialog closed", result);

                // Refresh form if we have form context (editing existing)
                if (formContext && formContext.data && typeof formContext.data.refresh === "function") {
                    console.log(LOG_PREFIX, "Refreshing form data");
                    formContext.data.refresh(false);
                }

                // Refresh list view if on list (creating new)
                if (params.isNew) {
                    try {
                        if (Xrm.Page && Xrm.Page.getControl) {
                            var grid = Xrm.Page.getControl("grid");
                            if (grid && typeof grid.refresh === "function") {
                                grid.refresh();
                            }
                        }
                    } catch (e) {
                        console.log(LOG_PREFIX, "Could not refresh grid:", e.message);
                    }
                }
            },
            function error(err) {
                console.error(LOG_PREFIX, "navigateTo error:", err);
                // errorCode 2 means user closed the dialog - not an error
                if (err && err.errorCode !== 2) {
                    showPlaybookErrorDialog("Error opening Playbook Builder: " + (err.message || "Unknown error"));
                }
            }
        );

    } catch (navError) {
        console.error(LOG_PREFIX, "Exception in navigateTo:", navError);
        showPlaybookErrorDialog("Error opening Playbook Builder: " + (navError.message || "Unknown error"));
    }
}

// ============================================================================
// HELPER FUNCTIONS
// ============================================================================

/**
 * Get playbook display name from form
 *
 * @param {object} formContext - Form context
 * @returns {string} Display name
 */
function getPlaybookDisplayName(formContext) {
    // Try primary name field
    var nameFields = ["sprk_name", "sprk_title"];

    for (var i = 0; i < nameFields.length; i++) {
        var value = getPlaybookAttributeValue(formContext, nameFields[i]);
        if (value) return value;
    }

    // Fallback to partial ID
    var recordId = formContext.data.entity.getId().replace(/[{}]/g, '');
    return "Playbook (" + recordId.substring(0, 8) + "...)";
}

/**
 * Get attribute value from form context
 *
 * @param {object} formContext - Form context
 * @param {string} attributeName - Attribute logical name
 * @returns {*} Attribute value or null
 */
function getPlaybookAttributeValue(formContext, attributeName) {
    try {
        var attribute = formContext.getAttribute(attributeName);
        if (attribute) {
            var value = attribute.getValue();
            if (value !== null && value !== undefined) {
                // Handle lookup fields
                if (Array.isArray(value) && value.length > 0) {
                    return value[0].id || value[0].name;
                }
                return value;
            }
        }
    } catch (e) {
        console.log(LOG_PREFIX, "Could not get attribute " + attributeName + ":", e.message);
    }
    return null;
}

/**
 * Show error dialog
 *
 * @param {string} message - Error message
 */
function showPlaybookErrorDialog(message) {
    Xrm.Navigation.openAlertDialog({
        text: message,
        title: "Playbook Builder"
    });
}

// ============================================================================
// ENABLE/VISIBILITY RULES
// ============================================================================

/**
 * Enable rule for "Open" button: Only enable if playbook is saved
 *
 * @param {object} primaryControl - Form context
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableOpenPlaybookBuilder(primaryControl) {
    try {
        var formContext = primaryControl;
        if (!formContext || !formContext.data) return false;

        // Check if record is saved
        var recordId = formContext.data.entity.getId();
        if (!recordId || recordId === "" || recordId === "{00000000-0000-0000-0000-000000000000}") {
            return false;
        }

        return true;

    } catch (error) {
        console.error(LOG_PREFIX, "EnableOpenPlaybookBuilder Error:", error);
        return false;
    }
}

/**
 * Enable rule for "+ New" button: Always enabled
 *
 * @returns {boolean} True (always enabled)
 */
function Spaarke_EnableNewPlaybookBuilder() {
    return true;
}

/**
 * Visibility rule: Always show buttons
 *
 * @returns {boolean} True to show button
 */
function Spaarke_ShowPlaybookBuilderButtons() {
    return true;
}

// ============================================================================
// DEPLOYMENT NOTES
// ============================================================================

/*
RIBBON CONFIGURATION:

================================================================================
1. LIST VIEW COMMAND BAR (sprk_analysisplaybook HomepageGrid)
================================================================================

Location: Mscrm.HomepageGrid.sprk_analysisplaybook.MainTab.Actions.Controls._children

Button: "+ New"
  - Command: Spaarke.Playbook.NewFromList.Command
  - Function: Spaarke_NewPlaybookFromList
  - Enable: Spaarke_EnableNewPlaybookBuilder (always true)
  - Icon: Add (or GridAdd)

================================================================================
2. FORM COMMAND BAR (sprk_analysisplaybook Form)
================================================================================

Location: Mscrm.Form.sprk_analysisplaybook.MainTab.Actions.Controls._children

Menu: "Analysis Builder"
  - Icon: Flow (or similar workflow icon)

  Submenu Items:

  a) "Open"
     - Command: Spaarke.Playbook.Open.Command
     - Function: Spaarke_OpenPlaybookBuilder
     - CrmParameter: PrimaryControl
     - Enable: Spaarke_EnableOpenPlaybookBuilder
     - Icon: OpenFile or GridExpand

  b) "+ New"
     - Command: Spaarke.Playbook.NewFromForm.Command
     - Function: Spaarke_NewPlaybookBuilder
     - CrmParameter: PrimaryControl
     - Enable: Spaarke_EnableNewPlaybookBuilder (always true)
     - Icon: Add

================================================================================
CODE PAGE WEB RESOURCE (replaces Custom Page since v2.0.0)
================================================================================
Web Resource: sprk_playbookbuilder (HTML web resource)
Navigation: Xrm.Navigation.navigateTo({ pageType: "webresource" })
Parameters: playbookId, playbookName, isNew (via data query string)

DEPRECATED: Custom Page sprk_playbookbuilder_c0199 (replaced by Code Page)

VERSION HISTORY:
- 1.0.0: Initial release (Custom Page)
- 1.1.0: Added list view button and form menu with Open/New options
- 2.0.0: Migrated from Custom Page to Code Page web resource (ADR-006)
*/
