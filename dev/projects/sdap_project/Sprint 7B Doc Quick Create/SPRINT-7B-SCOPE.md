# Sprint 7B: Document Quick Create - Revised Scope

**Version:** 2.0 (Revised Architecture)
**Date:** 2025-10-07
**Timeline:** 5-7 days
**Status:** Ready to Implement

---

## Sprint Goal

Enable users to upload multiple files to SharePoint Embedded from Quick Create forms, creating **multiple Document records (one per file)** with custom "Save and Create Documents" button in form footer.

---

## User Story

1. User is on Matter record, Document tab
2. On Document subgrid (standard subgrid), user clicks **"+ New Document"** → launches standard Quick Create
3. Standard Quick Create has our file upload PCF control
4. User clicks **"+ Add File"** and selects local file(s) - **multi-select supported**
5. User can add additional files (multiple file selection)
6. User can add other Document fields in Quick Create form (e.g., Description)
7. User clicks custom **"Save and Create Documents"** button **in form footer** (replaces standard "Save and Close")
8. SPE file upload process starts with progress bar
9. System orchestrates SPE file uploads and retrieves SPE metadata
10. System creates **multiple Document records** (one per file) with SPE metadata and URL links
11. When complete, Quick Create closes and **Document subgrid refreshes** showing new records

---

## In Scope ✅

### 1. File Upload to SharePoint Embedded

**Single & Multiple File Upload:**
- Single file upload
- **Multiple file upload** (up to 10 files recommended, 30 max)
- HTML5 multi-select file picker
- Selected files list with ability to remove files

**Adaptive Upload Strategy:**
- **Sync-parallel:** 1-3 small files (<10MB each, <20MB total) → Fast (3-4 seconds)
  - All files upload in parallel
  - Records created sequentially
  - Simple progress indicator

- **Long-running batched:** Large or many files → Batched with detailed progress (17-25 seconds)
  - Adaptive batch size (2-5 files based on file size)
  - Files upload in batches (parallel within batch)
  - Records created sequentially
  - File-by-file status display

**Progress & Feedback:**
- Progress bar during upload
- File-by-file status (✓ uploaded, ↻ uploading, ⏳ waiting, ✗ failed)
- Current file indicator
- Percentage complete
- Error messages per file

### 2. Multiple Document Record Creation

**Key Architecture:** PCF creates records directly via `context.webAPI.createRecord()` - does NOT use Quick Create's save mechanism.

**Each File → One Document Record:**
- `sprk_documenttitle` - File name (or user-entered)
- `sprk_description` - Shared across all files (user-entered)
- `sprk_matter` - From parent context (if available)
- `sprk_containerid` - From parent Matter (if available)
- `sprk_sharepointurl` - SPE file URL
- `sprk_driveitemid` - SPE drive item ID
- `sprk_filename` - Original file name
- `sprk_filesize` - File size in bytes
- `sprk_createddate` - SPE created timestamp
- `sprk_modifieddate` - SPE modified timestamp

**Process:**
1. Upload file 1 to SPE → Create Document record 1
2. Upload file 2 to SPE → Create Document record 2
3. Upload file 3 to SPE → Create Document record 3
4. Close form → Refresh parent subgrid

### 3. Custom Button in Form Footer

**Key UX Decision:** Custom button replaces standard "Save and Close" button in form footer.

**Implementation:**
- Hide standard "Save and Close" button via CSS injection
- Inject custom button into form footer via DOM manipulation
- Button positioned where users expect (next to Cancel button)
- Button dynamically updates based on state:
  - No files: "Select Files to Continue" (disabled)
  - Files selected: "Save and Create 3 Documents" (enabled)
  - Uploading: "Uploading 2 of 3..." (disabled)

**Why Footer?**
- ✅ Users expect action buttons in footer
- ✅ Familiar UX (standard location)
- ✅ No confusion about which button to click
- ✅ Maintains Quick Create appearance

### 4. Quick Create Form Integration

**Standard Quick Create:**
- Works with "+ New Document" button
- Opens from Matter subgrid
- Opens from navigation "+ New" menu
- Standard form layout

**PCF Control:**
- Bound to `sprk_fileuploadmetadata` field (Quick Create requirement)
- Renders file picker and selected files list
- Shared description field (applies to all files)
- Does NOT render other form fields (handled by Quick Create)

