/**
 * Event Ribbon Commands - Task Completion Workflow
 *
 * Web resource for Event entity ribbon command bar buttons:
 *
 * Main Form Buttons:
 * - Complete: Mark event as complete
 * - Cancel: Cancel event with optional reason
 * - Reschedule: Change event due date
 * - Reassign: Assign event to another user
 * - Add Memo: Create a memo for the event
 * - Close: Mark event as closed (no action taken)
 * - Archive: Archive event (sets status + deactivates)
 * - On Hold: Put event on hold
 * - Resume: Resume event from on hold
 *
 * HomepageGrid Buttons (Entity Main View - bulk operations):
 * - Complete: Mark selected events as complete
 * - Close: Close selected events
 * - Cancel: Cancel selected events
 * - On Hold: Put selected events on hold
 * - Archive: Archive selected events
 *
 * Event Status Values (sprk_eventstatus):
 * - 0: Draft, 1: Open, 2: Completed, 3: Closed, 4: On Hold
 * - 5: Cancelled, 6: Reassigned, 7: Archived
 *
 * Event History is stored as a JSON array in sprk_eventhistory field (Multiline Text).
 *
 * @see projects/events-workspace-apps-UX-r1/notes/design/event-completion-workflow.md
 * @see projects/events-workspace-apps-UX-r1/notes/design/statecode-statuscode-migration.md
 * @see docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md
 *
 * Deployment:
 * 1. Upload as web resource: sprk_event_ribbon_commands
 * 2. Add ribbon XML to Event entity (see EventRibbonDiffXml.xml)
 * 3. Publish customizations
 */

/* eslint-disable no-undef */
"use strict";

// Namespace for Spaarke Event commands
var Spaarke = Spaarke || {};
Spaarke.Event = Spaarke.Event || {};

/**
 * Event Status values (sprk_eventstatus custom field)
 * Replaces OOB statecode/statuscode for better control
 */
Spaarke.Event.EventStatus = {
    DRAFT: 0,
    OPEN: 1,
    COMPLETED: 2,
    CLOSED: 3,
    ON_HOLD: 4,
    CANCELLED: 5,
    REASSIGNED: 6,
    ARCHIVED: 7
};

/**
 * OOB State codes (only used when archiving)
 */
Spaarke.Event.StateCode = {
    ACTIVE: 0,
    INACTIVE: 1
};

/**
 * Action types for Event History JSON entries
 */
Spaarke.Event.ActionType = {
    COMPLETED: "completed",
    CANCELLED: "cancelled",
    RESCHEDULED: "rescheduled",
    REASSIGNED: "reassigned",
    MEMO_ADDED: "memo_added",
    STATUS_CHANGED: "status_changed"
};

// ─────────────────────────────────────────────────────────────────────────────
// Event History Helper (JSON field approach)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Create a new history entry object
 * @param {string} action - Action type (completed, cancelled, etc.)
 * @param {string} details - Optional details/reason
 * @returns {Object} History entry object
 */
Spaarke.Event._createHistoryEntry = function(action, details) {
    var xrm = (window.Xrm || window.parent.Xrm);
    var userSettings = xrm.Utility.getGlobalContext().userSettings;

    return {
        timestamp: new Date().toISOString(),
        action: action,
        userId: userSettings.userId.replace(/[{}]/g, ''),
        userName: userSettings.userName,
        details: details || null
    };
};

/**
 * Append a history entry to the sprk_eventhistory JSON field
 * @param {Object} formContext - The form context
 * @param {string} action - Action type
 * @param {string} details - Optional details/reason
 */
Spaarke.Event._appendHistoryEntry = function(formContext, action, details) {
    var historyAttr = formContext.getAttribute("sprk_eventhistory");
    if (!historyAttr) {
        console.warn("[Event Commands] sprk_eventhistory field not found on form");
        return;
    }

    // Get existing history or start empty array
    var existingHistory = [];
    var currentValue = historyAttr.getValue();
    if (currentValue) {
        try {
            existingHistory = JSON.parse(currentValue);
            if (!Array.isArray(existingHistory)) {
                existingHistory = [];
            }
        } catch (e) {
            console.warn("[Event Commands] Could not parse existing history, starting fresh");
            existingHistory = [];
        }
    }

    // Create and prepend new entry (most recent first)
    var newEntry = Spaarke.Event._createHistoryEntry(action, details);
    existingHistory.unshift(newEntry);

    // Update field
    historyAttr.setValue(JSON.stringify(existingHistory, null, 2));
    console.log("[Event Commands] History entry added:", newEntry);
};

