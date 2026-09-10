# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | none — 043 just completed |
| **Task File** | — |
| **Phase** | 4 Parity, deploy, close |
| **Status** | not-started |
| **Started** | — |
| **Rigor** | — |
| **Next Action** | Pick the next 🔲 from `tasks/TASK-INDEX.md`. |

> ⚠️ This file previously showed task **019** as in-progress. That was stale — 019's work is
> committed (`436507b32`, `12c1c3b74`, `336ea3809`) and tasks 032/044/043 have completed since.
> Reset here by task 043 per root CLAUDE.md §7.

## Critical Context

Task **043** shipped the office-addins PR gate. Two NEW files, nothing modified:

- `.github/workflows/office-addins-tests.yml` — narrow, path-filtered, PR-blocking.
- `src/client/office-addins/ci-gated-suites.txt` — the ratcheting allow-list (12 of 22 suites).

**The frozen tier files, `sdap-ci.yml` and `client-tests.yml` are byte-identical to HEAD and
`origin/master`.** Shadow-window cost: 0 PRs, 0 days. The window is exactly as task 044 left it,
still awaiting the operator's §6.5 path-B decision on the measurement latch.

**Not committed, not pushed** — per the task instruction.

## Handoff Notes

- The gate covers the 12 green suites (164 tests), incl. `WordAdapter.test.ts` 37/37 (the `.docx`
  binary save path / 2026-09-03 UAT defect) and `useAnnounce.test.ts` 19/19. The 10 red suites are
  untouched and still run nightly in `client-tests.yml`, which is now the promotion signal: when
  the nightly baseline shows one green, add it to `ci-gated-suites.txt` in the PR that fixed it.
- Open items surfaced but deliberately NOT absorbed — `notes/043-office-addins-ci-gate.md` §8:
  **F-1** `deploy-office-addins.yml`'s path filter misses `provenance.ts`, which ships (one-line
  fix, different owner) · **F-2** the 10 red suites, 84 failing tests, needs its own task ·
  **F-3** shadow-window false green on PR #934 (task 044 owns it) · **F-4** `identity-obj-proxy`
  referenced by `jest.config.js` but not a dependency · **F-5** `word/`+`outlook/` are outside
  jest's roots, so the entry points have no coverage · **F-6** `unified-access-control-r2` has a
  branch diff on the frozen `ci-tier1-blocking.yml` — operator's call.
- Reproduce the proof: `git archive HEAD src/client/office-addins src/client/shared/Spaarke.Auth
  src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` into a
  temp tree, `npm install --legacy-peer-deps --no-audit --no-fund`, then run the manifest paths via
  `npm test -- --ci --runTestsByPath …`. Expect 12/12, 164/164, exit 0.

## Completed Steps

- [x] Task 043 — all steps; acceptance criteria in `notes/043-office-addins-ci-gate.md` §7.

## Files Modified

- (none — two additions only, listed above)

## Decisions Made

- 2026-09-10: **New workflow, not an extension of `client-tests.yml`.** Extending it required
  either a `pull_request` trigger (fires all 40 packages per PR — the contention its own header
  rejects) or per-leg conditional blocking (falsifies its "advisory by construction" invariant),
  and its header names a promotion gate — shadow window closed AND baseline green — that is not
  met. Followed the existing narrow-gate pattern (`css-reset-gate.yml`) instead.
- 2026-09-10: **Allow-list of green suites, not an allowed-failure list.** An allowed-failure list
  makes the verdict depend on parsing failure identity out of a report, and would run the two
  slowest flaky suites on every PR for a discarded result.
- 2026-09-10: **Added a third trigger path** beyond the `deploy-office-addins.yml` pair —
  `…/Spaarke.Communication.Components/src/logic/connections/provenance.ts` — because both
  `jest.config.js` and `webpack.config.js` map it and the add-ins import it. Superset of the
  constraint, same rationale.
- 2026-09-10: **No build step in the gate.** `jest.config.js` maps `@spaarke/auth` to source, so
  the tests need no `dist/` and no webpack env vars. Verified on a from-scratch tree.
