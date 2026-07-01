# 04 — Latent bugs

> **Priority**: LOW-MEDIUM — one shipped module has the same defect R7 W12 fixed for Daily Briefing
>
> **Source**: R7 W12 grep discovery, 2026-07-01
>
> **Scope**: Not Daily-Briefing-specific but discovered via Daily Briefing UAT

## 1. `EventDetailSidePane/TodoSection.tsx` — `sprk_assignedto@odata.bind` casing bug

**Where**: `src/solutions/EventDetailSidePane/src/components/TodoSection.tsx:233`

```typescript
"sprk_assignedto@odata.bind": `/contacts(${selected.id})`,
```

**Bug**: same defect R7 W12 fixed in `useInlineTodoCreate.ts:263`. Dataverse OData rejects the lowercase attribute logical name; the navigation-property name for `@odata.bind` must be the attribute **SchemaName** (`sprk_AssignedTo`, PascalCase).

**Symptom**: any "Add to ToDo" action from the Event side pane will fail with the OData 400:

```
An undeclared property 'sprk_assignedto' which only has property annotations 
in the payload but no property value was found in the payload.
```

**R7 discovery**: found via `Grep sprk_assignedto|sprk_AssignedTo` when locating the Daily Briefing bug. Only two files use lowercase; one was fixed in R7, the other (EventDetailSidePane) was flagged for R5 because it's a separate solution/module — not in R7 W12 scope.

**Fix**: one-line change — `sprk_assignedto@odata.bind` → `sprk_AssignedTo@odata.bind`. Matches the convention documented across `SmartTodoWidget.tsx:759`, `SmartToDo.tsx:680`, `TodoDetail.tsx:636`, and (now) `useInlineTodoCreate.ts:263`.

**Severity**: Medium — silently breaks a shipped feature (Event side pane's Add-to-ToDo). Probably unnoticed because Event side pane usage may be low.

**R5 action**: fix in whatever release lands first — this doesn't need to wait for R5 architectural work. Could be a standalone bug-fix commit.

## Grep audit for similar bugs

To catch the class as a whole for R5, run:

```
grep -rn "sprk_[a-z_]*@odata\.bind" src/ --include="*.tsx" --include="*.ts" --include="*.js" --include="*.cs"
```

Any hit where the property name is all-lowercase is suspect. Manually verify against the entity's SchemaName in Dataverse metadata.

**Convention documentation**: R5 should add a short section to [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](../../../../docs/standards/DATA-ACCESS-DECISION-CRITERIA.md) or a new `docs/standards/DATAVERSE-ODATA-CONVENTIONS.md` explaining:

- Attribute `LogicalName` is used in FetchXml, QueryExpression `Filter`, `ColumnSet`, `Select`
- Attribute `SchemaName` is used in `@odata.bind` navigation-property names and in OData `$expand`
- One-line rule: "If you're binding a lookup via `@odata.bind`, use PascalCase SchemaName; if you're filtering or selecting a scalar, use lowercase LogicalName."

## References

- R7 W12 Daily Briefing fix: commit `2a7e47771`
- Same convention correctly used at:
  - [`src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:759`](../../../../src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx#L759)
  - [`src/solutions/SmartTodo/src/components/SmartToDo.tsx:680`](../../../../src/solutions/SmartTodo/src/components/SmartToDo.tsx#L680)
  - [`src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx:636`](../../../../src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx#L636)
- Bug site: [`src/solutions/EventDetailSidePane/src/components/TodoSection.tsx:233`](../../../../src/solutions/EventDetailSidePane/src/components/TodoSection.tsx#L233)
