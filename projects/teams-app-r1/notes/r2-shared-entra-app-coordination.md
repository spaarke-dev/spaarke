# Coordination: Shared Workforce Entra App (`1e40baad`) — teams-app-r1 → SPA-external-access-platform-r2

> **From**: teams-app-r1 (Teams host for the collaboration SPA)
> **To**: spaarke-SPA-external-access-platform-r2 (platform / module-framework owner)
> **Date**: 2026-08-07
> **Status**: Teams data-plane shipped + live (web verified); Teams **desktop** sign-in still being finalized. All changes below are on a **shared platform Entra app** — this document exists so R2 can review, ratify, and take ownership.
> **Sibling doc**: [`r2-coordination-response.md`](./r2-coordination-response.md) (the FR-22 `CallerPrincipalResolver` handoff; §8b summarizes these Entra changes).

---

## TL;DR

To host the collaboration SPA inside Microsoft Teams (workforce identity, NAA/SSO), teams-app-r1 made a series of **additive** changes to the **shared** Entra app **`1e40baad` = "SDAP-BFF-SPE-API"** — the same app that authenticates the BFF for the whole platform and that R2's module framework builds on. The four exposed API scopes are **unchanged** (verified before/after every edit) and the CIAM external path is untouched, so nothing R2 depends on is broken. **But this app is platform-owned, and one change in particular (pre-authorizing the Microsoft Authentication Broker) is a security-surface decision R2 should ratify.** We need R2 to (a) take ownership of this shared app's config, (b) ratify or replace the broker pre-authorization, and (c) decide the long-term Teams-SSO-fallback approach.

---

## 1. The shared asset

| Property | Value |
|---|---|
| App (client) id | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Display name | **SDAP-BFF-SPE-API** |
| App **object** id | `c2aab303-50f8-4279-9934-503ab3a4b357` |
| Enterprise app (SP) id | `d93c832e-9b1d-4ccc-a2a8-9419fbf3fc18` |
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` (Spaarke Development Environment) |
| Role | The BFF's server API app. Authenticates **every** Spaarke client surface against `spaarke-bff-dev`; R2's module framework + `CallerPrincipalResolver` sit on it. |
| Exposed API scopes (unchanged) | `access_as_user` (`7e9e1e5a-3b0b-4153-9753-85b41d48c6fe`), `access_as_external_user` (`f7236a6a-…`), `SDAP.Access` (`691ef488-…`), `user_impersonation` (`18afc847-…`) |
| BFF token audience | `AzureAd:Audience = api://1e40baad-…` (accepts **v1** tokens; the Teams NAA token validates here with no config change) |

---

## 2. Why teams-app-r1 touched this app

The Teams tab authenticates the user with their **workforce** Entra identity via **Nested App Auth (NAA)** (MSAL v5 `createNestablePublicClientApplication`) with a **Teams SSO (`getAuthToken`) fallback**, then calls the BFF broker-only (no OBO) — exactly the plane the `CallerPrincipalResolver` (FR-22) serves. NAA/SSO against `1e40baad` requires specific Entra config on **that** app (multitenant, broker redirect URIs, pre-authorized Teams client apps). There is no separate Teams app registration — by design the Teams host reuses `1e40baad` (the workforce app), which is why these changes land on shared infra.

---

## 3. Complete change log on `1e40baad` (all additive)

| # | Change | Concrete value | Date | Why | R2 action |
|---|---|---|---|---|---|
| 1 | `signInAudience` → multitenant | `AzureADMultipleOrgs` | (R1 build) | Workforce users from any customer tenant can sign in inside Teams | Ratify (needed for the multi-tenant Teams story) |
| 2 | Pre-authorize Teams **web** client | `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` on `access_as_user` | (R1 build) | Teams web NAA/SSO issues the token with no consent prompt | Ratify (standard Teams SSO config) |
| 3 | Pre-authorize Teams **desktop/mobile** client | `1fec8e78-bce4-4aaf-ab1b-5451cc387264` on `access_as_user` | (R1 build) | Teams desktop/mobile NAA/SSO | Ratify (standard Teams SSO config) |
| 4 | **Pre-authorize Microsoft Authentication Broker** | `29d9ed98-a469-4536-ade2-f981bc1d605e` on `access_as_user` | **2026-08-07** | The new-Teams **desktop** client brokers through the Windows WAM/OneAuth broker; its silent flow was **denied the resource** (see §4). Pre-authorizing the broker lets it obtain `access_as_user` silently. | **RATIFY OR REPLACE — this is the security-surface decision (see §5).** |
| 5 | SPA-platform redirect URIs | `https://green-dune-0c4f1221e.7.azurestaticapps.net`, `brk-multihub://green-dune-…`, `brk-1fec8e78-…://green-dune-…`, `brk-5e3ce6c0-…://green-dune-…` | (R1 build) | NAA broker reply addresses. `brk-multihub://{host}` was the fix for `AADSTS700046` (MSAL v5 sends that redirect). | Ratify. **Note the side effect in §6.** |

**Not changed / preserved:** the four exposed scopes (verified identical before/after every PATCH), the existing admin-consented app permissions (Graph `Directory.ReadWrite.All` / `Files.*` / `FileStorageContainer.*`, SharePoint `Container.Selected`, Dynamics `user_impersonation` — all `AllPrincipals`), the CIAM external path, and the BFF's accepted audience.

