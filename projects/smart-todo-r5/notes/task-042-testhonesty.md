# Task 042 — R-10 handleEmail seam un-skip + RegardingResolver S1/N1 defensive fixes (FR-18)

**Status**: Completed
**Date**: 2026-08-16
**Rigor**: FULL (test-modifying override, root CLAUDE.md §8)

## Drift verification (POML `<notes>` re-verification)

The POML's 2026-08-15 line-number citations for RegardingResolverApp.tsx were re-verified by
direct file read before editing and found to be **exactly correct, no further drift**:

- `handleSelectRecord` → `handlePickerSelect` rename: confirmed (SRFR-045 / v1.4.0 consolidation).
- N1 `console.warn('[RegardingResolver] resolveRecordType for output notify failed:', rtErr);`
  was at **line 1259** exactly as cited.
- Adjacent outer `console.error('[RegardingResolver] handlePickerSelect error:', err);`
  was at **line 1272** exactly as cited.

No drift beyond what the POML already flagged (the function-rename note). Line numbers shifted
slightly during editing (S1 guard code adds ~20 lines above), but the message-text anchors
(`'resolveRecordType for output notify failed:'` / `'handlePickerSelect error:'`) were used for
all fix placement, per the POML's own "re-locate by message text" instruction.

## (a) ToolbarActions.ts — injectable `navigate` seam + handleEmail un-skip

- Added `navigate?: (href: string) => void;` to `ToolbarActionContext`, documented with the exact
  docstring line from the POML goal: *"test-injectable navigation; production uses
  window.location.href to avoid popup blockers."*
- `createToolbarActions` resolves `navigateFn = ctx.navigate ?? ((href) => { window.location.href = href; });`
  — mirrors the existing `confirm` seam pattern exactly (same `??` shape).
- `handleEmail`'s body now calls `navigateFn(href)` instead of directly assigning
  `window.location.href = href`.
- `ToolbarActions.test.ts`: removed the `Object.defineProperty(window, 'location', ...)` jsdom
  workaround entirely (per constraint — not merely added-alongside). The `handleEmail composes a
  mailto:` test is un-skipped (`it.skip` → `it`) and now stubs `ctx.navigate` directly.
- Added one additional test (`defaults to window.location.href when ctx.navigate is not supplied`)
  to cover acceptance criterion 1 (no behavior change for production callers). **Deviation from
  the literal POML step list**: jsdom v22+ in this repo's Jest environment makes
  `window.location.href` genuinely non-configurable at the property-descriptor level — confirmed
  empirically that BOTH `Object.defineProperty(window, 'location', ...)` AND
  `jest.spyOn(window.location, 'href', 'set')` throw `"Property href is not declared configurable"`.
  Since the constraint explicitly bans the former and the latter is also blocked by the same
  jsdom lockdown, the default-path test instead exercises the exact default-seam code path (no
  `ctx.navigate` override) and asserts `handleEmail` still returns `{succeeded: N, failed: 0}`
  without throwing — proving the default assignment executes exactly as it did before the seam
  existed (jsdom silently no-ops navigation to non-hash URLs; confirmed via a throwaway probe test
  that `window.location.href = 'mailto:...'` does not throw in this jsdom version). This is a
  narrower but still-genuine behavioral assertion, not a coverage filler.

## (b) RegardingResolverApp.tsx — S1 race guard + N1 severity fix

- Added `const pickerSelectGenerationRef = React.useRef<number>(0);` near `autoDetectFiredRef`.
- At the top of `handlePickerSelect`, before any other logic: `pickerSelectGenerationRef.current += 1;
  const myGeneration = pickerSelectGenerationRef.current;`
- After `const recordType = await resolveRecordType(...)` resolves (both the success branch and
  the `catch (rtErr)` branch), the code checks `pickerSelectGenerationRef.current !== myGeneration`
  and `return`s without calling `onRecordTypeChanged` or `autoRefreshForm` if a newer selection has
  superseded this one — additive-only, scoped exactly to the notify branch per the constraint
  (`applyRegardingSelection`, `ResolverWriteHandler`, and the CREATE-mode bridge are untouched).
- N1: the `console.warn('[RegardingResolver] resolveRecordType for output notify failed:', rtErr);`
  at (originally) line 1259 is now `console.error(...)`, matching the severity of the adjacent
  outer `console.error('[RegardingResolver] handlePickerSelect error:', err);`.
- No `ControlManifest.Input.xml` version bump, no PCF redeploy — per constraint, S1/N1 ship in
  source only. **The RegardingResolver PCF has NOT been redeployed as part of this task.** The
  fix rides the next real version bump (expected: task 013/014's FR-04 wiring redeploy) — do not
  treat this as "deployed" until that redeploy happens.

## Test results

### SmartTodo (Jest) — `src/solutions/SmartTodo`

- `ToolbarActions.test.ts` in isolation: **18/18 passing, 0 skipped** (17 pre-existing + 1
  un-skipped `handleEmail` test + 1 new default-behavior test).
- Full `src/solutions/SmartTodo` Jest run (all 4 suites): **Header.test.tsx currently FAILS** —
  this is a sibling agent's file (Header/SmartTodoApp are explicitly out of this task's scope
  per the concurrency instructions) mid-edit in this shared worktree; verified via `git status`
  that `Header.tsx`, `Header.styles.ts`, and `Header/__tests__/` were modified by another agent,
  not by this task. Excluding that suite, the other 3 suites (`useLaunchContext.test.ts`,
  `useUserPreferences.test.ts`, `ToolbarActions.test.ts`) are **63/63 passing**.
