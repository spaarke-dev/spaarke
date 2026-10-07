# Task 056 — `office-addins` had no CI typecheck gate (GitHub #996 / ISS-004)

> **Status**: implemented 2026-09-19. Closes the *second* half of FR-18's acceptance, which was never built.
> **Scope honesty up front**: this restores a **reporting** gate, not a blocking one. See §5.

---

## 1. What was actually wrong

FR-18 (`spec.md:92`) accepted: *"`npm run typecheck` is clean; **CI gates it going forward**."*

Both halves were false, in different ways.

**The first half was deliberately superseded.** `npm run typecheck` exits **2**. That is not a regression — it is
an owner-approved accept (project `CLAUDE.md`, 2026-09-09, narrowed 290 → 111 on 2026-09-12). Every remaining
error is in a test file.

**The second half was simply never built.** Re-verified 2026-09-19 at `23fd17991`:

```
$ grep -c typecheck .github/workflows/office-addins-tests.yml   -> 0
$ grep -c typecheck .github/workflows/deploy-office-addins.yml  -> 0
```

Every `typecheck` occurrence in `.github/workflows/` belongs to a *different* package in `sdap-ci.yml:432-485`
(`Spaarke.UI.Components`, `Spaarke.AI.Widgets`, `Spaarke.Auth`).

**Why that mattered.** The 2026-09-09 accept rests on a stated safety property — *"production is at 0, so new
production errors stand out."* Nothing enforced it. `npm run build` is webpack and test files are not in its
graph; `ts-jest` runs `isolatedModules`, which is transpile-only and type-checks nothing. So a new **production**
TypeScript error would have reached master with nothing red. The accept itself flagged this as the thing to watch.

This is [`FAILURE-MODES.md` AP-12](../../../.claude/FAILURE-MODES.md) — prose outliving the mechanism it
describes — and it appeared **twice** in this one task (see §4).

---

## 2. Measured baseline (not quoted — run)

At `23fd17991`, in `src/client/office-addins`:

| Measure | Value |
|---|---|
| `npm run typecheck` exit | **2** |
| Total `error TS` lines | **111** |
| **Production errors** | **0** |
| Test-file errors (accepted debt) | 111 |
| Distinct files | 9 |

Classification expression (the one the CI job uses):

```bash
npm run typecheck 2>&1 | grep "error TS" | grep -vE '__tests__|\.test\.|\.spec\.|__mocks__'
```

---

## 3. Reproduce-first evidence (both directions seeded, verbatim)

A gate whose detection was never observed is not a gate. Both directions were seeded against the live tree and
reverted with `git checkout --`; the repo was confirmed clean afterwards.

**Seed A — a PRODUCTION error MUST be detected.** Appended to `shared/taskpane/utils/errorMessages.ts`:

```
shared/taskpane/utils/errorMessages.ts(586,14): error TS2322: Type 'string' is not assignable to type 'number'.
production_count=1
```

✅ Detected.

**Seed B — a TEST-FILE error MUST NOT be detected.** Appended to
`shared/taskpane/utils/__tests__/errorMessages.collision.test.ts`:

```
production_count=0
total_error_lines=113
shared/taskpane/utils/__tests__/errorMessages.collision.test.ts(116,7): error TS2322: ...
shared/taskpane/utils/__tests__/errorMessages.collision.test.ts(116,7): error TS6133: '__task056Probe' is declared but its value is never read.
```

✅ Correctly ignored — and the seeded error is visibly present in the output, proving `tsc` *saw* it and the
classifier, not blindness, is what excluded it.

> **A prediction of mine was wrong, recorded rather than smoothed.** I annotated Seed B *"expect baseline+1"*
> and measured **113, i.e. +2**. One seeded line yields two diagnostics (`TS2322` plus `TS6133`
> unused-variable). The verdict is unaffected — production stayed 0 — but the note states the measured number,
> not the predicted one.

---

## 4. The subtlety that decides this task

**`tsc` exits 2 today, on accepted debt.** A job that simply let a non-zero exit fail would have been red on
every PR from the moment it landed. This workflow already records what happens then (`office-addins-tests.yml`
:92-100): *"a check that is always red is a check everyone learns to ignore, which is worse than no check."*

So the job decides from the **production error count**, never the exit status. `set -e` is deliberately absent,
with a comment saying why — otherwise a future reader "fixes" its absence and silently breaks the gate.

It also carries a **no-vacuous-green guard**, mirroring the one `gated-jest` already needed: if `tsc` exits
non-zero while emitting **zero** `error TS` lines, that is a broken run (missing script, broken install, crash),
not a clean tree — and a production count of 0 would otherwise read as a pass. That case fails loudly.

**Second AP-12 instance, found while editing.** The Summary step printed `**Blocking.**` — which this same
file's own corrected header (:22-37) states outright is false: master's ruleset requires exactly one named check,
`Router`, and this workflow is not in it. The prose had outlived its correction *inside the file that documents
the correction*. Fixed to "Reports, does not block."

---

## 5. Scope: reporting, not blocking

A red here renders a red X; the PR can still merge. Promotion to blocking needs **both**:

1. path classification hoisted **into the job** (a path-filtered workflow in the required list deadlocks —
   "expected but never reported" on any PR outside its paths; observed on PR #322), and
2. N consecutive green runs on `ubuntu-latest`.

Neither is attempted here. Claiming otherwise would recreate the exact defect this task fixes.

---

## 6. Changes

| File | Change |
|---|---|
| `.github/workflows/office-addins-tests.yml` | New `typecheck` job (production-only gate + no-vacuous-green guard); `**Blocking.**` → `**Reports, does not block.**` |
| `spec.md:92` | FR-18 acceptance amended to the measured reality |
| `spec.md:214` | Criterion 12 amended |
| `spec.md:264` | Assumptions row amended |

**Not touched**, by constraint: `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` (frozen under
the shadow window); `client-tests.yml` (advisory by construction across 40 packages). No test or source file was
modified to make the gate green.

**`/conflict-check`**: soft warn → proceed. No open PR touches `office-addins-tests.yml`; no other worktree has
uncommitted workflow edits; master has **zero** commits touching `.github/workflows/` since merge-base
`e0a6f87c4`. Dependabot #909 bumps `actions/setup-node` across nine workflows but not this one, which already
pins v6 — the new job uses v6 to match its sibling.

**`actionlint`**: not installed locally. The workflow was validated by YAML parse (`js-yaml` → `YAML_OK`, jobs
`gated-jest` + `typecheck`). The `actionlint` criterion is satisfied by CI's own `actionlint` job on this PR —
stated as pending rather than claimed as run.
