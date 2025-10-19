# Azure AD App Registration Setup Guide - SPE BFF API

**Sprint 8 - MSAL Integration**
**Created:** 2025-10-06
**Purpose:** Configure Azure AD app registration for SPE BFF API to enable MSAL authentication

---

## Overview

The MSAL integration requires TWO Azure AD app registrations:

1. **Dataverse App** (✅ Already exists): `170c98e1-d486-4355-bcbe-170454e0207c`
   - Purpose: Represents the PCF control running in Dataverse
   - Uses MSAL to acquire tokens on behalf of users

2. **SPE BFF API App** (❌ Needs to be created):
   - Purpose: Represents the backend API that the PCF control calls
   - Exposes API scopes that the Dataverse app requests tokens for

---

## Step 1: Create SPE BFF API App Registration

### 1.1 Navigate to Azure Portal
1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** → **App registrations**
3. Click **+ New registration**

### 1.2 Configure Basic Settings
- **Name:** `SPE BFF API` (or `Spaarke SPE BFF API`)
- **Supported account types:**
  - Select **"Accounts in this organizational directory only (Single tenant)"**
  - Tenant: `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Redirect URI:** Leave blank (not needed for API registration)
- Click **Register**

### 1.3 Note the Application ID
After creation, copy the **Application (client) ID** - you'll need this later.

Example: `12345678-abcd-1234-abcd-123456789abc` (yours will be different)

---

## Step 2: Configure Application ID URI

### 2.1 Set the Application ID URI
1. In the SPE BFF API app registration, go to **Expose an API**
2. Click **+ Add** next to "Application ID URI"
3. **IMPORTANT:** Change the default URI to: `api://spe-bff-api`
   - Default will be: `api://{client-id}`
   - Change to: `api://spe-bff-api`
4. Click **Save**

### 2.2 Why Use Friendly Name?
- `api://spe-bff-api` is easier to read than `api://12345678-abcd-1234-abcd-123456789abc`
- Already configured in code (msalConfig.ts line 213)
- Matches Sprint 8 MSAL implementation

---

## Step 3: Expose API Scope

### 3.1 Add user_impersonation Scope
1. Still in **Expose an API**, click **+ Add a scope**
2. Configure the scope:
   - **Scope name:** `user_impersonation`
   - **Who can consent?:** Admins and users
   - **Admin consent display name:** `Access SPE BFF API`
   - **Admin consent description:** `Allows the application to access SPE BFF API on behalf of the signed-in user`
   - **User consent display name:** `Access SPE BFF API on your behalf`
   - **User consent description:** `Allows the application to access SPE BFF API on your behalf`
   - **State:** Enabled
3. Click **Add scope**

### 3.2 Verify the Scope
You should now see:
- **Scope name:** `api://spe-bff-api/user_impersonation`
- **State:** Enabled

---

## Step 4: Grant Dataverse App Permission to Access BFF API

### 4.1 Configure API Permissions in Dataverse App
1. Go back to **App registrations**
2. Find and open: **Sparke DSM-SPE Dev 2** (Client ID: `170c98e1-d486-4355-bcbe-170454e0207c`)
3. Go to **API permissions**
4. Click **+ Add a permission**
5. Select **My APIs** tab
6. Find and select **SPE BFF API**
7. Select **Delegated permissions**
8. Check **user_impersonation**
9. Click **Add permissions**

### 4.2 Grant Admin Consent
1. Still in **API permissions** for Dataverse app
2. Click **✓ Grant admin consent for [your tenant name]**
3. Confirm by clicking **Yes**
4. Verify status shows **Granted for [tenant]** with green checkmark

---

## Step 5: Verify Configuration

### 5.1 Check Dataverse App Permissions
In **Sparke DSM-SPE Dev 2** → **API permissions**, you should see:

| API / Permission name | Type | Admin consent | Status |
|----------------------|------|---------------|---------|
| Microsoft Graph / User.Read | Delegated | Yes | ✓ Granted |
| **SPE BFF API / user_impersonation** | **Delegated** | **Yes** | **✓ Granted** |

### 5.2 Check SPE BFF API Exposure
In **SPE BFF API** → **Expose an API**, you should see:

- **Application ID URI:** `api://spe-bff-api`
- **Scopes defined by this API:**
  - `api://spe-bff-api/user_impersonation` (Enabled)

---

## Step 6: Update MSAL Configuration (IF NEEDED)

### 6.1 Check Current Configuration
The MSAL config is already set to use the friendly name:

```typescript
// src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts
export const loginRequest = {
  scopes: ["api://spe-bff-api/user_impersonation"],
};
```

### 6.2 Alternative: Use Application ID Instead
If you prefer to use the actual Application ID instead of the friendly name:

```typescript
// Replace "api://spe-bff-api" with actual client ID of SPE BFF API app
scopes: ["api://{spe-bff-api-client-id}/user_impersonation"],
```

