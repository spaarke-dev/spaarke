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
- Then: clean the `sprk_ratecard` forms and views (PATCH), publish `sprk_ratecard`, re-read until the dependents clear (6 x 5 s), delete the three relationships by schema name (the lookup column goes with its relationship), wait for the columns to be gone, publish `sprk_ratecard` and the three core tables, verify.
- A second `-Apply` is a no-op pass. A half-finished run resumes.
- Exit codes: 0 ok, 1 error or verify failed, 2 refused.

## Tests

- `tests/scripts/Remove-EbillingRateCardCoreLookups.Tests.ps1`, Pester 5.7.1 (the declared version; plain `Invoke-Pester` loads 6.2 which fails here): 69 passed, 0 failed. Covers refusal for each column holding a value, an unknown dependent, a personal view, a filtered view and an uncountable table; `-WhatIf` writes nothing; write order; idempotent and partial resume; verify failures; the attestation scan on a throw-away git repo (must-fire and must-not-fire, missing path, unknown sha); AST checks (only PATCH x2, POST, DELETE in the three write functions; confirm before write; refuse before write; pre-flight before `Initialize-EbillingSchema`; no direct REST calls); the schema script, forms script and solution source no longer name the lookups.
- `tests/scripts/Deploy-EbillingFormsViews.Tests.ps1`: 7 passed (2 failed on the base files with the new expectations, before the lists changed).
- New suite against the base files: 4 failed (the source-no-longer-names tests), passed after.
- Seeded mutation (refusal guard `if ($blockers.Count -gt 0)` turned into `if ($false)`): 7 failed; restored.
- `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~ScriptMetadataStrongConsistencyGuardTests|...SpaarkeMasterApplicationDependencyTests|...InvoiceLookupCoverageGuardTests|...PolicyMatterCascadeGuardTests|...FeatureRolePrivilegeGuardTests|...InvoiceFamilyFieldLockSourceGuardTests|...WebResourceMirrorParityGuardTests"`: 106 passed, 0 failed (filtered; the whole project was not run). None of these classes names the three rate card columns.
- No BFF code touched, so no BFF build.

## Observed in passing (not changed)

- `Deploy-EbillingTimekeeperRateCardSchema.ps1` still adds the `sprk_sourcedocument` lookup, and SpaarkeMaster source still carries it: 116/079 territory.
- `Deploy-EbillingProcedureSchema.ps1:98` has a comment about "the sprk_ratecard core lookups"; that script never deleted them. Left as is.
- `Deploy-EbillingProcedureSchema.Tests.ps1` is a Pester 3.4.0 file; it needs its own runner and was not re-run (untouched by this task).

## Live dev run
