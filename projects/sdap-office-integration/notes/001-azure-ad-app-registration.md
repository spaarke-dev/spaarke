# Task 001: Azure AD App Registration for Office Add-in

> **Status**: Ready for manual execution
> **Requires**: Azure Portal access with Application Administrator or higher

## Overview

This document provides the steps to create the Azure AD app registration for the Spaarke Office Add-in. The add-in uses NAA (Nested App Authentication) as the primary auth method with Dialog API fallback.

## Prerequisites

- Access to Azure Portal (portal.azure.com)
- Application Administrator role in tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- BFF API App ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

## Step-by-Step Instructions

### Step 1: Create New App Registration

1. Go to **Azure Portal** → **Microsoft Entra ID** → **App registrations**
2. Click **+ New registration**
3. Configure:
   - **Name**: `Spaarke Office Add-in`
   - **Supported account types**: `Accounts in this organizational directory only (Single tenant)`
   - **Redirect URI**: Leave blank for now (configured in Step 3)
4. Click **Register**
5. **Record the Application (client) ID** - this is needed for the add-in config

### Step 2: Configure Platform Settings (SPA)

1. Go to **Authentication** → **+ Add a platform**
2. Select **Single-page application**
3. Configure redirect URIs:
   - **Primary (NAA broker)**: `brk-multihub://localhost`
   - **Fallback (Dialog API)**: `https://localhost:3000/taskpane.html` (for dev)
4. Under **Implicit grant and hybrid flows**:
   - Leave both unchecked (NAA uses authorization code flow with PKCE)
5. Click **Configure**

### Step 3: Add Additional Redirect URIs (Production)

After deployment, add production redirect URIs:
1. Go to **Authentication** → **Add URI**
2. Add:
   - `https://{addin-domain}/outlook/taskpane.html`
   - `https://{addin-domain}/word/taskpane.html`
   - `https://{addin-domain}/auth-dialog.html` (for Dialog API fallback)

### Step 4: Configure API Permissions

1. Go to **API permissions** → **+ Add a permission**
2. Add **Microsoft Graph** (Delegated):
   - `User.Read` (for user profile info)
3. Add **Spaarke BFF API** permission:
   - Click **APIs my organization uses**
   - Search for `Spaarke BFF API` (or use API ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`)
   - Select **Delegated permissions**
   - Add `user_impersonation`
4. Click **Grant admin consent for [tenant name]** (if you have admin rights)

### Step 5: Configure Token Settings (Optional but Recommended)

1. Go to **Token configuration** → **+ Add optional claim**
2. Select **ID** token type
3. Add claims:
   - `email`
   - `upn` (User Principal Name)
4. Click **Add**

### Step 6: Verify Configuration

Verify the app registration has:
- [ ] SPA platform configured
- [ ] `brk-multihub://localhost` redirect URI (NAA)
- [ ] Development redirect URIs (localhost:3000)
- [ ] `User.Read` Graph permission
- [ ] `user_impersonation` BFF API permission
- [ ] Admin consent granted (if required)

## Configuration Values for Add-in

After completing the registration, update the add-in configuration with:

```typescript
// src/client/office-addins/outlook/taskpane/index.tsx
// src/client/office-addins/word/taskpane/index.tsx

const CONFIG = {
  clientId: '<NEW_APP_CLIENT_ID>',  // From Step 1
  tenantId: 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  bffApiClientId: '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  bffApiBaseUrl: 'https://spe-api-dev-67e2xz.azurewebsites.net',
};
```

## Environment Variables

For build-time injection, set these environment variables:

```bash
ADDIN_CLIENT_ID=<NEW_APP_CLIENT_ID>
TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2
BFF_API_CLIENT_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c
BFF_API_BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net
```

## Post-Registration: Update BFF API

The BFF API may need to be updated to accept tokens from this new client app:
1. The BFF API's `ValidIssuers` and `ValidAudiences` should already accept tokens from this tenant
2. No code changes should be needed if the API uses standard AAD validation

## Manifest Updates

After registration, update the manifest files with the new client ID:
- `outlook/manifest.json` - Update `resource` in WebApplicationInfo
- `word/manifest.xml` - Update `<WebApplicationInfo>` section

## References

- [NAA Authentication for Office Add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in)
- [Office Add-in SSO](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/sso-in-office-add-ins)
- [auth.md constraints](.claude/constraints/auth.md)
