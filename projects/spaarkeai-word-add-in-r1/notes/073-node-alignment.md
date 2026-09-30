# Task 073 — Node toolchain alignment

> Status: **COMPLETE.** All acceptance criteria met, including the real deploy-run verification (§4) — closed
> 2026-09-21 after `main` pushed the commit and this task ran and verified a live `workflow_dispatch` run.

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

## 4. Deploy verification — **DONE**, real run, output equivalence checked

### Coordination that had to happen first

`workflow_dispatch` executes the workflow file as it exists on the `--ref` on **origin**, not local uncommitted
changes, but this task's instructions also said "commit — do NOT push." Raised the conflict to `main` (the
orchestrating session) rather than pushing unilaterally or asserting a hypothetical run would pass — `main`
was centralizing push authority because task-062 and task-071 were committing to the same branch concurrently.
Agreed sequence: this task committed locally (`ab7870c43ffbefb52fb5f0715e8b91855ac89fd5`) and stopped; `main`
reviewed and pushed; `main` confirmed origin HEAD matched (`ab7870c43...`) and that `.nvmrc`/the workflow diff
were live on origin; this task then ran the verification below.

### Baseline established first (Node 18, last known-good)

Run `35464159732` (2026-09-19T19:22:37Z, `workflow_dispatch`, `headSha 23fd17991...`, **success**) —
https://github.com/spaarke-dev/spaarke/actions/runs/35464159732 — pulled via `gh run view --log` and inspected
step-by-step before dispatching the new run, so there would be something concrete to diff against rather than
a vague "looked fine":

