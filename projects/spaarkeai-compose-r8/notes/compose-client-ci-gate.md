# Task 018 — `compose-client-gate`: putting the Compose client suite in CI

> Completed 2026-08-20. Rigor FULL, model tier opus @ xhigh.
> Client + CI only. No BFF change → no publish-size measurement applies.
> Baseline commit for every measurement below: `eeac5e0c1` (task 012), working tree otherwise clean.

---

## The one-line result

`Spaarke.Compose.Components` had **51 of 90 jest suites runnable and 802 tests executing**, in no CI job
at all. It now has **90 of 90 suites runnable and 1,103 tests executing**, in a self-contained
`compose-client-gate` job, with **six consecutive byte-identical green runs across six different suite
execution orders** at the job's concurrency — landed **ADVISORY**, because all six were on a developer
Windows box and the job has never executed once on `ubuntu-latest`.

---

## Step 1 — baseline, measured not assumed

All runs from `src/client/shared/Spaarke.Compose.Components`.

| # | Condition | Command | Suites | Tests |
|---|---|---|---|---|
| 1 | HEAD, sibling `dist/` absent (except `Spaarke.Auth`, stale local artifact) | `npx jest --runInBand` | **39 failed / 51 passed** of 90 | **2 failed / 800 passed** of 802 |
| 2 | Sibling `dist/` built, before any fix | `npx jest --runInBand` | 8 failed / 82 passed of 90 | 58 failed / 1045 passed of 1103 |
| 3 | Final (fix applied), CI concurrency | `npx jest --ci --coverage --maxWorkers=2` | **0 failed / 90 passed** | **0 failed / 1103 passed** |
| 4 | Final, serial | `npx jest --runInBand` | 0 failed / 90 passed | 0 failed / 1103 passed |
| 5 | Final, sibling `dist/` deliberately removed (fresh-clone simulation) | `npx jest --ci --maxWorkers=2` | 52 failed / 38 passed | 0 failed / 626 passed |

**Runnable-suite count: 51 → 90. Tests executed: 802 → 1,103 (+301).**

Every one of the 39 baseline suite failures had exactly one cause — `Cannot find module
'@spaarke/ui-components'` (76 occurrences in the log). None were test logic. The "~39 unrunnable suites"
carried over from task 010 is therefore **entirely resolution**, not latent breakage — but making them
runnable exposed real breakage underneath, which is row 2 above and the subject of the next two sections.

Row 5 is the honest cost line: see "What this change costs" below.

---

## Step 2 — the resolution decision: dist build, NOT `moduleNameMapper`

The POML framed this as a choice between building sibling `dist/` in CI (the `client-quality` recipe) and
`moduleNameMapper` entries to `src` (the precedent `jest.config.js` already sets for `@spaarke/ai-widgets`).
Task 012 hypothesised the mapper route. **It was tested and it does not work.**

### The mapper experiment, and why it failed

Added mappers for `@spaarke/ui-components`, `@spaarke/auth` and `@spaarke/document-operations` → their
`src`, mirroring the existing `@spaarke/ai-widgets` entries. Result: 37 failed / 53 passed suites,
828 passing tests — a small improvement, then a hard wall: **72 × `Cannot find module
'@fluentui/react-icons'`**.

The mechanism is a package-boundary violation, not a config detail. Mapping `@spaarke/ui-components` to its
`src` moves resolution of **its own** dependency graph into the consumer's `node_modules`. Ten of its
runtime dependencies are absent there, and correctly so — Compose does not declare them and should not:

`lexical` · `@lexical/react` · `@hello-pangea/dnd` · `react-window` · `d3-force` · `pdfjs-dist` ·
`dompurify` · `diff` · `marked` · `@microsoft/applicationinsights-web` · `@spaarke/sdap-client`

Making the mapper work would mean adding ~11 dependencies to `Spaarke.Compose.Components/package.json` that
it never imports, coupling its install to `Spaarke.UI.Components`' internal implementation choices. That is
a worse outcome than a build step. The mapper stays correct for `@spaarke/ai-widgets` (a subpath import with
no comparable dependency tail) and is unchanged.

