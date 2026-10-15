/**
 * KPI Ribbon Actions - Button command handlers
 *
 * Web Resource Name: sprk_/scripts/kpi_ribbon_actions.js
 *
 * Handles the "+ Add KPI" button on the KPI Assessments subgrid.
 * Opens Quick Create form for sprk_kpiassessment with the current
 * matter or project pre-populated in the appropriate lookup field.
 *
 * Supports both sprk_matter and sprk_project entities.
 *
 * @see projects/matter-performance-KPI-r1/tasks/045-add-add-kpi-ribbon-button.poml
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.KpiRibbon = Spaarke.KpiRibbon || {};

/**
 * Open the Quick Create form for KPI Assessment from a Matter form.
 * Pre-populates the sprk_matter lookup with the current matter record.
 *
 * Called from: Ribbon button command (sprk.matter.subgrid.kpi.AddKpiButton.Command)
 * Parameter: PrimaryControl (form context)
 *
 * @param {Object} primaryControl - The form context (formContext)
 */
Spaarke.KpiRibbon.openQuickCreate = function (primaryControl) {
    Spaarke.KpiRibbon._openQuickCreateForEntity(primaryControl, "sprk_matter", "subgrid_kpiassessments");
};

/**
 * Open the Quick Create form for KPI Assessment from a Project form.
 * Pre-populates the sprk_project lookup with the current project record.
 *
 * Called from: Ribbon button command (sprk.project.subgrid.kpi.AddKpiButton.Command)
 * Parameter: PrimaryControl (form context)
 *
 * @param {Object} primaryControl - The form context (formContext)
 */
Spaarke.KpiRibbon.openProjectQuickCreate = function (primaryControl) {
    Spaarke.KpiRibbon._openQuickCreateForEntity(primaryControl, "sprk_project", "subgrid_kpiassessments");
};

/**
 * Internal: Open Quick Create for KPI Assessment, pre-populating
 * the specified parent entity lookup field.
 *
 * @param {Object} primaryControl - The form context (formContext)
 * @param {string} parentEntityName - "sprk_matter" or "sprk_project"
 * @param {string} subgridName - Name of the subgrid control to refresh after save
 */
Spaarke.KpiRibbon._openQuickCreateForEntity = function (primaryControl, parentEntityName, subgridName) {
    try {
        var formContext = primaryControl;

        // Get the current record ID and name
        var recordId = formContext.data.entity.getId().replace(/[{}]/g, "");
        var recordName = formContext.data.entity.getPrimaryAttributeValue();

        // unified-access-control-r2 task 147 r1c (owner round 28 item 2, "E2"): the Quick Create saves AS THE USER, so the
        // KPI assessment would be owned by the user, in the user's business unit - under a SECURE record, readable by that
        // whole unit. It opens only when the record is read (through Xrm.WebApi) as NOT secure. Secure, or unreadable:
        // the BFF-backed "New KPI Assessment" (Spaarke.SecureChild.Ribbon) creates it instead (the ribbon's enable rule
        // already hides this button then; this is the script's own fail-closed check).
        return Spaarke.KpiRibbon._isNotSecure(parentEntityName, recordId).then(function (notSecure) {
            if (!notSecure) {
                var subgrid = formContext.getControl ? formContext.getControl(subgridName) : null;
                if (Spaarke.SecureChild && Spaarke.SecureChild.Ribbon && Spaarke.SecureChild.Ribbon.newChild) {
                    Spaarke.SecureChild.Ribbon.newChild(formContext, subgrid, "sprk_kpiassessment");
                } else {
                    Xrm.Navigation.openAlertDialog({
                        title: "Add KPI",
                        text: "This record is secure (or its security could not be checked). Use \"New KPI Assessment\" on " +
                            "the KPI Assessments list instead. Nothing was created."
                    });
                }
                return;
            }
            Spaarke.KpiRibbon._openQuickCreateForm(formContext, parentEntityName, subgridName, recordId, recordName);
        });
    } catch (error) {
        console.error("[KPI Ribbon] Error in openQuickCreate:", error);
    }
};

/**
 * Task 147 r1c: resolves true only when the record's sprk_issecure was read and is false; a read failure, an empty flag
 * or true resolve false (fail closed). Never rejects.
 */
Spaarke.KpiRibbon._isNotSecure = function (entityName, recordId) {
    try {
        return Xrm.WebApi.retrieveRecord(entityName, recordId, "?$select=sprk_issecure").then(function (row) {
            return !!row && row.sprk_issecure === false;
        }, function () {
            return false;
        });
    } catch (error) {
        return Promise.resolve(false);
    }
};

/** The original Quick Create launch (a record read as NOT secure only - see _openQuickCreateForEntity). */
Spaarke.KpiRibbon._openQuickCreateForm = function (formContext, parentEntityName, subgridName, recordId, recordName) {
    try {

        // Build the entity form options for Quick Create
        var entityFormOptions = {
            entityName: "sprk_kpiassessment",
            useQuickCreateForm: true
        };

        // Pre-populate the parent lookup field
        var formParameters = {};
        formParameters[parentEntityName] = recordId;
        formParameters[parentEntityName + "name"] = recordName;
        formParameters[parentEntityName + "type"] = parentEntityName;

        // Open the Quick Create form
        Xrm.Navigation.openForm(entityFormOptions, formParameters).then(
            function (result) {
                if (result.savedEntityReference && result.savedEntityReference.length > 0) {
                    console.log(
                        "[KPI Ribbon] Quick Create saved: " +
                        result.savedEntityReference[0].id
                    );
                    // Refresh the subgrid to show the new record
                    try {
                        var subgridControl = formContext.getControl(subgridName);
                        if (subgridControl) {
                            subgridControl.refresh();
                        }
                    } catch (refreshError) {
                        console.warn("[KPI Ribbon] Could not refresh subgrid:", refreshError);
                    }
                }
            },
            function (error) {
                console.error("[KPI Ribbon] Error opening Quick Create:", error);
            }
        );
    } catch (error) {
        console.error("[KPI Ribbon] Error in openQuickCreate:", error);
    }
};

/* eslint-enable no-undef */
