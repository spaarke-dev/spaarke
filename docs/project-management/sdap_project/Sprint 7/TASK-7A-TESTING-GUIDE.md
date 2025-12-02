# Sprint 7A - Universal Dataset Grid Testing Guide

**Date**: 2025-10-06
**Status**: Ready for Testing
**Test Container**: `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`

---

## Prerequisites Checklist

Before testing, verify:

- [x] ✅ SDAP BFF API is operational
- [x] ✅ Dataverse connection working
- [x] ✅ Universal Dataset Grid PCF control deployed
- [x] ✅ CORS configured for Power Apps domain
- [ ] ⏭️ Test user has access to Power Apps environment
- [ ] ⏭️ Test files uploaded to SharePoint container
- [ ] ⏭️ Document records created in Dataverse with file metadata

---

## Step 1: Verify PCF Control is Available

### 1.1 Open Power Apps Maker Portal

Navigate to: https://make.powerapps.com

**Environment**: SPAARKE DEV 1

### 1.2 Check Control is Deployed

1. Go to **Solutions**
2. Find **UniversalDatasetGridSolution** (Version 1.0)
3. Status should show: **Managed**
4. Open the solution
5. Verify component: **Spaarke.UniversalDatasetGrid**

**Expected**: Control appears in solution

---

## Step 2: Prepare Test Data

### 2.1 Verify Container Has Files

You mentioned this container already has files:
```
b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy
```

**To verify files exist**, we need to check via Power Apps or Graph API (requires authentication).

### 2.2 Create Test Document Records in Dataverse

You'll need to create `sprk_document` records that reference files in the container.

**Required fields**:
```json
{
  "sprk_documentname": "Test File 1",
  "sprk_containerid": "{container-guid}",
  "sprk_hasfile": true,
  "sprk_filename": "test-file-1.txt",
  "sprk_filesize": 1024,
  "sprk_mimetype": "text/plain",
  "sprk_graphdriveid": "b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy",
  "sprk_graphitemid": "{item-id-from-sharepoint}",
  "statuscode": 1
}
```

**How to create records**:

#### Option A: Via Power Apps (Recommended for testing)

1. Open **Dataverse** in Power Apps
2. Go to **Tables** → Find `Document` table (sprk_document)
3. Click **+ New** to create a record
4. Fill in the fields above
5. **Save**

#### Option B: Via API (if you have authentication)

```bash
# Would require user token
POST https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/sprk_documents
Authorization: Bearer {user_token}
Content-Type: application/json

{
  "sprk_documentname": "Test File 1",
  "sprk_hasfile": true,
  "sprk_filename": "test-file-1.txt",
  ...
}
```

---

## Step 3: Add PCF Control to a Form

### 3.1 Create or Open a Model-Driven App

1. Go to **Apps** in Power Apps
2. Find or create a model-driven app that uses the Document table
3. **Edit** the app

### 3.2 Edit the Document Form

1. In the app designer, navigate to the **Document** table
2. Open the **Main Form** for editing
3. This opens the form designer

### 3.3 Add the Universal Dataset Grid Control

1. In the form designer, find the **Components** panel (left side)
2. Click **+ Component** → **Get more components**
3. Search for: **Universal Dataset Grid** (or `Spaarke.UniversalDatasetGrid`)
4. Select it and click **Add**

5. Now drag the **Universal Dataset Grid** component onto the form
6. Place it where you want it (e.g., in a tab section)

### 3.4 Configure the Control

**Control Properties** (in the right panel):

1. **Dataset Binding**:
   - Bind to: **Documents** (1:N relationship to Document table)
   - Or create a view that shows documents you want to test

