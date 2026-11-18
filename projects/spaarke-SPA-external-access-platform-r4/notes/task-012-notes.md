# Task 012 — Events only: one list, one Add event with a Type choice

**Owner, 2026-11-14 retest:** "we only want Events (which allow for an event type of 'Task')". Both groups were already `sprk_event` rows (009 removed `sprk_todo`); the split read as To Do.

## What changed (src/client/external-spa)
- `EventsCalendar.tsx`: one date-ordered list; a "Task" badge on `isTask` rows; one create dialog with Type Event (default) / Task. The posted type is derived each render, so a stale choice can't post a type the caller lacks. The Description field and its state are removed: the route has no description column, the same "collected but never sent" defect as D-6. `createBusyReporters`, `KIND_TEXT`, the second dialog and `onCreateTaskActionChange` are gone; the busy flag goes straight to `onBusyChange`.
- `recordAccess.ts`: `createTaskRightOf({loaded, listCanCreateTask, levelAllows})`, plus new `addEventRightOf` (Event or Task allowed). `addTask` is removed from the SPA create-action ids. The shared icon map still exports `addTask` (3.0.0); no shared-lib edit.
- `recordTemplates.ts` / `recordTemplate.types.ts` / `TasksEventsSection.tsx`: section "Events", `tasksEvents: ['addEvent']`.
- Inside the template the list renders no heading of its own (the card is titled "Events"). Outside it (ProjectPage), the heading and the "Create Event" button stay.
- Delete confirmations always read "Delete event X".

## Tests
- `RecordTemplateAccess.test.tsx`: task-012 block covering a single list with the badge, payloads, gating (Task false/absent, Task-only, neither), the busy hold and truncated lists. The 64 new or changed tests fail on the old code.
- `NoTodoSurface.test.ts` scan: "Add task", "Create Task", "No tasks", "Tasks & events" and a "Tasks" heading, each with fire and no-fire controls.
- Updated: `RecordAccess.levels`, `TasksEventsSection.busyHold`, `RecordTemplateModal`, `RecordTemplateSections`, `DetailEventsRoots`, `RecordTemplateChildDialogs`.
- Results: typecheck 0 errors, lint clean, vitest 57 files / 1669 passed.

## Quality gates
- Adversarial review (opus, one full pass): no F1–F4. Every criterion met. Rights traced: a Task-only caller can't post an untyped event, and `canCreate` with unknown `canCreateTask` can't post "task".
- Known limits:
  - K2: the Task-only path is defensive, because today's BFF sets `canCreateTask` only when `canCreate` is true. The comment in `recordAccess.ts` was fixed to say so and to cite 008 F2-1.
  - K3: the `TasksEventsSection.busyHold` seam test is a pass-through pin; the real coverage is in `RecordTemplateAccess`.
  - K4: the scan can't see runtime-built labels.
  - Nit fixed: a stale "tasks & events" test comment.
- adr-check:
  - ADR-012: no shared-lib change.
  - ADR-021: Fluent v9 RadioGroup and Badge, with tokens.
  - ADR-050: the dialog stays a SprkModal child (unchanged pattern).
  - Project rule: hide, don't disable. Clean.

## Noticed, not changed
- `OutsideCounselDashboard.tsx` "Upcoming Events & Tasks": the label of a dashboard widget, not a record group.
- ProjectPage header "Tasks" tooltip: the dead header icon already filed as D-15 #2810.
