# Parent-context filtered grids — architectural pattern

> **Status**: Project-local reference (will be promoted to `.claude/patterns/datagrid/parent-context-pattern.md` once stable)
> **Established**: 2026-06-03 (Phase C UAT, task 026)
> **Applies to**: Spaarke DataGrid Framework R1 + every downstream consumer (Phase D EventsPage, Phase E SemanticSearch, workspace widgets, future drill-throughs)
> **Origin**: Verified end-to-end during R1 Phase C UAT for Matter → KPI Assessments + Matter → Invoices drill-throughs

---

## What problem this pattern solves

A drill-through grid hosted in a Power Apps Custom Page dialog needs to show ONLY the child records related to the originating parent record (e.g., Invoices belonging to a Matter, not all Invoices in the tenant).

Two facts make this non-trivial:

1. **Power Apps Custom Page dialogs are isolated.** A dialog launched via `Xrm.Navigation.navigateTo({ pageType: 'webresource' })` has **no `Xrm.Page` access** to the originating form. `window.parent`/`window.top` + every `Xrm.Utility.getPageContext()` fallback returns nothing. The parent record id MUST arrive via the URL data envelope.
2. **Dataverse rejects FetchXML placeholder syntax at save time.** You cannot embed `<condition value='@MatterId'/>` in a saved query — the platform rejects it. The parent filter MUST be applied at runtime, not baked into the savedquery.

This pattern resolves both via a runtime overlay + a data-side hook in VisualHost's chart definition.

---

## Two-layer architecture

The framework filters a drill-through grid via TWO composable layers:

| Layer | Where it lives | What it does |
|---|---|---|
| **Base savedquery** | `configjson.source.savedQueryId` | Defines columns, sort, base filters (`statecode=0`, `querytype=0`) |
| **Parent overlay** | `configjson.behavior.parentContextFilter` | At runtime, `overlayParentContextFilter` (`fetchXmlOverlay.ts`) injects `<condition attribute='sprk_matter' operator='eq' value='{matterGuid}'/>` into the base FetchXML |

The overlay runs inside the framework **regardless of which savedquery you pair it with**. The same overlay works with a dedicated context view, a standard view, or an auto-picked entity default.

---

## Three options for the savedquery pairing

| Option | Setup | Best when |
|---|---|---|
| **A. Dedicated context view** *(what R1 task 020 used)* | Create `<Entity> - <Parent> Context` savedquery + reference its GUID in `configjson.source.savedQueryId` | UX needs columns tailored for the drill-through (e.g., strip the Owner column when the context is already a Matter) |
| **B. Reuse existing standard view** | Reference any existing savedquery GUID in `configjson.source.savedQueryId` + add `behavior.parentContextFilter` overlay | The standard "Active X" view's columns are already adequate |
| **C. Auto-pick entity default** | `source.type: 'savedquery-set', entityLogicalName: 'X'` + overlay | Quick scaffold — framework picks the default view automatically |

The dedicated views shipped in R1 (`Invoice - Matter Context`, `KPI Assessment - Matter Context`) are **not required** for the pattern to function — they exist because the column composition for the Matter drill-through diverges from the standard list view. New drill-throughs should default to **Option B or C** unless there is a UX reason to author a dedicated view.

---

## Always required (regardless of option A/B/C)

Three things, for every parent-context grid:

### 1. configjson `behavior.parentContextFilter`

```json
{
  "behavior": {
    "parentContextFilter": {
      "attribute": "sprk_matter",
      "parentContextKey": "matterId",
      "operator": "eq"
    }
  }
}
```

- `attribute` — the **CHILD entity's lookup attribute name** (NOT the lookup column reference; that's #2 below)
- `parentContextKey` — the key in the `parentContext` prop passed to `<DataGrid>` (the Custom Page shell builds this object)
- `operator` — typically `eq` for single-parent drill-throughs; `in` is supported for multi-parent cases (not currently exercised)

### 2. VisualHost chart-def `sprk_contextfieldname`

The **lookup column reference on the child entity pointing to the parent**, in `_<lookupfield>_value` format.

| Drill-through | `sprk_contextfieldname` |
|---|---|
| KPI Assessment → Matter | `_sprk_matter_value` |
| Invoice → Matter | `_sprk_matter_value` |
| Event → Matter | `_sprk_regardingmatter_value` *(because the Event lookup is named `sprk_regardingmatter`, not `sprk_matter`)* |

> **Per-entity gotcha**: the lookup field name is **not** uniformly `sprk_matter` across child entities. Inspect the entity metadata before configuring.

This value lives on the `sprk_chartdefinition` record that drives the VisualHost CardChrome card. When set, `VisualHostRoot.handleExpandClick` builds the navigation URL with `filterValue=<parentGuid>`.

