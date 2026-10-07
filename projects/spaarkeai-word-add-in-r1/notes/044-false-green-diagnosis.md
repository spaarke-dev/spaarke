# Task 044 — Diagnosis: the shadow-window FALSE GREEN on PR #934

> **Task**: `tasks/044-shadow-window-false-green.poml` · **Issue**: ISS-002
> **Date**: 2026-09-10 · **Rigor**: FULL · opus @ xhigh
> **Files changed by this task**: NONE outside `projects/spaarkeai-word-add-in-r1/notes/`.
> No workflow file was modified. **Cost to the shadow window: 0 PRs, 0 days.**

---

## VERDICT

> ## ROUTER DEFECT — genuine, already remediated on master by PR #944.
> ## Path C was the right response and has already been executed.
>
> **But the window is still blocked, and NOT by the CI defect.** It is blocked by the
> *measurement instrument*: `shadow-window-status.ps1` latches on a historical false
> green that its own remedy ("the count restarts from the most recent one") was
> supposed to clear. Clearing it is a **§6.5 path B** decision — escalated in §6, not
> taken unilaterally here.

Verdict is **not** DESIGNED-BEHAVIOR and **not** ACCEPTED-GAP. §4 shows why each is wrong.

---

## 1. The fact, established from the recorded runs

PR **#934** — `feat(email-intelligence-r2): Outlook/Word add-in — Create To Do (sprk_todo), Word .docx save, web auth, icons`
merged `2026-09-03T15:56:35Z`, merge commit **`f5fee214112db2510e65092718e27a1ee8e66c30`**.

Four workflows ran on that commit:

| Run id | Workflow | Conclusion |
|---|---|---|
| `33775635122` | **SDAP CI** (legacy) | **failure** |
| `33775635602` | **CI** (new tier) | **success** |
| `33775635072` | Deploy Office Add-ins | success |
| `33775635294` | Deploy SpaarkeAi | success |

### 1a. The legacy failure — exact job and step

**Run `33775635122` → job `100716509606` — `Build & Test (Debug)` → step 7, `Build`.**
<https://github.com/spaarke-dev/spaarke/actions/runs/33775635122/job/100716509606>

Every subsequent step (`Test with coverage (pass 1)`, the retry classifier, `Final test verdict`)
was **skipped**. So the legacy red is a **compile failure, not a test failure** — no test ever ran.

```
##[error]D:\a\spaarke\spaarke\tests\integration\Sprk.Bff.Api.IntegrationTests\Membership\Phase2EndToEndFixture.cs(443,17):
  error CS1503: Argument 4: cannot convert from 'System.Threading.CancellationToken' to 'string?'
  [.../Sprk.Bff.Api.IntegrationTests.csproj]
Build FAILED.  5 Warning(s)  1 Error(s)
```

Root cause in the product diff: **#934 added a 4th argument to `IOfficeService.QuickCreateAsync`
and did not update `Phase2EndToEndFixture.cs`**, so `Sprk.Bff.Api.IntegrationTests` stopped
compiling. Legacy `Build & Test (Debug)` builds the **whole solution**, so it saw it and blocked.

### 1b. What the new tier did on the same commit

Run `33775635602`, all 18 jobs:

| Tier | Job | Conclusion |
|---|---|---|
| classify | Classify Diff | success |
| **Tier 1** | Classify Tier 1 Surfaces · Tenant Isolation · **Compile (Debug)** · Eval Gate · Compose Fidelity Gate · Arch Tests | **all success** |
| Tier 1 | Auth Smoke · Changed-Surface Integration Smoke | skipped (conditional) |
| Tier 2 | Markdown Link · Last Reviewed · Lint · Format · Plugin Size · ADR Compliance | success |
| **Tier 2** | **Full Unit Tests (Debug)** (job `100716654163`) | **failure** |
| **—** | **Router** | **success** |

Tier 2's `Full Unit Tests (Debug)` failed at **step 6, `Build`** — with the byte-identical error:

```
##[error]...Phase2EndToEndFixture.cs(443,17): error CS1503: Argument 4: cannot convert from
  'System.Threading.CancellationToken' to 'string?'
```

