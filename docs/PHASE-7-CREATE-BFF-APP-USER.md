# Phase 7: Create BFF API Application User in Dataverse

**Date:** October 20, 2025
**Status:** ACTION REQUIRED
**App Registration ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c` (spe-bff-api)

---

## Problem

NavMap API endpoint returns **AADSTS500011** error:

```
The resource principal named https://spaarkedev1.api.crm.dynamics.com/... was not found in the tenant
```

**Root Cause:** The BFF API App Registration (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) is not registered as an Application User in Dataverse.

---

## Current Status

### ✅ Completed
1. Changed authentication from ManagedIdentityCredential to ClientSecretCredential
2. Added Dynamics CRM API permission to BFF API App Registration
3. Granted admin consent for the permission

### ❌ Blocked
- BFF API App Registration not registered as Application User in Dataverse
- Previous Application User used Managed Identity ID (`6bbcfa82-14a0-40b5-8695-a271f4bac521`)
- **Need to create NEW Application User with BFF API ID (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)**

---

## Solution: Create Application User in Dataverse

### Method 1: PowerShell (Recommended)

```powershell
# Connect to Dataverse environment
Install-Module Microsoft.PowerApps.Administration.PowerShell -Force -Scope CurrentUser
Add-PowerAppsAccount

# Variables
$environmentId = "DATAVERSE_ENVIRONMENT_ID"  # Get from Power Platform Admin Center
$appId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"  # BFF API App Registration
$businessUnitId = "BUSINESS_UNIT_ID"  # Get from Dataverse Settings > Business Units

# Create Application User
New-PowerAppManagementApp `
    -EnvironmentName $environmentId `
    -ApplicationId $appId `
    -DisplayName "SPE BFF API Service" `
    -Description "Application User for SPE BFF API server-to-server authentication"

# Grant Security Role (System Administrator)
# Note: You'll need to assign the security role via Power Platform Admin Center or Dataverse UI
```

### Method 2: Power Platform Admin Center (GUI - EASIEST)

1. Go to [Power Platform Admin Center](https://admin.powerplatform.microsoft.com/)
2. Select **Environments** → Find `SPAARKE DEV 1` (or your Dataverse environment)
3. Click **Settings** (top menu)
4. Expand **Users + permissions** → Select **Application users**
5. Click **+ New app user**
6. Click **+ Add an app**
   - Search for App ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - Or search by name: `spe-bff-api`
   - Select the app
   - Click **Add**
7. Select **Business unit:** (typically `Organization` or default business unit)
8. Click **Create**
9. **IMPORTANT:** After creation, edit the Application User:
   - Click the pencil icon or **Edit security roles**
   - Assign **System Administrator** role (or custom role with required permissions)
   - Click **Save**

### Method 3: Dataverse API (via pac CLI)

```bash
# Install Power Platform CLI
# Download from: https://aka.ms/PowerAppsCLI

# Authenticate to Dataverse
pac auth create --environment https://spaarkedev1.crm.dynamics.com

# Create Application User
pac admin create-service-principal \
    --name "SPE BFF API Service" \
    --application-id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
    --environment https://spaarkedev1.crm.dynamics.com

# Assign Security Role (must be done via UI or PowerShell)
```

---

## Required Information

### BFF API App Registration Details

```
App ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
Display Name: spe-bff-api (or similar)
Tenant ID: a221a95e-6abc-4434-aecc-e48338a1b2f2
```

### Dataverse Environment Details

```
Environment Name: SPAARKE DEV 1
Environment URL: https://spaarkedev1.crm.dynamics.com
Environment ID: [Find in Power Platform Admin Center → Environments → Environment details]
```

### Security Role Assignment

The Application User needs **System Administrator** role (or custom role with these permissions):
- Read EntityDefinitions (metadata)
- Read Relationships (metadata)
- Access to entities: sprk_document, sprk_matter, etc.

---

## Verification Steps

### Step 1: Verify Application User Created

After creating the Application User, verify it exists:

**Via Power Platform Admin Center:**
1. Go to Power Platform Admin Center
2. Environments → SPAARKE DEV 1 → Settings
3. Users + permissions → Application users
4. Look for Application User with App ID `1e40baad-e065-4aea-a8d4-4b7ab273458c`
5. Verify **Status: Enabled**
6. Verify **Security Role: System Administrator** assigned

**Via pac CLI:**
```bash
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
```

Look for entry with `ApplicationId: 1e40baad-e065-4aea-a8d4-4b7ab273458c`

### Step 2: Test NavMap API Endpoint

After creating Application User, restart the BFF API and test:

```bash
# Restart Web App
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz

# Wait 30 seconds for restart
sleep 30

# Test NavMap API
curl -i "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document/lookup"
```

**Expected Result:** `200 OK` with JSON response

```json
{
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document",
  "logicalName": "sprk_matter",
  "schemaName": "sprk_Matter",
  "navigationPropertyName": "sprk_Matter",
  "targetEntity": "sprk_matter",
  "source": "dataverse"
}
```

---

## Why This is Required

### How Dataverse Authentication Works

1. **Azure AD Level:**
   - BFF API has Dynamics CRM API permission ✅
   - Admin consent granted ✅
   - Azure AD issues token for `https://spaarkedev1.crm.dynamics.com/.default` scope

2. **Dataverse Level:**
   - Dataverse receives token with App ID `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - Looks up Application User with that App ID
   - **If not found:** Returns AADSTS500011 error ❌
   - **If found:** Checks security roles and grants access ✅

### Current State

```
Azure AD:
  BFF API (1e40baad-e065-4aea-a8d4-4b7ab273458c)
  ├─ API Permission: Dynamics CRM (user_impersonation) ✅
  └─ Admin Consent: Granted ✅

Dataverse:
  Application Users:
  ├─ Managed Identity (6bbcfa82-14a0-40b5-8695-a271f4bac521) ← OLD (not used)
  └─ BFF API (1e40baad-e065-4aea-a8d4-4b7ab273458c) ← MISSING ❌
```

### After Creating Application User

```
Azure AD:
  BFF API (1e40baad-e065-4aea-a8d4-4b7ab273458c)
  ├─ API Permission: Dynamics CRM (user_impersonation) ✅
  └─ Admin Consent: Granted ✅

Dataverse:
  Application Users:
  ├─ Managed Identity (6bbcfa82-14a0-40b5-8695-a271f4bac521) ← Can delete
  └─ BFF API (1e40baad-e065-4aea-a8d4-4b7ab273458c) ✅
      └─ Security Role: System Administrator ✅
```

---

## Troubleshooting

### If still getting AADSTS500011 after creating Application User:

1. **Verify App ID matches:**
   ```bash
   # Check App ID in Azure Web App config
   az webapp config appsettings list \
     --resource-group spe-infrastructure-westus2 \
     --name spe-api-dev-67e2xz \
     --query "[?name=='API_APP_ID'].value" -o tsv
   ```
   Should return: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

2. **Check Application User is Enabled:**
   - In Power Platform Admin Center
   - Application Users list
   - Status should be "Enabled" (not "Disabled")

3. **Verify Security Role assigned:**
   - Edit the Application User
   - Check "Security Roles" tab
   - Should have at least one role assigned (preferably System Administrator for testing)

4. **Clear token cache:**
   ```bash
   az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
   ```

5. **Check Dataverse URL matches:**
   ```bash
   az webapp config appsettings list \
     --resource-group spe-infrastructure-westus2 \
     --name spe-api-dev-67e2xz \
     --query "[?name=='Dataverse__ServiceUrl'].value" -o tsv
   ```
   Should return: `https://spaarkedev1.crm.dynamics.com/` or `https://spaarkedev1.api.crm.dynamics.com/`

---

## Related Documentation

- [DATAVERSE-AUTHENTICATION-GUIDE.md](./DATAVERSE-AUTHENTICATION-GUIDE.md) - General Dataverse auth guide
- [KM-DATAVERSE-TO-APP-AUTHENTICATION.md](./KM-DATAVERSE-TO-APP-AUTHENTICATION.md) - App-to-Dataverse authentication patterns
- [PHASE-7-ADD-DATAVERSE-PERMISSION.md](./PHASE-7-ADD-DATAVERSE-PERMISSION.md) - Azure AD API permissions (completed)

---

## Summary

**What's Needed:**
1. Create Application User in Dataverse with App ID `1e40baad-e065-4aea-a8d4-4b7ab273458c`
2. Assign System Administrator security role to the Application User
3. Restart BFF API Web App
4. Test NavMap endpoint (should return 200 OK)

**Who Can Do This:**
- Power Platform Administrator
- Dynamics 365 System Administrator
- Global Administrator

**Estimated Time:** 5 minutes

**After Completion:** Phase 7 NavMap API will work, enabling dynamic navigation property metadata discovery in PCF controls.
