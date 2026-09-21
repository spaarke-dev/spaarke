# Task 073 — Node toolchain alignment

> Status: **local edits complete, committed; deploy-run verification PENDING** (blocked on a push-coordination
> handoff with the orchestrating session — see "Deploy verification" below). Do not read this note as claiming
> the deploy-run acceptance criterion is closed until that section says so.

---

## 1. The verified matrix (independently re-confirmed 2026-09-21, before any edits)

| Surface | Node | Where | How verified |
|---|---|---|---|
| office-addins gate | **20** | `.github/workflows/office-addins-tests.yml:162`, `:335` | `grep -n "node-version" office-addins-tests.yml` → both lines read `node-version: '20'` |
| nightly client baseline | **20** | `.github/workflows/client-tests.yml:151` | `grep -n "node-version"` → `node-version: '20'` |
| **deploy of what actually ships** | **18** (pre-fix) | `.github/workflows/deploy-office-addins.yml:34` | `grep -n "node-version"` → `node-version: '18'` |
| this desktop | **22.14.0** | — | `node --version` |
| declared engines | `>=18.0.0` (pre-fix) | `src/client/office-addins/package.json:68` | Read file directly |
| `.nvmrc` / `.node-version` | **absent, repo-wide** | — | `Glob **/.nvmrc` and `Glob **/.node-version` from repo root both return nothing outside `node_modules/is-generator-function/.nvmrc` (an unrelated third-party package file, not a repo convention) |

This matches the matrix stated in the task and in `notes/fable-review-2026-09-21.md` §8 exactly — no
discrepancy found on independent re-verification.

## 2. Chosen version: **Node 20**

**Reasoning.** Two of the three CI surfaces (the office-addins gate and the nightly client baseline) already
pin Node 20 and have been running on it — Node 20 is "the version the gate tested" in the task's own framing.
The outlier is the deploy job, still on 18. The binding rule in the task is explicit: *if 20 is chosen, the
deploy job moves up to 20 — not the gate down to 18.* Moving the gate down would touch
`office-addins-tests.yml`, whose LOGIC is owned by task 069 (per this task's own constraint: "only touch it if
the version genuinely needs changing, and then change ONLY the version" — and it does not need changing, it is
already correct). Moving the deploy job up is a single-line, single-file, disjoint-from-069 change. This is
also the lower-risk direction: the gate and nightly baseline have been exercising add-in installs/builds on
Node 20 repeatedly, so Node 20 compatibility with this package's dependency tree is already evidenced; Node 18
compatibility of a NEWER dependency tree has not been re-verified recently.

