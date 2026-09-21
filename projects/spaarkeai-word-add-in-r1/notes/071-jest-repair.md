# Task 071 — Ten red jest suites, 84 failing tests, none a shipped bug — repaired and gated

> **Result**: all 10 suites repaired. 56/56 office-addins jest suites pass (750/750 tests) and all 56
> are now in `ci-gated-suites.txt`. No shipped behaviour was changed. Two of the ten "inferred"
> diagnoses in the Fable review were **wrong** (not just incomplete) — see §2.

---

## 1. Post-`npm install` baseline (real, not stale-install noise)

`src/client/office-addins/node_modules` was stale (mtime 2026-09-04; `@testing-library/jest-dom` and
`user-event`, added 2026-09-09 in `cc318390f`, were absent). `npm install` was run FIRST, per the task's
binding instruction, before any diagnosis.

**Real baseline, full `npx jest` run:**

```
Test Suites: 10 failed, 46 passed, 56 total
Tests:       84 failed, 679 passed, 763 total
```

Scoped to just the 10 red suites (reconciling against the review's "84 failed / 139 passed / 223
total"): that figure is the sub-total **within those 10 files only** (84 + 139 = 223), not the
package-wide count. Both numbers are consistent; the review's text didn't say which scope, so this is
recorded here to remove the ambiguity for whoever reads it next.

## 2. Confirmed-vs-corrected diagnosis table

The Fable review (`notes/fable-review-2026-09-21.md` §8) diagnosed each suite with a confidence level
(observed vs. inferred). Every "observed" entry held up. Two of the "inferred" entries were **wrong**,
not just approximately right — both are recorded below with what was actually found.

| Suite | Review confidence | Outcome | What was actually found |
|---|---|---|---|
| `OutlookAdapter.test.ts` (18/34) | observed | **Confirmed**, +1 unrelated gap found | `mockReadItem` lacked `itemType`; `OutlookAdapter.determineMode()` (`:193`) gates its **entire** read/compose detection on `'itemType' in item`, so every mode-dependent call degraded to `'unknown'`. Real Outlook items always carry `itemType` (`@types/office-js`), so this was a stale mock, not an adapter defect. **Also found**: `mockComposeItem` had the identical gap (undiagnosed), and a genuinely separate harness gap — `jest.setup.js`'s `Office.MailboxEnums` mock had no `AttachmentContentFormat`, so `getAttachmentContent` threw on `undefined.Base64` for one test the review didn't attribute to this suite's root cause. |
| `ShareView.test.tsx` (16/20) | observed | **Partly corrected** | ResizeObserver confirmed. The review's "plus one 10s timeout" undersold the second defect: the suite's `beforeEach` called `jest.useFakeTimers()` globally. Once the ResizeObserver crash stopped masking it, **every** `userEvent`-driven test in the file hung to the 10s jest timeout — `@testing-library/user-event` v14's internal `wait()` schedules a real `setTimeout` and awaits it; fake timers never auto-advance it. Not "one timeout" — the whole suite's interactive tests were at risk once the first defect was fixed. Repaired by removing the suite-global fake timers and widening the one test that genuinely needs to wait out a 2000ms revert to a real-timer `waitFor` with a longer budget. |
| `useSaveFlow.test.ts` (7/26) | observed | **Confirmed**, +3 more issues found | `isValid` hard-coded `true` by design (`useSaveFlow.ts:509`) confirmed, and the suite's own "allows saving without entity selection" test already proved the design intent. The fetch-mock-lacking-`text()` diagnosis was also confirmed — `startSave` always reads `response.text()` then `JSON.parse`s it (`:1124`), never `.json()`, for every response in that path. **Also found**: two more `isValid).toBe(false)` assertions beyond the one line cited; the "calls API with correct payload" test asserted against the wrong internal object (`SaveRequest`, built only for idempotency-key hashing, never sent on the wire — the real body is `serverRequest`/`targetEntity`/`aiOptions`); and "clears error" assumed a synchronous validation error that no longer exists and never awaited the async save it triggered. |
| `TaskPaneNavigation.test.tsx` (1/7) | observed | **Confirmed exactly** | Fluent v9's `TabList` spreads `disabled` onto each rendered `<button role="tab">` as a real HTML attribute — not `aria-disabled` on the `role="tablist"` container. Verified by inspecting the rendered DOM. |
| `TaskPaneShell.test.tsx` (5/10) | partly inferred | **Confirmed**, +2 more issues found | Both named causes confirmed: exact-match `'Spaarke'` vs `appName = 'Spaarke DMS'`, and the Share tab (V1, hidden). **Also found**: the `title` prop is now entirely dead — task 015's `TaskPaneToolbar` consolidation replaced the old `TaskPaneHeader` (whose default title WAS `'Spaarke'`) and never wired `title` through; and the sign-out affordance moved behind a closed "More options" overflow menu, so there is no standalone "signed in as {name}" button anymore. |
| `SaveView.test.tsx` (22/26) | **inferred: "stale label strings"** | **Wrong** | Not a label problem. `SaveView` underwent a wholesale prop-API rewrite: the old `hostContext: IHostContext` (now `@deprecated` on `IHostAdapter.ts`) plus `onSave`/`isSaving`/`progress`/`error`/`success` props are gone entirely. `SaveView` now resolves data from a live `hostAdapter: IHostAdapter` and delegates ALL rendering (document info, attachments, save button, progress, messages) to a new `SaveFlow` child. Independently corroborated: the sibling `SaveView.captureAtSave.test.tsx` (already green, unrelated to this task) has its own header comment stating this exact file's old setup "would not apply here." The suite was rewritten (26 → 17 tests) around SaveView's actual current contract (adapter-driven loading/error state, capability-gated prop passthrough to a mocked `SaveFlow`), following the same mocking pattern that sibling file already proved. |
| `SaveFlow.test.tsx` (9/27) | **inferred: "stale label strings"** | **Partly wrong** | Confirmed: `"Associate With"` → `"Related to"`, `emailSender` → `senderEmail` prop rename, save button text `"Save to Spaarke"` → `"Save"`. **Wrong for the majority of the failures**: 5 of 9 were the AI-processing toggle switches (Profile Summary / Search Index / Deep Analysis), which were **intentionally removed** — `SaveFlow.tsx:1450-1452`'s own comment says they are "mandatory for all content saved to Spaarke — always on... no toggles (UI feedback 2026-09-02)." That's a deliberate, documented product decision predating this task, not a label drift. Also found: "disables save button when no entity is selected" assumed the same stale premise as `useSaveFlow.test.ts` — entity selection has never gated the save button's disabled state. |
| `EntityPicker.test.tsx` (4/32) | **inferred: "stale label strings"** | **Wrong** | No label was wrong anywhere in this suite. All 4 failures were a harness gap: Fluent's `Combobox` only renders its popup `Option`s while `open` (the component's own `isOpen` state) is true, which flips true on focus. Four tests asserted on popup content without ever focusing the input first — missing the same `fireEvent.focus(input)` step the sibling "shows recent entities" test in the *same file* already used correctly. |
| `ApiClient.test.ts` (1/20) | **inferred: "mock lacks headers"** | **Wrong** | `authenticatedJsonFetch` never reads `response.headers` at all — only `response.status`. The real cause: the test called `apiClient.get('/api/missing')` twice but queued only one `mockResolvedValueOnce` response; the second call received `undefined` from the mock, and `authenticatedJsonFetch` dereferenced `.status` on it, throwing a `TypeError` instead of the expected `ApiClientError`. |
| `useEntitySearch.test.ts` (1/21) | **inferred: "searchNow awaited under fake timers"** | **Confirmed** | The mock/simulated search path (used when no `apiBaseUrl` is supplied) awaits a real `setTimeout(resolve, 200)` (`useEntitySearch.ts:159`). Under the suite's `jest.useFakeTimers()`, nothing advanced that timer, so `await result.current.searchNow()` hung to the 10s jest test timeout. Fixed with `jest.advanceTimersByTimeAsync(250)` while the promise is pending. |

