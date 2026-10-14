# Task 039 - External event reads for Matter and Work Assignment (FR-03, server)

> Branch `worktree-agent-a0356b48145f4ef5f`, based on `fc71af7fb` (R3 head after 037). Rigor FULL (tags `bff-api`, `auth`, `testing`).
> Code commit `02150553b`. No push, no PR (see "Not done"). **Section 11 (fix round) supersedes sections 3 and 9 where they differ: the every-root rule now covers to-dos, documents, messages, the feed and the documents module grid, and is an INTERIM decision pending the owner.**

## 1. What changed

| File | Change |
|---|---|
| `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` | `ListEventsForRoot(rootKind, ...)` replaces the project-only handler body. `GetEvents`, `GetMatterEvents`, `GetWorkAssignmentEvents` are three one-line adapters. Two `MapGet` lines are new, inside the existing `/api/v1/external` group. |
| `Infrastructure/ExternalAccess/ExternalDataService.cs` | `GetEventsAsync(TodoRootKind, Guid, CallerPrincipal, CancellationToken)` replaces `GetEventsAsync(Guid, CancellationToken)`. `BuildEventsQuery(TodoRootKind, Guid)` takes its filter column from `RootBindings` (the table the to-do list and delete-own already share). `EventRow` reads the other two root lookups. `ReadableEvents` withholds rows (section 3). |
| `tests/unit/.../ExternalRootEventReadTests.cs` (new) | 44 tests over the real routes and the real service. |
| `tests/integration/contract/.../ExternalProjectEventContractTests.cs` | 10 tests for the two routes through the real grant resolution. |
| `ExternalAccessContractTests.cs`, `Issue963_ExternalDataPagingTests.cs`, `EventRoutesLiveTests.cs` | Call sites follow the new `GetEventsAsync` signature. The recorded read for the project route is byte-identical to before. |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `GET /projects/{id}/events` seam is now `RightsForRoot` (was `HoldsReadOnProject`); two new entries (deliberate). |

Not changed: `ExternalEventDto`, `CreateEvent` (project-only), `ExternalCollectionResponse`, the feed, delete-own, the module registry, DI, packages.

## 2. Design

- **One implementation, three adapters** - the pattern task 029 used for to-dos. The Read decision is `RightsForRoot(principal, rootKind, rootId).HasFlag(Read)`, the expression the to-do, document and delete-own routes use (`CallerPrincipal.GetEffectiveRights` / `GetMatterRights` / `GetWorkAssignmentRights`). It reads the principal, never Dataverse, so it is app-only, has no OBO, and identity comes from the resolved principal.
- **Project route is unchanged**: `HoldsReadOnProject` is `RightsForRoot(Project).HasFlag(Read)`, so the same callers pass. The 403 body is the same (`DenyRoot(Project)` = `Forbidden` / `You do not have access to this project`). The `$filter` is the same column. The `$select` gains two lookup columns that are never returned (below).
- **Escalation trigger checked**: `sprk_event` has `sprk_regardingmatter` and `sprk_regardingworkassignment` lookups (live metadata in `projects/unified-access-control-r2/notes/task-159-events-authorization.md` section 0.2; `docs/data-model/sprk_event-related-tables.md` lines 91, 102). The trigger does not fire.

## 3. Secure-record isolation (decision, with its reason)

The brief: events of a secure matter or work assignment follow the same rules as the record; never weaken a secure check.

1. **Access** to a matter or work assignment's events is the record's own rights. A secure record reached only through an organization grant, or a grant row with no level, is `None` in the principal (task 136) and is a 403. Tested.
2. **A second lookup must not bypass the first.** `sprk_event` can be filed to more than one root. Before this task an event filed to project P and to a secure matter M was returned by P's route to any reader of P. With matter events that becomes a real path: the secure matter's event shows on a project the caller can read. Delete-own already refuses a row unless the caller holds Read on EVERY parent (`AuthorizeDeleteOwnAsync`); the list now applies the same rule (`ExternalDataService.ReadableEvents`). An event is listed only if the caller holds Read on every root it is filed to. A row filed to one root is unaffected. A lookup value that is not a GUID is withheld (fail closed).
   - This is a tightening of the project route, and only for dual-parent events with an unreadable second parent. Every single-parent event lists exactly as before. A withheld event is logged (count only) and does NOT set `truncated`.
3. `RecordContainerResolver` / I-2 govern SPE container choice for documents. Events have no SPE content, so nothing there applies; the equivalent for events is items 1 and 2.
4. **Other surfaces**: the notification feed (037) contains no event items, so there is no other place an event's visibility is decided. `DELETE /events/{id}` (032) already covers all three roots with the same rule as the list. If events are ever added to the feed, they must use `RightsOn` per root and the every-parent rule.

## 4. CLAUDE.md section 11 answers (new surface = two GET routes)

1. **Existing**: `GET /projects/{id}/events` (`ListEventsForRoot` now), and the to-do routes for the three roots.
2. **Extension**: extended, not forked. The handler and the data read are the project's own, generalized over `TodoRootKind` and `RootBindings`. Only two route templates and two adapters are new.
3. **Cost of doing nothing**: the SPA's record detail view (task 050/054, FR-03) has no events for a Matter or Work Assignment; today it shows an empty state.

Placement (`bff-extensions.md`): in the BFF, in the existing external-access endpoint file and data service. No new service, DI registration, package, Dataverse column or file surface.

## 5. SPA contract

Routes (all under the existing `/api/v1/external` group; bearer token on either plane):

| Route | Notes |
|---|---|
| `GET /api/v1/external/projects/{id}/events` | unchanged |
| `GET /api/v1/external/matters/{id}/events` | new |
| `GET /api/v1/external/workassignments/{id}/events` | new |

- **Query parameters**: none. No paging parameters; the server pages to its cap.
- **Order**: due date ascending, then event id.
- **200 body** (identical for the three routes):
  `{ "value": [ ExternalEventDto ], "truncated": true }` - `truncated` is omitted when false.
  `ExternalEventDto` (same type as the project route):

  | Member | Type | Note |
  |---|---|---|
  | `sprk_eventid` | string (GUID) | |
  | `sprk_name` | string | Dataverse `sprk_eventname` |
  | `sprk_duedate` | string or null | `yyyy-MM-dd` |
  | `sprk_status` | int or null | Dataverse `statuscode` (Draft 1, Open 659490001, ...) |
  | `createdon` | string or null | ISO timestamp |
  | `_sprk_regardingproject_value` | string or null | null for matter and work-assignment events; ignore it there |
  | `_sprk_createdbyperson_value`, `_sprk_createdbycontact_value` | string or null | creator stamps; the SPA offers `DELETE /events/{id}` only when one matches the caller (`/me`) |

- **Codes**:

  | Status | Meaning |
  |---|---|
  | 200 | caller holds Read on the record (ViewOnly, Collaborate, FullAccess all read) |
  | 401 | no or invalid token |
  | 403 | no Read on the record. A record that does not exist, one outside the caller's set, one with a level-less grant and a record of a different kind with the same id are the SAME response: `{title:"Forbidden", detail:"You do not have access to this matter"}` (`project`, `work assignment` for the other kinds). No `reasonCode`. |
  | 405 | `POST` to the matter or work-assignment route (event create stays project-only) |

  There is no 404 and no 503 on these routes (section 6).
- **`truncated: true` with an empty `value`** means the events could not be read (or were cut at the cap). The SPA must show a "could not load" state, not "no events". This is how every list on this plane behaves (task 105).
- **Create/delete**: `POST` is project-only. `DELETE /api/v1/external/events/{id}` already works for events on all three roots (task 032).

## 6. Deviation to confirm: "an unreadable read gives 503"

The brief says an unreadable read gives 503. I did not make the event list return 503, for two reasons:

1. **The access decision has no Dataverse read.** It is the principal's rights, so there is no "could not tell" case to turn into a 503 (unlike service request and invoice, whose parents are read; those keep their 503s).
2. **The events list read follows the plane's existing contract** (task 105): a failed or cut-short read returns the rows read so far with `truncated: true`, never a clean empty list. `GET /projects/{id}/events` behaves this way today and the brief asks that it stay unchanged; the to-do and document lists do too. Making only the two new routes 503 would make the same read behave three ways. Tested: `Events_WhenDataverseCannotBeRead_ReturnEmptyFlaggedTruncated_NeverACleanEmptyList` (all three roots).

If the owner wants a 503 for an unreadable events list, it is a plane-wide change to `GetCollectionAsync` callers (SPA handling included) and belongs in its own task. The change for events alone is small: treat `CollectionReadOutcome.FirstPageFailed` as `DataverseUnavailableException` in `GetEventsAsync` and catch it in `ListEventsForRoot` (503 with a `reasonCode`).

## 7. Test evidence

Counts (all on the final tree):

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet build tests/integration/Sprk.Bff.Api.IntegrationTests/` (live tests compile) | 0 warnings, 0 errors |
| Unit, filter `ExternalAccess\|ExternalProjectData\|ExternalDelete\|Event\|Issue963` | 2958 passed, 0 failed |
| of which new: `ExternalRootEventReadTests` | 44 passed |
| of which new: `ExternalProjectEventContractTests` additions | 10 passed |
| `dotnet test tests/Spaarke.ArchTests/` | 941 passed, 0 failed |
| `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api) | none; no csproj or props file changed |

