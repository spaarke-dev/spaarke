# BFF API Configuration Update for MSAL Integration

**Sprint 8 - MSAL Integration - Phase 4**
**Created:** 2025-10-06
**Purpose:** Configure existing Web App with new SPE BFF API app registration

---

## Current Status

✅ **Completed:**
- SPE BFF API app registration created
- Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- Application ID URI: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- Scope exposed: `user_impersonation`
- Dataverse app granted permission and consented

✅ **Existing Infrastructure:**
- Web App: `spe-api-dev-67e2xz.azurewebsites.net`
- Resource Group: `spe-infrastructure-westus2`
- Application Insights configured
- Managed Identity: `6bbcfa82-14a0-40b5-8695-a271f4bac521`

❌ **Issue:**
- Web App configured with OLD Dataverse app registration (`170c98e1-d486-4355-bcbe-170454e0207c`)
- Needs NEW SPE BFF API app registration (`1e40baad-e065-4aea-a8d4-4b7ab273458c`)
- Missing client secret for OBO token exchange

---

## Step 1: Create Client Secret for SPE BFF API

### In Azure Portal:

1. Navigate to **Azure Active Directory** → **App registrations**
2. Find and open **SPE BFF API** (Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`)
3. Go to **Certificates & secrets**
4. Click **+ New client secret**
5. Configure:
   - **Description:** `SPE BFF API Secret - Dev`
   - **Expires:** 24 months (or your preference)
6. Click **Add**
7. **IMPORTANT:** Copy the secret **Value** immediately (you won't see it again)
   - Format: `~Xx8Q~...` (starts with tilde, contains alphanumeric)

---

## Step 2: Configure Web App Settings

### Required Settings to Update:

Update these app settings in the Azure Web App `spe-api-dev-67e2xz`:

```bash
# Using Azure CLI
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
    "API_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c" \
    "API_CLIENT_SECRET=<paste-secret-value-here>" \
    "TENANT_ID=a221a95e-6abc-4434-aecc-e48338a1b2f2"
```

### Or via Azure Portal:

1. Go to Azure Portal → **App Services** → `spe-api-dev-67e2xz`
2. Navigate to **Configuration** → **Application settings**
3. Update/Add these settings:

| Name | Value | Description |
|------|-------|-------------|
| `API_APP_ID` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | SPE BFF API Client ID |
| `API_CLIENT_SECRET` | `<secret-value>` | SPE BFF API Client Secret |
| `TENANT_ID` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Azure AD Tenant ID |

4. Click **Save** → **Continue** to restart the app

---

## Step 3: Grant SPE BFF API Permissions to Microsoft Graph

The SPE BFF API needs permissions to access SharePoint Embedded via Graph API.

### In Azure Portal:

1. Navigate to **Azure Active Directory** → **App registrations**
2. Find and open **SPE BFF API** (Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`)
3. Go to **API permissions**
4. Click **+ Add a permission**
5. Select **Microsoft Graph** → **Delegated permissions**
6. Add these permissions:
   - `Files.Read.All`
   - `Files.ReadWrite.All`
   - `Sites.Read.All`
   - `Sites.ReadWrite.All`
7. Click **Add permissions**
8. Click **✓ Grant admin consent for [tenant]** → **Yes**

### Why These Permissions?

- **Delegated permissions:** BFF API acts on behalf of the user
- **Files.*** permissions:** Required to access SharePoint Embedded files
- **Sites.*** permissions:** Required to access SharePoint containers

---

## Step 4: Verify BFF API Code Deployment

### Check if latest code is deployed:

```bash
# Check deployment status
az webapp deployment list --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query "[0]" -o json
```

### If code needs to be deployed:

```bash
# Build and publish
cd c:\code_files\spaarke\src\api\Spe.Bff.Api
dotnet publish -c Release -o ./publish

# Create deployment package
cd publish
tar -czf ../deploy.tar.gz *
cd ..

# Deploy to Azure
az webapp deployment source config-zip \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --src deploy.tar.gz
```

---

## Step 5: Verify Configuration

### 5.1 Check App Settings Applied

```bash
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='API_APP_ID' || name=='TENANT_ID'].{name:name, value:value}" \
  -o table
```