**Form Behavior:**
- Standard Quick Create opens
- PCF control displays in place of bound field
- Custom button in footer (standard button hidden)
- User uploads files, form closes automatically
- Parent subgrid refreshes showing new records

### 5. Parent Subgrid Refresh

**After Successful Upload:**
- PCF closes Quick Create form
- PCF triggers parent subgrid refresh via:
  - Method 1: `parent.Xrm.Page.getControl('Documents').refresh()`
  - Method 2: `parent.Xrm.Navigation.closeDialog()` with refresh
  - Method 3: Fallback to manual refresh message

**User Experience:**
- Form closes smoothly
- New Document records immediately visible in subgrid
- No manual refresh needed

---

## Out of Scope ❌

### 1. Backend Field Inheritance

- ❌ **NO Dataverse plugins** (ADR constraint: "we don't use plugins")
- ❌ NO automatic field copying from Matter to Document
- ❌ NO backend processing of metadata
- ❌ NO automatic Container ID retrieval
- ❌ NO field mapping configuration (defaultValueMappings)

**Reason:** Architecture Decision Record states no plugins. Field inheritance deferred to future sprint.

### 2. Dynamic Form Fields

- ❌ NO EntityFieldDefinitions
- ❌ NO configurable field mappings
- ❌ NO dynamic field rendering based on entity type
- ❌ NO custom field configuration UI

**Reason:** Keep scope focused on file upload only.

### 3. Multi-Entity Support

- ❌ Document entity ONLY
- ❌ NO Task, Contact, or other entity types
- ❌ NO entity type detection
- ❌ NO entity-specific logic

**Reason:** Prove the pattern with single entity first.

### 4. Advanced Upload Features

- ❌ NO chunked upload for very large files (>100MB)
- ❌ NO file preview before upload
- ❌ NO drag-and-drop file upload
- ❌ NO cancel individual files during upload
- ❌ NO resume upload on failure
- ❌ NO file type restrictions (accept all file types)

**Reason:** Can be added in future sprints if needed.

---

## Critical Architecture Decisions

### Decision 1: PCF Creates Records Directly ✅

**NOT THIS (v1.0 approach - WRONG):**
```
User clicks "Save and Close"
    ↓
Quick Create creates ONE Document record with metadata JSON array
    ↓
Backend plugin reads metadata, creates additional records
```

**BUT THIS (v2.0 approach - CORRECT):**
```
User clicks custom button
    ↓
PCF uploads files to SPE (with progress)
    ↓
PCF creates N Document records via context.webAPI.createRecord()
    ↓
PCF closes form and refreshes subgrid
```

**Why:**
- ✅ No backend plugins needed (ADR constraint)
- ✅ User sees exactly what was created
- ✅ Immediate feedback
- ✅ No inconsistent state
- ✅ Multiple records created atomically

### Decision 2: Custom Button in Form Footer ✅

**Implementation:**
1. CSS injection hides standard "Save and Close" button
2. DOM manipulation injects custom button into footer
3. MutationObserver re-injects if footer re-renders
4. Fallback button inside PCF if injection fails

**Benefits:**
- ✅ Button in expected location (users look at footer)
- ✅ No duplicate buttons confusing users
- ✅ Clean UX (looks like standard Quick Create)
- ✅ Button text updates dynamically

### Decision 3: Adaptive Upload Strategy ✅

**Based on TASK-7B-2A design:**

**Small/Few Files (Sync-Parallel):**
- Criteria: ≤3 files AND all <10MB AND total <20MB
- Upload: All files in parallel (fast!)
- Records: Created sequentially (avoid throttling)
- Time: ~3-4 seconds for 3×2MB files
- UX: Simple spinner

**Large/Many Files (Long-Running Batched):**
- Criteria: >3 files OR any >10MB OR total >20MB
- Upload: Batched (2-5 per batch based on avg file size)
- Records: Created sequentially
- Time: ~17-25 seconds for 5×15MB files
- UX: Detailed progress (file-by-file status)

**Why:**
- ✅ Best performance for common case (few small files)
- ✅ Safe for large files (controlled memory usage)
- ✅ Prevents browser crashes
- ✅ Appropriate UX for each scenario

### Decision 4: Field Binding (But Don't Use It) ✅

**Quick Create Requirement:** PCF must bind to a field.

**Solution:** Bind to `sprk_fileuploadmetadata` field, but PCF doesn't actually write to it.

