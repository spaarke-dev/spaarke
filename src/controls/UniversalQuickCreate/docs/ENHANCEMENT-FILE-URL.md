# Enhancement: Add File URL to Document Records

**Feature**: Store SharePoint file URL in Document records to enable "Open in SharePoint" links and iFrame file viewers.

**Version**: v3.0.6
**Date**: 2025-01-20
**Status**: ✅ Implemented

---

## 📋 Overview

This enhancement adds the SharePoint Embedded file URL to Document records when files are uploaded. The URL enables:

1. **Direct file access** - "Open in SharePoint" clickable links
2. **iFrame viewers** - Embed files directly in Dataverse forms
3. **SharePoint integration** - Deep linking to files in SharePoint Embedded containers

---

## 🎯 Business Value

### Use Cases

1. **Quick Access**
   - Users click URL in Document record to open file in SharePoint
   - No need to download file first

2. **File Previews**
   - Embed file viewers in Document form using iFrame
   - Preview PDFs, Office docs, images directly in Dataverse

3. **SharePoint Navigation**
   - Navigate from Dataverse to SharePoint container
   - See files in their SharePoint context

---

## 🏗️ Technical Implementation

### Changes Made

#### **1. DocumentRecordService.ts** ([Line 195](../UniversalQuickCreate/services/DocumentRecordService.ts#L195))

Added `sprk_filepath` field to record creation payload:

```typescript
// SharePoint file URL (enables "Open in SharePoint" links and iFrame viewers)
sprk_filepath: file.webUrl || null,
```

**Field Mapping**:
- **Source**: `SpeFileMetadata.webUrl` (from SDAP API response)
- **Target**: `sprk_filepath` (Dataverse URL field)
- **Type**: URL/String
- **Example**: `"https://spaarke.sharepoint.com/..."`

---

## 📊 Data Flow

```
User uploads file
  ↓
MultiFileUploadService → FileUploadService → SdapApiClient
  ↓
SDAP BFF API uploads to SharePoint Embedded
  ↓
API Response (SpeFileMetadata)
  {
    id: "01ABC...",
    name: "document.pdf",
    size: 102400,
    webUrl: "https://spaarke.sharepoint.com/..." ← **URL**
  }
  ↓
DocumentRecordService.buildRecordPayload()
  ↓
Dataverse WebAPI create sprk_document
  {
    sprk_filename: "document.pdf",
    sprk_filesize: 102400,
    sprk_graphitemid: "01ABC...",
    sprk_graphdriveid: "b!container-id...",
    sprk_filepath: "https://spaarke.sharepoint.com/..." ← **Stored**
  }
```

---

## 🗄️ Dataverse Schema

### Field: `sprk_filepath`

| Property | Value |
|----------|-------|
| **Schema Name** | `sprk_filepath` |
| **Display Name** | File Path / SharePoint URL |
| **Type** | URL (Single Line of Text with URL format) |
| **Required** | No |
| **Description** | SharePoint Embedded file URL for direct access |
| **Auto-populated** | Yes (during upload via PCF) |

**Expected Format**:
```
https://spaarke.sharepoint.com/contentstorage/CSP_container-id/Document.pdf
```

---

## ✅ Verification Steps

### 1. **Verify Field Exists in Dataverse**

```bash
# Check if sprk_filepath field exists on sprk_document entity
pac entity show --entity-name sprk_document --columns sprk_filepath
```

**Expected**: Field should exist with type URL/Text

### 2. **Test File Upload**

1. Open a Matter/Project/Invoice record
2. Click "Add Documents" ribbon button
3. Upload a test file (e.g., test.pdf)
4. Click "Save and Create Documents"

### 3. **Verify Field Populated**

Open created Document record and verify:

```
Field: sprk_filepath
Value: https://spaarke.sharepoint.com/contentstorage/CSP_[container-id]/test.pdf
```

**Browser Console Check**:
```javascript
// Check Document record
Xrm.Page.data.entity.getId(); // Get record ID
Xrm.Page.getAttribute("sprk_filepath").getValue(); // Should show URL
```

### 4. **Test URL Functionality**

Click the URL in Dataverse:
- ✅ Opens file in SharePoint
- ✅ Shows file in browser (if supported type like PDF)
- ✅ Shows SharePoint context (container, breadcrumb)

---

## 🔧 Configuration for iFrame Viewer

To embed file viewer in Document form:

### Option A: Direct URL Field (Clickable Link)

1. Open Document form in Form Designer
2. Add `sprk_filepath` field to form
3. Set Control: **URL** (default)
4. Users click link → Opens in new tab

### Option B: iFrame Viewer (Embedded Preview)

1. Add Web Resource control to form
2. Create custom HTML with iFrame:

```html
<iframe
  id="fileViewer"
  style="width: 100%; height: 600px; border: none;"
  src="">
</iframe>

<script>
  function loadFileUrl() {
    const fileUrl = parent.Xrm.Page.getAttribute("sprk_filepath").getValue();
    if (fileUrl) {
      document.getElementById("fileViewer").src = fileUrl;
    }
  }

  parent.Xrm.Page.data.entity.addOnLoad(loadFileUrl);
  parent.Xrm.Page.getAttribute("sprk_filepath").addOnChange(loadFileUrl);
  loadFileUrl();
</script>
```

