# SPE File Viewer - Solution Packaging Instructions

## Current Status

The PCF control has been successfully built with version 1.0.2 and includes the array extraction fix.

The control bundle is located at:
- `c:\code_files\spaarke\src\controls\SpeFileViewer\out\controls\SpeFileViewer\bundle.js`

## Issue

MSBuild packaging is failing due to .NET Framework reference assembly issues on this build environment.

## Solution: Deploy Using PAC PCF Push

Since the control is already built, you can deploy it directly to your Dataverse environment without creating a solution zip:

```bash
# Navigate to control directory
cd c:\code_files\spaarke\src\controls\SpeFileViewer

# Ensure you're authenticated to the correct environment
pac auth list

# Push the control directly (this will create/update the solution in your environment)
pac pcf push --publisher-prefix spaarke
```

This command will:
1. Read the already-built control from `out/controls`
2. Create or update the `SpeFileViewerSolution` in your environment
3. Automatically increment the version
4. Publish the control

## Alternative: Manual Import (If pac pcf push doesn't work)

If you need a solution zip file, you can create one manually using Solution Packager:

1. Export an existing empty solution from your environment that contains the SPE File Viewer control
2. Use `pac solution pack` to repackage it with the updated control

## Changes in Version 1.0.2

- **Array Extraction Fix**: The control now correctly extracts SharePoint Drive Item IDs from single-element arrays returned by Dataverse
- When binding to `sprk_graphitemid` or `sprk_driveitemid` fields, the control will now properly extract the ID value even if Dataverse returns it as `['01LBYCMX76QPLGITR47BB355T4G2CVDL2B']` instead of a plain string

## Testing After Deployment

1. **Leave the control's Document ID property UNBOUND** - The control will automatically use the form's record ID
2. Open a document record that has a valid SharePoint file
3. Check the browser console - you should see:
   ```
   [SpeFileViewer] Using form record ID: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
   [BffClient] GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5/preview-url
   [BffClient] Preview URL acquired for document: [filename]
   ```
4. The file preview should render in the control

**Note:** The document ID must be a GUID format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx), NOT a SharePoint Item ID format (01LBYCMX...).
