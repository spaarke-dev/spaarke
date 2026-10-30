# External Access Teams App Guide

> **Last Updated**: 2026-10-29 (`spaarke-SPA-external-access-platform-r3`, after task 071's Teams live checks)
> **Purpose**: Build, deploy, install and support the Spaarke External Access **Teams app**: a Teams personal tab that hosts the External Access SPA.
> **Audience**: DevOps engineers (build and deploy), Entra and Teams administrators (install), support engineers (troubleshoot).
> **Applies To**: Microsoft Teams (desktop, web, mobile), Microsoft Entra ID, Azure Static Web Apps, the BFF (`Sprk.Bff.Api`)
> **Sibling guide**: [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) (the SPA, CIAM partners, grants)

Ids in this guide are shown as 8-character prefixes (`5859e812…`). Dev values are real examples; other environments use `{placeholders}`.

---

## Overview

What the Teams app is, in brief. The "why" lives in ADR-028 Amendment A9 and [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md).

- **One SPA, two hosts.** The Teams app is a personal tab (`staticTabs`, scope `personal`). Its `contentUrl` is the External Access SPA on the Static Web App. There is no separate Teams build. `main.tsx` detects the Teams host and runs `bootstrapTeams` instead of the browser bootstrap (`src/client/external-spa/src/main.tsx`).
- **The package carries no code.** The Teams zip holds only `manifest.json`, `color.png` and `outline.png`. The tab runs whatever SPA build is live on the Static Web App.
- **Workforce home plane only.** The tab serves people who sign in with their own organization's Entra account. CIAM partners do not use Teams; they use the browser SPA.
- **Tenant first, then sign-in.** The tab reads the Teams context tenant id (`user.tenant.id`). If no configured customer lists that tenant, it shows "Your organization is not set up for Spaarke" and makes no token request and no BFF call (`main.tsx` `bootstrapTeams`, `src/auth/backend-selection.ts` `resolveHomeBackendSelection`).
- **NAA, then a popup.** The tab gets the BFF token with Nested App Authentication (NAA) through the Teams host. If NAA fails, it falls back to an MSAL popup. It never redirects. Both use the **multitenant workforce client** on authority `https://login.microsoftonline.com/{Teams tenant id}` and scope `api://{BFF app id}/user_impersonation` (`src/auth/msal-auth.ts`, Teams section).
- **No Teams SSO.** The manifest declares no `webApplicationInfo`, and the code never calls `getAuthToken`. The package workflow fails if `webApplicationInfo` appears (`.github/workflows/deploy-teams-app.yml`, "Validate Teams app manifest").

### Who can use the tab

| Identity | What happens in Teams | Server outcome |
|---|---|---|
| **Registered workforce contact**: a member of a listed customer directory with a WorkforceHome contact | The workspace loads | `[WF-AUTH] Resolved home tid=… oid=… to WorkforceHome contact …` |
| **New workforce contact**: a member of a listed directory, not yet registered | The profile form at first sign-in, then the workspace | `registration_required` on `/me`, then `POST /api/v1/external/registration` 201 |
| **Licensed user**: a member of a listed directory with a linked Spaarke `systemuser` | The workspace loads. The first time, a silent account link runs; if it fails, the profile form offers "Link my account" (a popup) | `[WF-AUTH] Resolved home tid=… oid=… to linked systemuser …` |
| **Guest**: a B2B guest of the Teams tenant | "This tab can't be used with a guest account". No BFF call | None (refused in the tab). The BFF would refuse the token too (`acct` ≠ 0) |
| **Unlisted organization**: a Teams tenant no customer lists | "Your organization is not set up for Spaarke". No token request, no BFF call | None |

---

## Prerequisites

Each environment needs all of these before the tab can work. Dev values are in the Quick Reference.

### Entra: the multitenant workforce client (SPA)

- [ ] A **multitenant** public client app registration (`AzureADMultipleOrgs`) in Spaarke's tenant. Dev: "Spaarke Workforce SPA" (`5859e812…`). No secret, no certificate.
- [ ] Platform **Single-page application** with these redirect URIs (origin = the SPA host):
  - `https://{spa-host}`
  - `https://{spa-host}/auth-callback.html` (the popup lands here: `popupRedirectUri()` in `src/auth/msal-config.ts`)
  - `brk-multihub://{spa-host}` (required by Teams NAA; host only, no path)
- [ ] One delegated API permission: the BFF app's `user_impersonation`.
- [ ] `authenticationBehaviors.removeUnverifiedEmailClaim` = `true`.
- [ ] Its id is the GitHub variable `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`. It must differ from `VITE_TEAMS_MSAL_CLIENT_ID`. Both workflows and `getWorkforceHomeClientConfig()` (`src/config.ts`) refuse the pair.

### Entra: the single-tenant A8 client (licensed-user link only)

- [ ] The single-tenant client ("Spaarke Workspace", dev `9f1da340…`, variable `VITE_TEAMS_MSAL_CLIENT_ID`) keeps its SPA redirects `brk-multihub://{spa-host}` and `https://{spa-host}/auth-callback.html`. In Teams it is used only for a licensed user's one-time account link. Do not prune these redirects (`appPackage/env.dev.json` `$comment`).

### Entra: the BFF app (the API)

- [ ] `api.requestedAccessTokenVersion` = `2`. The WorkforceHome scheme requires the issuer to be exactly `https://login.microsoftonline.com/{tid}/v2.0`; a v1 token is refused (`AuthorizationModule.cs` `WorkforceHomeDenyReason`, reason `iss_not_exact_v2_for_tid`).
- [ ] It exposes `user_impersonation`.
- [ ] Access-token optional claims: `acct`, `email`, `given_name`, `family_name`. `acct` decides member versus guest. `email` is needed to register. `given_name` and `family_name` prefill the profile form (`WorkforceIdentityOptions.cs` `WorkforceCallerClaims.From`).
- [ ] `authenticationBehaviors.removeUnverifiedEmailClaim` = `true`.
- [ ] Stamps: H3 sets `acct` and `removeUnverifiedEmailClaim`; see [SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) (H3). The v2 token version on a stamp needs a stamp BFF build that accepts both audience forms (R3 task 077) first (`projects/spaarke-SPA-external-access-platform-r3/notes/task-073-gate-acme.md`).

### The customer tenant must be listed twice

- [ ] **In the SPA build:** the customer's Entra tenant id is in the backend entry's `homeTenantIds`, inside the GitHub variable `VITE_BACKENDS` (a JSON list). The one-entry `VITE_BFF_API_URL` fallback lists no home tenant, so without `VITE_BACKENDS` every Teams tenant is "not set up" (`backend-selection.ts` `resolveHomeBackendSelection`).
- [ ] **In the BFF:** the same tenant id is in `WorkforceIdentity:CustomerTenantIds` (App Service form `WorkforceIdentity__CustomerTenantIds__0`, `__1`, …). Empty denies everyone (`WorkforceIdentityOptions.cs`).
- [ ] Never list Spaarke's own tenant, the CIAM tenant or the Microsoft consumer-account tenant in `homeTenantIds`. The SPA refuses such an entry (`backend-selection.ts` `reservedHomeTenantIds`). So a Spaarke-tenant user opening the tab in Spaarke's tenant sees "not set up". This is by design.

Example `VITE_BACKENDS` entry (dev shape, ids masked):

```json
[{"customerKey":"default","displayName":"Spaarke (dev)","bffBaseUrl":"https://spaarke-bff-dev.azurewebsites.net","bffAppId":"<bff-app-id>","ciamBffAppId":"<ciam-bff-app-id>","homeTenantIds":["<customer-tenant-id>"]}]
```

### Customer tenant consent

- [ ] An administrator of the customer directory grants **admin consent for the multitenant workforce client on the BFF's `user_impersonation`** (delegated). The BFF app's service principal must exist in the customer directory as a resource. It needs no grants of its own there.
- [ ] After consent, check the customer's **Enterprise applications**. The BFF app's entry must hold **no** granted permissions. If it holds any, remove them and tell the Spaarke security owner. Grant consent only for the workforce client's delegated scope; the rules for stamps are in `task-073-gate-acme.md`. The stamp consent procedure is **not designed yet** (2026-10-29).

### Users

- [ ] A workforce contact needs an **email attribute** on their Entra account to register. Registration reads only the token's `email` claim, never the username (`ExternalRegistrationEndpoints.cs`, `sdap.access.registration_email_missing`). Known defect: the form still shows the username as the email, then refuses (ISS-023, #2449).

### Teams pre-authorization: not needed (finding, 2026-10-29)

The BFF app does **not** need the Teams client ids (`1fec8e78…` desktop/mobile, `5e3ce6c0…` web) or the Microsoft Authentication Broker in its pre-authorized applications for NAA. Microsoft's NAA page says: "You don't need to preauthorize your hosts, such as Teams or Microsoft 365, to call your endpoints." ([Nested app authentication](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication)). Those ids are for Teams SSO (`getAuthToken` / `webApplicationInfo`), which this app does not use. What NAA does need is consent for **our** client (above) and the `brk-multihub://` redirect.

This is a documentation finding, not a live test: the dev BFF app already pre-authorizes those ids, so dev runs cannot show it. Leave the dev pre-authorizations in place; other projects may rely on them.

### BFF and hosting

- [ ] The SPA origin is in the BFF CORS list (`Cors__AllowedOrigins__N`). The Teams tab calls the BFF from the SPA origin, like the browser.
- [ ] `ExternalAccess__CustomerBusinessUnitId` is set. Without it registration answers 500 `registration_owner_unconfigured`.
- [ ] `AzureAd__TenantId` is Spaarke's tenant (a GUID). If it is not, the WorkforceHome scheme refuses every token (`spaarke_tenant_id_not_configured`).
- [ ] `staticwebapp.config.json` allows Teams to frame the SPA and serves the popup page (see Configuration).

### Tools and access

- [ ] `gh` CLI signed in with permission to run workflows in `spaarke-dev/spaarke`.
- [ ] A Teams administrator role, **active**, in the target tenant (see `INVALID_PRIVILEGE` in Troubleshooting).
- [ ] Read access to the environment's Application Insights.

---

## Quick Reference

| Item | Value |
|---|---|
| Teams app package source | `src/client/external-spa/appPackage/` (`manifest.json` = dev, `manifest.prod.json` = prod) |
| Teams app id (dev) | `23610794…` (in `manifest.json`); prod comes from the `TEAMS_APP_ID` variable |
| Manifest | schema 1.29; dev `version` 1.2.0 |
| Icons | `color.png` 192x192 PNG; `outline.png` 32x32 PNG, transparent background |
| Tab | `staticTabs[0]`, entity `spaarkeCollaborationHome`, scope `personal` |
| SPA host (dev) | `https://green-dune-0c4f1221e.7.azurestaticapps.net` |
| SPA host (prod) | `https://external.spaarke.com` |
| BFF (dev) | `https://spaarke-bff-dev.azurewebsites.net` |
| Multitenant workforce client (dev) | "Spaarke Workforce SPA", `5859e812…` |
| A8 client (dev) | "Spaarke Workspace", `9f1da340…` |
| BFF app (dev) | `1e40baad…` |
| Package workflow | `.github/workflows/deploy-teams-app.yml` (manual; input `environment` = `dev` or `prod`) |
| SPA workflow | `.github/workflows/deploy-external-spa.yml` (manual; input `environment`) |
| GitHub environments | `external-spa-dev`, `external-spa-prod` |
| Package artifact | `teams-app-package` (dev) or `teams-app-package-prod`, file `appPackage.zip`, kept 90 days |
| SPA deploy tag | `external-spa-dev-{sha}` |
| SPA rollback tag | `external-spa-dev-rollback-{sha}` |
| App Insights (dev) | `spe-insights-dev-67e2xz` |
| Log prefixes | `[WF-AUTH]`, `[A9-REG]`, `[AUTH] sdap.auth.deny.workforce_home` |

---

## Procedure

### Step 1: Set the GitHub environment variables

Both workflows read the same variables from the GitHub environment (`external-spa-dev` or `external-spa-prod`). No id or URL is written in the workflow files.

| Variable | Required | Notes |
|---|---|---|
| `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` | Yes | The multitenant workforce client |
| `VITE_BACKENDS` | Yes, for Teams | JSON list; each Teams customer entry needs `homeTenantIds` |
| `VITE_BFF_API_URL` | If `VITE_BACKENDS` is unset | One-entry fallback; lists no home tenant, so Teams shows "not set up" |
| `VITE_TEAMS_MSAL_CLIENT_ID`, `VITE_TEAMS_MSAL_TENANT_ID`, `VITE_TEAMS_MSAL_BFF_SCOPE` | Yes | The A8 client, Spaarke's tenant, `api://{id}/user_impersonation` |
| `VITE_MSAL_AUTHORITY`, `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_MSAL_BFF_SCOPE` | Yes | The CIAM (partner) plane; the build checks them too |
| `TEAMS_APP_ID` | prod only | The production Teams app id; not the all-zero placeholder |
| Secret `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` / `_PROD` | Yes (SPA deploy) | The Static Web App deploy token |

```powershell
gh variable list -R spaarke-dev/spaarke --env external-spa-dev --json name --jq '.[].name'
```

**Expected output**: every name above for that environment. The workflows' "Check required environment variables" step fails the run before building if one is missing or has the wrong format.

### Step 2: Deploy the SPA (always before a package)

The tab runs the SPA build on the Static Web App. Deploy the SPA first; publish a package only when the manifest or icons change.

1. Record the live build so you can roll back. A Static Web App keeps no previous build.

   ```powershell
   gh run list -R spaarke-dev/spaarke --workflow deploy-external-spa.yml -L 5 --json headSha,headBranch,conclusion,createdAt
   ```

2. Create the rollback tag for the live commit and push it:

   ```powershell
   git tag external-spa-dev-rollback-<live-sha> <live-sha>
   git push origin external-spa-dev-rollback-<live-sha>
   ```

3. Tag the commit to deploy and run the workflow on the tag. `workflow_dispatch` takes a branch or tag as `--ref`, not a bare SHA, so tag the exact commit:

   ```powershell
   git tag external-spa-dev-<sha> <sha>
   git push origin external-spa-dev-<sha>
   gh workflow run deploy-external-spa.yml -R spaarke-dev/spaarke --ref external-spa-dev-<sha> -f environment=dev
   gh run watch <run-id> -R spaarke-dev/spaarke --exit-status
   ```

**Expected output**: the run succeeds. The workflow checks variables, builds, runs `npm run check:dist`, copies `staticwebapp.config.json` into `dist/` and uploads to the Static Web App.

4. Check the live site:

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}`n" https://green-dune-0c4f1221e.7.azurestaticapps.net/
   curl.exe -s -o NUL -w "%{http_code}`n" https://green-dune-0c4f1221e.7.azurestaticapps.net/auth-callback.html
   curl.exe -sI https://green-dune-0c4f1221e.7.azurestaticapps.net/ | Select-String -Pattern "content-security-policy"
   ```

**Expected output**: `200`, `200`, and a header containing `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`.

**Rollback**: `gh workflow run deploy-external-spa.yml -R spaarke-dev/spaarke --ref external-spa-dev-rollback-<live-sha> -f environment=dev`.

### Step 3: Build the Teams package

Only when the manifest or icons changed. Raise `version` in the manifest for every changed package (dev is at 1.2.0).

```powershell
gh workflow run deploy-teams-app.yml -R spaarke-dev/spaarke --ref master -f environment=dev
gh run watch <run-id> -R spaarke-dev/spaarke --exit-status
gh run download <run-id> -R spaarke-dev/spaarke -n teams-app-package -D .\teams-pkg
```

What the workflow does (`deploy-teams-app.yml`):

1. Checks the environment variables (same checks as the SPA workflow; prod also needs `TEAMS_APP_ID`).
2. Builds the SPA as a sanity gate. It does **not** deploy that build.
3. Stages the manifest: dev copies `manifest.json`; prod renders `manifest.prod.json` with `id` = `TEAMS_APP_ID`.
4. Validates the manifest: `manifestVersion`, `id`, `staticTabs`, `validDomains` present; no `webApplicationInfo`; every `contentUrl` is `https://` and its host is in `validDomains`; no unrendered `${{…}}` placeholder.
5. Checks `color.png` and `outline.png` exist.
6. Zips `manifest.json`, `color.png` and `outline.png` at the archive root and fails unless the zip has exactly 3 files. The `env.*.json` files are never packaged.
7. Publishes `teams-app-package` (dev) or `teams-app-package-prod`, kept 90 days.

