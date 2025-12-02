# CORS Fix & Test File Upload - Sprint 7A

**Date**: 2025-10-06
**Issue**: CORS errors preventing file operations testing
**Status**: ✅ Fixed (pending deployment)

---

## Problem Summary

### CORS Error
```
Access to fetch at 'https://spe-api-dev-67e2xz.azurewebsites.net/...' from origin 'null'
has been blocked by CORS policy: The value of the 'Access-Control-Allow-Origin' header
in the response must not be the wildcard '*' when the request's credentials mode is 'include'.
```

**Root Cause**:
- SDAP BFF API CORS configuration only allowed `localhost` origins
- Power Apps domain (`https://spaarkedev1.crm.dynamics.com`) was not in the allowed origins list
- The API uses `.AllowCredentials()` which requires explicit origins (no wildcard)

### Missing File Metadata

**Problem**: No Document records in Dataverse have `sprk_graphitemid` populated
- Can't test Download/Delete/Replace without actual file metadata
- Need to upload a test file to get item IDs and URLs

---

## Solution

### 1. CORS Configuration Fix ✅

**File Modified**: `src/api/Spe.Bff.Api/appsettings.json`

**Before**:
```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:3001",
    "http://127.0.0.1:3000"
  ]
}
```

**After**:
```json
"Cors": {
  "AllowedOrigins": [
    "http://localhost:3000",
    "http://localhost:3001",
    "http://127.0.0.1:3000",
    "https://spaarkedev1.crm.dynamics.com",
    "https://spaarkedev1.api.crm.dynamics.com"
  ]
}
```

**What This Fixes**:
- ✅ Power Apps PCF controls can now call SDAP API
- ✅ Download/Delete/Replace buttons will work from Power Apps
- ✅ File operations won't be blocked by browser CORS policy

**Deployment Required**: Yes - need to deploy updated config to Azure App Service

---

### 2. Test File Upload Utility ✅

**Created**: `upload-test-file.html`

**Purpose**: Upload a test file to your SharePoint Embedded container to get file metadata for Dataverse records

**How to Use**:

1. **Open the HTML file**: [upload-test-file.html](upload-test-file.html)

2. **Pre-filled values**:
   - API URL: `https://spe-api-dev-67e2xz.azurewebsites.net`
   - Container ID: `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`
   - File Name: `test-document.txt`

3. **Select a file** from your computer

4. **Click "Upload File"**

5. **Copy the file metadata** from the success message:
   - Item ID
   - Web URL
   - Size
   - Created/Modified dates
   - ETag

6. **Update a Dataverse Document record** with the metadata

**API Endpoint Used**:
```
PUT https://spe-api-dev-67e2xz.azurewebsites.net/api/drives/{driveId}/upload?fileName={name}
```

**Expected Response**:
```json
{
  "id": "01ABC...",
  "name": "test-document.txt",
  "size": 1234,
  "webUrl": "https://...",
  "createdDateTime": "2025-10-06T...",
  "lastModifiedDateTime": "2025-10-06T...",
  "eTag": "...",
  "parentId": null,
  "isFolder": false
}
```

---

## Deployment Steps

### Step 1: Deploy CORS Configuration Update

**Option A: Azure Portal**:
1. Navigate to Azure Portal
2. Open App Service: `spe-api-dev-67e2xz`
3. Go to **Configuration** → **Application settings**
4. Add/Update configuration:
   ```
   Cors__AllowedOrigins__3 = https://spaarkedev1.crm.dynamics.com
   Cors__AllowedOrigins__4 = https://spaarkedev1.api.crm.dynamics.com
   ```
5. **Save** and **Restart** the App Service

**Option B: Deploy Updated Code**:
1. Build and deploy the Spe.Bff.Api project
2. Ensure `appsettings.json` is included in deployment

**Verification**:
```bash
# Check if CORS headers are present
curl -I -X OPTIONS https://spe-api-dev-67e2xz.azurewebsites.net/api/ping \
  -H "Origin: https://spaarkedev1.crm.dynamics.com" \
  -H "Access-Control-Request-Method: GET"

# Should return:
# Access-Control-Allow-Origin: https://spaarkedev1.crm.dynamics.com
# Access-Control-Allow-Credentials: true
```

---

### Step 2: Upload Test File

**Before deploying CORS fix**, you can still upload via:

**Option 1: PowerShell (No CORS issues)**:
```powershell
$apiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"
$driveId = "b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy"
$fileName = "test-document.txt"
$filePath = "C:\path\to\your\test\file.txt"

# Create test file if needed
"This is a test file for Sprint 7A testing." | Out-File -FilePath $filePath -Encoding UTF8

# Upload using Invoke-RestMethod
$url = "$apiUrl/api/drives/$driveId/upload?fileName=$fileName"
$response = Invoke-RestMethod -Uri $url -Method Put -InFile $filePath -ContentType "text/plain"

# Display result
$response | ConvertTo-Json -Depth 10
```

**Option 2: After CORS fix**, use the HTML upload utility

---

### Step 3: Update Dataverse Document Record

Once you have the file metadata from upload response:

1. **Open Dataverse** (https://spaarkedev1.crm.dynamics.com)

2. **Navigate to** Advanced Find or Documents table

3. **Create or Update** a Document record with:
   ```
   sprk_graphdriveid = b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy
   sprk_graphitemid = [Item ID from upload response]
   sprk_filename = test-document.txt
   sprk_filepath = [Web URL from upload response]
   sprk_filesize = [Size from upload response]
   sprk_hasfile = true
   sprk_createddatetime = [Created date from upload response]
   sprk_lastmodifieddatetime = [Modified date from upload response]
   sprk_etag = [ETag from upload response]
   sprk_parentfolderid = [Parent ID from upload response, if any]
   ```

4. **Save** the record

---

### Step 4: Test File Operations in Power Apps

1. **Open your Power Apps app** with the Universal Dataset Grid

2. **Navigate to the view** showing Document records

3. **Select the test record** you just created

4. **Test each button**:

   **Download** ✅:
   - Click Download button
   - Browser download should start
   - File should download with correct name

   **Delete** ✅:
   - Click Remove File button
   - Confirmation dialog should appear
   - Click Delete
   - File deleted from SharePoint
   - Record updated to `hasFile = false`

   **Replace** ✅:
   - Upload another test file first
   - Select the record
   - Click Update File button
   - File picker opens
   - Select new file
   - Upload completes
   - Record updated with new file metadata

   **SharePoint Link** ✅:
   - Click "Open in SharePoint" link in `sprk_filepath` column
   - Opens file in SharePoint in new tab

---

## Troubleshooting

### Issue: CORS still blocked after deployment

**Check**:
1. Verify App Service restarted after configuration change
2. Check browser DevTools Network tab for actual CORS headers
3. Ensure you're accessing from the correct domain
4. Clear browser cache

**Fix**:
```bash
# Restart App Service
az webapp restart --name spe-api-dev-67e2xz --resource-group [resource-group-name]
```

---

### Issue: Upload returns 401 Unauthorized

**Cause**: API requires authentication

**Fix**: The upload utility needs to include authentication. Use PowerShell with credentials:

```powershell
# Get access token
$token = (Get-AzAccessToken -ResourceUrl "api://170c98e1-d486-4355-bcbe-170454e0207c").Token

# Upload with bearer token
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "text/plain"
}

Invoke-RestMethod -Uri $url -Method Put -InFile $filePath -Headers $headers
```

---

### Issue: PowerShell "No files found" when listing container

**Cause**: PowerShell request doesn't include proper authentication token

**Fix**: Use the upload approach above to create a file first, then test in Power Apps

---

### Issue: Download button not working in Power Apps

**Checklist**:
- [ ] CORS configured and deployed?
- [ ] Record has `sprk_graphitemid` populated?
- [ ] Record has `sprk_graphdriveid` populated?
- [ ] Record has `sprk_hasfile = true`?
- [ ] Browser console shows any errors?
- [ ] SDAP API is running and accessible?

**Debug**:
1. Open browser DevTools (F12)
2. Go to Console tab
3. Click Download button
4. Check for errors or API call failures

---

## Testing Checklist

After deploying CORS fix and uploading test file:

- [ ] CORS headers present in API responses
- [ ] PowerShell upload successful
- [ ] Dataverse record created/updated with file metadata
- [ ] Download button works in Power Apps
- [ ] Delete button shows confirmation dialog
- [ ] Delete actually removes file from SharePoint
- [ ] Replace button opens file picker
- [ ] Replace uploads new file successfully
- [ ] SharePoint link opens in new tab
- [ ] No CORS errors in browser console

---

## Files Modified/Created

**Modified**:
- ✅ `src/api/Spe.Bff.Api/appsettings.json` - Added Power Apps origins to CORS

**Created**:
- ✅ `upload-test-file.html` - HTML utility for uploading test files
- ✅ `Get-ContainerFiles.ps1` - PowerShell script to list files (authentication issues)
- ✅ `CORS-FIX-AND-FILE-UPLOAD.md` - This document

---

## Next Actions

### Immediate (User)
1. ⏳ Deploy CORS configuration update to Azure App Service
2. ⏳ Upload test file using PowerShell or HTML utility (after CORS fix)
3. ⏳ Update Dataverse Document record with file metadata
4. ⏳ Test Download/Delete/Replace buttons in Power Apps

### After Testing Success
1. Create additional test files if needed
2. Test with different file types (PDF, DOCX, images)
3. Test with larger files
4. Document any issues found
5. Move to Sprint 7B planning (Universal Quick Create with file upload)

---

## Summary

**CORS Issue**: ✅ Fixed (pending deployment)
- Added Power Apps domains to CORS allowed origins
- Will resolve "blocked by CORS policy" errors

**Test File Upload**: ✅ Ready
- HTML utility created for manual upload
- PowerShell option available for authenticated upload
- Upload endpoint: `PUT /api/drives/{driveId}/upload?fileName={name}`

**Next Step**: Deploy CORS fix → Upload test file → Update Dataverse record → Test buttons!

---

**Owner**: AI-Directed Coding Session
**Date**: 2025-10-06
**Related**: Sprint 7A Deployment, SDAP Integration Testing
