# AccessRibbons — the shared "Access" group (project, matter, work assignment main forms)

> **Owner**: unified-access-control-r2 task 142 (GitHub #1065) — owner round 3 R3 ("Update Access") + round 3b UX
> (disposition row 4: ONE "Access" group, ONE source file, ONE script). **Task 150** adds Make Secure / Remove Secure to
> THIS group, in THIS template and THIS script — never a second group or script.
> **Live state**: NOT deployed (manual gate — see Deployment). Nothing here was applied to any environment.

## What is here

| File | Role |
|---|---|
| `access-group.template.xml` | The ONE authored definition: a FlyoutAnchor "Access" (Sequence 905) on `Mscrm.Form.{{entity}}.MainTab.Actions.Controls._children`, holding "Update Access". Never hand-edit a per-entity copy. |
| `Merge-AccessRibbon.ps1` | The mechanical per-entity generator: instantiates the template for one entity and merges it into a FRESH export of that entity's `RibbonDiff.xml`. Pure file transformation — no Dataverse call. |

The command script is `src/client/webresources/js/sprk_access_ribbon.js` (web resource `sprk_/scripts/access_ribbon.js`,
namespace `Spaarke.Access.Ribbon`). It reuses `Spaarke.BffAuth` (`sprk_/scripts/bff_auth.js`) and the ONE sync call in
`Spaarke.AssignedAccess` (`sprk_/scripts/assignedaccess_postsave.js`, `src/solutions/webresources/sprk_assignedaccess_postsave.js`).

## Why a FlyoutAnchor (realisation choice)

A ribbon `Group` does not render as a visual container in the Unified Interface command bar, so "Access" is one
FlyoutAnchor whose menu holds the commands — the FORM precedent is the "Dark Mode" flyout
(`ThemeMenuRibbons/Other/Customizations.xml`, `sprk.ThemeMenu.Project.Form.CustomAction` on
`Mscrm.Form.sprk_project.MainTab.Actions.Controls._children`). `docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md` shows no
better-rendering precedent for a multi-command group. UCI hides a command whose enable rule is false, so:

- **Update Access** is enabled only when `GET /api/v1/external-access/can-manage-access` answers 200 with
  `canManageAccess = true` for this record (the delegation filter's own verdict: Write on the record). Anything else is
  "no". The answer is cached per record in `sessionStorage`; on a cold cache the rule returns a promise (UCI waits for
  it). It is a convenience only — the sync route is gated by `DelegationRuleFilter` whatever the ribbon shows.
- **The flyout** is hidden when none of its items is available (`isAccessMenuVisible`), never shown empty.

## Libraries on every command and rule

Ribbon commands do not load form libraries. Every `CommandDefinition` action list and every `EnableRule` lists, in
order: `bff_auth.js` → `assignedaccess_postsave.js` (each as `FunctionName="isNaN"`: loads the library with no side
effect; `isNaN()` is true, so an AND-ed CustomRule is unaffected) → `access_ribbon.js`.

## Mechanism — why a merge script, not a checked-in RibbonDiff per entity

The project and matter form commands are split across several checked-in sources (Push Updates in
`ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml` / `MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml`; the wizard
buttons in `ProjectRibbons/customizations.xml` / `MatterRibbons/customizations.xml`; Create To Do in
`ProjectRibbons/createtodo-button.xml`; Dark Mode in `ThemeMenuRibbons/`), and the work-assignment form ribbon is not in
source control at all (`WorkAssignmentRibbons/` holds only a merge snippet). An import replaces the entity's
RibbonDiffXml **for the importing solution**, so the merge must start from what is LIVE. `Merge-AccessRibbon.ps1`:

1. instantiates the template for `-Entity` (`{{entity}}` = the logical name);
2. removes every earlier `sprk.Access.*` node (idempotent — a second run yields the same file);
3. appends the template's CustomAction, CommandDefinitions, EnableRules and LocLabels;
4. prints the command ids before and after, and **throws** if any pre-existing command id is missing afterwards.

Verified locally (2026-10-03) against the checked-in Project and Matter `RibbonDiff.xml`: merged, idempotent on a
second run, no command lost. That proves the transformation only — the live merge needs fresh exports (below).

## Deployment (MANUAL GATES — main session; not run by task 142)

Order matters: the BFF route and the web resources must exist before a ribbon that calls them.

1. **BFF** carrying task 142 deployed (after `scripts/Set-AssignedAccessLedgerSchema.ps1 -Apply` / `-Verify`).
2. **Web resources** (dataverse-deploy skill), published:
   `sprk_/scripts/assignedaccess_postsave.js` ← `src/solutions/webresources/sprk_assignedaccess_postsave.js`;
   `sprk_/scripts/access_ribbon.js` ← `src/client/webresources/js/sprk_access_ribbon.js`.
3. **Before import — record the live command bars** on each main form (expected, confirm live):
   project — Push Updates, Upload Documents, Summarize Files, Find Similar, Playbook Library, Create To Do, Dark Mode;
   matter — Push Updates, Create Project, Create Event, Create To Do, Upload Documents, Summarize Files, Find Similar,
   Playbook Library; work assignment — Create To Do, Dark Mode.
4. **Export** a dedicated small ribbon solution (per `.claude/skills/ribbon-edit/SKILL.md` — never SpaarkeCore) holding
   the three entities (ribbon only), unpack it, and **check in the exported work-assignment `RibbonDiff.xml`** under
   `WorkAssignmentRibbons/Entities/sprk_workassignment/` before editing (amendment UX (f)).
5. **Merge**, one entity at a time:
   ```powershell
   pwsh ./Merge-AccessRibbon.ps1 -ExportedRibbonDiff <unpacked>/Entities/sprk_Project/RibbonDiff.xml `
       -Entity sprk_project -Out <unpacked>/Entities/sprk_Project/RibbonDiff.xml
   # repeat for sprk_matter and sprk_workassignment
   ```
   Keep each "Commands before / after" print — the AFTER list must be the BEFORE list plus `sprk.Access.*`.
6. **Pack, import, publish.** Then on each form: every command from step 3 still renders and runs; the "Access" flyout
   shows "Update Access" to a Write-holder (cold cache too — first open after a sign-in) and is hidden for a Read-only
   user, whose direct call to the sync route still gets 403.
7. **Form libraries** (task 142's post-save call, separate from the ribbon): on the three main forms, register
   `sprk_/scripts/bff_auth.js` FIRST, then `sprk_/scripts/assignedaccess_postsave.js`, with OnLoad handler
   `Spaarke.AssignedAccess.onLoad` (pass execution context).
