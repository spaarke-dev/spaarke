# Sprint 7A - Power Apps Setup Guide

**Date**: 2025-10-06
**Purpose**: Visual step-by-step guide for adding Universal Dataset Grid to a model-driven app

---

## Quick Start Checklist

Before you begin, ensure:

- [x] ✅ SDAP BFF API is operational (`https://spe-api-dev-67e2xz.azurewebsites.net`)
- [x] ✅ Dataverse connection working (healthz/dataverse returns healthy)
- [x] ✅ Universal Dataset Grid PCF control deployed (v1.0)
- [x] ✅ CORS configured for Power Apps domain
- [ ] ⏭️ **YOU ARE HERE** - Add control to Power Apps form

---

## Part 1: Navigate to Power Apps

### Step 1.1: Open Power Apps Maker Portal

1. Go to: **https://make.powerapps.com**
2. Sign in with your credentials
3. Verify you're in the correct environment:
   - Look at the top-right corner
   - Should show: **SPAARKE DEV 1**
   - If not, click the environment selector and choose **SPAARKE DEV 1**

---

## Part 2: Verify PCF Control is Deployed

### Step 2.1: Check Solutions

1. In the left navigation, click **Solutions**
2. Look for: **UniversalDatasetGridSolution**
3. You should see:
   ```
   Display Name: UniversalDatasetGridSolution
   Name: UniversalDatasetGridSolution
   Version: 1.0
   Publisher: Spaarke
   Status: Managed
   ```

### Step 2.2: View Control Components

1. Click on **UniversalDatasetGridSolution** to open it
2. In the solution, look for:
   ```
   Component Type: Control
   Name: Spaarke.UniversalDatasetGrid
   Display Name: Universal Dataset Grid
   ```

**✅ If you see this, the control is successfully deployed!**

---

## Part 3: Create or Open a Model-Driven App

### Option A: Use Existing App (Recommended)

If you already have a model-driven app that uses the **Document** table:

1. Go to **Apps** in the left navigation
2. Find your app (e.g., "Document Management App" or similar)
3. Click the **⋮** (three dots) next to the app
4. Select **Edit**

### Option B: Create New Test App

If you don't have an app yet:

1. Go to **Apps** → **+ New app** → **Model-driven**
2. Give it a name: **Document Test App**
3. Click **Create**
4. In the app designer:
   - Click **+ Add page**
   - Select **Table based view and form**
   - Choose table: **Document** (sprk_document)
   - Select the **Main form**
   - Click **Add**
5. Click **Save**

---

## Part 4: Add Control to Document Form

### Step 4.1: Open Form Designer

1. In your app designer, find the **Document** entity/table
2. Click on the **Main Form** for Document
3. This opens the **Form Designer**

**You should now see the form layout with fields like:**
- Document Name
- Description
- Status
- etc.

### Step 4.2: Add Grid Component

1. In the **left panel** (Components), look for:
   - **+ Component** button (or **Insert** → **Component**)
2. Click it to open the component library

3. Search for: **Universal Dataset Grid**
   - You can also search for: `Spaarke.UniversalDatasetGrid`

4. Select the **Universal Dataset Grid** component
5. Click **Add** (or drag it onto the form)

### Step 4.3: Place the Component

1. The component will appear in the form designer
2. Drag it to where you want it (typically in a tab section)
3. You can resize it by dragging the edges

**Recommended placement:**
- In a new tab called "Files" or "Documents"
- Full width of the tab
- At least 400-500 pixels height for comfortable viewing

### Step 4.4: Configure Component Properties

When you select the Universal Dataset Grid component, the **Properties panel** appears on the right.

**Required Configuration:**

1. **Dataset Binding**:
   - Click **Select a dataset**
   - Choose: **Documents** (or create a view that shows documents)
   - This tells the grid which records to display

