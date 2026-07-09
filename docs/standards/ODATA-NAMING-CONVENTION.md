# Dataverse OData Naming Convention — LogicalName vs SchemaName

> **Status**: Active (binding)
> **Created**: 2026-07-08
> **Source**: R7 W12 latent-bug discovery (`useInlineTodoCreate.ts` fix, commit `2a7e47771`); codified by R5 FR-C9
> **Audience**: Anyone writing a Dataverse Web API request (`@odata.bind`, `$filter`, `$select`, `$expand`) from client or server code
> **Companion**: [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](DATA-ACCESS-DECISION-CRITERIA.md) (which transport to use); this doc governs attribute-name casing once you're on either transport

---

## The rule

Dataverse exposes two different names for the same attribute, and the Web API expects a different one depending on what you're doing:

1. **Binding a lookup via `@odata.bind`** → use the **PascalCase attribute SchemaName** as the navigation-property name.
2. **Filtering or selecting a scalar** (FetchXml, `QueryExpression`, OData `$filter`/`$select`) → use the **lowercase attribute LogicalName**.

Mixing these up is the single most common cause of a silent-looking OData 400 on an otherwise-correct lookup write.

---

## Where each is used

| Name form | Used for | Example |
|---|---|---|
| **LogicalName** (lowercase) | FetchXml attribute names; `QueryExpression` `ColumnSet` / `Filter` conditions; Web API `$select` and `$filter` query-string values | `sprk_assignedto` |
| **SchemaName** (PascalCase) | `@odata.bind` navigation-property names; Web API `$expand` navigation-property names | `sprk_AssignedTo` |

The two strings look almost identical — same base name, different casing — which is exactly why the bug is easy to introduce and hard to spot in review. LogicalName is what you see in Dataverse solution attribute lists; SchemaName is the metadata-defined navigation property Dataverse generates for the lookup (usually the same text, PascalCase, but not guaranteed to be a pure case transform for every attribute — always confirm against the entity's attribute metadata when in doubt).

---

## Examples

### Correct — `@odata.bind` uses PascalCase SchemaName

```typescript
// Binding a lookup: navigation-property name must be the SchemaName.
await webApi.updateRecord("sprk_todo", todoId, {
  "sprk_AssignedTo@odata.bind": `/contacts(${contactId})`,
});
```

### Incorrect — `@odata.bind` using lowercase LogicalName

```typescript
// WRONG: "sprk_assignedto" is the LogicalName, not the SchemaName.
// Dataverse rejects this with an OData 400:
//   "An undeclared property 'sprk_assignedto' which only has property
//    annotations in the payload but no property value was found in
//    the payload."
await webApi.updateRecord("sprk_todo", todoId, {
  "sprk_assignedto@odata.bind": `/contacts(${contactId})`, // ❌ wrong casing
});
```

The fix is a one-line casing change: `sprk_assignedto@odata.bind` → `sprk_AssignedTo@odata.bind`.

---

## Correct-usage reference sites

The convention is already applied correctly at:

- `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:759`
- `src/solutions/SmartTodo/src/components/SmartToDo.tsx:680`
- `src/client/shared/Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx:636`
- `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts:263`

---

*Maintained by the project owner.*