// ─────────────────────────────────────────────────────────────────────────────
// Enable Rules (used by ribbon to enable/disable buttons)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Check if the current event is in an active state (can be completed/cancelled)
 * Active states: Draft, Open, On Hold
 * @param {Object} formContext - The form context (PrimaryControl)
 * @returns {boolean} True if event is active and can be modified
 */
Spaarke.Event.IsEventActive = function(formContext) {
    if (!formContext || !formContext.data || !formContext.data.entity) {
        return false;
    }

    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (!eventStatusAttr) {
        // Fallback: check statecode if sprk_eventstatus not on form
        var stateCode = formContext.getAttribute("statecode");
        return stateCode && stateCode.getValue() === Spaarke.Event.StateCode.ACTIVE;
    }

    var currentStatus = eventStatusAttr.getValue();
    // Active states that allow completion/cancellation
    var activeStatuses = [
        Spaarke.Event.EventStatus.DRAFT,
        Spaarke.Event.EventStatus.OPEN,
        Spaarke.Event.EventStatus.ON_HOLD
    ];
    return activeStatuses.indexOf(currentStatus) !== -1;
};

/**
 * Check if the current event can be rescheduled
 * @param {Object} formContext - The form context (PrimaryControl)
 * @returns {boolean} True if event can be rescheduled
 */
Spaarke.Event.CanReschedule = function(formContext) {
    // Can reschedule if active and has a due date
    if (!Spaarke.Event.IsEventActive(formContext)) {
        return false;
    }

    var dueDate = formContext.getAttribute("sprk_duedate");
    return dueDate && dueDate.getValue() !== null;
};

// ─────────────────────────────────────────────────────────────────────────────
// Command Actions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Complete Event
 * Opens custom dialog for completion with date and optional notes
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.CompleteEvent = function(formContext) {
    var eventId = formContext.data.entity.getId().replace(/[{}]/g, '');
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "Event";
    var dueDateAttr = formContext.getAttribute("sprk_duedate");
    var dueDate = dueDateAttr && dueDateAttr.getValue() ? dueDateAttr.getValue().toISOString() : "";

    // Build URL with parameters
    var dialogUrl = "sprk_event_complete_dialog.html" +
        "?eventId=" + encodeURIComponent(eventId) +
        "&eventName=" + encodeURIComponent(eventName) +
        "&dueDate=" + encodeURIComponent(dueDate);

    // Set up message listener for dialog response
    var messageHandler = function(event) {
        if (event.data && event.data.type === "EVENT_COMPLETE_DIALOG_RESULT") {
            window.removeEventListener("message", messageHandler);
            if (event.data.data.confirmed) {
                Spaarke.Event._executeComplete(
                    formContext,
                    event.data.data.completedDate,
                    event.data.data.notes
                );
            }
        }
    };
    window.addEventListener("message", messageHandler);

    // Open custom dialog
    Xrm.Navigation.openWebResource(dialogUrl, {
        openInNewWindow: false,
        width: 450,
        height: 380
    });
};

/**
 * Internal: Execute the complete action
 * @param {Object} formContext - The form context
 * @param {string} completedDateStr - Optional completed date string (YYYY-MM-DD)
 * @param {string} notes - Optional completion notes
 * @private
 */
Spaarke.Event._executeComplete = function(formContext, completedDateStr, notes) {
    var completedDate = completedDateStr ? new Date(completedDateStr) : new Date();

    // Update Event Status (custom field)
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.COMPLETED);
    }

    // Set completed date
    var completedDateAttr = formContext.getAttribute("sprk_completeddate");
    if (completedDateAttr) {
        completedDateAttr.setValue(completedDate);
    }

    // Add history entry with optional notes
    var details = notes ? "Notes: " + notes : null;
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.COMPLETED,
        details
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2, // Success
            level: 1,
            message: "Event completed successfully",
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Complete save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to complete event: " + error.message
        });
    });
};

/**
 * Cancel Event
 * Sets status to Cancelled and adds history entry with reason
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.CancelEvent = function(formContext) {
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "Event";

    // Confirm action (for more sophisticated dialog with reason input, use custom HTML dialog)
    Xrm.Navigation.openConfirmDialog({
        title: "Cancel Event",
        text: "Are you sure you want to cancel \"" + eventName + "\"?",
        confirmButtonLabel: "Cancel Event",
        cancelButtonLabel: "Keep Active"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event._executeCancel(formContext, "");
        }
    });
};

/**
 * Internal: Execute the cancel action
 * @private
 */
