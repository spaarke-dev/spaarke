/**
 * Universal Multi-Document Upload Command Script
 *
 * PURPOSE: Opens Custom Page Dialog for uploading multiple documents
 * WORKS WITH: Any parent entity (Matter, Project, Invoice, Account, Contact, etc.)
 * DEPLOYMENT: Classic Ribbon Workbench command button on Documents subgrid
 * ARCHITECTURE: Custom Page dialog approach with PCF control
 *
 * @version 3.0.3
 * @namespace Spaarke.Commands.Documents
 */

// ============================================================================
// ENTITY CONFIGURATION
// ============================================================================

/**
 * Configuration for each entity that supports document upload
 * ADD NEW ENTITIES HERE as they are configured
 */
const ENTITY_CONFIGURATIONS = {
    "sprk_matter": {
        entityLogicalName: "sprk_matter",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_matternumber", "sprk_name"],
        entityDisplayName: "Matter"
    },
    "sprk_project": {
        entityLogicalName: "sprk_project",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_projectname", "sprk_name"],
        entityDisplayName: "Project"
    },
    "sprk_invoice": {
        entityLogicalName: "sprk_invoice",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_invoicenumber", "name"],
        entityDisplayName: "Invoice"
    },
    "account": {
        entityLogicalName: "account",
        containerIdField: "sprk_containerid",
        displayNameFields: ["name"],
        entityDisplayName: "Account"
    },
    "contact": {
        entityLogicalName: "contact",
        containerIdField: "sprk_containerid",
        displayNameFields: ["fullname", "lastname", "firstname"],
        entityDisplayName: "Contact"
    }
};

/**
 * Get entity configuration by logical name
 * @param {string} entityName - Entity logical name
 * @returns {object|null} Entity configuration or null if not found
 */
function getEntityConfiguration(entityName) {
    return ENTITY_CONFIGURATIONS[entityName] || null;
}

// ============================================================================
// MAIN COMMAND FUNCTION
// ============================================================================

/**
 * Main command: Opens multi-document upload dialog
 * This is called by the ribbon button on Documents subgrid
 *
 * @param {object} selectedControl - The subgrid control (passed by ribbon)
 */
function Spaarke_AddMultipleDocuments(selectedControl) {
    try {
        console.log("[Spaarke] ========================================");
    console.log("[Spaarke] AddMultipleDocuments: Starting v3.0.3 - CUSTOM PAGE DIALOG");
        console.log("[Spaarke] ========================================");
        console.log("[Spaarke] Received selectedControl:", selectedControl);
        console.log("[Spaarke] selectedControl type:", typeof selectedControl);

        if (selectedControl) {
            console.log("[Spaarke] selectedControl constructor:", selectedControl.constructor ? selectedControl.constructor.name : "no constructor");
            console.log("[Spaarke] selectedControl keys:", Object.keys(selectedControl));
        } else {
            console.error("[Spaarke] selectedControl is NULL or UNDEFINED!");
        }

        // STEP 1: Get parent form context from subgrid
        const formContext = getParentFormContext(selectedControl);
        if (!formContext) {
            console.error("[Spaarke] ========================================");
            console.error("[Spaarke] FAILED to get form context");
            console.error("[Spaarke] ========================================");
            showErrorDialog("Unable to access parent form context. Please refresh the page and try again.");
            return;
        }

        console.log("[Spaarke] SUCCESS - Got form context!");

        // STEP 2: Get parent entity information
        const parentEntityName = formContext.data.entity.getEntityName();
        const parentRecordId = formContext.data.entity.getId();

        console.log("[Spaarke] Parent Entity:", parentEntityName);
        console.log("[Spaarke] Parent Record ID:", parentRecordId);

        // Validate parent record is saved
        if (!parentRecordId || parentRecordId === "" || parentRecordId === "{00000000-0000-0000-0000-000000000000}") {
            showErrorDialog("Please save the record before uploading documents.");
            return;
        }

        // Clean GUID (remove braces)
        const cleanParentRecordId = parentRecordId.replace(/[{}]/g, '');

        // STEP 3: Check if entity is configured
        const entityConfig = getEntityConfiguration(parentEntityName);
        if (!entityConfig) {
            showErrorDialog(
                "Document upload is not configured for this entity type (" + parentEntityName + ").\n\n" +
                "Please contact your system administrator."
            );
            return;
        }

        // STEP 4: Get parent display name
        const parentDisplayName = getParentDisplayName(formContext, entityConfig);
        console.log("[Spaarke] Parent Display Name:", parentDisplayName);

        // STEP 5: Get SharePoint container ID
        const containerId = getContainerId(formContext, entityConfig);
        console.log("[Spaarke] Container ID:", containerId);

        if (!containerId) {
            showErrorDialog(
                "This " + entityConfig.entityDisplayName + " is not configured for document storage.\n\n" +
                "Please configure a SharePoint container before uploading documents."
            );
            return;
        }

        // STEP 6: Open custom page dialog
        openDocumentUploadDialog({
            parentEntityName: parentEntityName,
            parentRecordId: cleanParentRecordId,
            containerId: containerId,
            parentDisplayName: parentDisplayName
        }, selectedControl);

    } catch (error) {
        console.error("[Spaarke] AddMultipleDocuments Error:", error);
        showErrorDialog("An unexpected error occurred: " + error.message);
    }
}

