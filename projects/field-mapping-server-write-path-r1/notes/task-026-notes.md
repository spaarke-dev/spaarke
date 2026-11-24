# Task 026: Create Work Assignment wizard on the server write path (FR-16), code part

> Rigor FULL. Branch `task/fm-026`, from `accd2a4d37`. **Code part only.** Not done here (main session, owner OK needed): the deploy of `sprk_createworkassignmentwizard` (and the SpaarkeAi bundle for the in-app host), the live checks (AC5). Nothing here was run against dev, Dataverse or Azure.

## What changed

| File (under `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/`) | Change |
|---|---|
| `workAssignmentService.ts` | Deleted the `applyFieldMappings` import, call and comment. Deleted the BU cascade (`resolveUserBuDefaults` / `applyUserBuDefaults` / the `sprk_ai_search_index` bind) and the now-unused `_getCurrentUserId` and `getXrmUserId` import. The work-assignment create names the related record as `mappingSource` (below). The follow-on event is created with `createRecordViaBffWithWarnings(..., 'sprk_event', entity, { mappingSource: { entity: 'sprk_workassignment', id: workAssignmentId } })` instead of `this._dataService.createRecord`; `createFollowOnEvent` also returns `warnings` (the route's). Six `findNavProp` hints changed to full logical names (below). |
| `WorkAssignmentWizardDialog.tsx` | After the follow-on event succeeds, its route warnings are pushed into the success dialog's `warnings` (a 4-line addition; without it they would be dropped). |
| `__tests__/workAssignmentService.payload.test.ts` | New, 19 tests (below). |
| `__tests__/workAssignmentService.cascade.test.ts` | Converted (deviation 1). |
| `__tests__/workAssignmentService.followOnAssignee.test.ts` | The route matcher now allows the mapping-source query string. |
| `scripts/quality/client-lint/eslint-suppressions.json` | One count: `workAssignmentService.ts` `no-restricted-syntax` 3 to 2 (deviation 3). |

Hosts: **no change** to `src/solutions/CreateWorkAssignmentWizard/src/main.tsx` (passes `authenticatedFetch` and `resolvedBffBaseUrl`, :118-119) or `InAppWizardHost.tsx` (`common` carries `bffBaseUrl`; `authenticatedFetch` passed). `bffChildWriteAdapter.ts` and the shared fake are not edited.

## Key inventory (POML step 1)

### Work assignment (`createWorkAssignment`; `sprk_workassignment` row: Root, `SearchIndexed`, `TableServerOwnedColumns = RootServerOwnedColumns`, `ForPerson` + `ServerDefaults` on `sprk_assignedtointernal` / `sprk_assignedto`)

| Key | Class | Registry / server | Now |
|---|---|---|---|
| `sprk_name` | user-entered | none | sent |
| `sprk_priority` | user's pick (the form defaults to Normal and shows it selected; 023's decision) | none | sent |
| `sprk_description`, `sprk_responseduedate` | user-entered, only when set | none | sent |
| `sprk_MatterType@odata.bind`, `sprk_PracticeArea@odata.bind` | user-picked | not owned, not mapping-protected | sent |
| `sprk_AssignedTo@odata.bind` | user-picked (task 038: blank sends nothing so the server default applies) | `ServerDefaults` (037) | sent when picked |
| Assign Work: `sprk_AssignedAttorney1`, `sprk_AssignedParalegal1`, `sprk_AssignedLawFirm1`, `sprk_AssignedLawFirmAttorney1` `@odata.bind` | user-picked | none | sent when picked |
| `sprk_Regarding<Parent>@odata.bind`, `sprk_RegardingRecordType@odata.bind`, `sprk_regardingrecordid` / `name` / `number` / `url` (`applyResolverFields`) | user-picked filing keys (ADR-024) | the 045f record-URL rule | sent (as 023 / 024 decided) |
| `sprk_searchindexname` (BU cascade) | **client default** | `SearchIndexed = true`: both index columns are the server's (FR-38, FR-40); a caller's value is dropped at step 2 | **dropped** |
| `sprk_AI_Search_Index@odata.bind` (BU `searchIndexId`) | **client default** | same | **dropped** |
| `sprk_containerid` | (already removed by task 076) | `RootServerOwnedColumns` | stays unwritten |
| every mapped field (`applyFieldMappings`) | **client default** | server mapping step (FR-02, fill-only; `MappingProtectedColumns`) | **dropped** (server) |
| owner / owning team / impersonation / secure flag / creator person | never sent | `ResolveOwnerAsync` (FR-09), secure-intent query | not sent (asserted) |

