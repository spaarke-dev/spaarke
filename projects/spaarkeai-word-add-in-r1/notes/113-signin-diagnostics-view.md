# 113 — Dev-only sign-in Diagnostics view (provisioning request R1)

## Why
customer-provisioning-orchestration-r1 is designing one shared add-in for every Model 1 customer (one add-in site, one add-in app registration, one backend app registration per customer, chosen at runtime through a Spaarke directory endpoint). The open question is whether a **B2B guest** from another organization gets a Spaarke-tenant token for the backend in Outlook and Word, desktop and web. This view lets the owner read the answer off the token.

Exchange (2026-10-07):
- Our answer corrected their premise: the add-in already signs in to Spaarke's tenant (`TENANT_ID` → `https://login.microsoftonline.com/a221a95e-…`), not `/organizations`.
- They adopted our directory-endpoint shape (no `/me/memberOf`, no Graph permission on the shared client; Model 2 out of scope, app stays single-tenant).
- Production origin proposed: `addins.spaarke.com` (needs SPA redirects `brk-multihub://addins.spaarke.com` and `https://addins.spaarke.com/auth-callback.html` on the production add-in app).
- R1 revised: show `acct` and `idp` instead of `idtyp`; drop memberOf.

## What
- **"⋮ → Diagnostics"** — only in a build with `ADDIN_DIAGNOSTICS_ENABLED=true` (`webpack.config.js`, default off). Only the dev deploy workflow sets it.
- **The view** (`components/views/DiagnosticsView.tsx`, lazy-loaded, a panel above the tab content — tabs stay mounted) shows:

| Row | Source |
|---|---|
| tid, oid, acct (0 — member / 1 — guest), idp ("not present — not a guest sign-in" when absent), iss, aud | the backend access token, decoded for display (`services/tokenDiagnostics.ts`) |
| Office host | `Office.context.diagnostics` (host · platform · version) |
| Sign-in path | NAA (Office broker) or Popup fallback (`AuthService.getSignInDiagnostics`) |
| Authority | the MSAL authority actually used |

- **Copy** puts the rows on the clipboard as text; **Refresh** reads a fresh token; **Close** returns to the tabs.
- The token itself is never rendered, logged or copied. No Graph call, no new permission, no app-registration change.

## Reading the test
- Guest signed in correctly: `tid` = Spaarke's tenant (`a221a95e-…`), `acct` = `1 — guest`, `idp` = the guest's home issuer, `aud` = the dev backend.
- Problem shapes: `tid` = the guest's home tenant (wrong authority path), or no token / an error (the host could not get a Spaarke-tenant token for the guest — note the Sign-in path row: NAA vs popup).

## Gates
- New suites (gated): `tokenDiagnostics.test.ts` (12), `DiagnosticsView.test.tsx` (6) — claims, guest/member mapping, undecodable token, Copy without token, Refresh, Close, menu entry only with a handler.
- Full add-in jest 89 suites / 1,246 tests; lint 0; typecheck 0 production; test-file debt 68.

## Removal
Set `ADDIN_DIAGNOSTICS_ENABLED` to "false" (or remove it) in `deploy-office-addins.yml` when the guest test is done; no code change needed.
