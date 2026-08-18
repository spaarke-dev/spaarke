# Modifying OOB Dataverse Form-in-Dialog Chrome

> **Last Reviewed**: 2026-08-18
> **Status**: Current
> **Severity**: Medium — cosmetic-only, but the two gotchas below cause repeated failed deploys if unknown (Smart To Do R5 UAT: 4 blind deploys before the frame boundary was found)

## When

You need to change the **chrome of an out-of-box Dataverse main form** — the record-title header, the single-tab navigator pivot, the entity-name subtitle, the dialog header background — for a `sprk_*` (or OOB) form that opens either full-page OR as an `Xrm.Navigation.navigateTo({ pageType: "entityrecord" | "entitylist" }, { target: 2 })` modal.

Common cases:
- Hide the single-tab NAVIGATOR ("General") so a one-tab form reads as one page
- Hide the header entity-name SUBTITLE ("To Do") so every record shows a uniform title
- Restyle the dialog chrome (e.g. dark-mode header background)

This is distinct from our **proprietary** modal shell (`SprkModal`, see [`modal-shell.md`](modal-shell.md)) — this pattern is for the **platform-rendered** OOB form chrome we do not own.

## The supported-first ladder (ALWAYS try in this order)

1. **Form designer / formxml** — `ShowLabel`, `Visible`, section layout. *Caveat:* UCI renders the header from **formjson**, and the single-tab navigator NAME renders independently of the tab's `ShowLabel`/`Label` — so `showlabel="false"` has NO effect on it.
2. **Supported Client API in a FORM OnLoad handler** — the real lever for most chrome:
   - `formContext.ui.headerSection.setTabNavigatorVisible(false)` — hides the "General" pivot ✅
   - `formContext.ui.headerSection.get/setBodyVisible`, `getCommandBarButtonVisible`, etc.
   - **NOT** `formContext.ui.setFormEntityName(...)` for the subtitle — it prefixes the record TITLE (adds a stray `": "` colon), it does NOT touch the `entity_name_span`. There is **no supported API** for that span.
3. **Unsupported DOM (last resort, operator-approved)** — only when no supported API exists (the entity-name subtitle, chrome background). Use the two-gotcha-safe recipe below.

## Gotcha 1 (ROOT CAUSE): the form script runs in a DIFFERENT frame than the chrome

A form `OnLoad` handler executes inside the **form's own iframe document**. But UCI paints the **record header and dialog chrome in the SHELL (`window.top`) document** — a different frame. So:

```js
// ❌ Finds NOTHING — the span is not in the form iframe's document
document.querySelectorAll('[data-id="entity_name_span"]');
```

This is silent: no error, the hide just does nothing. It burned 3 deploys before a live console test proved the span was only reachable from `window.top.document`.

```js
// ✅ Collect the form doc + shell + iframes of both, then query all of them
var collectDocs = function () {
    var docs = [], push = function (d) { if (d && docs.indexOf(d) < 0) docs.push(d); };
    push(document);
    var roots = [document];
    try { if (window.top && window.top.document) { push(window.top.document); roots.push(window.top.document); } } catch (e) {}
    for (var r = 0; r < roots.length; r++) {
        try {
            var frames = roots[r].querySelectorAll("iframe");
            for (var f = 0; f < frames.length; f++) { try { push(frames[f].contentDocument); } catch (e2) {} }
        } catch (e3) {}
    }
    return docs; // all same-origin (*.crm.dynamics.com); cross-origin frames silently skipped
};
```

## Gotcha 2: UCI re-renders the header in PHASES — a one-shot hide gets undone

The form loads, header paints, then re-paints (e.g. status flips to "- Saved"). A single `display:none` set early is reverted by the later re-render. Fix: a **`MutationObserver`** per collected document that re-applies on every mutation, with a **bounded 30s lifetime** so no observer lingers.

## The recipe

