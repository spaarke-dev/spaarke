# Quick Create Form Configuration Guide

**Sprint:** 7B - Document Quick Create
**Control:** UniversalQuickCreate PCF
**Environment:** Spaarke Dev 1 (https://spaarkedev1.crm.dynamics.com/)
**Last Updated:** 2025-10-07

---

## Issue: PCF Control Not Available in Quick Create Form

### Problem

When configuring a Quick Create form, the UniversalQuickCreate PCF control does not appear in the "Add control" list, even though:
- ✅ Solution is imported successfully
- ✅ Control appears in Solutions → Controls
- ✅ Control works on main forms

### Root Cause

**PCF controls have different visibility rules for different form types.** By default, custom controls are only available on **main forms**, not Quick Create forms.

### Quick Create Form Limitations

Quick Create forms have restrictions:
1. **Field-level controls only** - Cannot use dataset-bound controls
2. **Limited control availability** - Must explicitly support Quick Create
3. **Simplified UI** - Reduced customization options

---

## Solution

The control is now configured correctly and deployed as an **Unmanaged** solution. Here's how to add it to your Quick Create form:

---

## Step-by-Step Configuration

### Step 1: Access Power Apps Maker Portal

1. Navigate to https://make.powerapps.com
2. Select **Spaarke Dev 1** environment (top-right dropdown)
3. Verify you're in the correct environment

---

### Step 2: Enable Quick Create on Document Entity

1. Go to **Tables** (left navigation)
2. Search for **Document** (sprk_document)
3. Click on the Document table
4. Click **Properties** (top toolbar)
5. Scroll to **Advanced options**
6. Enable **Allow quick create**
7. Click **Save**

**Verification:** "Allow quick create" checkbox is checked

---

### Step 3: Create or Open Quick Create Form

**Option A: Create New Quick Create Form**

1. In Document table → **Forms** tab
2. Click **+ New form** → **Quick Create**
3. Name: "Document Quick Create"
4. Description: "Multi-file upload Quick Create form"
5. Click **Save**

**Option B: Open Existing Quick Create Form**

1. In Document table → **Forms** tab
2. Find existing form with Type = "Quick Create"
3. Click to open in form designer

---

### Step 4: Add Required Field

**Critical:** The PCF control must be bound to a field. We use `sprk_filename` (an existing Single Line Text field on the Document entity).

1. In form designer, from the **Table columns** pane (left side)
2. Find field: **sprk_filename** (File Name)
   - This is an existing field on the Document entity
   - Type: Single Line of Text (MaxLength: 1000)
   - Required for PCF binding
3. Drag the field onto the form
4. Place it in a section called "Upload File" (or similar)
5. The field will appear as a standard text input (we'll replace this with the PCF control in the next step)

**Important Notes:**
- The field value will be automatically populated by the PCF control after file upload
- You can optionally hide the field label if desired (configure in field properties)
- The control will completely replace the standard text input UI

---

### Step 5: Configure PCF Control on Field

Now replace the standard text input with our custom control:

1. Click on the **sprk_fileuploadmetadata** field on the form
2. In the right properties pane → **Components** tab
3. Click **+ Component**
4. Search for: **Universal Quick Create**
   - Should now appear in the list
   - Published by: Spaarke.Controls
5. Click **Add**

**If control doesn't appear:**
- Verify solution imported (pac solution list)
- Publish all customizations (maker portal)
- Clear browser cache (Ctrl+F5)
- Sign out and sign back in

---

### Step 6: Configure Control Properties

After adding the control, configure its properties:

1. Select the control in the Components list
2. Click **Configure** or click the control in the properties pane

**Required Properties:**

| Property | Value | Description |
|----------|-------|-------------|
| **SDAP API Base URL** | `https://localhost:7299/api` (dev)<br>or production URL | SDAP BFF API endpoint |
| **Allow Multiple Files** | `Yes` (checked) | Enable multi-file selection |
| **Enable File Upload** | `Yes` (checked) | Enable file upload feature |

**Optional Properties:**

| Property | Value | Description |
|----------|-------|-------------|
| **Default Value Mappings** | (JSON) | Parent field → child field mappings |

**Default Value Mappings Example:**
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  }
}
```

3. Click **Done**

---

### Step 7: Set Control Visibility

Configure where the control appears:

1. With control still selected in Components
2. Look for **Visibility** or **Form factors** section
3. Set visibility:
   - **Web:** Universal Quick Create (radio button selected)
   - **Phone:** (Optional) Same or default
   - **Tablet:** (Optional) Same or default

**Important:** Ensure **Web** is set to "Universal Quick Create", not the default text control.

---

### Step 8: Hide Field Label (Optional but Recommended)

Since the PCF control has its own UI, the field label is redundant:

1. Click on the **sprk_fileuploadmetadata** field
2. In properties pane → **Display** tab
3. Uncheck **Show label**
4. Adjust field width to fill available space

**Result:** Clean UI with just the file upload component visible.

---

### Step 9: Add Additional Fields (Recommended)

For better UX, add these fields to the Quick Create form:

**Required Fields:**

1. **sprk_documenttitle** (Document Title)
   - Label: "Title"
   - Required: Yes
   - Display prominently

**Optional Fields:**

2. **sprk_description** (Description)
   - Label: "Description"
   - Type: Multi-line text
   - Required: No

3. **ownerid** (Owner)
   - Label: "Owner"
   - Required: No
   - Defaults to current user

**Form Layout Example:**

```
┌─────────────────────────────────────────────┐
│ Document Quick Create                        │
├─────────────────────────────────────────────┤
│                                              │
│ Title: [_____________________________] *     │
│                                              │
│ [PCF File Upload Component]                 │
│ ┌────────────────────────────────┐          │
│ │ + Add File                     │          │
│ │                                │          │
│ │ Selected Files:                │          │
│ │ • document.pdf (1.2 MB) [X]    │          │
│ │ • report.docx (856 KB)  [X]    │          │
│ └────────────────────────────────┘          │
│                                              │
│ Description:                                 │
│ ┌────────────────────────────────┐          │
│ │                                │          │
│ │                                │          │
│ └────────────────────────────────┘          │
│                                              │
│ Owner: [Current User ▼]                     │
│                                              │
└─────────────────────────────────────────────┘
        [Save and Create Documents] [Cancel]
