# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-03 (Task 030 closed; Task 031 next)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 031 — Rewrite EventsPage/App.tsx as ~150-line thin host |
| **Step** | Not started |
| **Status** | not-started |
| **Next Action** | Invoke `task-execute` with `projects/spaarke-datagrid-framework-r1/tasks/031-eventspage-rewrite.poml`. Task 030 just created the `sprk_event` configuration record (id `e15c2b93-a05f-f111-a825-70a8a59455f4`); task 031 rewrites the EventsPage code-page host as a ~150-line shell that mounts `<DataGrid configId={EVENT_CONFIG_ID} dataverseClient={new XrmDataverseClient()} />`. Phase D context: EventsPage is a top-level Code Page (not a drill-through dialog), so NO parent-context filter overlay. The `rowOpen.type = "webResource"` already in the config closes the record-link-not-opening bug. |

### Task 030 closure summary (just landed)

- ✅ Configuration record id `e15c2b93-a05f-f111-a825-70a8a59455f4` (`Event Default`, `sprk_isdefault=true`)
- ✅ Configjson authored verbatim from `design.md` Appendix §sprk_event
- ✅ `rowOpen.type = "webResource"` — the load-bearing detail (closes record-link bug)
- ✅ All 3 acceptance criteria pass
- ✅ Documented at `notes/drafts/030-config-record-id.md`
- ⏭ Step 6 smoke test deferred to task 031 (task 031 IS the real consumer)
- ✅ TASK-INDEX advanced 030 → ✅, unblocks 031 and 033

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 031 |
| **Task File** | `tasks/031-eventspage-rewrite.poml` |
| **Title** | Rewrite EventsPage/App.tsx as ~150-line thin host |
| **Phase** | 4: Phase D — EventsPage Migration |
| **Status** | not-started |
| **Started** | (pending) |

### EVENT_CONFIG_ID for task 031

```
e15c2b93-a05f-f111-a825-70a8a59455f4
```

---

## Quick Reference

### Project Context
- **Project**: spaarke-datagrid-framework-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Spec**: [`spec.md`](spec.md)
- **Design**: [`design.md`](design.md) — see Appendix §sprk_event for config shape
- **Parent-context pattern** (Phase C precedent, may apply to task 033): [`notes/parent-context-pattern.md`](notes/parent-context-pattern.md)
- **Configuration guide** (for future maker work): [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md)

### Phase D remaining tasks (after 031)

| ID | Title | Depends |
|---|---|---|
| 032 | Retire legacy events-components (GridSection + 3 filter chips) | 031 |
| 033 | SpaarkeAi Calendar widget migrate to new DataGrid (UQ-06) | 030, 031 |
| 034 | Phase D deploy | 031, 032, 033 |
| 035 | Phase D UAT (record-link bug closure) | 034 |

---

*This file is the primary source of truth for active work state. Keep it updated.*