**Why:**
- ✅ Satisfies Quick Create technical requirement
- ✅ PCF creates records directly (bypasses form save)
- ✅ Field exists but remains empty
- ✅ No confusion about data storage

---

## Technical Architecture

### Components

```
UniversalQuickCreate (PCF Control)
├─ FileUploadField.tsx (React UI)
│  ├─ File picker (HTML5 multi-select)
│  ├─ Selected files list (with remove option)
│  ├─ Shared description field
│  └─ Progress indicator (during upload)
│
├─ Button Management (NEW)
│  ├─ hideStandardButtons() - CSS injection
│  ├─ injectCustomButtonInFooter() - DOM manipulation
│  ├─ updateButtonState() - Dynamic text/status
│  ├─ setupFooterWatcher() - MutationObserver
│  └─ closeQuickCreateForm() - With subgrid refresh
│
├─ MultiFileUploadService.ts (from TASK-7B-2A)
│  ├─ determineUploadStrategy() - Sync vs long-running
│  ├─ handleSyncParallelUpload() - Fast path
│  ├─ handleLongRunningUpload() - Safe path
│  ├─ calculateBatchSize() - Adaptive batching
│  └─ createDocumentRecord() - Called N times
│
├─ FileUploadService.ts (existing)
│  └─ uploadFile() - Single file to SPE
│
└─ SdapApiClient.ts (existing, MSAL-enabled)
   └─ POST /api/spe/upload
```

### Data Flow

```
1. User clicks "+ New Document" from Matter subgrid
   ↓
2. Quick Create form opens with PCF control
   ↓
3. PCF injects custom button in footer, hides standard button
   ↓
4. User selects 3 files via file picker
   ↓
5. PCF updates button: "Save and Create 3 Documents"
   ↓
6. User clicks custom button in footer
   ↓
7. Button disabled, text changes: "Uploading..."
   ↓
8. MultiFileUploadService determines strategy: sync-parallel
   ↓
9. Upload file 1 to SPE → Get metadata → Create Document record 1
10. Update button: "Uploading 2 of 3..."
11. Upload file 2 to SPE → Get metadata → Create Document record 2
12. Update button: "Uploading 3 of 3..."
13. Upload file 3 to SPE → Get metadata → Create Document record 3
   ↓
14. PCF closes Quick Create form
   ↓
15. PCF triggers parent subgrid refresh
   ↓
16. User sees 3 new Document records in subgrid
```

### Upload Strategy Decision Tree

```
User selects files
    ↓
MultiFileUploadService.determineUploadStrategy()
    ↓
    ├─ Count ≤3 AND max size <10MB AND total <20MB?
    │   ↓ YES
    │   Sync-Parallel Upload (Fast Path)
    │   ├─ Upload all 3 files in parallel (Promise.all)
    │   ├─ MSAL token: 420ms (file 1), 5ms (files 2-3, cached)
    │   ├─ Total upload: ~0.8s (longest file)
    │   ├─ Create records sequentially: 3 × 0.5s = 1.5s
    │   └─ Total time: ~2.3 seconds ✓
    │
    └─ Count >3 OR any file >10MB OR total >20MB?
        ↓ YES
        Long-Running Batched Upload (Safe Path)
        ├─ Calculate batch size: 2-5 (based on avg file size)
        ├─ Upload batch 1 (2 files) in parallel
        ├─ Create records for batch 1 sequentially
        ├─ Upload batch 2 (2 files) in parallel
        ├─ Create records for batch 2 sequentially
        ├─ Upload batch 3 (1 file)
        ├─ Create record for batch 3
        ├─ Show detailed progress per file
        └─ Total time: ~17-25 seconds ✓
```

---

## Control Manifest Changes

### Remove (from v1.0):
```xml
<!-- REMOVE: Dataset binding -->
<data-set name="dataset" ...>

<!-- REMOVE: Field inheritance config -->
<property name="defaultValueMappings" .../>
```

