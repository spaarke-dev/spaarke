# sprk_todo date columns: Date Only behaviour

> **Changed in spaarkedev1 on 2026-10-08** by spaarke-ontology-platform-r1 task 106 (owner decision D-41).
> Every other environment still has the old behaviour until the procedure in [§4](#4-per-environment-procedure) is run there.
> The same change for `sprk_event` (task 098) is in [`sprk_event-date-columns.md`](sprk_event-date-columns.md); this page follows it and only records what differs.

## 1. The columns and their classification

Every date/time column on `sprk_todo` (live metadata, spaarkedev1, 2026-10-08):

| Column | Format | Behaviour before → after | Meaning | Decision |
|---|---|---|---|---|
| `sprk_duedate` | DateOnly | UserLocal → **DateOnly** | The day the To Do is due — a **calendar day** | **Converted** |
| `sprk_completedon` | DateAndTime | UserLocal (unchanged) | When it was completed — an **instant** (`TodoDetail` writes `new Date().toISOString()`; the schema says "set on transition to Completed") | Unchanged |
| `sprk_lastsyncedutc` | DateAndTime | TimeZoneIndependent (unchanged) | Last sync — an **instant** | Unchanged |
| `createdon`, `modifiedon`, `overriddencreatedon` | system | UserLocal (unchanged) | Platform audit instants | Unchanged (system columns; not changeable) |

`sprk_duedate` had `CanChangeDateTimeBehavior = True` already. DateOnly cannot be changed to any other behaviour.

**Wire contract (verified live 2026-10-08)** — the same as `sprk_event`'s (see that page §1):

- **Web API read**: `"2026-10-20"`. **Write**: only `"yyyy-MM-dd"`; a timestamp (`…Z`, an offset, `toISOString()`) is HTTP 400 (`Error identified in Payload provided by the user for Entity :'sprk_todos'`).
- **`$filter` with a timestamp literal** is accepted but compared by the literal's **UTC date** (probed: `eq 2026-07-13T23:00:00-04:00` matches 07-14, `eq 2026-07-14T01:00:00+05:00` matches 07-13). Filter with a `yyyy-MM-dd` literal of the day you mean.
- **SDK** (`Entity`): a `DateTime` at midnight of the date, `Utc` or `Unspecified` kind, is stored as that date (proven live through the production `DataverseServiceClientImpl.CreateAsync`, both kinds).
- **Clients**: read with `parseDueDate`, write with `formatDateOnly` (`@spaarke/ui-components`, `utils/dateLocal.ts`) — never `new Date("yyyy-MM-dd")` or `toISOString()`.
- **BFF**: `DataverseDateOnly` (Spaarke.Dataverse) normalises what the external app sends; "today" for a user is `DataverseUserTimeZone` / `DataverseRecipientDays` (D-25: assignee → owner → UTC).

## 2. What was done in spaarkedev1 (2026-10-08)

| Step | Action | Verified |
|---|---|---|
| 1 | Read all **43** stored `sprk_duedate` values (43 rows; 44 rows have any date) and classified each one (§4 step 2 rules; full table in [§6](#6-before-and-after-every-value)). Writers: one human user (Ralph Schroeder, time zone code 35 = Eastern) and two BFF service principals (code 92 = UTC). No user east of UTC. | 6 at `00:00Z` (date strings) · 21 at Eastern local midnight (`04:00Z`, all in EDT) · 9 at 17:00 Eastern (Daily Briefing "Add to To Do" default) · 2 at 04:00 Eastern · **5 at 23:59 Eastern** (SmartTodo quick-add) |
| 2 | `CanChangeDateTimeBehavior` | Already `True` — no change |
| 3 | `DateTimeBehavior = DateOnly` (Web API `PUT EntityDefinitions(LogicalName='sprk_todo')/Attributes(c55bfdf3-ce62-f111-ab0c-000d3a4d8152)` with the full retrieved `DateTimeAttributeMetadata`, `MSCRM.MergeLabels: true`), then `PublishXml` for **sprk_todo only** | 204 + 204; read back `Format: DateOnly`, `DateTimeBehavior: DateOnly` |
| 4 | `ConvertDateAndTimeBehavior`: `SpecificTimeZone`, `TimeZoneCode = 92` (UTC), `AutoConvert = false`, attribute `sprk_todo.sprk_duedate`. Job `b2977fd0-13c3-f111-a05c-0022482913fc` | **Succeeded**: converted by rule 37, auto-converted 0, **not converted 0**. The 6 values at `00:00Z` need no conversion. |
| 5 | Re-read all 43 | **38** on the intended day · **5** shifted — exactly the five predicted in step 1 |
| 5b | Hand-corrected the 5 (Web API PATCH, `yyyy-MM-dd`, HTTP 204 ×5) | **43 / 43** on the intended day; the formatted value the MDA shows equals the intended day for all 43 |
| — | `sprk_completedon` (4 values) and `sprk_lastsyncedutc` re-read | Unchanged |

The five corrections — SmartTodo quick-add wrote `toISOString()` of **23:59 local** on the picked day, which is the next day in UTC:

| To Do | Stored before | After UTC rule | Corrected to |
|---|---|---|---|
| `6cc533e7-ac6e-f111-ab0e-7ced8ddc4cc6` (New to do) | `2026-06-23T03:59:00Z` | 2026-06-23 | **2026-06-22** |
| `0630dff9-ac6e-f111-ab0e-7ced8ddc4cc6` (New to do 2) | `2026-06-23T03:59:00Z` | 2026-06-23 | **2026-06-22** |
| `6b72fd0b-ad6e-f111-ab0e-7ced8ddc4cc6` (New to do 3) | `2026-06-23T03:59:00Z` | 2026-06-23 | **2026-06-22** |
| `9bbd8063-a87c-f111-ab0e-7ced8ddc4cc6` (Review spaarke-daily-update-service-r5) | `2026-07-14T03:59:00Z` | 2026-07-14 | **2026-07-13** |
| `87f91c76-a87c-f111-ab0e-7ced8ddc4cc6` (Review Daily Update r5) | `2026-07-14T03:59:00Z` | 2026-07-14 | **2026-07-13** |

**Visible effect of the conversion itself:** the six values written as date strings (`00:00Z`, by the Word add-in and the create wizard) were shown **a day early** in the MDA to every user west of UTC (e.g. `2026-10-02T00:00:00Z` showed as 10/1/2026). They now show the day that was picked.

Dependency review, before step 3:
- `RetrieveDependentComponents` on `sprk_duedate`: 3 views and 1 form only.
- No workflow or business rule on `sprk_todo`; no calculated or rollup column; no field-mapping rule or profile targets `sprk_todo` (25 rules, 6 profiles checked).
- The 8 system views use no hour/minute operator (DateOnly forbids them); one uses `on` (`Quick Find Active To Dos`), which DateOnly supports. No personal views.

## 3. What must ship, and in which solution

- The **`sprk_duedate` attribute** is listed by **no** unmanaged solution of ours as its own component (`solutioncomponents`, spaarkedev1, 2026-10-08: only the system *Active* solution).
- The **`sprk_todo` entity** is in **SpaarkeMaster** with *include subcomponents* (`rootcomponentbehavior 0`), so an export of **SpaarkeMaster** from spaarkedev1 after 2026-10-08 carries `sprk_duedate` with `DateTimeBehavior = DateOnly`.
- It is also in **SpaarkeCore** (v1.1.0.0), **TodoRibbons**, **SpaarkeSecureChildRibbons** and **Cr2b7d5**, but all with `rootcomponentbehavior 1` (*do not include subcomponents*): **an export of any of those does NOT carry the behaviour change.** Ship SpaarkeMaster, add `sprk_duedate` to the solution the target is deployed from, or use the direct route (§4 step 3). (This differs from `sprk_event`, whose SpaarkeCore entry includes subcomponents.)
- Importing a solution with a DateOnly column over a UserLocal one changes the behaviour (Microsoft), but **this path has not been exercised**: §4 step 4 verifies it.
- **The data conversion does not ship in any solution.** It runs once per environment (§4 step 5).

## 4. Per-environment procedure

Run it once per environment, as **System Administrator**. It is `sprk_event-date-columns.md` §4 with these values:

**Ship the code and the schema change together.** Deploy task 106's PR (BFF, the Console/Workspace bundles carrying `@spaarke/smart-todo-components`, `@spaarke/daily-briefing-components` and LegalWorkspace, the SmartTodo Code Page, the external SPA) in the same window. Before it, the quick-add and Daily Briefing "Add to To Do" send a timestamp, which a converted column refuses with HTTP 400; the external app's earlier builds are covered by the BFF (it normalises their noon-UTC timestamp).

1. **Analyse before anything changes** (order-critical: afterwards the time portion is gone):
   ```
   GET [org]/api/data/v9.2/sprk_todos?$select=sprk_todoid,sprk_name,createdon,modifiedon,_createdby_value,_modifiedby_value,_ownerid_value,sprk_duedate
       &$filter=sprk_duedate ne null
   Prefer: odata.include-annotations="*"     (follow @odata.nextLink)
   ```
   For each user referenced, read `usersettingscollection(<systemuserid>)?$select=timezonecode` and that code's `timezonedefinitions` row. Save one row per To Do with the raw value: the before-state of record.
2. **Classify** with the rules of the `sprk_event` page §4 step 2, plus the To Do writers' own computed populations:

   | Population | Recognise it | Picked day | UTC rule keeps it? |
   |---|---|---|---|
   | Date string | `00:00:00Z` | the UTC date | yes |
   | MDA entry | local midnight of the entering user | that local date | yes if the user is UTC or **west** of it; **no if east** |
   | SmartTodo quick-add (pre-106) | **23:59 local** of the creating user | that local date | **no if the user is west of UTC** — hand-correct |
   | Daily Briefing default (pre-106) | **17:00 local** of the creating user | that local date | yes unless the user was at UTC−7 or further west (e.g. Pacific daylight time: 17:00 is 00:00Z the next day) — hand-correct those |
   | anything else | — | the local date of the user it was computed for | only if that equals the UTC date |

   **STOP and escalate** if MDA entries from users east of UTC exist, or a value fits no population.
3. **Behaviour**: solution route (SpaarkeMaster — §3), or the direct route: `GET EntityDefinitions(LogicalName='sprk_todo')/Attributes(LogicalName='sprk_duedate')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata`, set `DateTimeBehavior = {"Value":"DateOnly"}` (and `CanChangeDateTimeBehavior.Value = true` if it is not), `PUT` the whole body to `EntityDefinitions(LogicalName='sprk_todo')/Attributes(<MetadataId>)` with `MSCRM.MergeLabels: true`, then `POST PublishXml` with `<importexportxml><entities><entity>sprk_todo</entity></entities></importexportxml>` — **never `PublishAllXml`**.
4. **Verify**: `Format: DateOnly`, `DateTimeBehavior: DateOnly`. If an import left `UserLocal`, apply the direct route.
5. **Convert**: the `sprk_event` page's SDK code with `new KeyValuePair<string, StringCollection>("sprk_todo", new StringCollection { "sprk_duedate" })`, `SpecificTimeZone`, `TimeZoneCode = 92` (confirm it is UTC in the target), `AutoConvert = false`. Poll `asyncoperations(<jobId>)` to `statecode 3`; require **Succeeded** and **"Rows not converted: 0"**. One conversion job at a time.
6. **Re-read, hand-correct** each mismatch with `PATCH sprk_todos(<id>)` `{"sprk_duedate":"yyyy-MM-dd"}` (the mismatches must be exactly those step 2 flagged), **re-read**: every value and its formatted value equal the picked day.
7. **Record** the before/after table, the job id and the corrections with the environment's deployment record.

## 5. Every reader and writer, and its state

Inventoried 2026-10-08 by searching `src/` for `sprk_todo` and `sprk_duedate`. Live proof: §5.1.

**Changed by task 106**

| Surface | Kind | Change |
|---|---|---|
| SmartTodo quick-add — `SmartTodoWidget` (Console/Workspace) | write (BFF child-record route) | Sent `toISOString()` of 23:59 local (HTTP 400 now; the next UTC day from 20:00 Eastern before). Now sends the picked `yyyy-MM-dd` through `buildQuickAddTodoPayload` (package-internal); the default is today's local day (`formatDateOnly`). The SmartTodo Code Page's `QUICK_ADD_TODO_EVENT` listener had the same defect and is fixed the same way, but no surface dispatches that event any more. |
| Daily Briefing "Add to To Do" (`useInlineTodoCreate.computeDueDate`) | write (BFF child-record route) | Returned `toISOString()` (HTTP 400 now). Returns the local calendar day: a bare date as is, a timestamp as its local day, the default as today + 3 local days. |
| External app To Do create (`SmartTodo.tsx` → `ExternalDataService`) | write | The SPA sends the date input's `yyyy-MM-dd` (it sent noon UTC). The BFF normalises with `DataverseDateOnly` on create **and** on the PATCH route (which accepts a due date, though the SPA has no reschedule UI), so earlier SPA builds keep working, and refuses a non-date with 400 instead of Dataverse's 400 behind a 500. |
| External app "Mark as complete / incomplete" (`UpdateTodoAsync` with `statuscode`) | write | **Pre-existing defect found by the live leg:** it sent `statuscode` alone, which Dataverse refuses (`2 is not a valid status code for state code sprk_TodoState.Active`, HTTP 400) — completing a To Do from the external app never worked. The BFF now writes the matching `statecode` and refuses a status reason that is not a To Do one. |
| External app To Do list (`SmartTodo.tsx`) | read | Due date shown with `parseDueDate` (it showed the previous day west of UTC); overdue only once the due day has passed (it was overdue from local midnight of the due day). |
| SmartTodo Code Page toolbar "Email" (`ToolbarActions.handleEmail`) | read | `(due …)` uses `parseDueDate` (it said the previous day west of UTC). |
| LegalWorkspace Quick Summary "Open Tasks" overdue badge | read (`$filter`) | `sprk_duedate lt <local today yyyy-MM-dd>`; it compared with `new Date().toISOString()`, i.e. the UTC date, so from 20:00 Eastern a To Do due today counted as overdue. |
| `sprk_event` "Overdue" feed filter (`buildEventCategoryFilter`, LegalWorkspace and the SmartTodo Code Page copy) | read (`$filter`) | Same defect class on the **event** due date (task 098's column), found by this task's review: `lt <UTC midnight ISO>` → `lt <local today>`. |

**Already correct for a calendar date (no change)**

| Surface | Why |
|---|---|
| `CreateTodoWizard` / Invoice and Report Card follow-on To Do (`todoService`) | Send the date input's `yyyy-MM-dd`. |
| `TodoDetail` update (reschedule) and complete | Due date from the date input (`yyyy-MM-dd`); completion writes `sprk_completedon` (an instant, unchanged column). Its `toDateInputValue` keeps a bare date's day. |
| SmartTodo Kanban, cards, list, scoring, dismissed section (SmartTodo.Components, SmartTodo Code Page, LegalWorkspace) | `parseDueDate` / `daysBetweenLocalMidnight` since task 080/081. Sorts use `getTime()` of the same shape on both sides. |
| `todoSearchUtils` | Reads the date parts with a regex. |
| Outlook / Word add-in "create To Do" (`OfficeService`) | SDK write of the date at midnight, `Utc` kind — stored as that date (proven live). |
| `TodoGenerationService` (rule 3 due date) | Same SDK shape (proven live). Rules 1 and 3 judge each event on the recipient's today (task 098, D-25). |
| Daily Briefing server (`DailyBriefingCollector`: To Dos channel, High Priority) | Task 098: the caller's local today, `OnOrAfter` with `yyyy-MM-dd`; `DueDayOf` reads both shapes. |
| Shared DataGrid | Renders a DateOnly-behaviour column with the `dateonly` renderer (task 098, metadata-driven). |
| MDA form and views | Platform. |

**Deliberately unchanged**: the BFF child-record route (`ChildRecordEndpoints`) and the AI record tools pass the Web API body through — a client that sends a timestamp gets Dataverse's own 400 with its message (the clients above no longer do). No ribbon or web resource reads or writes `sprk_duedate` on `sprk_todo`. `sprk_event.sprk_tododuedate` is a legacy, empty, unused TimeZoneIndependent column (not this table; untouched).

### 5.1 Live proof (spaarkedev1, 2026-10-08)

- **Server paths** — `EventRoutesLiveTests.LiveMode_TodoDueDates_AreCalendarDates_OnTheServerWritePaths` (opt-in): production `ExternalDataService` create with a pre-106 SPA timestamp (`2026-10-21T12:00:00.000Z` → stored `2026-10-21`), reschedule (`2026-10-23`), complete (`statuscode 2` → `statecode 1`, due date unchanged), reopen (`statuscode 1` → `statecode 0`), list read; production `DataverseServiceClientImpl.CreateAsync` with the generation/Office value (midnight, `Utc`) and `Unspecified` → `2026-10-20`. Passed; rows deleted.
- **Browser paths** — the PR's own client functions, bundled, against the Web API as a real user in `America/New_York`: quick-add (201, `2026-10-20`, MDA `10/20/2026`); the pre-106 quick-add payload (HTTP 400); Daily Briefing for a bare date, an evening timestamp and the evening default (all the local day); a wizard create; `TodoDetail` reschedule and complete (204; due date kept); Kanban column move and reopen (204); the Quick Summary badge filter (yesterday counted, today not). All passed; 7 rows deleted, 0 left.

## 6. Before and after, every value

"MDA showed" is the formatted value for an Eastern user. Population labels follow §4 step 2.

| To Do (id prefix) | Stored before (UTC) | Population | UTC rule gives | Intended day | After (re-read) | MDA showed before (Eastern user) | MDA shows after |
|---|---|---|---|---|---|---|---|
| `9250d1b8` | `2026-07-09T00:00:00Z` | date-string (00:00Z) | 2026-07-09 | 2026-07-09 | **2026-07-09** | 7/8/2026 | 7/9/2026 |
| `a01477e8` | `2026-07-13T00:00:00Z` | date-string (00:00Z) | 2026-07-13 | 2026-07-13 | **2026-07-13** | 7/12/2026 | 7/13/2026 |
| `b70b7dab` | `2026-07-15T00:00:00Z` | date-string (00:00Z) | 2026-07-15 | 2026-07-15 | **2026-07-15** | 7/14/2026 | 7/15/2026 |
| `4bebbbc9` | `2026-10-02T00:00:00Z` | date-string (00:00Z) | 2026-10-02 | 2026-10-02 | **2026-10-02** | 10/1/2026 | 10/2/2026 |
| `8093a90f` | `2026-10-02T00:00:00Z` | date-string (00:00Z) | 2026-10-02 | 2026-10-02 | **2026-10-02** | 10/1/2026 | 10/2/2026 |
| `84c90350` | `2026-10-06T00:00:00Z` | date-string (00:00Z) | 2026-10-06 | 2026-10-06 | **2026-10-06** | 10/5/2026 | 10/6/2026 |
| `1b32926a` | `2026-06-22T04:00:00Z` | Eastern local midnight | 2026-06-22 | 2026-06-22 | **2026-06-22** | 6/22/2026 | 6/22/2026 |
| `d9807e99` | `2026-06-23T04:00:00Z` | Eastern local midnight | 2026-06-23 | 2026-06-23 | **2026-06-23** | 6/23/2026 | 6/23/2026 |
| `50b91e18` | `2026-06-23T04:00:00Z` | Eastern local midnight | 2026-06-23 | 2026-06-23 | **2026-06-23** | 6/23/2026 | 6/23/2026 |
| `e61772cb` | `2026-06-24T04:00:00Z` | Eastern local midnight | 2026-06-24 | 2026-06-24 | **2026-06-24** | 6/24/2026 | 6/24/2026 |
| `1832926a` | `2026-06-26T04:00:00Z` | Eastern local midnight | 2026-06-26 | 2026-06-26 | **2026-06-26** | 6/26/2026 | 6/26/2026 |
| `826f53c5` | `2026-07-01T04:00:00Z` | Eastern local midnight | 2026-07-01 | 2026-07-01 | **2026-07-01** | 7/1/2026 | 7/1/2026 |
| `286a7bc0` | `2026-07-02T04:00:00Z` | Eastern local midnight | 2026-07-02 | 2026-07-02 | **2026-07-02** | 7/2/2026 | 7/2/2026 |
| `1732926a` | `2026-07-03T04:00:00Z` | Eastern local midnight | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `c897f005` | `2026-07-03T04:00:00Z` | Eastern local midnight | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `0f32926a` | `2026-07-11T04:00:00Z` | Eastern local midnight | 2026-07-11 | 2026-07-11 | **2026-07-11** | 7/11/2026 | 7/11/2026 |
| `cd7c55a3` | `2026-07-11T04:00:00Z` | Eastern local midnight | 2026-07-11 | 2026-07-11 | **2026-07-11** | 7/11/2026 | 7/11/2026 |
| `896f53c5` | `2026-07-11T04:00:00Z` | Eastern local midnight | 2026-07-11 | 2026-07-11 | **2026-07-11** | 7/11/2026 | 7/11/2026 |
| `27f7fef4` | `2026-07-13T04:00:00Z` | Eastern local midnight | 2026-07-13 | 2026-07-13 | **2026-07-13** | 7/13/2026 | 7/13/2026 |
| `e1165e96` | `2026-07-16T04:00:00Z` | Eastern local midnight | 2026-07-16 | 2026-07-16 | **2026-07-16** | 7/16/2026 | 7/16/2026 |
| `2587b65f` | `2026-07-17T04:00:00Z` | Eastern local midnight | 2026-07-17 | 2026-07-17 | **2026-07-17** | 7/17/2026 | 7/17/2026 |
| `f33d9fdd` | `2026-08-17T04:00:00Z` | Eastern local midnight | 2026-08-17 | 2026-08-17 | **2026-08-17** | 8/17/2026 | 8/17/2026 |
| `b66c378f` | `2026-08-18T04:00:00Z` | Eastern local midnight | 2026-08-18 | 2026-08-18 | **2026-08-18** | 8/18/2026 | 8/18/2026 |
| `af1ba427` | `2026-08-21T04:00:00Z` | Eastern local midnight | 2026-08-21 | 2026-08-21 | **2026-08-21** | 8/21/2026 | 8/21/2026 |
| `4ff4dc1f` | `2026-08-21T04:00:00Z` | Eastern local midnight | 2026-08-21 | 2026-08-21 | **2026-08-21** | 8/21/2026 | 8/21/2026 |
| `6e1afc71` | `2026-08-21T04:00:00Z` | Eastern local midnight | 2026-08-21 | 2026-08-21 | **2026-08-21** | 8/21/2026 | 8/21/2026 |
| `86b7cdfa` | `2026-08-21T04:00:00Z` | Eastern local midnight | 2026-08-21 | 2026-08-21 | **2026-08-21** | 8/21/2026 | 8/21/2026 |
| `736a6eb7` | `2026-06-28T21:00:00Z` | computed 17:00 Eastern | 2026-06-28 | 2026-06-28 | **2026-06-28** | 6/28/2026 | 6/28/2026 |
| `bc19baa5` | `2026-06-28T21:00:00Z` | computed 17:00 Eastern | 2026-06-28 | 2026-06-28 | **2026-06-28** | 6/28/2026 | 6/28/2026 |
| `63f59783` | `2026-07-03T21:00:00Z` | computed 17:00 Eastern | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `3d9af8b4` | `2026-07-03T21:00:00Z` | computed 17:00 Eastern | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `e4471be4` | `2026-07-03T21:00:00Z` | computed 17:00 Eastern | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `e822d834` | `2026-07-03T21:00:00Z` | computed 17:00 Eastern | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `4c3bf851` | `2026-07-03T21:00:00Z` | computed 17:00 Eastern | 2026-07-03 | 2026-07-03 | **2026-07-03** | 7/3/2026 | 7/3/2026 |
| `e9bdb869` | `2026-07-04T21:00:00Z` | computed 17:00 Eastern | 2026-07-04 | 2026-07-04 | **2026-07-04** | 7/4/2026 | 7/4/2026 |
| `66f66723` | `2026-07-20T21:00:00Z` | computed 17:00 Eastern | 2026-07-20 | 2026-07-20 | **2026-07-20** | 7/20/2026 | 7/20/2026 |
| `b856b1ed` | `2026-06-30T08:00:00Z` | computed 04:00 Eastern | 2026-06-30 | 2026-06-30 | **2026-06-30** | 6/30/2026 | 6/30/2026 |
| `15d2a80b` | `2026-06-30T08:00:00Z` | computed 04:00 Eastern | 2026-06-30 | 2026-06-30 | **2026-06-30** | 6/30/2026 | 6/30/2026 |
| `6cc533e7` | `2026-06-23T03:59:00Z` | computed 23:59 Eastern | 2026-06-23 | 2026-06-22 | **2026-06-22** | 6/22/2026 | 6/22/2026 |
| `0630dff9` | `2026-06-23T03:59:00Z` | computed 23:59 Eastern | 2026-06-23 | 2026-06-22 | **2026-06-22** | 6/22/2026 | 6/22/2026 |
| `6b72fd0b` | `2026-06-23T03:59:00Z` | computed 23:59 Eastern | 2026-06-23 | 2026-06-22 | **2026-06-22** | 6/22/2026 | 6/22/2026 |
| `9bbd8063` | `2026-07-14T03:59:00Z` | computed 23:59 Eastern | 2026-07-14 | 2026-07-13 | **2026-07-13** | 7/13/2026 | 7/13/2026 |
| `87f91c76` | `2026-07-14T03:59:00Z` | computed 23:59 Eastern | 2026-07-14 | 2026-07-13 | **2026-07-13** | 7/13/2026 | 7/13/2026 |

## 7. References

The Microsoft Learn pages and the conversion code are in [`sprk_event-date-columns.md`](sprk_event-date-columns.md) §4 step 5 and §5.
