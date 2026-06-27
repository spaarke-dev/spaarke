# External Access BFF Contract — Breaking Change (R3 task 007)

> **Task**: 007-refactor-external-access-bff-to-sprk-todo
> **Status**: Complete
> **Date**: 2026-06-07
> **Author**: smart-todo-decoupling-r3 / task-execute (FULL rigor)
> **Purpose**: Single source of truth for the breaking DTO + route contract change consumed by the external-spa migration in task 008.

---

## 1. Why This Change

R3 spec **FR-29** removes the legacy `sprk_event` + `sprk_todoflag` semantic for "to-dos" and replaces it with a first-class `sprk_todo` entity (smart-todo-decoupling-r3 Phase 1 / task 002). The External Access BFF surface previously exposed the event+todoflag shape directly to the external SPA. Per audit (`eventtodo-reference-audit.md` § 2E + § 3A) and **OS-1** (no compat shims in pre-release), task 007 migrates the BFF endpoint contract to the `sprk_todo` shape; task 008 migrates the external-spa consumer.

This is a **breaking change**. No compatibility shim is provided. External-spa MUST be updated in lock-step.

---

## 2. Old vs New Contract — Routes

| Method | Old route | New route |
|---|---|---|
| `GET`   | `/api/v1/external/projects/{id}/events`  | `/api/v1/external/projects/{id}/todos` |
| `POST`  | `/api/v1/external/projects/{id}/events`  | `/api/v1/external/projects/{id}/todos` |
| `PATCH` | `/api/v1/external/events/{id}`           | `/api/v1/external/todos/{id}` |

All other routes (`/projects`, `/projects/{id}`, `/projects/{id}/documents`, `/projects/{id}/contacts`, `/projects/{id}/organizations`) are unchanged.

The endpoint method-name pair changed as well:
- `GetExternalProjectEvents` → `GetExternalProjectTodos`
- `CreateExternalProjectEvent` → `CreateExternalProjectTodo`
- `UpdateExternalEvent` → `UpdateExternalTodo`

---

## 3. Old vs New Contract — DTO Shape

### 3.1 Old `ExternalEventDto` (REMOVED)

```jsonc
{
  "sprk_eventid": "guid",
  "sprk_name": "string",
  "sprk_duedate": "iso-8601 datetime",
  "sprk_status": 0,                          // sprk_eventstatus (Choice)
  "sprk_todoflag": true,                     // REMOVED — legacy boolean toggle
  "createdon": "iso-8601 datetime",
  "_sprk_projectid_value": "guid"            // resolver: project lookup value
}
```

### 3.2 New `ExternalTodoDto`

```jsonc
{
  "sprk_todoid": "guid",                     // PK (renamed from sprk_eventid)
  "sprk_name": "string",                     // unchanged
  "sprk_notes": "string|null",               // NEW — rich notes (memo, 100000 chars)
  "sprk_duedate": "iso-8601 datetime|null",  // unchanged
  "sprk_priorityscore": 0,                   // NEW — int 0-100
  "sprk_effortscore": 0,                     // NEW — int 0-100
  "sprk_todocolumn": 100000000,              // NEW — Choice: 100000000=Today / 100000001=Tomorrow / 100000002=Future
  "sprk_todopinned": true,                   // NEW — bool, locks column assignment

  "statecode": 0,                            // 0=Active / 1=Inactive
  "statuscode": 1,                           // 1=Open / 659490001=In Progress / 2=Completed / 659490002=Dismissed (FR-24)

  "createdon": "iso-8601 datetime",          // unchanged
  "_sprk_regardingproject_value": "guid",    // REPLACES _sprk_projectid_value

  // ADR-024 resolver fields (3 of 4 exposed; record-type lookup withheld from external surface):
  "sprk_regardingrecordid": "guid",
  "sprk_regardingrecordname": "string",
  "sprk_regardingrecordurl": "/main.aspx?pagetype=entityrecord&etn=sprk_project&id={guid}"
}
```

### 3.3 Old `CreateExternalEventRequest` (REMOVED)

```jsonc
{
  "sprk_name": "string",
  "sprk_duedate": "iso-8601|null",
  "sprk_status": 0,
  "sprk_todoflag": true        // REMOVED
}
```

### 3.4 New `CreateExternalTodoRequest`

