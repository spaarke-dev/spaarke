# Task 021 — Picker Diff (RegardingResolver vs AssociationResolver)

**Purpose**: Reference-only summary of how the two current PCFs implement their pickers, and how `PolymorphicPicker` in `@spaarke/ui-components` consolidates the pattern.

## Existing patterns

### RegardingResolver (`RegardingResolverApp.tsx` v1.2.0)

- Layout: `Dropdown` labeled "Record Type" + primary `Button` "Select Record" with `SearchRegular` icon
- Data source: `TODO_REGARDING_CATALOG` filtered via `resolveAllowedCatalog(manifest input)` -> `ITodoRegardingTargetCatalogEntry[]`
- Lookup flow:
  1. User picks entity type via dropdown
  2. Clicks "Select Record"
  3. Calls `xrm.Utility.lookupObjects({ defaultEntityType, entityTypes: [t], allowMultiSelect: false })`
  4. Delegates result to `applyRegardingSelection(writeCtx, selection)` (ADR-024 write handler)
- Extra behavior (specific to RegardingResolver, NOT part of picker contract):
  - Read-only render branch (FR-24)
  - Create-mode `window.__sprk_regarding_pending__` bridge
  - `notifyOutputChanged` to bound `sprk_regardingrecordtype` lookup
  - `Clear` button, `Open` link, boundRecordType hydration

### AssociationResolver (`AssociationResolverApp.tsx` v1.1.0)

- Layout: `Dropdown` (uses `displayName` as label) + primary `Button` "Select Record" with `Search20Regular` icon
- Data source: dynamic `entityConfigs: EntityLookupConfig[]` loaded from `sprk_recordtype_ref` via `loadEntityConfigs(webApi)`
- Lookup flow: identical `Xrm.Utility.lookupObjects` invocation
- Extra behavior (specific to AssociationResolver, NOT part of picker contract):
  - Auto-detect pre-populated parent (subgrid context)
  - Field mapping handler + "Refresh from Parent" button + confirmation dialog
  - Toaster + mapping-result feedback

## Common (extract) surface

Both:

1. Render a small header row (title + trigger)
2. Present a list of allowed entity types (from catalog)
3. Call `Xrm.Utility.lookupObjects` for the chosen entity
4. Hand the picked record `(entityType, recordId, recordName)` back to a caller

## Design divergences (deliberately NOT ported to shared component)

| Divergence | RegardingResolver | AssociationResolver | Shared component |
|---|---|---|---|
| Entity option label | `entityType` (logical) | `displayName` | `displayName` (better UX) |
| Trigger UI | `Dropdown` + primary `Button` "Select Record" | `Dropdown` + primary `Button` "Select Record" | **`Menu` + `ToolbarButton` with `SearchRegular` icon** (per task POML §Pattern) |
| Catalog shape | `ITodoRegardingTargetCatalogEntry` (Todo-specific) | `EntityLookupConfig` (AR-local) | `RecordTypeCatalogEntry` (new, generic) |
| Selection callback | `applyRegardingSelection` (handler) | `handleRecordSelection` (handler) | Plain `onSelect(entityType, recordId, recordName)` (caller-owned side effects) |
| Cancel handling | early `return` after zero results | early `return` after zero results | Same — no `onSelect` invocation |
| Failure handling | try/catch → setError state | try/catch → setError state | Component surfaces error via MessageBar; caller may pass callback for external logging (future) |

## `PolymorphicPicker` extracted contract (task 021)

```typescript
export interface RecordTypeCatalogEntry {
  recordTypeRefId: string;               // sprk_recordtype_refid
  displayName: string;                   // sprk_recordtypename / sprk_recorddisplayname
  logicalName: string;                   // sprk_recordlogicalname
  regardingField: string;                // sprk_regardingfield (host-side lookup)
  regardingRecordNumberField?: string;   // sprk_regardingrecordnumberfield (target-side number)
}

export interface PolymorphicPickerProps {
  catalog: readonly RecordTypeCatalogEntry[];
  onSelect: (entityType: string, recordId: string, recordName: string) => void;
  webApi: IPolymorphicPickerWebApi;      // narrow shim, NOT ComponentFramework
  disabled?: boolean;
  readOnly?: boolean;
  title?: string;                        // default "Related Record"
  onError?: (message: string) => void;
  className?: string;
}
```

- **Layout**: 1 row, title text (left) + `ToolbarButton` with `SearchRegular` icon (right).
- **Menu**: `Menu` -> `MenuTrigger` -> `MenuPopover` -> `MenuList` -> one `MenuItem` per catalog entry (displayName label).
- **Lookup**: on click, `Xrm.Utility.lookupObjects({ entityTypes: [entityType], allowMultiSelect: false })`; empty/cancel = noop.
- **Callback**: `onSelect(entityType, recordId, recordName)` — caller owns side effects (writes, dirty state, toasts, feature-gated field mapping).
- **ADR-012 compliance**: no `ComponentFramework.*` in the component; `webApi` is a narrow shim with only what's needed (currently nothing — `Xrm.Utility.lookupObjects` is the only external call and it's routed via a `getXrm()` bridge inside the component). We accept `webApi` as a prop for future extension (record queries for enrichment) so consumers standardize on injection now rather than a later breaking refactor.
- **ADR-022 compliance**: no React-18-only APIs (`useTransition`, `useDeferredValue`); no `createRoot`; component is React 16/17-safe.
- **ADR-021 compliance**: `makeStyles` at module scope, all colors/spacing/radius via `tokens.*`.

## Adoption plan (waves 3 and 5, NOT this task)

- Wave 3 task SRFR-030+: RegardingResolver imports `PolymorphicPicker` from `@spaarke/ui-components`, wraps it with the read-only branch + Create-mode bridge + `applyRegardingSelection` handler.
- Wave 5 task SRFR-050+: AssociationResolver imports `PolymorphicPicker`, keeps its auto-detect + field mapping + toast layer around the shared trigger.

## Consumer-owned responsibilities (stay in each PCF)

- Read-only render branch
- Create-mode field bridge
- Write handler invocations (`applyResolverFields` / `handleRecordSelection`)
- Toast/status notification wiring
- Auto-detect subgrid parent
- Field mapping refresh flow

## Notes

- The task POML explicitly asks for `ToolbarButton + Menu` UX, replacing the current `Dropdown + Button` pattern. This is a small UX improvement (single control instead of two, less form real-estate) and both current PCFs will pick up the change when they adopt the shared component in waves 3 and 5.
- `sprk_recordtype_ref` (Wave 1 task 010) is the single canonical catalog source for callers to derive the new `RecordTypeCatalogEntry[]`.
