# SPE File Viewer v1.0.2 - Deployment Instructions

## Summary of Changes

**Version:** 1.0.2
**Fix:** Array extraction for SharePoint Drive Item IDs

### What Was Fixed

The control now correctly handles single-element arrays returned by Dataverse fields. When binding to `sprk_graphitemid` or `sprk_driveitemid`, the control will extract the ID value even if it's returned as:
```javascript
['01LBYCMX76QPLGITR47BB355T4G2CVDL2B']  // Array with single element
```

Instead of:
```javascript
'01LBYCMX76QPLGITR47BB355T4G2CVDL2B'   // Plain string
```

**Files Changed:**
- [index.ts:127-135](../SpeFileViewer/index.ts#L127-L135) - Updated `extractDocumentId()` method to handle array values
- [ControlManifest.Input.xml:3](../SpeFileViewer/ControlManifest.Input.xml#L3) - Version bumped to 1.0.2

## Solution Package Location

```
c:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewerSolution\bin\Release\SpeFileViewerSolution_v1.0.2.zip
```

**Size:** 195 KB
**Type:** Unmanaged solution

## Deployment Steps

### Option A: Import via Power Apps Portal (Recommended)

1. **Navigate to Power Apps**
   - Go to https://make.powerapps.com
   - Select your environment (SPAARKE DEV 1)

2. **Import Solution**
   - Click **Solutions** in the left nav
   - Click **Import** > **Import solution**
   - Click **Browse** and select `SpeFileViewerSolution_v1.0.2.zip`
   - Click **Next**

3. **Import Options**
   - Select **Upgrade** (not Update)
   - This will replace the existing control with version 1.0.2
   - Click **Import**

4. **Publish Customizations**
   - After import completes, click **Publish all customizations**
   - Wait for publish to complete (~1-2 minutes)

5. **Refresh Form**
   - Navigate to any form that has the SPE File Viewer control
   - **Hard refresh** the browser (Ctrl+Shift+R or Cmd+Shift+R)
   - This ensures the new control version loads

### Option B: Import via PAC CLI

```bash
# Navigate to solution directory
cd c:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewerSolution/bin/Release

# Ensure you're authenticated to correct environment
pac auth list

# Import solution (use --force-overwrite for upgrade)
pac solution import --path SpeFileViewerSolution_v1.0.2.zip --force-overwrite --publish-changes

# Output should show:
# Solution 'SpeFileViewerSolution' imported successfully.
```

## Post-Deployment Testing

### 1. Verify Control Configuration

Open a Document form and check the SPE File Viewer control configuration:

- **Document ID field:** ⚠️ **Leave UNBOUND (Select an option)** - The control will automatically use the form's record ID
- **BFF API URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
- **File Viewer Client App ID:** `b36e9b91-ee7d-46e6-9f6a-376871cc9d54`
- **BFF Application ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Tenant ID:** Your Azure AD tenant ID

**Important:** Leave the Document ID field unbound. The control will automatically use the form's record ID (Dataverse Document GUID), which the BFF uses to query Dataverse for SharePoint metadata.

### 2. Test with Valid Document

1. Open a Document record that has a SharePoint file
2. Verify the record has a value in the `sprk_graphitemid` field (e.g., `01LBYCMX76QPLGITR47BB355T4G2CVDL2B`)
3. Open browser Developer Tools (F12)
4. Check Console tab for expected logs:

```
[SpeFileViewer] Initializing control...
[SpeFileViewer] MSAL initialized. Scope: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access
[SpeFileViewer] Access token acquired
[SpeFileViewer] Using form record ID: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
[SpeFileViewer] Rendering preview for document: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
[BffClient] GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/preview-url
[BffClient] Preview URL acquired for document: [filename.docx]
[FilePreview] Preview loaded successfully
```

5. **Expected Result:** The SharePoint file preview should display in the control

### 3. Verify Form Record ID Usage

The key behavior to verify is that the control uses the **form record ID** (Dataverse Document GUID):

✅ **Correct console output:**
```
[SpeFileViewer] Using form record ID: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
[SpeFileViewer] Rendering preview for document: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
```

❌ **Incorrect (if field was bound to sprk_graphitemid):**
```
[SpeFileViewer] Extracted document ID from array: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
[SpeFileViewer] Rendering preview for document: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
```

**Note:** The document ID must be a **GUID format** (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx), NOT a SharePoint Item ID format (01LBYCMX...).

## Troubleshooting

### Issue: Control Still Shows Old Version

**Solution:** Clear browser cache and hard refresh
```
- Chrome/Edge: Ctrl+Shift+R (Windows) or Cmd+Shift+R (Mac)
- Or: Open DevTools (F12) > Right-click refresh button > Empty Cache and Hard Reload
```

### Issue: "File Viewer Client App ID" Field Not Showing

**Cause:** Control configuration UI is cached
**Solution:**
1. Remove the control from the form
2. Save and publish the form
3. Re-add the control
4. The new configuration field should appear

### Issue: Still Getting 404 Document Not Found

**Most Common Cause:** Document ID field is bound to the wrong field!

**Solution:** The Document ID field should be **left UNBOUND**. The control will automatically use the form's record ID.

**Debug Steps:**
1. ✅ **Verify field is UNBOUND** - Open form editor, select control, check that "Document ID" shows "Select an option"
2. ✅ **Check console output** - Should show `[SpeFileViewer] Using form record ID: [GUID]` with a proper GUID format
3. ✅ **Verify GUID format** - Document ID should be `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`, NOT `01LBYCMX...`
4. Check the Document record to confirm `sprk_graphitemid` and `sprk_graphdriveid` fields have values
5. Verify the document exists in SharePoint
6. Check BFF API logs for the correlation ID shown in error

**If the console shows:**
```
❌ [SpeFileViewer] Extracted document ID from array: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
```

Then the Document ID field is incorrectly bound to `sprk_graphitemid`. Unbind it and republish the form.

### Issue: Authentication Popup "Having Trouble Logging You In"

**Solution:** Verify the File Viewer Client App ID (`b36e9b91-ee7d-46e6-9f6a-376871cc9d54`) has:
1. API permissions to `spe.bff.api` with `SDAP.Access` scope
2. Admin consent granted for those permissions
3. Redirect URI configured for your Dataverse environment

## Version History

- **v1.0.2** (Nov 24, 2025) - Fixed array extraction for Drive Item IDs
- **v1.0.1** - Added File Viewer Client App ID configuration
- **v1.0.0** - Initial release with two-app MSAL architecture

## Next Steps After Testing

Once you confirm the control works correctly:

1. Mark this deployment as successful
2. Update deployment documentation with any environment-specific notes
3. Consider deploying to other environments (UAT, Production)
4. Update ADR documentation with the array extraction issue and solution

## Support

If you encounter issues:
1. Check the browser console logs
2. Note the Correlation ID from the error message
3. Check BFF API logs using that Correlation ID
4. Verify Azure AD app registrations and permissions