Its steps 7–10 (`Run unit tests (pass 1)` … `Test verdict (advisory — reported, never blocking)`)
were all **skipped**. So Tier 2 did not fail a *test*; it failed the *build that precedes* its tests.

### 1c. Why Tier 1's `Compile (Debug)` passed — the actual gap

From the recorded log of job `100716653033` (**not** from YAML):

```
##[group]Run dotnet restore src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj --verbosity minimal
##[group]Run dotnet build   src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Debug --no-restore
```

**Tier 1's compile gate built exactly one project — the production BFF csproj. It compiled no test
project at all.** The broken file lives in `tests/integration/Sprk.Bff.Api.IntegrationTests`, which
Tier 1 never touched. It passed truthfully about a scope far narrower than its name implied.

**Net effect: a green `CI / Router` over a solution that does not build.**

---

## 2. Which tier covers it, and whether Router adjudicates it

| Concern | Tier 1 (blocking) on 2026-09-03 | Tier 2 (advisory) | Adjudicated by Router? |
|---|---|---|---|
| Compile `Sprk.Bff.Api` | ✅ `Compile (Debug)` | (incidental) | **YES** |
| **Compile test projects** | ❌ **no job built any** | ✅ incidentally, as the build step of `Full Unit Tests` | **NO** |
| Execute the full unit suite | ❌ by design | ✅ `Full Unit Tests` | **NO — correct** |

`ci-router.yml` job `router-result` excludes tier2 **by construction**, not by allow-list:

```yaml
- name: Build adjudication set (tier2 excluded — advisory per FR-A03)
  run: |
    printf 'json={"classify":{...},"tier1":{...}}\n' ...
```

Only `classify` and `tier1` reach `alls-green`. That exclusion is correct and is not the defect.

**The defect is the empty cell**: on 2026-09-03, *"do the test projects compile?"* was adjudicated by
**no tier at all**. Tier 2 caught it only as a side effect of needing a build before running tests.

Scale of the gap at the time — **5 of 8 solution test projects were reachable by no blocking Tier 1
job**: `Sprk.Bff.Api.IntegrationTests`, `Sprk.Provisioning.ControlPlane.LoadTests`,
`Sprk.Provisioning.ControlPlane.NightlyTests`, `Spaarke.Core.Tests`, `Spaarke.Scheduling.Tests`.
This was a **class**, not a one-off. `Spaarke.Core.Tests` (46 tests) was not even a member of
`Spaarke.sln`, so nothing in CI had ever built or run it.

---

## 3. Remediation — already on master, and verified live

**PR #944** — `fix(ci): Tier 1 compiles the whole solution — closes the shadow window's false green`
commit **`ce5c2c3d71a3bab74ad7c9f1d85798a749262cc9`**, merged **`2026-09-04T22:13:09Z`**.

```yaml
- name: Restore (whole solution)
  run: dotnet restore Spaarke.sln --verbosity minimal
- name: Build (Debug, whole solution)
  run: dotnet build Spaarke.sln -c Debug --no-restore
```

It also added `Spaarke.Core.Tests` (plus `Spaarke.Core`, `Spaarke.Dataverse`) to `Spaarke.sln`.
Its controlled proof re-seeded #934's exact arity break: OLD scope → *build succeeded, 0 errors*;
NEW scope → *error CS1503*.

**Independently verified here from a recorded run**, not from YAML — job **`102698717302`**
(`Tier 1 (Blocking) / Compile (Debug)`, run `34421803991`, 2026-09-10T00:34Z):

```
##[group]Run dotnet build Spaarke.sln -c Debug --no-restore
  Spaarke.Core.Tests            -> .../tests/unit/Spaarke.Core.Tests/bin/Debug/net10.0/Spaarke.Core.Tests.dll
  Sprk.Bff.Api.IntegrationTests -> .../tests/integration/Sprk.Bff.Api.IntegrationTests/bin/Debug/net10.0/...
```

Tier 1 now demonstrably compiles the exact project whose CS1503 it missed. **Compile coverage is
9/9 test projects**: 8 via `Spaarke.sln`, plus `Spaarke.ArchTests` (deliberately outside the
solution, built directly by its own blocking Tier 1 `arch-tests` job).