**Expected Output:**
```
Name           Value
-------------  ------------------------------------
API_APP_ID     1e40baad-e065-4aea-a8d4-4b7ab273458c
TENANT_ID      a221a95e-6abc-4434-aecc-e48338a1b2f2
```

### 5.2 Check Application Logs

```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

**Look for:**
- ✅ Application started successfully
- ✅ Configuration validation passed
- ❌ Any errors related to Azure AD or authentication

### 5.3 Test Health Endpoint

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/health
```

**Expected:** `{"status":"Healthy"}` or similar

### 5.4 Test with MSAL Token

After configuration is complete:

1. Hard refresh browser (Ctrl+F5) to reload PCF control
2. Click **Download** button in grid
3. Check browser console for:
   - ✅ `[MsalAuthProvider] Token acquired successfully`
   - ✅ `[SdapApiClient] Download started`
   - ✅ File downloads successfully

---

## Troubleshooting

### Error: "Unauthorized" (401)

**Cause:** BFF API not accepting the token

**Solutions:**
1. Verify `API_APP_ID` is correct: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
2. Check Azure AD app registration exposed API scope matches
3. Verify Dataverse app has permission to SPE BFF API scope

### Error: "Internal Server Error" (500)

**Cause:** OBO token exchange failing

**Solutions:**
1. Verify `API_CLIENT_SECRET` is set correctly
2. Check SPE BFF API has Graph API permissions granted
3. Check application logs for detailed error:
   ```bash
   az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```

### Error: "Forbidden" (403)

**Cause:** BFF API lacks permissions to access SharePoint

**Solutions:**
1. Verify SPE BFF API has Graph API delegated permissions
2. Ensure admin consent was granted
3. Check if user has permission to access the specific file/container

---

## Configuration Summary

### Before (OLD - Not Working):
```
API_APP_ID = 170c98e1-d486-4355-bcbe-170454e0207c  ❌ (Dataverse app)
```

### After (NEW - Should Work):
```
API_APP_ID = 1e40baad-e065-4aea-a8d4-4b7ab273458c  ✅ (SPE BFF API)
API_CLIENT_SECRET = <secret-value>                 ✅ (NEW secret)
TENANT_ID = a221a95e-6abc-4434-aecc-e48338a1b2f2   ✅ (Same)
```

### OAuth Flow:
1. **User clicks Download** → PCF control acquires token from Dataverse app
2. **PCF sends token to BFF API** → `Authorization: Bearer <user-token>`
3. **BFF API validates token** → Checks audience is `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
4. **BFF API performs OBO** → Exchanges user token for Graph token using `API_CLIENT_SECRET`
5. **BFF API calls Graph** → Uses Graph token to access SharePoint Embedded
6. **BFF API returns file** → Streams content back to PCF control

---

## Next Steps

After completing configuration:

1. ✅ Restart the Web App (happens automatically when saving settings)
2. ✅ Test MSAL flow end-to-end
3. ✅ Complete Sprint 8 Phase 4 testing (7 scenarios)
4. ✅ Document successful deployment in PHASE-4-COMPLETE.md
5. ✅ Move to Sprint 8 Phase 5 (Documentation & Runbooks)

---

## Security Notes

- ⚠️ **Client Secret:** Store securely, never commit to git
- ✅ **Consider Key Vault:** For production, store secret in Azure Key Vault
- ✅ **Managed Identity:** Already configured (`6bbcfa82-14a0-40b5-8695-a271f4bac521`)
- ✅ **Least Privilege:** Only delegated permissions (acts on behalf of user)

---

## Reference

### App Registrations:
1. **Dataverse App** (PCF Control)
   - Client ID: `170c98e1-d486-4355-bcbe-170454e0207c`
   - Purpose: Acquire tokens on behalf of user
   - Permissions: SPE BFF API / user_impersonation

2. **SPE BFF API** (Backend API)
   - Client ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - Purpose: Validate tokens, perform OBO, call Graph
   - Permissions: Microsoft Graph (Files.*, Sites.*)

### Azure Resources:
- Web App: `spe-api-dev-67e2xz.azurewebsites.net`
- Resource Group: `spe-infrastructure-westus2`
- Application Insights: Configured
- Managed Identity: `6bbcfa82-14a0-40b5-8695-a271f4bac521`
