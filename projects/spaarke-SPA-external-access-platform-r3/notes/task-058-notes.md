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
