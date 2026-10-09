# Task 066: `sprk_event.sprk_eventstatus` deprecation inventory (D-28, FR-62)

Inventory taken 2026-10-09 on `origin/docs/ontology-platform-design` (53b385d59). Every reference was found by a case-insensitive search of
`src/`, `tests/`, `scripts/` and `docs/` for `sprk_eventstatus`, then read in context (the 13-file figure in the POML was taken on master and
counted the bare word "eventstatus", which also matches `EventStatusCode`, `UpdateEventStatusAsync` and similar, none of which touch the column).

**Result:** the column is read or written by 6 working surfaces in `src/` (all moved), plus the Dataverse solution export (11 views, form, entity
definition, 5 stale web-resource copies: left for the owner, see "Owner decisions"). After this task a repo guard test
(`EventStatusDeprecationTests.NoSourceFile_ReadsOrWrites_TheDeprecatedColumn`) fails on any `src/` or `scripts/` file other than the export that names the column.

## Live schema (spaarkedev1, `describe` of `sprk_event`, 2026-10-09)

| `sprk_eventstatus` (deprecated) | `statuscode` (authoritative) [statecode] |
|---|---|
| 0 Draft | 1 Draft [0 Active] |
| 1 Open | 659490001 Open [0] |
| 2 Completed | 659490002 Completed [0 Active] |
| 3 Closed | 659490003 Closed [0 Active] |
| 4 On Hold | 659490006 On Hold [0] |
| 5 Cancelled | 659490004 Cancelled [1 Inactive] |
| 6 Reassigned | 659490007 Reassigned [0] |
| 7 Archived | 2 No Further Action [1 Inactive] |
| (none) | 659490005 Transferred [1] |

Every deprecated value has a `statuscode` equivalent, so the POML escalation trigger (a value with no equivalent) did not fire. Archived maps to No
Further Action, the status the archive commands already set.

Live disagreement (83 rows, `GROUP BY statuscode, sprk_eventstatus`): 38 Draft rows have the column null; 18 Open rows null; the column is `Open` on 7
rows whose statuscode is Completed, and `Completed` on 1 Draft + 2 Open rows; `Reassigned` on 1 Open row. 27 rows are Open by statuscode (coordinator re-count, round 2); V-1 "My Tasks Open" returns 15 rows and misses 18 of the 24 open tasks.

## Inventory (file:line is on the base commit, before this task's change)

R = reads or filters on the column, W = writes it, D = documentation or comment only.

### BFF and shared server (`src/server`): 4 files, 19 lines (2 W code paths, 17 D)

| File:line | R/W | Change |
|---|---|---|
| `Sprk.Bff.Api/Services/Communication/CommunicationCreateTaskApplyService.cs:193` (const) , `:690` | W | Create-task apply and ad-hoc create PATCH wrote the column (`Status` Choice). Now writes `statuscode` and its paired `statecode` (`StatusMappings`, Number mappings, `EventStatusCode.GetStateCode`). A value outside the live set is sent as a String mapping so the existing fail-loud coercion still returns 422 (FIELD_PATCH_FAILED). |
| `…CreateTaskApplyService.cs:197`, `:615`, `:669` | W | Reconcile undo (soft-cancel) wrote `5`. Now `statuscode = 659490004` and `statecode = 1`; `UndoCreateTaskResult.NewStatus` is now the statuscode value. |
| `…CreateTaskApplyService.cs:36,94,117,155,170,189,195,666` | D | Doc comments reworded to statuscode. |
| `Api/CommunicationEndpoints.cs:439,446,1191` | D | Comments reworded. |
| `Services/Communication/Models/QueueFeedModels.cs:99` | D | `TaskStatus` doc reworded (statuscode). |
| `Infrastructure/ExternalAccess/ExternalDataService.cs:176` | D | Already read and wrote `statuscode` (task 097); comment now states D-28 instead of "pending owner decision". |

Already on `statuscode` and untouched: `EventEndpoints.cs`, `TodoGenerationService.cs`, `DailyBriefingCollector.cs`, `TaskActionCore.cs`,
`WorkspaceAiService.cs`, `DataverseWebApiService.cs`, `Models.cs` (`EventStatusCode`).

### Client and web resources (`src/client`, `src/solutions`): 9 files

