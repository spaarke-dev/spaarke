# Task 058 notes: SPA multitenant sign-in, tid → backend, silent/interactive link, profile form

> Status: in progress (2026-10-18). Rigor FULL. POML `tasks/058-spa-multitenant-signin-profile-form.poml`; design `notes/a9-bff-design.md` §5/§7.

## Escalation trigger check (POML `<escalation>`): ssoSilent across tenants vs third-party cookies

- `ssoSilent` runs in a hidden iframe on `login.microsoftonline.com`. The MSAL.js docs say that browsers which block or partition third-party cookies make iframe-based silent calls fail with `interaction_required`/`login_required`; the app must then fall back to an interactive call.
  - **Safari** (ITP): third-party cookies are blocked, so the silent link fails.
  - **Firefox** (Total Cookie Protection): cookies are partitioned, so the silent link fails.
  - **Chrome and Edge** (2026): third-party cookies are allowed by default, so the silent link works.
- **Decision:** the link flow is NOT changed, so the trigger's "before changing the link flow" does not fire. Design §5 already covers a failed silent attempt: the profile form shows; on submit the BFF answers `licensed_user_link_required` for a licensed user's email; the SPA then runs the popup.
- **Known limit (K2, fails closed):** in Safari and Firefox, a licensed user sees the profile form once, then the "Link my account" popup.
- **Reported to the owner in the 058 completion report.**

## Implementation plan (decided 2026-10-18; follow it after compaction)

### BFF contract (read from code)

**`GET /api/v1/external/me`** (403, `registration_required`)
- Problem body: `reasonCode` = `sdap.access.registration_required`, plus `givenName`, `familyName` and `email` (`CallerPrincipalResolver.cs:676-694`).

**`POST /api/v1/external/registration`**
- Body (closed, camelCase): `{firstName, lastName, department?, confirmExistingRecord?}`.
- Success: 201 or 200 `{contactId, systemUserId, outcome}`.
- Refusals: problem `reasonCode` = `sdap.access.<code>`:
  - 403 `licensed_user_link_required`;
  - 409 `existing_record_found`;
  - 403 `registration_contact_conflict`;
  - 403 `registration_email_missing`;
  - 500 `registration_owner_unconfigured`;
  - 503 `registration_unavailable`;
  - 400, with `errorCode` `registration_invalid`.
- Source: `ExternalRegistrationEndpoints.cs`.

**`POST /api/v1/external/workforce/link`**
- Body: `{guestAccessToken}`.
- Success: 200 `{systemUserId, outcome: linked|already_linked}`.
- 403 `reasonCode`:
  - `sdap.access.link.no_systemuser`, `systemuser_disabled`, `link_collision`, `systemuser_ambiguous`;
  - the guest-token deny codes.
- 503 `sdap.access.link.unavailable`; 500 `sdap.access.link.environment`.
- Source: `ExternalWorkforceLinkEndpoints.cs`.

### Code by file

**`config.ts`**
- `getWorkforceHomeClientConfig()` reads the new `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`, the multitenant client, lazily.
- It fails loud on missing, placeholder or non-GUID, and must differ from `VITE_TEAMS_MSAL_CLIENT_ID`.

**`msal-config.ts`**
- New kind `workforceHome`, with discovery authority `https://login.microsoftonline.com/organizations`.
- `workforceHomeTokenAuthority(tid)` returns `…/{tid}`.
- Exported `assertWorkforceHomeAuthority(authority, clientId)`:
  - passes only for the multitenant client id, with the `organizations` or `{GUID}` segment;
  - `common` is refused for both clients;
  - the A8 client id is refused.
- `assertTenantPinnedWorkforceAuthority` is unchanged for the A8 client.
- Option `cacheLocation: 'memoryStorage'` for the link instance.

**`backend-selection.ts`**
- `Backend.homeTenantIds?: string[]`: a GUID list, lower-cased; parsed from `VITE_BACKENDS`.
- `resolveHomeBackendSelection(tid)` returns `selected | pick | none | notSetUp`. It uses `directory.resolveByHomeTenant` when present, else `resolveWorkforce()` filtered by tid.
- `landingCustomerName(search)` names the customer for RealmChooser.

**`msal-auth.ts`**
- `StandaloneAcquireOptions.authority`: added to the silent and redirect requests only when set.
- An account-predicate seam for `accountsForActivePlane`:
  - before sign-in: home accounts only, i.e. `login.microsoftonline.com`, a GUID tenant, not the Spaarke tenant;
  - after sign-in: tenant = tid.
