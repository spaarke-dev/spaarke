# Wave 3 · Task 030 · RegardingResolver 2-row layout — inventory

**Task**: SRFR-030 — RegardingResolver 2-row streamlined layout + toolbar icon + PolymorphicPicker consumption + manifest properties
**Baseline version**: v1.2.0 (will remain 1.2.0 for this task; SRFR-033 bumps to 1.3.0)

## Current v1.2.0 layout snapshot

Multi-field visible layout with three visible cells inside `.searchSection`:

1. **Record Type Dropdown** (`Dropdown` — 11 entities) + Label above it
2. **Select Record Button** (primary, opens `Xrm.Utility.lookupObjects`)
3. **Selected Target row** (post-selection): `entityType : name Link + Open16Regular icon` + `Clear` subtle button
4. **Currently row** (pre-selection when bound lookup exists): `Currently: <name> [Clear]`
5. **Footer**: `v{version} • {entityType}`

Read-only mode collapses to `selectedLabel + Link + Open` or `boundRecordType.name` text.

## Bound properties on current manifest

| Property | of-type | usage | required | Default |
|---|---|---|---|---|
| `regardingRecordType` | Lookup.Simple | bound | true | — |
| `entity` | SingleLine.Text | input | true | — |
| `regardingTargets` | SingleLine.Text | input | false | — |
| `readOnly` | TwoOptions | input | false | — |

**Missing (added in this task)**:
- `regardingRecordNumberField` (bound, default `sprk_regardingrecordnumber`)
- `regardingRecordNameField` (bound, default `sprk_regardingrecordname`)
- `title` (input, default `Related Record`)

## Existing hooks / state in v1.2.0 React root

- `styles = useStyles()` (Griffel semantic tokens)
- `hostEntity` (from manifest `entity`)
- `catalog` (memo of `resolveAllowedCatalog`)
- `boundRecordType` (from bound lookup `raw`)
- `selectedEntityType` (state)
- `selectedTarget` (state)
- `isLookupPending`, `isWriting`, `error`, `statusMsg` (transient state)
- `writeCtx` (memo — webApi + hostEntity + hostRecordId)
- effect: sync default selected entity type

## Existing picker logic (v1.2.0)

- `handleEntityTypeChange` — dropdown option-select
- `handleSelectRecord` — calls `Xrm.Utility.lookupObjects` and `applyRegardingSelection` (local handler wrapper around shared `applyResolverFields`)
- `handleClear` — calls `clearRegarding`
- Post-selection CREATE-mode path: sets `window.__sprk_regarding_pending__` (SRFR-032 will extend to include `recordNumber`)
- Post-selection UPDATE-mode: `applyResolverFields` already persists via `webApi.updateRecord`

## Handlers directory

`RegardingResolver/handlers/ResolverWriteHandler.ts` — wraps shared `applyResolverFields` with nav-prop discovery + clear-and-set of 10 other lookups. This continues to be the write path in v1.3.0. NOT retired in this task.

## Wave 2 deliverables to consume

- `PolymorphicResolverService.applyResolverFields(...)` — extended to 5-field write; returns `{ recordNumber, recordNumberSourceField }` (used by SRFR-032 via `ResolverWriteHandler`)
- `PolymorphicPicker` from `@spaarke/ui-components` — Fluent v9 title + toolbar icon + Menu; wired to `Xrm.Utility.lookupObjects`; onSelect `(entityType, recordId, recordName)`
- `RecordTypeCatalogEntry` interface — for catalog rows (recordTypeRefId, displayName, logicalName, regardingField, regardingRecordNumberField?)

## Design decisions for this task

1. **Retain `handlers/ResolverWriteHandler.ts`** — do NOT replace with direct `PolymorphicPicker` onSelect body. The handler encapsulates nav-prop discovery + clear-and-set which the PCF still needs. `PolymorphicPicker` provides only the UI trigger.

2. **Catalog derivation** — the shared `PolymorphicPicker` takes `RecordTypeCatalogEntry[]`; the RegardingResolver already uses the internal `TODO_REGARDING_CATALOG` (`ITodoRegardingTargetCatalogEntry`) shape. Adapt the internal catalog to `RecordTypeCatalogEntry` in a memo (map entityType→logicalName, entitySet→n/a, lookupAttribute→regardingField, entityType→displayName). Also expose a stable id derived from `entityType` (no `sprk_recordtype_ref` UUID needed at this layer — the picker just needs a `recordTypeRefId` key).

3. **Row 2 record-number click** — TODO-only stub for SRFR-031 (modal open); rendered as `<Link>` with `data-testid="regarding-resolver-record-number"` + `role="link"`.

4. **Row 2 record-name** — plain `<Text>`.

5. **Bound field defaults** — new properties `regardingRecordNumberField` and `regardingRecordNameField` are bound; when maker binds them to a specific field on the host entity, the raw value = current field value on that host record. For display only (this task's rendering); the actual WRITE path uses hard-coded `sprk_regardingrecordnumber` + `sprk_regardingrecordname` inside `applyResolverFields` (per FR-A4-01), matching what the shared service writes.

6. **Backward compat** — the existing 3-field visible layout is COMPLETELY REPLACED by the 2-row layout. The manifest keeps all previously-declared properties, so existing form bindings continue to work; new bound properties have defaults that match Smart Todo R4 conventions.

7. **Read-only branch** — keep existing behavior for now; SRFR-033 handles the polished read-only variant.

## Files to modify

- `src/client/pcf/RegardingResolver/RegardingResolver/index.ts` — no structural change (getOutputs unchanged; new bound props are read on the React tree, not surfaced back)
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx` — REWRITE React root for 2-row layout + PolymorphicPicker consumption
- `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverHost.tsx` — pass version + host wiring (may need small adjustment)
- `src/client/pcf/RegardingResolver/RegardingResolver/ControlManifest.Input.xml` — add 3 new properties, preserve platform-libraries
- `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx` — replace v1.2.0 tests with 2-row structural + PolymorphicPicker + manifest-default tests

## New/updated unit test coverage plan

1. **2-row DOM structure**: `data-testid` for `regarding-resolver-row-1` + `regarding-resolver-row-2`; assert row 1 contains title + `polymorphic-picker-trigger`, row 2 contains `role="link"` for record-number + text for record-name.
2. **Toolbar-icon menu opens with 11 catalog entries**: user-events click on `polymorphic-picker-trigger`, assert `polymorphic-picker-menu` has 11 items.
3. **PolymorphicPicker.onSelect invokes applyResolverFields**: mock `@spaarke/ui-components.applyResolverFields`; simulate `Xrm.Utility.lookupObjects` returning `[{ id, name }]`; assert the mocked `applyResolverFields` was called.
4. **Manifest defaults render on missing bindings**: pass `context.parameters.title.raw = null`; assert `Related Record` text renders.
5. **No console errors on init**: spy on `console.error`, render, assert no calls.

## Constraints reiterated

- ADR-022 — virtual pattern preserved; React 16 + Fluent 9 platform-library declarations retained verbatim
- ADR-012 — `PolymorphicPicker` consumed via `@spaarke/ui-components` public export
- ADR-021 — Fluent v9 semantic tokens only (Griffel)
- Version stays 1.2.0 for this task