**Chosen: the dist route.** It is the contract the package already declares (`jest.config.js` header:
"`@spaarke/*` runtime deps resolve via node_modules → their built `dist/`"), and the recipe already exists
in `client-quality`. Build order, verified locally:

```
Spaarke.Auth → Spaarke.SdapClient → Spaarke.UI.Components → Spaarke.DocumentOperations
```

(`Spaarke.UI.Components`' tsconfig path-maps `@spaarke/sdap-client` to `../Spaarke.SdapClient/dist/index.d.ts`;
`Spaarke.DocumentOperations` depends on `@spaarke/auth`. `Spaarke.AI.Context` and `Spaarke.AI.Widgets` are
NOT needed — the latter is mapped to `src`.) All four `npx tsc` builds are clean, zero errors.

**`jest.config.js` change**: comment-only. The mapper table is byte-identical (7 keys before and after,
verified by loading the module). What was added is the resolution contract + the no-`virtual` rule that the
test files now cite.

---

## Step 2b — the actual determinism defect: `{ virtual: true }`

With the dists built, all 90 suites ran and **8 failed (58 tests)**. Every one of them passed when run in
isolation. That is the definition of the problem this task exists to solve, so it got root-caused rather
than retried.

**7 of the 8 failing suites were exactly the 7 files in the package carrying
`jest.mock('@spaarke/...', factory, { virtual: true })`.** There are no others.

`virtual: true` registers the specifier in jest's **resolver**, which is shared by every suite a worker
runs — not in the per-suite module registry, which is reset. One suite's virtual registration therefore
changes how a **later** suite in the same worker resolves the same specifier. A single-suite run can never
reproduce it, and because jest orders files by size/timing, **adding a test file reorders the run and can
surface or hide it** (task 012 hit this from the other direction: its new suite's virtual `@spaarke/auth`
mock made `useComposeWordShuttle.test.tsx` fail with `AuthError: Auth not initialized` sourced from the
real `Spaarke.Auth/dist`).

This is not a new discovery in this repo — five suites in this same package already document it per-file for
`@spaarke/ai-widgets/events` ("a virtual mock is keyed to the raw specifier and gets bypassed in a shared
`--runInBand` registry": `ComposeWorkspace.browse` / `.search` / `.upload` / `.imports`,
`hooks/usePendingRedline`). The `@spaarke/*` sibling mocks were simply the instances nobody had reached yet,
because the suites carrying them could not run.

**Fix: all 16 `{ virtual: true }` arguments removed** (7 files). The flag existed only so a suite could run
without the sibling `dist/`; the gate now builds those dists, so it is both unnecessary and harmful. The
rule is written into `jest.config.js` so the next author does not re-add it. No test was deleted, skipped or
weakened — the same 1,103 assertions run.

The 8th failing suite, `ComposeWorkspace.bornInEditorSave.test.tsx`, failed for a different reason covered
in the next section.

---

## Step 3 — the two `renderOnSave` failures: root cause

**Root cause: commit `cdb1dbcb4` (2026-08-18, "UAT-03 name prompt on first save") changed the product and
did not update the tests.** Confirmed by reading `ComposeWorkspace.saveNeedsName` and by
`git log -S` on the changed predicate; that commit touched exactly two files
(`ComposeBannerStack.tsx`, `ComposeWorkspace.tsx`) and no test.

Before UAT-03, only a born-in-editor "Untitled" draft was prompted for a name. After it:

```ts
// UAT-03 (owner 2026-08-18): prompt for a name on the FIRST save of ANY new-to-system document
return !ref.speDriveItemId && !ref.sprkDocumentId;
```

