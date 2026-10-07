# T240 — Shared M365 clients reach each customer's own BFF: plan (2026-10-07)

Background and the approved approach (owner D9/D11): `g14-shared-clients-auth-chain.md`. This note records what changed
on 2026-10-07, the split into three tasks, and the live test plan.

## Owner input, 2026-10-07

- The live tests may go ahead.
- **The add-ins and the Teams tab are used only by Dataverse-licensed users, internal or B2B guest. No external contact
  uses them.**
- Coordinate add-in and Teams client changes with spaarkeai-word-add-in-r1 directly, or give the owner a message to relay.
- Production client sites get a Spaarke name, not a random one (with the word-add-in-r1 reply).

## Facts re-checked (2026-10-07)

| # | Fact | Where |
|---|---|---|
| F1 | 🔴 A customer BFF does not start: outside Development/Testing, empty `Cors:AllowedOrigins` throws at startup, and nothing in provisioning (manifest, H4b, `customer.bicep`) sets it. H13's CORS probe would fail too. Dev works only because its origins were set by hand. | `CorsModule.cs:17-45`; `git grep AllowedOrigins` |
| F2 | H3 sets no `preAuthorizedApplications` and no SPA redirect URIs on the customer's BFF app registration. | `GraphAppRegistrationProvisioner.cs` |
| F3 | Code pages use `redirectUri = window.location.origin` (the Dataverse org). H7 defaults `sprk_MsalClientId` to the customer's BFF app, which has no SPA redirect, so code-page sign-in fails on a provisioned stamp. Dev uses the shared client `SDAP-PCF-CLIENT`, whose redirect list names each org by hand. | `Spaarke.Auth/src/config.ts:113`; H7 `:331` |
| F4 | *(corrected by word-add-in-r1)* The add-in already signs in to **Spaarke's tenant**: both task panes pass `TENANT_ID`, and the ribbon commands get it from `deploy-office-addins.yml`. `@spaarke/auth` falls back to `/organizations` only when neither an authority nor a tenant id is given. | add-in `outlook/taskpane/index.tsx:24`, `word/taskpane/index.tsx:20` |
| F5 | Every BFF self-describes anonymously: `GET /api/config` returns the authority, client id and scope. | `ConfigEndpoints.cs:83-140` |
| F6 | Model 1 users are members of the environment security group `sprk-{customerId}-users` in Spaarke's tenant (T232; Dataverse requires it). The registry row records that group (`sprk_securitygroupid`), the customer id, App Service name, tenant id and tenancy model. | `provisioning.md` "Model 1 users"; `DataverseEnvironmentRecord.cs` |
| F7 | The only guest in Spaarke's tenant today is a personal Microsoft account. The test needs a work account from another Entra tenant. | read-only `az ad user list` |
| F8 | The add-in app `c1258e2d` "Spaarke Office Add-in" is single-tenant, has Graph `User.Read`/`email`/`profile` only, so `/me/memberOf` returns group ids without names. | word-add-in-r1; Graph read 2026-10-07 |
| F9 | The dev BFF app `1e40baad` (SDAP-BFF-SPE-API) pre-authorizes 9 clients, including the add-in `c1258e2d`, `SDAP-PCF-CLIENT` `170c98e1` and Microsoft Teams' first-party clients `1fec8e78` (desktop/mobile) and `5e3ce6c0` (web); it carries SPA redirects for the Teams tab host (`brk-multihub://green-dune…`). Its access tokens carry the optional claim `acct` (1 = guest). | Graph read 2026-10-07 |
| F10 | There is no production add-in site; only dev `spaarke-office-addins` (`icy-desert-0bfdbb61e.6.azurestaticapps.net`, `spe-infrastructure-westus2`). The admin guide's `spe-office-addins-prod` does not exist. Azure generates `*.azurestaticapps.net` host names; they cannot be chosen. | word-add-in-r1 |
| F11 | `spaarke.com` DNS is hosted at the registrar (Namecheap, `dns1/dns2.registrar-servers.com`), not Azure DNS. | `Resolve-DnsName`, 2026-10-07 |
| F13 | 🔴 A stamp BFF cannot serve **CIAM external contacts**: the `Ciam` JwtBearer scheme reads `Ciam:Instance/TenantId/Audience`, and no provisioning step (manifest, H4b, `customer.bicep`) sets them; the CIAM Graph provisioner (`Ciam:GraphProvisioner`) signs in with a Key Vault certificate, which a keyless stamp does not have. External contacts are local accounts in the shared CIAM tenant `spaarkeextid`, not guests. | `AuthorizationModule.cs:59-71`, `CiamGraphClientFactory.cs`, `appsettings.template.json:52-62`; spaarke-SPA-external-access-platform-r3 reply |
| F14 | The External Access SPA is origin-agnostic in code (`redirectUri = window.location.origin`) and already selects authority and scope at runtime (NAA requests `cfg.bffScope` dynamically); only its configuration is single-valued at build time. Its Teams manifest names the origin in 4 places and uses the backend app `1e40baad` as `webApplicationInfo` (the Teams-SSO fallback, which can name only one audience). Standalone in a browser it serves workforce users AND CIAM external contacts. | external-access-r3 reply (`notes/coordination/2026-10-07-from-external-access-r3.md`) |
| F12 | The Teams tab is the **External Access SPA** (`swa-spaarke-external-spa-dev`, `green-dune-0c4f1221e.7.azurestaticapps.net`); its Teams package is `src/client/external-spa/appPackage`, owned by spaarke-SPA-external-access-platform-r3, not word-add-in-r1. | word-add-in-r1; repo |

