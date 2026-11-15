# Event create, details and edit — spec addendum (DRAFT, owner answers recorded)

> Status: **draft, 2026-11-15.** The owner answered Q-1 to Q-8 the same day (§6). Four follow-up rules (S-1 to S-4) are still open. When they are answered, this becomes FR-35 (create), FR-36 (details, edit, deep link) and FR-37 (assign) in `spec.md`, and tasks are created (§7).
> Source: owner retest round 3 (`notes/task-018-notes.md`): "the Create Event has option field for 'Event' or 'Task', rather than a lookup for 'Event Type'; also missing many important fields such as description, Assigned*; in the created Event the event type is missing; event status, etc. We need to spec out what this create form and details form needs."
> **Scope change:** spec §Out of scope says "editing core record fields". Owner Q-6 adds editing of **events** (child records the caller works with), not of the core records (matter, project, work assignment, invoice). The spec's out-of-scope line is narrowed to say so.

## 1. What exists today (master `c1eb6e4a8e`)

| Area | Today | Where |
|---|---|---|
| Create route | `POST /api/v1/external/{projects\|matters\|workassignments}/{id}/events`. Body: `sprk_name`, `sprk_duedate`, `sprk_status` (Draft/Open only, default Open), `eventType` (null or exactly `"task"`; anything else → 400 `sdap.event.type_not_allowed`) | `ExternalProjectDataEndpoints.cs:414/428/439`, handler `:2440`; DTO `ExternalProjectDtos.cs:488-512`; closed set `ExternalDataService.cs:259-267` |
| Columns written | `sprk_eventname`, `sprk_duedate`, `statuscode`/`statecode`, the root's regarding lookup, owner team, creator stamp; `sprk_eventtype_ref` **only for Task**. Never description, assignee or priority | `ExternalDataService.cs:3412-3454` |
| Event type | `EventTypeRef` knows only Task (`124f5fc9-…`). "Event" sends no type, so a plain event has a **blank** type | `Spaarke.Dataverse/Models.cs:1412-1422` |
| List DTO | id, name, due date, `sprk_status` (statuscode), createdon, regarding project, creator stamps, `isTask`. **No** type name, description, assignee or priority | `ExternalProjectDtos.cs:427-486`, `MapEvent` `ExternalDataService.cs:4749` |
| Create rights | `canCreate` = Create on the root; `canCreateTask` = `canCreate` + AppendTo on the Task type row | `ExternalProjectDataEndpoints.cs:2354/2361` |
| Details | None: no `sprk_event` template, no single-event GET, rows don't open | `recordTemplates.ts`, `EventsCalendar.tsx:326-382` |
| Update | None externally (`PATCH /events/{id}` was not restored). Internal plane: `POST /events/{id}/complete`, `PATCH /events/{id}/due-assignee` (due date / `sprk_assignedto`, runs as the caller) | `Api/Events/EventEndpoints.cs:170/188` |
| Delete | Creator only, while the caller can still read every parent; hard delete | `ExternalProjectDataEndpoints.cs:2833` |

`sprk_event` columns used (`docs/data-model/sprk_event-related-tables.md`):
- `sprk_eventtype_ref` (lookup).
- `sprk_description` (2,000 characters).
- `sprk_assignedto` (lookup → **contact only**; internal staff cannot be assigned through it).
- `sprk_priority` (Low/Normal/High/Urgent = 100000000–3).
- `sprk_duedate` (Date Only).
- `statuscode`: Draft 1, Open 659490001, Completed 659490002, Closed 659490003, On Hold 659490006, Reassigned 659490007, plus the inactive states No Further Action 2, Cancelled 659490004, Transferred 659490005.

`sprk_eventtype_ref` has no "external" flag.

## 2. Create form ("Add event", in the record's Events section)

| Field | Control | Rule |
|---|---|---|
| Title | text, required, 200 | as today |
| Event Type | dropdown, **required** (Q-2) | options = `creatableEventTypes` from the BFF (§4.1); replaces the Event/Task radio |
| Due Date | date | required when the type's `sprk_requiredduedate` = Yes |
| Priority | dropdown Low/Normal/High/Urgent (Q-4) | optional, default Normal |
| Assigned To | dropdown of assignable people (Q-3, S-2) | optional; "Me" listed first |
| Description | multiline, 2,000 | optional, plain text |
| Status | not shown | created Open |

## 3. Event record (details, edit, deep link)

