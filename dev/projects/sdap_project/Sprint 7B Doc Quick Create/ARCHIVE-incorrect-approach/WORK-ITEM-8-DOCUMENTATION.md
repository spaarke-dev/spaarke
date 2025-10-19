# Work Item 8: Create Documentation

**Estimated Time:** 1-2 hours
**Prerequisites:** Work Item 7 complete (Testing complete)
**Status:** Ready to Start

---

## Objective

Create comprehensive documentation for administrators and end users.

---

## Context

Documentation ensures:
- Admins can configure the control correctly
- Users understand how to upload files
- Troubleshooting guides help resolve issues
- Future maintenance is easier

---

## Documents to Create

### 1. Admin Configuration Guide
**Purpose:** Help admins deploy and configure the PCF control

### 2. User Guide
**Purpose:** Help end users upload files from Quick Create forms

### 3. Troubleshooting Guide
**Purpose:** Help resolve common issues

### 4. Technical Reference
**Purpose:** Document architecture and technical details for developers

---

## Document 1: Admin Configuration Guide

**Location:** `docs/QUICK-CREATE-FILE-UPLOAD-ADMIN-GUIDE.md`

### Content Outline

```markdown
# Quick Create File Upload - Admin Guide

## Overview
- What is the SPE File Upload control?
- What does it do?
- When to use it?

## Prerequisites
- Power Platform admin access
- SharePoint Embedded containers provisioned
- SDAP API deployed
- Document entity with sprk_fileuploadmetadata field

## Deployment Steps

### Step 1: Import Solution
- Download solution.zip
- Import to environment
- Verify import success

### Step 2: Configure Quick Create Form
- Add sprk_fileuploadmetadata field
- Bind PCF control
- Set control parameters
- Hide metadata field label
- Publish form

### Step 3: Configure Control Parameters
- sdapApiBaseUrl: SDAP API URL
- allowMultipleFiles: true/false
- containerid: (optional) manual Container ID

### Step 4: Test Configuration
- Open Quick Create from Matter subgrid
- Verify Container ID detected
- Upload test file
- Verify Document created

## Configuration Examples

### Example 1: Standard Configuration (Matter → Document)
- Auto-detect Container ID from parent Matter
- Allow multiple files
- SDAP API: https://api.azurewebsites.net/api

### Example 2: Manual Container ID
- User enters Container ID manually
- Single file only
- Custom API URL

## Troubleshooting

### Issue: Control doesn't appear
- Solution: Verify solution imported, refresh browser

### Issue: Container ID not detected
- Solution: Add sprk_containerid field to form

### Issue: File upload fails
- Solution: Check SDAP API URL, verify authentication

## Security & Permissions
- User permissions required
- SharePoint container permissions
- API permissions

## Support & Resources
- Browser console logs
- Common error messages
- Contact information
```

---

## Document 2: User Guide

**Location:** `docs/QUICK-CREATE-FILE-UPLOAD-USER-GUIDE.md`

### Content Outline

