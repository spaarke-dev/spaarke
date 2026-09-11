# Task 009 — Jest harness repair + test-file typecheck re-measurement

> **Task**: 009 (Phase 0) · **Measured**: 2026-09-09 · **Branch**: `work/spaarkeai-word-add-in-r1`
> **Scope**: repair the `src/client/office-addins` jest harness only. Do NOT fix test-file type errors,
> do NOT rewrite test bodies. Measuring honestly is the deliverable.

## Headline verdict

**Task 001's hypothesis PARTIALLY held and was materially incomplete.** The predicted single setup
change ("register jest-dom") was not even the right diagnosis of the *dependency* problem — the package
was never installed at all, not just unregistered — and a **second, previously undocumented harness gap
of the identical class** (`@testing-library/user-event` also imported but never installed) had to be
found and fixed to unblock 4 whole suites that were failing to load. Even with both gaps closed, **12 of
21 suites are still red**, for reasons that have nothing to do with jest-dom: a React 19 /
`@testing-library/react@14` (peer-declared for React 18) incompatibility, plus a set of unrelated
product/mock defects. The test-file typecheck count barely moved (296 → 289, and only because of the
user-event fix — the jest-dom fix alone changed it by exactly zero).

## Before / after — verbatim

### `npm test` suite/test counts

| State | Suites | Tests |
|---|---|---|
| **BEFORE** (baseline, re-verified 2026-09-09 before any change) | **13 failed, 8 passed, 21 total** | **57 failed, 169 passed, 226 total** |
| **AFTER jest-dom only** (interim measurement, `user-event` not yet installed) | 12 failed, 9 passed, 21 total | 41 failed, 185 passed, **226 total** |
| **AFTER jest-dom + user-event** (final state, run twice) | 12 failed, 9 passed, 21 total (both runs) | Run A: 92 failed, 237 passed, 329 total<br>Run B: 94 failed, 235 passed, 329 total |

The BEFORE numbers reproduce task 001's baseline exactly (13/21 suites, 57/226 tests) — confirmed by
running `npx jest` fresh before touching anything.

The jump from 226 → 329 total tests between the interim and final states is not new tests being written
— it's 4 suites (`EntityPicker.test.tsx`, `SaveFlow.test.tsx`, `SaveView.test.tsx`,
`ShareView.test.tsx`) that previously failed to load at all (`Cannot find module
'@testing-library/user-event'`, a compile-time `Test suite failed to run`, contributing **0** individual
tests to the `Tests:` total). Once they load, all their internal `it()`/`test()` blocks get collected and
counted for the first time, adding +103 tests to the denominator — most of which now pass.

The 2-test difference between Run A and Run B (92 vs 94 failed) is flakiness in
`EntityPicker.test.tsx` (`keyboard navigation › opens dropdown on arrow down`,
`search behavior › updates query when typing`) — timing-sensitive `userEvent` interactions, not caused
by this task's changes and not investigated further (out of scope — see Findings).

### What each fix actually accounted for (isolated)

