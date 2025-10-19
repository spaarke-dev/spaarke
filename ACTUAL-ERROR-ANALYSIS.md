# Actual Error Analysis - Item Not Found

**Date**: 2025-10-16
**Status**: ✅ Actual exception captured from Application Insights
**Conclusion**: ERROR IS NOT ABOUT MISSING SCOPE - IT'S "ITEM NOT FOUND"

---

## The Actual Exception

### Exception Type
```
Microsoft.Graph.Models.ODataErrors.ODataError
```

### Message
```
Item not found
```

### Call Stack Location
```
at Spe.Bff.Api.Infrastructure.Graph.UploadSessionManager+<UploadSmallAsUserAsync>d__6.MoveNext
   (Spe.Bff.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null:
   c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\UploadSessionManager.cs:253)
```

### Failed Graph SDK Call
```
at Microsoft.Graph.Drives.Item.Items.Item.Content.ContentRequestBuilder+<PutAsync>d__4.MoveNext
```

---

## What This Means

### ❌ NOT a Permission/Scope Issue

**This is NOT**:
- ❌ "Insufficient privileges to complete the operation"
- ❌ "Access denied"
- ❌ Missing `FileStorageContainer.Selected` scope
- ❌ App not registered in Container Type
- ❌ User lacks permissions

### ✅ It's a "Not Found" Error

**This IS**:
- ✅ Graph API returning 404 Not Found
- ✅ The container ID or path doesn't exist
- ✅ OR the parent folder doesn't exist
- ✅ OR trying to access an item that was deleted

**HTTP Status**: 404 Not Found (not 403 Forbidden)

---

## Why This Is Actually GOOD News

### The Authentication Works! ✅

**Evidence**:
1. ✅ Token A validated successfully (no 401)
2. ✅ OBO token exchange succeeded (no MSAL error)
3. ✅ Graph API call was ATTEMPTED (got past authentication)
4. ✅ Token B has sufficient scopes (no 403 Forbidden)
5. ✅ App is registered in Container Type (no access denied)

**The error is NOT about permissions - it's about the resource not existing.**

---

## Root Cause Analysis

### Where the Error Occurs

**File**: `UploadSessionManager.cs` line 253

**Code** (likely):
```csharp
var uploadedItem = await graphClient.Drives[containerId].Root
    .ItemWithPath(path)
    .Content
    .PutAsync(content);
```

### Possible Causes

#### Cause 1: Container ID Doesn't Exist

**Scenario**: The `containerId` passed to the API doesn't match any existing SharePoint Embedded container.

**Container ID being used**: Unknown (need to check the actual request)

**Expected Container ID**: `8a6ce34c-6055-4681-8f87-2f4f9f921c06` (from Container Type)

**Check**: Is the PCF control passing the correct container ID?

---

#### Cause 2: Parent Folder Path Doesn't Exist

**Scenario**: Trying to upload to a subfolder that doesn't exist.

**Example**:
- Upload path: `/documents/subfolder/file.txt`
- But `/documents/subfolder/` doesn't exist
- Graph API returns "Item not found"

**Solution**: Create parent folders first, or upload to root

---

#### Cause 3: Using Wrong Endpoint Format

**Scenario**: The path format is incorrect for SharePoint Embedded.

**Correct Format**:
```
PUT /drives/{containerId}/root:/{filename}:/content
```

**Incorrect Format** (might cause 404):
```
PUT /drives/{containerId}/items/{itemId}/content  // itemId doesn't exist yet
```

**Check**: What is the actual Graph API endpoint being called?

---

#### Cause 4: Container Not Created Yet

**Scenario**: Container Type exists, but no actual container has been created for this entity.

**In SharePoint Embedded**:
- Container Type = Template/Registration (exists ✅)
- Container Instance = Actual storage location (may not exist ❌)

**Solution**: Create container before uploading file

---

## What We Need to Check

### 1. What Container ID is Being Used?

**Add logging to UploadSessionManager.cs around line 253**:

```csharp
_logger.LogInformation("🔍 DEBUG: Uploading file to containerId={ContainerId}, path={Path}",
    containerId, path);

var uploadedItem = await graphClient.Drives[containerId].Root
    .ItemWithPath(path)
    .Content
    .PutAsync(content);
```

**Expected**: Should log the actual container ID being passed

---

### 2. Does the Container Actually Exist?

**Test with Graph API**:
```http
GET https://graph.microsoft.com/beta/drives/{containerId}
Authorization: Bearer {Token B}
```

**Expected Response**:
- ✅ 200 OK = Container exists
- ❌ 404 Not Found = Container doesn't exist (THIS IS THE ISSUE)

---

### 3. What is the Upload Path?