```markdown
# Quick Create File Upload - User Guide

## Overview
How to upload files to SharePoint Embedded when creating Documents.

## Before You Start
- Ensure parent Matter has valid Container ID
- Prepare files to upload (PDF, Word, Excel, etc.)
- Check file size limits (if any)

## How to Upload Files

### Method 1: Click to Select Files

**Steps:**
1. Open Matter record
2. Scroll to Documents section
3. Click "+ New" button
4. Quick Create form opens
5. Enter Document Title (required)
6. Click "Choose Files" button
7. Select 1 or more files (hold Ctrl for multiple)
8. Click "Open"
9. Wait for upload to complete
10. Click "Save"

**Result:**
- Files uploaded to SharePoint
- New Document record created
- Files visible in Documents subgrid

### Method 2: Drag-and-Drop

**Steps:**
1. Open Quick Create form (steps 1-4 above)
2. Open File Explorer (separate window)
3. Select file(s)
4. Drag files over drop zone
5. Drop zone highlights (blue)
6. Drop files
7. Wait for upload to complete
8. Click "Save"

**Result:**
- Same as Method 1

## Upload Progress

**While uploading:**
- Progress message: "Uploading 2 of 5 files... (40%)"
- Progress bar fills from left to right
- Wait until all files complete

**After upload:**
- "Uploaded Files (3)" section shows files
- Green checkmark next to each file
- File name and size displayed

## Supported File Types

**Common Types:**
- PDF (.pdf)
- Word (.docx, .doc)
- Excel (.xlsx, .xls)
- PowerPoint (.pptx, .ppt)
- Images (.jpg, .png, .gif)
- Text (.txt)

**Check with your admin** for file size limits.

## Multiple Files

**To upload multiple files:**
1. Click "Choose Files"
2. Hold Ctrl (Windows) or Cmd (Mac)
3. Click multiple files
4. Click "Open"

**Or:**
1. Drag-and-drop multiple files at once

**Result:**
- All files upload to same Document record
- Single Document with multiple files

## Error Messages

### "No Container ID found"
**Meaning:** Parent Matter doesn't have SharePoint container

**Solution:** Contact admin to provision container for Matter

### "File upload failed"
**Meaning:** Upload to SharePoint failed

**Possible Causes:**
- Network connection lost
- File too large
- Insufficient permissions

**Solution:**
- Check network connection
- Try smaller file
- Contact admin for permissions

### "Authentication failed"
**Meaning:** Login to SharePoint failed

**Solution:**
- Refresh page
- Sign out and sign back in
- Contact admin if persists

## Tips & Best Practices

**Tip 1: Use descriptive file names**
- Good: "Client_Contract_2025-10-07.pdf"
- Bad: "document.pdf"

**Tip 2: Upload multiple files at once**
- Faster than creating multiple Documents
- All files grouped together

**Tip 3: Check file size before upload**
- Large files (>50 MB) may take time
- Split very large files if needed

**Tip 4: Wait for upload to complete**
- Don't close form until "Uploaded Files" section shows files
- Don't click Save until upload finishes

## FAQ

**Q: Can I upload any file type?**
A: Most common types supported. Check with admin for restrictions.

**Q: How many files can I upload at once?**
A: Up to 30 files recommended. More may exceed metadata limit.

**Q: Can I delete files after upload?**
A: Contact admin. Files stored in SharePoint, not Dataverse.

**Q: What if upload fails midway?**
A: Some files may succeed. Check "Uploaded Files" section. Retry failed files.

## Support

**If you encounter issues:**
1. Take screenshot of error message
2. Note what you were doing when error occurred
3. Contact your system administrator
4. Provide Matter record ID

**Browser Console Logs (for admins):**
- Press F12 to open developer tools
- Go to Console tab
- Copy error messages
- Send to admin for troubleshooting
```

---

## Document 3: Troubleshooting Guide

**Location:** `docs/QUICK-CREATE-FILE-UPLOAD-TROUBLESHOOTING.md`

### Content Outline

