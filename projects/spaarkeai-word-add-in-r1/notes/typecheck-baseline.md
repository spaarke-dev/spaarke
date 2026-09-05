# Typecheck baseline — `src/client/office-addins`

> **Task**: 001 (Phase 0) · **Measured**: 2026-09-04 · **Branch**: `work/spaarkeai-word-add-in-r1`
> **Raw artifact**: [`typecheck-baseline-raw.log`](typecheck-baseline-raw.log) — untruncated `npm run typecheck` stdout+stderr
> **Purpose**: replace the unverified "~397" figure with a measurement, and size tasks 006/007/008.

## Headline

| Metric | Value |
|---|---|
| **Total diagnostics** | **395** |
| **Distinct files carrying ≥1 error** | **32** |
| `npm run typecheck` exit code | 2 |
| `npm run build` (production) | ✅ **passes** — but only after a prerequisite (below) |
| `npm test` | 🔴 **fails** — 13 of 21 suites, 57 of 226 tests |
| `npm run lint` | 🔴 **errors out** (exit 2) — globs a directory that doesn't exist |

**These are two different numbers and conflating them is how "~397" lost its meaning**: 395 is the diagnostic count; 32 is the file count.

---

## ⚠️ Environment correction — why the first capture said 396

The first run measured **396**. The committed log measures **395**. Exactly one line differs:

```
shared/services/AuthService.ts(2,71): error TS2307: Cannot find module '@spaarke/auth' or its corresponding type declarations.
```

That error was **environmental, not debt**. `@spaarke/auth` is a `file:../shared/Spaarke.Auth` dependency whose `package.json` points `main` at `dist/index.js`, and `dist/` is gitignored (`Spaarke.Auth/.gitignore:5`). On a fresh checkout it does not exist, so `npm install` links a package with no built output.

**The committed baseline is the post-prerequisite state (395)** — it is the reproducible one. To reproduce:

```bash
cd src/client/shared/Spaarke.Auth && npm install --legacy-peer-deps --no-audit --no-fund && npm run build
cd ../../office-addins            && npm install --legacy-peer-deps --no-audit --no-fund && npm run typecheck
```

`AuthService.ts` had exactly one error, so it drops off the file list entirely — hence 32 files, not 33.

---

## Comparison against the unverified ~397 figure

| | |
|---|---|
| Measured | **395** |
| Claimed | ~397 |
| Ratio | **0.995** |
| Escalation band (factor of 2) | 199 – 794 |
| **Trigger fired?** | ❌ **No** — 395 sits essentially on the claimed figure |

**The count was accurate. The characterization was not.**

Every source repeating the figure describes it as "~397 pre-existing **`exactOptionalPropertyTypes`** errors". That is wrong. The `exactOptionalPropertyTypes` family (TS2375 + TS2379 + TS2412) accounts for **23 diagnostics — 5.8%**. The dominant code is **TS2339 (181, 46%)** — *"Property does not exist on type"* — which is a different and generally harder class of fix.

Plan risk **R-6** ("backlog much larger than ~397 → Phase 0 balloons") **did not materialise on volume**. It materialised on *composition*: tasks 006–008 inherit a different kind of work than the spec implies.

---

## Per-directory rollup (task ownership boundaries)

| Bucket | Owner | Count | Share |
|---|---|---|---|
| `shared/taskpane/**` | **006** | **309** | 78.2% |
| `shared/adapters/**` + `shared/services/**` | **007** | **45** | 11.4% |
| `word/**` + `outlook/**` | **008** | **4** | 1.0% |
| **UNASSIGNED** | 🔴 **nobody** | **37** | 9.4% |
| **Total** | | **395** | 100% |

The split is severely lopsided: 006 carries **69× more** than 008.

### 🔴 UNASSIGNED bucket — escalation trigger 3 FIRED

| File | Count | Note |
|---|---|---|
| `shared/__mocks__/office-js.ts` | 26 | Test scaffolding. Task 007 conditionally absorbs this per its own constraint (confined to `shared/__mocks__/**`). |
| `../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` | **11** | 🔴 **Outside the `office-addins` package entirely.** |

