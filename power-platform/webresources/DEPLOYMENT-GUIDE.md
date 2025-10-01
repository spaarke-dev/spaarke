# Power Platform Web Resources - Deployment Guide

**Task:** 3.2 - JavaScript File Management Integration
**Version:** 1.0.0
**Date:** 2025-09-30

---

## 📋 Overview

This guide explains how to deploy the JavaScript web resources for document file management to your Power Platform environment.

**Web Resources:**
- **sprk_DocumentOperations.js** - Main file management operations

**Functionality:**
- Upload files to SharePoint Embedded (SPE)
- Download files from SPE
- Replace existing files
- Delete files
- Update Dataverse document metadata

---

## 🎯 Prerequisites

### 1. Power Platform Environment
- [ ] Dataverse environment accessible
- [ ] Solution `spaarke_document_management` created
- [ ] Document entity (`sprk_document`) deployed
- [ ] Container entity (`sprk_container`) deployed
- [ ] Model-driven app created with document forms

### 2. Backend Services
- [ ] BFF API deployed and running
- [ ] CORS configured for Power Platform domain
- [ ] API endpoints operational:
  - `/api/containers/{id}/drive`
  - `/api/drives/{driveId}/upload`
  - `/api/drives/{driveId}/items/{itemId}/content`
  - `/api/drives/{driveId}/items/{itemId}`

### 3. Authentication
- [ ] App registration configured
- [ ] Power Platform users can authenticate to API
- [ ] EasyAuth or managed identity configured

---

## 📦 Deployment Steps

### Step 1: Upload Web Resource

#### Option A: Power Platform Maker Portal (Recommended)

1. **Navigate to Solutions**
   ```
   https://make.powerapps.com → Solutions → spaarke_document_management
   ```

2. **Add New Web Resource**
   - Click **New** → **More** → **Web resource**
   - **Display name:** `Spaarke Document Operations`
   - **Name:** `sprk_DocumentOperations`
   - **Type:** Script (JScript)
   - **Upload file:** `sprk_DocumentOperations.js`

3. **Set Properties**
   - **Description:** `File management operations for SharePoint Embedded integration`
   - **Language:** English (no need for multiple languages initially)

4. **Save and Publish**
   - Click **Save**
   - Click **Publish** to make available

#### Option B: Power Platform CLI

```bash
# Navigate to web resources directory
cd power-platform/webresources

# Authenticate to environment
pac auth create --environment https://spaarkedev1.crm.dynamics.com

# Add web resource to solution
pac solution add-reference \
  --path scripts/sprk_DocumentOperations.js \
  --solution spaarke_document_management

# Or upload directly
pac webresource push \
  --file scripts/sprk_DocumentOperations.js \
  --name sprk_DocumentOperations \
  --display-name "Spaarke Document Operations" \
  --type JScript
```

---

### Step 2: Configure Form Events

#### Document Form Configuration

1. **Open Form Designer**
   ```
   Make Portal → Solutions → spaarke_document_management
   → Tables → Document → Forms → Information (Main Form)
   ```

2. **Add Form Libraries**
   - Click **Form Properties**
   - Go to **Events** tab
   - Under **Form Libraries**, click **Add library**
   - Select `sprk_DocumentOperations`
   - Click **Add**

3. **Configure OnLoad Event**
   - Event: **OnLoad**
   - Library: `sprk_DocumentOperations`
   - Function: `Spaarke.Documents.onFormLoad`
   - Enabled: ✅ Yes
   - Pass execution context: ✅ Yes

4. **Configure OnSave Event** (Optional)
   - Event: **OnSave**
   - Library: `sprk_DocumentOperations`
   - Function: `Spaarke.Documents.onFormSave`
   - Enabled: ✅ Yes
   - Pass execution context: ✅ Yes

5. **Save and Publish**
   - Click **Save**
   - Click **Publish**

---

### Step 3: Configure Ribbon Buttons

#### Option A: Using Ribbon Workbench (Recommended)

1. **Install Ribbon Workbench**
   - Download from: https://www.develop1.net/public/rwb/ribbonworkbench.aspx
   - Install in your environment

2. **Open Ribbon Workbench**
   ```
   Make Portal → Solutions → spaarke_document_management
   → Click ... → Open Ribbon Workbench
   ```

3. **Create Buttons**

   **Upload File Button:**
   - **Location:** Main Form Ribbon
   - **Section:** Actions
   - **Label:** Upload File
   - **Icon:** Upload icon
   - **Command:**
     - **Library:** `sprk_DocumentOperations`
     - **Function:** `Spaarke.Documents.uploadFile`
     - **Pass Primary Control:** ✅ Yes
   - **Enable Rule:** Show when `sprk_hasfile` is false

   **Download File Button:**
   - **Location:** Main Form Ribbon
   - **Section:** Actions
   - **Label:** Download File
   - **Icon:** Download icon
   - **Command:**
     - **Library:** `sprk_DocumentOperations`
     - **Function:** `Spaarke.Documents.downloadFile`
     - **Pass Primary Control:** ✅ Yes
   - **Enable Rule:** Show when `sprk_hasfile` is true

   **Replace File Button:**
   - **Location:** Main Form Ribbon
   - **Section:** Actions
   - **Label:** Replace File
   - **Icon:** Replace icon
   - **Command:**
     - **Library:** `sprk_DocumentOperations`
     - **Function:** `Spaarke.Documents.replaceFile`
     - **Pass Primary Control:** ✅ Yes
   - **Enable Rule:** Show when `sprk_hasfile` is true

   **Delete File Button:**
   - **Location:** Main Form Ribbon
   - **Section:** Actions
   - **Label:** Delete File
   - **Icon:** Delete icon
   - **Command:**
     - **Library:** `sprk_DocumentOperations`
     - **Function:** `Spaarke.Documents.deleteFile`
     - **Pass Primary Control:** ✅ Yes
   - **Enable Rule:** Show when `sprk_hasfile` is true

4. **Publish Ribbon**
   - Click **Publish** in Ribbon Workbench
   - Wait for completion

#### Option B: Using Ribbon XML (Advanced)

If you prefer XML configuration, create a ribbon customization file and import it into your solution. See Task 3.2 specification for detailed XML.

---

### Step 4: Verify Configuration

#### Test Checklist

1. **Form Load**
   - [ ] Open a document record
   - [ ] Open browser console (F12)
   - [ ] Verify message: "Spaarke Documents v1.0.0 initialized"
   - [ ] Verify API base URL is correct

2. **Upload File**
   - [ ] Click **Upload File** button
   - [ ] Select a small file (< 4MB)
   - [ ] Verify upload progress indicator
   - [ ] Verify success message
   - [ ] Verify form fields updated:
     - `sprk_hasfile` = true
     - `sprk_filename` = uploaded filename
     - `sprk_filesize` = file size
     - `sprk_graphitemid` = item ID
     - `sprk_graphdriveid` = drive ID

3. **Download File**
   - [ ] Click **Download File** button
   - [ ] Verify download progress indicator
   - [ ] Verify file downloads to browser
   - [ ] Verify file content is correct

4. **Replace File**
   - [ ] Click **Replace File** button
   - [ ] Confirm replacement
   - [ ] Select new file
   - [ ] Verify old file deleted
   - [ ] Verify new file uploaded
   - [ ] Verify form fields updated

5. **Delete File**
   - [ ] Click **Delete File** button
   - [ ] Confirm deletion
   - [ ] Verify file deleted from SPE
   - [ ] Verify form fields cleared

---

## 🔧 Troubleshooting

### Issue: "CORS policy: No 'Access-Control-Allow-Origin' header"

**Cause:** API CORS not configured for Power Platform domain

**Fix:**
1. Check `appsettings.Development.json` has:
   ```json
   "Cors": {
     "AllowedOrigins": "https://spaarkedev1.crm.dynamics.com"
   }
   ```
2. Restart BFF API
3. Clear browser cache
4. Test again

