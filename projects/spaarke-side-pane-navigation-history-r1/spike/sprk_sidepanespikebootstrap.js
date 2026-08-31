/*
 * Task 001 SPIKE — Path B bootstrap.
 * Deployed as Dataverse web resource: sprk_sidepanespikebootstrap.js (webresourcetype = 3 / JScript).
 *
 * Productionized from the recovered notes/retired-sidepane-code/SidePaneManager.ts.
 * Creates ONE app-level side pane (canClose:false, alwaysRender:true) hosting the
 * diagnostic pane sprk_sidepanespike.html, with a singleton guard so re-evaluation
 * never duplicates the pane.
 *
 * THREE entry paths (validated by the spike):
 *   1. Auto-init on script load          — console paste / Code Page injection (Stage 1).
 *   2. Spaarke.SidePaneSpike.initialize() — callable manually from DevTools (Stage 1).
 *   3. Spaarke.SidePaneSpike.enable()     — global-ribbon EnableRule side-effect (Stage 2,
 *      returns true; registers the pane as a side effect on every command-bar eval).
 *      Spaarke.SidePaneSpike.openPane()   — global-ribbon button Command (focuses/creates).
 *
 * Host-context only. No BFF, no plugin, no per-form handler (spec NFR-01/NFR-02).
 */
(function () {
  "use strict";

  var LOG = "[Spaarke.SidePaneSpike]";
  var PANE_ID = "sprk-spike";
  var PANE_TITLE = "Side-Pane Spike";
  var WEBRESOURCE = "sprk_sidepanespike.html";
  var PANE_WIDTH = 420;

  var _creating = false; // reentrancy guard while createPane() is in flight

  // Frame-walk to Xrm.App.sidePanes: pane/ribbon scripts may run in an iframe.
  // Try self -> parent -> top; accept the first exposing App.sidePanes.
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

  // Register (create + navigate) the spike pane once. Returns a Promise.
  function registerPane(sidePanes, focus) {
    if (paneExists(sidePanes)) {
      console.log(LOG, "pane already present; skipping create.");
      if (focus) {
        try { var p = sidePanes.getPane && sidePanes.getPane(PANE_ID); if (p && p.select) p.select(); } catch (e) {}
      }
      return Promise.resolve(true);
    }
    if (_creating) {
      console.log(LOG, "create already in flight; skipping.");
      return Promise.resolve(false);
    }
    _creating = true;
    console.log(LOG, "creating pane…");
    return sidePanes
      .createPane({
        paneId: PANE_ID,
        title: PANE_TITLE,
        canClose: false,      // spec MUST rule — always-available docked pane
        width: PANE_WIDTH,
        isSelected: !!focus,  // Stage 1: false = starts collapsed (proves alwaysRender)
        alwaysRender: true    // keeps pane JS running while collapsed (the load-bearing flag)
      })
      .then(function (pane) {
        return pane.navigate({ pageType: "webresource", webresourceName: WEBRESOURCE }).then(function () {
          console.log(LOG, "pane created + navigated to " + WEBRESOURCE);
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

  // Entry 1/2: idempotent bootstrap.
  function initialize() {
    var sidePanes = getSidePanesApi();
    if (!sidePanes) {
      console.log(LOG, "sidePanes API not reachable yet.");
      return false;
    }
    registerPane(sidePanes, false);
    return true;
  }

  // Entry 3 (ribbon Command): open/focus the pane.
  function openPane() {
    var sidePanes = getSidePanesApi();
    if (!sidePanes) { console.log(LOG, "sidePanes API not reachable (openPane)."); return; }
    registerPane(sidePanes, true);
  }

  // Entry 3 (ribbon EnableRule): return true so the button is enabled; the call itself
  // is the app-load bootstrap side-effect. Never throws (a throwing enable rule breaks
  // the command bar). This is the mechanism that makes the pane auto-create at app load.
  function enable() {
    try { initialize(); } catch (e) { console.warn(LOG, "enable() bootstrap error:", e); }
    return true;
  }

  var g = (typeof window !== "undefined" ? window : globalThis);
  g.Spaarke = g.Spaarke || {};
  g.Spaarke.SidePaneSpike = g.Spaarke.SidePaneSpike || {};
  g.Spaarke.SidePaneSpike.initialize = initialize;
  g.Spaarke.SidePaneSpike.openPane = openPane;
  g.Spaarke.SidePaneSpike.enable = enable;

  // Entry 1: auto-init on load (console-paste + Code Page injection). If the shell
  // isn't ready yet, retry a few times with backoff, then give up (ribbon enable rule
  // and manual initialize() remain available).
  var attempts = 0;
  (function autoInit() {
    if (initialize()) return;
    if (++attempts > 10) { console.log(LOG, "gave up auto-init after " + attempts + " attempts."); return; }
    setTimeout(autoInit, 500 * attempts);
  })();
})();
