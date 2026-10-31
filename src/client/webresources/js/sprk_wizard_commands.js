// sprk_wizard_commands.js
// Ribbon command handlers for wizard Code Page launches
// Deployed as: sprk_wizard_commands

"use strict";

var Spaarke = Spaarke || {};
Spaarke.Commands = Spaarke.Commands || {};
Spaarke.Commands.Wizards = (function () {

  // Common dialog options — standardized to 60% x 70% per UAT feedback E-03.
  // Matches the `wizard` OOB size in oobModalSizes.ts (spec FR-11/FR-18, task
  // 090). Plain Dataverse web resource (no bundler) — cannot import that
  // module; keep this literal in sync with it manually.
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

  /*
   * `getContainerId()` was DELETED here 2026-09-03 (unified-access-control-r2 task 076).
   *
   * It read the acting user's business unit's `sprk_containerid` and appended it to the
   * DocumentUploadWizard launch envelope. The wizard no longer reads that parameter: the SERVER
   * resolves the container from the parent record it authorizes the caller against. Three launch
   * envelopes carried this value (this one, SemanticSearchControl's `NavigationService`, and
   * LegalWorkspace's `WorkspaceGrid`); all three lost it in the same change.
   */

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
   * Helper: open a webresource dialog and refresh form on close (and the launching subgrid, when given)
   */
  function openWizardDialog(primaryControl, webresourceName, data, title, options, gridControl) {
    var dialogOptions = Object.assign({}, options || DIALOG_OPTIONS, { title: title });

    function refreshAfterClose() {
      if (primaryControl && primaryControl.data) {
        primaryControl.data.refresh();
      }
      if (gridControl && typeof gridControl.refresh === "function") {
        gridControl.refresh();
      }
    }

    Xrm.Navigation.navigateTo(
      { pageType: "webresource", webresourceName: webresourceName, data: data },
      dialogOptions
    ).then(function () {
      // Refresh form data after dialog closes
      refreshAfterClose();
    }).catch(function () {
      // User cancelled or error — still try to refresh
      refreshAfterClose();
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
   * @param {object} [gridControl] - Optional subgrid control, refreshed after the dialog closes.
   */
  function openWizardWithBff(primaryControl, webresourceName, baseData, title, options, gridControl) {
    getBffBaseUrl().then(function (bffBaseUrl) {
      var data = (baseData ? baseData + "&" : "") + "bffBaseUrl=" + encodeURIComponent(bffBaseUrl);
      openWizardDialog(primaryControl, webresourceName, data, title, options, gridControl);
    }).catch(function (err) {
      console.error("[WizardCommands] Failed to resolve BFF base URL, opening wizard without it:", err);
      openWizardDialog(primaryControl, webresourceName, baseData, title, options, gridControl);
    });
  }

  // ---------------------------------------------------------------------------
  // Project <-> Matter subgrids (unified-access-control-r2 task 195, #2352)
  // ---------------------------------------------------------------------------
  //
  // Owner decision 2026-10-31: a matter is added to a project's matter list (and a project to a matter's) through the
  // Create wizards only, which link the two AND file the project under the matter through the BFF. On a subgrid of the
  // N:N relationship sprk_Project_Matter_nn, the platform "New" / quick create and "Add Existing" commands are therefore
  // turned off (isNotProjectMatterLinkGrid, an extra enable rule on those commands in the sprk_matter and sprk_project
  // ribbons) and a "New Matter" / "New Project" command opens the matching wizard pre-filled with the form's record
  // (isProjectMatterLinkGrid + openLinkedWizardFromSubGrid). Every other subgrid of those tables is unchanged.

  /** The N:N relationship's schema name, lower-cased for comparison. */
  var PROJECT_MATTER_RELATIONSHIP = "sprk_project_matter_nn";

  /** The form's table -> the wizard that creates the other side of the link. */
  var LINKED_WIZARDS = {
    sprk_project: { webresourceName: "sprk_creatematterwizard", title: "Create New Matter" },
    sprk_matter: { webresourceName: "sprk_createprojectwizard", title: "Create New Project" }
  };

  /**
   * True when the subgrid shows the Project <-> Matter N:N (its relationship is sprk_Project_Matter_nn). False when the
   * relationship is another one, or cannot be read: the platform commands then stay as they are.
   */
  function isProjectMatterLinkGrid(selectedControl) {
    try {
      var relationship = selectedControl && selectedControl.getRelationship && selectedControl.getRelationship();
      var name = relationship && relationship.name;
      return typeof name === "string" && name.toLowerCase() === PROJECT_MATTER_RELATIONSHIP;
    } catch (e) {
      return false;
    }
  }

  /** The form record's display name ('' when none). */
  function primaryName(primaryControl) {
    try {
      var entity = primaryControl.data.entity;
      return (entity.getPrimaryAttributeValue && entity.getPrimaryAttributeValue()) || "";
    } catch (e) {
      return "";
    }
  }

  /**
   * "New Matter" on a project's Matters subgrid / "New Project" on a matter's Projects subgrid: opens the matching Create
   * wizard with the form's record as its fixed association (entityType, entityId, recordName). The wizard writes the
   * link and files the project under the matter; the subgrid is refreshed when the dialog closes.
   */
  function openLinkedWizardFromSubGrid(primaryControl, selectedControl) {
    var ctx = getEntityContext(primaryControl);
    var target = LINKED_WIZARDS[ctx.entityType];
    if (!target || !ctx.entityId) {
      console.error("[WizardCommands] openLinkedWizardFromSubGrid: no saved project or matter on this form");
      return;
    }
    var baseData =
      "entityType=" + encodeURIComponent(ctx.entityType) +
      "&entityId=" + encodeURIComponent(ctx.entityId) +
      "&recordName=" + encodeURIComponent(primaryName(primaryControl));
    openWizardWithBff(primaryControl, target.webresourceName, baseData, target.title, undefined, selectedControl);
  }

  return {
    // Task 195: the Project <-> Matter subgrid commands (see above).
    isProjectMatterLinkGrid: isProjectMatterLinkGrid,
    isNotProjectMatterLinkGrid: function (selectedControl) {
      return !isProjectMatterLinkGrid(selectedControl);
    },
    openLinkedWizardFromSubGrid: openLinkedWizardFromSubGrid,

    openCreateMatterWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      // Task 195: the record's name too, so a wizard that pre-selects the launch record (its partner table) shows it.
      var baseData =
        "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId +
        "&recordName=" + encodeURIComponent(primaryName(primaryControl));
      openWizardWithBff(primaryControl, "sprk_creatematterwizard", baseData, "Create New Matter");
    },

    openCreateProjectWizard: function (primaryControl) {
      var ctx = getEntityContext(primaryControl);
      // Task 195: the record's name too, so a wizard that pre-selects the launch record (its partner table) shows it.
      var baseData =
        "entityType=" + ctx.entityType + "&entityId=" + ctx.entityId +
        "&recordName=" + encodeURIComponent(primaryName(primaryControl));
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
      // The wizard reads parentEntityType / parentEntityId / parentEntityName (NOT
      // entityType/entityId). Without these it mounts with empty context and the upload never
      // associates to the parent record.
      //
      // 🔴 `containerId` was REMOVED from this envelope 2026-09-03 (task 076), along with the
      // "Cannot upload: no SPE container is configured for your business unit" alert that guarded
      // it. That alert refused the upload outright whenever the ACTING USER's business unit had no
      // container — including for a secure matter that has a container of its own, which the server
      // resolves perfectly well. The client is no longer a participant in that decision, so it is
      // not in a position to render a verdict on it. A record the server genuinely cannot resolve a
      // container for now fails per file, on the wizard's Processing step, with the server's reason.
      var ctx = getEntityContext(primaryControl);
      if (!ctx.entityType || !ctx.entityId) {
        console.error("[WizardCommands] openDocumentUploadWizard: no parent record context");
        return;
      }
      getParentEntityName(ctx.entityType, ctx.entityId).then(function (parentEntityName) {
        var baseData =
          "parentEntityType=" + encodeURIComponent(ctx.entityType) +
          "&parentEntityId=" + encodeURIComponent(ctx.entityId) +
          "&parentEntityName=" + encodeURIComponent(parentEntityName) +
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
