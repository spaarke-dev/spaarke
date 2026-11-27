# Task 066: remove "Add KPI Assessment"; "+ New" is the one KPI create

Owner 2026-11-26 (051 OQ-5): "only +New is required".

## 1. Change (d6de065bb3, merged as bd0b5a5873; title nit in 934c1a2a89)

- `SpaarkeMaster/Entities/sprk_KPIAssessment/RibbonDiff.xml`: the `sprk.matter.subgrid.kpi.AddKpiButton` CustomAction, command, four labels and the create-privilege display rule only it used are removed. The subgrid "+ New" (`sprk.SecureChild.sprk_kpiassessment.SubGrid.New`, its command with the table parameter, `NewAvailable`, `NativeNewAllowed`) is unchanged.
- `native-create-surfaces.json`: `sprk_kpiassessment` carries `mustBeAbsent: [AddKpiButton]` instead of `alsoPresent`.
- `Test-NativeCreateSurfaces.ps1 -Verify` fails and names a table that still shows a retired button. `Merge-NativeCreateRibbon.ps1` strips each `mustBeAbsent` button, its command and its labels from a fresh export, and throws if the button survives.
- Deleted (nothing else uses them): `SpaarkeCore/entities/sprk_matter/RibbonDiff/add-kpi-ribbon.xml`, both copies of `sprk_kpi_ribbon_actions.js` and its `.data.xml`. The root component is removed from `SpaarkeMaster/Other/Solution.xml`; the `Spaarke.KpiRibbon.openQuickCreate` launcher entry and the inventory row are removed.
- Tests: Pester must-fire / must-not-fire for `-Verify` and the merge; ArchTest `TheKpiAssessmentSubgrid_KeepsItsOneNew_AndNoRepoRibbonCarriesTheAddKpiButton` and its control; the Add KPI Jest block removed.

## 2. Checks

- Pester 5.7.1 `tests/scripts/Test-NativeCreateSurfaces.Tests.ps1`: 32/32 (the four 066 cases fail on the base scripts).
- ArchTests: full suite 2854/2854 on the merged branch.
- Jest `secureChildRibbonScript.test.ts`: 67/67.
- Review (Sonnet adversarial): no F-class. The SpaarkeMaster pack checks (`Find-LeakyDependencies`, `Find-MissingWebResourceReferences`) pass; all 236 web-resource root components have a file. A merge re-run on all 23 tables leaves the committed files unchanged except `sprk_Event`, whose node order differs at the base already (Add Memo was added by hand after the merge; order is set by `Sequence`, so harmless).
- Known limits: two comments in `sprk_secure_child_ribbon.js` (both copies) still name the Add KPI command; fix them when that web resource is next edited.

## 3. Dev import (needs the owner's go)

- Solution: SpaarkeMaster (unmanaged), or a fresh dev export merged with `Merge-NativeCreateRibbon.ps1` on `sprk_kpiassessment` (the 051 route), then Publish All.
- Component: the `sprk_kpiassessment` RibbonDiff only. No web resource upload. `sprk_kpi_ribbon_actions.js` stays on dev as an orphan unless removed there; remove it from dev's SpaarkeMaster solution at import time so a later export does not bring the root component back.
- Verify: `Test-NativeCreateSurfaces.ps1 -Verify` passes `sprk_kpiassessment` (button absent). Until the import, `-Verify` on dev reports it as a failure, as intended.
- Rollback: restore the pre-import `sprk_kpiassessment` RibbonDiff from the export.