The second file is in a **different shared library** — `src/client/shared/Spaarke.Communication.Components/`. It is pulled into the typecheck via the `@spaarke/communication-components` path alias (`tsconfig.json:27-29`), consumed by `shared/taskpane/services/communicationSuggestionsService.ts`.

**Consequence**: FR-18's acceptance criterion — *"`npm run typecheck` is clean"* — **cannot be met by 006 + 007 + 008 alone.** Eleven errors live in a package this project does not own, consumed by other solutions. Fixing them widens blast radius beyond the add-in.

This is task 001's third escalation trigger verbatim: *"If the UNASSIGNED bucket is non-empty with anything other than files under `shared/__mocks__/**`, STOP and request an ownership assignment before 006-008 are dispatched."*

---

## 🔴 The biggest sizing signal: 75% of the debt is in test files

| Category | Count | Share |
|---|---|---|
| Test + mock files (`__tests__/`, `*.test.*`, `__mocks__/`) | **296** | **74.9%** |
| Production source | **99** | 25.1% |

The five largest contributors are all test files (`SaveView.test.tsx` 61, `SaveFlow.test.tsx` 46, `ShareView.test.tsx` 45, `EntityPicker.test.tsx` 33, `OutlookAdapter.test.ts` 32) — **217 diagnostics, 55% of the total, in five files**.

This collides with **ADR-038**. Tasks 006–008 are typecheck-clearance tasks, but three quarters of what they must clear is test code, and the suite is already red (below). Clearing type errors in tests that are themselves failing is a different exercise from tidying production types — and per ADR-038 the tests must not be deleted or skipped to make the number go down.

**Only 99 diagnostics are in production source.** If FR-18's intent is "make new errors visible during feature work", that intent is satisfiable by fixing ~99 errors, not ~395.

---

## Error-code histogram

| Code | Count | Meaning | Driven by |
|---|---|---|---|
| TS2339 | 181 | Property does not exist on type | — (mostly test doubles / mock shapes) |
| TS6133 | 69 | Declared but never read | `noUnusedLocals` / `noUnusedParameters` (tsconfig:16,17) |
| TS2322 | 40 | Type not assignable | — |
| TS2352 | 24 | Neither type sufficiently overlaps (bad cast) | — |
| TS2375 | 17 | `undefined` not assignable to optional property | **`exactOptionalPropertyTypes`** (tsconfig:21) |
| TS2532 | 14 | Object is possibly `undefined` | **`noUncheckedIndexedAccess`** (tsconfig:20) |
| TS2724 | 7 | No exported member (did you mean…) | — |
| TS2353 | 7 | Object literal may only specify known properties | — |
| TS2345 | 7 | Argument type not assignable | — |
| TS18046 | 5 | Value is of type `unknown` | — |
| TS2379 | 4 | Argument not assignable (optional-property variant) | **`exactOptionalPropertyTypes`** |
| TS2307 | 4 | Cannot find module | — (see module-resolution note below) |
| TS18048 | 4 | Value is possibly `undefined` | **`noUncheckedIndexedAccess`** |
| TS2614 | 3 | No exported member (import-type mismatch) | — |
| TS2412 | 2 | `undefined` not assignable under exactOptional | **`exactOptionalPropertyTypes`** |
| TS7024, TS7022, TS7006, TS6192, TS2694, TS2367, TS2305 | 1 each | implicit-any / circular inference / unused import / namespace / comparison / no-exported-member | mixed |

**Attribution summary**: `exactOptionalPropertyTypes` → **23** · `noUncheckedIndexedAccess` → **18** · `noUnusedLocals`/`noUnusedParameters` → **70** · everything else (**284**) is ordinary type error, not strict-flag fallout.

### Remaining 4× TS2307

Four module-resolution errors survive the `@spaarke/auth` fix. Tasks 006–008 should confirm whether these are further unbuilt `file:` dependencies (same class as the auth one, i.e. environmental) or genuine missing modules, before counting them as debt.

