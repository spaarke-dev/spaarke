# Task 043 — A CI gate the office-addins jest suite can actually fail

> **Completed**: 2026-09-10 · **Rigor**: FULL · **Model tier**: opus @ high · **Step mode**: directional
> **Files added**: `.github/workflows/office-addins-tests.yml`, `src/client/office-addins/ci-gated-suites.txt`
> **Files modified**: none.
> **Frozen tier files touched: ZERO.** Cost to the shadow window: **0 PRs, 0 days.**

---

## VERDICT

> **Shipped: a standalone, path-filtered, PR-blocking workflow gating the 12 green suites
> (164 tests) of this package's 22 — including both surfaces this task existed to protect.**
>
> It is **proven green** on a from-scratch install (12/12, 164/164, exit 0) and **proven
> able to fail**: re-seeding the actual 2026-09-03 UAT defect turns it red (exit 1, 9 tests).
>
> No escalation trigger fired. No test was deleted, skipped, or modified.

---

## 1. The problem, restated correctly

Task 010 reported *"no CI job runs this suite at all."* That was wrong and the correction matters,
because it changes the fix.

`.github/workflows/client-tests.yml` **auto-discovers** every package declaring a real jest `test`
script by scanning `package.json` files. `src/client/office-addins/package.json` declares
`"test": "jest"`. It **is** discovered and it **is** executed. Two properties make that execution
unable to protect anything:

1. Its only triggers are `schedule` (nightly, 07:00 UTC) and `workflow_dispatch`. No `push`,
   no `pull_request` — it never runs on the PR that breaks something.
2. Every matrix leg is `continue-on-error: true`. A red package is **recorded**, not enforced.

So the accurate statement is: **the suite ran nightly and could never fail a PR.** The break is
recorded the next morning, on master, after it merged.

**What was unprotected, concretely.** `WordAdapter.test.ts` holds nine assertions pinning the
`.docx` binary save path onto `getFileAsync(Office.FileType.Compressed)` slice assembly and off
`body.getOoxml()` — the literal 2026-09-03 UAT defect, where flat OOXML *text* was returned where
a zip container was required, so SPE could not preview it and Word could not open it. One of them
is named `never falls back to body.getOoxml() for the default format`. Alongside it,
`useAnnounce.test.ts`'s 19 tests pin an accessibility regression (task 018). A PR reverting either
merged green.

*(Note on task 010's 36-case differential: that harness — `zz-task010-parity.test.ts` — was a
throwaway, written to run both implementations in one process while the reference still existed,
and deleted with the reference per `notes/010-adapter-consolidation.md` §2. It is not in the repo
and cannot be gated. What survives as the durable guard is the nine-assertion `.docx binary path
(FR-04)` block inside `WordAdapter.test.ts`, and that is what this gate protects.)*

---

## 2. Extend `client-tests.yml`, or add a narrow workflow? — the §11 decision

**Decision: a narrow new workflow.** Root CLAUDE.md §11 three-question template:

**1. Existing — what does this overlap with?**
`client-tests.yml` (40 packages, nightly, advisory, `continue-on-error` on every leg, has a
`package_filter` dispatch input). Verified by reading it in full; there is no other workflow that
runs any jest anywhere. `css-reset-gate.yml` overlaps in *shape* but not in subject.

**2. Extension — can I extend the existing instead?**
No — not without changing what that file *is*, which the task constraint forbids and which the
file's own header forbids more explicitly than the constraint does. Three separate blockers:

| To gate from inside `client-tests.yml` I would have to… | …which breaks |
|---|---|
| add a `pull_request` trigger | its header's stated reason for not having one: *"40 packages with no npm workspace root means 40 independent `npm install`s. Hanging that off every PR would contend for runners with the very PRs the CI shadow window is trying to accumulate."* The shadow window is **still open and still short** (5/20 post-#944). |
| make the office-addins leg blocking while 39 stay advisory | its header's invariant: *"this workflow is advisory by construction: every step is continue-on-error and it appears in no blocking filter."* A matrix where one leg blocks is no longer advisory by construction — it is advisory by allow-list, a weaker and more easily-broken property. |
| — anything at all, really | its header's **promotion path**: *"Promotion into Tier 2 as a real gate happens only AFTER the shadow window closes AND the baseline is green."* Neither precondition holds. The file names its own promotion gate and I do not clear it. |

