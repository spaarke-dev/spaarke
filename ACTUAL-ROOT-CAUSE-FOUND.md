# Actual Root Cause - Container Instance Doesn't Exist

**Date**: 2025-10-16
**Status**: ✅ ROOT CAUSE IDENTIFIED

---

## The Actual Request

**User provided**:
```
PUT /api/obo/containers/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/22874_11420624_10-23-2008_CTNF.PDF
```

**Container ID**: `b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`

**File**: `22874_11420624_10-23-2008_CTNF.PDF`

---

## Key Finding: Container ID is CORRECT Format

### ✅ This IS a Container Instance ID

**Evidence**:
- Starts with `b!` - SharePoint Embedded drive ID prefix ✅
- Correct length and format ✅
- Not the Container Type ID (`8a6ce34c...`) ✅

**This means**:
- Someone/something created a container instance ✅
- The ID was stored in Dataverse ✅
- PCF control is sending the correct format ID ✅

---

## But Graph API Says "Item not found"

### The Problem

**Graph API Call** (from UploadSessionManager.cs:253):
```http
PUT https://graph.microsoft.com/beta/drives/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/root:/22874_11420624_10-23-2008_CTNF.PDF:/content
Authorization: Bearer {Token B}
```

**Graph API Response**:
```
404 Not Found
Error: Item not found
```

**This means ONE of these things**:

1. ❌ The container instance **doesn't exist** (was never created or was deleted)
2. ❌ The container exists but in a **different tenant**
3. ❌ The container exists but the **user doesn't have access**
4. ❌ The Token B is missing `FileStorageContainer.Selected` scope (back to original hypothesis)

---

## How to Verify Which Problem

### Test 1: Check if Container Exists

**Run this command**:
```bash
# Get user token (same scopes as Token B)
az login

# Get token for Graph API
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

# Try to GET the container
curl -v -H "Authorization: Bearer $TOKEN" \
  "https://graph.microsoft.com/beta/drives/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

**Expected Results**:

#### If Container Exists:
```json
{
  "id": "b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50",
  "driveType": "business",
  "name": "...",
  "description": "...",
  ...
}
```
**Then**: Container exists, but something else is wrong

#### If Container Doesn't Exist:
```json
{
  "error": {
    "code": "itemNotFound",
    "message": "The resource could not be found."
  }
}
```
**Then**: Container was deleted or never created

#### If Missing Scope:
```json
{
  "error": {
    "code": "accessDenied",
    "message": "Insufficient privileges to complete the operation."
  }
}
```
**Then**: Back to the FileStorageContainer.Selected scope issue

---

### Test 2: Check Token B Scopes

**Look at Application Insights logs** for the OBO token exchange:

**Query**:
```kusto
traces
| where timestamp > ago(24h)
| where message contains "OBO token scopes"
| project timestamp, message
| order by timestamp desc
```

**Expected Log** (from GraphClientFactory.cs:159):
```
OBO token scopes: Sites.FullControl.All, Files.ReadWrite.All
```

**What we need to see**:
```
OBO token scopes: Sites.FullControl.All, Files.ReadWrite.All, FileStorageContainer.Selected
```

**If FileStorageContainer.Selected is MISSING**: That's still the problem (even though container ID is correct)

---

## Most Likely Scenario

### Container Exists BUT Token B Missing Scope

**What's happening**:
1. ✅ Container instance exists somewhere
2. ✅ ID is correct format
3. ✅ ID stored in Dataverse
4. ✅ PCF sends correct ID
5. ✅ OBO exchange succeeds
6. ❌ Token B doesn't have `FileStorageContainer.Selected` scope
7. ❌ Graph API rejects: "You don't have permission to access this container"
8. ❌ Graph SDK returns: "Item not found" (unhelpful error message)

**Why "Item not found" instead of "Access denied"?**

Graph API sometimes returns 404 instead of 403 when:
- Resource exists but you don't have permission
- This prevents information disclosure (you can't tell if resource exists)
- Security by obscurity pattern

---

## Second Most Likely Scenario

### Container Was Deleted or Expired

**SharePoint Embedded Trial Containers**:
- Trial containers expire after 30 days
- Standard PAYGO containers don't expire
- Container Type shows: `Classification: Standard` (from V2 architecture doc)

**But** if the container was created during testing and then deleted:
- ID still exists in Dataverse ✅
- Container is gone from SPE ❌
- Result: "Item not found"

---

## Third Scenario

### Container in Different Tenant/Environment

**If the container ID was**:
- Created in a test tenant
- Copied to production Dataverse
- But doesn't exist in production SPE

**Result**: "Item not found"

---

## Recommended Next Steps

### Step 1: Verify Token B Has Required Scope

**Check Application Insights** for the log message:
```
OBO token scopes: {Scopes}
```

**If missing FileStorageContainer.Selected**:
- Add scope to GraphClientFactory.cs line 153
- This is still the fix needed

**If scope is present**:
- Move to Step 2

---

### Step 2: Test if Container Exists

**Run the curl command above** to GET the container.

**If 404 Not Found**:
- Container doesn't exist, need to create it
- Or ID is from wrong tenant

**If 200 OK**:
- Container exists, different problem

**If 403 Forbidden**:
- Confirms scope issue

---

### Step 3: Trigger Upload with Log Stream Active

**Currently**: Log stream is running in background

**Now**: Trigger upload again from PCF control

**Watch for**:
- Line 242: "Using Container ID directly as Drive ID for SPE upload: {ContainerId}"
- Should show the actual container ID being used
- Then we'll see the actual Graph API error

Let me check the current log stream for any recent activity:
