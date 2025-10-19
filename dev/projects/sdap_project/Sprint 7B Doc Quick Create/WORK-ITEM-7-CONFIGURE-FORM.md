# Work Item 7: Configure Quick Create Form

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 2 hours
**Prerequisites:** Work Item 8 (Build and Deploy Solution)
**Status:** Ready to Start

---

## Objective

Configure the Document entity's Quick Create form to include the UniversalQuickCreate PCF control and necessary fields. Enable Quick Create on Document subgrid.

---

## Context

Quick Create forms are simplified forms for rapid record creation from subgrids. Configuration involves:
1. Enable Quick Create for Document entity
2. Create/customize Quick Create form
3. Add PCF control to form
4. Add optional fields (Description, etc.)
5. Configure Document subgrid to use Quick Create
6. Publish customizations

**Result:** Users can click "+ New Document" on subgrid and see our custom PCF control.

---

## Implementation Steps

### Step 1: Enable Quick Create for Document Entity

1. Navigate to [Power Apps Maker Portal](https://make.powerapps.com)
2. Select your environment
3. Go to **Tables** → Find **Document** (sprk_document)
4. Click on table → **Properties**
5. Under **Advanced options**:
   - Enable **Quick create form**
   - Enable **Allow quick create**
   - Save

**Verification:** "Allow quick create" checkbox is checked.

---

### Step 2: Create or Open Quick Create Form

**Option A: Create New Form**
1. In Document table → **Forms**
2. Click **+ New form** → **Quick Create**
3. Name: "Document Quick Create"
4. Save

**Option B: Use Existing Form**
1. In Document table → **Forms**
2. Find form of type "Quick Create"
3. Open for editing

**Verification:** Form editor opens with "Quick Create" form type.

---

### Step 3: Add PCF Control to Form

1. In form editor, click **+ Component** or **+ Field**
2. Select field: **sprk_fileuploadmetadata** (the bound field)
3. Click **Add**
4. Field appears on form (default text input)

Now configure the control:
1. Click on the **sprk_fileuploadmetadata** field
2. In properties pane → **Controls** tab
3. Click **+ Add control**
4. Search for "Universal Quick Create" (Spaarke.Controls)
5. Select and click **Add**
6. Click **Configure control**

**Configuration:**
- **SDAP API Base URL**: `https://your-bff-api-url.com/api` (production URL)
- **Allow Multiple Files**: Yes (checkbox checked)
- **Enable File Upload**: Yes (checkbox checked)

7. Set control visibility:
   - **Web**: Select radio button for "Universal Quick Create"
   - **Phone**: (Optional) Select default control or same
   - **Tablet**: (Optional) Select default control or same

8. Click **OK**

**Verification:** "Universal Quick Create" control is active for Web.

---

### Step 4: Add Optional Fields to Form

Add these fields for better UX (drag from field list):

1. **sprk_documenttitle** (Document Title)
   - Label: "Title"
   - Required: Yes

2. **sprk_description** (Description)
   - Label: "Description"
   - Required: No
   - Multiline: Yes

3. **ownerid** (Owner)
   - Label: "Owner"
   - Required: No (defaults to current user)

**Form Layout:**
```
Document Quick Create
┌─────────────────────────────────────┐
│ Title: [_________________________]  │
│                                     │
│ File Upload: [PCF CONTROL HERE]    │
│                                     │
│ Description: [________________]     │
│              [________________]     │
│              [________________]     │
│                                     │
│ Owner: [Current User ▼]            │
└─────────────────────────────────────┘
          [Save and Create Documents]
```

**Keep form minimal** - Quick Create is for speed!

---

### Step 5: Configure Form Properties

1. In form editor → **Form Properties** (gear icon)
2. Set **Form name**: "Document Quick Create"
3. Set **Description**: "Quick create form with multi-file upload"
4. **Save** the form

---

### Step 6: Configure Document Subgrid

Now configure the subgrid to use Quick Create:

1. Navigate to **Matter** entity (or parent entity)
2. Open **Main Form** (Information or default form)
3. Find **Documents** subgrid on form
4. Click on subgrid → **Properties**
5. In **Controls** tab:
   - Ensure "Show Related Records" is selected
   - Entity: sprk_document
   - View: Active Documents (or default view)
6. In **Display** tab:
   - Enable **Show "New" button**
7. Save form

**Verification:** "+ New Document" button appears on subgrid.

---

### Step 7: Publish Customizations

**CRITICAL:** Changes only take effect after publishing!

1. Go to **Solutions** → Your solution
2. Click **Publish all customizations**
3. Wait for publish to complete

OR use maker portal:
1. Click **Publish** button (top right)
2. Confirm

**Verification:** "Published successfully" message appears.

---

### Step 8: Test Quick Create Flow

1. Open a **Matter** record (or parent entity)
2. Navigate to **Documents** tab
3. Click **+ New Document** button
4. Quick Create dialog opens
5. Verify:
   - PCF control visible
   - "+ Add File" button present
   - Title field visible
   - Description field visible
   - Custom "Save and Create Documents" button in footer (standard button hidden)

**If control doesn't appear:**
- Verify solution imported correctly (Work Item 8)
- Verify PCF control configured on field
- Verify field added to form
- Clear browser cache (Ctrl+F5)

---

## Troubleshooting

### Issue: Quick Create option not available
**Cause:** Entity doesn't support Quick Create
**Fix:** Enable "Allow quick create" in entity properties

### Issue: Control not in "Add control" list
**Cause:** Solution not imported or PCF not registered
**Fix:** Complete Work Item 8 first (build and deploy)

### Issue: Control shows as text field
**Cause:** Control not set as active for Web
**Fix:** In field properties → Controls → Select "Universal Quick Create" for Web

### Issue: Standard button still shows
**Cause:** CSS injection failed or footer not found
**Fix:** Check browser console for errors, verify button management code (Work Item 2)

### Issue: "+ New Document" button not on subgrid
**Cause:** Subgrid not configured to show New button
**Fix:** Edit subgrid properties → Display → Enable "Show New button"

---

## Configuration Checklist

- [ ] Quick Create enabled for Document entity
- [ ] Quick Create form created
- [ ] sprk_fileuploadmetadata field added to form
- [ ] Universal Quick Create control configured
- [ ] SDAP API Base URL set correctly
- [ ] Allow Multiple Files enabled
- [ ] Title field on form
- [ ] Description field on form (optional)
- [ ] Owner field on form (optional)
- [ ] Form saved
- [ ] Document subgrid shows "+ New" button
- [ ] Customizations published
- [ ] Quick Create opens when clicking "+ New Document"
- [ ] PCF control visible in Quick Create
- [ ] Custom button in footer

---

## Form Field Recommendations

### Minimal (Fastest):
- Title (required)
- File Upload (PCF control)

### Recommended:
- Title (required)
- File Upload (PCF control)
- Description (optional)
- Owner (defaults to current user)

### Advanced (Future):
- Title
- File Upload (PCF control)
- Description
- Owner
- Document Type (option set)
- Status (option set)

**Best Practice:** Keep Quick Create forms simple (5-7 fields max).

---

## Alternative: Using Xrm.Navigation API

For programmatic Quick Create (future enhancement):

```javascript
// Open Quick Create programmatically
Xrm.Navigation.openForm({
    entityName: "sprk_document",
    formType: 1,  // Quick Create
    useQuickCreateForm: true
}).then(
    function(result) {
        console.log("Record created:", result.savedEntityReference);
    },
    function(error) {
        console.error("Error:", error);
    }
);
```

---

## Verification Commands

```bash
# After publishing, test in browser
# 1. Navigate to Matter record
# 2. Documents tab
# 3. Click "+ New Document"
# 4. Verify PCF control loads
# 5. Select files
# 6. Verify custom button appears
# 7. Click and upload
# 8. Verify records created
```

---

## Next Steps

After form configuration:
1. Test end-to-end flow
2. Verify subgrid refresh works
3. Proceed to **Work Item 9** (Testing)

---

**Status:** Ready for implementation
**Time:** 2 hours
**Prerequisites:** Work Item 8 (solution deployed)
**Next:** Work Item 9 - End-to-End Testing
