# ISSUE: DataGrid attribute-metadata fetch fails — `retrieveMultipleRecords('EntityDefinition')`

> **Filed**: 2026-07-12 (during VisualHost `visual-host-version-update` UAT on SPAARKE DEV 1)
> **Severity**: Low — **non-fatal** (graceful fallback), but noisy + degrades column-header labels
> **Subsystem**: DataGrid framework (`@spaarke/ui-components/services/XrmDataverseClient.ts`)
> **NOT** caused by the VisualHost decoupling project — pre-existing; surfaced by clicking VisualHost's *expand* (drill-through) which opens the DataGrid list pages.

## Symptom

Every DataGrid drill-through/list page logs this on load (one per entity):

```
[XrmDataverseClient] Attribute metadata fetch failed for sprk_event; falling back to Xrm.Utility values only.
{ errorCode: 2147868684, code: 2147868684, title: 'Invalid Entity',
  message: 'The entity "EntityDefinition" cannot be found. Specify a valid query, and try again.' }
```

Observed on `sprk_eventspage`, `sprk_invoicespage`, `sprk_kpiassessmentspage` (all DataGrid pages; the entity in the message varies).

## Root cause

[`src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts:231`](../../../src/client/shared/Spaarke.UI.Components/src/services/XrmDataverseClient.ts) — `fetchAttributeDisplayNames()`:

```ts
const result = await xrm.WebApi.retrieveMultipleRecords('EntityDefinition', options);
```

The in-code comment (≈ lines 202–204) asserts that `Xrm.WebApi` auto-translates the singular `'EntityDefinition'` to the `EntityDefinitions` **metadata** collection. It does not. `Xrm.WebApi.retrieveMultipleRecords` resolves the argument against **data**-entity metadata (to derive the entity-set/collection name); there is no data entity `EntityDefinition`, so resolution fails with `2147868684 / "The entity \"EntityDefinition\" cannot be found."`. Metadata entities are **not** retrievable through the standard `Xrm.WebApi` data methods in this environment.

The call is wrapped in `try/catch` (≈ line 384) that warns and returns an empty map, so the grid still renders — but column headers fall back to humanized **logical names** instead of localized **DisplayName** labels, and every load logs a console error.

## Why this is a regression (fix seam already exists)

The client interface still carries (≈ lines 62–65):

```ts
/** Returns the global Xrm context (used to derive the MDA base URL for direct
 *  EntityDefinitions Web API calls when fetching attribute DisplayName labels). */
getGlobalContext?: () => XrmGlobalContextLike;
```

i.e. the **intended** design was a **direct Web API `fetch`** to `EntityDefinitions`, using the MDA base URL from `getGlobalContext()`. The implementation drifted to the invalid `retrieveMultipleRecords` call; `getGlobalContext` is now vestigial.

## Suggested fix (~15 lines, one file)

Replace the `retrieveMultipleRecords('EntityDefinition', …)` call in `fetchAttributeDisplayNames()` with a direct authenticated `fetch` to the EntityDefinitions metadata endpoint, deriving the base URL from `getGlobalContext().getClientUrl()`:

```
GET {clientUrl}/api/data/v9.2/EntityDefinitions(LogicalName='{entity}')
      ?$select=LogicalName
      &$expand=Attributes($select=LogicalName,DisplayName,AttributeType)
Headers: OData-Version 4.0, OData-MaxVersion 4.0, Accept application/json
```

(In MDA context the request is same-origin and rides the session cookie — no extra token wiring. Keep the existing `try/catch` graceful-fallback.) Parse `result.Attributes[]` exactly as the current code parses `ed.Attributes` (lines ≈ 232–248 unchanged). Then delete the misleading "Xrm translates singular→plural automatically" comment.

## Acceptance

- No `Attribute metadata fetch failed` console error on DataGrid page load.
- Column headers render **localized DisplayName** labels (not logical names) for `sprk_event` / `sprk_invoice` / `sprk_kpiassessment` grids.
- `XrmDataverseClient.test.ts` updated to assert the direct-fetch path (mock `fetch` + `getGlobalContext`), replacing the `retrieveMultipleRecords('EntityDefinition')` expectation.

## Deploy note

Fixing this rebuilds `@spaarke/ui-components`; the affected **DataGrid list-page** web resources (`sprk_eventspage`, `sprk_invoicespage`, `sprk_kpiassessmentspage`, and any other DataGrid pages) must be rebuilt + redeployed. The VisualHost create-wizard pages and VisualHost PCF are **unaffected** (they don't use `XrmDataverseClient`).