```markdown
# Quick Create File Upload - Troubleshooting Guide

## Common Issues

### Issue 1: PCF Control Doesn't Appear in Form

**Symptoms:**
- Field shows but no file upload UI
- Only text box visible

**Root Causes:**
- Solution not imported
- Control not bound to field
- Browser cache issue

**Solutions:**
1. Verify solution imported: Solutions > All > "UniversalQuickCreateSolution"
2. Open form designer, check control bound to sprk_fileuploadmetadata
3. Clear browser cache: Ctrl+Shift+R

**Verification:**
- Control UI shows drop zone and "Choose Files" button

---

### Issue 2: Container ID Not Detected

**Symptoms:**
- Warning: "No Container ID found"
- File picker disabled

**Root Causes:**
- Parent Matter has no Container ID
- Quick Create opened outside parent context
- Parent context not accessible

**Solutions:**
1. Check parent Matter: Open Matter, verify sprk_containerid field has value
2. Provision container: Use SPE provisioning tool
3. Manual entry: Add sprk_containerid field to form (visible)

**Browser Console:**
```
[FileUploadPCF] Parent record has no Container ID
```

---

### Issue 3: File Upload Fails

**Symptoms:**
- Error: "File upload failed"
- Upload starts but doesn't complete

**Root Causes:**
- SDAP API unreachable
- Network connection lost
- Authentication failed
- File too large

**Solutions:**
1. **Check API URL:**
   - Form designer > Control parameters > sdapApiBaseUrl
   - Test: https://your-api.azurewebsites.net/api/health
   - Should return 200 OK

2. **Check network:**
   - Verify internet connection
   - Check browser Network tab (F12)
   - Look for failed requests (red)

3. **Check authentication:**
   - Browser console: Look for MSAL errors
   - Clear cookies: Settings > Privacy > Clear browsing data
   - Sign out and sign back in

4. **Check file size:**
   - Verify file < API limit (e.g., 100 MB)
   - Try smaller test file

**Browser Console:**
```
[FileUploadService] File upload failed: Network error
OR
[MsalAuthProvider] Token acquisition failed
OR
[SdapApiClient] API request failed: 401 Unauthorized
```

---

### Issue 4: Save Button Doesn't Create Record

**Symptoms:**
- Click Save, form closes, no Document created

**Root Causes:**
- Required fields missing
- Form validation failed
- JavaScript error during save

**Solutions:**
1. **Check required fields:**
   - Document Title must be filled
   - Look for red validation messages

2. **Check browser console:**
   - F12 > Console tab
   - Look for JavaScript errors during save

3. **Check form permissions:**
   - User must have Create permission on Document entity
   - Verify security roles

**Verification:**
- After save, Document appears in subgrid
- Open Document, verify fields populated

---

### Issue 5: Metadata Field Empty After Upload

**Symptoms:**
- File uploads successfully
- Document created
- sprk_fileuploadmetadata field is empty

**Root Causes:**
- getOutputs() not called
- Metadata not serialized correctly
- Form closed before outputs written

**Solutions:**
1. **Check browser console:**
   ```
   [FileUploadPCF] getOutputs() called: { metadataCount: 1 }
   ```
   - If missing, getOutputs() not called

2. **Verify control binding:**
   - Form designer > sprk_fileuploadmetadata field
   - Check "SPE File Upload" control is bound

3. **Check timing:**
   - Wait for "Uploaded Files" section to show before saving
   - Don't close form immediately after upload

**Verification:**
- Open Document, check sprk_fileuploadmetadata field
- Should contain JSON array: [{ "driveItemId": "...", ... }]

---

### Issue 6: Authentication Popup Blocks Upload

**Symptoms:**
- MSAL popup appears
- User closes popup
- Upload fails

**Root Causes:**
- User not authenticated
- Token expired
- Popup blocker enabled

**Solutions:**
1. **Allow popup:**
   - Browser settings > Popups and redirects > Allow for this site
   - Look for popup blocked icon in address bar

2. **Complete authentication:**
   - When popup appears, sign in
   - Don't close popup
   - Wait for authentication to complete

3. **Use redirect mode (alternative):**
   - Update MSAL config: interactionType: "redirect"
   - No popup, uses page redirect instead

**Browser Console:**
```
[MsalAuthProvider] Token acquisition cancelled by user
```

---

### Issue 7: Drag-and-Drop Doesn't Work

**Symptoms:**
- Drag file over drop zone
- Drop zone doesn't highlight
- File doesn't upload

**Root Causes:**
- Browser doesn't support drag-and-drop
- Drop zone event handlers not attached
- Container ID missing (disabled)

**Solutions:**
1. **Check browser support:**
   - Use modern browser (Chrome, Edge, Firefox)
   - Update to latest version

2. **Use file picker instead:**
   - Click "Choose Files" button
   - Drag-and-drop is convenience feature, not required

3. **Check Container ID:**
   - Drop zone disabled if no Container ID
   - Fix Container ID issue first

**Verification:**
- Drag file over drop zone → highlights blue
- Drop file → upload starts

---

### Issue 8: Too Many Files Error

**Symptoms:**
- Error: "Too many files. Metadata exceeds 10,000 character limit."

**Root Causes:**
- sprk_fileuploadmetadata field limit: 10,000 characters
- Too many files uploaded (~40+ files)

**Solutions:**
1. **Reduce file count:**
   - Upload max 30 files per Document
   - Create multiple Documents for more files

2. **Future fix:**
   - Backend plugin to store metadata in separate entity
   - No user action needed

**Verification:**
- Upload < 30 files: Success
- Upload > 40 files: Error

---

## Diagnostic Tools

### Browser Console Logs

**To access:**
1. Press F12
2. Go to Console tab
3. Look for logs starting with:
   - [FileUploadPCF]
   - [FileUploadService]
   - [MsalAuthProvider]
   - [SdapApiClient]

**Key logs to check:**

**Successful upload:**
```
[FileUploadPCF] Container ID loaded successfully: { containerId: "..." }
[FileUploadPCF] Files selected for upload: { fileCount: 3 }
[FileUploadService] File uploaded successfully: { driveItemId: "..." }
[FileUploadPCF] Upload complete: { successCount: 3, failCount: 0 }
```

**Failed upload:**
```
[FileUploadService] File upload failed: Network error
[FileUploadPCF] Upload complete: { successCount: 1, failCount: 2 }
```

---

### Network Tab

**To access:**
1. Press F12
2. Go to Network tab
3. Click record button (red circle)
4. Reproduce issue

**What to check:**
- Failed requests (red)
- Request URL (correct API endpoint?)
- Status code (200=success, 401=auth, 404=not found, 500=server error)
- Response body (error details)

**Example failed request:**
```
POST https://api.azurewebsites.net/api/spe/upload
Status: 401 Unauthorized
Response: { "error": "Invalid access token" }
```

---

## Contact Support

**When contacting support, provide:**

1. **Error message** (screenshot or copy text)
2. **Steps to reproduce** (what you did before error)
3. **Browser console logs** (F12 > Console > copy all logs)
4. **Environment** (Test / Production)
5. **User info** (username, security role)
6. **Record info** (Matter ID, Document ID if created)

**Example support request:**
```
Subject: File upload fails with Network error

