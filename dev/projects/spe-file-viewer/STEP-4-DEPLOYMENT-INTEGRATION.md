# Step 4: Deployment and Integration

**Phase**: 4 of 5
**Duration**: ~1.5 hours
**Prerequisites**:
- Step 2 completed (Custom API registered)
- Step 3 completed (PCF control built)

---

## Overview

Deploy the SDAP BFF API to Azure, import the PCF control to Dataverse, and configure the Document form to display the file viewer. This makes the complete solution available to end users.

**Deployment Steps**:
1. Deploy SDAP BFF API to Azure App Service
2. Import PCF solution to Dataverse
3. Add PCF control to Document form
4. Configure control properties
5. Publish customizations

---

## Task 4.1: Deploy SDAP BFF API

**Goal**: Deploy updated BFF API with `/preview-url` endpoint to Azure

### Build API

```bash
# Navigate to API project
cd c:/code_files/spaarke/src/api/Spe.Bff.Api

# Clean and build Release
dotnet clean
dotnet build -c Release
```

### Publish to Azure

```bash
# Publish to folder
dotnet publish -c Release -o ./publish

# Package for deployment
cd publish
tar -czf ../deploy.tar.gz *
cd ..

# Deploy to Azure App Service
az webapp deploy \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz \
    --src-path deploy.tar.gz \
    --type targz \
    --async true

# Wait for deployment to complete (~30-60 seconds)
sleep 60

# Restart app service to ensure new code loads
az webapp restart \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz
```

### Verify Deployment

```bash
# Check app service status
az webapp show \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz \
    --query "state" -o tsv

# Expected output: Running
```

### Test API Endpoint

```bash
# Get access token (replace with your credentials)
TOKEN=$(az account get-access-token \
    --resource https://spe-api-dev-67e2xz.azurewebsites.net \
    --query accessToken -o tsv)

# Test /preview-url endpoint
curl -X GET \
    "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{test-document-id}/preview-url" \
    -H "Authorization: Bearer $TOKEN" \
    -H "X-Correlation-Id: test-123"

# Expected response (200 OK):
# {
#   "data": {
#     "previewUrl": "https://...",
#     "postUrl": "https://...",
#     "expiresAt": "2025-01-21T16:30:00Z",
#     "contentType": "application/pdf"
#   },
#   "metadata": { ... }
# }
```

**Validation**:
- [ ] API deployed successfully
- [ ] App service status = Running
- [ ] `/preview-url` endpoint returns 200 OK

---

## Task 4.2: Import PCF Solution to Dataverse

**Goal**: Import PCF control solution into Dataverse environment

### Option A: Power Apps Maker Portal (Recommended)

1. Navigate to **https://make.powerapps.com**
2. Select **SPAARKE DEV 1** environment
3. Click **Solutions** in left navigation
4. Click **Import solution**
5. Click **Browse** and select:
   ```
   c:\code_files\spaarke\src\controls\SpeFileViewer\solutions\bin\Release\Spaarke_SpeFileViewer_1_0_0_0.zip
   ```
6. Click **Next**
7. Click **Import**
8. Wait for import to complete (~2-3 minutes)
9. ✅ Success message: "Solution imported successfully"

### Option B: PAC CLI

```bash
# Navigate to solution directory
cd c:/code_files/spaarke/src/controls/SpeFileViewer/solutions

# Authenticate to Dataverse
pac auth create --environment https://your-org.crm.dynamics.com

# Import solution
pac solution import \
    --path bin/Release/Spaarke_SpeFileViewer_1_0_0_0.zip \
    --async \
    --activate-plugins

# Check import status
pac solution list
```

### Verify Import

```powershell
# PowerShell verification
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

# Query for PCF control
Get-CrmRecords -conn $conn -EntityLogicalName "customcontrol" `
    -FilterAttribute "name" -FilterOperator "like" -FilterValue "%SpeFileViewer%"
