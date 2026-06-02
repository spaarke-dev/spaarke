# Task 004 — Deviations

> **Task**: 004 — Lift ColumnHeaderMenu + ColumnFilterHeader from events-components with applyStylesToPortals fix
> **Completed**: 2026-06-01
> **Rigor Level**: FULL

## Scope-Brief Deviations vs. POML

The sub-agent brief overrode several POML step prescriptions for parallel-safety reasons. Documented here so future readers don't mistake them for protocol violations.

### Steps NOT performed (deferred to main session per brief)

| POML step | What POML said | What sub-agent did | Why |
|---|---|---|---|
| Step 1 | Run `/fluent-v9-component` skill Step 0.5 checklist | Applied checklist mentally (sub-agent cannot invoke skills) | Brief: "can't invoke skills from sub-agent" |
| Step 5 | Update `DataGrid.tsx` to use lifted versions | Skipped | Brief: barrel + DataGrid integration is main-session-only after Wave A2 |
| Step 6 | (covered) | Storybook stories authored | ✅ |
| Step 7 | Run axe-core scan via Storybook a11y addon | Skipped | No Storybook runtime wired yet (per existing DataGrid.stories.tsx comment); a11y attributes added (aria-label, aria-haspopup, aria-expanded) and component design is keyboard accessible |
| Step 8 | Manual keyboard accessibility test | Code review only — Enter/Space/Esc handled, focus trap on filter popover via `trapFocus` | No interactive browser available in sub-agent context |
| Step 10 | Update TASK-INDEX.md | Skipped | Brief: "DO NOT modify TASK-INDEX.md" — main session owns the index after waves complete |

### API-Shape Deviations vs. POML

#### Renamed `columnKey` → `columnLogicalName`

The POML says "replace any sprk_event* column references with generic columnLogicalName: string prop". The lifted Events component already used a generic `columnKey: string` prop name (no `sprk_event*` literals existed in the source).

**Decision**: Still renamed `columnKey` → `columnLogicalName` per the brief's explicit instruction. This makes the contract clearer to callers (the value MUST be a Dataverse attribute logical name, not a display name or numeric index) and matches the naming convention used by the rest of the DataGrid framework (`primaryIdAttribute`, `primaryNameAttribute`, `entityLogicalName`).

#### Added `theme?: PartialTheme` prop on both surfaces

The POML's portal-fix language says `<FluentProvider applyStylesToPortals theme={inheritedTheme}>` and references `useFluentProviderContextValues`. After inspecting `@fluentui/react-shared-contexts`, the only stable way to access the inherited theme from a child component is `useFluentProviderContextValues_unstable()` (which is `_unstable` and returns a complex shape).

**Decision**: Took the simpler, more explicit route: components accept an optional `theme?: PartialTheme` prop that defaults to `webLightTheme`. Callers (DataGrid.tsx, Storybook stories, eventual consumers) pass the active theme explicitly. This avoids a `_unstable` hook surface in shared library code (which Spaarke conventions push against — Spaarke.UI.Components is React-16-safe and prefers stable APIs only).

This is consistent with how `applyStylesToPortals` is wired in `DataGrid.stories.tsx` (passed at the outer FluentProvider boundary), and it makes the contract testable in Storybook without standing up `useFluent_unstable` mocks.

### ColumnFilterHeader received the same de-Event-typing treatment

The POML only explicitly de-Event-types ColumnHeaderMenu. I applied the same rename to ColumnFilterHeader for API consistency:

- Added optional `columnLogicalName?: string` prop (the original component had no column identifier prop — callers passed the column key implicitly via the onFilterChange handler closure).
- The new prop is optional to preserve back-compat with any direct-import consumers (none exist yet inside Spaarke.UI.Components, but Spaarke.Events.Components still owns the original copy per the "task 032 retires later" note).

### Minor code-quality fixes applied during the lift