The `package_filter` input does not rescue this. It is a `workflow_dispatch` input; it does not
exist on `pull_request`, and the matrix is built by discovery rather than by changed paths, so
plumbing "which paths changed → which packages to run" into the discover job would be new logic
inside the very file whose character is being protected.

**3. Cost-of-doing-nothing — name a concrete failure.**
Not abstract: a PR restoring `getDocumentContent()`'s default format to `body.getOoxml()` merges
green today. That exact change shipped once already (found in UAT on 2026-09-03) and was fixed in
task 010. §5 below shows the seeded re-introduction passing CI-as-was and failing CI-as-shipped.

**Why a new workflow is nonetheless not scope creep.** It is not a new *pattern* — it is the
repo's existing narrow-gate pattern, applied again. `css-reset-gate.yml` is the same shape: one
regression class, `pull_request` + `push: master`, path-filtered, blocking, `concurrency` with
cancel-in-progress, Node 20, `checkout@v6` / `setup-node@v6`, living outside the tier files. This
workflow was written against it deliberately, down to the trigger block layout.

**What was NOT touched, and why** — all verified in §7:
- `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml`: frozen under the
  shadow-comparison window, which additionally holds the unresolved measurement latch task 044
  escalated (§6). Editing one restarts the cutover clock on an already-stuck measurement. This
  task had no authorization to touch them and did not need it — **escalation trigger 1 did not
  fire**, because the gate never required a tier file.
- `sdap-ci.yml`: legacy, pending retirement.
- `client-tests.yml`: unchanged, and **it keeps its job.** It is now the *ratchet signal* for this
  gate (§4) rather than a workflow this gate replaces.

---

## 3. Handling the 10 red suites — the explicit decision

**The measured state (this branch, `c525f3276`, 2026-09-10).** Full-package run:
**22 suites — 12 pass, 10 fail; 380 tests — 296 pass, 84 fail.**

| | Suite | Tests |
|---|---|---|
| ✅ | `shared/adapters/__tests__/HostAdapterFactory.test.ts` | 12/12 |
| ❌ | `shared/adapters/__tests__/OutlookAdapter.test.ts` | 11/29 |
| ✅ | `shared/adapters/__tests__/WordAdapter.test.ts` | **37/37** |
| ❌ | `shared/services/__tests__/ApiClient.test.ts` | 19/20 |
| ❌ | `shared/taskpane/components/__tests__/EntityPicker.test.tsx` | 28/32 |
| ✅ | `shared/taskpane/components/__tests__/LinkedTodosBanner.test.tsx` | 21/21 |
| ❌ | `shared/taskpane/components/__tests__/SaveFlow.test.tsx` | 16/25 |
| ✅ | `shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx` | 9/9 |
| ❌ | `shared/taskpane/components/__tests__/TaskPaneNavigation.test.tsx` | 6/7 |
| ❌ | `shared/taskpane/components/__tests__/TaskPaneShell.test.tsx` | 5/10 |
| ❌ | `shared/taskpane/components/views/__tests__/SaveView.test.tsx` | 4/26 |
| ❌ | `shared/taskpane/components/views/__tests__/ShareView.test.tsx` | 4/20 |
| ✅ | `shared/taskpane/hooks/__tests__/useAnnounce.test.ts` | **19/19** |
| ✅ | `shared/taskpane/hooks/__tests__/useCreateTodoFromEmail.test.ts` | 7/7 |
| ❌ | `shared/taskpane/hooks/__tests__/useEntitySearch.test.ts` | 20/21 |
| ✅ | `shared/taskpane/hooks/__tests__/useLinkedTodosForCommunication.test.ts` | 16/16 |
| ❌ | `shared/taskpane/hooks/__tests__/useSaveFlow.test.ts` | 19/26 |
| ✅ | `shared/taskpane/hooks/__tests__/useTheme.test.ts` | 9/9 |
| ✅ | `shared/taskpane/services/__tests__/communicationLookupService.test.ts` | 11/11 |
| ✅ | `shared/taskpane/services/__tests__/communicationSuggestionsService.test.ts` | 8/8 |
| ✅ | `shared/taskpane/services/__tests__/createTodoLauncher.test.ts` | 11/11 |
| ✅ | `shared/taskpane/services/__tests__/quickSaveHelpers.test.ts` | 4/4 |

