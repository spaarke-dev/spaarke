# Task 057 - Events and to-dos on the Matter and Work Assignment detail views (FR-03 client)

**Status (2026-10-12):** implemented (rigor FULL, sonnet, effort high). Left UNCOMMITTED for the main session to commit by path. SPA only (`src/client/external-spa`); no server file touched.

## What changed

| File | Change |
|---|---|
| `src/api/web-api-client.ts` | New `EventsTodosRoot` (`projects` / `matters` / `workassignments`), `EVENTS_TODOS_ROOT_NOUN`, `rootEventsPath`, `rootTodosPath`, `getRootEvents(root, id)`, `getRootTodos(root, id)`. `getEvents` and `getProjectTodos` now call them with `'projects'`: same URLs (`/api/v1/external/projects/{id}/events` and `/todos`). The dead `defaults` block in `getEvents` went (`getCollection` ignores options). |
| `src/components/EventsCalendar.tsx` | Optional `root` prop (default `projects`); reads `getRootEvents`; Create only for a project; the empty-state sentence names the record (project / matter / work assignment). A 401/403 on the list is the shared `BffAccessState`; any other failure keeps its message and gains **Try again**. |
| `src/components/SmartTodo.tsx` | Same: optional `root`, `getRootTodos`, shared denied / sign-in state, **Try again**. A non-project root is forced read-only (create and the status toggle are project-only here). |
| `src/components/detail/RecordDetailModal.tsx` | `EventsAndTasksSection` maps the entity to its root (`EVENTS_TODOS_ROOTS`) and renders the two existing components for Project, Matter and Work Assignment. The static "No events or tasks to show for this record yet." and `record-detail-events-unavailable` are gone. |
| `src/mocks/mock-service.ts` | The dev-mock events / to-dos routes match all three roots (the mock has no matter or work assignment rows, so those answer an empty list). The mock is reached only through `isDevMockEnabled`. |
| `tests/RecordDetailModal.test.tsx` | The network mock serves the matter / work assignment routes; the Matter section test now expects the two reads and the normal empty states (it used to assert the static state and no reads). |
| `tests/DetailEventsTodosRoots.test.tsx` (new) | 27 tests, below. |

## Server contract used (read from task 039 notes and `ExternalProjectDataEndpoints.cs`, not docs)

- `GET /api/v1/external/{matters|workassignments}/{id}/events` and `/todos`: same row shape as the project routes; `{ value, truncated? }`.
- Errors: **403** (no Read on the record, or the record is absent or outside the set: one refusal), 401. There is **no 404 and no 503** on these routes (task 039 notes section 6): a failed or cut-short read is `truncated: true` with the rows read so far. The SPA therefore shows `truncated` on an empty list as "could not be loaded", never "no events" (existing `TruncatedListNotice`).
- The every-root rule is applied server-side; the client filters nothing.

## Test map (`tests/DetailEventsTodosRoots.test.tsx`)

Network seam: `bffApiCall` (real `RecordDetailModal`, `EventsCalendar`, `SmartTodo`, `DocumentLibrary`, `RecordMessages`, `BffAccessState`, `delete-own`, `web-api-client`).

| Acceptance criterion | Tests |
|---|---|
| Matter / WA call `/matters/{id}/events` + `/todos`, `/workassignments/{id}/...` and render rows | `reads /{root}/{id}/events and /todos, renders the rows, and no longer shows the static empty state` (x2); `getRootEvents / getRootTodos call ...` (x3 roots) |
| Empty -> same empty states as the project; 403 -> denied; 5xx -> retryable; rest of the detail view intact | `empty lists show the same empty states ...` (x2); `a cut-short empty read ...` (x2); `a 403 ... denied state in the section only` (x2); `a 5xx ... Try again re-reads` (x2); `a 401 ... sign-in state` (x2) |
| Delete-own only on rows with a matching stamp | `delete-own is offered only on rows whose creator stamp is the caller ...` (x2); `a refused delete (403) ...` (x2) |
| Negative: Project URLs unchanged; Invoice and Service Request make no events / to-dos calls | `Project still calls /projects/{id}/events and /todos ...`; `the project functions keep their URLs byte for byte`; `invoice` / `sr: no events section and no events or to-do call at all` |
| Static text gone for Matter / WA | asserted in the rows test and the empty-state test (`queryByText(STATIC_EMPTY)` null) |
| Read-only on Matter / WA | `read-only: no create or status toggle ...` (x2); `a matter or work assignment list is read-only even at Collaborate; a project at Collaborate can still create` |

Fail-before: with the five source files restored to `HEAD`, the new file has **22 failed / 5 passed** of 27 (the five that pass are the negative / project-unchanged cases, which must hold before and after). After: 27 / 27.

## Deviations and decisions

1. **The project section had no denied state and no retry**, although the POML says "match the project section" and requires both. Both are now in the shared components, so they apply to the Project view too: a 401/403 on its events or to-dos shows the shared sign-in / denied state (was "Failed to load events: {raw problem body}"), and any other failure gains a **Try again** button (its message text is unchanged). Success rendering and URLs for Project are unchanged. This is the only Project behaviour change.
2. **Empty-state sentence** for events names the record ("No events have been added to this matter yet."); the Project text is byte-identical. The heading "No events yet" and "No tasks yet" are shared.
3. **`projectId` prop name kept** on `EventsCalendar` / `SmartTodo` (renaming touches `ProjectPage`, the dashboard and many tests); its doc comment says it is the id of the record named by `root`.

## Noticed, not changed

- The project load-error text still echoes the raw ProblemDetails body (`Failed to load events: {"status":500,...}`): `ApiError.message` is the raw body. Pre-existing in `EventsCalendar` / `SmartTodo`; the shared denied / sign-in states no longer go through it.
- `tests/OutsideCounselDashboard.projectName.test.tsx` fails `prettier --check` (pre-existing, not touched).
- No live browser check was run (no live session from this agent); the dev deploy is task 070.

## Review fixes (main session)

One fable review: one F-class finding, fixed.
- **F2:** a non-401/403 list failure rendered the raw response body (an HTML 503 page or a ProblemDetails JSON) in the events and to-dos cards, on Project too. Both now use `problemMessage` (`bff-client.ts`): the server `detail` when it wrote one, else "Events/Tasks could not be loaded just now.". Tests: HTML 503 body and ProblemDetails 500 on both roots; must-fire: restoring the raw message fails 4 tests. `TruncatedLists.test.tsx` updated to the new to-dos text.
- **K4 (test gap) fixed:** to-do delete-own 204 removes the row, on both roots.
- **Accepted known limits:** K2 three "Sign in again" bars can stack on a 401 (same pattern as RecordMessages, no loop); K3 the project Tasks card keeps its title and Add Task beside the denied bar; K4 `EVENTS_TODOS_ROOTS` and `showsEventsAndTasks` must agree (a mismatch hides the section silently).

**Final verification:** typecheck 0, lint 0, vitest 33 files / 842 tests, build ok, check:dist clean.
