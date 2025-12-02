# Update Plugin Assembly in Dataverse

## What Changed
The plugin has been refactored to eliminate Azure.Identity dependencies:
- **Before**: Used Azure.Identity (complex dependency tree, ILMerge required)
- **After**: Uses simple HTTP OAuth2 calls (only needs Newtonsoft.Json + System.Net.Http)

This fixes the `Could not load file or assembly 'Azure.Identity'` error.

## Steps to Update Plugin Assembly

### Option 1: Using Plugin Registration Tool (Recommended)

1. **Open Plugin Registration Tool**
   - You should already have this open

2. **Find the Existing Assembly**
   - Expand the connection to your Dataverse environment
   - Find "Spaarke.Dataverse.CustomApiProxy" in the list

3. **Update Assembly**
   - Right-click on **"Spaarke.Dataverse.CustomApiProxy"**
   - Select **"Update"**
   - Browse to the new DLL:
     ```
     c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
     ```
   - Click **"Register Selected Plugins"** or **"Update"**

4. **Verify Update**
   - The assembly should update successfully
   - The plugin types (GetFilePreviewUrlPlugin) should still be there
   - The Custom API link should remain intact

5. **Publish Customizations**
   - In PRT, there may be a "Publish All Customizations" option
   - OR use Power Apps: Settings → Advanced Settings → Customizations → Publish All Customizations

### Option 2: Using XrmToolBox Plugin Registration

1. Open XrmToolBox → Plugin Registration
2. Find "Spaarke.Dataverse.CustomApiProxy" assembly
3. Click "Update" button
4. Select the new DLL (same path as above)
5. Complete update and publish

### Option 3: Delete and Re-register (if Update fails)

If the Update option doesn't work:

1. **Delete Old Assembly**
   - Right-click "Spaarke.Dataverse.CustomApiProxy" → Delete
   - Confirm deletion

2. **Re-register Assembly**
   - Click "Register" → "Register New Assembly"
   - Select DLL: `c:\code_files\spaarke\...\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll`
   - Select plugin type: `GetFilePreviewUrlPlugin`
   - Isolation Mode: Sandbox
   - Location: Database
   - Click "Register Selected Plugins"

3. **Re-link Custom API**
   - Open XrmToolBox → Custom API Manager
   - Find `sprk_GetFilePreviewUrl`
   - Set Plugin Type to: `Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin`
   - Save

4. **Publish**
   - Publish all customizations

## After Update

Once the assembly is updated and customizations are published:

1. **Run the test script again**:
   ```powershell
   cd c:\code_files\spaarke\dev\projects\spe-file-viewer
   .\test-customapi.ps1
   ```

2. **Expected Result**: ✅ Success with all 6 output parameters returned

## What's in the New Plugin

The updated plugin now uses `SimpleAuthHelper.cs` which:
- Makes direct HTTP POST to `https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`
- Sends client_id, client_secret, scope, grant_type
- Parses JSON response to get access_token
- **No Azure.Identity, Azure.Core, or Microsoft.Identity.Client dependencies**
- Only uses: Microsoft.CrmSdk, Newtonsoft.Json, System.Net.Http (all standard for Dataverse)

## File Location

**New Plugin DLL**:
```
c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
```

File size should be much smaller now (~ 200-300 KB instead of several MB).