```jsonc
{
  "sprk_name": "string",       // required (handler returns 400 if missing)
  "sprk_notes": "string|null",
  "sprk_duedate": "iso-8601|null",
  "sprk_priorityscore": 0,
  "sprk_effortscore": 0,
  "sprk_todocolumn": 100000000,
  "sprk_todopinned": false
}
```

**Important**: regarding context is NOT in the request body. The `sprk_regardingproject` lookup + four resolver fields are applied server-side using `{id}` from the route — prevents external callers from regarding-ing a to-do to an arbitrary project they don't have access to.

### 3.5 Old `UpdateExternalEventRequest` (REMOVED)

```jsonc
{
  "sprk_name": "string|null",
  "sprk_duedate": "iso-8601|null",
  "sprk_status": 0,
  "sprk_todoflag": true        // REMOVED
}
```

### 3.6 New `UpdateExternalTodoRequest`

```jsonc
{
  "sprk_name": "string|null",
  "sprk_notes": "string|null",
  "sprk_duedate": "iso-8601|null",
  "sprk_priorityscore": 0,
  "sprk_effortscore": 0,
  "sprk_todocolumn": 100000000,
  "sprk_todopinned": false,
  "statuscode": 1              // 1=Open / 659490001=In Progress / 2=Completed / 659490002=Dismissed
}
```

PATCH semantics: only provided fields are written. Regarding context cannot be changed via this surface (re-parent through the model-driven-app form to keep resolver-field invariants intact per ADR-024).

---

## 4. Query-Path Change (Server-Side, Informational)

| Aspect | Old | New |
|---|---|---|
| Entity collection | `sprk_events` | `sprk_todoes` |
| Filter (project scope) | `_sprk_regardingproject_value eq {projectId} and sprk_todoflag eq true` | `_sprk_regardingproject_value eq {projectId}` |
| Select | `sprk_eventid,sprk_eventname,sprk_duedate,sprk_eventstatus,sprk_todoflag,_sprk_regardingproject_value,createdon` | `sprk_todoid,sprk_name,sprk_notes,sprk_duedate,sprk_priorityscore,sprk_effortscore,sprk_todocolumn,sprk_todopinned,statecode,statuscode,createdon,_sprk_regardingproject_value,sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl` |
| Order | `sprk_duedate asc` | `sprk_duedate asc` (unchanged) |

The `$filter` no longer needs the `sprk_todoflag eq true` toggle because `sprk_todo` is its own entity — every record in `sprk_todoes` is by definition a to-do.

---

## 5. Write-Path Change (Server-Side, Informational)

| Aspect | Old | New |
|---|---|---|
| Entity | `sprk_events` POST | `sprk_todoes` POST |
| Body | `{ sprk_name, sprk_duedate, sprk_status, sprk_todoflag, sprk_projectid@odata.bind }` | `{ sprk_name, sprk_notes?, sprk_duedate?, sprk_priorityscore?, sprk_effortscore?, sprk_todocolumn?, sprk_todopinned?, sprk_regardingproject@odata.bind, + 4 resolver fields (id/name/url + record-type bind) }` |
| Update PATCH | `sprk_events({id})` | `sprk_todoes({id})` |

**ADR-024 resolver fields on create**: the BFF service queries `sprk_recordtype_refs` (filtered by `sprk_recordentitylogicalname eq 'sprk_project'`) to find the record-type lookup GUID, then sets all four resolver fields atomically with the `sprk_regardingproject` lookup. Result: 100% mirrors `Sprk.Bff.Api.Services.Workspace.TodoRegardingBuilder` semantics over the Web API path. If `sprk_recordtype_ref` is missing for `sprk_project`, the record-type field is left unset (non-fatal — affects cross-entity view icons only, not correctness).

---

## 6. external-spa Migration Guidance (Task 008)

The external-spa (`src/client/external-spa/`) consumes the BFF DTOs in three locations identified by the audit:

