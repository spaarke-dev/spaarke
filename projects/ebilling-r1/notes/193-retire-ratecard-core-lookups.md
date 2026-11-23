# Task 193 - retire the sprk_ratecard core-record lookups

Branch `task/ebilling-193`, based on `7a6c530000`. Source and scripts only: no live Dataverse write, no solution import, no deploy.

Why: since task 085 a rate card belongs to a Procedure Version and nothing reads `sprk_ratecard.sprk_matter`, `sprk_project` or `sprk_workassignment` (DEF-027). The owner decided on 2026-11-23 (via uac-r2) to remove them everywhere.

## Grep before and after

Relationship names (`sprk_matter_ratecard_matter|sprk_project_ratecard_project|sprk_workassignment_ratecard_workassignment`), command `git grep -c -E "<names>" <rev> -- .`:

| File | Before (7a6c530000) | After (HEAD) |
|---|---|---|
| docs/data-model/sprk_ratecard.md | 3 | 0 |
| scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 | 3 | 0 |
| SpaarkeMaster/Other/Relationships.xml | 3 | 0 |
| SpaarkeMaster/Other/Relationships/sprk_Matter.xml | 2 | 0 |
| SpaarkeMaster/Other/Relationships/sprk_Project.xml | 2 | 0 |
| SpaarkeMaster/Other/Relationships/sprk_WorkAssignment.xml | 2 | 0 |
| scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1 | 0 | 7 (new: the delete list) |
| tests/scripts/Remove-EbillingRateCardCoreLookups.Tests.ps1 | 0 | 9 (new) |
| projects/ebilling-r1 task 193 POML, notes/defer-issues.md, projects/unified-access-control-r2 notes | 6 | 6 (project notes, unchanged by design) |

Column mentions (`git grep -c -i -E "sprk_(matter|project|workassignment)\b" <rev> -- <rate card files>`):

| File | Before | After |
|---|---|---|
| SpaarkeMaster/Entities/sprk_ratecard/Entity.xml | 9 | 0 |
| .../FormXml/main/{da801593-...}.xml | 3 | 0 |
| .../FormXml/main/{da801593-...}_managed.xml | 3 | 0 |
| .../SavedQueries/{de71004e-...}.xml | 6 | 0 |
| scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 | 7 | 0 |
| scripts/ebilling/Deploy-EbillingFormsViews.ps1 | 3 | 1 (a comment saying the columns are retired) |
| tests/scripts/Deploy-EbillingFormsViews.Tests.ps1 | 1 | 1 (the new expectation that none appear) |
| docs/data-model/sprk_ratecard.md | 3 | 0 |

## Files changed and why

- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ratecard/Entity.xml`: removed the `sprk_Matter`, `sprk_Project`, `sprk_WorkAssignment` attributes (`sprk_firm`, `sprk_procedureversion`, `sprk_sourcedocument` stay).
- `.../FormXml/main/{da801593-...}.xml` and `_managed.xml`: removed the Matter/Project row and the Work Assignment cell.
- `.../SavedQueries/{de71004e-...}.xml` (Active Rate Cards): removed three layout cells and three fetch attributes.
- `Other/Relationships.xml` and `Other/Relationships/sprk_Matter.xml`, `sprk_Project.xml`, `sprk_WorkAssignment.xml`: removed the three `EntityRelationship` blocks.
- `scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1`: no `Add-Lookup` for the three (a new environment would have re-created them); the core tables are no longer required to exist for them; the publish list drops them. Found while grepping: not in the task's inventory.
- `scripts/ebilling/Deploy-EbillingFormsViews.ps1` and `tests/scripts/Deploy-EbillingFormsViews.Tests.ps1`: the `sprk_ratecard` form/view column lists named the three; a re-run would have put them back and failed on the missing columns. Lists changed and pinned by a new test. Found while grepping: not in the task's inventory.
- `scripts/ebilling/Remove-EbillingRateCardCoreLookups.ps1` (new) and `tests/scripts/Remove-EbillingRateCardCoreLookups.Tests.ps1` (new): the guarded dev delete and its tests.
- `docs/data-model/sprk_ratecard.md`, `scripts/README.md`, `projects/ebilling-r1/notes/defer-issues.md`: documentation; DEF-027 gets one line and stays open.

## Solution version decision

`SpaarkeMaster` is `1.3.2.0` in `Other/Solution.xml`. It is NOT bumped here. Reasons: the version belongs to the release commit (master already carries `1.3.2.1`; a bump on this branch would conflict and would claim a release this task does not make). What the release commit must do:

1. Bump the version past `1.3.2.1` and list these removals in its change notes.
2. Import SpaarkeMaster as a managed upgrade (stage for upgrade), not an update: only an upgrade import removes a column and a relationship from an environment that has them. Environments that already took the columns keep them until that import.
3. provisioning-r1 must be told that managed environments need that upgrade import.

For dev, the new script is the delete (below); the source retirement alone changes nothing live.

## Script behaviour (`Remove-EbillingRateCardCoreLookups.ps1`)

Every call goes through `EbillingSchema.psm1` (`Consistency: Strong`); entity-scoped `PublishXml` only; counts only, no row ids.

- No switch (or `-WhatIf`): report only. Per column: attribute, non-null row count (all states), dependencies (`RetrieveDependenciesForDelete`), relationship and its dependents; `sprk_ratecard` forms, views, personal views and grid configurations that name a column.
- `-Verify`: read only; exit 1 while any attribute, relationship, form or view reference remains.
- `-Apply` needs `-OwnerApproval`, `-BffDeployAttestation <sha>` and `-SpaDeployAttestation <sha>`; dev host only (`spaarkedev1.crm.dynamics.com`). Refuses with exit 2 and nothing written when:
  - any `sprk_ratecard` row of any state holds a value in a column, or the count cannot be had;
  - the attested BFF or SPA source names a rate card relationship, or a file that names the rate card table also names one of the three columns. The check is by content (`git grep` at the attested sha), not ancestry. 015's "grep the column name" does not fit: the names are columns of many tables, so the check is anchored on the rate card. A path that does not exist at the sha is a refusal, so the scan cannot pass vacuously. Limits: a file-level heuristic can false-positive; it cannot see a reader that builds the name at run time; code pages deployed from an older commit are not covered;
  - anything but the form and view it cleans depends on a column or relationship; a personal view or grid configuration names a column; a filter or order on a column blocks a view; a relationship is not exactly `sprk_ratecard.<column>` to the core table.
- Then: clean the `sprk_ratecard` forms and views (PATCH), publish `sprk_ratecard`, re-read until the dependents clear (6 x 5 s), delete the three relationships by schema name (the lookup column goes with its relationship), wait for the columns to be gone, publish `sprk_ratecard` again, verify. (Fix round 1: the core tables are never published, the `sprk_ratecard` publishes go through the shared D-83 procedure and refuse pending collateral unless `-AllowPendingCollateral`, and the caller must be shown to read every row before any count is believed; see "Review pass 1 and fix round 1".)
- A second `-Apply` is a no-op pass. A half-finished run resumes.
- Exit codes: 0 ok, 1 error, verify failed, or stopped after the first write (partly applied: re-run), 2 refused before the first write.

## Tests

- `tests/scripts/Remove-EbillingRateCardCoreLookups.Tests.ps1`, Pester 5.7.1 (the declared version; plain `Invoke-Pester` loads 6.2 which fails here): 69 passed, 0 failed. Covers refusal for each column holding a value, an unknown dependent, a personal view, a filtered view and an uncountable table; `-WhatIf` writes nothing; write order; idempotent and partial resume; verify failures; the attestation scan on a throw-away git repo (must-fire and must-not-fire, missing path, unknown sha); AST checks (only PATCH x2, POST, DELETE in the three write functions; confirm before write; refuse before write; pre-flight before `Initialize-EbillingSchema`; no direct REST calls); the schema script, forms script and solution source no longer name the lookups.
- `tests/scripts/Deploy-EbillingFormsViews.Tests.ps1`: 7 passed (2 failed on the base files with the new expectations, before the lists changed).
- New suite against the base files: 4 failed (the source-no-longer-names tests), passed after.
- Seeded mutation (refusal guard `if ($blockers.Count -gt 0)` turned into `if ($false)`): 7 failed; restored.
- `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ScriptMetadataStrongConsistencyGuardTests|...SpaarkeMasterApplicationDependencyTests|...InvoiceLookupCoverageGuardTests|...PolicyMatterCascadeGuardTests|...FeatureRolePrivilegeGuardTests|...InvoiceFamilyFieldLockSourceGuardTests|...WebResourceMirrorParityGuardTests"`: 106 passed, 0 failed (filtered; the whole project was not run). None of these classes names the three rate card columns.
- No BFF code touched, so no BFF build.

