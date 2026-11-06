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
- G9: stays in the baseline; it joins the Part D list.
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
| Office Add In User | Office save, Office version save, Office quick create, Event (typed route) |
| AI Analysis User | Analysis from a playbook, Analysis run, Analysis run (writing playbook) |
| Console User | Decision commit and Decision actions, the Decide lane: Write on the subject is not asked of it alone for event, To Do and work assignment subjects (the Do lane's subjects, `SignalReadService.cs:229-234`; the Do lane stays Core-only) |
| Billing Administrator | Billing: Procedure override (its row was already alone) |
| AI Analysis Admin | Playbook administration (rows: Create and Share on `sprk_analysisplaybook`, asked of the caller, `PlaybookEndpoints.cs:27,107,120,176`, `PlaybookAuthorizationFilter.cs:55-75,166,172`; Write for the edit routes and Read for a clone's source, added in fix round 2) |
| Ontology Administrator | none in this matrix: its routes write as the user, so Dataverse asks each right itself (`OntologyAdminCallerBoundaryGuardTests`) |
| Access Administrator | none in this matrix: membership writes, pinned by `ContactOrganizationMembershipWriterGuardTests` |
| Reporting Access Admin, Author, Viewer | none in this matrix: the reporting routes admit a caller by the role itself (`ReportingAuthorizationFilter.cs`) |

### The gap list (for UAC-r2, one owner decision; nothing applied)

39 gaps (38 at first write, D39 added in fix round 1), each in `KnownGaps` with its held depth. Evidence paths are under `src/server/api/Sprk.Bff.Api/` unless stated.

| # | Role (alone) | Privilege | Depth needed | Held | Feature | Evidence |
|---|---|---|---|---|---|---|
| D1 | Office Add In User | `prvAppendTosprk_Todo` | Deep | none | Office save to a To Do | `Api/Office/OfficeEndpoints.cs:185-194`; `Api/Filters/EntityAccessFilter.cs:94,115-155,273-282` |
| D2 | Office Add In User | `prvReadContact` | Deep | Basic | Office quick create, the assignee | `Api/Filters/QuickCreateSourceAccessFilter.cs:310-345`; `Services/Office/OfficeService.cs:2437-2455` |
| D3 | Office Add In User | `prvAppendTosprk_RecordType_Ref` | Basic | none | Office quick create, a project's record type | `Services/Office/OfficeService.cs:2408-2419` |
| D4 | Office Add In User | `prvReadsprk_Todo` | Deep | none | Office quick create from a To Do | `Api/Filters/QuickCreateSourceAccessFilter.cs:246-260`; `Api/Filters/EntityAccessFilter.cs:115-155` |
| D5 | Office Add In User | `prvAppendTosprk_RecordType_Ref` | Basic | none | Event (typed route), the record type | `Api/Events/EventEndpoints.cs:949`; `Services/Dataverse/CallerWriteAuthorization.cs:390-409` |
| D6 | Office Add In User | `prvAppendTosprk_analysis` | Deep | none | Event (typed route) regarding an analysis | `Api/Events/EventEndpoints.cs:131,378-405`; `src/server/shared/Spaarke.Dataverse/Models.cs:1439-1449` |
| D7 | AI Analysis User | `prvReadsprk_Document` | Deep | none | Analysis from a playbook | `Api/Filters/AnalysisAuthorizationFilter.cs:152,781-788` |
| D8 | AI Analysis User | `prvAppendTosprk_Document` | Deep | none | Analysis from a playbook | `Api/Filters/AnalysisAuthorizationFilter.cs:152,781-788` |
| D9 | AI Analysis User | `prvReadsprk_Document` | Deep | none | Analysis run | `Api/Ai/AnalysisEndpoints.cs:115-120`; `Api/Filters/AnalysisAuthorizationFilter.cs:26-28,943-952` |
| D10 | AI Analysis User | `prvWritesprk_Document` | Deep | none | Analysis run, writing playbook (G9; with Basic User too) | `Api/Ai/AnalysisEndpoints.cs:118-120`; `Api/Filters/AnalysisAuthorizationFilter.cs:922-952,970` |
| D11-D14 | Console User | `prvReadsprk_Matter`, `prvReadsprk_Project`, `prvReadsprk_WorkAssignment`, `prvReadsprk_ServiceRequest` | Deep | none | Decision commit: read the core record | `Services/Signals/SignalCoreRecordAccess.cs:239-247`; `Services/Dataverse/CoreAncestorResolver.cs:67-73` |
| D15-D18 | Console User | `prvAppendTosprk_Matter`, `prvAppendTosprk_Project`, `prvAppendTosprk_WorkAssignment`, `prvAppendTosprk_ServiceRequest` | Deep | none | Decision commit: AppendTo on the core record | `Services/Signals/DecisionCallerRights.cs:61`; `Services/Dataverse/CoreAncestorResolver.cs:67-73` |
| D19-D22 | Console User | `prvWritesprk_Matter`, `prvWritesprk_Project`, `prvWritesprk_Communication`, `prvWritesprk_Invoice` | Deep | none | Decision commit: Write on the subject (Decide lane) | `Services/Signals/DecisionCallerRights.cs:42,67-83`; `Services/Signals/SignalWriter.cs:503-507` |
| D23 | Console User | `prvAppendToTransactionCurrency` | Basic | none | Create budget: the currency | `Services/Signals/Actions/DecisionActionExecutors.cs:700` |
| D24-D27 | Console User | `prvCreatesprk_Todo`, `prvCreatesprk_Event`, `prvCreatesprk_WorkAssignment`, `prvCreatesprk_Budget` | Basic | none | Add To Do, create event, assign task, assign work, create budget | `Services/Signals/Actions/DecisionActionExecutors.cs:217,588,1247,1321,1355,1386,1449` |
| D28-D31 | Console User | `prvAppendsprk_Todo`, `prvAppendsprk_Event`, `prvAppendsprk_WorkAssignment`, `prvAppendsprk_Budget` | Basic | none | The same actions: the created row is filed under the core | `Services/Signals/Actions/DecisionActionExecutors.cs:1278-1281,1299`; `Services/Signals/Actions/DecisionRouteCores.cs:74-85` |
| D32 | Console User | `prvAppendTosprk_RecordType_Ref` | Basic | none | The same actions and the budget inquiry: the record type | `Services/Signals/Actions/DecisionActionExecutors.cs:1281`; `Services/Signals/Actions/BudgetInquiryExecutor.cs:348` |
| D33 | Console User | `prvWritesprk_Budget` | Deep | none | Revise budget | `Services/Signals/Actions/DecisionActionExecutors.cs:523` |
| D34 | Console User | `prvAppendToContact` | Deep | none | Add To Do, assign work, assign task (the assignee) | `Services/Signals/Actions/DecisionActionExecutors.cs:880,1254-1255,1323,1399,1451` |
| D35 | Console User | `prvAppendTosprk_EventType_Ref` | Global | none | Assign task: the Task event type | `Services/Signals/Actions/DecisionActionExecutors.cs:1462-1463` |
| D36-D37 | Console User | `prvCreatesprk_ServiceRequest`, `prvAppendsprk_ServiceRequest` | Basic | none | Budget inquiry | `Services/Signals/Actions/BudgetInquiryExecutor.cs:146,181,276-282` |
| D38 | Console User | `prvAppendTosprk_Organization` | Deep | none | Budget inquiry: the outside firm | `Services/Signals/Actions/BudgetInquiryExecutor.cs:181,352` |
| D39 | Office Add In User | `prvReadsprk_Document` | Deep | none | Office version save: the "Save as version" offer on a name collision | `Api/Office/OfficeEndpoints.cs:547-605` |

Also open for the same batch: **G9** (Basic User + AI Analysis User, Write on `sprk_document` at Deep, held none; D10 is the same privilege for the add-on alone).

Decisions for the owner with this list: whether the Console's Decide lane is meant for Console User alone at all (28 of 38), or Console User stays a companion of a base role in practice; and whether D1-D6 belong on Office Add In User or the add-in's Office save to a To Do and the regarding-an-analysis event stay Core-only.

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

Branch `task/fm-046b`. No Dataverse write was made and `-Apply` was not run. The script's apply path is shown against a test double only (no network, no token).

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

`dotnet build tests/Spaarke.ArchTests --no-incremental`: 0 Warning(s), 0 Error(s). `FeatureRolePrivilegeGuardTests`: 22 / 22 (20 before; added `ASecondFileNamedForAnyRoleFails` and `MustNotHoldNamesRealTables`). Whole `Spaarke.ArchTests`: 1962 / 1962 passed (1960 before; the two added tests). `Sprk.Bff.Api.Tests` not run (no `src/server` change).
