# Task 069 — the office-addins gate: what bound already, what did not, what is pinned now

> **Date**: 2026-09-28
> **Task**: `tasks/069-make-office-gate-block-and-pin-debt.poml` · FULL · opus @ xhigh · `steps mode="prescriptive"`
> **File changed**: `.github/workflows/office-addins-tests.yml`

---

## Disposition of the three findings

| Finding | Verdict | Why |
|---|---|---|
| **(a)** "The gate does not bind" | 🔴 **REJECTED on evidence** | Both jobs already fail the workflow run. The finding rests on a conflation the file itself warns about. |
| **(b)** "The 111 is unpinned" | ✅ **DONE** | Real, and the live form of FAILURE-MODES AP-12. Pinned at **74**, measured not copied. |
| **(c)** "A false claim in a comment" | ✅ **DONE — implemented, not deleted** | The claim was false. A real check cost three lines, so it was cheaper to close the hole than to document its absence. |
| *(extra)* two further stale claims | ✅ Corrected | Same AP-12 class the task exists to fix; found while making the file truthful. |

---

## (a) Why "make the gate bind" had no work in it

**Step 2 of a `prescriptive` task could not be executed as written.** Per `task-execute`, a needed deviation
in prescriptive mode is an escalation, not a silent improvisation — so this is the escalation, recorded before
any edit was made.

The POML says:

> **(a) The gate does not bind.** `office-addins-tests.yml:275` and `:427` mark both jobs *"Reports, does not
> block"*. … A non-blocking job is a report. … and **nothing consumes that result**.

Measured against the file:

| Claim | Reality |
|---|---|
| jobs do not fail | **They do.** No `continue-on-error` exists anywhere in the file — the only two matches are comments *discussing* it. Every `run` step is under `set -euo pipefail` (the typecheck step deliberately `set +e`, then re-asserts `-uo pipefail`). There are **six** `exit 1` decision points. |
| "nothing consumes that result" | **False.** `if [ "$prod" -gt 0 ]; then … exit 1; fi` consumes the production count exactly. The no-vacuous-green guards consume theirs too. |

**"Reports, does not block" means does not block a MERGE.** That is a statement about master's ruleset
(`21824191`), which requires exactly one named status check — `Router` — and this workflow is not in it. The
file's header draws the distinction explicitly and names the conflation:

> `on: pull_request` plus no `continue-on-error` makes a check that CAN REPORT FAILURE; it does not make a
> check that BLOCKS. Those are not the same claim, and conflating them is FAILURE-MODES AP-12.

So finding (a) committed the exact error the file it is auditing warns about, one screen above the line it
cites. The remedy it proposes — make the jobs fail the run — was already true; the only lever that would make
a red here block a merge is the ruleset, which the POML itself scopes out as an owner action.

**The header also documents why naive promotion is wrong, not merely out of scope.** Adding a *path-filtered*
workflow to the required-checks list **deadlocks**: on any PR outside its paths the check is "expected but
never reported" and blocks forever — observed on **PR #322**. Promotion needs path classification hoisted
*into* the job first, plus N consecutive green runs on `ubuntu-latest`, a standard stated independently in
three places.

**What was genuinely wrong, and is fixed.** The two summary strings were accurate in their own sense but
invited precisely this misreading. Both now state the two facts separately — *"This job DOES fail the
workflow run… It does NOT block a merge"* — so the next reader cannot make finding (a) again.

---

## (b) The debt pin

**Pinned at 74** via a step-level `ACCEPTED_TEST_DEBT_LINES`, asserted as an **equality**, positioned *after*
the production check so a production error is always reported first.

**Provenance of the number — measured, not copied.** CI's own output at `949bfa123`
(run **36162453924**): `Production typecheck clean: 0 production error(s); 74 total line(s) are accepted
test-file debt.` No `src/client/office-addins` or `Spaarke.Auth` commit exists on this branch since
`c564a4e81`, so 74 was still current at the moment of pinning.

Three traps avoided, each one a real near-miss:

1. **The POML's own figure was stale.** It carried **111**; task 071 repaired the ten red jest suites and
   removed 37 lines. Pinning 111 would have pinned a ceiling 37 lines above reality — accepting future debt
   silently, which is the defect being fixed.
