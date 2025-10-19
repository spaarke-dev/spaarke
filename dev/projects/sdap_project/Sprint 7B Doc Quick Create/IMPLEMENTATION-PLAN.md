# Sprint 7B Implementation Plan

**Version:** 2.0 (Revised)
**Timeline:** 5-7 days
**Status:** Ready to Execute

---

## Overview

Implement multi-file upload to SharePoint Embedded from Quick Create forms with custom "Save and Create Documents" button in form footer. Each file creates a separate Document record.

**Key Architecture:**
- PCF creates records directly (bypasses Quick Create save)
- Custom button in footer (replaces standard "Save and Close")
- Adaptive upload strategy (sync-parallel vs long-running)
- Subgrid refresh after completion

---

## Phase 1: Core Services (Day 1-2)

**Goal:** Implement file upload logic and button management

### Work Item 1: Create MultiFileUploadService (4 hours)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MultiFileUploadService.ts`

**Based on:** TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md (lines 163-755)

**Responsibilities:**
- `determineUploadStrategy()` - Choose sync-parallel or long-running
- `handleSyncParallelUpload()` - Fast path for 1-3 small files
- `handleLongRunningUpload()` - Safe path for large/many files
- `calculateBatchSize()` - Adaptive batch size (2-5)
- `createDocumentRecord()` - Create Dataverse record via WebAPI

**Key Logic:**
```typescript
if (files.length <= 3 && maxSize < 10MB && total < 20MB) {
    // Sync-parallel: Upload all at once (3-4 seconds)
} else {
    // Long-running: Batch upload with progress (17-25 seconds)
}
```

**Testing:**
- 3 small files → sync-parallel
- 5 large files → long-running batched
- Verify batch size calculation

---

### Work Item 2: Implement Button Management (3 hours)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts`

**Responsibilities:**
- `hideStandardButtons()` - CSS injection to hide "Save and Close"
- `injectCustomButtonInFooter()` - DOM manipulation to add custom button
- `findFormFooter()` - Multiple fallback selectors
- `updateButtonState()` - Dynamic button text/status
- `setupFooterWatcher()` - MutationObserver for re-injection
- `closeQuickCreateForm()` - Close form + refresh subgrid

**Button States:**
- No files: "Select Files to Continue" (disabled, gray)
- Files selected: "Save and Create 3 Documents" (enabled, blue)
- Uploading: "Uploading 2 of 3..." (disabled, gray)

**Footer Selectors (fallbacks):**
1. `[data-id="quickCreateFormFooter"]`
2. `[data-id="dialogFooter"]`
3. `.ms-Dialog-actions`
4. Find Cancel button parent

**Testing:**
- Button appears in footer
- Standard button hidden
- Button text updates correctly
- MutationObserver re-injects if footer re-renders

---

### Work Item 3: Update Control Manifest (1 hour)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`

**Changes:**

**REMOVE:**
```xml
<data-set name="dataset" ...>  <!-- Dataset not supported in Quick Create -->
<property name="defaultValueMappings" .../>  <!-- Field inheritance out of scope -->
```

**ADD/UPDATE:**
```xml
<property name="speMetadata"
          display-name-key="SPE_Metadata_Field"
          of-type="Multiple"
          usage="bound"
          required="false" />

<property name="sdapApiBaseUrl"
          display-name-key="SDAP_API_Base_URL"
          of-type="SingleLine.Text"
          usage="input"
          required="false"
          default-value="https://localhost:7299/api" />

<property name="allowMultipleFiles"
          display-name-key="Allow_Multiple_Files"
          of-type="TwoOptions"
          usage="input"
          required="false"
          default-value="true" />
```

**Testing:**
- `npm run build` succeeds
- ManifestTypes.d.ts regenerated correctly

---

## Phase 2: UI & Progress (Day 3-4)

**Goal:** Implement file picker UI and progress indicators

### Work Item 4: Create File Upload UI Component (4 hours)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx`

**Based on:** TASK-7B-2A lines 760-919 (FilePickerField component)

**UI Elements:**
- File picker (HTML5 multi-select)
- Selected files list with remove buttons
- File size display (formatted KB/MB)
- Total files and size summary
- Shared description field (optional)

**Layout:**
```
┌─────────────────────────────────────┐
│ Select File(s) (up to 10)           │
│ [Choose Files]                      │
│                                     │
│ Selected Files:                     │
│ ☑ contract.pdf (2.0 MB)    [×]    │
│ ☑ invoice.pdf (1.5 MB)     [×]    │
│ ☑ receipt.pdf (500 KB)     [×]    │
│                                     │
│ 3 files • 4.0 MB total              │
│                                     │
│ Description (optional):             │
│ [____________________________]      │
└─────────────────────────────────────┘
```

