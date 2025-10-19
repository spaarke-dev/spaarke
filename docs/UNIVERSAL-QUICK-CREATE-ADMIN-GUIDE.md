# Universal Quick Create - Admin Configuration Guide

**Version:** 2.0.0 (Revised Approach)
**Last Updated:** 2025-10-07
**Sprint:** 7B

---

## ⚠️ Important: Revised Approach (v2.0)

**Date:** 2025-10-07

This guide has been updated to reflect the **revised Sprint 7B approach**:

- **PCF Control:** File upload only (no field inheritance in frontend)
- **Field Inheritance:** Backend Dataverse plugin (separate component)
- **Binding Field:** `sprk_fileuploadmetadata` (temporary metadata storage)

**Benefits:** Simpler PCF, more reliable field inheritance, cleaner architecture.

See [SPRINT-7B-REVISED-APPROACH.md](../dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/SPRINT-7B-REVISED-APPROACH.md) for details.

---

## Overview

The SPE File Upload PCF control enables:
- **File upload to SharePoint Embedded** - Upload files directly from Quick Create forms
- **Multi-file support** - Upload multiple files at once (creates separate Document records)
- **MSAL authentication** - Secure file operations with Microsoft Authentication Library
- **Backend field inheritance** - Dataverse plugin auto-populates fields from parent Matter

---

## Supported Entities

### Out of the Box:

1. **Document** (sprk_document)
   - **Fields**: Document Title, Description
   - **File Upload**: Enabled
   - **Use Case**: Create documents with files from Matter, Client, or Account

2. **Task** (task)
   - **Fields**: Subject, Description, Due Date, Priority
   - **File Upload**: Disabled
   - **Use Case**: Create tasks from Matter or other entities

3. **Contact** (contact)
   - **Fields**: First Name, Last Name, Email, Phone
   - **File Upload**: Disabled
   - **Use Case**: Create contacts from Account or Matter

### Custom Entities:
To add support for custom entities, contact your development team (requires code change, ~15 minutes).

---

## Configuration Steps

### 1. Deploy Solution to Environment

**Prerequisites:**
- Solution package: `UniversalQuickCreateSolution.zip` (195 KB)
- Power Platform admin or system customizer role
- PAC CLI installed (for command-line deployment)

**Option A: Deploy via Power Apps Portal**
1. Navigate to: https://make.powerapps.com
2. Select your environment
3. Go to: Solutions > Import solution
4. Upload `UniversalQuickCreateSolution.zip`
5. Click Next > Import
6. Wait for import to complete (~2-3 minutes)

**Option B: Deploy via PAC CLI**
```bash
# Authenticate to environment
pac auth create --url https://your-environment.crm.dynamics.com

# Import solution
pac solution import --path UniversalQuickCreateSolution.zip --async

# Monitor import status
pac solution list
```

### 2. Configure Quick Create Form

1. Navigate to: **Tables** > **[Entity Name]** > **Forms**
2. Open or create **Quick Create** form
3. Add/remove fields as needed:
   - For Document: sprk_documenttitle, sprk_description
   - For Task: subject, description, scheduledend, prioritycode
   - For Contact: firstname, lastname, emailaddress1, telephone1
4. Click **Add control** > Select **Universal Quick Create**
5. Configure control parameters (see Section 3 below)
6. **Save** and **Publish** form

### 3. Configure Control Parameters

#### Parameter: defaultValueMappings

**Purpose:** Maps parent entity fields to child entity default values

**Format:** JSON object
```json
{
  "parent_entity_name": {
    "parent_field_name": "child_field_name"
  }
}
```

**Example (Document from Matter):**
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  }
}
```

**Important Notes:**
- Use **logical names**, not display names (e.g., "sprk_matter", not "Matter")
- Container ID mapping required for file upload to work
- Multiple parent entities supported (e.g., sprk_matter, account, contact)

**Field Mapping Types:**

1. **Simple Field Mapping** (Text, Number, Date, etc.)
   - Copies value directly from parent field to child field
   - Example: `"name": "sprk_companyname"` (copies Account Name to Company Name)
   - Example: `"sprk_containerid": "sprk_containerid"` (copies Container ID)

2. **Lookup Field Mapping** (Relationships)
   - Creates relationship reference between parent and child records
   - Child field name matches parent entity name (e.g., `sprk_matter` for sprk_matter parent)
   - Example: `"sprk_matternumber": "sprk_matter"` (links Document to parent Matter)
   - Automatically uses OData bind syntax internally: `sprk_matter@odata.bind`

**Example (Multiple Parent Entities):**
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  },
  "account": {
    "sprk_containerid": "sprk_containerid",
    "name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  }
}
```