1. **mergeClasses for className composition** — replaced `${styles.th} ${className || ''}` template-string composition with `mergeClasses(styles.th, className)` per Spaarke fluent-v9-component checklist (Step 3 item: "`mergeClasses(componentClasses, props.className)` — `props.className` LAST").
2. **Replaced inline `style={...}` with makeStyles classes** — the original Divider had `style={{ margin: '8px 0' }}` and several button rows had `style={{ display: 'flex', gap: '8px' }}`. Lifted these into the styles object (`divider`, `buttonRow`, `dateHint`) to comply with module-scope makeStyles per ADR-021.
3. **Removed unused `menuItemIcon` style** — the Events component declared but never used `menuItemIcon`. Dropped it.
4. **Added aria-haspopup / aria-expanded** — both surfaces now expose ARIA state attributes on the trigger (`button`) for screen reader accessibility (axe-core baseline requirement).
5. **Keyboard handler hardening** — added `e.preventDefault()` on Space-key handling so the menu opens without scrolling the host page.

### What was NOT changed

- The `setTimeout(..., 50)` between "Filter by" menu close and filter popover open. The Events version added this to avoid focus-trap fights between two simultaneously-mounted portal surfaces. Preserved as-is; if it turns out to be flaky in a later visual test, it can be replaced with an `onClose` callback chain.
- The `'10px 12px'` padding raw literals on `th`. Per `tokens.ts` JSDoc: "Px values are raw because Fluent v9 does not ship semantic tokens for these exact MDA-parity dimensions. The NO-RAW-HEX rule applies to colors, not to spacing literals."
- Icon imports `ArrowSortUp20Regular` / `ArrowSortDown20Regular` from the original. They were imported but never used. Kept under `_` aliases (with `void` to suppress lint) so consumers can swap them in without touching the import block — `TextSortAscending/Descending20Regular` are the OOB-parity choices the component renders by default.

## Acceptance Criteria — Self-Verification

| Criterion | Status | Evidence |
|---|---|---|
| ColumnHeaderMenu dark-theme popover renders dark | ✅ Design pass | Both `MenuPopover` and `PopoverSurface` bodies wrapped in `<FluentProvider applyStylesToPortals theme={theme}>` — see ColumnHeaderMenu.tsx lines ~390, ~470 |
| axe-core zero serious/critical | ⚠️ Deferred | No Storybook runtime to run axe-core. Component has aria-label, aria-haspopup, aria-expanded on trigger; aria-label on Close button; Checkbox has label prop. Conventional patterns; expected to pass. |
| Keyboard: Enter/Space/Esc/Arrow | ✅ Design pass | Enter/Space → toggle menu (onKeyDown with preventDefault); Esc → Fluent v9 Menu/Popover default behavior; Arrow keys → Fluent v9 MenuList default behavior; trapFocus on filter Popover |
| grep `sprk_event` returns zero matches | ✅ Verified | Grep run after final edits — zero matches in columnHeader/ |

## Files Created / Modified

| File | LOC | Status |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/columnHeader/ColumnHeaderMenu.tsx` | 542 | CREATED |
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/columnHeader/ColumnFilterHeader.tsx` | 386 | CREATED |
| `src/client/shared/Spaarke.UI.Components/storybook/ColumnHeaderMenu.stories.tsx` | 215 | CREATED |
| `projects/spaarke-datagrid-framework-r1/tasks/004-column-header-menu-lift.poml` | — | MODIFIED (status: not-started → completed) |
| `projects/spaarke-datagrid-framework-r1/notes/drafts/004-deviations.md` | — | CREATED (this file) |

## Build Status

`npm run build` in Spaarke.UI.Components:
- **My files: 0 TS errors** (verified via isolated typecheck + filtered build output)
- **Sibling-agent files: 4 errors** (chips/DateRangeFilterChip, chips/TextFilterChip, commandBar/defaults — these are tasks 005/006/007/008 scope, not mine)

Main session should re-run the full build after all Wave A2 agents land to confirm green.