```

---

### Step 10: Save and Publish Form

1. Click **Save** (top toolbar)
2. Click **Publish** (top toolbar)
3. Wait for "Published successfully" message

**Critical:** Changes only take effect after publishing!

---

### Step 11: Configure Document Subgrid (if not already done)

Enable the "+ New" button on the parent entity's subgrid:

1. Open **Matter** entity (or parent entity)
2. Go to **Forms** → Open main form (Information or default)
3. Find **Documents** subgrid on the form
4. Click subgrid → **Properties**
5. In **Display** tab:
   - ✅ Enable **Show "New" button**
6. **Save** and **Publish** the form

**Verification:** "+ New Document" button appears on Documents subgrid

---

## Testing Your Configuration

### Test 1: Quick Create Opens

1. Open a Matter record
2. Navigate to **Documents** tab
3. Click **+ New Document** on subgrid
4. Quick Create dialog opens

**Expected:** Form opens in modal dialog

---

### Test 2: PCF Control Visible

1. In Quick Create dialog
2. Verify PCF control is visible (not standard text box)
3. Should show: "+ Add File" button

**If text box appears instead:**
- Control visibility not set to "Web"
- Customizations not published
- Browser cache issue (clear with Ctrl+F5)

---

### Test 3: Custom Button Appears

1. Look at form footer
2. Standard "Save and Close" button should be **hidden**
3. Custom button should appear: **"Select Files to Continue"** (gray, disabled)

**If standard button still visible:**
- Button management code issue
- CSS injection failed
- Check browser console for errors (F12)

---

### Test 4: File Selection Works

1. Click **+ Add File** button
2. File picker opens
3. Select one or more files
4. Files appear in list with name and size
5. Custom button updates: **"Save and Create N Documents"** (blue, enabled)

**If button doesn't enable:**
- handleFilesChange callback not firing
- Check browser console for errors

---

### Test 5: Upload and Record Creation

1. Click **"Save and Create N Documents"**
2. Progress UI appears showing upload status
3. Each file shows status: pending → uploading → complete (green check)
4. Form closes automatically after completion
5. Documents subgrid refreshes
6. New records appear in subgrid

**If upload fails:**
- SDAP API not accessible
- Network connectivity issue
- Check browser console for error details

---

## Troubleshooting

### Issue: Control Not in "Add Component" List

**Possible Causes:**
1. Solution not imported
2. Customizations not published
3. Control not configured for Quick Create forms
4. Browser cache

**Solutions:**
```bash
# 1. Verify solution imported
pac solution list --environment https://spaarkedev1.crm.dynamics.com/ | grep UniversalQuickCreate

# 2. Check if managed or unmanaged
# Should show: UniversalQuickCreateSolution ... 1.0.1 False
# (False = Unmanaged, True = Managed)

