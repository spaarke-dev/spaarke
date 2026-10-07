# Task 070 — a PR-time runner for this project's server suites

> **Date**: 2026-09-28
> **Task**: `tasks/070-pr-time-runner-for-office-server-tests.poml` · FULL · opus @ xhigh · directional
> **Files changed**: `.github/workflows/office-addins-tests.yml`

---

## The finding, restated accurately

This project added **46** C# test files (the POML says "~30" — corrected). They compile into the single
`Sprk.Bff.Api.Tests` assembly, and **no completing PR-time job ran them**:

| Runner | Why it doesn't cover them |
|---|---|
| `ci-tier1-blocking.yml` | Runs `Sprk.Bff.Api.Tests` only under `Category=GoldenUtteranceEval` / `FidelityGate`. None of these tests carry either category. |
| `ci-tier2-advisory.yml` | `continue-on-error: true`, 30-minute cap; the test step was **cancelled in 6 of 6** recent runs and the verdict step never executed. |
| `sdap-ci.yml` | The only one that completes — **and another project's task 077 deletes it.** |

**One correction to the framing**: `sdap-ci.yml` **still exists** as of 2026-09-28. The gap is therefore
**prospective**, not yet live. Saying "these tests run nowhere" today would be false; saying they will run
nowhere once that deletion lands is the accurate claim.

**Why Tier 2's cap is not the defect.** Measured, not assumed: the equivalent `sdap-ci` "Build & Test" job takes
**23–34 minutes** (runs `36434557427` = 32.6 min, `36431157503` = 34.4 min, `36162831641` = 22.8 min) for
**12,359** tests. A 30-minute cap sits *below the real cost of the whole assembly*. The **scope** is the defect,
so raising a cap would only move the cancellation.

## Escalation trigger 2 was checked FIRST, and did not fire

The whole design exists to work around a freeze, so the freeze was verified before designing anything.
`scripts/ci/shadow-window-status.ps1` on 2026-09-28: *"Window still open… **Do NOT edit ci-router.yml /
ci-tier1-blocking.yml / ci-tier2-advisory.yml** while it runs."* (6/20 agreeing PRs, 16.1 calendar days.)

**The reverse obligation is now recorded in the workflow header**: when the window closes, **migrate this job
into Tier 1 and delete it here.** Tier 1 is the correct home; leaving the job in both places is duplication, not
defence in depth.

## Selection mechanism — a third option the task did not anticipate

The POML's escalation trigger 1 offered two options and asked for both to be costed if neither worked:

| Option | Cost | Verdict |
|---|---|---|
| Run the whole assembly | 12,359 tests, 23–34 min | ❌ Reproduces the exact timeout being fixed |
| Add `[Trait("Category", …)]` to 46 files | scope creep; inclusion then depends on an attribute a new file can silently omit | ❌ |
| **Namespace-prefix filter** | **zero test-file changes** | ✅ **chosen** |

The namespaces were **already organised by feature area**, so prefix selection needed no change to any test
file — the trigger didn't fire because a third path existed.

**15 filter terms** over `Sprk.Bff.Api.Tests.{Api.Office, Api.Ai, Api.Documents, Integration.DataMutation,
Integration.Regression, Services.Office, Services.Documents, Domain.Office, Domain.Ai, Domain.Dataverse,
Seam.Office, Filters, Infrastructure.Graph, Infrastructure.Authentication, AccessControl}`.

**How a new suite gets picked up** (criterion 4): a new test class in any listed namespace is included
**automatically**, no edit. A test in a **new namespace** is not — and that omission is visible as one missing
line in the filter. So the mechanism is explicit, greppable, and fails visibly rather than silently.

**Criterion-1 suites verified present by name, not assumed:**

| Suite | Tests selected |
|---|---|
| `VisualizationRowAuthorizationContractTests` (NFR-02) | **11** — matches the POML's "11-Fact per-row authorization suite" exactly |
| `OfficeEndpointsContractTests` (the un-skipped `POST /api/office/save`) | 23 |
| `OfficeEndpointAuthorizationContractTests` | 8 |
| `OfficeSaveNoTargetContainerContractTests` | 8 |
| `OfficeSaveContainerProvenanceTests` | 3 |

## Duration and margin (criterion 2)

| Measure | Value |
|---|---|
| Tests selected | **1,776** (1,752 passed / 0 failed / 24 skipped) |
| Test execution on CI | 7 m 16 s |
| **Whole job on CI** | **9 m 40 s** (16:45:19 → 16:54:59) |
| Timeout | 20 min |
| **Margin** | **≈ 2.1×** |

Local run was 3 m 44 s; CI adds restore, build and LFS smudge. The header says explicitly: if this job ever
approaches the cap, **narrow the filter — do not raise the cap**, because raising it is how Tier 2 ended up
cancelled 6 of 6 times.

> Note the two counts measure different things and should not be conflated: `--list-tests` reported **1,708**
> selected locally, while the run's TRX counter reports **1,776** total. The floor sits under both.