### Keep/Add:
```xml
<!-- Field binding (Quick Create requirement) -->
<property name="speMetadata"
          display-name-key="SPE_Metadata_Field"
          of-type="Multiple"
          usage="bound"
          required="false" />

<!-- Configuration parameters -->
<property name="sdapApiBaseUrl"
          display-name-key="SDAP_API_URL"
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

---

## Success Criteria

### Functional Requirements

- ✅ Standard "+ New Document" button launches Quick Create with PCF
- ✅ User can select multiple files (multi-select file picker)
- ✅ Selected files displayed in list (can remove before upload)
- ✅ User can add shared description for all files
- ✅ Custom "Save and Create Documents" button in form footer
- ✅ Standard "Save and Close" button hidden
- ✅ Button text updates dynamically based on file count
- ✅ Button shows progress during upload ("Uploading 2 of 3...")
- ✅ Progress indicator shows file-by-file status
- ✅ Each file creates separate Document record
- ✅ All records include SPE metadata (URL, drive item ID, etc.)
- ✅ Form closes automatically after completion
- ✅ Parent subgrid refreshes showing new records

### Performance Requirements

- ✅ 3 small files (2MB each): Complete in 3-4 seconds (sync-parallel)
- ✅ 5 large files (15MB each): Complete in 17-25 seconds (long-running)
- ✅ MSAL token caching: 5ms per file after first (82x improvement)
- ✅ No browser crashes with 10 × 50MB files
- ✅ Adaptive strategy selection automatic (no user configuration)

### Error Handling

- ✅ Partial success supported (4 of 5 succeed → creates 4 records)
- ✅ Clear error messages ("invoice.pdf failed: Network error")
- ✅ Failed uploads don't block successful ones
- ✅ Form doesn't close if errors occur
- ✅ User can retry failed uploads
- ✅ No orphaned files (if record creation fails, SPE file still exists)

---

## Dependencies

### External Services
- ✅ SDAP BFF API (deployed and accessible)
- ✅ SharePoint Embedded containers provisioned on Matters
- ✅ Azure AD app registration for MSAL

### NPM Packages (Already Installed)
- ✅ `react@18.2.0`
- ✅ `react-dom@18.2.0` (using createRoot API)
- ✅ `@fluentui/react-components@9.54.0`
- ✅ `@fluentui/react-icons@2.0.239`
- ✅ `@azure/msal-browser@4.24.1`

### Dataverse
- ✅ Document entity (`sprk_document`) with required fields
- ✅ Matter entity (`sprk_matter`) with `sprk_containerid` field
- ✅ `sprk_fileuploadmetadata` field (10,000 chars) - created by user
- ✅ Test Matter record with valid Container ID

### Existing Code (Reuse from TASK-7B-2A)
- ✅ `MultiFileUploadService.ts` - Adaptive strategy implementation
- ✅ `FileUploadService.ts` - Single file upload
- ✅ `SdapApiClient.ts` - MSAL-enabled API client
- ✅ `MsalAuthProvider.ts` - Authentication
- ✅ `types/index.ts` - TypeScript types

---

## Constraints

### ADR Constraints
- ❌ **NO Dataverse plugins** (explicitly stated by user)
- ❌ **NO backend processing** in this sprint

### Technical Constraints
- ✅ Quick Create forms only support field-level controls (not datasets)
- ✅ Quick Create "Save and Close" creates ONE record (not multiple)
- ✅ PCF must bypass Quick Create save mechanism
- ✅ Button injection relies on DOM manipulation (may break on Power Apps updates)
- ✅ Form footer selectors may change (use multiple fallback selectors)

### Business Constraints
- ✅ File upload only (no field inheritance this sprint)
- ✅ Document entity only (no Task, Contact, etc.)
- ✅ Manual Container ID entry if not auto-detected from parent

---

## Risks & Mitigations

### Risk 1: Form Footer Selectors Change (Power Apps Update)

**Risk:** Custom button injection fails if footer DOM structure changes

**Mitigation:**
- Use 6+ fallback selectors (data-id, class, aria-label, etc.)
- MutationObserver re-injects button if removed
- Fallback: Show button inside PCF control if footer injection fails
- Monitor Power Apps release notes for changes

**Impact:** Medium (button appears in wrong location)
**Likelihood:** Low (footer structure stable)
**Priority:** Medium

### Risk 2: Parent Subgrid Doesn't Refresh

**Risk:** Records created but not visible without manual refresh

**Mitigation:**
- Try 3 refresh methods (Xrm.Page, Navigation API, grid control)
- Fallback: Show success message "3 documents created. Please refresh."
- Test across Power Apps versions

**Impact:** Low (cosmetic only, records exist)
**Likelihood:** Low
**Priority:** Low

### Risk 3: Large File Upload Timeout

**Risk:** Files >100MB may timeout

**Mitigation:**
- Document file size limits (recommend <50MB per file)
- Show warning if file >50MB selected
- Future: Implement chunked upload for large files

**Impact:** Medium (users can't upload large files)
**Likelihood:** Medium (depends on user behavior)
**Priority:** Low (can address in future sprint)

### Risk 4: MSAL Authentication Fails

**Risk:** User not authenticated, all uploads fail

**Mitigation:**
- MSAL handles auth prompts automatically
- Clear error message: "Authentication failed. Please sign in."
- User can retry after authentication
- Token refresh automatic (1 hour expiry)

**Impact:** High (no uploads work)
**Likelihood:** Low (MSAL handles auth flows)
**Priority:** High

---

## Testing Strategy

### Test Scenarios (15 total)

1. **Single File Upload** - Baseline test
2. **Multi-File Upload (3 files, small)** - Sync-parallel strategy
3. **Multi-File Upload (5 files, large)** - Long-running strategy
4. **Partial Failure** - 4 of 5 files succeed
5. **No Container ID** - Error handling
6. **Network Error During Upload** - Error handling
7. **MSAL Authentication Failure** - 401 error
8. **Large File (50MB)** - Performance test
9. **Custom Button in Footer** - Verify button placement
10. **Standard Button Hidden** - Verify CSS injection
11. **Button Text Updates** - Verify dynamic text
12. **Progress Indicators** - Verify file-by-file status
13. **Subgrid Refresh** - Verify records appear
14. **Form Closes Automatically** - Verify close logic
15. **Browser Compatibility** - Chrome, Edge, Firefox

See revised WORK-ITEM-7-TESTING.md for detailed test plan.

---

## Future Enhancements (Not This Sprint)

### Sprint 7C: Backend Field Inheritance (Future)
- Dataverse plugin to auto-populate Matter lookup
- Auto-copy Container ID from Matter
- Field mappings (Matter → Document)
- Clear `sprk_fileuploadmetadata` field after processing

### Future Sprints:
- Chunked upload for very large files (>100MB)
- True parallel file upload (faster than batched)
- File preview before upload
- Drag-and-drop file upload
- Cancel individual files during upload
- Resume upload on failure
- File type restrictions (e.g., only PDFs)
- Support for more entity types (Task, Contact, etc.)
- Field validation beyond PCF
- Integration with Power Automate flows

---

## Key Files

### To Create:
- `MultiFileUploadService.ts` - Adaptive upload strategy (from TASK-7B-2A)
- `FileUploadField.tsx` - React UI with file picker
- `UniversalQuickCreatePCF.ts` - PCF control with button management

### To Modify:
- `ControlManifest.Input.xml` - Update to field binding

### To Keep (No Changes):
- `FileUploadService.ts` - Single file upload
- `SdapApiClient.ts` - API client
- `SdapApiClientFactory.ts` - Factory
- `MsalAuthProvider.ts` - Authentication
- `types/index.ts` - Types
- `utils/logger.ts` - Logging

### To Delete:
- `DynamicFormFields.tsx` - Not needed
- `QuickCreateForm.tsx` - Not needed
- `EntityFieldDefinitions.ts` - Not needed
- `DataverseRecordService.ts` - Not needed
- `FieldMetadata.ts` - Not needed

---

## Timeline: 5-7 Days

### Day 1-2: Core Implementation (2 days)
- Update ControlManifest.Input.xml
- Create MultiFileUploadService.ts (from TASK-7B-2A)
- Implement UniversalQuickCreatePCF.ts with button management
- Test button injection and hiding

### Day 3-4: UI & Progress (2 days)
- Create FileUploadField.tsx
- Implement progress indicators
- Test sync-parallel vs long-running strategies
- Test multi-file upload end-to-end

### Day 5: Form Configuration & Integration (1 day)
- Configure Quick Create form
- Test from Matter subgrid
- Test subgrid refresh
- Verify button placement

### Day 6-7: Testing & Documentation (1-2 days)
- Run all 15 test scenarios
- Browser compatibility testing
- Create admin configuration guide
- Create user guide
- Deploy to test environment

---

**Sprint Focus:** File upload + multiple record creation + custom footer button. No backend plugins. ✅

**Date:** 2025-10-07
**Status:** Scope Approved - Ready to Implement
