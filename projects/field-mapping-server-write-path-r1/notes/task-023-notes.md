# Task 023: Create Event wizard on the server write path (FR-16) — code part

> Rigor FULL. Branch `task/fm-023`, from `2dc873ac65` (the work branch tip). **Code part only.** Not done here (main session, owner OK needed): the deploy of `sprk_createeventwizard` and the live checks. Nothing in this branch has been run against dev, Dataverse or Azure.

## What changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` | Deleted the `applyFieldMappings` call and import. Deleted the BU cascade block (`resolveUserBuDefaults` / `applyUserBuDefaults` writing `sprk_searchindexname`, and the `sprk_aisearchindex` nav-prop bind) with the helpers only it used (`_tryGetCurrentUserId`, `_toWebApiLike`, the `EntityCreationService`, `IWebApiLike`, `getXrmUserId` imports). The create now goes through `createRecordViaBffWithWarnings(authenticatedFetch, bffBaseUrl, 'sprk_event', entity, { mappingSource })` (the shape 021 used), not `withBffChildWrites(...).createRecord`; server warnings are pushed into `warnings`. A service built without `authenticatedFetch`/`bffBaseUrl` returns a failed result (fails closed, as `withBffChildWrites` did). The not-found wording is the root-create sentence when the event has no regarding parent. |
| `.../CreateEventWizard/CreateEventWizard.tsx` | Comment only (the "drive the Field Mapping Framework engine" comment above `new EventService(...)`). |
| `.../CreateEventWizard/__tests__/eventService.fieldMapping.test.ts` | Rewritten (below). |
| `.../CreateEventWizard/__tests__/eventService.cascade.test.ts` | Converted (deviation 1). |
| `.../src/__mocks__/bffChildWriteFake.ts` | The create route regex now accepts an optional query string (deviation 2). |
| `scripts/quality/client-lint/eslint-suppressions.json` | Pruned: `eventService.ts` `no-restricted-syntax` 1 → 0 (the ratchet improved; `check` fails on an unpruned suppression). |

Host (`src/solutions/CreateEventWizard/src/main.tsx`): **no change.** It passes `bffBaseUrl` through to the shared wizard, which already has `authFetch`.

`createEvent`'s third parameter (`options.getCurrentUserId`) is **left in place, now unused** (`_options`). No production caller passes it, but three test files do (`eventService.assignedTo`, `.resolver`, `.cascade`), and removing it would change them for no gain. It can go with a later cleanup.

## Client-default inventory (POML step 1)

Every key `createEvent` put on `entity` before this task, and what happens to it (Event row: `RecordTableRules.cs` ~:295):

