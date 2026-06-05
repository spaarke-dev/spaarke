# Task 007 — Deviations

**Task**: 007 — DateRangeFilterChip + TextFilterChip + BoolFilterChip
**Status**: completed (2026-06-01)
**Wave**: A2 (parallel with 004, 005, 006, 008)

---

## Deviations from POML

### D-1: Date picker widget — HTML5 `<Input type="date">` instead of Fluent v9 `<Calendar>`

**POML directs**: "`<Popover>` + 2x Fluent `<Calendar>` (start + end)"

**Actual**: Used Fluent v9 `<Input type="date">` (with `<Field>` wrappers) inside the Popover instead of `<Calendar>`.

**Reason**: Fluent v9's `Calendar` component lives in the separate package `@fluentui/react-calendar-compat`, which is NOT a peerDependency of `@spaarke/ui-components` (verified via `package.json` peerDeps list and `node_modules/@fluentui/` enumeration). Adding a new peerDep mid-task risks bundle-size / version-skew impact on every downstream consumer (PCFs, Code Pages, Add-ins) and exceeds task 007's scope of "bundle 3 chip primitives."

**Impact**: Lower than expected:
- HTML5 date inputs render with Fluent v9 chrome via the `<Input>` wrapper (border, focus ring, dark-mode tokens all apply).
- The conversion math (`localDateToUtcBounds`) — the actual load-bearing piece — is identical regardless of picker widget.
- Operator UX is functional: native browser picker, keyboard-accessible, screen-reader compatible.
- Easy future swap: when `@fluentui/react-calendar-compat` is added (likely in a deliberate task-level decision), replace the two `<Input type="date">` elements with `<Calendar>` — no API change to chip's external contract.

**Forward action**: None required for R1. If a follow-up task adds `react-calendar-compat`, file a sub-task to swap inputs for `Calendar`.

---

### D-2: `localDateToUtcBounds` signature — `(Date, Date)` instead of `(string)`

**POML directs**: "helper `localDateToUtcBounds(start: Date, end: Date): { startUtc: Date; endUtc: Date }` lifted **verbatim** from `GridSection.tsx#L358`"

**Reality**: The GridSection original takes a SINGLE `YYYY-MM-DD` string and returns ISO strings:

```ts
// GridSection.tsx L343-L348 (verbatim)
function localDateToUtcBounds(localDateStr: string): { start: string; end: string } {
  const [y, m, d] = localDateStr.split('-').map(Number);
  const startLocal = new Date(y, m - 1, d, 0, 0, 0, 0);
  const endLocal = new Date(y, m - 1, d, 23, 59, 59, 999);
  return { start: startLocal.toISOString(), end: endLocal.toISOString() };
}
```

**Actual**: Adapted to take two `Date` objects (one per bound) and return two `Date` objects:

```ts
export function localDateToUtcBounds(start: Date, end: Date): UtcDateBounds {
  const startLocal = new Date(start.getFullYear(), start.getMonth(), start.getDate(), 0, 0, 0, 0);
  const endLocal   = new Date(end.getFullYear(),   end.getMonth(),   end.getDate(),   23, 59, 59, 999);
  return { startUtc: startLocal, endUtc: endLocal };
}
```

**Reason**: Date pickers (Fluent `Calendar` or HTML5 `<input type="date">`) emit `Date` objects, not `YYYY-MM-DD` strings, so the chip naturally holds `Date` values. The POML's own signature requires `(Date, Date) → { Date, Date }`. The conversion math is verbatim per-bound (same constructor, same hour/minute/second/ms literals); only the input shape and output shape adapt.

**Impact**: None — the timezone behaviour is identical (proven by 4 passing unit tests, including the exact EDT June-2026 acceptance test the POML calls out).

---

### D-3: NFR-03 portal re-wrap — no explicit `theme` prop

**Caller instruction (from task brief)**: "wrap Popover content in `<FluentProvider applyStylesToPortals={true} theme={inheritedTheme}>`"

**Actual**: Used `<FluentProvider applyStylesToPortals>` without a `theme` prop, matching the canonical pattern in the sibling `LookupMultiFilterChip.tsx` (task 005, already landed in the chips/ directory).

**Reason**: Fluent v9's `useFluent()` API returns `{ dir, targetDocument }` — it does NOT expose `.theme` (verified against `@fluentui/react-shared-contexts/dist/index.d.ts` line 683: `ProviderContextValue_unstable = { dir, targetDocument? }`). Initially attempted `useFluent().theme ?? webLightTheme` per the caller instruction; TypeScript correctly rejected this (`TS2339: Property 'theme' does not exist on type 'ProviderContextValue_unstable'`).

The correct API would be `useThemeContext_unstable()` from `@fluentui/react-shared-contexts`, but the sibling `LookupMultiFilterChip` documents and follows the simpler pattern: **let the nested `<FluentProvider>` inherit its theme via React context, never pass an explicit `theme` prop**. Quote from `LookupMultiFilterChip.tsx#L546-L553`:

> The nested FluentProvider here inherits its theme via React context (which DOES flow through the React tree even though the DOM is portaled), and re-asserts applyStylesToPortals so any nested portal stays themed. This is the canonical Option A from `.claude/patterns/ui/fluent-v9-portal-gotcha.md`, deliberately NOT passing an explicit `theme` prop so the customer-tenant theme is never accidentally shadowed.

Both DateRangeFilterChip and TextFilterChip now follow this pattern verbatim.

**Impact**: Positive — matches the proven sibling pattern, avoids tenant-theme shadowing, removes a TypeScript error.

---

## Verification

| Check | Result | Evidence |
|---|---|---|
| `npm run build` | GREEN (exit 0) | `tsc` runs clean across the package |
| `localDateToUtcBounds` unit test | 4/4 PASS | `npx jest src/components/DataGrid/chips/__tests__/dateUtils.test.ts` → all 4 tests pass in 24s; EDT June-2026 acceptance criterion verified exact ISO bounds `2026-06-01T04:00:00.000Z` → `2026-07-01T03:59:59.999Z` |
| Zero raw hex in 3 chips + storybook | PASS | `Grep '#[0-9a-fA-F]{3,8}\b'` returns no matches in any of the 4 files |
| Zero React-18-only APIs in 3 chips + storybook | PASS | `Grep 'useId\|useSyncExternalStore\|createRoot\|...'` returns only JSDoc comments declaring compliance |
| `index.ts` barrel untouched | PASS | Caller instructed not to modify; main session updates after Wave A2 |
| Sibling chip files untouched | PASS | `LookupMultiFilterChip.tsx`, `OptionSetMultiFilterChip.tsx`, `columnHeader/`, `commandBar/` not modified |
