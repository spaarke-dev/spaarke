# Task 059 — dev Entra and settings setup for A9 (plan and record)

> **Status (2026-10-19): stage R (read-only) DONE with owner approval ("proceed"). Stop checks S-1 and S-2 fired, so no change has run. Waiting for the owner's decision (§11).** Each stage runs only after the owner approves that stage.
> Task: `tasks/059-dev-entra-and-settings-a9.poml` (prescriptive). Design: `notes/a9-bff-design.md` §8, §9, §12.
> Ids appear masked to 8 characters. Commands use the shell variables set in §1; the session fills them from the reads and shows each value masked.
> No secret, certificate, token or key value is created, read or printed. Every "before" snapshot is saved to the session scratchpad and is the rollback input.

## 0. What this sets up, and why

After 059, a Dewey Cheatham employee can sign in to the dev SPA, in the browser or in Teams, with their own company account. The BFF then accepts that token.

| # | Thing | Why |
|---|---|---|
| A | New app **"Spaarke Workforce SPA"**: multitenant public client in the Spaarke tenant | The A9 sign-in client (tasks 058 and 075). Nothing uses it until the SPA is redeployed with its id |
| B | Dev BFF app `1e40baad`: multitenant, known client = A, access-token claims | So a customer directory can consent to it and issue it tokens that the BFF's `WorkforceHome` scheme accepts |
| C | Admin consent in Dewey Cheatham (`bc3aa7f4`), by its admin | One consent covers A and B for that directory |
| D | `spaarke-bff-dev` settings: `CustomerTenantIds` += `bc3aa7f4`, `ExternalAccess__CustomerBusinessUnitId` = BU1 | The BFF allowlist; the owner team for new contacts |
| E | GitHub `external-spa-dev`: `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`, `VITE_BACKENDS` | The SPA build. Without `VITE_BACKENDS` + `homeTenantIds`, every A9 sign-in shows "not set up" |
| F | SPA deploy (`deploy-external-spa.yml`, dev) | Ships the 058 and 075 code. Today's dev SPA is `ebc74b331`, built before both |

**Two things the POML did not list, which the code needs (found while planning):**
1. **Access-token optional claims on `1e40baad`.** The registration endpoint takes the address only from the access token's `email` claim (`ExternalRegistrationEndpoints.cs:256-265`). Without it every registration is refused with `registration_email_missing`. The prefill reads `given_name` and `family_name` (`WorkforceIdentityOptions.cs:180-181`), and the BFF scheme requires `acct` = 0 (`AuthorizationModule.cs` `WorkforceHomeDenyReason`). v2 access tokens carry these only when they are configured as optional claims. So step B reads them and adds any that are missing.
2. **Token version.** The `WorkforceHome` scheme requires the issuer to be exactly `https://login.microsoftonline.com/{tid}/v2.0`, which only v2 tokens have. If `1e40baad` is not already on `requestedAccessTokenVersion` = 2, changing it also changes the tokens issued to every dev caller of `1e40baad`: PCFs, code pages, the Console, the Office add-ins, the Copilot agent and the SPA. **In that case this plan stops** and puts the change to the owner as its own decision (§3, stop S-1).

## 1. Variables (set by the session from the reads; shown masked)

| Variable | Value | Source |
|---|---|---|
| `$SPAARKE_TID` | `a221a95e…` | Spaarke tenant (R1 confirms the CLI sign-in is there) |
| `$CUST_TID` | `bc3aa7f4…` | Dewey Cheatham home directory. The full id is in `scripts/Set-ExternalFlagForB2BGuests.ps1:77`; R6 and the owner confirm it is Dewey Cheatham |
| `$BFF_APP_ID` / `$BFF_OBJ` / `$BFF_SP` | `1e40baad…` / from R2 / from R2 | Dev BFF app |
| `$UI_SCOPE_ID` | from R2 | Id of `user_impersonation` on `1e40baad` |
| `$NEW_APP_ID` / `$NEW_OBJ` | from C1 | The new client |
| `$CIAM_BFF_APP_ID` | `4a4d5126…` | The app id inside `external-spa-dev` `VITE_MSAL_BFF_SCOPE` (R8 re-reads it) |
| `$BU1` | `cb15f587…` | Dev business unit for new contacts (ISS-005 option A) |
| `$SWA` | `https://green-dune-0c4f1221e.7.azurestaticapps.net` | Dev SPA origin |

## 2. Stage R — read-only reads (approve once; nothing changes)

Run as the session's current `az` sign-in (`ralph.schroeder@spaarke.com`, Spaarke tenant) and `gh` sign-in. Output is recorded masked.

