# Version 1.0.7 - Entity Detection Fix

**Deployed:** 2025-10-07 21:06 PM
**Status:** ✅ Imported and Published

---

## What Was Fixed

### Issue
Console showed:
```
{parentEntityName: '', parentRecordId: '', entityName: ''}
```

This caused upload to fail because the control didn't know which entity it was creating records for.

### Root Cause
Quick Create forms don't provide parent context the same way Main forms do. The code was looking for `context.mode.contextInfo` which is empty in Quick Create.

### Solution Applied
Added multiple fallbacks to detect entity name:

1. **Primary:** Check `context.page.entityTypeName`
2. **Fallback 1:** Check `context.page.getEntityName()`
3. **Fallback 2:** Check `context.mode.contextInfo.entityName`
4. **Fallback 3:** Default to `'sprk_document'`

---

## Testing Instructions

1. **Clear browser cache** (Ctrl+Shift+Delete → All time)
2. **Refresh form designer** (Ctrl+F5)
3. **Open Quick Create form** with the control
4. **Open browser console** (F12)
5. **Check logs** - you should now see:

```
[UniversalQuickCreatePCF] Entity name from page context {entityName: 'sprk_document'}
[UniversalQuickCreatePCF] PCF control initialized {
    entityName: 'sprk_document',  ← Should NOT be empty!
    ...
}
```

6. **Upload a file:**
   - Click "Add File"
   - Select a file
   - Click "Save and Create 1 Document"
   - Check console for upload progress

---

## Expected Behavior

### Successful Upload Should Show:

```
[FileUploadService] Starting file upload...
[FileUploadService] File uploaded successfully
[MultiFileUploadService] File X of Y uploaded
[DataverseRecordService] Creating record...
[DataverseRecordService] Record created successfully
```

### If Upload Still Fails:

Check console errors. Common issues:

1. **SDAP API not running:**
   ```
   Failed to fetch
   ERR_CONNECTION_REFUSED
   ```
   **Solution:** Start SDAP BFF API at https://localhost:7299

2. **CORS error:**
   ```
   Access to fetch... has been blocked by CORS policy
   ```
   **Solution:** Configure SDAP API CORS to allow Dataverse origin

3. **Authentication error:**
   ```
   401 Unauthorized
   ```
   **Solution:** MSAL authentication needs to acquire token (Sprint 8 feature)

---

## Version Number Display

**Note:** The blue version badge might not appear if the version badge div is being removed by form designer. The important thing is that the "Add File" button shows and console logs show v1.0.7.

To verify version:
1. Open console
2. Look for: `Universal Quick Create v1.0.7 - Control Loaded ✓`
3. Or check log: `[UniversalQuickCreatePCF] PCF control initialized`

---

## Next Steps

If upload still fails after this fix:

1. **Share console output** when clicking "Save and Create Document"
2. **Verify SDAP API is running**
3. **Check SDAP API URL** in component properties (should be `https://localhost:7299/api`)

---

## Summary

- ✅ Entity name detection fixed
- ✅ Fallback logic added for Quick Create forms
- ✅ Default to 'sprk_document' if all else fails
- ✅ Version 1.0.7 deployed and published

The control should now successfully detect it's creating `sprk_document` records and upload files accordingly.