2. **SDAP Configuration** (if property fields exist):
   - **API Base URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`
   - **Timeout**: `300000` (5 minutes in milliseconds)

   **Note**: These might be hardcoded in the control (from types/index.ts), so you may not see these fields.

3. **Display Configuration**:
   - The columns should auto-configure based on the control's manifest
   - You should see columns for:
     - Document Name
     - File Name
     - File Size
     - Actions (Download, Delete, Replace buttons)

### Step 4.5: Save Form

1. Click **Save** in the top-right
2. Wait for "Saved successfully" message

---

## Part 5: Publish the App

### Step 5.1: Publish

1. Click **Publish** in the top-right
2. Wait for publishing to complete (may take 30-60 seconds)
3. You should see "Published successfully"

### Step 5.2: Play the App

1. Click **Play** (or close the designer and find your app in the **Apps** list)
2. Click the app to launch it

---

## Part 6: Create Test Data

Before you can test the grid, you need document records with file metadata.

### Step 6.1: Get Files from Container

**Container ID**: `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`

You mentioned this container already has files. We need to:

1. Get the file metadata (names, IDs, sizes)
2. Create Document records that reference these files

**How to get file metadata**:

#### Option A: Via Power Apps (if you can see them)
- If you have access to SharePoint Embedded management, you can view files there
- Note down: file name, item ID, drive ID, size, MIME type

#### Option B: Via Graph API (requires authentication)

You'll need to call:
```bash
GET https://graph.microsoft.com/v1.0/drives/b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy/root/children
```

This requires a user token with Files.Read.All permission.

**Let me know if you need help getting the file list.**

### Step 6.2: Create Document Records

Once you have file metadata, create Document records in Dataverse:

1. In your app, navigate to **Documents**
2. Click **+ New**
3. Fill in the form:

   ```
   Document Name: Test File 1
   Container ID: <lookup to container record>
   Has File: Yes
   File Name: test-file-1.txt
   File Size: 1024
   MIME Type: text/plain
   Graph Drive ID: b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy
   Graph Item ID: <item ID from file metadata>
   Status: Active
   ```

4. Click **Save**

**Repeat for 2-3 test files.**

---

## Part 7: Test the Grid

### Step 7.1: View Grid Display

1. Navigate to a Document record that has a file
2. The Universal Dataset Grid should load automatically
3. You should see:

   ```
   Name          | File Name       | Size  | SharePoint | Actions
   ------------- | --------------- | ----- | ---------- | ------------------
   Test File 1   | test-file-1.txt | 1 KB  | [🔗]       | [⬇] [🗑️] [🔄]
   ```

### Step 7.2: Test Download Button (Blue ⬇)

1. Click the **Download** button (blue)
2. **Expected**:
   - Button shows loading spinner
   - API call to: `GET /api/drives/{driveId}/items/{itemId}/content`
   - Browser downloads the file
   - Success message appears

3. **If it fails**:
   - Open browser console (F12)
   - Check for errors
   - Verify user has Read permission on document

### Step 7.3: Test Delete Button (Red 🗑️)

1. Click the **Delete** button (red)
2. **Expected**:
   - Confirmation dialog appears:
     ```
     Are you sure you want to delete this file?
     This action cannot be undone.

     [Cancel] [Delete]
     ```
3. Click **Cancel** → Dialog closes, nothing happens
4. Click **Delete** (on another record):
   - Button shows loading spinner
   - File deleted from SharePoint
   - Dataverse record updated (`hasFile=false`)
   - Grid refreshes
   - Success message

5. **If it fails**:
   - Check user has Delete permission
   - Check API logs for errors

### Step 7.4: Test Replace Button (Yellow 🔄)

1. Click the **Replace** button (yellow)
2. **Expected**:
   - File picker dialog opens
3. Select a new file
4. **Expected**:
   - Button shows loading spinner
   - New file uploaded to SharePoint
   - Dataverse record updated with new file metadata
   - Grid refreshes with new file info
   - Success message

5. **If it fails**:
   - Check user has Write permission
   - Check file size (API may have limits)
   - Check API logs

### Step 7.5: Test SharePoint Link (🔗)

1. Click the **SharePoint link** icon
2. **Expected**:
   - New browser tab opens
   - File opens in SharePoint/Office Online

---

## Part 8: Debugging Common Issues

### Issue 1: Grid Doesn't Load

**Symptoms**: Empty grid, no data displayed

**Possible Causes**:
- No Document records with `hasFile=true`
- Dataset binding not configured
- Permissions issue

**Fix**:
1. Check that Document records exist
2. Verify `hasFile=true` on records
3. Check dataset binding in form designer
4. Verify user has Read permission on Documents

### Issue 2: CORS Error

**Symptoms**: Browser console shows CORS policy error

**Fix**:
- Verify CORS configuration includes Power Apps domain
- Check: `https://spaarkedev1.crm.dynamics.com` is in allowed origins
- Restart App Service after CORS changes

### Issue 3: 401 Unauthorized

**Symptoms**: API calls return 401

**Possible Causes**:
- User not authenticated in Power Apps
- Token not being passed correctly

**Fix**:
1. Refresh Power Apps (Ctrl+F5)
2. Log out and log back in
3. Check browser console for token details

### Issue 4: Buttons Disabled

**Symptoms**: Cannot click buttons

**Possible Causes**:
- Record has `hasFile=false`
- Missing `graphItemId` or `graphDriveId`
- Permissions issue

**Fix**:
1. Verify record has `hasFile=true`
2. Check `graphItemId` and `graphDriveId` are populated
3. Verify user permissions on Document table

### Issue 5: File Not Downloading

**Symptoms**: Download button clicked but no download

**Possible Causes**:
- Invalid `graphItemId`
- File deleted from SharePoint
- API error

**Fix**:
1. Open browser console (F12)
2. Check Network tab for API call
3. Look for error response
4. Verify file exists in SharePoint container

---

## Part 9: Check API Logs

If operations fail, you can check the API logs:

```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

Look for:
- API endpoint calls
- Error messages
- Stack traces

---

## Part 10: Next Steps

### If Everything Works ✅

1. Document test results using template in TASK-7A-TESTING-GUIDE.md
2. Take screenshots of working functionality
3. Mark Sprint 7A as **COMPLETE**
4. Plan any enhancements for Sprint 7B

### If Issues Found ❌

1. Document issues with error messages
2. Check debugging guide above
3. Review API logs
4. Report issues for fixing

---

## Summary

**You are now ready to:**
1. ✅ Open Power Apps maker portal
2. ✅ Verify PCF control is deployed
3. ✅ Add control to a model-driven app form
4. ✅ Create test document records
5. ✅ Test Download, Delete, Replace operations
6. ✅ Debug any issues that arise

**Container ID for testing**: `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`

**API Health**: All checks passing ✅

**Next Action**: Open Power Apps and start testing!

---

**Questions or Issues?**
- Refer to TASK-7A-TESTING-GUIDE.md for detailed testing procedures
- Refer to docs/DATAVERSE-AUTHENTICATION-GUIDE.md for authentication issues
- Check API logs for backend errors