**Testing:**
- Multi-select works
- File list displays correctly
- Remove button works
- Size formatting correct

---

### Work Item 5: Create Progress Component (3 hours)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/UploadProgress.tsx`

**Based on:** TASK-7B-2A lines 923-1103 (UploadProgress component)

**UI Elements:**
- Progress bar (0-100%)
- Current file indicator
- File-by-file status icons:
  - ✓ Uploaded (green)
  - ↻ Uploading (blue spinner)
  - ⏳ Waiting (gray)
  - ✗ Failed (red)

**Layout:**
```
Uploading 3 files...
████████████░░░░░░ 66%
2 of 3 files • 66% complete

✓ contract.pdf - Uploaded
↻ invoice.pdf - Uploading...
⏳ receipt.pdf - Waiting...

⚠️ Please keep this window open
```

**Testing:**
- Progress updates in real-time
- Icons display correctly
- Percentage calculates correctly

---

### Work Item 6: Integrate Services with PCF (3 hours)

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts`

**Connect:**
- PCF init() → Initialize MultiFileUploadService
- Custom button click → Call handleSaveAndCreateDocuments()
- handleSaveAndCreateDocuments() → MultiFileUploadService.uploadFiles()
- Progress callback → Update button state and UI
- Success → Close form and refresh subgrid
- Error → Show error, keep form open

**Flow:**
```typescript
handleSaveAndCreateDocuments() {
    // 1. Update button: "Uploading..."
    this.updateButtonState(false, 0, true);

    // 2. Upload files
    const result = await this.multiFileUploadService.uploadFiles(
        { files, driveId, sharedMetadata },
        (progress) => {
            // Update button: "Uploading 2 of 3..."
            this.updateButtonProgress(progress.current, progress.total);
        }
    );

    // 3. Close form and refresh
    if (result.success) {
        this.closeQuickCreateForm();
        this.refreshParentSubgrid();
    }
}
```

**Testing:**
- Button updates during upload
- Progress callback fires
- Form closes on success
- Subgrid refreshes

---

## Phase 3: Form Configuration (Day 5)

**Goal:** Configure Quick Create form and deploy

### Work Item 7: Configure Quick Create Form (2 hours)

**Steps:**

1. **Open Quick Create form for Document entity**
   - Power Apps portal → Tables → Document → Forms
   - Find or create Quick Create form

2. **Add fields to form:**
   - `sprk_matter` (Lookup, visible)
   - `sprk_documenttitle` (Text, visible, optional)
   - `sprk_description` (Multiline, visible, optional)
   - `sprk_fileuploadmetadata` (Multiline, **hidden label**)

3. **Add PCF control to sprk_fileuploadmetadata:**
   - Click field → Controls tab
   - Add "Universal Quick Create" control
   - Set as default for Web
   - Configure parameters:
     - `sdapApiBaseUrl`: https://your-api.azurewebsites.net/api
     - `allowMultipleFiles`: true

4. **Hide field label:**
   - Properties → Hide label: ON
   - (PCF renders its own UI)

5. **Publish form**

**Testing:**
- Form opens from "+ New Document"
- PCF control renders
- Custom button in footer
- Standard button hidden

---

### Work Item 8: Build and Deploy (2 hours)

**Build:**
```bash
cd src/controls/UniversalQuickCreate
npm run build
```

**Package Solution:**
```bash
cd ../../..
pac solution pack --zipfile solution.zip --folder Solutions/UniversalQuickCreate
```

**Deploy:**
1. Import solution.zip to environment
2. Publish all customizations
3. Verify control appears in form

**Testing:**
- Solution imports successfully
- Form configuration preserved
- Control renders correctly

---

## Phase 4: Testing & Documentation (Day 6-7)

**Goal:** Comprehensive testing and documentation

### Work Item 9: End-to-End Testing (4 hours)

**Test Scenarios:**

1. **Single File Upload**
   - Select 1 file (2MB)
   - Verify sync-parallel strategy
   - Verify 1 Document created
   - Time: ~2-3 seconds

2. **Multi-File Upload (Small)**
   - Select 3 files (2MB each)
   - Verify sync-parallel strategy
   - Verify 3 Documents created
   - Time: ~3-4 seconds

3. **Multi-File Upload (Large)**
   - Select 5 files (15MB each)
   - Verify long-running strategy
   - Verify batch size = 2
   - Verify 5 Documents created
   - Time: ~17-25 seconds

4. **Partial Failure**
   - Disconnect network after 2 files
   - Verify 2 Documents created
   - Verify error message for remaining 3
   - Verify form stays open

5. **Custom Button Placement**
   - Verify button in footer (next to Cancel)
   - Verify standard button hidden
   - Verify button text updates

6. **Subgrid Refresh**
   - Upload files
   - Verify form closes
   - Verify subgrid refreshes automatically
   - Verify new records visible

7. **Browser Compatibility**
   - Test in Chrome
   - Test in Edge
   - Test in Firefox

**See WORK-ITEM-7-TESTING.md for complete test plan.**

---

### Work Item 10: Documentation (3 hours)

**Create 3 Documents:**

1. **Admin Configuration Guide** (1 hour)
   - How to configure Quick Create form
   - How to set control parameters
   - Troubleshooting common issues

2. **User Guide** (1 hour)
   - How to upload files from Quick Create
   - What to expect during upload
   - Error messages and solutions

3. **Technical Reference** (1 hour)
   - Architecture overview
   - Component descriptions
   - API integration details
   - Performance metrics

**See WORK-ITEM-8-DOCUMENTATION.md for templates.**

---

## Daily Breakdown

### Day 1 (8 hours)
- ✅ Work Item 1: MultiFileUploadService (4h)
- ✅ Work Item 2: Button Management (3h)
- ✅ Work Item 3: Update Manifest (1h)

**Deliverable:** Core services implemented, manifest updated

---

### Day 2 (8 hours)
- ✅ Work Item 4: File Upload UI (4h)
- ✅ Work Item 5: Progress Component (3h)
- ✅ Work Item 6: Integrate Services (1h partial)

**Deliverable:** UI components complete

---

### Day 3 (8 hours)
- ✅ Work Item 6: Integration complete (2h)
- ✅ Test local build (2h)
- ✅ Work Item 7: Configure Form (2h)
- ✅ Work Item 8: Build & Deploy (2h)

**Deliverable:** Deployed to test environment

---

### Day 4 (8 hours)
- ✅ Work Item 9: Testing (full day)
- Fix any issues found

**Deliverable:** All tests passing

---

### Day 5 (Optional, 4-8 hours)
- ✅ Work Item 10: Documentation (3h)
- Additional testing if needed
- Performance tuning
- Deploy to production

**Deliverable:** Documentation complete, production ready

---

## Dependencies

### External
- ✅ SDAP BFF API running
- ✅ SharePoint Embedded containers provisioned
- ✅ MSAL configured
- ✅ Test Matter with valid Container ID

### Code
- ✅ FileUploadService.ts (existing)
- ✅ SdapApiClient.ts (existing)
- ✅ MsalAuthProvider.ts (existing)
- ✅ Types defined (existing)

### Dataverse
- ✅ `sprk_fileuploadmetadata` field created (10,000 chars)
- ✅ Document entity with all required fields

---

## Risks

### Technical Risks

**Risk:** Footer selectors change after Power Apps update
**Mitigation:** Multiple fallback selectors + MutationObserver
**Impact:** Medium

**Risk:** Large file timeouts (>100MB)
**Mitigation:** Document size limits, future chunked upload
**Impact:** Low (users can split files)

**Risk:** Subgrid refresh fails
**Mitigation:** Multiple refresh methods, fallback message
**Impact:** Low (records still created)

### Schedule Risks

**Risk:** Button injection more complex than expected
**Mitigation:** Fallback button inside PCF if footer injection fails
**Impact:** Medium (UX degraded but functional)

**Risk:** Testing takes longer (issues found)
**Mitigation:** +1-2 days buffer in timeline
**Impact:** Low (5-7 day range has buffer)

---

## Success Criteria

### Must Have (Blocker)
- ✅ Multiple files upload successfully
- ✅ Multiple Document records created (one per file)
- ✅ Custom button in footer
- ✅ Standard button hidden
- ✅ Form closes and subgrid refreshes

### Should Have (High Priority)
- ✅ Adaptive upload strategy working
- ✅ Progress indicators accurate
- ✅ Error handling for partial failures
- ✅ Button text updates dynamically

### Nice to Have (Low Priority)
- ⚠️ MutationObserver working
- ⚠️ All 3 subgrid refresh methods working
- ⚠️ Performance optimization

---

## Rollback Plan

If major issues found:

1. **Revert to previous version**
   - Remove custom button injection
   - Show button inside PCF control
   - Less optimal UX but functional

2. **Fallback: Dataset Grid approach**
   - Use UniversalDatasetGrid instead
   - Requires different form configuration
   - More work but proven approach

3. **Minimal Viable:**
   - Single file only
   - No custom button (inside PCF)
   - Manual subgrid refresh

---

## Next Steps After Completion

### Sprint 7C (Future)
- Backend plugin for field inheritance
- Auto-populate Matter lookup
- Auto-retrieve Container ID
- Clear metadata field after processing

### Future Enhancements
- Chunked upload for large files
- True parallel upload
- Drag-and-drop support
- File preview
- Resume on failure

---

**Timeline:** 5-7 days
**Team:** 1 developer
**Status:** Ready to Execute
**Start Date:** TBD
**Target Completion:** Start + 5-7 days