Spaarke.Event._executeCancel = function(formContext, reason) {
    // Update Event Status (custom field)
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.CANCELLED);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.CANCELLED,
        reason || null
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event cancelled",
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Cancel save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to cancel event: " + error.message
        });
    });
};

/**
 * Reschedule Event
 * Opens a dialog to set new due date, updates event, adds history entry
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.RescheduleEvent = function(formContext) {
    var dueDateAttr = formContext.getAttribute("sprk_duedate");
    var currentDueDate = dueDateAttr ? dueDateAttr.getValue() : null;

    // For now, use a simple prompt approach
    // TODO: Replace with custom dialog web resource for better UX
    var newDateStr = prompt(
        "Enter new due date (YYYY-MM-DD):",
        currentDueDate ? currentDueDate.toISOString().split('T')[0] : ""
    );

    if (newDateStr) {
        var newDate = new Date(newDateStr);
        if (isNaN(newDate.getTime())) {
            Xrm.Navigation.openAlertDialog({
                title: "Invalid Date",
                text: "Please enter a valid date in YYYY-MM-DD format."
            });
            return;
        }

        Spaarke.Event._executeReschedule(formContext, newDate, currentDueDate);
    }
};

/**
 * Internal: Execute the reschedule action
 * @private
 */
Spaarke.Event._executeReschedule = function(formContext, newDate, oldDate) {
    var details = "Due date changed";
    if (oldDate) {
        details += " from " + oldDate.toLocaleDateString() + " to " + newDate.toLocaleDateString();
    } else {
        details += " to " + newDate.toLocaleDateString();
    }

    // Update due date
    var dueDateAttr = formContext.getAttribute("sprk_duedate");
    if (dueDateAttr) {
        dueDateAttr.setValue(newDate);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.RESCHEDULED,
        details
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event rescheduled to " + newDate.toLocaleDateString(),
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Reschedule save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to reschedule event: " + error.message
        });
    });
};

/**
 * Reassign Event
 * Opens user lookup and reassigns the event to selected user
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.ReassignEvent = function(formContext) {
    // Open user lookup
    Xrm.Utility.lookupObjects({
        entityTypes: ["systemuser"],
        allowMultiSelect: false,
        defaultViewId: null
    }).then(function(results) {
        if (results && results.length > 0) {
            var newOwner = results[0];
            Spaarke.Event._executeReassign(formContext, newOwner);
        }
    }).catch(function(error) {
        console.error("[Event Commands] User lookup failed:", error);
    });
};

/**
 * Internal: Execute the reassign action
 * Sets Event Status to REASSIGNED and updates owner
 * @private
 */
Spaarke.Event._executeReassign = function(formContext, newOwner) {
    // Get current owner for history
    var ownerAttr = formContext.getAttribute("ownerid");
    var currentOwner = ownerAttr ? ownerAttr.getValue() : null;
    var details = "Reassigned to " + newOwner.name;
    if (currentOwner && currentOwner.length > 0) {
        details = "Reassigned from " + currentOwner[0].name + " to " + newOwner.name;
    }

    // Update Event Status to Reassigned
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.REASSIGNED);
    }

    // Update owner
    if (ownerAttr) {
        ownerAttr.setValue([{
            id: newOwner.id.replace(/[{}]/g, ''),
            name: newOwner.name,
            entityType: newOwner.entityType || "systemuser"
        }]);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.REASSIGNED,
        details
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event reassigned to " + newOwner.name,
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Reassign save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to reassign event: " + error.message
        });
    });
};

/**
 * Add Memo to Event
 * Opens Quick Create form for Memo entity pre-populated with event reference
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.AddMemo = function(formContext) {
    var eventId = formContext.data.entity.getId().replace(/[{}]/g, '');
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "";

    // Open Quick Create form for Memo entity
    Xrm.Navigation.openForm({
        entityName: "sprk_memo",
        useQuickCreateForm: true,
        formId: null, // Use default quick create form
        createFromEntity: {
            entityType: "sprk_event",
            id: eventId,
            name: eventName
        }
    }).then(function(result) {
        if (result.savedEntityReference && result.savedEntityReference.length > 0) {
            // Memo was created - add history entry
            Spaarke.Event._appendHistoryEntry(
                formContext,
                Spaarke.Event.ActionType.MEMO_ADDED,
                "Memo: " + (result.savedEntityReference[0].name || "New memo")
            );

            // Save to persist the history entry
            formContext.data.save().then(function() {
                Xrm.App.addGlobalNotification({
                    type: 2,
                    level: 1,
                    message: "Memo added to event",
                    showCloseButton: true
                });
            });
        }
    }).catch(function(error) {
        console.error("[Event Commands] Add Memo form failed:", error);
    });
};

/**
 * Close Event
 * Sets status to Closed (no action taken or required)
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.CloseEvent = function(formContext) {
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "Event";

    Xrm.Navigation.openConfirmDialog({
        title: "Close Event",
        text: "Close \"" + eventName + "\" without action?\n\nThis indicates no action was taken or required.",
        confirmButtonLabel: "Close Event",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event._executeClose(formContext);
        }
    });
};

/**
 * Internal: Execute the close action
 * @private
 */