Description:
I tried to upload 3 files from Matter TM-2025-001 but got error "File upload failed: Network error".

Steps to reproduce:
1. Opened Matter TM-2025-001
2. Clicked "+ New" in Documents subgrid
3. Selected 3 PDF files
4. Upload started but failed after 10 seconds

Browser console logs:
[FileUploadService] File upload failed: Network error

Environment: Test
User: john.doe@company.com
Matter ID: abc-123-def-456
```
```

---

## Document 4: Technical Reference

**Location:** `docs/QUICK-CREATE-FILE-UPLOAD-TECHNICAL-REFERENCE.md`

### Content Outline

```markdown
# Quick Create File Upload - Technical Reference

## Architecture Overview

### Components
- FileUploadPCF.ts: Main PCF control
- FileUploadField.tsx: React UI component
- FileUploadService.ts: File upload service
- MsalAuthProvider.ts: Authentication provider
- SdapApiClient.ts: API client

### Data Flow
```
User selects files
    ↓
FileUploadField.tsx (UI)
    ↓
FileUploadPCF.ts (PCF control)
    ↓
FileUploadService.ts
    ↓
SdapApiClient.ts (MSAL token injected)
    ↓
SDAP BFF API
    ↓
SharePoint Embedded
    ↓
SPE metadata returned
    ↓
FileUploadPCF.getOutputs()
    ↓
sprk_fileuploadmetadata field (JSON array)
    ↓
Quick Create form saves
    ↓
Document record created
```

## Control Manifest

### Properties

**speMetadata (bound field)**
- Type: Multiple Lines of Text
- Usage: bound
- Purpose: Stores SPE metadata JSON array

**sdapApiBaseUrl (input parameter)**
- Type: SingleLine.Text
- Usage: input
- Required: true
- Default: https://localhost:7299/api
- Purpose: SDAP API endpoint URL

**allowMultipleFiles (input parameter)**
- Type: TwoOptions
- Usage: input
- Required: false
- Default: true
- Purpose: Enable/disable multi-file selection

**containerid (input parameter)**
- Type: SingleLine.Text
- Usage: input
- Required: false
- Purpose: Manual Container ID override

## Field Binding

### sprk_fileuploadmetadata Field

**Type:** Multiple Lines of Text (10,000 characters)

**Purpose:** Temporary storage for SPE metadata during Document creation

**Lifecycle:**
1. **T0:** User opens Quick Create → field is NULL
2. **T1:** User uploads files → field contains JSON array
3. **T2:** User clicks Save → Document created with metadata
4. **T3:** Backend plugin processes (future sprint) → field cleared

**Format:**
```json
[
    {
        "driveItemId": "01ABC...",
        "fileName": "contract.pdf",
        "fileSize": 2097152,
        "sharePointUrl": "https://...",
        "webUrl": "https://...",
        "createdDateTime": "2025-10-07T12:00:00Z",
        "lastModifiedDateTime": "2025-10-07T12:00:00Z"
    }
]
```

## API Integration

### SDAP BFF API

**Endpoint:** POST /api/spe/upload

**Request:**
```
Content-Type: multipart/form-data
Authorization: Bearer {msalToken}

Body:
    file: {file binary}
    containerId: {guid}
    fileName: {string}
