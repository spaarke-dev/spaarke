/**
 * Event Side Pane Form Script
 *
 * Adds a visible Save button to the Event form when displayed in a side pane.
 * Also hides the form selector dropdown for cleaner UX.
 *
 * Add this web resource to the Event form's OnLoad event:
 * - Library: sprk_event_sidepane_form.js
 * - Function: Spaarke.EventSidePaneForm.onLoad
 * - Pass execution context as first parameter: Yes
 *
 * @namespace Spaarke.EventSidePaneForm
 * @version 1.3.0
 */
var Spaarke = window.Spaarke || {};
Spaarke.EventSidePaneForm = Spaarke.EventSidePaneForm || {};

(function (ns) {
  "use strict";

  /**
   * Form OnLoad handler
   * Injects floating Save/Open buttons and hides form selector in side pane context
   *
   * @param {Object} executionContext - Form execution context
   */
  ns.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();

    console.log("[EventSidePaneForm] v1.3.0 onLoad triggered, window width:", window.innerWidth);

    // Always inject styles (to hide form selector, toolbar, share)
    injectStyles();

    // Detect side pane context - this script is only loaded for side pane forms
    // so we always inject the buttons
    if (isSidePaneContext()) {
      console.log("[EventSidePaneForm] Side pane detected, injecting buttons");
      // Wait for form DOM to be ready
      setTimeout(function () {
        injectSaveButton(formContext);
      }, 500);
    } else {
      console.log("[EventSidePaneForm] Not in side pane context, skipping button injection");
    }
  };

  /**
   * Detect if form is displayed in a side pane
   *
   * Detection strategies:
   * 1. Check if we're in an iframe (side panes run in iframes)
   * 2. Check window width as fallback (side panes are typically narrower)
   * 3. Always return true if this webresource is specifically for side pane forms
   *
   * @returns {boolean} True if in side pane context
   */
  function isSidePaneContext() {
    // Strategy 1: Check if we're in an iframe (side panes run in iframes)
    if (window !== window.parent) {
      return true;
    }

    // Strategy 2: Check window width (side panes are typically < 800px wide)
    if (window.innerWidth < 800) {
      return true;
    }

    // Strategy 3: Check for side pane specific URL patterns
    var url = window.location.href.toLowerCase();
    if (url.includes("sidepane") || url.includes("paneId")) {
      return true;
    }

    // This script is loaded specifically for side pane forms,
    // so if none of the above match, still return true
    return true;
  }

  /**
   * Inject CSS styles to hide form selector, toolbar, share and style the save button
   */
  function injectStyles() {
    // Check if styles already injected
    if (document.getElementById("sprk-sidepane-styles")) {
      return;
    }

    var style = document.createElement("style");
    style.id = "sprk-sidepane-styles";
    style.textContent = [
      // ═══════════════════════════════════════════════════════════════════════
      // Hide form selector dropdown and form name
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="form-selector"],' +
      '[data-id="form-selector-container"],' +
      '.pa-as.pa-cx,' +
      '[aria-label="Form selector"],' +
      '.form-selector-flyout-button,' +
      // Form name/type text below record title
      '[data-id="form-selector"] + span,' +
      '.pa-y.pa-bd,' + // Form type label container
      '[data-id="MscrmControls.Containers.SubHeaderControlViewManager-subHeaderControlViewPart"] [data-id="form-selector"],' +
      // The entire form selector row
      '[class*="formSelectorFlyout"],' +
      '[class*="FormSelectorFlyout"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide three-dot menu (more options) and share button
      // ═══════════════════════════════════════════════════════════════════════
      // Three-dot menu button
      '[data-id="record-overflow-menu"],' +
      '[data-id="record-set-overflow-menu"],' +
      '[aria-label="More commands for this record"],' +
      '[aria-label="More Actions"],' +
      'button[title="More commands"],' +
      'button[title="More Commands"],' +
      '[data-id="OverflowButton"],' +
      '.pa-s.pa-dm,' + // Overflow menu container
      // Share button
      '[data-id="record-share"],' +
      '[aria-label="Share"],' +
      'button[title="Share"],' +
      '[data-id="ShareButton"],' +
      // The entire command bar with share/more options
      '[data-id="header_overflow_command_bar"],' +
      '[data-id="record-command-bar-overflow"],' +
      // Chevron dropdown on share
      '[data-id="record-share-split-button"],' +
      '[class*="shareButton"],' +
      '[class*="ShareButton"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide the form type indicator row entirely
      // ═══════════════════════════════════════════════════════════════════════
      // The row showing "Event" with form selector
      '[data-id="MscrmControls.Containers.SubHeaderControlViewManager-subHeaderControlViewPart"],' +
      '[class*="subHeaderControlView"],' +
      // Form type chip/badge
      '[data-id="header_formTypeSelectorContainer"],' +
      '[class*="formTypeSelectorContainer"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Style the save button container
      // ═══════════════════════════════════════════════════════════════════════
      '#sprk-sidepane-save-container {',
      '  position: fixed !important;',
      '  bottom: 0 !important;',
      '  left: 0 !important;',
      '  right: 0 !important;',
      '  padding: 12px 16px !important;',
      '  background: linear-gradient(transparent, #ffffff 20%) !important;',
      '  display: flex !important;',
      '  justify-content: flex-end !important;',
      '  gap: 8px !important;',
      '  z-index: 100000 !important;',
      '  border-top: 1px solid #e0e0e0 !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Ensure form content has bottom padding for save buttons
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="editFormRoot"],' +
      '.customControl,' +
      'main,' +
      '.mainContent {',
      '  padding-bottom: 70px !important;',
      '}'
    ].join('\n');

    document.head.appendChild(style);
    console.log("[EventSidePaneForm] Styles injected (v1.2.0 - hiding form selector, toolbar, share)");
  }

  /**
   * Inject a floating Save button into the form
   *
   * @param {Object} formContext - Xrm form context
   */
  function injectSaveButton(formContext) {
    // Check if button already exists
    if (document.getElementById("sprk-sidepane-save-container")) {
      console.log("[EventSidePaneForm] Save button container already exists");
      return;
    }

    // Create button container (fixed at bottom of form)
    var container = document.createElement("div");
    container.id = "sprk-sidepane-save-container";

    // Create Open button (opens record in modal)
    var openBtn = document.createElement("button");
    openBtn.id = "sprk-sidepane-open-btn";
    openBtn.innerText = "Open";
    openBtn.type = "button";
    openBtn.style.cssText = [
      "padding: 8px 16px",
      "background-color: #ffffff",
      "color: #242424",
      "border: 1px solid #e0e0e0",
      "border-radius: 4px",
      "font-size: 14px",
      "font-weight: 600",
      "cursor: pointer",
      "min-width: 80px"
    ].join(";");

    openBtn.onmouseover = function () {
      openBtn.style.backgroundColor = "#f5f5f5";
    };
    openBtn.onmouseout = function () {
      openBtn.style.backgroundColor = "#ffffff";
    };

    openBtn.onclick = function (e) {
      e.preventDefault();
      e.stopPropagation();
      openRecordInModal(formContext);
    };

    // Create Save button
    var saveBtn = document.createElement("button");
    saveBtn.id = "sprk-sidepane-save-btn";
    saveBtn.innerText = "Save";
    saveBtn.type = "button";
    saveBtn.style.cssText = [
      "padding: 8px 20px",
      "background-color: #0078d4",
      "color: white",
      "border: none",
      "border-radius: 4px",
      "font-size: 14px",
      "font-weight: 600",
      "cursor: pointer",
      "min-width: 80px"
    ].join(";");

    saveBtn.onmouseover = function () {
      saveBtn.style.backgroundColor = "#106ebe";
    };
    saveBtn.onmouseout = function () {
      saveBtn.style.backgroundColor = "#0078d4";
    };

    saveBtn.onclick = function (e) {
      e.preventDefault();
      e.stopPropagation();
      saveRecord(formContext, saveBtn);
    };

    // Create Save & Close button
    var saveCloseBtn = document.createElement("button");
    saveCloseBtn.id = "sprk-sidepane-saveclose-btn";
    saveCloseBtn.innerText = "Save & Close";
    saveCloseBtn.type = "button";
    saveCloseBtn.style.cssText = [
      "padding: 8px 16px",
      "background-color: #ffffff",
      "color: #242424",
      "border: 1px solid #e0e0e0",
      "border-radius: 4px",
      "font-size: 14px",
      "font-weight: 600",
      "cursor: pointer",
      "min-width: 100px"
    ].join(";");

    saveCloseBtn.onmouseover = function () {
      saveCloseBtn.style.backgroundColor = "#f5f5f5";
    };
    saveCloseBtn.onmouseout = function () {
      saveCloseBtn.style.backgroundColor = "#ffffff";
    };

    saveCloseBtn.onclick = function (e) {
      e.preventDefault();
      e.stopPropagation();
      saveAndCloseRecord(formContext, saveCloseBtn);
    };

    // Add buttons to container (order: Save & Close, Open, Save)
    container.appendChild(saveCloseBtn);
    container.appendChild(openBtn);
    container.appendChild(saveBtn);

    // Always append to document.body - the container uses position: fixed
    // which positions relative to viewport, not parent element
    document.body.appendChild(container);

    console.log("[EventSidePaneForm] Buttons injected (Open, Save, Save & Close) v1.3.0");
  }

  /**
   * Open the record in a modal dialog
   *
   * @param {Object} formContext - Xrm form context
   */
  function openRecordInModal(formContext) {
    try {
      var entityId = formContext.data.entity.getId();
      var entityName = formContext.data.entity.getEntityName();

      // Remove braces from GUID if present
      entityId = entityId.replace(/[{}]/g, "");

      // Use parent Xrm for navigation (side pane runs in iframe)
      var xrm = window.parent && window.parent.Xrm ? window.parent.Xrm : (typeof Xrm !== "undefined" ? Xrm : null);

      if (!xrm || !xrm.Navigation) {
        console.error("[EventSidePaneForm] Xrm.Navigation not available");
        return;
      }

      var pageInput = {
        pageType: "entityrecord",
        entityName: entityName,
        entityId: entityId
      };

      var navigationOptions = {
        target: 2, // Modal dialog
        width: { value: 80, unit: "%" },
        height: { value: 80, unit: "%" },
        position: 1 // Center
      };

      xrm.Navigation.navigateTo(pageInput, navigationOptions).then(
        function () {
          console.log("[EventSidePaneForm] Opened record in modal");
        },
        function (error) {
          console.error("[EventSidePaneForm] Failed to open modal:", error);
        }
      );
    } catch (e) {
      console.error("[EventSidePaneForm] Error opening modal:", e);
    }
  }

  /**
   * Save the record
   *
   * @param {Object} formContext - Xrm form context
   * @param {HTMLElement} button - Button element (for loading state)
   */
  function saveRecord(formContext, button) {
    // Disable button and show loading state
    button.disabled = true;
    var originalText = button.innerText;
    var originalBg = button.style.backgroundColor;
    button.innerText = "Saving...";

    formContext.data.save().then(
      function () {
        console.log("[EventSidePaneForm] Record saved successfully");
        button.innerText = "Saved!";
        button.style.backgroundColor = "#107c10"; // Green
        setTimeout(function () {
          button.innerText = originalText;
          button.style.backgroundColor = originalBg;
          button.disabled = false;
        }, 1500);
      },
      function (error) {
        console.error("[EventSidePaneForm] Save failed:", error);
        button.innerText = "Failed";
        button.style.backgroundColor = "#d13438"; // Red
        setTimeout(function () {
          button.innerText = originalText;
          button.style.backgroundColor = originalBg;
          button.disabled = false;
        }, 2000);
      }
    );
  }

  /**
   * Save the record and close the side pane
   *
   * @param {Object} formContext - Xrm form context
   * @param {HTMLElement} button - Button element (for loading state)
   */
  function saveAndCloseRecord(formContext, button) {
    button.disabled = true;
    var originalText = button.innerText;
    button.innerText = "Saving...";

    formContext.data.save().then(
      function () {
        console.log("[EventSidePaneForm] Record saved, closing pane");
        closeSidePane();
      },
      function (error) {
        console.error("[EventSidePaneForm] Save failed:", error);
        button.innerText = "Failed";
        setTimeout(function () {
          button.innerText = originalText;
          button.disabled = false;
        }, 2000);
      }
    );
  }

  /**
   * Close the side pane
   */
  function closeSidePane() {
    try {
      // Method 1: Use Xrm.App.sidePanes from parent
      if (window.parent && window.parent.Xrm && window.parent.Xrm.App && window.parent.Xrm.App.sidePanes) {
        var sidePanes = window.parent.Xrm.App.sidePanes;
        var pane = sidePanes.getPane("event_detail_pane");
        if (pane) {
          pane.close();
          console.log("[EventSidePaneForm] Closed via Xrm.App.sidePanes");
          return;
        }
      }

      // Method 2: Try current window's Xrm
      if (typeof Xrm !== "undefined" && Xrm.App && Xrm.App.sidePanes) {
        var pane2 = Xrm.App.sidePanes.getPane("event_detail_pane");
        if (pane2) {
          pane2.close();
          console.log("[EventSidePaneForm] Closed via current Xrm.App.sidePanes");
          return;
        }
      }

      // Method 3: Close the window/frame
      window.close();
    } catch (e) {
      console.warn("[EventSidePaneForm] Could not close side pane:", e);
    }
  }

})(Spaarke.EventSidePaneForm);