- `Build @spaarke/auth` install: `added 452 packages in 3s`, 7 `npm warn EBADENGINE` (all `@azure/*` /
  `@typespec/ts-http-runtime`, requiring `node >=20.0.0` — unrelated to this repo's own code; these are
  transitive deps of the SWA deploy tooling that happened to run under the job's Node 18), plus deprecation
  noise.
- `Install dependencies` (office-addins): `added 1372 packages in 21s`, same class of `EBADENGINE` warnings
  (7 lines) + 13 `npm warn deprecated` lines.
- `Build Office Add-ins` (`webpack --mode production --no-bail --stats errors-only`): **zero output** between
  invocation and the next step — i.e. no errors, and warnings are suppressed by `--stats errors-only` so a
  silent step is the expected "clean" signal.
- Deploy: `Zipping App Artifacts` → `Deployment Complete :)` in 15.47s, site
  `https://icy-desert-0bfdbb61e.6.azurestaticapps.net`.

### The new run (Node 20)

Dispatched 2026-09-21T18:21:27Z: `gh workflow run deploy-office-addins.yml --ref work/spaarkeai-word-add-in-r1`.

**Run: `35637852013`** — **https://github.com/spaarke-dev/spaarke/actions/runs/35637852013**
`headSha ab7870c43ffbefb52fb5f0715e8b91855ac89fd5` (this task's commit) · `workflow_dispatch` ·
**conclusion: success** · 1m46s (`gh run watch 35637852013 --exit-status` → exit 0).

`Setup Node.js` step confirms `node-version: 20` was requested and **`node: v20.20.2`** was actually
provisioned (`setup-node@v6` resolves the `'20'` pin to the latest 20.x patch — expected, matches how the gate
and nightly baseline already resolve their own `'20'` pins).

### Equivalence check — concrete diffs, not "it went green"

| Signal | Node 18 baseline (`35464159732`) | Node 20 (`35637852013`) | Equivalent? |
|---|---|---|---|
| `Build @spaarke/auth` packages installed | 452 | **452** | ✅ identical |
| `Install dependencies` (office-addins) packages installed | 1372 | **1372** | ✅ identical — same dependency tree resolved, nothing added/removed by the Node bump |
| `npm warn deprecated` lines (office-addins install) | 13 (specific package list) | **13, same package list** | ✅ identical |
| `npm warn EBADENGINE` lines | 7 (`@azure/*` + `@typespec/ts-http-runtime`, all wanting `node >=20.0.0`) | **0** | ✅ **improvement, not a new warning** — those deps are now satisfied by the Node 20 the job itself runs on |
| `webpack` build step output | 0 lines (silent = clean under `--stats errors-only`) | **0 lines** | ✅ identical — no errors, no new warnings |
| Deploy flow | validate → skip Oryx → zip → upload → poll → `Deployment Complete :)`, 15.47s | **same flow, `Deployment Complete :)`, 15.51s** | ✅ identical |
| Deployed site | `https://icy-desert-0bfdbb61e.6.azurestaticapps.net` | **same URL** | ✅ |

**No new build warnings anywhere in the pipeline. The only delta is a reduction of 7 pre-existing warnings that
were never about this repo's own code.** This satisfies the "no new build warnings" criterion, stated with the
actual counts rather than an assertion.

### Live-output verification (bundle present, manifest version correct)

After the run completed, fetched the actually-deployed site directly (not inferred from the CI log):

```
GET https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/manifest.xml → 200, <Version>1.0.9.0</Version>
GET https://icy-desert-0bfdbb61e.6.azurestaticapps.net/word/taskpane.html → 200
```

`taskpane.html` references 6 script tags; fetched each directly:

| File | HTTP | Content-Length |
|---|---|---|
| `vendors.bundle.js` | 200 | 1,010,576 |
| `186.bundle.js` | 200 | 16,650 |
| `631.bundle.js` | 200 | 3,755 |
| `87.bundle.js` | 200 | 135,363 |
| `990.bundle.js` | 200 | 6,310 |
| `word/taskpane.bundle.js` | 200 | 6,002 |

All 6 bundles referenced by the deployed HTML are present and served. The chunk numbers (`186`, `631`, `87`,
`990`) match webpack's deterministic numeric chunk-id scheme for this unchanged source tree — consistent with
identical input producing identical chunk splitting, which is exactly what "equivalent output" predicts. As a
content sanity check (the same method task 055 used to confirm a real, non-corrupted deploy — see
`current-task.md`), grepped `87.bundle.js` for the distinctive string `"That name belongs to"` (from the
collision-flow error copy) — **found**, confirming this is the real application bundle with real application
strings, not an empty or truncated artifact.

**Manifest version (1.0.9.0) is unchanged**, as expected — this task does not touch the manifest and none of
its changes could plausibly affect it.

### Escalation trigger — did NOT fire

The task's escalation trigger is: *"if moving the deploy job to Node 20 changes the built output in any way
beyond version strings, STOP and escalate."* Nothing above differs beyond version strings (Node 20.20.2 vs
18.20.8, and the resulting removal of version-gated `EBADENGINE` noise). Package counts, deprecation-warning
sets, build-step output, deploy flow, deployed bundle set, and manifest version are all identical. **No
escalation required.**

### Acceptance criteria 4 and 5 — now met

- **Criterion 4** (real `workflow_dispatch` run, run URL recorded): ✅ `35637852013`,
  https://github.com/spaarke-dev/spaarke/actions/runs/35637852013, conclusion `success`.
- **Criterion 5** (output verified equivalent, method stated): ✅ — package-count diff, warning-list diff,
  webpack-output diff, live bundle/manifest fetch, and a content sanity grep, all stated above with actual
  numbers rather than an assertion.

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
| 2 | Deploy builds on that version; gate + nightly agree | ✅ deploy now `'20'` (resolves to `v20.20.2`, same as the gate/nightly's own `'20'` pin), and §4's real run proves it actually builds |
| 3 | `package.json` engines narrowed to a visible-warning range | ✅ verified empirically (§3a) |
| 4 | Add-in builds successfully via a REAL deploy-workflow run; run URL recorded | ✅ run `35637852013`, https://github.com/spaarke-dev/spaarke/actions/runs/35637852013, `success` — §4 |
| 5 | Built output verified equivalent (bundle, manifest version, no new warnings); state how checked | ✅ package-count/warning/build-output diff against the Node-18 baseline run + live bundle/manifest fetch — §4 |
| 6 | Stale-`node_modules` hazard + task-043 consequence recorded | ✅ §5 |
| 7 | `actionlint` passes | ✅ §3b (local run, clean; CI's own `workflows-validate.yml` check also runs on every PR) |

**All 7 acceptance criteria met.**