**Reading the pattern**: every "observed" entry in the review held up under direct reproduction. Every
"inferred" entry that named a *specific line* (useSaveFlow's `:82`, `:508-509`; ApiClient's `:100-104`)
was checked against the actual runtime path and corrected where wrong. The two fully-wrong diagnoses
(SaveView, EntityPicker — and half of SaveFlow) share a shape: they guessed "stale label" for failures
that were actually a wholesale API rewrite, a removed feature, or a missing test-harness step. None of
the ten pointed at a shipped code defect — the review's central claim holds.

## 3. No shipped behaviour was changed

Every fix is in a test file (`*.test.ts(x)`) or the shared test harness (`jest.setup.js`). The only
non-test files touched are `src/client/office-addins/ci-gated-suites.txt` and one count string in
`.github/workflows/office-addins-tests.yml` (per the task's binding rule 5) — neither is shipped
runtime code.

## 4. `ResizeObserver` polyfill consolidated

Added once, globally, in `jest.setup.js`. Removed the 11 identical per-file copies it made redundant
(`SaveFlow.documentName.test.tsx`, `SaveFlow.captureAtSave.test.tsx`, `SaveFlow.quickCreateErrorSurfacing.test.tsx`,
`SaveFlow.matterTypeQuickCreate.test.tsx`, `SaveFlow.versionMode.test.tsx`, `SaveFlowCollision.test.tsx`,
`RelatedToPicker.matterType.test.tsx`, `RelatedToPicker.errorSurfacing.test.tsx`,
`DocumentProfileSection.test.tsx`, `LinkedTodosBanner.test.tsx`, `FindView.test.tsx`) — one more than the
review's "10 green suites polyfill per-file" count; the eleventh (`FindView.test.tsx`) was missed by the
review's count but found by grep during repair. `SaveFlow.openRecord.test.tsx` was checked too — it only
*mentions* ResizeObserver in a comment while avoiding it via careful mocking, so it had nothing to remove.

## 5. Final counts

Full `npx jest` run after all repairs:

```
Test Suites: 56 passed, 56 total
Tests:       750 passed, 750 total
```

Gated run (`npx jest --runTestsByPath $(grep -vE '^\s*(#|$)' ci-gated-suites.txt)`) — identical: **56
passed / 56 total, 750/750 tests.**

Reconciling against the 84 failed / 139 passed / 223 total starting point (scoped to the 10 suites): all
84 failing tests now pass. The total test count for those 10 suites is 223 → 214 after repair (139 + 84
= 223 before; SaveView.test.tsx dropped 9 tests in its rewrite — removed only because the behaviour they
tested no longer exists — and SaveFlow.test.tsx dropped 4 — the removed AI-processing toggles, replaced
by 1 negation test). Package-wide: 763 → 750 total tests, a net **-13**, entirely from those two
intentional removals; nothing else changed test count.

## 6. `ci-gated-suites.txt`: 46 → 56

All 10 suites added to the manifest, taking it from 46 to 56 gated paths (verified: every path in the
file resolves to a real file; `grep -vE '^\s*(#|$)' ci-gated-suites.txt | wc -l` → 56). The stale "12 of
22" prose at the top of the file was corrected to describe the 46 → 56 ratchet and this task's closing
of it. The workflow's `office-addins-tests.yml:275` summary string ("of 22 in the package") was corrected
to "of 56" — the only edit made to that file, per the task's binding rule that 069 owns the rest of its
logic.

The suite-count assertion (`office-addins-tests.yml:258-263`, `numTotalTestSuites` vs the manifest count)
was verified locally by running the exact `--runTestsByPath` invocation the gate uses: it reports 56 ran
against a 56-line manifest, so the assertion passes.

## 7. What was NOT done — stated explicitly

- **PROVEN ON CI acceptance criterion — NOT met.** Per the dispatching instruction for this run
  ("COMMIT on the current branch... Do NOT push"), this change was committed but **not pushed**, so no
  CI run exists to cite a URL for. The POML's own step 5 ("push and prove the gated job green... record
  the run URL") is explicitly overridden by that instruction. Everything CI would check was verified
  locally instead: the exact gated `--runTestsByPath` invocation (56/56, §5), the suite-count assertion's
  inputs (§6), and `npm run typecheck` (0 production errors — see §8). This is the one acceptance
  criterion this run did not satisfy; it needs a push + a real CI run to close.
- No new happy-path test cases were added beyond what each repair required. The only genuinely new
  assertions are: one negation test in `SaveFlow.test.tsx` (confirms the AI-processing toggles stay gone,
  replacing four tests of behaviour that no longer exists) and the `SaveView.test.tsx` rewrite's tests,
  each of which maps directly onto an original test's intent retargeted at the current `hostAdapter`
  contract (justified inline in the file's own header comment).

## 8. Typecheck hygiene (not gating, but checked)

`npm run typecheck` after all repairs: **0 production errors** (all `error TS` lines are under
`__tests__/`, the accepted-debt pattern the CI classifier already excludes). Two type errors introduced
by this task's own edits were cleaned up rather than left as new debt: a leftover `emailSender` prop
reference in `SaveFlow.test.tsx` and an `exactOptionalPropertyTypes` mismatch in the rewritten
`SaveView.test.tsx`'s adapter helper. A handful of unrelated, pre-existing unused-variable errors in
`SaveFlow.test.tsx` (`mockEntity`, `mockSaveResponse`, `mockJobStatus`, `onDuplicate`, `onViewDocument` —
all inside pre-existing placeholder "Note: this test would require..." stubs) were left untouched; they
predate this task and are outside its scope.