| # | Command | What it answers |
|---|---|---|
| R0 | `gh variable list -R spaarke-dev/spaarke --json name --jq '.[].name'` | **Your 076 question:** the names of repository-level Actions variables (names only, no values). A `VITE_*` or `TEAMS_APP_ID` name here would silently fill a missing environment value. `spaarke-dev` is a user account, so organization-level variables don't exist |
| R1 | `az account show --query "{tenant:tenantId, user:user.name}" -o json` | The sign-in is in the Spaarke tenant |
| R2 | `az ad app show --id $BFF_APP_ID --query "{obj:id, audience:signInAudience, tokenVersion:api.requestedAccessTokenVersion, known:api.knownClientApplications, scopes:api.oauth2PermissionScopes[].{v:value,id:id,on:isEnabled}, preAuth:api.preAuthorizedApplications[].appId, idUris:identifierUris, accessClaims:optionalClaims.accessToken[].name, passwordCreds:length(passwordCredentials), keyCreds:length(keyCredentials), fics:'see R2b'}" -o json` → saved as `before-1e40baad.json` | The current audience, token version, known clients, claims and scope id. **Escalation trigger:** `passwordCreds` > 0 (a client secret on the BFF app) → STOP (no-secret rule) |
| R2b | `az rest --method GET --url "https://graph.microsoft.com/v1.0/applications/$BFF_OBJ/federatedIdentityCredentials?\$select=name,issuer,subject"` | How the BFF app authenticates (expect the MI federated credential, ADR-028 A4). Recorded, not changed |
| R2c | `az rest --method GET --url "https://graph.microsoft.com/beta/applications/$BFF_OBJ?\$select=authenticationBehaviors"` | The current `removeUnverifiedEmailClaim` |
| R3 | `az ad sp show --id $BFF_APP_ID --query "{sp:id, enabled:accountEnabled}" -o json`, then `az rest --method GET --url "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?\$filter=resourceId eq '$BFF_SP'&\$select=clientId,consentType,scope"` | **Who depends on `1e40baad` in the Spaarke tenant** (the escalation trigger "read its other clients first"). Each `clientId` is resolved to a display name |
| R4 | `az ad app list --display-name "Spaarke Workforce SPA" --query "[].{appId:appId, audience:signInAudience}" -o json` | No app with that name exists yet (else stop and ask) |
| R5 | `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev -o json`, piped to a local filter that prints only the names and masked values of `AzureAd__ClientId`, `AzureAd__TenantId`, `AzureAd__Audience` (S-1), `Ciam__TenantId`, `WorkforceIdentity__CustomerTenantIds__*`, `ExternalAccess__CustomerBusinessUnitId` → full output saved as `before-bff-settings.json` | The current allowlist (append, never replace) and whether the BU setting exists |
| R6 | `az rest --method GET --url "https://graph.microsoft.com/v1.0/tenantRelationships/findTenantInformationByTenantId(tenantId='$CUST_TID')" --query "{name:displayName, domain:defaultDomainName}"` | That `$CUST_TID` is Dewey Cheatham |
| R7 | `az rest --resource https://spaarkedev1.crm.dynamics.com --method GET --url "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/teams?\$filter=_businessunitid_value eq $BU1 and isdefault eq true&\$select=teamid,name"` and `…/businessunits($BU1)?\$select=name,isdisabled` | BU1 exists, is enabled and has exactly one default team (else registration returns 500 `registration_owner_unconfigured`) |
| R8 | `gh variable list -R spaarke-dev/spaarke --env external-spa-dev --json name,value`, printed masked | The current `external-spa-dev` values (expect `VITE_BACKENDS` unset, as on 2026-10-13) |
| R9 | `curl.exe -s -o NUL -w "%{http_code}" https://spaarke-bff-dev.azurewebsites.net/healthz` | Baseline before any change |

**After R, the session reports the results and the stop checks (§3) before asking for stage A.**

## 3. Stop checks after the reads (no change runs if one fires)

| Stop | Condition | What happens |
|---|---|---|
| S-1 | `1e40baad` `tokenVersion` is not 2 (null means 1) | Stop. A9 cannot work without the change: v1 home tokens have issuer `sts.windows.net`, which the `WorkforceHome` scheme refuses. But the change is not local. Every dev caller of `1e40baad` then gets v2 tokens: `aud` becomes the bare GUID, `appid` becomes `azp`, and `upn`/`unique_name` drop out. The BFF's default scheme may pin `AzureAd:Audience` to `api://…` (R5 reads it), and a v2 token would then be refused, so every dev surface would get 401 (research item 1). The decision goes to the owner with options: (a) a BFF change first (accept both audience forms on the default scheme, plus a test that sends it a v2 token), deployed, then the flip; (b) postpone A9 on dev. |
| S-2 | `1e40baad` has a client secret (`passwordCreds` > 0) | Stop (POML escalation trigger) |
| S-3 | R3 shows a caller that a multitenant audience would change | Stop and present it |
| S-4 | An app named "Spaarke Workforce SPA" already exists (R4) | Stop and ask |
| S-5 | R6 does not name Dewey Cheatham, or R7 finds no single default team | Stop and ask |
| S-6 | R0 lists a `VITE_*` or `TEAMS_APP_ID` name at repository level | Report it. Removing it is a separate approval |

## 4. Stage A — create the multitenant client (approval 1)

```powershell
# C1: create (public client; no secret, no certificate)
az ad app create --display-name "Spaarke Workforce SPA" --sign-in-audience AzureADMultipleOrgs
#  -> $NEW_APP_ID, $NEW_OBJ
az ad sp create --id $NEW_APP_ID

# C2: SPA redirects, delegated permission, unverified-email removal
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$NEW_OBJ" --headers Content-Type=application/json --body "@new-client.json"
```

