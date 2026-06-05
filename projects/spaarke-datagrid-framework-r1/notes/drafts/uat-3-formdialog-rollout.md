# UAT-3 — `formDialog` rowOpen type rollout

> **Date**: 2026-06-04
> **Trigger**: User UAT on the redeployed Custom Pages. Two refinements over UAT-1/UAT-2:
> 1. The dataset grid code page standard should be **modal**, not new tab.
> 2. The drill-through new-tab-vs-modal choice should eventually be configurable per chart-def (deferred — see "Followup" below).

---

## Framework change

`@spaarke/ui-components/components/DataGrid/DataGrid.tsx` — the framework's `defaultRecordOpen` previously **ignored** the configjson's `rowOpen` and always opened a new browser tab via `window.open(main.aspx?pagetype=entityrecord&...)`. That meant every configjson change to `rowOpen.type` was a no-op (a latent bug from Phase A; the schema in `DataGridConfiguration.ts` already declared the type union but nothing dispatched on it).

This task wires up actual dispatch:

```ts
const rowOpen = resolved?.rowOpen;
if (rowOpen?.type === 'formDialog' && xrm?.Navigation?.navigateTo) {
  // Xrm.Navigation.navigateTo({pageType:'entityrecord'}, {target:2, position:1, width:80%, height:80%})
  // → Dataverse-native centered modal of the entity record form.
  ...
  return;
}
// Legacy fallback: open in new tab via window.open
```

Sibling additions to `DataGridConfiguration.ts`:

- `RowOpenType` union gained `'formDialog'` (next to existing `'navigateToForm'`, `'dialog'`, `'webResource'`, etc.).
- `RowOpenConfig` gained optional `formDialogWidthPercent` and `formDialogHeightPercent` (1..100; default 80 if omitted or out of range — clamped via `clampPercent()` helper).

Hosts can still pass `onRecordOpen` to override anything in the configjson — the escape hatch for surfaces that need custom side panes / registered React dialogs / etc.

## Configjson updates (PATCHed live on `spaarkedev1`)

| Entity | Record | Before | After |
|---|---|---|---|
| `sprk_event` | `e15c2b93-a05f-f111-a825-70a8a59455f4` | `{"type":"navigateToForm"}` | `{"type":"formDialog"}` |
| `sprk_invoice` | `d021827b-9b5e-f111-ab0c-7c1e521545d7` | `{"type":"navigateToForm"}` | `{"type":"formDialog"}` |
| `sprk_kpiassessment` | `3019a06e-9b5e-f111-ab0c-7c1e521545d7` | `{"type":"navigateToForm"}` | `{"type":"formDialog"}` |

GET-after-PATCH verified each record has the new `rowOpen` shape.

## App.tsx revert

`EventsPage/App.tsx` UAT-2 standalone-only `onRecordOpen` override removed. The dialog behavior is now driven entirely by the configjson — same code path on standalone EventsPage as on the drill-through host as on Invoices/KPI.

The `xrmHelpers` import + `openEventInDialog` function + `isDrillThrough` conditional are all gone. App.tsx is back to the post-UAT-1 shape minus the `onRecordOpen` prop.

## Browser observed behavior

After hard refresh, clicking the primary-name link on any row of:

- Standalone EventsPage (with Calendar pane)
- Drill-through Events grid (from Matter card)
- Drill-through Invoices grid (from Matter card)
- Drill-through KPI Assessments grid (from Matter card)

→ Opens the entity record form in a centered Dataverse-native modal at 80%×80%. The current page (the grid) stays in place; the user dismisses the dialog with Esc / X to return.

## Followup

A future task should allow per-drill-through override. When a chart-def opens the Custom Page, it could pass a `?data=mode=dialog&rowOpenMode=newTab&...` URL param; the host shell would read `rowOpenMode` and override the configjson's `rowOpen.type` accordingly. This keeps the "standard is modal" default while letting individual chart-defs opt back to new-tab for their specific drill-throughs.

Filed in TASK-INDEX for post-R1 consideration.

## Build artifacts

- Shared library: `Spaarke.UI.Components` rebuilt clean (tsc exit 0).
- All 4 consumers rebuilt + redeployed via `Deploy-AllDataGridConsumers.ps1`.
  - EventsPage 1230 KB
  - sprk_invoicespage 1226 KB
  - sprk_kpiassessmentspage 1226 KB
  - LegalWorkspace 2159 KB
- Single atomic `PublishXml` issued at end. All 4 visible to runtime simultaneously.
