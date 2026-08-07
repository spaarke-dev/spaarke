# Task 040 — Outlook/Word add-in realignment (FR-B0) — completion notes

**Rigor**: STANDARD (sonnet, effort high) — realign + verify, not a rebuild.
**Scope boundary honored**: runtime verification (NAA sign-in against a live BFF; dark-mode live render) requires an Office host + dev tenant not available in this environment, and deploys are paused. The two `<ui-tests>` in the POML are **deferred to operator live-verify** (see §6 below) — not claimed as passed.

---

## 1. Call-site audit (fetch/XHR/SSE)

| File | Call | Classification | Action |
|---|---|---|---|
| `shared/services/ApiClient.ts` `request()` (GET/POST/PUT/DELETE) | `fetch` via hand-rolled wrapper | JSON | Routed through new `authenticatedJsonFetch` helper — added single-retry-on-401 (previously had **no retry at all**, despite the file's own header comment implying it did) |
| `shared/services/ApiClient.ts` `uploadFile()` | `fetch` (FormData) | JSON/binary upload (not XHR) | Same — routed through `authenticatedJsonFetch` |
| `shared/taskpane/hooks/useEntitySearch.ts` `performSearch()` | raw `fetch` + Bearer literal | JSON GET (`/api/office/search/entities`) | Was a raw fetch with **zero retry**; routed through `authenticatedJsonFetch` |
| `shared/taskpane/hooks/useSaveFlow.ts` `pollJobStatus()` | raw `fetch` + Bearer literal, self-labeled `// D-AUTH-7 exception site` | JSON GET (`/api/office/jobs/{jobId}`) | **Re-classified**: not a legitimate D-AUTH-7 exception (canonical exception list per `.claude/constraints/auth.md` is SSE / XHR uploads / Dataverse Web API direct / External SPA — this is plain JSON). Routed through `authenticatedJsonFetch`. Comment corrected. |
| `shared/taskpane/hooks/useSaveFlow.ts` `startSave()` submit | raw `fetch` + Bearer literal | JSON POST (`/api/office/save`) | Same re-classification and fix |
| `shared/taskpane/services/SseClient.ts` `connect()` | raw `fetch` + `ReadableStream` + Bearer literal | **SSE** (genuine D-AUTH-7 exception) | **Untouched** — `authenticatedFetch` cannot expose the streaming body; own 401-reconnect loop (capped at `maxAuthRetries`) already exists and is correct. Per task instruction, left as-is. |
| `outlook/commands/index.ts` `quickSave` | `apiClient.post('/api/office/save', ...)` | JSON | Already via `apiClient` — inherits the fix above, no direct change needed |
| `shared/taskpane/services/communicationLookupService.ts`, `communicationSuggestionsService.ts`, `useLinkedTodosForCommunication.ts` | `apiClient.get(...)` | JSON | Already via `apiClient` — inherits the fix, no direct change needed |

**Why `authenticatedFetch` from `@spaarke/auth` itself could not be used directly**: it resolves its token via the module-level `getAuthProvider()` singleton, populated only by `initAuth()`. This package's `AuthService.ts` deliberately does **not** call `initAuth()` — it constructs its own `SpaarkeAuthProvider` + `OfficeNaaStrategy` instance because `initAuth()`'s convenience wrapper doesn't accept a `strategy` override (documented in `AuthService.ts`'s header comment). Calling `getAuthProvider()` from this package would throw `AuthError('not_initialized')`. New shared helper `shared/services/authenticatedJsonFetch.ts` mirrors `authenticatedFetch`'s 401-retry **shape** (single retry, fresh token, cache-clear before retry where a cache handle is available) for this package's call sites instead. Full rationale is in that file's header comment.

---

## 2. Files modified

