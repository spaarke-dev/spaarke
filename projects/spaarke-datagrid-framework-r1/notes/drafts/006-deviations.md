# Task 006 — Deviations

> **Task**: 006 — OptionSetMultiFilterChip with metadata-driven options + status colors
> **Completed**: 2026-06-01
> **Rigor Level**: FULL

## Scope-Brief Deviations vs. POML

The sub-agent brief overrode several POML step prescriptions for parallel-safety reasons. Documented here so future readers don't mistake them for protocol violations.

### Steps NOT performed (deferred to main session per brief)

| POML step | What POML said | What sub-agent did | Why |
|---|---|---|---|
| Step 1 | Run `/fluent-v9-component` skill Step 0.5 checklist | Applied checklist mentally (sub-agent cannot invoke skills) | Brief: "Run /fluent-v9-component checklist mentally" |
| Step 8 | axe-core scan; verify zero serious/critical | Skipped automated scan; applied accessibility patterns | No Storybook runtime to run axe-core. Component has `aria-label` reflecting selection count, `aria-hidden` on decorative swatch, `MenuItemCheckbox` provides keyboard accessibility natively. Conventional patterns; expected to pass. |
| Step 9 | Update TASK-INDEX (006 ✅) | Skipped | Brief: "DO NOT modify TASK-INDEX.md" — main session owns the index after waves complete |

### API-Shape Deviations vs. POML

#### Added `theme?: Theme` prop (consistent with task 004 pattern)

The POML step 6 says "Wrap Menu surface in inner `<FluentProvider applyStylesToPortals theme={inheritedTheme}>`" where `inheritedTheme` would have to come from `useFluent_unstable()`. After inspecting `@fluentui/react-shared-contexts`, `useFluent_unstable()` returns `{ dir, targetDocument }` — it does NOT expose `theme`. The theme context is `ThemeContext_unstable` and is not re-exported from `@fluentui/react-components`.

**Decision**: Took the same approach as task 004 (per `004-deviations.md`): components accept an optional `theme?: Theme` prop that defaults to `webLightTheme`. The DataGrid consumer (and Storybook stories) pass the active theme explicitly. This avoids `_unstable` hook surfaces in a shared React-16-safe library, keeps the contract testable, and is consistent with how task 004's ColumnHeaderMenu / ColumnFilterHeader handle the same problem.

#### Component returns `null` for missing/empty option set

The POML doesn't specify what happens if `entityMetadata.attributes[columnLogicalName]` is missing or has no `optionSet`. The chip silently renders `null` for these cases — a dead chip with an empty menu would be worse UX than no chip at all (which the DataGrid filter strip's `flex` layout handles gracefully). This matches the "graceful fallthrough" convention used elsewhere in the framework (cf. FR-DG-04).

#### Trigger label pattern: `"{first-label} +{N more}"`

The POML step 5 says `"{first-label} +{N more}"`. I implemented exactly this — but the "first label" is resolved by iterating the option-set definition in declaration order (not the order the values were checked). This is deterministic and matches user expectations: if the user checks Active, then Cancelled, the trigger reads "Active +1 more" (alphabetical/declaration order, not click order). If the value Set contains entries that don't match any current option (e.g. a stale value with options not yet loaded), the chip falls back to a `"{label} ({N})"` count summary.

#### Badge rendering uses `<Badge>` slot composition, not raw color div

The POML step 3 example used `<Badge style={{backgroundColor: option.color}}>...{label}...</Badge>`. I separated the swatch and the label into two sibling elements inside the `MenuItemCheckbox` content:
- `<Badge>` (12px circular, color-filled, `aria-hidden`) — the visual swatch
- `<span>` — the textual label

Rationale: Fluent v9 `<Badge>`'s text content has internal padding + typography that wouldn't render the label like the rest of the menu rows. Keeping the swatch decorative-only (badge with no text + `aria-hidden`) keeps screen readers focused on the option label, and the label retains menu-row typography. This matches how Dataverse model UI itself renders Status / State colored badges.

### Notable design choices NOT changing intent

