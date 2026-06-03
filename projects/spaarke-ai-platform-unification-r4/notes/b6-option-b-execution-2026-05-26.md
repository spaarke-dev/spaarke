# B-6 Option B execution — CalendarFilterPane promoted to shared lib

**Task**: 055 (B-6) — Reconcile CalendarSidePane.CalendarSection
**Date**: 2026-05-26
**Author**: Claude (task-execute, FULL rigor, second sub-agent — re-scope to Option B)
**Status**: Code + build verify complete. **Deploy NOT performed** (per re-scope guardrail).

---

## What shipped

Per operator decision on 2026-05-26, the divergence documented in
`b6-pre-change-diff.md` was resolved via Option B from that memo's
recommendation:

1. **NEW shared component** `CalendarFilterPane` created in
   `@spaarke/events-components` at
   `src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/CalendarFilterPane.tsx`
   (~720 LOC including comments; the prior local
   `CalendarSection.tsx` was 925 LOC; the small reduction is comment
   consolidation + dropping the now-unused local export-default).
2. **Existing shared `CalendarSection`** in the same package
   (workspace widget — Pattern D canonical, R3 tasks 114–129) was
   **NOT modified**. The two components coexist intentionally — they
   serve different user intents (filter builder vs click-day widget).
3. **CalendarSidePane consumer rewired** to import `CalendarFilterPane`
   from `@spaarke/events-components`. The local
   `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx`
   was deleted; the entire `src/components/` folder was removed (it
   had no other files).
4. **CalendarSidePane `package.json`** gained
   `"@spaarke/events-components": "file:../../client/shared/Spaarke.Events.Components"`
   as a dependency.
5. **CalendarSidePane `vite.config.ts`** was upgraded to the
   canonical source-aliased-shared-lib pattern from
   `EventsPage/vite.config.ts`: adds the `resolveSharedLibDeps()`
   plugin, aliases `@spaarke/events-components` to its source folder,
   and dedupes Fluent + React + scheduler. Without this, Rollup walks
   into the shared-lib source files and fails to resolve
   `@fluentui/react-components` (the shared lib has no own
   `node_modules` for transitive peer deps).
6. **UTC bug fix applied during promotion**:

   **Before** (prior local CalendarSection, buggy in non-UTC timezones):
   ```ts
   function toIsoDateString(date: Date): string {
     return date.toISOString().split("T")[0];
   }
   ```

   **After** (new CalendarFilterPane — mirrors R3 task 120 fix):
   ```ts
   export function toIsoDateString(date: Date): string {
     const y = date.getFullYear();
     const m = String(date.getMonth() + 1).padStart(2, "0");
     const d = String(date.getDate()).padStart(2, "0");
     return `${y}-${m}-${d}`;
   }
   ```

   Smoke-tested against 5 timezone-sensitive cases (see
   `CalendarFilterPane.test.ts`). Runtime evidence in the execution log:
   the buggy helper gives `2026-02-04` for `new Date(2026, 1, 3, 23, 30)`
   in this machine's local timezone — the fix gives `2026-02-03`. The
   year-end case shifts `2025-12-31 23:59 local` → `2026-01-01` under
   the buggy helper (wrong year), correctly preserved at `2025-12-31`
   under the fix.

---

## File-by-file changes

| File | Change |
|---|---|
| `src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/CalendarFilterPane.tsx` | **NEW** — promoted local copy + UTC fix |
| `src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/index.ts` | **NEW** — folder barrel |
| `src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/CalendarFilterPane.test.ts` | **NEW** — unit assertions for UTC fix + filter-shape contract |
| `src/client/shared/Spaarke.Events.Components/src/components/index.ts` | **MODIFIED** — add `CalendarFilterPane` exports (alongside existing `CalendarSection`) |
| `src/solutions/CalendarSidePane/package.json` | **MODIFIED** — add `@spaarke/events-components` dependency |
| `src/solutions/CalendarSidePane/src/App.tsx` | **MODIFIED** — import `CalendarFilterPane` from `@spaarke/events-components` (was local `./components`) |
| `src/solutions/CalendarSidePane/src/components/CalendarSection.tsx` | **DELETED** |
| `src/solutions/CalendarSidePane/src/components/index.ts` | **DELETED** (folder also removed — no other files) |
| `src/solutions/CalendarSidePane/vite.config.ts` | **MODIFIED** — adopt EventsPage's `resolveSharedLibDeps` plugin + source alias for `@spaarke/events-components` |