**The decisive fact: both surfaces this task exists to protect are already green.**
`WordAdapter.test.ts` 37/37 and `useAnnounce.test.ts` 19/19. The gate does not need to wait on
anything.

**Options considered:**

| Option | Rejected / chosen | Why |
|---|---|---|
| Gate the whole suite | ❌ | Red the moment it is created. A check that is always red is a check everyone learns to route around, and a merge queue that trains people to click past a red X is worse than no gate. The task constraint forbids it outright. |
| Recorded allowed-failure list (gate everything, tolerate the 10 named failures) | ❌ | Inverts the safety property. It runs the flaky suites and then has to *decide* whether their red is the allowed one, so the gate's verdict depends on parsing failure identity out of a report — the most fragile thing it could depend on. It also runs `ShareView` (187 s) and `SaveFlow` (75 s) on every PR for a result that is discarded. |
| Fix the 10 first | ❌ | This is 84 failing tests across mock/assertion defects in six RTL component suites and two hook suites. It is a project, not a step in a CI task — and the POML names absorbing it as **escalation trigger 2**. Reported, not absorbed: see §8. |
| **Allow-list of green suites, ratcheting** | ✅ | Green on day one, protects both target surfaces plus ten more, and grows monotonically. |

**Nothing is deleted, skipped, or modified (ADR-038).** The 10 red suites still run, unchanged,
every night in `client-tests.yml`. This gate decides **what blocks a merge**, not what runs. That
distinction is the whole reason the allow-list is admissible rather than a coverage cut.

**The anti-drift mechanism.** The allow-list lives in `src/client/office-addins/ci-gated-suites.txt`
and the workflow **fails if any listed path no longer exists**. So a gated test cannot be renamed
away or deleted to make the gate pass — proven in §5b. Precedent for a checked-in registry that CI
consumes: `tests/.reliability-registry.json` (ADR-038 §2a), which has the same "membership
describes the file as it exists today, not a permanent label" contract.

---

## 4. The ratchet — and why it costs nothing

The obvious weakness of an allow-list is drift: a suite gets fixed, nobody promotes it, the gate
silently stops growing. The obvious fix — have the gate also run the ungated suites and report
which now pass — would put `ShareView`'s 187 s and `SaveFlow`'s 75 s on every PR for a
non-blocking report.

That report **already exists**. `client-tests.yml` runs this package's full 22-suite surface every
night and publishes a per-package pass/fail table. So the promotion signal is free: when the
nightly baseline shows one of the 10 green, add it to `ci-gated-suites.txt` in the PR that fixed
it. That instruction is written into the manifest header and the workflow's job summary.

This is why `client-tests.yml` had to survive unchanged and un-extended. The two workflows are
complements:

```
client-tests.yml   40 packages · nightly · advisory · sees everything  → the ratchet signal
this file           1 package  · per-PR  · BLOCKING · sees one subset  → the gate
```

Extending the first into the second would have destroyed the thing that makes the second
maintainable.

---

## 5. Proof — the gate is green, and the gate can fail

Local runs used `npx jest` / `npm test --` with the same flags the workflow uses.

### 5a. Green at creation, including on a from-scratch tree

