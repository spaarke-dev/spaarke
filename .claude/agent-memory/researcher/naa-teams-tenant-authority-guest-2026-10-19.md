---
name: naa-teams-tenant-authority-guest-2026-10-19
description: NAA in Teams - tenanted authority, cross-tenant/B2B guest token via NAA (undocumented), msal-browser 5.5.0 source facts (config authority not forwarded to host, loginHint dropped), popup fallback, two PCAs; task 075 escalation verdict
metadata:
  type: reference
---

## 2026-10-19: NAA in Teams - tenanted authority, B2B guest / other-tenant token, fallbacks (task 075)

**Question**: Does Teams NAA support (A) multitenant client with authority = host tenant H, (B) single-tenant client with authority = Spaarke tenant S (S != H) for a B2B guest token; popup fallback; requirements; two PCAs.

**Findings**
- (A) host-tenant authority: documented-supported in practice. Teams NAA doc sample uses `https://login.microsoftonline.com/{your_tenant_id}`; Office NAA doc uses `/common` and says "If you want to configure a single tenant ... see Application configuration options". No documented restriction on `authority` value.
- (B) other-tenant / guest token via NAA: UNDOCUMENTED (neither supported nor refused). No doc, no error code, no GitHub issue found (searched MSAL.js, teams-js, office-js, msteams-docs, Office-Add-in-samples). Docs only say NAA supports MSA + Entra work/school, not B2C. Host (Teams) is the broker; its behaviour for authority != its tenant is closed-source.
- msal-browser 5.5.0 source (dist/naa/mapping/NestedAppAuthAdapter.mjs, controllers/NestedAppAuthController.mjs): `toNaaTokenRequest` sends `authority: request.authority` (PER-REQUEST only; config `auth.authority` is NOT forwarded to the bridge), `platformBrokerId: account.homeAccountId`, clientId, scope, claims, extraParameters. `loginHint` is NOT in the bridge TokenRequest type (dropped). acquireTokenRedirect/logout*/clearCache/getTokenCache throw "unsupported" under NAA. Bridge errors map: USER_CANCEL -> ClientAuthError `user_canceled`; NESTED_APP_AUTH_UNAVAILABLE ("can retry with standard browser based auth") / DISABLED -> `nested_app_auth_bridge_disabled`; USER_INTERACTION_REQUIRED -> InteractionRequiredAuthError; ACCOUNT_UNAVAILABLE -> `no_account_found`. Cache-hit path keys on host-supplied accountContext (home tenant account), so a guest-tenant token likely never cache-hits (always bridge round trip) - inference.
- msal-browser docs/accounts.md: for NAA "getActiveAccount/setActiveAccount are NO-OP", "NAA application is always expected to have one account".
- Popup fallback: Teams doc "Have a fallback" for down-level clients -> tab-sso-overview (getAuthToken) or pop-up auth; Teams `authentication.authenticate()` is the documented IdP popup (Entra can't be iframed; opens in Teams popup, all clients, TeamsJS >= 1.4.1 mobile). Teams doc "Known limitations": "Redirects aren't supported for iframes or brokered apps. ... use the popup API". MSAL standard-PCA popup via window.open in Teams is NOT documented as supported; only live test settles it. Tab auth doc: for guests/Free use tenant-specific endpoint, `/common` can log user into home tenant.
- Requirements: SPA-platform redirect `brk-multihub://<host>` (origin only, no path); manifest "Recommended*" not required for NAA (webApplicationInfo only needed for admin approval visibility / token prefetch via nestedAppAuthInfo, manifest >= 1.22; prefetch web+desktop only); init TeamsJS before MSAL. Consent: tenant must have app consented or "the request for tokens will be denied" (SpecterOps, secondary). NAA token = issued to the nested app's own client id (no preauthorization of Teams needed).
- Two PCAs: undocumented; source shows each `createNestablePublicClientApplication` calls BridgeProxy.create() -> extra `message` listener on shared `window.nestedAppAuthBridge`; requests matched by unique requestId; cache namespaced by clientId. Mechanically plausible, never documented.

**Sources**
- https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication (updated 2026-09-30)
- https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in (2026-01-23)
- https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/nested-app-auth-requirement-sets
- https://learn.microsoft.com/en-us/microsoftteams/platform/tabs/how-to/authentication/auth-tab-aad ; tab-sso-overview ; auth-flow-tab
- https://learn.microsoft.com/en-us/javascript/api/@microsoft/teams-js/nestedappauth (isNAAChannelRecommended)
- https://specterops.io/blog/2025/10/15/naa-or-broci-let-me-explain/ (secondary; host exchanges its own RT)
- https://github.com/OfficeDev/microsoft-teams-library-js/issues/1452 (guest + getAuthToken other tenant -> consent_required until silent=false)
- local: src/client/external-spa/node_modules/@azure/msal-browser (5.5.0) dist/naa + controllers
- repo: src/client/external-spa/src/auth/msal-auth.ts, host/TeamsHostAdapter.ts, shared/Spaarke.Auth/.../OfficeNaaStrategy.ts

**Open questions**: live probe of NAA with per-request `authority` = S from H-tenant Teams (web + desktop + mobile); does existing Teams NAA path really get a tid=S token (config authority not forwarded) - check tid claim in task 071; MSAL standard-PCA popup in Teams desktop/mobile; two-PCA coexistence.