| File:line | R/W | Change |
|---|---|---|
| `solutions/EventCommands/sprk_event_ribbon_commands.js:84-92` (map 0-7) | W/R | Map now the live statuscode values; added `StateOfStatus`, `_statusPayload`, `_saveThenSetStatus`. |
| `…ribbon_commands.js:190-203` (`IsEventActive`) | R | Now reads `statuscode` (open work: Draft, Open, On Hold, Reassigned; same set as `EventStatusCode.IsOpenWork`); falls back to the header `statecode` when `statuscode` is not on the form. |
| `…ribbon_commands.js:281,345,482,591,650,714,750` (Complete, Cancel, Reassign, Close, Archive, On Hold, Resume: `getAttribute("sprk_eventstatus").setValue`) | W | the status is now written (round 1 believed `statuscode` was not a form attribute; round 2: the main form has it, but the PATCH works either way) by `_saveThenSetStatus`: form save (dates, owner, history), then a `statuscode` + `statecode` PATCH, then form refresh. |
| `…ribbon_commands.js:823` (bulk complete), `:881,912,942,972,1016` (homepage bulk), `:1072,1079` (homepage archive) | W | PATCH body is `{statuscode, statecode}` (+ `sprk_completeddate` for complete). Archive is one update (was two). |
| `solutions/EventCommands/EventRibbonDiffXml.xml:11`, `…ribbon_commands.js:24` | D | Value list updated. |
| `solutions/EventsPage/src/registerEventHandlers.ts:34,52,57,75,99,114` | W | Bulk status handlers write `{statuscode, statecode}`; archive writes No Further Action + Inactive in one update; `EventStatus` map is the live set. |
| `solutions/EventDetailSidePane/src/types/EventRecord.ts:40,41,43,94,167,190` | R | `sprk_eventstatus` removed from the interface and from the header and full `$select` lists (nothing used the value); the unused `EventStatus` enum and helpers now carry live statuscode values and `ACTIVE_EVENT_STATUSES` equals `IsOpenWork`. |
| `client/shared/Spaarke.Communication.Components/…/TaskReconcileTab.tsx:74,80-86,418` | W (wire) | `EVENT_STATUS` was the 0-5 set sent as `status` to the BFF apply endpoints; now the live statuscode values. |
| `client/shared/Spaarke.Events.Components/…/CalendarWorkspaceWidget.tsx:163-192,634` | R | The Event Status filter chip filtered `sprk_eventstatus eq <0-7>`; now `statuscode eq <live value>`, options are the 9 live statuses. |
| `client/external-spa/…/EventsCalendar.tsx:182`; `client/shared/Spaarke.AI.Widgets/…/register-workspace-widgets.ts:774`; `client/shared/Spaarke.UI.Components/…/fetchXmlOverlay.ts:97`; `…/storybook/OptionSetMultiFilterChip.stories.tsx:55,66,190,251` | D | Comment or example text only; reworded or renamed to `statuscode`. |

`EventsCalendar.tsx` (external SPA) and `ExternalDataService` already used `statuscode` (task 097 review F2).

### Scripts and docs (outside `src/`): 6 files

| File:line | R/W | Change |
|---|---|---|
| `scripts/Load-DemoSampleData.ps1:368`, `scripts/demo-data/demo-records.json:100,105,110,115,120` | W | Demo loader wrote `100000000` (not a valid option of the column, so every demo event create was a 400). Writer removed; the row takes the platform default statuscode (Draft). |
| `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md:172,244`, `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md:227` | D | Example filters now `statuscode`; line 244 states the view still filters the old column (see V-1). |
| `docs/data-model/sprk_event-related-tables.md:59` | D | Row marked DEPRECATED (D-28). Row stays: the column exists. |

### Tests (before this task)

`EventReadPathTests.cs:155,156,170` assert the column is NOT selected (kept, still true). `CreateTaskApplySeamTests.cs` (9 lines) and
`CommunicationRecordAuthorizationContractTests.cs:597` asserted the old writes (updated to the new writes).

### Dataverse solution export `src/dataverse/solutions/SpaarkeMaster` (NOT changed: owner decision, see below)

