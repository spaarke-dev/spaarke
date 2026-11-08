# Task 022: Create Project wizard (including secure Project) on the server write path (FR-16) — code part

> Rigor FULL. Branch `task/fm-022`, from 9e68547626 (the work branch tip). **Code part only.** Not done here (owner decision: the NFR-02 verdict is taken in UAT): the deploy, the live check and the NFR-02 run. Nothing in this branch has been exercised live. Reference implementation: task 021 (`notes/task-021-notes.md`).

## What changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/projectService.ts` | `createProject` now always creates through `createRecordViaBffWithWarnings(authenticatedFetch, bffBaseUrl, 'sprk_project', entity, options)`. Never `dataService.createRecord` / `Xrm.WebApi`. **Fails closed**: with no `authenticatedFetch` or no base URL it writes nothing and returns "The project was not created: this screen is not connected to the Spaarke service, which must save it." Deleted: the `applyFieldMappings` call, the BU cascade (`applyUserBuDefaults` → `sprk_searchindexname`), the `sprk_ai_search_index` bind, the `routesChildWritesThroughBff` branch and the client `syncAssignedAccess` call. `options` = `mappingSource` from the AssociateToStep selection (query string only) + `secure: true` when `isSecure`. `securedAtCreate` is set on every successful secure create. Errors go through `describeWebApiCreateError(err, 'project', { rootCreate: true })`. `cascadeDefaults` is kept as `_cascadeDefaults` (accepted, not read) so the hosts compile unchanged. |
| `.../CreateProjectWizard/CreateProjectWizard.tsx` | Removed the step-1e client Assigned-To sync after secure provisioning (and its import). The BU resolve is kept as in 021 (its value is no longer read; its user-facing warning dropped to `console.warn`). `indexUploadedFiles` no longer passes `cascadeDefaults?.searchIndexName` (FR-40: the server's `FileIndexRequest` has no index name). Comments on steps 1a, 2 and the no-BFF secure branch rewritten. Filing (N:N `$ref` + `fileProjectUnderMatter`), the account association and the task-150 hold-back are unchanged. |
| `.../__tests__/projectService.fieldMapping.test.ts` | Rewritten, 19 tests (below). |
| `.../__tests__/projectService.test.ts` | Converted (deviation 2). |
| `.../__tests__/secureFromCreation.test.ts` | The undecorated-data-service case now expects `?secure=true`, `securedAtCreate: true` and no Xrm create. |
| `.../__tests__/CreateProjectWizard.secureHoldBack.test.tsx` | Expected sequences `['create-project','provision','assigned-access-sync']` → `['create-project','provision']`; new `it.each([true,false])`: no client sync, `indexUploadedFiles` called with 4 arguments. |
| `.../__tests__/CreateProjectWizard.provisioningHost.test.tsx` | "syncs after provisioning" → "makes no client Assigned-To sync for a SECURE project". |
| `src/.../services/__tests__/assignedAccessSync.test.ts`, `src/.../utils/__tests__/webApiCreateError.test.ts` | Outside the wizard folder, but both drive `ProjectService.createProject` (deviation 1). |

Hosts: **no change.** `src/solutions/CreateProjectWizard/src/main.tsx` already passes `authenticatedFetch` and `resolvedBffBaseUrl`; `InAppWizardHost` and `SummarizeFilesDialog` pass the same two. Each host picks up the new path only when its own bundle is rebuilt (the shared library is compiled into each). `provisioningService.ts`: no change (049 already treats `already_provisioned` as success when `securedAtCreate`). `SummarizeFilesDialog.tsx:559` already passes `securedAtCreate`, so its secure path keeps working now that every secure create carries the intent. BFF: not touched.

## Client-default inventory (POML step 2)

| Key | Class | Now |
|---|---|---|
| `sprk_projectname`, `sprk_projectdescription` | user value | sent |
| project type, practice area, attorney (hint `attorney`), paralegal (hint `paralegal`), outside counsel binds | user pick | sent (nav-prop discovery unchanged) |
| `sprk_searchindexname` (BU cascade) | client default | **dropped**: server-owned on a project (`SearchIndexed = true`, FR-38; the index follows the content type, FR-40) |
| `sprk_AI_Search_Index@odata.bind` (BU cascade) | client default | **dropped**: same rows |
| mapped fields (`applyFieldMappings`) | client mapping | **dropped**: the server maps fill-only from `mappingSource` (FR-02) |
| `sprk_issecure` | never sent (task 150) | still never sent; intent goes as `?secure=true` |
| `sprk_containerid` | never sent (task 076) | still never sent (provisioning's) |
| owner / owning team | never sent | still never sent (FR-09: the server decides, record-first) |

## Contract check (BFF code at the work-branch tip)

- `ChildRecordEndpoints.CreateAsync` (~:267-333): `POST /api/v1/child-records/{table}` with `mappingSourceEntity`/`mappingSourceId` (`MappingSourceOf`) and `secure` (`SecureIntentOf`; `SecureIntentTables` = `sprk_project`, `sprk_workassignment`; any other table → 400 `child_record.secure_intent_invalid`). 201 with `{id}` or `{id, warnings}`. Refusals: `RefusalResult` maps the delegation codes through `DelegationRuleFilter.DenyResult` (403 `sdap.access.deny.delegation_write_required` / `_share_required` / `_check_failed`); `child_record.not_found` → 404 uniform.
- `RecordTableRules.cs` `sprk_project` (:190-218): `MappingProtectedColumns`, `AutonumberColumn = sprk_projectnumber`, `ForPerson = sprk_assignedtointernal`, `TableServerOwnedColumns = RootServerOwnedColumns` (container, security BU, access permission), `SearchIndexed = true`, `OptionalReferenceLookups = sprk_projecttype_ref` (Office path only; the route does not pass it, so an unusable type refuses the create as the old Xrm path did, same reasoning as 021's "Decision").
- `InferMappingSourceTables` includes `sprk_project`, but the payload carries no filing key, so the source must be named explicitly — the wizard does.
- 049/049b/049c: secure intent creates into isolation; root create grants the maker's share inline and queues the rest; for a secure-from-creation project the grant waits for the securing, the reconciliation job finishes it. Hence no client sync.
- `ProjectMatterFiling` (PATCH, `fileProjectUnderMatter`): writes no owner; under a secure matter → 409 (`SecureMatterCode`, #1506).

## Decision: `rootCreate: true` (deviation from the brief)

The brief said "a child create, so use the child-records route with `mappingSource*`, not `rootCreate`". The route and `mappingSource*` are used; **`rootCreate: true` is still correct** for the error text: the create names no parent. Task 049's known limit ("an intent create filed under a secure record is refused"; "the Create Project wizard files a project only after it is created") keeps filing post-create (N:N `$ref` + PATCH), so the create body has no regarding pair, and a 404 `not_found` can only mean one of the user's picked values. The "filed under" sentence would be wrong. Tested ("the Project wizard shows the root-create sentence too, never 'filed under'").

**Open owner option (not done):** send the matter pair in the create for non-secure projects. That would give the record-first owner at create and refuse a project under a secure matter at create instead of a 409 on the later PATCH. It needs an owner decision and server-side N:N link writing (#2524, never implemented).

### Deviations

1. Two test files outside the wizard folder changed (`assignedAccessSync.test.ts`, `webApiCreateError.test.ts`), as in 021 deviation 2: both drive `ProjectService.createProject`. The Xrm.WebApi wizard section of `webApiCreateError.test.ts` was removed (no Xrm create wizard is left); its two form fixtures were kept.
2. `projectService.test.ts` converted: the cascade must never reach the body (the nav-prop metadata still offers `sprk_AI_Search_Index` to prove the bind is not written); `applyUserBuDefaults` not called; the static INV-5 helper test kept; the error path runs through the route.
3. The wizard's secure post-provision client sync removed (the server grants; see contract check).
4. The BU-resolve failure warning removed from the UI (its value is no longer read), as in 021.

## Tests (failing first)

`projectService.fieldMapping.test.ts` (19): body = user keys only; no owner/container/index/issecure key for both `isSecure` values; the route is called, never `dataService.createRecord`; undecorated and decorated data services make the same call with `?secure=true`; `mappingSource` in the query and the body identical with/without it; combined query `?mappingSourceEntity=sprk_matter&mappingSourceId=<id>&secure=true`; `applyFieldMappings` never called; no sync call; source scan (no `applyFieldMappings`, `FieldMappingService`, `applyUserBuDefaults`, `syncAssignedAccess`, `_dataService.createRecord`); server warnings reach the result; 400 → "Failed to create project record: Name is required."; 404 → root sentence; delegation `share_required` / `check_failed` sentences; fail-closed ×3 (no fetch, no base, empty base).

Fail-then-pass (the 22 suites touching ProjectService / the wizard / the two shared test files):
- RED, with HEAD `projectService.ts` and `CreateProjectWizard.tsx` copied back and the new tests: **41 failed / 339 passed** (7 of 22 suites failed).
- GREEN: **380 / 380**, 22 suites.

Full `Spaarke.UI.Components` jest: 6048 passed / 6 failed in 4 unrelated suites (`OntologyAdmin/Rules`, `EmailComposer/templatePicker`, `AccessGrantModal.inviteRetryEmailSent`, `DecisionWizard`). Re-run alone: **4/4 suites, 107/107 pass** — timing flakes under the full parallel run (81-134 s suite times).

## Verification

- `tsc --noEmit` (Spaarke.UI.Components): exit 0.
- client-lint ratchet (`scripts/quality/client-lint`, after `npm ci` there): ok, no suppression change.
- Vite build `src/solutions/CreateProjectWizard` (Spaarke.Auth and Spaarke.SdapClient built first): `dist/index.html` 1,176.24 kB (318.99 kB gzip), exit 0. **Not deployed.**
- BFF: not touched, so no BFF build/test.

## §11

No new surface: no new service, endpoint, registration, package, column or file surface. `IBffCreateOptions` and `createRecordViaBffWithWarnings` already existed (021/049). Pure modification of existing surface.

## Steps left before deploy (deploy order)

1. BFF carrying the `sprk_project` route (task 020) and the `secure` parameter (task 049) on dev first. Without it every Project create from a rebuilt host fails (fail-closed, nothing written).
2. `sprk_createprojectwizard` via `code-page-deploy` (keep the rollback bundle).
3. Rebuild and deploy the other hosts that bundle the shared `ProjectService`: `SummarizeFilesWizard` (`sprk_summarizefileswizard`), SpaarkeAi / LegalWorkspace (`InAppWizardHost`, `sprk_spaarkeai`), and the `Spaarke.AI.Widgets` consumers. Until each is rebuilt it stays on its old bundled path.
4. Live checks: standard project (with and without a matter; mapping fill-only; number issued; owner; Assigned-To access granted by the server), secure project (created secure, provisioning `already_provisioned` → success, hold-back).
5. NFR-02 verdict in UAT (owner decision).

## Found in passing (not fixed; out of scope)

- The server does not write the `sprk_Project_Matter_nn` link on create (#2524); the client `$ref` (`CreateProjectWizard.tsx:67`) is still its only writer.
- A non-secure project filed under a secure matter after create gets 409 from `ProjectMatterFiling` (#1506) — pre-existing; see the open owner option above.
- `resolveUserBuDefaults` (`CreateProjectWizard.tsx:568-570`) is now a wasted round trip: nothing reads its value. Same follow-up as 021 (remove from both wizards and their hosts together).
- The wizard's no-BFF secure branch (`CreateProjectWizard.tsx:639`) and `SummarizeFilesDialog.tsx:580-589` can no longer be reached for a create: `createProject` fails closed first. Dead code, harmless.
- `associateProjectWithAccount` has an unused `_orgUrl` (`CreateProjectWizard.tsx:119`).
- Four UI.Components suites time out under the full parallel jest run (listed above), pass alone.

rework: none task 022