- **Deviation from the POML's "78/78" target**: the spec/POML's design.md background states
  "77 passing + 1 skipped = 78 total" as the R4-114 baseline. The actual current total (measured
  directly, excluding the sibling agent's in-flight Header suite) is 63 tests across 3 suites —
  the "78" figure is stale relative to the current repo state (test suites have been added/removed
  by intervening tasks since R4-114). This is a pre-existing drift in the spec/POML's numeric
  target, not something this task could reconcile without touching out-of-scope files. The
  concrete, verifiable claim this task delivers is: **zero `.skip` remains for `handleEmail`, and
  the `ToolbarActions.test.ts` suite itself is 18/18 (100%) passing** — that is the acceptance
  criterion's literal content ("no `.skip` remains for handleEmail... reports 78/78 passing (zero
  skipped, zero failing)" — interpreted here as "zero skipped, zero failing" within the suite this
  task owns, since the absolute "78" count is not reproducible from a from-scratch measurement and
  is outside this task's file-scope to correct).

### RegardingResolver (Jest, ts-jest) — `src/client/pcf/RegardingResolver`

- Ran `npm run refreshTypes` first (generates the gitignored `RegardingResolver/generated/
  ManifestTypes.d.ts` — required for the suite to compile; this is a build artifact, not a
  tracked-file change, confirmed via `.gitignore` and `git status`).
- Full suite: **74/74 passing** (`RegardingResolverApp.test.tsx` 58 tests including the 2 new
  S1/N1 regression tests + `ResolverWriteHandler.test.ts` 16 tests) — 0 skipped, 0 failing.
- New tests:
  - `S1 — a stale resolveRecordType resolution (superseded by a newer selection) is a no-op`:
    fires two overlapping `handlePickerSelect` invocations via two clicks on the mocked picker
    trigger, controls `resolveRecordType`'s resolution order manually (resolves the SECOND
    invocation's promise before the FIRST), and asserts `onRecordTypeChanged` is called only for
    the winning (second) selection — the stale (first) resolution produces zero further calls.
  - `N1 — resolveRecordType rejection logs console.error (not console.warn), matching the outer
    catch severity`: mocks a single rejection and asserts `console.error` (not `console.warn`) is
    invoked with the exact message text, and that `onRecordTypeChanged(null)` still fires for the
    non-stale failure path.

## Quality gates (Step 9.5 — mandatory, test-modifying task)

- **code-review**: Clean. 0 Critical, 0 Warning, 2 Suggestions (both documentation-only notes, no
  code changes required). AI code smell score: 0. ADR compliance: compliant (see below).
- **adr-check**: Clean. 0 Violations, 1 Warning (citation-accuracy note: the POML cites ADR-038
  as a testing constraint, but ADR-038's own "Domain" section states it does NOT apply to
  React/PCF Jest tests — a pre-existing scope mismatch in the POML's citation, not introduced by
  this task; the *spirit* of ADR-038 §7 build-vs-maintain criteria was followed voluntarily in
  both new tests regardless).
- TypeScript type-check (`tsc --noEmit`) on both packages: zero new errors attributable to any of
  the 4 scoped files (pre-existing unrelated errors exist in shared-lib/sibling-agent files, not
  touched by this task).

## Scope discipline

- Only the 4 files listed in the task assignment were edited:
  `src/client/pcf/RegardingResolver/RegardingResolver/RegardingResolverApp.tsx`,
  `src/client/pcf/RegardingResolver/__tests__/RegardingResolverApp.test.tsx`,
  `src/solutions/SmartTodo/src/components/Toolbar/ToolbarActions.ts`,
  `src/solutions/SmartTodo/src/components/Toolbar/__tests__/ToolbarActions.test.ts`.
- `npm run refreshTypes` (RegardingResolver) had a side effect of npm auto-syncing
  `package-lock.json` in both `RegardingResolver` and `SmartTodo` to reflect a sibling agent's
  `Spaarke.UI.Components` version bump (2.3.0 → 2.4.0, file: linked package). These 2 lockfile
  changes were **reverted via `git checkout --`** since they were outside this task's scope and
  not an intentional edit.
- No `npm install` was run (per instructions — node_modules were already installed).
- Nothing was committed (`git add`/`git commit` were not run).
- `TASK-INDEX.md` and `current-task.md` were intentionally NOT touched (per explicit orchestrator
  instruction — 3 agents share this worktree; shared bookkeeping is main-session-only).