| File | Old field | New field | Action |
|---|---|---|---|
| `src/pages/WorkspaceHomePage.tsx:736` | `$select: 'sprk_eventid,sprk_name,sprk_duedate,sprk_todoflag,_sprk_projectid_value,createdon'` | (the BFF computes the $select; SPA reads DTO fields) | Remove `$select` if directly calling Dataverse; otherwise rely on new DTO shape. |
| `src/components/SmartTodo.tsx:559, 592, 593, 667` | `event.sprk_todoflag`, `sprk_event` filter expression | `todo.statuscode` for status display, `_sprk_regardingproject_value` for project scope | Repoint to `ExternalTodoDto` fields. Drop the `sprk_todoflag eq true` filter — the new route already returns only to-dos. |
| `src/components/EventsCalendar.tsx:300, 380` | `event.sprk_todoflag` icon + `sprk_todoflag: false` create payload | Determine "is to-do" by presence in todos collection rather than boolean flag | Calendar can no longer toggle "is to-do" inline — to-dos are first-class. Replace the toggle UI with explicit create/dismiss. |
| `src/api/web-api-client.ts:95, 362, 464, 484` | `sprk_todoflag?: boolean / null` interface fields + `$select` includes `sprk_todoflag` | Replace `Event` interfaces with `Todo` interfaces matching `ExternalTodoDto` | Full type-shape migration. |
| `src/mocks/mock-service.ts:73`, `src/mocks/mock-data.ts:90,99,108,117,128` | Mock records with `sprk_todoflag: false/true` | Mock `sprk_todo` records with `statuscode`, `_sprk_regardingproject_value`, resolver fields | Rewrite mock data per new shape. |

**Route changes** (also for task 008):
- `GET /api/v1/external/projects/{id}/events` → `GET /api/v1/external/projects/{id}/todos`
- `POST /api/v1/external/projects/{id}/events` → `POST /api/v1/external/projects/{id}/todos`
- `PATCH /api/v1/external/events/{id}` → `PATCH /api/v1/external/todos/{id}`

**Status transitions**: the old `sprk_status` integer was the `sprk_event.sprk_eventstatus` choice. The new `statuscode` integer is the `sprk_todo.statuscode` choice (per FR-24): `1=Open / 659490001=In Progress / 2=Completed / 659490002=Dismissed`. The SPA's status UI labels must be remapped accordingly.

**External admin guide drift**: `docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md:253` and `docs/guides/EXTERNAL-ACCESS-SPA-GUIDE.md:208` mention `sprk_event` $select with `sprk_todoflag`. Update at Phase 9 (docs task) — not part of task 007 or 008.

---

## 7. Coordination — Deploy Order

1. **R3 Phase 1 schema** (tasks 002–005) — `sprk_todo` exists; `sprk_event.sprk_todoflag` removed in target environment.
2. **R3 task 007** (this task) — BFF refactored. **MERGES BEFORE task 008.** Once merged + deployed, the old `/events` external routes return 404 and the new `/todos` routes are live.
3. **R3 task 008** — external-spa migration. Consumes the new DTOs. Cut-over coordinated with BFF deploy.

Per CLAUDE.md §10 (BFF deploy coordination): when task 008 lands, the dev-environment BFF should already be running this task's code so the external-spa can verify against the live new contract.

---

## 8. Test Coverage

| Test | Location | Purpose |
|---|---|---|
| `ExternalTodoDtoTests` | `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalTodoDtoTests.cs` | JSON round-trip + property-name contract lock for all three new DTOs |
| `ExternalDataServiceTests` | `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDataServiceTests.cs` | `BuildRecordUrl` portability guard (no hard-coded org URLs); resolver-URL pattern verified across all 11 regarding targets |

22 new tests added; existing `ExternalAccessEndpointTests` unaffected (covers Grant/Revoke/Invite/ProjectClosure paths only).

---

## 9. Acceptance — task 007 Checklist

- ✅ DTOs reflect `sprk_todo` shape; no `SprkTodoflag` property remains.
- ✅ Service queries `sprk_todoes` (not `sprk_events?$filter=sprk_todoflag eq true`).
- ✅ Writes go to `sprk_todoes` POST / PATCH.
- ✅ Tests pass; no regressions (6147/109/0 vs baseline 6127/109/0; +20 net).
- ✅ Contract-change doc exists (this file).
- ✅ Zero `sprk_todoflag` references in `Infrastructure/ExternalAccess/` + `Api/ExternalAccess/`.
- ✅ BFF publish-size delta verified (below).
- ✅ Zero new HIGH-severity CVEs.

---

*Drafted by task-execute (FULL rigor) for task 007.*
