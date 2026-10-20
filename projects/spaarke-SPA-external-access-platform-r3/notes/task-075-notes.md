# Task 075 notes: Teams → multitenant client through NAA (Teams context tid), registration and link in Teams

> Status: in progress (2026-10-19). Rigor FULL (auth), sonnet @ high, directional. POML `tasks/075-spa-teams-multitenant-naa.poml`. Design: `notes/a9-bff-design.md` §5, §7, §12. ADR-028 A9 (Teams amendment).

## Escalation trigger check

> POML trigger: "If NAA with a tenant-specific authority other than the host's is refused by the Teams NAA bridge in documentation or tests, STOP and escalate."

The researcher checked this on 2026-10-19; full notes are in `.claude/agent-memory/researcher/naa-teams-tenant-authority-guest-2026-10-19.md`.

- **(A) The multitenant client, authority = the Teams context tid (the host's own tenant):** documented as supported (the Teams NAA sample uses a tenanted authority).
- **(B) The A8 client, authority = the Spaarke tenant (cross-tenant, B2B guest token) through NAA:** **undocumented**, neither supported nor refused. No bridge error code is tenant-specific.
- **Decision:** the trigger does not fire. (B) is best effort, with a fail-closed fallback. Task 071 probes it live.

## Facts from the msal-browser 5.5.0 source that change the code

These are a source reading; they need confirming in task 071.

- **Authority:** under a live NAA bridge, MSAL sends Teams only the **per-request** `authority`. The configured `auth.authority` is never sent (`NestedAppAuthAdapter.toNaaTokenRequest`).
  - Today's `acquireBffTokenViaNaa` sets no per-request authority, so the "pinned to the Spaarke tenant" claim held only on the popup fallback.
  - **075 sets `authority` on every NAA and popup request.**
- **Login hint:** the bridge `TokenRequest` has no `loginHint` field, so `ssoSilent({ loginHint })` loses the hint under NAA. Do not rely on it.
- **Active account:** under NAA, `getActiveAccount`/`setActiveAccount` are NOT no-ops (corrected in review round 1): in 5.5.0 `NestedAppAuthController` reads and writes the controller's cache through `AccountManager`. NAA still has one host account, so the active account is that account once set.
- **Two NAA instances** with different client ids are undocumented. The source suggests they work: requests are matched by id and the cache is namespaced by client id. Task 071 probes this.
- **Popup fallback:** the standard `PublicClientApplication` popup inside Teams is not documented for desktop or mobile; the documented path is `authentication.authenticate()`. The existing repo fallback uses a standard PCA popup, and this task keeps that. Task 071 checks desktop, web and mobile. If the popup fails there, a follow-up switches to `authentication.authenticate()`.

## AC3 interpretation: owner-confirmed 2026-10-19 ("Form; popup on click")

- **AC3 literally:** "registration_required in Teams → silent link via NAA → popup → form".
- **Design §5 (browser, task 058):** the popup runs only after the registration POST returns `licensed_user_link_required`, from a click.
- **Read literally,** every new contact in Teams would get a gesture-less Spaarke-tenant sign-in popup, and it would fail for them: contacts are not guests there.
- **Recommendation, implemented here:**
  1. NAA silent link;
  2. on failure, the profile form;
  3. `licensed_user_link_required` → the "Link my account" click → popup.
- This matches the browser and design §5. The popup step is one switch point, so the owner can change it.

## Implementation plan

### `src/auth/msal-auth.ts`

- **Teams BFF strategy.** It moves from the A8 client to the multitenant client (`getWorkforceHomeClientConfig().clientId`). The authority is `https://login.microsoftonline.com/{tid}`, where tid is the Teams context tid, validated against the directory before use. The same config is used for both the NAA instance and the standard popup-fallback instance, with the 058 home assert.
  - Set `authority` on every request: `acquireTokenSilent`, `ssoSilent` and `acquireTokenPopup`.
  - **Fail closed on a tid mismatch:** a token whose `result.tenantId` (case-insensitive) is not the selected tid is never returned or used. Throw `TeamsWorkforceAuthError`.
  - Keep the ladder: NAA (when `isNAAChannelRecommended`), then the popup; user-cancel opens no second popup; `noInteraction` gives `SignInRequiredError`; never a redirect.
  - Memo keys include the tid.
- **Guest link in Teams.** `acquireSpaarkeGuestToken('silent')` in Teams uses a nestable instance for the A8 client:
  - Spaarke authority, set per request; `memoryStorage`.
  - Any failure, or a result whose tenant is not the Spaarke tenant, is `null`.
  - `'popup'` stays on the existing standard-PCA guest instance, from a click only.
  - Cleanup is account-scoped on both instances, never a no-account `clearCache()`. NAA `clearCache` may throw; catch it.
- **Teams home plane flag.** Teams sets `setWorkforceHomePlaneActive(true)` and the tid account filter, so the 058 gate and `/me` fallback treat Teams as the home plane.

### `src/main.tsx` and `src/host/TeamsHostAdapter.ts`

- **Backend selection** (`bootstrapTeams`) is by the Teams context tid, read from `app.getContext()` → `user.tenant.id` (a GUID, lower-cased). It uses `resolveHomeBackendSelection(tid)`:
  - selected → activate, then auth;
  - pick → the customer picker, with the 058 re-check of the picked key against the tid;
  - notSetUp, none, or a missing or invalid tid → the Teams not-set-up screen. That sends zero token requests and zero BFF calls, plus `notifyAppLoaded` and `notifyFailure(Other)`.
- **Adapter config:** the adapter's auth config uses the multitenant client and the tid.
- **058 carry-over:**
  - remove the stale `/join` comment (TeamsHostAdapter.ts ~149);
  - remove the unused `notifyTeamsSuccess` export (~153), if it has no callers;
  - fix the `/join` wording in `spec.md` and `design.md` to the profile form.
- No `getAuthToken`, and no `webApplicationInfo`. The manifest belongs to task 076.

### `src/components/auth/WorkforceHomeGate.tsx`

- `isHomeGateActive` is true whenever the home plane is active, in both hosts.
- The guest silent link in Teams goes through the NAA branch above.

### `src/components/auth/OrganizationNotSetUp.tsx`

- A Teams variant with no Sign out and no "choose a different sign-in", since Teams has no realm chooser.
- Fluent v9 tokens; it follows the Teams theme.

### Tests: `tests/teams-home-auth.test.ts` (new)

Mock teams-js (context tid, NAA recommended or not) and msal-browser. Cover:

1. A listed tid uses NAA with the multitenant client, with the per-request `authority` = `/{tid}`; an NAA failure → the popup with the same authority.
2. An unlisted, missing or invalid tid → not set up, with zero token requests and zero fetches.
3. `registration_required` in Teams:
   - NAA guest silent (A8 client, Spaarke authority per request), then the link POST, then `/me`;
   - NAA failure → the form;
   - `licensed_user_link_required` → the click → popup.
4. A result whose `tenantId` is not the tid → refused (fail closed); a guest result not from the Spaarke tenant → null.
5. No `getAuthToken` in `src/`, and no `webApplicationInfo` in `appPackage/`.
6. The 058 browser tests and the CIAM tests pass unchanged.

### Follow-ups

- **Task 071 live probes:**
  - the `tid`/`acct` claims of the NAA home token (also: what the NAA account's `homeAccountId` / `username` look like for a B2B guest, since the Teams guest screen relies on them);
  - (B), the Spaarke-tenant guest token through NAA, with the exact error if refused;
  - the standard popup in Teams desktop, web and mobile;
  - two NAA instances together.
- **Task 076:** corrected in review round 1. The Teams tab's `contentUrl` is the external-SPA deployment (`appPackage/manifest*.json`: `external.spaarke.com`, or the dev Static Web App), and `deploy-external-spa.yml` already requires `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`. `deploy-teams-app.yml` only sanity-builds the SPA and zips `appPackage/`; it ships no runtime bundle, so the missing variable there is NOT a runtime failure. 076 may still add the variable to that sanity build so the build mirrors the real one.


## Implementation record (2026-10-19; implementer, Sonnet; not committed, not pushed, not deployed)

### Files (all under `src/client/external-spa/` unless noted)

**Changed**
- `src/auth/msal-config.ts`: `workforceHomeAuthorityConfig({ clientId, tenantId? })`. With a tenant the authority is `https://login.microsoftonline.com/{tid}` (the Teams instances); without it, `organizations` (the browser, unchanged).
- `src/auth/msal-auth.ts`:
  - The Teams BFF strategy now uses the multitenant client on `/{tid}` (`TeamsWorkforceAuthConfig.clientId` = the home client, `.tenantId` = the Teams context tid).
  - `authority` is set per request on the NAA `acquireTokenSilent` / `ssoSilent` / NAA popup, and on the standard-instance silent and popup calls.
  - `assertTeamsResultTenant`: a result whose `tenantId` (account tenant as the fallback; none = refused) is not the tid is never returned and its account is never made active.
  - A non-GUID tid throws `TeamsWorkforceAuthError` before any MSAL instance or host call.
  - `setGuestLinkHost('browser'|'teams')` and `acquireSpaarkeGuestTokenViaNaa` (the Teams silent guest link).
  - The section header is rewritten for A9.
- `src/auth/standalone-plane.ts`: `pickedKeyListsTenant(tid, key, directory?)`, extracted from `prepareWorkforceHomePlane` (identical behaviour; 058's tests pass unchanged) so the Teams picker uses the same re-check.
- `src/host/TeamsHostAdapter.ts`:
  - `readTeamsPreAuthContext()`: `context.user.tenant.id` -> GUID-validated, lower-cased, else null; also wires the Teams theme for the pre-auth screens.
  - `createTeamsHostAdapter(tenantId)`; the auth config is the multitenant client + the tid.
  - After auth setup the tab is the home plane: `setWorkforceHomePlaneActive(true)`, the tid account filter, `setGuestLinkHost('teams')`.
  - 058 carry-over: the stale `/join` comment and the unused `notifyTeamsSuccess` export are removed (no callers in `src/` or `tests/`).
- `src/main.tsx`: `bootstrapTeams` reads the tid, then `resolveHomeBackendSelection(tid)` BEFORE any token request.
  - `selected` -> activate, then auth.
  - `pick` -> picker, with the `pickedKeyListsTenant` re-check.
  - `notSetUp`, a missing or invalid tid, or an unreadable context -> the Teams not-set-up screen.
  - `none` (directory failure) -> the existing "No organization available" screen.
- `src/components/auth/OrganizationNotSetUp.tsx`: `variant="teams"` shows a message only (no buttons, no tenant id).
- `src/components/auth/WorkforceHomeGate.tsx`:
  - `isHomeGateActive()` is now just the home-plane flag (Teams and browser). The `teamsHost` parameter and prop are gone; `App.tsx` is updated.
  - **The one switch point for the popup decision is `afterSilentLinkFailure(prefill)`**: it returns the form. To open the popup earlier, change that function only.
- `src/App.tsx`: calls `isHomeGateActive()` and `<WorkforceHomeGate onReady>`.
- `spec.md` and `design.md` (project root): the `/join` route and "join page" wording now describe the profile form at first sign-in (FR-13, C7 headings and bullets, the Affected Areas line, the gap-analysis row, the acceptance line, the picker-storage line, the R4 fallback line). Minimal edits only; the superseded registration-service text (FR-14) is not rewritten.

**Added**
- `tests/teams-home-auth.test.ts`: 55 tests.

**Not changed**
- `TeamsHostContext.tsx` (listed in the POML) needed no change: it only carries the host flag to `BffAccessState`.
- `config.ts`: `getWorkforceHomeClientConfig()` already fits.
- The CIAM plane, the browser behaviour of 058, `.claude/`, the BFF, `appPackage/` and the workflows.

### Design decisions (beyond the plan)

1. **Configured authority is `/{tid}`** for both Teams instances (not `organizations`), so no multi-tenant alias exists anywhere in Teams. The per-request authority is the same value.
2. **The guest NAA instance is created per call, never memoized.**
   - Source reading: `NestedAppAuthController.clearCache` always throws "unsupported", and the cache lookup serves later requests from the instance cache by the bridge account.
   - So the only way the guest token lives "for the link call only" is to drop the instance (and its memory cache) after the call.
   - Cleanup is still account-scoped (`dropGuestLinkAccounts`), and the NAA throw is caught. Tested.
3. **No `loginHint` on the guest NAA request.** The bridge request has no such field.
4. **Guest silent in Teams is skipped (null) when `isNAAChannelRecommended()` is false.** A bridge-less nestable instance would run the browser's hidden-iframe path with its 10 s timeout, which does not apply in Teams.
5. **Tenant check fallback:** `result.tenantId || result.account.tenantId`; neither present means refused. NAA sets `tenantId = account.tenantId` itself.
6. **The Teams theme is wired in `readTeamsPreAuthContext`**, so the not-set-up and picker screens follow the Teams theme (before, only the post-auth app did). It is the same handler the adapter registers, so the registration is idempotent.
7. **`bootstrapTeams` no longer uses `resolveBackendSelection('workforce')`.** Teams entries must list `homeTenantIds`; the one-entry `VITE_BFF_API_URL` fallback lists none, so every Teams tid is "not set up" there (tested). Tasks 059/076 must set `VITE_BACKENDS` with `homeTenantIds` for the Teams build.
8. **Escalation trigger:** not fired. Nothing in code, docs or tests refused a tenant-specific authority for the host's own tenant.

### Retargeted existing tests (each justified; no meaningful assertion deleted)

- `tests/workforce-auth.test.ts` (16 Teams tests).
  - They asserted the A8 Teams BFF path (A8 client id, no per-request authority).
  - Now: the config uses the home client id; `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` is stubbed; `createTeamsHostAdapter(SPAARKE_TENANT)`; the `acquireTokenPopup` expectation gains `authority`.
  - The Teams tid equals the Spaarke tenant GUID in the fixtures, so every authority assertion text is unchanged.
  - A new `a8Cfg` keeps the two A8 `workforceAuthorityConfig` tests on the A8 client.
- `tests/background-no-interaction.test.ts` (2): the Teams cfg names the home client, and the env is stubbed for that describe.
- `tests/backend-selection.test.ts` (1): the Teams adapter test stubs the home client, uses `createTeamsHostAdapter(SPAARKE_TENANT)`, and gives the NAA host account the Teams tenant.
- `tests/main-bootstrap.test.tsx` (3): the Teams bootstrap needs a `user.tenant.id` in the context and entries that list it (`getContext` is now a hoisted mock).
- `tests/workforce-home-auth.test.ts` (1): "the gate does nothing outside the home plane: Teams and CIAM". The Teams half asserted the behaviour this task reverses (the gate is active in Teams). It is removed there and covered in `teams-home-auth.test.ts`; the CIAM half is kept.

### Tests

- New file: 55 tests. Against the pre-change `src/`: **42 failed / 13 passed** of 55 (the 13 passes are negative controls and static checks that hold before too).
- After: 55 / 55.
- Mutation checks: without `assertTeamsResultTenant`, 4 tests fail; without the per-request authority, 9 tests fail.
- Full suite after: **40 files, 1136 tests, 0 failed** (`--pool=forks --poolOptions.forks.minForks=1 --poolOptions.forks.maxForks=4`).
- typecheck 0, lint 0, build OK, `check:dist` clean.
- ExternalSpa ArchTests: 231 / 231.

### Acceptance criteria -> tests (`tests/teams-home-auth.test.ts`)

- **AC1**: "Teams BFF token: multitenant client through NAA on authority /{tid}" (NAA first with per-request authority; cached-account and forced-refresh paths; NAA popup; NAA failure -> standard popup with the same authority; cancel opens no second popup; `noInteraction`; never a redirect; no alias; the A8 client id refused; a non-GUID tid reaches nothing) and "Teams bootstrap ... AC1 end to end".
- **AC2**: "Teams bootstrap ... AC2" (unlisted tid; missing user.tenant, no user, non-GUID, empty, non-string; unreadable context; no buttons and no tenant id; loaded and failed(Other) signals; Teams theme; the one-entry fallback -> not set up; the tid selects the listing entry; the picker; `pickedKeyListsTenant`).
- **AC3**: "Registration in Teams (AC3)": NAA silent guest link (A8 client, Spaarke authority per request, memory cache, no `loginHint`) -> link POST -> `/me`; NAA failure -> the form with no popup before it; a non-Spaarke or tenant-less guest result -> null -> form; NAA not recommended -> skipped; `licensed_user_link_required` -> click -> popup on the standard A8 instance -> link -> `/me`; a contact registers with no popup; account-scoped cleanup with the NAA throw caught; the guest token only in the link body; the switch point `afterSilentLinkFailure`.
- **AC4**: "Shipped files": no `getAuthToken` and no Teams `authentication` import in `src/`; no `webApplicationInfo` key in any `appPackage/*.json`.
- **AC5**: 058's browser tests and CIAM tests pass unchanged (`workforce-home-auth.test.ts` has one Teams-half retarget, above; `guest-token-cache-isolation.test.ts`; the CIAM cases in `workforce-auth.test.ts`); typecheck, lint, build and `check:dist` are clean.
- **Carry-over**: no `notifyTeamsSuccess` and no `/join` in the adapter; no `/join` route or join-link wording in `spec.md` / `design.md`.

### Teams build variable (finding for task 076; no workflow edited)

- `.github/workflows/deploy-teams-app.yml` "Build (Vite production)" env passes `VITE_BFF_API_URL`, `VITE_BACKENDS`, the CIAM vars and `VITE_TEAMS_MSAL_*`, but **not `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`**.
- Correction (review round 1): this is NOT a runtime failure. The tab's runtime bundle is the external-SPA deployment, whose workflow already requires the variable; this workflow only sanity-builds and zips.
- `deploy-external-spa.yml` already has it, required, GUID-checked and checked to differ from the A8 client. For 076: add it to the Teams build env with the same preflight.
- The comment in that block ("Workforce plane (Teams host): single-tenant Spaarke public client...") is now stale.

### Observations (not fixed)

- `appPackage/env.dev.json` / `env.prod.json` `$comment`s still say the Teams tab signs in through the single-tenant client. Task 076 owns `appPackage/`.
- The multitenant client's SPA registration needs the NAA broker redirect `brk-multihub://<host>` and `/auth-callback.html`, like the A8 client. Tasks 059/076 should check; task 071 verifies it live.
- 058's `validateHomeTenantIds` refuses Spaarke's own tenant in `homeTenantIds` (K-2), so a Spaarke-tenant employee opening the Teams tab in Spaarke's tenant sees "not set up". That follows the plan; flag it if licensed staff are expected there.
- `acquireTeamsWorkforceBffToken` still picks `getActiveAccount() ?? getAllAccounts()[0]` with no tenant filter; the result tenant check is what keeps a wrong-tenant account from being used (it then falls to the popup).
- The Teams popup fallback and the guest popup are standard-PCA popups (undocumented in Teams desktop/mobile): task 071, unchanged.


## Review fix round 1 (2026-10-19; implementer, Sonnet; not committed, not pushed, not deployed)

All paths under `src/client/external-spa/`. Facts below were checked against the msal-browser 5.5.0 source and, for A and B, against the REAL library in `tests/teams-real-msal.test.ts` (only the Teams JS handshake, a fake host bridge and `window.open` are faked).

### Findings -> change -> proving test

| # | Finding | Change | Test (fail-before) |
|---|---|---|---|
| A (F1) | A stale `msal.interaction.status` lock survives a Teams iframe reload; every popup then throws `interaction_in_progress` | `standalone-plane.ts`: `clearStaleInteractionLock(ownerClientIds)` (the browser helper `clearStaleGuestInteractionLock` now calls it) and `clearStaleTeamsInteractionLock()` (A8 client + multitenant home client; other owners and keys untouched). `main.tsx` `bootstrapTeams` calls it first, before any MSAL use | `teams-real-msal.test.ts`: CONTROL (the defect, real msal); clear-then-popup not blocked (both owners); END TO END through `main.tsx` with real msal; must-not-fire (foreign owner stays). `teams-home-auth.test.ts`: lock gone before the first token request (A8, home, upper-case), foreign lock and other keys kept, no lock, unparsable lock, helper must-fire/must-not-fire, unconfigured client skipped. Without the call in `main.tsx`: 4 tests fail |
| B (F4) | The tenant checks read `result.tenantId`, which under NAA is the HOST account's tenant | `msal-auth.ts`: `tokenTenantId(result)` = `idTokenClaims.tid`, else the access token payload `tid` (base64url JSON, unverified), else '' (fail closed); used by `assertTeamsResultTenant` (BFF token) and `acquireSpaarkeGuestTokenViaNaa` (guest) | `teams-real-msal.test.ts` (real NAA controller + fake bridge): PROBE shows `result.tenantId` = host tid for a foreign-issued token; BFF and guest accepted/refused both ways; id-token-without-tid fallback. `teams-home-auth.test.ts`: `grant()` now models NAA honestly (`tenantId` from the host account, `tid` in the claims); BFF and guest accept/refuse both ways; JWT fallback; opaque / no-tid / non-string / malformed fail closed. With the old `result.tenantId` logic restored: 27 tests fail |
| C (F2) | The guest silent NAA call has no time limit and no trace | `acquireSpaarkeGuestTokenViaNaa`: 10 s race (`GUEST_NAA_TIMEOUT_MS`), the abandoned attempt still cleans up when it settles; `console.warn` with a reason code on every null path (`naa_not_recommended`, `no_token`, `tenant_mismatch`, the MSAL `errorCode` or `error`, `timeout`), never a message, token or claim | `teams-home-auth.test.ts`: a never-answering bridge gives null at exactly 10 s (fake timers); a late answer is discarded and cleaned up; in the gate the form shows after 10 s; reason-code tests (no message or token leaked). Without the race the never-answers test hangs and times out |
| D (F1) | Teams sign-in hint said "in Spaarke's directory" | `TeamsAuthError.tsx:114` now reads "Sign in with your work account for your organization." | `teams-home-auth.test.ts` "Teams sign-in error screen says ..." (no other test asserted the old text) |
| E (F1) | A B2B guest in a listed tenant's Teams loops on 401 | `msal-auth.ts`: `isTeamsGuestAccount` (the `homeAccountId` `utid` suffix is a GUID that differs from the tid; or `#EXT#` in the username) and `TeamsGuestAccountError`. `TeamsHostAdapter.ts`: `assertNotGuest` after the token in `selectAuthStrategy` and `signInWithPopup`, before the BFF acquirer is installed. `main.tsx`: `renderTeamsGuestAccount` (bootstrap catch and recovery click). New `components/auth/TeamsGuestAccount.tsx` (Fluent v9 tokens, no buttons; `notifyAppLoaded` + `notifyFailure(Other)`) | `teams-home-auth.test.ts`: guest by home tenant (bootstrap), guest by `#EXT#`, guest by the recovery click -> the screen with zero fetches and no buttons; member unchanged (bootstrap, opaque homeAccountId, recovery); `isTeamsGuestAccount` must-fire/must-not-fire. Without the check: 3 tests fail |
| F (F2) | A theme failure showed "not set up" | `TeamsHostAdapter.ts`: `wireTeamsTheme` has its own try/catch (also guards the theme-change handler); it is shared by the pre-auth read and the adapter, so both are covered | `teams-home-auth.test.ts`: listed tenant with blocked theme storage -> the app; unlisted still not set up; `readTeamsPreAuthContext` still returns the tid. Without the try/catch: 2 tests fail |
| G (F3) | The recovery path's home-plane wiring was untested | `workforce-auth.test.ts` "clicking drives the real popup path" asserts `isWorkforceHomePlaneActive()`, the `accountsForActivePlane` tid filter, and the Teams guest-link host (the silent guest token builds a nestable A8 instance). The Teams tests in that file moved from the Spaarke tid to a customer tid (`TEAMS_TID`), with an NAA-honest `token()` | same file |
| H (F4) | Docs described the old sign-in | `spec.md` (the C7 decision row; acceptance item 7), `design.md` (C7 line), `README.md` (graduation criterion 7) reworded to the A9 multitenant sign-in | `teams-home-auth.test.ts` "Shipped files" doc test still passes |

### Cheap known-limit fixes

- `loginHint` is passed on the guest silent request (the live bridge drops it; a bridge-less standard instance uses it). Test: the request shape in the registration test (removing it fails 1 test); the real-msal test shows the bridge request carries no `loginHint` field.
- When the Teams NAA guest attempt returns null, `acquireSpaarkeGuestToken` prewarms the standard guest instance (`void getGuestLinkInstance(bffScope).catch(...)`). Tests: prewarmed after a null, not after a token (removing it fails 1 test).
- `reservedHomeTenantIds()` includes the consumer-account tenant `9188040d-6c67-4c5b-b112-36a304b66dad`; must-fire in `workforce-home-auth.test.ts` (lower and upper case).
- The removed `teamsHost` prop is gone from `workforce-home-auth.test.ts`.
- Stale comments fixed: `msal-auth.ts` (`createTeamsWorkforceMsalInstance`, the `acquireTeamsWorkforceBffToken` `@param cfg`), `TeamsHostAdapter.ts` (header, `signInWithPopup`, `completeAuthSetup`: "ADR-028 A9 Teams amendment").
- This file: the `getActiveAccount`/`setActiveAccount` claim and the task 076 follow-up are corrected above.

### What the NAA account object carries (5.5.0, `NestedAppAuthAdapter.fromNaaAccountInfo`)

`homeAccountId` = the bridge account's, else `{localAccountId}.{tenantId}`; `tenantId` = the bridge account's (the HOST tenant), else the token's `tid`; `username` = the bridge's, else `preferred_username` / `upn` / `emails[0]`; plus `idTokenClaims` (from the id token), `loginHint` and `tenantProfiles`. So the guest check relies on the bridge supplying a real `homeAccountId` (`oid.tid` of the HOME tenant). If a host omits it, the id is synthesized from the host tenant and a guest cannot be told apart by suffix; the `#EXT#` username is then the only other signal (a guest's `preferred_username` is often the plain email, so it is weak). The BFF still refuses a guest token, so the failure mode is the old 401 screen, not a leak. Task 071 probes what the real Teams bridge returns for a guest.

### Known limits (recorded, no code)

- **One bridge listener leaks per guest NAA attempt.** Each `createNestablePublicClientApplication` adds a `message` listener on the bridge and 5.5.0 has no teardown (the probe counted 3 listeners after 3 creates). Bounded by the number of silent link attempts (one per registration probe, so by clicks), not unbounded.
- **`notifyFailure(Other)` on the not-set-up and guest screens may make Teams show its own error page** instead of our screen. This is the existing pattern of task 041's "No organization available" screen.
- **`app.getContext()` has no timeout** (existing exposure; the tid read waits as long as the host does).
- **An unreadable context shows "not set up"** (plan choice: zero token requests and zero BFF calls beats guessing a tenant).

### Task 071 probe list (additions)

- What the real Teams NAA bridge returns as `homeAccountId` / `username` / `tenantId` for a B2B guest (the guest screen depends on it).
- Whether a bridge listener leak is observable after repeated guest silent attempts.
- Whether `notifyFailure(Other)` replaces the not-set-up and guest screens in Teams desktop, web and mobile.
- Whether `getContext` can hang in any Teams client.

### Verification (from `src/client/external-spa`)

- `npm run typecheck`: clean. `npm run lint`: clean. `npm run build`: OK. `npm run check:dist`: "1 file(s) clean".
- Full vitest (`--pool=forks --poolOptions.forks.minForks=1 --poolOptions.forks.maxForks=4`): **41 files, 1189 tests, 0 failed** (before this round: 40 files, 1136 tests; `teams-home-auth.test.ts` 55 -> 96, new `teams-real-msal.test.ts` 12).
- ExternalSpa ArchTests (`--no-build`): 231 / 231.
- Fail-before method: each fix was reverted one at a time in `src/` (A: the `bootstrapTeams` call; B: `tokenTenantId` back to `result.tenantId`; C: the race; E: the guest check; F: the try/catch; prewarm; `loginHint`), the suite run, then the fix restored byte for byte.

## Quality gates (main session, 2026-10-19)

- **adr-check:** 0 violations.
  - ADR-028 A9: the Teams BFF token comes only from the multitenant client at `/{Teams tid}`, set per request, with the token's own `tid` checked; the A8 client is used only for the guest link.
  - No `getAuthToken`, no `webApplicationInfo`, no `common`, no redirect on any Teams path.
  - ADR-021: Fluent v9 tokens, no hard-coded colours.
- **Adversarial passes:** 2 (auth).
- **Fix round 1 closed:**
  - A: the stale Teams interaction lock;
  - B: the token-tenant check (both reviewers; confirmed with real msal-browser);
  - C: the guest NAA timeout and reason-code logging;
  - D: the TeamsAuthError hint;
  - E: the B2B guest screen;
  - F: the theme failure isolated;
  - G: the recovery-path test;
  - H: the A9 wording in spec, design and README;
  - plus the cheap K fixes.
- **Re-check of the fix diff:** the main session read `tokenTenantId`, `assertTeamsResultTenant`, `isTeamsGuestAccount` and `clearStaleTeamsInteractionLock`. Fail-before evidence for A, B, C and E is in "Review fix round 1".
- **Final verification (main session):** typecheck and lint clean; vitest 41 files / 1189 passed; build OK; check:dist clean; ExternalSpa ArchTests 231/231.
- **Owner decision:** AC3, "Form; popup on click" (2026-10-19).

rework: fix-round-1 task 075
