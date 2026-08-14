# Runbook — Task 004: Entra NAA app-registration (Outlook/Word add-in)

> 2026-08-06. Verified LIVE via `az` (read-only) against the Spaarke Dev tenant. No secrets recorded.
> Scope: verify/provision + runbook (FR-B0 runtime prerequisite). No code/manifest changes (that's FR-B0 / Phase 4).

## Identities (non-secret)

| Thing | Value |
|---|---|
| Tenant | `a221a95e-6abc-4434-aecc-e48338a1b2f2` (Spaarke Dev) |
| **Add-in app** (`ADDIN_CLIENT_ID`) | `c1258e2d-1688-49d2-ac99-a7485ebd9995` — display name **"Spaarke Office Add-in"**, `signInAudience = AzureADMyOrg` (single-tenant) |
| **BFF API app** (`BFF_API_CLIENT_ID`) | `1e40baad-e065-4aea-a8d4-4b7ab273458c` — **"SDAP-BFF-SPE-API"**, App ID URI `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` |

## Verified state (all ✅ except the one prod item)

1. **Add-in app registration EXISTS** ✅ — `c1258e2d` "Spaarke Office Add-in".
2. **Pre-authorized to the BFF API scope** ✅ — the BFF app's `api.preAuthorizedApplications` includes `c1258e2d` with delegated permissions **`SDAP.Access` + `user_impersonation`**. (Pre-authorization = no per-user consent prompt for these BFF scopes — silent acquisition works.)
3. **Admin consent granted tenant-wide** ✅ — the add-in service principal (objectId `53a09090-3305-4a19-baf8-14bff96c3df9`) has `oauth2PermissionGrants` with `consentType = AllPrincipals` for:
   - BFF API: `SDAP.Access user_impersonation`
   - Microsoft Graph: `email profile User.Read` (OIDC sign-in + basic profile)
   → No pending admin-consent step; users get no consent prompt.
4. **Declared API permissions match** ✅ — add-in `requiredResourceAccess`: BFF (`user_impersonation`,`SDAP.Access`) + Graph (openid/profile/User.Read set).
5. **NAA broker redirect** — `publicClient.redirectUris` contains **`brk-multihub://localhost`** (+ `https://login.microsoftonline.com/common/oauth2/nativeclient`). ✅ for **local dev** (`localhost:3000`).
   `spa.redirectUris` also carry the current prod SWA pages: `https://icy-desert-0bfdbb61e.6.azurestaticapps.net/{outlook/taskpane.html, auth-end.html, auth-dialog.html}` + localhost.

## The ONE remaining item (deploy-time, deferred to task 044)

- **Production NAA broker redirect** — `brk-multihub://localhost` covers local dev only. When the R2 add-in is deployed (task 044, Azure SWA), the add-in app needs a `brk-multihub://<final-prod-add-in-domain>` redirect for NAA to broker tokens on that host. Currently only the `icy-desert-…azurestaticapps.net` SWA is registered (as SPA pages, not as a `brk-multihub` broker entry).
  - **Confirm the exact NAA redirect FORMAT against current Microsoft NAA docs before adding** (the `brk-multihub://` value form has varied across MSAL/NAA versions — domain vs. fixed value). Flagged for a `researcher` pass at 044 time so we don't register a wrong-format URI.
  - This does NOT block dev/local realignment work (040) — only the production deployment (044).

## Bottom line

For **dev/local** add-in work (FR-B0 / task 040): the registration is **ready — nothing to do**. For **production** (task 044): add one `brk-multihub://<prod-domain>` redirect once the deploy host is final (format confirmed first). No secrets involved; no escalation needed (all provisioning + admin consent already in place).

## Commands used (read-only, reproducible)

```
az ad app show --id c1258e2d-1688-49d2-ac99-a7485ebd9995 --query "{spa:spa.redirectUris, public:publicClient.redirectUris, audience:signInAudience}"
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query "{scopes:api.oauth2PermissionScopes[].value, preAuth:api.preAuthorizedApplications[].appId}"
az ad sp show --id c1258e2d-1688-49d2-ac99-a7485ebd9995 --query id
az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/{sp-objectId}/oauth2PermissionGrants"
```
