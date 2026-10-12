# Task 039 - External event reads for Matter and Work Assignment (FR-03, server)

> Branch `worktree-agent-a0356b48145f4ef5f`, based on `fc71af7fb` (R3 head after 037). Rigor FULL (tags `bff-api`, `auth`, `testing`).
> Code commit `02150553b`. No push, no PR (see "Not done").

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
