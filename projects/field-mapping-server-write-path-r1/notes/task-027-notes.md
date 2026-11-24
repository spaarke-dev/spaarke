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

## Left (on the owner's go)

1. Live check on dev under a Matter whose profile maps Law Firm 1 / External / Internal: pick one of each and confirm the picks are kept; create one leaving them unpicked and confirm they are filled from the matter; owner = the matter's team; mapped values present on first load.
