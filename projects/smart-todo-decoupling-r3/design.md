# Smart To Do — Decoupling from Events (R3)

> **Project**: smart-todo-decoupling-r3
> **Author**: Ralph Schroeder (design discussion 2026-06-07)
> **Status**: Design draft — pending review
> **Predecessors**: events-smart-todo-kanban-r1, events-smart-todo-kanban-r2

---

## 1. Problem statement

Today, a "To Do" in Spaarke is not its own concept — it is an `sprk_event` row with `sprk_todoflag = true`, optionally extended by an `sprk_eventtodo` sibling that carries notes / completion state. The kanban Code Page queries `sprk_event` directly and the detail panel performs a two-entity load/save.

This model has three problems:

1. **Conceptually wrong** — a calendar event and a task are different domain concepts. Coupling them forces every event row to carry todo metadata it may never use, and forces every todo to be born as a calendar event.
2. **Can't stand alone or hang off non-events** — a user working in a Matter, in an email (`sprk_communication`), or on a Work Assignment cannot create a To Do "about" that record. The only way to create a todo today is through the CreateTodo wizard, which creates an event first.
3. **No external task surface** — to-dos live only inside the Spaarke kanban. They don't appear in Outlook's My Tasks pane, in Teams' Tasks app, or in the Microsoft To Do mobile/desktop apps. Users have to leave their primary work surface to manage them.

We are in pre-release, so this is the right moment to make the change cleanly — without backward-compatibility shims and without data migration.

---

## 2. Goals & non-goals

### Goals
- **G1**. `sprk_todo` is a first-class custom entity, independent of `sprk_event`.
- **G2**. Any `sprk_todo` can be standalone OR associated with one parent record drawn from a fixed set of supported entities, using Spaarke's existing multi-entity resolution pattern.
- **G3**. The existing Kanban UX, scoring formula, drag/pin behavior, threshold preferences, and detail panel are preserved — just retargeted at `sprk_todo`.
- **G4**. All Kanban UI components live in `@spaarke/ui-components` (shared lib). Domain composition lives in the SmartTodo Code Page.
- **G5**. All new and migrated UI is Fluent UI v9 (`@fluentui/react-components`, semantic tokens, Griffel `makeStyles`). No Fluent v8 or custom CSS.
- **G6**. `sprk_todo` records mirror bidirectionally to Microsoft Graph `/me/todo` for users who opt in, exposing the same to-dos in Outlook web/desktop, Teams Tasks app, Microsoft To Do mobile/desktop/Windows.
- **G7**. Outlook add-in users can create a `sprk_todo` from an email via a ribbon action, with `sprk_regardingcommunication` prefilled, and see an indicator when an email already has a Spaarke to-do attached.
- **G8**. The `sprk_eventtodo` entity is retired entirely. Associated technical debt (two-entity load/save, `todoflag` filters, etc.) is removed — no shims, no compat code.

