/*
 * Spaarke Side-Pane Manager — Path B app-startup bootstrap for the Navigator pane.
 * Deployed as Dataverse web resource: sprk_SidePaneManager (webresourcetype 3 / JScript).
 * spaarke-side-pane-navigation-history-r1 — task 086 (spec FR-02).
 *
 * Productionized from the task-001 spike bootstrap (spike/sprk_sidepanespikebootstrap.js),
 * which was itself productionized from the recovered notes/retired-sidepane-code/SidePaneManager.ts.
 *
 * WHY THIS EXACT NAME + NAMESPACE: the spaarkedev1 application ribbon STILL contains the
 * hidden global bootstrap wiring from the retired side-pane platform (task-001 post-verdict
 * discovery) — a command + EnableRule that fires:
 *     CustomRule FunctionName="Spaarke.SidePaneManager.initialize"
 *                Library="$webresource:sprk_SidePaneManager"
 * The rule went dormant only because the sprk_SidePaneManager web resource was deleted.
 * Recreating THIS web resource under THIS name, exposing Spaarke.SidePaneManager.initialize,
 * reactivates the existing rule → the Navigator pane auto-creates at app load with no ribbon
 * import required.
 *
 * initialize() is the EnableRule entry: it MUST return true and MUST NEVER throw (a throwing
 * enable rule breaks the whole command bar). Registering the pane is its side effect, run on
 * every command-bar evaluation but guarded so it only ever creates ONE pane.
 *
 * Creates ONE app-level side pane (canClose:false, alwaysRender:true) hosting the Navigator
 * code page sprk_NavigatorPane.html. alwaysRender:true keeps the pane's JS (the capture poll)
 * alive while collapsed (NFR-05). Singleton guard so re-evaluation never duplicates the pane.
 *
 * Host-context only. No BFF, no plugin, no per-form handler (spec NFR-01/NFR-02).
 */
(function () {
  "use strict";

  var LOG = "[Spaarke.SidePaneManager]";
  var PANE_ID = "sprk-navigator";
  var PANE_TITLE = "Navigator";
  var WEBRESOURCE = "sprk_NavigatorPane.html";
  var PANE_WIDTH = 420;

  var _creating = false; // reentrancy guard while createPane() is in flight

  // Frame-walk to Xrm.App.sidePanes: the ribbon/enable-rule script may run in an iframe.
  // Try self -> parent -> top; accept the first exposing App.sidePanes. Never throws.
  function getSidePanesApi() {
    var frames = [];
    try { frames.push(window); } catch (e) {}
    try { if (window.parent && window.parent !== window) frames.push(window.parent); } catch (e) {}
    try { if (window.top && window.top !== window) frames.push(window.top); } catch (e) {}
    for (var i = 0; i < frames.length; i++) {
      try {
        var x = frames[i].Xrm;
        if (x && x.App && x.App.sidePanes && typeof x.App.sidePanes.createPane === "function") {
          return x.App.sidePanes;
        }
      } catch (e) { /* cross-origin frame — skip */ }
    }
    return null;
  }

  function paneExists(sidePanes) {
    try {
      if (typeof sidePanes.getPane === "function") {
        return !!sidePanes.getPane(PANE_ID);
      }
      if (typeof sidePanes.getAllPanes === "function") {
        var all = sidePanes.getAllPanes() || [];
        for (var i = 0; i < all.length; i++) {
          if (all[i] && all[i].paneId === PANE_ID) return true;
        }
      }
    } catch (e) { /* fall through */ }
    return false;
  }

  // Register (create + navigate) the Navigator pane once. Returns a Promise.
  function registerPane(sidePanes, focus) {
    if (paneExists(sidePanes)) {
      if (focus) {
        try { var p = sidePanes.getPane && sidePanes.getPane(PANE_ID); if (p && p.select) p.select(); } catch (e) {}
      }
      return Promise.resolve(true);
    }
    if (_creating) {
      return Promise.resolve(false);
    }
    _creating = true;
    console.log(LOG, "creating Navigator pane…");
    return sidePanes
      .createPane({
        paneId: PANE_ID,
        title: PANE_TITLE,
        canClose: false,      // spec MUST rule — always-available docked pane
        width: PANE_WIDTH,
        isSelected: false,    // start collapsed; alwaysRender keeps the capture poll alive
        alwaysRender: true    // load-bearing: keeps pane JS running while collapsed (NFR-05)
      })
      .then(function (pane) {
        return pane.navigate({ pageType: "webresource", webresourceName: WEBRESOURCE }).then(function () {
          console.log(LOG, "Navigator pane created + navigated to " + WEBRESOURCE);
          _creating = false;
          return true;
        });
      })
      .catch(function (err) {
        _creating = false;
        console.warn(LOG, "createPane/navigate failed:", (err && err.message) ? err.message : err);
        return false;
      });
  }

  // EnableRule entry (ribbon FunctionName="Spaarke.SidePaneManager.initialize").
  // MUST return true and MUST NOT throw. Registering the pane is the app-load side effect.
  function initialize() {
    try {
      var sidePanes = getSidePanesApi();
      if (sidePanes) { registerPane(sidePanes, false); }
    } catch (e) {
      console.warn(LOG, "initialize() bootstrap error:", e);
    }
    return true;
  }

  // Optional ribbon Command / manual entry: open + focus the pane.
  function openPane() {
    var sidePanes = getSidePanesApi();
    if (!sidePanes) { console.log(LOG, "sidePanes API not reachable (openPane)."); return; }
    registerPane(sidePanes, true);
  }

  var g = (typeof window !== "undefined" ? window : globalThis);
  g.Spaarke = g.Spaarke || {};
  g.Spaarke.SidePaneManager = g.Spaarke.SidePaneManager || {};
  g.Spaarke.SidePaneManager.initialize = initialize;
  g.Spaarke.SidePaneManager.openPane = openPane;

  // Auto-init on load (covers console-paste / Code Page injection, and the case where the
  // enable rule has not yet fired). If the shell isn't ready, retry a few times with backoff,
  // then give up — the ribbon enable rule + manual initialize() remain available.
  var attempts = 0;
  (function autoInit() {
    try {
      var sidePanes = getSidePanesApi();
      if (sidePanes) { registerPane(sidePanes, false); return; }
    } catch (e) { /* retry */ }
    if (++attempts > 10) { console.log(LOG, "gave up auto-init after " + attempts + " attempts."); return; }
    setTimeout(autoInit, 500 * attempts);
  })();
})();