#### Parameter: enableFileUpload

**Purpose:** Show or hide file picker field

**Values:**
- `true` - Show file picker (for Documents)
- `false` - Hide file picker (for Tasks, Contacts)
- *Leave blank* - Auto-configured based on entity (Document=true, Task/Contact=false)

**Recommendation:** Leave blank unless you need to override the default behavior

#### Parameter: sdapApiBaseUrl

**Purpose:** SDAP BFF API endpoint URL

**Values:**
- **Test Environment:** `https://your-test-api.azurewebsites.net/api`
- **Production Environment:** `https://your-prod-api.azurewebsites.net/api`

**Important:** Ensure SDAP API is deployed and accessible before configuring Quick Create

---

## Common Configurations

### Configuration 1: Document from Matter

**Scenario:** Create documents with file upload from Matter subgrid

**Field Mapping:**

| Parent Table | Parent Column Name | Parent Column Logical | Child Table   | Child Column Name | Child Column Logical | Field Type |
| ------------ | ------------------ | --------------------- | ------------- | ----------------- | -------------------- | ---------- |
| sprk_matter  | Matter Number      | sprk_matternumber     | sprk_document | Matter            | sprk_matter          | LookupType |
| sprk_matter  | Container Id       | sprk_containerid      | sprk_document | Container Id      | sprk_containerid     | Text       |

**Quick Create Form Fields:**
- sprk_documenttitle (visible)
- sprk_description (visible)
- sprk_containerid (hidden - auto-populated)
- sprk_matter (lookup - auto-populated)

**Control Parameters:**
```json
{
  "defaultValueMappings": {
    "sprk_matter": {
      "sprk_matternumber": "sprk_matter",
      "sprk_containerid": "sprk_containerid"
    }
  },
  "enableFileUpload": true,
  "sdapApiBaseUrl": "https://your-api.azurewebsites.net/api"
}
```

**Result:**
- File picker shown
- Matter lookup pre-filled with parent Matter
- Container ID pre-filled (hidden)
- User enters Document Title, selects file, clicks Save
- File uploads to SharePoint Embedded
- Document record created with SPE metadata

---

### Configuration 2: Contact from Account

**Scenario:** Create contacts without file upload from Account subgrid

**Field Mapping:**

| Parent Table | Parent Column Name | Parent Column Logical | Child Table | Child Column Name | Child Column Logical | Field Type |
| ------------ | ------------------ | --------------------- | ----------- | ----------------- | -------------------- | ---------- |
| account      | Account Name       | name                  | contact     | Company Name      | sprk_companyname     | Text       |
| account      | Address 1          | address1_composite    | contact     | Address 1         | address1_composite   | MemoType   |

**Quick Create Form Fields:**
- firstname (visible)
- lastname (visible)
- emailaddress1 (visible)
- telephone1 (visible)
- sprk_companyname (auto-populated)
- address1_composite (auto-populated)

**Control Parameters:**
```json
{
  "defaultValueMappings": {
    "account": {
      "name": "sprk_companyname",
      "address1_composite": "address1_composite"
    }
  },
  "enableFileUpload": false
}
```

**Result:**
- File picker NOT shown
- Company Name pre-filled with Account Name
- Address 1 pre-filled from Account
- User fills first/last name, email, phone
- Contact created

---

## Field Types Supported

| Field Type | Description | Example Use Case |
|------------|-------------|------------------|
| **text** | Single-line input | Document Title, Subject, First Name |
| **textarea** | Multi-line input | Description, Notes |
| **number** | Numeric input | Amount, Quantity |
| **date** | Date picker | Due Date, Start Date |
| **datetime** | Date + time picker | Meeting Date/Time |
| **boolean** | Toggle switch | Active, Billable |
| **optionset** | Dropdown | Priority (Low/Normal/High), Status |

