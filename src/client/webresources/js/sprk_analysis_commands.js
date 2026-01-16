/**
 * Analysis Command Script
 *
 * PURPOSE: Opens Analysis Builder Custom Page for creating new AI analyses
 * WORKS WITH: sprk_document entity (from Analysis tab subgrid or form command)
 * DEPLOYMENT: Ribbon button on sprk_document form Analysis tab
 * ARCHITECTURE: Custom Page dialog approach
 *
 * @version 1.0.0
 * @namespace Spaarke.Commands.Analysis
 */

// ============================================================================
// MAIN COMMAND FUNCTION
// ============================================================================

/**
 * Main command: Opens Analysis Builder dialog
 * This is called by the "+ New Analysis" ribbon button on the Analysis tab
 *
 * @param {object} primaryControl - The form context (passed by ribbon via PrimaryControl)
 */
function Spaarke_NewAnalysis(primaryControl) {
    try {
        console.log("[Spaarke.Analysis] ========================================");
        console.log("[Spaarke.Analysis] NewAnalysis: Starting v1.0.0");
        console.log("[Spaarke.Analysis] ========================================");

        // Get form context
        const formContext = primaryControl;
        if (!formContext || !formContext.data || !formContext.data.entity) {
            console.error("[Spaarke.Analysis] Invalid form context");
            showErrorDialog("Unable to access form context. Please refresh the page and try again.");
            return;
        }

        // Get document information
        const documentId = formContext.data.entity.getId();
        const entityName = formContext.data.entity.getEntityName();

        console.log("[Spaarke.Analysis] Document ID:", documentId);
        console.log("[Spaarke.Analysis] Entity Name:", entityName);

        // Validate document is saved
        if (!documentId || documentId === "" || documentId === "{00000000-0000-0000-0000-000000000000}") {
            showErrorDialog("Please save the document before creating an analysis.");
            return;
        }

        // Clean GUID (remove braces)
        const cleanDocumentId = documentId.replace(/[{}]/g, '').toLowerCase();

        // Get document display name
        const documentName = getDocumentDisplayName(formContext);
        console.log("[Spaarke.Analysis] Document Name:", documentName);

        // Get container ID for file access
        const containerId = getAttributeValue(formContext, "sprk_containerid");
        console.log("[Spaarke.Analysis] Container ID:", containerId);

        // Get file path for SPE access (sprk_filepath indicates file is associated)
        const filePath = getAttributeValue(formContext, "sprk_filepath");
        console.log("[Spaarke.Analysis] File Path:", filePath);

        if (!filePath) {
            showErrorDialog(
                "This document does not have an associated file.\n\n" +
                "Please upload a file before creating an analysis."
            );
            return;
        }

        // Get file ID for SPE access (optional, may not be on form)
        const fileId = getAttributeValue(formContext, "sprk_fileid");

        // Open Analysis Builder Custom Page
        openAnalysisBuilderDialog({
            documentId: cleanDocumentId,
            documentName: documentName,
            containerId: containerId,
            fileId: fileId
        }, formContext);

    } catch (error) {
        console.error("[Spaarke.Analysis] NewAnalysis Error:", error);
        showErrorDialog("An unexpected error occurred: " + error.message);
    }
}

/**
 * Command: Opens Analysis Builder from subgrid selection
 * Called when user selects a document row in the Analysis tab subgrid
 *
 * @param {object} selectedControl - The subgrid control (passed by ribbon)
 */
function Spaarke_NewAnalysisFromSubgrid(selectedControl) {
    try {
        console.log("[Spaarke.Analysis] NewAnalysisFromSubgrid: Starting");
        console.log("[Spaarke.Analysis] selectedControl:", selectedControl);

        // Get form context from subgrid
        const formContext = getParentFormContext(selectedControl);
        console.log("[Spaarke.Analysis] formContext:", formContext);

        if (!formContext) {
            console.error("[Spaarke.Analysis] Failed to get form context from subgrid");
            showErrorDialog("Unable to access form context. Please refresh the page and try again.");
            return;
        }

        // Debug: Log entity info
        if (formContext.data && formContext.data.entity) {
            console.log("[Spaarke.Analysis] Entity name:", formContext.data.entity.getEntityName());
            console.log("[Spaarke.Analysis] Entity ID:", formContext.data.entity.getId());
        }

        // Debug: Log all attributes
        if (formContext.data && formContext.data.entity && formContext.data.entity.attributes) {
            console.log("[Spaarke.Analysis] Available attributes:");
            formContext.data.entity.attributes.forEach(function(attr) {
                console.log("  - " + attr.getName() + ": " + attr.getValue());
            });
        }

        // Delegate to main function
        Spaarke_NewAnalysis(formContext);

    } catch (error) {
        console.error("[Spaarke.Analysis] NewAnalysisFromSubgrid Error:", error);
        showErrorDialog("An unexpected error occurred: " + error.message);
    }
}