- `acquireSpaarkeGuestToken(mode silent|popup, loginHint, bffScope)`:
  - uses a memoized A8 client instance with `memoryStorage` and redirect URI `/auth-callback.html`;
  - silent: `ssoSilent`, and any failure → null;
  - popup: `acquireTokenPopup`; a user cancel → `LinkCancelledError`;
  - always calls `clearCache()` afterwards (the guest token is never stored).

**`standalone-plane.ts`**
- `prepareWorkforceHomePlane({pickedKey})`:
  1. memoized multitenant instance;
  2. sync active account;
  3. `initialize`;
  4. home account → tid → `resolveHomeBackendSelection` → activate → plane.
- The plane carries: acquireOptions `{tenantId: tid, silentRedirectUri, authority: /{tid}}`, `loginScope` `openid`, and the active home account seam.
- Results:
  - no account → `signIn` (App mounts; AuthGuard redirects with OIDC scopes only);
  - unknown tid → `notSetUp`, with no token and no BFF call.
- `resolveStandalonePlane('workforce')` no longer builds the A8 browser plane.

**`registration-client.ts`** (rewritten)
- BFF calls for registration and link.
- `parseRegistrationRequired(err)`.
- Client-side validation (≤ 50 / ≤ 100, no control characters).

**`components/auth/WorkforceHomeGate.tsx`** (new)
- Active only on the home plane.
- Order: `/me` → on `registration_required`, the silent link → link POST → re-probe `/me`.
  - Silent failure, or link `no_systemuser` → profile form.
  - Other link refusals → admin message.
  - 503 → retry.
- Publishes readiness, so the bell waits.

**`pages/RegistrationPage.tsx`** (replaces `JoinPage.tsx`, which is deleted with `JoinPage.test.tsx`)
- Fields: first name, last name, department; email read-only.
- States:
  - `existing_record_found` → confirm screen → resubmit with `confirmExistingRecord: true`;
  - `licensed_user_link_required` → "Link my account" button (popup on click); a cancel shows an explanation and sends no registration POST;
  - `contact_conflict` → admin message;
  - 5xx → generic error with retry.

**`main.tsx`**
- The `/join` route is removed.
- The `workforce` realm runs `prepareWorkforceHomePlane`.
- New screen "Your organization is not set up for Spaarke", with Sign out and Choose a different sign-in.

**`App.tsx`**
- The gate wraps `RecordDeepLink` and `Routes`.
- The bell is gated on readiness.

**`RealmChooser.tsx`**
- "Work for {customer}? Sign in with your company account".

**Configuration files**
- `vite-env.d.ts` and the `.env.*` files: JOIN vars removed; `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` added.
- Workflow: the new var in both env blocks, with `need` + GUID match + a check that it differs from the A8 client id.

### Tests

- **New:** `tests/workforce-home-auth.test.ts` covers acceptance criteria 1–7.
- **Updated:** `workforce-auth.test.ts` (browser "My organization" A8 tests at :284-313 and :834-880) and `backend-selection.test.ts:644` (`resolveStandalonePlane('workforce')`).
- **Deleted:** `JoinPage.test.tsx`, because its subject is removed.

### Follow-ups for task 059 (dev setup; no live change in 058)

- The new multitenant client needs SPA redirect URIs for the origin **and** `/auth-callback.html` (the silent iframe retry).
- GitHub env `external-spa-dev`:
  - `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` (the workflow now fails closed without it);
  - `VITE_BACKENDS` with `homeTenantIds`, because the one-entry `VITE_BFF_API_URL` fallback has none, so every home sign-in shows "not set up".
- The A8 client `9f1da340` already has `/auth-callback.html`; it is used for the guest-token acquisition.


## Implementation record (2026-10-19; implementer, Sonnet; not committed, not pushed, not deployed)

### Files