**Note:** Lookup fields not supported yet (use default value mapping to pre-populate)

---

## Troubleshooting

### Issue: File Upload Fails

**Symptoms:**
- Error message: "Container ID not found"
- Or: "File upload failed"

**Solutions:**
1. **Verify Container ID exists on parent Matter:**
   - Open Matter record
   - Check Container ID field has value
   - If empty, provision SharePoint container first

2. **Check SDAP API URL:**
   - Verify URL in control parameters is correct
   - Test API endpoint: `https://your-api.azurewebsites.net/api/health`
   - Should return 200 OK

3. **Verify user permissions:**
   - User must have write permission on SharePoint container
   - Check Azure AD app registration permissions

4. **Check browser console:**
   - Press F12 to open developer tools
   - Look for red errors in Console tab
   - Check Network tab for failed API requests

---

### Issue: Default Values Not Applied

**Symptoms:**
- Fields are empty when they should be pre-filled

**Solutions:**
1. **Verify JSON syntax:**
   - Check for missing quotes, commas, or brackets
   - Use JSON validator: https://jsonlint.com
   - Correct: `"sprk_name": "subject"`
   - Incorrect: `sprk_name: subject` (missing quotes)

2. **Check field names:**
   - Use logical names, not display names
   - For lookups, use `_fieldname_value` syntax
   - Example: `_ownerid_value` (not `ownerid` or `Owner`)

3. **Ensure parent record has values:**
   - Open parent record (e.g., Matter)
   - Verify mapped fields have values
   - Empty parent fields = empty child fields

4. **Check browser console:**
   - Look for mapping logs: `[UniversalQuickCreatePCF] Default value mappings loaded`
   - Look for warnings: "No default value mappings found"

---

### Issue: Form Doesn't Open

**Symptoms:**
- Clicking "+ New" doesn't show Quick Create form

**Solutions:**
1. **Verify form is published:**
   - Go to Forms > Quick Create form
   - Check "Published" status
   - Click Publish if needed

2. **Check user permissions:**
   - User must have Create permission on entity
   - Check security roles

3. **Clear browser cache:**
   - Hard refresh: Ctrl+Shift+R (Windows) or Cmd+Shift+R (Mac)
   - Or use private/incognito window

4. **Check if entity supports Quick Create:**
   - Some entities don't support Quick Create forms
   - Verify in entity settings

---

### Issue: Wrong Fields Shown

**Symptoms:**
- Form shows hardcoded fields instead of configured fields

**Solutions:**
1. **Entity not in EntityFieldDefinitions:**
   - Control uses hardcoded field definitions
   - Supported: sprk_document, task, contact
   - For other entities, contact development team

2. **Form cached:**
   - Clear browser cache
   - Hard refresh page

---

## Browser Console Logging

For troubleshooting, open browser console (F12) and look for logs:

### Successful Document Creation:
```
[UniversalQuickCreatePCF] Field configuration loaded: { entityName: "sprk_document", fieldCount: 2, supportsFileUpload: true }
[UniversalQuickCreatePCF] Default value mappings loaded: { sprk_matter: {...} }
[UniversalQuickCreatePCF] Save requested: { formData: {...}, hasFile: true, fileName: "contract.pdf" }
[FileUploadService] Starting file upload: { fileName: "contract.pdf", fileSize: 2048576, driveId: "b!..." }
[MsalAuthProvider] Token retrieved from cache (5ms)
[FileUploadService] File uploaded successfully: { fileName: "contract.pdf", driveItemId: "01ABC..." }
[DataverseRecordService] Document record created successfully: { recordId: "..." }
[UniversalQuickCreatePCF] Save complete - form will close
```

### Error: Missing Container ID:
```
[UniversalQuickCreatePCF] Save requested: { ... }
[UniversalQuickCreatePCF] Save failed: Container ID not found. Please ensure the parent record has a valid SharePoint container.
[QuickCreateForm] Form submission failed: Container ID not found...
```