// ============================================================================
// NAVIGATION FUNCTIONS
// ============================================================================

/**
 * Navigate to Analysis Workspace Custom Page
 * Called when user clicks on an analysis record in the subgrid
 *
 * @param {string} analysisId - The analysis record GUID
 * @param {string} documentId - The parent document GUID
 */
function Spaarke_OpenAnalysisWorkspace(analysisId, documentId) {
    try {
        console.log("[Spaarke.Analysis] OpenAnalysisWorkspace: Starting");
        console.log("[Spaarke.Analysis] Analysis ID:", analysisId);
        console.log("[Spaarke.Analysis] Document ID:", documentId);

        if (!analysisId) {
            showErrorDialog("Please select an analysis to open.");
            return;
        }

        const cleanAnalysisId = analysisId.replace(/[{}]/g, '').toLowerCase();
        const cleanDocumentId = documentId ? documentId.replace(/[{}]/g, '').toLowerCase() : '';

        // Prepare data payload for Custom Page
        const dataPayload = JSON.stringify({
            analysisId: cleanAnalysisId,
            documentId: cleanDocumentId
        });

        const pageInput = {
            pageType: "custom",
            name: "sprk_analysisworkspace_8bc0b",
            recordId: dataPayload  // Pass data via recordId (dialog workaround)
        };

        // Get app ID if available
        try {
            const globalContext = Xrm.Utility.getGlobalContext();
            if (globalContext && globalContext.client && typeof globalContext.client.getAppId === 'function') {
                pageInput.appId = globalContext.client.getAppId();
            }
        } catch (e) {
            console.log("[Spaarke.Analysis] Could not get appId:", e.message);
        }

        // Navigate to Custom Page (full page, not dialog)
        const navigationOptions = {
            target: 1  // Full page (not dialog)
        };

        console.log("[Spaarke.Analysis] Navigating to Analysis Workspace:", pageInput);

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success() {
                console.log("[Spaarke.Analysis] Navigation successful");
            },
            function error(err) {
                console.error("[Spaarke.Analysis] Navigation error:", err);
                if (err && err.errorCode !== 2) {
                    showErrorDialog("Error opening Analysis Workspace: " + (err.message || "Unknown error"));
                }
            }
        );

    } catch (error) {
        console.error("[Spaarke.Analysis] OpenAnalysisWorkspace Error:", error);
        showErrorDialog("An unexpected error occurred: " + error.message);
    }
}

/**
 * Navigate to Analysis Workspace from subgrid row click
 * Called by ribbon OnRowClick or double-click handler
 *
 * @param {object} selectedItems - Selected items from subgrid
 * @param {object} selectedControl - The subgrid control
 */
function Spaarke_OpenAnalysisWorkspaceFromSubgrid(selectedItems, selectedControl) {
    try {
        console.log("[Spaarke.Analysis] OpenAnalysisWorkspaceFromSubgrid: Starting");

        if (!selectedItems || selectedItems.length === 0) {
            showErrorDialog("Please select an analysis to open.");
            return;
        }

        // Get first selected item
        const selectedItem = selectedItems[0];
        const analysisId = selectedItem.Id || selectedItem.id;

        // Get parent document ID from form context
        const formContext = getParentFormContext(selectedControl);
        const documentId = formContext ? formContext.data.entity.getId() : null;

        Spaarke_OpenAnalysisWorkspace(analysisId, documentId);

    } catch (error) {
        console.error("[Spaarke.Analysis] OpenAnalysisWorkspaceFromSubgrid Error:", error);
        showErrorDialog("An unexpected error occurred: " + error.message);
    }
}

// ============================================================================
// HELPER FUNCTIONS
// ============================================================================

/**
 * Get parent form context from subgrid control
 *
 * @param {object} selectedControl - Subgrid control
 * @returns {object|null} Form context or null
 */