> **Why we don't just touch the VisualHost PCF code**: NFR-06 forbids it in R1. The chart-def `sprk_contextfieldname` field is the data-side hook that lets VisualHost include the parent id in the URL without code changes.

### 3. Custom Page shell — URL data envelope parsing

The Custom Page shell MUST parse the URL `data=` envelope (form-encoded, **NOT JSON** — VisualHost convention) and pass `parentContext={{ matterId }}` to `<DataGrid>`. The shell pattern is shared verbatim between `sprk_kpiassessmentspage` and `sprk_invoicespage` — reusable for any future drill-through page.

Reference implementations:
- [`src/solutions/sprk_kpiassessmentspage/src/main.tsx`](../../../src/solutions/sprk_kpiassessmentspage/src/main.tsx) — `parseMatterId()` function (form-encoded envelope → JSON envelope fallback → `window.parent`/`window.top` Xrm fallback)
- [`src/solutions/sprk_invoicespage/src/main.tsx`](../../../src/solutions/sprk_invoicespage/src/main.tsx) — identical pattern

The fallbacks beyond the URL envelope (Xrm.Page, getPageContext) exist for resilience but **will not work inside the Custom Page dialog** due to the platform isolation noted above. The URL envelope is the only reliable channel.

---

## Anti-patterns

| ❌ Don't | ✅ Instead |
|---|---|
| Embed `<condition value='@MatterId'/>` placeholders in the savedquery FetchXML | Use the runtime overlay (`behavior.parentContextFilter`). Dataverse rejects placeholder syntax at save time. R1 task 020 deviation D-020-02 documents the original attempt. |
| Touch VisualHost PCF code to pass `recordId` to the Custom Page | Set chart-def `sprk_contextfieldname` (data-side). NFR-06 forbids touching VisualHost code in R1. |
| Rely on `Xrm.Page` / `Xrm.Utility.getPageContext()` inside the Custom Page dialog | Parse the URL `data=` envelope. Power Apps `navigateTo({pageType:'webresource'})` dialogs are isolated from the originating form's Xrm context. |
| JSON.parse the `data=` envelope | URL-decode it as a form-encoded string. VisualHost emits `entityName=...&filterValue=...&mode=dialog` form-encoded, not JSON. The reference shells try JSON only as a legacy fallback. |
| Assume the child lookup is named `sprk_matter` | Inspect the entity. Event uses `sprk_regardingmatter`. Future entities may differ. |

---

## End-to-end verification

After configuring all three required pieces, drill-through dialog → DevTools Console should show:

```
[DataGrid] fetchXml composition {
  parentContext: { matterId: '<guid>' },
  hasParentFilterMatch: true,
  composed: '<fetch ...><entity name="sprk_invoice">...<condition attribute="sprk_matter" operator="eq" value="<guid>"/>...</fetch>'
}
```

If `parentContext.matterId` is `''` (empty string), the URL envelope was missing `filterValue` → chart-def `sprk_contextfieldname` is not set. Verify the chart-def record via Dataverse MCP or the maker portal.

If `hasParentFilterMatch: false`, the configjson `behavior.parentContextFilter.attribute` doesn't match a lookup attribute on the child entity → check entity metadata.

---

## Where this pattern shows up next

- **Phase D — EventsPage migration (task 031)**: pair `behavior.parentContextFilter` with the Event → Matter lookup. **Use `_sprk_regardingmatter_value`**, not `_sprk_matter_value`.
- **Phase E — SemanticSearch migration (task 041)**: search is global; parent context likely not needed. The option-A vs B vs C choice will apply if any drill-through views are added later.
- **Future workspace widgets** hosting a `<DataGrid>` inside a parent-record context (Calendar widget, Insights Engine drill-throughs, etc.): same three required pieces apply.

---

## Reference files (R1 canonical implementations)

| Concern | File |
|---|---|
| Overlay implementation | [`src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts`](../../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts) |
| configjson schema | [`src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts`](../../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts) (`behavior.parentContextFilter`) |
| Custom Page shell (KPI) | [`src/solutions/sprk_kpiassessmentspage/src/main.tsx`](../../../src/solutions/sprk_kpiassessmentspage/src/main.tsx) |
| Custom Page shell (Invoice) | [`src/solutions/sprk_invoicespage/src/main.tsx`](../../../src/solutions/sprk_invoicespage/src/main.tsx) |
| Diagnostic logging | `DataGrid.tsx` — search for `[DataGrid] fetchXml composition` |
| R1 task that established the pattern | [`projects/spaarke-datagrid-framework-r1/tasks/023-drill-through-custom-pages.poml`](../tasks/023-drill-through-custom-pages.poml) |
| Deviation log for placeholder-rejection workaround | `projects/spaarke-datagrid-framework-r1/notes/drafts/020-deviation.md` (D-020-02) |