No `TaskCanceledException` occurred; the full unit suite was not run (machine load rule).

**Fail-before** (mutations of the committed code, run, then `git checkout`-restored):

- Routes mapped to a dead path (`.../eventsX`) and `ReadableEvents` disabled: **34 of 62** tests in `ExternalRootEventReadTests` + `ExternalProjectEventContractTests` fail (404 where 200 or 403 is expected; the three withheld-event tests; the malformed-lookup test). 28 pass, which are the project-route and pure-query tests that the mutation does not touch.
- Old ledger (`fc71af7fb`) with the new code: `ArchTests` `Task 167 Rule A` fails naming all three event routes, and `every HandlerDecision is verified in source` fails with `the seam 'HoldsReadOnProject' is not reached` for `GET /projects/{id}/events`. With the updated ledger: 941 pass.

Acceptance criteria:

| Criterion | Evidence |
|---|---|
| Matter and WA events return that record's events for a caller with Read | `Events_ForACallerWithRead_ReturnTheRecordsEvents_AndNoOthers` (3 roots x 3 levels, including ViewOnly), contract `GetRootEvents_WhenGranted_Returns200_...` |
| 403 outside the caller's set | `..._ForARecordOutsideTheCallersSet_Are403_AndNothingIsRead`, `..._ForARecordTheCallerHoldsNoRightsOn_Are403`, kind-confusion theory, contract `..._WhenNotGranted_...`, `..._WhenGrantedADifferentRecord_...`, `..._WhenTheGrantRowHasNoLevel_...`; each asserts no Dataverse read |
| Missing and forbidden are one refusal | `Events_ForAnIdThatDoesNotExist_AndOneThatIsForbidden_AreTheSameRefusal` |
| Project route and its contract tests unchanged | all pre-existing `ExternalProjectEventContractTests` and project cases pass untouched; `BuildEventsQuery_ForTheProject_IsTheShippedQueryPlusTheTwoOtherLookups`; denial body test for `projects` |
| Secure isolation | `ProjectEvents_AlsoFiledToASecureMatterTheCallerCannotRead_AreWithheld`, `MatterEvents_AlsoFiledTo...CannotRead_AreWithheld`, `..._CanRead_AreListed`, `Events_FiledToAMalformedLookup_AreWithheld_FailClosed` |
| Read only | `EventCreate_OnAMatterOrWorkAssignment_IsNotARoute` (405) |
| Unreadable events read is never a clean empty list | `Events_WhenDataverseCannotBeRead_...` |

Extra tests beyond the stated contract, one line each: the DTO-shape test pins "same DTO, no new member"; the kind-confusion theory pins that rights on one record kind do not open another kind with the same id (the dispatch is per kind); the malformed-lookup test pins the fail-closed branch of `ReadableEvents`.