## Recommendation (2026-10-07, after the word-add-in-r1 reply): a Spaarke directory endpoint

The earlier recommendation (the client reads `/me/memberOf`) is withdrawn: it needs `GroupMember.Read.All` on the shared
client, depends on what a guest may read in Spaarke's directory, and bakes the BFF naming into the client. Instead,
as word-add-in-r1 recommends:

1. The client signs in to Spaarke's tenant (it already does, F4) and calls **one Spaarke-run directory endpoint** with
   that token.
2. The directory returns only the caller's own environments: `[{customerId, displayName, apiBaseUrl, authority, scope}]`,
   from the registry rows (active, `Setup Status = Ready`) whose `sprk_securitygroupid` the caller is a member of.
3. The client requests that BFF's scope (pre-authorized by H3, so no consent prompt) and calls it. A user in several
   environments picks one; the add-in remembers it in per-origin local storage.

Server-side details (decided in 240c):
- **Membership:** the directory's token carries the caller's `sprk-*-users` groups (`groupMembershipClaims =
  ApplicationGroup`, each environment group assigned to the directory app where provisioning creates the group). No Graph
  permission on either the client or the directory, and no Graph call per request.
- **Host:** its own minimal service in `rg-spaarke-shared-prod`, with its own managed identity that can only read the
  registry. Not the L2 control plane: L2 holds Owner on every customer subscription, and an end-user endpoint does not
  belong on that host.
- **Address:** a Spaarke custom domain, like the production add-in site.

No anonymous endpoint, no published customer list, and Model 2 later becomes data (the `authority` field), not code.

## Production client origins

- **Add-in:** one production site on a Spaarke custom domain, recommended `addins.spaarke.com` (F10, F11). The owner adds
  a CNAME at Namecheap; the add-in app gets `brk-multihub://addins.spaarke.com` and
  `https://addins.spaarke.com/auth-callback.html` as SPA redirects when the site is created.
- **Teams tab / External Access SPA:** one shared production site on a Spaarke custom domain (recommended
  `external.spaarke.com`, owner decision pending); every customer BFF lists it once in CORS (it covers Teams and the
  browser). Sign-in in Teams: a dedicated Spaarke Teams client app with nested app auth, the add-ins' shape (agreed by
  external-access-r3). Recommended: drop the Teams-SSO fallback for the multi-customer build, because a single manifest can
  name only one audience; then Teams' first-party clients (F9) need no pre-authorization on customer BFFs.

## Live actions (each owner-approved)

| Date | Action | Result |
|---|---|---|
| 2026-10-07 | Created Static Web App `swa-spaarke-office-addins-prod` (Standard, westus2, RG `rg-spaarke-shared-prod`, subscription Spaarke Shared Production `cd95fcec-6b89-49ea-8339-c2b579b12587`; tags application/environment/scope from the RG) for the production add-in site | default host `green-plant-09ecafa1e.1.azurestaticapps.net`; empty until word-add-in-r1 deploys |
| 2026-10-07 | Owner added CNAME `addins` → `green-plant-09ecafa1e.1.azurestaticapps.net` at Namecheap; custom domain `addins.spaarke.com` added to the site (`az staticwebapp hostname set`, CNAME validation) | Ready; `https://addins.spaarke.com` serves HTTP 200 with a valid managed certificate |

## Split

| Task | Scope | Depends on |
|---|---|---|
| **240a** server side, needed either way | (1) **CORS:** provisioning sets `Cors__AllowedOrigins__N` on every stamp BFF (the customer's Dataverse origins + the platform's shared client origins), so the BFF starts (F1). (2) **H3:** SPA redirect = the customer's own Dataverse origin(s) on its BFF app registration (fixes code pages, F3; H7's default kept); `preAuthorizedApplications` for the platform's configured client ids (the add-in `c1258e2d` now; Teams' clients if F12's answer needs them). | — |
| **240b** live test (owner-approved 2026-10-07) | A work-account guest from another tenant, in Outlook and Word (desktop + web): get a Spaarke-tenant token for the dev BFF through Office's nested app auth, and record `tid`, `oid`, `acct`, `idp`, `iss`, `aud`. Teams moves to the external-access project. | a guest account; the word-add-in-r1 diagnostics build |
| **240d** external contacts on stamps (F13) | Stamp BFFs serve CIAM external contacts: `Ciam:*` settings per stamp; the CIAM audience shape; a keyless CIAM Graph provisioner (federated credential from the stamp identity instead of the Key Vault certificate); how a contact reaches the right customer (the directory or the invitation); CORS for the SPA origin. Designed with spaarke-SPA-external-access-platform-r3 and unified-access-control-r2. | owner answer to Q3 (external contacts on customer BFFs) |
| **240c** directory + client discovery | The directory service (owner OK for a new shared-prod service), H-step assigning each environment group to the directory app, the registry fields it needs (e.g. the BFF app id for `scope`), the directory app registration with the add-in pre-authorized (one-time); the client change in the add-in (word-add-in-r1). | 240a, 240b; owner OK; word-add-in-r1 |
