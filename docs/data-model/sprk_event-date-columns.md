# sprk_event date columns: Date Only behaviour

> **Changed in spaarkedev1 on 2026-10-05** by spaarke-ontology-platform-r1 task 098 (owner-approved).
> Every other environment still has the old behaviour until the procedure in [§4](#4-per-environment-procedure) is run there.

## 1. The columns and their contract

| Column | Display name | Behaviour (spaarkedev1, since 2026-10-05) | Before |
|---|---|---|---|
| `sprk_duedate` | Due Date | **DateOnly** | Format DateOnly, behaviour **UserLocal** |
| `sprk_finalduedate` | Final Due Date | **DateOnly** | same |
| `sprk_basedate` | Base Date | **DateOnly** | same |
| `sprk_completeddate` | Completed Date | **DateOnly** | same |
| `sprk_approveddate` | Approved Date | **DateOnly** | same |
| `sprk_meetingdate` | Meeting Date | **DateOnly** | same |

All six columns have Format DateOnly and `CanChangeDateTimeBehavior = True`. DateOnly cannot be changed to any other behaviour.

**Wire contract (verified live 2026-10-05):**

- **Web API read**: `"2026-10-02"`, a calendar date with no time or zone (OData `Edm.Date`).
- **Web API write**: **only** `"yyyy-MM-dd"` is accepted. A timestamp is refused with HTTP 400 `Cannot convert the literal '2026-10-02T00:00:00.000Z' to the expected type 'Edm.Date'`. This includes `Z`, an offset, or `toISOString()` output. A probe event was used to check this and was then deleted.
- **SDK** (`Entity`): a `DateTime` with the time at midnight, per Microsoft's documentation of the behaviour. This was not exercised here; the BFF's event routes use the Web API.
- **BFF**: `EventEntity` and `EventDto` carry these as `DateOnly`, which serialises as `yyyy-MM-dd`. `DataverseDateOnly` (Spaarke.Dataverse) is the one parser and formatter.
- **`POST /api/v1/events/{id}/complete`** writes today's date **in the completing user's Dataverse time zone**:
  - It reads `usersettings.timezonecode` app-only. In spaarkedev1 the BFF app users `# mi-bff-api-dev` and `SDAP-BFF-SPE-API` hold `prvReadUserSettings` at Global depth (checked 2026-10-05).
  - If the zone cannot be read, it falls back to the UTC date and logs a warning.
  - **Grant the same privilege to the BFF's app user in every environment.**
- **Clients**:
  - **Read** with `parseDueDate` from `@spaarke/ui-components` (`utils/dateLocal.ts`), never with `new Date("yyyy-MM-dd")`. The latter is UTC midnight, which is the previous day anywhere west of UTC.
  - **Write** with `formatDateOnly(date)`, the browser's LOCAL calendar day. `toISOString()` is refused, and is also the UTC date.
  - Plain web resources (`src/solutions/EventCommands`) cannot import the package, so they carry local equivalents with the same semantics.
- **Server writers** pass midnight of the intended calendar date (`DateTimeKind.Utc` or `Unspecified`):
  - Probed live through the SDK: a `Local`-kind value is converted to UTC first, so 22:00 EDT on 10-20 was stored as 10-21.
  - `TaskActionCore` pins every value it writes to its calendar date as written.
  - A date computed FOR a user is that user's "today" in their Dataverse time zone (`DataverseUserTimeZone`), falling back to UTC.
- **Grid metadata**: `EntityAttributeMetadata.dateTimeBehavior` (and the BFF `AttributeDto.DateTimeBehavior`) carries the column's behaviour. The shared DataGrid renders a `DateOnly`-behaviour column with the `dateonly` renderer.

**Why it changed.** With UserLocal behaviour, a Date Only *format* column stores an instant, and the model-driven app (MDA) shows that instant in each user's own time zone. A value written as `2026-10-02T00:00:00Z` therefore appeared as **10/1/2026** to every user west of UTC. Event 171b8d24 is a live example. Microsoft's guidance is explicit: *"Avoid **Date only** format with **User local** behavior. Users in different time zones might see a different date"* ([Behavior and format of the Date and Time column](https://learn.microsoft.com/power-apps/maker/data-platform/behavior-format-date-time-field)).

## 2. What was done in spaarkedev1 (2026-10-05)

| Step | Action | Verified |
|---|---|---|
| 1 | Read all **120** stored values across 61 events and classified each one (rules in §4 step 2) | 36 at `00:00Z` (date strings) · 77 at Eastern local midnight (`04:00Z` in EDT / `05:00Z` in EST; every one checked against the real DST calendar) · 7 computed timestamps |
| 2 | `CanChangeDateTimeBehavior.Value = true` on the six columns: Web API `PUT EntityDefinitions(LogicalName='sprk_event')/Attributes(<MetadataId>)` with the full retrieved `DateTimeAttributeMetadata`, header `MSCRM.MergeLabels: true` | 204 ×6; read back `True` |
| 3 | `DateTimeBehavior = DateOnly` (same PUT), then `PublishXml` for **sprk_event only** | 204 ×6 + 204; read back `DateOnly` ×6 |
| 4 | `ConvertDateAndTimeBehavior`: `ConversionRule = SpecificTimeZone`, `TimeZoneCode = 92` (UTC, bias 0, no daylight rule), `AutoConvert = false`. Job `0062e480-22c1-f111-a05c-3833c5e9614d` | **Succeeded** in 5 s. Converted by rule: due 31, final due 39, base 9, completed 5, approved 0, meeting 0. **Not converted: 0.** The 36 values already at `00:00Z` need no conversion and are not counted. |
| 5 | Re-read all 120 values | **115** already on the intended day · **5** shifted (all predicted in step 1) |
| 5b | Hand-corrected the 5 (Web API PATCH, `yyyy-MM-dd`) | **120 / 120** on the intended day; the formatted value the MDA shows equals the intended day for all 120 |

The five corrections were all computed timestamps written after 20:00 Eastern, so their UTC date was the next day:

| Event | Column | Stored before | After UTC rule | Corrected to |
|---|---|---|---|---|
| `7300ed8f-8fbf-f111-aaaf-0022482913fc` (ONTOLOGY DEV SEED 005) | `sprk_duedate` | `2026-10-05T01:04:37Z` | 2026-10-05 | **2026-10-04** |
| same | `sprk_finalduedate` | `2026-10-07T01:04:37Z` | 2026-10-07 | **2026-10-06** |
| `9e48e2b0-8fbf-f111-aaaf-0022482913fc` (ONTOLOGY DEV SEED 005) | `sprk_duedate` | `2026-10-05T01:05:39Z` | 2026-10-05 | **2026-10-04** |
| same | `sprk_finalduedate` | `2026-10-07T01:05:39Z` | 2026-10-07 | **2026-10-06** |
| `a0cb27af-992e-f111-88b5-7ced8d1dc988` (Analysis AI review) | `sprk_completeddate` | `2026-06-04T03:49:16Z` | 2026-06-04 | **2026-06-03** |

Dependency review, done before step 2 as Microsoft requires:
- `RetrieveDependentComponents` on each column returns only forms, views and the platform's per-column `aiskillconfig`.
- No workflow or business rule exists on sprk_event, and no calculated or rollup column depends on these columns.
- No system view (19) or personal view (1) uses the hour/minute operators that DateOnly forbids (*Older Than X Hours/Minutes*, *Last/Next X Hours*), and none appears in repo code.

## 3. What must ship, and in which solution

- The six **attributes** are owned by the unmanaged solution **SPRKDOCINTELLIGENCE** (publisher Spaarke, v1.0.0.0). It is the only solution listing them as components (`solutioncomponents`, read live from spaarkedev1 on 2026-10-05). **Its source is not in this repo.**
- The **sprk_event entity** is also in **SpaarkeCore** (v1.1.0.0), **SpaarkeMaster** and **OntologyPlatformSolution**, all with *include subcomponents* (`rootcomponentbehavior 0`). An export of any of these from spaarkedev1 after 2026-10-05 therefore carries the six attributes with `DateTimeBehavior = DateOnly` and `CanChangeDateTimeBehavior = True`. Ship whichever of them the target environment is normally deployed from.
- Per Microsoft, importing a solution whose date column is DateOnly over a target where it is UserLocal changes the behaviour (*"When you import a solution that contains a Date column with User local, you can change the behavior to Date only"*). **This import path has not been exercised here**, so §4 step 4 verifies it explicitly and gives the direct metadata route as the fallback.
- **The data conversion does not ship in any solution.** `ConvertDateAndTimeBehavior` has to run once in each environment after the behaviour change (§4 step 5).

## 4. Per-environment procedure

Run it once per environment, with an account that holds **System Administrator** (needed for `ConvertDateAndTimeBehavior`).

Deploy the code from task 098's PR **first**. In that PR:
- the BFF, the external SPA and the Copilot agent read both the old timestamp shape and the new date shape;
- their writers send `yyyy-MM-dd`, which both behaviours accept.

**Ship the code and the schema change together.** Converting an environment without this code breaks the surfaces in [§4a](#4a-every-surface-and-its-state): timestamp writes get HTTP 400, and bare-date reads show the previous day.

### Step 1: Analyse the values BEFORE anything changes (mandatory, and order-critical)

Once the behaviour changes, the Web API returns only the date, so the time portion that tells you what the user picked can no longer be seen. **Run this before the solution import or the metadata change.**

```
GET [org]/api/data/v9.2/sprk_events?$select=sprk_eventid,sprk_eventname,createdon,modifiedon,_createdby_value,_modifiedby_value,_ownerid_value,sprk_duedate,sprk_finalduedate,sprk_basedate,sprk_completeddate,sprk_approveddate,sprk_meetingdate
    &$filter=sprk_duedate ne null or sprk_finalduedate ne null or sprk_basedate ne null or sprk_completeddate ne null or sprk_approveddate ne null or sprk_meetingdate ne null
Prefer: odata.include-annotations="*"     (follow @odata.nextLink)
```

For each user referenced, read `usersettingscollection(<systemuserid>)?$select=timezonecode` and that code's `timezonedefinitions` row (`standardname`, `bias`, plus `timezonerules` for daylight saving). Save one row per (event, column) with the raw value. This file is the before-state of record.

### Step 2: Classify each value and decide

| Population | How to recognise it | Day the user picked | Does the UTC rule keep it? |
|---|---|---|---|
| Date string (BFF or API write) | time is exactly `00:00:00Z` | the UTC date | **yes** |
| MDA entry | local midnight in the entering user's time zone (creator or last modifier) | that local date | **yes** if the user's zone is UTC or **west** of it · **NO if east of UTC** (local midnight is the previous UTC day) |
| Computed timestamp | anything else | the local date in the zone of the user it was computed for | yes only if that local date equals the UTC date; otherwise **hand-correct** |

- **The UTC rule is correct** when there are no MDA entries from users east of UTC. Only a short, listed set of computed timestamps may then disagree, and those are corrected by hand in step 6. This was the case in spaarkedev1, where every user is Eastern or UTC.
- **STOP and escalate** if MDA entries from users east of UTC exist, or if any value fits no population. No single conversion rule is right then: `LastUpdatedByTimeZone` / `CreatedByTimeZone` are correct only when that user is the one who picked the date. The owner has to choose.

### Step 3: Put the new behaviour in place

- **Solution route**: import the SpaarkeCore / SpaarkeMaster / SPRKDOCINTELLIGENCE export taken from spaarkedev1 after 2026-10-05.
- **Direct route** (the one verified in spaarkedev1): for each of the six columns:
  1. `GET EntityDefinitions(LogicalName='sprk_event')/Attributes(LogicalName='<col>')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata`
  2. Set `CanChangeDateTimeBehavior.Value = true` and `PUT` the whole body back to `EntityDefinitions(LogicalName='sprk_event')/Attributes(<MetadataId>)` with `MSCRM.MergeLabels: true`.
  3. Repeat with `DateTimeBehavior = {"Value":"DateOnly"}`. Format must already be `DateOnly`; otherwise the update throws.
  4. Then `POST PublishXml` with `{"ParameterXml":"<importexportxml><entities><entity>sprk_event</entity></entities></importexportxml>"}`. Publish **this entity only, never `PublishAllXml`**.

### Step 4: Verify the metadata

Re-read each column's `DateTimeAttributeMetadata`. All six must report `Format: DateOnly`, `DateTimeBehavior: DateOnly`. **If the solution import left any column at `UserLocal`, apply the direct route to that column.**

### Step 5: Convert the stored values

`ConvertDateAndTimeBehavior` is an Organization Service message. It is **not** in the Web API `$metadata` and not exposed through the Dataverse MCP, so it runs from the SDK. This is the exact code run in spaarkedev1 (`Microsoft.PowerPlatform.Dataverse.Client` 1.1.32, authenticated with an `az account get-access-token` token provider):

```csharp
var request = new ConvertDateAndTimeBehaviorRequest
{
    Attributes = new EntityAttributeCollection
    {
        new KeyValuePair<string, StringCollection>("sprk_event", new StringCollection
        {
            "sprk_duedate", "sprk_finalduedate", "sprk_basedate",
            "sprk_completeddate", "sprk_approveddate", "sprk_meetingdate",
        }),
    },
    ConversionRule = DateTimeBehaviorConversionRule.SpecificTimeZone.Value,
    TimeZoneCode = 92,   // (GMT) Coordinated Universal Time — confirm in the target: timezonedefinitions?$filter=timezonecode eq 92
    AutoConvert = false,
};
var jobId = ((ConvertDateAndTimeBehaviorResponse)service.Execute(request)).JobId;
```

Poll `asyncoperations(<jobId>)?$select=statecode,statuscode,message` until `statecode = 3`. Require `statuscode` **Succeeded** and **"Rows not converted: 0"** for every column in `message`. Run **one** conversion job at a time, with no solution import in progress (Microsoft's guidance). The job runs no plug-ins or workflows, leaves `modifiedon` unchanged, and is audited.

### Step 6: Re-read, correct, and re-read again

1. Re-read every value from step 1 and compare it with the day the user picked from step 2.
2. Hand-correct each mismatch with `PATCH sprk_events(<id>)` `{"<col>":"yyyy-MM-dd"}`. The mismatches should be exactly the computed timestamps flagged in step 2.
3. Re-read everything. **Every value must equal the intended day**, and the formatted value (`@OData.Community.Display.V1.FormattedValue`) must show the same day.

### Step 7: Record it

Keep the before/after table (one row per value) with the environment's deployment record. Also note the job id and the corrections.

## 4a. Every surface and its state

Inventoried on 2026-10-05 by grepping all of `src/` for the six columns. Every **product write path** to them now sends a calendar date. The generic writers under "Deliberately unchanged" pass through whatever value they are given.

**Converted by task 098 (this PR)**

| Surface | Kind | Change |
|---|---|---|
| BFF event routes (`EventEndpoints`, `DataverseWebApiService`) | read + write | `DateOnly` end to end. `/complete` writes the caller's local date. |
| BFF external SPA path (`ExternalDataService`) | read + write | `sprk_duedate` normalised to `yyyy-MM-dd`. A non-date gets 400. |
| BFF task writers (`TaskActionCore`, `ActionSeam`, `CreateTaskNodeExecutor`) | write (SDK) | The calendar date as written: no `ToUniversalTime()`, midnight Unspecified. |
| `CommunicationRiActionService` (RI follow-up task) | write | +N days from the **recipient's** local today. The former `UtcNow.AddDays(n)` produced the two SEED 005 values. |
| Ribbon `sprk_event_ribbon_commands.js` (complete, bulk complete, Homepage Complete Selected, reschedule) and its complete/reschedule dialogs | read + write | Local `YYYY-MM-DD`. The bulk paths were HTTP 400 before. Proven live 2026-10-05. |
| EventsPage `registerEventHandlers` (CompleteEvents / BulkUpdateEventStatus) | write | `formatDateOnly(new Date())`. Was HTTP 400. |
| EventDetailSidePane date and date-time renderers | read (+ date write) | `parseDueDate` / `formatDateOnly`. |
| Shared DataGrid cell (`DataGrid.tsx`) and `ColumnRendererService` | read | `dateonly` renderer when the metadata says DateOnly behaviour. A bare `yyyy-MM-dd` under a `date` renderer is read as that day; instants are unchanged. |
| Calendar workspace widget (`CalendarWorkspaceWidget`) | read | Event dots on the stored day. |
| VisualHost `CalendarVisual` | read | Bucketed on the stored day. |
| External SPA (`EventsCalendar`, `OutsideCounselDashboard`) | read + write | Due dates parsed with `parseDueDate`. Create sends the date input's own `yyyy-MM-dd` (it was `toISOString()`). |
| Copilot agent (`spaarke-bff-openapi.yaml`, `declarativeAgent.json`) | read + write | `Event.dueDate` / `CreateEventRequest.dueDate` are `format: date`. The instructions say dates are calendar dates, never shifted. |
| Daily Briefing server (`DailyBriefingCollector`: High Priority Overdue/DueToday/DueSoon, the overdue-task cutoff, the to-do "today or later" floor) | read | Judged against the **caller's local today**. The time zone is read AS the caller, through the existing impersonated seam, so no app-only client is added. Before, from 20:00 Eastern a task due today was Overdue, the cutoff was a day late, and a to-do due today dropped out. |
| Playbook "Query Overdue Tasks" (`sprk_duedate`/`sprk_finalduedate lt {{todayUtc}}`) | read | `PlaybookSchedulerJob` runs one playbook per recipient and sets `{{todayUtc}}`/`{{dueSoonWindowUtc}}` to **that user's** local `yyyy-MM-dd` (it was the UTC timestamp). `QueryDataverseNodeExecutor`'s own substitution does the same with the run user, and uses the UTC date when the run has no user. The names are kept because live node configs use them. |
| Daily Briefing (`useInlineTodoCreate.computeDueDate`, `notificationService.filterByDueWithinDays`) | read | A notification's bare `yyyy-MM-dd` due date is that local day: "Add to To Do" dated the To Do a day early, and the "due within N days" window was measured from UTC midnight. |
| `TodoGenerationService` Rule 1 (overdue) and Rule 3 (due within N days) | read | Owner decision **D-25** (2026-10-06): each event is judged on the "today" of the person its To Do is for. That is the **assignee's** zone, else the **owner's**, else **UTC** with a `todo_today_utc_fallback` warning. The queries take the union over every possible today (the UTC date ± 1), and each event is then judged. An event whose verdict is the same on all three days needs no lookup. Each distinct user's zone is read once per run (`DataverseRecipientDays`, beside `DataverseUserTimeZone`; task 031 reuses it, spec FR-47). Before, from 20:00 Eastern an event due today was "Overdue". |

**Fixed by PR #1309 (task 081), which merges first, so it is not duplicated here**
- The Daily Briefing `formatDueDate` ("Due today" / "Overdue by Nd"), which moves to `parseDueDate`.
- VisualHost `DueDateCard` / `DueDateCardList` / `ViewDataService.mapRecordToEvent`, through `utils/eventDueDate.ts`.
- The LegalWorkspace activity feed (`ActivityFeed`, `FeedItemCard`, `useActivityFeedFilters`), through `feedDueAccent.ts`.

Both use `parseDueDate`. **If #1309 did not merge, these would still show the previous day.**

**Deliberately unchanged**

Every "today" the surfaces above compute for a user is now that user's local day, with the UTC date as a logged fallback. The items below are value pass-throughs, not day boundaries.

- **AI record tools** (`DataverseCreateRecordHandler` / `DataverseUpdateRecordHandler`). They write model-supplied values to any table. A timestamp sent to a Date Only column comes back to the model as Dataverse's own 400 (visible and correctable, not silent). Coercing it would need per-column type handling for every table, not these six.
- **Field-mapping push and the playbook UpdateRecord node.** These copy configured values. No live `sprk_fieldmappingrule` or `sprk_fieldmappingprofile` targets the six columns (checked 2026-10-05).
- **`$filter` with a timestamp literal** on these columns is still accepted (HTTP 200, probed live).

**How D-25 maps "assignee" and "owner" to a time zone**

A time zone belongs to a `systemuser` (`usersettings.timezonecode`), but the people on an event are contacts and teams.

- **Assignee.** This is the person `AssignedToDefaults` assigns the generated To Do to: the event's `sprk_assignedtointernal`, else `sprk_assignedattorney1`. Both are contacts. The event's own `sprk_assignedto` is not consulted, because `AssignedToDefaults` does not consult it. A contact's user is the enabled user that task 141 links to it: `systemuser.sprk_primarycontact`, else the user whose Entra oid is in the contact's `sprk_externalobjectid`. A contact with no user (an external person) or with several users (a collision) gives no zone.
- **Owner.** The owner gives a zone only when it is a `systemuser`. **A team has no time zone.** In spaarkedev1 the sampled events are owned by the business-unit team, so for them the owner step does not apply.
- **Live effect.** The sampled events (2026-10-07) fill `sprk_assignedto` but not `sprk_assignedtointernal`. Their generated To Dos would be unassigned, so they are judged on the UTC date with the warning. That judgement only differs for events due within a day of a boundary.
- **Event-sourced creation is still gated.** Rules 1 and 3 create nothing unless `EnableEventSourcedGeneration` is on.

## 5. References

- Microsoft Learn, *Configure the behavior and format of the date and time column using code*: the behaviour members, *"When you update `DateTimeBehavior` from `UserLocal` to `DateOnly`, ensure that you also change the `Format` … Otherwise, an exception occurs"*, the `CanChangeDateTimeBehavior` managed property, *"After updating the behavior of a column, you must publish the customizations"*, *"the existing column values in the database don't automatically convert"*, the four conversion rules, the System Administrator requirement, the async job and `AsyncOperation.Message`, and *"run a single conversion job at a time"*. <https://learn.microsoft.com/power-apps/developer/data-platform/behavior-format-date-time-attribute>
- Microsoft Learn, *Behavior and format of the Date and Time columns*: the display and entry examples per behaviour, the one-time UserLocal change, change during solution import, and the query operators DateOnly does not support. <https://learn.microsoft.com/power-apps/maker/data-platform/behavior-format-date-time-field>
- Schema of record for the rest of sprk_event: [`sprk_event-related-tables.md`](sprk_event-related-tables.md).