## Two guards, both from lessons already in this repo

**1. `MIN_SELECTED_TESTS` floor (1600).** `sdap-ci.yml:246` already documents the hazard: *"A `--filter` term can
silently match zero tests (renamed, deleted, …)"*. A filter that selects **nothing** exits 0 and reports a green
check — the identical false green to a gate that runs no suites. Rename one namespace and, without this floor,
coverage collapses behind a passing check.

- A **floor, not an equality** — unlike 069's typecheck debt pin, tests are *expected* to grow, and an exact pin
  would fail every PR that adds one. Set ~10% under the measured 1,776.
- Checked **before** the failure check, deliberately: a silent coverage collapse is worse than a red test.

**2. `set +e`.** The 056/069 lesson, applying again: `shell: bash` expands to `bash --noprofile --norc -e -o
pipefail`, so errexit is injected. `dotnet test` exits non-zero on any failure, which would kill the step before
the count assertion and summary ran — making a real test failure and a collapsed filter *indistinguishable in
the log*.

## The fix that would have made the job pointless if missed

`on: paths` listed only **client** paths. A BFF-only PR would change the code under test and **not fire this
workflow at all** — tests selected, never run. Added the Office/Documents server dirs plus the suite dirs to
**both** `pull_request` and `push`, verified identical (15 each; a path in one list and not the other is a silent
hole).

Scoped to this project's surface rather than all of `src/server/**`, so this workflow does not quietly become a
repo-wide BFF gate. **Accepted cost, stated in-file**: `on:`-level paths fire the *whole* workflow, so a
server-only PR also runs the npm jobs. Under-triggering loses regressions; over-triggering loses runner-minutes
— erring toward correctness. Job-level path classification is *already* a prerequisite for promoting this
workflow to a required check, so it belongs there, once, rather than as a second mechanism here.

## 🔴 The gate found a real defect on its first run — mine

The first green-direction attempt (**run 36451879399**) failed with exactly **2** failures, both
`NdaSaveNo422RegressionTests`. Those belong to the Compose/NDA work, not this project; the broad
`Integration.Regression` prefix is what pulled them in.

**The tempting fix was the wrong one.** Narrowing the filter to dodge them would have gone green. Diagnosis
instead: the tests pass locally *and* pass in `sdap-ci` on CI, so local-vs-CI was never the axis. The difference
was **my checkout versus sdap-ci's** — `.gitattributes` sets `*.docx filter=lfs`, `actions/checkout` does **not**
smudge LFS by default, and that test opens `AppligentNDA_Signed.docx`. It was reading a ~130-byte **pointer
file**. `sdap-ci.yml` already carries `lfs: true` at `:122` and `:878` with exactly this comment — *"else ~200
Compose corpus seam tests fail on pointer files"* — and `:968` states outright that checkout does not smudge LFS.

**The failing tests were right; the job was misconfigured.** Dodging them would have left *every* LFS-backed
fixture in the selection silently reading pointer bytes, including fixtures not yet selected. Fixed with
`lfs: true` and a comment explaining why it must not be "tidied" away. LFS smudge cost ~10 s.

A small vindication of including other projects' tests in the prefix: their test caught my runner defect on its
first run.

## Criterion 5 — per-file inventory

**54** `.cs` test files touched by this project (46 added + 8 modified). **50 covered, 4 not.** Computed by
matching each file's namespace against the 15 filter prefixes, not by eye.

| Covered namespace | Files |
|---|---|
| `Api.Office` | 11 |
| `Integration.DataMutation.OfficeVersionSave` | 9 |
| `Api.Ai` | 6 |
| `Domain.Ai` | 4 |
| `Api.Documents` · `Integration.Regression` | 3 each |
| `Filters` · `Infrastructure.Graph` · `Services.Documents` · `Services.Office` | 2 each |
| `AccessControl` · `Domain.Dataverse` · `Domain.Office` · `Infrastructure.Authentication` · `Integration.DataMutation.SpeUploadPaths` · `Seam.Office` | 1 each |

### The 4 not covered, each with a reason

