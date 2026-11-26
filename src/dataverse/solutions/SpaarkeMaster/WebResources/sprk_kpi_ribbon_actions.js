/**
 * KPI Ribbon Actions - Button command handlers
 *
 * Web Resource Name: sprk_/scripts/kpi_ribbon_actions.js
 *
 * Handles the "+ Add KPI" button on the KPI Assessments subgrid of a matter or project.
 *
 * field-mapping-server-write-path-r1 task 051 (FR-24): the button no longer opens the KPI assessment quick create form. A
 * quick create saves AS THE USER through the platform, so the row would bypass the server's field mapping, owner rule and
 * stamp. The button now creates the KPI assessment through the BFF (POST /api/v1/child-records/sprk_kpiassessment, filed under
 * the matter or project) and opens the new row - the same command a subgrid "New KPI Assessment" runs
 * (Spaarke.SecureChild.Ribbon.newChild). The function names below are kept: the ribbon commands call them by name.
 *
 * The command must load, in order: sprk_/scripts/bff_auth.js, sprk_/scripts/assignedaccess_postsave.js and
 * sprk_/scripts/secure_child_ribbon.js (the shared BFF create) before this file. Without them nothing is created and the user is
 * told so; the button never falls back to a quick create or to Xrm.WebApi.
 *
 * @see projects/matter-performance-KPI-r1/tasks/045-add-add-kpi-ribbon-button.poml
 */

/* eslint-disable no-undef */
"use strict";

var Spaarke = Spaarke || {};
Spaarke.KpiRibbon = Spaarke.KpiRibbon || {};

/**
 * Add a KPI Assessment from a Matter form (creates it through the BFF, filed under the matter, then opens it).
 *
 * Called from: Ribbon button command (sprk.matter.subgrid.kpi.AddKpiButton.Command)
 * Parameter: PrimaryControl (form context)
 *
 * @param {Object} primaryControl - The form context (formContext)
 */
Spaarke.KpiRibbon.openQuickCreate = function (primaryControl) {
    return Spaarke.KpiRibbon._addKpi(primaryControl, "subgrid_kpiassessments");
};

/**
 * Add a KPI Assessment from a Project form (creates it through the BFF, filed under the project, then opens it).
 *
 * Called from: Ribbon button command (sprk.project.subgrid.kpi.AddKpiButton.Command)
 * Parameter: PrimaryControl (form context)
 *
 * @param {Object} primaryControl - The form context (formContext)
 */
Spaarke.KpiRibbon.openProjectQuickCreate = function (primaryControl) {
    return Spaarke.KpiRibbon._addKpi(primaryControl, "subgrid_kpiassessments");
};

/**
 * Internal: runs the BFF-backed "New KPI Assessment" for the form's record.
 *
 * @param {Object} primaryControl - The form context (formContext)
 * @param {string} subgridName - Name of the subgrid control (read for its relationship and refreshed after the create)
 */
Spaarke.KpiRibbon._addKpi = function (primaryControl, subgridName) {
    try {
        var ribbon = typeof Spaarke.SecureChild !== "undefined" ? Spaarke.SecureChild.Ribbon : null;
        if (!ribbon || !ribbon.newChild) {
            Xrm.Navigation.openAlertDialog({
                title: "Add KPI",
                text: "The create helper for this command is not loaded on this form. Reload the page and try again; if it " +
                    "persists, ask an administrator to check the command's libraries. Nothing was created."
            });
            return;
        }
        var subgrid = primaryControl.getControl ? primaryControl.getControl(subgridName) : null;
        ribbon.newChild(primaryControl, subgrid, "sprk_kpiassessment");
    } catch (error) {
        console.error("[KPI Ribbon] Error in Add KPI:", error);
    }
};

/* eslint-enable no-undef */
