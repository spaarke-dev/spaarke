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
rows whose statuscode is Completed, and `Completed` on 1 Draft + 2 Open rows; `Reassigned` on 1 Open row. 26 rows are Open by statuscode; 20 rows have
`sprk_eventstatus = 1`, of which only 6 are Open by statuscode.

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
| `…ribbon_commands.js:281,345,482,591,650,714,750` (Complete, Cancel, Reassign, Close, Archive, On Hold, Resume: `getAttribute("sprk_eventstatus").setValue`) | W | `statuscode` is not an attribute of the main form (only the `statecode` header is), so the status is now written by `_saveThenSetStatus`: form save (dates, owner, history), then a `statuscode` + `statecode` PATCH, then form refresh. |
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
| Main form (`{90d2eff7-…}.xml` and `_managed`) | 2 | The column's form placement (stays: POML constraint). `statuscode` is NOT on the form. |
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

## Owner decisions needed (Dataverse changes are out of this task)

1. **Views V-1 to V-6 filter the deprecated column and give wrong answers today.** "My Tasks Open" (the My Tasks widget source) returns the 20 rows
   with `sprk_eventstatus = 1` (6 Open, 7 Draft, 7 Completed by statuscode) and misses the 20 Open rows where the column is null. Change each condition to
   `statuscode eq 659490001` (or `in (1, 659490001, 659490006, 659490007)` for open work) and swap the layout cell/attribute to `statuscode`. The solution
   export is refreshed from Dataverse, so this is a live view edit followed by a re-export.
2. **Add Status Reason (`statuscode`) to the main form** if the ribbon enable rule must hide Complete/Cancel on finished events. Without it,
   `IsEventActive` degrades to the `statecode` header, and Completed and Closed are Active, so those buttons stay enabled on finished events. The
   form saves still work either way (status is written through the Web API).
3. **Deployment coupling.** The reconcile tab now sends `status` as a statuscode value and the BFF accepts only statuscode values on the create-task
   apply and ad-hoc endpoints. Deploy the BFF and the client bundle that carries `Spaarke.Communication.Components` together. An old client against the new
   BFF would send 2 (read as No Further Action, Inactive) for "Completed". The converse (new client, old BFF) sends a value the old coercion rejects with 422.
4. **Column removal** (recommendation): after decisions 1 and 2 are applied and verified, remove the form placement, then the column. Nothing in `src/`
   or `scripts/` reads or writes it any more; the guard test will stay green and should then be deleted with the column. Do not remove before 1: the
   six views break.
5. **Data**: 38 Draft and 18 Open rows have the column null, 10 rows disagree. No backfill is needed once readers are on `statuscode`.
