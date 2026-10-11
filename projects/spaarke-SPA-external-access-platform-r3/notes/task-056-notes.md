# Task 056 — `/join?customer={key}` page behind a flag (FR-13): notes

**Date:** 2026-10-11 · **Rigor:** FULL (tags auth, frontend, testing) · **Model:** opus · **Base:** `db91ced2b`
**Decision followed:** `notes/spikes/fr-12-self-registration-decision.md` Option B (owner 2026-10-11), §4 and §7 "Inputs for task 056".
POML step 7 (TASK-INDEX, POML `<status>`, drift script) was not run: it is outside this agent's edit scope.

## 1. What shipped

| File | Change |
|---|---|
| `src/client/external-spa/src/auth/registration-client.ts` | New. Flag, service URL, key check (041 seam), return-contract parser, one-time `state`, start URL builder, workforce hand-off. No MSAL, no token, no scope. |
| `src/client/external-spa/src/pages/JoinPage.tsx` | New. State machine: not available / Teams / checking / link not recognised / intro / start again / outcome. Uses the shared `EmptyState` (ADR-012) and Fluent v9 tokens only (ADR-021). |
| `src/client/external-spa/src/main.tsx` | `/join` is rendered by the bootstrap **before** the realm chooser, backend activation and any MSAL instance (`renderJoin`). Inside Teams it calls `notifyTeamsAppLoaded()` and shows the browser-only message. |
| `src/client/external-spa/src/vite-env.d.ts`, `.env.example` | `VITE_JOIN_ENABLED` and `VITE_REGISTRATION_SERVICE_URL` declared and documented (commented out, default off). |
| `src/client/external-spa/tests/JoinPage.test.tsx` | New, 62 tests. |

## 2. Flag

- **Name:** `VITE_JOIN_ENABLED`. **Default:** off in every environment. Only the exact string `true` turns it on. Unset, empty, `false`, `TRUE`, `1` and an unsubstituted `#{…}#` placeholder all count as off.
- It is not set in `.env.development`, `.env.production` or `deploy-external-spa.yml`, so every build today has it off. A test asserts that no committed env file or workflow sets it to `true`.
- With the flag off, `/join` shows "Joining isn't available". It does not show the realm chooser or a 404, and it does not look up the key.
- Even with the flag on, a missing or refused `VITE_REGISTRATION_SERVICE_URL` keeps the page in the "not available" state (fail closed). The URL must be absolute and https (http only on localhost or 127.0.0.1), with no credentials, query or fragment.
- **To turn it on (task 060):** add `VITE_JOIN_ENABLED` and `VITE_REGISTRATION_SERVICE_URL` to the `env:` blocks and the validation step of `deploy-external-spa.yml`, set them as GitHub environment variables, and register the SPA's `/join` as the service's fixed return address.

## 3. Return contract — the interface task 060 must honour

**Start (SPA → service).** The page sends a full-page GET:

```
GET {VITE_REGISTRATION_SERVICE_URL}?customer={key}&state={state}
```

- `customer` is a key that already passed the 041 check (`^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$`).
- `state` is 32 lowercase hex characters (128 random bits) and is single-use.
- The request carries nothing else: no scope, no client id, no return address. Any query on the configured URL is refused. The service owns its client, its `openid profile` request, its `acct` claim and its return address (spike §4 step 2).
- The service MUST treat `customer` only as a registry key. It must not trust `state` beyond echoing it back.

**Return (service → SPA).** The service redirects with a 302 to:

```
{SPA origin}/join?customer={key}&state={state}&result={code}
```

- `{SPA origin}` is the service's own configuration (for example `https://external.spaarke.com`). It must never come from the start request (X2).
- `customer` is the same key. `state` is echoed unchanged.
- No other parameter is read. Free text (`message`, `error_description`, …) is never shown.

**`result` codes**

