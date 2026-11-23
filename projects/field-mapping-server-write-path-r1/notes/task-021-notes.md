# Task 021: Create Matter wizard on the server write path (FR-16) — code part

> Rigor FULL. Branch `task/fm-021`, from 5f443eaae0 (the work branch tip). **Code part only.** Not done here (owner OK needed, main session): the NFR-02 before/after p95 (steps 1 and 8), the deploy and the live check (step 7), and the UI tests. The BFF route for `sprk_matter` (task 020, #2551) is not yet on dev, so nothing in this branch has been exercised live.

## What changed

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts` | Deleted the matter-number generation (`{typeCode}-{random6}`, and the Matter Type code read that fed it). Deleted the `applyFieldMappings` call and import. Deleted the BU cascade write (`applyUserBuDefaults`: `sprk_searchindexname`) and the `sprk_ai_search_index` bind. The create now goes through `createRecordViaBffWithWarnings(authenticatedFetch, bffBaseUrl, 'sprk_matter', entity, { mappingSource })`, the shape `workAssignmentService.ts` uses. Server warnings are pushed into `warnings` (so a create with a dropped mapped value ends `partial` and the dialog shows them). |
| `.../CreateMatterWizard/__tests__/matterService.fieldMapping.test.ts` | Rewritten (below). |
| `.../CreateMatterWizard/__tests__/matterService.cascade.test.ts` | Converted (deviation 1). |
| `.../CreateMatterWizard/__tests__/matterService.navProp.test.ts` | Mechanical: the payload is now read from the POST body to the route instead of `dataService.createRecord`. Every payload assertion is unchanged. |
| `.../services/__tests__/assignedAccessSync.test.ts`, `.../utils/__tests__/webApiCreateError.test.ts` | Outside the wizard folder, but both drive `MatterService.createMatter` (deviation 2). |
| `scripts/quality/client-lint/eslint-suppressions.json` | Pruned: matterService.ts `no-unnecessary-condition` 3 → 1 and `no-restricted-syntax` 2 → 1 (the ratchet got better; `check` fails on an unpruned suppression). |

Host (`src/solutions/CreateMatterWizard/src/main.tsx`): **no change.** It does not construct a BFF-wrapped data service, and the service uses the `authenticatedFetch` and `bffBaseUrl` it is already given. The other hosts (`InAppWizardHost`, `CreateMatterWizardWidget`) pass the same two props, **but a host picks up the new path only when its own bundle is rebuilt and deployed** (the shared library is compiled into each host). The Spaarke Console's in-app wizard (`InAppWizardHost` -> the shared `CreateMatterWizard`, `src/solutions/SpaarkeAi/.../ThreePaneShell.tsx:960`) therefore stays on the old `Xrm.WebApi` path until the SpaarkeAi bundle is rebuilt and deployed. `resolveUserBuDefaults` is still wired in the code page, but since fix round 1 nothing reads `cascadeDefaults`: the matter's index columns are the server's, and the uploaded files are indexed without naming an index (the server's `FileIndexRequest` has no `searchIndexName`, FR-40).

Mapping source: the wizard sends the AssociateToStep selection as the explicit `mappingSource` (query string `?mappingSourceEntity=&mappingSourceId=`; the adapter strips braces and lower-cases). It has to: the matter payload carries no filing key (no regarding pair, no typed parent lookup) from which the server could infer a source. The body is identical with or without it (tested). No association: no query string, the server infers nothing and warns of nothing.

## Decision: `ReferenceLookupColumns` and `PostMappingDefaults` on the route (task 020 notes, "Open for 021/022" #2)

**Recommendation: the Matter wizard needs neither. No BFF change. Proceeding.**

`ReferenceLookupColumns` (task 018 K-3; matter: `sprk_mattertype`, `sprk_practicearea`):
- The Office path drops an unusable type or practice area with a warning because the Office add-in previously did, and 018 had to keep that.
- The wizard never did. It created through `Xrm.WebApi`, where Dataverse itself checks AppendTo on every record a create binds and **refuses the whole create** when the user lacks it. The route's check as the caller is the same rule, so the wizard's outcome for "a type or practice area the user may not link" is unchanged: refusal, not a drop. Only the wording changes (the route answers the uniform 404/403 detail instead of Dataverse's privilege sentence).
- The pickers (`searchMatterTypes`, `searchPracticeAreas`) read through the user's own data service, so they only offer reference rows the user can read. A refusal needs a user who can read a row but not AppendTo it, which is a role misconfiguration the old path refused too.
- Passing the set would be a behaviour change, not parity: a value the user picked would be silently dropped. That is a product call, not a migration step. If the owner wants it, it is a BFF edit in `ChildRecordEndpoints.CreateAsync` (`ReferenceLookupColumns = rule.OptionalReferenceLookups`), small and testable, but it would also apply to the Project wizard and the ribbon, so it belongs in its own decision.

`PostMappingDefaults` (task 018 K-7; matter: `sprk_assignedtointernal` = the maker's contact):
- The wizard never writes `sprk_assignedtointernal` (grep of the wizard folder and the host: no occurrence). The Matter form has no such field, and no client code defaulted it to the maker. A wizard matter therefore had no Assigned To Internal before, and has none now. The task 020 concern ("Assigned To Internal no longer defaults to the maker") describes the Office path, not the wizard.
- Giving wizard matters a maker default would be new behaviour, and would need the route to resolve the maker's linked contact (`OfficeService.ResolveMakerContactIdAsync`), which the route does not. Out of scope; listed under "Follow-ups".

No other per-request default exists in the wizard. The only caller-specific values it computed were the BU cascade (below) and the number.

## Client-default inventory (POML step 2)

Every key `createMatter` put on `entity` before this task, and what happens to it:

| Key | Class | Registry row (`RecordTableRules.cs`, `sprk_matter`) | Now |
|---|---|---|---|
| `sprk_mattername` | user-entered | — | sent |
| `sprk_matterdescription` | user-entered (Summary) | — | sent when non-blank |
| `sprk_MatterType@odata.bind`, `sprk_PracticeArea@odata.bind` | user-picked | `OptionalReferenceLookups` (Office only; see the decision) | sent |
| `sprk_AssignedAttorney1@odata.bind`, `sprk_AssignedParalegal1@odata.bind`, `sprk_AssignedLawFirm1@odata.bind` | user-picked | — | sent |
| `sprk_matternumber` | **client default** | `AutonumberColumn = "sprk_matternumber"`; also `MappingProtectedColumns`. A caller-supplied number is refused at step 2 (`CallerBlockedColumnRefusal`: "set by Spaarke — omit it") | **dropped** (the platform autonumber issues it, I-11) |
| `sprk_searchindexname` (BU cascade) | **client default** | `SearchIndexed = true`: both index columns are the server's; a caller's value is dropped at step 2, and nothing writes them (FR-38/FR-40, task 041) | **dropped** |
| `sprk_AI_Search_Index@odata.bind` (BU `searchIndexId`) | **client default** | same | **dropped** |
| `sprk_containerid` | (already removed by task 076) | `TableServerOwnedColumns = RootServerOwnedColumns`, `MappingProtectedColumns` | stays unwritten |
| every mapped field (`applyFieldMappings`) | **client default** | server mapping step (FR-02, fill-only; `MappingProtectedColumns` keeps the number, owner, container, creator person, secure flag and access columns out of any rule) | **dropped** (server) |
| owner / owning team / impersonation | never sent | `ResolveOwnerAsync` (FR-09) | not sent (asserted) |

**Result: every client default is either a server table rule in the Matter row or dropped. None is missing from the registry, so no BFF edit is needed or made.**

## Tests (failing first)

- **RED** with the HEAD `matterService.ts` put back and the new/converted tests in place: `matterService.fieldMapping` (all 9 new tests fail), `matterService.cascade` (4), `matterService.navProp` (2), `webApiCreateError` Matter case (1): **16 failed, 73 passed** across the 8 suites. The restored file is the one committed.
- **GREEN:** the CreateMatterWizard folder, 6 suites, 42 tests; plus `assignedAccessSync` and `webApiCreateError`.
- The full `Spaarke.UI.Components` jest run: 359 of 360 suites, 5901 of 5903 tests pass. The one failing suite, `OntologyAdmin/Rules/__tests__/Rules.test.tsx` (2 tests), is unrelated and a load flake: it passes alone (38 of 38) on the same tree.

`matterService.fieldMapping.test.ts` (rewritten) holds: (1) the body holds exactly the user's keys (all five lookups, name, summary), with a full set of BU defaults resolved by the host; (2) no `sprk_matternumber` and no Matter Type read; (3) no owner / owning / impersonation / container / index key or value; (4) `dataService.createRecord` is never called, the POST goes to `/api/v1/child-records/sprk_matter` with no query without an association; (5) with an association the source is in the query string and the body is identical (and holds only the user's value); (6) `applyFieldMappings` (mocked module) is never called and no `/field-mappings/` request is made; (7) a source scan: `matterService.ts` contains neither `applyFieldMappings` nor `FieldMappingService`; (8) route warnings reach the result as `partial`; (9) a refusal is a hard error with no sync and no follow-on.

### Deviations

1. **`matterService.cascade.test.ts` could not stay as written.** It asserted that `sprk_searchindexname` IS in the payload (FR-WIZ-01). That is a client default the server drops (FR-38) and the POML's own rule ("a default missing there is added to the registry or dropped, never kept on the client") and acceptance criterion ("the wizard payload contains no key the user did not enter") forbid it. I converted it, as task 076 did for the container: the same scenarios (full defaults, container from either source, undefined index name, defaults omitted), now asserting the cascade columns never reach the body. It still passes; its meaning is the opposite.
2. **Two existing tests outside the wizard folder changed** (acceptance criterion: "no existing test outside this wizard changes"). Both construct `MatterService` and read the create through `dataService.createRecord`, which the matter create no longer uses: `assignedAccessSync.test.ts` (the two Create Matter cases now answer the route with an id; assertions unchanged) and `webApiCreateError.test.ts` (the Matter case moved from the Xrm section to the BFF-routed table: it now asserts the neutral sentence for the route's 403, like Event, Invoice, Report Card, To Do and Work Assignment). No assertion about another wizard changed.
3. `eslint-suppressions.json` is edited (a prune), outside the named folders. `client-lint check --package ui-components` passes after it.

## Verification

- `npm run build` in `Spaarke.UI.Components` (tsc): clean. Spaarke.Auth and Spaarke.SdapClient built first (fresh worktree).
- `npm run build` in `src/solutions/CreateMatterWizard` (vite): built, `dist/index.html` 1,150.95 kB (313 kB gzip). `dist/` is gitignored; **not deployed**.
- `eslint` on the touched files: the only error is `no-dupe-else-if` at `matterService.ts` ~:378 (`else if (uploadedFiles.length > 0 && !this._containerId)` after `if (uploadedFiles.length > 0)`). It is pre-existing and dead code (present at HEAD, ~:425); not changed here.
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok (after the prune above).

## Left for the live part (main session, needs owner OK)

1. **Step 1 (before) / step 8 (after) NFR-02 p95** — method proposed in `notes/nfr02-matter-p95.md`. The "before" must be measured on the CURRENT dev bundle, before this branch deploys.
2. **Deploy** the BFF route for `sprk_matter` first (task 020, #2551 is not on dev), then the wizard via `code-page-deploy` (`sprk_creatematterwizard`). Note the previous bundle for rollback. The wizard calls the route unconditionally: with the old BFF it would be refused (`unsupported_table` 400), so the order matters.
3. **Live checks:** mapped fields present on first load (create under a Project/Account with an active profile); a user value kept over a mapped one; owner = the owning team (FR-09); the number comes from the autonumber (not `LIT-123456`); Assigned attorney/paralegal/law firm are saved; the success state; a files-attached create (upload and index still work; indexing names no index, the server picks it by content type); dark mode. **These checks run only from the `sprk_creatematterwizard` code-page host.** The Spaarke Console moves with its own SpaarkeAi deploy, and its in-app wizard is not covered by any check here until then.
4. If the autonumber column is empty on dev: STOP (POML escalation 2; schema is out of scope).

## Follow-ups (not done; not in this task's scope)

- **Redundant sync call (DONE in fix round 1: removed).** `syncAssignedAccess` (task 142) after the create was an idempotent repeat: the route's create already runs the I-12 L1 materialiser for a root (`RecordCreationService.cs` ~:1796). The work-assignment wizard dropped its call when it moved. Left in place so this task stays a payload change; removing it saves one round trip after "Finish click to record id" and is a one-line follow-up with its test (`assignedAccessSync.test.ts` expects it today).
- **Other Matter creators still on `Xrm.WebApi`:** `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:222` (`this._webApi.createRecord('sprk_matter', ...)`). Not in this task; it needs its own migration or inventory entry.
- **Wizard error wording (DONE in fix round 1, F1).** A matter type or practice area the user may not link was refused with the route's "A record this matter is filed under was not found". The matter catch now maps `child_record.not_found` to a root-create sentence (see "Fix round 1").
- **`matterService.ts` dead branch** (`no-dupe-else-if`, above): trivial to remove; report only.
- The `main.tsx` comment above `resolveUserBuDefaults` (DONE in fix round 1: rewritten).
- **`resolveUserBuDefaults` is now a wasted round trip.** The wizard still resolves the BU defaults on Finish (two `Xrm.WebApi` reads) and passes them to `MatterService`, which ignores them. Removing the host callback and the `cascadeDefaults` parameter is a signature change across hosts, so it is left for a follow-up.
## Fix round 1 (review findings, 2026-11-05)

Branch `task/fm-021`, merged with the work branch (`2a05764261`) first. Nothing deployed, no PR, no Dataverse. Test counts: see "Verification (fix round 1)".

| # | Finding | Fix | Evidence |
|---|---|---|---|
| 1 | Notes said the other hosts "move with the shared service" | Corrected above (Host paragraph, live-checks line). A host moves only when its own bundle is rebuilt and deployed. The Console's in-app wizard (`InAppWizardHost` -> shared `CreateMatterWizard`, `ThreePaneShell.tsx:960`) stays on the old path until SpaarkeAi is rebuilt and deployed. The live checks run only from the `sprk_creatematterwizard` code-page host. | notes only |
| 2 | NFR-02 stub: in-frame PerformanceObserver | `nfr02-matter-p95.md` now records the run used a scripted HTTP replay (`task-021-live.md`) and that a browser run must observe at the top window, or use Playwright `requestfinished`. | notes only |
| F1 | A 404 `child_record.not_found` on the matter create said "A record this matter is filed under was not found" | `describeWebApiCreateError(err, noun, { rootCreate: true })` maps it to "One of the values you selected was not found, or you don't have permission to link it to this matter. Ask your administrator." with `isAccessDenied: true` (so the wizard shows it without the "Failed to create matter record:" prefix). The matter catch passes `rootCreate: true`; other wizards are unchanged (no option, no mapping). | `webApiCreateError.test.ts`: 4 new cases (unit with and without the option; Matter wizard end to end through a 404 ApiError; another reasonCode keeps the prefix). Failed before the fix (2 failed), pass after. |
| 4 | `indexUploadedFiles` still got `cascadeDefaults?.searchIndexName` | Argument removed (the server's `FileIndexRequest` has no `searchIndexName`, FR-40). `createMatter`'s docstring and the parameter (`_cascadeDefaults`, unread) updated; the "indexing still uses the BU searchIndexName" notes line corrected. `EntityCreationService.indexUploadedFiles` is unchanged (other wizards). | `matterService.cascade.test.ts`: new case asserts 4 arguments and no index name in the call, with a resolved BU index. Failed before (5 arguments), passes after. |
| 5 | Stale comments | Rewrote `CreateMatterWizard.tsx` (the cascade comment before `MatterService`, and the Task 020 comment before `createMatter`) and `src/solutions/CreateMatterWizard/src/main.tsx` (above `resolveUserBuDefaults`) to say what the code does now. | comments only; build ok |
| 6 | `syncAssignedAccess` after a root create | Call and import removed from `matterService.ts`; the server already runs `AssignedAccessMaterializer.RunAfterWriteAsync` in the create. The two Create Matter cases in `assignedAccessSync.test.ts` now assert no call to the sync route (and no sync warning). No other host's call touched. | Old "syncs the created matter" case would fail against the new code; new case failed against the old code. |
| 7 | Success-with-warnings body said "though some follow-on actions could not complete" | Now "...has been created, with warnings. See details below." (the warnings can be server mapping drops, not only follow-on actions). | text only; the string is not asserted by any test |

Effect on NFR-02 (see `task-021-live.md`, deviation 6): the live run counted the I-12 grant inside the create window on the "after" side while the old wizard ran it after the window. Finding 6 removes the client's second, repeated grant call, so the user's wait after the create is shorter by that call; the measured create window is unchanged.

### Found in passing (filed here only; pre-existing, not reached today, not fixed)

- `MatterService._assignCounsel` (`matterService.ts` ~:415-423) binds `sprk_assignedlawfirm1` to `/contacts(...)`. The lookup targets `sprk_organizations` (the create payload binds it that way at ~:259). Only reached by the "assign counsel" follow-on, which is not wired from the wizard today.
- `matterService.ts` ~:374: `else if (uploadedFiles.length > 0 && !this._containerId)` after `if (uploadedFiles.length > 0)` is dead code (`no-dupe-else-if`; suppressed in the lint baseline).
- The Console's in-app wizard stays on the old path until the SpaarkeAi bundle is rebuilt and deployed (finding 1).

### Verification (fix round 1)

- jest (\`Spaarke.UI.Components\`): \`assignedAccessSync\`, \`webApiCreateError\`, \`components/CreateMatterWizard\`, \`utils/adapters\`: 14 suites, 201 tests, all pass. Run against the pre-fix \`matterService.ts\` and \`webApiCreateError.ts\`: 4 tests fail (2 for F1, 1 for finding 4, 1 for finding 6); all pass after.
- \`tsc --noEmit\` in \`Spaarke.UI.Components\`: clean.
- \`node scripts/quality/client-lint/client-lint.mjs check --package ui-components\`: ok.
- \`npm run build\` in \`src/solutions/CreateMatterWizard\`: built (\`dist/\` gitignored, not deployed).

## Resume 2026-11-08 (fix-round-1 close-out, merge, contract check)

Branch `task/fm-021`, merged with `origin/work/field-mapping-server-write-path-r1` (57748ebf6d). One conflict, `webApiCreateError.ts`: fix round 1's `rootCreate` not-found branch (this branch) against the secure-create reason-code branches (049: `SECURE_CREATE_CHECK_FAILED`, `SECURE_CREATE_DENIED_CODES`; work branch). Resolved by keeping both: `reasonCodeOf(err)` is read once into `reason`; the `rootCreate` + `child_record.not_found` branch comes first, then the two secure-create branches. The test file merged without conflict. Nothing deployed.

### Fix-round-1 items, checked in code

| # | Item | In code | Test |
|---|---|---|---|
| 1, 2 | Notes corrections | notes only | n/a |
| F1 | Root-create not-found text | `matterService.ts` passes `{ rootCreate: true }`; `webApiCreateError.ts` maps `child_record.not_found` | `webApiCreateError.test.ts` (4 cases) |
| 4 | No index name to `indexUploadedFiles` | call has 4 arguments | `matterService.cascade.test.ts` |
| 5 | Stale comments | rewritten | comments only |
| 6 | No client `syncAssignedAccess` | removed | `assignedAccessSync.test.ts` |
| 7 | Success text with warnings | `CreateMatterWizard.tsx` "...with warnings. See details below." | not asserted |

All seven are closed. None was open.

### Contract check against the server (work branch tip)

- Route and query: `POST /api/v1/child-records/sprk_matter?mappingSourceEntity=&mappingSourceId=`. `secure` is a new optional query (049 fix round 2), but `SecureIntentTables` is project and work assignment only, so the matter wizard sends none. No drift.
- Response: `{ id }` or `{ id, warnings }` where `warnings` is `IReadOnlyList<string>` (`RecordCreationOutcome.Warnings`), which is what `createRecordViaBffWithWarnings` reads. No drift.
- Refusals: `child_record.denied` and `child_record.not_found` are unchanged and handled; the 049 secure-create codes arrive through the merge into the same function.
- 049b (the maker's subject inline, the rest queued, an inline full pass when the queue is down): removing the client sync stays correct, and 049's notes say a client sync right after a create can meet the queued grant. **Drift fixed (comment only).** The comment in `matterService.ts` said the full I-12 pass runs in the same request; it now says the maker's share is inline and the rest queued, so an assigned attorney or paralegal may see the matter a moment after the wizard closes. The test comment in `assignedAccessSync.test.ts` was updated the same way.
- 049c: server-side speed-up only; no client surface.
- 045a `RecordCreationWarning.RegardingTypeClearedCode`: **not merged** (no such constant on the work branch). A matter has no regarding pair, so it would not reach this wizard in any case.
- 076c: a push-preview panel fix (`task-040-notes.md`); not a create-wizard surface.

### NFR-02

Dev re-measure r3 (2026-11-08, `task-021-live.md`): p95 delta +1288 ms against the +500 ms budget. **Owner ruling 2026-11-08: the NFR-02 verdict is taken in UAT ("we'll test this in UAT").** NFR-02 does not block finishing the code, and no performance work is done here. The verdict stays open until UAT.

### Left before the wizard can deploy

UAC-r2 deploys with the owner's go: the BFF route for `sprk_matter` first (the wizard calls the route unconditionally), then `sprk_creatematterwizard` via `code-page-deploy`, then the live checks in "Left for the live part" above, then the NFR-02 verdict in UAT. The Console's in-app wizard moves only with a SpaarkeAi rebuild and deploy.

File hygiene: the sections above this one were written with a stray backtick at the start of each line, so they render as code. Left as is.
