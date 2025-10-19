# How to Confirm the HTTP 500 Root Cause

**Date**: 2025-10-16
**Current Status**: Detailed logging enabled, waiting to capture actual error
**Hypothesis**: Missing `FileStorageContainer.Selected` scope in OBO token

---

## Current State

### What We've Done

✅ **Enabled Detailed Logging** (Oct 16 04:51 AM):
```bash
az webapp config appsettings set --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings ASPNETCORE_DETAILEDERRORS=true \
             Logging__LogLevel__Default=Debug \
             Logging__LogLevel__Microsoft.AspNetCore=Debug
```

✅ **Restarted App**: App restarted successfully at 04:52 AM

✅ **Verified App Running**: `/ping` endpoint returns JSON ✅

❌ **Missing**: Actual file upload attempt to capture the error

---

## Option 1: Trigger Upload and Check Live Logs (RECOMMENDED)

### Step 1: Start Fresh Log Stream

```bash
az webapp log tail \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

**Expected Output**: Live stream of logs with timestamps

### Step 2: Trigger File Upload from PCF Control

**In Dataverse**:
1. Navigate to Model Driven App with PCF control
2. Open a record that has the UniversalQuickCreate control
3. Select a small test file (< 1MB)
4. Click Upload

**Expected Behavior**:
- Browser shows HTTP 500 error
- Log stream captures the actual .NET exception

### Step 3: Review Log Output

**Look For These Patterns**:

#### Pattern 1: Missing Scope Error
```
Error: AADSTS65001: The user or administrator has not consented to use the application
Error: AADSTS70011: The provided request must include a 'scope' input parameter
Error: Insufficient privileges to complete the operation
```

#### Pattern 2: Graph API 403 Forbidden
```
Microsoft.Graph.ServiceException: Code: AccessDenied
Message: Access denied. Insufficient privileges to complete the operation.
Status: Forbidden
```

#### Pattern 3: Container Access Denied
```
Microsoft.Graph.ServiceException: Code: AccessDenied
Message: Application is not registered for this container type
Status: Forbidden
```

#### Pattern 4: OBO Token Exchange Failure
```
Microsoft.Identity.Client.MsalServiceException: AADSTS65001
ErrorCode: invalid_grant
Claims: scope=FileStorageContainer.Selected
```

### Step 4: Analyze the Error

**If you see "FileStorageContainer.Selected"** mentioned in the error:
- ✅ **Confirms our hypothesis** - scope is missing
- ✅ **Next action**: Add scope to GraphClientFactory.cs

**If you see "Application is not registered"**:
- ⚠️ **Different issue** - Container Type registration
- ⚠️ **Next action**: Verify registration status

**If you see a different error**:
- 🔍 **Unknown issue** - share the error for analysis
- 🔍 **Next action**: Investigate based on actual error message

---

## Option 2: Check Application Insights (Historical Logs)

### Query Application Insights for Recent Errors

```bash
# Get recent exceptions from Application Insights
az monitor app-insights query \
  --app 6a76b012-46d9-412f-b4ab-4905658a9559 \
  --analytics-query "exceptions | where timestamp > ago(1h) | project timestamp, type, outerMessage, innermostMessage | order by timestamp desc" \
  --output table
```

**Expected Output**: Recent exceptions with error messages

**Look For**:
- "ServiceException" from Microsoft.Graph
- "MsalServiceException" from token acquisition
- "AccessDenied" or "Forbidden" messages
- Any mention of "FileStorageContainer" or "scope"

---

## Option 3: Verify Current App Permissions (No Upload Needed)

### Check if FileStorageContainer.Selected is Granted

```bash
# List all Microsoft Graph permissions for BFF API app
az ad app permission list \
  --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[].{id:id,type:type}" \
  --output table
```

**Look For**:
- ID: `085ca537-6565-41c2-aca7-db852babc212` (FileStorageContainer.Selected - Delegated)
- Type: `Scope`

**If NOT found**:
- ⚠️ **Problem**: Permission not granted in app registration
- ⚠️ **Next action**: Add permission to app manifest first

**If found**:
- ✅ **Good**: Permission is granted in app registration
- ⚠️ **Problem**: OBO code doesn't REQUEST it (confirmed from code review)
- ✅ **Next action**: Add scope to GraphClientFactory.cs

---

## Option 4: Add Temporary Debug Logging (Code Change)

### Add Logging to See OBO Token Scopes

**Location**: `GraphClientFactory.cs` after line 159

```csharp
_logger.LogInformation("OBO token exchange successful");
_logger.LogInformation("OBO token scopes: {Scopes}", string.Join(", ", result.Scopes));