// ============================================================================
// HELPER FUNCTIONS
// ============================================================================

/**
 * Get parent form context from subgrid control
 * Handles both modern and legacy API
 *
 * @param {object} selectedControl - Subgrid control
 * @returns {object|null} Form context or null
 */
function getParentFormContext(selectedControl) {
    if (!selectedControl) {
        console.error("[Spaarke] selectedControl is null or undefined");
        return null;
    }

    console.log("[Spaarke] selectedControl type:", typeof selectedControl);
    console.log("[Spaarke] selectedControl keys:", Object.keys(selectedControl || {}));

    // FIRST: Try the _formContext property directly (modern Dataverse has this)
    if (selectedControl._formContext) {
        console.log("[Spaarke] Using _formContext property");
        return selectedControl._formContext;
    }

    // Try modern API
    if (typeof selectedControl.getFormContext === "function") {
        console.log("[Spaarke] Using getFormContext() method");
        return selectedControl.getFormContext();
    }

    // Try getting form context via getParent (check if result has data property)
    if (typeof selectedControl.getParent === "function") {
        console.log("[Spaarke] Using getParent() method");
        const parent = selectedControl.getParent();
        if (parent && parent.data) {
            return parent;
        }
    }

    // Fallback to legacy _context property
    if (selectedControl._context) {
        console.log("[Spaarke] Using _context property");
        return selectedControl._context;
    }

    // Try accessing via getGrid().getParent()
    if (selectedControl.getGrid && typeof selectedControl.getGrid === "function") {
        const grid = selectedControl.getGrid();
        if (grid && typeof grid.getParent === "function") {
            console.log("[Spaarke] Using getGrid().getParent() method");
            return grid.getParent();
        }
    }

    console.error("[Spaarke] Could not retrieve form context from selectedControl");
    console.error("[Spaarke] Available methods:", Object.getOwnPropertyNames(selectedControl));
    return null;
}

/**
 * Get parent record display name
 * Tries multiple field names in priority order
 *
 * @param {object} formContext - Form context
 * @param {object} entityConfig - Entity configuration
 * @returns {string} Display name
 */
function getParentDisplayName(formContext, entityConfig) {
    // Try each display name field in priority order
    for (let i = 0; i < entityConfig.displayNameFields.length; i++) {
        const fieldName = entityConfig.displayNameFields[i];
        const attribute = formContext.getAttribute(fieldName);

        if (attribute && attribute.getValue()) {
            const value = attribute.getValue();

            // Handle different field types
            if (typeof value === "string") {
                return value;
            } else if (value && value.name) {
                // Lookup field
                return value.name;
            }
        }
    }

    // Fallback: Use entity type + partial record ID
    const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');
    return entityConfig.entityDisplayName + " (" + recordId.substring(0, 8) + "...)";
}