---

## Naming choice — separate type aliases

The new shared `CalendarFilterPane` filter-output types are named
`CalendarFilterPaneSingle`, `CalendarFilterPaneRange`,
`CalendarFilterPaneClear`, and `CalendarFilterPaneOutput` — distinct
from the workspace-widget `CalendarSection`'s existing
`CalendarFilterSingle` / `CalendarFilterRange` / etc. Rationale:

1. The two output shapes differ on `dateFields` (REQUIRED for filter
   pane, OPTIONAL for workspace widget). Sharing a type name would
   force one or the other to soften — and the side-pane parent
   record-form JS reads `filter.dateFields` unconditionally, so
   softening to optional would silently break the postMessage
   contract.
2. The package now exports BOTH sets cleanly from `index.ts` with no
   collision; consumers pick the right one by import name.

The CalendarSidePane `parseParams.ts` still defines its own LOCAL
`CalendarFilterOutput` type (used by `getInitialFilterState` +
`sendFilterChanged`/`sendCalendarReady` postMessage helpers). The
shared `CalendarFilterPaneOutput` and the local `CalendarFilterOutput`
are structurally identical, so TypeScript's structural typing accepts
assignment between them (no glue code needed). Future cleanup could
collapse the local definition to a re-export from the shared lib —
that's outside this task's scope.

---

## Build verification (post-change)

### Shared lib (`@spaarke/events-components`)
```
> tsc --noEmit
(0 errors, 0 warnings)
```

### CalendarSidePane (`src/solutions/CalendarSidePane`)
```
vite v5.4.21 building for production...
✓ 3286 modules transformed.
dist/index.html  1,099.79 kB │ gzip: 306.44 kB
✓ built in 4.89s
```

---

## Bundle delta (CalendarSidePane gzip)

| Build | Raw | Gzip |
|---|---|---|
| **Pre-change baseline** (local CalendarSection) | 1250.17 KB | **355.62 KB** |
| **Post-change** (shared CalendarFilterPane via source-alias) | 1099.79 KB | **306.44 KB** |
| **Δ** | **−150.38 KB** | **−49.18 KB (−13.8%)** |

The reduction comes from the source-aliased import bundling only
the imported subtree (CalendarFilterPane + its direct deps) rather
than the whole local file. The shared lib is tree-shake-friendly
because each component lives in its own folder with a focused
barrel. Well under the NFR-08 budget (≤ +50 KB delta — we are
−49.18 KB instead).

---

## Tests added

`src/client/shared/Spaarke.Events.Components/src/components/CalendarFilterPane/CalendarFilterPane.test.ts`
(~190 LOC including comments). Three test groups:

1. **UTC bug fix** — 5 timezone-sensitive Date objects asserted to
   serialize via `toIsoDateString` to their LOCAL Y-M-D, not the UTC
   equivalent. Mid-evening, year-end, year-start, midnight, and
   early-morning cases.
2. **Filter output shape** — type-level assertion that
   `CalendarFilterPaneSingle.dateFields` and
   `CalendarFilterPaneRange.dateFields` are REQUIRED (omitting them
   would be a TS error). Runtime structural sanity check.
3. **Apply button gating** — documented invariant (single emit path
   via `handleApplyFilter`); full @testing-library render-test
   deferred to when this package has a configured test runner.

**Caveat**: `@spaarke/events-components` does not yet have a test
runner configured (no jest.config / vitest.config). The test file is
checked in for future runner setup. The UTC-fix assertions were
ALSO smoke-verified by transpiling the helper to plain JS and
running it under Node, with 5/5 cases passing. See
`CalendarFilterPane.test.ts` for the in-file documentation of how to
wire it up.

---

## Deviations from the prior `b6-pre-change-diff.md` memo's plan