This task deliberately did **not** consider bumping everything to Node 22 (what this desktop happens to run).
That would require touching `office-addins-tests.yml` (task 069's surface) and `client-tests.yml` in addition
to the deploy job — a materially larger footprint than what this task's constraint and escalation trigger
("if 20 is chosen...") anticipate, and outside this task's scope.

## 3. Changes made

| File | Change |
|---|---|
| `.nvmrc` (new, repo root) | Contains `20` — matches `actions/setup-node@v6`'s `node-version: '20'` convention used by all three CI workflow references. |
| `src/client/office-addins/package.json` | `engines.node` narrowed from `>=18.0.0` to `>=20.0.0 <21.0.0`. |
| `.github/workflows/deploy-office-addins.yml:34` | `node-version: '18'` → `node-version: '20'`. No other line in this workflow changed. |
| `.github/workflows/office-addins-tests.yml` | **Not touched.** Already on Node 20 at both references; task 069 owns its logic per this project's task ownership split. |
| `.github/workflows/client-tests.yml` | **Not touched.** Already on Node 20; not in scope. |

### 3a. Engines narrowing verified empirically, not just asserted

Before the edit, `>=18.0.0` silently permits this desktop's Node 22.14.0 — no warning. After narrowing to
`>=20.0.0 <21.0.0`, ran (read-only, `--dry-run`) from `src/client/office-addins`:

```
npm install --dry-run --legacy-peer-deps --no-audit --no-fund
```

Output:
```
npm warn EBADENGINE Unsupported engine {
npm warn EBADENGINE   package: '@spaarke/office-addins@0.1.0',
npm warn EBADENGINE   required: { node: '>=20.0.0 <21.0.0' },
npm warn EBADENGINE   current: { node: 'v22.14.0', npm: '10.9.2' }
npm warn EBADENGINE }
```

Confirms acceptance criterion 3 ("narrowed so a mismatched local version is a visible warning") on the actual
mismatched machine, not by inspection of the range alone.

### 3b. `actionlint` — passed, run locally against the real binary

`actionlint` is normally absent on this desktop (consistent with the 2026-09-19/21 handoff notes). Downloaded
the same installer CI's `workflows-validate.yml` uses
(`bash <(curl -sSL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)`)
— a cached copy (v1.7.7; CI's script pulls latest, currently 1.7.12 per the download log, so there is a minor
version drift between this local run and what CI will use, noted for honesty) was already present from a prior
session and was reused rather than re-downloading. Ran:

```
actionlint -color -shellcheck= .github/workflows/deploy-office-addins.yml
actionlint -color -shellcheck= .github/workflows/*.yml
```

Both exit **0**, no findings — against the single changed file and against the full workflow set (to catch any
cross-file effect). The dedicated `workflows-validate.yml` "actionlint" check also runs automatically on all
PRs (no path filter) once this branch is pushed, so this will be re-verified by CI itself, on the newer
actionlint release, as part of the PR.

## 4. Deploy verification — **PENDING**, not yet closed

The task's acceptance criterion requires proving the Node-20 deploy config with a REAL `workflow_dispatch` run
and recording the run URL, plus verifying output equivalence (bundle present, manifest version correct, no new
build warnings) — not asserting it from the diff.

**Blocker surfaced and resolved with the orchestrating session before proceeding further**: `workflow_dispatch`
executes the workflow file as it exists on the `--ref` on **origin**, not local uncommitted changes. This
task's instructions say "commit — do NOT push." Those two requirements conflict: no real run can exercise this
change until the commit is on origin. Raised this to `main` (the orchestrating session) rather than either (a)
silently asserting the run would work, or (b) pushing unilaterally — `main` has push authority centralized in
one place because task-062 and task-071 are committing to the same branch concurrently, and a push from this
task would have carried their commits too, unreviewed.

**Resolution agreed with `main`**: this task commits locally and stops. `main` pushes the branch (after
reviewing all three concurrent tasks' commits together) and will notify this task when the push is live. Only
then will `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1` actually exercise this
change, and this note will be updated with: the run URL, its result, and the equivalence check (bundle present,
`word/manifest.xml` version, diffed build-warning count vs. the last known-good Node-18 deploy run). If the
built output differs in any way beyond version strings, the task's escalation trigger fires and this section
will say so explicitly rather than accepting it.

**This criterion is NOT met as of this commit.** Do not read TASK-INDEX/POML status as claiming it is.

## 5. Stale local install — the hazard this task was asked to record

`notes/fable-review-2026-09-21.md` §8 and this project's `current-task.md` both record: as of the review
(2026-09-21, before this task started), this desktop's `src/client/office-addins/node_modules` had mtime
2026-09-04, and `@testing-library/jest-dom` + `@testing-library/user-event` — added 2026-09-09 in `cc318390f`
— were **absent**. Consequence, stated plainly so the next person does not re-trust it: **the five "green"
local runs recorded for this project's task 043 cannot have been produced on this install** — that recorded
local result was either produced on a different (correctly-installed) machine, or is simply wrong. Do not treat
task 043's recorded local green runs as verified without re-deriving them on a fresh install.

**What I observed independently, for completeness — this does not retroactively validate task 043.** At the
point I checked (during this task's execution, 2026-09-21), `node_modules` mtime read as *today*, and both
`@testing-library/jest-dom` and `@testing-library/user-event` were present. `git status` at the start of this
task already showed `src/client/office-addins/package-lock.json` modified (2 deletions) — evidence a real
`npm install` had already run in this worktree, most likely by the concurrently-active agent working on
office-addins test files and `ci-gated-suites.txt` in this same wave (per this task's own dispatch note: "another
agent is concurrently editing `src/client/office-addins/` TEST files and `ci-gated-suites.txt`"), before this
task touched anything. A subsequent read-only `npm install --dry-run` run by this task (documented in §3a)
additionally reported adding two optional platform packages (`fsevents`, `@rollup/rollup-linux-x64-gnu`).
**The point stands regardless of the current install's freshness**: the hazard is about what task 043's
recorded runs could have been produced on AT THE TIME they were recorded, and that has not changed. The next
person should still re-derive any locally-recorded test result rather than trust a note, on this desktop or any
other, without confirming the install that produced it.

## 6. Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | Single Node version chosen + `.nvmrc` exists and matches | ✅ Node 20; `.nvmrc` = `20` |
| 2 | Deploy builds on that version; gate + nightly agree | ✅ config-level (deploy now `'20'`, matching gate/nightly); **build-level proof is the pending step 4 below** |
| 3 | `package.json` engines narrowed to a visible-warning range | ✅ verified empirically (§3a) |
| 4 | Add-in builds successfully via a REAL deploy-workflow run; run URL recorded | ❌ **PENDING** — see §4 |
| 5 | Built output verified equivalent (bundle, manifest version, no new warnings); state how checked | ❌ **PENDING** — see §4 |
| 6 | Stale-`node_modules` hazard + task-043 consequence recorded | ✅ §5 |
| 7 | `actionlint` passes | ✅ §3b (local run; CI's own check will also run on push) |