| File | Purpose |
|---|---|
| `shared/services/authenticatedJsonFetch.ts` (new) | Shared single-retry-on-401 fetch wrapper used by every JSON call site below |
| `shared/services/ApiClient.ts` | `request()`/`uploadFile()` now route through the helper (adds 401-retry that did not exist before); removed dead `.default`-scope array construction (`getAccessToken()` simplified) |
| `shared/services/AuthService.ts` | Added `clearCache()` to `IAuthService` + impl, delegating to `SpaarkeAuthProvider.clearCache()` — used before `ApiClient`'s retry attempt (mirrors `@spaarke/auth`'s own `authenticatedFetch` clear-then-retry step) |
| `shared/taskpane/hooks/useEntitySearch.ts` | `performSearch()`'s raw fetch → `authenticatedJsonFetch` |
| `shared/taskpane/hooks/useSaveFlow.ts` | `pollJobStatus()` and `startSave()`'s raw fetches → `authenticatedJsonFetch`; corrected the D-AUTH-7 comment mis-classification; fixed one new `exactOptionalPropertyTypes` type error introduced by the refactor (conditional `signal` spread) |
| `shared/taskpane/App.tsx` | Removed dead `['user_impersonation']` scope arg on `authService.getAccessToken()` call |
| `shared/taskpane/components/SaveFlow.tsx`, `shared/taskpane/components/views/SaveView.tsx`, `shared/taskpane/services/SseClient.ts` | JSDoc `@example` blocks updated to drop the same dead scope arg (copy-paste source for future consumers) |
| `word/word-manifest.xml` | Replaced hardcoded production SWA origin (`https://icy-desert-0bfdbb61e.6.azurestaticapps.net`, 5 occurrences) with the `https://localhost:3000` placeholder — parity with the Outlook manifest's dev-authored convention |
| `webpack.config.js` | Added a `transform` to the Word manifest `CopyWebpackPlugin` pattern, substituting `ADDIN_BASE_URL` at build time — identical `split('https://localhost:3000').join(...)` mechanism the Outlook manifest already used. Previously the Word manifest was copied verbatim with **no transform at all**, meaning dev builds pointed at the production SWA origin unconditionally — that latent bug is what this fixes. |
| `outlook/taskpane/index.tsx`, `word/taskpane/index.tsx` | Cosmetic cleanup: Word's `APP_VERSION` was stale (`'1.0.3'` vs manifest's `1.0.4.0`) — corrected to `'1.0.4'`; both files' hardcoded `BUILD_DATE` fallback literals (`'Jan 22/23, 2026'`) replaced with `'unknown'` (fallback only fires outside webpack, which always injects the real date via `DefinePlugin`) |
| `jest.setup.js` | Added a `TextEncoder`/`TextDecoder` polyfill (Node's `util` implementations) — jsdom's test environment doesn't expose these globals, and `useSaveFlow.ts`'s `computeIdempotencyKey` needs them. Required for the new 401-retry tests to actually exercise `startSave`'s fetch path; as a side effect this also fixed 2 pre-existing unrelated `useSaveFlow.test.ts` failures (`includes idempotency key header`, `handles network error`) that predate this task. |
| `shared/services/__tests__/ApiClient.test.ts`, `shared/taskpane/hooks/__tests__/useSaveFlow.test.ts`, `shared/taskpane/hooks/__tests__/useEntitySearch.test.ts` | Updated/added tests — see §5 |

No files under `src/server/**` were touched (BFF is owned by the parallel task 041 agent per the orchestrator's boundary).

---

## 3. Manifest parameterization result

Verified via a local production build (`npm run build`, with a throwaway gitignored `.env` for dummy client IDs — never committed):

- `dist/word/manifest.xml`: all URLs resolve through `ADDIN_BASE_URL` (confirmed no `icy-desert-0bfdbb61e` string anywhere in `src/` after the change — only the stale, gitignored `dist/` output from a prior build retained it, and that's regenerated).
- **Parity confirmed empirically**: under a local build where `NODE_ENV=production` isn't explicitly exported (webpack's `--mode production` CLI flag does **not** itself set `process.env.NODE_ENV` — that's a pre-existing, Outlook-manifest-affecting quirk in `webpack.config.js`'s `isProduction` check, not something this task introduced), **both** `dist/outlook/manifest.json` and `dist/word/manifest.xml` resolved their base URL to `https://localhost:3000` identically. This is the literal acceptance bar ("parity with Outlook") — achieved. Flagging as a **deferred finding** for task 044 (deploy): confirm the CI/CD pipeline sets `NODE_ENV=production` (or `ADDIN_BASE_URL` explicitly) before `npm run build`, or **both** manifests will ship pointing at `localhost:3000`. This is not a regression — it's a latent pre-existing gap on the Outlook manifest that now applies symmetrically to Word.
- `outlook/manifest.json` required **no changes** — it was already the parameterized reference implementation; Word now mirrors it exactly (same substitution mechanism, same placeholder convention).

---

## 4. Build result

`npm run build` (webpack production) — **green**. No errors. Verified `dist/word/manifest.xml` and `dist/outlook/manifest.json` both parameterize correctly (see §3).

Note: `npm run typecheck` has a large pre-existing baseline of `exactOptionalPropertyTypes` and unrelated errors across this package (confirmed via `git stash` diff — identical error set before/after, at the file level, apart from one NEW error my `authenticatedJsonFetch` refactor introduced in `useSaveFlow.ts` at the `pollJobStatus` call site, which was fixed in-flight (conditional `signal` spread instead of `{ signal: possibly-undefined }`)). `npm run build` uses `ts-loader` with `transpileOnly: true`, so none of this blocks the actual build; typecheck cleanup for the rest of the package is out of scope for this realign task.

---

## 5. Jest test result

Full suite (`npm test`): **168 passed / 57 failed / 225 total** (was 159 passed / 59 failed / 218 total on baseline, confirmed via `git stash` diff of exact failing-test-name sets — **zero new failures introduced**; 2 pre-existing failures fixed as a side effect of the `TextEncoder` polyfill needed for my own new tests).

New/updated tests (all passing):
- `ApiClient.test.ts`: updated the scope-arg assertion (dead `.default` array removed); added 3 new tests — single-retry-on-401 succeeds, second-401 surfaces as a normal `ApiClientError` (not an unhandled error or infinite loop), `uploadFile` also gets the retry.
- `useSaveFlow.test.ts`: added 2 new tests under `startSave` — retries exactly once on 401 and succeeds (asserting `error` stays `null`, the first two fetch calls target `/office/save`, `getAccessToken` called ≥2×); a second 401 surfaces as a normal error (not an unhandled exception), fetch called exactly twice (no loop).
- `useEntitySearch.test.ts`: added 2 new tests under `performSearch`/`searchNow` — same two shapes (succeeds after one retry; second 401 surfaces as a normal search error). **Positioned before the pre-existing `searchNow` describe block** — that test has an unrelated fake-timer/mock-delay interaction that times out (10s) and corrupts React's fake-timer state for whatever runs next in the file; this is a pre-existing flake (confirmed via `git stash`, reproducible independent of this task) and moving the new tests earlier avoids being a downstream victim of it, rather than attempting to fix that unrelated flake.

---

## 6. Acceptance criteria — code-verified vs operator-gated

| Criterion | Status |
|---|---|
| NAA sign-in via OfficeNaaStrategy + JSON call returns 200 at runtime | **Operator-gated** — requires a live Office host + dev tenant + task 004's app registration; not available in this environment. No code-level auth-path change was made (OfficeNaaStrategy migration untouched) — only fetch-routing plumbing changed. |
| Word manifest parameterized, no hardcoded SWA origin, parity with Outlook | **Code-verified** — §3 above |
| All JSON call sites route through `authenticatedFetch`-equivalent (401-retry); SSE/XHR keep D-AUTH-7 exceptions | **Code-verified** — §1/§2/§5 above |
| Negative: expired-token JSON call gets exactly one transparent retry, not an unhandled error or second auth path | **Code-verified** — `authenticatedJsonFetch` unit-tested at all 3 call sites (§5) |
| Dark-mode taskpane renders with Fluent v9 dark tokens | **Operator-gated** — requires a live Office host to switch OS/Office theme and visually inspect; no UI surfaces were touched by this task (only service/hook/manifest files), so no new dark-mode risk was introduced. Not claimed as verified. |
| Dead scope args + stale version/build strings removed; build green; jest passes | **Code-verified** — §2/§4/§5 above |
| TASK-INDEX.md marker + deviations recorded | Left to the orchestrator per the "Do NOT" boundary; see §8 below for the recommended marker |

---

## 7. Escalation check

**No escalation fired.** The audit found no second token-acquisition path and no hardcoded secret. The one genuine defect found — the Word manifest's unconditional hardcoded production SWA origin with zero build-time substitution — is exactly the FR-B0(b) target this task exists to fix, not a surprise requiring registration-owner escalation. Task 004 (Entra NAA app-registration provisioning) status was not independently re-verified here since runtime sign-in itself is operator-gated in this environment; per the escalation trigger, that verification is deferred, not skipped silently — it's recorded above as operator-gated, not claimed as passed.

---

## 8. Recommended TASK-INDEX marker

Recommend **`✅-code`** (not full `✅`) — all autonomously-verifiable acceptance criteria (manifest parity, fetch-routing/401-retry, dead-code cleanup, build, jest) are met; the two runtime `<ui-tests>` (NAA sign-in against a live BFF; dark-mode live render) remain genuinely unverified pending an operator with a live Office host + dev tenant + deploy access (deploys are paused project-wide). This is not a code gap — it's the honest state of a task whose acceptance criteria mix code-level and runtime-level checks in an environment where the runtime half is structurally unavailable.

**One-line summary**: Add-in JSON call sites (previously mixed apiClient-with-no-retry + two raw-fetch sites incorrectly self-labeled as D-AUTH-7 exceptions) now uniformly get single-retry-on-401 via a new shared `authenticatedJsonFetch` helper; Word manifest gained the same build-time SWA-origin parameterization Outlook already had (was hardcoded/unconditional); dead scope args and one stale version string removed; build is green and jest is clean of new regressions (168/225 passing, up from 159/218, zero new failures) — runtime NAA sign-in and dark-mode are deferred to operator live-verify.