**Backup:** the pre-change `api` object was saved to `c:/tmp/entra-api.json` (session-local) before change #4 — the broker pre-auth is reversible in a single PATCH.

---

## 4. The desktop investigation (why change #4 exists)

Web Teams worked end-to-end (workforce token → BFF → records). **Desktop** Teams failed at token acquisition. We instrumented the SPA's fail-loud error screen to surface the underlying errors (deployed to green-dune), which revealed:

- **NAA (primary):** `ServerError / IncorrectConfiguration`, OneAuth **2002 "Access denied for the resource"**, `auth_flow=Broker`, `authorization_type=WindowsIntegratedAuth`, `api=AcquireTokenSilently`. → The **Windows WAM/OneAuth broker** on the desktop client was denied a silent token for `api://1e40baad-…/access_as_user`. (Web NAA can broker interactively, so it succeeds; the desktop silent flow can't.)
- **Teams SSO (fallback):** `App resource defined in manifest and iframe origin do not match`. → The manifest's `webApplicationInfo.resource = api://1e40baad-…` is **not domain-qualified** to the tab origin (`green-dune-…`), so `getAuthToken` rejects it. This only fires because NAA failed first.

teams-app-r1's least-invasive unblock was change #4 (pre-authorize the broker so its silent flow is granted the scope). **Pending desktop retest** to confirm it clears the NAA error.

Separately, a bare `/adminconsent?client_id=1e40baad-…` URL failed with **`AADSTS7000471`** — see §6.

---

## 5. Decision R2 owns: the broker pre-authorization (change #4)

teams-app-r1 pre-authorized the **Microsoft Authentication Broker** (`29d9ed98`, a Microsoft first-party WAM app) on `access_as_user`. This means that broker can obtain `access_as_user` tokens for `1e40baad` **without an interactive consent**. It is additive and reversible, but it widens who can silently acquire the app's user scope, so **R2 should ratify it or choose an alternative**:

- **Option A (applied): keep the broker pre-auth.** Simplest; matches how Teams web/desktop clients are already pre-authorized; unblocks desktop NAA. R2 ratifies.
- **Option B: fix the Teams-SSO fallback instead.** Domain-qualify `webApplicationInfo.resource` to `api://green-dune-…/1e40baad-…`, add that identifier URI to `1e40baad`, **and add it to the BFF's accepted audiences** (`ValidAudiences`) so SSO-issued tokens validate. More moving parts (manifest + Entra + BFF code/config + redeploy) and it changes the token audience — a platform decision R2 must make.
- **Option C: revert #4** and hold desktop until R2 decides. (Backup available.)

teams-app-r1 recommends **A** for now (additive, reversible) with R2 ratifying; but the call is R2's because it's R2's app.

---

## 6. Side effect R2 should know: `brk-…` redirects break bare admin-consent URLs

Because the app now carries `brk-…` SPA redirect URIs (change #5), a bare `https://login.microsoftonline.com/{tenant}/adminconsent?client_id=1e40baad-…` picks a `brk-` reply address and fails with **`AADSTS7000471`** ("reply address scheme reserved for brokered application requests"). **To grant admin consent on this app, use the Entra portal** (Enterprise applications → SDAP-BFF-SPE-API → Permissions → *Grant admin consent*) **or `az ad app permission admin-consent`, not a bare consent URL.**

---

## 7. Governance ask

1. **R2 takes ownership of `1e40baad`'s configuration** as shared platform infra. teams-app-r1 should not be making unilateral changes to it; this document is the handoff of what was done and why.
2. **Ratify or replace change #4** (broker pre-auth) per §5.
3. **Decide the durable Teams-SSO-fallback stance** (Option B in §5) if desktop NAA proves fragile across clients/tenants.
4. **Coordinate future changes** to this app (redirect URIs, pre-authorized clients, scopes, audiences) through R2, since they affect every consumer — including R2's module framework.
5. Fold the Teams NAA/SSO Entra recipe into R2's environment-provisioning docs so a second/customer tenant onboarding (graduation criterion 6, accepted as a go-live item) reproduces it deterministically.

---

## 8. Reproduce / verify (for R2)

```bash
# Current pre-authorized clients + exposed scopes on the shared app:
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "{scopes:api.oauth2PermissionScopes[].value, preAuth:api.preAuthorizedApplications[].{app:appId,scopes:delegatedPermissionIds}, spaRedirects:spa.redirectUris, audience:signInAudience}" -o json

# Existing admin-consented (AllPrincipals) delegated grants on the enterprise app:
az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/d93c832e-9b1d-4ccc-a2a8-9419fbf3fc18/oauth2PermissionGrants"

# To REVERT change #4 (remove the broker pre-auth), remove appId 29d9ed98-… from
# api.preAuthorizedApplications (full-object round-trip; backup at c:/tmp/entra-api.json).
```

---

## 9. References
- `projects/teams-app-r1/notes/r2-coordination-response.md` — FR-22 resolver handoff (§8b = short form of this doc).
- `projects/teams-app-r1/notes/spa-v2-handoff-workforce-endpoint-gap.md` — the original endpoint-gap handoff + Entra recipe.
- `projects/teams-app-r1/notes/integration-verification-report.md` — graduation-criteria verification (criterion 6-live = accepted go-live item).
