# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-03 (Task 032 closed; Task 033 next)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 033 — SpaarkeAi Calendar widget migrate to new DataGrid (UQ-06) |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Invoke `task-execute` with `projects/spaarke-datagrid-framework-r1/tasks/033-calendar-widget-migrate.poml`. Task 033 migrates `Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget` to consume `<DataGrid configId={EVENT_CONFIG_ID} />` instead of the legacy `GridSection`. **MANDATORY closing step from task 032 deviation**: after the migration removes the `GridSection` import, DELETE `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` and remove its barrel re-exports (in `components/index.ts` + `types/index.ts`). |

### Task 032 closure summary (PARTIAL — just landed)

- ✅ **3 of 4** legacy filter component directories DELETED: `AssignedToFilter/`, `RecordTypeFilter/`, `StatusFilter/`
- ⏭ **`GridSection/` DEFERRED** to task 033 closure (last consumer is `CalendarWorkspaceWidget` which task 033 migrates)
- ✅ Barrels updated (`components/index.ts` + `types/index.ts`) with comments explaining the deferral
- ✅ events-components build: `tsc --noEmit` clean
- ✅ EventsPage build: 1259 KB single-file HTML
- 📋 Audit doc: [`notes/drafts/032-consumer-audit.md`](notes/drafts/032-consumer-audit.md) — documents the partial-scope decision (D-032-01), the POML's incorrect parallel-safe declaration, and the GridSection retirement protocol assigned to task 033's closing step

### EVENT_CONFIG_ID for task 033

```
e15c2b93-a05f-f111-a825-70a8a59455f4
```

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 033 |
| **Task File** | `tasks/033-calendar-widget-migrate.poml` |
| **Title** | SpaarkeAi Calendar widget migrate to new DataGrid (UQ-06) |
| **Phase** | 4: Phase D — EventsPage Migration |
| **Status** | not-started |
| **Started** | (pending) |

### Task 033 closing-step addendum (from task 032 partial-scope decision)

After the CalendarWorkspaceWidget migration removes the `GridSection` import, the following deletion MUST happen as part of task 033 closure (not a follow-up task):

```bash
git rm -rf src/client/shared/Spaarke.Events.Components/src/components/GridSection/
```

And remove these now-orphaned barrel re-exports:
- `src/client/shared/Spaarke.Events.Components/src/components/index.ts` lines that re-export `GridSection`, `GridSectionProps`, `IEventRecord`
- `src/client/shared/Spaarke.Events.Components/src/types/index.ts` line that re-exports `IEventRecord`

After deletion, `npm run build` in `Spaarke.Events.Components` MUST still pass.

---

## Quick Reference

### Project Context
- **Project**: spaarke-datagrid-framework-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Spec**: [`spec.md`](spec.md)
- **Design**: [`design.md`](design.md)

### Phase D remaining

| ID | Title | Depends |
|---|---|---|
| 033 | SpaarkeAi Calendar widget migrate to new DataGrid | 030, 031 |
| 034 | Phase D deploy | 031, 032, 033 |
| 035 | Phase D UAT (record-link bug closure) | 034 |

---

*This file is the primary source of truth for active work state. Keep it updated.*