⚠️ **Minor doc defect**: the comment introduced by #944 says *"widened 2026-08-31"*. That date is
wrong and chronologically impossible — it predates the 09-03 failure it describes. Actual merge:
**2026-09-04**. Worth a one-line correction whenever that file is next opened; **not** worth an
edit of its own, since touching a frozen tier file costs window time (see §6).

---

## 4. Why this is a defect and not the other two verdicts

The POML's escalation trigger reads: *"legacy's failing check maps to Tier 2, which Router correctly
excludes → DESIGNED, do not fix."* Superficially it fires — the failing **job** was in Tier 2.
It does not actually fire, and the distinction is the whole judgment of this task.

**Map the concern, not the job.** Tier 2's charter is *test execution* ("a unit-test flake must never
block master"). The thing that failed was **compilation**, which is Tier 1's declared charter — the
job is literally named `Compile (Debug)`. Tier 2 caught it only because it must build before it can
test. Attributing the catch to Tier 2's advisory role confuses an incidental prerequisite with a
designated responsibility.

Three confirmations that "designed" is the wrong reading. Only the first two are *independent* of
the fix — the third is corroboration, and is labelled as such deliberately:

1. **The step-level evidence.** In *both* systems the failing step is `Build`, and in both the
   test-execution steps are `skipped`. Not one test executed. There is no test outcome anywhere in
   this incident for Tier 2's advisory status to be about. **This alone carries the verdict.**
2. **The pre-existing charter — written before either PR.** `ci-cd-unit-test-remediation-r1/spec.md`
   **FR-A02** defines Tier 1's contents as *"compile (Debug only, no `-warnaserror`); critical
   NetArchTest MUST-NOT subset; changed-surface integration smoke; auth smoke…"* and names what is
   **`NOT in Tier 1: format, lint, markdown validator, dependency audit, Trivy, full unit/integration
   tests.`** Note what that exclusion list contains and what it does not: it excludes **running**
   tests, never **compiling** them — and it assigns *"compile"* to Tier 1 unscoped, with no
   restriction to the BFF csproj. The design always intended Tier 1 to own compilation. The
   implementation under-delivered against its own spec.
3. **Corroboration (not independent):** the owners shipped #944 as a scope correction rather than
   defending the behavior as designed. This is the same event as the fix, so it cannot prove the
   point on its own — the `ci-tier1-blocking.yml` comment stating *"the gate is 'the solution
   builds', not 'the tests pass'"* was **authored by #944 itself**, not pre-existing doctrine. It is
   listed for completeness, and item 2 is the load-bearing pre-existing source.

**Not ACCEPTED-GAP** either: an accepted gap is one left open on purpose. This one is closed, with
a controlled experiment proving it closed, and coverage widened from 3/8 to 9/9 test projects.

**What genuinely IS designed and correctly remains so**: *test execution* for the projects outside
Tier 1's filtered gates stays advisory in Tier 2. Blocking Tier 1 test execution is deliberately a
narrow subset — ArchTests MUST-NOT facts, tenant isolation I1–I5, golden-utterance eval, compose
fidelity, and conditional auth / changed-surface smoke. Everything else is advisory **by design**,
and Router excluding it is correct. That is the north star working, not a gap.

---

## 5. 🔔 The window is still blocked — and the CI fix did not unblock it

This is the finding that keeps ISS-002 open, and it is **not** what the task expected to find.

`shadow-window-status.ps1` today (unchanged since ISS-002 was filed):

```
Window opened : 2026-08-27 20:47 UTC
Agreeing      : 11 / 20     Calendar-day span : 4.1 / 5
False reds    : 0           FALSE GREENS      : 1     <-- still #934
```

**#944 fixed the CI defect but did not clear the measurement.** The script derives everything live
from the GitHub API, and `#934` (2026-09-03) still falls inside the default window that opens
2026-08-27. Two things in the script then collide:

```powershell
# remedy: a false green RESETS the count
$countingFrom = ($falseGreens | Sort-Object MergedAt | Select-Object -Last 1).MergedAt
$counting     = @($agreed | Where-Object { $_.MergedAt -gt $countingFrom })

# gate: but it ALSO latches, permanently, over the whole window
$ready = ($counting.Count -ge 20) -and ($daySpan -ge 5) -and ($falseGreens.Count -eq 0)
```

