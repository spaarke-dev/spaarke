# AccessRibbons — the shared "Access" group (project, matter, work assignment main forms)

> **Owner**: unified-access-control-r2 task 142 (GitHub #1065) — owner round 3 R3 ("Update Access") + round 3b UX
> (disposition row 4: ONE "Access" group, ONE source file, ONE script). **Task 150** added Make Secure / Remove Secure
> to THIS group, in THIS template and THIS script — never a second group or script.
> **Live state**: NOT deployed (manual gate — see Deployment). Nothing here was applied to any environment. `-Verify`
> was run read-only against spaarkedev1 on 2026-10-04: FAIL on all three forms (no `sprk.Access.*` command) — the
> correct pre-import verdict.

## What is here

| File | Role |
|---|---|
| `access-group.template.xml` | The ONE authored definition: a FlyoutAnchor "Access" (Sequence 905) on `Mscrm.Form.{{entity}}.MainTab.Actions.Controls._children`, holding "Update Access" (10), "Make Secure" (20, task 150, release-gated) and "Remove Secure" (30, task 150). Never hand-edit a per-entity copy. |
| `Merge-AccessRibbon.ps1` | The mechanical per-entity generator: instantiates the template for one entity and merges it into a FRESH export of that entity's `RibbonDiff.xml`. Pure file transformation — no Dataverse call. `-SecureTransitionDeployed` keeps Make Secure; without it the generator removes it. |
| `Set-AccessRibbon.ps1` | Task 150: the three forms in one step — dry run (default; 142's checked-in exports, no Dataverse call), `-Apply` (live: records the live command lists, exports the dedicated ribbon solution, checks in the work-assignment export, merges, packs, imports, publishes, then verifies) and `-Verify` (read-only `RetrieveEntityRibbon`: the before-list is intact, the Access commands call `access_ribbon.js`, Make Secure present exactly when `-SecureTransitionDeployed`). |

### Make Secure is release-gated (task 150 acceptance (b); owner R3b / F7)

Make Secure secures an EXISTING record, so its existing children — and, with round 26 item 3, its files — must follow
it: the confirmation the user accepts (owner round 27) says both happen. Children: task 148's provisioning transition.
Files: round 26 item 3's ONE relocation service, `DocumentContainerRelocator` — built by task 166, and wired into
provisioning's Make Secure path by the main session when 166 merges (an integration step; task 150 builds no second
relocator). The scheduled backstop for a relocation left pending (`files_incomplete`) is task 147's
`SecureChildReconciliationJob` (every 2 minutes), which also settles the PENDING Make Secure relocations recorded in the
relocation ledger through that ONE relocator, capped per run and reported (round 46 item 2; wired at integration once
147, 150 and 166 are all on the integration branch). **The rule:** Make Secure is imported only into an environment whose
BFF carries ALL THREE — task 148's transition, the wired relocation, and that backstop (`SecureChildReconciliationJob`
registered with its writes on) — in the same release: run the generator (and `Set-AccessRibbon.ps1`) with
`-SecureTransitionDeployed` only there. Elsewhere the Access group ships Update Access and Remove Secure, and `-Verify`
fails if Make Secure is present. Tasks 148 and 150 are integrated together (`integ/uac-r2-batch4`); the relocation
arrives with 166's merge and the backstop with 147's, so pass `-SecureTransitionDeployed` only for a BFF built after
that wiring. It is a packaging rule by design — the ribbon has no reliable runtime signal of the BFF's build, and a
missing command is the safe default.

**Who may use them.** Both commands are enabled only for a caller with Write (the cached can-manage-access verdict — the
same rule as Update Access). Make Secure needs the record NOT secure — or flagged secure with a transition that did not
finish (round 40 item 1, round 46 item 4: no container recorded, or owned by a user, or owned by a team OTHER than the
Secure Record Owners team; a PROVISIONED record is owned by the Secure Record Owners team and records its own container,
and hides it) — and Remove Secure needs it secure; `sprk_issecure`, `sprk_containerid` and `_owninguser_value` are read
in ONE `Xrm.WebApi.retrieveRecord`, and a failed or masked (empty) flag hides both. For a flagged, team-owned record with
a container the script asks the server which team that is (`can-manage-access?…&includeOwner=true`: the owning team and
whether it is the Secure Record Owners team, the server's configuration); an answer it cannot get keeps Make Secure
hidden on that record. A Make Secure failure after the flag write that the server answers as "the same caller may call
again" (`MAKE_SECURE_RETRY_IN_PLACE`) offers that call in place: a confirm dialog with the server's message, Make
Secure / Cancel (round 40 item 1). Who may REMOVE the designation is
the server's decision (owner F3: Full Access holders and the record's creator); a refusal shows the endpoint's
ProblemDetails message. Make Secure confirms first with the owner-authored copy (owner round 27, the
`MAKE_SECURE_CONFIRMATION` constant in the script) and sends `transition: "make-secure"` (exactly; it names no
colleagues), which the server holds to the Write gate (round 33 item 1; the creator rule belongs to the wizards'
create-then-secure path) while sharing the record to its creator too. If the server could not share it to someone it
names in `skippedPrincipals` — the creator on the record's No Access list, that list unverifiable, or the share itself
failed — the script shows a per-person warning after the success notification (`SKIPPED_PRINCIPAL_COPY`; an unknown
reason gets the generic warning and is logged — never silent, round 33 item 5; a person whose name cannot be read is
"Someone", round 40 item 3). Remove Secure confirms first too (round
33 item 2, `REMOVE_SECURE_CONFIRMATION`). Cancel calls nothing.

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

