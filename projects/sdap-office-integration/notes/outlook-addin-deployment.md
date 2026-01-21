# Task 057: Outlook Add-in Deployment to M365 Admin Center

> **Status**: Ready for Manual Execution
> **Type**: Office Add-in Deployment (Unified Manifest)
> **Location**: Microsoft 365 Admin Center + Azure Static Hosting
> **Last Updated**: 2026-01-20

---

## Overview

Deploy the Spaarke Outlook add-in to the Microsoft 365 admin center for the development tenant. The add-in uses the unified JSON manifest format and provides "Save to Spaarke" and "Share from Spaarke" functionality.

## Prerequisites

Before deployment, ensure these tasks are complete:

- [x] Task 040: NAA authentication service implemented
- [x] Task 041: Dialog API auth fallback implemented
- [x] Task 042-044: Host adapters created (Outlook adapter)
- [x] Task 045: API client service created
- [x] Task 046-055: Task pane UI components implemented
- [x] Task 056: Unit tests created and passing
- [x] Azure AD app registration exists (c1258e2d-1688-49d2-ac99-a7485ebd9995)
- [x] BFF API deployed to Azure (spe-api-dev-67e2xz.azurewebsites.net)

---

## Add-in Information

| Property | Value |
|----------|-------|
| **Add-in Name** | Spaarke |
| **Full Name** | Spaarke Document Management for Outlook |
| **Version** | 1.0.0 |
| **Client ID** | c1258e2d-1688-49d2-ac99-a7485ebd9995 |
| **Manifest Type** | Unified JSON Manifest |
| **Manifest Location** | src/client/office-addins/outlook/manifest.json |

## Environment Information

| Environment | Value |
|-------------|-------|
| **Target Tenant** | a221a95e-6abc-4434-aecc-e48338a1b2f2 |
| **M365 Admin Center** | https://admin.microsoft.com |
| **BFF API URL** | https://spe-api-dev-67e2xz.azurewebsites.net |
| **Static Hosting URL** | TBD (Azure Static Web App or Blob Storage) |

---

## Deployment Steps

### Step 1: Build Production Version

```powershell
# Navigate to the office-addins directory
cd src/client/office-addins

# Clean previous build
npm run clean

# Build production bundle
npm run build

# Verify build output
Get-ChildItem dist/outlook
```

**Expected output**:
```
dist/
├── outlook/
│   ├── taskpane.html
│   ├── taskpane.bundle.js
│   ├── commands.html
│   ├── commands.bundle.js
│   └── manifest.json
├── assets/
│   ├── icon-16.png
│   ├── icon-32.png
│   ├── icon-80.png
│   └── ...
└── vendors.bundle.js
```

### Step 2: Deploy Static Files to Azure

**Option A: Azure Static Web Apps (Recommended)**

1. Create a new Azure Static Web App in the Azure Portal:
   ```powershell
   az staticwebapp create `
     --name spe-office-addins-dev `
     --resource-group spe-infrastructure-westus2 `
     --location westus2 `
     --source https://github.com/spaarke/spaarke-wt-SDAP-outlook-office-add-in `
     --branch work/SDAP-outlook-office-add-in `
     --app-location "src/client/office-addins/dist" `
     --output-location ""
   ```

2. Or deploy manually:
   ```powershell
   # Build and deploy
   npm run build
   az staticwebapp upload `
     --name spe-office-addins-dev `
     --resource-group spe-infrastructure-westus2 `
     --source dist
   ```

**Option B: Azure Blob Storage with Static Website**

1. Enable static website on existing storage account:
   ```powershell
   az storage blob service-properties update `
     --account-name spedevstorageaccount `
     --static-website `
     --index-document taskpane.html
   ```

2. Upload files:
   ```powershell
   az storage blob upload-batch `
     --account-name spedevstorageaccount `
     --source dist `
     --destination '$web'
   ```

**Record the hosting URL** (e.g., `https://spe-office-addins-dev.azurestaticapps.net`)

### Step 3: Update Manifest with Production URLs

Edit `src/client/office-addins/outlook/manifest.json` to replace localhost URLs:

```json
{
  "runtimes": [
    {
      "id": "TaskpaneRuntime",
      "code": {
        "page": "https://{HOSTING_URL}/outlook/taskpane.html"
      }
    },
    {
      "id": "CommandRuntime",
      "code": {
        "page": "https://{HOSTING_URL}/outlook/commands.html",
        "script": "https://{HOSTING_URL}/outlook/commands.js"
      }
    }
  ],
  "ribbons": [
    {
      "groups": [
        {
          "icons": [
            { "size": 16, "url": "https://{HOSTING_URL}/assets/icon-16.png" },
            { "size": 32, "url": "https://{HOSTING_URL}/assets/icon-32.png" },
            { "size": 80, "url": "https://{HOSTING_URL}/assets/icon-80.png" }
          ],
          "controls": [
            {
              "icons": [
                { "size": 16, "url": "https://{HOSTING_URL}/assets/save-16.png" },
                { "size": 32, "url": "https://{HOSTING_URL}/assets/save-32.png" },
                { "size": 80, "url": "https://{HOSTING_URL}/assets/save-80.png" }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

**Important**: Replace ALL occurrences of `https://localhost:3000` with the production hosting URL.

### Step 4: Access Microsoft 365 Admin Center

1. Navigate to: https://admin.microsoft.com
2. Sign in with a Global Administrator or Apps Administrator account
3. You should see the Microsoft 365 admin center dashboard

### Step 5: Navigate to Integrated Apps

1. In the left navigation, expand **Settings**
2. Click **Integrated apps**
3. You will see a list of apps deployed to your organization

