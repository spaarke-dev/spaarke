/**
 * Update Related Commands - Ribbon JavaScript for Event entity
 *
 * Provides command bar functionality to push field mappings from Event to related records.
 * Calls POST /api/v1/field-mappings/push endpoint.
 *
 * Used by:
 * - Event HomepageGrid (main view - when records selected)
 * - Event Form (command bar)
 * - Event SubGrid (when Events appear on parent forms)
 *
 * @see spec.md - Push Mappings section
 * @see UpdateRelatedButton PCF for form-embedded version
 */

// API Configuration
var SPAARKE_UPDATE_RELATED_CONFIG = {
    apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net",
    entityLogicalName: "sprk_event"
};

/**
 * Enable rule for Update Related button on Homepage Grid
 * Returns true if at least one record is selected
 *
 * @param {Xrm.Controls.GridControl} selectedControl - The grid control
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableUpdateRelated_Grid(selectedControl) {
    if (!selectedControl) {
        return false;
    }

    try {
        var selectedRows = selectedControl.getGrid().getSelectedRows();
        return selectedRows && selectedRows.getLength() > 0;
    } catch (e) {
        console.error("[UpdateRelated] EnableRule error:", e);
        return false;
    }
}

/**
 * Enable rule for Update Related button on Form
 * Returns true if the form has a valid record ID
 *
 * @param {Xrm.FormContext} primaryControl - The form context
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableUpdateRelated_Form(primaryControl) {
    if (!primaryControl) {
        return false;
    }

    try {
        var entityId = primaryControl.data.entity.getId();
        return entityId && entityId.length > 0;
    } catch (e) {
        console.error("[UpdateRelated] EnableRule error:", e);
        return false;
    }
}

/**
 * Enable rule for Update Related button on SubGrid
 * Returns true if at least one record is selected in the subgrid
 *
 * @param {Xrm.Controls.GridControl} selectedControl - The subgrid control
 * @returns {boolean} True if button should be enabled
 */
function Spaarke_EnableUpdateRelated_SubGrid(selectedControl) {
    // Same logic as grid
    return Spaarke_EnableUpdateRelated_Grid(selectedControl);
}

/**
 * Update Related command for Homepage Grid (multiple selection supported)
 *
 * @param {Xrm.Controls.GridControl} selectedControl - The grid control
 */
function Spaarke_UpdateRelated_Grid(selectedControl) {
    if (!selectedControl) {
        Xrm.Navigation.openAlertDialog({ text: "No grid control available." });
        return;
    }

    var selectedRows = selectedControl.getGrid().getSelectedRows();
    if (!selectedRows || selectedRows.getLength() === 0) {
        Xrm.Navigation.openAlertDialog({ text: "Please select at least one record." });
        return;
    }

    // Collect selected record IDs
    var recordIds = [];
    selectedRows.forEach(function(row) {
        var entityRef = row.getData().getEntity().getEntityReference();
        recordIds.push(entityRef.id.replace("{", "").replace("}", ""));
    });

    // Confirm before executing
    var confirmMessage = recordIds.length === 1
        ? "This will update all related records with values from the selected Event.\n\nThis action cannot be undone. Continue?"
        : "This will update related records for " + recordIds.length + " selected Events.\n\nThis action cannot be undone. Continue?";

    Xrm.Navigation.openConfirmDialog({ text: confirmMessage, title: "Update Related Records" }).then(
        function(result) {
            if (result.confirmed) {
                Spaarke_ExecutePushMappings(recordIds, SPAARKE_UPDATE_RELATED_CONFIG.entityLogicalName);
            }
        }
    );
}

/**
 * Update Related command for Form (single record)
 *
 * @param {Xrm.FormContext} primaryControl - The form context
 */
function Spaarke_UpdateRelated_Form(primaryControl) {
    if (!primaryControl) {
        Xrm.Navigation.openAlertDialog({ text: "Form context not available." });
        return;
    }

    var entityId = primaryControl.data.entity.getId();
    if (!entityId) {
        Xrm.Navigation.openAlertDialog({ text: "Record must be saved before updating related records." });
        return;
    }

    // Clean GUID
    var cleanId = entityId.replace("{", "").replace("}", "");

    // Confirm before executing
    Xrm.Navigation.openConfirmDialog({
        text: "This will update all related records with values from this Event.\n\nThis action cannot be undone. Continue?",
        title: "Update Related Records"
    }).then(
        function(result) {
            if (result.confirmed) {
                Spaarke_ExecutePushMappings([cleanId], SPAARKE_UPDATE_RELATED_CONFIG.entityLogicalName);
            }
        }
    );
}

/**
 * Update Related command for SubGrid (multiple selection supported)
 *
 * @param {Xrm.Controls.GridControl} selectedControl - The subgrid control
 */
function Spaarke_UpdateRelated_SubGrid(selectedControl) {
    // Same logic as grid
    Spaarke_UpdateRelated_Grid(selectedControl);
}

/**
 * Execute push mappings API call for multiple records
 *
 * @param {string[]} recordIds - Array of record GUIDs
 * @param {string} entityLogicalName - Logical name of the source entity
 */
function Spaarke_ExecutePushMappings(recordIds, entityLogicalName) {
    // Show progress indicator
    Xrm.Utility.showProgressIndicator("Updating related records...");

    var successCount = 0;
    var failCount = 0;
    var totalCount = recordIds.length;
    var processedCount = 0;
    var errors = [];

    // Process each record
    recordIds.forEach(function(recordId) {
        var requestBody = {
            sourceEntity: entityLogicalName,
            sourceRecordId: recordId
        };

        fetch(SPAARKE_UPDATE_RELATED_CONFIG.apiBaseUrl + "/api/v1/field-mappings/push", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify(requestBody),
            credentials: "include"
        })
        .then(function(response) {
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            return response.json();
        })
        .then(function(data) {
            if (data.success) {
                successCount++;
            } else {
                failCount++;
                if (data.errors && data.errors.length > 0) {
                    errors.push(data.errors[0].error);
                }
            }
        })
        .catch(function(error) {
            failCount++;
            errors.push(error.message || "Unknown error");
        })
        .finally(function() {
            processedCount++;

            // Check if all records are processed
            if (processedCount === totalCount) {
                Xrm.Utility.closeProgressIndicator();
                Spaarke_ShowPushResults(successCount, failCount, errors);
            }
        });
    });
}

/**
 * Show results dialog after push operation completes
 *
 * @param {number} successCount - Number of successful updates
 * @param {number} failCount - Number of failed updates
 * @param {string[]} errors - Array of error messages
 */
function Spaarke_ShowPushResults(successCount, failCount, errors) {
    var message;

    if (failCount === 0) {
        message = "Successfully updated related records for " + successCount + " Event(s).";
        Xrm.Navigation.openAlertDialog({ text: message, title: "Update Complete" });
    } else if (successCount === 0) {
        message = "Failed to update related records.\n\n" + errors.slice(0, 3).join("\n");
        Xrm.Navigation.openAlertDialog({ text: message, title: "Update Failed" });
    } else {
        message = "Partially complete:\n" +
            "- " + successCount + " Event(s) updated successfully\n" +
            "- " + failCount + " Event(s) failed\n\n" +
            (errors.length > 0 ? "Errors: " + errors.slice(0, 3).join(", ") : "");
        Xrm.Navigation.openAlertDialog({ text: message, title: "Update Partially Complete" });
    }
}
