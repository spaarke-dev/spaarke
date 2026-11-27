# Task 044 notes - no-mock-identity warm-up (FR-29, #2275)

## Change
`src/client/external-spa/tests/no-mock-identity.test.tsx` only: a `beforeAll` (hook timeout 180 s, its own constant `WARMUP_TIMEOUT_MS`) dynamically imports the specifiers the tests already import (`dev-mock`, `auth/backend-selection`, `auth/msal-auth`, `auth/bff-client`, `api/me-client`, `pages/WorkspaceHomePage`, `App`), then calls `vi.resetModules()`. It sits before the first env stub (stubs are per-test and `afterEach` unstubs). No test removed, skipped, renamed or edited; `SLOW` (60 s), `load()` and `vitest.config.ts` unchanged. Added `beforeAll` to the vitest import.

## Cold-run timings (cold = `src/App.tsx` LastWriteTime touched, content unchanged; `npx vitest run tests/no-mock-identity.test.tsx`)

| Run | File duration | Slowest test | Wall |
|---|---|---|---|
| Before (no warm-up) | tests 43.4 s | "App header shows no mock user" 37.3 s (first App import; 23 s from the 60 s limit; `bffApiCall` test 2.2 s) | 75.4 s |
| After, cold run 1 | tests 18.5 s | App test 0.49 s | 26.2 s |
| After, cold run 2 | tests 19.0 s | App test 0.48 s | 23.0 s |

Both post-change cold runs: 31/31 pass. The "before" run did not time out on this occasion (37 s of 60 s), but the first App import absorbed the whole transform cost; the machine can push that past 60 s under load. The cost has moved into the hook (budget 180 s) and no test now takes more than ~2 s.

## Verification
- `npx tsc --noEmit -p .`: exit 0. `npx eslint tests/no-mock-identity.test.tsx`: exit 0.
- Full suite (`npm test`): first run had 7 files with a 60 s timeout each (other files, machine loaded); those 7 files re-run alone: 444/444 pass. A second full run: 77 files, 2170 tests, all pass (192 s).

## Found, not fixed
Other test files (`invite-code`, `workforce-auth`, `teams-real-msal`, `DocumentUploadPage.externalUpload`, `teams-home-auth`, `workforce-home-auth`, `InvoiceSubmitPage`) time out at 60 s on a loaded machine and pass when re-run alone (444/444). Same cold-transform pattern as #2275; out of scope for 044.


**Tracking.** The other slow files are D-29 (#2964), which already tracked them. A duplicate D-45 (#3309) was filed and closed into it on 2026-11-27, with this task's result added to #2964.
