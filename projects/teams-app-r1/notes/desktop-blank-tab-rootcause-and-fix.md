# Teams DESKTOP blank-tab — root cause + fix (2026-08-10)

## Symptom
Teams **web** tab: loads and works fully. Teams **desktop** tab: authenticates (no
"Sign-in failed" screen) but renders a **blank content area**.

## Root cause (confirmed by code trace)
The blank tab is the `AuthGuard` "Redirecting to sign-in…" state stuck inside the Teams
iframe, caused by the **Teams SSO fallback** path desktop uses:

1. On desktop, NAA (`acquireBffTokenViaNaa`) **fails** — the Windows WAM/OneAuth broker
   returns "access denied for the resource" (OneAuth 2002), even after broker pre-auth.
2. `acquireTeamsWorkforceBffToken` falls through to the **Teams SSO fallback**
   (`authentication.getAuthToken`), which **succeeds** and returns a bearer token — so
   bootstrap does NOT throw, and no "Sign-in failed" screen appears. ✅ auth "works".
3. BUT `getAuthToken()` returns a **raw JWT string that never lands in the MSAL account
   cache**. The NAA MSAL instance handed to `<MsalProvider>` therefore has **zero accounts**
   and no active account. (NAA on web DOES populate the account via `setActiveAccount()`,
   which is why web works.)
4. In `AuthGuard`, `useIsAuthenticated()` → **false**, `inProgress` → `None`, so its effect
   fires `instance.loginRedirect(...)`.
5. A full-page redirect **inside the Teams iframe is blocked** (the app's own NFR-04 — "never
   redirect inside Teams"). AuthGuard renders the "Redirecting to sign-in…" spinner while the
   redirect can never complete → effectively a **blank tab**.

The `TeamsHostAdapter.selectAuthStrategy` comment already stated the design assumption:
*"the redirect branch in AuthGuard is never reached inside Teams."* That holds only when NAA
populates the account — it **breaks on the SSO fallback path**, which is exactly what desktop
falls back to.

## Fix (teams-app-r1 Teams-path code — minimal, matches documented intent)
Inside the Teams host, authentication is already completed during bootstrap: `main.tsx` only
renders `<App>` after `TeamsHostAdapter.initialize()` resolves, which means a BFF-valid
workforce token was acquired and the active BFF token acquirer (`acquireActiveBffToken`) is
wired. So `AuthGuard` must render children directly in Teams and never run the MSAL account
gate / redirect.

- `src/client/external-spa/src/components/AuthGuard.tsx` — added `teamsHost?: boolean` prop;
  early-returns `<>{children}</>` when `teamsHost` (before the MSAL hooks/gate). Full rationale
  in the inline comment.
- `src/client/external-spa/src/App.tsx` — `AppShell` passes `teamsHost={teamsHost}` to
  `<AuthGuard>`.

Verified: `npx vite build` (the deploy build) succeeds. The pre-existing repo-wide `tsc`
`@types/react` "bigint/ReactNode" JSX errors are unrelated and not introduced here; my two
files are clean under `tsc`.

## Known residual (cosmetic, acceptable)
On desktop the MSAL account is absent, so `App.accountToPortalUser(accounts[0])` → `null`
and `AppHeader` shows no user name/avatar. Data still loads (every BFF call uses the
SSO-fallback bearer token). A later enhancement could decode the SSO token for a display name.

The deeper item — **NAA failing on desktop** (OneAuth 2002 broker "access denied") — is a
Windows-broker / Entra concern owned by `spaarke-SPA-external-access-platform-r2` (they own the
shared Entra app `1e40baad` + the broker pre-auth). This fix makes the intended SSO **fallback**
actually render instead of blanking; it does not attempt to fix NAA-on-desktop.

## Deploy coordination (R2-owned surface)
R2 owns the deployed external-spa (SWA) + the shared Entra app. This code fix lands on
`work/teams-app-r1` → master. The desktop tab will only pick it up once the external-spa SWA is
**redeployed from master** (`deploy-external-spa.yml` workflow_dispatch, or R2's deploy). Do not
clobber R2's build unilaterally — coordinate the redeploy.