---

### Issue: "Failed to get container drive information"

**Cause:** Document not associated with container

**Fix:**
1. Ensure document has `sprk_containerid` field populated
2. Verify container exists and has SPE container ID
3. Check API logs for errors

---

### Issue: "File size exceeds maximum allowed size"

**Cause:** File larger than 4MB (Sprint 2 limit)

**Fix:**
1. Use smaller file for testing
2. Large file support will be added in Sprint 3

---

### Issue: Buttons not visible on form

**Cause:** Ribbon customizations not published

**Fix:**
1. Re-publish ribbon customizations
2. Clear browser cache (Ctrl+Shift+Delete)
3. Refresh form (Ctrl+F5)
4. Verify web resource is added to form libraries

---

### Issue: JavaScript errors in console

**Cause:** Various issues

**Debug Steps:**
1. Open browser console (F12)
2. Check for error messages
3. Verify API base URL is correct
4. Check network tab for failed requests
5. Verify CORS headers in response

---

## 🔐 Security Considerations

### Field-Level Security

The `sprk_filename` field has field-level security enabled. To ensure users can see filenames:

1. **Create Field Security Profile**
   ```
   Settings → Security → Field Security Profiles
   → New → Name: "Document Managers"
   ```

2. **Grant Permissions**
   - Field: `sprk_filename`
   - Read: ✅ Yes
   - Update: ✅ Yes

3. **Assign Users**
   - Add users/teams to the profile

---

### Role-Based Access

Configure security roles for document operations:

1. **Document Manager Role**
   - Create, Read, Update, Delete documents
   - Upload, download, replace, delete files

2. **Document User Role**
   - Read documents
   - Download files only

---

## 📊 Monitoring

### Browser Console Logs

The JavaScript logs key operations to console:

```javascript
// Initialization
"Spaarke Documents v1.0.0 initialized"
"API Base URL: https://spaarke-bff-dev.azurewebsites.net"

// API Calls
"API Call: GET /api/containers/{id}/drive [{correlationId}]"
"API Call: PUT /api/drives/{driveId}/upload?fileName=test.pdf [{correlationId}]"

// Operations
"Button visibility update - Has file: true"
```

### API Logs

Check BFF API logs for:
- CORS preflight requests
- File upload requests
- Download requests
- Error responses

---

## 🚀 Deployment to Other Environments

### DEV → UAT

1. **Export Solution**
   ```bash
   pac solution export \
     --path spaarke_document_management_1_0_0_0.zip \
     --name spaarke_document_management
   ```

2. **Import to UAT**
   ```bash
   pac auth create --environment https://spaarkeuat.crm.dynamics.com

   pac solution import \
     --path spaarke_document_management_1_0_0_0.zip \
     --publish-changes
   ```

3. **Verify API URL**
   - JavaScript automatically detects UAT environment
   - No code changes needed

### UAT → PROD

Same process as DEV → UAT, using PROD environment URL.

---

## ✅ Post-Deployment Checklist

- [ ] Web resource uploaded and published
- [ ] Form events configured
- [ ] Ribbon buttons created
- [ ] CORS configured in API
- [ ] Field security profiles created
- [ ] Security roles assigned
- [ ] Upload file tested
- [ ] Download file tested
- [ ] Replace file tested
- [ ] Delete file tested
- [ ] Browser console shows no errors
- [ ] Network tab shows successful API calls
- [ ] Documentation updated

---

## 📞 Support

### Resources
- Task 3.2 Specification: `dev/projects/sdap_project/Sprint 2/Task-3.2-JavaScript-File-Management-Integration.md`
- API Documentation: `docs/configuration/CORS-Configuration-Strategy.md`
- Authentication Guide: `docs/configuration/Certificate-Authentication-JavaScript.md`

### Common Contacts
- Power Platform Admin: [Contact]
- API Support: [Contact]
- Security Team: [Contact]

---

**Deployment Guide Version:** 1.0.0
**Last Updated:** 2025-09-30
**Author:** Spaarke Development Team
