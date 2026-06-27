# Ownership Filter Alignment — widget + Code Page

**Filed**: 2026-06-19
**Trigger**: spaarke-prototype/smart-todo-r4-uat harness mounted both surfaces against the same seed; widget returned 15 records, Code Page returned 0. Investigation found two surfaces filtered by different ownership fields.

## The bug

| Surface | Pre-fix filter | Source line |
|---|---|---|
| Code Page (`SmartTodoApp`) | `_sprk_assignedto_value eq <userId>` | `src/solutions/SmartTodo/src/services/queryHelpers.ts:525` |
| Widget (`SmartTodoWidget`) | `_ownerid_value eq <userId>` | `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:189` |

## Impact in production

- A to-do where you are **Owner** but someone else is **Assigned To** → visible in widget, invisible in Code Page
- Quick-create from the widget toolbar → record gets `ownerid = <calling user>` (Dataverse server-side default) but `sprk_assignedto = null` → record appears in widget, never appears in Code Page
- UAT pattern: "I added a to-do but it's not in the app" — predicts a real R4 deploy issue had this not been caught

## Why two filters existed

The Code Page was deliberately narrowed in **R4 task 031 / FR-07 / OD-2** (per the comment in `queryHelpers.ts`):

> "Assigned to Me" is the SOLE ownership clause. The legacy R3 "My Tasks" (owner OR assignee) and "All" modes were dropped because `ownerid` is BU-owned (not user-meaningful) and UAT users could not distinguish the parallel modes.

The widget was not updated at the same time — it stayed on `_ownerid_value`. This was a missed cross-surface alignment.

## Fix applied 2026-06-19

### 1. Widget query aligned to `sprk_assignedto`

`SmartTodoWidget.tsx` `buildSmartTodoQuery`:
- `scope='my'` (default): `_ownerid_value eq <userId>` → `_sprk_assignedto_value eq <userId>`
- `scope='all'` (rare): `(_ownerid_value OR _owningbusinessunit_value)` → `(_sprk_assignedto_value OR _owningbusinessunit_value)`
- `$select` now includes `_sprk_assignedto_value` (read-path requires it)

### 2. Quick-create defaults

`SmartTodoWidget.tsx` `submitQuickAdd` payload changed from:

```typescript
{ sprk_name: title }
```

to:

```typescript
{
  sprk_name: title,
  'sprk_assignedto@odata.bind': `/systemusers(${userId})`,
  sprk_duedate: <end of today>,
}
```

This ensures the created record:
- Passes the assignedto filter on BOTH surfaces (widget + Code Page see it)
- Has a due date so the kanban bucketing logic places it in Today/Tomorrow rather than the unbucketed default

The `@odata.bind` format is the canonical Web API binding for lookup fields on create (`_<lookup>_value` is read-only and cannot be used in create payloads).

## Going forward — binding rule

**Any sprk_todo query in any Spaarke surface MUST use `_sprk_assignedto_value` for user-scoped filtering**, not `_ownerid_value`. Reason: per R4-031, `ownerid` is BU-default and not user-meaningful; `sprk_assignedto` is the canonical ownership semantic. Document new surfaces accordingly.

**Any sprk_todo create from any Spaarke surface MUST set `sprk_assignedto@odata.bind`** so created records pass the same filter the read-path uses.

## Cross-references

- R4-031 decision: `src/solutions/SmartTodo/src/services/queryHelpers.ts:519-527` (canonical narrowing rationale)
- Pre-fix widget query: `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx` (commit before 2026-06-19)
- Caught by: `c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat/` (Mode 2 harness — first production-bug save)