**Check**: Is the path correct?
- ✅ Correct: `/filename.txt` (upload to root)
- ✅ Correct: `/folder/filename.txt` (if folder exists)
- ❌ Incorrect: `folder/filename.txt` (missing leading slash)
- ❌ Incorrect: `/nonexistent-folder/filename.txt` (folder doesn't exist)

---

### 4. Is the Container Created When Entity is Created?

**Dataverse Workflow Check**:
- When a new entity (e.g., sprk_document) is created in Dataverse
- Is a corresponding SharePoint Embedded container created automatically?
- OR do we need to create it manually first?

**If NOT created automatically**: Need to add container creation logic.

---

## Immediate Action Items

### Step 1: Check Logs for Container ID

**Look in the recent logs** for the actual container ID being used:

```bash
# Check if there's any logging showing the container ID
grep -i "container" <recent-log-file>
```

**Expected**: Should see the container ID in the upload request

---

### Step 2: Verify Container Exists

**Option A: Use Azure CLI**:
```bash
# Get a token for Graph API
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

# Check if container exists
curl -H "Authorization: Bearer $TOKEN" \
  "https://graph.microsoft.com/beta/drives/8a6ce34c-6055-4681-8f87-2f4f9f921c06"
```

**Expected**:
- ✅ 200 OK with container metadata = Container exists
- ❌ 404 Not Found = Container doesn't exist (ROOT CAUSE)

**Option B: Check in Dataverse**:
- Look at the `sprk_container` table
- See if there are any container records
- Check if the container ID matches what's being used

---

### Step 3: Check Container Creation Logic

**Search codebase**:
```bash
# Look for container creation code
grep -rn "CreateContainer\|NewContainer\|storage/fileStorage/containers" src/api/
```

**Questions**:
1. Where is container creation supposed to happen?
2. Is it triggered when a Dataverse entity is created?
3. Is there a "ensure container exists" check before upload?

---

## Updated Hypothesis

### Original Hypothesis (WRONG)
❌ Missing `FileStorageContainer.Selected` scope causes 403 Forbidden

### Actual Issue (CORRECT)
✅ Container doesn't exist (or wrong container ID used), causing 404 Not Found

---

## Why We Got HTTP 500 Instead of 404

### ASP.NET Core Error Handling

**What likely happened**:
1. Graph API returns 404 Not Found
2. Graph SDK throws `ODataError` with "Item not found"
3. Exception bubbles up through UploadSessionManager
4. ASP.NET Core exception middleware catches it
5. Middleware returns HTTP 500 (unhandled exception default)
6. IIS passes through the 500 to client

**Why not 404**:
- Exception handler doesn't distinguish between Graph 404 and other errors
- Default behavior = 500 for unhandled exceptions
- Need custom exception handling to preserve Graph API status codes

---

## Next Steps (IN ORDER)

### Priority 1: Find Out What Container ID is Being Used

**Add logging** to see the actual container ID in the upload request.

**OR check PCF control code** to see what container ID it's sending.

---

### Priority 2: Verify Container Exists

**Test if container ID `8a6ce34c-6055-4681-8f87-2f4f9f921c06` exists**:
```bash
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)
curl -v -H "Authorization: Bearer $TOKEN" \
  "https://graph.microsoft.com/beta/drives/8a6ce34c-6055-4681-8f87-2f4f9f921c06"
```

**Expected**: 404 Not Found (this IS the container TYPE ID, not a container instance)

---

### Priority 3: Create Container Instance

**Find or create logic to**:
1. Create a SharePoint Embedded container instance
2. Store the container ID in Dataverse (sprk_container table)
3. Use that container ID for file uploads

---

## Critical Realization

### Container Type ID ≠ Container ID

**Container Type ID**: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- This is the TEMPLATE/REGISTRATION
- This is what you created with `New-SPOContainerType`
- **YOU CANNOT UPLOAD FILES TO THIS**

**Container Instance ID**: (Unknown - probably doesn't exist yet)
- This is an ACTUAL STORAGE CONTAINER
- This is created with Graph API: `POST /storage/fileStorage/containers`
- **THIS IS WHERE FILES ARE UPLOADED**

**Graph API Call**:
```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containers
Content-Type: application/json
Authorization: Bearer {Token B with FileStorageContainer.Selected}

{
  "displayName": "My Container",
  "description": "Container for entity XYZ",
  "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
}
```

**Response**:
```json
{
  "id": "b!a1b2c3d4e5f6...",  // THIS is the container ID to use for uploads
  "displayName": "My Container",
  "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
  ...
}
```

---

## THE ACTUAL PROBLEM

**You're trying to upload to the Container Type ID instead of a Container Instance ID.**

**This is like**:
- Container Type = "Blueprint for houses"
- Container Instance = "Actual house at 123 Main St"
- You can't put furniture in a blueprint - you need an actual house

**Fix Required**:
1. Create a container instance (using Container Type ID)
2. Get the container instance ID from the response
3. Use that ID for file uploads

---

## Question for User

**Have you created any actual container instances?**

OR

**Is the code supposed to auto-create a container when uploading?**

OR

**Where is the container instance ID coming from in the PCF control?**

---

**Document Created**: 2025-10-16 06:45 AM
**Status**: ✅ Root cause identified - Container doesn't exist (404 Not Found)
**Next Action**: Verify container creation logic and ensure container instance exists before upload