**Recommendation:** Keep the friendly name `api://spe-bff-api` for readability.

---

## Step 7: Test MSAL Token Acquisition

### 7.1 Deploy and Test
1. Build and deploy the PCF control (already done - v2.1.3)
2. Hard refresh browser (Ctrl+F5)
3. Click Download button
4. Check browser console for:
   - ✅ `[MsalAuthProvider] MSAL instance initialized ✅`
   - ✅ `[MsalAuthProvider] Token acquired successfully via ssoSilent ✅`
   - ✅ `[SdapApiClientFactory] Access token retrieved successfully`

### 7.2 Expected Log Sequence
```
[Control] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[MsalAuthProvider] Configuration validated ✅
[MsalAuthProvider] PublicClientApplication created ✅
[MsalAuthProvider] MSAL instance initialized ✅
[MsalAuthProvider] No active account found (user not logged in)
[MsalAuthProvider] Initialization complete ✅
[Control] MSAL authentication initialized successfully ✅
[Control] User authenticated: false

[User clicks Download]

[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Attempting ssoSilent token acquisition...
[MsalAuthProvider] Token acquired successfully via ssoSilent ✅
[SdapApiClientFactory] Access token retrieved successfully
```

### 7.3 Previous Error (Should Be Fixed)
**Before:** `AADSTS500011: The resource principal named api://spe-bff-api was not found`

**After:** Token acquisition should succeed

---

## Troubleshooting

### Error: "invalid_resource: AADSTS500011"
- **Cause:** SPE BFF API app registration doesn't exist or Application ID URI not set
- **Fix:** Complete Steps 1-2 above

### Error: "AADSTS65001: The user or administrator has not consented"
- **Cause:** Dataverse app doesn't have permission to access BFF API
- **Fix:** Complete Step 4 (Grant admin consent)

### Error: "AADSTS70011: The provided value for the input parameter 'scope' is not valid"
- **Cause:** Scope name doesn't match (typo or wrong Application ID URI)
- **Fix:** Verify Application ID URI is exactly `api://spe-bff-api` and scope is `user_impersonation`

### Error: "redirect_uri_mismatch"
- **Cause:** Different issue - redirect URI not configured (should already be fixed)
- **Current Redirect URI:** `https://spaarkedev1.crm.dynamics.com`

---

## Summary Checklist

Before testing, verify all items are complete:

- [ ] **Step 1:** SPE BFF API app registration created
- [ ] **Step 2:** Application ID URI set to `api://spe-bff-api`
- [ ] **Step 3:** `user_impersonation` scope exposed and enabled
- [ ] **Step 4:** Dataverse app granted permission to `api://spe-bff-api/user_impersonation`
- [ ] **Step 4.2:** Admin consent granted (green checkmark visible)
- [ ] **Step 5:** Configuration verified in both app registrations
- [ ] **Step 7:** PCF control tested in Dataverse

---

## Next Steps

After completing Azure AD setup:

1. ✅ Hard refresh browser (Ctrl+F5) to load v2.1.3
2. ✅ Test file download operation
3. ✅ Verify token acquisition in console logs
4. ✅ Complete Sprint 8 Phase 4 testing (7 scenarios)
5. ✅ Document deployment in PHASE-4-COMPLETE.md

---

## Reference Information

### Tenant Details
- **Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Dataverse Environment:** SPAARKE DEV 1
- **Dataverse URL:** `https://spaarkedev1.crm.dynamics.com`

### App Registrations
1. **Sparke DSM-SPE Dev 2** (Dataverse/PCF Control)
   - Client ID: `170c98e1-d486-4355-bcbe-170454e0207c`
   - Redirect URI: `https://spaarkedev1.crm.dynamics.com`

2. **SPE BFF API** (Backend API) - TO BE CREATED
   - Client ID: `<will be generated>`
   - Application ID URI: `api://spe-bff-api`
   - Exposed Scope: `user_impersonation`

### Resource Group
- **Name:** SharePointEmbedded
- **Purpose:** Contains SPE infrastructure resources

---

## Additional Notes

### Why Separate App Registrations?
- **Security:** Separates concerns between client (PCF) and API (BFF)
- **Permissions:** Allows fine-grained control over who can access what
- **OAuth Best Practice:** Client apps request tokens for specific APIs

### Why user_impersonation Scope?
- **On-Behalf-Of (OBO) Flow:** BFF API uses user's token to call Graph API
- **User Context:** All operations performed as the signed-in user
- **No Elevated Privileges:** API cannot do more than user is allowed to do

### Application ID URI Naming
- **Friendly Name:** `api://spe-bff-api` (human-readable)
- **GUID Format:** `api://{client-id}` (default)
- **Custom Domain:** `https://api.spaarke.com` (requires domain verification)

Current implementation uses friendly name for clarity.