| `result` | When (spike ref) | SPA shows |
|---|---|---|
| `registered` | Invited (`sendInvitationMessage: false`) and added to `sprk-{customerId}-users` (§4 steps 4–5) | "You're registered", **Sign in** → hand-off |
| `already_registered` | An existing guest was reused (ObjectConflict, F1, T240d S1 (ii)); group ensured | "You're already registered — sign in", **Sign in** → hand-off |
| `tenant_not_allowed` | Home `tid` is not in the customer's `CustomerTenantIds`, or the list is empty (§4 step 3, R14.1, R14.7) | "Your company is not set up for access" (no sign-in) |
| `guest_account` | `acct` is missing or not `"0"` (§4 step 3, R14.2, F3) | "Use your own company account", **Try a different account** |
| `account_conflict` | The address belongs to an existing **Member** of Spaarke's tenant (§4 step 4) | "This account can't join this way" (no sign-in) |
| `consent_required` | The home tenant blocks user consent to "Spaarke Registration" (F5, Q2) | "Your company needs to approve Spaarke", **Try again** |
| `cancelled` | The user cancelled the home sign-in (OIDC `access_denied`) | "Sign-in was cancelled", **Try again** |
| `unavailable` | Joining cannot work right now and retrying will not help: invitations are disabled for the tenant (F7), or the service's customer registry is misconfigured (review fix) | "Joining isn't available right now", **no button** |
| `error` | Anything else, transient | "Something went wrong", **Try again** |
| anything else / missing value | — | treated as `error` |

**State check.** The SPA shows an outcome only when `state` equals the one this tab created **for this customer key**. The stored state is removed on every return, so it is single-use. On a mismatch, a missing state, a state from another customer or a replayed link, the SPA shows the neutral "Let's start again" page: no outcome, and **Start again** plus **Sign in** (the page says that someone who already completed sign-up can just sign in; the same hand-off as `already_registered`, so it reveals nothing beyond what the key check already shows). After reading the return, the SPA removes `result` and `state` from the address bar.

**Failures that are NOT `result` codes (review fix).** Three things can still go wrong *after* the service returns `registered` / `already_registered`, during the workforce sign-in that **Sign in** starts. The service never sees them and must not try to return a code for them:

- spike **F6** (outbound B2B blocked at the guest's home tenant);
- spike **F8** (alias mismatch);
- a declined consent surface (the "Review permissions" page, `access_denied` / AADSTS65004).

Entra returns the error to the SPA's redirect URI. The SPA now shows its **"Sign-in did not complete"** screen (the safe error code only, **Try again**) and does not redirect again on its own. See "Review fix round" below.

**Entra error → `result` mapping task 060 should use** (for errors Entra returns to the *registration service's* own redirect):

| Entra / OIDC error | `result` |
|---|---|
| `consent_required`, AADSTS65001, AADSTS90094 | `consent_required` |
| `access_denied` with AADSTS65004 | `cancelled` |
| invitations disabled (F7), or a misconfigured customer registry | `unavailable` |
| anything else | `error` |

**Dependency on T240c / task 060: the key check.** The page validates `?customer=` with the workforce `resolveByKey`, which must therefore be callable **before sign-in**: unauthenticated and keyed only by the customer key. Today `DirectoryServiceClient` throws (`backend-selection.ts` ~300), so with `VITE_USE_DIRECTORY_SERVICE=true` every join link shows "Link not recognised". That is **fail-closed** and intended until T240c ships. Because the lookup answers "does this customer key exist" to anyone, it is inherently a customer-key existence oracle: **T240c / 060 must rate-limit it** (per client address, and an overall ceiling).

**Known limit under T240c.** A user who has just been added to `sprk-{customerId}-users` and has not yet been grouped by T240c's propagation gets **"Sign-in unavailable"** at the workforce sign-in until group propagation completes. The page cannot detect this; **Sign in** is correct and retrying later works.

**What the hand-off assumes of the service.** Before it returns `registered` or `already_registered`, the guest must exist and be in `sprk-{customerId}-users`. On the next screen the SPA starts the Spaarke-tenant workforce sign-in. The FR-15 member-test branch and the T240c group claim must already be in place for that sign-in to resolve.

## 4. Decisions

1. **No MSAL "registration-only client" in the SPA (deviation from POML step 3).** The POML predates the Option B decision. Under Option B (spike §4 step 2 and §7 refinement 1), the registration client is a server-side confidential client with an MI-FIC credential, and its ID token never passes through the browser. So `registration-client.ts` only builds the redirect and maps the return. It creates no `PublicClientApplication` and makes no token or scope request. The criterion "the registration-only client never requests a BFF scope" holds by construction. Tests check the start URL, MSAL `initialize()` (never called) and a source scan (no MSAL import, `acquireToken*`, `login*`, `bffApiCall`). The `/organizations` authority now lives only in task 060.
2. **`/join` is handled in `main.tsx`, not `App.tsx` (deviation).** `App` mounts only after the realm chooser and the MSAL plane are ready, and every route sits behind `AuthGuard`. A pre-sign-in page cannot be an `App` route. `App.tsx` is unchanged.
3. **`staticwebapp.config.json` is unchanged (deviation).** The existing `navigationFallback` already serves `/join` from `index.html`, and a test now asserts this. Navigating to the service is a top-level navigation, which the CSP (`frame-ancestors` only) does not restrict.
4. **Key check.** The check is `readCustomerQueryKey` (same syntax as 041), then `directory.resolveByKey(key)` on the workforce-plane directory, then `validateBackend`, then the record's key must equal the link key. It does not use `resolveBackendSelection`, for two reasons:
   - that function's single-candidate fallback would accept an unknown key whenever only one backend is configured (a test guards this);
   - the workforce `resolveWorkforce()` is a **membership** lookup under T240c, and someone who has not joined is not a member yet.
   **T240c dependency:** the directory's `resolveByKey` must answer for workforce customers too. It is documented as the CIAM lookup today. While `VITE_USE_DIRECTORY_SERVICE=true`, the stub throws and every key is "not recognised" (fail closed).
5. **No oracle.** A missing, malformed, unknown, mismatched or refused key, and a directory failure, all give the same "Link not recognised" page. That page does not echo the key and lists no customer. When the flag is off, or inside Teams, the key is not looked up at all. The page inevitably reveals that a working link works, and the registration service would reveal the same.
6. **Hand-off** (`startWorkforceSignIn`): sets the tab realm to `workforce`, clears any active customer, and navigates to `/?customer={key}`. The 041 bootstrap then selects that customer's backend, with no chooser and no picker, and `AuthGuard` runs the Spaarke-tenant `loginRedirect`. The hand-off is a **Sign in** button rather than an automatic redirect, so the user reads the outcome before Microsoft's one-time "Review permissions" page (spike P8). It sends no `login_hint`. Adding one would mean task 060 returns the UPN; that is left out to keep the contract minimal.
7. **Teams (NFR-04).** The join flow is browser-only. Inside the Teams tab, `/join` shows "Open this link in your browser". It has no button, does not navigate the iframe and does not look up the key. Teams is told the app loaded, so its spinner stops.
8. **Dark mode (ADR-021).** The bootstrap wraps the page in `FluentProvider theme={resolveCodePageTheme()}`, the same as the other pre-auth screens. Every colour is a semantic token (`colorNeutral*`, `colorBrandForeground1`, `colorStatus{Success,Warning,Danger}Foreground1`). A test renders under `webDarkTheme`, and a source scan rejects hex, rgb and hsl values.

## 5. Acceptance criteria → evidence (`tests/JoinPage.test.tsx`)

| Criterion | Evidence |
|---|---|
| Flag on + known key: link → signed-in SPA in one flow (mock service) | "a known key shows the intro; continuing goes to the FIXED service URL…" → "registered: … "Sign in" hands off…" → bootstrap "hand-off: after "Sign in", the SPA root starts the workforce sign-in for the joined customer". In that last test the app mounts on the workforce plane with `fabrikam` active (scope `api://{fabrikam}/user_impersonation`) out of two backends, with no chooser and no picker. |
| Unknown key → "link not recognised", no service call | "an unknown key shows "link not recognised" and makes no service call"; 5 malformed-key cases; directory failure / wrong record / refused record; "no oracle" |
| Non-listed tenant and guest of another tenant → clear refusal | `tenant_not_allowed` → "Your company is not set up for access"; `guest_account` → "Use your own company account"; neither offers sign-in |
| Already registered → told to sign in and handed off | "already registered: told to sign in, and handed off to sign-in" |
| Registration-only client never requests a BFF scope | "the registration-only flow never requests a BFF scope…" (start URL has only `customer` + `state`; no `scope`, `api://`, `user_impersonation`, app id, `client_id` or `redirect_uri`; MSAL never initialised); "a crafted link cannot change the service host, scope or return address"; source scan |
| Flag off → "not available" | Flag tests (unset, `false`/`TRUE`/`1`/`yes`/empty/placeholder; flag on without a URL); bootstrap "flag off: /join shows "not available"" |
| typecheck, lint, tests, production build pass | §6 |

## 6. Verification (`src/client/external-spa`, Windows worktree)

| Check | Result |
|---|---|
| `npm run typecheck` | exit 0 |
| `npm run lint` | exit 0, no warnings |
| `npx vitest run tests/JoinPage.test.tsx` | **62/62 pass** |
| `npx vitest run` (full) | 496 tests pass, 0 fail. **Exit 1**, caused only by the pre-existing `tests/check-dist.test.ts` load failure described below |
| `npm run build` | exit 0 (only the existing >500 kB chunk warning) |
| `npm run check:dist` | exit 0: "1 file(s) clean" |

**Fail-before:**
1. With every task source removed and `main.tsx` at HEAD, the suite fails to load: `Failed to resolve import "../src/pages/JoinPage"`.
2. With the new modules present but `main.tsx` at HEAD: **4 failed / 58 passed**, namely the 4 bootstrap tests (flag on, flag off, Teams, hand-off).
3. Mutations, each run and then reverted:
   - state check disabled → 3 failures;
   - flag ignored → 7 failures;
   - service URL read from the link → 1 failure (crafted-link test);
   - unknown key falling back to the single backend → 2 failures.

Every source file was restored and compared byte-for-byte afterwards.

**Pre-existing failures (found in passing; not caused or changed by this task):**
- `tests/check-dist.test.ts` fails to **load** on a Windows checkout with `core.autocrlf=true`. The CRLF after the `#!/usr/bin/env node` shebang of `scripts/check-dist.mjs` makes Vite's import fail (`SyntaxError: Invalid or unexpected token`). With the script converted to LF, its 37 tests pass, and the base commit `db91ced2b` fails the same way. CI checks out on Linux (LF) and is not affected. Possible fix: a `.gitattributes` rule `src/client/external-spa/scripts/*.mjs text eol=lf`. Not changed here because it is outside the task's scope. Report to the operator.
- `tests/BffAccessState.signIn.test.tsx`, 2 tests, time out at the default 5000 ms under full-suite parallel load. They pass in isolation (5/5), and they fail the same way at base `db91ced2b`.

## 7. Quality gates (Step 9.5)

- **adr-check:** compliant on every ADR considered.
  - ADR-021: v9 only, tokens only, inside a `FluentProvider`, dark mode tested.
  - ADR-012: the shared `EmptyState` is reused and no new component was added to the shared library.
  - ADR-028: no `PublicClientApplication`, no raw bearer fetch, no `/organizations` or `/common` authority in the SPA. A7: the hand-off uses the existing tenant-pinned workforce client.
  - ADR-038: tests written; no KEEP-path deletion.
  - ADR-006, ADR-026: the SPA's existing Vite build; no new surface type.
  - Out of scope: ADR-001/002/007/008/010/013/052 (no BFF or Dataverse change) and ADR-050 (no modal).
  - Warning, pre-existing: the SPA uses React 18 while ADR-021/026 say React 19 for code pages. Not touched here.
- **code-review:** no Critical findings.
  - Both new files are over 200 lines, and each is cohesive: the client covers one flow's URL and state contract, and the page is one state-to-view mapping.
  - The `useEffect` is StrictMode-safe: the `cancelled` check comes before the single-use `consumeJoinState`.
  - Suggestions not acted on:
    - add a `login_hint` on the hand-off (decision 6);
    - with the flag off the join code still ships in the bundle, as inert UI. It holds no secret, and `check:dist` is clean.

## 8. Not done / for other tasks

- **Task 060:**
  - implement the contract in §3;
  - wire the two env vars into `deploy-external-spa.yml`;
  - register `/join` as the service's fixed return address;
  - prove the end-to-end flow with the "Spaarke Test Partner" tenant (the POML `<ui-tests>`, NFR-07 stamp-shaped configuration).
- **T240c:** `resolveByKey` for workforce customers (decision 4).
- The POML `<ui-tests>` were not run: the service does not exist, and no live calls were allowed.

## 9. Review fix round

Two adversarial passes found no F-class defects; the items below were fixed in one round. Evidence is `tests/AuthGuard.signInFailure.test.tsx` and `tests/JoinPage.test.tsx`.

1. **Sign-in redirect loop on login failure (pre-existing).** `AuthGuard` re-fired `loginRedirect` whenever there was no account and MSAL was idle, and nothing handled a failed redirect, so a new guest who declined Entra's "Review permissions" page (`#error=access_denied`, AADSTS65004) was bounced to Entra forever. The same held for any error Entra returns to the redirect URI.
   - `msal-auth.ts`: the single event callback `syncActiveAccountOnInteractiveSignIn` (registered before `initialize()`, so it sees the response) now also records an `ACQUIRE_TOKEN_FAILURE` of a **redirect** interaction (msal-browser 5.5 emits it for a rejected `handleRedirectPromise`; it has no `LOGIN_FAILURE` event). `interaction_in_progress` and silent/popup failures are ignored. A later successful interactive sign-in clears it. Only the MSAL `errorCode` (a plain identifier) and an `AADSTS` number are kept; the raw description is never stored or shown.
   - `AuthGuard.tsx`: with a recorded failure and no account the gate renders `components/auth/SignInFailed.tsx` ("Sign-in did not complete", the safe code, **Try again**) and starts no redirect. Only the **Try again** click calls `loginRedirect` (and a start that fails says so).
   - Both planes (CIAM and workforce browser) go through the same callback and gate. **Teams is not affected:** `AuthGuard` returns its children before the account gate in Teams, and the Teams bootstrap acquires its token by NAA / popup before `<App>` mounts, so there is no redirect; a test asserts it.
   - Not changed (listed): a *signed-in* user whose later token redirect (`acquireStandaloneBffToken`) comes back with an error is not gated by this screen; the failure is recorded but the app stays up. A declined re-consent there could loop through `bff-client`; no evidence it is reachable today.
2. **`unavailable` outcome.** Added to `JOIN_RESULT_CODES`; shown as "Joining isn't available right now" with no button (the view the start-failure path already used). `error` stays the transient "Try again".
3. **Stale-state wording.** "Let's start again" now says that if you already completed sign-up you can just sign in, and offers **Sign in** (the `already_registered` hand-off) beside **Start again**.
4. **Accessibility.** Every join state renders a real `h1`. The shared `EmptyState` gained an optional `headingAs` prop (omitted: the unchanged `<span>`; one test pins it). Focus moves to the primary action, or to the heading when there is none, on every state change, and `document.title` follows the heading. The transient "Checking your link" spinner has no heading and only sets the title.
5. **Teams terminal screen.** `renderJoin` in Teams now also calls the new best-effort wrapper `notifyTeamsSuccess()` (`TeamsHostAdapter.ts`, next to `notifyTeamsAppLoaded` / `notifyTeamsFailureOther`; no wrapper existed for `notifySuccess`).
6. **Tests that proved less than their titles.** The dark-theme test is renamed to what it checks (a clean dark mount; token colours are covered by the source scan). The hand-off bootstrap test now also asserts the login scope the account gate would request. The wrong-state test now also renders the legitimate state afterwards and expects "start again" (a mismatched return consumes the stored state).