/**
 * Get SharePoint container ID from parent record
 *
 * @param {object} formContext - Form context
 * @param {object} entityConfig - Entity configuration
 * @returns {string|null} Container ID or null
 */
function getContainerId(formContext, entityConfig) {
    // Try configured container ID field
    const containerIdField = entityConfig.containerIdField;
    if (containerIdField) {
        const attribute = formContext.getAttribute(containerIdField);
        if (attribute && attribute.getValue()) {
            return attribute.getValue();
        }
    }

    // Fallback: Try common field name
    const fallbackAttribute = formContext.getAttribute("sprk_containerid");
    if (fallbackAttribute && fallbackAttribute.getValue()) {
        return fallbackAttribute.getValue();
    }

    return null;
}

/**
 * Open Custom Page Dialog for document upload
 * Uses Custom Page with PCF control (v3.0.3)
 *
 * @param {object} params - Dialog parameters
 * @param {object} selectedControl - Subgrid control for refresh
 * @version 3.0.3
 */
function openDocumentUploadDialog(params, selectedControl) {
    console.log("[Spaarke] Opening Custom Page Dialog with parameters:", params);

    // Try to get appId (may not be available in ribbon context)
    let appId = null;
    try {
        const globalContext = Xrm.Utility.getGlobalContext();
        if (globalContext && globalContext.client && typeof globalContext.client.getAppId === 'function') {
            appId = globalContext.client.getAppId();
            console.log("[Spaarke] Using appId:", appId);
        } else {
            console.log("[Spaarke] client.getAppId() not available in this context (ribbon commands), using default app resolution");
        }
    } catch (e) {
        console.log("[Spaarke] Could not get appId:", e.message);
    }

    // Fallback: try to parse appId from current window URL when navigateTo runs in-app
    if (!appId) {
        try {
            const currentUrl = window?.location?.href;
            const match = currentUrl ? currentUrl.match(/[?&]appid=([0-9a-f-]+)/i) : null;
            if (match && match[1]) {
                appId = match[1];
                console.log("[Spaarke] Parsed appId from window.location:", appId);
            }
        } catch (e) {
            console.log("[Spaarke] Could not parse appId from URL:", e.message);
        }
    }

    // Configure Custom Page navigation
    // NOTE: Using 'parameters' property so Custom Page can access values via Param("parameterName")
    // IMPORTANT: Ensure all values are serializable (use ?? "" for safety, clean GUID format)
    const dialogParameters = {
        parentEntityName: params?.parentEntityName ?? "",
        parentRecordId: (params?.parentRecordId ?? "").replace(/[{}]/g, "").toLowerCase(),
        containerId: params?.containerId ?? "",
        parentDisplayName: params?.parentDisplayName ?? ""
    };

    const pageInput = {
        pageType: "custom",
        name: "sprk_documentuploaddialog_e52db",  // Custom Page logical name (with Dataverse suffix)
        parameters: dialogParameters,
        data: JSON.stringify(dialogParameters)
    };

    if (appId) {
        pageInput.appId = appId; // Only include when available; null can break custom page resolution
    }

    // Configure dialog display options
    const navigationOptions = {
        target: 2,      // Dialog
        position: 2,    // Right side pane (Quick Create style)
        width: { value: 640, unit: 'px' }
        // Height removed - let Custom Page control its own height
    };

    console.log("[Spaarke] Page Input:", pageInput);
    console.log("[Spaarke] Navigation Options:", navigationOptions);

    // Navigate to Custom Page
    Xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
        function success(result) {
            console.log("[Spaarke] Custom Page Dialog closed successfully", result);

            // Refresh subgrid to show new documents
            if (selectedControl && typeof selectedControl.refresh === "function") {
                console.log("[Spaarke] Refreshing subgrid...");
                selectedControl.refresh();
            }

            // NOTE: Post-dialog success popup is disabled
            // User already sees success message inside dialog before it closes
            // Subgrid refresh provides visual confirmation of new records
        },
        function error(err) {
            // Dialog was cancelled or error occurred
            console.log("[Spaarke] Dialog error or cancelled", err);

            // Only show error if not user cancellation
            // errorCode 2 = user clicked Cancel or ESC
            if (err && err.errorCode !== 2) {
                showErrorDialog("Error opening document upload dialog: " + (err.message || "Unknown error"));
            }
        }
    );
}

