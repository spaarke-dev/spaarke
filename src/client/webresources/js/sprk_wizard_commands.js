// sprk_wizard_commands.js
// Ribbon command handlers for wizard Code Page launches
// Deployed as: sprk_wizard_commands

"use strict";

var Spaarke = Spaarke || {};
Spaarke.Commands = Spaarke.Commands || {};
Spaarke.Commands.Wizards = (function () {

  // Common dialog options — standardized to 60% x 70% per UAT feedback E-03
  var DIALOG_OPTIONS = {
    target: 2,
    width: { value: 60, unit: "%" },
    height: { value: 70, unit: "%" }
  };

  var SMALL_DIALOG_OPTIONS = {
    target: 2,
    width: { value: 60, unit: "%" },
    height: { value: 70, unit: "%" }
  };

  /**
   * Helper: extract entity context from PrimaryControl (form context)
   */
  function getEntityContext(primaryControl) {
    var formContext = primaryControl;
    if (formContext && formContext.data && formContext.data.entity) {
      var entity = formContext.data.entity;
      return {
        entityType: entity.getEntityName(),
        entityId: entity.getId().replace(/[{}]/g, ""),
        entityName: formContext.getAttribute && formContext.getAttribute("sprk_name")
          ? formContext.getAttribute("sprk_name").getValue()
          : ""
      };
    }
    return { entityType: "", entityId: "", entityName: "" };
  }

  /**
   * Helper: open a webresource dialog and refresh form on close
   */
  function openWizardDialog(primaryControl, webresourceName, data, title, options) {
    var dialogOptions = Object.assign({}, options || DIALOG_OPTIONS, { title: title });

    Xrm.Navigation.navigateTo(
      { pageType: "webresource", webresourceName: webresourceName, data: data },
      dialogOptions
    ).then(function () {
      // Refresh form data after dialog closes
      if (primaryControl && primaryControl.data) {
        primaryControl.data.refresh();
      }
    }).catch(function () {
      // User cancelled or error — still try to refresh
      if (primaryControl && primaryControl.data) {
        primaryControl.data.refresh();
      }
    });
  }

  return {
    openCreateMatterWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_creatematterwizard", data, "Create New Matter");
    },

    openCreateProjectWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_createprojectwizard", data, "Create New Project");
    },

    openCreateEventWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_createeventwizard", data, "Create New Event");
    },

    openCreateTodoWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_createtodowizard", data, "Create New To Do");
    },

    openDocumentUploadWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_documentuploadwizard", data, "Upload Documents");
    },

    openSummarizeFilesWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_summarizefileswizard", data, "Summarize Files");
    },

    openFindSimilarDialog: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_findsimilar", data, "Find Similar Documents", SMALL_DIALOG_OPTIONS);
    },

    openPlaybookLibrary: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardDialog(primaryControl, "sprk_playbooklibrary", data, "Playbook Library");
    },

    // Intent-based playbook launcher (used by ribbon buttons that pre-select a playbook)
    openPlaybookWithIntent: function (primaryControl, intent) {
      var ctx = getEntityContext(primaryControl);
      var data = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId + "&intent=" + intent;
      openWizardDialog(primaryControl, "sprk_playbooklibrary", data, "Playbook Library");
    }
  };
})();
