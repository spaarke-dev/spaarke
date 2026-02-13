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