| # | Environment | Result |
|---|---|---|
| 1 | worktree `node_modules` | 12/12 suites, **164/164 tests**, exit 0, 10.7 s |
| 2 | worktree `node_modules` | 12/12, **164/164**, exit 0, 29.3 s |
| 3 | worktree `node_modules` | 12/12, **164/164**, exit 0, 16.3 s |
| 4 | **from-scratch `npm install --legacy-peer-deps --no-audit --no-fund`** on a tree built by `git archive HEAD` (no `node_modules`, no `dist/`) | 12/12, **164/164**, exit 0, 15.8 s |
| 5 | as #4, driving the workflow's **actual manifest-resolution + run steps** verbatim | 12/12, **164/164**, exit 0, 14.0 s |

Runs 1–3 were compared **per test, not per count**: dumping every `fullName :: status` sorted gave
byte-identical output across all three (`diff` clean, 164 lines each).

**Run #4 earned its place.** On the first attempt it failed 1 of 12 —
`communicationSuggestionsService.test.ts` could not resolve
`@spaarke/communication-components/logic/connections/provenance`. That was an artifact of my
partial archive (I had extracted only `office-addins` + `Spaarke.Auth`), not a repo defect: real
`actions/checkout` takes the whole repo, so the file is present. But it surfaced a genuine gap in
the *path filter*, which §6 acts on. After adding the third package's one mapped file to the
archive, run #4 passed 164/164.

Run #4 also settles a design question empirically: **the gate needs no build step.**
`jest.config.js` maps `@spaarke/auth` to the sibling package's TypeScript *source* (ts-jest
transforms it) specifically to sidestep the ESM/CJS mismatch in its compiled `dist/`. So unlike
`deploy-office-addins.yml`, which must run `npm run build` in `Spaarke.Auth` first, this workflow
does not — 164/164 with no `dist/` anywhere. That also means the gate needs none of
`ADDIN_CLIENT_ID` / `TENANT_ID` / `BFF_API_CLIENT_ID` / `BFF_API_BASE_URL`, since it never invokes
webpack.

### 5b. The gate fails when it should — two seeded breaks

**Break 1 — re-introduce the 2026-09-03 UAT defect.** On the from-scratch tree, edit
`shared/adapters/WordAdapter.ts:232` so `getDocumentContent()`'s default `'ooxml'` format falls
through past `getCompressedFile()` to the `body.getOoxml()` branch — the exact shape of the
shipped defect.

```
Test Suites: 1 failed, 11 passed, 12 total
Tests:       9 failed, 155 passed, 164 total
GATE EXIT=1
```

All nine failures are in `getDocumentContent — .docx binary path (FR-04)`, including
`● … › never falls back to body.getOoxml() for the default format`,
`● … › returns a buffer beginning with the PK zip signature 0x50 0x4B 0x03 0x04`, and
`● … › requests Compressed with a 65536-byte slice size`. Reverted afterward.

**Break 2 — delete a gated test.** Rename `useAnnounce.test.ts` → `useAnnounce.spec.ts` (a
rename is the *kinder* case; an outright delete behaves identically). The manifest step:

```
::error file=src/client/office-addins/ci-gated-suites.txt::Gated suite
'shared/taskpane/hooks/__tests__/useAnnounce.test.ts' does not exist.
MANIFEST STEP EXIT=1
```

Note that jest alone would **not** have caught this: `--runTestsByPath` on a missing path is not
by itself an error, and the suite would simply have stopped being gated. The existence check is
what makes ADR-038's "no deleting tests to go green" mechanically true here rather than aspirational.

---

## 6. The flakiness — escalation trigger 3 did **not** fire, and here is why

The task brief warned that this package's aggregate counts drift run-to-run (task 018 saw two runs
on identical code differ by 3 tests), and set the bar: *a flaky gate is worse than no gate.*

The drift is real and much larger than 3 tests. Two runs preserved from task 017/018
(`jest-after-run1.log`, `jest-after-run2.log`, same commit) report **83 total tests** and **329
total tests** respectively — a 246-test swing.