function getParentFormContext(selectedControl) {
    if (!selectedControl) return null;

    // Try _formContext property (modern Dataverse)
    if (selectedControl._formContext) {
        return selectedControl._formContext;
    }

    // Try getFormContext method
    if (typeof selectedControl.getFormContext === "function") {
        return selectedControl.getFormContext();
    }

    // Try getParent method
    if (typeof selectedControl.getParent === "function") {
        const parent = selectedControl.getParent();
        if (parent && parent.data) {
            return parent;
        }
    }

    // Try _context property
    if (selectedControl._context) {
        return selectedControl._context;
    }

    return null;
}

/**
 * Get document display name from form
 *
 * @param {object} formContext - Form context
 * @returns {string} Display name
 */
function getDocumentDisplayName(formContext) {
    // Try primary name field
    const nameFields = ["sprk_name", "sprk_filename", "sprk_title"];

    for (const fieldName of nameFields) {
        const value = getAttributeValue(formContext, fieldName);
        if (value) return value;
    }

    // Fallback to partial ID
    const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');
    return "Document (" + recordId.substring(0, 8) + "...)";
}

/**
 * Get attribute value from form context
 *
 * @param {object} formContext - Form context
 * @param {string} attributeName - Attribute logical name
 * @returns {*} Attribute value or null
 */
function getAttributeValue(formContext, attributeName) {
    try {
        const attribute = formContext.getAttribute(attributeName);
        if (attribute) {
            const value = attribute.getValue();
            if (value !== null && value !== undefined) {
                // Handle lookup fields
                if (Array.isArray(value) && value.length > 0) {
                    return value[0].id || value[0].name;
                }
                return value;
            }
        }
    } catch (e) {
        console.log("[Spaarke.Analysis] Could not get attribute " + attributeName + ":", e.message);
    }
    return null;
}

/**
 * Get environment variable value from Dataverse
 *
 * @param {string} schemaName - The schema name of the environment variable
 * @returns {Promise<string|null>} The value or null if not found
 */
async function getEnvironmentVariable(schemaName) {
    try {
        var defResult = await Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariabledefinition",
            "?$filter=schemaname eq '" + schemaName + "'&$select=environmentvariabledefinitionid,defaultvalue"
        );
        if (defResult.entities.length === 0) {
            console.log("[Spaarke.Analysis] Environment variable '" + schemaName + "' not found");
            return null;
        }
        var defId = defResult.entities[0].environmentvariabledefinitionid;
        var defaultValue = defResult.entities[0].defaultvalue;
        var valueResult = await Xrm.WebApi.retrieveMultipleRecords(
            "environmentvariablevalue",
            "?$filter=_environmentvariabledefinitionid_value eq '" + defId + "'&$select=value"
        );
        if (valueResult.entities.length > 0 && valueResult.entities[0].value) {
            return valueResult.entities[0].value;
        }
        return defaultValue || null;
    } catch (error) {
        console.error("[Spaarke.Analysis] Error getting environment variable:", error);
        return null;
    }
}

/**
 * Open Analysis Builder Custom Page dialog
 *
 * @param {object} params - Dialog parameters
 * @param {object} formContext - Form context for refresh
 */
async function openAnalysisBuilderDialog(params, formContext) {
    console.log("[Spaarke.Analysis] Opening Analysis Builder with parameters:", params);

    // Get API URL from environment variable
    var apiBaseUrl = await getEnvironmentVariable("sprk_BffApiBaseUrl");
    if (!apiBaseUrl) {
        apiBaseUrl = "https://spe-api-dev.azurewebsites.net";
        console.log("[Spaarke.Analysis] Using fallback API URL:", apiBaseUrl);
    }

    // Prepare data payload (includes API URL for Custom Page)
    const dataPayload = {
        documentId: params.documentId,
        documentName: params.documentName,
        containerId: params.containerId,
        fileId: params.fileId,
        apiBaseUrl: apiBaseUrl
    };

    // Use Xrm.Navigation.navigateTo - minimal parameters
    try {
        // Store data in sessionStorage as backup
        sessionStorage.setItem("analysisBuilderParams", JSON.stringify(dataPayload));
        console.log("[Spaarke.Analysis] Stored params in sessionStorage:", dataPayload);

        // Pass data via recordId parameter (Custom Page reads via Param("recordId"))
        const pageInput = {
            pageType: "custom",
            name: "sprk_analysisbuilder_40af8",
            recordId: JSON.stringify(dataPayload)
        };

        // Dialog options for Analysis Builder - percentage-based for responsive sizing
        const navigationOptions = {
            target: 2,      // Dialog
            position: 1,    // Center
            width: { value: 60, unit: '%' },
            height: { value: 70, unit: '%' }
        };

        console.log("[Spaarke.Analysis] Opening Analysis Builder with pageInput:", pageInput);
        console.log("[Spaarke.Analysis] Navigation options:", navigationOptions);

        Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
            function success(result) {
                console.log("[Spaarke.Analysis] Analysis Builder dialog closed", result);
                // Clean up sessionStorage
                sessionStorage.removeItem("analysisBuilderParams");
                // Refresh the subgrid if available
                if (formContext && formContext.getControl) {
                    var subgrid = formContext.getControl("Subgrid_analysis");
                    if (subgrid && typeof subgrid.refresh === "function") {
                        subgrid.refresh();
                    }
                }
            },
            function error(err) {
                console.error("[Spaarke.Analysis] navigateTo error:", err);
                sessionStorage.removeItem("analysisBuilderParams");
                // errorCode 2 means user closed the dialog - not an error
                if (err && err.errorCode !== 2) {
                    showErrorDialog("Error opening Analysis Builder: " + (err.message || "Unknown error"));
                }
            }
        );

    } catch (navError) {
        console.error("[Spaarke.Analysis] Exception in navigateTo:", navError);
        showErrorDialog("Error opening Analysis Builder: " + (navError.message || "Unknown error"));
    }
}

