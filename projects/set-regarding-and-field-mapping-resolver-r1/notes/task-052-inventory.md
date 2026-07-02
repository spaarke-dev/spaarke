# SRFR-052 · Private Picker Inventory (AssociationResolver v1.1.0)

**Date**: 2026-07-02
**Author**: task-execute agent

## What "private picker" means in AssociationResolverApp.tsx

The v1.1.0 file's manual-selection UI is a **Dropdown + Search-button** pair that (a) displays entity display names from `entityConfigs`, (b) tracks `selectedEntityType`, and (c) on click of the Search button opens `Xrm.Utility.lookupObjects` scoped to that type. On selection, `handleLookupClick` synthesizes an `IRecordSelection` and calls `handleRecordSelection(...)`.

This picker JSX + handler pair is the surface that the shared `PolymorphicPicker` collapses into a single `<PolymorphicPicker>` element.

## Private-picker JSX to REMOVE

Lines removed from `AssociationResolverApp.tsx`:

1. **searchSection block** (~lines 738-760, manual mode) — the `<div className={styles.searchSection}> <Dropdown ...> <Button ...icon={Search20Regular}> </div>`.
2. **`handleEntityTypeChange` handler** (~lines 330-337) — Dropdown `onOptionSelect` callback.
3. **`handleLookupClick` handler** (~lines 406-491) — Button click handler that opens `Xrm.Utility.lookupObjects` and calls `handleRecordSelection`.
4. **`Search20Regular` icon import** — no longer needed once the button is gone.
5. **`Dropdown` + `Option` component imports** — no longer needed.
6. **`styles.searchSection` + `styles.dropdown`** — private Griffel style entries no longer needed.

## What STAYS (auto-detect, refresh, toast, selected display)

**Retained deliberately** (NOT the "private picker" — these are AssociationResolver-specific UX around the picker):

- Auto-detect flow (`detectPrePopulatedParent` + `completeAutoDetectedAssociation` + read-only auto-detected display) — this is subgrid-relationship context handling, not covered by the shared picker.
- Field mapping application (`applyFieldMappings`, `fieldMappingHandler`, `useMappingToast`) — post-selection side effect specific to AssociationResolver.
- "Refresh from Parent" button + confirmation Dialog — an operation ON an existing selection, not a picker action.
- "Clear selection" button + `handleClearSelection` — clearing an existing selection, not a picker action.
- Selected-record display block (`selectedRecord && selectedEntityType` render) — shows the picked record + entity display name + navigate link + refresh/clear buttons.
- Toaster + MessageBar surfaces for error / mapping status.
- Version footer.

## Wire onSelect from shared PolymorphicPicker

The shared picker's `onSelect(entityType, recordId, recordName)` fires AFTER the user picks a record via `Xrm.Utility.lookupObjects`. This maps 1:1 to the pre-existing `handleRecordSelection(selection, webApi)` call inside `handleLookupClick`. In the refactor, `onSelect` becomes a new `handlePickerSelect` handler that:

1. Builds `selection: IRecordSelection` from the three args
2. Calls `handleRecordSelection(selection, context.webAPI)` — same call, same error handling
3. On success: updates `selectedRecord` state, calls `onRecordSelected` prop, computes `mappingStatus`, and invokes `applyFieldMappings` (existing helper) — identical to the current success branch of `handleLookupClick`.
4. On partial success: same as current — sets `selectedRecord`, notifies parent, records `error` if any, still calls `applyFieldMappings`.

Errors from the picker (Xrm unavailable, lookupObjects throw) are surfaced via the shared component's internal `MessageBar` + optional `onError` callback (which we wire to `setError`).

## AR-specific "quirks" — do they cross the picker contract?

Checked against `PolymorphicPickerProps` (contract):