No other client default existed in this file. The wizard sets no `statecode` / `statuscode`.

### Follow-on event (`createFollowOnEvent`; `sprk_event` row: Child, `SearchIndexed`, autonumber `sprk_eventnumber` mapping-protected, `ForPerson` on `sprk_assignedto`)

| Key | Class | Now |
|---|---|---|
| `sprk_eventname` | user-entered | sent |
| `sprk_priority` | user's pick (the step shows it selected) | sent |
| `sprk_description`, `sprk_duedate`, `sprk_finalduedate` | user-entered, only when set | sent |
| `sprk_AssignedTo@odata.bind` | user-picked, only when picked | sent |
| `sprk_RegardingWorkAssignment@odata.bind` | filing key (the event is filed under the new work assignment) | sent |
| mapped fields | server | none sent (the event never had a client mapping call) |

No client default on the event. Nothing dropped.

## mappingSource answer

`ChildRecordEndpoints.InferMappingSourceTables` holds only `sprk_matter` and `sprk_project` (its comment says `sprk_event` "joins with task 026"; the brief for this task says not to change the BFF, so it did not). A create of `sprk_workassignment` or `sprk_event` with no `mappingSource` asks for no mapping, silently. So:

- **Work assignment:** `{ entity: refMapping.refEntity, id: form.recordId }`, the record the wizard files it under (matter, project, invoice or event). It is the same record the old client mapping read. No related record (`assignWithoutRecord`): no query string, no mapping.
- **Follow-on event:** `{ entity: 'sprk_workassignment', id: <the new work assignment> }` (its filed-under parent, FR-03).

Both go in the query string only (`?mappingSourceEntity=&mappingSourceId=`, lower-case, braces stripped), never in the body.

## Completion order (POML escalation trigger): not hit

`createFollowOnEvent` is still called by the dialog after the work assignment and after the optional Send Email, and before the success dialog. It was already on the BFF route (`withBffChildWrites` routed `sprk_event`); only the call site changed. Its failure handling is unchanged (a returned warning, "create manually"). Asserted: `the event is created after the work assignment`.

## Lookup binding by fragment (POML step 5)

`findNavProp` returns the single relationship to an entity as is; with several it matches the exact column, else the **first column containing the fragment**, in Dataverse's metadata order. `sprk_workassignment` has Attorney 1/2, Paralegal 1/2, Law Firm 1/2, Law Firm Attorney 1 and several `assignedto*` contacts (`docs/data-model/sprk_workassignment.md`). The wizard has ONE of each, so each is the primary slot (`1`; `sprk_matter-related-tables.md` calls `sprk_assignedattorney1` "primary attorney", `...paralegal1` "primary paralegal").

| Call | Fragment before | Full name now | Why it was wrong |
|---|---|---|---|
| Assign Work attorney | `attorney` | `sprk_assignedattorney1` | also contained in `sprk_assignedattorney2` and `sprk_assignedlawfirmattorney1` |
| Assign Work paralegal | `paralegal` | `sprk_assignedparalegal1` | also `sprk_assignedparalegal2` |
| Assign Work law firm | `lawfirm` | `sprk_assignedlawfirm1` | also `sprk_assignedlawfirm2` (and, for contacts, the law-firm attorney) |
| Assign Work law-firm attorney | `lawfirmattorney` | `sprk_assignedlawfirmattorney1` | unique today; passed in full for the same reason |
| Matter type | `mattertype` | `sprk_mattertype` | one relationship to the type today; full name for safety |
| Practice area | `practicearea` | `sprk_practicearea` | same |
| Follow-on event filing (`sprk_event`) | `workassignment` | `sprk_regardingworkassignment` | one relationship today; a second work-assignment lookup would bind whichever is listed first |

Left unchanged (not `bindLookup` fragments; resolved already by exact column): `sprk_assignedto` (task 038, `toNavPropMap`), the event's `sprk_assignedto` (task 097), `sprk_document` parent (task 162). The matter / project / invoice / event regarding hints (`matter`, `project`, `invoice`, `event`) go through `applyResolverFields` with a referenced entity that has one lookup on `sprk_workassignment` each, so no look-alike exists.

