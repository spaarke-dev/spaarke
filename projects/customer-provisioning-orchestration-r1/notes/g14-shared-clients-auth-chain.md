# G14 — Shared M365 clients vs per-customer BFFs: current auth chain (facts) + recommendation

> **Date**: 2026-09-30 (SESSION 26). **Source**: read-only investigation of this branch + `origin/master`
> (office add-ins changed on master but the auth/config mechanism is identical; H3, the registration script and
> ADR-028 are newer on this branch). **Consumer**: T240 (owner D9 — in scope).

## 1. Current binding chain

| Client | Signs in as (client ID) | Token audience / scope | BFF URL | Where the values come from |
|---|---|---|---|---|
| Outlook + Word add-ins (one shared bundle on SWA `spaarke-office-addins`) | `c1258e2d…` "Spaarke Office Add-in" (single-tenant, Spaarke tenant) via NAA; Office on the web falls back to an MSAL popup (`/auth-callback.html`) | `api://{BFF_API_CLIENT_ID}/user_impersonation`, fallback hard-coded `1e40baad` | build-time constant, fallback `spaarke-bff-dev` | webpack build-time injection (`webpack.config.js`), CI hard-codes dev values (`deploy-office-addins.yml:62-65`); `c1258e2d` pre-authorized on `1e40baad` **by hand** (runbook `004-entra-naa-registration.md`) |
| Teams tab (external-spa) | the BFF app itself, `1e40baad` (NAA, `/organizations`; fallback Teams SSO) | `api://1e40baad…/access_as_user`; manifest `webApplicationInfo {id 1e40baad, resource api://green-dune-…/1e40baad…}` | build-time `VITE_BFF_API_URL` (+ manifest `validDomains`) | Vite env at build time |
| Dataverse code pages / PCF | `sprk_MsalClientId` | `api://{sprk_BffApiAppId}/user_impersonation` | `sprk_BffApiBaseUrl` | per-org Dataverse env vars, written per customer by **H7** — already per-customer |

**BFF validation**: Microsoft.Identity.Web, `AzureAd:ClientId = API_APP_ID`, `Audience = api://API_APP_ID`
(`AuthorizationModule.cs:47-49`), fed per customer from KV `BFF-API-ClientId` / `BFF-API-Audience` (written by H3).
Only extra audience: `AgentToken:CopilotAudience`. OBO runs as the BFF's own app (MI-FIC). ⇒ a token minted for
app A is rejected (401) by a BFF configured as app B; OBO would fail anyway.

**`tid` routing**: `TenantEnvironmentRouter` keys only on `tid`, treats duplicate `tid` entries as ambiguous → 403,
runs server-side after the client already reached a BFF, and is **registered but not wired** to any endpoint (HEAD
and master). It cannot distinguish Model 1 customers (all share Spaarke's `tid`).

## 2. What breaks with one BFF app-reg + one BFF URL per customer

1. Shared clients carry ONE build-time audience → every other customer's BFF returns 401.
2. Shared clients carry ONE build-time URL → no way to choose a customer.
3. `tid` cannot discriminate Model 1 customers.
4. H3 app-regs do **not** set `preAuthorizedApplications` / `knownClientApplications`, SPA or `brk-multihub`
   redirect URIs, `access_as_user`, or a Teams identifier URI (`GraphAppRegistrationProvisioner.cs`). The Teams tab
   uses the BFF app **as its own client**, and its manifest holds a single `webApplicationInfo`.
5. OBO is bound to the BFF's own app registration (fine per customer, but only if the token's audience is that app).
6. Each customer BFF must allow the add-in + Teams SWA origins in CORS (`CorsModule.cs:17,81-105`).
7. 🔴 **Likely broken for code pages too**: H7 defaults `sprk_MsalClientId` to the customer's BFF app
   (`H7DataverseEnvVarValuesHandler.cs:141,311-326`), but H3 registers **no SPA redirect** on it — code-page sign-in in
   a provisioned customer env would fail unless a separate client ID is supplied. (Dev uses a separate client app
   `170c98e1`.)
8. Stale/contradictory: `Register-EntraAppRegistrations.ps1 -CreateBffApp Model1` still documents "ONE shared BFF
   app-reg"; the script prints `knownClientApplications` instructions instead of configuring them; H3 uses
   `AzureADMultipleOrgs` while the script uses `AzureADMyOrg`; `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:527-532` says H3
   adds add-in SPA redirects (it does not).

## 3. Reusable mechanisms

- Anonymous `/api/config/client` + `/api/config` on every BFF (`ConfigEndpoints.cs:57-144`) — self-description, but
  the client must know the URL first.
- Predictable BFF URL per customer: `sprk-{customerId}-prod-api.azurewebsites.net` (`customer.bicep:547`).
- Registry `sprk_dataverseenvironment` (one row per customer) + BFF `DataverseEnvironmentService` already reads it.
- `Customer:Id` (D-14, master) — each BFF knows its customer.
- MSAL scopes are **runtime** parameters — a client can request a different audience without a rebuild.
- No user → customer mapping exists anywhere today.

## 4. Recommendation — ✅ APPROVED by owner 2026-09-30 (plan D11)

**Keep ONE shared add-in package and ONE Teams package — do not build per-customer instances.** The clients choose
the customer at runtime:

1. **Identify the customer.** After sign-in the client knows the user's **home tenant** (MSAL account
   `homeAccountId` = `{oid}.{homeTid}`) — i.e. the customer's own M365 organization, which is distinct per customer even
   though every Model 1 guest token carries Spaarke's `tid`. A Spaarke-hosted **customer directory** maps home tenant →
   `{customerId, bffBaseUrl, bffAppId}`; provisioning writes the entry at completion (H13 / L3 Step 6). Fallback for
   ambiguous or unmapped users: the user enters their Spaarke customer code once (remembered in Office roaming
   settings / local storage).
2. **Get the right token.** **H3 pre-authorizes** the shared add-in client (`c1258e2d`) and a dedicated Teams client
   app on every customer BFF app registration (and exposes the scope they request), so the client silently requests
   `api://{thatCustomerBffAppId}/user_impersonation` — no consent prompt, no rebuild. The Teams tab stops using the
   BFF app as its client (a dedicated Teams client app, like the add-in has), so its manifest is no longer bound to
   one BFF.
3. **Call the right BFF** with that token; each customer BFF allows the add-in + Teams origins in CORS (H4b settings).
4. **Fix code pages** (item 7): H3 registers the SPA redirect needed, or H7 sets `sprk_MsalClientId` to a dedicated
   client app pre-authorized like the add-in.

**Why not per-customer instances**: they solve only item 2 (URL/audience baked per build). Items 4, 6 and 7 are
needed either way; in exchange they cost a build + SWA path + manifest per customer on every release, customer
admins installing a customer-specific manifest, and version skew between customers.

**Spike first (can change the design)**: Model 1 users are **B2B guests in Spaarke's tenant**, but Outlook/Word run
under their **home** account in their own tenant. Confirm that NAA (and the Office-on-the-web popup fallback) can
acquire a token from **Spaarke's tenant authority** for a guest account, and that the Spaarke-tenant single-tenant
add-in app works for users from other tenants in that flow. Same check for Teams (guest must reach the tab in the
Spaarke tenant, or the tab runs in the home tenant and needs a multi-tenant client).

Open design choices for T240: where the customer directory is hosted (a small anonymous-read endpoint vs. a
generated config file on the add-in SWA — the latter publishes the customer list); whether the directory keys on
home tenant, email domain, or both.
