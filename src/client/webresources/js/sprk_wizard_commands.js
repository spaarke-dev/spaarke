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

  // ---------------------------------------------------------------------------
  // BFF Base URL resolution (E-02: propagate bffBaseUrl to all Code Pages)
  // ---------------------------------------------------------------------------

  /** Cached BFF API base URL — resolved once per page load and reused. */
  var _cachedBffBaseUrl = null;

  /**
   * Resolve the BFF API base URL from the Dataverse Environment Variable
   * "sprk_BffApiBaseUrl". Caches the result so subsequent calls are instant.
   *
   * @returns {Promise<string>} Resolved BFF base URL.
   */
  function getBffBaseUrl() {
    if (_cachedBffBaseUrl) {
      return Promise.resolve(_cachedBffBaseUrl);
    }

    var schemaName = "sprk_BffApiBaseUrl";

    return Xrm.WebApi.retrieveMultipleRecords(
      "environmentvariabledefinition",
      "?$filter=schemaname eq '" + schemaName + "'&$select=environmentvariabledefinitionid,defaultvalue"
    ).then(function (defResult) {
      if (!defResult.entities || defResult.entities.length === 0) {
        throw new Error(
          "[WizardCommands] Environment variable \"" + schemaName + "\" not found in Dataverse."
        );
      }

      var definition = defResult.entities[0];
      var definitionId = definition.environmentvariabledefinitionid;
      var defaultValue = definition.defaultvalue || null;

      return Xrm.WebApi.retrieveMultipleRecords(
        "environmentvariablevalue",
        "?$filter=_environmentvariabledefinitionid_value eq '" + definitionId + "'&$select=value"
      ).then(function (valResult) {
        var finalValue = null;
        if (valResult.entities && valResult.entities.length > 0) {
          finalValue = valResult.entities[0].value;
        } else {
          finalValue = defaultValue;
        }

        if (!finalValue) {
          throw new Error(
            "[WizardCommands] Environment variable \"" + schemaName + "\" has no value configured."
          );
        }

        _cachedBffBaseUrl = finalValue;
        return finalValue;
      });
    });
  }

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
   * Helper: resolve the SPE container ID from the current user's business unit.
   * Mirrors the resolution NavigationService.openAddDocument uses in the
   * SemanticSearchControl so ribbon and PCF launches behave identically.
   *
   * @returns {Promise<string>} Container ID, or '' if not resolvable.
   */
  function getContainerId() {
    try {
      var userId = Xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, "");
      return Xrm.WebApi.retrieveRecord("systemuser", userId, "?$select=_businessunitid_value").then(function (user) {
        var buId = user._businessunitid_value;
        if (!buId) return "";
        return Xrm.WebApi.retrieveRecord("businessunit", buId, "?$select=sprk_containerid").then(function (bu) {
          return bu.sprk_containerid || "";
        });
      });
    } catch (err) {
      console.warn("[WizardCommands] getContainerId failed:", err);
      return Promise.resolve("");
    }
  }

  /**
   * Helper: resolve the display name of the parent record so the wizard can
   * label its target. Falls back to '' when no recognized name field is present.
   */
  function getParentEntityName(entityType, entityId) {
    if (!entityType || !entityId) return Promise.resolve("");
    return Xrm.WebApi.retrieveRecord(
      entityType,
      entityId,
      "?$select=sprk_mattername,sprk_projectname,sprk_matternumber,sprk_projectnumber,name"
    ).then(function (record) {
      return record.sprk_mattername
        || record.sprk_projectname
        || record.sprk_matternumber
        || record.sprk_projectnumber
        || record.name
        || "";
    }).catch(function () { return ""; });
  }

  /**
   * Helper: detect dark mode (parity with ThemeService.getEffectiveDarkMode).
   * Light fallback if anything is unavailable.
   */
  function getTheme() {
    try {
      var stored = window.localStorage && window.localStorage.getItem("spaarke_theme");
      if (stored === "dark" || stored === "light") return stored;
      var bg = window.getComputedStyle(document.body).backgroundColor || "";
      var m = bg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)/);
      if (m) {
        var lum = (0.2126 * +m[1] + 0.7152 * +m[2] + 0.0722 * +m[3]) / 255;
        if (lum < 0.5) return "dark";
      }
    } catch (e) { /* ignore */ }
    return "light";
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

  /**
   * Helper: resolve bffBaseUrl then open wizard dialog.
   * Includes bffBaseUrl as a query param in the data string.
   *
   * @param {object} primaryControl - Xrm form/grid context for post-close refresh.
   * @param {string} webresourceName - Target Code Page web resource name.
   * @param {string} baseData - Query string params without bffBaseUrl (e.g. "entityType=sprk_matter&entityId=...").
   * @param {string} title - Dialog title bar text.
   * @param {object} [options] - Optional dialog size options (defaults to DIALOG_OPTIONS).
   */
  function openWizardWithBff(primaryControl, webresourceName, baseData, title, options) {
    getBffBaseUrl().then(function (bffBaseUrl) {
      var data = (baseData ? baseData + "&" : "") + "bffBaseUrl=" + encodeURIComponent(bffBaseUrl);
      openWizardDialog(primaryControl, webresourceName, data, title, options);
    }).catch(function (err) {
      console.error("[WizardCommands] Failed to resolve BFF base URL, opening wizard without it:", err);
      openWizardDialog(primaryControl, webresourceName, baseData, title, options);
    });
  }

  return {
    openCreateMatterWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_creatematterwizard", baseData, "Create New Matter");
    },

    openCreateProjectWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_createprojectwizard", baseData, "Create New Project");
    },

    openCreateEventWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_createeventwizard", baseData, "Create New Event");
    },

    openCreateTodoWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_createtodowizard", baseData, "Create New To Do");
    },

    openDocumentUploadWizard: function (primaryControl) {
      // The wizard reads parentEntityType / parentEntityId / parentEntityName /
      // containerId (NOT entityType/entityId). Without these it mounts with
      // empty context and the upload never associates to the parent record.
      var ctx = getEntityContext(primaryControl);
      if (!ctx.entityType || !ctx.entityId) {
        console.error("[WizardCommands] openDocumentUploadWizard: no parent record context");
        return;
      }
      Promise.all([
        getContainerId(),
        getParentEntityName(ctx.entityType, ctx.entityId)
      ]).then(function (results) {
        var containerId = results[0];
        var parentEntityName = results[1];
        if (!containerId) {
          Xrm.Navigation.openAlertDialog({
            title: "Upload Documents",
            text: "Cannot upload: no SPE container is configured for your business unit (sprk_containerid)."
          });
          return;
        }
        var baseData =
          "parentEntityType=" + encodeURIComponent(ctx.entityType) +
          "&parentEntityId=" + encodeURIComponent(ctx.entityId) +
          "&parentEntityName=" + encodeURIComponent(parentEntityName) +
          "&containerId=" + encodeURIComponent(containerId) +
          "&theme=" + getTheme();
        openWizardWithBff(primaryControl, "sprk_documentuploadwizard", baseData, "Upload Documents");
      }).catch(function (err) {
        console.error("[WizardCommands] openDocumentUploadWizard failed:", err);
      });
    },

    openSummarizeFilesWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_summarizefileswizard", baseData, "Summarize Files");
    },

    openFindSimilarDialog: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_findsimilar", baseData, "Find Similar Documents", SMALL_DIALOG_OPTIONS);
    },

    openPlaybookLibrary: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId;
      openWizardWithBff(primaryControl, "sprk_playbooklibrary", baseData, "Playbook Library");
    },

    // Intent-based playbook launcher (used by ribbon buttons that pre-select a playbook)
    openPlaybookWithIntent: function (primaryControl, intent) {
      var ctx = getEntityContext(primaryControl);
      var baseData = "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId + "&intent=" + intent;
      openWizardWithBff(primaryControl, "sprk_playbooklibrary", baseData, "Playbook Library");
    },

    // -----------------------------------------------------------------------
    // Standalone launchers (no parent context) — for entity homepage grid
    // "+New" button replacements. No PrimaryControl needed.
    // -----------------------------------------------------------------------

    openCreateMatterStandalone: function () {
      openWizardWithBff(null, "sprk_creatematterwizard", "", "Create New Matter");
    },

    openCreateProjectStandalone: function () {
      openWizardWithBff(null, "sprk_createprojectwizard", "", "Create New Project");
    },

    openCreateEventStandalone: function () {
      openWizardWithBff(null, "sprk_createeventwizard", "", "Create New Event");
    },

    openCreateWorkAssignmentStandalone: function () {
      openWizardWithBff(null, "sprk_createworkassignmentwizard", "", "Assign Work");
    }
  };
})();