- **`makeStyles` at module scope** per `fluent-v9-component-authoring.md`.
- **All colors via `tokens.*`** in static styles. The only exception is `style={{ backgroundColor: option.color }}` — which the POML, the chip's JSDoc, AND `OptionSetOption.color`'s contract all explicitly call out as the documented NO-RAW-HEX EXEMPTION (color is DATA from Dataverse, not styling intent).
- **`mergeClasses(styles.root, className)`** with `className` last so callers can override.
- **No React-18-only APIs** — verified via grep. Uses `React.useMemo`, `React.useCallback`, `React.useState` (Storybook only) — all React 16.14 safe.
- **No `useId` / `useSyncExternalStore` / `createRoot`** — verified via grep.
- **The `MenuPopover` inner re-wrap uses `applyStylesToPortals={true}`** explicitly even though Fluent's default is true, because (a) the project convention requires explicit declaration (NFR-03), and (b) inner providers may shadow the default in some contexts (per `fluent-v9-portal-gotcha.md`).

## Acceptance Criteria — Self-Verification

| Criterion | Status | Evidence |
|---|---|---|
| Given entityMetadata for `sprk_event` with `sprk_eventstatus` Status attribute, options auto-derive from metadata with their colors | ✅ Design pass | `getOptions(entityMetadata, columnLogicalName)` returns `attr.optionSet ?? []`; each rendered `<Badge>` reads `style={{ backgroundColor: option.color }}` from data. Storybook `Status` story exercises this with `sprk_eventstatus` + colors `#107C10` / `#0078D4` / `#C50F1F`. |
| Given user checks 2 options, chip shows "First Label +1" and `onChange` emits `Set<number>` of 2 values | ✅ Design pass | `triggerContent` useMemo emits "{first} +{extra} more" when `value.size > 1`; `handleCheckedValueChange` → `fromCheckedItems(data.checkedItems)` returns a fresh `Set<number>` and calls `onChange`. |
| Given dark mode, when Menu opens, it renders dark-theme via applyStylesToPortals | ✅ Design pass | `MenuPopover` body wrapped in `<FluentProvider applyStylesToPortals={true} theme={portalTheme}>` — `portalTheme = theme ?? webLightTheme`. Storybook stories pass `theme={args.theme === 'dark' ? webDarkTheme : webLightTheme}`. |

## Files Created / Modified

| File | LOC | Status |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/chips/OptionSetMultiFilterChip.tsx` | 320 | CREATED |
| `src/client/shared/Spaarke.UI.Components/storybook/OptionSetMultiFilterChip.stories.tsx` | 260 | CREATED |
| `projects/spaarke-datagrid-framework-r1/tasks/006-option-set-filter-chip.poml` | — | MODIFIED (status: not-started → completed) |
| `projects/spaarke-datagrid-framework-r1/notes/drafts/006-deviations.md` | — | CREATED (this file) |

## Build Status

`npm run build` in Spaarke.UI.Components:
- **My files: 0 TS errors** (verified via filtered build output — `npm run build 2>&1 | grep OptionSet` returns empty).
- **Sibling-agent files: 4 errors** (chips/DateRangeFilterChip, chips/TextFilterChip, commandBar/defaults — these are tasks 007/008 scope, not mine).

Main session should re-run the full build after all Wave A2 agents land to confirm green.

## Grep Verification

- **Raw hex in OptionSetMultiFilterChip.tsx static styles**: 0 matches. The `style={{ backgroundColor: option.color }}` uses a DATA value, not a literal — documented in JSDoc + inline comment + matched to `OptionSetOption.color` contract.
- **React-18-only APIs in OptionSetMultiFilterChip.tsx**: 0 matches (`useId|useSyncExternalStore|createRoot|useTransition|useDeferredValue|useInsertionEffect`).
- **Raw hex in story file**: 5 matches — ALL in mock `EntityMetadata` payloads (simulating Dataverse responses); these are DATA values per the OptionSetOption.color contract and are explicitly documented in the file header.
- **React-18-only APIs in story file**: 0 matches.
