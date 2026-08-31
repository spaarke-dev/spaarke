---
name: uci-hide-form-header-entity-name-2026-08-18
description: Supported Client API to hide/blank the table display-name (entity_name_span in form-header-title) on a UCI main form — setFormEntityName exists; setBodyVisible hides a different region
metadata:
  type: reference
---

# Hiding the form-header entity/table NAME span in UCI (2026-08-18)

Sibling of [[uci-single-tab-navigator-hide-2026-08-18]] (that one = the tab-navigator PIVOT; this one = the table display-name in the header title). Different DOM elements, different APIs.

**Target:** `<span data-id="entity_name_span">To Do</span>` inside `data-lp-id="form-header-title"` — the TABLE display name rendered in the form header. Separate from: the dialog chrome title `h1#defaultDialogChromeTitle` (navigateTo target:2 outer chrome, not form-config), the record primary-name title, and the tab navigator pivot.

## Verdict: YES — supported method exists

- **`formContext.ui.setFormEntityName(arg)` EXISTS in the current Client API** (Learn page ms.date 2022, updated 2024-12-06). Signature `formContext.ui.setFormEntityName(arg /* String, required */)`. Description verbatim: "Sets the name of the table to be displayed on the form." This is exactly the `entity_name_span` text. It lives on **`formContext.ui`** (NOT headerSection). The Client API reference is UCI-scoped; method is current for 2025-2026.
- To BLANK it, pass a space: `formContext.ui.setFormEntityName(" ")`. The method is documented only as "sets the name" — the empty/blank behavior is NOT explicitly documented, so verify empirically. Prefer a single space `" "` over `""` (empty string can be ignored / revert to default label in some renderers). This is the supported lever; call it in the existing OnLoad handler.

## What the OTHER candidates do (rejected)

- **`setBodyVisible(false)` — WRONG element.** "Sets the header's body visibility" = hides the header BODY = the read-only header COLUMN-VALUE region (the up-to-4 fields), NOT the entity/table name. UCI-only. Using it would blank the four header fields, not the "To Do" label. Reject for this goal.
- **`setCommandBarVisible` / `setTabNavigatorVisible`** — command bar and tab pivot respectively; neither touches the entity-name span.
- **No maker/designer property hides the entity name.** form-designer-header-properties (updated 2026-04) exposes only: Show header flyout, high-density vs low-density header, Show image in the form. High-density header "ensures the record title never truncates" — the title/entity-name ALWAYS renders; there is no checkbox to remove it.
- **DOM manipulation** (query `[data-id="entity_name_span"]`, set `display:none`) works but is UNSUPPORTED (undocumented data-id, can break on UI updates). Not needed since setFormEntityName covers it. Carl de Souza's header-hiding article + comments confirm no one surfaced a supported method for the title element — but setFormEntityName IS that method.

## Exact OnLoad code
```js
function onLoad(executionContext) {
    var formContext = executionContext.getFormContext();
    formContext.ui.setFormEntityName(" "); // blank the header table-name span
    formContext.ui.headerSection.setTabNavigatorVisible(false); // hide single-tab pivot (separate)
}
```

## Sources
- Learn setFormEntityName — https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui/setformentityname (MOST authoritative; method exists, String arg, "Sets the name of the table to be displayed on the form")
- Learn formContext.ui methods list — https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui (setFormEntityName listed alongside setFormNotification/getFormType)
- Learn setBodyVisible — https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/formcontext-ui-headersection/setbodyvisible ("Sets the header's body visibility"; UCI-only) — proves it is a DIFFERENT region
- Learn form-designer-header-properties (updated 2026-04) — no property to hide entity name; high-density header always shows the title
- Carl de Souza header-hiding article — corroborates the three headerSection setters + that none target the title span

## Open questions
- Empirical: does `setFormEntityName(" ")` fully blank the span with no residual layout gap, and does it hold in a navigateTo target:2 dialog (OnLoad fires there — should work; unverified for possible brief flash before it applies)? Verify in the To Do form. `""` vs `" "` behavior also worth a quick test.