## 8. Measurements (BFF hygiene item 4)

Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; framework-dependent linux-x64 (net10, SDK 10.0.101); Release; `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; **PDBs included**. Each side built from a fresh detached worktree at a short path (`C:\wt039m`, `C:\wt039b`, `C:\wt039t`, removed afterwards).

| Tree | Commit | Files | Zip |
|---|---|---|---|
| Fresh `origin/master` | `37470dacc` | 192 | 36.465 MB |
| R3 head before this task | `fc71af7fb` | 192 | 36.520 MB |
| This task | `02150553b` | 192 | 36.521 MB |

- **Task delta** (this task vs R3 head): +0.001 MB.
- **Delta vs fresh master** (all R3 BFF tasks so far, this one included): +0.056 MB.
- File counts match on all three. Well under the +5 MB trigger, 55 MB review and 60 MB stop.
- These figures are on a lower absolute scale (36.5 MB) than the 44.96 MB baseline quoted in `azure-deployment.md`; the baseline is not the comparison point and is not used here.
- **No new HIGH CVE**: `dotnet list package --vulnerable --include-transitive` reports none, and no package reference changed.

## 9. Known limits

- **K1**: a dual-parent event withheld from a caller is invisible rather than flagged, so a caller cannot know it exists. By design (it is the matter's isolation); logged server-side with a count.
- **K2**: the DTO does not say which other records an event is filed to. The SPA cannot link an event to its other parent; it did not need to before.
- **K3**: matter and work-assignment event reads inherit the 200-rows-per-page, row-cap behaviour of the shared reader (task 105); `truncated` says when it bites.
- **K4**: `_sprk_regardingproject_value` is returned as null on matter and work-assignment events (same DTO).
- **K5 (observation, not changed)**: `CreateEvent` still gates on `HasProjectAccess` plus `GetEffectiveRights(...).Create`, not `RightsForRoot`. Equivalent for projects today; left alone (read-only task).

## 10. Not done

- `/conflict-check` and the PR: no PR was opened (the brief says do not push). `Api/ExternalAccess/**` and `Infrastructure/ExternalAccess/**` are shared with `unified-access-control-r2` (open PRs #1583 touching `ExternalAccessModule.cs`, #1586 touching `ExternalAccessEndpoints.cs`); this change touches neither file. Run `/conflict-check` before the PR.
- `TASK-INDEX.md`, the POML `<status>` and `current-task.md` were not edited (the caller owns them). The drift script was not run for that reason.
- Live check against dev Dataverse: not run (no deploy in scope; task 038 deploys the BFF). The live `EventRoutesLiveTests` compile but were not executed.

## 11. Fix round (reviews A and B): the every-root rule across the external lists

### 11.1 INTERIM main-session decision, pending the owner

> **Superseded by section 12** (owner decision 2026-10-12, option B "secure roots only"). Kept for the record.

On the external plane, a row filed to SEVERAL core roots (project / matter / work assignment) is listed only when the caller holds Read on EVERY root it carries. It is ONE function, `Infrastructure/ExternalAccess/ExternalMultiRootRule.AllRootsReadable`, called by every list below. `ExternalDataService.ParentReadable` is gone (extracted, not copied).

- **What it applies to now**: events (039), to-dos (F1), documents, message threads, the notification feed, and the module grid's documents module (F2 audit, section 11.3).
- **What it changes on the wire**: `GET /projects/{id}/events` and the three to-do routes, the document routes, `GET /{root}/{id}/messages` and `GET /notifications`, for MULTI-ROOT rows only. A single-root row lists exactly as before (tested).
- **The POML acceptance criterion "project route unchanged" is deliberately NOT met for those rows.** It still holds for every single-root row, the 403 body, the DTO and the query filter.
- **Known cost**: the principal cannot tell a secure root from one that is merely not granted, so a row filed to an ungranted NON-secure second root is also withheld (over-hides; never leaks).
- **Owner options** (a later choice is a change in `ExternalMultiRootRule` only, plus the `RequireReadOnEveryRoot` flag on the documents module):
  1. **every-root** (this, the default now);
  2. **secure-only**: withhold only when the unreadable root is secure; needs a secure check on each unreadable root;
  3. **union**: list when ANY root is readable (the pre-fix behaviour for project routes, which is the leak).
- Withheld rows are logged with a count and do NOT set `truncated`.

### 11.2 Known limits recorded

- **K1**: `sprk_regardingservicerequest` is not consulted. There are no secure service requests, and delete-own (`AuthorizeDeleteOwnAsync`) does not consult it either. A service request is reached through its requester, not through the principal's root sets.
- **K2**: pair-only filings are not found. A row filed only through the polymorphic regarding pair (`sprk_regardingrecordid`) and no typed root lookup is invisible to the typed-lookup `$filter` on every route, so it is neither listed nor checked.
- **SPA gap (the main session will schedule it; the SPA is not touched here)**: there is no consumer yet for matter or work-assignment events and to-dos. `RecordDetailModal.tsx:~436-455` shows a static "No events or tasks...", and `EventsCalendar` / `SmartTodo` are project-only.
- **K3 (new; FIXED in 11.7)**: the module grid's documents module could only evaluate the three typed root columns its FetchXML projects (`sprk_project`, `sprk_matter`, `sprk_workassignment`). The related twins are not projected there, so a grid row linked to a hidden root ONLY through a twin is not withheld by the grid (the record-detail document routes do read the twins).
- **Observation (FIXED in 11.7)**: `DownloadDocumentForParent` and the versions route authorize by Read on the ROUTE parent plus `GetDocumentParentAndNameAsync` (the document's lookup for that kind equals the route id), then resolve the SPE pointers. A document filed to project P and secure matter M therefore downloads through P's route for a P-only reader who knows the document id. The list no longer reveals the id, but the single-item check does not apply the every-root rule. Whether `RecordContainerResolver` (I-2) refuses such a document at `GetSpePointersAsync` was not verified. Needs its own task: apply `ExternalMultiRootRule` where `GetDocumentParentAndNameAsync` is called (`ExternalProjectDataEndpoints.cs:618, :1388`).

### 11.3 Audit of every external list (verdicts)

| List | Can a row carry an unreadable second root? | Verdict |
|---|---|---|
| `GET /projects/{id}/events`, `/matters/...`, `/workassignments/...` | Yes: `sprk_event` has the three lookups | Already ruled in 039 (`ReadableEvents`, now the shared rule) |
| `GET .../todos` x3 (F1, `ExternalDataService.GetTodosAsync`) | Yes: a to-do under an invoice of secure matter M and project P is stamped with both (`CoreAncestorResolver`, `StandardRootColumns`). `TodoRow` and `BuildTodoListUrl` already read all three lookups | **Leak, fixed**: `GetTodosAsync` takes the `CallerPrincipal` and applies the rule |
| `GET .../documents` x5 (`GetDocumentsAsync`) | Yes: `sprk_document` carries typed links to project, matter and work assignment plus a related twin of each (`DocumentLinkFields.All`; `CoreAncestorResolver.IntermediateRootColumns["sprk_document"]`). The list returns name, type and AI summary. The select read only `_sprk_project_value` | **Leak, fixed**: the select reads all six root links; the rule applies for every parent kind. A pin test checks the select against `DocumentLinkFields`. For invoice documents the rule is applied too: documents carry explicit links, no derived stamps |
| `GET /{root}/{id}/messages` (projects, matters, work assignments, service requests) | Yes: `sprk_communication` is stamped with the root of whatever it is filed under (invoice, event, analysis...). The read was keyed on ONE regarding lookup (`ReadRegardingAppOnlyAsync`) | **Leak, fixed**: the three stamp columns are always selected (`ExternalSelect`), and the rule is a REQUIRED parameter of the read (`rootsReadable`, no default) |
| `GET /invoices/{id}/messages` | Yes, but the stamps are exactly the invoice's own parents | **Exempt by the owner's decision** (2026-10-11, option A "inherit": an invoice is reached through the UNION of its parents). Applying the rule would empty the thread for the caller the owner chose to admit. Tested (`AnInvoiceThread_IsExemptFromTheEveryRootRule...`). If the owner moves invoices to every-root, remove the invoice branch of `ExternalMessageEndpoints.RootsReadableFor` |
| `GET /notifications` (message items; task 037) | Yes: the same communications, matched on any accessible record | **Leak, fixed**: `ReadRecentForRecordsAppOnlyAsync` takes the same required rule. Grant items are single-root rows (one grant = one root, re-checked by `RightsOn`) |
| Module grid, documents module (`/data/savedquery/...`, `ScopeRows`) | Yes: OR across the three document dimensions, in the server-side filter and in `ScopeRows` | **Leak, fixed** (new `ExternalModuleDescriptor.RequireReadOnEveryRoot`, set only on documents; the rule runs in `ScopeRows`, the grid's post-filter). See K3 above |
| Module grid, invoices module | An invoice carries matter + project | **Unchanged**, owner decision above (union of parents). Tested |
| `GET /projects/{id}/contacts`, `/organizations` | No: the rows are contacts / accounts, read through the grant junction (`GetProjectContactIdsAsync`), not root-filed records | Not applicable |
| `GET /projects`, `/projects/{id}`, `/me`, `/me/entitlements`, `assigned-access`, `contact-grants`, `user-shares`, `can-manage-access`, no-access reads, `/data/record`, `/data/metadata`, `/data/savedqueries` | No: roots themselves, principal-derived data, grant ledgers (one root per row), single records, view or metadata lists | Not applicable |

### 11.4 Fix 3 (K3, review A): the fake respects `$select`

`ExternalRootEventReadTests.FakeDataverse` now serves `sprk_events`, `sprk_todos` and `sprk_documents` and projects each row through the request's `$select`. The message and feed harnesses do the same (`AppOnlySelectProjection`). Proof (mutation, restored afterwards): the matter lookup removed from the events, to-do and document selects and from the message stamp columns, one build, one run: **15 of 175 fail**, including `ProjectEvents_AlsoFiledToASecureMatterTheCallerCannotRead_AreWithheld`, `Todos_AlsoFiledToARootTheCallerCannotRead_AreWithheld_OnAllThreeRoutes`, `Documents_...AreWithheld_OnAllThreeRoutes`, the message and feed withholding tests, and the pinned-select tests.

### 11.5 Fix 4 (K3, review B)

`tests/integration/regression/Issue963_ExternalDataPagingTests.cs`: the event and to-do rows now carry `_sprk_regardingproject_value = Root`, and `Creator` holds Read on `Root` (`CallerProjectAccess.FromLevel(Root, ViewOnly)`), so the paging test models rows the rule would actually list. The documents and to-do reads now pass `Creator`.

### 11.6 Evidence

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `tests/unit/Sprk.Bff.Api.Tests` and `tests/integration/Sprk.Bff.Api.IntegrationTests` (live tests compile): 0 warnings, 0 errors.
- Filtered unit run (the brief's filter plus `ExternalMultiRoot|ExternalModule|Tier2Scope|ExternalScope|CommunicationThread`): **1165 passed, 0 failed, 1 skipped**. `Issue963`: 36 passed. New: `ExternalMultiRootListReadTests` 24, plus 7 message tests and 3 feed tests added to the existing classes.
- `ArchTests --filter RouteAuthorizationGuard`: 87 passed (ledger cites for `ExternalMessageEndpoints.cs` moved to :335/:481/:538).
- **Fail-before** (mutation: `ExternalMultiRootRule.AllRootsReadable` skips the refusal, and `RequireReadOnEveryRoot = false`): **17 of 175 fail** across events, to-dos (3 routes), documents (3 routes), messages (3 routes + malformed), feed (2) and the module-grid documents test; the fail-closed (malformed lookup) tests fail too.
- Test changes forced by the new signatures: `GetTodosAsync` / `GetDocumentsAsync` take the `CallerPrincipal` (4 test overrides and 3 call sites updated); `ProjectListUrl_IsUnchanged` renamed `ProjectListUrl_IsTheShippedQuery_PlusTheOtherRootLinks`; the pinned message and feed `$select` strings gained the two extra stamp columns.

### 11.7 Fix round 2: the every-root rule on every single-document path (supersedes the "Observation" bullet and K3 in 11.2)

**Defect (found by the previous round).** `DownloadDocumentForParent` and `ListDocumentVersionsForParent` checked only that the document's lookup for the route's parent kind equalled the route id. A caller with Read on project P who knew the id of document D, filed to P AND to secure matter M, downloaded D through P's route although the list hid it.

**Fix.** `ExternalDataService.GetDocumentParentAndNameAsync` now reads the six root links (typed + related twin of project, matter and work assignment: the root subset of `DocumentLinkFields`, pinned by test) and returns a `DocumentScopeRead` (parent id, name, `DocumentRootLinks`; it still deconstructs to `(ParentId, DocumentName)`). `DocumentScopeRead.IsReadableUnder(routeId, caller)` is "under the route parent AND `ExternalMultiRootRule.AllRootsReadable` over the six links". The list uses the same `DocumentRootLinks.AllReadableBy`, so the list and the single-record paths cannot disagree. A non-string lookup value reads as unreadable (fail closed). The deny is `DenyDocumentNotUnderParent`: the exact status, title and detail the route already returned for "document not under this parent", so a withheld document, a misfiled one and a missing one are indistinguishable (tested by comparing the three bodies). Only the server log differs.

**Per-path verdicts**

| Path | Applies? | Verdict and reason |
|---|---|---|
| `GET /{projects,matters,workassignments,servicerequests,invoices}/{id}/documents/{docId}/content` (5 routes, one handler) | Yes | **Leak, fixed.** Rule applied after the parent Read check and before any pointer resolution (`PointerResolutions == 0` asserted). Applied to all five kinds, as the list does (invoice and service-request documents carry the same typed links) |
| `.../documents/{docId}/versions` (5 routes, one handler) | Yes | **Leak, fixed**, same check, same deny |
| Preview | No route | The external plane maps only `content` and `versions` for a single document (every `Map*` in `Api/ExternalAccess` was checked). Nothing to change |
| Document metadata / detail by id | No route | The only by-id read is the module grid `GET /data/record/sprk_document/{id}` (`GetScopedRecordCoreAsync`). Its Tier-2 gate is `IsRecordAccessible`: the documents module's dimensions are PARENT ids, so a document's own id is never in them and the gate refuses before any read. Tested (`DocumentsModule_SingleRecordRead_IsRefusedForADocumentId`: 403, zero reads). Not applicable |
| Upload new version | No route | There is no version-upload route. `POST .../documents` creates a NEW document bound to exactly one parent (`BuildDocumentCreatePayload`; already pinned by "a document is filed to exactly one parent"), so no existing multi-root row is touched. Not applicable |
| `DELETE /documents/{id}` (032) | Yes, partly in place | Already required Read on EVERY typed parent (`sprk_project`, `_matter`, `_workassignment`, `_relatedservicerequest`, `_invoice`). **Two gaps, fixed:** (1) the related twins `sprk_relatedproject` / `_relatedmatter` / `_relatedworkassignment` were not consulted, so a creator's document also filed to a hidden root through a twin could be deleted; they are now parents of a document delete (`Deletables`). (2) a parent lookup that was present but not a GUID was silently ignored (`GuidValue` returned null); `DeleteTarget.HasUnreadableParentLink` now makes `AuthorizeDeleteOwnAsync` refuse (same uniform 403). The other `DocumentLinkFields` (`_relatedinvoice`, `_relatedagreement`, `_relatedcontact`, `_parentdocument`, `_email`) remain unconsulted, as 032 documents |
| `DELETE /todos/{id}`, `DELETE /events/{id}` | Yes, in place | Already Read on every one of the three regarding lookups. Gap (2) above applies and is fixed for them too (shared `ParseDeleteTarget`) |
| `PATCH /todos/{id}` | Stricter already | `GetTodoRootAsync` answers `Ambiguous` for a to-do with more than one root lookup and the route denies it wholesale; it returns 204 and no data. Not changed |
| Event or to-do by id (read) | No route | Events and to-dos are only listed (rule applied in 11.3) or deleted/patched (above). No module is registered for `sprk_event` / `sprk_todo`, so `/data/record` cannot reach them (modules: project, document, invoice, work assignment, matter, gridconfiguration, servicerequest) |
| Message by id | No route | Messages are listed (`GET /{root}/{id}/messages`) and posted. The invoice thread stays exempt (owner 2026-10-11, any-parent) and was not touched |

**Q5: would `RecordContainerResolver` / `GetSpePointersAsync` have refused the scenario anyway?** No (by code reading; not executed). `IDocumentStorageResolver.GetSpePointersAsync(documentId, ct)` takes no caller. It validates that the pointer's drive and item are legitimate for the DOCUMENT (`EnsureDocumentPointerContainerAsync`, the anti-re-pointing check), and for a document that hangs off secure matter M it accepts M's own container (`RecordContainerResolver.DocumentPointer.cs`: "a SECURE record's own container is honoured only for a document that hangs off that very record"). The document does hang off M, so the resolver hands the pointer out. It protects pointer integrity, not the caller's authority over the document's other roots. The route check is the only gate, which is why the fix belongs there.

**Grid K3: done (contained).** The documents module now declares `AdditionalRootLookups` (the three related twins, built from `DocumentLinkFields` in `ExternalAccessModule.DocumentRelatedRootTwins`, because the DocumentLinkVocabulary arch guard forbids a second literal list). The fetch endpoint projects them SERVER-SIDE after the column guard (`Tier2ScopeFilterInjector.EnsureProjected`; the guard has already refused all-attributes, aggregates, aliases and joins, so the FetchXML is a flat attribute list), `ScopeRows` checks them in the every-root rule (a non-GUID value is unreadable), and the response strip removes them because they are not in `ReadableColumns` (the caller's own column allow-list is unchanged: asking for a twin is still a 400). They are not part of the OR scope: a document linked to a root ONLY through a twin is still not returned by the grid (K2 below), which fails closed. Residual: `ScopeRows` reads an absent twin key as "not filed", because Dataverse omits a null attribute; rows that never went through the endpoint's projection get the old behaviour (only the fetch endpoint and tests call it).

**K2 reminder (unchanged).** A row filed only through the polymorphic regarding pair, or only through a related twin, is invisible to the typed-lookup `$filter` on every list and the grid. It is neither listed nor checked; it fails closed.

**Evidence.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `tests/unit/Sprk.Bff.Api.Tests` and `tests/integration/Sprk.Bff.Api.IntegrationTests`: 0 warnings, 0 errors.
- New: `ExternalMultiRootDocumentRouteTests` (43: must-fire x 3 roots x {content, versions} for each of the six links; identical-body comparison of missing / misfiled / multi-root; malformed lookup (string and number); must-not-fire for a single-root document, a document also filed to a root the caller can read, and a second readable record of the same type; the select pinned to `DocumentLinkFields`). In `ExternalDeleteOwnTests`: twin parent denies (3), readable twin deletes, non-GUID parent denies on all three kinds, `ParseDeleteTarget` reads twins and flags a non-GUID, the select pin gained the twins. In `ExternalMultiRootListReadTests`: grid twin withheld (3), twin readable kept, twin declaration, the fetch pipeline projects the twins and never returns them, single-record gate. In `Tier2ScopeFilterInjectorTests`: `EnsureProjected` x 3.
- Test signature changes forced by `GetDocumentParentAndNameAsync` returning `DocumentScopeRead`: the two overrides (`ExternalRecordDocumentRoutesTests.InMemoryExternalDataService`, `ExternalAccessContractTests.StubExternalDataService`). `ExternalRootEventReadTests.FakeDataverse` now answers a single-row `sprk_documents({id})` read through `$select`.
- Unit, filter `FullyQualifiedName~External|Tier2Scope|CommunicationThread`: **2876 passed, 0 failed, 0 skipped**. `tests/Spaarke.ArchTests` (whole project): **941 passed** (it caught my first cut of the grid change: three `sprk_related*` literals in `ExternalAccessModule.cs` tripped `DocumentLinkVocabularyGuardTests`, so the twins are now derived from `DocumentLinkFields`). The route ledger cites for the document scoping moved to `ExternalProjectDataEndpoints.cs:623` and `:1403` (`RouteAuthorizationGuard`: 87 passed).
- **Fail-before** (mutations, restored afterwards; one build, one run each): (a) `IsReadableUnder` ignores the roots, the delete drops the twin parents and the unreadable-link refusal, and the fetch endpoint skips `EnsureProjected`: **25 of 234 fail** in the four touched classes (download and versions must-fire and malformed tests across all roots and both routes, the identical-body test, the delete malformed test on 3 kinds, the delete parse and select-pin tests, the `DocumentScopeRead` unit test, the fetch-pipeline test). (b) `ScopeRows` ignores `AdditionalRootLookups`: **4 of 31 fail** (3 twin withholding cases + the fetch-pipeline test).

### 11.8 Fix round 3: the grid fails closed when the FetchXML omits a root column (F1), K3 alignment, known limits K1/K2/K4

**F1 (fixed).** The documents module's every-root rule read `sprk_matter` / `sprk_workassignment` from the row, but the endpoint projected only the three related twins. A grid FetchXML naming only `sprk_project` made Dataverse return no matter or work-assignment key (it omits a null attribute, and a column the query did not read is not returned either), `AllRootsReadable` read the absent key as "not filed", and a document filed to project P and secure matter M passed the server `or` filter via P and leaked its id and name.

- `ExternalModuleDescriptor.RootRuleAttributes` (scope dimensions + `AdditionalRootLookups`) is the one list of attributes the rule reads. The fetch endpoint projects ALL of them when `RequireReadOnEveryRoot` (`EnsureProjected(..., out serverProjected)`), after the column guard, so a column the caller left out of its FetchXML cannot read as "not filed".
- `EnsureProjected` has an overload reporting the attributes it ADDED (the ones the caller did not request). The response is stripped to `ReadableColumns` minus those, in rows and in `@formattedValues`, so a root column the caller did not ask for is not returned even when it is readable (`sprk_matter`). The twins were already stripped (not readable).
- General case: `RequireReadOnEveryRoot` is set only on the documents module (checked: `ExternalAccessModule.cs`), and the fetch endpoint is the only caller of `ScopeRows` for a module grid. Other `ScopeRows` callers (`ExternalMessageEndpoints.cs` record gate, `ExternalNotificationFeedEndpoints.cs` service requests) run modules without the every-root rule, where an absent dimension attribute already means "not matched" (fail closed). A new module that sets the flag gets the projection for free; `EveryModuleWithTheEveryRootRule_ProjectsEveryAttributeTheRuleReads` pins it.
- Tests (`ExternalMultiRootListReadTests`): the `executeFetch` double now projects rows through the EXECUTED FetchXML's `<attribute>` list and omits nulls (as `FakeDataverse` does for `$select`). A FetchXML naming only `sprk_project` withholds the P+M, P+work-assignment and P+twin rows, returns the P-only row, requests every rule attribute, and returns only `@logicalName`, `sprk_documentname`, `sprk_project`. A P+readable-M row is returned without `sprk_matter` (row and `@formattedValues`). A FetchXML naming the root columns gets them back as before.
- Fail-before (mutations, restored): (a) projecting only `AdditionalRootLookups` (the old behaviour) fails `..._NamingOnlySprkProject_StillWithholdsARowFiledToAHiddenRoot_...`; (b) the `ExceptWith(serverProjected)` strip removed fails `..._ListsARowFiledToTwoReadableRoots_WithoutTheUnrequestedRootColumn`.

**K3 (aligned).** `CommunicationThreadReadService.RootsOf` used `TryString`, so a stamp column present as a non-string JSON value (number, boolean, array) read as "not filed" (open), while `DocumentRootLinks.Raw` and `ParseDeleteTarget` read the same case as malformed (closed). `RootStamp` now returns the lookup for a string, null for an absent or JSON-null column, and a non-GUID marker for anything else, so the multi-root rule withholds the row. Tests (`ExternalMessageEndpointsTests`): number, boolean and array stamps are withheld; a JSON-null stamp is listed. Fail-before: reverting to the old read fails the three withheld cases.

**Known limits recorded (not fixed here)**

- **K1**: the message/feed `truncated` flag and the scan budget are computed BEFORE the root filter (`CommunicationThreadReadService.cs:441-447` for the thread, `:607` and `:625`/`:653` for the feed). `truncated` is a one-bit "more exists" signal over the unfiltered rows, and rows the rule withholds consume the scan budget, so a record with many hidden messages can show fewer visible ones than the bound.
- **K2**: the list `Truncated` flag includes withheld rows, and the module grid always answers `MoreRecords=false` (paging metadata is not propagated), so withholding thins a page rather than backfilling it.
- **K4**: invoice documents use the every-root rule (documents carry explicit typed links) while the invoice message thread uses any-parent (owner decision 2026-10-11). A caller who reaches an invoice through one parent sees its thread but not documents also filed to another parent. This is an over-hide, not a leak, pending the owner.

**Evidence.** `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. Unit, filter `External|Tier2Scope|CommunicationThread`: 2884 passed, 0 failed (plus `Tier2ScopeFilterInjectorTests` 14 passed after one more test). `tests/Spaarke.ArchTests`: 941 passed; no ledger cite moved.

## 12. Owner decision 2026-10-12: option B (secure roots only)

Supersedes the interim every-root rule of 11.1 (option A). Commit `d29fe0342`.

### 12.1 The rule

- The caller still reaches the row through a root it can read: the route parent, or the dimension it matched in a list or the grid. That part is unchanged.
- In addition, the caller needs Read on every linked root that is **secure** (`sprk_issecure = true` on that root record).
- A linked root the caller cannot read that is NOT secure no longer hides the row.
- A linked root the caller CAN read never needs a flag lookup.
- One implementation: `ExternalMultiRootRule.SecureRootsReadable(caller, flags, roots)`, renamed from `AllRootsReadable`. Every path calls it with flags from `ExternalMultiRootRule.ReadFlagsAsync`.
  - `DocumentRootLinks.AllReadableBy` is now `SecureRootsReadableBy(caller, flags)`.
  - `DocumentScopeRead.IsReadableUnder` takes the flags.
  - The module flag `RequireReadOnEveryRoot` is renamed `RequireReadOnSecureRoots`.
- **Which roots carry the flag.** All three: `sprk_project`, `sprk_matter` and `sprk_workassignment` (live metadata, recorded in `SecureRecordRoot` and `ExternalParticipationService.RootFlagSources`). No root entity lacks it, so there was no "unknown entity" case to decide. The rule only ever looks up these three; `sprk_regardingservicerequest` is still not consulted (11.2 K1).

### 12.2 Fail-closed cases

A linked root counts as SECURE, and the row is withheld, unless a successful read returned `sprk_issecure = false` for exactly that root:

- the flag read fails (non-success status) or throws (transport fault, token failure). `GetCollectionAsync` logs it, and an exception escaping the reader is caught by `ReadFlagsAsync`, which logs it and returns `SecureRootFlags.None`;
- the root record is not returned: it is missing, or invisible to the app identity;
- `sprk_issecure` is absent or null. A field-security-masked value reads empty, matching `ExternalParticipationService.FlagsFrom` and `RecordContainerResolver`;
- `sprk_issecure` is not a JSON boolean;
- the lookup value is not a GUID (malformed). It is withheld without a lookup;
- the root is past the per-request bound `MaxFlagLookups` = 500 distinct unreadable roots (logged);
- the reader answers for an id that was not asked. That answer is ignored.

### 12.3 Data access and batching

- **Reader.** `ExternalDataService.GetNonSecureRootsAsync`.
  - It reads app-only and reuses the existing `GetByIdsAsync` helper (chunked `or` filter on ids, `IdFilterChunkSize` = 100, paged, no new client).
  - It sends one query per root entity per 100 ids, `$select={entity}id,sprk_issecure`.
  - Only the distinct linked roots the caller cannot read are asked, across the whole page or row set: `ReadFlagsAsync` collects them before deciding any row.
  - No unreadable root means no query at all.
- **Why not `ExternalParticipationService.GetRootRecordFlagsAsync`.**
  - It is the other app-only reader of the flag, and it also selects `sprk_accesspermission` and `statecode`.
  - `ExternalDataService`, which runs the lists, does not hold it. Injecting it would change a constructor that about 70 test sites build.
  - The brief asked for id + `sprk_issecure` only.
  - Both readers fail closed on an empty flag.
- **Cache.** No request-scoped cache. Each path makes exactly one batched read per request, so a cache would add a scoped DI registration and save nothing. There is no cross-request cache, so a staff change to `sprk_issecure` applies on the next request.
- **Seams.**
  - The message thread and the feed hand `CommunicationThreadReadService` a `MessageRootsFilter`. The service calls it once with every candidate row's stamps and gets the per-row predicate back. It still has no reference to the external-access layer.
  - The grid fetch core and the message core take a `NonSecureRootReader`. The handlers pass `ExternalDataService.GetNonSecureRootsAsync`, now resolved from DI in `ExecuteScopedFetchAsync` and `GetMessagesAsync`.

### 12.4 Per-path verdicts

| Path | Rule now | Notes |
|---|---|---|
| `GET /{projects,matters,workassignments}/{id}/events` | Secure roots | One flag read per list (`ReadableEventsAsync`) |
| `GET .../todos` x3 | Secure roots | One flag read per list |
| `GET .../documents` x5 | Secure roots | Six links (typed + related twin); one flag read per list |
| `GET .../documents/{docId}/content` and `/versions` (5 kinds each) | Secure roots | The flags are read only once the document is under the route parent (a misfiled or missing document is refused with no flag read). The deny is still `DenyDocumentNotUnderParent`, byte-identical for missing, misfiled, secure-linked and unknown-state (tested) |
| `GET /{projects,matters,workassignments,servicerequests}/{id}/messages` | Secure roots | One flag read per thread |
| `GET /invoices/{id}/messages` | **Exempt (any parent)** | Owner 2026-10-11, kept: no flag is read, and even a secure other parent does not withhold (tested) |
| `GET /notifications` (message items) | Secure roots | One flag read per feed. Grant items are single-root (unchanged) |
| Module grid documents module (`/data/fetch`) | Secure roots | `ScopeRows` stays synchronous. The endpoint pre-fetches the flags over `module.RootLinks(row)` for every returned row, then passes them in. `ScopeRows` without flags fails closed (tested) |
| Module grid invoices module | Unchanged (union of parents) | Owner 2026-10-11 |
| **`DELETE /documents|todos|events/{id}` (task 032)** | **Unchanged: Read on EVERY parent** | FR-26 requires Read on the parent. A delete is a write, and the creator stamp plus every-parent Read stays the bar. `AuthorizeDeleteOwnAsync` does not call the multi-root rule, and nothing in it changed |

### 12.5 Tests

All tests read the flags through the REAL `ExternalDataService.GetNonSecureRootsAsync` over `ExternalRootEventReadTests.FakeDataverse`, extended to serve the root entity sets:

- the `or` filter on ids;
- `$select`-projected;
- a null value omitted;
- `FlagFault` / `FlagReadThrows`;
- `FlagReads` counts the queries.

The feed's `FeedData` now has a test token, so its base flag read reaches the fake. `ExternalModuleColumnAllowListTests` (column guard only) passes a reader that knows nothing.

| Path family | must-fire (secure, unreadable → hidden) | must-not-fire (non-secure unreadable → visible; secure readable → visible, no flag read) | fail-closed (throws / fault / missing / null flag / malformed → hidden) | batching (same unreadable root on N rows → one query per entity) |
|---|---|---|---|---|
| Lists (events, to-dos, documents x 3 routes), `ExternalMultiRootListReadTests` | `OptionB_ARowAlsoFiledToASecureRoot...` (9), `Todos_/Documents_AlsoFiledToASecureRoot...` | `OptionB_...NonSecureRoot..._IsListed` (9), `OptionB_...SecureRootTheCallerCanRead..._WithoutAFlagRead` (3), `OptionB_ARowFiledToOneRoot_CausesNoFlagRead` (3) | `OptionB_TheOtherRootsSecureStateCannotBeRead...` (15) | `OptionB_ManyRowsLinkedToTheSameUnreadableRoots_CostOneFlagQueryPerRootType` (3) |
| Single document (content, versions x 3 routes), `ExternalMultiRootDocumentRouteTests` | `ADocumentAlsoFiledToASecureRoot..._IsRefused...` (6), identical-body test (6) | `..._NonSecureRoot..._IsServed_OptionB` (6), `..._SecureRootTheCallerCanRead_IsServed_WithoutAFlagRead` (6) | `ADocumentWhoseOtherRootsSecureStateCannotBeRead...` (8), malformed (6, existing) | `ADocumentLinkedToTheSameUnreadableRootTwice_CostsOneFlagQuery_AndAMisfiledOneNone` (2) |
| Messages, `ExternalMessageEndpointsTests` | `AMessageAlsoFiledToASecureRoot..._IsWithheld_OnEveryRootRoute` (3) | `..._NonSecureRoot..._IsListed_OptionB` (3), `..._SecureRootTheCallerCanRead_IsListed_WithoutAFlagRead`, invoice exemption (secure parent, no read) | `AMessageWhoseOtherRootsSecureStateCannotBeRead...` (4), malformed / non-string stamps (existing) | `ManyMessagesStampedWithTheSameUnreadableRoots_CostOneFlagQueryPerRootType` |
| Feed, `ExternalNotificationFeedTests` | `AMessageAlsoFiledToASecureRoot..._IsNotInTheFeed` (2) | `..._NonSecureRoot..._IsInTheFeed_OptionB` (2), `..._SecureRootTheCallerCanRead_..._WithoutAFlagRead` | `AMessageWhoseOtherRootsSecureStateCannotBeRead...` (5, incl. malformed) | `ManyFeedMessagesStampedWithTheSameUnreadableRoot_CostOneFlagQuery` |
| Grid documents module, `ExternalMultiRootListReadTests` | `DocumentsModuleFetch_..._SecureRoot..._IsWithheld_OptionB` (2), existing withholding tests (now seeded secure) | `DocumentsModuleFetch_..._NonSecureRoot..._IsListed_OptionB` (4, typed + twin), readable-secure test asserts no flag read | `DocumentsModuleFetch_TheOtherRootsSecureStateCannotBeRead...` (5), `DocumentsModule_ScopeRows_WithoutFlags_...` | `DocumentsModuleFetch_ManyRowsLinkedToTheSameUnreadableRoot_CostOneFlagQuery` |
| The rule itself, `ExternalMultiRootRuleTests` (new) | secure withholds | non-secure opens; a readable root is never asked | reader throws; an unasked answer opens nothing; malformed; past the bound | 20 rows, one id asked |

**New tests: 100** (filtered unit run 2885 → 2985).

**Fail-before** (mutations in `SecureRootsReadable`, one build and one filtered run each, restored with `git checkout`):

- **(a) Old rule.** Every unreadable root withholds (option A). **34 of 2985 fail.** Every one is a must-not-fire or batching test across all five families plus the rule tests: lists 9 + 3, single document 6 + 2, grid 4 + 1, messages 3 + 1, feed 2 + 1, rule 2.
- **(b) Secure check removed.** An unreadable root never withholds. **84 of 2985 fail.** These are every must-fire and fail-closed test (throws / fault / missing / null flag) in all five families, plus the existing event withholding tests and the rule tests.
  - The malformed-lookup tests do not fail under (b). They are withheld by the rule's own non-GUID branch, before any flag is consulted, which is the intended structure.

### 12.6 Evidence

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `tests/unit/Sprk.Bff.Api.Tests`: 0 warnings, 0 errors.
- Unit, filter `External|Tier2Scope|CommunicationThread|Issue963`: **2985 passed, 0 failed, 0 skipped**.
- `tests/Spaarke.ArchTests`: **941 passed**. No ledger cite moved.
- Publish size: 12.7.

### 12.7 Publish size (BFF hygiene item 4)

The method of section 8, with one difference: each tree is exported with `git archive <rev> -- src/server config Directory.Build.props Directory.Packages.props global.json .editorconfig` into a short path (`C:\wt039bm`, `C:\wt039bh`, `C:\wt039bb`; removed afterwards) instead of a detached worktree.

The publish is `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, then `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.

| Tree | Commit | Files | Zip |
|---|---|---|---|
| Fresh `origin/master` | `37470dacc` | 192 | 36.461 MB |
| R3 head before this change | `b60f11898` | 192 | 36.524 MB |
| This change | `d29fe0342` | 192 | 36.531 MB |

- **Change delta** (vs R3 head): +0.007 MB.
- **Delta vs fresh master** (all R3 BFF work so far): +0.070 MB.
- File counts are equal. This is far under the +5 MB trigger, the 55 MB review and the 60 MB stop.
- **No new HIGH CVE**: no package reference changed.

### 12.8 Component justification (CLAUDE.md section 11)

The new surface consists of:

- `ExternalDataService.GetNonSecureRootsAsync` and `ReadSecureRootFlagsAsync`;
- `ExternalMultiRootRule.ReadFlagsAsync`, `UnreadableRoots`, `Stamps` and `MaxFlagLookups`;
- the `SecureRootFlags` type;
- the `NonSecureRootReader` and `MessageRootsFilter` delegates;
- `ExternalModuleDescriptor.RootLinks`.

The three answers:

1. **Existing.** `ExternalParticipationService.GetRootRecordFlagsAsync` reads the same flag; see 12.3 for why it was not reused here. `GetByIdsAsync` is reused, and the rule stays in the one existing class.
2. **Extension.** Each item extends an existing component: the rule class, the data service, the module descriptor, the read service's existing required parameter (its type changed from a per-row predicate to a batch filter). There are no new DI registrations and no packages.
3. **Cost of doing nothing.** Without the flag read the rule cannot tell a secure root from a non-secure one, so it either over-hides (option A, which the owner rejected) or leaks a secure root's rows (no check).

### 12.9 Not settled

- **Own flag vs effective flag: decided (main session, 2026-10-12): effective status, the stricter reading of option B.** See 12.10. Open for the owner only if they want the literal own-flag reading back.
- K1, K2 and K4 of 11.8 are unchanged. Under option B, K4 (invoice documents use the multi-root rule, the invoice thread does not) over-hides only when the other parent is secure.

### 12.10 Decision (main session, 2026-10-12): the rule uses the EFFECTIVE secure status

The stricter reading of option B. It supersedes the "own flag" wording of 12.1 and 12.3 wherever they differ. Commit `edd2ad4df`.

- **Rule.** A linked root the caller cannot read counts as non-secure only when its **effective** status is non-secure (UAC-r2 task 174: a record filed under a secure record is secure before its own flag catches up). A work assignment W whose own flag is false but which is filed under secure matter M is secure, so the row is withheld even when it carries no M stamp.
- **One walk, reused.** `GetNonSecureRootsAsync` reads each root's own `sprk_issecure` over the Web API as before. For each table, the ids whose own flag is `false` go through `EffectiveRootFlags.ReadAncestryAsync` (the ONE filing walk, `SecureRootInheritance.ReadSecureParentsOfManyAsync`) and `EffectiveRootFlags.Fold`, the same pair `ExternalParticipationService.GetEffectiveRootRecordFlagsAsync` uses. No second walk was written.
- **Seam.** `ExternalDataService` takes the app-only `IGenericEntityService` as an optional LAST constructor argument (`filing = null`; registered unconditionally in GraphModule, so the typed-client factory supplies it). The about 70 test construction sites are unchanged. `ExternalParticipationService` does the same.
- **Matter and own-secure roots.** A matter files under nothing, so its own flag is its effective flag (no walk). A root whose own flag is `true` is secure already (no walk).
- **Batching.** One walk per table for the distinct own-open ids of the whole request: one batched read of their rows, one batched read of their parents, per level, chunked at 200 ids (`SecureRootInheritance.IdsPerQuery`). A single id uses the one-record shape (two point reads). Never one walk per row. The existing 500-distinct-roots bound in `ExternalMultiRootRule.MaxFlagLookups` is unchanged and applies before any read.
- **Fail closed.** The root counts as secure when: its own-flag read fails or returns nothing or a non-boolean (as before); the walk's read of the root or of an ancestor throws or faults; an ancestor's `sprk_issecure` is empty; the chain is longer than `MaxFilingDepth` (4); a pair type cannot be read; or no filing reader is configured. All of these fold to `RootRecordFlags.Unreadable`, which is secure.
- **Unchanged shared-walk behaviour (noted, not changed).** The walk treats an ancestor record that does not exist as conferring nothing. Dataverse lookups cannot dangle, so this only matters in a delete race; changing the shared walk was out of scope. A root that is itself missing is secure (the own-flag read returns nothing).

#### Test map (new tests: 61; filtered unit run 2985 to 3046)

The test double: `ExternalRootEventReadTests.FakeDataverse` gains a filing world (`AddRoot` registers the same record there, `FileWorkAssignmentUnder` files W under a matter or project), a Moq `IGenericEntityService` (`FilingReader`) that answers the walk's `RetrieveMultipleAsync` by the id condition, a `FilingReadLog` that counts reads per entity, and the fault switches `FilingReadThrows` and `FilingFaultEntity`. `Fixture`, `NewService()` (with `withFilingReader: false` for the no-reader case) and the feed's `FeedData` pass it in.

| Family | must-fire (own flag false, under a secure matter, no M stamp: hidden) | must-not-fire (under a non-secure matter: visible) | fail-closed (walk throws, ancestor read throws, ancestor flag empty: hidden) | batching |
|---|---|---|---|---|
| The read itself, `ExternalMultiRootRuleTests` | `EffectiveStatus_AWorkAssignmentWithAFalseOwnFlag_UnderASecureMatter_IsSecure`, `..._UnderASecureProject_IsSecure` | `..._UnderANonSecureMatter_IsNotSecure` (also pins the 2 reads), matter and own-secure roots cause no walk | `..._TheRootCountsAsSecure_FailClosed` (5: every read throws, ancestor read throws, W's read throws, ancestor flag empty, no filing reader) | `..._ManyRoots_CostOneOwnFlagQuery...` (20 W, 2 matters: 1 flag query, 2 walk reads), `..._OneRootAskedByManyRows_IsWalkedOnce` (25 rows) |
| Lists (events, to-dos, documents), `ExternalMultiRootListReadTests` | `Effective_ARowFiledToAWorkAssignment...UnderASecureMatter_IsWithheld...` (6) | `..._UnderANonSecureMatter_IsListed` (6) | `Effective_WhenTheFilingChainCannotBeRead_TheRowIsWithheld_FailClosed` (9) | `Effective_ManyRowsLinkedToTheSameWorkAssignment_...` (3: 12 rows, 2 W, 1 flag query, 2 walk reads) |
| Single document (content, versions), `ExternalMultiRootDocumentRouteTests` | `Effective_ADocumentFiledToAWorkAssignment...UnderASecureMatter_IsRefused...` (4, typed and twin link) | `..._UnderANonSecureMatter_IsServed` (4) | `Effective_WhenTheFilingChainCannotBeRead_TheDocumentIsRefused_FailClosed` (6) | `Effective_ADocumentLinkedToTheSameWorkAssignmentTwice_CostsOneWalk_AndAMisfiledOneNone` (2) |
| Grid documents module, `ExternalMultiRootListReadTests` | `Effective_DocumentsModuleFetch_...UnderASecureMatter_IsWithheld` (typed and twin) | `..._UnderANonSecureMatter_IsListed` | `Effective_DocumentsModuleFetch_WhenTheFilingChainCannotBeRead...` | covered by the read-level batching tests (the endpoint makes one read per request) |
| Messages, `ExternalMessageEndpointsTests` | `AMessageFiledToAWorkAssignmentWithAFalseOwnFlag_IsWithheldOnlyWhenItsMatterIsSecure(true)` | `(false)` | `AMessageFiledToAWorkAssignment_WhoseFilingChainCannotBeRead_IsWithheld_FailClosed` | read-level |
| Feed, `ExternalNotificationFeedTests` | `AMessageFiledToAWorkAssignmentWithAFalseOwnFlag_IsInTheFeedOnlyWhenItsMatterIsNotSecure(true)` | `(false)` | `..._WhoseFilingChainCannotBeRead_IsNotInTheFeed_FailClosed` | read-level |

#### Evidence

- `dotnet build src/server/api/Sprk.Bff.Api/` and `tests/unit/Sprk.Bff.Api.Tests`: 0 warnings, 0 errors.
- Unit, filter `External|Tier2Scope|CommunicationThread|Issue963`: **3046 passed, 0 failed, 0 skipped** (was 2985).
- `tests/Spaarke.ArchTests`: **941 passed** (unchanged).
- **Fail-before.** With the walk removed from `GetNonSecureRootsAsync` (the own-flag behaviour of `d375c597d`; `ancestry` forced to `null`, restored with `git checkout`), the new and effective-status tests (filter `Effective|FalseOwnFlag|WhoseFilingChainCannotBeRead`, 131 tests) give **46 failed, 85 passed**. The 46 are every must-fire, fail-closed and batching-by-walk test in all six families. The must-not-fire visibility tests pass under both behaviours, as expected; the one that also pins the walk's read log (`EffectiveStatus_..._UnderANonSecureMatter_IsNotSecure`) is among the 46.
- **Publish size** (method of 12.7, tree `edd2ad4df` exported with `git archive` into `C:\wt039bz`): 192 files, zip **36.531 MB**, delta against 12.7's 36.531 MB: +0.000 MB at three decimals. No package reference or csproj changed, so no new CVE exposure. The scratch folder `C:\wt039bz` is left behind (removal of that path is blocked in this environment); delete it by hand.

### 12.11 Review fixes (F4, K1-K4) - rule: Read on every EFFECTIVELY secure linked root; unknown counts as secure

| Item | Outcome |
|---|---|
| **F4** route ledger prose | The ten entries that said "Read on EVERY root the document is filed to" now say "Read on every SECURE linked root, by effective status (`ExternalMultiRootRule` / `ExternalDataService.GetNonSecureRootsAsync`; unknown counts as secure)". The two event entries say the same (`ReadableEventsAsync`). Line cites in the touched entries were re-read against the current `ExternalProjectDataEndpoints.cs`: download Read check `:606`, download scoping `:624`, versions Read check `:1410`, versions scoping `:1420`, requester check `:964`, events Read check `:1499`, `AuthorizeDeleteOwnAsync` `:1724-1771`, `DeleteOwnDocument` delete `:1820`, `DeleteAuthorizedRowAsync` `:1966`, service-request delete `:1930` / `:1943` / `:1959`. The delete-own comment says a delete keeps the every-parent bar and option B governs reads only. |
| **K1** named-but-absent filing parent | `SecureRootInheritance` gains an opt-in `absentIsUnknown` (default `false`) on `ReadSecureParentsAsync`, `ReadSecureParentsOfManyAsync`, `ClimbAsync`, `DecideParentsIntoAsync`, `DecideParentsCoreAsync` and `DecideNamedParents`. When set, a parent the row NAMES but the read does not return (and a climbed row that is no longer there) makes the answer Unverifiable. `EffectiveRootFlags.ReadAncestryAsync` exposes it as `absentParentIsUnknown` (default `false`); `ExternalDataService.GetNonSecureRootsAsync` passes `true`, so the fold counts the root as secure. The write-time semantics (UAC-r2: an absent record confers nothing) are the default and are unchanged; a test pins both shapes of the walk (one record, batched) under default and opt-in. |
| **K2** incomplete read must not shrink a list silently | `GetNonSecureRootsAsync` now returns `NonSecureRootsRead(NotSecure, Complete)`; `NonSecureRootReader` returns it. `Complete` is false when a flag chunk did not complete (`CollectionReadOutcome != Complete`) or a root folded to Unreadable (walk fault, unreadable or missing ancestor, empty ancestor flag, chain past the bound, no filing reader). `SecureRootFlags.IsComplete` carries it (also false past `MaxFlagLookups` and on a throwing reader). `GetDocumentsAsync`, `GetTodosAsync` and `GetEventsAsync` set `Truncated = true` when the flag read was incomplete AND a row was withheld. A complete read that withheld a genuinely secure row does not. |
| **K3** project under a secure matter through the pair | `FakeDataverse.FileProjectUnderByPair` seeds `sprk_regardingrecordid` (text GUID) and `sprk_regardingrecordtype` on the project, and the `sprk_recordtype_ref` row whose `sprk_recordlogicalname` names the parent table, exactly the columns the walk reads. `Effective_AProjectWithAFalseOwnFlag_UnderASecureMatter_IsSecure` (must-fire), a non-secure-matter must-not-fire, and a named-but-absent matter case. |
| **Test placement** | `tests/integration/auth/ExternalAccess/ExternalMultiRootSecureRootsTests.cs` (21 tests, KEEP path, compiled by the existing auth glob; no csproj change). Must-fire / must-not-fire pairs at the route boundary for events, to-dos and documents: secure vs non-secure unreadable matter; a work assignment with a false own flag under a secure vs non-secure matter; a work assignment whose named matter is absent; a project under a secure vs non-secure matter through the pair. `ExternalMultiRootRuleTests` stays where it is (it also tests the rule below the route). |
| **Coverage 6** | `GET /invoices/{id}/documents` and `GET /servicerequests/{id}/documents` (both `GetDocumentsAsync`): one must-fire and one must-not-fire each, in the new file, through the real routes and the real rights arms (`GetInvoiceParentsAsync`, `GetServiceRequestRequesterAsync`) over `FakeDataverse`, which now serves `sprk_invoices` / `sprk_servicerequests` single-row reads. |
| **K4** `filing = null` seam | No code change. The optional `IGenericEntityService? filing = null` argument of `ExternalDataService` is supplied by DI (`ExternalAccessModule.cs:95` through `ActivatorUtilities`; the registration is `GraphModule.cs:140`). If the registration were dropped, `ReadAncestryAsync` answers every own-open root Unverifiable, so the result is OVER-hiding (every unreadable linked root counts as secure), never a leak. A DI-registration test is banned (ADR-038 B3), so none was added; the no-reader case is covered at the behaviour level (`..._no filing reader is configured`). |

**Known limits left (K-class), recorded rather than fixed:**

- **K2 residual: message thread and feed.** An incomplete flag read withholds rows from `GET .../messages` and the notification feed with no signal. Adding one would change a response contract (the thread and feed DTOs carry no `Truncated`), so the shape is unchanged. The grid documents module (`/data/fetch`) is the same: its `MoreRecords` means paging, not this.
- **K2 is conservative.** `Truncated` is set whenever the flag read was incomplete and any row was withheld, including a row that a complete read would also have withheld. It never hides that a list may be short.
- **A named-but-absent parent now reports an incomplete read** (`Complete = false`) as well as folding to secure, so a list that withheld a row because of it says `Truncated`. This is deliberate: the status was unknown, not known-secure.
- **Stale cites outside the touched entries.** Other ledger entries (the to-do list `:411-412`, `:445-446`, `:930-936`, the document list `:446`, the invoice list `:531` / `:886`, the module fetch `:264` / `:271` / `:372`) still carry the line numbers of an older `ExternalProjectDataEndpoints.cs` / `ExternalModuleDataEndpoints.cs`. They were not touched by this fix and were not re-verified.
- **A project whose pair names a deleted matter stays secure permanently.** A project files under a matter only through the polymorphic pair (`sprk_regardingrecordid`, a text GUID, not a real lookup). If that pair names a matter that was deleted, the read-time opt-in (`absentParentIsUnknown`) treats the parent as unknown, so the project counts as secure for good: its rows are withheld and the list says `Truncated`. This follows the owner's "unknown is secure" policy; cleaning the dangling pair is the way out.
- **`Fold` over-reports Unreadable.** `EffectiveRootFlags.Fold` reports `Unreadable` (so `Complete = false`) even when a real secure parent was also found, so `Truncated` may over-report. This is conservative: the root is secure either way.

#### Evidence

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Unit, filter `External|Tier2Scope|CommunicationThread|Issue963|SecureRoot|EffectiveRoot`: **3197 passed, 0 failed, 0 skipped**. New tests: 45 (rule-level 12 in `ExternalMultiRootRuleTests`, list-level `Truncated` 12 in `ExternalMultiRootListReadTests`, 21 in the new KEEP file). The walk's other consumers (filter `NoAccess|DocumentIndexParent|SecureChild|ExternalParticipation|Inherit|AccessibleRecordSet|SpeContainerMembership`): 1187 passed, 0 failed.
- `tests/Spaarke.ArchTests`: **941 passed**.
- **Fail-before (K1).** With `absentParentIsUnknown: true` changed to `false` in `GetNonSecureRootsAsync` (restored afterwards): filter `ExternalMultiRootSecureRootsTests|ExternalMultiRootRuleTests` gives **5 failed, 47 passed**. The 5 are every named-but-absent case: the work assignment and the project at the read level, and the three route kinds.
- **Fail-before (K2).** With `FlagReadCutListShort` returning `false` (restored afterwards): filter `ExternalMultiRootListReadTests|ExternalMultiRootRuleTests` gives **6 failed, 154 passed**, the six `OptionB_AnIncompleteFlagRead_ThatWithholdsARow_SetsTruncated` cases (events, to-dos, documents, each with a thrown and a faulted read). The must-not-fire tests (complete read with a secure row withheld; a fault that withholds nothing) pass under both.

#### F4 follow-up (an unresolved root makes the read incomplete)

- **Defect.** `GetNonSecureRootsAsync` left `Complete` true when a flag chunk succeeded but an asked root was unresolved: no row returned (missing, or invisible to the app identity), `sprk_issecure` absent (field security), or a non-boolean value. The root rightly counted as secure, but the rows it withheld did not say `Truncated`.
- **Fix.** After the row pass, `complete = false` when fewer asked ids came back with a row AND a boolean `sprk_issecure` than were asked. The root still counts as secure.
- **Tests (14 new cases).** Rule level (`ExternalMultiRootRuleTests`): a theory of three unresolved shapes (no row, absent flag, non-boolean flag) gives `Complete == false` with `NotSecure` unchanged; a fully resolved read gives `Complete == true`; the depth-2 chain (work assignment, then project, then an ABSENT matter) folds to secure under the read opt-in and keeps the legacy answer under the default. List level (`ExternalMultiRootListReadTests`, events / to-dos / documents): an unresolved matter that withholds a row sets `Truncated`; a non-boolean flag that withholds a row sets `Truncated`; a resolved non-secure matter lists the row with no `Truncated`. `FakeDataverse.AddRootWithRawFlag` seeds the non-boolean flag. `ProjectEvents_AlsoFiledToASecureMatterTheCallerCannotRead_AreWithheld` now seeds the secure matter it always claimed to have (it passed before only because the matter was never seeded, which is exactly the unresolved case).
- **Counts.** Unit, same filter as above: **3211 passed, 0 failed** (3197 before). `tests/Spaarke.ArchTests`: **941 passed**. Build: 0 warnings, 0 errors.
- **Fail-before.** With the fix reverted (restored afterwards), filter `ExternalMultiRoot|ExternalRootEventReadTests` gives **9 failed, 311 passed**: the three rule-level unresolved cases and the six list-level `Truncated` cases (unresolved matter and non-boolean flag, each for events, to-dos, documents). The must-not-fire tests pass under both.

### 12.12 Master merge (`b0f8772b2`) and the intermediate gap on the read path

**Merge resolution.** Master's #1630 added intermediates to `SecureRootInheritance`: a work assignment's typed `sprk_regardinginvoice` / `sprk_regardingevent` lookup is followed to the matters and projects that invoice or event belongs to (`IntermediateFilingColumns`, `IntermediateParentColumns`). It also split the naming of a row's parents from the decision over them: `NameParentsAsync` returns a `NamedParents` record (`Named`, `Unknown`, `Unrepeated`, `ViaIntermediateOnly`), and `DecideNamedParents(naming, reads, absentIsUnknown)` decides over it. R3's K1 opt-in (`absentIsUnknown`) was re-threaded onto that shape:

- `DecideParentsCoreAsync` gained `IntermediateReads? intermediates` BEFORE `bool absentIsUnknown`, so a positional `absentIsUnknown` no longer binds. The non-batched caller in `DecideParentsIntoAsync` passes it by name (`absentIsUnknown: absentIsUnknown`); the write-time caller (`:662`, passes `intermediates` positionally) and `DecideParentsAsync` (no flag) keep the default.
- The batched `DecideParentsIntoAsync` reads every row's intermediates in chunks first (`ReadIntermediatesIntoAsync(batched: true)`), names each row, then decides with `DecideNamedParents(naming, reads, absentIsUnknown)`.
- Both read shapes (point reads and chunked) were already tested for a NAMED matter or project that is absent (K1); neither was tested for an absent INTERMEDIATE.

**The gap.** The opt-in reached `DecideNamedParents` (the matters and projects a row names) but not `NameParentsAsync`. An intermediate the read does not return (deleted in a race, or not visible to the reader) has `IntermediateRead.Parents == null`; `read.Parents ?? Array.Empty<...>()` then named no parents through that route, so on the READ path a work assignment with a false own flag, filed through such an invoice or event, was counted as non-secure (visible), where an absent matter or project folds to secure. Only a FAULTED intermediate read became unknown.

**Fix.** `NameParentsAsync` takes `bool absentIsUnknown = false`; both callers pass it (`DecideParentsCoreAsync`, and the batched `DecideParentsIntoAsync` loop). In the intermediate loop, `read.Parents is null && absentIsUnknown` sets `unknown ??= "the {intermediate.Table} it is filed under was not returned (missing, or not visible to the reader)"`. One branch covers both shapes, because the point read and the chunked read both record an absent row as `Parents == null`. An intermediate that EXISTS but names no matter or project (`Parents` empty) is not unknown. With the flag off (default, every write-time caller) the code path is byte-for-byte the previous one.

**Tests (30 new cases).**

- `ExternalMultiRootRuleTests` (12 cases; each theory runs for `sprk_invoice` and `sprk_event`): must-fire (absent intermediate: secure, `Complete == false`, read log pins the one intermediate read); the same batched with two work assignments (only the one with the absent intermediate is withheld); must-not-fire (present intermediate of a non-secure matter: not secure, complete; of a secure matter: secure); a present intermediate that belongs to nothing is NOT unknown; default unchanged vs opt-in through `EffectiveRootFlags.ReadAncestryAsync` for one record and for the batched shape.
- `ExternalMultiRootSecureRootsTests` (18 cases = events / to-dos / documents x invoice / event): through the real routes, a row linked to a readable project and to a work assignment filed through an absent intermediate is withheld; through a present intermediate of a non-secure matter it is shown; of a secure matter it is withheld.
- `FakeDataverse` gains `FileWorkAssignmentUnderIntermediate` and `AddIntermediate` (the invoice's `sprk_matter` / `sprk_project`, the event's `sprk_regardingmatter` / `sprk_regardingproject`).

**Evidence.**

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Unit, filter `SecureRoot|EffectiveRoot|ExternalMultiRoot|ExternalRootEventRead|NoAccess|SecureChild|DocumentIndexParent|ExternalParticipation|Inherit|AccessibleRecordSet|RecordContainer|Intermediate|1630`: **1965 passed, 0 failed** (1935 before the 30 new cases).
- **Fail-before.** With the new branch disabled (`if (false && read.Parents is null && absentIsUnknown)`, restored afterwards), filter `ExternalMultiRootRuleTests|ExternalMultiRootSecureRootsTests` gives **12 failed, 75 passed**: the 6 route must-fire cases and, at rule level, the absent-intermediate must-fire, the batched one and the default-vs-opt-in one (2 tables each). The must-not-fire and exists-but-belongs-to-nothing tests pass under both behaviours, as do the default-path asserts.

### 12.12a Scoped re-check of the merge + intermediate fix (fable, 2026-10-12)

No F-class finding. Build 0 errors; 1733 passed on the access filter. Known limits, both inherited from master (#1630/#1616), recorded rather than changed:
- **K2:** IntermediateParentColumns keeps only matter/project roots of an invoice or event, so a work assignment filed through an event whose own root is another work assignment W2 confers no parent. On the read path the row is judged by the platform's inheritance, which does not make W secure through W2; this is master's documented filed issue on IntermediateFilingColumns, identical at write time. Changing it on the read path alone would make the read rule stricter than inheritance. Owner call if wanted.
- **K3:** an unrepeated pair on a work assignment makes the answer Unverifiable, so Fold (tests IsKnown) withholds the row and the list says Truncated. Fail-closed; a usability limit only.
