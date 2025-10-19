# SDAP BFF API Configuration Issue - URGENT ⚠️

**Date**: 2025-10-06
**Severity**: **CRITICAL** - API is not functional
**Status**: ❌ Blocking all file operations

---

## Issue Summary

The SDAP BFF API deployment succeeded, but the API is **throwing errors on all requests** due to an invalid Dataverse Service URL configuration.

**Error**:
```
System.UriFormatException: Invalid URI: The URI scheme is not valid.
   at Spe.Bff.Api.Infrastructure.DI.SpaarkeCore.<>c.<AddSpaarkeCore>b__0_0
```

**Location**: `SpaarkeCore.cs:line 31` - Dataverse HttpClient configuration

---

## Root Cause

The API is trying to create an HttpClient for Dataverse using a URL from Azure KeyVault, but the URL is **malformed or empty**.

**Configuration Location** (`appsettings.json`):
```json
"Dataverse": {
  "ServiceUrl": "@Microsoft.KeyVault(SecretUri=https://spaarke-spevcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)"
}
```

**The KeyVault secret `SPRK-DEV-DATAVERSE-URL` is either**:
1. Empty/null
2. Malformed (missing `https://` scheme)
3. Not accessible by the App Service Managed Identity

---

## Impact

### What's Broken ❌
- **ALL API endpoints** return 500 Internal Server Error
- File upload fails (after uploading to SharePoint)
- File download will fail
- File delete will fail
- File list/children will fail
- PCF control buttons **will not work**

### Why It Fails
The error occurs during **dependency injection** when trying to create the Dataverse HTTP client. Since this happens on every request, all endpoints fail.

---

## How to Fix

### Option 1: Fix KeyVault Secret (Recommended)

1. **Navigate to**: Azure Portal → Key Vault `spaarke-spevcert`

2. **Find secret**: `SPRK-DEV-DATAVERSE-URL`

3. **Check/Update value**: Should be:
   ```
   https://spaarkedev1.api.crm.dynamics.com
   ```

4. **Verify Managed Identity** has access:
   - Go to Key Vault → Access policies
   - Ensure the App Service Managed Identity has "Get" permission for secrets

5. **Restart App Service**:
   ```bash
   az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
   ```

### Option 2: Override in App Service Configuration (Quick Fix)

1. **Navigate to**: Azure Portal → App Service `spe-api-dev-67e2xz`

2. **Configuration** → **Application settings**

3. **Add new setting**:
   - Name: `Dataverse__ServiceUrl`
   - Value: `https://spaarkedev1.api.crm.dynamics.com`

4. **Save** and **Restart**

This will override the KeyVault reference temporarily.

---

## Verification Steps

### After fixing the configuration:

1. **Test the ping endpoint**:
   ```bash
   curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
   ```

   **Expected**:
   ```json
   {
     "service": "Spe.Bff.Api",
     "version": "1.0.0",
     "environment": "Production",
     "timestamp": "2025-10-06T..."
   }
   ```

2. **Test Dataverse health check**:
   ```bash
   curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
   ```

   **Expected**:
   ```json
   {
     "status": "healthy",
     "message": "Dataverse connection successful"
   }
   ```

3. **Test file upload**:
   ```bash
   curl -X PUT "https://spe-api-dev-67e2xz.azurewebsites.net/api/drives/b!.../upload?fileName=test.txt" \
     -H "Content-Type: text/plain" \
     --data "Test content"
   ```

   **Expected**: JSON response with file metadata (id, name, webUrl, etc.)

---

## Why This Wasn't Caught Earlier

1. **Local development** likely uses a different configuration (local secrets or appsettings.Development.json)
2. **Previous deployments** may have had the correct KeyVault value
3. **CORS fix deployment** didn't change KeyVault configuration, so the error existed before

---

## Current Status

### What Works ✅
- CORS configuration deployed correctly
- API service is running
- Authentication endpoints accessible

### What's Broken ❌
- All endpoints requiring Dataverse access
- All endpoints requiring authorization (due to DI failure)
- File operations
- PCF control buttons

### Blocking Issues
- ⚠️ **Fix Dataverse URL configuration** before proceeding with testing

---

## Recommended Action Plan

1. **Immediate** (User): Check/fix KeyVault secret `SPRK-DEV-DATAVERSE-URL`
2. **Alternative** (Quick): Add `Dataverse__ServiceUrl` to App Service configuration
3. **Verify**: Test ping and Dataverse health endpoints
4. **Then**: Retry file upload
5. **Finally**: Test PCF control buttons in Power Apps

---

## Technical Details

### Error Location
**File**: `src/api/Spe.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`
**Line**: 31

**Code** (likely):
```csharp
builder.Services.AddHttpClient<IDataverseService, DataverseWebApiService>(client =>
{
    var dataverseUrl = sp.GetRequiredService<IOptions<DataverseOptions>>().Value.ServiceUrl;
    client.BaseAddress = new Uri(dataverseUrl); // <-- FAILS HERE
    client.Timeout = TimeSpan.FromSeconds(30);
});
```

### KeyVault Reference Format
Azure App Service automatically resolves KeyVault references:
```
@Microsoft.KeyVault(SecretUri=https://vault-name.vault.azure.net/secrets/secret-name)
```

If the secret doesn't exist or can't be accessed, the value will be empty/null.

### Expected Dataverse URL
```
https://spaarkedev1.api.crm.dynamics.com
```

**NOT**:
- `spaarkedev1.api.crm.dynamics.com` (missing scheme)
- `http://...` (wrong scheme)
- Empty/null

---

## Summary

**Issue**: Invalid/missing Dataverse Service URL in configuration
**Impact**: API completely non-functional
**Fix**: Update KeyVault secret or App Service configuration
**Priority**: **CRITICAL** - must fix before testing PCF control

---

**Status**: ⏳ Awaiting configuration fix
**Blocking**: All Sprint 7A testing
**Next**: Fix Dataverse URL → Verify API → Test file upload → Test PCF buttons