`requestSave` opens `ComposeSaveNameDialog` and **returns without posting**. The two failing tests
(`renderBornInEditor()` and `renderUploadMount()`) both mount a never-persisted document, clicked Save, and
asserted a POST that by design cannot happen until the modal is confirmed — `saveRequests` stayed `[]`. The
sibling PDF tests kept passing because they mount with `sprkDocumentId: 'sprk-doc-1'`, and the `forkNew` test
kept passing because it already drives the modal inline.

**This is a test bug, not a create-on-save defect. The POML's second escalation trigger does NOT fire.** The
behavior change is deliberate, owner-decided, and documented in-code — including the specific part that looks
surprising (an uploaded file with a perfectly good filename still prompts): *"Previously an imported/uploaded
file (which carries a real filename) skipped the prompt; FR-02's intent is to prompt on every create-on-save.
The modal is seeded with the current filename so the user confirms or renames rather than being blocked."*

**Fix** (no skip, no quarantine — coverage went **up**): a named `confirmSaveName()` helper in each suite, and
the tests now assert the gate they were blind to —

- the name dialog is present after Save, and `saveRequests` is still empty (nothing posts before confirmation);
- the confirmed name threads through to `displayName` (`'draft.docx'` / `'uploaded.docx'`);
- the **second** save of the now-persisted document does **not** re-prompt (a prompt on every save would be
  the regression).

`ComposeWorkspace.bornInEditorSave.test.tsx` had the identical defect plus an incomplete
`@spaarke/ui-components` mock (no `FormModal`, added after that suite went dark), which surfaced as
`Element type is invalid ... Check the render method of ComposeSaveNameDialog`. Same fix, plus the missing
stub.

> Worth stating plainly, because it is this project's thesis in miniature: a deliberate product change
> shipped, two tests went red the same day, and nobody saw it for two days — because nothing ran them.

---

## Step 4 — determinism evidence

Command (identical to what the job runs): `npx jest --ci --coverage --maxWorkers=2`, i.e. the package's own
declared `test:ci`, on unchanged code.

| Run | Suites | Tests | Wall time | Per-test signature |
|---|---|---|---|---|
| 1 | 90 passed / 0 failed | 1103 passed / 0 failed | 114.0 s | `14f57e9d4b71678f` |
| 2 | 90 passed / 0 failed | 1103 passed / 0 failed | 86.4 s | `14f57e9d4b71678f` |
| 3 | 90 passed / 0 failed | 1103 passed / 0 failed | 93.5 s | `14f57e9d4b71678f` |

The signature is a SHA-256 over the sorted set of 1,103 `(suite file, full test name, status)` triples from
each run's `--json` output — so this is identity at the individual-test level, not just matching totals.

**The stronger evidence is that the execution ORDER differed in all three runs** (jest reorders from its
timing cache), and the results were identical anyway:

```
run1 first 5: saveLifecycle, saveErrorRouting, saveOpLogPreservation, saveLifecycleDirty, ComposeFormatToolbar
run2 first 5: saveErrorRouting, saveLifecycle, usePendingRedline, ComposeAiToolbar, ComposeFormatToolbar
run3 first 5: usePendingRedline, ComposeFormatToolbar, saveErrorRouting, useComposeToolbarActivation, redline-from-ledger
```

Three *identical-order* runs would have proven much less. Three *different* orders is the direct test of the
order-dependence this task had to eliminate. A serial `--runInBand` run — the order under which the 8
failures originally appeared — is also green.

Runs 4–6 repeat all of the above on the final, Prettier-formatted content (formatting was applied after runs
1–3, so the "unchanged code" requirement is honoured against the exact tree being handed over).

| Run | Content | Suites | Tests | Wall time | Per-test signature |
|---|---|---|---|---|---|
| 4 | final (Prettier-formatted) | 90 passed / 0 failed | 1103 passed / 0 failed | 99.9 s | `14f57e9d4b71678f` |
| 5 | final | 90 passed / 0 failed | 1103 passed / 0 failed | 89.5 s | `14f57e9d4b71678f` |
| 6 | final | 90 passed / 0 failed | 1103 passed / 0 failed | 88.1 s | `14f57e9d4b71678f` |