### Non-goals
- **NG1**. Migrating existing dev-environment data. Pre-release, no data migration.
- **NG2**. Planner / group-owned tasks. Out of scope — Planner has separate APIs and a separate sharing model. We integrate only with `/me/todo` (which is what "Teams Private Tasks" is in 2026 — the user's personal lists shown in the Teams sidebar).
- **NG3**. Outlook Tasks (the legacy `/me/outlook/tasks` API). Deprecated by Microsoft in August 2022. We use `/me/todo` exclusively, which surfaces in Outlook anyway.
- **NG4**. A custom Teams app or personal tab. Phase-2 thought, not in R3 scope. The Graph sync already makes todos visible in Teams' built-in Tasks app.
- **NG5**. Changing the scoring formula, threshold logic, or kanban UX. R3 is data-model + integration; the UX is preserved as-is.

---

## 3. Architectural decisions

| # | Decision | Rationale |
|---|---|---|
| D-1 | **`sprk_todo` is a custom entity**, NOT a Dataverse Activity entity. | Activities have UI/customization constraints, and Spaarke does not use activities. The scoring + kanban semantics fit awkwardly into the Activity model. |
| D-2 | **Multi-entity resolution pattern**, NOT polymorphic / native Regarding lookup. | Matches Spaarke convention (ADR-024, polymorphic-resolver pattern). Reuses `PolymorphicResolverService` and `AssociateToStep` shared components. |
| D-3 | **`sprk_eventtodo` is retired entirely**. | Pre-release, no compat burden. Two-entity load/save was a workaround for the event-coupled model — it disappears with the new entity. |
| D-4 | **`sprk_event` keeps `sprk_priorityscore` and `sprk_effortscore`** (per Ralph 2026-06-07). | Events have their own prioritization use cases beyond todo placement. `sprk_todo` gets its OWN copies of these fields. |
| D-5 | **`sprk_event` loses** `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned`. | These are todo-specific. With `sprk_todo` independent, they have no reason to live on the event. |
| D-6 | **Kanban components migrate to `@spaarke/ui-components`**. | Mandatory per Spaarke architecture. The existing component is already domain-agnostic and Fluent-v9-compliant — migration is a hoist, not a rewrite. |
| D-7 | **Fluent v9 is mandatory** for all new and migrated components. | Spaarke standard. No Fluent v8, no custom CSS, no third-party UI libs. |
| D-8 | **One dedicated "Spaarke" `/me/todo` list per user**, auto-provisioned on first sync. | Simplest topology. Avoids per-parent fanout (one list per matter) which would balloon the user's list count. Users can rename the list in Microsoft To Do if they want. |
| D-9 | **Microsoft To Do sync is opt-in via user preference**, not always-on, not per-record. | Matches existing email-sync UX pattern. Single mental model: "I turned on Spaarke sync, all my todos appear in MS To Do." |
| D-10 | **`linkedResources` on each `todoTask` carries a Power Apps deep link back to `sprk_todo`**. | Lets users click through from MS To Do → Dataverse record. Free, supported on every Graph client. |
| D-11 | **Sync engine is a BFF service**, not a Dataverse plugin alone. | Plugin enqueues a Service Bus message (existing `ServiceBusJobProcessor`); the BFF handler does the Graph call. Plugins cannot do OBO; BFF can. Outbound is plugin-triggered, inbound is webhook-triggered. |
| D-12 | **No backward compatibility for `sprk_eventtodo` or `sprk_event.sprk_todo*`** during this project. | Pre-release. Per Ralph: tech-debt removal is a goal of R3. |

---

## 4. Data model

### 4.1 New entity: `sprk_todo`

| Logical name | Type | Notes |
|---|---|---|
| `sprk_todoid` | Uniqueidentifier | Primary key |
| `sprk_name` | Text, 200 | Primary name field, shown as kanban card title |
| `sprk_description` | Multiline text, 4000 | Plain description (not the rich-text Notes) |
| `ownerid` | Owner (User/Team) | Standard ownership |
| `owningbusinessunit` | Lookup → BU | Standard |
| `sprk_assignedto` | Lookup → systemuser | Per Ralph 2026-06-07 (revised): User-only. Teams can still own the record via standard `ownerid`. May differ from owner. |
| `statecode` / `statuscode` | State | `Active → Open` / `Inactive → Completed` / `Inactive → Dismissed` |
| `createdby` / `modifiedby` / `createdon` / `modifiedon` | Standard | |
| **Kanban behavior** | | (lifted from `sprk_event`) |
| `sprk_todocolumn` | Choice | Today / Tomorrow / Future |
| `sprk_todopinned` | Boolean | Locks column assignment against auto-reassign |
| `sprk_priorityscore` | Decimal 0–100 | Independent from `sprk_event.sprk_priorityscore` |
| `sprk_effortscore` | Decimal 0–100 | Independent from `sprk_event.sprk_effortscore` |
| `sprk_duedate` | DateOnly | |
| `sprk_completedon` | DateTime | Set on transition to Completed |
| **Detail** | | (lifted from `sprk_eventtodo`) |
| `sprk_notes` | Multiline rich text | Replaces `sprk_eventtodo.sprk_todonotes` |
| **Regarding (multi-entity resolution)** | | See §4.2 |
| `sprk_regardingmatter` | Lookup → `sprk_matter` | |
| `sprk_regardingproject` | Lookup → `sprk_project` | |
| `sprk_regardingevent` | Lookup → `sprk_event` | |
| `sprk_regardingcommunication` | Lookup → `sprk_communication` | |
| `sprk_regardingworkassignment` | Lookup → `sprk_workassignment` | |
| `sprk_regardinginvoice` | Lookup → `sprk_invoice` | |
| `sprk_regardingbudget` | Lookup → `sprk_budget` | |
| `sprk_regardinganalysis` | Lookup → `sprk_analysis` | |
| `sprk_regardingorganization` | Lookup → `sprk_organization` | Account |
| `sprk_regardingperson` | Lookup → `sprk_contact` | OOB-derived per Ralph 2026-06-07 |
| `sprk_regardingdocument` | Lookup → `sprk_document` | Added per Ralph 2026-06-07 |
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref` | Resolver: which entity type |
| `sprk_regardingrecordid` | Text, 100 | Resolver: normalized GUID |
| `sprk_regardingrecordname` | Text, 200 | Resolver: display name |
| `sprk_regardingrecordurl` | URL, 500 | Resolver: clickable link to parent |
| **Graph sync state** | | See §6 |
| `sprk_graphtodolistid` | Text, 100 | `/me/todo/lists/{id}` the todo mirrors into |
| `sprk_graphtodotaskid` | Text, 100 | Mirrored `todoTask` id |
| `sprk_lastsyncedutc` | DateTime | Last successful sync |
| `sprk_synchash` | Text, 64 | Short content hash for loop detection |
| `sprk_syncerror` | Multiline text | Last sync error message (if any) |

### 4.2 Regarding semantics

Identical to the pattern used by `sprk_communication`, `sprk_event`, `sprk_workassignment`, `sprk_document`, `sprk_memo`:

- **At most one** of the eleven `sprk_regarding*` specific lookups is set at a time.
- When one is set, the four resolver fields (`sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`) are populated **atomically** by `PolymorphicResolverService.applyResolverFields`.
- Standalone case: all eleven lookups null, all four resolver fields null. Valid state.
- The eleven specific lookups give subgrids on each parent form a clean filter target (e.g., the Matter form's "To Dos" subgrid filters by `sprk_regardingmatter eq matterid`).
- The four resolver fields give unified cross-entity views (e.g., "All To Dos regardless of regarding type, grouped by parent name").

### 4.3 Removals from `sprk_event`

These attributes are removed from `sprk_event`:
- `sprk_todoflag`
- `sprk_todostatus`
- `sprk_todocolumn`
- `sprk_todopinned`

These remain on `sprk_event` (per D-4):
- `sprk_priorityscore`
- `sprk_effortscore`
- `sprk_duedate`

### 4.4 Removed entity: `sprk_eventtodo`

The entire entity is deleted, including:
- All attributes (`sprk_todonotes`, `sprk_completed`, `sprk_completeddate`, `sprk_regardingevent`, etc.)
- The `sprk_eventtodo_RegardingEvent_n1` relationship
- All saved queries / views
- The entity SVG icon (`exports/SpaarkeCore_v2_unpacked/WebResources/sprk_/icons/entity/eventtodo.svg`)

---

## 5. UI architecture

### 5.1 Shared library mandate

All Kanban + To Do UI components live in `@spaarke/ui-components`. The SmartTodo Code Page becomes a thin domain consumer.

| Component | Status today | Target |
|---|---|---|
| `KanbanBoard<T>` | `src/solutions/SmartTodo/src/components/shared/` (already domain-agnostic) | Hoist to `src/client/shared/Spaarke.UI.Components/src/components/Kanban/` |
| `KanbanColumn`, `KanbanCard` | Same | Hoist alongside |
| `TodoDetail` | Already in shared lib | Simplify (single-entity load/save) |
| `PanelSplitter`, `useTwoPanelLayout` | Already in shared lib | Unchanged |
| `AssociateToStep` | Already in shared lib | Reuse in CreateTodo wizard |
| `PolymorphicResolverService` | Already in shared lib | Reuse for regarding writes |
| `WizardShell`, `CreateRecordWizard` | Already in shared lib | Reuse for CreateTodo |
| `useKanbanColumns`, `useUserPreferences` (thresholds) | SmartTodo-local | Keep local (domain-specific) OR hoist if reusable — TBD per task |
| Score formula + threshold logic | SmartTodo-local | Keep local — domain-specific |

### 5.2 Fluent v9 mandate

All components must use:
- `@fluentui/react-components` v9.x (no v8, no v0)
- Griffel `makeStyles` (no inline `style={{}}`, no CSS modules, no external CSS files)
- Semantic tokens (`tokens.colorNeutralBackground1` etc.) — never raw hex values
- Theme detection that respects light / dark / high-contrast (already in place via the SmartTodo Code Page's 4-level cascade — keep it)

### 5.3 Code Page composition

The SmartTodo Code Page ([src/solutions/SmartTodo/](src/solutions/SmartTodo/)) becomes:

```
SmartTodoApp
├─ TodoProvider (state: items, selectedTodoId, optimistic updates)
│
├─ SmartToDoView (left panel, was SmartToDo)
│  ├─ useTodoItems() → fetch sprk_todo (NOT sprk_event)
│  ├─ useKanbanColumns() → score-based partitioning (unchanged)
│  ├─ useUserPreferences() → thresholds (unchanged)
│  ├─ KanbanHeader
│  │  ├─ Title
│  │  ├─ AddTodoBar (inline quick-add)
│  │  ├─ MyTasksFilter (NEW — ownerid eq currentUser toggle)
│  │  └─ Settings, Recalculate
│  ├─ <KanbanBoard from @spaarke/ui-components>
│  │  ├─ Today / Tomorrow / Future columns
│  │  └─ TodoCard (domain-specific renderer)
│  └─ DismissedSection
│
├─ <PanelSplitter from @spaarke/ui-components>
│
└─ <TodoDetail from @spaarke/ui-components>
   ├─ Single sprk_todo load/save (NOT two-entity)
   ├─ Notes editor (Lexical, unchanged)
   ├─ <AssociateToStep from @spaarke/ui-components> for regarding edit
   ├─ Score breakdown dialog (unchanged)
   └─ Complete / Dismiss / Sync-to-MS-To-Do toggle
```

### 5.4 "My Tasks" filter

New header toggle in `KanbanHeader`. Three modes:
- **My Tasks** — `ownerid eq @currentuser OR sprk_assignedto eq @currentuser`
- **Assigned to me** — `sprk_assignedto eq @currentuser`
- **All** — no filter (subject to Dataverse security)

Default: My Tasks. Persisted in user preferences alongside thresholds.

### 5.5 CreateTodo wizard rework

The current wizard creates `sprk_event` with `todoflag=true`. After R3:

- Target entity changes to `sprk_todo`.
- Adds `AssociateToStep` as a step ("What is this To Do about?"). Skippable for standalone todos.
- Pre-fill behavior:
  - From kanban "Add To Do" — no regarding prefilled, AssociateToStep starts blank.
  - From Matter / Communication / Event / Contact / etc. ribbon button — regarding prefilled from launch context, AssociateToStep starts on the correct step.
  - From Outlook add-in "Create To Do" — regarding prefilled to `sprk_regardingcommunication` (after the communication is saved to Spaarke).

### 5.6 Subgrids on parent forms

Each of the eleven regarding-target entities gets a "To Dos" subgrid on its main form, filtered by the corresponding `sprk_regarding*` lookup. Big UX win — users see all to-dos in context.

Forms to update: Matter, Project, Event, Communication, Work Assignment, Invoice, Budget, Analysis, Organization, Contact, Document.

### 5.7 Decommissioned UI surfaces

These are evaluated for removal:
- `src/solutions/TodoDetailSidePane/` — standalone detail form. After `TodoDetail` collapses to single-entity load/save, this solution may be redundant. **Decision deferred to task phase.**
- Any direct `sprk_event` query in SmartTodo (multiple call sites) — replaced with `sprk_todo` query.
- `FeedTodoSyncContext` cross-block sync events — payload shape changes from event-id to todo-id.

---

## 6. Microsoft Graph integration

### 6.1 Topology

```
                                  ┌─────────────────────────┐
                                  │   Microsoft To Do       │
                                  │   /me/todo/lists        │
                                  │   "Spaarke" list        │
                                  └────────┬────────────────┘
                                           ▲
                                           │ Graph (OBO,
                                           │ Tasks.ReadWrite)
                                           │
                                  ┌────────┴────────────────┐
                                  │  Sprk.Bff.Api           │
                                  │  TodoGraphSyncHandler   │
                                  │  + /api/graph/webhooks  │
                                  │    /todo (inbound)      │
                                  └────────┬────────────────┘
                                           ▲
                ┌──────────────────────────┤
                │                          │
                │ Service Bus              │ Change notif.
                │ (outbound from DV)       │ (inbound from Graph)
                │                          │
       ┌────────┴────────┐          ┌──────┴───────────┐
       │ sprk_todo       │          │ Graph webhook    │
       │ Dataverse       │          │ subscription     │
       │ plugin on       │          │ (3-day lifetime, │
       │ Create/Update   │          │ auto-renewed)    │
       └─────────────────┘          └──────────────────┘
```

### 6.2 Sync engine

**Outbound (Dataverse → Graph)**:
1. Plugin on `sprk_todo` Create / Update enqueues a Service Bus message: `{ todoId, userId, op }`.
2. Existing [`ServiceBusJobProcessor`](src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) routes to a new `TodoGraphSyncHandler`.
3. Handler reads the user's opt-in preference. If off, skip.
4. Handler resolves the user's Spaarke list (auto-creates on first sync, stores list id on the user preference record).
5. Handler computes `sprk_synchash` from current field values.
6. Handler PATCHes `/me/todo/lists/{listId}/tasks/{taskId}` via Graph OBO (or POSTs if first sync). Sets `linkedResources[0]` with deep link + `externalId = todoId`.
7. Handler updates `sprk_graphtodotaskid`, `sprk_lastsyncedutc`, `sprk_synchash` on the `sprk_todo` row.

**Inbound (Graph → Dataverse)**:
1. Graph posts a change notification to `/api/graph/webhooks/todo`.
2. BFF endpoint authenticates the notification (validation token + clientState).
3. BFF fetches the changed task via delta query against `/me/todo/lists/{listId}/tasks/delta`.
4. BFF locates the corresponding `sprk_todo` by `sprk_graphtodotaskid` (or `linkedResources[0].externalId`).
5. BFF computes a fresh `sprk_synchash` from the incoming task. If equal to the stored hash, **drop the update** (loop detection — this change came from us).
6. Otherwise, PATCH the `sprk_todo` row with the changed fields. Set a thread-local "skip outbound" flag during the write to prevent the outbound plugin from re-firing.

**Subscription lifecycle**:
- Reuse the existing [`GraphSubscriptionManager`](src/server/api/Sprk.Bff.Api/Infrastructure/Graph/) pattern (already running for email change notifications).
- Per-user subscription on `/me/todo/lists/{spaarkeListId}/tasks`, 3-day lifespan, renewed nightly by a scheduled job.
- Subscription state stored on the user preference record alongside the Spaarke list id.

### 6.3 Field mapping

| `sprk_todo` | `/me/todo` `todoTask` |
|---|---|
| `sprk_name` | `title` |
| `sprk_notes` | `body.content` (contentType: `text` or `html`) |
| `sprk_duedate` | `dueDateTime.dateTime` + timeZone |
| `statecode/statuscode` | `status` — `notStarted` (Open + no progress) / `inProgress` (Open + has progress) / `completed` (Completed) — Dismissed maps to `deferred` |
| `sprk_priorityscore` ≥ 70 | `importance: high`; 30–69 → `normal`; < 30 → `low` |
| Modern UCI URL to `sprk_todo` (`https://{org}.crm.dynamics.com/apps/{appid}/r/sprk_todo/{id}`) | `linkedResources[0].webUrl` |
| `sprk_todoid` | `linkedResources[0].externalId` |
| `"Spaarke"` | `linkedResources[0].applicationName` |

### 6.4 Loop prevention

Three mechanisms work together:

1. **`sprk_synchash`** — short content hash (e.g., SHA-256 of canonical JSON of synced fields, truncated to 16 hex chars). Computed before every write. If incoming change has the same hash as stored, it's our own echo — drop it.
2. **Thread-local "skip outbound" flag** — set by the inbound handler during the Dataverse write to prevent the plugin from re-firing into Service Bus.
3. **Last-write-wins by timestamp** (per Ralph 2026-06-07) — when both sides have changed within the sync window (hashes differ AND timestamps fall within an overlapping interval), the side with the later modification timestamp wins on a per-field basis. Compare `sprk_lastsyncedutc` + Dataverse `modifiedon` against `todoTask.lastModifiedDateTime`. The "loser" field write is suppressed and logged to `sprk_syncerror` for observability. Matches Microsoft To Do's own behavior; pragmatic default.

Combined, the only paths that cause an outbound message are: (a) user edits in the Spaarke kanban, (b) user edits in a Spaarke form. The only paths that cause an inbound write are: (a) user edits in Outlook / Teams / MS To Do, (b) initial backfill after opt-in.

### 6.5 Scopes & consent

- New delegated scope: `Tasks.ReadWrite`
- Added to Azure AD app registration alongside existing `Sites.FullControl.All`, `Files.ReadWrite.All`, `FileStorageContainer.Selected`.
- Tenant admin consent required (one-time, deploy-time gate).
- OBO flow already configured in [`GraphClientFactory`](src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs) — `Tasks.ReadWrite` is added to the existing scope list, no new auth flow.

### 6.6 User preference

A new row on `sprk_userpreference` (existing entity):
- `sprk_preferencetype = 100000001` (or next free choice — "MicrosoftToDoSync")
- `sprk_preferencevalue = JSON`:
  ```json
  {
    "enabled": true,
    "spaarkeListId": "AAMkAGI2...",
    "subscriptionId": "abc-123-...",
    "subscriptionExpiresUtc": "2026-06-10T12:00:00Z",
    "initialBackfillCompletedUtc": "2026-06-07T16:00:00Z"
  }
  ```

**Initial backfill on first opt-in** (per Ralph 2026-06-07):
On the first transition `enabled: false → true`, the BFF performs a one-time backfill: enumerates all Active `sprk_todo` records where the user is owner OR assignee, batches them via Graph `$batch` endpoint (20 per request), and creates the corresponding `todoTask` entries in the Spaarke list. Throttling-aware (exponential backoff on 429). Progress tracked via a Service Bus session; UI shows "Syncing N todos to Microsoft To Do…" toast. `initialBackfillCompletedUtc` is set on completion; if interrupted (BFF restart, network), the backfill resumes from the last successfully synced todo.

### 6.7 What's covered "for free" by `/me/todo`

A single integration with `/me/todo` makes Spaarke todos visible in:
- ✅ Outlook web / desktop "My Tasks" pane
- ✅ Teams "Tasks by Planner and To Do" personal list (this IS "Teams Private Tasks")
- ✅ Microsoft To Do app on iOS, Android, Windows
- ✅ Windows widget board

Out of scope:
- ❌ Planner / group plans (separate API, separate sharing model)
- ❌ Legacy Outlook Tasks (`/me/outlook/tasks` — deprecated by Microsoft)

---

## 7. Outlook add-in updates

The existing add-in shell at [src/client/office-addins/outlook/](src/client/office-addins/outlook/) is extended.

### 7.1 Ribbon action: "Create To Do"

Available in the email-read context (eventually also message-compose, but read-only is v1).

Flow:
1. User clicks "Create To Do" on an open email.
2. Add-in checks: is this email already a `sprk_communication` in Spaarke?
   - If no → save the email as a `sprk_communication` first (existing flow), then proceed.
   - If yes → proceed.
3. Open CreateTodo wizard taskpane with `sprk_regardingcommunication` pre-filled.
4. User completes wizard. `sprk_todo` is created. Add-in confirms.

### 7.2 Indicator: "this email has a Spaarke To Do"

Per Ralph 2026-06-07: when viewing an email that already has one or more `sprk_todo` records linked via `sprk_regardingcommunication`, show an indicator.

Options (deferred to task phase, listed by preference):
- **(A)** A pinned message banner inside the add-in taskpane: "This email has N Spaarke to-dos" with a list link.
- **(B)** A status icon on the ribbon button itself (e.g., button label changes to "Create To Do (2)" or shows a small badge).
- **(C)** Office.js `notificationMessages` API (yellow info bar at the top of the message).

The indicator queries: `GET sprk_todos?$filter=_sprk_regardingcommunication_value eq {commId}&$select=sprk_todoid,sprk_name,statecode&$top=10`.

### 7.3 What's NOT in v1
- "Show all my Spaarke To Dos in Outlook" (full kanban inside Outlook) — phase 2.
- Editing existing to-dos from the add-in — phase 2.
- Convert "this email" into a quick to-do without the wizard — phase 2.

---

## 8. Tech debt removal inventory

This is binding per Goal G8. Each item must be removed (not deprecated, not shimmed) within R3:

| Item | Location | Removal action |
|---|---|---|
| `sprk_eventtodo` entity | Dataverse schema | Delete entirely |
| `loadTodoExtension` | [src/solutions/SmartTodo/src/services/todoDetailService.ts](src/solutions/SmartTodo/src/services/todoDetailService.ts) | Delete |
| `saveTodoExtensionFields` | Same file | Delete |
| `deactivateTodoExtension` | Same file | Replaced by single-entity `statecode` update |
| `_sprk_regardingevent_value` queries | Anywhere in `src/solutions/SmartTodo/` | All replaced with `sprk_todo` queries |
| Two-entity parallel-load in TodoDetailPanel | [src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx](src/solutions/SmartTodo/src/components/TodoDetailPanel.tsx) | Single-entity load |
| `todoflag=true` filter on `sprk_event` queries | SmartTodo + LegalWorkspace widget | All replaced |
| `sprk_todoflag`, `sprk_todostatus`, `sprk_todocolumn`, `sprk_todopinned` on `sprk_event` | Dataverse schema | Delete attributes |
| CreateTodo wizard's "create event with todoflag" | [src/client/shared/Spaarke.UI.Components/src/components/CreateTodo/](src/client/shared/Spaarke.UI.Components/src/components/CreateTodo/) | Target `sprk_todo` directly |
| `FeedTodoSyncContext` event-id payload | wherever it lives | Re-shaped to todo-id |
| `TodoDetailSidePane` solution | [src/solutions/TodoDetailSidePane/](src/solutions/TodoDetailSidePane/) | **Decision deferred to task phase** — likely retired |
| Any reference to `sprk_eventtodos` in docs / `.claude/` | Across repo | Updated to `sprk_todos` |

---

## 9. Phasing (high-level)

Detailed task decomposition happens in `plan.md` after design approval. Rough phase outline:

1. **Phase 1 — Schema foundations**
   - Create `sprk_todo` entity with all attributes (§4.1)
   - Create / extend `sprk_recordtype_ref` entries for the eleven regarding targets
   - Register `sprk_todos` in shared resolver pattern
   - Delete `sprk_eventtodo` entity
   - Remove `sprk_todoflag`/etc. from `sprk_event`

2. **Phase 2 — Shared lib migration**
   - Hoist `KanbanBoard`, `KanbanColumn`, `KanbanCard` to `@spaarke/ui-components/Kanban/`
   - Simplify `TodoDetail` to single-entity
   - Add `MyTasksFilter` component

3. **Phase 3 — SmartTodo Code Page rework**
   - Repoint TodoContext + services at `sprk_todo`
   - Wire MyTasksFilter
   - Wire `AssociateToStep` into TodoDetail "Regarding" edit
   - Delete obsolete `sprk_eventtodo`-related code

4. **Phase 4 — CreateTodo wizard rework**
   - Target `sprk_todo` entity
   - Add `AssociateToStep`
   - Pre-fill behaviors (kanban / form ribbon / Outlook add-in)

5. **Phase 5 — Parent form subgrids**
   - Add "To Dos" subgrid to each of the eleven parent forms

6. **Phase 6 — Graph integration foundation**
   - Add `Tasks.ReadWrite` scope, obtain tenant admin consent
   - User preference row + opt-in toggle UI
   - Auto-provision "Spaarke" list on first opt-in
   - Subscription lifecycle (reuse `GraphSubscriptionManager`)

7. **Phase 7 — Graph sync engine**
   - Dataverse plugin on `sprk_todo` Create/Update → Service Bus
   - `TodoGraphSyncHandler` in BFF (outbound)
   - `/api/graph/webhooks/todo` endpoint (inbound)
   - Loop prevention (`sprk_synchash` + skip-outbound flag)

8. **Phase 8 — Outlook add-in updates**
   - Ribbon "Create To Do" action
   - Indicator for emails with existing Spaarke to-dos

9. **Phase 9 — Cleanup & verification**
   - Doc updates (`docs/architecture/event-to-do-architecture.md` superseded; new `docs/architecture/spaarke-todo-architecture.md`)
   - Update CLAUDE.md pointer table
   - `repo-cleanup` skill pass
   - Decision on `TodoDetailSidePane` solution (retire or repurpose)

---

## 10. Risks & open questions

| # | Risk / question | Mitigation / proposed answer |
|---|---|---|
| R-1 | ~~`AssignedTo` as User-or-Team — does Dataverse support a single lookup targeting both?~~ | **RESOLVED 2026-06-07**: Single User-only lookup (`sprk_assignedto` → systemuser). Teams can still own a `sprk_todo` via the standard `ownerid` field. Explicit AssignedTo-to-team is out of scope for R3. |
| R-2 | Graph subscription limit per user (Microsoft caps at ~100 subscriptions per app per tenant for some resources) | Use a single subscription per user on `/me/todo/lists/{spaarkeListId}/tasks`. Caps unlikely to hit. Monitor in operations. |
| R-3 | Throttling on bulk initial sync (user opt-in with 500 existing todos) | Initial-sync batch loop with throttle awareness (use `$batch` endpoint, backoff on 429). Phase 7 task. |
| R-4 | ~~Loop detection edge case: simultaneous edit on both sides within sync window~~ | **RESOLVED 2026-06-07**: Last-write-wins by timestamp on a per-field basis, comparing `sprk_lastsyncedutc` + `modifiedon` against `todoTask.lastModifiedDateTime`. See §6.4 mechanism #3. |
| R-5 | Office add-in indicator latency (every email open triggers a Dataverse query) | Cache by `communicationid` in-memory per add-in session. Phase 8. |
| R-6 | Subgrid performance on parent forms with thousands of related todos | Default view shows only Active (`statecode eq 0`); Inactive accessible via "View All". Phase 5. |
| R-7 | `TodoDetailSidePane` decommission impact — is it referenced by ribbons / app modules outside SmartTodo? | Audit during Phase 9. May survive as a thin shell around the shared `TodoDetail` component, or retire entirely. |
| R-8 | "My Tasks" filter UX when user is a Team member assigned via team — does `ownerid eq @currentuser` cover team-owned records? | Add `OR ownerid eq @currentuserteams` to the filter. Phase 3. |
| R-9 | ~~Linked resources URL stability (deep links to Power Apps must use a stable form)~~ | **RESOLVED 2026-06-07**: Modern UCI scheme — `https://{org}.crm.dynamics.com/apps/{appid}/r/sprk_todo/{id}`. App id stored in BFF config. Builder lives in a new `DeepLinkBuilder` service in `Sprk.Bff.Api`. Phase 7. |

---

## 11. References

**ADRs**
- [ADR-024 Polymorphic resolver pattern](.claude/adr/ADR-024-polymorphic-resolver-pattern.md)
- [ADR-009 OBO token caching](.claude/adr/ADR-009-graph-obo-token-caching.md) (or current OBO ADR)
- [ADR-028 Spaarke auth architecture](.claude/adr/ADR-028-spaarke-auth-architecture.md)

**Patterns**
- [.claude/patterns/dataverse/polymorphic-resolver.md](.claude/patterns/dataverse/polymorphic-resolver.md)

**Prior projects**
- `projects/events-smart-todo-kanban-r1/` (initial kanban)
- `projects/events-smart-todo-kanban-r2/` (Code Page consolidation)

**Architecture docs**
- [docs/architecture/event-to-do-architecture.md](docs/architecture/event-to-do-architecture.md) (will be **superseded** by `spaarke-todo-architecture.md` in Phase 9)
- [docs/data-model/sprk_communication.md](docs/data-model/sprk_communication.md) (regarding pattern reference)

**BFF binding governance** (mandatory)
- [.claude/constraints/bff-extensions.md](.claude/constraints/bff-extensions.md) — load before adding `TodoGraphSyncHandler`, webhook endpoint, or Graph services
- [docs/standards/DATA-ACCESS-DECISION-CRITERIA.md](docs/standards/DATA-ACCESS-DECISION-CRITERIA.md)

**Standards**
- [docs/standards/CODING-STANDARDS.md](docs/standards/CODING-STANDARDS.md)
- Fluent v9 conventions (per `.claude/skills/fluent-v9-component/`)

**Microsoft Learn**
- [Microsoft To Do API overview](https://learn.microsoft.com/en-us/graph/todo-concept-overview)
- [`todoTask` resource](https://learn.microsoft.com/en-us/graph/api/resources/todotask)
- [`linkedResource` resource](https://learn.microsoft.com/en-us/graph/api/resources/linkedresource)
- [Change notifications](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions)

---

## 12. Decision log

| Date | Decision | Decided by |
|---|---|---|
| 2026-06-07 | `sprk_todo` is custom entity, NOT activity | Ralph Schroeder |
| 2026-06-07 | Multi-entity resolution pattern, NOT polymorphic lookup | Ralph Schroeder |
| 2026-06-07 | No backward compat for `sprk_eventtodo` / `sprk_event.sprk_todo*` | Ralph Schroeder |
| 2026-06-07 | No data migration (pre-release) | Ralph Schroeder |
| 2026-06-07 | Regarding targets: Matter, Project, Event, Communication, WorkAssignment, Invoice, Budget, Analysis, Organization, Contact (sprk_contact OOB), Document | Ralph Schroeder |
| 2026-06-07 | AssignedTo: User OR Team, can differ from Owner | Ralph Schroeder |
| 2026-06-07 | Keep `sprk_priorityscore` / `sprk_effortscore` on `sprk_event` | Ralph Schroeder |
| 2026-06-07 | MS To Do sync opt-in: per-user toggle (option B) | Ralph Schroeder |
| 2026-06-07 | Outlook add-in v1: ribbon "Create To Do" + indicator for emails with existing Spaarke to-dos | Ralph Schroeder |
| 2026-06-07 | Shared UI components mandatory; migrate Kanban to `@spaarke/ui-components` | Ralph Schroeder |
| 2026-06-07 | Fluent v9 mandatory | Ralph Schroeder |
| 2026-06-07 | AssignedTo is a single User-only lookup (`sprk_assignedto` → systemuser). Teams own via `ownerid`. | Ralph Schroeder (revised from prior "User or Team") |
| 2026-06-07 | Initial MS To Do sync on opt-in: backfill all Active todos owned-by or assigned-to the user, batched via Graph `$batch` with throttling-aware retry | Ralph Schroeder |
| 2026-06-07 | Sync conflict resolution: last-write-wins by timestamp, per-field | Ralph Schroeder |
| 2026-06-07 | `linkedResources.webUrl` uses Modern UCI scheme (`/apps/{appid}/r/{etn}/{id}`) | Ralph Schroeder |
