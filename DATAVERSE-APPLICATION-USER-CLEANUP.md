# Dataverse Application User Cleanup - Action Plan

## Current State Assessment

### Check Existing Application Users in Dataverse

**Navigate to**: Power Platform Admin Center → Environments → spaarkedev1 → Settings → Users → Application Users

**Expected Findings**:
- ✅ Application User for `170c98e1...` (Spaarke DSM-SPE Dev 2) - **TO REMOVE**
- ✅ Application User for `6bbcfa82...` (spe-api-dev-67e2xz MI) - **TO REMOVE OR KEEP**
- ❓ Application User for `1e40baad...` (SDAP-BFF-SPE-API) - **SHOULD EXIST**

---

## Required Actions

### Action 1: Verify SDAP-BFF-SPE-API Application User Exists

**Application ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**If EXISTS**:
- ✅ Verify Security Role: **System Administrator**
- ✅ Verify Application ID matches: `1e40baad...`
- ✅ No action needed - already correct

**If DOES NOT EXIST**:
- ❌ **CREATE** Application User
- Assign Security Role: **System Administrator**

**PowerShell Command to Create** (if needed):
```powershell
# Install required module
Install-Module Microsoft.PowerApps.Administration.PowerShell -Scope CurrentUser

# Connect to environment
Add-PowerAppsAccount
$envId = "b5a401dd-b42b-e84a-8cab-2aef8471220d"  # spaarkedev1

# Create Application User
New-PowerAppManagementApp `
    -ApplicationId "1e40baad-e065-4aea-a8d4-4b7ab273458c" `
    -EnvironmentName $envId `
    -SecurityRoles @("System Administrator")
```

---

### Action 2: Remove Spaarke DSM-SPE Dev 2 Application User

**Application ID**: `170c98e1-d486-4355-bcbe-170454e0207c`

**Reason for Removal**: Public client, performs no S2S operations

**Steps**:
1. Navigate to Power Platform Admin Center
2. Select Environment: spaarkedev1
3. Go to Settings → Users → Application Users
4. Find Application User with App ID: `170c98e1...`
5. **Delete** the Application User

**Verification**:
- ✅ PCF control still works (uses user token, not app identity)
- ✅ User authentication unaffected
- ✅ BFF API still validates tokens correctly

**Note**: This will NOT break PCF authentication because:
- PCF never calls Dataverse directly
- Token represents the USER (ralph.schroeder@spaarke.com)
- User already has Dataverse access
- BFF API performs authorization checks on behalf of user

---

### Action 3: Decide on Managed Identity Application User

**Application ID**: `6bbcfa82-14a0-40b5-8695-a271f4bac521`

**Option A: Keep MI Application User**
```
Use Cases:
- System-level Dataverse operations without user context
- Background jobs that need Dataverse access
- Health checks and monitoring
- Future automation scenarios

Configuration:
- Keep Application User with System Administrator role
- MI authenticates to Dataverse for system operations
- BFF API uses client secret for user-context operations
```

**Option B: Remove MI Application User (Recommended)**
```
Simpler Pattern:
- BFF API (1e40baad...) handles ALL Dataverse operations
- MI only for Azure resource access (KeyVault, Service Bus)
- Single security principal for all Dataverse operations
- Aligns with sdap_V2 target architecture

