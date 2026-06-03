# Task 073 (B.1): DatasetGrid Rules-of-Hooks Fix — Implementation Memo

> **Filed**: 2026-05-27
> **Task**: `projects/spaarke-ai-platform-unification-r4/tasks/073-b1-datasetgrid-rules-of-hooks.poml`
> **Status**: ✅ Complete
> **Rigor**: FULL
> **Parallel group**: K

---

## What this fixed

Ten REAL pre-existing `react-hooks/rules-of-hooks` violations in:

- `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx`

Surfaced by task 065's ESLint flat config, demoted from `error` → `warn` in task B-9 to keep the lint gate green (recorded in [`notes/b9-lint-warnings.md`](b9-lint-warnings.md) §B-11). These were **genuine correctness bugs**: hooks (`useMemo`, `useCallback`) called AFTER an early conditional `return` violate React's invariant that hooks must run in the same order on every render. When the conditional fires (virtualized large-record path, or empty state), the second-render hook list diverges from the first → React error or incorrect state preservation.

---

## Before / after hook positions

### GridView.tsx

| Hook | Before (line) | After (line) | Position vs conditional return |
|---|---|---|---|
| `useStyles()` | 75 | 75 | ABOVE (unchanged) |
| `useRef` | 76 | 76 | ABOVE (unchanged) |
| `useVirtualization` | 79 | 79 | ABOVE (unchanged) |
| `readableColumns` (`useMemo`) | 84 | 84 | ABOVE (unchanged) |
| **Virtualized early-return** | **90** | **~93 (moved DOWN)** | — |
| `isInfiniteScroll` (`useMemo`) | 107 (AFTER return ❌) | ~95 (now ABOVE return ✅) | hoisted |
| `handleScroll` (`useCallback`) | 115 (AFTER return ❌) | ~103 (now ABOVE return ✅) | hoisted |
| `gridColumns` (`useMemo`) | 136 (AFTER return ❌) | ~124 (now ABOVE return ✅) | hoisted |
| `handleSelectionChange` (`useCallback`) | 155 (AFTER return ❌) | ~143 (now ABOVE return ✅) | hoisted |
| `handleRowClick` (`useCallback`) | 164 (AFTER return ❌) | ~152 (now ABOVE return ✅) | hoisted |

Net change: 5 hooks hoisted ABOVE the conditional returns. Conditional returns (virtualized + empty-state) moved BELOW all hooks.

### ListView.tsx

| Hook | Before (line) | After (line) | Position vs conditional return |
|---|---|---|---|
| `useStyles()` | 115 | 115 | ABOVE (unchanged) |
| `useRef` | 116 | 116 | ABOVE (unchanged) |
| `useVirtualization` | 119 | 119 | ABOVE (unchanged) |
| `readableColumns` (`useMemo`) | 124 | 124 | ABOVE (unchanged) |
| **Virtualized early-return** | **129** | **~ moved DOWN** | — |
| `isInfiniteScroll` (`useMemo`) | 145 (AFTER return ❌) | hoisted ✅ | hoisted |
| `handleScroll` (`useCallback`) | 151 (AFTER return ❌) | hoisted ✅ | hoisted |
| `handleItemSelect` (`useCallback`) | 167 (AFTER return ❌) | hoisted ✅ | hoisted |
| `handleItemClick` (`useCallback`) | 178 (AFTER return ❌) | hoisted ✅ | hoisted |
| `displayColumns` (`useMemo`) | 188 (AFTER return ❌) | hoisted ✅ | hoisted |

Net change: 5 hooks hoisted ABOVE the conditional returns. Conditional returns (virtualized + empty-state) moved BELOW all hooks.

---

## Virtualization-branch handling

The hoisted hooks (`isInfiniteScroll`, `handleScroll`, `gridColumns`, `handleSelectionChange`, `handleRowClick`, `handleItemSelect`, `handleItemClick`, `displayColumns`) are only **consumed** when the standard (non-virtualized) branch renders. When the virtualized branch renders, these values are computed but discarded — that is the intended React 19 pattern (hooks always run; the branch decides what to render).

No hook depends on data that is only available in the virtualized branch. The virtualized branch consumes:

- `props.records` (always available)
- `readableColumns` (computed BEFORE the conditional return — unchanged)
- `virtualization.itemHeight` / `virtualization.overscanCount` (computed BEFORE the conditional return — unchanged)
- `props.selectedRecordIds`, `props.onRecordClick` (props — always available)

So **no hook body required conditional logic**. The fix is a pure structural hoist; no behavior diverges between the pre- and post-refactor virtualized path, and no hook body was modified.

---

## Lint promotion confirmation

`eslint.config.js` changed:

```diff
-      "react-hooks/rules-of-hooks": "warn",
+      "react-hooks/rules-of-hooks": "error",
```

Header docstring updated to reflect the promotion. `b9-lint-warnings.md` reference retained for historical context but the rule itself is no longer demoted.

---

## Verification

### Lint

```
npm run lint
✖ 151 problems (0 errors, 151 warnings)
```

- 0 errors (rule promoted to `error` and passes)
- 0 `react-hooks/rules-of-hooks` violations (confirmed via `grep -c "rules-of-hooks"`)
- 151 remaining warnings are unrelated (`no-explicit-any`, `no-unused-vars`, `prefer-const`, `prefer-as-const`) — all pre-existing, enumerated in `b9-lint-warnings.md`

### Tests

```
npx jest src/components/DatasetGrid --no-coverage
Test Suites: 2 passed, 2 total
Tests:       26 passed, 26 total
```

Both `GridView.test.tsx` (12 tests, includes virtualization branch) and `ViewSelector.test.tsx` (14 tests) pass. The virtualization tests directly exercise the early-return branch with a 1500-record dataset and confirm the post-refactor render still produces the expected output.

---

## Behavior parity

✅ **Zero behavior change concerns.** The refactor is a pure structural hoist — hooks compute the same values, the branches consume the same values. The virtualized large-record path (`virtualization.shouldVirtualize && props.records.length > 1000` in GridView; `virtualization.shouldVirtualize` in ListView) still renders `VirtualizedGridView` / `VirtualizedListView` with identical props.

---

## Out of scope

- `react-hooks/exhaustive-deps` warnings in the same files — separate concern, lower priority per `b9-lint-warnings.md`
- Other DatasetGrid files (`ViewSelector.tsx`, etc.) — task 074's domain
- `no-explicit-any` warnings in DatasetGrid hooks — accumulated cleanup, not in this task's scope

---

## References

- `.claude/adr/ADR-022-react-19-code-pages.md` (React 19 patterns)
- React docs — Rules of Hooks: https://react.dev/reference/rules/rules-of-hooks
- `projects/spaarke-ai-platform-unification-r4/notes/b9-lint-warnings.md` §B-11 (origin of the carry-over)
- Task POML: `projects/spaarke-ai-platform-unification-r4/tasks/073-b1-datasetgrid-rules-of-hooks.poml`
