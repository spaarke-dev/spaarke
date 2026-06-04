# Task 033a — Deviations + closure notes

> **Date**: 2026-06-03
> **Task**: 033a — DataGrid `hostFilters` framework extension (FR-MIG-05 scope expansion)
> **Status**: closed (commit pending in this same chain)
> **Parent**: task 033 (SpaarkeAi Calendar widget migration) — split into 033a (framework extension) + 033b (widget migration). See `current-task.md` for the split rationale + user direction.

---

## What shipped in 033a

### Framework surface (new permanent API)

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts` | Added `HostFilterOperator` type + `HostFilterCondition` interface + `overlayHostFilters(fetchXml, conditions)` function. Sibling to the existing `overlayParentContextFilter`. |
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` | Added `hostFilters?: ReadonlyArray<HostFilterCondition>` prop + `onRecordsLoaded?: (records) => void` callback to `DataGridProps`. Plumbed `hostFilters` into the `fetchXml` `useMemo` composition pipeline (third layer, after parent-context overlay). Added effect that fires `onRecordsLoaded` on records identity change. |
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/index.ts` | Re-exported `overlayHostFilters` + `HostFilterCondition` + `HostFilterOperator` from the barrel. |

### Documentation

| File | Change |
|---|---|
| `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` | §6 composition flow diagram updated to show host-filters as a third layer. New "Host filters (imperative, third composition layer)" subsection with prop reference table + the choice matrix (chips vs. parent-context vs. host-filters). Updated `fetchXmlOverlay.ts` module-table entry. |
| `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` | Added "Step 4b — If the host owns its own filter UI: `hostFilters` prop" section with worked Calendar-widget example + behavioral notes + cross-link to the choice matrix. |

---

## Composition order (final)

```
base (savedquery / inline)
  → overlayParentContextFilter   (declarative, configjson behavior.parentContextFilter)
  → overlayHostFilters           (imperative, <DataGrid hostFilters={…}/> prop)  ← NEW (033a)
  → augmentFetchXmlWithChips     (user-driven, from filter chip strip)
```

Each layer is a pure string transform. Composition is memoized on its inputs. Identical inputs → identical output (the lazy-load reset detector relies on this).

---

## API shape decision — Option 1 (per current-task.md "Open API design notes")

Per the resume protocol, the API decision was the recommended **Option 1 (minimal)** from the checkpoint:

```ts
interface HostFilterCondition {
  attribute: string;
  operator: HostFilterOperator; // curated subset of FetchXML operators
  value?: string | number | boolean | ReadonlyArray<string | number | boolean>;
}
```

Operator vocabulary (final): `eq`, `neq`, `in`, `not-in`, `gt`, `lt`, `ge`, `le`, `like`, `not-like`, `null`, `not-null`, `on`, `on-or-after`, `on-or-before`, `between`, `not-between`, `eq-userid`, `eq-userteams`.

Multi-value operators serialize as `<value>` child elements per FetchXML schema; single-value operators serialize as a `value=…` attribute. Valueless operators (`null`, `not-null`, `eq-userid`, `eq-userteams`) ignore `value`.

**Option 2 (richer — nested AND/OR groups, declarative configjson counterpart) was deferred to a follow-up project** — R1 ships the imperative-prop-only flat-list variant. The current shape is sufficient for the Calendar widget's filter row mapping (the immediate driver) and the common dashboard-widget host pattern.

---

## Deviations from POML steps

None — the POML's task 033 steps were for the widget migration (the 033b half). 033a is the scope expansion the user added at sign-off ("this will be standard feature of the dataset grid"). It is documented in:

- `projects/spaarke-datagrid-framework-r1/tasks/033-spaarkeai-calendar-widget-migrate.poml` (notes section updated 2026-06-03 with the 033a/033b split)
- `projects/spaarke-datagrid-framework-r1/current-task.md` (Quick Recovery + Q1-Q4 answers + Open API design notes)
- `projects/spaarke-datagrid-framework-r1/notes/drafts/033-widget-owner-signoff.md` (full sign-off + Q&A matrix)

---

## What was intentionally NOT done in 033a

- **No tests added** — the package has zero existing test infrastructure (no `*.test.ts` files anywhere in `Spaarke.UI.Components`). Adding a single test file would set a precedent inconsistent with the framework's current posture. Tests are deferred to the project wrap-up (task 090) where the framework's overall test strategy gets revisited.
- **No Storybook story added** — the existing DataGrid stories don't exercise `parentContextFilter` either; both overlays are transparent string transforms covered indirectly by the parent component's stories. Adding a dedicated `hostFilters` story would be inconsistent.
- **No declarative configjson counterpart for `hostFilters`** — deferred per the API-decision note above (R1 ships the imperative-prop-only variant).
- **No PR description Placement Justification** — `hostFilters` does NOT add code to `Sprk.Bff.Api`; the BFF binding governance rule (CLAUDE.md §10) does not apply.

---

## Build verification

- `npx tsc --noEmit` from `src/client/shared/Spaarke.UI.Components/` — **PASSES** (exit 0).
- No new lint issues introduced (changes follow existing module patterns).

---

## Next step: task 033b — widget migration

The Calendar widget's `applied` filter state will be converted to a `HostFilterCondition[]` array at the existing dispatch site (effect at `CalendarWorkspaceWidget.tsx` L697-743) and passed to `<DataGrid hostFilters={…} />`. The `handleRecordsLoaded` callback (L585-620) wires to `<DataGrid onRecordsLoaded={…} />`. Toolbar (L1102-1155) + bulk-status callbacks get DELETED. Then the deferred GridSection cleanup from task 032 D-032-01 closes.