Register **one FORM `OnLoad` handler** (it MUST be on the form — a webresource loaded another way won't fire inside a `navigateTo` dialog). In it:

1. Supported API first (`setTabNavigatorVisible(false)` etc.), each step independently try-guarded.
2. For anything needing DOM: `collectDocs()` → hide across all → install a `MutationObserver` on each doc's `body` (`{childList:true, subtree:true}`) → `setTimeout(disconnect, 30000)`.
3. Never throw — guard every step so a chrome tweak never blocks the form.

**Scope decision**: hide by a stable `data-id` selector. An **unscoped** hide (every `entity_name_span` across frames) is safe when the form opens over a **non-entity host** (a Code Page / workspace widget with no background entity form) — which is the Spaarke To Do case. If your form can open over ANOTHER entity form, scope the hide so you don't collateral-hide the background form's chrome.

## Console-first workflow (do this BEFORE every deploy)

Redeploying a web resource to test a selector is a slow guess-loop. Instead, open the dialog, F12 → Console (set the frame dropdown to **`top`**), and paste a snippet that runs the same `collectDocs` + query and reports counts/text + applies the change live. Iterate in seconds; deploy ONCE when it visibly works. The diagnostic that found Gotcha 1:

```js
(function () {
  var docs = [document];
  try { if (window.top && window.top.document && docs.indexOf(window.top.document) < 0) docs.push(window.top.document); } catch (e) {}
  document.querySelectorAll('iframe').forEach(function (f) { try { if (f.contentDocument) docs.push(f.contentDocument); } catch (e) {} });
  var total = 0;
  docs.forEach(function (d, i) {
    var s = d.querySelectorAll('[data-id="entity_name_span"]');
    if (s.length) { console.log('doc[' + i + ']', s.length, Array.prototype.map.call(s, function (x) { return x.textContent; })); s.forEach(function (x) { x.style.display = 'none'; total++; }); }
  });
  console.log('hidden', total, '(0 → span is in a cross-origin frame or shadow DOM)');
})();
```

## Deployment

The chrome script is a JS **web resource** (`webresourcetype: 3`) registered as an OnLoad handler on the form. To update: PATCH the web resource `content` (base64), then `PublishXml` the web resource + entity. Handler registration (formLibraries `<Library>` + onload `<Handler>`) is a one-time formxml edit. Reference deploy script: `scripts/`-style Web-API PATCH + PublishXml (see the R5 UAT script `register-tabnav.ps1`).

## Reference implementation

- **`src/client/webresources/js/sprk_todo_hide_tabnav.js`** (v1.6.0) — the canonical example: `setTabNavigatorVisible(false)` (supported) + cross-frame observer-backed `entity_name_span` hide (unsupported, operator-approved). Read the header comment block — it narrates the full v1.0→v1.6 root-cause trail.

## Supported API reference (Microsoft Learn — Unified Interface only)

- `formContext.ui.headerSection.setTabNavigatorVisible`
- `formContext.ui.headerSection` body/command-bar visibility getters/setters
- (No supported API exists for `entity_name_span`, the record-title prefix behaves as `setFormEntityName` documents.)

## Do NOT

- **Query only `document`** for chrome elements — the chrome lives in `window.top`, not the form iframe (Gotcha 1).
- **Use a one-shot hide** — UCI re-render undoes it; use the bounded observer (Gotcha 2).
- **Use `setFormEntityName` to hide the subtitle** — wrong element; it prefixes the record title and injects a `": "` colon.
- **Leave the MutationObserver running forever** — always `disconnect()` after a bounded window (30s).
- **Deploy to test a selector** — use the console-first loop; deploy only the confirmed change.
- **Rely on `data-id` hooks being permanent** — they are UCI-internal and MAY break on a platform update. Prefer the supported API whenever one exists; document the DOM dependency where you use it.

## Related

- [`record-modal-selection.md`](record-modal-selection.md) — deciding OOB `navigateTo` vs proprietary modal in the first place
- [`navigateto-popup-result-bridge.md`](navigateto-popup-result-bridge.md) — cross-window result signaling for `navigateTo` webresource dialogs (a sibling frame-boundary gotcha)
- [`modal-shell.md`](modal-shell.md) — the proprietary `SprkModal` shell (the chrome we DO own)
- [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../../docs/standards/MODAL-DECISION-CRITERIA.md) — OOB vs proprietary decision layer
- [`docs/architecture/spaarke-todo-architecture.md`](../../../docs/architecture/spaarke-todo-architecture.md) — the To Do form + Code Page + widget surfaces this pattern was proven on