## Observed in passing (not changed)

- `Deploy-EbillingTimekeeperRateCardSchema.ps1` still adds the `sprk_sourcedocument` lookup, and SpaarkeMaster source still carries it: 116/079 territory.
- `Deploy-EbillingProcedureSchema.ps1:98` has a comment about "the sprk_ratecard core lookups"; that script never deleted them. (Fixed in fix round 1: the comment now points at tasks 116 and 193.)
- `Deploy-EbillingProcedureSchema.Tests.ps1` is a Pester 3.4.0 file; it needs its own runner and was not re-run (untouched by this task).

## Review pass 1 and fix round 1

Nothing live was run in this round: no Dataverse call, import or deploy. All tests run against test doubles; Pester 5.7.1 unless stated.

| # | Finding | Fix commit | Fail-before test |
|---|---|---|---|
| 1 (F1) | The repo-wide scoped-publish lint matched the banned word in the script help and in one test | `c07bc13e9d` | `tests/scripts/Publish-SolutionComponents.Tests.ps1` "no tenant-wide publish anywhere in the repo (D-83): has zero hits for any banned form": 76 passed / 1 failed on the review tip (hits: script help line 32, test line 477); 77 / 0 after the help was reworded ("never tenant-wide") and the test line dropped (the exact-equality check of the publish body already pins it) |
| 2 (F1) | A hand-rolled `Publish-Tables` published sprk_matter, sprk_project, sprk_workassignment (and sprk_ratecard) with no pending-collateral check | `c07bc13e9d` | New tests in `Remove-EbillingRateCardCoreLookups.Tests.ps1`: core tables never in any publish body; collateral read through `Get-EntityPublishCollateral` with `-ExcludeIds` = exactly the form and view the script edits; foreign pending item refuses (exit-2 exception, no write, nothing published); `-AllowPendingCollateral` proceeds; a pending item that appears after the edit stops the publish (plain error, no relationship deleted); `-WhatIf` refuses the same way. Against the review-tip script the whole file fails (the new functions are not found); mutations of the fixed script: `-ExcludeIds` dropped, 2 failed; refusal disabled, 3 failed. The sprk_ratecard publish now goes through `scripts/lib/Publish-SolutionComponents.ps1` (`Get-EntityPublishCollateral`, `Invoke-PublishXml`, the Deploy-EbillingFormsViews procedure); the three core tables are not published at all (deleting a relationship needs no publish) |
| 4 (F1) | The "any row holds a value" count only saw rows the caller may read; sprk_ratecard is user-owned | `f4cf4820ff` | New `Get-ReadAllRowsGap` in `EbillingSchema.psm1` (System Administrator role, or organization-wide Read; a fault in the check is a gap). Tests: admin proceeds, non-admin Global proceeds, Deep / Local / Basic and no privilege refuse, a faulting check refuses, nothing is counted when there is a gap, `-Apply` refuses with exit 2 before any write or count, the report mode only reports. Mutations: check neutered, 8 failed; count made unconditional, 2 failed |
| 5 (F2) | Data model doc said the lookups "are removed from dev" | `f338faafea` | Wording only (no test): "to be removed from dev by ... (task 193; the live run needs the owner's go)" |
| 6 (K1) | The "dependents remain after the edit" stop threw an `EscalationException` (exit 2, documented "nothing changed") after the form and view were edited and published | `c07bc13e9d` | Test "a dependent that appears after the form and view edit stops the run with a plain error (exit 1, partly applied), not exit 2 (K1)". Mutation back to `EscalationException`: 1 failed. Help text now defines exit 1 as "after the first write, re-run to resume" and exit 2 as "before the first write" |
| 7 | Same unchecked publish and caller-visible count in `Remove-EbillingSourceDocumentColumns.ps1` (task 116, not yet run live; what it deletes is unchanged) | `5f8c1deba6` | Tests in its own file (Pester 5.7.1, its declared version): gap per depth, fault, nothing counted on a gap, `-Delete` refuses with the gap, pending collateral refuses / `-AllowPendingCollateral` proceeds / appears mid-run stops with a plain error / `-WhatIf` does not read, the only `-Method` is the DELETE. Mutations: gap neutered, 3 failed; collateral refusals disabled, 2 failed |
| 8 | Stale comments in `Deploy-EbillingProcedureSchema.ps1` (line 98 "never deleted or changed ... the sprk_ratecard core lookups"; line 72 credited the core-record delete planner to task 117) | `f338faafea`, `93b0696dff` | Its Pester 3.4.0 suite pins that the script names no core table: the first reword broke it (1 failed), the second passes (95 / 0). The same planner credit sits in a description string at line ~589 (`New-LookupSpec` text written to dev); not changed: it is schema text, not a comment |
| F2 | Nothing in CI stopped the lookups coming back (a 029-style export from dev before `-Apply`, an old branch, a copied schema script) | `6f705cbd8b` | New `tests/Spaarke.ArchTests/RateCardCoreLookupRetirementGuardTests.cs` (18 cases): the three relationship names anywhere under `src/dataverse` and `scripts/ebilling`; any whole-word mention of the three columns in the `sprk_ratecard` table source (Entity.xml, forms, views). The delete script is the one allowed file, with its reason in the test. Must-fire and must-not-fire controls, a non-vacuity walk test, the maintenance procedure in the file. Seeded a relationship name into `Other/Relationships.xml` and a column into the rate card `Entity.xml`: both tests failed; seeds reverted |

