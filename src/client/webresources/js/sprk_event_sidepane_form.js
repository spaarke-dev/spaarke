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
 * @version 1.30.0
 */
var Spaarke = window.Spaarke || {};
Spaarke.EventSidePaneForm = Spaarke.EventSidePaneForm || {};

(function (ns) {
  "use strict";

  // Store reference to button container for cleanup
  var buttonContainer = null;
  var cleanupInterval = null;
  var instanceId = null; // Unique ID for this form instance
  var buttonsInjectedAt = null; // Timestamp when buttons were injected (for grace period)

  // v1.30.0: Track previous state to reduce logging (only log on state changes)
  var lastPaneOpenState = null;
  var lastEventsPageState = null;
  var lastSelectedPane = null;

  /**
   * Form OnLoad handler
   * Injects floating Save/Open buttons and hides form selector in side pane context
   *
   * @param {Object} executionContext - Form execution context
   */
  ns.onLoad = function (executionContext) {
    var formContext = executionContext.getFormContext();

    console.log("[EventSidePaneForm] v1.30.0 onLoad triggered");

    // Always inject styles (to hide form selector, toolbar, share, OVERVIEW tab)
    injectStyles();

    // Setup cleanup when form unloads
    setupCleanup();

    // Detect side pane context - this script is only loaded for side pane forms
    // so we always inject the buttons
    if (isSidePaneContext()) {
      console.log("[EventSidePaneForm] Side pane detected, waiting for form to render...");
      // Poll for form dimensions - form may not be rendered yet
      waitForFormToRender(function() {
        injectSaveButton(formContext);
      });
    } else {
      console.log("[EventSidePaneForm] Not in side pane context, skipping button injection");
    }
  };

  /**
   * Setup cleanup handlers to remove buttons when form unloads
   * v1.30.0: Fixed to not remove buttons owned by other form instances
   */
  function setupCleanup() {
    // Generate unique instance ID for this form
    instanceId = "form_" + Date.now() + "_" + Math.random().toString(36).substr(2, 9);
    console.log("[EventSidePaneForm] v1.30.0 Instance ID:", instanceId);

    // Store instance ID on the button container when created
    // Clean up on window unload
    window.addEventListener("beforeunload", cleanup);
    window.addEventListener("unload", cleanup);

    // Clear any existing interval from previous form instance
    if (cleanupInterval) {
      clearInterval(cleanupInterval);
      cleanupInterval = null;
    }

    // Check periodically for cleanup scenarios
    // v1.30.0: INVERTED LOGIC - Only check visibility when we have buttons to remove
    cleanupInterval = setInterval(function() {
      try {
        if (!window.parent || !window.parent.document) {
          console.log("[EventSidePaneForm] v1.30.0 No parent document, stopping interval");
          if (cleanupInterval) {
            clearInterval(cleanupInterval);
            cleanupInterval = null;
          }
          return;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 1: Check if we have buttons before doing ANY visibility checks
        // This is the key fix - don't check visibility unless we have something to remove
        // ═══════════════════════════════════════════════════════════════════════
        var container = window.parent.document.getElementById("sprk-sidepane-save-container");

        // Case A: No buttons exist at all - nothing to clean up, skip all checks
        if (!container) {
          // Buttons haven't been created yet, or were already removed
          // Nothing for us to do - just keep interval running for future cleanup
          return;
        }

        // Case B: Buttons exist but belong to another form instance
        if (container.dataset.instanceId && container.dataset.instanceId !== instanceId) {
          // Another form has taken over - stop OUR interval, don't touch their buttons
          console.log("[EventSidePaneForm] v1.30.0 Buttons owned by another instance, stopping our interval");
          if (cleanupInterval) {
            clearInterval(cleanupInterval);
            cleanupInterval = null;
          }
          return;
        }

        // Case C: Buttons exist and belong to US - now we can check visibility
        // ═══════════════════════════════════════════════════════════════════════
        // STEP 2: Grace period - don't remove our buttons too quickly after injection
        // ═══════════════════════════════════════════════════════════════════════
        var GRACE_PERIOD_MS = 2000; // 2 seconds grace period
        if (buttonsInjectedAt && (Date.now() - buttonsInjectedAt) < GRACE_PERIOD_MS) {
          // Too soon after injection - visibility might be transitioning
          return;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // STEP 3: Now check visibility - we definitely have OUR buttons to manage
        // ═══════════════════════════════════════════════════════════════════════
        var eventPaneOpen = isEventPaneOpen();
        if (!eventPaneOpen) {
          console.log("[EventSidePaneForm] v1.30.0 Event pane closed/hidden, cleaning up OUR buttons");
          cleanup();
          return;
        }

        // Check if Events page is still present (user navigated away)
        var eventsPagePresent = isEventsPagePresent();
        if (!eventsPagePresent) {
          console.log("[EventSidePaneForm] v1.30.0 Events page gone, cleaning up and closing pane");
          closeSidePaneAndCleanup();
          return;
        }

      } catch (e) {
        // Cross-origin or other error - don't clean up aggressively on errors
        console.warn("[EventSidePaneForm] v1.30.0 Interval check error:", e);
      }
    }, 500);
  }

  /**
   * Check if the Event side pane (eventDetailPane) is still open AND selected
   * v1.30.0: REMOVED iframe visibility checks - they're unreliable during form transitions
   *          Now ONLY uses sidePanes API. Conservative approach: keep buttons if uncertain.
   * v1.30.0: Reduced logging - only logs on state changes to avoid console spam
   */
  function isEventPaneOpen() {
    try {
      var xrm = window.parent && window.parent.Xrm ? window.parent.Xrm : null;

      // If no Xrm API available, assume pane is open (conservative)
      if (!xrm || !xrm.App || !xrm.App.sidePanes) {
        return true;
      }

      // Check if our Event pane exists in the registry
      var pane = xrm.App.sidePanes.getPane("eventDetailPane");
      if (!pane) {
        pane = xrm.App.sidePanes.getPane("event_detail_pane");
      }

      // If pane doesn't exist at all, it was closed
      if (!pane) {
        if (lastPaneOpenState !== false) {
          console.log("[EventSidePaneForm] v1.30.0 Pane closed (not in registry)");
          lastPaneOpenState = false;
        }
        return false;
      }

      // Check if Event pane is the SELECTED pane (not just registered)
      // This is the key check for Calendar switch detection
      try {
        var selectedPane = xrm.App.sidePanes.getSelectedPane();
        if (selectedPane) {
          var selectedId = selectedPane.paneId || selectedPane.id;

          // Only log when selection changes
          if (lastSelectedPane !== selectedId) {
            console.log("[EventSidePaneForm] v1.30.0 Selected pane changed:", selectedId);
            lastSelectedPane = selectedId;
          }

          // If another pane is selected (e.g., Calendar), hide our buttons
          if (selectedId !== "eventDetailPane" && selectedId !== "event_detail_pane") {
            if (lastPaneOpenState !== false) {
              console.log("[EventSidePaneForm] v1.30.0 Event pane deselected, another pane is active");
              lastPaneOpenState = false;
            }
            return false;
          }
        }
        // If no pane is selected, assume ours is visible (conservative)
      } catch (e) {
        // getSelectedPane might not exist - assume open (conservative)
      }

      // Pane exists and is selected (or we couldn't determine selection)
      if (lastPaneOpenState !== true) {
        console.log("[EventSidePaneForm] v1.30.0 Event pane open and selected");
        lastPaneOpenState = true;
      }
      return true;
    } catch (e) {
      // v1.30.0: Be CONSERVATIVE on errors - keep buttons visible
      console.warn("[EventSidePaneForm] v1.30.0 isEventPaneOpen error:", e);
      return true;
    }
  }

  /**
   * Check if the Events Custom Page is still present in the Dataverse shell
   * v1.30.0: More aggressive detection - returns false if Events page is likely gone
   * v1.30.0: Reduced logging - only logs on state changes to avoid console spam
   */
  function isEventsPagePresent() {
    try {
      if (!window.parent || !window.parent.document) {
        if (lastEventsPageState !== false) {
          console.log("[EventSidePaneForm] v1.30.0 No parent document, Events page gone");
          lastEventsPageState = false;
        }
        return false;
      }

      var parentDoc = window.parent.document;
      var parentUrl = window.parent.location.href || "";

      // Strategy 1: Check URL - if not on Events related page, we've navigated away
      // Events page URL contains "pagetype=custom" and often "sprk_eventspage" or "Events"
      var isEventsUrl = parentUrl.indexOf("sprk_event") !== -1 ||
                        parentUrl.indexOf("/Events") !== -1 ||
                        parentUrl.indexOf("etn=sprk_event") !== -1;

      if (!isEventsUrl) {
        // Check if we're on a completely different area (Accounts, Contacts, etc.)
        var otherEntities = ["account", "contact", "lead", "opportunity", "incident", "matter"];
        for (var i = 0; i < otherEntities.length; i++) {
          if (parentUrl.toLowerCase().indexOf(otherEntities[i]) !== -1) {
            if (lastEventsPageState !== false) {
              console.log("[EventSidePaneForm] v1.30.0 Navigated away to", otherEntities[i]);
              lastEventsPageState = false;
            }
            return false;
          }
        }
      }

      // Strategy 2: Look for the Events page web resource iframe
      var iframes = parentDoc.querySelectorAll('iframe');
      var foundEventsPage = false;
      for (var j = 0; j < iframes.length; j++) {
        var src = iframes[j].src || "";
        if (src.indexOf("sprk_eventspage") !== -1 || src.indexOf("eventspage") !== -1) {
          foundEventsPage = true;
          break;
        }
      }

      // Strategy 3: Check if Events page iframe is visible (not just exists)
      if (!foundEventsPage) {
        // No Events page iframe found - likely navigated away
        // But double-check URL before concluding
        if (!isEventsUrl) {
          if (lastEventsPageState !== false) {
            console.log("[EventSidePaneForm] v1.30.0 No Events iframe and not Events URL");
            lastEventsPageState = false;
          }
          return false;
        }
      }

      // Default: Events page is probably still there
      if (lastEventsPageState !== true) {
        lastEventsPageState = true;
        // Don't log "still present" every time - silent success
      }
      return true;
    } catch (e) {
      console.warn("[EventSidePaneForm] v1.30.0 isEventsPagePresent error:", e);
      // Error checking - be aggressive, assume navigated away
      return false;
    }
  }

  /**
   * Close the side pane and clean up buttons
   * v1.30.0: Called when Events page is no longer present
   */
  function closeSidePaneAndCleanup() {
    cleanup();
    closeSidePane();
  }

  /**
   * Clean up buttons from parent document
   * v1.30.0: Only remove buttons if they belong to THIS instance (prevents removing new form's buttons)
   */
  function cleanup() {
    console.log("[EventSidePaneForm] v1.30.0 Cleanup called for instance:", instanceId);

    // Clear interval
    if (cleanupInterval) {
      clearInterval(cleanupInterval);
      cleanupInterval = null;
    }

    // Remove button container from parent document - BUT only if it belongs to this instance
    try {
      if (window.parent && window.parent.document) {
        var container = window.parent.document.getElementById("sprk-sidepane-save-container");
        if (container) {
          // v1.30.0: Check if container belongs to another instance - if so, DON'T remove it
          var containerInstanceId = container.dataset.instanceId;
          if (containerInstanceId && containerInstanceId !== instanceId) {
            console.log("[EventSidePaneForm] v1.30.0 Container belongs to another instance (" + containerInstanceId + "), NOT removing");
            buttonContainer = null;
            return; // Don't remove - let the new form instance keep its buttons
          }

          // Container belongs to us or has no instance ID - safe to remove
          container.parentNode.removeChild(container);
          console.log("[EventSidePaneForm] v1.30.0 Removed button container (owned by this instance)");
        }
      }
    } catch (e) {
      console.warn("[EventSidePaneForm] v1.30.0 Cleanup error:", e);
    }

    buttonContainer = null;
  }

  /**
   * Wait for the form to render with valid dimensions
   * Polls every 200ms for up to 5 seconds
   *
   * @param {Function} callback - Called when form has valid dimensions
   */
  function waitForFormToRender(callback) {
    var attempts = 0;
    var maxAttempts = 25; // 5 seconds total
    var interval = 200;

    function checkDimensions() {
      attempts++;

      // Try multiple sources for dimensions
      var dims = getDimensionsFromAllSources();

      console.log("[EventSidePaneForm] v1.30.0 Poll #" + attempts + ": " + dims.width + " x " + dims.height + " (source: " + dims.source + ")");

      if (dims.height > 100 && dims.width > 100) {
        // Form has rendered with valid dimensions
        console.log("[EventSidePaneForm] v1.30.0 Form rendered, injecting buttons");
        callback();
      } else if (attempts < maxAttempts) {
        // Keep polling
        setTimeout(checkDimensions, interval);
      } else {
        // Timeout - inject anyway with fallback positioning
        console.log("[EventSidePaneForm] v1.30.0 Timeout waiting for dimensions, using fallback");
        callback();
      }
    }

    /**
     * Get dimensions from multiple sources including parent window and form elements
     */
    function getDimensionsFromAllSources() {
      // Source 1: Current window (standard approach)
      var w = window.innerWidth || 0;
      var h = window.innerHeight || 0;
      if (w > 100 && h > 100) {
        return { width: w, height: h, source: "window" };
      }

      // Source 2: Document element
      w = document.documentElement.clientWidth || 0;
      h = document.documentElement.clientHeight || 0;
      if (w > 100 && h > 100) {
        return { width: w, height: h, source: "documentElement" };
      }

      // Source 3: Parent window (for iframe context)
      try {
        if (window.parent && window.parent !== window) {
          w = window.parent.innerWidth || 0;
          h = window.parent.innerHeight || 0;
          if (w > 100 && h > 100) {
            return { width: Math.min(w, 500), height: h, source: "parent" };
          }
        }
      } catch (e) {
        // Cross-origin blocked
      }

      // Source 4: Find actual form container element
      var formRoot = document.querySelector('[data-id="editFormRoot"]');
      if (formRoot) {
        var rect = formRoot.getBoundingClientRect();
        if (rect.width > 100 && rect.height > 100) {
          return { width: rect.width, height: rect.height, source: "editFormRoot" };
        }
      }

      // Source 5: Find any main content area
      var mainContent = document.querySelector('main, .mainContent, [role="main"]');
      if (mainContent) {
        var rect = mainContent.getBoundingClientRect();
        if (rect.width > 100 && rect.height > 100) {
          return { width: rect.width, height: rect.height, source: "mainContent" };
        }
      }

      // Source 6: Find the form's outermost container
      var formContainer = document.querySelector('[data-id="form-selector"]');
      if (formContainer) {
        var parent = formContainer.closest('div');
        if (parent) {
          var rect = parent.getBoundingClientRect();
          if (rect.width > 100 && rect.height > 100) {
            return { width: rect.width, height: rect.height, source: "formContainer" };
          }
        }
      }

      // Source 7: Get the largest element in the document
      var allDivs = document.querySelectorAll('div');
      var maxWidth = 0, maxHeight = 0;
      for (var i = 0; i < allDivs.length; i++) {
        var rect = allDivs[i].getBoundingClientRect();
        if (rect.width > maxWidth) maxWidth = rect.width;
        if (rect.height > maxHeight) maxHeight = rect.height;
      }
      if (maxWidth > 100 && maxHeight > 100) {
        return { width: maxWidth, height: maxHeight, source: "largestDiv" };
      }

      return { width: 0, height: 0, source: "none" };
    }

    // Start polling after initial delay
    setTimeout(checkDimensions, 300);
  }

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
   * Inject CSS styles to hide form selector, toolbar, share, OVERVIEW tab
   * Injects into both current document and parent document for complete coverage
   */
  function injectStyles() {
    var styleContent = buildStyleContent();

    // Inject into current document
    if (!document.getElementById("sprk-sidepane-styles")) {
      var style = document.createElement("style");
      style.id = "sprk-sidepane-styles";
      style.textContent = styleContent;
      document.head.appendChild(style);
      console.log("[EventSidePaneForm] v1.30.0 Styles injected into form document");
    }

    // Also inject into parent document (side pane chrome elements may be there)
    try {
      if (window.parent && window.parent.document && window.parent !== window) {
        if (!window.parent.document.getElementById("sprk-sidepane-styles")) {
          var parentStyle = window.parent.document.createElement("style");
          parentStyle.id = "sprk-sidepane-styles";
          parentStyle.textContent = styleContent;
          window.parent.document.head.appendChild(parentStyle);
          console.log("[EventSidePaneForm] v1.30.0 Styles injected into parent document");
        }
      }
    } catch (e) {
      console.warn("[EventSidePaneForm] v1.30.0 Could not inject styles into parent:", e);
    }

    // Log what elements we found to help debug
    logFoundElements();
  }

  /**
   * Build the CSS content for hiding elements
   * v1.30.0: REMOVED overly-generic selectors (.pa-na, [role="tablist"]) that were hiding form content
   *          Now uses only specific data-id selectors that target known Dataverse chrome elements
   */
  function buildStyleContent() {
    return [
      // ═══════════════════════════════════════════════════════════════════════
      // Hide form selector dropdown (the dropdown to switch forms)
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="form-selector"] {',
      '  display: none !important;',
      '}',
      '[data-id="form-selector-container"] {',
      '  display: none !important;',
      '}',
      '[data-id="header_form_selector_flyout"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide three-dot menu (more options)
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="record-overflow-menu"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide share button
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="record-share"] {',
      '  display: none !important;',
      '}',
      '[data-id="record-share-split-button"] {',
      '  display: none !important;',
      '}',
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide the record type row (shows "Event · Event Task side pane form")
      // v1.30.0: Only target specific data-id elements, NOT generic classes
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="MscrmControls.Containers.SubHeaderControlViewManager-subHeaderControlViewPart"] {',
      '  display: none !important;',
      '}',
      '[data-id="header_formTypeSelectorContainer"] {',
      '  display: none !important;',
      '}',
      '[data-id="entity_name_span"] {',
      '  display: none !important;',
      '}',
      // v1.30.0: REMOVED .pa-na selector - it was hiding form content!
      '',
      // ═══════════════════════════════════════════════════════════════════════
      // Hide OVERVIEW tab - ONLY specific data-id selectors
      // v1.30.0: REMOVED [role="tablist"] and .pa-hi selectors - too generic
      // ═══════════════════════════════════════════════════════════════════════
      '[data-id="tablist"] {',
      '  display: none !important;',
      '}',
      '[data-id="form-tab-list"] {',
      '  display: none !important;',
      '}',
      '[data-id="tablist-tab_overview"] {',
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
      '[data-id="editFormRoot"] {',
      '  padding-bottom: 70px !important;',
      '}'
    ].join('\n');
  }

  /**
   * Log found elements to help debug CSS selectors
   */
  function logFoundElements() {
    var selectors = {
      "form-selector": '[data-id="form-selector"]',
      "overflow-menu": '[data-id="record-overflow-menu"], [aria-label="More commands for this record"]',
      "share-button": '[data-id="record-share"], [aria-label="Share"]',
      "tablist": '[data-id="tablist"], [role="tablist"]',
      "tab-role": '[role="tab"]',
      "OVERVIEW-label": '[aria-label="OVERVIEW"], [title="OVERVIEW"]'
    };

    console.log("[EventSidePaneForm] v1.30.0 DOM element search results:");
    for (var name in selectors) {
      var el = document.querySelector(selectors[name]);
      console.log("  " + name + ": " + (el ? "FOUND" : "not found"));
    }

    // Also check parent
    try {
      if (window.parent && window.parent.document && window.parent !== window) {
        console.log("[EventSidePaneForm] v1.30.0 Parent DOM element search:");
        for (var name2 in selectors) {
          var el2 = window.parent.document.querySelector(selectors[name2]);
          console.log("  " + name2 + ": " + (el2 ? "FOUND" : "not found"));
        }
      }
    } catch (e) {
      console.warn("[EventSidePaneForm] v1.30.0 Could not search parent DOM");
    }
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

    // Also check in parent document
    try {
      if (window.parent && window.parent.document) {
        var existing = window.parent.document.getElementById("sprk-sidepane-save-container");
        if (existing) {
          console.log("[EventSidePaneForm] v1.30.0 Button container exists in parent, removing old one");
          existing.parentNode.removeChild(existing);
        }
      }
    } catch (e) {
      // Ignore cross-origin
    }

    // Create button container with inline styles (more reliable than stylesheet in iframe)
    var container = document.createElement("div");
    container.id = "sprk-sidepane-save-container";
    container.dataset.instanceId = instanceId; // Track which form instance owns this

    // v1.30.0: Record injection time for grace period checking
    buttonsInjectedAt = Date.now();

    console.log("[EventSidePaneForm] v1.30.0 Creating buttons for instance:", instanceId);

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

    // Add buttons to container (order: Save, Save & Close, Open)
    container.appendChild(saveBtn);
    container.appendChild(saveCloseBtn);
    container.appendChild(openBtn);

    // Try to find the side pane container in parent window
    var sidePaneEl = findSidePaneContainer();

    if (sidePaneEl) {
      var sidePaneRect = sidePaneEl.getBoundingClientRect();

      // Get the parent window's visible viewport height
      var parentViewportHeight = window.parent.innerHeight || window.parent.document.documentElement.clientHeight;

      console.log("[EventSidePaneForm] v1.30.0 Found side pane. Rect:", JSON.stringify({
        width: sidePaneRect.width,
        height: sidePaneRect.height,
        top: sidePaneRect.top,
        left: sidePaneRect.left
      }));
      console.log("[EventSidePaneForm] v1.30.0 Parent viewport height:", parentViewportHeight);

      // Calculate position: fixed at bottom of visible viewport, aligned with side pane
      var containerWidth = sidePaneRect.width;
      var containerHeight = 56; // Fixed footer height
      var containerLeft = sidePaneRect.left;

      // Position at the bottom of the visible viewport
      // But don't go below the side pane's visible bottom
      var visibleBottom = Math.min(sidePaneRect.bottom, parentViewportHeight);
      var containerTop = visibleBottom - containerHeight;

      console.log("[EventSidePaneForm] v1.30.0 Calculated position: left=" + containerLeft + ", top=" + containerTop + ", width=" + containerWidth);

      // Use fixed positioning in the parent window's coordinate system
      container.style.cssText = [
        "position: fixed",
        "top: " + containerTop + "px",
        "left: " + containerLeft + "px",
        "width: " + containerWidth + "px",
        "height: " + containerHeight + "px",
        "padding: 12px 16px",
        "background: #ffffff",
        "display: flex",
        "align-items: center",
        "justify-content: flex-end",
        "gap: 8px",
        "z-index: 100000",
        "border-top: 1px solid #e0e0e0",
        "box-sizing: border-box"
      ].join(" !important; ") + " !important";

      // Append to parent document body (not the side pane) for fixed positioning to work
      window.parent.document.body.appendChild(container);
      buttonContainer = container; // Store reference for cleanup
      console.log("[EventSidePaneForm] v1.30.0 Appended to parent body with fixed positioning");

      // Verify final position
      setTimeout(function() {
        var finalRect = container.getBoundingClientRect();
        console.log("[EventSidePaneForm] v1.30.0 Final button rect:", JSON.stringify({
          width: finalRect.width,
          height: finalRect.height,
          top: finalRect.top,
          left: finalRect.left
        }));
      }, 100);
    } else {
      // Fallback: append to document body with fixed positioning at bottom
      console.log("[EventSidePaneForm] v1.30.0 Side pane not found, using body fallback");

      // Use fixed position as last resort
      container.style.cssText = [
        "position: fixed",
        "bottom: 0",
        "left: 0",
        "right: 0",
        "width: 100%",
        "height: 56px",
        "padding: 12px 16px",
        "background: #ffffff",
        "display: flex",
        "align-items: center",
        "justify-content: flex-end",
        "gap: 8px",
        "z-index: 100000",
        "border-top: 1px solid #e0e0e0",
        "box-sizing: border-box"
      ].join(" !important; ") + " !important";

      document.body.appendChild(container);
      buttonContainer = container; // Store reference for cleanup

      // Log body dimensions for debugging
      var bodyRect = document.body.getBoundingClientRect();
      console.log("[EventSidePaneForm] v1.30.0 Body rect:", JSON.stringify({
        width: bodyRect.width,
        height: bodyRect.height,
        scrollHeight: document.body.scrollHeight
      }));
    }

    // Log final position for debugging
    var rect = container.getBoundingClientRect();
    console.log("[EventSidePaneForm] v1.30.0 Buttons injected. Container rect:", JSON.stringify({
      width: rect.width,
      height: rect.height,
      top: rect.top,
      left: rect.left
    }));
  }

  /**
   * Find the side pane container element in parent window
   * The side pane is created with paneId "eventDetailPane"
   */
  function findSidePaneContainer() {
    try {
      if (!window.parent || window.parent === window) {
        console.log("[EventSidePaneForm] v1.30.0 No parent window");
        return null;
      }

      var parentDoc = window.parent.document;

      // Log what we can see in parent DOM for debugging
      console.log("[EventSidePaneForm] v1.30.0 Searching parent DOM...");

      // Strategy 1: Try multiple data-id patterns Dataverse might use
      var dataIdSelectors = [
        '[data-id="eventDetailPane"]',
        '[data-id="event_detail_pane"]',
        '[data-paneid="eventDetailPane"]',
        '[data-pane-id="eventDetailPane"]',
        '#eventDetailPane',
        '[id="eventDetailPane"]',
        '[id*="eventDetailPane"]',
        '[id*="sidePane"]',
      ];

      var pane;
      for (var i = 0; i < dataIdSelectors.length; i++) {
        pane = parentDoc.querySelector(dataIdSelectors[i]);
        if (pane) {
          var rect = pane.getBoundingClientRect();
          console.log("[EventSidePaneForm] v1.30.0 Found by '" + dataIdSelectors[i] + "', rect:", rect.width + "x" + rect.height);
          if (rect.width > 100 && rect.height > 100) {
            return pane;
          }
        }
      }

      // Strategy 2: Look for aria labels/titles
      pane = parentDoc.querySelector('[aria-label*="Event"], [title*="Event"]');
      if (pane) {
        var rect = pane.getBoundingClientRect();
        console.log("[EventSidePaneForm] v1.30.0 Found by aria/title, rect:", rect.width + "x" + rect.height);
        if (rect.width > 100 && rect.height > 100) {
          return pane;
        }
      }

      // Strategy 3: Find by side pane class patterns
      var classSelectors = [
        '[class*="sidePane"]',
        '[class*="SidePane"]',
        '[class*="sidepane"]',
        '[class*="side-pane"]',
        '.ms-Panel',
        '.ms-Layer',
        '[role="complementary"]',
      ];

      for (var j = 0; j < classSelectors.length; j++) {
        var elements = parentDoc.querySelectorAll(classSelectors[j]);
        for (var k = 0; k < elements.length; k++) {
          var el = elements[k];
          var rect = el.getBoundingClientRect();
          if (rect.width > 300 && rect.height > 300) {
            console.log("[EventSidePaneForm] v1.30.0 Found by '" + classSelectors[j] + "', rect:", rect.width + "x" + rect.height);
            return el;
          }
        }
      }

      // Strategy 4: Find the iframe we're in and get its parent container with valid dimensions
      var iframes = parentDoc.querySelectorAll('iframe');
      console.log("[EventSidePaneForm] v1.30.0 Checking " + iframes.length + " iframes in parent");

      for (var m = 0; m < iframes.length; m++) {
        try {
          if (iframes[m].contentWindow === window) {
            console.log("[EventSidePaneForm] v1.30.0 Found our iframe at index " + m);

            // Log iframe's own dimensions
            var iframeRect = iframes[m].getBoundingClientRect();
            console.log("[EventSidePaneForm] v1.30.0 Iframe rect:", iframeRect.width + "x" + iframeRect.height + " at (" + iframeRect.left + "," + iframeRect.top + ")");

            // Walk up to find a container with valid dimensions
            var iframeParent = iframes[m].parentElement;
            var level = 0;
            while (iframeParent && iframeParent.tagName !== 'BODY' && level < 10) {
              var parentRect = iframeParent.getBoundingClientRect();
              console.log("[EventSidePaneForm] v1.30.0 Parent level " + level + " (" + iframeParent.tagName + "." + (iframeParent.className || "").substring(0, 30) + "): " + parentRect.width + "x" + parentRect.height);

              if (parentRect.width > 300 && parentRect.height > 300) {
                return iframeParent;
              }
              iframeParent = iframeParent.parentElement;
              level++;
            }
          }
        } catch (e) {
          // Cross-origin iframe, skip
        }
      }

      console.log("[EventSidePaneForm] v1.30.0 Side pane container not found");
      return null;
    } catch (e) {
      console.error("[EventSidePaneForm] v1.30.0 Error finding side pane:", e);
      return null;
    }
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
        cleanup(); // Remove buttons before closing
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
      // Method 1: Use Xrm.App.sidePanes from parent with correct pane ID
      if (window.parent && window.parent.Xrm && window.parent.Xrm.App && window.parent.Xrm.App.sidePanes) {
        var sidePanes = window.parent.Xrm.App.sidePanes;
        // Try eventDetailPane first (our ID)
        var pane = sidePanes.getPane("eventDetailPane");
        if (pane) {
          pane.close();
          console.log("[EventSidePaneForm] Closed via Xrm.App.sidePanes (eventDetailPane)");
          return;
        }
        // Fallback to old ID
        pane = sidePanes.getPane("event_detail_pane");
        if (pane) {
          pane.close();
          console.log("[EventSidePaneForm] Closed via Xrm.App.sidePanes (event_detail_pane)");
          return;
        }
      }

      // Method 2: Try current window's Xrm
      if (typeof Xrm !== "undefined" && Xrm.App && Xrm.App.sidePanes) {
        var pane2 = Xrm.App.sidePanes.getPane("eventDetailPane");
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
