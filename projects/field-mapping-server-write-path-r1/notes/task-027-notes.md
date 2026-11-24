# Task 027 — Report Card wizard (FR-16, FR-02 regression)

FULL rigor, sonnet @ high, directional. One Sonnet implementer in an `isolation: worktree` agent on `task/fm-027`
(from 9f61f10961), commit b29bb1f5f5; merged into the work branch `--no-ff` as 33459cfbdb.

## What changed

- `CreateReportCardWizard/reportCardService.ts`:
  - `applyFieldMappings` import and call removed. The server maps, fill-only, from the regarding Matter/Project.
  - The create calls `createRecordViaBffWithWarnings(...)` on `POST /api/v1/child-records/sprk_reportcard`, and the server's warnings reach `result.warnings` (status `partial`). Before, the create already went to the BFF through `withBffChildWrites`, but its warnings were dropped.
  - No `mappingSource` is sent: the regarding parent is the mapping source, and the route infers it for `sprk_matter` / `sprk_project`.
  - The eight lookup hints are the full column names, so `findNavProp` takes its exact-match path.
  - Doc comments no longer describe client mapping.
- New `__tests__/reportCardService.payload.test.ts` (8 tests, exact request bodies). 5 of them fail on the old code: the Law Firm 1 / External / Internal regression, all eight pickers, unpicked pickers absent, no `/field-mappings/` request, and server warnings surfacing.
- Not changed: `CreateReportCardWizard.tsx`, `CreateReportCardStep.tsx` (the pickers already forward their ids), the host `main.tsx`, `reportCardService.resolver.test.ts`, and the adapter.

## Key inventory (step 1)

- Payload: `sprk_name` (trimmed), `sprk_narrative` and `sprk_duedate` when filled; the eight `sprk_Assigned*@odata.bind` lookups when picked; the regarding keys from `applyResolverFields`. No client default was set, so none moved to the registry. Nothing empty or null is sent.
- Dev metadata (read 2026-11-24): the report card's contact lookups are `sprk_AssignedAttorney1/2`, `sprk_AssignedParalegal1/2`, `sprk_AssignedtoExternal` and `sprk_AssignedToInternal`; the organization lookups are `sprk_AssignedToLawFirm1` and `sprk_AssignedLawFirm2`.
- Dev profile "Matter to Report Card (Attorney Matrix)" (b2915dad-587b-f111-ab0e-7ced8ddc4a05) is active, with 8 Manual Refresh rules. They include `sprk_assignedlawfirm1` → `sprk_assignedtolawfirm1`, `sprk_assignedtoexternal` and `sprk_assignedtointernal`, so the live regression check needs no temporary profile.

## Fix the class (Step 9.5 rule 6)

- Fragment lookup hints: the only other bind by a fragment of an assigned-person column is `CreateEventWizard/eventService.ts:191` (`'assignedto'`). It is already filed as DI-039 / #3039. The other fragment hints (`TodoRegardingUpdateBuilder`, `sprkAnalysis`) pick a regarding lookup by its target table, where each table has one lookup, so they are not the same defect.
- `applyFieldMappings(` callers left: `Spaarke.AI.Widgets/.../CreateAnalysisWizardWidget.tsx` (task 028), plus `FieldMappingService` itself and its test (task 035).

## Main session: review, merge, gates (2026-11-24)

- Review of the diff: no finding.
  - The error path is unchanged: `describeWebApiCreateError`, never throws.
  - With no association the body is the user's values only, as before.
- Gates (in this worktree, after the merge): jest `CreateReportCardWizard` + `CreateWorkAssignmentWizard` + `utils/adapters` 26 suites / 258 tests pass; `tsc --noEmit` 0 errors; eslint on the two changed files clean; no `applyFieldMappings` in the service.
- ADR check:
  - ADR-012: the change stays in the shared library.
  - ADR-021: no UI change.
  - ADR-024: the resolver fields are still written by `applyResolverFields`.
  - ADR-002: no plugin.
  - FR-02 / FR-09: no owner or mapped value is sent.

## PR and deploy (2026-11-24)

- PR #3072 merged to master at 20:20Z as 3c1018c97f. Nothing in `src/client/shared` or the wizard changed on master after it.
- Deployed `sprk_createreportcardwizard` to dev at 22:19Z (owner's go), built in a temporary worktree at 3c1018c97f.
  - Built bundle: SHA256 3F0701D3…6FC3ABB, 1,542,749 bytes.
    - No `/field-mappings/` call and no `applyFieldMappings`.
    - `mappingSourceEntity` appears only because the shared adapter supports it.
  - Read-back from dev: the same SHA.
  - Rollback: the previous dev bundle, deployed 2026-11-23, SHA 0B0B550C…36702005, 1,548,994 bytes. It still called `/field-mappings/`. A copy is kept outside the repo; redeploying it is the rollback.

## Live check on dev (2026-11-24, 22:52Z): FAIL, nothing mapped

Matter FMSWP-021-after-08 (MAT-000065, 3fa0d616-1183-4088-930e-109fdc981810), team-owned. All eight mapped columns are set on it. Wizard launched from the Matter form, so the association was locked to the Matter.

| Report card | Picked | Result |
|---|---|---|
| FMSWP-027-picks 45edd549-3658-4e42-88ed-9bd8ee330e09 | Law Firm 1 Tennant Whitcombe PC, External Ralph Dewey, Internal Test User 1 | The three picks were kept. The five unpicked columns are EMPTY; the Matter has values for all of them. |
| FMSWP-027-unpicked 737f7880-1b70-451b-8c56-943e6f2aba01 | nothing | All eight columns are EMPTY. |

Both rows:
- Owner is the Matter's team (Spaarke Business Unit 1).
- Regarding matter, record type, id and name are all right.
- No warnings were shown.
- The browser made one call, `POST /api/v1/child-records/sprk_reportcard`, with no query string and no `/field-mappings/` request.

**Cause.** The BFF trace for the second create says `step FieldMapping: NoOp no field mapping was asked for`.
- `ChildRecordEndpoints.InferMappingSourceTables` lists the TABLES BEING CREATED whose create infers a source. It is currently `sprk_matter` and `sprk_project`. It is not a list of parent tables.
- A create of `sprk_reportcard` that names no `mappingSource` therefore asks for no mapping.
- Task 027 removed the client mapping and sent no `mappingSource`. That rested on a misreading of the set. Its payload test asserted the misreading ("no mappingSource"), and review missed it.
- Every other wizard already on the server names its parent explicitly: Event, Matter, Project, To Do, and Work Assignment with its follow-on event. The Report Card wizard is the only one that does not.

**Fix (in scope of 027).** `reportCardService.ts` names the regarding Matter or Project as `mappingSource`, the same way the other wizards do. The payload test asserts the query string. No BFF change. Then PR, redeploy, and re-check on dev; each needs the owner's go.

## Left (on the owner's go)

1. Live check on dev under a Matter whose profile maps Law Firm 1 / External / Internal: pick one of each and confirm the picks are kept; create one leaving them unpicked and confirm they are filled from the matter; owner = the matter's team; mapped values present on first load.