| File | Reason |
|---|---|
| `tests/integration/Shared/Office/MinimalDocx.cs` | **Not a test** — verified **0** `[Fact]`/`[Theory]` attributes. A docx-building helper. Adding `Shared.Office` to the filter would select nothing. |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` | Different assembly, and **genuinely covered by Tier 1** — four of its facts are named explicitly in Tier 1's filter (`EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver`, `NoAuthorizationFilterIsDecorative`, `ScannerAccountsForEveryRegistrationInTheGovernedFiles`, `TheEndpointFileCensusIsPinned`). Better coverage than this job could give — Tier 1 is the blocking tier. |
| `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` | Different assembly (`Spe.Integration.Tests`), which this job does not build. |
| `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs` | ⚠️ **See the residual below — this one is a real gap, not a benign omission.** |

### 🔴 Residual gap worth handing off

`SpeWriteSinkContainerProvenanceGuardTests` is **not** in Tier 1's filter — verified, **0** occurrences in
`ci-tier1-blocking.yml`. It runs only in `sdap-ci.yml` (which runs the whole `Spaarke.ArchTests` project, 2 call
sites) and in Tier 2, which is advisory and was cancelled 6 of 6 times.

**So once `sdap-ci.yml` is deleted, that guard runs in no blocking job at all.**

Deliberately **not** fixed here: it is a different assembly, and pulling a second test project into this job
would duplicate the ArchTests job pattern that already exists in three workflows and lengthen a job whose margin
is the point. The correct fix belongs to the **deletion PR** (another project's task 077): a PR that removes the
only runner for a guard should extend Tier 1's filter to cover what it removes. **Hand this to whoever owns that
deletion.**

## CI proofs

| # | Proof | Commit | Run | Result |
|---|---|---|---|---|
| 1 | First green attempt — **found the LFS defect** | `caf6171d5` | 36451879399 | ❌ 2 failures on pointer files (job itself completed in 9 m 30 s) |
| 2 | Green after `lfs: true` | `0572bbce2` | **36453304430** | ✅ `1752 passed, 0 failed, 1776 selected (floor 1600)` |
| 3 | **Red direction** — NFR-02 assertion inverted | `b6628af82` | **36455895181** | ✅ **failed, correctly** — `1 server test(s) failed`, naming `VisualizationRowAuthorizationContractTests.Related_WhenCallerIsDeniedReadOnAMatter_ServesNoneOfThatMattersDocuments` |
| 4 | Revert → green | `1e8c0fc79` | **36456902915** | ✅ **success** |

All four on `ubuntu-latest`. Two green, one deliberate red **that failed for the intended reason**, and one red
that exposed a genuine defect in the runner — stronger evidence than four greens would have been.

**The red-direction seed was chosen deliberately.** Not a convenient arithmetic typo: `NotContain` → `Contain`
on the core NFR-02 claim — *a caller denied Read on a matter must not receive that matter's documents through
the similarity surface*. The job's own justification says the cost of doing nothing is *"an authorization
regression merges silently"*, so the honest demonstration is that the gate catches an **inverted authorization
assertion**.

## 🔴 What this task did NOT close (criterion 6)

**This check does not block a merge.** Master's ruleset `21824191` requires exactly one named check, `Router`,
and this workflow is not in it. That is a **repository ruleset setting — an owner action**, and it is not a
settings flip: a path-filtered workflow added to the required list **deadlocks** ("expected but never reported"
on any PR outside its paths — observed on PR #322), so promotion needs path classification hoisted into the jobs
first, plus N consecutive green runs on `ubuntu-latest`.

Together with task 069, FR-18's CI story is now: the checks **exist, run, and report truthfully**. Making them
**binding** remains one owner action away.

---

## 🔴 STATUS CHANGE 2026-09-29 — the shadow window CLOSED, so the migrate-and-delete obligation is LIVE

When this task landed, the CI freeze was active and this note recorded a conditional: *"when the window closes,
migrate this job into Tier 1 and delete it here."* **The window has now closed.**
`scripts/ci/shadow-window-status.ps1` on master (PR #1028, task 071) reports:

```
  Comparable PRs examined : 9
  Agreeing                : 9 / 8
  Calendar-day span       : 24.7 / 5
  False reds (logged)     : 0
  FALSE GREENS            : 0

  WINDOW SATISFIED -- sdap-ci.yml may be retired (tasks 071/075/077),
  then branch protection with `CI / Router` as the required check.
```

**Three consequences, in order of how quietly they bite:**

1. **The reason this job lives here is gone.** Tier 1 is the correct home; it was only unavailable because
   editing it restarted the cutover clock. That constraint has expired, so `server-tests` (and the `lint` job
   task 072 added) should migrate into Tier 1 and be **deleted here**. Leaving them in both places is
   duplication, not defence in depth — and the duplicate is the copy that will drift.
2. **This task's "prospective" framing expires with it.** The note says the coverage gap is prospective because
   `sdap-ci.yml` still exists. It is now *sanctioned for retirement*, so the gap converts to live the moment
   someone acts on that.
3. **Issue #1016 becomes urgent rather than advisory.** `SpeWriteSinkContainerProvenanceGuardTests` appears
   **zero** times in `ci-tier1-blocking.yml`'s filter and runs only in `sdap-ci.yml` and advisory Tier 2. The
   moment `sdap-ci.yml` goes, that guard is evaluated **nowhere** — it does not fail, it stops being checked.

**Deliberately NOT done now**, and the reason matters: the migration edits `ci-tier1-blocking.yml`, which every
project depends on, and it would land minutes before a merge to master with no CI cycle to validate it. A CI
change whose first real exercise is on master is the shape of defect this whole wave has been removing. It wants
its own task, its own red/green proof, and the ruleset decision alongside it.