| Item | Count | Detail |
|---|---|---|
| Entity.xml | 4 lines | The column definition (stays until removal is approved). |
| Event modal form (`{90d2eff7-…}.xml` and `_managed`) | 2 | The column as an editable field (stays: POML constraint; see owner decision 2). This is the modal form, not the main form; the main form already has `statuscode`. |
| Views (`SavedQueries`) that FILTER `sprk_eventstatus eq 1` (R) | 6 | V-1 My Tasks Open `12a510e4`; V-2 Matter All Tasks Open 7 Days `1e26ed14`; V-3 All Tasks Open 7 Days `491e5733`; V-4 My Events Open `9399ba21`; V-5 All Tasks Open `e0d27d71`; V-6 All Tasks Open subgrid `9268f905`. |
| Views that only DISPLAY the column (layout cell + fetch attribute) (R) | 5 | All Tasks subgrid `174210c3`, All Tasks `32c1041a`, All Events `b836398f`, All Deadlines `db70b5a9`, Matter All Events `f0221227`. |
| Web resources in the export | 5 | `sprk_event_ribbon_commands.js` (a stale pre-task-098 copy, 43 refs; it is overwritten when the source above is deployed), and four bundles (`sprk_corporateworkspace`, `sprk_eventdetailsidepane.html`, `sprk_eventspage.html`, `sprk_spaarkeai`): generated, change when their source rebuilds (POML scope). |

### Counts by surface

| Surface | Files | Reader | Writer | Doc-only |
|---|---|---|---|---|
| BFF / shared server | 4 | 0 | 1 (both apply paths + undo) | 3 |
| Ribbon + Events grid + side pane + calendar + reconcile tab | 6 | 3 | 4 | 0 |
| Doc-only client comments | 4 | 0 | 0 | 4 |
| Scripts / docs | 6 | 0 | 1 | 5 |
| Dataverse export (untouched) | 4 kinds (entity, form, 11 views, 5 web resources) | 11 views | 0 | |

### Live readers in Dataverse found in review round 2 (not in the solution export; all unchanged, all part of the removal prerequisites)

| Item | Reads | Detail |
|---|---|---|
| `sprk_gridconfiguration` "Event Default" and "Event Default - Calendar Widget" | R | `filterChips.allowlist` names the column; their default view is V-5 (All Tasks Open). |
| `sprk_chartdefinition` "Matter Tasks" | R | filter `sprk_eventstatus ne 2`. |
| `sprk_chartdefinition` "TASKS & EVENTS" | R | filter `sprk_eventstatus eq 1`. |
| Charts "Due Date Count Card" and "Due Date Card List" | R | inherit V-3's filter (All Tasks Open 7 Days). |
| Personal view "My Tasks Open" (`userquery` 9b207347) | R | filters the column like V-1. |
| "Event modal form" (`90d2eff7`) | R/W | still carries the column as an editable field. The main form already has `statuscode` (correcting the round-1 note, which said it was not on the form). |

## Owner decisions needed (Dataverse changes are out of this task)

1. **Views V-1 to V-6 (and the grid configurations, charts and personal view in the table above) filter the deprecated column and give wrong answers today.**
   "My Tasks Open" (the My Tasks widget source) returns 15 rows and misses 18 of the 24 open tasks. Change each condition to
   `statuscode eq 659490001` (or `in (1, 659490001, 659490006, 659490007)` for open work) and swap the layout cell/attribute to `statuscode`. The solution
   export is refreshed from Dataverse, so this is a live view edit followed by a re-export.
2. **The "Event modal form" (`90d2eff7`) still carries `sprk_eventstatus` as an editable field.** Replace it with Status Reason (`statuscode`) on that form
   (the main form already has `statuscode`, so the ribbon enable rule reads it; this corrects round 1, which assumed it was missing). Until then a user can edit
   the deprecated column from the modal.
3. **Deployment coupling (hardened in round 2).** The request field was renamed `status` to `statusCode` on the create-task apply and ad-hoc endpoints, and a
   body that still carries `status` is refused 422 `STATUS_FIELD_RETIRED` with nothing written (values 1 and 2 are valid in both vocabularies, so a value check
   could not catch an old client). A mixed deploy therefore fails loudly instead of mis-mapping. Deploy the BFF with every bundle that carries the reconcile
   tab (Console via AI.Widgets, LegalWorkspace, the CommunicationReconciliation code page). A new client against an old BFF has its `statusCode` ignored
   (the old BFF reads `status`), so the task is created without the chosen status.
4. **Column removal** (recommendation): after decisions 1 and 2 are applied and verified (views, grid configurations, charts, personal view and the modal form), remove the form placements, then the column. Nothing in `src/`
   or `scripts/` reads or writes it any more; the guard test will stay green and should then be deleted with the column. Do not remove before 1: the
   six views, two grid configurations, charts and the personal view break.
5. **Data**: 38 Draft and 18 Open rows have the column null, 10 rows disagree. No backfill is needed once readers are on `statuscode`.