| Key | Class | Registry row (`sprk_event`) | Now |
|---|---|---|---|
| `sprk_eventname` | user-entered | — | sent |
| `sprk_priority` | user-entered (the form's Priority, default Normal) | — | sent |
| `sprk_description`, `sprk_duedate` | user-entered | — | sent when non-blank |
| `sprk_EventType@odata.bind` | user-picked | — | sent |
| `<assignedto navprop>@odata.bind` | user-picked (only when the user chose a person) | `ForPerson` + `ServerDefaults`: a blank picker gets the triggering person's contact, else the regarding parent's responsible internal contact (`AssignedToDefaults`) | sent when picked; a blank picker still writes nothing (the server fills it) |
| `sprk_Regarding<Parent>@odata.bind` + `sprk_RegardingRecordType@odata.bind` + `sprk_regardingrecordid` / `name` / `number` / `url` (`applyResolverFields`) | user-picked filing keys (ADR-024) | `RegardingPairTables` includes `sprk_event`; see "Regarding-pair finding" | sent |
| `sprk_searchindexname` (BU cascade) | **client default** | `SearchIndexed = true`: both index columns are the server's; a caller's value is dropped at step 2 (FR-38/FR-40) | **dropped** |
| `sprk_AI_Search_Index@odata.bind` (BU `searchIndexId`) | **client default** | same | **dropped** |
| `sprk_eventnumber` | never sent | `AutonumberColumn`, `MappingProtectedColumns` (`EVT-{SEQNUM:6}`, #2515) | still never sent (asserted) |
| `sprk_containerid` | never sent (task 076) | `TableServerOwnedColumns` | still never sent |
| every mapped field (`applyFieldMappings`) | **client default** | server mapping step (FR-02, fill-only) | **dropped** (server) |
| owner / owning team / impersonation | never sent | `ResolveOwnerAsync` (FR-09) | not sent (asserted) |

**Result: every client default is either a server rule in the Event row or dropped. The Event row has no required column, and the wizard never defaulted the assignee on the client, so removing the client mapping leaves no field empty that an Event rule needs (escalation trigger 7 does not fire).** No BFF edit is needed or made for the payload.

## Regarding-pair finding (POML step 3)

What the route does with a client-sent regarding pair and display fields on create (`ChildRecordEndpoints.CreateAsync` → `RecordCreationService.CreateAsync`):

- **Keeps them as sent.** `sprk_event` is in `OwnedChildWrite.RegardingPairTables`. Step 4½ (task 045a) only fills or clears a pair that names a TYPE with NO id (`IsTypeOnlyRegardingPair`); the wizard always sends type and id together, so nothing is rewritten.
- **Task 045f record-URL rule:** `RegardingRecordUrlRuleApplies('sprk_event')` is true (a pair table that is not an invoice-family table). A sent `sprk_regardingrecordurl` must name the record the pair names (`etn=` and `id=` in the query string, read by `TryReadRecordUrl`), or the create is refused 400 `RecordUrlMismatchCode`. An admitted URL is rewritten to its canonical form. The wizard's `buildRecordUrl` writes `…/main.aspx?…&etn=<parent>&id=<id>`, which parses and matches (asserted in the new test: `etn=sprk_matter`, `id=<the matter id>`).
- **Name and number** (`sprk_regardingrecordname`, `sprk_regardingrecordnumber`) are not checked or overwritten on a create; they are stored as sent.
- `workAssignmentService.ts` sends the same pair and display fields (`applyResolverFields`, ~:444) through the same route and is live-verified.

Decision: **keep sending them.** The route neither refuses nor overwrites a wizard-shaped pair, and the pair is the filing key that decides the owner and the regarding inheritance.

## Warnings finding (POML step 4)

`withBffChildWrites(...).createRecord` goes through `createChildRecordViaBff`, which is `(await createRecordViaBffWithWarnings(...)).id`: **it drops the route's `warnings`.** Switched to `createRecordViaBffWithWarnings` as 021 did. `eventService.ts` pushes `created.warnings` into `warnings`, so a create with a dropped mapped value returns `success: true` with a warning, which is how the wizards already show it. `CreateEventWizard.tsx` copies `result.warnings` into its success dialog; the Matter and Project wizards push `eventResult.warnings` into theirs. No adapter was edited.

(There is no `partial` status on the Event result: `ICreateEventResult` is `success` plus `warnings`, and the three callers already treat a non-empty `warnings` as the warning state.)

## mappingSource answer (POML step 5)

**Deviation from the brief's premise: the server does NOT infer the source for `sprk_event`, so the wizard names it explicitly.** `ChildRecordEndpoints.InferMappingSourceTables` holds only `sprk_matter` and `sprk_project` (its comment: "Tasks 023-029 add their table HERE"). For any other table, a create with no `mappingSource` asks for **no mapping at all** (`MappingSourceOf` returns null). Dropping `applyFieldMappings` without naming a source would therefore silently stop mapping for events.

The three callers all file the event under the same record that is the intended mapping source, so the explicit source is that record:

| Caller | Regarding | mappingSource sent |
|---|---|---|
| `CreateEventWizard.tsx` ~:422 | `context.association` (entity + id merged by `mergeEventRegardingFromAssociation`) | the association; none when the step is skipped |
| `CreateMatterWizard.tsx` ~:656 | the new matter (`'sprk_matter'`, `matterId`) | the matter |
| `CreateProjectWizard.tsx` ~:746 | the new project (`'sprk_project'`, `projectId`) | the project |

`createEvent` sends `{ entity: regardingEntityName, id: regardingRecordId }` when both are set, in the query string only (`?mappingSourceEntity=&mappingSourceId=`; the adapter lower-cases and strips braces). The body is unchanged by it (tested). No parent: no query string, no mapping, no warning.

Why not add `sprk_event` to `InferMappingSourceTables` instead: it is a BFF edit outside this task's brief, and it would also start inferred mapping for every other caller of the event route that names no source (not inventoried here). If the owner prefers inference, that is a one-line BFF change plus its seam test, and the explicit source here stays correct (same record).

## Tests

- **RED**, the HEAD `eventService.ts` put back with the new/converted tests in place (CreateEventWizard folder, 9 suites): **12 failed, 46 passed.** The two failing suites are `eventService.fieldMapping` and `eventService.cascade`; the old file still maps on the client, writes the BU index, drops route warnings and sends no `mappingSource`. The restored file is the one committed.
- **GREEN:** CreateEventWizard folder, 9 suites, **58 passed**. CreateMatterWizard + CreateProjectWizard + `webApiCreateError` + `utils/adapters` (38 suites): **505 passed.** All 13 suites that import `bffChildWriteFake`: **170 passed.**
- The full `Spaarke.UI.Components` jest run was not repeated (the change is local; the shared mock's users all pass).

`eventService.fieldMapping.test.ts` (rewritten, 10 tests): (1) the body holds exactly the user's keys plus the six regarding keys (name, priority, description, due date, event type, assigned to), and the record URL names the pair's record; (2) a minimal event sends only name and priority; (3) no `sprk_eventnumber`, owner / owning / impersonation, container or index key or value even when a BU with a container and index is resolvable, and the BU is never read; (4) `dataService.createRecord` is never called, the POST goes to `/api/v1/child-records/sprk_event` with no query without a parent; (5) the parent is the explicit source in the query string (lower-case, braces stripped) and the body holds nothing but the user's and regarding keys; (6) `applyFieldMappings` (mocked module) is never called and no `/field-mappings/` request is made; (7) a source scan: `eventService.ts` contains neither `applyFieldMappings` nor `FieldMappingService` (nor the BU helpers nor `withBffChildWrites`); (8) route warnings reach the result and the event stays created; (9) a refusal is a hard error; (10) a service with no BFF connection creates nothing and never calls `dataService.createRecord`.

`eventService.cascade.test.ts` (converted, 8 tests): the same scenarios as before (full BU defaults, with a regarding record, null index name, null container, no user, a BU read that would throw, no BU, the injected `getCurrentUserId`), now asserting the cascade columns never reach the body and the BU is never read.

`eventService.assignedTo.test.ts`, `eventService.resolver.test.ts` and the component tests are unchanged and pass.

### Deviations

1. **`eventService.cascade.test.ts` could not stay as written.** It asserted that `sprk_searchindexname` IS in the payload (FR-WIZ-05). That is a client default the server drops (FR-38), and the POML's rule ("a default missing there is added to the registry or dropped, never kept on the client") and acceptance criterion ("the wizard payload contains no key the user did not enter") forbid it. Converted as 021 converted the matter cascade test; the scenarios are the same, the meaning is the opposite.
2. **One shared test helper outside the wizard folder changed:** `src/__mocks__/bffChildWriteFake.ts` matched the create route only at the end of the URL. The Event create now carries `?mappingSourceEntity=…` whenever the event has a parent, so the fake answered 404 and four existing Event tests with a regarding parent failed. The regex now allows an optional query string. No assertion in any suite changed; the 13 suites that use the fake pass.
3. **`mappingSource` is sent explicitly** although the brief expected server inference (see above). It is the only way the mapping survives on the current BFF.
4. `eslint-suppressions.json` is edited (a prune), outside the named folders. `client-lint check --package ui-components` passes after it.

## Verification

- `npm run build` in `Spaarke.UI.Components` (tsc): clean. `Spaarke.Auth` and `Spaarke.SdapClient` built first (fresh worktree).
- `npm run build` in `src/solutions/CreateEventWizard` (vite): built, `dist/index.html` 1,073.85 kB (292 kB gzip). `dist/` is gitignored; **not deployed**.
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok (after the prune above).
- `grep -n applyFieldMappings` on `eventService.ts`: none.

## Left for the live part (main session, needs owner OK)

1. **Deploy** `sprk_createeventwizard` via `code-page-deploy`. Note the previous bundle for rollback. The route already supports `sprk_event` (it was a child-records table before this project), so there is no BFF ordering constraint, but mapped fields appear on first load only if the BFF build on dev carries the project's mapping step (tasks 013-020).
2. **Live checks:** create under a parent with an active `{parent → sprk_event}` profile and open the event at once (mapped fields present); a user value kept over a mapped one (fill-only); owner = the owning team (FR-09); the event number is `EVT-nnnnnn` from the autonumber; blank assignee gets the server default; files attached (upload and index); dark mode.
3. **The Matter and Project wizards' follow-on events change only when those bundles are rebuilt and deployed** (the shared library is compiled into each host): `sprk_creatematterwizard` and `sprk_createprojectwizard`. Their follow-on event keeps the old client mapping and BU cascade until then.
4. **The Spaarke Console's in-app Event wizard** (`InAppWizardHost` → shared `CreateEventWizard`) moves only with a SpaarkeAi rebuild and deploy.
5. Decision for the owner (optional): add `sprk_event` to `ChildRecordEndpoints.InferMappingSourceTables` (see "mappingSource answer").

## Follow-ups (not done; not in scope)

- Stale comments in `CreateMatterWizard.tsx` (~:643) and `CreateProjectWizard.tsx` (~:733) still say the follow-on `EventService` "drives the Field Mapping Framework engine". Not edited (another wizard's files); fixed when those wizards' bundles are next touched.
- `createEvent`'s unused `_options` parameter (above).

## Review (Opus adversarial pass, 2026-11-16) and fix round 1

One full pass (sonnet-tier task, no auth/security tag). No F1. The reviewer ran 62 jest suites (656 passed), `tsc --noEmit` and `client-lint check`: clean.

| # | Finding | Class | Outcome |
|---|---|---|---|
| 1 | `ChildRecordEndpoints.InferMappingSourceTables` says tasks 023-029 add their table there; 023 sends an explicit source instead, and task 026 plans a work-assignment follow-on event through the route with no source | F2 | Decided in the main session: `sprk_event` joins the inference set in **task 026**. The To Do wizard creates `sprk_todo`, not events; the only other browser create of `sprk_event` is the Work Assignment wizard's follow-on event (`workAssignmentService.ts` ~:683, `dataService.createRecord`, maps nothing and names no source today). Adding the table now would start mapping that follow-on event before 026 moves and live-checks it. The route comment (`ChildRecordEndpoints.InferMappingSourceTables`, comment only) and 026 step 4 now say so (work branch). The explicit source stays correct either way. |
| 2 | Comments in `CreateMatterWizard.tsx` / `CreateProjectWizard.tsx` and `SPAARKE-FIELD-MAPPING-FRAMEWORK.md` (:51, :197) still describe client mapping in `EventService` | F2, out of scope (other wizards' files; docs task 062) | Filed: #2874, DI-031 |
| 3 | The `rootCreate` wording was untested (the refusal test sent no reason code) | F2 | Fixed: two tests send `child_record.not_found` and assert the root-create sentence with no parent and the filed-under reading with one. Mutation check: flag inverted, 2 fail; flag always on, 1 fails. |
| 4 | `sprk_priority` is always sent (form default Normal), so a parent → event priority rule can never fill it; the old client engine overwrote it | K (owner informed) | The form shows Normal selected and the user submits it, so it is the user's value and fill-only (FR-02) keeps it; the old overwrite also overwrote a deliberate choice. No profile in the repo maps priority. Owner told; changing it needs a "priority not chosen" state in the form. |
| 5 | A service built on an already-wrapped data service with no fetch now refuses | K2 | No real caller does this (all four construction sites pass both props); fails closed. |
| 6 | Unused `_options` parameter kept | K4 | Cleanup later. |

FR-16's per-wizard integration test is the live check below (criterion 5, not met until the deploy).

## Live check on dev (owner go 2026-11-17; waits for the `sprk_createeventwizard` deploy)

Merged to master as #2875 (merge 3df5ee9e3f). Deploy asked of UAC-r2. The dev web resource was last modified 2026-11-15 09:48Z (the bundle this deploy replaces).

Setup, read from dev before the deploy:
- Active profile "Matter to Event (Attorney Matrix)" (`24dc0ed2-537b-f111-ab0e-7ced8ddc4a05`). Eight Copy rules, all lookups, same name on both sides: `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedtointernal`, `sprk_assignedtoexternal`.
- Parent: Matter PAT-176903 "Targeted Protein Degradation Patent Issuance" (`232852f8-a263-f111-ab0c-000d3a582930`). It has attorney 1 and 2, paralegal 1, law firm 1 and assigned-to-internal set. The other three mapped columns are empty on the parent, so they stay empty on the event.
- **Fill-only cannot be shown through this wizard.** Its only person field is Assigned To (`sprk_assignedto`), which no rule maps, so no wizard value can meet a mapped one. Fill-only stays proven by the server tests (FR-02); the live check confirms that a mapped column the user never touched is filled and that `sprk_assignedto` carries the user's pick.
- The Assigned To bind is chosen by `findNavProp(navProps, 'contact', 'assignedto')`. The exact-match test compares the hint with `sprk_assignedto` and never matches, so the first column containing "assignedto" wins, in metadata order. Wizard events on dev so far (e.g. EVT-000025) wrote `sprk_assignedto`. The live check confirms the column again.

Checks: mapped columns present at first load; `sprk_assignedto` = the picked contact and nothing else changed; owner = the Matter's owning team; `EVT-nnnnnn` autonumber; a second event with no assignee gets the server default (or stays blank, per the Event row of `RecordTableRules.cs`); dark mode.

### Result: PASS, 2026-11-23 23:11–23:14Z (dev; `sprk_createeventwizard` from master 3df5ee9e3f)

Run in the owner's signed-in Chrome, from the "Create Event" command on Matter PAT-176903 (`232852f8-a263-f111-ab0c-000d3a582930`). Each new row was read back with the Web API as an admin. Log: session scratchpad `live023/`.

| Check | Event A "FMSWP-023 live check A" (`e6a65599-be22-42e0-9f94-a8e744dab467`) | Event B "FMSWP-023 live check B" (`4290d1ce-d191-4165-ad8a-1cf8ed848d17`) |
|---|---|---|
| Mapped columns at first load | **PASS**: all eight equal the matter. Six set (attorney 1/2, paralegal 1, law firm 1/2, assigned-to-internal); paralegal 2 and assigned-to-external are empty on both | **PASS**, same eight |
| Created in one write by the BFF | **PASS**: `createdon` = `modifiedon`; created and modified by `# mi-bff-api-dev` | **PASS** |
| User's pick kept | **PASS**: `sprk_assignedto` = Jane Doe (picked in the wizard) | n/a: no pick; `sprk_assignedto` stays blank, as the Event row's server default applies to server writers only (`RecordTableRules.cs`) |
| Owner = the matter's owning team (FR-09) | **PASS**: Spaarke Business Unit 1, the same as the matter | **PASS** |
| Autonumber | **PASS**: EVT-000030 | **PASS**: EVT-000031 |
| Regarding | **PASS**: the matter, shown on the event form at first load | **PASS** |
| Console errors | none matching event, wizard, bff or mapping | none |

**Dark mode: left for the owner.** The page's `themeOption=darkmode` flag turns the form dark, but the wizard stays light. The browser holds the user's explicit Spaarke preference `spaarke-theme` = `light`, which wins by design (`themeStorage.getEffectiveDarkMode`, ADR-021). Switching it would also change the theme for another session working on the same origin, so the owner checks by setting the Spaarke theme to dark and opening the wizard.

Rows left on dev for later cleanup: events `e6a65599-be22-42e0-9f94-a8e744dab467` and `4290d1ce-d191-4165-ad8a-1cf8ed848d17`.
