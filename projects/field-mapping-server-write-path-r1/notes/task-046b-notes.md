# Task 046b notes - role rulings applied, apply script, writer-only rule, standalone add-ons

Branch `task/fm-046b` from `work/field-mapping-server-write-path-r1` at `0e098c3be6` (contains 046's merge `0b0edfbe35` and this POML).
Rigor FULL (POML). Owner rulings: `notes/decisions.md`, "Role rulings on task 046's gaps" (2026-11-05).

No Dataverse write was made. Every call against dev was a GET (the script's dry run and `-Verify`, with the operator's az token held in memory).

## Outputs

| File | What |
|---|---|
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Core User.xml` | G1: Create, Append, AppendTo on `sprk_reportcard` at Deep |
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Office Add In User.xml` | G2: AppendTo `sprk_eventtype_ref` Deep → Global; G4/G7: AppendTo `contact` Basic → Deep; G6: Write `sprk_document` at Deep; G7: AppendTo `account` Basic → Deep; G8: AppendTo `sprk_budget` at Deep |
| `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml` | G5: Read `sprk_procedure` at Deep. The hunk is byte-identical to ebilling-r1 089e's (`origin/task/ebilling-089e`, `c564ab0709`), so whichever merges second merges it cleanly and keeps one line |
| `config/feature-role-privileges.json` | The declared set: the nine ruled privilege changes (role, table, privilege, depth, the earlier level, gap, feature) |
| `scripts/Set-FeatureRolePrivileges.ps1` | Applies the declared set: dry run by default, `-Apply`, `-Verify` |
| `tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs` | Baseline cleaned; rows for G6's new role set and G1's KPI assessment; G10's row removed; `RoleClasses` and the add-on-alone rows (Part D); `MustNotHold` (Part C); the ruled-change checks; controls |
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.1 | Role model (standalone add-ons), new rows (KPI assessment, invoice line create, playbook administration), "alone" role sets, To Do and the Do lane Core-only, gap tables (open, applied, G10 withdrawn), standalone add-on gap summary, dev paragraph |

| `tests/Spaarke.ArchTests/CoreUserWizardLinkingGuardTests.cs` | Its Core User pin on `sprk_reportcard` (a table it owns) now holds G1's Create, Append, AppendTo at Deep |
| `tests/Spaarke.ArchTests/RuleSetRolePrivilegeGuardTests.cs`, `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1` | G5: ebilling-r1 089e's own hunks (`c564ab0709`), byte-identical: the Billing Administrator's exact set and wanted set include Read on `sprk_procedure` at Deep |
| `tests/Spaarke.ArchTests/ProcedureRolePrivilegeGuardTests.cs` | G5: 089e's one allowed grant before uac-r2 191, with its controls; 089e's stored-file item and its one stored-file assertion are left out (that rule is not on this branch), so this file's hunk differs from 089e's and the second merge resolves it |

Nothing for G3 or G9 and nothing from Part D went into a role file.

## Part A - the rulings

- G1, G2, G4, G5, G6, G7, G8: in the role files exactly as ruled (table above). Their `KnownGaps` entries are removed in the same commit.
- G1's AppendTo: the ruling says it files a KPI assessment under a report card. New feature rows "KPI assessment (filed under a report card)" [Core]: Create and Append on `sprk_kpiassessment` (Basic), AppendTo on `sprk_reportcard` (Deep); evidence `ChildRecordEndpoints.cs:70-85` (`sprk_kpiassessment` is a create table, the secure-record ribbon's "New KPI Assessment") and `SecureChildLineage.cs:302-303` (a KPI assessment is filed under a matter, project or report card). Without the row, removing the AppendTo would fail nothing.
- G6: the Write went on Office Add In User, so the version-save row's set is now Basic User + Office Add In User (it was Basic User alone).
- G3: accepted, not applied. The baseline entry stays, its reason updated.
- G9: stayed in the baseline in the first pass; dropped in fix round 2 (below): Basic User with AI Analysis User is not a set a document-writing run is meant for. (Fix round 3 corrects the wording: the run is not limited to one role; it asks Write on `sprk_document` as the caller, so any role set holding it can run one.)
- G10: removed from the baseline, the gap table and the Decision commit Write row (seven subjects remain). Recorded in the doc and the test comment: no role change; invoice-line Signals are decided in the invoice review; the Console route will refuse them (#2572, ontology DI-139).
- Office To Do and the Console Do lane: Core-only (doc rows; test comments). Console User alone is checked against the Decide lane only (below).
- Create on `sprk_billingevent`: "none, by design (BFF-written, ebilling D-18)" in the doc (new row) and a `MustNotHold` rule (no person role holds it). The doc notes that ebilling hides the model-driven "New Billing Event" button in its own ribbon deploy.

## Part C - the writer-only rule

`MustNotHold` holds two rules over every person role (every role file but the app users' role; an unclassified file counts as a person role): no Create, Write or Delete on `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision`, except Spaarke Ontology Administrator (the designated cleanup role, ontology D-160); and no Create on `sprk_billingevent`, no exception. Any depth counts; names match case-insensitively. The repo's role files pass.

`SpaarkeRoleLedgerPrivilegeGuardTests` (ontology 049) stays as it is: it pins a stricter subset (only the writer creates a Decision Record, no role writes or deletes one, Console User and Ontology Administrator never write a Signal). The two agree today; the new rule covers every person role and all three tables.

## Part D - standalone add-ons

`RoleClasses` classifies all 13 role files (2 base, 1 app users' role, 10 add-ons). A file it does not list fails the guard. Each add-on either names the declared features it exists for, whose rows the guard repeats with the add-on as the only role (feature name "… [role alone]"), or says why no feature in the matrix is its own:

| Add-on | Checked alone against |
|---|---|
| Office Add In User | Ruled not standalone in fix round 2 (below): not checked alone. Its features, still checked with a base role: Office save, Office version save, Office quick create, Event (typed route) |
| AI Analysis User | Ruled not standalone in fix round 2 (below): not checked alone. Its features, still checked with Basic User: Analysis from a playbook, Analysis run (not the writing run) |
| Console User | Ruled in fix round 2 (below) to need Core User: not checked alone. Its features are checked with Core User: Decision commit and Decision actions (the Do lane stays Core-only) |
| Billing Administrator | Billing: Procedure override (its row was already alone) |
| AI Analysis Admin | Playbook administration (rows: Create and Share on `sprk_analysisplaybook`, asked of the caller, `PlaybookEndpoints.cs:27,107,120,176`, `PlaybookAuthorizationFilter.cs:55-75,166,172`; Write for the edit routes and Read for a clone's source, added in fix round 2) |
| Ontology Administrator | none in this matrix: its routes write as the user, so Dataverse asks each right itself (`OntologyAdminCallerBoundaryGuardTests`) |
| Access Administrator | none in this matrix: membership writes, pinned by `ContactOrganizationMembershipWriterGuardTests` |
| Reporting Access Admin, Author, Viewer | none in this matrix: the reporting routes admit a caller by the role itself (`ReportingAuthorizationFilter.cs`) |

### The gap list (withdrawn in fix round 2)

The first pass listed 39 gaps (D1-D39) for Office Add In User, AI Analysis User and Console User checked alone, for the owner's one decision. The owner ruled (2026-11-05) that none of the three is standalone, so no gap is open for any of them alone and the list, its `KnownGaps` entries and doc §5.1's gap summary are gone. `KnownGaps` now holds one entry, G3 (accepted, not applied).

## Part B - the apply script

`scripts/Set-FeatureRolePrivileges.ps1` follows `Set-WizardLinkingAndMembershipPrivileges.ps1`'s shape (modes, root-role resolution, privileges from table metadata, AddPrivilegesRole on the root role, read-back). Differences, each from the POML: it reads back **every business-unit copy** (not only the root), reports a copy whose depth differs from its root (fails `-Verify`), and every read happens inside one fail-closed block before any write (exit 1, "Nothing was changed"). Before it connects it checks the config against the role XML and stops if they differ. It only adds or raises; a live depth above the declared one is reported and fails `-Verify`, never lowered.

### Dry run against dev (2026-11-05, GETs only)

```
Roles:
  Spaarke Billing Administrator        root role in the root BU; every business-unit copy takes its privileges
  Spaarke Core User                    root role in the root BU; every business-unit copy takes its privileges
  Spaarke Office Add In User           root role in the root BU; every business-unit copy takes its privileges
Changes     : 8
  Spaarke Core User                    prvAppendsprk_ReportCard           None   -> Deep   (G1; every business-unit copy)
  Spaarke Core User                    prvAppendTosprk_ReportCard         None   -> Deep   (G1; every business-unit copy)
  Spaarke Core User                    prvCreatesprk_ReportCard           None   -> Deep   (G1; every business-unit copy)
  Spaarke Office Add In User           prvAppendToAccount                 Basic  -> Deep   (G7; every business-unit copy)
  Spaarke Office Add In User           prvAppendToContact                 Basic  -> Deep   (G4, G7; every business-unit copy)
  Spaarke Office Add In User           prvAppendTosprk_Budget             None   -> Deep   (G8; every business-unit copy)
  Spaarke Office Add In User           prvAppendTosprk_EventType_Ref      Deep   -> Global (G2; every business-unit copy)
  Spaarke Office Add In User           prvWritesprk_Document              None   -> Deep   (G6; every business-unit copy)
Live depth above the declared one (never lowered here): none
Business-unit copies that differ from their root role on a declared privilege: none
DRY RUN: no writes. Re-run with -Apply to make the 8 change(s) above.   (exit 0)
```

G5 is not pending: dev already holds it. Every privilege name resolved from table metadata.

- `-Verify` against dev: `VERIFY FAIL: 8 change(s) pending`, **exit 1**.
- Fail closed: a scratch config naming a table that does not exist, run with `-Apply -WhatIf`: `FAIL: a read failed: … 404 (Not Found). Nothing was changed.`, exit 1. An unreachable environment URL: `FAIL: a read failed: No such host is known`, exit 1. A scratch config whose depth differs from the role XML: stops before connecting, exit 1.
- `-Apply` was not run (UAC-r2's, in its session, under the owner's go).

## Component justification (CLAUDE.md §11)

- **`scripts/Set-FeatureRolePrivileges.ps1`.** Existing: `Set-WizardLinkingAndMembershipPrivileges.ps1` (issue #2382's two sets: Core User's wizard AppendTo and contact-organization membership), `Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1` (removals). Extension: the wizard-linking script's logic is specific to its two sets (AppendTo capped at the live Read, Append raised to the Create depth, a membership writer allowlist and removals); folding nine unrelated ruled grants on three roles into it would mix a third rule into a script UAC-r2 has already reviewed and run, and it reads back only the root role. This script copies its shape and adds the business-unit read-back the POML asks for. Cost of doing nothing: the role files would hold the rulings while no reviewed, repeatable path brings an environment to them; `-Verify` is the only check that dev (and each customer environment) matches.
- **`config/feature-role-privileges.json`.** Existing: `core-user-wizard-linking-role.json`, `contact-organization-membership-role.json` (other purposes, other shapes). Extension: neither describes "role R holds privilege P at depth D because the owner ruled gap G"; adding these to the wizard-linking file would make that guard and script apply them under the wrong rule. Cost of doing nothing: the script would carry the list in code, and nothing would tie it to the role files (the ArchTest `RuledChangesAreInTheRoleFiles` does).
- **The guard additions** (in the existing `FeatureRolePrivilegeGuardTests`, not a new file): `RoleClasses` and the alone rows extend the existing `Requirements`/`Gaps`/`Ratchet` path; `MustNotHold` reuses its role loader. `SpaarkeRoleLedgerPrivilegeGuardTests` is the nearest existing guard: it pins a stricter, narrower set (Decision Record and Signal Write) with no exception mechanism and no billing rule; extending it would mean adding the person/app-user classification there as well, so the rule lives beside the classification it needs. Cost of doing nothing: a person role could gain Create on a Budget Revision or a billing line and nothing would fail.

## Failing-first evidence

1. **Part A.** The pre-task guard (046's test file, unchanged) on the new role files: `FeatureRequirementsAreHeldByTheirRoles` failed with eight stale entries (G1 x2, G2, G4, G5, G7 x2, G8: "remove it from KnownGaps"); G6 still reproduced because its row was Basic User alone, which is why the row's set changed.
2. **Seeded drift, old guard vs new guard** (real files mutated in place from scratch copies, restored after): S1 Basic User + `prvCreatesprk_BillingEvent`; S2 Core User + `prvDeletesprk_BudgetRevision`; S3 a new role file "Spaarke Seeded Add On"; S4 AI Analysis User minus `prvReadsprk_analysisplaybook`. Pre-task guard on the pre-task role files with S1-S4: **10 / 10 passed** (none seen). New guard on the new role files with S1-S4: **7 failed**, naming each seed: `PersonRolesHoldNothingTheyMustNot` (S1, S2), `EveryRoleFileIsClassified…` (S3), `FeatureRequirementsAreHeldByTheirRoles` (S4: the AI Analysis User alone rows for the playbook Read; the Basic User + AI Analysis User rows still passed, because Basic User holds it), and the controls that read the real files.
3. **AC1, whole files.** Each pre-task role file put back in turn (Core User, Office Add In User, Billing Administrator): the new guard failed each time (`FeatureRequirementsAreHeldByTheirRoles`, `RuledChangesAreInTheRoleFiles` naming every changed privilege, and `PuttingARuledChangeBackFailsTheGuard`); 16 others passed.
4. **AC1, per change, kept as a test.** `PuttingARuledChangeBackFailsTheGuard` copies the role files, confirms the copy passes, then puts each of the nine changes back to its earlier level one at a time and asserts the guard reports it.
5. `git status` after every seeded run showed only the task's files.

## Guard controls (new)

| Check | Must fire | Must not fire | Seeded drift |
|---|---|---|---|
| Classification | unclassified file, class with no file, add-on with neither alone rows nor reason, a stale feature, a stale exception | the real set; an add-on with a reason; a live exception | S3 |
| Must not hold | forbidden privilege at any depth or casing (two roles) | excepted role; Read on the table | every person role x every forbidden privilege on copies of the real roles (12 person roles x 10 privileges = 120 cases); through the files: Console User and Basic User copies fire, Ontology Administrator and the app users' role do not |
| Ruled changes vs files | deeper, shallower, missing, `from` not below `depth`, duplicate | exact hold in other casing | the whole-file restores (3) |
| Add-on alone | the existing ratchet (unlisted, changed, stale) | the existing controls | S4 |

## Known limits

- K4: Console User alone is checked against the Decide lane by subject type (event, To Do and work assignment subjects excluded as the Do lane's). A Decide-lane Signal on one of those subjects would ask Write the alone rows do not hold; the commit route checks it live.
- K4: Ontology Administrator, Access Administrator and the Reporting Access roles are classified "no feature in this matrix" with the guard that covers each; their own features are not rows here.
- The script reads back each business-unit copy once after the write; Dataverse propagates root-role changes to copies itself, so a slow propagation shows as a read-back failure (exit 1), not a silent pass.

## Verification

| Check | Result |
|---|---|
| `dotnet build tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --no-incremental` | 0 Warning(s), 0 Error(s) |
| `FeatureRolePrivilegeGuardTests` | 19 / 19 passed (10 before the task) |
| Whole `Spaarke.ArchTests`, first run after commit 1 | 1953 passed, **5 failed**: three other role guards pin the privileges the rulings changed (`CoreUserWizardLinkingGuardTests` x3 on `sprk_reportcard`, `RuleSetRolePrivilegeGuardTests` and `ProcedureRolePrivilegeGuardTests` on G5). Fixed in commit 3 (Outputs above). |
| Whole `Spaarke.ArchTests`, after the fix | **1959 / 1959 passed** |
| `Sprk.Bff.Api.Tests` | not run (no `src/server` change) |
| PowerShell parse of both scripts | 0 errors. PSScriptAnalyzer reports only `PSAvoidUsingWriteHost`, which is the reference script's idiom |
| Quality gates | Self-review against code-review Steps 3, 5, 6, 6.6 and ADR-038: KEEP path only (`tests/Spaarke.ArchTests/**`), no test deleted, every new check has must-fire, must-not-fire and seeded-drift proof, ratchet entries carry their held depth, no `src/server` file, no secret, no token written to disk, no Dataverse write. ADR-002: no plugin. The main session runs the adversarial pass. |

## Fix round 1 (role rulings, apply script, role guards)

Branch `task/fm-046b`. No Dataverse write was made and `-Apply` was not run; the script was run in its default dry-run mode with no token, and its apply path was exercised against stubbed calls only (below).

| Item | Outcome |
|---|---|
| F1 settle wait on `-Apply`'s read-back | Done. `scripts/Set-FeatureRolePrivileges.ps1` has `Wait-Settled` (the shape of `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1`). For the root role and each business-unit copy it re-reads until every declared privilege reads back at its declared depth, up to 8 tries, 4 seconds apart, before recording a failure. A failure after the retries still throws (exit non-zero) and names the role, the copy and the privilege. |
| F3 AppUsers classification pinned | Done. `ExpectedAppUsersRoles` is `{"Spaarke Ontology Service"}`; `AppUsersProblems` reports any other role with the kind and the expected role without it. Test `TheAppUsersSetIsPinned`: must-not-fire on the real set; must-fire on Console User marked AppUsers; must-fire on the expected role marked otherwise. |
| K1 `MustNotHold` scanned one folder | Done. `PersonRoles` now takes the solutions root and uses `ProcedureRolePrivilegeGuardTests.RoleFiles` (every `Roles` folder under `src/dataverse/solutions`), skips the AppUsers role, and fails on a role named by two files. The seeded-file test adds a role file under another solution's `Roles` folder (must fire) and one outside any `Roles` folder (must not). Results on the repo's role files are unchanged. |
| K2 partial apply gave no summary | Done. Each AddPrivilegesRole POST is in try/catch. On failure the script prints the failing role, the changes applied in this run, the changes not applied, and "Re-run -Apply to converge (the script only adds or raises)", then rethrows (exit non-zero). The error is not swallowed. |
| UAC-r2 note (a) | Done. In the plan, a privilege whose live depth differs from the config's `from` is printed (role, privilege, config `from`, live depth) under its own heading. Informational: the plan still uses the live depth, and `-Verify` does not fail on it. |
| K4 Office version-save Read | The version save's own row read is app-only (`OfficeService.cs:714` calls `OfficeDocumentPersistence.ResolveVersionTargetAsync`, `OfficeDocumentPersistence.cs:633-646`, which uses the singleton Dataverse service client without impersonation, `DataverseServiceClientImpl.cs:379`). But the "Save as version" offer on a name collision reads the colliding document's rights as the caller and offers the version retry only to a caller who holds Read on it (`OfficeEndpoints.cs:547-605`, `GetCallerAccessAsync`). So rows were added: "Office version save" (Core User) and "Office version save (Basic User + Office Add In User)", Read on `sprk_document` at Deep. Office Add In User alone holds none: gap D39, in `KnownGaps` with held depth none, and in the doc (gap summary 39; the Office save row). No role file changed. |

### Proofs

- **K1, failing first.** `PersonRoles` put back to the top `SpaarkeMaster/Roles` folder only: `MustNotHoldFiresOnSeededRoleFiles` failed (19 passed). With the fix: 20 / 20.
- **F3.** The pin test did not exist, so the proof is a seed: Console User marked AppUsers in `RoleClasses` fails `TheAppUsersSetIsPinned` (and six other checks); the real set passes.
- **K4 row.** With the Read rows and no `KnownGaps` entry: `FeatureRequirementsAreHeldByTheirRoles` failed naming the gap. With the Read added to the Office role file and the entry kept: it failed naming the stale entry.
- **Script F1/K2.** The apply section was run against stubbed `Get-HeldMap` and `Invoke-RestMethod` (no network, no token): a copy that settles on the 4th read passes with the new script and fails with the previous one; a copy that never settles exits 1 naming the role, the copy and the privilege; a failing POST on the second role prints the applied and not-applied lists and exits 1 (the previous script printed only the raw error).
- **Fail closed.** A dry run with an empty `AZURE_CONFIG_DIR`: `FAIL: a read failed: No Dataverse token ... Nothing was changed.`, exit 1.
- **Lint.** `[System.Management.Automation.Language.Parser]::ParseFile` on the script: 0 errors.

### Verification (fix round 1)

`dotnet build tests/Spaarke.ArchTests --no-incremental`: 0 Warning(s), 0 Error(s). `FeatureRolePrivilegeGuardTests`: 20 / 20 (19 before). Whole `Spaarke.ArchTests`: 1960 / 1960 passed (1959 before; the one added test is `TheAppUsersSetIsPinned`). `Sprk.Bff.Api.Tests` not run (no `src/server` change).

## Fix round 2 (playbook administration rows, role-file duplicates, apply script hardening)

Branch `task/fm-046b`. No Dataverse write was made and `-Apply` was not run by this task. The script's apply path is shown against a test double only (no network, no token).

| Item | Outcome |
|---|---|
| 1. F3: AI Analysis Admin alone is not fully checked | Done. The "Playbook administration" feature now has two more rows, both checked with the add-on as the only role: Write on `sprk_analysisplaybook` at Basic (the update, canvas-save and node create / update / delete / scopes routes ask the caller's Write first, then ownership of the row, which is user-owned, so Basic is enough: `PlaybookEndpoints.cs:41,155`, `NodeEndpoints.cs:34,59,73,89`, `PlaybookAuthorizationFilter.cs:166-172`), and Read at Global for a clone's source (`PlaybookEndpoints.cs:177` with `AddPlaybookAccessAuthorizationFilter`, mode `OwnerOrSharedOrPublic`: a public playbook needs no Read, any other is the caller's own Dataverse Read on a user-owned row, which for another owner's playbook is the role depth; Global matches the other rows that read a playbook). The role files meet both, so no `KnownGaps` entry, no gap-table row and no gap-summary change. The doc's §5.1 "Playbook administration" row lists the added asks. |
| 2. F3: the AppUsers file-name skip hid duplicates | Done. `PersonRoles` now runs `DuplicateRoleFiles` over every role file under every `Roles` folder before it leaves the app users' role out; a second file with any role's name (any casing) throws. |
| 3. K1: a role outside the root business unit | Done. The script throws, inside the fail-closed read block and before any write, when a resolved root role's business unit is not the root business unit (`Root role '<name>' is not in the root business unit. Nothing was changed.`, copied from `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1`). |
| 4. K2: no depth pre-check | Done. After the table-metadata reads and before any write the script reads `privileges(<id>)?$select=name,canbebasic,canbelocal,canbedeep,canbeglobal` for each declared privilege (once per privilege) and throws, naming the privilege, role and depth, when the declared depth is not allowed. `canbelocal` was added to the three the item names so a declared Local is checked too. |
| 5. K2: `-Apply` exited 0 on drift | Done. With nothing pending, `-Apply` (not a dry run) exits 1 when a live depth is above the declared one or a business-unit copy differs, printing the same reasons `-Verify` prints. A dry run still exits 0; `-Apply` with nothing pending and no drift still exits 0. |
| 6. K2: a failed read-back printed no summary | Done. The writes and the read-back share one try; the catch prints the applied and not-applied lists for either phase (and, for the read-back, that every write was sent and `-Verify` shows what the live roles hold), then rethrows (exit non-zero). |
| 7. K1: the role-name filter was not encoded | Done. The `$filter` value is `[Uri]::EscapeDataString` of the name with its quotes doubled. |
| 8. K3: `MustNotHold` names could be misspelt | Done. `MustNotHoldNamesRealTables`: each privilege must be `prv` + a known verb + a table that is an entity folder under some `src/dataverse/solutions/*/Entities` folder (case-insensitive). |
| 9. Notes hygiene | Done. The dry-run output above says "every business-unit copy", not a number. |

Not touched: `ProcedureRolePrivilegeGuardTests.cs` and `scripts/ebilling/*` (copies of ebilling's files, replaced at merge time).

### Proofs

- **Item 1, seeded.** Deleting `prvWritesprk_analysisplaybook` from `Spaarke AI Analysis Admin.xml` fails `FeatureRequirementsAreHeldByTheirRoles` ("Playbook administration (update, canvas, nodes) ... held: none") and `PuttingARuledChangeBackFailsTheGuard`; deleting `prvReadsprk_analysisplaybook` fails it on the clone row. 20 passed / 2 failed each time; the role file restored with `git checkout`.
- **Item 2.** A second `Spaarke Ontology Service.xml` in another solution's `Roles` folder: the round 1 guard (the file as committed) passed 20 / 20 with it present; the new guard fails (`A role is named by two role files`, plus the three checks that load the person roles). Must-not-fire: the repo's role files (`ASecondFileNamedForAnyRoleFails`, which also seeds a second person-role file and a second app-users file in a scratch copy).
- **Item 8.** A misspelt name in the real `MustNotHold` list (`prvDeletesprk_DecisonRecord`) fails `MustNotHoldNamesRealTables`. Must-not-fire: the real list, and the same names in other casing. Must-fire in the test: misspelt table, misspelt verb, no `prv` prefix, a verb with no table.
- **Items 3-7, the script.** `tests/scripts/Set-FeatureRolePrivileges.Tests.ps1` (Pester 3.4) runs the whole script in a child pwsh against `tests/scripts/Set-FeatureRolePrivileges.Harness.ps1`, a test double of `az`, `Invoke-RestMethod` and `Start-Sleep` (same approach as `Apply-DoLanePolicies.Harness.ps1`). Against the round 1 script: 9 passed, **7 failed** (non-root role, disallowed depth, depth read before the first write, `-Apply` on drift, the two read-back failures, the encoded filter); the controls pass in both. Against this round's script: **16 / 16**.
- **Fail closed.** The real script, a dry run, an empty `AZURE_CONFIG_DIR`: `FAIL: a read failed: No Dataverse token ... Nothing was changed.`, exit 1 (also a test).
- **Lint.** `Parser::ParseFile` on the script: 0 errors.

### Verification (fix round 2)

`dotnet build tests/Spaarke.ArchTests --no-incremental`: 0 Warning(s), 0 Error(s). `FeatureRolePrivilegeGuardTests`: 22 / 22 (20 before; added `ASecondFileNamedForAnyRoleFails` and `MustNotHoldNamesRealTables`). Whole `Spaarke.ArchTests`: 1962 / 1962 passed after the first part (1960 before; the two added tests); 1965 / 1965 after the second part (items 10-13: three more tests). `Sprk.Bff.Api.Tests` not run (no `src/server` change).

### Fix round 2, second part (owner rulings 2026-11-05: standalone exceptions and two removals; `decisions.md`, "Standalone add-on rulings, two role removals, ...")

| Item | Outcome |
|---|---|
| 10. Standalone exceptions | Done. `StandaloneExceptions` in `FeatureRolePrivilegeGuardTests` lists Office Add In User, AI Analysis User and Console User, each with its reason; `ExpectedStandaloneExceptions` pins the list literally (`TheStandaloneExceptionsArePinned`: an added or dropped role, no reason, or a role that is not an add-on with features fails). The three are no longer repeated alone (`AloneRows` skips the exceptions); every other add-on stays checked alone. `RemovingAStandaloneExceptionBringsItsGapsBack` takes each exception away in turn and shows that gaps come back for that role (must-fire), and shows none with the exceptions (must-not-fire). The `KnownGaps` entries for the three roles alone, D39 included, are deleted; the baseline keeps G3 only. G9 is dropped: the "Analysis run (writing playbook, Basic User + AI Analysis User)" row is deleted (the Core User row stays), and the AI Analysis User class no longer names it. Doc §5.1: role model, gap table (G9 row gone), the standalone gap summary (replaced by the ruling), the Roles-meant column of the Office, analysis and Decision commit rows. This notes file: Part D table, the gap list section. |
| 11. Removal 1 | Done. `prvCreatesprk_BudgetRevision` is out of `Spaarke Ontology Administrator.xml` (Write and Delete stay). The writer-only `MustNotHold` rule no longer lists Create on `sprk_budgetrevision`; a new rule forbids Create on it for every person role with no exception. Seeded: Create put back in the role file fails the guard (3 tests: the must-not-hold check on the real files, the seeded role-file test, the removals test). |
| 12. Removal 2 | Done. Checked first: the "Report Card wizard" and "KPI assessment" rows are [Core] only, and no declared row asks `prvAppendTosprk_ReportCard` of a role set that includes Basic User (the event regarding-types list has no report card), so nothing blocked it. `prvAppendTosprk_ReportCard` was taken out of `Spaarke Basic User.xml`. **Superseded in fix round 3: R2 is not applied** (the privilege is back in the role file and the rule is gone; see below). `MustNotHoldRule` gained an optional `Only` list, which stays. |
| 13. Script removal step | Done. `config/feature-role-privileges.json` has a separate `removals` block (role, table, privilege, `expectedDepth`, gap, feature) with the two removals. `scripts/Set-FeatureRolePrivileges.ps1 -Remove` acts on that block only: dry run by default, `-Remove -Apply`, `-Remove -Verify`. The add path never removes: it validates the block (a stale entry fails closed) but never acts on it. RemovePrivilegeRole is a Web API action bound to the role (the repo's reviewed scripts `Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1` and `ebilling/Deploy-EbillingBillingAdminRole.ps1` call it with the body `{ Privilege: { @odata.type: Microsoft.Dynamics.CRM.privilege, privilegeid } }`; the same shape is used, one call per privilege on the root role). Same fail-closed reads, the root-business-unit and single-root-role checks, the settle loop, the read-back that the privilege is absent on the root role and every business-unit copy, and the applied / not-applied summary. A privilege held at a depth other than `expectedDepth` is not removed (exit 1, nothing changed); the config is refused before connecting when the role XML still holds a listed privilege or a privilege is both added and removed; after the removal the root role is compared with its state before and any other difference fails the run. `-Verify` (plain or `-Remove`) fails while a removal is pending or a copy still holds a removed privilege. |

Proofs for items 10-13: the same seeded fail-before method as above (role files and the exception entry edited in place, restored with `git checkout` or from a saved copy). The script is shown against the test double only (`tests/scripts/Set-FeatureRolePrivileges.Tests.ps1`, now 32 cases, 14 of which fail on the previous script): `-Remove` dry run; `-Remove -Apply` sends one RemovePrivilegeRole whose body names only the listed privilege; the add path with a removals block present makes no RemovePrivilegeRole call; `-Remove` reads and writes none of the adds; `-Remove -Verify` and plain `-Verify`; nothing pending; a copy that still holds it; a role outside the root business unit; a depth other than expected; the role file still holding the privilege; both added and removed; a copy that never drops it; a failed read-back; a failed RemovePrivilegeRole; a platform that also drops another privilege. `Parser::ParseFile` on the script: 0 errors.

## Applied on dev

- 2026-11-05, spaarkedev1, run by UAC-r2 under the owner's go, from `98813dcaa2`.
- Dry run: 8 changes (G1 x3, G2, G4, G6, G7, G8). G5 was already live.
- `-Apply`: exit 0, and every business-unit copy read back.
- `-Verify`: exit 0, PASS.
- Observed: AddPrivilegesRole raises an existing lower-depth row to the requested depth in place (Basic -> Deep), with no separate removal.
- The removals (items 11-13) were not applied in that run; after fix round 3 the block lists one removal (R1). R1 is applied below.

## Fix round 3 (R2 withdrawn, communication rows, apply script exit codes, Pester 5)

Branch `task/fm-046b`. No Dataverse write was made and `-Apply` / `-Remove -Apply` were not run; the script's apply paths are shown against the test double only.

| Item | Outcome |
|---|---|
| 1. F1: drop removal R2 | Done. **R2 not applied: communication send, reply and re-file ask AppendTo on a report card regarding record as the caller; awaiting the owner.** Evidence: `CommunicationRecordAuthorizationFilter.cs:650-706` (send and send-bulk: AppendTo on associations and on regarding records copied from the email replied to; `RegardingFieldMap.All` includes `sprk_regardingreportcard`; `CallerRecordAccessProbe` at `:779-787`), and the re-file (`:79-84`, `CommunicationEndpoints.cs:128-153`, `OwnedChildWrite.cs:803,832-836,897`). `Spaarke Basic User.xml` is back to its state before `8b45f8dfd0` (byte-identical to `98813dcaa2`'s); R2 is out of `config/feature-role-privileges.json` removals; the `Only=[Basic]` MustNotHold rule is gone; `RemovalsAreAbsentFromTheRoleFilesAndForbidden` expects the single removal (R1). |
| 2. New matrix rows | Done. "Communication send / reply" and "Communication re-file" in the guard and doc §5.1, each for Core User and for Basic User (a base role alone; the existing communication rows are Core User's, and Basic User files and reads). Asks, all as the caller, at Deep (communications and regarding records are team-owned, I-6): AppendTo on the twelve tables of `RegardingFieldMap.All` (`RegardingFieldMap.cs:14-27`, `RegardingNameFields.cs:70-85`); send / reply also Read on `sprk_communication` (a reply reads the email it inherits from, `:687-696`, `CommunicationThreadReadService.cs:1311-1335`); re-file also Write (`:218,536-540`) and Read (`ChildRecordEndpoints.cs:685-687`) on `sprk_communication`. Create on `sprk_communication` is not asked of the caller (the row is written app-only). Core User meets every row; Basic User's gaps are in the table below. |
| 3. F1: exit codes | Done. After a successful read-back the written roles are re-read and the state rebuilt (`Get-State`, `Write-Report`, `Get-Reasons`, the shapes `-Verify` uses); a depth above the declared one, a copy that differs from its root role, a removal held at another depth or a copy still holding a removed privilege prints the same report and exits 1 ("APPLY: the changes were applied and read back, but the live roles do not match the declared set"). A copy that drifted before the write and was converged by it is not reported (control). |
| 4. F3: Pester 5 | Done. `tests/scripts/Set-FeatureRolePrivileges.Tests.ps1` uses `BeforeAll` helpers and `Should -Be` syntax; it passes under Pester 5.7.1 (38 / 38; the 32 earlier cases are all kept). Added to the Pester list of the `ontology-script-tests` job in `.github/workflows/ci-standalone-checks.yml` (same checkout paths, same 5.7.1 pin). |
| 5. F3: "both added and removed" | Done. Role C's file holds `prvAppendTosprk_c` at Global (the added depth), so the add check passes and only the script's own pair check can stop the run; the test asserts that message alone (and not the XML messages). |
| 6. K: vacuous `Only` | Done. `VacuousOnly`: `Only` empty, or a role in both `Only` and `Except`, is reported (`PersonRolesHoldNothingTheyMustNot` asserts none). Controls in `MustNotHoldRejectsAVacuousOnly`: must-fire (empty; overlapping), must-not-fire (`Only` unset; `Only` naming a role `Except` does not), and `Only` limits `ForbiddenHeld` to the roles it names. |
| 7. K: "other privilege changed" report | Done. Each reported privilege names its depth before and after (`changed or went missing (before Basic, after None)`, `appeared during the removal (before None, after Global)`); the "also gained" warning on the add path names the depth after. Harness: `removeAlsoSets` (a platform that adds or changes a privilege). |
| 8. K: wording | Done. Doc §5.1 (Removals paragraph) and the script header: the add path validates the removals block but never acts on it, and a stale entry fails closed. G9 paragraph and the Analysis run row: a writing run asks Write on `sprk_document` as the caller, so any role set that holds it can run one (the "Core User only" wording is gone). |

### New gaps (Basic User; owner decision pending, in `KnownGaps` with the held depth)

Core User meets every new row. Basic User, with the report card AppendTo back, lacks:

| # | Feature | Privilege (Deep needed) | Held by Basic User |
|---|---|---|---|
| G11 | Communication send / reply | AppendTo `sprk_servicerequest` | none |
| G11 | Communication send / reply | AppendTo `sprk_budget` | none |
| G11 | Communication send / reply | AppendTo `account`, AppendTo `contact` | Basic |
| G11 | Communication send / reply | Read `sprk_communication` | Basic |
| G12 | Communication re-file | Write and Read `sprk_communication` | Basic |
| G12 | Communication re-file | AppendTo `sprk_servicerequest`, `sprk_budget` | none |
| G12 | Communication re-file | AppendTo `account`, `contact` | Basic |

No role file changed for G11 and G12. The baseline also still holds G3. **Ruled 2026-11-06 (owner; `decisions.md`, "Communication send, reply and re-file need Core User"): not granted. Basic User is not raised; these features need Core User.** G11 and G12 are no longer open owner questions; the baseline entries stay, with that reason. The final round adds one Append row to G12 and a new G13 (below).

### Proofs (fix round 3)

- **Item 2, seeded.** Taking `prvAppendTosprk_ReportCard` out of the real `Spaarke Basic User.xml` fails `FeatureRequirementsAreHeldByTheirRoles`, naming "Communication send / reply (Basic User)" and "Communication re-file (Basic User)" (held: none). `RemovingReportCardAppendToFromBasicUserFailsTheGuard` does the same on a copy: the copy passes first, then fails with both rows named, while Core User's rows still hold.
- **Item 3, failing first.** The Pester file against the previous script (`8b45f8dfd0`): 38 cases, **6 failed** (the three exit-code cases, the three before / after cases); the other 32 passed. Against this round's script: 38 / 38.
- **Item 5.** Deleting the script's added-and-removed check (the `is both added and removed in the config` line) fails the test, because the XML check fires with a different message.
- **Item 6.** `VacuousOnly` did not exist before; the control fails to compile without it, and passes with it.
- **Fail closed.** The real script, a dry run, an empty `AZURE_CONFIG_DIR`: `FAIL: a read failed` ... `Nothing was changed`, exit 1 (also a Pester case).
- **Lint.** `Parser::ParseFile` on the script, the test file and the harness: 0 errors.

### Known limits (fix round 3)

- Not modelled in the new rows: the attachment Read (an `AuthorizationService` decision, not a single privilege), the thread visibility read, and the Append Dataverse itself asks on `sprk_communication` when a re-file writes a lookup.
- The re-file's AppendTo is the BFF's for ownership parents (`OwnedChildWrite.cs:803,832`) and Dataverse's, on the caller's own PATCH, for the other regarding tables; both are the caller's right on the record.

### Verification (fix round 3)

| Check | Result |
|---|---|
| `dotnet build tests/Spaarke.ArchTests --no-incremental` | 0 Warning(s), 0 Error(s) |
| `FeatureRolePrivilegeGuardTests` | 27 / 27 (25 before; added `MustNotHoldRejectsAVacuousOnly`, `RemovingReportCardAppendToFromBasicUserFailsTheGuard`) |
| Whole `Spaarke.ArchTests` | 1967 / 1967 passed (1965 before) |
| Pester 5.7.1, `tests/scripts/Set-FeatureRolePrivileges.Tests.ps1` | 38 / 38 passed (32 before); 6 fail on the previous script |
| `Parser::ParseFile` on the script, test file and harness | 0 errors |
| Dry run, empty `AZURE_CONFIG_DIR`, real script | exit 1, `FAIL: a read failed` ... `Nothing was changed` |
| `Sprk.Bff.Api.Tests` | not run (no `src/server` change) |

## Applied on dev: removal R1

- 2026-11-06 02:35Z, spaarkedev1, run by UAC-r2 under the owner's go, from `e6c0c94fa3`.
- `-Remove` dry run: 1 removal (R1), no depth mismatch, no copy drift.
- `-Remove -Apply`: exit 0, read back absent on the root role and every business-unit copy.
- `-Remove -Verify`: exit 0, PASS.
- `-Verify` (additions): exit 0, PASS. Its informational list of "live differs from config `from`" shows the already-applied additions, which is expected.

## Fix round 4 (final)

Branch `task/fm-046b`. No Dataverse write was made; `-Apply` and `-Remove -Apply` were not run (the script is shown against the test double only). `Sprk.Bff.Api.Tests` was not run; no `src/server` change beyond what master brought.

| Item | Outcome |
|---|---|
| 1. Merge master | `origin/master` (`f34993dc78`, #2577) merged into the branch with no conflicts (merge commit `b5f243e0ab`). Ebilling's `ProcedureRolePrivilegeGuardTests.cs` and `scripts/ebilling/*` were not taken from anywhere else (their 089e is not on master; the merge-second rule applies later). |
| 2. Summarize Files row | Done. `Summarize Files (unfiled document)`, `[Core]`, Deep, `prvCreatesprk_document`, evidence `DataverseDocumentsEndpoints.cs:1074` (the `accessProbe.CallerHoldsPrivilegeAsync` call in `AuthorizeDocumentCreateAsync` after the merge). Deep because the new row is owned by the caller's business-unit team and Core User holds Create at Deep. The create binds no record, so there is no AppendTo row. The row comment and doc §5.1 note that the probe accepts the privilege at any depth (a known limit). Seeded proof: `RemovingDocumentCreateFromCoreUserFailsTheSummarizeFilesRow` takes Create off a copy of Core User and the guard reports the row (the copy passes first). |
| 3. K2: re-file needs Append on the communication | Done. `prvAppendsprk_Communication` (Deep) on both re-file rows (Core User, Basic User); evidence `CommunicationEndpoints.cs:63-95`, `ChildRecordEndpoints.cs:685-687` (the PATCH runs as the caller, so Dataverse asks Append on the row that carries the lookup as well as AppendTo on the target). Core User holds Deep: no gap. Basic User holds Basic: G12 gains a `KnownGaps` entry (held Basic), and the doc's G12 row and this table name it. Seeded proof: `RemovingCommunicationAppendFromCoreUserFailsTheRefileRow`. |
| 4. K2: thread read | Done. The solution's `sprk_CommunicationThread` entity is **UserOwned** (`OwnershipTypeMask`; a per-user thread is the user's own, a record thread is team-owned, I-6), so the case is the user- or team-owned one: `prvReadsprk_CommunicationThread` at Deep on the send / reply rows (Core User and Basic User), evidence `CommunicationRecordAuthorizationFilter.cs:647,672,767`, `CommunicationThreadReadService.cs:1260-1275`. **Both roles hold Read on the thread at Basic, so both have a gap: G13**, two `KnownGaps` entries (held Basic), owner decision pending. Not asked of the re-file (the re-file does not read the thread in the filter). |
| 5. K4: doc wording | Done. Removals paragraph: "the role file keeps the privilege; the config's removals block no longer lists it". |
| 6. K4: re-read phase label | Done. `$phase = 're-read'` before the post-write re-read loop; the catch prints "The writes were confirmed by the read-back; the final re-read of the written roles failed." instead of the read-back message. New Pester case "the post-write re-read itself throws" (harness option `failReadAfter`, a read of the root role that fails from its second read after the first POST): exit 1, `FAIL: re-read on`, not `FAIL: read-back on`, applied 2 / not applied 0. Fails on the previous script (1 of 39), passes now. |
| 7. G11 / G12 ruling (coordinator, owner 2026-11-06) | Done. Basic User is not raised. Every G11 / G12 `KnownGaps` entry (and the new Append one) carries the reason "Owner ruled: not granted (Core User required), 2026-11-06"; doc §5.1 marks them "Ruled: not granted, needs Core User"; no role file changed. G13 is not covered by that ruling for Core User, whose gap stays open. |

### Existing tests on the not-held path (coordinator question: a Basic-User-only caller is refused with the normal 403, not a 500)

All in `tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs` unless named. **Send:** covered. AppendTo not held on an association `Send_AnAssociationWithoutAppendTo_Is403_...` (`:820`), on the regarding of an email replied to `Send_AVisibleInheritFromCommunicationWhoseRegardingTheCallerCannotAttachTo_Is403` (`:837`), on a thread's record `Send_AMessageIntoARecordThreadWithoutAppendToOnItsRecord_Is403_...` (`:867`) and `..._WithAnEmptyAssociationsArray_..._Is403WithoutAppendTo` (`:952`), `SendBulk_AnAssociationWithoutAppendTo_Is403_...` (`:1070`); Read not held on the inherit-from email `Send_AnInheritFromCommunicationTheCallerCannotSee_Is403_...` (`:806`); thread not readable `Send_AThreadTheCallerCannotSee_Is403ForAMessage_...` (`:790`). **Re-file, filter stage:** Write not held `Filing_ByAReadOnlyCaller_Is403_InTheFamilysOwnShape_...` (`:748`). **Re-file, handler stage:** AppendTo not held on the target is the uniform 404 (`ChildRecordEndpoints.NotFoundCode`) by design, not a 403: `CommunicationFiling_UnderARecordTheCallerCannotAppendTo_IsTheUniformNotFound_AndNothingIsWritten` (`tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs:927`) and `...CommunicationFilingSignal.cs:140`. **Gap, not added (the brief said to stop on this point and report):** the re-file with Append (or Write) denied by Dataverse on the caller's own PATCH is tested only as "not 204" (`SecureChildOwnershipAiToolTests.CommunicationFilingSignal.cs:156`, `PatchStatus = 403`); no test pins its status to the route's 403 ProblemDetails rather than a 500. A Basic-User-only caller's re-file is also refused with the 404 (not the 403) when AppendTo on the target is the missing right.

### Verification (fix round 4)

| Check | Result |
|---|---|
| `dotnet build Spaarke.sln` | 0 Warning(s), 0 Error(s) |
| `FeatureRolePrivilegeGuardTests` | 29 / 29 (27 before; added the Summarize Files and the re-file Append seeded proofs); with the two new rows removed, the two new tests and four others fail |
| Whole `Spaarke.ArchTests` | 1970 / 1970 passed |
| Pester 5.7.1, `tests/scripts/Set-FeatureRolePrivileges.Tests.ps1` | 39 / 39 (38 before); the new case is the one that fails on the previous script |
| `Parser::ParseFile` on the script, test file and harness | 0 errors |
| `Sprk.Bff.Api.Tests` | not run (no `src/server` change beyond master's) |
