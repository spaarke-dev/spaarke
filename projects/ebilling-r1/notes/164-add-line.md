# Task 164 - Add line: notes

This file is the task 164 summary. 164a and 164b have their own notes (`164a-*.md`, `164b-add-line-client.md`); the final sections (what landed, deviations, Q1-Q7, K1-K6, publish delta) are written when 164 closes. This section is 164c.

## 164c - billing event ribbon shell, hides and deploy script

### What landed

- `scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1`: a copy of `Deploy-EbillingBillingExportRibbon.ps1` for `sprk_billingevent` (dry run by default, `-Apply`, `-Verify`, the same stored-ribbon refusal, the same rollback copy, scoped PublishXml, no `PublishAllXml`). One change from the reference: the hide list is printed BEFORE the stored-ribbon refusal, so a refused dry run still shows what the diff would hide.
- `infrastructure/dataverse/ribbon/BillingEventRibbons/` (`Other/Solution.xml`, `Other/Customizations.xml`, `Entities/sprk_billingevent/Entity.xml`): the ribbon-only shell, root component `sprk_billingevent` behavior 2 (no subcomponents). `RibbonDiff.xml` is copied in at pack time from SpaarkeMaster, never checked in here. A local `pac solution pack` of the shell plus the diff succeeds.
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_BillingEvent/RibbonDiff.xml`: NOT changed in 164c. 096b (405cdaebb2) already reduced it to the five `HideCustomAction` rows, which are exactly the set the brief lists: `Mscrm.Form.sprk_billingevent.NewRecord`, `Mscrm.Form.sprk_billingevent.SaveAndNew`, `Mscrm.HomepageGrid.sprk_billingevent.NewRecord`, `Mscrm.SubGrid.sprk_billingevent.AddNewStandard`, `Mscrm.SubGrid.sprk_billingevent.NewRecord`. The SubGrid NewRecord is present in the live ribbon, so it stays.
- `sprk_secure_child_ribbon.js` and uac-r2's SecureChild ribbon were not touched (Q6 is out of scope).

### Dry run against dev (read-only), 2026-11-08

`pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1`, exit 2, nothing written (the rollback copy went to the local temp folder):

```
Deploy-EbillingBillingEventRibbon  env: https://spaarkedev1.crm.dynamics.com  mode: DRY RUN (no writes)
Rollback copy of dev's ribbon for sprk_billingevent: ...\BillingEventRibbons-rollback-<timestamp> (effective ribbon XML + stored diff, command and rule rows).
Hidden controls in the diff: 5; in the effective ribbon now: 5; already hidden by a stored row: 0
  Mscrm.Form.sprk_billingevent.NewRecord                                 present now (will be hidden)
  Mscrm.Form.sprk_billingevent.SaveAndNew                                present now (will be hidden)
  Mscrm.HomepageGrid.sprk_billingevent.NewRecord                         present now (will be hidden)
  Mscrm.SubGrid.sprk_billingevent.AddNewStandard                         present now (will be hidden)
  Mscrm.SubGrid.sprk_billingevent.NewRecord                              present now (will be hidden)
Stored definitions compared by content: 3 diff rows, 2 commands, 2 rules; differing from the checked-in diff: 4
REFUSED: dev's stored ribbon for sprk_billingevent differs from the checked-in diff, and the import would replace it: command Mscrm.AddNewRecordFromSubGridStandard (not in the checked-in diff); command sprk.SecureChild.sprk_billingevent.New.Command (not in the checked-in diff); rule sprk.SecureChild.sprk_billingevent.NativeNewAllowed.EnableRule (not in the checked-in diff); rule sprk.SecureChild.sprk_billingevent.NewAvailable.EnableRule (not in the checked-in diff). ... Nothing was changed.
```

P1 holds: the dry run lists the five hides and refuses (exit 2) while dev still carries uac 147's `sprk.SecureChild.sprk_billingevent.*` command and rules (and the `Mscrm.AddNewRecordFromSubGridStandard` override that belongs to the same ribbon), none of which the checked-in diff holds since 096b.

The consequence for the lead: `-Apply` is blocked by that same refusal until dev's SecureChild ribbon for `sprk_billingevent` is gone or the Q6 owner decides the import may replace it. Removing it is uac-r2's deploy (`scripts/Deploy-SecureChildNewCommands.ps1`, whose 096b change drops billing event from its table list); this script will not overwrite it. After that, the dry run goes on to pack the zip and run the import pre-flight (not reachable today, so that part was checked only by a local `pac solution pack`).

### -Verify

`pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -Verify`: exit 1 ("VERIFY FAIL: still in the effective ribbon" for all five controls), the expected state before any import. P2 holds (exit 0 or 1, read-only).

### -Apply (NOT run; waits for Q6 and the owner's go)

```
pwsh -File scripts/ebilling/Deploy-EbillingBillingEventRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
```

The script imports through `Invoke-ScopedSolutionImport` (`scripts/lib/Publish-SolutionComponents.ps1`, which wraps `pac solution import` and the scoped `PublishXml`), so there is no separate `Import-SolutionScoped.ps1` call. It then re-runs the `-Verify` check with a bounded retry (up to about 3 minutes); exit 0 means all five hides are in effect. Order: (1) Q6 resolved and the SecureChild ribbon for `sprk_billingevent` removed from dev, (2) a dry run that no longer refuses, (3) the owner's go, (4) `-Apply`, (5) `-Verify`.