/**
 * Show error dialog
 *
 * @param {string} message - Error message
 */
function showErrorDialog(message) {
    Xrm.Navigation.openAlertDialog({
        text: message,
        title: "Analysis"
    });
}

// ============================================================================
// ENABLE/VISIBILITY RULES
// ============================================================================

/**
 * Enable rule: Only enable if document is saved and has a file
 *
 * @param {object} primaryControl - Form context
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableNewAnalysis(primaryControl) {
    try {
        const formContext = primaryControl;
        if (!formContext || !formContext.data) return false;

        // Check if record is saved
        const recordId = formContext.data.entity.getId();
        if (!recordId || recordId === "" || recordId === "{00000000-0000-0000-0000-000000000000}") {
            return false;
        }

        // Check if file exists (use sprk_filepath to determine if document has file)
        const filePath = getAttributeValue(formContext, "sprk_filepath");
        return !!filePath;

    } catch (error) {
        console.error("[Spaarke.Analysis] EnableNewAnalysis Error:", error);
        return false;
    }
}

/**
 * Enable rule for subgrid: Only enable if parent document is saved and has file
 *
 * @param {object} selectedControl - Subgrid control
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableNewAnalysisSubgrid(selectedControl) {
    try {
        const formContext = getParentFormContext(selectedControl);
        return Spaarke_EnableNewAnalysis(formContext);
    } catch (error) {
        console.error("[Spaarke.Analysis] EnableNewAnalysisSubgrid Error:", error);
        return false;
    }
}

/**
 * Visibility rule: Always show button
 *
 * @returns {boolean} True to show button
 */
function Spaarke_ShowNewAnalysis() {
    return true;
}

// ============================================================================
// DEPLOYMENT NOTES
// ============================================================================

/*
RIBBON CONFIGURATION:

1. Button Location Options:
   a) Form Command Bar: Mscrm.Form.sprk_document.MainTab.Actions.Controls._children
   b) Subgrid Command Bar: Mscrm.SubGrid.sprk_analysis.MainTab.Actions.Controls._children

2. Command Definition:
   - Command ID: Spaarke.Analysis.NewAnalysis
   - JavaScript Function: Spaarke_NewAnalysis
   - Library: sprk_analysis_commands.js
   - CrmParameter: PrimaryControl (for form) or SelectedControl (for subgrid)

3. Enable Rule:
   - Function: Spaarke_EnableNewAnalysis (form) or Spaarke_EnableNewAnalysisSubgrid (subgrid)
   - Library: sprk_analysis_commands.js
   - CrmParameter: PrimaryControl or SelectedControl

4. Button Properties:
   - Label: "+ New Analysis"
   - Tooltip: "Create a new AI-powered document analysis"
   - Icon: AnalysisAdd or custom icon

CUSTOM PAGE NAMES:
- Analysis Builder: sprk_analysisbuilder_40af8
- Analysis Workspace: sprk_analysisworkspace_8bc0b

VERSION HISTORY:
- 1.0.0: Initial release for AI Document Intelligence Phase 2
*/
