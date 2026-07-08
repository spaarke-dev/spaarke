# SRFR-020 — Inventory of `applyResolverFields()` callers

> Purpose: capture current input shape + output payload keys before extending; verify backward-compat surface.

## Current signature

```ts
export async function applyResolverFields(
  webApi: IPolymorphicWebApi,
  entity: Record<string, unknown>,
  navProps: INavPropEntry[],
  parentEntityLogicalName: string,
  parentEntitySet: string,
  parentRecordId: string,
  parentRecordName: string,
  entityLookupHint?: string
): Promise<void>;
```

## Current output payload (mutated on `entity`)

| Key | Source |
|---|---|
| `<entityNavProp>@odata.bind` | entity-specific lookup (found via `findNavProp`) |
| `sprk_regardingrecordid` | cleaned parent GUID (text) |
| `sprk_regardingrecordname` | `parentRecordName` (text) |
| `sprk_regardingrecordurl` | `buildRecordUrl(entity, id)` (URL text) |
| `<rtNavProp>@odata.bind` | `sprk_recordtype_ref` binding |

**4 denormalized fields today.** Task extends to a 5th: `sprk_regardingrecordnumber`.

## Callers (repo-wide grep)

### Production callers (3)

| # | Caller | File | Line | Notes |
|---|---|---|---|---|
| 1 | `applyRegardingSelection` (RegardingResolver PCF) | `src/client/pcf/RegardingResolver/RegardingResolver/handlers/ResolverWriteHandler.ts` | 218 | Wraps for the 11-entity picker. Sets clear-and-set for other 10 lookups then delegates. |
| 2 | `TodoService.createTodo` | `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts` | 189 | CreateTodoWizard flow. |
| 3 | `useInlineTodoCreate` hook | `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useInlineTodoCreate.ts` | 289 | Daily briefing sub-row create. |

### Test callers (mocked, do not exercise real service)

- `src/client/pcf/RegardingResolver/__tests__/ResolverWriteHandler.test.ts` — mocks `applyResolverFields` at the module boundary.
- `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` — mocks entire `@spaarke/ui-components`.

## Backward-compat requirements

- Callers 1, 2, 3 do NOT know about the new record-number field. They MUST continue to work with the existing 4-field payload if the resolver cannot resolve the source field (metadata-null case per NFR-06).
- New parameter must be **optional**. Recommended shape: extend to accept a trailing `options?: { sourceRecordNumberField?: string }` argument to preserve positional compatibility.
- The service should auto-resolve the record-number field from `sprk_recordtype_ref.sprk_regardingrecordnumberfield` if the caller does not supply `sourceRecordNumberField` explicitly — that's the FR-A4-01 data-driven behavior.

## D-13 per-target host column name

- 9 sprk_ target entities → HOST writes to `sprk_regardingrecordnumber` (uniform).
- Wait — D-13 is about the HOST target-column naming, not the target-entity source-column.
- HOST column on `sprk_todo` and `sprk_communication` is uniformly `sprk_regardingrecordnumber` (both are `sprk_` entities).
- D-13 applies only to Wave 1 schema additions on TARGET entities' `sprk_regardingrecordnumber` column (contact, account got `contact_/account_` prefixes because of OOB).
- So: **HOST-side write** always to `sprk_regardingrecordnumber`. **Target-side read** uses whatever field name is in `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (e.g., `sprk_matternumber`, `accountnumber`, or null for Contact/Person → graceful-blank).
- **No convention-derivation needed on the write path**. The task's mention of per-target column names refers to the source-field on the target entity, which is fully catalog-driven.

## `__sprk_regarding_pending__` bridge shape

Currently emitted by `RegardingResolverApp.tsx` (lines 337-351):

```ts
window.__sprk_regarding_pending__ = {
  hostEntity, entityType, entitySet, lookupAttribute,
  recordId, recordName, recordUrl,
};
```

Task requires adding a `recordNumber` key. **The PCF sets this**, NOT the shared service. So `applyResolverFields()` must return the resolved record-number so `RegardingResolverApp` can propagate it. Task 032 (Wave 3) is the actual consumer that adds `recordNumber` to `__sprk_regarding_pending__` — this task provides the value.

## Design decision — return-value change

The current signature is `Promise<void>`. To surface the resolved record-number value to callers without forcing them to re-query, return a `Promise<IApplyResolverFieldsResult>` with the resolved record-number.

Backward compat check: callers 1, 2, 3 all `await applyResolverFields(...)` without using the return value. TypeScript widens `Promise<void>` → `Promise<T>` compatibly (a `void` awaiter can still consume any T).

## Summary of extensions

1. Add optional final param `options?: { sourceRecordNumberField?: string }`.
2. Add helper `resolveRecordNumberFieldName(webApi, entityType): Promise<string | null>`.
3. Change return type from `Promise<void>` to `Promise<IApplyResolverFieldsResult>` (backward-compat).
4. When source-field resolves and target record's value is non-null → set `entity['sprk_regardingrecordnumber']` on payload AND include in return value.
5. Metadata-null case → `console.warn` per NFR-06, skip the record-number write, return `null` in result.
6. Target-record null-value case → same graceful-blank behavior.