**But the drift is entirely an artifact of the red suites, and it is structural, not mysterious.**
When a suite dies at load or blows the 10 s per-test timeout, jest never enumerates its remaining
tests, so they vanish from the *denominator*. Under runner load the six RTL component suites — the
ones that dominate wall time (`ShareView` 187 s, `SaveFlow` 75 s, `SaveView` 38 s, `TaskPaneShell`
30 s, `EntityPicker` 22 s) — bail at different points on each run. That is where the count moves.
Those six are all in the ungated 10.

Confirmed directly rather than argued: runs 1–3 of the **gated set** produced identical results
per individual test name, three times running (§5a), and run #5 on a cold tree agreed. Wall time
10–30 s, so there is little load-induced timeout pressure left to be nondeterministic about.

**A caveat stated honestly.** Two gated suites are RTL component suites of the same family as the
flaky ten — `LinkedTodosBanner` (21 tests) and `TaskPaneHeader` (9). They were stable across all
five runs here, including a cold-install run where they took 63 s and 57 s before npm/ts caches
warmed. If either ever flakes in real CI, the correct response is to **remove that one line from
`ci-gated-suites.txt`** with a note — the manifest exists precisely so that costs one line and
loses nothing else. Neither `WordAdapter.test.ts` nor `useAnnounce.test.ts` — the two suites this
task exists for — is in that family.

---

## 7. Acceptance criteria

