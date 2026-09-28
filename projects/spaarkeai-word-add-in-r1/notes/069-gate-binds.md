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

| # | Proof | Commit | Run | Result |
|---|---|---|---|---|
| 1 | Baseline: pin at the true value, all changes in | `4d8752caa` | **36445980632** | ✅ **success** |
| 2 | `actionlint` (criterion 6) | `4d8752caa` | `actionlint` workflow | ✅ **success** |
| 3 | Debt pin **fires**: one extra accepted test-file error | `a154a0cb0` | **36447308107** | ✅ **failure, correctly** — `##[error]Accepted test-file debt ROSE: 75 line(s) vs the pinned 74`. `typecheck` job failed; `gated-jest` stayed **green**. |
| 4 | Debt pin **passes** back at the true value | `dd4add510` | **36447724326** | ✅ **typecheck success** — `74 total line(s), of which 74 are accepted test-file debt (pinned at 74)` |
| 5 | Newline guard **fires**: manifest's final newline stripped | `dd4add510` | **36447724326** | ✅ **gated-jest failure, correctly** — `##[error]ci-gated-suites.txt does not end with a newline…` |
| 6 | Newline guard silent when restored → both green | `47c7b5272` | **36448019636** | ✅ **both jobs success**; `actionlint` success |

**All six proofs are on `ubuntu-latest`**, which matters beyond this task: the promotion standard for making
this gate merge-blocking requires *N consecutive green runs on `ubuntu-latest`*, and at landing this gate's
evidence was five green runs all on a local Windows box and none on CI. These runs are the first real entries
against that standard — two green (`36445980632`, `36448019636`) plus two deliberate reds that failed **for
the intended reason**, which is stronger evidence than green alone.

**Proofs 4 and 5 shared one run deliberately.** They fire in **different jobs**, so neither can mask the
other — `typecheck` proved the pin passes at 74 while `gated-jest` proved the newline guard refuses, in the
same CI cycle. Isolation is per guard, which is what the prescriptive step sequence is protecting; one run
fewer is not a shortcut through it.

**What proof 3 also establishes**: `gated-jest` stayed green with a live TypeScript error in a gated test
file, confirming the header's claim that `ts-jest` runs `isolatedModules` and type-checks nothing. That is
*why* the typecheck job has to exist separately — jest cannot see this class of defect at all.

**What proof 5 establishes about the old code** — the thing the deleted comment claimed to prevent. Strip
that newline and: the manifest loses its final entry (`useSaveFlow.test.ts`), `GATED_COUNT` drops 56 → 55,
jest runs 55, and the suite-count assertion compares **55 to 55 and PASSES**. A live gated suite silently
ungated behind a green check. The old comment credited that assertion with catching exactly this, and it
never could.

**Proof 1 was checked for vacuity, not just for green.** A pass proves nothing if the assertion never
executed, so the run's own output was read back:

```
Production typecheck clean: 0 production error(s); 74 total line(s), of which 74 are accepted test-file debt (pinned at 74).
Suite-count assertion OK: jest ran 56 suite(s), manifest declares 56.
```

The pin is named in the output, so it was evaluated. The newline guard correctly stayed silent — the manifest
ends `0x0a` today.

### 🔴 The blocker that had to be cleared first: this PR was running NO CI at all

Every criterion here requires proof on CI, so the first push went out and **nothing ran** — not the
office-addins gate, not `Router`, **no workflow at all**, for five consecutive pushes across three days. The
API confirmed it bluntly: `actions/runs?head_sha=<HEAD>` returned `total_count: 0`.

Actions was healthy repo-wide the whole time (`unified-access-control-r2`'s PR ran normally 30 minutes
earlier), which is what made it confusing.

**Cause: PR #960 was `mergeable=CONFLICTING`.** A `pull_request` workflow runs against the PR's *merge
commit*; a conflicted PR has no merge ref, so GitHub silently schedules nothing.

> **The generalisable trap**: a conflicted PR does not show red checks — **it shows no checks**, which reads
> as "nothing to report" rather than "your gates are off". Any judgement of the form "CI is green" or "no
> failures" is *vacuous* on a conflicted PR. Check `gh pr view N --json mergeable` before trusting a check
> state. This is the same class as the no-vacuous-green guards inside this very workflow, one level up: the
> workflow guards against jest running nothing, and nothing guarded against *GitHub* running nothing.
> Candidate for `.claude/FAILURE-MODES.md` as a gotcha — not filed here, since that is a `.claude/` edit
> outside this task's scope.

Cleared by merging `origin/master` (we were 1 commit behind — **#1012, ADR-002 landing**, which also makes
`RecordOwnershipResolver.cs` as owner of invariant I-6 citable from master rather than an unmerged branch).
One conflict, in `.claude/agent-memory/researcher/MEMORY.md`: resolved by comparing the *referenced entry
sets* rather than by eye — HEAD was a superset (78 refs vs 56) missing exactly one master entry
(`dataverse-plugins-state-alternatives-2026-09-25.md`, from the ADR-002 work itself), so the resolution is
HEAD plus that line. Build after merge: **0 warnings / 0 errors**. `mergeable=MERGEABLE`, CI restored.

### A stale figure in the POML, corrected

The POML warned that local and CI disagree — *"80 local vs 74 CI at the same commit"* — and told this task to
pin the CI number for that reason. **At the pinning commit they agree: local `tsc` reports 74, CI reports 74.**
Pinning the CI figure remains correct (CI is what enforces it), but the specific discrepancy cited no longer
reproduces.

---

## Step 9.5 quality gates

`code-review` and `adr-check` are **not in this session's available-skills listing**, so the gate work was done
directly rather than by guessing at a skill name. What was actually checked, and how:

| Check | Result | How |
|---|---|---|
| **ADR-038** (testing strategy) | ✅ | No test file added, moved or deleted — `git diff` over `src/client/office-addins/` across the proof commits is **empty**. No KEEP-path change. The pin is CI configuration, not a test; bans B1–B17 untouched. |
| **Frozen-workflow constraint** | ✅ | `ci-tier1-blocking.yml` *does* appear in the range diff, but `git log` attributes it to `5fedee9e8` (#1012), confirmed an **ancestor of `origin/master`**. My only workflow edit is `office-addins-tests.yml`. |
| **Criterion 5 — `set +e` preserved** | ✅ **proven, not assumed** | The task diff removes **no** line matching `set +e` / errexit / `noprofile`, and the block plus its `🔴 LOAD-BEARING` comment are still present at `:438`–`:455`. |
| **CLAUDE.md §10 BFF hygiene** | N/A | No BFF change: no endpoint, service, DI registration or package. No publish-size or CVE impact. |
| **CLAUDE.md §6.5** | No conflict | The (a) rejection is not an ADR conflict — it is a factual correction to a task premise, escalated under `task-execute`'s prescriptive-mode rule. |
| **`actionlint`** | ✅ | Green on CI at three separate commits. |
| **Shell correctness** | ✅ with a caveat | `shellcheck` is **not installed locally**, so this is inspection plus *executed* edge-case tests, not static analysis. Three cases run against the new guard: empty manifest → passes and falls through to the pre-existing zero-entry check (correct layering, no double report); `CRLF` ending → passes (a CRLF file does end with a newline); bare `CR` → fires (malformed, correct to refuse). Integer comparison via `-ne` on the env-supplied `'74'`, and the failure branch's grep pipeline is `|| true`-guarded so `pipefail` cannot abort it. |

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
