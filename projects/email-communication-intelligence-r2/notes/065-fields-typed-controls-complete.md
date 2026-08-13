# Task 065 — E2b typed controls + E2c "+ Update other fields" (complete)

> **Completed**: 2026-08-11 · FULL rigor · opus · Step 9.5 clean · plan §9 (build SECOND). Shared lib only — NO BFF, no new host prop.

## What changed (`FieldUpdateReconcileTab.tsx`)
- **E2b type-correct controls.** Local extended Xrm type (`ReconcileXrm`: `Utility.getEntityMetadata` + `Utility.lookupObjects` + `Navigation.navigateTo`) cast from the shared `getXrmForPicker` (does NOT widen ui-components' bridge). A best-effort, guarded `useEffect` resolves `getEntityMetadata(targetEntity,[targetField])` per proposal into `fieldMeta`; `parseFieldMeta` defensively handles PascalCase/camelCase + array/collection Attributes. `renderValueControl` switches on `controlKind(meta, fieldType)`:
  - **DateTime** → `<Input type="date">` · **Number** → `<Input type="number">` (both work from the `fieldType` hint, no metadata needed).
  - **Picklist/State/Status** → `<Select>` seeded from the metadata option-set (`field-reconcile-edit-optionset`).
  - **Lookup** → OOB advanced-lookup (`lookupObjects` with the attribute `Targets`) → sets normalized id + shows the picked name (`field-reconcile-edit-lookup` + `-btn`).
  - else / no metadata / **non-MDA** → the original editable text `<Input>` (`field-reconcile-edit`). Nothing is ever blocked on missing metadata.
- **E2c "+ Update other fields".** Full-width button (shown only for a confirmed `regarding`) → self-resolved `Navigation.navigateTo({pageType:'entityrecord', entityName, entityId}, {target:2,…})` (guarded no-op non-MDA); re-`load()`s the proposals on close (the record may have changed). Modal-on-modal — the tab stays mounted.
- **Accept `{overrideValue}` contract unchanged** — the control's current string (option-set value / lookup id / date) flows through `edited` → `handleAccept` exactly as before.

## Verify
- tsc 0-err. jest **15/15** on the tab suite (11 existing + 4 new: option-set dropdown→Accept value; lookup→normalized id+name; non-MDA text fallback; E2c navigateTo). **47/47** across ReconciliationWorkspace/BrowseShell/ReconcileTabs (no regressions).
- Step 9.5: code-review CLEAN, adr-check CLEAN (ADR-021 tokens; ADR-012 via getXrmForPicker; ADR-050/022 untouched; no BFF).

## Next (plan §9)
064 (E1b `onLaunchCreateRecord` + QuickStartModal additive `onRecordCreated`; E1c BFF `.eml`→session-document resolver) — BFF §10 + SpaarkeAi hot-path; /conflict-check both.
