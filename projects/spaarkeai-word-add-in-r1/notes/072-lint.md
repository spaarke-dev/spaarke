# Task 072 — lint ran for the first time

> **Date**: 2026-09-28
> **Task**: `tasks/072-fix-and-wire-lint.poml` · FULL · sonnet @ high · directional
> **Files changed**: `src/client/office-addins/package.json`, `.eslintrc.js`,
> `.github/workflows/office-addins-tests.yml`, plus 16 source/test files

---

## The finding

`package.json` declared `"lint": "eslint src --ext .ts,.tsx"` in a package with **no `src/` directory** — the
sources are `shared/`, `word/` and `outlook/`. It printed *"No files matching the pattern src"*. No workflow
called it either; the repo's ESLint jobs run in `src/client/pcf`.

**CI passed because lint never ran, not because lint passed.** Nobody knew how many violations the package held.
The first successful run answered it: **32** — **4 errors, 28 warnings** — across **17 of 125** files.

| Rule | Count | Severity |
|---|---|---|
| `@typescript-eslint/no-empty-function` | 13 | warn |
| `@typescript-eslint/no-unused-vars` | 12 | warn |
| `react-hooks/exhaustive-deps` | 3 | warn |
| `@typescript-eslint/no-namespace` | 2 | **error** |
| `no-case-declarations` | 1 | **error** |
| `@typescript-eslint/no-var-requires` | 1 | **error** |

Not hundreds, so the escalation trigger did not fire. **Backlog driven to zero.**

## No rule is disabled or downgraded (criterion 5)

**One rule *option* changed, and it is the rule's own.** `@typescript-eslint/no-namespace` gains
`allowDeclarations: true`:

- Both hits are `declare global { namespace Office { namespace MailboxEnums { … } } }` in
  `shared/adapters/OutlookAdapter.ts:54-56`, which augments the global `Office` type so `getImportance()` can
  read the real office.js enum.
- `no-namespace` exists to steer new code toward ES2015 modules. **Module syntax cannot express global
  augmentation at all**, so the advice has no applicable form here — which is exactly what `allowDeclarations`
  is for.
- The declaration is **erased at compile time**; no runtime namespace object is emitted, so the shape the rule
  guards against is never created. Non-declaration namespaces remain errors.

**The 13 empty functions needed no config change, because I checked instead of assuming.** A probe run
confirmed ESLint's `no-empty-function` treats a body containing a comment as non-empty, so each became
`{ /* no-op */ }`. The obvious alternative — an `overrides` block switching the rule off for `__tests__` —
would have disabled a rule across a quarter of the package to fix 11 stubs.

**`--max-warnings 0` added to the script, and it is load-bearing.** 28 of 32 were *warnings*. Without the flag
this gate would have gone green over 87% of the backlog and been decoration — the precise failure this wave
exists to remove.

## 🔴 Two inert suppressions — same class as 069 and 070

Both look present and do nothing. Neither was discoverable until lint ran once.

**1. `DocumentProfileSection.tsx`** carried

```
// eslint-disable-next-line react-hooks/exhaustive-deps -- see comment above: refetch is
// intentionally excluded.
```

`eslint-disable-next-line` applies to the **next line only**. Wrapping the justification onto a second `//`
line made that next line *the comment*, not the hook call — so the rule still fired. Fixed by putting the
directive on the single line immediately above the dependency array.

**2. `matterTypeLookupService.cache.test.ts`** disabled `@typescript-eslint/no-require-imports`, but the rule
that actually fires for `require()` under typescript-eslint 6 is **`no-var-requires`**. Wrong rule name, so the
suppression was inert. Corrected; the original justification (deliberate fresh module load for a cache test)
was sound and is kept.

## One real code improvement, not a suppression

`EntityPicker.tsx` built `allOptions` as a bare `const [] ` plus `.push(...)` on every render — a **new array
identity each render**, which made the `useCallback` for the keyboard handler below it re-create every render
too. The memoisation there was doing nothing, and that is what the rule was reporting. Now `useMemo`'d with
explicit dependencies; **contents unchanged, identity stable**.

The other two `exhaustive-deps` sites are genuine intentional omissions and carry per-site reasons:

| Site | Why the dep is omitted |
|---|---|
| `DocumentProfileSection.tsx` | Including `refetch` re-fetches on every identity change, not only on a genuine `refreshSignal` bump. |
| `StatusView.tsx` | `fetchJobs` is redefined every render, so including it would tear down and re-create the polling interval every render — resetting the cadence the effect exists to establish. |