1. **BFF** carrying task 142 deployed (after `scripts/Set-AssignedAccessLedgerSchema.ps1 -Apply` / `-Verify`) — and,
   for Make Secure, tasks 148 + 150, the Make Secure file relocation (task 166's `DocumentContainerRelocator`, wired
   at integration) and its scheduled backstop (task 147's `SecureChildReconciliationJob` settling pending Make Secure
   relocations, registered with its writes on — round 46 item 2) in the same release.
2. **Web resources** (dataverse-deploy skill), published:
   `sprk_/scripts/assignedaccess_postsave.js` ← `src/solutions/webresources/sprk_assignedaccess_postsave.js`;
   `sprk_/scripts/access_ribbon.js` ← `src/client/webresources/js/sprk_access_ribbon.js`.
3. **Before import — record the live command bars** on each main form (expected, confirm live):
   project — Push Updates, Upload Documents, Summarize Files, Find Similar, Playbook Library, Create To Do, Dark Mode;
   matter — Push Updates, Create Project, Create Event, Create To Do, Upload Documents, Summarize Files, Find Similar,
   Playbook Library; work assignment — Create To Do, Dark Mode.
4–6. **Since task 150 these three steps are ONE script** (`Set-AccessRibbon.ps1`; the BFF must carry tasks 148 + 150,
   the wired relocation and its backstop for `-SecureTransitionDeployed`, and the web resources must be
   `access_ribbon.js` 1.4.0 — task 150 round 40: Make Secure offered on an unfinished secure transition, and its
   in-place retry; round 46 item 4: a flagged record owned by a team other than the Secure Record Owners team is
   unfinished too, which needs the BFF's `can-manage-access` `includeOwner` answer):
   ```powershell
   pwsh ./Set-AccessRibbon.ps1 -SecureTransitionDeployed                                    # dry run (no Dataverse call)
   pwsh ./Set-AccessRibbon.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -SolutionName <ribbon solution> `
       -SecureTransitionDeployed -Apply                                                  # LIVE: export → merge → import → verify
   pwsh ./Set-AccessRibbon.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com -SecureTransitionDeployed `
       -Verify -BeforeList <WorkDir>/before.json                                         # read-only, exit 0 = PASS
   ```
   `-Apply` records each form's live command list (`before.json`), exports a dedicated small ribbon solution (per
   `.claude/skills/ribbon-edit/SKILL.md` — never SpaarkeCore) holding the three entities, **checks in the exported
   work-assignment `RibbonDiff.xml`** under `WorkAssignmentRibbons/Entities/sprk_workassignment/` before editing it
   (amendment UX (f) — commit it), merges each entity with `Merge-AccessRibbon.ps1` (its "Commands before / after" check:
   the AFTER list is the BEFORE list plus `sprk.Access.*`), packs, imports with publish, and runs `-Verify` against
   `before.json`. Omit `-SecureTransitionDeployed` in an environment whose BFF does not carry task 148's transition, the
   wired file relocation AND its `SecureChildReconciliationJob` backstop with writes on.
   Then on each form (the task 150 POML ui-tests): every command from step 3 still renders and runs; the "Access" flyout
   shows "Update Access" to a Write-holder (cold cache too — first open after a sign-in) and is hidden for a Read-only
   user, whose direct call to the sync route still gets 403; Make Secure / Remove Secure follow the secure state (Make
   Secure also on a record flagged secure whose transition did not finish — no container recorded, user-owned, or
   owned by a team other than the Secure Record Owners team; hidden on a PROVISIONED one: acceptance (e) as amended by
   round 40, and round 46 item 4).
7. **Form libraries** (task 142's post-save call, separate from the ribbon): on the three main forms, register
   `sprk_/scripts/bff_auth.js` FIRST, then `sprk_/scripts/assignedaccess_postsave.js`, with OnLoad handler
   `Spaarke.AssignedAccess.onLoad` (pass execution context).