2. **SDAP Configuration**:
   - **SDAP API Base URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`
   - **Timeout**: `300000` (5 minutes)

3. **Display Configuration**:
   - Columns should auto-configure from the control's manifest

### 3.5 Save and Publish

1. **Save** the form
2. **Publish** the app
3. **Play** the app to test

---

## Step 4: Test the PCF Control

### 4.1 Open the Form with Test Data

1. In the published app, navigate to a Document record that has a file
2. The Universal Dataset Grid should load and display the document(s)

**Expected Display**:
```
Name          | File Name       | Size  | SharePoint | Actions
------------- | --------------- | ----- | ---------- | ------------------
Test File 1   | test-file-1.txt | 1 KB  | [🔗]       | [⬇] [🗑️] [🔄]
```

### 4.2 Test Download Button (Blue ⬇)

**Action**: Click the Download button

**Expected Behavior**:
1. Button shows loading spinner
2. API call to: `GET /api/drives/{driveId}/items/{itemId}/content`
3. Browser downloads the file
4. Success message appears
5. Button returns to normal state

**If it fails**:
- Check browser console for errors
- Check Network tab for API call details
- Verify user has Read permission on document

### 4.3 Test Delete Button (Red 🗑️)

**Action**: Click the Delete button

**Expected Behavior**:
1. Confirmation dialog appears:
   ```
   Are you sure you want to delete this file?
   This action cannot be undone.

   [Cancel] [Delete]
   ```
2. Click **Cancel**: Dialog closes, nothing happens
3. Click **Delete**:
   - Button shows loading spinner
   - API call to: `DELETE /api/drives/{driveId}/items/{itemId}`
   - File deleted from SharePoint Embedded
   - Dataverse record updated: `hasFile=false`, file fields cleared
   - Grid refreshes (row still shows but no file info)
   - Success message: "File deleted successfully"

**If it fails**:
- Check browser console for errors
- Verify user has Delete permission on document
- Check API logs for error details

### 4.4 Test Replace Button (Yellow 🔄)

**Action**: Click the Replace button

**Expected Behavior**:
1. File picker dialog opens (browser native)
2. Select a new file
3. Button shows loading spinner
4. API call to: `PUT /api/containers/{containerId}/files/{path}`
5. New file uploaded to SharePoint
6. Dataverse record updated with new file metadata:
   - `fileName` = new file name
   - `fileSize` = new file size
   - `mimetype` = new file type
   - `graphItemId` = new item ID
7. Grid refreshes with new file info
8. Success message: "File replaced successfully"

**If it fails**:
- Check browser console for errors
- Verify user has Write permission on document
- Check file size limits (API may have max size)
- Check API logs for error details

### 4.5 Test SharePoint Link (Icon 🔗)

**Action**: Click the SharePoint link icon

**Expected Behavior**:
1. New browser tab opens
2. File opens in SharePoint/Office Online
3. URL is the file's `webUrl` from Graph API

**If it fails**:
- Verify `graphItemId` and `graphDriveId` are populated
- Verify user has permission to access SharePoint

---

## Step 5: Error Scenarios to Test

### 5.1 No File Attached

**Setup**: Document record with `hasFile=false`

**Expected**: Buttons should be disabled or show "No file" message

### 5.2 Missing Permissions

**Setup**: User without Delete permission

**Expected**: Delete button disabled or shows permission error

### 5.3 Network Error

**Setup**: Disconnect internet (temporarily)

**Expected**:
- Operation fails gracefully
- Error message shown
- Grid remains functional

### 5.4 Large File

**Setup**: Try to replace with a very large file (>100MB)

**Expected**:
- May take longer (shows loading state)
- Or API returns size limit error with clear message

---

## Debugging Guide

### Check Browser Console

Open browser Developer Tools (F12) → **Console** tab

Look for:
- CORS errors (should NOT appear if configured correctly)
- API errors (401, 403, 500, etc.)
- JavaScript errors from PCF control
- Network request details

### Check Network Tab

Open Developer Tools → **Network** tab

Filter by: **XHR** or **Fetch**

Look for API calls:
- `GET .../api/drives/{driveId}/items/{itemId}/content` (Download)
- `DELETE .../api/drives/{driveId}/items/{itemId}` (Delete)
- `PUT .../api/containers/{containerId}/files/...` (Replace)

**Check**:
- Request headers include: `Authorization: Bearer ...`
- Response status codes
- Response bodies (error messages)

### Check API Logs

If operations fail:

```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