`no-case-declarations` in `SseClient.ts` was a genuine minor defect: an unbraced `case 'retry':` put its
`const` in scope for the **whole switch**, so a later reference from another branch would be a runtime
`ReferenceError`. Braced.

## Cross-task interaction, caught locally rather than on CI

Six of the unused imports/locals were **also** counted by `tsc --noEmit` under `noUnusedLocals`, so **069's
accepted test-file debt fell 74 → 68**.

Because 069 pinned that as an **equality rather than a ceiling**, CI would have refused the reduction until it
was written down — which is the pin doing exactly the job it was built for, on its first real encounter.
Re-baselined to **68** in this same PR with the reason in-file. Production errors remain **0**.

## CI proofs

| # | Proof | Commit | Run | Result |
|---|---|---|---|---|
| 1 | Green — all four jobs, backlog at zero | `8f3a16eef` | **36459612571** | ✅ `ESLint => success`, and the pin verified in-run: *"0 production error(s); 68 total line(s) … (pinned at 68)"* |
| 2 | **Red** — warning-only, lint-only seed | `13b89a8b3` | **36460853616** | ✅ `ESLint => failure`; **all three other jobs green** — clean isolation |
| 3 | Revert → green | `17a2…` (this commit) | | *pending* |

Run 36460853616's own log is the proof that `--max-warnings 0` is load-bearing, not an opinion:

```
ESLint (office-addins)   ✖ 1 problem (0 errors, 1 warning)
ESLint (office-addins)   ESLint found too many warnings (maximum: 0).
Production typecheck     0 production error(s); 68 total line(s) … (pinned at 68)
```

**Zero errors. One warning. Job red.** And the typecheck job stayed green at the re-baselined pin, confirming
the seed hit only the guard under test.

### 🔴 The seed was defeated by the config on the first attempt

Worth recording, because it would have produced a **false proof**. The first seed was
`const __072_unusedSeed = 1;`. Local lint exited **0** — `.eslintrc` sets `varsIgnorePattern: '^_'`, so the
leading underscore made ESLint ignore it *by design*.

Had that been pushed, CI would have gone green and the record would have claimed a proof that the gate
**cannot fail**. Proving the seed locally before pushing is what caught it — the same discipline as checking a
green for vacuity instead of trusting it.

The second attempt needed narrowing too: an unused `const` also trips tsc's `noUnusedLocals`, so in a
production file it would have failed the **typecheck** job as well (68 → 69 with `prod=1`), leaving two red
jobs and no clean attribution. The final seed is deliberately:

- **warning-only** — because 28 of 32 were warnings, this is what proves `--max-warnings 0` matters; and
- **lint-only** — *exported*, so invisible to `noUnusedLocals`, and an empty body is invisible to tsc.

Verified locally before pushing: lint exit 1 (`1 problem (0 errors, 1 warning)` → *"ESLint found too many
warnings (maximum: 0)"*), tsc total unchanged at 68.

## 🔴 Out-of-scope finding: five tests that assert nothing

Surfaced by the unused-variable warnings, and **deliberately not absorbed into this task** — it is test
quality, not lint.

`shared/taskpane/components/__tests__/SaveFlow.test.tsx` contains **five `it(...)` blocks with no
assertions**. They build mocks and then carry only a comment:

| Test | Body |
|---|---|
| duplicate-response case (~line 340) | sets up `duplicateResponse` + `mockFetch` + `onDuplicate`, then *"Note: This test would require simulating entity selection first"* |
| `displays error message on API failure` | sets up `problemDetails` + `mockFetch`, then *"…would require simulating entity selection and form submission"* |
| `renders success UI after completion` | comment only |
| `handles view document action` | `jest.fn()` then comment only |
| `handles copy link action` | comment only |

**They pass.** An `it` with no assertions is a pass, and `SaveFlow.test.tsx` **is in `ci-gated-suites.txt`** —
so the 56-suite gate counts them as coverage. That is vacuous green one level below anything 069/070/072
addressed, and it is ADR-038's banned coverage-filler class.

I did **not** delete the now-unused fixtures, because deleting them would make the empty tests look tidy while
still asserting nothing. Instead they are `_`-prefixed using the rule's own `varsIgnorePattern` convention,
which records the honest state: **pending, not dead** — they are exactly the fixtures whoever completes these
tests will need.

**Needs a decision**: `/test-diet` at task 090, or its own task. Either is fine; leaving five assertion-free
tests inside a gate that reports 56/56 green is not.

## What this task did NOT close

Lint **runs** and **can fail the workflow run**. It does **not** block a merge — master's ruleset requires only
`Router`. Same owner action as 069 and 070.