| AR v1.1.0 behavior | Shared picker satisfies? | Notes |
|---|---|---|
| Entity-type dropdown showing `displayName` values from `entityConfigs` | ✅ Yes — catalog rendered as MenuItems | Adapter maps `EntityLookupConfig` → `RecordTypeCatalogEntry` |
| Open `Xrm.Utility.lookupObjects` scoped to picked entity | ✅ Yes — component does this internally | No wrapper needed |
| Show Spinner in Button while `isLoading` | ⚠️ Divergent (see below) | Shared picker has its own `isLookingUp` state; toolbar button disables during lookup |
| Disable Button when no entity selected OR `isLoading` OR `isApplyingMappings` | ✅ Yes via `disabled` prop | Wire `disabled={isLoading \|\| isApplyingMappings}` (empty-catalog disable is intrinsic to shared) |
| Error text via MessageBar | ✅ Yes — internal MessageBar + `onError` callback | We keep our own error state for non-picker errors (mapping etc.) |
| Placeholder "Select entity type" in Dropdown | ⚠️ Divergent — shared uses Menu, no placeholder concept | Menu opens on icon click; entity chosen inside menu directly. Acceptable improvement; consistent with SRFR-030 UX shift. |
| Two-step UX (choose type → click search) | ⚠️ Collapsed to one step (click icon → choose type → auto-lookup) | Intended UX improvement per SRFR-030 log §Design decision 1. |

**All divergences are consistent with the SRFR-021 + SRFR-030 UX pivot** (Menu + ToolbarButton beats Dropdown + Button — see wave-2-task-021.log line 71). Documented here as **intentional divergences**, not blockers.

## Catalog adapter

Shape mapping — `EntityLookupConfig` (from `RecordSelectionHandler.ts`) → `RecordTypeCatalogEntry` (from `@spaarke/ui-components`):

```ts
const pickerCatalog: RecordTypeCatalogEntry[] = entityConfigs.map(cfg => ({
  recordTypeRefId: cfg.logicalName,       // stable string key — picker doesn't touch the Dataverse GUID
  displayName: cfg.displayName,           // "Matter", "Contact", …
  logicalName: cfg.logicalName,           // "sprk_matter", "contact", …
  regardingField: cfg.regardingField,     // "sprk_regardingmatter"
  regardingRecordNumberField: cfg.regardingRecordNumberField, // optional; shared picker doesn't read it
}));
```

`RecordSelectionHandler` remains the authoritative source for the write path — the picker only picks; the handler writes. This matches the SRFR-030 pattern exactly.

## React types cast at the seam (per SRFR-030 log §Divergences 1)

The shared lib's `.d.ts` bundles `React.FC` from React 19 types; PCF pins React 16. The React 19 `React.FC` return type is incompatible with React 16's JSX element type.

Fixed at the seam:

```ts
import { PolymorphicPicker as PolymorphicPickerRaw, type PolymorphicPickerProps, type RecordTypeCatalogEntry, type IPolymorphicPickerWebApi } from '@spaarke/ui-components';
const PolymorphicPicker = PolymorphicPickerRaw as unknown as React.ComponentType<PolymorphicPickerProps>;
```

Runtime unaffected.

## Files touched by this task

- `src/client/pcf/AssociationResolver/AssociationResolverApp.tsx` — refactored (LOC delta negative)
- `projects/set-regarding-and-field-mapping-resolver-r1/notes/task-052-inventory.md` — this file
- `projects/set-regarding-and-field-mapping-resolver-r1/notes/wave-5-task-052.log` — execution log (post-task)
- `projects/set-regarding-and-field-mapping-resolver-r1/tasks/052-*.poml` — status → completed
- `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` — 052 🔲 → ✅

**No test file exists for AssociationResolverApp** — confirmed via glob search. Nothing to update.

## Grep verification target

After the refactor, `grep -R "PolymorphicPicker" src/client/pcf/AssociationResolver/` (excluding node_modules) MUST show only:
- The `import { PolymorphicPicker ...} from '@spaarke/ui-components'` line
- The `const PolymorphicPicker = PolymorphicPickerRaw as unknown as React.ComponentType<...>` cast
- The `<PolymorphicPicker ... />` JSX usage
- Comments referencing the shared component

Any hits inside the file for a private `PolymorphicPicker`-equivalent Dropdown block = leftover to delete.

## Coordination note (SRFR-051 running in parallel)

SRFR-051 refactors `handlers/RecordSelectionHandler.ts` internals. This task (SRFR-052) only calls `handleRecordSelection(selection, webApi)` — signature stable per SRFR-051's contract. No shared-file collision expected.
