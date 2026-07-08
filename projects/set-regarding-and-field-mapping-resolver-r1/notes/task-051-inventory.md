# SRFR-051 Inventory — RecordSelectionHandler Duplicate Write Logic

> **Executed**: 2026-07-02 · **Task**: SRFR-051 · **Rigor**: FULL

## Duplicate write logic in current `RecordSelectionHandler.ts`

### Field-name constants (local duplicate of shared-service knowledge)

```typescript
// L61-66
const DENORMALIZED_FIELDS = {
  recordName: 'sprk_regardingrecordname',
  recordId: 'sprk_regardingrecordid',
  recordType: 'sprk_regardingrecordtype',
  recordUrl: 'sprk_regardingrecordurl',
};
```
Missing 5th field: `sprk_regardingrecordnumber`. Duplicates the field enumeration that `applyResolverFields` in `@spaarke/ui-components` also encodes (see PolymorphicResolverService §Field Writes).

### `buildRecordUrl()` local duplicate (L73-124)

Duplicates the identical helper exported from `@spaarke/ui-components` (`buildRecordUrl`). Behavior parity verified: both walk Xrm context, extract clientUrl + appId, fall back to relative URL on failure.

### `getRecordTypeByEntityLogicalName()` local duplicate (L348-382)

Duplicates `resolveRecordType()` from the shared service. Both query `sprk_recordtype_ref`, cache per-page-lifetime, return null gracefully. Local version uses class-level Map cache; shared uses module-level `_recordTypeCache`.

### 4-field write payload construction (repeated twice)

**In `handleRecordSelection()` L451-485** (manual selection):
- setValue for `sprk_regardingrecordname`
- setValue for `sprk_regardingrecordid`
- setValue for `sprk_regardingrecordurl`
- setValue for `sprk_regardingrecordtype` (lookup)

**In `completeAutoDetectedAssociation()` L640-671** (auto-detect):
- Identical 4-field write, restructured slightly.

### Missing 5th-field write

Neither entry point writes `sprk_regardingrecordnumber` today. Per spec FR-B5-01, AssociationResolver should write this via delegation to the shared service (free once FR-C1-01 lands).

## Architectural distinction from RegardingResolver + shared service

`PolymorphicResolverService.applyResolverFields()` operates on a **payload object** for `webApi.createRecord()` / `webApi.updateRecord()` — sets `@odata.bind` keys for lookups. Requires `navProps` from metadata discovery.

`RecordSelectionHandler` operates on **`Xrm.Page` form attributes** via `getAttribute().setValue()` — no `@odata.bind`, no nav-props, no create/update payload. This is fundamentally a form-mode adapter.

### Delegation strategy

Because the semantics differ (payload vs form attributes), the thin adapter cannot directly delegate to `applyResolverFields()`. Instead, it MUST delegate to the shared service's **primitive helpers** (`resolveRecordType`, `buildRecordUrl`, `resolveRecordNumberFieldName`) and combine them via a local form-write orchestration. This is the ADR-012-compliant boundary (context-agnostic shared lib vs PCF-form-specific caller).

## Refactor scope

1. Delete `DENORMALIZED_FIELDS` const → replace with inline field names (or import a shared const if we add one)
2. Delete local `buildRecordUrl` + `getAppIdFromUrl` → import from `@spaarke/ui-components`
3. Delete local `getRecordTypeByEntityLogicalName` + `recordTypeCache` → import `resolveRecordType` from `@spaarke/ui-components`
4. Add 5th-field write: import `resolveRecordNumberFieldName` from shared lib + query target record for value
5. Preserve: `handleRecordSelection` + `completeAutoDetectedAssociation` entry-point signatures, the other-lookup-clearing behavior, form-attribute write semantics
6. Rewrite the write path to be data-driven via shared primitives — no local reinvention

## Behavior parity verification

| Behavior | Before | After |
|---|---|---|
| Manual selection writes 4 fields via Xrm.Page | ✅ | ✅ (via shared primitives) |
| Auto-detect writes 4 fields via Xrm.Page | ✅ | ✅ |
| 5th field (`sprk_regardingrecordnumber`) written | ❌ (missing) | ✅ (via shared `resolveRecordNumberFieldName`) |
| Clears other 7 entity-specific lookups | ✅ | ✅ (preserved wrap) |
| Uses cached Record Type lookups | ✅ (local cache) | ✅ (shared cache — same behavior) |

## Test approach

Since no tests exist today for RecordSelectionHandler (verified via `ls src/client/pcf/AssociationResolver/handlers/__tests__/` = not found), add new Jest tests that:
- Mock `Xrm.Page.getAttribute` returning attributes with `setValue` spies
- Mock `webApi.retrieveMultipleRecords` for `sprk_recordtype_ref` + target record queries
- Assert all 5 setValue calls happen with correct field names + values
- Assert other-lookup-clearing preserved
- Assert graceful-blank behavior when no record-number field is configured
