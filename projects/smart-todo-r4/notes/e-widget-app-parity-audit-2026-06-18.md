# Wave E — Widget/App parity UAT audit & remediation plan

> **Date**: 2026-06-18 (later same day as Wave D deploy)
> **Trigger**: User testing of Wave D deployed bits surfaced 11 functional gaps split across widget (7) and app (4). The unifying ask: widget and app should have the same UI/UX functionality.
> **Decision**: Three new tasks (R4-102 / 103 / 104) — Wave E follow-up to PR #391 + #392 + #393.

## Eleven UAT items (raw user feedback)

### Widget items (7)

1. Search/filter as expand/collapse icon (right-aligned with other icons), NOT an always-visible SearchBox in the toolbar.
2. Open icon should always be enabled (opens the full To Do app in modal). Currently selection-aware (disabled when nothing selected).
3. Widget needs the vertical/horizontal layout switch (parity with app's `<OrientationToggle>`).
4. Widget needs drag-drop between columns (parity with app's Kanban), including pin.
5. Each column card should have a light bg shade: Today=red, Tomorrow=yellow, Future=green. Labels should be Capital Case, NOT ALL CAPS.
6. To Do item cards need select checkboxes + tools (parity with app's KanbanCard from R4-060 — multi-select, not single).
7. Toolbar needs an inline quick-add field + Add button (fast new item without wizard); the `+` button continues to open the wizard for full info.

### App items (4)

8. App should have ONE title — current "Smart To Do" with icon. (Today the app has duplicate chrome.)
9. Reduce size of the To Do item quick-create field; use same compact layout as the widget's new quick-add (per item 7).
10. Default layout should show all 3 columns (Today, Tomorrow, Future) at first load.
11. Consolidate toolbars to ONE row (today the app's Header is 4-row from R4-030).

## Wave D vs Wave E delta — what Wave D shipped that this round changes/extends

Wave D (already on master via PR #391) shipped:
- Widget chrome consolidation: Calendar-canonical Pattern D (R4-099)
- Today/Tomorrow/Future grouping via hoisted `useKanbanColumns` (R4-101)
- `openTodo` launch protocol + BroadcastChannel refetch (R4-100)

Wave E now **extends** that work:
- Wave D's SearchBox in toolbar → Wave E makes it an expand/collapse icon (item 1)
- Wave D's selection-aware Open → Wave E always-on (item 2)
- Wave D's grouped LISTS → Wave E full Kanban with drag-drop + pin + multi-select (items 3, 4, 6) — closes the explicitly-deferred "13-file rich-feature subtree" from R4-020 + R4-101 follow-ups
- Wave D's plain text section headers → Wave E adds bucket-color visual treatment + Capital Case (item 5)
- Wave D's `+` opens wizard → Wave E adds inline quick-add alongside (item 7)
- Wave D didn't touch the app chrome → Wave E consolidates the app's Header to single toolbar (items 8, 9, 10, 11)

## Three-task remediation plan

| Task | Closes UAT items | Effort | Files | Parallel-safe with |
|---|---|---|---|---|
| **R4-102 (E-1)** Widget Kanban hoist (drag-drop + orientation + multi-select + card tools) | 3, 4, 6 | ~1.5-2 days | Peer package: full KanbanBoard hoist + react-dnd setup + KanbanCard hoist. SmartTodoWidget.tsx swaps grouped lists → KanbanBoard. App's SmartToDo.tsx + KanbanBoard import source swaps to peer package. | R4-104 |
| **R4-103 (E-2)** Widget toolbar polish (search-icon + always-Open + colors + Capital case + quick-add) | 1, 2, 5, 7 | ~1 day | SmartTodoWidget.tsx (toolbar layout), SmartTodoWidget.styles.ts (column color shades, expand/collapse search), useKanbanColumns (label casing if needed) | None — depends on R4-102's Kanban hoist for the column container styling hook points |
| **R4-104 (E-3)** App chrome consolidation + quick-create resize + 3-col default | 8, 9, 10, 11 | ~1-1.5 days | SmartTodoApp.tsx, Header.tsx (collapse 4-row to 1), components/Header/ (if needed), components/index.ts. Default `viewMode` defaults to "kanban" so 3 cols visible on first load. | R4-102 (different files) |

**Dispatch order**: R4-102 + R4-104 parallel; R4-103 serial after R4-102 lands. All three land in ONE PR.

**Out of scope for Wave E**:
- New To Do entity fields or schema
- BFF / API changes
- Tests for hoisted Kanban (existing app-side tests cover the logic; this is structural re-housing)
- R4-093 automated UI test suite (the user is running iterative UAT instead)

## Architectural notes

- **react-dnd hoist**: The Kanban drag-drop uses `react-dnd` + `react-dnd-html5-backend`. These become workspace deps of `@spaarke/smart-todo-components`. Hoisting them means the widget bundle grows (~50 KB minified estimate). Acceptable trade-off for feature parity.
- **Pin functionality**: Today's pin lives in the app's KanbanCard + persists via a Dataverse field on `sprk_todo` (verify which field during R4-102 implementation). Pin state must be hoisted alongside.
- **Multi-select state**: Wave D's R4-099 added single-select to widget; R4-102 upgrades to multi-select Set<string> matching the app's pattern (SmartTodoLayout:~121).
- **App default to "kanban" view**: R4-033 introduced viewMode preference with default "card". R4-104 changes this to default to "kanban" (3 columns) per UAT item 10. Existing user preferences are preserved (only first-time defaults change).

## Cache / cosmetic-confirmation note (in case some Wave D items are still hidden by stale bundles)

Before R4-102 / R4-103 / R4-104 run, the user should hard-refresh (Ctrl+F5) the workspace and confirm Wave D's chrome consolidation is visible:
- Widget title reads "Smart To Do" (not "My To Do List")
- ONE title + ONE toolbar (not duplicates)
- 3 grouped sections (Today/Tomorrow/Future) visible

If Wave D chrome is visible post-refresh, the Wave E plan above is the right scope. If Wave D chrome is NOT visible, that's a deploy/cache issue requiring redeploy investigation BEFORE Wave E starts.

## References

- `projects/smart-todo-r4/notes/d-widget-parity-audit-2026-06-18.md` — the audit that drove Wave D
- `projects/smart-todo-r4/notes/d-master-deploy-runbook-2026-06-18.md` — Wave D deploy runbook (will need a re-deploy for Wave E)
- PR #391 (Wave D), PR #392 (CI hardening lockfile), PR #393 (CI hardening eslint direct dep) — all on master
- `src/client/shared/Spaarke.SmartTodo.Components/` — the peer package that R4-102 will extend with full Kanban
- `src/solutions/SmartTodo/src/components/{SmartToDo.tsx, KanbanCard.tsx, Header/Header.tsx}` — current app implementations being mirrored
- `src/solutions/SmartTodo/src/hooks/useKanbanColumns.ts` — already hoisted in R4-101 (peer package version); label casing change happens here
