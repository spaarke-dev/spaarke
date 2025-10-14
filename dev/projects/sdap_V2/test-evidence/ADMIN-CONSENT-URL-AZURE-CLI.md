# Admin Consent for Azure CLI to Access BFF API

**Issue**: Azure CLI (04b07795-8ddb-461a-bbee-02f9e1bf7b46) needs admin consent to access BFF API (1e40baad-e065-4aea-a8d4-4b7ab273458c)

**Error**: AADSTS650057 - Resource not listed in Azure CLI's requested permissions

---

## Solution: Grant Admin Consent

**Click this URL to grant consent** (must be Global Admin or Application Admin):

```
https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0/adminconsent?client_id=04b07795-8ddb-461a-bbee-02f9e1bf7b46&scope=api://1e40baad-e065-4aea-a8d4-4b7ab273458c/.default
```

**Steps**:
1. Click the URL above (or copy-paste into browser)
2. Sign in with admin account
3. Review permissions requested
4. Click **Accept**
5. Return here and re-run the upload test

---

## What This Does

**Grants Permission**:
- **FROM**: Azure CLI (Microsoft-owned app)
- **TO**: Your BFF API (SDAP-BFF-SPE-API)
- **SCOPE**: user_impersonation (act on behalf of users)

**Why Needed**:
- Azure CLI is a Microsoft-owned public client
- It needs explicit permission to call custom APIs (like your BFF API)
- This is a one-time setup per tenant

**After Consent**:
- `az account get-access-token --resource "api://1e40baad..."` will work
- PowerShell scripts using Azure CLI will work
- File upload tests will succeed

---

## Alternative: Use Your Own App Registration

If you don't have admin rights, you can create your own app registration for testing:

```powershell
# Create test app
$app = az ad app create --display-name "SDAP Test Client" --query appId -o tsv

# Add redirect URI for device code flow
az ad app update --id $app --public-client-redirect-uris "https://login.microsoftonline.com/common/oauth2/nativeclient"

# Grant permission to BFF API (requires admin consent)
$bffApiId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"
az ad app permission add --id $app --api $bffApiId --api-permissions <scope-id>=Scope

# Get token with your app
az login --allow-no-subscriptions --scope "api://$bffApiId/.default"
```

But this also requires admin consent, so the admin consent URL above is simpler.

---

## Production Note

**In Production**: This won't be an issue because:
- PCF control uses MSAL.js (user delegation, no admin consent needed)
- Users authenticate directly (not via Azure CLI)
- This is a DEV testing limitation only

---

**Created**: 2025-10-14
**Purpose**: Grant Azure CLI permission to access BFF API for Phase 5 testing