**Expected output**: `teams-pkg\appPackage.zip` (about 2.5 KB for dev).

For **prod**, use `-f environment=prod` and `-n teams-app-package-prod`. The production package is built only. Do not submit it to a tenant until go-live is approved (task 073 gate).

### Step 4: Install in the customer tenant

Do this in the **customer's** tenant, as a Teams administrator of that tenant. Uploaded in a tenant that no customer lists, the tab opens to "Your organization is not set up for Spaarke". This is by design.

**Option A: Teams admin center (preferred)**

1. Open the Teams admin center → **Teams apps** → **Manage apps**.
2. If "Spaarke" is not listed: **Upload new app** → choose `appPackage.zip`. If it is listed: open it and upload the new version.
3. Open the app and set its availability: **Status** Allowed, and who it is available to (Everyone, or specific users or groups).
4. Check the org-wide app settings: custom apps must be allowed. On tenants that still use app permission policies, the users' policy must allow the app.

**Option B: the Teams client.** An administrator can use **Apps** → **Manage your apps** → **Upload an app** → **Upload an app to your org's app catalog**. Use this when the admin center is not usable (see `INVALID_PRIVILEGE`).

**Avoid the Developer Portal "Publish to your org".** It creates an approval request, not an installed app. An administrator must then approve it in the admin center. It can appear as a second "Spaarke" entry with status Blocked or Submitted next to the uploaded one. If you used it by mistake, cancel the request or reject the submission so only one entry stays (seen in Spaarke's tenant, 2026-10-29).

**Propagation.** Availability and policy changes can take hours to reach users (Microsoft states up to 24 hours). If a user cannot add the app right after upload, wait before debugging.

**Expected output**: users in scope find "Spaarke" under Apps (built for your org) and can add it. The tab is named "Spaarke".

**Rollback**: upload the previous package version, or set the app's status to Blocked. The SPA is not affected.

### Step 5: Verify (see Verification)

Run the checklist below and read App Insights.

---

## Configuration

### Manifest (`src/client/external-spa/appPackage/manifest*.json`)

| Field | Dev | Prod | Notes |
|---|---|---|---|
| `id` | `23610794…` | `${{TEAMS_APP_ID}}`, rendered by CI | Must be a GUID, not all zeros |
| `version` | `1.2.0` | `1.0.0` | Raise it for every changed package |
| `staticTabs[0].contentUrl` / `websiteUrl` | the dev SWA host | `https://external.spaarke.com` | `websiteUrl` is what "Open app in browser tab" opens |
| `validDomains` | SWA host, `spaarke-bff-dev.azurewebsites.net`, `teams.microsoft.com`, `*.cloud.microsoft` | `external.spaarke.com`, `teams.microsoft.com`, `*.cloud.microsoft` | Must contain the `contentUrl` host |
| `permissions` | `["identity"]` | same | |
| `webApplicationInfo` | absent | absent | Must stay absent; CI fails otherwise |

`appPackage/env.dev.json` and `env.prod.json` are reference files. No build step reads them, and they are not packaged. `tests/production-package.test.ts` checks `env.prod.json`.

### Static Web App (`src/client/external-spa/staticwebapp.config.json`)

| Setting | Value | Why it matters for Teams |
|---|---|---|
| `globalHeaders.Content-Security-Policy` | `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft` | Without the Teams origins, Teams cannot frame the tab |
| `navigationFallback.exclude` | includes `/auth-callback.html` | The popup must get the real bridge page, not `index.html` |
| `public/auth-callback.html` | copied to `dist/` by Vite | The MSAL popup redirect page |

### BFF settings (App Service)

| Setting | Default | Description |
|---|---|---|
| `WorkforceIdentity__CustomerTenantIds__N` | empty (denies all) | Customer home directories the WorkforceHome scheme accepts. Read per request; startup refuses a non-GUID, the all-zero GUID or the CIAM tenant |
| `AzureAd__ClientId` | — | The BFF app id; the WorkforceHome audience (bare id or `api://{id}`) |
| `AzureAd__TenantId` | — | Spaarke's tenant. Tokens from this tenant are refused by the WorkforceHome scheme |
| `ExternalAccess__CustomerBusinessUnitId` | — | Owner business unit for newly registered contacts |

The WorkforceHome scheme accepts a token only when all of these hold (`AuthorizationModule.cs` `WorkforceHomeDenyReason`): `tid` is a customer tenant in `CustomerTenantIds` (and is neither Spaarke's nor the CIAM tenant); `iss` is exactly `https://login.microsoftonline.com/{tid}/v2.0`; it is a user token whose `scp` contains `user_impersonation`; and `acct` = `0`.

---

## Verification

### The 9-run check

Run each identity on each client. Results of the dev run on 2026-10-29 are in `projects/spaarke-SPA-external-access-platform-r3/notes/fr-21-teams-live-check.md` (8 of 9 passed; unlisted × mobile was deferred by the owner).

| Identity | Desktop | Web | Mobile |
|---|---|---|---|
| Registered workforce contact | Workspace loads, full function | Workspace loads (after Teams' warning page, if shown: "Show the app anyway") | Workspace loads (layout not phone-optimized) |
| Licensed user (linked `systemuser`) | Workspace loads, no profile form | Same as desktop (after the warning page, if shown) | Workspace loads |
| Unlisted organization | "Your organization is not set up for Spaarke" | Same | Same (expected; not run on dev) |

A run passes when:

1. The tab opens **inside Teams**. On Teams web, use "Show the app anyway", not "Open app in browser tab" (that opens the standalone SPA, which proves nothing about Teams).
2. A workforce contact or licensed user sees the workspace with no error screen. For a new contact, the profile form appears once, then the workspace.
3. An unlisted organization sees the not-set-up screen, and App Insights shows no `/api/v1/external/*` request from it.
4. App Insights shows the expected line for each signed-in identity (next section) and no `[WF-AUTH] Denying request` line except `registration_required` before a first registration.

### App Insights checks

```kusto
traces
| where timestamp > ago(1h)
| where message has "[WF-AUTH]" or message has "[A9-REG]" or message has "sdap.auth.deny.workforce_home"
| project timestamp, severityLevel, message
| order by timestamp desc
```

| Expect | Line |
|---|---|
| Workforce contact | `[WF-AUTH] Resolved home tid=… oid=… to WorkforceHome contact …` |
| Licensed user | `[WF-AUTH] Resolved home tid=… oid=… to linked systemuser …` |
| After resolution | `[WF-AUTH] Workforce {Kind} (systemuser=…, contact=…) resolved with N project / N matter / N work-assignment accessible roots` |
| Unregistered caller (normal, once) | `[WF-AUTH] Denying request: sdap.access.registration_required` |
| Not expected | `[AUTH] sdap.auth.deny.workforce_home … (Reason) Tid=…` at Warning level |

A Debug-level `workforce_home` deny with reason `tid_is_spaarke_tenant` or `tid_is_ciam_tenant` is expected noise: the external policy tries every scheme.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "Your organization is not set up for Spaarke" | The Teams tenant is not in any `VITE_BACKENDS` entry's `homeTenantIds` in the **live** SPA build, or `VITE_BACKENDS` is unset, or the tenant is reserved (Spaarke's, CIAM, consumer) | Add the tenant to `homeTenantIds`, redeploy the SPA (Step 2). Also add it to `WorkforceIdentity:CustomerTenantIds`. Expected for Spaarke's own tenant |
| "No organization available" | The backend list could not be read (bad `VITE_BACKENDS`, directory error) | Fix `VITE_BACKENDS` (a valid JSON list); redeploy |
| "This tab can't be used with a guest account" | See the guest section below | See below |
| "Sign-in failed" with a diagnostic block | NAA and the popup both failed | See the sign-in section below |
| Teams web shows "This app may have issues in the web version of Teams" | Teams' own page, before Spaarke loads | Choose **Show the app anyway**. See below |
| Teams desktop cannot sign in to Teams itself | Teams client problem, before Spaarke loads | See below |
| Profile form, then "We could not read your email address" | The user's Entra account has no email attribute, so the token has no `email` claim (`[A9-REG] Registration refused: the home token carries no usable email claim`) | An administrator of the customer directory sets the user's email attribute; the user signs in again. Known UI defect ISS-023 (#2449) |
| Registration answers 500 `registration_owner_unconfigured` | `ExternalAccess__CustomerBusinessUnitId` unset or invalid | Set it on the BFF |
| Signed in but every BFF call is 401; App Insights shows `sdap.auth.deny.workforce_home` at Warning | The token failed the WorkforceHome rules. The `(Reason)` names which: `tid_not_listed`, `customer_tenant_list_empty`, `iss_not_exact_v2_for_tid`, `acct_missing`, `acct_not_member`, `user_impersonation_scope_missing`, `spaarke_tenant_id_not_configured` | `tid_not_listed`/`customer_tenant_list_empty`: add the tenant to `CustomerTenantIds`. `iss_not_exact_v2_for_tid`: set the BFF app to v2 tokens. `acct_missing`: add the `acct` optional claim. `spaarke_tenant_id_not_configured`: set `AzureAd__TenantId` |
| Upload error "Provided image file color.png is invalid. The image is either empty or too small" | Seen 2026-10-29. Cause **not confirmed**. The repo icons are 192x192 and 32x32 RGBA PNGs | Re-download the artifact and check the zip's icons are intact (`file color.png outline.png` in Git Bash). Check the icons meet Microsoft's rules (color 192x192; outline 32x32, white on transparent). Try importing the zip in the Teams Developer Portal to see its validation |
| Teams admin center shows `INVALID_PRIVILEGE` ("You don't have access") for a Global Administrator | Most likely the admin role is not **active** (eligible only, under PIM). **Unconfirmed**; Microsoft documents this page only as "no admin role" | Activate the Teams Administrator or Global Administrator role, sign out, sign back in. Or use Option B (Teams client upload to the org catalog) |
| Users cannot find or add the app | Availability not set, custom apps blocked org-wide, the user's policy blocks it, or propagation delay | Check Status, "Available to", the org-wide custom app setting and permission policies (Step 4). Wait up to 24 hours after a change |
| Two "Spaarke" entries, one Blocked or Submitted | A Developer Portal "Publish to your org" request next to the uploaded app | Cancel or reject the submission (Step 4) |
| Teams mobile layout is cramped | The layout is not optimized for phones | Known; deferred to R4 (ISS-025, #2466). The tab loads and works |

### "This tab can't be used with a guest account"

The tab shows this after it gets a token and judges the signed-in account a guest of the Teams tenant. It makes no BFF call. Below the message is a line like:

```
Diagnostic: path=naa acct=1 home=differs ext=yes src=fresh
```

The line holds no ids, names or addresses (`teamsGuestVerdict` in `src/auth/msal-auth.ts`). The fields:

| Field | Values | Meaning |
|---|---|---|
| `path` | `naa`, `popup` | Which step yielded the token. `popup` means NAA failed or the host did not recommend NAA |
| `acct` | `0`, `1`, `other`, `missing` | The token's `acct` claim. It decides the verdict when present: `0` = member, anything else = not a member. The BFF requires `0` too |
| `home` | `match`, `differs`, `unreadable`, `none` | Whether the Teams account's home tenant equals the Teams tenant. Used only when `acct` is `missing` |
| `ext` | `yes`, `no` | Whether the username carries `#EXT#` (the B2B guest form). Used only when `acct` is `missing` |
| `src` | `cache`, `fresh` | Whether MSAL served the token from its cache or Entra issued it now |

How to read it:

- **`acct=1 … src=fresh`**: a real non-member token. The person is a B2B guest of this organization in Teams. They must open Spaarke in Teams signed in to their **own** organization. This is correct behaviour.
- **`acct=1 … src=cache`**: the token came from MSAL's cache. It can belong to an account that signed in earlier in the same Teams window. Have the user sign out of Teams and back in (or clear the Teams cache), then retry. If it then passes, it was a stale cached account.
- **`acct=missing`**: the BFF app does not emit `acct`. Add the access-token optional claim (Prerequisites). The verdict then came from `home`/`ext`, and the BFF will refuse the token anyway.
- **`path=popup`**: NAA did not work on this client. Note the client (desktop, web, mobile) and report it with the line.

**History.** Before 2026-10-29 the tab judged "guest" from the Teams host's cached account description, and Teams desktop showed members this screen. PR #2461 changed the verdict to the token's `acct` claim and added the diagnostic line. It is in dev SPA builds from master `96f1f6b675`. If a member still sees this screen on a build with the fix, collect the Diagnostic line.

### "Sign-in failed"

The tab shows this when NAA and the popup both failed. It has a **Sign in** button that opens the popup from a click (a click is needed so the browser does not block the popup), and a block headed `Diagnostic detail (share this):` with up to three lines (`src/auth/auth-diagnostics.ts`):

```
top: name=TeamsWorkforceAuthError  ·  msg=…
NAA: name=…  ·  errorCode=…  ·  subError=…  ·  msg=…  ·  AADSTS=…
Popup: name=…  ·  errorCode=…  ·  msg=…
```

The block holds no tokens. Ask the user for a screenshot. Then:

| What the block shows | Likely cause | Fix |
|---|---|---|
| `Popup: … errorCode=popup_window_error` or a blocked popup | The bootstrap popup had no user click | Click **Sign in** |
| `NAA: … errorCode=user_canceled` or `user_cancelled` | The user closed the sign-in | Click **Sign in** |
| `AADSTS65001` | Consent missing for the workforce client in the customer directory (expected code; not observed live for the tab) | The customer admin grants consent (Prerequisites) |
| `AADSTS50011` | A redirect URI is missing on the workforce client (expected code; not observed live) | Add `brk-multihub://{spa-host}` and `https://{spa-host}/auth-callback.html` |
| `msg=The sign-in returned a token for a different organization than this Teams tab belongs to` | The popup signed in a different account | Sign in with the account of the organization Teams is open in |
| `msg=The Teams context tenant id is not valid` | Teams gave no usable tenant id | Restart Teams; report the client |
| `msg=Teams JS app.initialize() failed` | The page is not running inside a Teams host | Open it from Teams |

### Teams web: "This app may have issues in the web version of Teams"

Teams web shows this page before the app loads. Spaarke code does not produce it. Buttons:

- **Show the app anyway**: opens the tab inside Teams web. This works (2026-10-29, both identities).
- **Open app in browser tab**: opens the manifest `websiteUrl`, the standalone SPA on the browser sign-in plane. It does not test the Teams path.
- **Open Teams desktop**: switches to the desktop client.

Public reports link the page to new Teams web with third-party cookies blocked (for example, private windows): [OfficeDev/microsoft-teams-library-js#2185](https://github.com/OfficeDev/microsoft-teams-library-js/issues/2185). The cause is **not confirmed** for our case.

### Teams desktop's own sign-in fails

Symptoms: "We can't connect you", an HTTP 400 at `login.microsoftonline.com`, or "No Network Connection [2603]". This is the Teams client signing the user in to Teams, before Spaarke loads. It is not a Spaarke defect. On 2026-10-29 it happened on one PC and the same user worked on another PC.

General remedies (not verified against this case): **Add another account** in Teams; reset the Teams app (Windows Settings → Apps → Microsoft Teams → Advanced options → Reset); or try another Windows profile or PC. Use Teams web to confirm the Spaarke tab itself works.

### Where to look

- **App Insights** (dev `spe-insights-dev-67e2xz`): the query in Verification. `[WF-AUTH]` lines show how each caller resolved or why it was denied. `[A9-REG]` lines show registration refusals. `[AUTH] sdap.auth.deny.workforce_home` shows WorkforceHome scheme refusals with a reason.
- **Live BFF log stream**: `az webapp log tail -g rg-spaarke-dev -n spaarke-bff-dev`, then filter for `[WF-AUTH]`.
- **The tab itself**: the guest Diagnostic line and the sign-in diagnostic block. Teams desktop has no easy developer tools, so these screens are the main client-side evidence.

---

## Related

- [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md): the SPA hosting, CIAM partners, grants and access levels
- [EXTERNAL-ACCESS-SPA-GUIDE.md](EXTERNAL-ACCESS-SPA-GUIDE.md): SPA development and environment variables
- [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md): architecture
- [SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md): stamp provisioning (H3 app registration, H4b CORS and settings)
- [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md): auth architecture (Amendment A9: the workforce home plane and the Teams host)
- [UAC-ACCESS-MODEL-GUIDE.md](UAC-ACCESS-MODEL-GUIDE.md): what a signed-in workforce user can see
- Project records (dated history): `projects/spaarke-SPA-external-access-platform-r3/notes/` (`task-059-dev-setup-record.md`, `task-070-deploy-record.md`, `fr-21-teams-live-check.md`, `task-073-gate-acme.md`)
- Microsoft: [Nested app authentication](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/authentication/nested-authentication), [Manage apps in the Teams admin center](https://learn.microsoft.com/en-us/microsoftteams/manage-apps), [Custom app policies and settings](https://learn.microsoft.com/en-us/microsoftteams/teams-custom-app-policies-and-settings), [App icons](https://learn.microsoft.com/en-us/microsoftteams/platform/concepts/design/design-teams-app-icon-store-appbar)