Spaarke.Event._executeClose = function(formContext) {
    // Update Event Status
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.CLOSED);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.STATUS_CHANGED,
        "Closed - no action taken or required"
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event closed",
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Close save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to close event: " + error.message
        });
    });
};

/**
 * Archive Event
 * Sets status to Archived AND sets statecode to Inactive to hide the record
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.ArchiveEvent = function(formContext) {
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "Event";

    Xrm.Navigation.openConfirmDialog({
        title: "Archive Event",
        text: "Archive \"" + eventName + "\"?\n\nThis will hide the event from active views. The record will not be deleted.",
        confirmButtonLabel: "Archive",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event._executeArchive(formContext);
        }
    });
};

/**
 * Internal: Execute the archive action
 * Sets both sprk_eventstatus to Archived AND statecode to Inactive
 * @private
 */
Spaarke.Event._executeArchive = function(formContext) {
    var eventId = formContext.data.entity.getId().replace(/[{}]/g, '');

    // Update Event Status
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.ARCHIVED);
    }

    // Add history entry before deactivating
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.STATUS_CHANGED,
        "Archived"
    );

    // Save first, then deactivate (SetState)
    formContext.data.save().then(function() {
        // Use SetState request to deactivate the record
        return Xrm.WebApi.updateRecord("sprk_event", eventId, {
            statecode: Spaarke.Event.StateCode.INACTIVE,
            statuscode: 2 // Inactive status code (may need adjustment based on entity config)
        });
    }).then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event archived",
            showCloseButton: true
        });
        // Refresh form to show inactive state
        formContext.data.refresh(false);
    }).catch(function(error) {
        console.error("[Event Commands] Archive failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to archive event: " + error.message
        });
    });
};

/**
 * Put Event On Hold
 * Sets status to On Hold (temporarily paused)
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.PutOnHold = function(formContext) {
    var eventNameAttr = formContext.getAttribute("sprk_name");
    var eventName = eventNameAttr ? eventNameAttr.getValue() : "Event";

    Xrm.Navigation.openConfirmDialog({
        title: "Put Event On Hold",
        text: "Put \"" + eventName + "\" on hold?",
        confirmButtonLabel: "Put On Hold",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event._executePutOnHold(formContext);
        }
    });
};

/**
 * Internal: Execute put on hold action
 * @private
 */
Spaarke.Event._executePutOnHold = function(formContext) {
    // Update Event Status
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.ON_HOLD);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.STATUS_CHANGED,
        "Put on hold"
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event put on hold",
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Put on hold save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to put event on hold: " + error.message
        });
    });
};

/**
 * Resume Event (from On Hold)
 * Sets status back to Open
 * @param {Object} formContext - The form context (PrimaryControl)
 */
Spaarke.Event.ResumeEvent = function(formContext) {
    // Update Event Status
    var eventStatusAttr = formContext.getAttribute("sprk_eventstatus");
    if (eventStatusAttr) {
        eventStatusAttr.setValue(Spaarke.Event.EventStatus.OPEN);
    }

    // Add history entry
    Spaarke.Event._appendHistoryEntry(
        formContext,
        Spaarke.Event.ActionType.STATUS_CHANGED,
        "Resumed from on hold"
    );

    // Save the form
    formContext.data.save().then(function() {
        Xrm.App.addGlobalNotification({
            type: 2,
            level: 1,
            message: "Event resumed",
            showCloseButton: true
        });
    }).catch(function(error) {
        console.error("[Event Commands] Resume save failed:", error);
        Xrm.Navigation.openAlertDialog({
            title: "Error",
            text: "Failed to resume event: " + error.message
        });
    });
};

// ─────────────────────────────────────────────────────────────────────────────
// Grid Command Support (for bulk actions from Events grid)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Complete multiple events from grid selection
 * @param {Object} selectedControl - The grid control with selected rows
 */