3. Pass `sprk_filepath` as data parameter

**Supported File Types** (for iFrame preview):
- ✅ PDF
- ✅ Images (PNG, JPG, GIF)
- ✅ Office docs (if SharePoint viewer enabled)
- ❌ Executables, archives (download only)

---

## 📝 Code References

### Modified Files

1. **`DocumentRecordService.ts`** - Added sprk_filepath field
   - [Line 195](../UniversalQuickCreate/services/DocumentRecordService.ts#L195)
   - Method: `buildRecordPayload()`

### Supporting Files (No Changes Needed)

2. **`FileUploadService.ts`** - Already normalizes webUrl
   - [Line 70](../UniversalQuickCreate/services/FileUploadService.ts#L70)
   - Sets `sharePointUrl: apiResponse.webUrl`

3. **`types/index.ts`** - Type already includes webUrl
   - [Line 189](../UniversalQuickCreate/types/index.ts#L189)
   - `webUrl?: string`

4. **`SdapApiClient.ts`** - API returns webUrl
   - [Line 72](../UniversalQuickCreate/services/SdapApiClient.ts#L72)
   - Returns `SpeFileMetadata` with webUrl

---

## 🧪 Testing Scenarios

### Test Case 1: Standard Upload
**Steps**:
1. Upload single file via PCF
2. Verify Document record created
3. Check `sprk_filepath` has value
4. Click URL → Opens in SharePoint

**Expected**:
- ✅ Field populated with valid URL
- ✅ URL opens file in browser
- ✅ URL format: `https://spaarke.sharepoint.com/...`

### Test Case 2: Multi-File Upload
**Steps**:
1. Upload 5 files via PCF
2. Verify 5 Document records created
3. Check each has unique `sprk_filepath`

**Expected**:
- ✅ Each record has different URL
- ✅ URLs match file names
- ✅ All URLs valid and accessible

### Test Case 3: Missing webUrl (API Edge Case)
**Steps**:
1. Mock SDAP API to return null webUrl
2. Upload file
3. Verify record still creates

**Expected**:
- ✅ Record creates successfully
- ✅ `sprk_filepath` = null (graceful degradation)
- ✅ Other fields still populated

### Test Case 4: Special Characters in Filename
**Steps**:
1. Upload file: `Test Document (2024) - Final.pdf`
2. Verify URL encoding correct

**Expected**:
- ✅ URL properly encoded
- ✅ URL still works (opens file)
- ✅ Filename readable in SharePoint

---

## 🐛 Troubleshooting

### Issue: sprk_filepath is null/empty

**Possible Causes**:
1. SDAP API not returning webUrl
2. Field doesn't exist in Dataverse
3. User lacks permissions

**Debug Steps**:
```javascript
// Check API response in browser console
// Look for: [FileUploadService] File uploaded successfully
// Should show: webUrl: "https://..."

// Check payload sent to Dataverse
// Look for: [DocumentRecordService] Payload:
// Should show: sprk_filepath: "https://..."
```

**Solution**:
1. Verify SDAP BFF API version includes webUrl in response
2. Check Dataverse field exists: `pac entity show --entity-name sprk_document`
3. Grant user write permission to sprk_filepath field

### Issue: URL doesn't open file

**Possible Causes**:
1. User lacks SharePoint permissions
2. Container access revoked
3. File deleted from SharePoint

**Solution**:
1. Check user has read access to SharePoint container
2. Verify container still exists in SharePoint admin
3. Test URL in incognito browser (rules out browser cache)

---

## 📚 Related Documentation

- [SDAP API Specification](../../../../dev/projects/sdap_project/Sprint%207_Dataset%20Grid%20to%20SDAP/SPRINT-7-MASTER-RESOURCE.md)
- [UniversalDatasetGrid File URL Implementation](../../UniversalDatasetGrid/docs/FILE-URL-FIELD.md)
- [SharePoint Embedded Documentation](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/)

---

## 🚀 Future Enhancements

### Potential Additions

1. **File Type Icons**
   - Detect file type from URL
   - Show appropriate icon in grid
   - Color code by category

2. **Preview Thumbnails**
   - Generate thumbnails via Graph API
   - Store thumbnail URL in separate field
   - Show in grid view

3. **Version History**
   - Track file URL changes
   - Link to SharePoint version history
   - Show "modified by" from SharePoint

4. **Broken Link Detection**
   - Periodic job to validate URLs
   - Flag deleted files
   - Update status field

---

## ✅ Deployment Checklist

- [ ] Verify `sprk_filepath` field exists on `sprk_document` entity
- [ ] Field type is URL (Single Line of Text)
- [ ] Field is on Document form (visible or hidden)
- [ ] Security role has write permission to field
- [ ] Deploy PCF control v3.0.6
- [ ] Test upload → verify field populated
- [ ] Test URL click → opens file in SharePoint
- [ ] Update any custom views to include sprk_filepath column
- [ ] Document how to configure iFrame viewers (if needed)

---

## 📝 Version History

| Version | Date | Changes |
|---------|------|---------|
| v3.0.6 | 2025-01-20 | Added sprk_filepath field to DocumentRecordService |

---

**Questions or Issues?**
Contact: Development Team
Last Updated: 2025-01-20