Configuration:
- Remove MI Application User from Dataverse
- MI still has KeyVault access (Key Vault Secrets User)
- MI still has Service Bus access (Azure Service Bus Data Receiver)
- BFF API uses client secret for ALL Dataverse operations
```

**Recommendation**: **Option B** - Remove MI Application User

**Steps to Remove** (if Option B chosen):
1. Navigate to Power Platform Admin Center
2. Select Environment: spaarkedev1
3. Go to Settings → Users → Application Users
4. Find Application User with App ID: `6bbcfa82...`
5. **Delete** the Application User

**Keep Azure RBAC Roles**:
- ✅ Key Vault Secrets User (for reading configuration secrets)
- ✅ Azure Service Bus Data Receiver (for background job processing)

---

## Final Target Configuration

### Dataverse Application Users (After Cleanup)

| App Name | App ID | Has Application User? | Security Role | Purpose |
|----------|--------|-----------------------|---------------|---------|
| **SDAP-BFF-SPE-API** | `1e40baad...` | ✅ **YES** | System Administrator | ALL Dataverse S2S operations |
| **Spaarke DSM-SPE Dev 2** | `170c98e1...` | ❌ **NO** | N/A | Public client (browser auth only) |
| **spe-api-dev-67e2xz MI** | `6bbcfa82...` | ❌ **NO** | N/A | Azure resources only (KeyVault, Service Bus) |

### Azure AD App Registrations (No Change)

| App Name | App ID | Type | Secrets | Purpose |
|----------|--------|------|---------|---------|
| **SDAP-BFF-SPE-API** | `1e40baad...` | Confidential | ✅ Has secret | Backend API (OBO + Dataverse) |
| **Spaarke DSM-SPE Dev 2** | `170c98e1...` | Public | ❌ No secrets | PCF control (user auth) |

### Azure Managed Identity (No Change)

| Resource | Identity ID | RBAC Roles | Purpose |
|----------|-------------|------------|---------|
| **spe-api-dev-67e2xz** | `6bbcfa82...` | Key Vault Secrets User<br/>Azure Service Bus Data Receiver | Azure resource access |

---

## Configuration Updates Required

### Update appsettings.json (No Change Needed)

Current configuration is already correct:
```json
{
  "API_APP_ID": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "API_CLIENT_SECRET": "@Microsoft.KeyVault(...BFF-API-ClientSecret)",
  "TENANT_ID": "a221a95e-6abc-4434-aecc-e48338a1b2f2",

  "Dataverse": {
    "ServiceUrl": "@Microsoft.KeyVault(...SPRK-DEV-DATAVERSE-URL)",
    "ClientId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",      // Same as API_APP_ID
    "ClientSecret": "@Microsoft.KeyVault(...BFF-API-ClientSecret)"  // Same secret
  }
}
```

### Update DataverseServiceClientImpl.cs (Phase 1 Refactoring)

**Current (Broken)**:
```csharp
// Uses Managed Identity - FAILS
var credential = new ManagedIdentityCredential();
_serviceClient = new ServiceClient(instanceUrl, tokenProviderFunction: ...);
```

**Target (Correct)**:
```csharp
// Uses Client Secret - WORKS
var connectionString =
    $"AuthType=ClientSecret;" +
    $"Url={dataverseUrl};" +
    $"ClientId={clientId};" +        // 1e40baad...
    $"ClientSecret={clientSecret};" +
    $"RequireNewInstance=false;";    // Enable connection pooling

_serviceClient = new ServiceClient(connectionString);
```

---

## Testing Checklist

After cleanup, verify:

### ✅ User Authentication
- [ ] PCF control login still works
- [ ] User receives valid JWT token
- [ ] Token audience is `api://1e40baad...`

### ✅ BFF API Validation
- [ ] BFF API validates user tokens
- [ ] JWT bearer authentication works
- [ ] Authorization policies work

### ✅ Dataverse Operations
- [ ] BFF API can query Dataverse
- [ ] Authorization checks work (user access rights)
- [ ] Document CRUD operations work
- [ ] Health check `/healthz/dataverse` returns success

### ✅ SPE Operations
- [ ] File upload works (OBO flow)
- [ ] File download works
- [ ] Container operations work

### ✅ Background Services
- [ ] Service Bus processor starts without errors
- [ ] Document event processing works
- [ ] Jobs execute successfully

---

## Rollback Plan

If issues occur after cleanup:

### Restore Spaarke DSM-SPE Dev 2 Application User
```powershell
New-PowerAppManagementApp `
    -ApplicationId "170c98e1-d486-4355-bcbe-170454e0207c" `
    -EnvironmentName "b5a401dd-b42b-e84a-8cab-2aef8471220d" `
    -SecurityRoles @("System Administrator")
```

### Restore MI Application User
```powershell
New-PowerAppManagementApp `
    -ApplicationId "6bbcfa82-14a0-40b5-8695-a271f4bac521" `
    -EnvironmentName "b5a401dd-b42b-e84a-8cab-2aef8471220d" `
    -SecurityRoles @("System Administrator")
```

---

## Summary

**Correct Configuration** (Option 1):
- ✅ **SDAP-BFF-SPE-API** (`1e40baad...`) - HAS Application User (System Administrator)
- ❌ **Spaarke DSM-SPE Dev 2** (`170c98e1...`) - NO Application User
- ❌ **spe-api-dev-67e2xz MI** (`6bbcfa82...`) - NO Application User (Azure resources only)

**Why This is Simpler**:
- Single security principal for ALL Dataverse operations
- Clear separation: User auth (PCF) vs Service operations (BFF)
- Aligns with sdap_V2 target architecture
- Easier to audit and troubleshoot

**Next Step**: Execute cleanup, then proceed to Phase 1 refactoring
