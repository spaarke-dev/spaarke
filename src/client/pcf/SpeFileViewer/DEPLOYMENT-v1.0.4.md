# SPE File Viewer v1.0.4 - Deployment Instructions

## Summary of Changes

**Version:** 1.0.4
**Fix:** Control type changed from field control to standalone component

### What Changed

The control has been changed from a **field control** to a **standalone/virtual component**:

**Problem with v1.0.3:**
- `control-type="standard"` made it a field-level control
- Users had to select a field and change its control type
- This didn't make sense for a file viewer that just needs to display the current document

**Solution in v1.0.4:**
- `control-type="virtual"` makes it a standalone component
- Appears in the **+ Component** menu when editing forms
- Can be placed anywhere on the form
- Automatically uses the form's record ID to show the document preview

**Files Changed:**
- [ControlManifest.Input.xml:3](SpeFileViewer/ControlManifest.Input.xml#L3) - Changed `control-type="standard"` to `control-type="virtual"`, version bumped to 1.0.4

## How It Works Now

```
1. User edits a Document form in Power Apps
2. User clicks "+ Component" in the form editor
3. User selects "Spaarke.SpeFileViewer" from the component list
4. Component is added to the form
5. When viewing a document, the component automatically:
   - Gets the form's record ID (Dataverse Document GUID)
   - Authenticates with MSAL
   - Calls BFF API to get preview URL
   - Displays SharePoint file preview
```

## Deployment Steps

### Option A: Already Deployed

If you're continuing from the previous session, the control has already been imported:

```bash
pac solution import --path ".../PowerAppsToolsTemp_sprk.zip" --force-overwrite --publish-changes
```

**Status:** ✅ Deployed and published

### Option B: Manual Import via Power Apps

1. **Download Solution Package**
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
   - Select **Upgrade** (replaces v1.0.3 with v1.0.4)
   - Click **Import**

5. **Publish Customizations**
   - After import completes, click **Publish all customizations**
   - Wait for publish to complete (~1-2 minutes)

## Post-Deployment Configuration

### 1. Add Control to Form

Open any Document form and add the SPE File Viewer component:

1. **Edit the form** in Power Apps
2. **Click "+ Component"** in the left toolbar
3. **Select "Spaarke.SpeFileViewer"** from the component list
4. **Drag and drop** the component onto the form where you want it
5. **Configure properties**:

   - **Document ID:** ⚠️ **Leave BLANK** (the control uses the form record ID automatically)
   - **BFF API URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
   - **File Viewer Client App ID:** `b36e9b91-ee7d-46e6-9f6a-376871cc9d54`
   - **BFF Application ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
   - **Tenant ID:** Your Azure AD tenant ID

6. **Save and Publish** the form

### Key Difference from v1.0.3

| Version | Control Type | How to Add |
|---------|--------------|------------|
| v1.0.3  | **Field control** (`control-type="standard"`) | Select field → Properties → Controls → Add control |
| v1.0.4  | **Standalone component** (`control-type="virtual"`) | + Component → Select SpeFileViewer |

### Removing v1.0.3 Control (if already added)

If you previously added the v1.0.3 field control:

1. **Remove the old control** from the field's Controls tab
2. **Save and publish** the form
3. **Hard refresh browser** (Ctrl+Shift+R)
4. **Add the new v1.0.4 component** using "+ Component"

## Testing

### 1. Hard Refresh Browser

After publishing, ensure you clear the browser cache:
- Chrome/Edge: Ctrl+Shift+R (Windows) or Cmd+Shift+R (Mac)
- Or: DevTools (F12) > Right-click refresh > Empty Cache and Hard Reload

### 2. Verify Component Appears in Component Menu

1. Edit any form
2. Click **+ Component** in the left toolbar
3. Verify **Spaarke.SpeFileViewer** appears in the list
4. **Expected:** Component shows up with version 1.0.4

### 3. Open a Document Record

1. Navigate to a Document record that has a SharePoint file
2. Verify the record has values in:
   - `sprk_graphitemid` (SharePoint Item ID, e.g., `01LBYCMX...`)
   - `sprk_graphdriveid` (SharePoint Drive ID, e.g., `b!...`)

### 4. Check Console Output

Open browser Developer Tools (F12) and check the Console tab:

**✅ Expected Output (v1.0.4):**
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

### 5. Verify Preview Displays

The SharePoint file preview should display in the component. If it doesn't:
- Check that the Document record has valid `sprk_graphitemid` and `sprk_graphdriveid` values
- Check the BFF API logs using the Correlation ID from the console
- Verify Azure AD app permissions are configured correctly

## Troubleshooting

### Issue: Component Doesn't Appear in + Component Menu

**Cause:** Browser cached the old control definition or solution wasn't published

**Solution:**
1. Hard refresh browser (Ctrl+Shift+R)
2. Verify solution was published (make.powerapps.com > Solutions > Check status)
3. Re-import solution if needed
4. Clear Power Apps cache: Sign out and sign back in

### Issue: Still Shows as Field Control

**Cause:** Old v1.0.3 control is still cached

**Solution:**
1. Remove the control completely from the form
2. Delete solution and re-import v1.0.4
3. Hard refresh browser
4. Re-add using "+ Component" menu

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

- **v1.0.4** (Nov 24, 2025) - Changed from field control to standalone component
- **v1.0.3** (Nov 24, 2025) - Changed Document ID from bound field to optional input property
- **v1.0.2** (Nov 24, 2025) - Fixed array extraction for Drive Item IDs
- **v1.0.1** - Added File Viewer Client App ID configuration
- **v1.0.0** - Initial release with two-app MSAL architecture

## What's Next

Once you confirm the control works correctly:

1. ✅ Verify component appears in "+ Component" menu
2. ✅ Add component to a Document form
3. ✅ Verify preview loads with correct console output
4. ✅ Test on multiple Document records
5. ✅ Document any environment-specific notes
6. Consider deploying to other environments (UAT, Production)

## Related Documentation

- [DEPLOYMENT-v1.0.3.md](DEPLOYMENT-v1.0.3.md) - Previous version (field control)
- [FIX-404-ERROR.md](FIX-404-ERROR.md) - Root cause analysis of the 404 error
- [KNOWN-ISSUES.md](KNOWN-ISSUES.md) - Known issues and workarounds
- [PACKAGE-SOLUTION.md](PACKAGE-SOLUTION.md) - Solution packaging instructions

---

**Last Updated:** November 24, 2025
**Status:** Deployed and ready for testing