```

**Response (Success):**
```json
{
    "driveItemId": "01ABC...",
    "fileName": "contract.pdf",
    "fileSize": 2097152,
    "sharePointUrl": "https://...",
    "webUrl": "https://...",
    "createdDateTime": "2025-10-07T12:00:00Z",
    "lastModifiedDateTime": "2025-10-07T12:00:00Z"
}
```

**Response (Error):**
```json
{
    "error": "Container not found",
    "statusCode": 404
}
```

## Authentication

### MSAL Configuration

**Library:** @azure/msal-browser v4.24.1

**Config:**
```typescript
{
    auth: {
        clientId: "[Azure AD App ID]",
        authority: "https://login.microsoftonline.com/[tenantId]",
        redirectUri: window.location.origin
    },
    cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false
    }
}
```

**Token Caching:**
- First request: ~200-500 ms (acquire token)
- Subsequent requests: ~5 ms (cached token)
- Cache location: sessionStorage
- Cache lifetime: Until browser tab closes

## Performance Metrics

### Upload Performance

**Single File (1 MB):**
- First upload: ~1-2 seconds (includes MSAL)
- Subsequent: ~0.5-1 seconds (cached token)

**Multiple Files (3 files, 1 MB each):**
- First upload: ~3-4 seconds
- Subsequent: ~2-3 seconds

**Large File (50 MB):**
- Upload time: ~10-30 seconds (depends on network)

### Bundle Size

**Production Build:**
- Control bundle: ~723 KB (minified)
- Includes: React, Fluent UI, MSAL

## Browser Compatibility

**Supported Browsers:**
- ✅ Chrome 90+
- ✅ Edge 90+
- ✅ Firefox 88+
- ✅ Safari 14+ (macOS/iOS)

**Required Features:**
- ES6 support
- Drag-and-drop API
- FormData API
- Fetch API
- SessionStorage

## Error Codes

### HTTP Status Codes

**200 OK:** Upload successful
**400 Bad Request:** Invalid request (missing file, Container ID, etc.)
**401 Unauthorized:** Authentication failed (invalid/expired token)
**403 Forbidden:** Insufficient permissions
**404 Not Found:** Container not found or API endpoint wrong
**500 Internal Server Error:** Server error (check SDAP API logs)

### Custom Error Messages

**"Container ID not found":** Parent Matter has no sprk_containerid value
**"File upload service not initialized":** MSAL or API client failed to initialize
**"Network error":** Network connection lost during upload
**"Too many files":** Metadata exceeds 10,000 character limit

## Logging

### Console Logs

**Format:** `[ComponentName] Message: { details }`

**Log Levels:**
- **Info:** Normal operations
- **Warn:** Non-critical issues
- **Error:** Failures

**Examples:**
```
[FileUploadPCF] Container ID loaded successfully: { containerId: "..." }
[FileUploadService] File uploaded successfully: { driveItemId: "..." }
[MsalAuthProvider] Token retrieved from cache (5ms)
```

## Security Considerations

### Authentication
- MSAL handles OAuth 2.0 flow
- Access tokens stored in sessionStorage (not localStorage)
- Tokens expire after 1 hour (auto-renewed)

### Authorization
- User must have:
  - Create permission on Document entity (Dataverse)
  - Write permission on SharePoint container (SharePoint)

### Data Privacy
- File metadata stored in Dataverse (sprk_fileuploadmetadata field)
- File content stored in SharePoint Embedded (not in Dataverse)
- Metadata cleared by backend plugin (future sprint)

## Extensibility

### Adding New Parameters

**Steps:**
1. Update ControlManifest.Input.xml
2. Add property definition
3. Rebuild control: `npm run build`
4. Update FileUploadPCF.ts to read parameter
5. Redeploy solution

**Example:**
```xml
<property name="maxFileSize"
          display-name-key="Max_File_Size"
          of-type="Whole.None"
          usage="input"
          required="false"
          default-value="104857600" />
```

### Customizing UI

**File:** `components/FileUploadField.tsx`

**Styling:** Uses Fluent UI makeStyles (CSS-in-JS)

**Example: Change drop zone color:**
```typescript
dropZone: {
    backgroundColor: tokens.colorBrandBackground2,  // Change this
    border: `2px dashed ${tokens.colorBrandStroke1}`
}
```

## Future Enhancements

### Phase 1 (Current Sprint)
- ✅ File upload to SPE
- ✅ Multi-file support
- ✅ Quick Create form integration

### Phase 2 (Future Sprint)
- ⏳ Backend field inheritance plugin
- ⏳ Auto-populate Matter lookup
- ⏳ Auto-clear metadata field

### Phase 3 (Future)
- ⏳ Parallel file upload (faster)
- ⏳ File preview before upload
- ⏳ Drag-and-drop reordering
- ⏳ Cancel individual files during upload
```

---

## Documentation Checklist

After creating all documents:

- ✅ Admin Configuration Guide created
- ✅ User Guide created
- ✅ Troubleshooting Guide created
- ✅ Technical Reference created
- ✅ Screenshots added (if applicable)
- ✅ Code examples tested
- ✅ Links to other docs verified
- ✅ Spelling/grammar checked
- ✅ Docs reviewed by peer (optional)
- ✅ Docs published to team location

---

## Next Steps

After completing documentation:

1. ✅ All 4 documents created
2. ✅ Publish docs to team wiki or SharePoint
3. ✅ Share docs with admins and users
4. ✅ Sprint 7B complete! 🎉

---

**Status:** Ready for implementation
**Estimated Time:** 1-2 hours
**Next:** Sprint Complete - Move to Production Deployment