**Added**
- `src/client/external-spa/src/components/auth/WorkforceHomeGate.tsx`: the `/me` probe, the silent stored link and the profile-form hand-off, active only on the home plane in a browser.
- `src/client/external-spa/src/pages/RegistrationPage.tsx`: the profile form and its refusal screens (existing record, link required, conflict, email missing, blocked, sign-in, error); also exports `LinkBlockedNotice`.
- `src/client/external-spa/src/components/auth/OrganizationNotSetUp.tsx`: "Your organization is not set up for Spaarke", with Sign out and Choose a different sign-in.
- `src/client/external-spa/tests/workforce-home-auth.test.ts`: 86 tests (the task's contract).

**Changed** (all under `src/client/external-spa/` unless noted)
- `src/config.ts`: `isGuid`; `getWorkforceHomeClientConfig()` (lazy, fail-loud, must differ from the A8 client).
- `src/auth/msal-config.ts`: authority kind `workforceHome`; `assertWorkforceHomeAuthority` (organizations or a tenant GUID, only for the multitenant client id); `workforceHomeTokenAuthority(tid)`; `cacheLocation` option.
- `src/auth/backend-selection.ts`: `Backend.homeTenantIds`; `resolveHomeBackendSelection`; `landingCustomerName`.
- `src/auth/msal-auth.ts`: per-request `authority` on the silent and redirect calls; the home-account predicate seam; the home-plane flag; `acquireSpaarkeGuestToken` and `GuestLinkTokenError` (A8 client, memory cache, cleared after each call).
- `src/auth/standalone-plane.ts`: `prepareWorkforceHomePlane`; `resolveStandalonePlane('workforce')` now throws; `applyStandalonePlane` applies `loginScope`, the account filter and the home flag, and refuses a non-`user_impersonation` scope on the home plane.
- `src/auth/registration-client.ts`: rewritten to the BFF routes (`/api/v1/external/registration`, `/api/v1/external/workforce/link`) through `bffApiCall` only; the 056 registration-service hand-off is gone.
- `src/main.tsx`: `/join` route removed; the workforce realm runs `prepareWorkforceHomePlane`; the not-set-up screen.
- `src/App.tsx`: the gate wraps the routes; the bell waits for `homeReady`.
- `src/components/auth/RealmChooser.tsx`: "Work for {customer}? Sign in with your company account".
- `src/vite-env.d.ts`, `.env.example`, `.env.development`, `.env.production`: JOIN and registration-service variables removed; `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` added.
- `.github/workflows/deploy-external-spa.yml`: the new variable in both env blocks, required, GUID-checked, and checked to differ from `VITE_TEAMS_MSAL_CLIENT_ID`.
- `tests/workforce-auth.test.ts` and `tests/backend-selection.test.ts`: retargeted from the A8 browser plane to the A9 home plane (see Tests).

**Deleted**: `src/pages/JoinPage.tsx`, `tests/JoinPage.test.tsx` (their subject is removed).

### Decisions and deviations from the plan

1. **Account filter is tenantId-only** (a GUID that is not the CIAM tenant). It does not exclude the Spaarke tenant: excluding it would send the sign-in round forever. A Spaarke-tenant sign-in is an unlisted tid and gets the not-set-up page.
2. **Login scope on the home plane is always `'openid'`**, so `BffAccessState` needed no change (the POML file list had it). The BFF scope is requested only per request, on authority `/{tid}`.
3. **The gate handles `registration_required` inside `AuthGuard`** (not before it), so a token for the signed-in tenant's backend exists when `/me` is read.
4. **`prepareWorkforceHomePlane` calls `handleRedirectPromise()`** on the home instance before reading accounts: the backend depends on the signed-in tid, and `initialize()` does not process the login response. MsalProvider calling it again is safe (memoized).
5. **Home-plane acquirer fails closed**: a scope that is not `api://{app id}/user_impersonation` is rejected before MSAL is asked for anything.
6. **`.env.development` documents the variable but does not declare it**: a value there would outrank `.env.local` (Vite mode files win).
7. **`resolveStandalonePlane('workforce')` throws** (points at `prepareWorkforceHomePlane`); CIAM is unchanged.
8. **Deviation, tests**: `tests/workforce-auth.test.ts` and `tests/backend-selection.test.ts` were edited, not only the new file. They asserted the A8 browser plane the task replaces (tenant-pinned Spaarke authority, `resolveStandalonePlane('workforce')`). The CIAM and Teams tests are untouched. `backend-selection.test.ts` also got `handleRedirectPromise` and `addEventCallback` on its fake MSAL instance (the home plane calls both).

### Tests

- New file: 86 tests, written first. Before the change (HEAD sources, new and retargeted tests): 83 failed / 149 passed of 232 (76 of the new file's 86 fail; 6 in `workforce-auth.test.ts`; 1 in `backend-selection.test.ts`; the passing ones are negative or unchanged-CIAM/Teams controls). After: all pass.
- Full suite after: 37 files, 1043 tests, 0 failed (run with `--pool=forks --poolOptions.forks.maxForks=4`; the default pool times out under load, as it did at baseline: 24 timeouts that pass in isolation).
- typecheck 0, lint 0 (`eslint src tests`), build 0, `check:dist` clean (1 file, no mock marker, routes match the baseline).
- Extra tests beyond the stated contract, one line each:
  - Guest-token group (5): the hard rules (memory cache, cleared after, one-shot, scope check) are security-relevant and have no other test.
  - Bootstrap group (7, `main.tsx`): AC2 (zero BFF calls) can only be shown through the real bootstrap.
  - Landing wording (5): the task's landing-wording line; includes "unknown customer key is never echoed".
  - Removed-strings and env-file checks (3 groups) and workflow checks (2): AC7 and the fail-closed deploy.
  - `?customer=` and stored-key filtering, directory-failure fail-closed: the named edge cases for the tid-to-backend choice.

### Observations (not fixed)

- `src/api/me-client.ts:109` rollout fallback chooses `teamsHost ? 'workforce' : 'ciam'`; with the home plane in a browser the fallback is not the home plane. Not touched.
- `src/host/TeamsHostAdapter.ts:149` has a stale `/join` comment (outside this task's files).
- `notifyTeamsSuccess` (`src/host/TeamsHostAdapter.ts:153`) is possibly unused now (an exported function, harmless).
- Pre-existing "/common" text in comments in `msal-config.ts`.

### Follow-ups for task 059 (unchanged from the plan, plus one)

- The multitenant client needs SPA redirect URIs for the origin and `/auth-callback.html`.
- GitHub env `external-spa-dev` needs `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` and a `VITE_BACKENDS` whose entries carry `homeTenantIds`. The one-entry `VITE_BFF_API_URL` fallback lists none, so every home sign-in shows "not set up".
- Safari and Firefox block third-party cookies, so the silent link fails there: a licensed user sees the profile form once and then the "Link my account" popup (known limit K2, fails closed).

## Review fix round 1

Findings from the review of the uncommitted change set, fixed in `src/client/external-spa` (nothing committed).

| Finding | Change | Proving test |
|---|---|---|
| F-A: guest-token cleanup wiped the home plane | `msal-auth.ts` `acquireSpaarkeGuestToken` `finally` now calls `dropGuestLinkAccounts`: `clearCache({ account })` per account the guest instance holds, never the no-account form. msal-browser 5.5.0: no-account `clearCache()` = `BrowserCacheManager.clear()` removes every sessionStorage key containing "msal" (temporary cache is always sessionStorage); the account form = `clearCacheOnLogout` -> `BrowserCacheManager.removeAccount` on the instance's own (memory) store. | F-B |
| F-B: test for F-A | New `tests/guest-token-cache-isolation.test.ts`: REAL `@azure/msal-browser`, a real sessionStorage home instance seeded through `hydrateCache`, the guest flow through the real `acquireSpaarkeGuestToken` with only `ssoSilent` / `acquireTokenPopup` stubbed (they write via the real `hydrateCache`); 5 paths + a repeat call. Asserts home keys unchanged, home account re-readable, guest instance has no account/token (internals `controller.browserStorage.getTokenKeys()`), no guest token in storage, and every `clearCache` call names an account. `BroadcastChannel` is stubbed (Node/jsdom event mismatch). `workforce-home-auth.test.ts` fake `clearCache` now removes the named account and its guest tests assert account-scoped clears. | Fails before (6/6, "home key msal.version survived: expected undefined"), passes after (6/6). Mutation: no-op cleanup fails 6/6 on "guest instance holds nothing". |
| F-C: stale A8 interaction lock | `standalone-plane.ts` `clearStaleGuestInteractionLock(a8ClientId)`, called in `getHomeInstance` before the home instance is created. Key `msal.interaction.status`, value `{"clientId","type"}` (5.5.0 `setInteractionInProgress`). Removes the key only when `clientId` equals the A8 client id (case-insensitive). | 7 tests in `workforce-home-auth.test.ts` ("a stale A8 (guest popup) interaction lock"): A8 lock removed (other keys kept), case-insensitive, home / CIAM / other-client locks kept, unparsable kept, no lock no-op. With the call removed the two must-fire tests fail. |
| F-D: "Try again" lost the confirmation | `RegistrationPage.tsx`: error view is `{ kind: 'error'; confirm: boolean }`; `refusalView(reason, confirm)`; "Try again" calls `submit(view.confirm)`. Dead `retry` field removed (a link failure never reaches this view). | "409 -> confirm -> 503: Try again resends WITH confirmExistingRecord true" (fails with `submit(false)`: bodies `[false,true,false]`). |
| F-E: CIAM defaults for a home-plane user | `me-client.ts`: `basePlane = teamsHost || isWorkforceHomePlaneActive() ? 'workforce' : 'ciam'` (`msal-auth` is already imported by `bff-client`; no cycle). | `no-mock-identity.test.tsx` "the browser workforce HOME plane + a 502/503/500 falls back to the WORKFORCE defaults" (fails without the change); the existing CIAM 5xx tests are unchanged. |
| K-1: unmapped denies offered a retry | `registration-client.ts`: any other 403/409 registration code -> new refusal `blocked` -> the administrator ("conflict") screen; link 500 `sdap.access.link.environment` -> `blocked` -> the link-blocked screen. Other 5xx / network keep the retry. | 4 registration cases + silent and popup link-environment + "other link 5xx keeps the retry" (6 fail without the change). |
| K-2: reserved tenants in `homeTenantIds` | `backend-selection.ts` `validateHomeTenantIds` refuses the CIAM tenant (`MSAL_TENANT_ID`) and Spaarke's tenant (`VITE_TEAMS_MSAL_TENANT_ID`, when the build sets it). Config entry throws; a directory record is dropped. | 2 tests (must-fire and must-not-fire); both fail with the check disabled. |
| K-3: picked key not re-checked | `prepareWorkforceHomePlane` picked path re-runs `resolveHomeBackendSelection(tid, { search: '' })`; the picked key must be among the entries that list the tid, else `clearActiveBackend()` and `notSetUp`. | "a picked key whose entry does not list the signed-in tid is not set up" and "a picked key that lists it still continues with a different ?customer= in the URL". |
| K-4: not-set-up wording | `OrganizationNotSetUp.tsx` text: use "Sign out" first to use a different company account; both actions kept. | "the not-set-up text tells the person to sign out ...". |
| K-5: wording | `config.ts` two error strings say "company account" sign-in; `.env.production` comment says the variable is REQUIRED (workflow preflight). | Full suite (the production-package and env tests). |

Notes for the next reader:
- `acquireSpaarkeGuestToken` keeps its memoized memory-only instance. Cleanup relies on every cached token having an account entity (true for every msal-browser flow and for `hydrateCache`); the new test asserts the instance's token key lists are empty.
- Verification: typecheck clean, lint clean, full vitest 38 files / 1072 tests pass, `npm run build` and `npm run check:dist` pass. `--poolOptions.forks.maxForks=4` alone fails in this vitest (2.1.9: "minThreads and maxThreads must not conflict"); it needs `--poolOptions.forks.minForks=1` as well.
- Out of scope, left for task 075: the stale /join comment and the unused `notifyTeamsSuccess` export in `src/host/TeamsHostAdapter.ts`; spec/design /join doc drift.

## Quality gates (main session, 2026-10-19)

- **adr-check:** 0 violations, 0 warnings.
  - ADR-028 A9: `organizations` only for the multitenant client, only for discovery; BFF token authority `/{tid}` only after the tid matches configured `homeTenantIds`; A8 still refuses `organizations`; no `common`.
  - ADR-028 core: the Bearer header only at the existing allowed `bff-client.ts` site; direct PCA under the collaboration-host exemption.
  - ADR-021/012: Fluent v9, tokens, shared EmptyState.
  - ADR-050: n/a (the confirm screen is a page state).
  - ADR-038: Vitest unit tests.
- **Adversarial passes:** 2 (auth). Both independently found the no-account `clearCache()` wipe of the whole sessionStorage MSAL cache, proved with real msal-browser 5.5.0. Fix round 1 above closed F-A…F-E and K-1…K-5.
- **Re-check of the fix diff:**
  - main session read `dropGuestLinkAccounts` and `clearStaleGuestInteractionLock` (runs once at home-instance creation, so it cannot cancel a live popup);
  - the new real-MSAL test fails 6/6 on the pre-fix code.
- **Final verification (main session):** typecheck clean; lint clean; vitest 38 files / 1072 passed; build OK; check:dist clean.
- **Carry-over to 075:** recorded in its POML `<notes>`.

rework: fix-round-1 task 058