The prior memo's Option B description said:

> Move local copy to `@spaarke/events-components/src/components/CalendarFilterPane/`
> (or similar) as a SECOND shared component. Delete local copy.
> CalendarSidePane imports the new shared one. Embedded Calendar
> still uses `CalendarSection`. ~6–8 h.

What shipped matches that exactly. **Additional changes** the prior
memo didn't anticipate:

1. **`vite.config.ts` upgrade** — necessary because the prior
   CalendarSidePane vite config didn't have the source-aliased-shared-
   lib plugin pattern. Without it, Rollup couldn't resolve bare
   imports from inside the shared lib's source files. This is a known
   pattern (`EventsPage`, `LegalWorkspace`) and the upgrade is
   structural, not behavioral.
2. **Type-name disambiguation** (`CalendarFilterPaneOutput` vs
   `CalendarFilterOutput`) — see "Naming choice" section. Necessary
   to keep the workspace-widget `CalendarSection` type signature
   intact (optional `dateFields`) without softening the filter-pane
   contract.
3. **UTC bug fix applied during promotion**, per the parent
   re-scope instructions. The fix is in-scope because it's an
   objective improvement with no contract impact (the bug only ever
   produced wrong dates — the contract is "this field is a YYYY-MM-DD
   string for the date the user clicked," which the fix satisfies
   correctly for the first time).
4. **No e2e UI test or deploy** — per re-scope guardrail
   ("code + build verify only"). The POML's Step 8–10 (deploy + UI
   tests + post-deploy smoke) are deferred to a follow-up.

Approx effort: ~5 hours (within the 6-8h budget).

---

## What was NOT done (per re-scope guardrails)

- **DID NOT deploy** the CalendarSidePane web resource.
- **DID NOT modify** the existing shared `CalendarSection`
  (workspace widget) — both components coexist intentionally.
- **DID NOT touch** `TASK-INDEX.md`, `current-task.md`, root
  `CLAUDE.md`, or any `.claude/` files.
- **DID NOT run** the POML's Step 9 (post-deploy visual smoke) or
  Step 10 (ui-test skill invocation) — both are deploy-dependent.
- **DID NOT add** a jest/vitest runner config to
  `@spaarke/events-components` — beyond the re-scope budget; the
  test file is checked in for future runner setup.

---

## R4 lessons-learned (for `lessons-learned.md` aggregation)

1. **Components labeled "same name, divergent code" are often genuinely
   different components.** R3 task 114 (the original hoist) extracted
   the EventsPage CalendarSection — leaving the side-pane
   CalendarSection in place even though they shared the name. The
   pre-change diff (first sub-agent) correctly escalated rather than
   forcing a misleading unification.

2. **"Single source of truth" means single source per intent.** ADR-012
   is satisfied here: the side-pane filter builder has ONE source
   (`CalendarFilterPane`), the workspace-widget calendar has ONE
   source (`CalendarSection`). Both live in the shared lib; the
   solutions folder has zero local copies of either.

3. **Source-aliased shared libs need the resolveSharedLibDeps plugin.**
   Any new solution that source-aliases a shared lib should copy the
   pattern from `EventsPage/vite.config.ts` (now also in
   `CalendarSidePane/vite.config.ts`). Consider hoisting the plugin
   into a shared util in a future R5 task.

4. **Operator decision unblocked a 6h task in <30 min.** The
   "Option A → escalate → Option B → execute" path worked exactly as
   CLAUDE.md §6 prescribes. The escalation memo (b6-pre-change-diff.md)
   was load-bearing.

---

## References

- Prior sub-agent's escalation memo: `notes/b6-pre-change-diff.md`
- Task POML: `tasks/055-b6-reconcile-calendar-sidepane.poml`
- ADR-012 (single source of truth): `.claude/adr/ADR-012-shared-components.md`
- R3 task 120 (UTC bug fix in workspace widget — pattern source):
  `projects/spaarke-ai-platform-unification-r3/tasks/120-*`
- Canonical source-aliased-shared-lib vite pattern:
  `src/solutions/EventsPage/vite.config.ts`