/**
 * Show error dialog to user
 *
 * @param {string} message - Error message
 */
function showErrorDialog(message) {
    Xrm.Navigation.openAlertDialog({
        text: message,
        title: "Document Upload"
    });
}

/**
 * Show success dialog to user
 *
 * @param {string} message - Success message
 */
function showSuccessDialog(message) {
    Xrm.Navigation.openAlertDialog({
        text: message,
        title: "Success",
        confirmButtonLabel: "OK"
    });
}

// ============================================================================
// ENABLE/VISIBILITY RULES
// ============================================================================

/**
 * Enable rule: Only enable button if parent record is saved
 *
 * @param {object} selectedControl - Subgrid control
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableAddDocuments(selectedControl) {
    try {
        const formContext = getParentFormContext(selectedControl);
        if (!formContext) return false;

        // Only enable if parent record exists (is saved)
        const recordId = formContext.data.entity.getId();
        if (!recordId || recordId === "" || recordId === "{00000000-0000-0000-0000-000000000000}") {
            return false;
        }

        // Check if entity is configured
        const entityName = formContext.data.entity.getEntityName();
        const config = getEntityConfiguration(entityName);

        return config !== null;

    } catch (error) {
        console.error("[Spaarke] EnableAddDocuments Error:", error);
        return false;
    }
}

/**
 * Visibility rule: Always show button
 *
 * @returns {boolean} True to show button
 */
function Spaarke_ShowAddDocuments() {
    return true;
}

// ============================================================================
// DEPLOYMENT NOTES
// ============================================================================

/*
RIBBON WORKBENCH CONFIGURATION:

1. Create Command:
   - Command ID: Spaarke.Document.AddMultiple
   - Location: Mscrm.SubGrid.sprk_document.MainTab.Actions.Controls._children

2. Command Properties:
   - JavaScript Function: Spaarke_AddMultipleDocuments
   - Library: sprk_subgrid_commands.js (this file)
   - CrmParameter: SelectedControl (CRITICAL - must be first parameter)

3. Enable Rule:
   - Function: Spaarke_EnableAddDocuments
   - Library: sprk_subgrid_commands.js
   - CrmParameter: SelectedControl

4. Display Rule (optional):
   - Function: Spaarke_ShowAddDocuments
   - Library: sprk_subgrid_commands.js

5. Button Properties:
   - Label: "Add Documents"
   - Tooltip: "Upload multiple documents"
   - Icon: Use DocumentAdd icon or custom icon

TESTING CHECKLIST:
☐ Button appears on Documents subgrid
☐ Button disabled on unsaved records
☐ Button enabled on saved records
☐ Clicking button opens dialog
☐ Dialog receives correct parameters
☐ Subgrid refreshes after upload
☐ Works on Matter entity
☐ Works on Account entity
☐ Works on Contact entity
☐ Error handling works (no container ID, etc.)

ADDING NEW ENTITIES:
1. Add entity to ENTITY_CONFIGURATIONS object above
2. Ensure parent entity has sprk_containerid field
3. Ensure sprk_document has lookup field to parent entity
4. Add to EntityDocumentConfig.ts in PCF control
5. Test end-to-end flow

VERSION HISTORY:
- 2.1.0: Form Dialog approach using sprk_uploadcontext utility entity
- 2.0.0: Initial release for Custom Page architecture (deprecated)
*/
