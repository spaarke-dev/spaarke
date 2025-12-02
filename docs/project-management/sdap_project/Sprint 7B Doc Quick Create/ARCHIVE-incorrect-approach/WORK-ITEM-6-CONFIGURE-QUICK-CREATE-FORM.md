# Work Item 6: Configure Quick Create Form

**Estimated Time:** 1 hour
**Prerequisites:** Work Item 5 complete (FileUploadField.tsx created), Solution deployed
**Status:** Ready to Start

---

## Objective

Configure the Quick Create form for Document entity to use the SPE File Upload PCF control.

---

## Context

Quick Create forms are simplified forms that open in a modal dialog, typically from subgrids (e.g., Documents subgrid on Matter form).

**Key Configuration:**
1. Add `sprk_fileuploadmetadata` field to form
2. Bind PCF control to this field
3. Set control parameters (Container ID, SDAP API URL, allow multiple)
4. Hide metadata field (users don't need to see JSON)
5. Publish form

---

## Prerequisites

Before starting, ensure:

1. ✅ Solution deployed to environment
2. ✅ Document entity has `sprk_fileuploadmetadata` field (10,000 chars)
3. ✅ PCF control registered in solution
4. ✅ User has System Customizer or System Administrator role

---

## Configuration Steps

### Step 1: Navigate to Document Entity Forms

1. Open Power Apps portal: https://make.powerapps.com
2. Select your environment (top-right dropdown)
3. Navigate to: **Tables** > **Document** (sprk_document)
4. Click **Forms** tab

---

### Step 2: Open Quick Create Form

**Option A: Existing Quick Create Form**

1. Look for form named "Quick Create" or "Document Quick Create"
2. Click to open in Form Designer

**Option B: Create New Quick Create Form**

If no Quick Create form exists:

1. Click **+ New form** > **Quick Create form**
2. Name: "Document Quick Create"
3. Click **Create**

---

### Step 3: Add Required Fields

Quick Create form should have these fields:

**Required Fields:**
- `sprk_documenttitle` (Document Title) - **Visible**
- `sprk_fileuploadmetadata` (File Upload Metadata) - **Hidden**

**Optional Fields:**
- `sprk_description` (Description) - **Visible**
- `sprk_matter` (Matter lookup) - **Visible** (auto-populated from parent)
- `sprk_containerid` (Container ID) - **Hidden** (auto-populated from parent)

**Add Field Steps:**

1. Click **+ Add field** (left panel)
2. Search for field name (e.g., "sprk_documenttitle")
3. Drag field to form layout
4. Repeat for all fields

**Form Layout:**

```
┌─────────────────────────────────────────┐
│ Document Quick Create                   │
├─────────────────────────────────────────┤
│                                         │
│ Document Title: [___________________]  │  ← sprk_documenttitle (visible)
│                                         │
│ Description:    [___________________]  │  ← sprk_description (visible)
│                 [___________________]  │
│                                         │
│ Matter:         [___________________]  │  ← sprk_matter (visible, auto-populated)
│                                         │
│ File Upload Metadata: (hidden)         │  ← sprk_fileuploadmetadata (hidden, bound to PCF)
│                                         │
│                     [Save] [Cancel]     │
└─────────────────────────────────────────┘
```

---

### Step 4: Add PCF Control to sprk_fileuploadmetadata Field

1. Click on `sprk_fileuploadmetadata` field in form
2. Click **+ Component** (right panel)
3. Select **SPE File Upload** control (or "Universal Quick Create")
4. Click **Add**

**If control doesn't appear:**
- Verify solution is imported
- Refresh browser
- Check control is registered in solution

---

### Step 5: Configure Control Parameters

After adding control, configure parameters:

1. Select `sprk_fileuploadmetadata` field
2. In right panel, expand **SPE File Upload** control
3. Set parameters:

#### Parameter 1: sdapApiBaseUrl

**Value (Test Environment):**
```
https://your-test-api.azurewebsites.net/api
```

**Value (Production):**
```
https://your-prod-api.azurewebsites.net/api
```

**Important:** Replace with your actual SDAP API URL.

---

#### Parameter 2: allowMultipleFiles

**Value:** `true`

**Effect:** Users can upload multiple files at once.

**Alternative:** Set to `false` to allow only single file upload.

---

#### Parameter 3: containerid

**Value:** Leave blank (auto-detected from parent Matter)

**Alternative:** If parent context doesn't work, bind to form field:
1. Add hidden field `sprk_containerid` to form
2. Set default value from parent Matter
3. Bind this parameter to `sprk_containerid` field

---

### Step 6: Hide sprk_fileuploadmetadata Field Label

Since the PCF control renders its own UI, hide the field label:

1. Select `sprk_fileuploadmetadata` field
2. In right panel, find **Label** section
3. Toggle **Hide label** to **On**

**Result:** Only PCF control UI is visible, not the field label.

---

### Step 7: Configure Field Properties

For each field, set properties:

#### sprk_documenttitle (Document Title)

- **Label:** Document Title
- **Visible:** Yes
- **Required:** Yes
- **Field Behavior:** Simple

#### sprk_description (Description)

- **Label:** Description
- **Visible:** Yes
- **Required:** No
- **Field Behavior:** Simple

#### sprk_matter (Matter Lookup)

- **Label:** Matter
- **Visible:** Yes
- **Required:** No (auto-populated from parent)
- **Field Behavior:** Simple
- **Default Value:** From parent context (automatic)

#### sprk_fileuploadmetadata (File Upload Metadata)

- **Label:** (hidden)
- **Visible:** Yes (control is visible, label is hidden)
- **Required:** No
- **Field Behavior:** Simple
- **Control:** SPE File Upload (PCF)

#### sprk_containerid (Container ID) - Optional

- **Label:** Container ID
- **Visible:** No (hidden field)
- **Required:** No
- **Field Behavior:** Simple
- **Default Value:** From parent Matter (sprk_containerid field)

---

### Step 8: Save and Publish Form

1. Click **Save** (top-right)
2. Click **Publish** (top-right)
3. Wait for publish to complete (~30 seconds)

**Verify:**
- No errors shown
- "Form published successfully" message appears

---

### Step 9: Test Quick Create Form

1. Navigate to Matter entity
2. Open any Matter record
3. Scroll to **Documents** subgrid
4. Click **+ New** button

**Expected:**
- Quick Create form opens in modal dialog
- Document Title field is visible
- Description field is visible
- Matter field is pre-filled with parent Matter
- File upload UI is visible (drop zone, "Choose Files" button)
- Container ID is auto-detected (check browser console)

**Browser Console (F12):**
```
[FileUploadPCF] Initializing SPE File Upload control
[FileUploadPCF] Retrieving Container ID from parent: { parentEntityName: "sprk_matter", parentRecordId: "..." }
[FileUploadPCF] Container ID loaded successfully: { containerId: "abc-123-def-456" }
[FileUploadPCF] Services initialized successfully
```

---

### Step 10: Test File Upload

1. In Quick Create form, click **Choose Files**
2. Select 1 file (e.g., contract.pdf)
3. Wait for upload to complete
4. Verify file appears in "Uploaded Files" list
5. Click **Save**

**Expected:**
- File uploads to SharePoint Embedded
- "Uploaded Files (1)" section shows file
- Save button creates Document record
- Form closes
- New Document appears in subgrid

**Verify in Dataverse:**
1. Open created Document record
2. Check `sprk_fileuploadmetadata` field
3. Should contain JSON:
   ```json
   [
       {
           "driveItemId": "01ABC...",
           "fileName": "contract.pdf",
           "fileSize": 2048576,
           "sharePointUrl": "https://...",
           "webUrl": "https://...",
           "createdDateTime": "2025-10-07T12:00:00Z",
           "lastModifiedDateTime": "2025-10-07T12:00:00Z"
       }
   ]
   ```

---

## Alternative Configuration: Manual Container ID Entry

If parent context doesn't provide Container ID automatically, configure manual entry:

### Add Container ID Field to Form

1. Add `sprk_containerid` field to form (visible)
2. Set as required field
3. User must manually enter Container ID

**Form Layout:**

```
┌─────────────────────────────────────────┐
│ Document Title: [___________________]  │
│ Container ID:   [___________________]  │  ← User enters manually
│ Description:    [___________________]  │
│                                         │
│ File Upload UI                          │
└─────────────────────────────────────────┘
```

**PCF Control Parameter:**

Set `containerid` parameter to bind to `sprk_containerid` field:

```
containerid: {field: sprk_containerid}
```

---

## Troubleshooting

### Issue: PCF Control Doesn't Appear in Component List

**Symptoms:** "SPE File Upload" control not available when adding component

**Solutions:**

1. **Verify solution imported:**
   - Go to **Solutions** > **All**
   - Look for "UniversalQuickCreateSolution"
   - Status should be "Published"

2. **Refresh browser:**
   - Close form designer
   - Hard refresh (Ctrl+Shift+R)
   - Reopen form designer

3. **Check control registration:**
   - Open solution
   - Go to **Objects** > **Controls**
   - Verify "SPE File Upload" control is listed

4. **Verify field type:**
   - PCF control only works with field types it's registered for
   - `sprk_fileuploadmetadata` should be "Multiple Lines of Text"

---

### Issue: Form Opens But Control Doesn't Render

**Symptoms:** Field shows but no file upload UI

**Solutions:**

1. **Check browser console (F12):**
   - Look for JavaScript errors
   - Look for control initialization logs

2. **Verify control bound to field:**
   - Open form designer
   - Click `sprk_fileuploadmetadata` field
   - Check "SPE File Upload" control is added

3. **Clear browser cache:**
   - Hard refresh (Ctrl+Shift+R)
   - Or use incognito window

---

### Issue: Container ID Not Detected

**Symptoms:** Warning message "No Container ID found"

**Solutions:**

1. **Verify parent context:**
   - Quick Create must be opened from Matter subgrid
   - Parent Matter must have `sprk_containerid` field populated

2. **Check browser console:**
   ```
   [FileUploadPCF] No parent context found
   ```
   Or:
   ```
   [FileUploadPCF] Parent record has no Container ID
   ```

3. **Fallback to manual entry:**
   - Add `sprk_containerid` field to form (visible)
   - User enters Container ID manually

---

### Issue: File Upload Fails

**Symptoms:** Error message "File upload failed"

**Solutions:**

1. **Verify SDAP API URL:**
   - Check `sdapApiBaseUrl` parameter is correct
   - Test API endpoint: `https://your-api.azurewebsites.net/api/health`
   - Should return 200 OK

2. **Check MSAL authentication:**
   - Look for browser console logs:
     ```
     [MsalAuthProvider] Token retrieved successfully
     ```
   - If token fails, check Azure AD app registration

3. **Verify user permissions:**
   - User must have write permission on SharePoint container
   - Check SharePoint container permissions

---

### Issue: Save Button Doesn't Create Record

**Symptoms:** Form closes but no Document record created

**Solutions:**

1. **Check required fields:**
   - `sprk_documenttitle` must be filled
   - Verify all required fields have values

2. **Check form validation:**
   - Look for red validation messages
   - Form won't save if validation fails

3. **Check browser console:**
   - Look for errors during save
   - Check Network tab for failed API calls

---

## Form Configuration Checklist

After completing configuration:

- ✅ Quick Create form exists for Document entity
- ✅ `sprk_documenttitle` field added (visible, required)
- ✅ `sprk_description` field added (visible, optional)
- ✅ `sprk_matter` field added (visible, auto-populated)
- ✅ `sprk_fileuploadmetadata` field added (visible, label hidden)
- ✅ SPE File Upload PCF control bound to `sprk_fileuploadmetadata`
- ✅ `sdapApiBaseUrl` parameter set correctly
- ✅ `allowMultipleFiles` parameter set to `true`
- ✅ Form saved and published
- ✅ Form tested from Matter subgrid
- ✅ File upload works
- ✅ Document record created successfully

---

## Next Steps

After completing this work item:

1. ✅ Quick Create form configured
2. ✅ PCF control working in form
3. ✅ File upload tested successfully
4. ⏳ Move to Work Item 7: End-to-End Testing

---

**Status:** Ready for implementation
**Estimated Time:** 1 hour
**Next:** Work Item 7 - End-to-End Testing