// ADD THIS NEW LINE:
_logger.LogWarning("🔍 DEBUG: Token scopes = [{Scopes}]. Expected to include 'FileStorageContainer.Selected'",
    string.Join(", ", result.Scopes));
```

**Then**:
1. Deploy updated code
2. Trigger upload
3. Check logs for the DEBUG line
4. Verify if `FileStorageContainer.Selected` appears in the list

**Expected Result**:
- Current scopes will show: `Sites.FullControl.All Files.ReadWrite.All`
- Missing: `FileStorageContainer.Selected`

---

## Option 5: Test with Postman/curl (Bypass PCF Control)

### Step 1: Get User Token for BFF API

**Use Browser Dev Tools**:
1. Open Dataverse Model Driven App
2. Open browser console (F12)
3. Look for network requests to `spe-api-dev-67e2xz.azurewebsites.net`
4. Copy the `Authorization: Bearer {token}` header value

**Or use MSAL in browser console**:
```javascript
// Run in browser console on Dataverse page
const token = await window.msalInstance.acquireTokenSilent({
    scopes: ["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"]
});
console.log(token.accessToken);
```

### Step 2: Test Upload Endpoint Directly

```bash
# Replace {TOKEN} with actual token from step 1
curl -X PUT \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/8a6ce34c-6055-4681-8f87-2f4f9f921c06/files/test.txt" \
  -H "Authorization: Bearer {TOKEN}" \
  -H "Content-Type: text/plain" \
  -d "test file content" \
  -v
```

**Expected Response**:
- **If scope missing**: HTTP 500 with IIS error page
- **If scope present**: HTTP 201 with file metadata (after we fix it)

**Look for in response headers**:
- `X-Correlation-Id` - Use this to search logs
- Error message in response body

---

## Recommended Approach

### Best: Option 1 (Live Logs + Upload Test)

**Why**:
- ✅ Most direct - see the actual error as it happens
- ✅ Fastest feedback loop
- ✅ Detailed logging already enabled
- ✅ No code changes needed

**Time Required**: 2 minutes

**Steps**:
1. Start log stream: `az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2`
2. Keep terminal open
3. Trigger upload from PCF control in Dataverse
4. Watch logs for exception details

### Alternative: Option 3 (Check App Permissions)

**Why**:
- ✅ No upload needed
- ✅ Verifies if permission exists in app registration
- ✅ Quick check (30 seconds)

**Limitation**:
- ⚠️ Only tells us if permission is GRANTED, not if it's REQUESTED in OBO

**Steps**:
1. Run: `az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c`
2. Look for `085ca537-6565-41c2-aca7-db852babc212`
3. User already confirmed this exists, but good to verify

---

## What We Already Know (From Code Review)

✅ **Confirmed from actual code**:
- GraphClientFactory.cs line 150-154 does NOT request `FileStorageContainer.Selected`
- Only requests: `Sites.FullControl.All`, `Files.ReadWrite.All`
- Graph endpoint is correctly set to `/beta/` (line 207)

✅ **User confirmed**:
- App registration HAS the permission granted
- Container Type registration completed
- Admin consent given

❌ **Still unknown**:
- What is the ACTUAL error message from Graph API?
- Is it 403 Forbidden (scope missing) or something else?

---

## Decision Tree

```
START: Want to confirm error cause
  │
  ├─> Do you want ABSOLUTE certainty before code change?
  │   └─> YES → Use Option 1 (trigger upload, watch logs)
  │   └─> NO → Skip to code fix (we're 95% certain it's the missing scope)
  │
  ├─> Can you trigger upload from PCF right now?
  │   └─> YES → Use Option 1 (best option)
  │   └─> NO → Use Option 3 (check app permissions only)
  │
  └─> Do you trust the code review findings?
      └─> YES → Make the one-line fix now (add scope)
      └─> NO → Use Option 1 to see actual error first
```

---

## My Recommendation

**Just trigger the upload now with log stream running.**

**Rationale**:
1. We've already reviewed the actual code - it's missing the scope
2. The guide explicitly shows the scope is required
3. User confirmed permission is granted in app registration
4. We have detailed logging enabled
5. It will take 2 minutes to confirm

**Command to run in terminal**:
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

**Then**: Trigger upload from Dataverse PCF control and watch what error appears.

**Expected log output**:
```
2025-10-16 XX:XX:XX [Error] Microsoft.Graph.ServiceException:
  Code: AccessDenied
  Message: Insufficient privileges to complete the operation
  StatusCode: Forbidden
  ... somewhere in stack trace will mention FileStorageContainer or scope ...
```

This will give us 100% certainty before making the code change.

---

**Document Created**: 2025-10-16 06:00 AM
**Status**: Ready to confirm error - awaiting user to trigger upload with log stream running