# 3. Re-import if needed
cd src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
pac solution import --path bin/Release/UniversalQuickCreateSolution.zip --environment https://spaarkedev1.crm.dynamics.com/ --async
```

4. Publish all customizations in maker portal
5. Clear browser cache (Ctrl+F5)
6. Sign out and sign back in

---

### Issue: Standard "Save and Close" Still Visible

**Cause:** CSS injection failed or timing issue

**Solution:**
1. Open browser console (F12)
2. Check for errors in ButtonManagement
3. Verify CSS style element injected:
   ```javascript
   document.getElementById('spaarke-hide-quickcreate-buttons')
   ```
4. If missing, button management not initializing
5. Check control loaded successfully (no JavaScript errors)

---

### Issue: Form Data Not Included in Records

**Cause:** Form data extraction failing

**Check:**
1. Browser console for errors
2. Verify `window.Xrm.Page` accessible
3. Check getFormData() method in PCF code

**Common Issue:** Quick Create forms have limited Xrm API access

---

### Issue: Subgrid Doesn't Refresh

**Cause:** Parent context not extracted or refresh method failed

**Solutions:**
1. Manual refresh: Click refresh icon on subgrid
2. Check extractParentContext() in PCF code
3. Verify parent window accessible (Quick Create is in iframe)

---

### Issue: Upload Fails with Network Error

**Cause:** SDAP API not accessible

**Check:**
1. SDAP API Base URL configured correctly
2. API running and accessible
3. Network connectivity
4. CORS configuration on API

**Test API:**
```bash
# From browser console
fetch('https://localhost:7299/api/health')
  .then(r => r.json())
  .then(console.log)
```

---

## Advanced Configuration

### Custom Field Mappings

Configure parent → child field mappings in control properties:

```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid",
    "sprk_matternumber": "sprk_referencenumber"
  }
}
```

**Format:**
- Key: Parent entity logical name
- Value: Object with parent field → child field mappings

---

### Production API URL

For production deployment, update SDAP API Base URL:

**Development:**
```
https://localhost:7299/api
```

**Production:**
```
https://api.spaarke.com/sdap/api
```
(Update with actual production URL)

---

### Form Customization

**Recommended Form Settings:**

1. **Form Type:** Quick Create
2. **Max width:** 600px (default)
3. **Show header:** Yes
4. **Show footer:** Yes (for custom button)
5. **Number of columns:** 1 (single column for simplicity)

**Field Order:**
1. Title (required)
2. File Upload (PCF control)
3. Description (optional)
4. Owner (optional)

**Visual Hierarchy:**
- Most important fields at top
- File upload prominent
- Optional fields at bottom

---

## Managed vs Unmanaged Solutions

### Current Deployment: Unmanaged

**Advantages:**
- ✅ Can modify control in environment
- ✅ Easy to update and iterate
- ✅ Good for development/testing

**Disadvantages:**
- ⚠️ Cannot be cleanly uninstalled
- ⚠️ Not recommended for production

### For Production: Use Managed

To create managed solution:

1. Edit `UniversalQuickCreateSolution.cdsproj`
2. Change:
   ```xml
   <SolutionPackageType>Managed</SolutionPackageType>
   ```
3. Rebuild and redeploy

**Benefits:**
- Protects intellectual property
- Clean uninstall capability
- Version upgrade support

---

## Version History

| Version | Date | Changes | Package Type |
|---------|------|---------|--------------|
| 1.0.0 | 2025-10-07 | Initial release | Managed |
| 1.0.1 | 2025-10-07 | Added Quick Create support | Unmanaged |

---

## Quick Reference

### Control Information

- **Namespace:** Spaarke.Controls
- **Name:** UniversalQuickCreate
- **Version:** 1.0.1
- **Bound Field:** sprk_fileuploadmetadata
- **Package Type:** Unmanaged (for dev)

### Required Configuration

- [x] Solution imported
- [x] Quick Create enabled on Document entity
- [x] Quick Create form created
- [x] sprk_fileuploadmetadata field added
- [x] UniversalQuickCreate control configured
- [x] SDAP API Base URL set
- [x] Allow Multiple Files enabled
- [x] Form published
- [x] Subgrid configured with "+ New" button

---

## Support

**For configuration issues:**
1. Check this troubleshooting guide
2. Verify all configuration steps completed
3. Check browser console for errors (F12)
4. Review [SPRINT-7B-IMPLEMENTATION-STATUS.md](./SPRINT-7B-IMPLEMENTATION-STATUS.md)

**For deployment issues:**
- See [KM-DATAVERSE-SOLUTION-DEPLOYMENT.md](../../../docs/KM-DATAVERSE-SOLUTION-DEPLOYMENT.md)

---

**Last Updated:** 2025-10-07
**Tested On:** Spaarke Dev 1 environment
**Status:** ✅ Deployed and Ready for Configuration