K2 attestation scan fix (`d7884f3e34`): the two web resource folders under `src/dataverse` (`webresources`, `solutions/SpaarkeMaster/WebResources`) are now in the SPA scan paths (cheap; both exist at the paths on master, and neither names the rate card table today). Test: a form script naming the table and a column is refused (fails when the paths are removed: 2 failed).

### Known limits (not fixed; decided)

- **K2, attestation scan blind spots.** The scan is a file-level content heuristic at the attested commit. It cannot see a constant defined in one file and used in another, or a name assembled at run time. A code page or web resource deployed from an older commit than the attested one is not covered: the owner confirms every deployed surface is at or after the attested commit. Stated in the script help.
- **K3, a managed upgrade deletes the columns with any data, and nothing guards it.** The delete script guards dev only. A SpaarkeMaster upgrade import into another environment removes the three columns even if rate cards hold values. The release runbook therefore runs this script's report mode (no switches; it reads only and works against any URL: `-EnvironmentUrl <target>`) against each target before that import, and the import goes ahead only where it shows zero rows holding a value. An unmanaged install never loses them through an import (an update import leaves the columns behind).
- **K4, another user's personal view may break.** The script refuses on a personal view or grid configuration it can see that names a column, but personal views of other users are only visible to a caller with the right to read them. A personal view that names a retired column fails to load for its owner, who re-creates it; this does not block the retirement.

### Counts (filters and results)

- Pester 5.7.1: `Remove-EbillingRateCardCoreLookups.Tests.ps1` 94 passed / 0 failed (was 69); `Remove-EbillingSourceDocumentColumns.Tests.ps1` 37 / 0 (was 25); `Deploy-EbillingFormsViews.Tests.ps1` 7 / 0; `Publish-SolutionComponents.Tests.ps1` 77 / 0 (the lint suite; was 76 / 1); `Deploy-EbillingDecisionNoteColumns.Tests.ps1` 32 / 0 and `Deploy-EbillingFirmDescriptionColumn.Tests.ps1` 24 / 0 (they import the module that gained one function).
- Pester 3.4.0 (their declared version): `Deploy-EbillingStoredFileSchema.Tests.ps1` 76 / 0; `Deploy-EbillingProcedureSchema.Tests.ps1` 95 / 0.
- `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~RateCardCoreLookupRetirementGuardTests|FullyQualifiedName~InvoiceStatusRetirementGuard|FullyQualifiedName~ScriptMetadataStrongConsistency|FullyQualifiedName~SpaarkeMasterApplicationDependency"`: 36 passed / 0 failed (18 of them the new guard). The whole ArchTests project and Sprk.Bff.Api.Tests were not run.

## Live dev run