```

**Expected**: 1 record found with name "Spaarke.SpeFileViewer"

**Validation**:
- [ ] Solution imported successfully
- [ ] No import errors
- [ ] PCF control visible in Custom Controls list

---

## Task 4.3: Add PCF Control to Document Form

**Goal**: Configure Document main form to display file viewer

### Open Form Designer

1. Navigate to **make.powerapps.com**
2. Select **SPAARKE DEV 1** environment
3. Go to **Tables** → **Document (sprk_document)**
4. Click **Forms** tab
5. Open **Main form** (Information form)

### Add New Section for File Viewer

1. In **Form Designer**, click **+ Add component** or add new section
2. Name section: **"File Preview"**
3. Section properties:
   - **Layout**: 1 column
   - **Show section label**: No (cleaner look)

### Add PCF Control

1. Click **+ Component** in the new section
2. Select **Get more components**
3. Search for **"SpeFileViewer"**
4. Select **Spaarke.SpeFileViewer**
5. Click **Add**

### Configure Control Properties

1. Select the **SpeFileViewer** control
2. In properties panel:

| Property | Value |
|----------|-------|
| **Control** | `Spaarke.SpeFileViewer` |
| **Bind to field** | `Document ID (sprk_documentid)` |
| **Height** | `600` (pixels) |
| **Show File Name** | `Yes` |

3. **Layout**:
   - **Width**: Full width (100%)
   - **Height**: 600px or custom

4. **Visibility**:
   - **Visible by default**: Yes
   - **Hide on devices**: No

### Alternative: Bind to Custom Field

If you want to conditionally show/hide:

1. Create a field: `sprk_showfileviewer` (Two Options)
2. Bind control visibility to this field
3. Set business rules to control when viewer appears

---

## Task 4.4: Configure Form Layout (Optional)

**Goal**: Optimize form layout for best user experience

### Recommended Layout

```
┌─────────────────────────────────────────┐
│  Document Information (General Tab)     │
│  ─────────────────────────────────────  │
│  Name: [...........................]    │
│  Matter: [........................]     │
│  File Type: [.......................]   │
│  Status: [.........................]    │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│  File Preview                            │
│  ─────────────────────────────────────  │
│  [    Full-width file viewer PCF     ]  │
│  [         600px height              ]  │
│  [                                   ]  │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│  Related Records (Tabs)                  │
│  ─────────────────────────────────────  │
│  • Activities                            │
│  • Notes                                 │
│  • Audit History                         │
└─────────────────────────────────────────┘
```

### Form Sections Order

1. **General Information** (top)
2. **File Preview** (middle - PCF control)
3. **Related** (bottom tabs)

This puts the file viewer prominently without overwhelming the form.

---

## Task 4.5: Publish Form Customizations

**Goal**: Make form changes live

### Publish in Form Designer

1. Click **Save** in Form Designer
2. Click **Publish**
3. Wait for publish to complete

### Publish All Customizations

```bash
# PAC CLI
pac solution publish

# Or PowerShell
Publish-CrmAllCustomization -conn $conn
```

**Validation**:
```powershell
# Check publish status
$pubRequest = Get-CrmRecords -conn $conn -EntityLogicalName "publishxml" `
    -TopCount 1 -OrderBy "createdon" -OrderByDescending

Write-Host "Last publish time: $($pubRequest.CrmRecords[0].createdon)"
```

---

## Task 4.6: Configure App Service Configuration (If Needed)

**Goal**: Ensure SDAP BFF API has correct settings

### Verify App Settings

```bash
# Get current app settings
az webapp config appsettings list \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz \
    --query "[].{Name:name, Value:value}" -o table
```

### Required Settings

| Setting | Value | Description |
|---------|-------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Environment |
| `AzureAd:TenantId` | `{tenant-id}` | Azure AD tenant |
| `AzureAd:ClientId` | `{api-client-id}` | API registration |
| `AzureAd:ClientSecret` | `{api-secret}` | API secret (for Graph) |
| `Graph:Scopes` | `https://graph.microsoft.com/.default` | Graph API scope |
| `Dataverse:Url` | `https://your-org.crm.dynamics.com` | Dataverse URL |
| `Dataverse:ClientId` | `{dataverse-client-id}` | Dataverse app registration |
| `Dataverse:ClientSecret` | `{dataverse-secret}` | Dataverse secret |

### Add/Update Settings

```bash
# Add or update setting
az webapp config appsettings set \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz \
    --settings "Graph:Scopes=https://graph.microsoft.com/.default"

# Restart to apply
az webapp restart \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz
```

