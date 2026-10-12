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