**Six runs in total, six DISTINCT suite execution orders, one identical signature across all six.**
Runs 4-6 alone satisfy the three-green-runs rule on the exact tree being handed over; runs 1-3 satisfy it on
the functionally identical pre-formatting tree. The signature being stable across pre- and post-formatting
content is also the check that the Prettier pass changed nothing but layout.

Closing the loop on task 012's canary — its deterministic repro
(`npx jest --runInBand --runTestsByPath ComposeEditor.saveLifecycleDirty.test.tsx useComposeWordShuttle.test.tsx`,
which produced 2 failures the moment the `@spaarke/auth` mock was made virtual) is now **2 suites / 17 tests,
all passing**, and the workaround task 012 had to apply (mocking `./ComposeAiToolbar` purely to dodge the
leak) is no longer load-bearing — it is kept as belt-and-braces, with its comment corrected to say so.

---

## Step 5/6 — the job, and the blocking/advisory decision

`compose-client-gate` is appended to `.github/workflows/sdap-ci.yml` as a **separate, self-contained job**,
copying `compose-fidelity-gate`'s structure and its stated rationale: `build-test`, `code-quality`,
`client-quality` and `integration-readiness` all carry job-level `continue-on-error: true`, so a step placed
inside any of them would have its failure swallowed; they are also owned by `ci-cd-unit-test-remediation-r1`.
**None of those four jobs was modified** — the diff is a single append hunk at line 799.

Shape: checkout → node 20 → install+`npx tsc` for the four siblings in dependency order → install Compose →
`npm run test:ci -- --json --outputFile=…` → summary/annotation step → artifact upload `if: failure()`.
`npm install --legacy-peer-deps --no-audit --no-fund` throughout; no `npm ci` (root CLAUDE.md §12).

### Decision: **ADVISORY** (`continue-on-error: true`), with a written flip condition

The three-green-runs rule is satisfied twice over *at the job's concurrency* — but not *on the job's
runner*. All six runs were on a developer Windows box; the job targets `ubuntu-latest` and **has never
executed once**. I cannot
push (this run is under an explicit no-commit boundary), so no CI-runner evidence exists.

Marking it blocking on that evidence would be betting a merge gate on a platform I have not observed, for a
suite where 39 of 90 files were dark until today and 8 broke the instant they were lit. If the first red is
an environment artifact, the gate gets ignored, then disabled, and the surface reverts to exactly today's
state — the outcome the POML calls "the one outcome worse than today's". Advisory-plus-honest-report is the
POML's own sanctioned landing for this case.

**It is advisory in verdict only, not in visibility.** A `continue-on-error` job renders a failed step as a
green check with an easy-to-miss warning, so the job writes a pass/fail table to `$GITHUB_STEP_SUMMARY` and
emits an `::error::` annotation naming every failed suite
(`scripts/ci/summarize-jest-results.js`, verified locally against a real green result, a synthetic red one,
and a missing file). The per-suite JSON uploads as an artifact on failure.

**Flip condition, written into the job comment so it cannot quietly become permanent:** after three
consecutive green `compose-client-gate` runs on unchanged code *on this runner*, delete the
`continue-on-error: true` line and record the run ids in the PR. Nothing else about the job changes.

---

## What this change costs (stated, not buried)

Removing `{ virtual: true }` means the 7 suites that carried it now need the sibling `dist/` like the other
83. On a fresh clone with no SharedLibs build, runnable suites go **51 → 38** (row 5 of the step-1 table).
CI is unaffected — the gate builds them — and `scripts/Build-AllClientComponents.ps1 -Component SharedLibs`
is the documented local step.

That trade is worth taking: the 51 "runnable" suites included 7 that were *silently order-dependent*, and
the alternative (keep the flag) is a gate that flakes. 38 honestly-runnable beats 51 with a booby trap. The
underlying DX gap — a package whose tests need a sibling build — is real but is not this task's scope, and
the mapper route that would have closed it is ruled out on package-boundary grounds above.

