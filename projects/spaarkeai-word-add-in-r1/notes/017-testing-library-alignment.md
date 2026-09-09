# Task 017 — Align `@testing-library/react` to v16 (React 19) and re-measure the suite

> **Task**: 017 (Phase 0) · **Measured**: 2026-09-09 · **Branch**: `work/spaarkeai-word-add-in-r1`
> **Scope**: bump `@testing-library/react` in `src/client/office-addins` only, re-run the suite, and
> categorize every remaining failure. No test body rewritten, no production source touched.

## Headline verdict

**The alignment is complete and correct, but it changed nothing measurable in the suite.** The RTL bump
required a second dependency addition not named in the POML (`@testing-library/dom`, discovered — see
below) to even install cleanly. Once both changes were in, the suite came back **suite-for-suite identical**
to task 009's baseline: the same 12 of 21 suites fail, the same 9 pass, and total failing-test count is
statistically unchanged (92 vs. 009's 92–94, well inside its own documented run-to-run flakiness). Most
importantly, `useAnnounce.test.ts` — the suite 009 named as the "signature failure" and the whole reason
this task exists — **still fails 16/16 with the identical `NotFoundError`**. Investigation (below) found its
root cause is not an RTL-version peer mismatch at all: it is a genuine product-code pattern (imperative
`document.body.appendChild`/`removeChild` outside React's tracked tree) that happens to throw the same
error under React 19 regardless of RTL major version. This is reported as a finding, not fixed — production
source is out of this task's scope.

## Before / after — verbatim

### `npm test` suite/test counts

| State | Suites | Tests |
|---|---|---|
| **BEFORE** (re-verified 2026-09-09, before touching `package.json`) | **12 failed, 9 passed, 21 total** | **93 failed, 236 passed, 329 total** |
| **AFTER RTL 16.1.0 alone** (interim — before `@testing-library/dom` was added) | 17 failed, 4 passed, 21 total | 11 failed, 72 passed, **83 total** |
| **AFTER RTL 16.1.0 + `@testing-library/dom` 10.4.1** (final state) | **12 failed, 9 passed, 21 total** | **92 failed, 237 passed, 329 total** |

The BEFORE run reproduces 009's final measurement (12/21 suites, 92-94/329 tests) inside its own stated
range — confirmed by a fresh `npx jest --ci --maxWorkers=2` run before any change.

**The interim collapse to 83 total tests is not a regression in test content** — it is 17 of 21 suites
failing to *load* at all (`Cannot find module '@testing-library/dom' from
'node_modules/@testing-library/react/dist/pure.js'`), which zeroes out every suite's collectible test count
the same way 009 saw suites collapse to 0 tests when `@testing-library/user-event` was missing. Once
`@testing-library/dom` was added, all 21 suites loaded again and the total returned to 329.

### What each change accounted for (isolated)

| Change | Effect |
|---|---|
| `@testing-library/react` `^14.2.1` → `^16.1.0` alone | Broke 17 of 21 suites at load time: `Cannot find module '@testing-library/dom'`. RTL 14 bundled `@testing-library/dom` as a regular `dependency`; RTL 16 demotes it to a `peerDependency` only (verified via `node_modules/@testing-library/react/package.json`). `npm install --legacy-peer-deps` does not auto-satisfy peer deps, so nothing pulled it in. |
| `@testing-library/dom` `^10.4.1` added as an explicit devDependency | Restored all 21 suites to loading. This is not an invented version — it is the **exact same pin both sibling packages already carry** (`PlaybookBuilder/package.json` and `SemanticSearch/package.json` both declare `"@testing-library/dom": "^10.4.1"`), confirming this is the alignment the task intended, not scope creep. |
| Net effect on suite/test counts | **Zero measurable change** vs. the pre-bump baseline: same 12 failing suites, same 9 passing, test count within the documented flakiness band (92 vs 92-94). |

**Root-cause finding on `@testing-library/dom`**: not a "mechanical RTL-16 API migration" in the sense of a
renamed import or helper — it is a **peer-dependency now requires an explicit devDependency entry**, the
same class of gap 009 found twice already (jest-dom, user-event) for this package. Adding it is squarely
inside "align to the sibling precedent" (the POML's own instruction to check what `PlaybookBuilder` and
`SemanticSearch` pin) and does not touch React, Fluent UI, or any test assertion — so it did not require
escalation under either trigger 1 or trigger 2.

## Categorized breakdown of all 12 remaining failing suites (final state)

Every one of the 12 failing suites was investigated individually against the post-bump run's full log. None
of the 12 differ from what 009 already found for the suites it named, and the 4 it did not name by title
(`SaveView`, `SaveFlow`, `ShareView`, `EntityPicker`) show the identical failure signatures in this run as
they did in 009's — i.e. **the RTL bump changed which module resolves, not which assertions pass.**

| Category | Count | Suites | Evidence |
|---|---|---|---|
| **(a) Harness/config** | 0 | — | None found. The two harness gaps this task uncovered (peer-dep `@testing-library/dom`) were fixed as part of the bump itself, not left outstanding. |
| **(b) Mechanical RTL-16 migration still owed** | 0 | — | Every `@testing-library/react` import in this package (`render`, `screen`, `fireEvent`, `waitFor`, `act`, `within`, `renderHook` — verified via `grep` across all 14 test files that import from `@testing-library/react`) is a stable API present unchanged in v16. No import path or helper rename was needed anywhere. |
| **(c) Genuine product/test defect** | 12 suites / 92 tests | see below | Pre-existing, unrelated to RTL version — confirmed present identically before and after this task's change. |

### (c) breakdown — the 12 failing suites, by defect shape

| Defect shape | Suites (tests) | Detail |
|---|---|---|
| **Imperative DOM manipulation outside React's tracked tree** | `useAnnounce.test.ts` (16/16, 100% of suite) | `useAnnounce.ts` calls `document.body.appendChild`/`removeChild` directly in a `useEffect` to create/destroy ARIA live regions, bypassing React's own commit tree. Under React 19, this collides with the reconciler's own DOM-removal bookkeeping during unmount, throwing `NotFoundError: The node to be removed is not a child of this node.` — **identical error, identical count, before and after the RTL bump** (confirmed by diffing this task's post-bump log against a direct re-check of `useAnnounce`'s failures). This is a **production-code pattern issue**, not a testing-library version issue; 009's framing of it as "React 19 / RTL14 peer mismatch" does not hold once RTL is on 16 — the true cause is the hook's own DOM ownership, which a version bump cannot fix. Out of this task's scope to touch (production source). |
| **Missing/incomplete Office.js mock coverage** | `OutlookAdapter.test.ts` (1, suite fails to load — `Cannot read properties of undefined (reading 'Importance')`, `Office.MailboxEnums` unmocked), `WordAdapter.test.ts` (10/10 — mock's `getSelectedDataAsync`/body accessor returns `undefined`, so every assertion on `body.content` throws `TypeError`) | Points at `shared/__mocks__/office-js.ts` gaps, not jest configuration. |
| **Accessible-name / role query mismatches** | `TaskPaneShell.test.tsx` (5/5), `TaskPaneNavigation.test.tsx` (1 of 2 — `aria-disabled` never set), `SaveView.test.tsx` (21/22), `SaveFlow.test.tsx` (9/9), `EntityPicker.test.tsx` (4/4 investigated), `useSaveFlow.test.ts` (7-8) | E.g. `getByRole('button', {name: /save to spaarke/i})` finds nothing because the rendered markup exposes an `aria-label="Save to Spaarke"` `<div role="form">`, not an accessible button with that name — a genuine test-vs-implementation mismatch (either the test's query or the component's markup is wrong; not investigated further to decide which, per the "report, don't fix" constraint). |
| **Assertion type mismatch** | `ApiClient.test.ts` (1/1) | Test expects a caught error to be `instanceof ApiClientError`; the code throws a bare `TypeError` instead. Not a `@testing-library` test at all (pure service unit test) — proves this class of failure is fully independent of the RTL bump. |
| **Async/timeout defect** | `ShareView.test.tsx` (16/16 — most exceed jest's 10000ms default timeout) | Tests `await` a `renderWithProvider` + interaction that never resolves within the window; likely an unresolved promise chain in the component or an incomplete mock for its search/generate-link flow. Not investigated further (test-body re-authoring territory, out of scope). |
| **Timing-sensitive flakiness** | `useEntitySearch.test.ts` (1-2) | 009 already documented ~2-test run-to-run flakiness in this general area (debounce/timer-dependent assertions); this run's failing test name (`searchNow › triggers search immediately bypassing debounce`) is consistent with that class, not a new defect. |

**Total accounted for**: 12 suites, matching the jest summary's "12 failed" exactly. Per-suite failing-test
counts above sum to slightly more than the jest-reported 92 total-failed-tests figure (a few duplicate
`describe`-block names across suites were double-counted while extracting names via `grep`) — the jest
top-line summary (`Tests: 92 failed, 237 passed, 329 total`) is the authoritative number; the per-suite
table is a categorization aid, not a second source of truth.

## Typecheck re-measurement

| Bucket | Before this task (009's final measurement) | After this task |
|---|---|---|
| **Production** (`src/client/office-addins`, excl. `__tests__/`, `*.test.ts(x)`, `shared/__mocks__/`) | 0 | **0 — unchanged** |
| **Test-file + mocks bucket** (`__tests__/`, `*.test.ts(x)`, `shared/__mocks__/office-js.ts`) | 289 | **289 — unchanged** |

Production stays at zero — the RTL bump did not touch or regress tasks 006/007/008's typecheck-clearance
work (`git diff --stat` confirms exactly 2 files changed by this task: `package.json`, `package-lock.json`;
no `.ts`/`.tsx` source file was edited). The 289 test-file figure is **re-measured, not re-cleared** — per
the task's constraint, this task does not attempt to reduce it; reporting the number is the whole
obligation. It did not move even though `@testing-library/react`'s bundled type declarations changed
major versions, which is a mildly interesting fact on its own (no test file's usage of RTL's types was
affected enough to gain or lose a diagnostic) but not something this task investigated further.

## Constraint verification

- ✅ **React stays `^19.0.0`.** `node_modules/react/package.json` and `node_modules/react-dom/package.json`
  both report `19.2.6` after the bump — unchanged from before. `package.json`'s `react`/`react-dom` entries
  were not edited.
- ✅ **No PCF control and no `external-spa` file touched.** `git status --porcelain` shows exactly
  `src/client/office-addins/package.json` and `src/client/office-addins/package-lock.json` modified — no
  file outside `src/client/office-addins` changed.
- ✅ **`@testing-library/react` is `^16.1.0`** — matches the major used by both `PlaybookBuilder`
  (`^16.0.0`) and `SemanticSearch` (`^16.1.0`); this task pinned to `^16.1.0` (the higher of the two
  minors already in the repo) rather than inventing a new value.
- ✅ **No production source modified.** `git diff --stat` isolates this task's changes to `package.json`
  (+3/-2, the two devDependency lines) and `package-lock.json` (lockfile regeneration).
- ✅ **No test deleted or newly skipped (ADR-038).** Zero files under `__tests__/` or matching `*.test.*`
  were touched at any point in this task.
- ✅ **No re-authored assertions.** No test file's content was changed; the mechanical-migration check
  (grepping every `@testing-library/react` import across all 14 consuming test files) confirmed every
  imported API (`render`, `screen`, `fireEvent`, `waitFor`, `act`, `within`, `renderHook`) is unchanged in
  v16, so no import-path or helper-rename migration was even needed.
- ✅ **Production typecheck still 0.** Re-measured via `npm run typecheck`, filtered to exclude
  `__tests__/`, `*.test.ts(x)`, and `shared/__mocks__/`.
- ✅ **Test-file typecheck bucket re-measured, not touched.** 289, identical to 009's final figure.

## Escalation trigger check

- **Trigger 1** (RTL 16 needs a React or Fluent UI major bump to install/run) — **did not fire.** RTL
  16.3.3's own `peerDependencies` declare `react`/`react-dom`: `^18.0.0 || ^19.0.0`; the install with
  `--legacy-peer-deps` completed cleanly with React still at `^19.0.0`, and Fluent UI was never touched.
- **Trigger 2** (bump needs re-authored assertions, not a mechanical migration) — **did not fire.** The
  only change needed beyond the version bump itself was adding a devDependency
  (`@testing-library/dom`) that both sibling packages already carry — not a test-body change.
- **Trigger 3** (suite comes back WORSE than 009's baseline) — **did not fire in the final state**, but
  **did fire transiently** in the interim state (RTL 16 alone, before `@testing-library/dom` was added):
  17 of 21 suites failed to load, worse than the 12-suite baseline. This was diagnosed and resolved within
  the same task (the missing peer-turned-explicit dependency), landing back at parity with the baseline —
  reported here for honesty rather than silently smoothed over. The **final** state is suite-for-suite
  identical to baseline, not worse, so the task did not stop and escalate.

## Recommendation

The alignment itself is done and correct — `office-addins` now matches the repo's React-19 RTL convention
with no outstanding peer-dependency gap. But **the 12 failing suites / 92 failing tests remain fully
unowned** and this task's evidence base makes the shape of the remaining work clearer than 009's did:

1. **`useAnnounce.ts`'s live-region pattern is a production-code fix, not a test fix.** Moving the
   ARIA live-region container(s) into a React-rendered `createPortal` target (or otherwise letting React
   own their lifecycle) would very likely resolve all 16 of that suite's failures. This is squarely a
   production-source task, explicitly out of bounds for a testing-library alignment task.
2. **The accessible-name / role-query mismatches** (`SaveView`, `SaveFlow`, `TaskPaneShell`,
   `TaskPaneNavigation`, most of `useSaveFlow`) are the single largest remaining bucket (~50 of the 92
   failing tests) and need a component-by-component decision: is the test's query wrong, or is the
   component's accessible markup wrong? That investigation was explicitly out of scope here (ADR-038 /
   this task's "report, don't fix" constraint) and is sized for its own task.
3. **`ShareView`'s timeout cluster** (16 tests, ~10s each) is worth investigating on its own — a hung
   promise or an incomplete mock is adding ~160s to every full suite run regardless of pass/fail outcome.
