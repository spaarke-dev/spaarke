# Spaarke Auth — New Environment Setup Guide

> **Version**: 1.0
> **Last Updated**: 2026-05-19
> **Status**: Validated against the dev environment cutover (Phase C deploy, 2026-05-19)
> **Applies To**: Deploying Spaarke Auth v2 to a new Dataverse + Azure tenant
> **Audience**: Customer DevOps / platform operator. **Zero engineering decisions required** — every value below is mechanical.

---

## Overview

This guide is the mechanical operator checklist for standing up Spaarke Auth v2 in a fresh customer tenant. It captures the **client-side Dataverse environment variables**, **App Service configuration**, **Azure AD permissions** for the BFF managed identity, **Dataverse Application User**, and **post-deploy verification smoke tests** required to make the platform live.

It complements (does not replace):
- [`ENVIRONMENT-DEPLOYMENT-GUIDE.md`](ENVIRONMENT-DEPLOYMENT-GUIDE.md) — the broader end-to-end environment build (Azure infra, app regs, SPE, solution import).
- [`SECRET-ROTATION-PROCEDURES.md`](SECRET-ROTATION-PROCEDURES.md) — ongoing secret rotation cadence.

Spaarke is deployed **per-customer-tenant**. Each customer has their own App Service, Key Vault, Dataverse org, and Azure AD app registrations. Tokens and data never cross customer boundaries.