Second, smaller cost: the suite now loads the real `@spaarke/ui-components` dist in suites that do not mock
it, so wall time rose from ~110 s to ~90–115 s at `--maxWorkers=2` for 38 % more tests. Fine.

---

## `/conflict-check` — run 2026-08-20, before handoff

Scope: `.github/workflows/sdap-ci.yml` across all 24 open PRs.

| PR | Branch | Touches `sdap-ci.yml` | Assessment |
|---|---|---|---|
| **#779** | `work/customer-provisioning-orchestration-r1` | **yes** (+70/−1) | **No textual conflict.** It adds a `tenant-isolation` job before `integration-readiness` (~line 564) and a step inside `code-quality` (~line 499); this task appends at line 799. Both follow the same "separate self-contained job" pattern and cite the same rationale. They must not be rebased blindly past each other, but they do not overlap. |
| #244 | dependabot `actions/setup-node` 4→6 | yes | Version bump only. This job uses `actions/setup-node@v4`, matching `client-quality` today; dependabot rewrites all occurrences when it lands. No action. |
| #203 / #202 | dependabot `upload-artifact` 6→7 / `download-artifact` 7→8 | yes | Same. This job uses `@v6`, matching `compose-fidelity-gate` today. |
| #806 | this branch (draft) | no | — |
| all others (20) | — | no | — |

`projects/INDEX.md`: the `spaarkeai-compose-r8` row **already declared `CI Workflows: Y`** at project
initialization. Confirmed against the header row (columns: BFF · SpaarkeAi · CI Workflows · Skill Directives
= Y · Y · Y · N). The row's narrative was updated to name the new job, record the advisory posture, and note
the #779 overlap; last-commit date moved to 2026-08-20.

---

## Issue to file

Not filed from this run (no GitHub write was permitted). File it verbatim.

**Title**

```
`Spaarke.Compose.Components` declares an ESLint script but ships no ESLint config
```

**Body**

```markdown
## What

`src/client/shared/Spaarke.Compose.Components/package.json` declares:

    "lint": "eslint src --ext .ts,.tsx",
    "lint:fix": "eslint src --ext .ts,.tsx --fix",

and has `eslint@^9.17.0` in `devDependencies` — but the package contains **no**
`eslint.config.js` / `.mjs` / `.cjs` and no `.eslintrc.*`. Both scripts fail immediately:

    $ node_modules/.bin/eslint src --ext .ts,.tsx
    Oops! Something went wrong! :(
    ESLint: 9.39.4
    ESLint couldn't find an eslint.config.(js|mjs|cjs) file.

Verified 2026-08-20 on `work/spaarkeai-compose-r8` @ `eeac5e0c1`.

## Why it matters

Root Prettier (`client-quality` job) covers formatting for `src/client/**/*.{ts,tsx}`, so this is a
lint-rule gap, not a formatting gap: no `no-floating-promises`, no `react-hooks/exhaustive-deps`, no
`no-unused-vars` on ~90 source and test files in the package that owns the Compose save path.

The sibling `Spaarke.UI.Components` has `eslint.config.js` and is the obvious template.

## Scope / suggested fix

- Add `eslint.config.js` modelled on `src/client/shared/Spaarke.UI.Components/eslint.config.js`.
- Expect a non-zero starting warning count; follow the repo precedent of NOT passing `--max-warnings 0`
  (see the `ESLint check` step comment in `.github/workflows/sdap-ci.yml`).
- Once it runs clean enough to be meaningful, consider adding a lint step to the `compose-client-gate`
  job — it already installs this package.

## Provenance

Explicitly scoped OUT of `spaarkeai-compose-r8` task 018 (`018-compose-client-ci-gate.poml`, constraint:
"The package's missing ESLint config is explicitly OUT of scope — root Prettier already covers these
files. File it as a GitHub issue instead.").
```