![Navigation Path](https://docs.microsoft.com/path/to/screenshot)

### Step 6: Upload the Unified Manifest

1. Click **Upload custom apps** (or **Deploy App** button)
2. Select **Upload custom app (manifest)**
3. Choose **Office Add-in** as the app type
4. Click **Choose file** and select the updated `manifest.json` from:
   - `src/client/office-addins/dist/outlook/manifest.json`
5. Click **Upload**

**Validation**: The admin center will validate the manifest. If errors occur:
- Check JSON syntax
- Verify all URLs are HTTPS
- Verify icon URLs are accessible
- Verify the web application info matches your app registration

### Step 7: Configure Deployment Settings

After upload, configure the deployment:

1. **Users and groups**:
   - Select **Specific users/groups** for initial testing
   - Add your test users (e.g., developers, QA team)
   - Or select **Entire organization** for wider rollout

2. **Permissions**:
   - Review the permissions the add-in requests
   - NAA (Nested App Authentication) requires no special permissions

3. **Availability**:
   - Select **Available** (users can install) or **Fixed** (automatically installed)
   - For dev testing, start with **Available** to specific users

4. Click **Deploy**

### Step 8: Wait for Deployment Propagation

**Important**: Deployment can take up to 24 hours to propagate to all Outlook clients.

Typical propagation times:
- Outlook Web: 15-30 minutes
- New Outlook (Windows): 1-2 hours
- New Outlook (Mac): 1-2 hours

You can check deployment status in the admin center under **Integrated apps** > **Your app** > **Deployment status**.

### Step 9: Test in New Outlook (Windows)

1. Open New Outlook on Windows
2. Create or open an email in **Read mode**
3. Look for the **Spaarke** group in the ribbon
4. Click the **Save to Spaarke** button
5. Verify the task pane opens

**Test the following**:
- [ ] Task pane loads without errors
- [ ] Authentication completes (NAA or Dialog fallback)
- [ ] Entity search works
- [ ] Save flow completes
- [ ] Error handling displays correctly

### Step 10: Test in Outlook Web

1. Navigate to: https://outlook.office.com
2. Open an email in **Read mode**
3. Look for the **Spaarke** ribbon button (may be in "..." menu)
4. Click **Save to Spaarke**
5. Verify the task pane opens

**Test the following**:
- [ ] Task pane loads without errors
- [ ] NAA authentication works
- [ ] API calls succeed
- [ ] UI is responsive

### Step 11: Document Test Results

Record test results in this file (see Deployment Log section below).

### Step 12: Update TASK-INDEX.md

After successful deployment and testing, update the task status:
1. Edit `projects/sdap-office-integration/tasks/TASK-INDEX.md`
2. Change task 057 status from 🔲 to ✅

---

## Deployment Log

| Date | Version | Environment | Status | Tester | Notes |
|------|---------|-------------|--------|--------|-------|
| TBD | 1.0.0 | Dev | Pending | — | Initial deployment |

---

## Verification Checklist

- [ ] Production build succeeds without errors
- [ ] Static files deployed to Azure hosting
- [ ] Manifest updated with production URLs
- [ ] Manifest uploaded to M365 admin center
- [ ] Deployment configured for test users
- [ ] Add-in visible in M365 admin center
- [ ] Add-in loads in New Outlook (Windows)
- [ ] Add-in loads in Outlook Web
- [ ] Ribbon buttons appear correctly
- [ ] Task pane opens on button click
- [ ] Authentication completes successfully
- [ ] Save flow works end-to-end
- [ ] Share flow works end-to-end

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Add-in not visible | Propagation delay | Wait up to 24 hours, clear Outlook cache |
| Manifest validation error | Invalid JSON or URLs | Validate JSON syntax, ensure HTTPS URLs |
| Task pane blank | HTTPS mixed content | Ensure all resources use HTTPS |
| Auth fails with NAA | Client ID mismatch | Verify manifest webApplicationInfo.id matches app registration |
| Auth fails with Dialog | Redirect URI not registered | Add redirect URI to app registration |
| API calls fail | CORS or network | Check BFF API CORS settings, verify network |
| Icons not showing | URL incorrect | Verify icon URLs are accessible (test in browser) |

### Clear Outlook Cache (Windows)

If the add-in doesn't appear or shows old version:

```powershell
# Close Outlook first
Stop-Process -Name "olk" -Force

# Clear add-in cache
Remove-Item -Path "$env:LOCALAPPDATA\Microsoft\Outlook\16.0\Wef\*" -Recurse -Force
```

### Check Browser Console (Outlook Web)

1. Open Developer Tools (F12)
2. Navigate to Console tab
3. Look for errors related to the add-in
4. Check Network tab for failed requests

---

## Rollback Procedure

If deployment fails or causes issues:

1. Navigate to M365 admin center > Integrated apps
2. Find the Spaarke app
3. Click **Remove** to disable the add-in
4. Users will no longer see the add-in in Outlook

---

## Related Documentation

- [NAA Authentication Guide](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/enable-nested-app-authentication-in-your-add-in)
- [Unified Manifest Overview](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/unified-manifest-overview)
- [Deploy add-ins in the admin center](https://learn.microsoft.com/en-us/microsoft-365/admin/manage/manage-deployment-of-add-ins)
- [spec.md](../spec.md) - Project specification
- [001-azure-ad-app-registration.md](001-azure-ad-app-registration.md) - App registration details

---

## Acceptance Criteria Verification

| Criterion | Status | Notes |
|-----------|--------|-------|
| Add-in appears in M365 admin center | ⏳ Pending | |
| Add-in loads in New Outlook | ⏳ Pending | |
| Add-in loads in Outlook Web | ⏳ Pending | |
| Ribbon buttons appear and work | ⏳ Pending | |

---

*After completing this task, proceed to Task 058 (Deploy Word add-in) and Task 070 (E2E test: Outlook save flow).*