**Estimated duration**: ~45 minutes once the prerequisite resources exist.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Dataverse environment variables (client side — 4 vars)](#2-dataverse-environment-variables-client-side--4-vars)
3. [App Service configuration (server side — 8 settings)](#3-app-service-configuration-server-side--8-settings)
4. [Key Vault secrets](#4-key-vault-secrets)
5. [Azure AD permissions for BFF managed identity (replicate from app reg)](#5-azure-ad-permissions-for-bff-managed-identity-replicate-from-app-reg)
6. [Dataverse Application User for BFF managed identity](#6-dataverse-application-user-for-bff-managed-identity)
7. [Exchange Online — ApplicationAccessPolicy for mailbox access](#7-exchange-online--applicationaccesspolicy-for-mailbox-access)
8. [Deploy + restart](#8-deploy--restart)
9. [Verification smoke tests](#9-verification-smoke-tests)
10. [Customer responsibilities (per per-tenant model)](#10-customer-responsibilities-per-per-tenant-model)
11. [Appendix A — value cheat sheet template](#appendix-a--value-cheat-sheet-template)

---

## 1. Prerequisites

The following must already exist before starting this checklist. See [`ENVIRONMENT-DEPLOYMENT-GUIDE.md`](ENVIRONMENT-DEPLOYMENT-GUIDE.md) sections 2–7 to provision them.

| Resource | Example value pattern | Notes |
|---|---|---|
| Azure subscription with Contributor access | — | For App Service + Key Vault management |
| Azure AD tenant GUID | `{tenant-guid}` | The customer's home tenant |
| BFF Azure AD app registration | App ID `{bff-app-id}` | One client secret in Key Vault (still required for OBO; MI replaces the rest) |
| App Service (BFF API) | `https://spe-api-{env}.azurewebsites.net` | **Must have a system-assigned managed identity enabled** |
| App Service managed identity (MI) | Service principal `{mi-sp-object-id}` | Auto-created when MI is enabled — record its Object ID |
| Key Vault | `{kv-name}.vault.azure.net` | App Service MI must have **Key Vault Secrets User** role at the vault scope |
| Dataverse environment | `https://{dataverse-org}.crm.dynamics.com` | The customer's Dataverse org |
| Dataverse solution import complete | `SpaarkeCore` + `SpaarkeFeatures` | Provides the `sprk_*` environment variable definitions |
| Tooling | Azure CLI ≥ 2.55, PAC CLI ≥ 1.46, PowerShell 7 | For all CLI commands below |

**One-time look-ups** (record these before you start):

```bash
# Tenant GUID
az account show --query tenantId -o tsv

# BFF managed identity service principal object ID
az webapp identity show --name spe-api-{env} --resource-group {rg} --query principalId -o tsv

# Microsoft Graph service principal object ID (constant per tenant — needed for permissions in §5)
az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv
```

---

## 2. Dataverse environment variables (client side — 4 vars)

These 4 variables are read at runtime by every PCF control, Code Page, and Office Add-in to bootstrap MSAL and locate the BFF. They are **defined** by the imported solution; only their **current values** need to be set per environment.

Set via **Power Apps Maker portal**: *Solutions* → *Default Solution* → *Environment Variables* → select each variable → *New environment variable value* for the target env.

Or set via PAC CLI / Web API (script-friendly).

| Variable | Type | Example value | Source |
|---|---|---|---|
| `sprk_BffApiBaseUrl` | URL | `https://spe-api-{env}.azurewebsites.net` | Host of the BFF App Service. **No trailing slash, no `/api` suffix** — `buildBffApiUrl()` adds the path. |
| `sprk_BffApiAppId` | GUID | `{bff-app-id}` | BFF Azure AD app registration → *Overview* → *Application (client) ID* |
| `sprk_MsalClientId` | GUID | `{bff-app-id}` | **Same value as `sprk_BffApiAppId`** in the standard per-tenant deployment (clients use the BFF app reg for MSAL public client). Decoupled because future B2C scenarios may use a separate public client app reg. |
| `sprk_TenantId` | GUID | `{tenant-guid}` | Azure AD tenant GUID. Drives the MSAL authority (`https://login.microsoftonline.com/{sprk_TenantId}/`). |

**Validation**: open any Dataverse model-driven app that hosts a Spaarke PCF. Browser console should log the authority as `https://login.microsoftonline.com/{tenant-guid}/` — **never** `/common` or `/organizations`. If you see the latter, `sprk_TenantId` is unset or has a stale value.

---

## 3. App Service configuration (server side — 8 settings)

Set on the BFF App Service via *Configuration → Application settings* in the portal, or via the CLI block below. **Restart the App Service once** after all changes are applied (single restart cycle keeps downtime to one window).

| App Setting | Required value | Notes |
|---|---|---|
| `AzureAd__TenantId` | `{tenant-guid}` | JWT validation authority |
| `AzureAd__ClientId` | `{bff-app-id}` | BFF app registration |
| `AzureAd__Audience` | `api://{bff-app-id}` | Audience the BFF accepts on incoming JWTs |
| `Graph__ManagedIdentity__Enabled` | `true` | **Set AFTER §5 permissions are granted AND (if Email/Communication modules are enabled) §7 Exchange policies are created.** Switches `GraphClientFactory.CreateAppOnlyClient()` from `ClientSecretCredential` to `DefaultAzureCredential`. Enabling this before §7 → mailbox calls return `403 ErrorAccessDenied`. |
| `Communication__WebhookSigningKey` | Key Vault reference (preferred) or 48-byte base64 secret | HMAC-SHA256 key for `/api/communications/incoming-webhook`. Generate: `openssl rand -base64 48`. **Generate per env; never commit.** |
| `EmailProcessing__WebhookSigningKey` | Key Vault reference (preferred) or 48-byte base64 secret | HMAC-SHA256 key for `/api/v1/emails/webhook-trigger`. Generate: `openssl rand -base64 48`. **Generate per env; never commit.** |
| `AgentToken__CopilotAudience` | `api://{copilot-sso-provider-app-id}/{bff-app-id}` | Token audience for Copilot Studio SSO. The `{copilot-sso-provider-app-id}` comes from the Copilot Studio agent registration. |
| `AzureAd__ClientSecret` | Key Vault reference: `@Microsoft.KeyVault(SecretUri=https://{kv-name}.vault.azure.net/secrets/BFF-API-ClientSecret/)` | Still required for OBO (OAuth spec mandates middle-tier confidential credential). Other server flows (Graph app-only, Dataverse, Cosmos, AI) now use MI and don't read this. |

**Plus the standard template tokens** (substituted by the deployment script from `appsettings.template.json`): `Dataverse__ServiceUrl`, `ConnectionStrings__ServiceBus`, `ConnectionStrings__Redis`, `ApplicationInsights__ConnectionString`, `AzureOpenAI__Endpoint`, and other infrastructure connection settings. See [`appsettings.template.json`](../../src/server/api/Sprk.Bff.Api/appsettings.template.json) — every `#{TOKEN}#` placeholder must be substituted at deploy time.

### CLI block (single restart)

```bash
RG={resource-group}
APP=spe-api-{env}
KV={kv-name}

# Generate webhook keys ONCE per env (record in your secrets manager; do not commit)
COMM_KEY=$(openssl rand -base64 48)
EMAIL_KEY=$(openssl rand -base64 48)

# Recommended: store keys in Key Vault first, then reference them
az keyvault secret set --vault-name $KV --name communication-webhook-signing-key --value "$COMM_KEY"
az keyvault secret set --vault-name $KV --name Email-WebhookSigningKey --value "$EMAIL_KEY"

# Set all app settings in one call (single restart)
az webapp config appsettings set --resource-group $RG --name $APP --settings \
  "AzureAd__TenantId={tenant-guid}" \
  "AzureAd__ClientId={bff-app-id}" \
  "AzureAd__Audience=api://{bff-app-id}" \
  "Graph__ManagedIdentity__Enabled=true" \
  "Communication__WebhookSigningKey=@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/communication-webhook-signing-key/)" \
  "EmailProcessing__WebhookSigningKey=@Microsoft.KeyVault(SecretUri=https://$KV.vault.azure.net/secrets/Email-WebhookSigningKey/)" \
  "AgentToken__CopilotAudience=api://{copilot-sso-provider-app-id}/{bff-app-id}"
```

> **Why Key Vault references are preferred**: plain-text secrets in App Service config are visible to anyone with `az webapp config appsettings list` access. Key Vault references resolve at runtime, gated by the MI's RBAC role, and centralize rotation.

---

## 4. Key Vault secrets

The BFF reads the following secrets from Key Vault at startup or on first use. Populate them before the App Service restart.

| Secret name | Purpose | Rotation |
|---|---|---|
| `BFF-API-ClientSecret` | BFF app registration client secret. Still required for OBO. | Per [`SECRET-ROTATION-PROCEDURES.md`](SECRET-ROTATION-PROCEDURES.md) |
| `Dataverse-ServiceUrl` | Dataverse env URL (`https://{org}.crm.dynamics.com`). | Static per env (immutable) |
| `ServiceBus-ConnectionString` | Service Bus namespace connection string. | Rotated with namespace key rotation |
| `Redis-ConnectionString` | Azure Cache for Redis primary access key. | Rotated with Redis key rotation |
| `communication-webhook-signing-key` | HMAC key for Microsoft Graph subscription webhooks (Communication module). | Every 90 days or on incident |
| `Email-WebhookSigningKey` | HMAC key for Dataverse Service Endpoint webhooks (Email module). | Every 90 days or on incident |
| `AppInsights-ConnectionString` | Application Insights ingestion. | Static per env |
| `ai-openai-endpoint`, `ai-openai-key`, `ai-docintel-endpoint`, `ai-docintel-key`, `ai-search-endpoint`, `ai-search-key` | Azure AI dependencies. | Static per env unless AI resources are rotated |

**All values are generated per environment. Never commit. Use Key Vault as the single source of truth.**

> **Note on `Dataverse-ClientSecret` and `AzureAd__ClientSecret`**: as of Phase C, server-side Dataverse access uses the App Service MI via `DefaultAzureCredential` (see §6). The `BFF-API-ClientSecret` is still read by OBO and a handful of other code paths. `Dataverse-ClientSecret` can be removed from Key Vault after the deploy is verified.

---

## 5. Azure AD permissions for BFF managed identity (replicate from app reg)

Once `Graph__ManagedIdentity__Enabled=true`, the BFF's outbound Graph calls authenticate as the **App Service managed identity**, not the BFF app registration. The MI service principal therefore needs the same Microsoft Graph **application** permissions the BFF app registration currently holds.

Managed identities don't appear in the *App registrations* blade — you cannot grant their permissions through the portal UI in the same way. Use the Microsoft Graph API directly (Azure CLI script below).

### Step 5a — Enumerate the BFF app registration's current Graph application permissions

```bash
# List the BFF app reg's app role assignments on Microsoft Graph
BFF_SP=$(az ad sp show --id {bff-app-id} --query id -o tsv)
GRAPH_SP=$(az ad sp show --id 00000003-0000-0000-c000-000000000000 --query id -o tsv)

az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$BFF_SP/appRoleAssignments" \
  --query "value[?resourceId=='$GRAPH_SP'].{appRoleId:appRoleId,resourceDisplayName:resourceDisplayName}" \
  -o table
```

Record the list of `appRoleId` GUIDs. These are the IDs you'll grant to the MI.

### Step 5b — Map the `appRoleId` GUIDs back to permission names (for the runbook record)

```bash
# Get the full list of Graph app roles with their human-readable names
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$GRAPH_SP" \
  --query "appRoles[?id=='{appRoleId}'].{value:value,displayName:displayName}" \
  -o table
```

For a baseline Spaarke deployment, the BFF app reg typically holds Graph application permissions including:

- `FileStorageContainer.Selected` — SharePoint Embedded container access (core)
- `Files.Read.All` / `Files.ReadWrite.All` — Drive/file operations
- `Sites.Read.All` / `Sites.ReadWrite.All` (if SharePoint sites are used)
- `User.Read.All` — user lookup for sharing and audit
- `Group.Read.All` — group membership for access checks
- `Mail.Read` / `Mail.ReadWrite` (if Email module ingests Outlook mail) — **also requires §7 Exchange ApplicationAccessPolicy**
- `Mail.Send` (if BFF sends mail app-only) — **also requires §7 Exchange ApplicationAccessPolicy**
- `MailboxSettings.Read` (if Email module reads mailbox settings) — **also requires §7 Exchange ApplicationAccessPolicy**

**The dev cutover on 2026-05-19 granted 11 Graph + SharePoint app role assignments to the MI.** The exact list is environment-dependent (some permissions are only needed if the corresponding module is enabled), so the canonical source is always **what the BFF app registration currently has** — Step 5a above.

### Step 5c — Grant each permission to the MI

For **each** `appRoleId` from Step 5a, run:

```bash
MI_SP={mi-sp-object-id}            # from §1 prerequisites
GRAPH_SP={graph-sp-object-id}      # from §1 prerequisites
APP_ROLE_ID={appRoleId-from-step-5a}

az rest --method POST \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$MI_SP/appRoleAssignments" \
  --headers Content-Type=application/json \
  --body "{
    \"principalId\": \"$MI_SP\",
    \"resourceId\": \"$GRAPH_SP\",
    \"appRoleId\": \"$APP_ROLE_ID\"
  }"
```

A loop wrapping this call against the Step 5a output is the simplest scripted approach. Replicate identically for any SharePoint Online service principal app roles the BFF holds (resourceId differs).

### Step 5d — Verify

```bash
az rest --method GET \
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$MI_SP/appRoleAssignments" \
  --query "value[].{resourceDisplayName:resourceDisplayName,appRoleId:appRoleId}" \
  -o table
```

Output should match Step 5a one-for-one. If counts differ, the MI will fail at Graph with `403 Insufficient Privileges` on first call.

---

## 6. Dataverse Application User for BFF managed identity

The MI's service principal must be registered as a **Dataverse Application User** with the appropriate security role so the BFF (via `DefaultAzureCredential`) can call Dataverse Web API as itself.

1. Open the Dataverse environment in the **Power Platform admin center**: *Environments* → *{your env}* → *Settings* → *Users + permissions* → *Application users*.
2. Click *New app user*.
3. *Add an app* → search by the **MI's app ID** (not its object ID). To find the app ID:
   ```bash
   az ad sp show --id {mi-sp-object-id} --query appId -o tsv
   ```
4. Select the appropriate **business unit**.
5. Assign **security role(s)**. The dev cutover used **System Administrator**; production should use the **least-privilege role** that grants:
   - Read/Write/Append/Append To on entities the BFF mutates (`sprk_document`, `sprk_matter`, `sprk_chatsession`, etc.)
   - Read on lookup targets (`systemuser`, `team`, `account`)
   - The custom security role created by the SpaarkeCore solution (if present) is the recommended starting point.
6. *Create*.

**If the BFF needs to call multiple Dataverse environments** (e.g., a `spaarke-demo` sister env), repeat this step in each environment. Skipping environments → BFF calls return `401 Unauthorized` against the unregistered env.

---

## 7. Exchange Online — ApplicationAccessPolicy for mailbox access

If the Communication or Email modules read/send mail as the application (Graph `Mail.Read`, `Mail.ReadWrite`, `Mail.Send`, `MailboxSettings.Read`), Exchange Online **enforces a second authorization layer** on top of Graph application permissions: the **`ApplicationAccessPolicy`** (also called *RBAC for Applications in Exchange Online*).

Without an applicable `ApplicationAccessPolicy`, Graph mailbox calls from an app-only principal return:

```
StatusCode: 403  Forbidden
ErrorCode:  ErrorAccessDenied
Message:    Access to OData is disabled.
            -- OR --
            Access is denied. Check credentials and try again.
```

This is what triggers the recurring `InboundPollingBackupService` 403s when `Graph__ManagedIdentity__Enabled=true` is set without this section completed.

### Why this layer exists (and why §5 isn't sufficient on its own)

Graph application permissions (`Mail.Read`, etc.) grant the *capability* to call mailbox endpoints, but Exchange enforces a separate, mailbox-scoped policy that controls *which mailboxes* the app may touch. The default is **no mailbox access** until a policy is created. Microsoft's per-tenant default is intentional — without it, a single Graph permission would grant tenant-wide mailbox read.

Two consequences:

1. **Both the BFF app registration AND the App Service managed identity need their own policy.** They are separate service principals to Exchange. Granting a policy to one does not grant it to the other.
2. **Only app-only (application-permission) flows are subject to this policy.** OBO / delegated flows go through the *user's* mailbox permissions and are exempt — no policy entry needed for users.

### Scope: which mailboxes need to be in the policy

Put a mailbox in the policy scope if **any** of the following is true:

- It is configured as a **Communication Account** in Dataverse (table `sprk_communicationaccount`) — these are the mailboxes `InboundPollingBackupService` polls.
- The BFF sends mail **as** that mailbox via app-only Graph (`Mail.Send` with the mailbox as the from address).
- The BFF reads mailbox settings as that mailbox via app-only Graph.

Do **not** add user mailboxes accessed only through OBO (e.g., when a user opens their own inbox in the UI — that runs through delegated permissions, not app-only).

The recommended pattern: maintain a **mail-enabled security group** (suggested name: `Spaarke Email Access`, email `spaarke-central-email@spaarke.com`) containing the in-scope mailboxes, and scope the policy to that group. Adding a mailbox to the group automatically extends access; removing it revokes — no Exchange admin re-touch required.

### Step 7a — Connect to Exchange Online

```powershell
# One-time install
Install-Module ExchangeOnlineManagement -Scope CurrentUser

# Connect as Exchange admin (device code flow recommended for MFA accounts)
Connect-ExchangeOnline -UserPrincipalName admin@{customer-tenant} -ShowProgress $true
```

### Step 7b — Create the scope group (if it doesn't exist)

```powershell
# Create a mail-enabled security group to hold in-scope mailboxes
New-DistributionGroup `
  -Name "Spaarke Email Access" `
  -PrimarySmtpAddress "spaarke-central-email@{customer-tenant}" `
  -Type Security

# Add each mailbox the BFF will access app-only
Add-DistributionGroupMember -Identity "spaarke-central-email@{customer-tenant}" `
  -Member "testuser1@{customer-tenant}"
Add-DistributionGroupMember -Identity "spaarke-central-email@{customer-tenant}" `
  -Member "mailbox-central@{customer-tenant}"
# ...repeat per Communication Account / send-as mailbox
```

### Step 7c — Create one policy per app-only principal

Two `ApplicationAccessPolicy` objects are required: one for the **BFF app registration** and one for the **App Service managed identity**.

```powershell
# Get the AppIds (NOT object IDs — Exchange uses AppId / ClientId)
$BFF_APP_ID   = "{bff-app-id}"           # from app registration Overview blade
$MI_APP_ID    = "{mi-sp-app-id}"         # az ad sp show --id {mi-sp-object-id} --query appId -o tsv

# Policy 1 — BFF app registration
New-ApplicationAccessPolicy `
  -AppId $BFF_APP_ID `
  -PolicyScopeGroupId "spaarke-central-email@{customer-tenant}" `
  -AccessRight RestrictAccess `
  -Description "Spaarke BFF app reg — restrict app-only mailbox access to Spaarke Email Access group"

# Policy 2 — App Service managed identity
New-ApplicationAccessPolicy `
  -AppId $MI_APP_ID `
  -PolicyScopeGroupId "spaarke-central-email@{customer-tenant}" `
  -AccessRight RestrictAccess `
  -Description "Spaarke BFF MI — restrict app-only mailbox access to Spaarke Email Access group"
```

`AccessRight RestrictAccess` is the secure default: deny everything except the scope group. The opposite (`DenyAccess` with a deny list) is brittle and not recommended.

### Step 7d — Verify with `Test-ApplicationAccessPolicy`

The authoritative live-check (not log-tailing) of whether a principal can access a given mailbox:

```powershell
Test-ApplicationAccessPolicy `
  -Identity "testuser1@{customer-tenant}" `
  -AppId $MI_APP_ID
# Expect: AccessCheckResult = Granted

Test-ApplicationAccessPolicy `
  -Identity "testuser1@{customer-tenant}" `
  -AppId $BFF_APP_ID
# Expect: AccessCheckResult = Granted

# Negative control — a mailbox NOT in the group should return Denied
Test-ApplicationAccessPolicy `
  -Identity "ralph.schroeder@{customer-tenant}" `
  -AppId $MI_APP_ID
# Expect: AccessCheckResult = Denied   (until/unless added to the group)
```

`Granted` means the policy is active for that principal/mailbox pair. `Test-ApplicationAccessPolicy` reflects the current effective policy immediately — it does not wait for propagation.

### Step 7e — Wait for propagation before retesting Graph

Microsoft documents up to **30 minutes** of propagation delay between policy creation and Graph mailbox calls succeeding. `Test-ApplicationAccessPolicy` returns `Granted` instantly, but live Graph requests may continue to return `403` for several minutes. Re-tail the App Service logs ~20–30 minutes after policy creation:

```bash
az webapp log tail --name spe-api-{env} --resource-group {rg}
# Look for: InboundPollingBackupService should stop logging 403 ErrorAccessDenied
```

### Step 7f — Adding mailboxes later (operator runbook)

When a new Communication Account is provisioned in Dataverse:

```powershell
Connect-ExchangeOnline -UserPrincipalName admin@{customer-tenant}
Add-DistributionGroupMember -Identity "spaarke-central-email@{customer-tenant}" `
  -Member "new-mailbox@{customer-tenant}"

# Optional verify
Test-ApplicationAccessPolicy `
  -Identity "new-mailbox@{customer-tenant}" `
  -AppId $MI_APP_ID
```

No App Service restart required.

### Common failure modes

| Symptom | Cause | Fix |
|---|---|---|
| `403 Access to OData is disabled` on every Graph mailbox call | No `ApplicationAccessPolicy` exists for this principal | Run Step 7c for the failing principal (AppId from log entry's `appid` claim) |
| BFF app reg works, MI fails (or vice versa) | Only one of the two policies was created | Both are required — run Step 7c for the missing one |
| Newly added mailbox returns 403 for ~10 minutes | Propagation delay | Wait up to 30 min; `Test-ApplicationAccessPolicy` returning `Granted` confirms the policy is active |
| `Test-ApplicationAccessPolicy` returns `Denied` for a mailbox you expected to work | Mailbox not in the scope group | `Add-DistributionGroupMember` |
| Mailbox in group, policy says `Granted`, Graph still 403s | Permissions in §5 (`Mail.Read` etc.) missing or unconsented | Re-verify §5d output — Graph permissions are necessary AND sufficient *together with* this policy |

---

## 8. Deploy + restart

Deploy the BFF code (per [`bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) skill or `scripts/Deploy-BffApi.ps1`). The deploy script does:

1. Build + publish the API
2. Substitute `appsettings.template.json` placeholders with environment-specific values
3. Zip-deploy to App Service (with hash-verify retry on Windows file-lock failure mode)
4. Trigger an App Service restart

If you set app settings in §3 **after** the deploy, manually restart:

```bash
az webapp restart --name $APP --resource-group $RG
```

DefaultAzureCredential caches the resolved credential in the singleton — the new MI mode applies on the next process startup.

---

## 9. Verification smoke tests

Run all five after every fresh-env deploy. All must pass before declaring the environment live.

### 9a. Liveness — `/healthz` returns 200

```bash
curl -i https://spe-api-{env}.azurewebsites.net/healthz
# Expect: HTTP/1.1 200 OK
```

### 9b. JWT validation + OBO — authenticated endpoint round-trip

Acquire a user JWT (via MSAL or `az account get-access-token` for a test user) and hit an OBO-backed endpoint:

```bash
USER_TOKEN={user-jwt}
curl -i -H "Authorization: Bearer $USER_TOKEN" \
  "https://spe-api-{env}.azurewebsites.net/api/ai/chat/context-mappings/standalone?entityType=sprk_matter&entityId={any-matter-guid}"
# Expect: HTTP/1.1 200 OK + JSON body
```

A 200 confirms: (a) JWT validation works (`AzureAd__*` settings correct), (b) OBO token exchange to Graph works (`BFF-API-ClientSecret` valid).

### 9c. MI → Dataverse — managed identity path exercised

```bash
curl -i "https://spe-api-{env}.azurewebsites.net/healthz/dataverse/doc/00000000-0000-0000-0000-000000000001"
# Expect: HTTP/1.1 200 OK with body containing "Does Not Exist" or "not found"
```

A 200 with a "not found" body confirms the MI authenticated against Dataverse and the entity lookup ran. A `401` or `403` here means §6 (Dataverse App User) is incomplete or the MI lacks the security role.

### 9d. Exchange Online — MI mailbox access (if Email/Communication modules enabled)

After the §7 policies have propagated (~20–30 min after creation), verify the BFF MI no longer logs `403 ErrorAccessDenied` on mailbox calls:

```bash
az webapp log tail --name spe-api-{env} --resource-group {rg} | grep -E "InboundPollingBackupService|ErrorAccessDenied|403"
# PASS: poll cycles complete without 403; you may still see other expected log noise
# FAIL: recurring "Access to OData is disabled" or "ErrorAccessDenied" → re-check §7c (policy creation) and §7d (Test-ApplicationAccessPolicy)
```

For an authoritative point-in-time check (no propagation wait):

```powershell
Test-ApplicationAccessPolicy -Identity "{any-scoped-mailbox}@{customer-tenant}" -AppId {mi-sp-app-id}
# Expect: AccessCheckResult = Granted
```

### 9e. Browser MSAL regression (client-side)

After the BFF is live, open the customer's Dataverse model-driven app in a fresh browser session and:

```javascript
// In browser DevTools console
localStorage.clear(); sessionStorage.clear();
document.cookie.split(';').forEach(c => {
  document.cookie = c.split('=')[0].trim() + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
});
// CLOSE BROWSER. Reopen. Navigate to a page hosting any Spaarke PCF.
// PASS: no popup; console shows  authority: https://login.microsoftonline.com/{tenant-guid}/
// FAIL: popup OR  /organizations  or  /common  in the authority
```

A failure here almost always means `sprk_TenantId` is unset or has the wrong value (§2).

---

## 10. Customer responsibilities (per per-tenant model)

Spaarke's per-tenant deployment model puts certain controls firmly in the customer's hands. Spaarke does **not** manage these — the customer's tenant admin / SecOps team does, using their existing Microsoft 365 / Azure AD governance.

| Concern | Owner | Recommendation |
|---|---|---|
| **Conditional Access policies** | Customer | Apply CA policies to the BFF app registration and any user-facing apps per the customer's security baseline. Spaarke is CAE-aware (Phase D) and will honor revocation events. |
| **Multi-factor authentication (MFA)** | Customer | Enforce via CA. Spaarke does not impose its own MFA layer. |
| **Secret rotation cadence** | Customer | `BFF-API-ClientSecret`: 90 days or per customer policy. Webhook signing keys: 90 days or on incident. See [`SECRET-ROTATION-PROCEDURES.md`](SECRET-ROTATION-PROCEDURES.md). |
| **Identity governance / lifecycle** | Customer | User provisioning, access reviews, and offboarding flow through the customer's existing IGA. |
| **Audit log retention + SIEM integration** | Customer | Spaarke emits structured audit logs (Phase C task 048). Pipe to the customer's Sentinel / Monitor workspace via App Service *Diagnostic Settings* — no code change per customer. |
| **Network egress / private endpoints** | Customer | If the customer requires private network paths to Dataverse, Graph, or Key Vault, configure via VNet integration / Private Endpoints on the App Service. |
| **Backup + DR for Dataverse data** | Customer | Dataverse-native backup is the source of record. Spaarke stores no customer data outside Dataverse + SPE. |

Spaarke owns: the deployment artifacts, the hardened code, the audit emission contract, the claims handling, the mechanical environment tokenization in this guide.

---

## Appendix A — value cheat sheet template

Copy this block into your environment's runbook and fill in the `{…}` placeholders. Treat the completed sheet as a **secret** — store in your secrets manager, never in source control.

```
ENVIRONMENT NAME: ____________________
DEPLOY DATE:      ____________________

# Identifiers
Tenant GUID:              {tenant-guid}
BFF app ID:               {bff-app-id}
Copilot SSO provider app: {copilot-sso-provider-app-id}
Dataverse org URL:        https://{dataverse-org}.crm.dynamics.com
BFF App Service URL:      https://spe-api-{env}.azurewebsites.net
Key Vault name:           {kv-name}
MI service principal ID:  {mi-sp-object-id}
MI app ID:                {mi-sp-app-id}

# Dataverse env vars (§2)
sprk_BffApiBaseUrl  = https://spe-api-{env}.azurewebsites.net
sprk_BffApiAppId    = {bff-app-id}
sprk_MsalClientId   = {bff-app-id}
sprk_TenantId       = {tenant-guid}

# App Service settings (§3)
AzureAd__TenantId                    = {tenant-guid}
AzureAd__ClientId                    = {bff-app-id}
AzureAd__Audience                    = api://{bff-app-id}
Graph__ManagedIdentity__Enabled      = true
Communication__WebhookSigningKey     = @Microsoft.KeyVault(...)   # generated per env
EmailProcessing__WebhookSigningKey   = @Microsoft.KeyVault(...)   # generated per env
AgentToken__CopilotAudience          = api://{copilot-sso-provider-app-id}/{bff-app-id}
AzureAd__ClientSecret                = @Microsoft.KeyVault(SecretUri=.../BFF-API-ClientSecret/)

# Graph permissions granted to MI (§5) — record the count + the list
Permissions granted (count): __________
List:
  - ____________________________________
  - ____________________________________
  - ____________________________________
  - ____________________________________

# Exchange ApplicationAccessPolicy (§7) — only if Email/Communication enabled
Scope group:                  spaarke-central-email@{customer-tenant}
Group members (in-scope mailboxes):
  - ____________________________________
  - ____________________________________
Policy 1 (BFF app reg) AppId: {bff-app-id}            → [ ] Granted (Test-ApplicationAccessPolicy)
Policy 2 (MI)          AppId: {mi-sp-app-id}          → [ ] Granted (Test-ApplicationAccessPolicy)

# Smoke tests (§9)
9a /healthz                                 → [ ] PASS  [ ] FAIL
9b OBO endpoint (200 + JSON)                → [ ] PASS  [ ] FAIL
9c /healthz/dataverse/doc/{guid} (MI path)  → [ ] PASS  [ ] FAIL
9d EXO mailbox access (no 403 in logs)      → [ ] PASS  [ ] FAIL  [ ] N/A (Email module off)
9e Browser MSAL regression (no popup)       → [ ] PASS  [ ] FAIL
```

---

*This guide reflects Spaarke Auth v2 + Phase C hardening. For the architectural rationale behind these mechanics, see [`ADR-028: Spaarke Auth Architecture`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md).*