---

## Validation Checklist

- [ ] **SDAP BFF API**: Deployed to Azure
- [ ] **API Status**: Running
- [ ] **Endpoint Test**: `/preview-url` returns 200 OK
- [ ] **PCF Solution**: Imported to Dataverse
- [ ] **Custom Control**: Visible in Dataverse
- [ ] **Document Form**: Updated with PCF control
- [ ] **Control Properties**: Configured (documentId, height, showFileName)
- [ ] **Form Layout**: Optimized for user experience
- [ ] **Customizations Published**: Changes live
- [ ] **App Settings**: Verified in Azure

---

## User Acceptance Test

### Test Scenario 1: View Existing Document

1. Navigate to **Documents** in Spaarke app
2. Open an **existing Document record** with a file
3. **Expected**:
   - Form loads successfully
   - File Preview section visible
   - PCF control shows loading spinner briefly
   - File displays in iframe
   - File name appears above viewer (if showFileName = Yes)
   - No errors in browser console

### Test Scenario 2: Upload New Document and View

1. Navigate to **Matter** record
2. Click **Add Documents**
3. Upload a new PDF or Office file
4. Save and create Document
5. Open the Document record
6. **Expected**:
   - File viewer loads automatically
   - Preview displays correctly
   - No authentication prompts

### Test Scenario 3: Different File Types

Test with:
- **PDF**: Should display in browser viewer
- **Word (.docx)**: Should open in Office Online (editable)
- **Excel (.xlsx)**: Should open in Office Online (editable)
- **PowerPoint (.pptx)**: Should open in Office Online

**Expected**: All file types render correctly

---

## Common Issues

### Issue: PCF control shows "Unable to Load File Preview"

**Possible Causes**:
1. Custom API not registered (check Step 2)
2. External Service Config missing (check Task 2.1)
3. SDAP BFF API not deployed
4. Network connectivity issue

**Debug**:
```javascript
// Open browser console on Document form
// Check for errors:
// - "Custom API not found: sprk_GetFilePreviewUrl"
// - "External service config not found: SDAP_BFF_API"
// - "Failed to get preview URL"
```

### Issue: "Custom API not found: sprk_GetFilePreviewUrl"

**Fix**:
1. Verify Custom API registration (Step 2, Task 2.3)
2. Publish customizations
3. Clear browser cache
4. Hard refresh (Ctrl+Shift+R)

### Issue: PCF control not appearing in form

**Fix**:
1. Verify solution imported successfully
2. Check control is added to form in Form Designer
3. Publish form
4. Clear browser cache

### Issue: CORS errors in browser console

**Fix**: Add CORS policy to SDAP BFF API

```csharp
// In Spe.Bff.Api/Program.cs
app.UseCors(builder => builder
    .WithOrigins("https://*.dynamics.com", "https://*.powerapps.com")
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials()
);
```

Redeploy API after adding CORS.

### Issue: "Failed to acquire access token"

**Fix**:
1. Verify External Service Config credentials (Task 2.1)
2. Test service principal login:
   ```bash
   az login --service-principal \
       --username {client-id} \
       --password {client-secret} \
       --tenant {tenant-id}
   ```
3. Grant API permissions to service principal

---

## Rollback Plan

If deployment fails or issues occur:

### Rollback API Deployment

```bash
# List deployment slots
az webapp deployment list \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz

# Rollback to previous deployment
az webapp deployment source sync \
    --resource-group {your-resource-group} \
    --name spe-api-dev-67e2xz
```

### Rollback PCF Control

1. Navigate to **Solutions** in Power Apps
2. Select **Spaarke_SpeFileViewer** solution
3. Click **Delete**
4. Publish customizations

### Rollback Form Changes

1. Open **Document form** in Form Designer
2. Remove **SpeFileViewer** control
3. Delete **File Preview** section
4. Save and Publish

---

## Next Step

Once deployment is complete and basic validation passes, proceed to **Step 5: Testing** for comprehensive end-to-end testing and performance validation.

**Deployed Components**:
- SDAP BFF API with `/preview-url` endpoint
- PCF control (Spaarke.SpeFileViewer) in Dataverse
- Document form with file viewer integration