Spaarke.Event.CompleteSelectedEvents = function(selectedControl) {
    var selectedIds = selectedControl.getSelectedRows().getAll().map(function(row) {
        return row.getData().getEntity().getId().replace(/[{}]/g, '');
    });

    if (selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to complete."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Complete Events",
        text: "Mark " + selectedIds.length + " event(s) as complete?",
        confirmButtonLabel: "Complete All",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event._executeBulkComplete(selectedIds, selectedControl);
        }
    });
};

/**
 * Internal: Execute bulk complete
 * Note: Bulk complete updates status but cannot easily append to JSON history field
 * For full audit trail on bulk operations, consider a Power Automate flow
 * @private
 */
Spaarke.Event._executeBulkComplete = function(eventIds, gridControl) {
    var now = new Date().toISOString();
    var promises = eventIds.map(function(eventId) {
        return Xrm.WebApi.updateRecord("sprk_event", eventId, {
            sprk_eventstatus: Spaarke.Event.EventStatus.COMPLETED,
            sprk_completeddate: now
            // Note: Cannot append to sprk_eventhistory JSON via WebApi without read-modify-write
            // For bulk history entries, consider a Power Automate flow triggered on status change
        });
    });

    Promise.all(promises)
        .then(function() {
            gridControl.refresh();
            Xrm.App.addGlobalNotification({
                type: 2,
                level: 1,
                message: eventIds.length + " event(s) completed",
                showCloseButton: true
            });
        })
        .catch(function(error) {
            console.error("[Event Commands] Bulk complete failed:", error);
            Xrm.Navigation.openAlertDialog({
                title: "Error",
                text: "Some events failed to complete: " + error.message
            });
            gridControl.refresh();
        });
};

// ─────────────────────────────────────────────────────────────────────────────
// Homepage Grid Commands (Entity main view - bulk operations)
// These functions receive SelectedControlSelectedItemIds (array of GUIDs)
// and SelectedEntityTypeName (entity name string)
// ─────────────────────────────────────────────────────────────────────────────

Spaarke.Event.Homepage = Spaarke.Event.Homepage || {};

/**
 * Complete selected events from HomepageGrid (Entity main view)
 * @param {string[]} selectedIds - Array of selected event GUIDs
 * @param {string} entityName - Entity type name (sprk_event)
 */
Spaarke.Event.Homepage.CompleteSelected = function(selectedIds, entityName) {
    if (!selectedIds || selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to complete."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Complete Events",
        text: "Mark " + selectedIds.length + " event(s) as complete?",
        confirmButtonLabel: "Complete",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event.Homepage._executeBulkStatusUpdate(
                selectedIds,
                Spaarke.Event.EventStatus.COMPLETED,
                "Completed",
                { sprk_completeddate: new Date().toISOString() }
            );
        }
    });
};

/**
 * Close selected events from HomepageGrid
 * @param {string[]} selectedIds - Array of selected event GUIDs
 * @param {string} entityName - Entity type name
 */
Spaarke.Event.Homepage.CloseSelected = function(selectedIds, entityName) {
    if (!selectedIds || selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to close."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Close Events",
        text: "Close " + selectedIds.length + " event(s) without action?",
        confirmButtonLabel: "Close",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event.Homepage._executeBulkStatusUpdate(
                selectedIds,
                Spaarke.Event.EventStatus.CLOSED,
                "Closed"
            );
        }
    });
};

/**
 * Cancel selected events from HomepageGrid
 * @param {string[]} selectedIds - Array of selected event GUIDs
 * @param {string} entityName - Entity type name
 */
Spaarke.Event.Homepage.CancelSelected = function(selectedIds, entityName) {
    if (!selectedIds || selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to cancel."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Cancel Events",
        text: "Cancel " + selectedIds.length + " event(s)?",
        confirmButtonLabel: "Cancel Events",
        cancelButtonLabel: "Keep Active"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event.Homepage._executeBulkStatusUpdate(
                selectedIds,
                Spaarke.Event.EventStatus.CANCELLED,
                "Cancelled"
            );
        }
    });
};

/**
 * Put selected events on hold from HomepageGrid
 * @param {string[]} selectedIds - Array of selected event GUIDs
 * @param {string} entityName - Entity type name
 */