`new-client.json`:
```json
{
  "spa": { "redirectUris": [
    "https://green-dune-0c4f1221e.7.azurestaticapps.net",
    "https://green-dune-0c4f1221e.7.azurestaticapps.net/auth-callback.html",
    "brk-multihub://green-dune-0c4f1221e.7.azurestaticapps.net",
    "http://localhost:3000",
    "http://localhost:3000/auth-callback.html"
  ] },
  "requiredResourceAccess": [
    { "resourceAppId": "<BFF_APP_ID>", "resourceAccess": [ { "id": "<UI_SCOPE_ID>", "type": "Scope" } ] }
  ]
}
```
```powershell
# C3: drop emails from unverified domains (Graph v1.0; research item 3)
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$NEW_OBJ/authenticationBehaviors" --headers Content-Type=application/json --body '{"removeUnverifiedEmailClaim":true}'
```
- No Microsoft Graph sign-in permissions (`openid`, `profile`, `email`, `offline_access`) are declared: they are implicit and need no admin consent (research item 7). `user_impersonation` on the BFF is the only declared permission.
- **If Graph refuses `brk-multihub://…` in `spa.redirectUris`** (that route is not documented, research item 5): the PATCH runs again without it, and the owner adds it in the portal (App registrations → Authentication → Single-page application). That is the documented way and the one the Office add-in app used.

- **Read after:** `az ad app show --id $NEW_APP_ID --query "{audience:signInAudience, spa:spa.redirectUris, rra:requiredResourceAccess, secrets:length(passwordCredentials), certs:length(keyCredentials)}"`, plus `GET …/applications/$NEW_OBJ?$select=authenticationBehaviors`. Expect 5 redirects, the 1 delegated scope, 0 secrets, 0 certificates and `removeUnverifiedEmailClaim` true.
- **Rollback:** `az ad app delete --id $NEW_APP_ID`. Nothing depends on the app until stage E. It stays soft-deleted for 30 days and can be restored.

## 5. Stage B — change the dev BFF app `1e40baad` (approval 2; only if stops S-1 to S-3 did not fire)

One PATCH, built from `before-1e40baad.json` so the lists are extended, never replaced:

```powershell
az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$BFF_OBJ" --headers Content-Type=application/json --body "@bff-after.json"
```

**Precondition (added 2026-10-19 after stage R and task 077):** the live dev BFF build contains task 077 (`f68d0f312` and its fix round). Read the live DLL version and run `git merge-base --is-ancestor <077 fix commit> <live commit>` immediately before the PATCH. Without 077, the v2 switch sends every dev surface a 401.

`bff-after.json` sets only:
- `signInAudience`: already `AzureADMultipleOrgs` (R2), so it is left out;
- `api.requestedAccessTokenVersion`: `2` (S-1; ADR-028 A9). This is the only change existing callers can notice: their next token has `aud` = the bare app id, a v2 issuer and `azp` instead of `appid`. Task 077 makes the BFF accept both forms at the same time, so tokens cached before the switch keep working;
- `api.knownClientApplications`: the existing list + `$NEW_APP_ID`;
- `optionalClaims`: the **whole** object from `before-1e40baad.json`, because Graph replaces collections. Its `accessToken` array gets every existing entry plus any of these that are missing: `{"name":"acct","essential":false}`, `{"name":"email","essential":false}`, `{"name":"given_name","essential":false}`, `{"name":"family_name","essential":false}` (research item 2). ID-token and SAML claims are copied unchanged.

Then, as a separate call: `az rest --method PATCH --url "https://graph.microsoft.com/v1.0/applications/$BFF_OBJ/authenticationBehaviors" --headers Content-Type=application/json --body '{"removeUnverifiedEmailClaim":true}'`. This is explicit because the default for a converted single-tenant app is not documented (research item 3).

- **Read after:** the R2 query again. Expect `AzureADMultipleOrgs`, token version 2, `known` containing the new client, the four access-token claims, and `removeUnverifiedEmailClaim` true.
- **Effect on existing callers:** new tokens are v2 (bare-id audience, v2 issuer, `azp`). The BFF accepts both forms (077); the claim reads were checked (§11 S-1). The new access-token claims are additive.
  - Check right after, one fresh token per surface (077 review K1: no test runs Identity Web's real issuer check on a v2 token): Console, a PCF, the Office add-in, the Teams/A8 SPA and, if in use, the Copilot plugin each get 200 on dev. The owner signs in with a fresh session in the browser; the session reads the BFF log for 401s and `sdap.auth.deny.*`.
- **Rollback:** PATCH back `requestedAccessTokenVersion` (null), `knownClientApplications`, `optionalClaims` and `authenticationBehaviors` from `before-1e40baad.json`. One caveat: a multitenant app that another directory has consented to (stage C) must first have that consent removed. Rollback order is therefore C, then B.
  - **Do not roll the dev BFF back to a build without 077 while the app is on v2, or within 90 minutes of reverting it** (cached v2 tokens live up to ~90 minutes). Any other session's dev deploy in that window must also contain 077 (the deploy round's ancestor check).

## 6. Stage C — admin consent in Dewey Cheatham (the owner, as that directory's admin)