2. **Local disagrees with CI** — 80 local vs 74 CI at the same commit. The **CI** number is pinned, because
   CI is what enforces it.
3. This is the same stale-baseline shape root CLAUDE.md §10 records for publish size, where a three-week-old
   figure led a project to overstate its own contribution **46×**.

**A decrease fails too, deliberately.** Equality, not a ceiling: paying debt down turns the job red until the
pin is lowered in the same PR. That stops a hard-won reduction from being silently spent again later. The
re-baselining procedure is written into the workflow's `env:` block, so a legitimate move is a deliberate edit
with a reason in the PR body.

---

## (c) The trailing-newline hole — closed for real

**The claim was false, exactly as the POML said.** `:252-254` credited the suite-count assertion with
catching a manifest whose last line lacks a newline. It cannot: `GATED_COUNT` and the suites jest runs both
derive from the *same* `while read` array, so a dropped final line shrinks both equally and the equality still
passes.

**The POML authorised deletion if a real check was not cheap. It was cheap** — an explicit `tail -c 1` guard
in the manifest step, the only site where the hole can be closed. Three lines, so implementing beat
documenting an absence.

**One POML characterisation was stale and made the hazard more live, not less.** It called the risk latent
because *"its last real entry is followed by comment lines"*. The manifest's last line is now a **real
entry** — `shared/taskpane/hooks/__tests__/useSaveFlow.test.ts` — so a stripped newline would silently ungate
a live suite. The file does currently end `0x0a`; the mitigation the POML relied on is gone.

---

## Two further stale claims, corrected

Both are the AP-12 class this task exists to fix. Beyond the closed acceptance set — recorded as deliberate
additions, because leaving a known-false claim in a file being edited *for* truthfulness would be perverse.

1. **"12 of 22 suites green (164 of 380 tests)"** — stale since task 071. The gate now covers **56 of the
   package's 56 suites**, verified on CI (*"jest ran 56 suite(s), manifest declares 56"*). The original
   reasoning is kept, because it is why the ratchet exists and why it must not be replaced with a blanket
   `npm test`.
2. **`roots` is "currently `<rootDir>/shared`"** — and therefore a `word/` or `outlook/` suite would be
   silently ungated. `jest.config.js:12` now lists **three** roots, which is why
   `word/commands/__tests__/commands.test.ts` is reachable and the 56/56 assertion passes. The assertion
   stays: `roots` can narrow again and it is the only thing that would notice.

Task 056's `set +e` block and its comment are **untouched**, as the POML requires.

---

## CI proofs

Every change proven on CI, not locally — the task's binding constraint, because `shell: bash` errexit
injection is invisible locally and this desktop additionally has stale `node_modules`.

| # | Proof | Run | Result |
|---|---|---|---|
| 1 | Baseline: pin at the true value, all changes in | `6d8e1c1c4` | *pending* |
| 2 | `actionlint` (criterion 6) via `workflows-validate.yml` | `6d8e1c1c4` | *pending* |
| 3 | Debt pin fires: seed one extra accepted test-file error → job fails | | *pending* |
| 4 | Debt pin reverts → green | | *pending* |
| 5 | Trailing-newline guard fires: strip the manifest's final newline → job fails | | *pending* |
| 6 | Newline guard reverts → green | | *pending* |

---

## 🔴 What this task did NOT close (criterion 7)

**A red office-addins check still does not block a merge.** Master's ruleset `21824191` requires exactly one
named check, `Router`, and this workflow is not in it. Nothing in a workflow file can change that — it is a
**repository ruleset setting, and therefore an owner action.**

It is also not a settings flip. Promotion requires, in order:

1. **Path classification hoisted into the job** so the check always reports on every PR. Adding a
   path-filtered workflow to the required list deadlocks (PR #322).
2. **N consecutive green runs on `ubuntu-latest`** — the settled repo standard. This gate's evidence at
   landing was five green runs, all on a local Windows box and none on `ubuntu-latest`.
3. Then the ruleset edit.

Task 070 adds the C# runner and is the other half of FR-18's CI story. **Do not read this task as closing
FR-18.** It pinned the debt, closed a real hole, and made the file stop lying about what it does — it did not
make the gate merge-blocking, and could not have.
