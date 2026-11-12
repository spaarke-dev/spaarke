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

### Fix round (review of 8e8ea5e79d), 2026-11-08

#### Q6 decided: option (b), by uac-r2

The import may replace exactly four live definitions on `sprk_billingevent` that the checked-in diff does not carry: `sprk.SecureChild.sprk_billingevent.New.Command`, `sprk.SecureChild.sprk_billingevent.NativeNewAllowed.EnableRule`, `sprk.SecureChild.sprk_billingevent.NewAvailable.EnableRule` and `Mscrm.AddNewRecordFromSubGridStandard` (the SecureChild override of the platform command for this table). They are the named list `$ReplacedByQ6` in the script (exact, case-sensitive match; no prefix, no wildcard), commented "replaced by this import per uac-r2 Q6 (b), 2026-11-08; billingevent's SecureChild New was removed in 096b/164a". The drift, `$lost` and `$unknown` refusals skip only those four; every other live id the diff does not carry still refuses (exit 2). The dry run prints the four as "will be replaced (uac-r2 Q6)".

Two `-Apply` conditions (the operator checks both before running it):

1. Our source removal (096b and this branch's `RibbonDiff.xml`) is on master no later than the dev `-Apply`.
2. After `-Apply`, the read-backs go to uac-r2: our `-Verify` with all 11 hides, and SecureChild `-Verify` passing for the remaining eight tables.

Dry run on dev after the change (exit 2, read-only): the four print as "will be replaced"; the content drift check is 0; the run then refuses on three OTHER stored SecureChild diff rows that the allow-list does not cover: `sprk.SecureChild.sprk_billingevent.New.LabelText`, `sprk.SecureChild.sprk_billingevent.New.ToolTipDescription` and `sprk.SecureChild.sprk_billingevent.SubGrid.New.CustomAction`. Those are the label, tooltip and custom action of the same SecureChild New button. They were NOT added, because the decision names four ids. Open question for uac-r2: either add these three to the allow-list or remove them from dev first. Until then `-Apply` stays refused and the pack and pre-flight steps are not reached live (the pack was checked locally).

#### Refusal message (F4)

When the refused set holds any `sprk.SecureChild.*` id or `Mscrm.AddNewRecordFromSubGridStandard`, the drift and `$lost` messages no longer say "Merge dev's version into the diff". They say these are SecureChild definitions owned by unified-access-control-r2 and must not be merged into the billing-event diff.

#### Positive control (K2)

`-Verify` and the post-apply check also require the sibling control `Mscrm.Form.sprk_billingevent.Save` to be in the effective ribbon before reporting PASS. An empty or wrong payload fails (exit 1). The dry run only warns.

#### Other creating controls (K1)

Six more hides, with the ids exactly as they appear in the effective ribbon: `Mscrm.HomepageGrid.sprk_billingevent.ImportData`, `.ImportDataFromExcel`, `.ImportDataFromCSV`, `Mscrm.HomepageGrid.sprk_billingevent.Copy.Selected`, `Mscrm.Form.sprk_billingevent.CopySelected`, `Mscrm.SubGrid.sprk_billingevent.CopySelected`. They are in `RibbonDiff.xml` (11 `HideCustomAction` rows now), the script's hide list (read from the diff) and `-Verify`. Rationale: a new line has one owner, Add line (164a).

UI hides are not a privilege boundary: a user can still create a row through the API, an import wizard route we have not hidden (`ImportDataFromXML` and the platform Import) or another client. The server refusal (164a-1) is the boundary. Open question for the owner's D3 role-change bundle: whether user Create on `sprk_billingevent` is removed the way D3 removes it for `sprk_invoice`. No role was changed here.

#### Deviations from the reference script and P1

Two deviations from `Deploy-EbillingBillingExportRibbon.ps1`: (1) the hide list is printed BEFORE the stored-ribbon refusal, so a refused dry run still shows the hides; (2) `$SourceDiff` is hard-coded to the mixed-case folder `sprk_BillingEvent` (the checked-in source folder), while the shell and the table logical name are lower case.

P1 is met in the inverse direction: the script refuses when dev carries rows the diff does not (outside the four-id allow-list), because the import would delete them.

### Q6 allow-list widened (lead, 2026-11-08)

uac-r2 approved adding the three further rows of the same SecureChild New button, and confirmed against master's `sprk_BillingEvent/RibbonDiff.xml` that the SecureChild footprint on this table is exactly: the CustomAction `sprk.SecureChild.sprk_billingevent.SubGrid.New.CustomAction`, the CommandDefinitions `sprk.SecureChild.sprk_billingevent.New.Command` and `Mscrm.AddNewRecordFromSubGridStandard`, the EnableRules `...NativeNewAllowed.EnableRule` and `...NewAvailable.EnableRule`, the LocLabels `...New.LabelText` and `...New.ToolTipDescription`, plus the Button `sprk.SecureChild.sprk_billingevent.SubGrid.New` if dev stores it as its own row. `$ReplacedByQ6` now holds exactly those eight ids (exact, case-sensitive; no prefix, no wildcard); any other id still refuses. The "four ids" wording above is superseded.

`-Verify` now also requires every hide in the diff (11) to be a stored `ribbondiffs` row on the table (uac-r2 condition 2), besides the effective-ribbon absence and the sibling `Save` control.