- The session gives the owner the consent URL: `https://login.microsoftonline.com/<CUST_TID>/adminconsent?client_id=<NEW_APP_ID>`. This is the v1 admin-consent endpoint, documented without `redirect_uri` and driven by the client's `requiredResourceAccess`. Through `knownClientApplications`, one consent creates the service principals of both the client and the BFF app in Dewey Cheatham (research item 4).
- The account must be able to grant tenant-wide consent for an API permission there (a Global, Privileged Role or Cloud/Application Administrator, as that directory's consent policy allows).
- The owner opens it, signs in as a Dewey Cheatham administrator and accepts.
- **Read after** (owner's sign-in to `bc3aa7f4`, read-only): in the Entra admin center for Dewey Cheatham, Enterprise applications should list "Spaarke Workforce SPA" and the dev BFF app, each with admin-granted delegated permissions. The CLI alternative needs a sign-in to that directory; the owner chooses.
- **Rollback:** in Dewey Cheatham, delete the two enterprise applications (owner, admin center).

## 7. Stage D — `spaarke-bff-dev` settings (approval 3)

```powershell
# Index N = the next free index after the existing WorkforceIdentity__CustomerTenantIds__* (from R5); existing entries untouched.
az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev --settings "WorkforceIdentity__CustomerTenantIds__N=$CUST_TID" "ExternalAccess__CustomerBusinessUnitId=$BU1"
```
- If R5 shows `bc3aa7f4` already listed, only the BU setting is set. (It is: R5.)
- `AzureAd__ValidAudiences__0..2` are read by no code (R5; 077 review). They are left as they are; the owner may ask to delete them in the same call (rollback: set them back from `before-bff-settings.json`).
- **Effect:** App Service restarts the BFF (brief interruption on dev). `CustomerTenantIds` is read per request, but a settings change restarts the app anyway.
- **Before the change:** tell the UAC, ontology and ebilling sessions (they want restart notices).
- **Read after:** the R5 filter again (the old entries are kept and the new ones present), and `/healthz` returns 200.
- **Rollback:** `az webapp config appsettings delete -g rg-spaarke-dev -n spaarke-bff-dev --setting-names WorkforceIdentity__CustomerTenantIds__N ExternalAccess__CustomerBusinessUnitId`, or restore the previous value of each setting from `before-bff-settings.json`.

## 8. Stage E — GitHub `external-spa-dev` (approval 4)

```powershell
gh variable set VITE_WORKFORCE_HOME_MSAL_CLIENT_ID -R spaarke-dev/spaarke --env external-spa-dev --body $NEW_APP_ID
gh variable set VITE_BACKENDS -R spaarke-dev/spaarke --env external-spa-dev --body (Get-Content backends-dev.json -Raw)
```
`backends-dev.json` (one line in practice):
```json
[{"customerKey":"default","displayName":"Spaarke (dev)","bffBaseUrl":"https://spaarke-bff-dev.azurewebsites.net","bffAppId":"<BFF_APP_ID>","ciamBffAppId":"<CIAM_BFF_APP_ID>","homeTenantIds":["<CUST_TID>"]}]
```
- `customerKey` `default` keeps the key the one-entry fallback uses today, so a stored backend choice stays valid.
- `ciamBffAppId` keeps CIAM sign-in on the CIAM API app. Without it the CIAM plane would request `1e40baad` (AADSTS500207).
- `displayName` is shown on the backend chooser; the owner may want a different text.
- **Read after:** R8 again.
- **Rollback:** `gh variable delete VITE_BACKENDS -R spaarke-dev/spaarke --env external-spa-dev` (the fallback returns), and the same for `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`. Neither changes the live SPA until stage F.

## 9. Stage F — SPA deploy to dev (approval 5)

```powershell
gh workflow run deploy-external-spa.yml --ref work/spaarke-SPA-external-access-platform-r3 -f environment=dev
gh run watch <run id> --exit-status
```
- **Ref:** the R3 branch. 058 and 075 are not on master yet (the final R3 PR). The alternative is to wait for that PR and deploy from master; the owner chooses.
- **Impact:**
  - Everyone on the dev SPA gets the 058/075 build on their next page load.
  - CIAM partners and licensed users should see no change in behaviour.
  - Workforce contacts get the A9 company-account path.
- **Verify:** `/auth-callback.html` returns the bridge page. Then the owner's browser checks hand over to task 074: CIAM sign-in still works; a Dewey Cheatham sign-in reaches the profile form; the licensed-user silent link works.
- **Rollback:** redeploy `ebc74b331`, the live dev build today, by tag, the way task 040 did (`git tag external-spa-dev-rollback-ebc74b331 ebc74b331`, push it, run the workflow on that ref). A Static Web App keeps no previous build.

## 10. Rollback order (whole task)

F (SPA) → E (variables) → D (BFF settings) → C (consent) → B (BFF app) → A (delete the client).

The dev BFF build (task 077) is not part of this order. It stays in place: it accepts v1 and v2 tokens, so it is safe either side of stage B. Roll it back only after B is reverted and 90 minutes have passed.

## 11. Record (filled in as each stage runs)

### Stage R — 2026-10-19, owner approval "proceed"; read-only, exactly as written in §2

Output masked; full snapshots are in the session scratchpad (`before-1e40baad-full.json`, `before-bff-settings.json`, `r3-grants.json`).

| # | Result |
|---|---|
| R0 | Repository-level variables: `PROVISIONING_ARTIFACTS_STORAGE_ACCOUNT` and `SIDECAR_ACR_LOGIN_SERVER` only. **No `VITE_*` and no `TEAMS_APP_ID`**, so a missing environment value cannot be silently filled (answers the owner's 076 K3 question; S-6 clear). `spaarke-dev` is a user account, so there are no organisation variables |
| R1 | Tenant `a221a95e…`, `ralph.schroeder@spaarke.com` |
| R2 | `1e40baad`, object `c2aab303…`:<br>- `signInAudience` = **AzureADMultipleOrgs already**<br>- `requestedAccessTokenVersion` = **null, i.e. v1** (S-1 fires)<br>- known clients: [`170c98e1…` SDAP-PCF-CLIENT]<br>- scopes: `access_as_user`, `access_as_external_user`, `SDAP.Access`, `user_impersonation` = `18afc847…`, all enabled<br>- 10 pre-authorized clients<br>- identifier URIs: `api://1e40baad…`, `api://auth-3e04ab58…/1e40baad…`, `api://green-dune…/1e40baad…`<br>- access-token optional claims: `email`, `preferred_username`, `upn`, `acct` (missing: `given_name`, `family_name`)<br>- **1 password credential** (S-2 fires), metadata only: `Dataverse-Checkout-20251218`, valid 2025-12-19 → 2027-12-19<br>- 0 certificates<br>- SPA redirects already include `brk-multihub://green-dune…` |
| R2b | Federated credentials: `mi-bff-api-dev-assertion` (Spaarke-tenant issuer, the MI) and `github-actions-deploy-staging` |
| R2c | `authenticationBehaviors`: not set (default) |
| R3 | SP `d93c832e…`, enabled. Tenant-wide consents to `1e40baad`:<br>- Spaarke Office Add-in [`SDAP.Access user_impersonation`]<br>- **spaarke-bff-api-prod** [`SDAP.Access`]<br>- SPE File Viewer PCF [`SDAP.Access`]<br>- SDAP-External-Workspace [`user_impersonation`]<br>- SDAP-PCF-CLIENT [`user_impersonation`]<br>- spaarke-external-access-SPA [`access_as_external_user SDAP.Access user_impersonation`]<br>Pre-authorized: Spaarke Workspace (A8), SDAP-PCF-CLIENT, Spaarke Office Add-in, Azure CLI, spaarke-external-access-SPA, Microsoft Teams Graph Service, and four app ids with no SP in this tenant (`1fec8e78…`, `5e3ce6c0…`, `29d9ed98…`, `f257a0a9…`) |
| R4 | No app named "Spaarke Workforce SPA" (S-4 clear) |
| R5 | `spaarke-bff-dev`:<br>- `AzureAd__Audience` = `api://1e40baad…`; `AzureAd__ClientId` = `1e40baad…`; `AzureAd__TenantId` = `a221a95e…`; `Ciam__TenantId` = `7052feba…`<br>- `WorkforceIdentity__CustomerTenantIds__0` = `a221a95e…` (NFR-07), `__1` = **`bc3aa7f4…` already listed**<br>- **`ExternalAccess__CustomerBusinessUnitId` absent**<br>- `AzureAd__ValidAudiences__0..2` (`api://1e40baad…`, `1e40baad…`, `api://green-dune…/1e40baad…`) exist, but no BFF code reads them. The default scheme's audiences come from `AzureAd:Audience` plus `AgentToken:CopilotAudience` (`AuthorizationModule.cs`) |
| R6 | `bc3aa7f4…` = **Dewey Cheatham & Howe PC** (`deweycheatham.onmicrosoft.com`) (S-5 clear) |
| R7 | BU1 = "Spaarke Business Unit 1", enabled; exactly one default team (`cf15f587…`) (S-5 clear) |
| R8 | `external-spa-dev`: `VITE_BFF_API_URL`, `VITE_MSAL_AUTHORITY`, `VITE_MSAL_BFF_SCOPE` (`4a4d5126…`), `VITE_MSAL_CLIENT_ID`, `VITE_MSAL_TENANT_ID`, `VITE_TEAMS_MSAL_BFF_SCOPE` (`1e40baad…`), `VITE_TEAMS_MSAL_CLIENT_ID` (`9f1da340…`), `VITE_TEAMS_MSAL_TENANT_ID`. `VITE_BACKENDS` and `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` unset |
| R9 | `/healthz` 200 |

### Stop checks

- **S-1 FIRED.** `1e40baad` issues v1 tokens. A9 cannot work on dev as is: a v1 home token's issuer is `sts.windows.net/{tid}/`, and the `WorkforceHome` scheme requires `login.microsoftonline.com/{tid}/v2.0`.
  - Switching to v2 changes every dev caller's token (§12 research item 1). `aud` becomes the bare GUID, and the default scheme accepts only `api://1e40baad…` plus the Copilot URI (R5), so every dev surface (PCFs, Console, Office add-in, A8 SPA) would likely get 401. This is inferred from the code and must be proved by a test before any switch.
  - Claim-shape reads checked in code (2026-10-19): harmless. `unique_name` is only the last fallback after `upn`/`preferred_username` (`DailyBriefingEndpoints.cs`, `CommunicationService.cs`); the `appid` reads in logs fall back to `azp` or are logging only (`MiddlewarePipelineExtensions.cs`, `AuthorizationModule.cs`, `KeylessProofAuthorizationFilter.cs`, `GraphClientFactory.cs`); `CallerIdentity.cs` also reads `azp`; `WorkforceIdentityOptions.cs` accepts both issuer forms. **The audience is the real blocker.**
  - **The switch therefore needs a BFF change and a deploy first.**
- **S-2 FIRED.** The BFF app has one password credential: `Dataverse-Checkout-20251218`, expiring 2027-12-19. It is already known: `docs/architecture/auth-azure-resources.md` records it as no longer used by the BFF since 2026-08-24 (auth-v4 task 033). It was left valid on the app; removing it is auth-v4/provisioning's item. 059 adds no credential and changes none.
- S-3: not separately fired. The audience is already multitenant, so stage B's audience change is unnecessary. The callers in R3 matter only for S-1.
- S-4, S-5 and S-6: clear.

### What the reads change in the plan (if the owner goes ahead)

- **Stage B:** no audience change. Only `knownClientApplications` += the new client, access-token claims += `given_name`, `family_name`, `removeUnverifiedEmailClaim` = true, and **the v2 switch, after the BFF change**.
- **Stage D:** only `ExternalAccess__CustomerBusinessUnitId` = BU1. Dewey Cheatham is already allowed.
- **Stage A:** unchanged.

### Observation for the owner (not 059 scope)

R3 shows **`spaarke-bff-api-prod` with tenant-wide consent to the DEV BFF app** (`SDAP.Access`). A production identity able to call a dev API may be a leftover. The owner decides whether it is intended.

### Owner decisions 2026-10-19

- "S-1: follow recommended": task 077 first. It was done and deployed to dev as `283466d40` (20:30–20:32Z). This meets stage B's precondition.
- "S-2: yes proceed": the dormant secret is not touched.

### Stage A — 2026-10-19 ~21:00Z, owner approval "proceed 059 A"

- S-4 re-checked just before: 0 apps named "Spaarke Workforce SPA".
- C1: `az ad app create` created **`NEW_APP_ID` = `5859e812-e5e7-452c-be21-1e5aa7b4d478`**, object `e97e41bf…`; SP `afe6ad3c…`.
- C2: the PATCH succeeded. Graph accepted `brk-multihub://` in `spa.redirectUris`, so the portal fallback was not needed.
- C3: `removeUnverifiedEmailClaim` = true.
- **Read after (matches §4):**
  - `AzureADMultipleOrgs`;
  - 5 SPA redirects (dev origin, `/auth-callback.html`, `brk-multihub://green-dune…`, localhost and its callback);
  - 0 web or public-client redirects;
  - `requiredResourceAccess` = `1e40baad` → `18afc847` (`user_impersonation`, Scope) only;
  - 0 secrets, 0 certificates, 0 federated credentials;
  - `authenticationBehaviors.removeUnverifiedEmailClaim` = true.
- Ids are saved in the scratchpad (`stageA-ids.json`). Rollback: `az ad app delete --id 5859e812-e5e7-452c-be21-1e5aa7b4d478`.

### Stage B — 2026-10-19 21:13:14Z, owner approval "059 stage B is approved"

- **Precondition:** the live dev BFF is `00aba6d99` (deployed 21:09–21:11Z, ProductVersion verified). `git merge-base --is-ancestor 77d74f305 00aba6d99` gives exit 0, so the build contains 077 and its fix round.
- **Before-snapshot taken just before the PATCH** (scratchpad `before-stageB.json`, `before-stageB-ab.json`). It matched R2: token version null, known [`170c98e1`], 4 scopes, 10 pre-authorized clients, access-token claims `email`, `preferred_username`, `upn`, `acct`; `authenticationBehaviors` null.
- B1: PATCH `api` + `optionalClaims` (`bff-after.json`, lists extended from the snapshot; `acceptMappedClaims` was null and is left out). Exit 0.
- B2: PATCH `authenticationBehaviors` `{"removeUnverifiedEmailClaim":true}`. Exit 0.
- **Read after (matches §5):**
  - `AzureADMultipleOrgs`;
  - `requestedAccessTokenVersion` = **2**;
  - known clients `5859e812`, `170c98e1`;
  - scopes `access_as_user`, `access_as_external_user`, `SDAP.Access`, `user_impersonation`, all enabled;
  - 10 pre-authorized clients (unchanged);
  - access-token claims `email`, `preferred_username`, `upn`, `acct`, `given_name`, `family_name`;
  - `removeUnverifiedEmailClaim` = true.
- **Session smoke check (Azure CLI, a Spaarke-tenant member; claim shapes only, no values):**

  | Token | Shape | `/api/me` |
  |---|---|---|
  | Cached, requested for `api://1e40baad…` (issued before the switch) | `ver` 1.0, `aud` = `api://…`, `sts.windows.net` issuer, `appidacr`, `email` present | 200 |
  | Fresh, requested for the bare app id (issued after the switch; Entra backdates `iat` by ~5 min) | `ver` **2.0**, `aud` = **bare GUID**, `login.microsoftonline.com/{tid}/v2.0` issuer, `azpacr` (no `appidacr`), `email` **present** | **200** |

  Anonymous `/api/me` 401; `/healthz` 200.
- App Insights, 21:13:14Z → 21:15:44Z (ingestion lags a few minutes): no `sdap.auth.deny.*`. One 401, on `GET /` (no route, anonymous). Re-run after the browser checks below.
- **Ontology session check (reported 2026-10-19, after the switch): PASS on both token versions.**
  - Fresh v2 token (`ver` 2.0, bare `aud`, `azp`, no `appid`): `POST /api/v1/ontology/admin/authoring/validate` returned 200 `valid:true` on the Standard matter.
  - On the Restricted matter it returned 200 `valid:false` with `ontology.admin.scope_matter_secured`.
  - Both results match its v1 baseline on `00aba6d99`.
  - Cached v1 token: also passes.
  - Tip: `az --resource api://…` keeps serving the cached v1 token for up to ~90 min; use `--scope <appId>/.default` for a fresh v2 token.
- **Still open (§5 per-surface check, owner):** fresh-session sign-ins to Console, a PCF, the Office add-in, the Teams/A8 SPA and the Copilot plugin if in use. A guest token's `email` (UAC's question for task 186) is not proved by the member token above.
- Rollback unchanged (§5): PATCH back from `before-stageB.json` and reset `authenticationBehaviors`; C before B once stage C runs; the BFF stays at a build containing 077 for 90 minutes after.

### Stage C — 2026-10-19, owner approval "yes approve DC admin consent"

- URL given to the owner: `https://login.microsoftonline.com/bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b/adminconsent?client_id=5859e812-e5e7-452c-be21-1e5aa7b4d478`.
- Done by the owner (2026-10-19/20). Read-back (owner's screenshots of the Dewey Cheatham admin center):
  - "Spaarke Workforce SPA": one grant, SDAP-BFF-SPE-API `user_impersonation`, delegated, admin consent. **As planned.**
  - **SDAP-BFF-SPE-API (the dev BFF app): NOT as planned.** Its own permissions were consented in Dewey Cheatham too:
    - Dataverse `user_impersonation` (delegated);
    - 25 Microsoft Graph grants, including **application** permissions `User.ReadWrite.All`, `Directory.ReadWrite.All`, `Group.ReadWrite.All`, `Files.ReadWrite.All`, `Mail.Read`, `Mail.Send`, `AppRoleAssignment.ReadWrite.All`, `FileStorageContainer.Selected`, `FileStorageContainerTypeReg.Selected`, `Files.SelectedOperations.Selected`;
    - plus delegated `Directory.ReadWrite.All`, `Files.Read(Write).All`, `FileStorageContainer*`, `GroupMember.ReadWrite.All`, `Mail.Read` and more.
- **Finding C-1 (security, plan defect):** the v1 `/adminconsent` for a client listed in the BFF's `knownClientApplications` is a combined consent. It also consented the BFF app's own `requiredResourceAccess`, application permissions included. §6 said only that both service principals get created; it did not foresee this.
  - **Exposure:** any holder of a credential for `1e40baad` can now get app-only Graph tokens in Dewey Cheatham with tenant-wide write rights. That includes the MI federated credential, the GitHub `deploy-staging` federated credential and the dormant password credential that is valid to 2027-12.
  - **Need:** the BFF does not use them. No BFF code builds a customer-tenant authority (`WithTenantId`/`WithAuthority` match only provisioning code). A9 validates home tokens and writes the stamp's Dataverse app-only in Spaarke's tenant (`a9-bff-design.md`). The BFF's service principal must exist in Dewey Cheatham as a resource; it needs no grants there.
  - **Proposed fix (owner decision):** revoke every grant whose client is SDAP-BFF-SPE-API in Dewey Cheatham, both app-role assignments and its delegated grants. Keep the service principal and keep Spaarke Workforce SPA → `user_impersonation`.
  - **Production/design follow-up:** stamp onboarding must not use a combined consent that carries the BFF's permissions into customer tenants. Candidates, to research before choosing: v2 `adminconsent` with an explicit `scope`, or no `knownClientApplications` plus a separate resource consent. Provisioning is informed after the owner decides.
- **Browser check (owner, 2026-10-20): all surfaces work.** App Insights 21:13:14Z → 03:30Z:
  - 466 × 200; 77 × 401, all on `GET /` (anonymous root probe);
  - 11 × `sdap.auth.deny.workforce_home` (`tid_is_spaarke_tenant`), each within 2 s of a 200/204 on `/api/v1/external/*` (notifications poll, entitlements, grid-config, metadata, fetch).
  - These 11 are new multi-scheme noise: a Spaarke-tenant **v2** token now passes the WorkforceHome scheme's issuer form and is refused at its tenant check, while the default scheme accepts it. Behaviour is correct; the noise is a candidate for the issuer-based scheme selector noted above.
- From here, rolling back stage B first needs this consent removed (§5, §10).

### Stage D — 2026-10-20 13:02:01Z, owner approval "yes approved--proceed"

- Re-read just before:
  - BU1 `cb15f587-baa0-f111-aaac-000d3a99d1d7` = "Spaarke Business Unit 1", enabled, exactly one default team (`cf15f587…`);
  - `ExternalAccess__CustomerBusinessUnitId` absent;
  - `CustomerTenantIds` `__0` `a221a95e…` and `__1` `bc3aa7f4…` (unchanged, so only the BU setting was set);
  - 215 settings saved to scratchpad `before-stageD-settings.json`.
- Restart notices sent to UAC, ontology and ebilling (the sessions had restarted under new names).
- `az webapp config appsettings set … --settings ExternalAccess__CustomerBusinessUnitId=<BU1> -o none`: exit 0. Output suppressed, because the command echoes every setting.
- **Read after:**
  - 216 settings;
  - the new key present (`cb15f587…`);
  - **no existing setting changed or removed** (compared name by name with the snapshot);
  - `/healthz` 200 by 13:02:14Z;
  - `/healthz/dataverse` 200;
  - `/api/me` 200 with a v2 token.
- The dead `AzureAd__ValidAudiences__0..2` were left as they are (the owner did not ask to delete them).
- After-notices sent. Rollback: `az webapp config appsettings delete -g rg-spaarke-dev -n spaarke-bff-dev --setting-names ExternalAccess__CustomerBusinessUnitId`.

### Stage E — 2026-10-20 14:48:39Z, owner approval "for 186 you asked for approval to update portal--yes proceed"

- Trigger: UAC's guest test (186) was blocked, because dev was serving the A8 bundle `ebc74b331`.
- **Read before (R8):** 8 variables. `VITE_BACKENDS` and `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` were absent, as recorded at R8.
- **Set:**
  - `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` = `5859e812-e5e7-452c-be21-1e5aa7b4d478`.
  - `VITE_BACKENDS` = one entry:
    - `customerKey` `default`, `displayName` "Spaarke (dev)";
    - `bffBaseUrl` `https://spaarke-bff-dev.azurewebsites.net`;
    - `bffAppId` `1e40baad…`, `ciamBffAppId` `4a4d5126…`;
    - `homeTenantIds` [`bc3aa7f4…`].
- **Read after:** 10 variables. Both new values equal what was set, and the backends value parses.
- Rollback: `gh variable delete VITE_BACKENDS -R spaarke-dev/spaarke --env external-spa-dev`, then the same for `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`.

### Stage F — 2026-10-20 14:48:58Z, same owner approval

- **Deploy:**
  - Workflow `deploy-external-spa.yml`, run `39442677693`, ref `work/spaarke-SPA-external-access-platform-r3` at **`1908696a8`** (contains `3723eca0b`, 058 and 075).
  - Environment dev. Conclusion: success.
- **Live check** (`green-dune-0c4f1221e.7.azurestaticapps.net`, bundle `/assets/app.js`):
  - it contains the workforce client `5859e812…`, `registration_required`, the `/registration` route and the DC tenant id;
  - `/auth-callback.html` returns 200 with the bridge page.
- Previous live build: `ebc74b331` (A8, deployed 2026-10-18 03:36Z).
- UAC was told the SHA and the bundle result for 186. The owner's browser checks (CIAM, Dewey Cheatham profile form, licensed-user link) hand over to task 074.
- **Rollback:**
  1. `git tag external-spa-dev-rollback-ebc74b331 ebc74b331` and push the tag.
  2. `gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-ebc74b331 -f environment=dev`.
  3. Then roll back stage E if needed. With `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID` set, the old build ignores it.

### SPA redeploy from master — 2026-10-20 17:40:47Z (owner: "yes can deploy SPA")

- **Deploy:** `deploy-external-spa.yml` run `39468172374`, ref `master` at **`28274e04e`** (PR #2141; contains `d4ea1b4a8`, the UAC #2128 guidance mapping). Conclusion: success. Only SPA change since `1908696a8`: `BffAccessState.tsx` and its test.
- **Live check:** `/assets/app.js` contains `workforce_guest_use_home_signin`; `/auth-callback.html` returns 200. UAC was told (precondition for #2138).
- **Rollback:** tag `external-spa-dev-rollback-1908696a8` (pushed); `gh workflow run deploy-external-spa.yml --ref external-spa-dev-rollback-1908696a8 -f environment=dev`.

### Side finding (owner-approved App Insights read, 2026-10-19): `IDX10214` and `IDX40001` on dev are log noise, not failures

- App Insights `spe-insights-dev-67e2xz`, last 6 h.
- `IDX10214` (142): logged by IdentityModel itself with no request attached. **All of them fall within 2 s of an `/api/v1/external/*` request** (mostly the SPA's once-a-minute `GET /notifications` poll, also grid-config and metadata reads). Those requests returned 200/204 (some 499s, where the client closed the request).
- `IDX40001` (167): logged by the default scheme's `OnAuthenticationFailed`. All have `aud` = the CIAM API app `4a4d5126`, a ciamlogin.com issuer, and `appid` `bd57e54e` (the CIAM SPA).
- Cause: the `ExternalCollaboration` policy tries every scheme. A CIAM token fails the default scheme (issuer) and the WorkforceHome scheme (audience) before the Ciam scheme accepts it.
- Behaviour is correct. Reducing the noise would need a scheme selector by issuer: a design change, not done.

## 12. Research used by this plan (researcher, 2026-10-19; V = in current Microsoft docs, I = inferred)

Saved in the researcher's memory as `entra-multitenant-api-a9-dev-setup-2026-10-19.md`.

1. **Token version (V).** The resource app sets it for every caller. v2: `iss` = `login.microsoftonline.com/{tid}/v2.0`, `aud` = the GUID, `azp` replaces `appid`, no `upn`/`unique_name` unless configured. v2 is not required for `AzureADMultipleOrgs`. Microsoft.Identity.Web accepts both issuer forms; a configured `Audience` is used as-is (I), hence S-1.
2. **Access-token optional claims (V).** They come from the resource app. `acct` and `email` must be added; `given_name`/`family_name` are v2 optional claims and also need the `profile` scope, which MSAL sends. `email` is emitted only when the user has a mail attribute, and is removed when its domain is unverified and `removeUnverifiedEmailClaim` is true.
3. **`removeUnverifiedEmailClaim` (V).** Graph v1.0: `PATCH /applications/{objectId}/authenticationBehaviors`. Its default for a converted app is not documented, so it is set explicitly.
4. **Admin consent (V).** `/{tid}/adminconsent?client_id=` (v1, no `redirect_uri`). `knownClientApplications` lets one consent create both service principals; both apps must be in the same tenant. The interactive "on behalf of your organization" checkbox is not offered to every admin role, so the link is used.
5. **`brk-multihub` (V for the portal; not documented for Graph).** Teams NAA needs an SPA-type redirect `brk-multihub://{fqdn}`, origin only; the portal fallback is in stage A.
6. **Audience change (V/I).** The documented refusal is a non-unique App ID URI. `api://{appId}` is unique, so it is expected to pass (I). Same-tenant token `iss`/`aud` are not changed by the audience change itself (I).
7. **OIDC scopes (I).** Not declared; implicit, and not admin-restricted.