### MSAL Authentication:
```
[UniversalQuickCreatePCF] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[UniversalQuickCreatePCF] MSAL authentication initialized successfully ✅
[UniversalQuickCreatePCF] User authenticated: true
```

---

## Performance Expectations

### Load Time:
- **Quick Create form opens:** <1 second
- **First file upload:** ~2-5 seconds (includes MSAL token acquisition)
- **Subsequent uploads:** ~1-2 seconds (MSAL token cached, 82x faster)

### File Upload Time:
- **Small file (<1 MB):** ~1-2 seconds
- **Medium file (1-10 MB):** ~2-10 seconds
- **Large file (10-50 MB):** ~10-30 seconds

*Note: Actual times depend on network speed and SDAP API performance*

---

## Security & Authentication

### MSAL Authentication:
- Uses Microsoft Authentication Library (MSAL) for secure authentication
- Token cached in browser sessionStorage (cleared when tab closes)
- First upload acquires token (~420ms), subsequent uploads use cache (~5ms)
- 401 errors automatically retry with fresh token

### Permissions Required:
- **User Permissions:** Create permission on entity (Document, Task, Contact)
- **SharePoint Permissions:** Write permission on SharePoint container
- **Azure AD Permissions:** API permissions configured on app registration

---

## Support & References

### Documentation:
- **Sprint 7B Task 1 Summary:** [TASK-7B-1-COMPLETION-SUMMARY.md](../dev/projects/sdap_project/Sprint%207_Dataset%20Grid%20to%20SDAP/TASK-7B-1-COMPLETION-SUMMARY.md)
- **Sprint 7B Task 2 Summary:** [TASK-7B-2-COMPLETION-SUMMARY.md](../dev/projects/sdap_project/Sprint%207_Dataset%20Grid%20to%20SDAP/TASK-7B-2-COMPLETION-SUMMARY.md)
- **Sprint 7B Task 3 Summary:** [TASK-7B-3-COMPLETION-SUMMARY.md](../dev/projects/sdap_project/Sprint%207_Dataset%20Grid%20to%20SDAP/TASK-7B-3-COMPLETION-SUMMARY.md)

### Technical Details:
- **Bundle Size:** 723 KB (production)
- **MSAL Version:** @azure/msal-browser v4.24.1
- **React Version:** 18.2.0
- **Fluent UI Version:** v9.54.0

### Common Field Names:

| Entity | Display Name | Logical Name |
|--------|--------------|--------------|
| Matter | Name | sprk_name |
| Matter | Container ID | sprk_containerid |
| Matter | Owner | _ownerid_value |
| Document | Document Title | sprk_documenttitle |
| Document | Description | sprk_description |
| Task | Subject | subject |
| Task | Description | description |
| Task | Due Date | scheduledend |
| Task | Priority | prioritycode |
| Contact | First Name | firstname |
| Contact | Last Name | lastname |
| Contact | Email | emailaddress1 |
| Contact | Phone | telephone1 |
| Account | Account Name | name |
| Account | Owner | _ownerid_value |

---

## Frequently Asked Questions

### Q: Can I use this control for entities other than Document/Task/Contact?
**A:** Yes, but requires code changes. Contact your development team to add support for custom entities (~15 minutes per entity).

### Q: Can I disable file upload for Document entity?
**A:** Yes, set `enableFileUpload: false` in control parameters. However, this defeats the primary purpose of the control.

### Q: Can users upload multiple files at once?
**A:** Not yet. Multi-file upload is planned for future release (Sprint 7B Task 2A).

### Q: What happens if file upload succeeds but record creation fails?
**A:** The file will be uploaded to SharePoint Embedded but no Dataverse record created. Check browser console for error details. The file remains in SharePoint (orphaned).

### Q: Can I change the default file size limit?
**A:** File size limits are enforced by SDAP API and SharePoint Embedded, not the PCF control. Contact your API team to adjust limits.

### Q: Does this work offline?
**A:** No. Requires active internet connection for MSAL authentication and file upload.

---

**Last Updated:** 2025-10-07
**Version:** 1.0.0
**Support:** Check browser console (F12) for detailed error logs