A new `sprk_event` record template (the seventh type).
- It opens by clicking an Events row, as a **child record over the current record** (the task-013 `ChildRecordModal` pattern, one level, focus returns to the row).
- It also opens from a deep link `?record=sprk_event:{id}` as a top-level record (Q-7, via the existing `RecordDeepLink`).
- **Header:** "Event" + title. Secondary line: type name, status badge, and "Your access".
- **Information:** Event Type, Status, Due Date, Priority, Assigned To (name), Description, Created On, Created By (name), Regarding (record name, a link that opens the parent record).
- **Toolbar** (hide, don't disable; each action only when the BFF says the caller may):
  - **Edit** (Q-6): opens a FormModal child dialog with Title, Due Date, Priority, Assigned To and Description. Event Type changes per S-4.
  - **Change status** (Q-5): a menu of the statuses allowed from the current one (S-3).
  - **Copy link** (Q-7).
  - **Delete**: creator only, today's rule.
- After any save, the record and the parent's Events list refresh.

## 4. Backend (BFF; deploy via uac-r2; UAC review of every new column and right; two adversarial passes, because this adds an external write route)

### 4.1 Which types may be picked (Q-1: allow all)
- The events list response gains `creatableEventTypes: [{ id, name, requiresDueDate }]` next to `canCreate`.
- The set is every **active** `sprk_eventtype_ref` row on which the caller has AppendTo (a Dataverse-decided caller; for others, every active row).
- One batched read through #2845's `RequestAccessMemo` (key: principal + entity + sorted type ids; complete answers only).
- The create and update routes validate the chosen id against the same set: unknown or not allowed → 400 `sdap.event.type_not_allowed` (the code is unchanged).
- **Fallback the owner named:** if the set must be limited, it is Task + Reminder only. That is one config list (`ExternalAccess:EventTypes:Allowed`, empty = all). No Dataverse column.
- `eventType: "task"` / `canCreateTask` stay accepted for one release for the old SPA, then go.

### 4.2 Create request
- Adds `eventTypeId` (required), `sprk_description` (≤ 2,000), `sprk_priority` (one of four) and `assignedToContactId` (must be in the assignable set, S-2; otherwise 400 `sdap.event.assignee_not_allowed`).
- Written in `BuildEventCreatePayload`.
- Same filters, owner team, creator stamp and errors as today.

### 4.3 Read: list fields + single event (Q-7)
- **List DTO** adds `eventTypeId`, `eventTypeName`, `sprk_description`, `sprk_priority`, `assignedToName`, `createdByName`, and per row `canEdit`, `canChangeStatus`, `allowedStatuses` (S-1, S-3).
- **New `GET /api/v1/external/events/{id}`** returns the same DTO plus the regarding record (kind, id, name).
  - Read gate: the caller can read **every** parent the event is filed under. This is the delete rule's parent check, and a multi-parent event is withheld from a caller missing one parent, as lists do.
  - Not found or not readable → the uniform 404 (`sdap.access.deny.record_unavailable`).
- Names are FormattedValues. The assignee's **id** is returned only where the caller may assign (`assignedToContactId` inside the edit payload, from the assignable set), never email or UPN. This narrows task 036's "never the assignee's GUID" to the case where the caller could choose that person anyway (UAC review).

### 4.4 Assignable people (Q-3)
- **New `GET /api/v1/external/{root}/{id}/assignable-people`** returns `{ value: [{ contactId, name, isMe }] }` for the set S-2 decides.
- It is the only place contact ids reach the SPA.
- Gate: the caller can create or edit events on that record. Otherwise it answers 403, and the SPA never asks.

### 4.5 Update (Q-5, Q-6)
- **New `PATCH /api/v1/external/events/{id}`** takes partial `sprk_name`, `sprk_duedate`, `sprk_priority`, `sprk_description`, `assignedToContactId`, `eventTypeId` (S-4) and `sprk_status` (one of `allowedStatuses`; `statecode` is paired as on create).
- Right per S-1, checked server-side. The checks run as the app identity over the grant model, like create; the BFF decides, so no action the UI offers answers 403 (FR-06/07).
- Errors: 400 with stable codes per field; the uniform 404; 409 when the event is inactive (Cancelled/Transferred/No Further Action) and the change is not a reopen.
- Route ledger and plane tests updated. DELETE routes are unchanged, so `TheExternalPlane_MapsExactlyFourDeleteRoutes` still holds.
- This also answers **D-10 #2717** for events (a server-side update right).

### 4.6 §11 component justification
| Addition | Existing | Extension? | Cost of doing nothing |
|---|---|---|---|
| `creatableEventTypes` on the events list | `canCreate`/`canCreateTask` | Yes, it extends them | No Event Type lookup; plain events stay type-less |
| Create fields | `CreateExternalEventRequest` | Yes, extended | Description, priority, type and assignee cannot be entered |
| List fields + `canEdit`/`canChangeStatus`/`allowedStatuses` | `ExternalEventDto` | Yes, extended | No details form; actions cannot follow the hide-don't-disable rule |
| `GET /events/{id}` | list route | No: a deep link has no list context, and a 1-row list filter would need the parent id the link doesn't carry | Q-7 deep links cannot open |
| `GET …/assignable-people` | `/me`, grant lists | No: no current route lists the people on a record for an external caller | Assigned To cannot be offered (Q-3) |
| `PATCH /events/{id}` | internal `PATCH /events/{id}/due-assignee`, `POST /complete` | No: internal routes are on the workforce plane with OBO-as-caller; the external plane needs its own gate | Edit and status change (Q-5/6) cannot happen |
| `sprk_event` record template | six templates | Yes, a config entry | Events cannot be opened |

## 5. Overlaps
- **Task 036 / FR-23** (matter events show type and assignee): delivered by §4.3 for every root. **036 is folded in and closed as superseded** (Q-8 = `sprk_assignedto`; ontology D-199 is informed, not waited on).
- **D-10 #2717**: answered for events by §4.5's right. The to-do toggle part stays with D-13's retirement.
- **D-17 #2845 / PR #2847**: the type-row AppendTo read and the assignable-people read go through `RequestAccessMemo`.
  - Lock `context.Items` if a read runs inside a composition.
  - The PATCH must clear the memo, because an assignee change can alter access state.
  - No R4 edits in `Infrastructure/ExternalAccess/**` until #2847 merges.

## 6. Owner decisions (2026-11-15)

| # | Question | Decision |
|---|---|---|
| Q-1 | Selectable event types | **All** (every active type the caller has AppendTo on). If ever limited: Task + Reminder |
| Q-2 | Type required on create | **Yes** |
| Q-3 | Assigned To on create | **Yes** (who: S-2) |
| Q-4 | Priority on create | **Yes** |
| Q-5 | Status change from the details form | **Yes** (who and which: S-1, S-3) |
| Q-6 | Edit after create | **Yes** (who: S-1) |
| Q-7 | Deep link to an event | **Yes** |
| Q-8 | Assignee column | **`sprk_assignedto`** |
| Q-9 | Existing type-less events | Not answered. Assumed: leave them; they show "—" (recommendation) |

### Still open (needed before the tasks are written)
| # | Question | Options | Recommendation |
|---|---|---|---|
| S-1 | Who may edit an event and change its status? | (a) only its creator; (b) creator **or** assignee; (c) anyone with Collaborate or Full Access on the record | **(b)**, and only while the caller still has Collaborate or Full Access on every parent (View Only never). The person who owns or does the work can update it; others on the record cannot overwrite it |
| S-2 | Who can be chosen in Assigned To? | (a) only "Me"; (b) any contact with an active grant on the same record (incl. me); (c) any contact in the caller's organization | **(b)**. It never shows people who can't see the record. Internal staff can't be chosen because the column targets contacts |
| S-3 | Which statuses can an external user set? | free choice / a fixed set | **Open, On Hold, Completed, Cancelled** (Draft only on create; Closed, Reassigned and Transferred stay internal); a Completed or Cancelled event can be reopened to Open |
| S-4 | Can Event Type change after create? | yes / no | **Yes**, within the same allowed set; due date then follows the new type's rule |

## 7. Proposed tasks (after S-1 to S-4)

| Task | Scope | Gate |
|---|---|---|
| 014 BFF | §4.1–4.5: types, create fields, list fields + rights, `GET /events/{id}`, assignable people, `PATCH /events/{id}`; must-fire/must-not-fire tests for each right; call-count test; route ledger; UAC review per column/right. FULL rigor, Opus, **two adversarial passes** (external write route) | after PR #2847 merges; uac-r2 deploy, owner go per SHA |
| 015 SPA | §2 create form; §3 Event record template (child record + deep link), Edit and Change status child dialogs; tests (focus return, one-level rule, hide-don't-disable, deep link) | after 014 is live |
| 016 deploy + E2E | SPA deploy after the BFF swap; owner retest | owner go |
| 036 | closed as superseded by 014/015 | — |