| Fix | Suites | Tests (of the original 226) | Typecheck (test-file bucket) |
|---|---|---|---|
| `@testing-library/jest-dom` (task 001's hypothesis) | 13 → 12 (**-1**) | 57 → 41 failed (**-16, 28%**) | 296 → 296 (**0 change**) |
| `@testing-library/user-event` (found during this task, not predicted) | 12 → 12 (no suite fully flips; the class of failure changes from "won't load" to "loads, partial pass") | +103 tests become collectible; roughly half now pass | 296 → 289 (**-7**, of which **exactly -4** is the 4 `TS2307 Cannot find module '@testing-library/user-event'` errors this fix removes — verified file-by-file) |

**jest-dom's real effect**: eliminated every `TypeError: expect(...).toBeInTheDocument is not a
function` error (verified: zero occurrences remain in any post-fix run). That error class was real and
is fully gone. But it only accounted for 16 of the 57 baseline-failing tests (28%) and 1 of the 13
failing suites — not "a large share" as predicted, and it had **zero** effect on the typecheck count
(jest runs tests via `ts-jest` with `isolatedModules: true` — transpile-only, no type checking — so a
runtime harness fix cannot touch `tsc` diagnostics; this mechanism was not considered in task 001's
prediction).

**Escalation trigger 1 assessment** ("the missing jest-dom registration turns out NOT to be the cause of
the 13 failing suites"): **fires in substance, though not in the literal binary sense.** jest-dom was *a*
real cause (confirmed, quantified above) but not *the* cause of most of the 13 — 12 of 13 originally
failing suites are still failing after the fix, for causes unrelated to jest-dom (below). Per the
trigger's intent, I stopped rather than continuing an open-ended repair campaign: I fixed the
user-event gap because it is the identical defect class (a testing utility imported by tests but never
declared as a dependency — a harness gap, not a test-body issue), but did not attempt to fix any of the
downstream causes described next.

## Root cause of the 12 still-failing suites (evidence, not fixed)

Categorized from the final run's full failure log (`grep` over stack traces, not guesswork):

| Cause | Suites / tests affected | Evidence |
|---|---|---|
| **React 19 / `@testing-library/react@14` peer mismatch** | `useAnnounce.test.ts` — all 16 tests (100% of the suite) | `node_modules/@testing-library/react/package.json` declares `"peerDependencies": {"react": "^18.0.0", "react-dom": "^18.0.0"}`; the package's own `react`/`react-dom` are `^19.0.0`. Every failure is `NotFoundError: The node to be removed is not a child of this node.` — the documented React 19 vs. testing-library-v14 DOM-cleanup incompatibility (v14's `cleanup()` predates React 19's root-unmount changes). |
| **Missing/incomplete Office.js mock coverage** | `OutlookAdapter.test.ts` (`Cannot read properties of undefined (reading 'Importance')` — `Office.MailboxEnums` not mocked), `WordAdapter.test.ts` (`body.content` undefined — adapter mock returns nothing) | Stack traces point at test fixtures / `shared/__mocks__/office-js.ts`, not at jest configuration. |
| **Assertion/behavior mismatches** | `ApiClient.test.ts` (expects `ApiClientError`, receives `TypeError`), `TaskPaneShell.test.tsx` / `TaskPaneNavigation.test.tsx` (accessible-name / text queries not found — `getByLabelText(/signed in as john doe/i)`, `getByText('Spaarke')` split across elements), `useEntitySearch.test.ts`, `useSaveFlow.test.ts` | These are genuine test-vs-implementation mismatches, not harness gaps — some may be pre-existing test bugs, some may be real product defects surfaced now that the suite actually runs. Not investigated further; reporting per the "report as a finding, don't edit away" constraint. |

**Escalation trigger 2 fires**: fully repairing the harness beyond this task's two dependency additions
would require bumping `@testing-library/react` past its React-18 peer declaration (e.g. to v16, which
supports React 19) — a package-graph decision explicitly called out as this task's second escalation
trigger. **I did not perform this bump.** Per the POML: *"escalate rather than resolving it
unilaterally."*

## Re-measured test-file typecheck count

| Measurement | Test-file bucket | Production bucket | Total |
|---|---|---|---|
| **Baseline** (task 001, 2026-09-04/05, pre-`6b987bb31`) | 296 | 99 | 395 |
| **TASK-INDEX re-measurement** (2026-09-09, post-`6b987bb31`, before 006/007/008 started) | 296 | 88 | 384 |
| **This task, jest-dom only** | 296 (**unchanged**) | 80 | 376 |
| **This task, jest-dom + user-event (final)** | **289** | 27 | 316 |

**The test-file number is 289, not 296 — a movement of only -7, all of it structurally explained:**
- **-4** is my own, confirmed contribution: the 4 `TS2307 Cannot find module '@testing-library/user-event'`
  errors in `EntityPicker.test.tsx`, `SaveFlow.test.tsx`, `SaveView.test.tsx`, `ShareView.test.tsx`
  resolve once the package is installed. Verified file-by-file (each dropped by exactly 1).
- **-3** is **not this task's work** — `OutlookAdapter.test.ts` (-1) and `shared/__mocks__/office-js.ts`
  (-2) changed between my two typecheck runs. This worktree is shared with concurrently-running tasks
  006/007/008, which are actively editing `shared/adapters/OutlookAdapter.ts` and (per the baseline
  note, `shared/__mocks__/office-js.ts` is conditionally owned by task 007) the office-js mock, live,
  during this task's execution window. **This is drift, not a jest-dom/user-event effect** — reported
  for honesty, not claimed as this task's result.

**Reading this against acceptance criterion 2**: the test-file typecheck debt is **effectively untouched
by the harness repair** — 289 of 384 (75.3%) vs. 296 of 384 (77.1%) is a rounding-level difference, not a
meaningful clearance. Task 001's prediction that the setup fix "likely clears... some TS2339 count" did
**not** materialize: 0 TS2339 diagnostics were removed by either dependency addition (the -4 were all
TS2307 module-resolution errors, a different code entirely).

**The production bucket collapsed 88 → 27 during this task's window — that is tasks 006/007/008's
concurrent work, not task 009's.** Flagged here only so the number isn't misread as this task's doing.

## Recommendation: does the residual need its own task?

**(a) Yes — the residual needs its own task(s), separate from 006/007/008's typecheck-clearance work
and separate from this task.** The number that justifies the call: **289 test-file typecheck errors**
(75%+ of the package's total diagnostic count) plus **12 of 21 test suites still failing at runtime**
after the harness itself is repaired — both are far too large to "fold into" any existing task, and both
are currently unowned:

1. **A React 19 / `@testing-library/react` major-version decision** — escalation trigger 2, above.
   `useAnnounce.test.ts` (16 tests, 100% of the suite) fails purely on this; other suites likely share
   the same root cause partially. This is a package-graph decision, not a task-009-sized fix.
2. **An `office-addins` test-suite repair task** — the assorted mock/assertion defects above
   (`OutlookAdapter`, `WordAdapter`, `ApiClient`, `TaskPaneShell`, `TaskPaneNavigation`,
   `useEntitySearch`, `useSaveFlow`) are genuine findings, not harness gaps, and need their own
   investigation per test file. Do not lump this in with #1 — the causes are unrelated.
3. **The 296(→289)-error test-file typecheck debt itself remains unowned.** Operator decision B1
   explicitly narrowed FR-18 to production types and left this bucket unassigned on purpose, on the
   condition that it not be buried. It is not buried (this note + task 009's existence discharge that),
   but it is also not scheduled. The operator should decide whether it becomes its own task or an
   explicit, documented deferral — silence is not an acceptable third option per this project's
   deferral policy.

**(b) is not viable** — 289 errors and 12 failing suites are not "small enough to fold into an existing
task." 006/007/008 are explicitly forbidden from touching test files, and none of the three has spare
scope of this size.

## Constraint verification

- ✅ **No production source file modified by this task.** `git diff --stat` against this task's own
  changes shows exactly 3 files: `src/client/office-addins/jest.setup.js` (+7),
  `src/client/office-addins/package.json` (+2), `src/client/office-addins/package-lock.json` (+104,
  `npm install` lockfile update). This worktree is shared with concurrently-running tasks 006/007/008,
  whose production-file edits appear in `git status` but were not made by this task — verified by
  isolating `git diff --stat` to exactly the 3 paths this task touched.
- ✅ **No test deleted or newly skipped.** Only `jest.setup.js` (adds a `require()` call) and
  `package.json`/`package-lock.json` (adds 2 devDependencies) were edited. Zero files under
  `__tests__/` or matching `*.test.*` were touched.
- ✅ **ADR-038 honored.** No test was deleted, skipped, or rewritten to reach a green number. Every
  remaining failure is reported above as a finding with evidence, not edited away.
- ✅ **Genuine product/test defects reported, not fixed.** See "Root cause of the 12 still-failing
  suites" above.

## Deviations from the task definition

| Step | Deviation | Why |
|---|---|---|
| Fix | Also added `@testing-library/user-event@^6.9.1`→ actually `^14.6.1` (repo convention), not named in the POML | Discovered mid-task: 4 suites failed to load entirely on `Cannot find module '@testing-library/user-event'` — the exact same defect class as the jest-dom gap (a testing utility imported by tests, never installed). Fixing it is "repair the harness," not "fix test-file type errors" or "rewrite test bodies." Escalation trigger 2 was checked first: `user-event@14` has no peer dependency on React or `@testing-library/react`, so this addition does not touch the React-19 tension and did not require escalation. |
| Scope boundary | Did NOT bump `@testing-library/react` past v14, despite clear evidence (`useAnnounce.test.ts`, 16/16 tests) that doing so would likely fix a meaningful chunk of the remaining failures | Escalation trigger 2 fires exactly here: `@testing-library/react@14` peer-declares React 18; the package is on React 19. A major-version bump is a package-graph decision outside this task's authority per the POML. Reported, not resolved. |

## Verification

- ✅ Before-state suite/test counts reproduce task 001's baseline exactly (13/21, 57/226).
- ✅ `git diff --stat` isolates this task's changes to 3 files, all additive, all jest/package
  configuration — no production source.
- ✅ Re-ran the final state twice; suite counts identical both times (12/9/21); test counts within 2
  (flaky `EntityPicker` timing tests, identified by name, not a harness regression).
- ✅ Re-ran typecheck at both the jest-dom-only and final states; the -4 user-event-attributable
  change was verified file-by-file, and the additional -3 was isolated as concurrent drift from
  006/007/008, not this task.