**Genuinely ambiguous:** none. One judgement call: "primary slot = `1`" for attorney, paralegal and law firm is inferred (one wizard field, `1` is the documented primary). If the owner wants the wizard to offer a second attorney, that is a form change.

## Tests

- **RED** (HEAD `workAssignmentService.ts` put back, new and converted tests in place, `CreateWorkAssignmentWizard` folder, 12 suites): **18 failed, 61 passed**: `workAssignmentService.payload` 16 of 19 fail (the 3 that pass on the old code: the minimal work assignment body, the no-related-record case, and the law-firm-attorney bind, which was already unique), `workAssignmentService.cascade` 2. The new file was restored afterwards: the committed one is mine.
- **GREEN:** `CreateWorkAssignmentWizard` folder + `WizardFollowOns`, 19 suites: **139 passed.**

`workAssignmentService.payload.test.ts` (19 tests): the work assignment body is the exact key set (name, priority, description, due date, matter type, practice area, assigned to, the four Assign Work binds, the six regarding keys) with no server-owned key; a minimal create is exactly `{ sprk_name, sprk_priority }`; the BU is never read and `dataService.createRecord` is never called; `applyFieldMappings` (mocked module) never called and no `/field-mappings/` request; a source scan for `applyFieldMappings` / `FieldMappingService`; the work-assignment URL carries `mappingSourceEntity` / `mappingSourceId` (matter, project, invoice, event) and none without a related record; the event URL names the new work assignment; the event body is an exact key set (full and minimal); route warnings reach the caller and a refusal is the existing warning; the completion order; and the four look-alike binds plus the event filing bind, each with a look-alike column listed first.

### Deviations

1. **`workAssignmentService.cascade.test.ts` converted, not kept.** It asserted `sprk_searchindexname` IS in the payload (FR-WIZ-01). That is a client default the server drops (FR-38) and the POML forbids ("the wizard payload contains no key the user did not enter"). Same treatment as 021: same scenarios, now asserting the cascade columns never reach the body; the first also asserts the BU is not read.
2. **`WorkAssignmentWizardDialog.tsx` changed** (POML: "only if needed"): needed to surface the event's route warnings. `createFollowOnEvent` returns `warnings?: string[]` besides `warning`; no test mocks it.
3. **`eslint-suppressions.json` changed** (one count, 3 to 2 `no-restricted-syntax` for this file): removing the cascade's catch / warn removed one baselined violation, and the client-lint check fails on a suppression that no longer occurs. Pruned with `client-lint prune --package ui-components` and only that line kept; the file's trailing newline restored.
4. **The new test file also covers the four non-matter related-record types** and the event filing bind; not asked for, a few lines each.

## Verification

- `npx tsc --noEmit -p .` in `Spaarke.UI.Components`: 0 errors. (`Spaarke.Auth`, `Spaarke.SdapClient` built and `npm install --legacy-peer-deps` run first, fresh worktree.)
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok.
- `grep applyFieldMappings workAssignmentService.ts`: none.
- The host build (`src/solutions/CreateWorkAssignmentWizard`, vite) was **not run** in this part.

## Left for the live part (main session, needs owner OK)

1. **Deploy** `sprk_createworkassignmentwizard` via `code-page-deploy`; note the previous bundle for rollback. The route already supports `sprk_workassignment` and `sprk_event`. Mapped fields appear on first load only if the BFF build on dev carries the mapping step (tasks 013-020).
2. **Live checks (AC5):** a work assignment created under a parent (matter, project) with an active `{parent → sprk_workassignment}` profile, opened at once (mapped fields present); a user value kept over a mapped one; owner = the parent's owning team (FR-09); a secure matter parent (created into isolation); no parent (no mapping, no warning); the Assign Work picks land in the primary slots (Attorney 1, Paralegal 1, Law Firm 1, Law Firm Attorney 1); the follow-on Event is created, filed under the work assignment, with a mapped value from it if a `{work assignment → event}` profile is active; the Assigned To pick; files and Send Email; dark mode.
3. **Other bundles change only when rebuilt and deployed** (the shared library is compiled into each host): the Console's in-app Work Assignment wizard (`InAppWizardHost`, a SpaarkeAi rebuild) and the Report Card wizard's use of `WorkAssignmentWizardDialog`.

## Follow-ups (not done; not in scope)