| Criterion | Status |
|---|---|
| A PR touching `src/client/office-addins/**` runs the suite as a check that CAN fail — demonstrated, not asserted | ✅ `on: pull_request` + no `continue-on-error` anywhere in the job; **and** demonstrated by seeding the real defect → exit 1 (§5b) |
| `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `sdap-ci.yml` unmodified — verified by `git diff --name-only` | ✅ All four byte-identical to **both** `HEAD` and `origin/master`. `git status --porcelain` shows exactly two untracked additions and zero modifications |
| `client-tests.yml`'s other 39 packages remain non-blocking; no unrelated package became a gate | ✅ `client-tests.yml` unmodified (same check). The new workflow's matrix is one job over one package |
| Path-filtered to `src/client/office-addins/**` + `src/client/shared/Spaarke.Auth/**`, matching `deploy-office-addins.yml` | ✅ Both present, copied verbatim with the deploy file's own comment. **Plus one path the deploy file lacks** — deviation justified in §8 (F-1) |
| Handling of the 10 red suites explicit and justified in notes; gate GREEN on the current branch at creation | ✅ §3 (decision + rejected alternatives) and §5a (5 runs, incl. from-scratch, all 164/164 exit 0) |
| `actionlint` passes on any workflow added or changed | ✅ actionlint **1.7.7**, `-color -shellcheck=` (the exact flags `workflows-validate.yml` uses): exit 0 on the new file, and exit 0 **repo-wide** |
| Shadow-window false green recorded as a separate finding with its PR number, NOT diagnosed here | ✅ §8 (F-3). PR **#934**. Diagnosis belongs to task 044 (`notes/044-false-green-diagnosis.md`); this task neither re-opened nor perturbed it |

---

## 8. Findings surfaced, not absorbed

**F-1 — `deploy-office-addins.yml`'s path filter has a hole. Reported, not fixed.**
`src/client/office-addins/webpack.config.js:101` aliases
`@spaarke/communication-components/logic/connections/provenance` at
`src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts`, and
`shared/taskpane/services/communicationSuggestionsService.ts:7` imports it — so that file **ships
inside the add-ins**. `deploy-office-addins.yml` triggers only on `office-addins/**` and
`Spaarke.Auth/**`, so editing `provenance.ts` alone changes the add-in's behavior **without
redeploying it**. This is the identical failure mode the `Spaarke.Auth` path was added to prevent.

*This gate closes the hole on its own side* — the third path is in its trigger, for the same
reason and with the same rationale, and `jest.config.js` maps the same module. `provenance.ts` has
no imports of its own, so the exact file is the precise filter. **Fixing the deploy workflow is
not this task's scope** (different workflow, different blast radius, deploy-trigger semantics) and
is left as a one-line change for whoever owns it next.

**F-2 — the 10 red suites (84 failing tests). Scope reported, not absorbed — escalation trigger 2,
handled per its own instruction.** `OutlookAdapter` (18 failing), `SaveView` (22), `ShareView`
(16), `SaveFlow` (9), `useSaveFlow` (7), `TaskPaneShell` (5), `EntityPicker` (4), `ApiClient` (1),
`TaskPaneNavigation` (1), `useEntitySearch` (1). Pre-existing mock/assertion defects; several of
the RTL ones present as 10 s per-test timeouts rather than assertion failures, which usually means
a promise that never settles under the current mock, not a wrong expectation. Sizing this properly
is its own task. The nightly baseline already tracks it, and `ci-gated-suites.txt` already names
each one as a promotion candidate with its current pass ratio.

**F-3 — the shadow-window false green stands, on PR #934.** Recorded here as required, and
**deliberately not touched**. Task 044 diagnosed it (router defect, remediated on master by PR
#944) and escalated the surviving blocker — the measurement latch in
`scripts/ci/shadow-window-status.ps1`, awaiting an operator §6.5 path-B decision. This task
changed no tier file and no CI-comparison input, so the window is exactly as task 044 left it:
**0 PRs and 0 days spent.**

**F-4 — `identity-obj-proxy` is referenced but not installed.** `jest.config.js` maps
`\.(css|less|scss|sass)$` → `identity-obj-proxy`, which appears in neither `package.json` nor
`package-lock.json`. It resolves lazily, so it only detonates if a suite imports a stylesheet —
none currently does, including on the from-scratch tree. Latent: the first CSS import added to
this package's test graph fails with a confusing "could not locate module" rather than anything
about CSS. One `devDependencies` line whenever someone is in there.

**F-5 — `word/` and `outlook/` are outside jest's roots.** `jest.config.js` sets
`roots: ['<rootDir>/shared']`, so the two add-in entry points have no test coverage at all and
this gate cannot change that. The gate protects the shared logic layer, which is where both
target regressions live. Noted so the gate is not mistaken for whole-package protection.

**F-6 — `unified-access-control-r2` has a branch diff on `ci-tier1-blocking.yml`.** Surfaced by
the §0.5 hot-path conflict check (`git diff origin/master...HEAD` across active worktrees). It is
a **frozen** file, on another project's branch. Not this task's file and not this task's call —
flagged for the operator, since it may be another unbudgeted charge against the shadow window.
No conflict with this task: no other active worktree touches `client-tests.yml`,
`office-addins-tests.yml`, or `src/client/office-addins/**`.

---

## 9. Evidence index

| Artifact | Reference |
|---|---|
| Full-package baseline | 22 suites / 12 pass / 10 fail; 380 tests / 296 pass / 84 fail — `jest --ci --json`, this branch |
| Gated-set stability | 3 runs, per-test `fullName :: status` diff clean, 164 lines each |
| From-scratch proof | `git archive HEAD` → `npm install --legacy-peer-deps --no-audit --no-fund` → 12/12, 164/164, exit 0 |
| Workflow-step simulation | manifest step (12 resolved) + run step verbatim → exit 0 |
| Seeded defect | `WordAdapter.ts:232` bypass → 9 failed / 155 passed, exit 1 |
| Seeded deletion | `useAnnounce.test.ts` renamed → manifest step exit 1 |
| actionlint | v1.7.7 `-color -shellcheck=`, exit 0 on the new file and repo-wide |
| Frozen-file check | `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `sdap-ci.yml`, `client-tests.yml` all identical to `HEAD` **and** `origin/master` |
| Narrow-gate precedent | `.github/workflows/css-reset-gate.yml` |
| Shadow-window state | `projects/spaarkeai-word-add-in-r1/notes/044-false-green-diagnosis.md` §5–§6 |
