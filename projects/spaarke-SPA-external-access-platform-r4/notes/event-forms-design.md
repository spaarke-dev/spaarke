# Event create and details forms — spec addendum (DRAFT for owner review)

> Status: **draft, 2026-11-15.** Not yet in `spec.md`. Once the owner answers §6, the decisions become FR-35/FR-36 in `spec.md` and tasks are created (§7).
> Source: owner retest round 3 (`notes/task-018-notes.md`): "the Create Event has option field for 'Event' or 'Task', rather than a lookup for 'Event Type'; also missing many important fields such as description, Assigned*; in the created Event the event type is missing; event status, etc. We need to spec out what this create form and details form needs."

## 1. What exists today (master `c1eb6e4a8e`)

| Area | Today | Where |
|---|---|---|
| Create route | `POST /api/v1/external/{projects\|matters\|workassignments}/{id}/events`. Body takes `sprk_name`, `sprk_duedate`, `sprk_status` (Draft/Open only, default Open) and `eventType` (null or exactly `"task"`; anything else → 400 `sdap.event.type_not_allowed`) | `ExternalProjectDataEndpoints.cs:414/428/439`, handler `:2440`; DTO `ExternalProjectDtos.cs:488-512`; closed set `ExternalDataService.cs:259-267` |
| Columns written | `sprk_eventname`, `sprk_duedate`, `statuscode`/`statecode`, the root's regarding lookup, owner team, creator stamp; `sprk_eventtype_ref` **only for Task**. Description, assignee and priority are never written | `ExternalDataService.cs:3412-3454` |
| Event type | `EventTypeRef` knows only Task (`124f5fc9-…`). "Event" sends no type, so a plain event has a **blank** type (the owner's finding) | `Spaarke.Dataverse/Models.cs:1412-1422` |
| List DTO | id, name, due date, `sprk_status` (statuscode), createdon, regarding project, creator stamps, `isTask`. **No** type name, description, assignee or priority | `ExternalProjectDtos.cs:427-486`, `MapEvent` `ExternalDataService.cs:4749` |
| Create rights | `canCreate` = Create on the root; `canCreateTask` = `canCreate` + AppendTo on the Task type row | `ExternalProjectDataEndpoints.cs:2354/2361` |
| Details | **None.** No `sprk_event` record template, no single-event GET, rows do not open | `recordTemplates.ts`, `EventsCalendar.tsx:326-382` |
| Update | **None** externally (no PATCH; `PATCH /events/{id}` was not restored). Internal plane has `POST /events/{id}/complete` and `PATCH /events/{id}/due-assignee` (writes due date / `sprk_assignedto`, runs as the caller) | `Api/Events/EventEndpoints.cs:170/188` |
| Delete | Creator only, and only while the caller can still read every parent; hard delete | `ExternalProjectDataEndpoints.cs:2833` |

Relevant `sprk_event` columns (`docs/data-model/sprk_event-related-tables.md`): `sprk_eventtype_ref` (lookup → `sprk_eventtype_ref`), `sprk_description` (multiline, 2,000), `sprk_assignedto` (lookup → **contact** only), `sprk_priority` (Low/Normal/High/Urgent = 100000000–3), `sprk_duedate` (Date Only), `statuscode` (authoritative; `sprk_eventstatus` is deprecated).
`sprk_eventtype_ref` has `sprk_name`, `sprk_description`, `sprk_eventtypecode`, `sprk_eventtypecolor`, `sprk_requiredduedate`, `sprk_requiresbasedate`. **It has no flag saying a type is available to external users.**

## 2. Proposed create form ("Add event", in the record's Events section)

| Field | Control | Rule |
|---|---|---|
| Title | text, required, 200 | as today |
| Event Type | dropdown (single select), **required** | options = the types this caller may create under this record (§4.1), served by the BFF; Task is one of them. Replaces the Event/Task radio |
| Due Date | date | required when the chosen type has `sprk_requiredduedate` = Yes, else optional |
| Description | multiline, 2,000 | optional; plain text |
| Priority | dropdown Low/Normal/High/Urgent | optional (owner Q-4) |
| Assigned To | per owner Q-3 | — |
| Status | not shown | created Open, as today |

## 3. Proposed details form (Event record template)

A new `sprk_event` record template (the seventh type), opened by clicking an Events row as a **child record over the current record** — the task-013 `ChildRecordModal` pattern, one level only, focus returns to the row.

- **Header:** "Event" + title; secondary line = type name and status badge.
- **Information (read-only):** Event Type, Status, Due Date, Priority, Assigned To (name only), Description, Created On, Created By (name only), Regarding (record name).
- **Toolbar:** Delete (creator only, today's rule). Further actions only per owner Q-5/Q-6.
- No tabs; no documents or messages on an event in this round.

## 4. Backend changes (BFF; deploy via uac-r2; UAC review per column)

### 4.1 Which types an external user may pick
The events list response gains `creatableEventTypes: [{ id, name, requiresDueDate }]` next to `canCreate`, so the SPA needs **no new route**. For each candidate type the BFF applies today's Task rule (AppendTo on that type row for a Dataverse-decided caller). The create route validates `eventTypeId` against the same set (unknown or not allowed → 400 `sdap.event.type_not_allowed`, unchanged code). `canCreateTask` / `eventType: "task"` stay accepted for one release for the old SPA, then are removed.

Where the candidate set comes from is owner **Q-1**.

### 4.2 Create request
Add `eventTypeId` (Guid, required once the SPA ships), `sprk_description` (≤ 2,000), `sprk_priority` (one of the four values) and the assignee per Q-3. Written in `BuildEventCreatePayload`. Same filters, owner, creator stamp and errors as today (ADR-008/019).

### 4.3 List DTO (also feeds the details form — no single-event GET)
Add `eventTypeName`, `sprk_description`, `sprk_priority`, `assignedToName` (FormattedValue only — never the assignee's id, email or UPN, per task 036's constraint), `createdByName`. Each is a new readable column → UAC review recorded in the PR (FR-18). The details form renders from the row already loaded. A deep link to a single event (FR-23's optional link) would need `GET /events/{id}`; **not proposed** unless the owner wants deep links (Q-7).

### 4.4 §11 component justification (draft)
- `creatableEventTypes` on the list — *Existing:* `canCreate`/`canCreateTask` on the same response. *Extension:* it **is** the extension (a list instead of one boolean). *Cost of doing nothing:* the SPA cannot offer an Event Type lookup, and plain events stay type-less.
- New create fields — *Existing:* `CreateExternalEventRequest`. *Extension:* extended, no new route. *Cost:* description/priority/type cannot be entered by external users.
- New list fields — *Existing:* `ExternalEventDto`. *Extension:* extended. *Cost:* no details form can show them.
- `sprk_event` record template — *Existing:* the six templates in `recordTemplates.ts`. *Extension:* a seventh config entry, no new component. *Cost:* events cannot be opened at all.
- If Q-1 = (b): one Dataverse Yes/No column on `sprk_eventtype_ref` — *Existing:* none (no external flag). *Cost:* without it, which types external users see is a code/config deploy each time.

## 5. Overlap with planned work
- **Task 036 / FR-23** (matter events show task type and assignee; gated on ontology D-199/D-129). §4.3 delivers the type and assignee names for **every** root, so 036 shrinks to the assignee-source decision (it names `sprk_assignedto1` from D-129; the live data model shows `sprk_assignedto` → contact). **Proposal:** fold 036 into the new tasks; the assignee source is owner/ontology Q-8.
- **D-10 #2717** (no server answer for a PATCH right): any status change from the details form (Q-5) needs a new external update route and its own right; that is the same gap.
- **D-17 #2845** (speed): the new list fields add no Dataverse round trips (same query, more columns); `creatableEventTypes` adds one read of the type rows plus one AppendTo check per type — keep it to one batched call (call-count test). **Agreed with uac-r2 (2026-11-15):** #2845 adds a scoped per-request memo service in `Infrastructure/ExternalAccess`. It is get-or-add keyed on all of a read's inputs, never keeps a faulted or unknown answer, and is cleared on any access write in the same request. The AppendTo-on-type-rows read goes through it: key (principal, entity type, sorted type-row id set), one batched read, only a complete answer memoized. Task 014 builds on it after #2845 merges. **API (PR #2847):**
- `RequestAccessMemo` (`Infrastructure/ExternalAccess/RequestAccessMemo.cs`), stored in `HttpContext.Items`. Reached via `RequestAccessMemo.For(context)` / `IHttpContextAccessor`.
- Get-or-add with a `remember` predicate (keep only accepted answers; a thrown or rejected answer is read again).
- Use an own key prefix covering every input.
- Lock `context.Items` if the read can run inside a composition.
- Any in-request access-state write must clear the memo.

## 6. Owner questions

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q-1 | Which event types may an external user pick? | (a) a fixed list in BFF config (GUIDs in appsettings); (b) a new Yes/No column "Available to external users" on `sprk_eventtype_ref`, set by admins; (c) every active type the caller has AppendTo on | **(b)**: admins control it per environment without a deploy; (c) risks exposing internal-only types (e.g. billing) |
| Q-2 | Is Event Type required on create? | required / optional with a default type | **Required**, so no event is created type-less again |
| Q-3 | Assigned To on create | (a) not on create — shown read-only on details, set internally; (b) "Me" or unassigned; (c) any contact who has access to the record (needs an "assignable people" list → ids exposed, UAC review) | **(a) now**, (b) as a cheap follow-up. (c) is a new access surface |
| Q-4 | Priority on create? | yes / details only | **Yes** (closed choice, no access risk) |
| Q-5 | Can an external user change status (e.g. Mark complete) from the details form? | no / yes for events they created / yes for events assigned to them | **No in this round**; it needs a new update route + right (D-10) |
| Q-6 | Can the creator edit title/description/due date after creating? | no / yes | **No in this round** (spec: no editing of core record fields) |
| Q-7 | Deep link to an individual event? | no / yes (needs `GET /events/{id}`) | **No** for now |
| Q-8 | Assignee column for display | `sprk_assignedto` (contact, live) / `sprk_assignedto1` (D-129) | Confirm with ontology D-199; `sprk_assignedto` is what the live data model shows |
| Q-9 | Existing type-less events (created by the SPA before this change) | leave / backfill to a default type | Leave; they show "—" for type |

## 7. Proposed tasks (after the owner answers)

| Task | Scope | Gate |
|---|---|---|
| 014 BFF | §4.1–4.3: `creatableEventTypes`, create fields, list fields, Q-1 column if (b), tests incl. must-not-fire (a type not in the set → 400; a caller without AppendTo on a type doesn't get it), call-count test; UAC review of each new column | uac-r2 deploy, owner go per SHA |
| 015 SPA | §2 create form (type dropdown, description, priority, required due date by type) + §3 Event record template opened as a child record; tests incl. focus return and one-level rule | after 014 is live |
| 016 deploy + E2E | SPA deploy after the BFF swap; owner retest | owner go |
| 036 | folded into 014/015, or reduced to Q-8 | ontology D-199 |