`$falseGreens` is computed over **all** rows in the window, never filtered by `$countingFrom`.
So `$ready` **can never become true** while #934 is in range, no matter how many PRs subsequently
agree. The script's own prose — *"the count above restarts from the most recent one"* — describes a
remedy the gate then makes unreachable. **One false green, ever, is a permanent latch.**

A second, independent staleness: the `-Since` docstring justifies its default as *"immediately after
PR #841 … the last change to the CI configuration under observation. Starting earlier would measure
a configuration that no longer exists."* **That claim is now false** — #944 changed the configuration
on 2026-09-04. By the script's own doctrine the default window start is out of date.

Measured both ways today:

| `-Since` | Comparable | Agreeing | Day span | False greens |
|---|---|---|---|---|
| `2026-08-27T20:47Z` (current default) | 44 | 11 / 20 | 4.1 / 5 | **1 — latched** |
| `2026-09-04T22:13:10Z` (just after #944) | 5 | 5 / 20 | 3.0 / 5 | **0** |

**Correction to the task brief's cost estimate.** The brief priced a change at "11 PRs and 4 days".
For *this* remedy the marginal cost is **6 PRs and 1.1 days** — the delta between the two rows above
(`11 − 5 = 6` agreeing PRs, `4.1 − 3.0 = 1.1` days), **not** the full 11/4.1. #934's own reset had
already discarded everything before 2026-09-03, so those 11 agreements were themselves being counted
from the defect; only the 09-03 → 09-04 slice is actually forfeited.

Post-fix the window is accumulating cleanly: of the **5 comparable PRs** merged since #944, **all 5
agree — 0 false greens, 0 false reds.** (Read the table's "5 / 20" as *progress toward the 20-PR
target*; "5 of 5 agree" is the *agreement rate* among those same 5. Two different fractions over the
same measurement, not a contradiction.)

---

## 6. 🔔 Criterion Conflict — Resolution Required (§6.5 protocol, applied by analogy)

> **On the header**: root CLAUDE.md §6.5 is written for *ADRs*. The rule in conflict here is a
> **project spec criterion**, not a numbered ADR — no ADR is being challenged. The §6.5 machinery is
> applied by analogy because it is the repo's escalation format for "a codified prior decision now
> produces a worse outcome", and because this task's own POML constraint directs it: *"if the outcome
> is 'the cutover criteria are wrong as written', that is a path B amendment and needs the §6.5
> format and operator sign-off."*

- **Rule in question**: the shadow-window exit criterion (`ci-cd-unit-test-remediation-r1`
  spec.md, *"Shadow-window exit criterion"*, amended 2026-08-27), as implemented by
  `scripts/ci/shadow-window-status.ps1`.
- **Specific rule**: *"20 code PRs on which sdap-ci.yml and CI (Router) returned the SAME blocking
  verdict, with **ZERO false greens**, spanning ≥ 5 calendar days."*
- **Conflict**: implemented as a permanent latch over the whole observation window, "zero false
  greens" contradicts the same tool's documented remedy that the count "restarts from the most
  recent one". As written, the window can **never** close — the cutover is blocked by a reporting
  artifact of a defect that is fixed and proven non-reproducible, not by any live CI weakness.
- **Proposed path**: **B — amendment**, operator sign-off required. Two admissible shapes:

  | | Option 1 — advance `-Since` (RECOMMENDED) | Option 2 — de-latch `$ready` |
  |---|---|---|
  | Change | default `-Since` → `2026-09-04T22:13:10Z` | evaluate "zero false greens" over the counting run only |
  | Rationale | the script's **own doctrine**: `-Since` = just after the last CI-config change; #944 *is* one | makes the gate match the script's own prose |
  | Criterion semantics | **unchanged** | relaxed — a false green becomes a reset, not a hard stop |
  | Cost to window | **6 PRs, 1.1 days** (5/20, 3.0/5 days today) | **0 PRs, 0 days** (keeps 11/20, 4.1/5) |
  | Risk | none — strictly conservative | weakens a safety criterion |

- **Rationale for Option 1**: it changes *no* criterion semantics, only applies the tool's existing
  doctrine consistently. #944 altered the independent variable; observations of the old Tier 1 are
  observations of a system that no longer exists. The cost is small and the post-fix window is
  already clean. Option 2 is cheaper but edits a safety gate to make a cutover reachable — the wrong
  direction to relax under pressure.
- **Impact if accepted**: one-line default change in `scripts/ci/shadow-window-status.ps1` plus its
  `.PARAMETER Since` prose. **No frozen tier file is touched, so the shadow window forfeits nothing
  beyond what #944 already spent.**
- **Alternative considered and rejected — do nothing (ACCEPTED-GAP)**: not viable. The latch is
  unconditional; `sdap-ci.yml` could never retire.
- **Residual to name whichever option is chosen**: under Option 1 the latch survives. A *new* false
  green re-latches, and the only escape is another `-Since` bump — which conflates "the config
  changed" with "we found a defect". Worth closing eventually; not required for this cutover.

**This task made no such change.** Per the POML constraint (*"path B … needs the §6.5 format and
operator sign-off, not a unilateral rewrite"*), the decision is the operator's.

---

## 7. Escalation triggers — disposition

| POML trigger | Fired? | Why |
|---|---|---|
| Failing check maps to Tier 2, Router correctly excludes → DESIGNED | **NO** | The failing *job* was Tier 2; the failing *concern* (compilation) is Tier 1's. §4. |
| Diagnosis requires re-running historical workflows / mutating comparison data | **NO** | Read-only throughout: `gh run view`, `gh api .../jobs`, `.../logs`. No re-runs, no writes. |
| A whole CLASS of failures Router cannot see | **YES — reported, and already closed** | 5 of 8 test projects had no blocking compile coverage; now 9/9 via #944. Test *execution* for those projects remains Tier-2 advisory **by design** and is not a gap. |
| — | **NEW** | The exit criterion cannot be satisfied as implemented (§5). Escalated as §6.5 path B in §6. |

---

## 8. Acceptance criteria

| Criterion | Status |
|---|---|
| Legacy failing job + step identified from the recorded run, cited by run id + job name | ✅ Run `33775635122`, job `100716509606` `Build & Test (Debug)`, step 7 `Build`, `CS1503` at `Phase2EndToEndFixture.cs(443,17)` |
| Corresponding coverage in classify/tier1/tier2 stated, incl. Router adjudication | ✅ §2 — test-project compilation was in **no** tier on 09-03; Tier 2 caught it incidentally; Router excludes tier2 by construction |
| One verdict, distinguished from the other two | ✅ **ROUTER DEFECT (path C, already executed by #944)** — §4 rules out DESIGNED-BEHAVIOR and ACCEPTED-GAP |
| If a tier file was modified: window cost + live count of any guard | ✅ **N/A — no tier file modified.** Cost 0 PRs / 0 days. No guard added. |
| `sdap-ci.yml` unmodified | ✅ `git diff --name-only origin/master -- .github/workflows/` → empty; `git status --porcelain` → clean |
| `actionlint` passes on any changed workflow | ✅ Vacuous — no workflow file changed |
| ISS-002 updated with verdict + next owner | ✅ `notes/defer-issues.md` — verdict recorded, open item narrowed to the §6 path-B decision, owner = operator |

---

## 9. Evidence index

| Artifact | Reference |
|---|---|
| PR #934 | <https://github.com/spaarke-dev/spaarke/pull/934> · merge `f5fee214112db2510e65092718e27a1ee8e66c30` · 2026-09-03T15:56:35Z |
| Legacy run (failure) | run `33775635122` · job `100716509606` `Build & Test (Debug)` step 7 `Build` |
| New-tier run (success) | run `33775635602` · job `100718544561` `Router` |
| Tier 1 compile that missed it | job `100716653033` — `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` |
| Tier 2 job that caught it | job `100716654163` `Full Unit Tests (Debug)` step 6 `Build` |
| Fix | PR #944 · commit `ce5c2c3d71a3bab74ad7c9f1d85798a749262cc9` · merged 2026-09-04T22:13:09Z |
| Fix verified live | job `102698717302` (run `34421803991`, 2026-09-10T00:34Z) — `dotnet build Spaarke.sln`, builds `Sprk.Bff.Api.IntegrationTests.dll` |
| Window state | `pwsh scripts/ci/shadow-window-status.ps1` (default) vs `-Since '2026-09-04T22:13:10Z'` |
