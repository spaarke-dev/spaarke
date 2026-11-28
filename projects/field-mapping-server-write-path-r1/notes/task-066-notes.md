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

## 4. Dev import (main session, 2026-11-27, owner go "Yes, import on dev")

- **Method.** The 051 route. `pac solution export` of dev SpaarkeMaster (unmanaged) at ~20:18Z to `C:\tmp\fm066-rollback\SpaarkeMaster_dev_pre066.zip`. Only the `sprk_KPIAssessment` `RibbonDiffXml` in `customizations.xml` was replaced with the master copy (49eece4926); every other file in the zip is unchanged (683 = 683 files).
- **Drift check.** The Spaarke ids on dev's KPI ribbon that are not in the repo copy were exactly the seven `sprk.matter.subgrid.kpi.AddKpiButton*` ids plus `sprk.CreatePrivilege.sprk_kpiassessment.DisplayRule`, which only Add KPI used. `Merge-NativeCreateRibbon.ps1` run on dev's copy gave the repo copy except for node order and that display rule. No other entity referred to `AddKpiButton` or `sprk_kpi_ribbon_actions.js`.
- **Import.** Async operation b7cfbe46…, 20:22:41Z–21:16:02Z, succeeded. That is 54 minutes, against 5 for 051. pac stopped waiting at 9 minutes and printed "Solution import failed" while the job was still running; the job status is the result. Publish All ran 21:16–21:22Z.
- **Web resource.** `sprk_kpi_ribbon_actions.js` (803a95f8…) had no dependents. It was removed from SpaarkeMaster with `RemoveSolutionComponent` at 21:23:53Z and stays on dev outside any solution.
- **Verify.**
  - `Test-NativeCreateSurfaces.ps1 -Verify`: PASS (23 tables, Add KPI absent).
  - `Set-CreatePrivilegeRibbon.ps1 -Verify`: PASSED.
  - Not looked at in the browser.
- **Overwrite window.** The import re-applied the 20:18Z copy of every SpaarkeMaster component.
  - No deploy workflow ran between the export and the import's end; Deploy SpaarkeAi ran at 21:28Z, after it.
  - UAC-r2, mcp-r1, word-add-in-r1 and ontology-r1 each wrote no SpaarkeMaster component on dev in the window; their writes were data rows only. The one component change just before it, `prvReadsprk_ProcessingJob` (Basic) added to the Secure Record Owner role at ~20:13Z by another project, is still on the role after the import (read 21:35Z). Nothing was reverted.
- **Rollback.** Re-import `SpaarkeMaster_dev_pre066.zip`, or put `dev_kpi_ribbondiff_pre066.xml` back into a fresh export. Then add `sprk_kpi_ribbon_actions.js` back to SpaarkeMaster.