Look for:
- Authentication errors
- Permission errors
- Graph API errors
- Dataverse errors

### Common Issues

| Issue | Symptom | Solution |
|-------|---------|----------|
| CORS error | API call blocked in browser | Verify CORS config includes Power Apps domain |
| 401 Unauthorized | API rejects request | User not authenticated, refresh Power Apps |
| 403 Forbidden | API rejects operation | User lacks Dataverse permissions |
| 404 Not Found | File not found | Check `graphItemId` is correct |
| 500 Server Error | API internal error | Check API logs for details |
| Button disabled | Cannot click buttons | Check `hasFile=true` and permissions |

---

## Test Results Template

Use this template to document test results:

```markdown
## Test Results - Sprint 7A Universal Dataset Grid

**Tester**: [Your Name]
**Date**: 2025-10-06
**Environment**: SPAARKE DEV 1
**Browser**: [Chrome/Edge/Firefox + Version]

### Test Data
- Container ID: b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy
- Document Records: [Number of test records]
- Test Files: [List file names]

### Test Results

#### 1. Grid Display
- [ ] Pass / [ ] Fail
- **Notes**:

#### 2. Download Button
- [ ] Pass / [ ] Fail
- **File downloaded**: [Yes/No]
- **Notes**:

#### 3. Delete Button
- [ ] Pass / [ ] Fail
- **Confirmation dialog shown**: [Yes/No]
- **File deleted**: [Yes/No]
- **Dataverse updated**: [Yes/No]
- **Notes**:

#### 4. Replace Button
- [ ] Pass / [ ] Fail
- **File picker opened**: [Yes/No]
- **New file uploaded**: [Yes/No]
- **Dataverse updated**: [Yes/No]
- **Notes**:

#### 5. SharePoint Link
- [ ] Pass / [ ] Fail
- **Link opened in new tab**: [Yes/No]
- **Notes**:

### Issues Found
[List any issues, errors, or unexpected behavior]

### Screenshots
[Attach screenshots of working functionality and any errors]

### Overall Assessment
- [ ] Ready for Production
- [ ] Issues to Resolve
- [ ] Major Issues - Do Not Deploy

**Comments**:
```

---

## Success Criteria

Sprint 7A is considered **COMPLETE** when:

- [x] ✅ PCF control deployed to Power Apps
- [x] ✅ SDAP BFF API operational
- [ ] ⏭️ Grid displays documents with file information
- [ ] ⏭️ Download button successfully downloads files
- [ ] ⏭️ Delete button deletes files with confirmation
- [ ] ⏭️ Replace button uploads new files
- [ ] ⏭️ SharePoint links open correctly
- [ ] ⏭️ All operations update Dataverse metadata
- [ ] ⏭️ Error handling works gracefully
- [ ] ⏭️ No CORS or authentication errors

---

## Next Steps After Testing

### If All Tests Pass ✅

1. Document test results (use template above)
2. Take screenshots for Sprint documentation
3. Mark Sprint 7A as **COMPLETE**
4. Plan Sprint 7B enhancements

### If Issues Found ❌

1. Document issues with details (error messages, screenshots)
2. Prioritize issues (Critical / High / Medium / Low)
3. Create fix tasks
4. Re-test after fixes
5. Update documentation with known issues/workarounds

---

## Contact for Issues

**API Issues**:
- Check: `docs/DATAVERSE-AUTHENTICATION-GUIDE.md`
- Check: API health checks
- Review: App Service logs

**PCF Control Issues**:
- Check: Browser console errors
- Review: PCF control source code
- Verify: Control configuration in form

**Permission Issues**:
- Check: Dataverse security roles
- Check: User permissions on Document table
- Verify: Application User has System Administrator role

---

**Ready to Test**: ✅
**Next Action**: Add PCF control to Power Apps form and test file operations