---

## Per-file breakdown

Counts sum to 395.

| File | Errors |
|---|---|
| `shared/taskpane/components/views/__tests__/SaveView.test.tsx` | 61 |
| `shared/taskpane/components/__tests__/SaveFlow.test.tsx` | 46 |
| `shared/taskpane/components/views/__tests__/ShareView.test.tsx` | 45 |
| `shared/taskpane/components/__tests__/EntityPicker.test.tsx` | 33 |
| `shared/adapters/__tests__/OutlookAdapter.test.ts` | 32 |
| `shared/__mocks__/office-js.ts` | 26 |
| `shared/taskpane/components/SaveFlow.tsx` | 19 |
| `shared/taskpane/hooks/useSaveFlow.ts` | 14 |
| `shared/taskpane/components/__tests__/TaskPaneShell.test.tsx` | 13 |
| `shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx` | 12 |
| `../shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` | 11 |
| `shared/taskpane/components/__tests__/TaskPaneNavigation.test.tsx` | 10 |
| `shared/taskpane/App.tsx` | 10 |
| `shared/taskpane/hooks/__tests__/useAnnounce.test.ts` | 9 |
| `shared/adapters/OutlookAdapter.ts` | 9 |
| `shared/taskpane/components/AttachmentSelector.tsx` | 8 |
| `shared/taskpane/hooks/__tests__/useEntitySearch.test.ts` | 6 |
| `shared/taskpane/components/EntityPicker.tsx` | 6 |
| `outlook/OutlookHostAdapter.ts` | 4 |
| `shared/taskpane/utils/errorMessages.ts` | 3 |
| `shared/taskpane/components/views/SaveView.tsx` | 3 |
| `shared/taskpane/components/TaskPaneShell.tsx` | 3 |
| `shared/taskpane/index.ts` | 2 |
| `shared/adapters/__tests__/WordAdapter.test.ts` | 2 |
| `shared/taskpane/services/SseClient.ts` | 1 |
| `shared/taskpane/hooks/__tests__/useSaveFlow.test.ts` | 1 |
| `shared/taskpane/components/views/index.ts` | 1 |
| `shared/taskpane/components/TaskPaneNavigation.tsx` | 1 |
| `shared/taskpane/components/TaskPaneHeader.tsx` | 1 |
| `shared/taskpane/components/ErrorBoundary.tsx` | 1 |
| `shared/services/ApiClient.ts` | 1 |
| `shared/adapters/HostAdapterFactory.ts` | 1 |

**Note on file counts vs error counts**: `shared/taskpane` has 56 `.ts`/`.tsx` files but carries 309 errors; `word/` + `outlook/` have 6 files and carry 4. The debt is concentrated in the taskpane's *tests*, not spread evenly.

---

## Capture correctness check — the three known barrel defects

All three present, so the capture reaches the files it must (Step 7):

| Location | Code | Message |
|---|---|---|
| `shared/taskpane/index.ts(3,25)` | **TS2305** | Module `"./App"` has no exported member `'ViewType'` |
| `shared/taskpane/index.ts(18,30)` | **TS2614** | Module `"./components/views/SaveView"` has no exported member `'SaveOptions'` |
| `shared/taskpane/components/views/index.ts(2,30)` | **TS2614** | Module `"./SaveView"` has no exported member `'SaveOptions'` |

✅ Capture is sound.

---

## Build, test, lint

### `npm run build` — ✅ passes (with two caveats)

`build` **is** the production build (`webpack --mode production --no-bail --stats errors-only`, `package.json:7`). **There is no `build:prod` script** — `src/client/office-addins/CLAUDE.md:39` names one that does not exist. *Documentation defect; recorded, not fixed (out of scope).*

Two prerequisites, neither documented in the module CLAUDE.md:

1. **`@spaarke/auth` must be built first**, or webpack fails with `Can't resolve '@spaarke/auth'`. The CI workflow (`deploy-office-addins.yml:43-47`) knows this and does it explicitly; the module CLAUDE.md build section does not mention it.
2. **Four environment variables are required** or webpack aborts before compiling: `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` (`webpack.config.js:24`). Copy `.env.example` → `.env`, or pass inline. Verified using the non-secret values already committed in the CI workflow.

With both met, the build compiles clean.

### `npm test` — 🔴 fails at baseline

```
Test Suites: 13 failed, 8 passed, 21 total
Tests:       57 failed, 169 passed, 226 total
Time:        58.7 s
```

Dominant failure: `TypeError: expect(...).toBeInTheDocument is not a function` — `@testing-library/jest-dom` matchers are not registered in the Jest setup. This is a **suite-wide harness gap, not 57 independent test bugs**.

⚠️ Per ADR-038 and this task's constraint, **no test was added, deleted, skipped or modified** to investigate this. Recorded only.

This interacts directly with the 75%-of-debt-is-tests finding: tasks 006–008 will be fixing types in a suite that does not currently pass.

### `npm run lint` — 🔴 broken script

```
ESLint: 8.57.1
No files matching the pattern "src" were found.
```

Exit code **2**. The script is `eslint src --ext .ts,.tsx` (`package.json:13`) and this package has **no `src/` directory** — its sources are `shared/`, `word/`, `outlook/`. So lint does not "silently pass"; it **errors**. Recorded as a finding, not fixed (FR-18 is about typecheck, not lint).

---

## Recommendations for tasks 006 / 007 / 008

1. **🔴 Resolve the UNASSIGNED ownership question first** (escalation trigger 3). The 11 `Spaarke.Communication.Components` errors are unowned and outside the package. Options: (a) add a fourth task owning that file, (b) coordinate with whoever owns that library, (c) narrow FR-18's acceptance to the `office-addins` package and accept that `npm run typecheck` is not clean while the alias pulls in a foreign file.
2. **Re-scope the three-way split.** 309 / 45 / 4 is not a sensible three-way division of labour. Consider splitting by the *five large test files* (217 errors) versus *production source* (99) instead of by directory.
3. **Settle the ADR-038 question before starting.** 75% of the debt is in test files whose suite is red. Decide explicitly whether FR-18 means "fix production types" (~99 errors, achievable) or "fix everything including a broken test harness" (395, and entangled with a separate defect).
4. **Verify the remaining 4× TS2307** are real before counting them as debt — one of the original five was purely environmental.
5. **Fix the harness gap separately from FR-18.** The missing `jest-dom` registration is one setup change that likely clears a large share of both the 57 test failures and some TS2339 count.

---

## Deviations from the task definition

| Step | Deviation | Why |
|---|---|---|
| 2 | The committed raw log is the **post-prerequisite** capture (395), not the first run (396) | The first run measured an unbuilt `@spaarke/auth`. Committing it would have made the baseline non-reproducible — the opposite of the step's stated purpose. Both numbers and the one-line diff are documented above. |
| 8 | Built `@spaarke/auth` (outside `office-addins`) | Required to answer "does the build pass?" at all. Creates only gitignored output (`dist/`, `node_modules/`); `git status` under `src/` is clean. |
| 8 | Supplied four env vars inline | `webpack.config.js:24` aborts without them. Used the non-secret values already committed in `deploy-office-addins.yml`. No `.env` file was created. |
| 2 | Raw log committed with `git add -f` | `.gitignore:33` ignores `*.log` repo-wide, but this task's acceptance criterion names `notes/typecheck-baseline-raw.log` explicitly and requires it committed — an unsourced number is the exact failure mode this task exists to correct. Force-added this one file rather than amending the repo-wide ignore rule or renaming the required path. |

## Verification

- ✅ `git status --porcelain src/` — empty. No tracked file under `src/` modified.
- ✅ `package-lock.json` md5 unchanged (`66032a00b42d39773d1cd8f2293fd2a4`) before and after install.
- ✅ Re-running `npm run typecheck` reproduces **395**, matching the committed log.
- ✅ No typecheck error was fixed by this task.