- `ChildRecordEndpoints.InferMappingSourceTables` comment says `sprk_event` "joins with task 026". It does not: the event names its source explicitly (this task and 023), so the set is unchanged. The comment is stale; a BFF edit, not made here.
- Dead code after the cascade removal: the host `resolveUserBuDefaults` plumbing for this wizard, if any, now feeds nothing (as 021 noted for the Matter wizard). Not looked for.
- Stale comments in `CreateReportCardWizard.tsx` (task 027) that describe the client mapping.

## Main session: review, merge, gates (2026-11-24)

- **Review of 9b60222577: no finding.** Checked against dev metadata (read-only):
  - every bind names a real column: `sprk_assignedattorney1`, `sprk_assignedparalegal1`, `sprk_assignedlawfirm1`, `sprk_assignedlawfirmattorney1`, `sprk_mattertype`, `sprk_practicearea` on `sprk_workassignment`; `sprk_regardingworkassignment` is the event's only lookup to `sprk_workassignment`;
  - the "slot 1" choice matches the active Matter-to-Work-Assignment profile (7b147316…), which maps attorney 1/2, paralegal 1/2, law firm 1/2, Assigned To External and Assigned To Internal one to one; in the old fragment behaviour `lawfirm` already resolved to law firm 1, the first column in metadata order;
  - the adapter cleans the source id and lower-cases the entity before the query string.
- **Behaviour note:** dev has no active `{work assignment → event}` profile, so the follow-on event gets no mapped value there today. That is unchanged: before 026 the event named no source and got none either. The "Matter to Event" profile does not apply, because the event's source is the work assignment.
- **Merged** into the work branch with `--no-ff`.
- **Gates in the main worktree:**
  - jest (`CreateWorkAssignmentWizard` and `WizardFollowOns`): 19 suites, 139 passed;
  - `tsc --noEmit`: 0 errors;
  - client-lint: `ui-components` ok. Eight other packages fail locally, on files this task does not touch (for example `EmailViewSelector.tsx`, last changed 4 months ago), while master's client-lint run is green (72df391fa7). This worktree's local install is behind for those packages. The PR's CI run decides.
- **ADR check:**
  - ADR-024: the polymorphic resolver stays;
  - ADR-012: no new shared surface (only an optional `warnings` on an existing return type);
  - ADR-002: no plugin, the server owns mapping.
- **Dead code:** the cascade removal leaves none. The Work Assignment host does not call `resolveUserBuDefaults`.
- **Stale comment** (`ChildRecordEndpoints.InferMappingSourceTables`, sprk_event "joins with task 026"): wording only. It is fixed the next time that BFF file is touched, like 045e K8, and gets no PR of its own.

## On master (2026-11-24)

- **#3049 merged** as dc33f14d35 (a merge commit).
- **One Tier 1 flake on the way:**
  - What failed: `DataGrid.persistedSettings` "Clear all also clears the column filters" (`Unable to find role="textbox"`). It failed on attempt 1 and passed on the re-run, on the same commit.
  - Cause: the test's `applyTextFilter` helper waited a fixed 120 ms for the filter popover.
  - Tracking: filed as #3064. Already fixed on master by spa-r4's #3055 (8dd44cf528), which waits for the popover's textbox and Apply button and reopens it up to 3 times. #3064 is closed against #3055.
  - Nothing of this project's needed changing.

## Deploy to dev (2026-11-24 15:37Z, owner go in this session)

