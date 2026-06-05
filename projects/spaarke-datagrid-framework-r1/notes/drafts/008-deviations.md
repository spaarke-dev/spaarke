# Task 008 — Deviations from Spec

> **Task**: 008-command-bar (CommandBar primitive + 6 default handlers + CSV export + registry)
> **Status**: completed
> **Date**: 2026-06-01

This document captures intentional deviations from the task POML's expected outputs.

---

## 1. Test file location

**Spec POML** (`<outputs>`) lists:

```
<output type="test">src/client/shared/Spaarke.UI.Components/tests/csvExport.test.ts</output>
```

**Actual location**:

```
src/client/shared/Spaarke.UI.Components/src/components/DataGrid/commandBar/__tests__/csvExport.test.ts
```

**Reason**: The project's `jest.config.js` (`testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx']`) only discovers tests under `__tests__/` directories adjacent to the SUT. Placing the test under `/tests/csvExport.test.ts` would silently exclude it from `npm test`. The `__tests__/` colocated convention also matches every other test in the codebase (56 existing matches).

---

## 2. Test environment shims (Node `Blob` + `TextDecoder`)

**Issue**: jsdom's `Blob` shim in this project's `jest-environment-jsdom@30` setup lacks `.arrayBuffer()`, `.stream()`, and `.text()`. Standard browser-style approaches (`new Response(blob).arrayBuffer()`, `FileReader.readAsBinaryString`, `blob.text()`) either don't exist (`Response`, `arrayBuffer`) or strip the UTF-8 BOM during decoding — making it impossible to verify the BOM-presence acceptance criterion via standard test patterns.

**Resolution**: At the top of `csvExport.test.ts`, swap `globalThis.Blob` for `require('node:buffer').Blob` and `globalThis.TextDecoder` for `require('node:util').TextDecoder`. Node's implementations are spec-compliant and preserve every byte through `arrayBuffer()` + `TextDecoder({ ignoreBOM: true })`. The production code is unchanged.

**Impact**: None — production runs in the browser where `Blob` and `TextDecoder` are native and BOM-preserving. The shim is test-only.

---

## 3. `edit-columns` action default state

**Spec** says all 6 default actions are wired; project brief notes "edit-columns hidden in R1".

**Implementation**: `<CommandBar />` synthesizes `edit-columns` ONLY when the host explicitly opts in via `commandBarConfig.showDefaultCommands.editColumns = true`. By default it is omitted from the rendered toolbar.

**Reason**: design.md §11.5 explicitly defers the column-picker UI to R2. The handler stub exists in `DEFAULT_ACTION_HANDLERS` so registry lookups don't error, but rendering is suppressed by default.

---

## 4. `edit-filters` action behavior

**Spec** suggests `edit-filters` "toggles filter strip visibility (emit event or use context)".

**Implementation**: `defaultEditFiltersHandler` dispatches a `CustomEvent('spaarke-datagrid:toggle-filter-strip')` on `window`. The handler does NOT modify any shared state directly because the `<CommandBar />` does not own the filter-strip controller.

**Reason**: The filter strip lives in the parent `<DataGrid />` (or wrapping host). Wiring a controller setter through `DataGridContext` would add a coupling that the R1 scope (filter strip always visible) does not need. The event handle is documented in the file's JSDoc as a future hook point. R2 can refactor when the column / filter pickers actually ship.

---

## 5. Icon registry (string → component) approach

**Spec POML** assumes icons are referenced by string name (`'Add20Regular'`) per the `CommandBarItem.icon: string` schema (set in task 001 by the `DataGridConfiguration.ts` file).

**Implementation**: `CommandBar.tsx` defines a `ICON_REGISTRY` map of the 7 icon strings actually used by the 6 built-in actions + overflow trigger. Unknown icon strings resolve to `undefined` (icon-less button); ToolbarButton handles this gracefully.

**Reason**: importing every icon under `@fluentui/react-icons` would inflate bundle size unnecessarily, and the configjson contract intentionally constrains icons to a known set. This is consistent with the existing `CustomCommandFactory.ts` pattern in `Spaarke.UI.Components`.

**Future extension**: hosts wanting custom icons can extend via a future `registerCommandIcon(name, element)` API parallel to the handler registry, but R1 keeps the icon set fixed.

---

## 6. Tooltip integration

**Spec POML** does not call out tooltips explicitly.

**Implementation**: Each enabled toolbar button is wrapped in `<Tooltip content={item.label} relationship="label" withArrow>` for accessibility (NFR-04 hover affordance). Disabled buttons skip tooltips because Fluent v9 disabled elements break tooltip interactivity.

**Reason**: MDA OOB toolbars show hover tooltips; matching that improves visual parity (FR-DG-10) and helps screen-reader announcements via the `relationship="label"` ARIA hint.

---

## 7. `parentContext` shape simplified

**Spec FR-DG-15** defines `DataGridParentContext` as `{ entityType, id, name, ...extras }`.

**Implementation in CommandBar**: `CommandBarProps.parentContext` is typed as `{ entityType: string; id: string; name: string }` without the indexed extras. The default `create-form` handler only uses these three fields.

**Reason**: TypeScript's structural typing means any caller passing a fuller `DataGridParentContext` still type-checks (since the simpler type is a strict subset). Keeping the props type narrow prevents accidental misuse of `parentContext.someExtra` inside the default handlers where the shape is not guaranteed.

---

## Test results

- **csvExport tests**: 26/26 passing.
- **Build**: `npm run build` succeeds clean for the entire `Spaarke.UI.Components` package after this task's files were added (earlier transient errors in `chips/` were Wave A2 sibling task scope, not this task).

## Safety checks

- `window.confirm` matches inside `commandBar/`: 0 actual calls (3 documentary-comment mentions describing the explicit removal — these are intentional).
- Raw-hex matches inside `commandBar/`: 0.
- React-18-only API matches (`useId`, `useSyncExternalStore`, `createRoot`, `useTransition`, `useDeferredValue`): 0.
