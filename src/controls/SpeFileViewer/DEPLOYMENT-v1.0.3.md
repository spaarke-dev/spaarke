# SPE File Viewer v1.0.3 - Deployment Instructions

## Summary of Changes

**Version:** 1.0.3
**Fix:** Document ID property changed from bound field to optional input

### What Changed

The `Document ID` property has been changed from a **bound field** (requires binding to a Dataverse field) to an **optional input** (can be left blank).

**Problem with v1.0.2:**
- `usage="bound"` required binding the Document ID to a Dataverse field
- The `sprk_documentid` field (Unique Identifier) wasn't available in the dropdown
- Binding to `sprk_graphitemid` sent the wrong ID (SharePoint Item ID instead of Document GUID)
- User workaround required binding to an empty field

**Solution in v1.0.3:**
- `usage="input"` allows leaving Document ID blank
- When blank, the control automatically uses the form's record ID (Dataverse Document GUID)
- This is the correct ID that the BFF API expects

**Files Changed:**
- [ControlManifest.Input.xml](SpeFileViewer/ControlManifest.Input.xml#L19-L20) - Changed `usage="bound"` to `usage="input"`, removed type-group
- [index.ts:114-134](SpeFileViewer/index.ts#L114-L134) - Simplified `extractDocumentId()` method for string input

## How the Architecture Works

```
1. PCF Control → Sends Dataverse Document GUID (e.g., ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5)
2. BFF API → Queries Dataverse using the GUID
3. BFF API → Retrieves sprk_graphdriveid and sprk_graphitemid from Document record
4. BFF API → Calls Graph API with SharePoint pointers
5. BFF API → Returns preview URL to PCF
6. PCF → Displays preview
```

**No BFF code changes needed** - The BFF was always correctly implemented!

## Deployment Steps

### Option A: Already Deployed

If you followed this conversation, the control has already been imported to your environment via:

```bash
pac solution import --path ".../PowerAppsToolsTemp_sprk.zip" --force-overwrite --publish-changes
```

**Status:** ✅ Deployed and published

### Option B: Manual Import via Power Apps

1. **Download Solution Package** (if deploying to another environment)
   - Location: `c:\code_files\spaarke\src\controls\SpeFileViewer\obj\PowerAppsToolsTemp_sprk\bin\Debug\PowerAppsToolsTemp_sprk.zip`
   - Size: ~195 KB
   - Type: Unmanaged solution

2. **Navigate to Power Apps**
   - Go to https://make.powerapps.com
   - Select your environment (SPAARKE DEV 1)

3. **Import Solution**
   - Click **Solutions** in the left nav
   - Click **Import** > **Import solution**
   - Click **Browse** and select `PowerAppsToolsTemp_sprk.zip`
   - Click **Next**

4. **Import Options**
   - Select **Upgrade** (replaces v1.0.2 with v1.0.3)
   - Click **Import**

5. **Publish Customizations**
   - After import completes, click **Publish all customizations**
   - Wait for publish to complete (~1-2 minutes)

## Post-Deployment Configuration

### 1. Update Control Configuration

Open any Document form that has the SPE File Viewer control:

1. **Edit the form** in Power Apps
2. **Select the SPE File Viewer control**
3. **Update properties**:

   - **Document ID:** ⚠️ **Leave BLANK** (the control will use the form record ID automatically)
   - **BFF API URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
   - **File Viewer Client App ID:** `b36e9b91-ee7d-46e6-9f6a-376871cc9d54`
   - **BFF Application ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - **Tenant ID:** Your Azure AD tenant ID

4. **Save and Publish** the form

### Key Difference from v1.0.2

| Version | Document ID Configuration |
|---------|---------------------------|
| v1.0.2  | **Bound to field** (required binding to `sprk_graphitemid` - WRONG!) |
| v1.0.3  | **Input property** (leave blank to use form record ID - CORRECT!) |

## Testing

### 1. Hard Refresh Browser

After publishing, ensure you clear the browser cache:
- Chrome/Edge: Ctrl+Shift+R (Windows) or Cmd+Shift+R (Mac)
- Or: DevTools (F12) > Right-click refresh > Empty Cache and Hard Reload

### 2. Open a Document Record

1. Navigate to a Document record that has a SharePoint file
2. Verify the record has values in:
   - `sprk_graphitemid` (SharePoint Item ID, e.g., `01LBYCMX...`)
   - `sprk_graphdriveid` (SharePoint Drive ID, e.g., `b!...`)

### 3. Check Console Output

Open browser Developer Tools (F12) and check the Console tab:

**✅ Expected Output (v1.0.3):**
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

**Key indicators:**
- ✅ `Using form record ID:` message with GUID format
- ✅ Document ID is in GUID format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`
- ✅ Preview loads successfully

**❌ Incorrect Output (if you manually enter a SharePoint Item ID):**
```
[SpeFileViewer] Using configured document ID: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B
[BffClient] GET .../api/documents/01LBYCMX76QPLGITR47BB355T4G2CVDL2B/preview-url
[BffClient] Error: Document ID must be a valid GUID
```

### 4. Verify Preview Displays

The SharePoint file preview should display in the control. If it doesn't:
- Check that the Document record has valid `sprk_graphitemid` and `sprk_graphdriveid` values
- Check the BFF API logs using the Correlation ID from the console
- Verify Azure AD app permissions are configured correctly

## Troubleshooting

### Issue: Control Still Shows "Document ID" as Required Field

**Cause:** Browser cached the old control definition

**Solution:**
1. Remove the control from the form
2. Save and publish the form
3. Hard refresh browser (Ctrl+Shift+R)
4. Re-add the control to the form
5. Configure properties (leave Document ID blank)
6. Save and publish

### Issue: Still Getting 404 Document Not Found

**Possible Causes:**
1. **Document ID is not blank** - Make sure you didn't manually enter a value
2. **Form record ID is not available** - Control must be on a Document form
3. **Document missing SharePoint metadata** - Check `sprk_graphitemid` and `sprk_graphdriveid` fields

**Debug Steps:**
1. Check console output - should show `[SpeFileViewer] Using form record ID: [GUID]`
2. Verify the GUID format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)
3. Check that `sprk_graphitemid` and `sprk_graphdriveid` have values in Dataverse
4. Verify the document exists in SharePoint
5. Check BFF API logs for the Correlation ID

### Issue: Authentication Popup "Having Trouble Logging You In"

**Solution:** Verify the File Viewer Client App ID (`b36e9b91-ee7d-46e6-9f6a-376871cc9d54`) has:
1. API permissions to `spe.bff.api` with `SDAP.Access` scope
2. Admin consent granted for those permissions
3. Redirect URI configured for your Dataverse environment

## Version History

- **v1.0.3** (Nov 24, 2025) - Changed Document ID from bound field to optional input property
- **v1.0.2** (Nov 24, 2025) - Fixed array extraction for Drive Item IDs
- **v1.0.1** - Added File Viewer Client App ID configuration
- **v1.0.0** - Initial release with two-app MSAL architecture

## What's Next

Once you confirm the control works correctly:

1. ✅ Verify preview loads with correct console output
2. ✅ Test on multiple Document records
3. ✅ Document any environment-specific notes
4. Consider deploying to other environments (UAT, Production)

## Related Documentation

- [FIX-404-ERROR.md](FIX-404-ERROR.md) - Root cause analysis of the 404 error
- [KNOWN-ISSUES.md](KNOWN-ISSUES.md) - Known issues and workarounds
- [PACKAGE-SOLUTION.md](PACKAGE-SOLUTION.md) - Solution packaging instructions

---

**Last Updated:** November 24, 2025
**Status:** Deployed and ready for testing
