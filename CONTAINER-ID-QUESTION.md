# The Container ID Question

**Date**: 2025-10-16
**Issue**: "Item not found" error - which container ID is being used?

---

## The Critical Question

**You said**: "we do have the Container ID in the form--is that not included in the Graph API call?"

**My question back**: **WHICH container ID is in that form field?**

---

## Two Very Different IDs

### Option A: Container Type ID (Template)
```
8a6ce34c-6055-4681-8f87-2f4f9f921c06
```

**What it is**:
- The Container Type you created with PowerShell
- The template/blueprint for containers
- Stored in SPE tenant configuration

**Can you upload files to it?**: ❌ **NO**
- This is like trying to upload files to a blueprint
- Graph API will return: "Item not found" (404)

---

### Option B: Container Instance ID (Actual Storage)
```
b!a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6...
```

**What it is**:
- An actual storage container created FROM the Container Type
- A real drive/library where files can be stored
- Each entity (document, project, etc.) gets its own container instance

**Can you upload files to it?**: ✅ **YES**
- This is an actual storage location
- Graph API will accept file uploads

---

## How to Tell Which You Have

### Container Type ID Format
- Usually a standard GUID format
- Example: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
- Created by: `New-SPOContainerType` PowerShell command

### Container Instance ID Format
- Starts with `b!` (SharePoint Embedded drive ID prefix)
- Example: `b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
- Created by: Graph API POST to `/storage/fileStorage/containers`

---

## What You Need to Check

### In Dataverse Form

**Look at the container ID field value**:

1. **If it's `8a6ce34c-6055-4681-8f87-2f4f9f921c06`** (or similar GUID):
   - ❌ This is the Container TYPE ID
   - ❌ Cannot upload files to this
   - ✅ Need to create container INSTANCES first

2. **If it starts with `b!`** (like `b!21yLRdWE...`):
   - ✅ This is a Container INSTANCE ID
   - ✅ Can upload files to this
   - ⚠️ But it still returned "Item not found" - why?

---

## Scenario 1: Using Container Type ID (Most Likely)

### The Problem

**PCF Control sends**: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`

**API tries**: `PUT /drives/8a6ce34c-6055-4681-8f87-2f4f9f921c06/root:/{filename}:/content`

**Graph API says**: ❌ "Item not found" (404)
- Because `8a6ce34c...` is NOT a drive
- It's a Container Type, not a Container Instance

### The Solution

**Need to create container instances**:

```http
POST https://graph.microsoft.com/beta/storage/fileStorage/containers
Content-Type: application/json
Authorization: Bearer {Token B with FileStorageContainer.Selected}

{
  "displayName": "Document XYZ Container",
  "description": "Container for document record ABC-123",
  "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
}
```

**Response** (this is what you upload to):
```json
{
  "id": "b!a1b2c3d4e5f6...",  // Container INSTANCE ID - use THIS for uploads
  "displayName": "Document XYZ Container",
  "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
  "createdDateTime": "2025-10-16T...",
  ...
}
```

**Then update Dataverse**: Store `b!a1b2c3d4e5f6...` in the container ID field (NOT the type ID)

---

## Scenario 2: Using Container Instance ID

### If Container ID Starts with `b!`

**Then the issue might be**:
1. ✅ Container instance WAS created
2. ❌ But it was deleted or doesn't exist anymore
3. ❌ OR the ID is from a different tenant
4. ❌ OR the ID is from a test/dev environment that no longer exists

### How to Verify

**Test if the container exists**:
```bash
# Get a token
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

# Try to get the container (replace with actual ID from form)
curl -H "Authorization: Bearer $TOKEN" \
  "https://graph.microsoft.com/beta/drives/{CONTAINER_ID_FROM_FORM}"
```

**Expected**:
- ✅ 200 OK = Container exists (different problem then)
- ❌ 404 Not Found = Container doesn't exist (need to create it)

---

## What We Need from You

### Please check the Dataverse form and tell us:

1. **What is the exact container ID value in the form field?**
   - Is it `8a6ce34c-6055-4681-8f87-2f4f9f921c06`?
   - Or does it start with `b!`?

2. **Where did that value come from?**
   - Was it manually entered?
   - Auto-populated from configuration?
   - Created by some automation?

3. **Is there a "sprk_container" table in Dataverse?**
   - Does it have any records?
   - What container IDs are stored there?

---

## Likely Root Cause

### My Best Guess

**You have been using the Container Type ID for uploads**, thinking it was a container instance ID.

**Why this seemed to work before** (if it ever did):
- It didn't actually work - always got 404
- OR you were testing with different code that created containers
- OR there's confusion between "container type" and "container instance"

**Why it's failing now**:
- The code is correct (uses the ID directly as drive ID)
- The authentication is correct (OBO works, token has permissions)
- **But the ID being passed is the TYPE, not an INSTANCE**

---

## The Fix (If This Is The Problem)

### Option 1: Create Container on Demand

**In the BFF API, before upload**:

```csharp
// Check if container ID looks like a Container Type ID (plain GUID)
if (Guid.TryParse(containerId, out _) && !containerId.StartsWith("b!"))
{
    // This is a Container Type ID, need to create an instance
    _logger.LogWarning("Container Type ID provided instead of instance: {ContainerTypeId}", containerId);

    // Create container instance
    var container = await CreateContainerAsync(containerId, documentName);
    containerId = container.Id; // Use the new instance ID

    // Save the instance ID back to Dataverse
    await UpdateDocumentContainerIdAsync(documentId, containerId);
}

// Now proceed with upload using actual container instance ID
```

### Option 2: Pre-Create Containers

**When entity is created in Dataverse**:
1. Trigger creates new SPE container instance
2. Store the instance ID in Dataverse
3. Upload uses the stored instance ID

---

## Next Steps

1. **Tell us what container ID value is in the form**
2. **We'll verify if it's a type or instance**
3. **Then we'll know exactly what needs to be fixed**

---

**Document Created**: 2025-10-16 07:00 AM
**Status**: Waiting for user to confirm which container ID is being used
**Expected Answer**: Probably Container Type ID (`8a6ce34c...`), which explains the 404 error