- **Built from:** master d0453e583a (contains #3049), in a temporary worktree (`C:\wt026`, since removed): SharedLibs, then `src/solutions/CreateWorkAssignmentWizard` (`npm install --legacy-peer-deps`, clean `dist`, `npm run build`).
- **Bundle checks:**
  - it has `mappingSourceEntity`, `sprk_assignedattorney1`, `sprk_assignedlawfirm1` and `sprk_regardingworkassignment`;
  - it has neither `applyFieldMappings` nor the `/field-mappings/` route.
- **Deployed:** `sprk_createworkassignmentwizard` (84bda2e0-d523-f111-88b4-7ced8d1dc988) via `Deploy-WebResourceInline.ps1`, then published.
  - Read-back SHA-256 `E8DE9527…4B60A1` equals the build.
  - The web resource is in SpaarkeMaster.
- **Rollback:** the previous bundle was saved before the deploy. It was 1,506,031 bytes, SHA-256 `02A824E9…AB45F46`, modified 2026-11-23 21:35Z, and still had the client mapping. Saved in this session's scratchpad as `wr026/rollback-sprk_createworkassignmentwizard.html`. Redeploying it is the rollback.
- **Next:** the live check ("Left for the live part" item 2).

## Live check on dev (AC5), 2026-11-24 15:46–15:52Z (owner go in this session)

- **How it was run:** in the owner's signed-in Chrome, in this session's own tab group. The wizard was opened as a dialog via `Xrm.Navigation.navigateTo` (`sprk_createworkassignmentwizard`, the workspace launcher's call), because the Matter form has no Create Work Assignment command.
- **How rows were checked:** read back with the Web API as an admin, with request timings from App Insights. The read script and logs are in this session's scratchpad (`live026/`).
- **Mapping profile:** "Matter to Work Assignment (Attorney Matrix)" (7b147316…), already active on dev. No temporary profile was needed.

| Check | A: under MAT-000110 (`4dbaa0c7-79ec-4c9f-8b3f-6a8eb2479adc`), Attorney = Jane Smith, Law Firm = Morrison Foerster LLP, Assigned To blank, follow-on event | B: under secure PAT-908068 (`adff04f4-157e-405a-9d15-2bc76b2f976a`), follow-on event | C: no parent (`9891e505-b71c-4c51-ab92-0270ce9e226b`) |
|---|---|---|---|
| Created on the route, by the BFF | **PASS**: 201 with `mappingSourceEntity=sprk_matter`; created and modified by `# mi-bff-api-dev`; one write | **PASS**: 201 with the matter as source | **PASS**: no query string |
| Mapped values at first load | **PASS**: Attorney 2 (Jessica Broyles), Paralegal 1 (Eyal Iffergan), Paralegal 2, Assigned To Internal (Test User 1) and Assigned To External all come from the matter; Assigned To = Test User 1 (the server default from Internal) | n/a: the matter has no person values (warning `copied_default_source_unset`, as expected) | **PASS**: none, and no warning |
| User value kept (fill-only) | **PASS**: Attorney 1 = Jane Smith (the wizard's pick), not the matter's Test User 1 | n/a | n/a |
| Primary slots | **PASS**: Attorney → `sprk_assignedattorney1`, Law Firm → `sprk_assignedlawfirm1` | n/a | n/a |
| Owner (FR-09) | **PASS**: Spaarke Business Unit 1, the matter's owner team | **PASS**: Secure Record Owners, `sprk_issecure` = true (created into isolation) | **PASS**: Spaarke, the maker's business-unit team. The maker's account is homed in the root business unit, so a record with no parent takes the root team (the #3047 shape; UAC-r2 confirmed it and left this row as it is) |
| Follow-on event | Created under the work assignment, owner = the work assignment's team. **DEFECT (fixed):** regarding pair blank | Created, owner Secure Record Owners. Same defect | n/a |
| Duration (server) | 4.7 s | 24.6 s: secure provisioning (SPE container 5.5 s, about 90 sharing calls 9.3 s). This is UAC-r2's secure path; the latency verdict is UAT's (owner) | about 2 s |

**Defect found and fixed in scope (2156330275):**
- **What:** the follow-on event bound only `sprk_regardingworkassignment`, so its regarding pair and display columns were empty and the event showed no Regarding.
- **Not new:** every follow-on event on dev back to 2026-03 lacks the pair or has only part of it. It is not a 026 regression.
- **Fix:** `createFollowOnEvent` now files the event through `applyResolverFields`, the same way the work assignment is filed (ADR-024). The dialog passes the work assignment's name as the fallback.
- **Tests:** the payload tests assert the pair keys. 3 tests fail on the old code. Jest 19 suites / 140 tests pass, `tsc` 0, client-lint `ui-components` ok.
- **Left:** a PR to master, a redeploy of `sprk_createworkassignmentwizard`, and a re-check that a new follow-on event shows its work assignment as Regarding. Each needs the owner's go.

**Not covered:**
- Send Email and file uploads were not exercised; 026 did not change them.
- The Console's in-app wizard and the Report Card wizard need their own bundles rebuilt.
- Dark mode is left for the owner, as in 023 and 024.

**Rows left on dev:**
- work assignments A, B and C (above);
- events `a9b1aaee-d5dd-473a-a021-cdf5f3a864b6` and `d5d8d35b-0d64-48c3-8adf-62bcc3045bf7`;
- B's SPE container "Secure Work Assignment – FMSWP-026 live check B secure".