Labels: `tech-debt`, `client`, `tooling`. No milestone.

---

## Verification standard used, and what I could NOT verify

Kept from task 012: every number here was produced by a command in this worktree, baselines were measured
rather than quoted, and the fixes were checked against the failing state before being called fixes.

**Not verified — read these as open:**

1. **The job has never run on `ubuntu-latest`.** Everything above is Windows + Node 22.14 local; the job
   pins Node 20 on ubuntu. Platform-specific breakage (path casing, `npx tsc` on a case-sensitive
   filesystem, jsdom timing) would not have shown up here. This is the whole reason the gate lands
   advisory.
2. **The four sibling `npm install`s were not run from a clean cache**, and their lockfiles were already
   present. A cold CI install could resolve differently.
3. **No `dotnet` command was run** (deliberate — the main session was building the BFF concurrently, and
   the obj/bin locks would collide). Nothing in this change touches server code.
4. **The main session was editing `ComposeWorkspace.tsx`'s neighbourhood concurrently** (task 014, BFF-side).
   `git status` was clean at start and my measurements were taken against `eeac5e0c1`, but a full re-run
   after all of today's tasks merge is the only way to confirm the interaction.
5. **The 8 suites that were dark and are now lit were fixed to the contract as it exists today.** They were
   dark for an unknown period; if any of them encoded an intent that the product has since deliberately
   moved away from (as the UAT-03 pair did), the fix records today's behavior. That is the right default,
   but it is a default, not a proof.

---

## Files changed

| File | Change |
|---|---|
| `.github/workflows/sdap-ci.yml` | **NEW `compose-client-gate` job** appended (single hunk at line 799). Separate + self-contained; SharedLibs dist build in dependency order; `npm run test:ci`; summary/annotation step; artifact on failure. Advisory with a written flip condition. No existing job touched. |
| `scripts/ci/summarize-jest-results.js` | **NEW** — renders a jest `--json` file as a GitHub job summary + `::error::` annotation. Exists so an advisory gate's red is legible; §11 justification in its header. Verified against green / red / missing inputs. |
| `src/client/shared/Spaarke.Compose.Components/jest.config.js` | Comment-only. Documents the sibling-resolution contract (dist for `ui-components`, why the `src` mapper was rejected, with the measurement) and the binding no-`{ virtual: true }` rule. Mapper table byte-identical. |
| `.../widgets/ComposeWorkspace.renderOnSave.test.tsx` | UAT-03 name gate: `confirmSaveName()` helper; the two create-on-save tests now assert the gate, the `displayName` threading, and that the second save does not re-prompt. `virtual: true` removed (×3). Header docblock item 7 added. |
| `.../widgets/ComposeWorkspace.bornInEditorSave.test.tsx` | Same UAT-03 fix; `FormModal` added to the `@spaarke/ui-components` mock (missing since FR-02 task 030 — the suite had been dark). |
| `.../widgets/ComposeWorkspace.saveErrorRouting.test.tsx` | `virtual: true` removed (×3) + rationale comment corrected. |
| `.../widgets/ComposeWorkspace.saveLifecycle.test.tsx` | `virtual: true` removed (×3) + rationale comment corrected. |
| `.../widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx` | `virtual: true` removed (×3) + rationale comment corrected. |
| `.../widgets/ComposeEditor.saveLifecycleDirty.test.tsx` | `virtual: true` removed (×2); task 012's two prose blocks corrected (the "with dist built the mock behaves identically" claim was measurably false). |
| `.../widgets/ComposeApplyTemplateDialog.test.tsx` | `virtual: true` removed (×1). |
| `.../widgets/ComposeSaveNameDialog.test.tsx` | `virtual: true` removed (×1). |
| `projects/INDEX.md` | `CI Workflows: Y` confirmed (already declared); narrative updated with the new job + advisory posture + the #779 overlap; last-commit 2026-08-20. |

All eight touched `.tsx` files pass `npx prettier --check` with the repo config.