Spaarke.Event.Homepage.OnHoldSelected = function(selectedIds, entityName) {
    if (!selectedIds || selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to put on hold."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Put Events On Hold",
        text: "Put " + selectedIds.length + " event(s) on hold?",
        confirmButtonLabel: "Put On Hold",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event.Homepage._executeBulkStatusUpdate(
                selectedIds,
                Spaarke.Event.EventStatus.ON_HOLD,
                "On Hold"
            );
        }
    });
};

/**
 * Archive selected events from HomepageGrid
 * Sets both sprk_eventstatus=Archived AND statecode=Inactive
 * @param {string[]} selectedIds - Array of selected event GUIDs
 * @param {string} entityName - Entity type name
 */
Spaarke.Event.Homepage.ArchiveSelected = function(selectedIds, entityName) {
    if (!selectedIds || selectedIds.length === 0) {
        Xrm.Navigation.openAlertDialog({
            title: "No Selection",
            text: "Please select one or more events to archive."
        });
        return;
    }

    Xrm.Navigation.openConfirmDialog({
        title: "Archive Events",
        text: "Archive " + selectedIds.length + " event(s)?\n\nThis will hide them from active views.",
        confirmButtonLabel: "Archive",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            Spaarke.Event.Homepage._executeBulkArchive(selectedIds);
        }
    });
};

/**
 * Internal: Execute bulk status update for HomepageGrid
 * @param {string[]} eventIds - Array of event GUIDs
 * @param {number} newStatus - New sprk_eventstatus value
 * @param {string} statusLabel - Status label for notification message
 * @param {Object} additionalFields - Optional additional fields to update
 * @private
 */
Spaarke.Event.Homepage._executeBulkStatusUpdate = function(eventIds, newStatus, statusLabel, additionalFields) {
    var updateData = {
        sprk_eventstatus: newStatus
    };

    // Merge additional fields if provided
    if (additionalFields) {
        for (var key in additionalFields) {
            if (additionalFields.hasOwnProperty(key)) {
                updateData[key] = additionalFields[key];
            }
        }
    }

    // Clean the IDs (remove braces if present)
    var cleanIds = eventIds.map(function(id) {
        return id.replace(/[{}]/g, '');
    });

    var promises = cleanIds.map(function(eventId) {
        return Xrm.WebApi.updateRecord("sprk_event", eventId, updateData);
    });

    Promise.all(promises)
        .then(function() {
            Xrm.App.addGlobalNotification({
                type: 2,
                level: 1,
                message: eventIds.length + " event(s) set to " + statusLabel,
                showCloseButton: true
            });
            // Refresh the view
            Xrm.Utility.getGlobalContext().getQueryStringParameters();
            // Force page refresh to show updated data
            location.reload();
        })
        .catch(function(error) {
            console.error("[Event Commands] Bulk status update failed:", error);
            Xrm.Navigation.openAlertDialog({
                title: "Error",
                text: "Some events failed to update: " + error.message
            });
        });
};

/**
 * Internal: Execute bulk archive for HomepageGrid
 * Sets both sprk_eventstatus=Archived AND statecode=Inactive
 * @param {string[]} eventIds - Array of event GUIDs
 * @private
 */
Spaarke.Event.Homepage._executeBulkArchive = function(eventIds) {
    // Clean the IDs (remove braces if present)
    var cleanIds = eventIds.map(function(id) {
        return id.replace(/[{}]/g, '');
    });

    // For archive, we need to:
    // 1. Set sprk_eventstatus to Archived
    // 2. Set statecode to Inactive (deactivate)
    // This requires two operations: update then SetState (or use updateRecord with statecode)

    var promises = cleanIds.map(function(eventId) {
        // First update the custom status, then deactivate
        return Xrm.WebApi.updateRecord("sprk_event", eventId, {
            sprk_eventstatus: Spaarke.Event.EventStatus.ARCHIVED
        }).then(function() {
            // Deactivate the record
            return Xrm.WebApi.updateRecord("sprk_event", eventId, {
                statecode: Spaarke.Event.StateCode.INACTIVE,
                statuscode: 2 // Inactive status code
            });
        });
    });

    Promise.all(promises)
        .then(function() {
            Xrm.App.addGlobalNotification({
                type: 2,
                level: 1,
                message: eventIds.length + " event(s) archived",
                showCloseButton: true
            });
            // Refresh the view
            location.reload();
        })
        .catch(function(error) {
            console.error("[Event Commands] Bulk archive failed:", error);
            Xrm.Navigation.openAlertDialog({
                title: "Error",
                text: "Some events failed to archive: " + error.message
            });
        });
};

/* eslint-enable no-undef */
