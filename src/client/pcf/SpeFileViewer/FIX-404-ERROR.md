# Fix for 404 "Document Not Found" Error

## Root Cause

The 404 error is caused by **incorrect field binding** in the PCF control configuration.

### What Was Wrong

The deployment guide incorrectly instructed users to bind the **Document ID** property to `sprk_graphitemid`:

```
❌ INCORRECT: Document ID field bound to sprk_graphitemid
```

When bound to `sprk_graphitemid`, the PCF extracts the **SharePoint Item ID** (e.g., `01LBYCMX76QPLGITR47BB355T4G2CVDL2B`) and sends it to the BFF API.

However, the BFF API expects the **Dataverse Document GUID** (e.g., `ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5`), NOT the SharePoint Item ID.

### How the BFF Works (Already Correct!)

The BFF API is **already implemented correctly**:

1. **Receives**: Dataverse Document GUID (e.g., `ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5`)
2. **Queries Dataverse**: Retrieves the Document record using that GUID
3. **Extracts SharePoint Pointers**: Gets `sprk_graphdriveid` and `sprk_graphitemid` from the record
4. **Calls Graph API**: Uses the SharePoint pointers to get the preview URL

See [FileAccessEndpoints.cs:58-86](../../api/Spe.Bff.Api/Api/FileAccessEndpoints.cs#L58-L86):

```csharp
// Step 1: Get Document record from Dataverse
var document = await dataverseService.GetDocumentAsync(documentId); // ← Expects Document GUID

// Step 2: Validate SPE metadata exists
if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
{
    return ProblemDetailsHelper.ValidationError("Document missing SPE metadata");
}

// Step 3: Get preview URL from SPE via Graph API
var previewResult = await speFileStore.GetPreviewUrlAsync(
    document.GraphDriveId,  // ← sprk_graphdriveid
    document.GraphItemId,   // ← sprk_graphitemid
    correlationId);
```

**The BFF code doesn't need any changes!**

## The Fix

### ✅ Leave Document ID Field UNBOUND

The control has a built-in fallback mechanism that automatically uses the form's record ID when no field is bound:

```typescript
// If no field is bound (or empty), use the current form record ID
const recordId = (context.mode as any).contextInfo?.entityId;
if (recordId) {
    console.log('[SpeFileViewer] Using form record ID:', recordId);
    return recordId;  // Returns the Document GUID
}
```

See [index.ts:145-151](index.ts#L145-L151)

### Configuration Steps

1. **Open Form Editor** in Power Apps
2. **Select the SPE File Viewer control**
3. **In Control Properties**:
   - **Document ID field**: ⚠️ **Leave as "Select an option" (unbound)**
   - BFF API URL: `https://spe-api-dev-67e2xz.azurewebsites.net`
   - File Viewer Client App ID: `b36e9b91-ee7d-46e6-9f6a-376871cc9d54`
   - BFF Application ID: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - Tenant ID: Your Azure AD tenant ID
4. **Save and Publish** the form

### Why This Works

When the control is placed on a Document form:
- The form context contains the **record ID** (Dataverse GUID)
- The control extracts this automatically via `context.mode.contextInfo.entityId`
- This GUID is sent to the BFF API
- The BFF queries Dataverse using the GUID to get SharePoint pointers
- Everything works correctly!

### Expected Console Output

After applying the fix, you should see:

```
[SpeFileViewer] Initializing control...
[SpeFileViewer] MSAL initialized
[SpeFileViewer] Using form record ID: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
[SpeFileViewer] Rendering preview for document: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
[BffClient] GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/preview-url
[BffClient] Preview URL acquired for document: [filename.docx]
[FilePreview] Preview loaded successfully
```

**Key difference**: The document ID is now a proper GUID format (`ad1b0c34-...`) instead of SharePoint Item ID format (`01LBYCMX...`)

## Why the Incorrect Binding Happened

The `sprk_driveitemid` field was found to be empty (see [KNOWN-ISSUES.md](KNOWN-ISSUES.md)), so the deployment guide recommended binding to `sprk_graphitemid` instead. However, this was incorrect because:

1. The BFF doesn't need the PCF to send SharePoint IDs
2. The BFF queries Dataverse to get those IDs itself
3. The PCF only needs to identify **which Document record** to retrieve

## Testing the Fix

1. **Remove the field binding** from the control
2. **Hard refresh** the browser (Ctrl+Shift+R)
3. **Open a Document record** that has a file
4. **Check the console** - you should see the form record ID being used
5. **Verify the preview displays**

## No Code Changes Required

- ✅ PCF control v1.0.2 is correct (already has fallback logic)
- ✅ BFF API is correct (already queries Dataverse for SharePoint pointers)
- ❌ Only deployment documentation needed correction

## Files to Update

1. [DEPLOYMENT-v1.0.2.md](DEPLOYMENT-v1.0.2.md) - Line 83: Change binding instruction
2. [PACKAGE-SOLUTION.md](PACKAGE-SOLUTION.md) - Line 49: Update testing instructions

---

**Last Updated:** November 24, 2025
**Status:** Fix identified, no code changes required
