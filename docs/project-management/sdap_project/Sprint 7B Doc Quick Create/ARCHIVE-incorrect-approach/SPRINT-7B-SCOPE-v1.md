# Sprint 7B - Document Quick Create with SPE File Upload

**Version:** 2.0 (Revised Architecture)
**Date:** 2025-10-07
**Timeline:** 5-7 days
**Status:** Ready to Implement

---

## Sprint Goal

Enable users to upload multiple files to SharePoint Embedded from Quick Create forms, creating multiple Document records (one per file) with custom button in form footer.

---

## In Scope ✅

### 1. File Upload PCF Control
- Upload single file to SharePoint Embedded
- Upload multiple files to SharePoint Embedded
- Store SPE metadata in `sprk_fileuploadmetadata` field
- MSAL authentication
- Error handling and user feedback

### 2. Quick Create Form
- Configure Quick Create form for Document entity
- Add PCF control to form
- User can enter Document Title and Description
- User can select file(s) to upload

### 3. Document Record Creation
- Quick Create form saves Document record
- SPE metadata stored in `sprk_fileuploadmetadata` field
- User-entered fields saved (Title, Description)

---

## Out of Scope ❌

### Not in This Sprint:

1. **Backend Field Inheritance** ❌
   - NO Dataverse plugin
   - NO automatic field copying from Matter
   - NO Container ID auto-population
   - Will handle in future sprint or manually

2. **Field Mappings** ❌
   - NO defaultValueMappings configuration
   - NO parent-child field inheritance
   - NO automatic lookup population

3. **Dynamic Form Fields** ❌
   - NO EntityFieldDefinitions
   - NO configurable field rendering
   - Just standard Quick Create form fields

4. **Multi-Entity Support** ❌
   - Document ONLY
   - NO Task, Contact, or other entities
   - Keep it simple

---

## What User Will Do

### Manual Steps (Acceptable for This Sprint):

1. Open Quick Create form for Document
2. **Manually select Matter lookup** (if needed)
3. **Manually enter Container ID** (if field on form)
4. Enter Document Title
5. Enter Description (optional)
6. **Select file(s) using PCF control**
7. Click Save and Close

### What Happens Automatically:

1. ✅ Files upload to SharePoint Embedded
2. ✅ SPE metadata stored in `sprk_fileuploadmetadata` field
3. ✅ Document record created
4. ✅ Multi-file: Additional Document records created (one per file)

### What User Must Do Manually (For Now):

1. ⚠️ Select Matter lookup manually
2. ⚠️ Enter Container ID manually (if required)
3. ⚠️ Ensure Matter has Container ID before uploading

**Note:** Backend automation will be added in future sprint.

---

## Success Criteria

### Must Have:
- ✅ User can select single file
- ✅ File uploads to SharePoint Embedded
- ✅ SPE metadata stored in field
- ✅ Document record created successfully
- ✅ Quick Create form works from Matter subgrid
- ✅ Quick Create form works from "+ New" menu

### Should Have:
- ✅ User can select multiple files
- ✅ Multiple Document records created (one per file)
- ✅ Error handling for failed uploads
- ✅ User feedback (progress, success, errors)

### Nice to Have (If Time):
- ⚠️ File type validation
- ⚠️ File size limits
- ⚠️ Upload progress indicators

---

## Technical Components

### 1. PCF Control: SPE File Upload

**Purpose:** Upload files to SharePoint Embedded

**Responsibilities:**
- Display file picker
- Upload files to SPE via SDAP API
- Store SPE metadata in bound field
- Show upload progress/errors

**Does NOT:**
- Retrieve parent Matter data
- Apply field inheritance
- Populate Container ID
- Create relationships

---

### 2. Quick Create Form

**Purpose:** Standard Dataverse Quick Create form for Document

**Fields:**
- `sprk_documenttitle` (Text, required, visible)
- `sprk_description` (Multiline, optional, visible)
- `sprk_matter` (Lookup, optional, visible) - **User selects manually**
- `sprk_containerid` (Text, optional, visible or hidden) - **User enters manually**
- `sprk_fileuploadmetadata` (Multiline, hidden) - **PCF bound**

**Configuration:**
- PCF control binds to `sprk_fileuploadmetadata`
- Standard form save behavior
- No custom JavaScript needed

---

### 3. SDAP API

**Existing API** - No changes needed

**Endpoints Used:**
- POST `/files/upload` - Upload file to SPE

**Authentication:** MSAL (already configured)

---

## Timeline: 5-7 Days

### Day 1-2: PCF Control Core (2 days)
- Update manifest for field binding
- Create `FileUploadPCF.ts` (simplified)
- Implement file upload logic
- Test single file upload

### Day 3-4: Multi-File & UI (2 days)
- Add multi-file support
- Create file picker UI
- Add progress indicators
- Error handling

### Day 5: Quick Create Form Setup (1 day)
- Configure form
- Add PCF control
- Test end-to-end

### Day 6-7: Testing & Documentation (1-2 days)
- Test all scenarios
- Document configuration
- Create user guide

---

## Dependencies

### Required:
- ✅ `sprk_fileuploadmetadata` field created
- ✅ SDAP API deployed and accessible
- ✅ MSAL configuration in place
- ✅ SharePoint Embedded containers exist on Matters

### Not Required (Out of Scope):
- ❌ Backend plugin
- ❌ Field mapping configuration
- ❌ Dynamic field definitions

---

## Risks & Mitigations

### Risk 1: User forgets to select Matter

**Impact:** File uploads but no Matter relationship

**Mitigation:**
- Document requirement in user guide
- Make Matter field required on form (if appropriate)
- Future: Add validation or backend automation

---

### Risk 2: User doesn't know Container ID

**Impact:** Can't upload file

**Mitigation:**
- Document how to find Container ID
- Hide Container ID field (get from Matter later)
- Future: Backend retrieves from Matter automatically

---

### Risk 3: Multi-file creates too many records

**Impact:** Database bloat

**Mitigation:**
- Limit to 10 files per upload
- Document in UI
- Future: Batch processing or combined records

---

## Future Enhancements (Not This Sprint)

1. **Backend Field Inheritance Plugin**
   - Auto-populate Matter lookup
   - Auto-retrieve Container ID
   - Apply field mappings

2. **Advanced Form Features**
   - Dynamic field rendering
   - Multiple entity support
   - Configurable field definitions

3. **Bulk Operations**
   - Import from CSV
   - Batch file upload
   - Background processing

---

## Key Decisions

### Decision 1: No Backend Plugin (This Sprint)

**Rationale:**
- Keep sprint focused
- Deliver file upload functionality faster
- Backend can be added later without affecting PCF
- Users can work manually in the meantime

---

### Decision 2: Document Entity Only

**Rationale:**
- Simplest use case
- Clear requirements
- Can expand to other entities later
- Proves the pattern

---

### Decision 3: Manual Matter Selection

**Rationale:**
- Out-of-box Quick Create behavior
- No custom code needed
- Works from subgrid (Matter pre-selected)
- Backend automation can be added later

---

## What We're NOT Building

To keep this sprint focused, we are **explicitly NOT building**:

1. ❌ Dataverse plugin for field inheritance
2. ❌ Backend field mapping logic
3. ❌ Automatic Container ID retrieval
4. ❌ Parent-child relationship automation
5. ❌ Dynamic form field configuration
6. ❌ Multiple entity support (Task, Contact, etc.)
7. ❌ Custom field definitions
8. ❌ Field validation beyond PCF
9. ❌ Business rules integration
10. ❌ Power Automate flows

All of the above can be added in future sprints if needed.

---

## Success Metrics

### Sprint Complete When:

1. ✅ User can create Document from Quick Create
2. ✅ User can upload single file
3. ✅ User can upload multiple files
4. ✅ Files stored in SharePoint Embedded
5. ✅ SPE metadata in `sprk_fileuploadmetadata` field
6. ✅ Document records created successfully
7. ✅ Works from Matter subgrid
8. ✅ Works from "+ New" menu
9. ✅ Error handling functional
10. ✅ Documentation complete

---

**Sprint Focus:** File upload to SPE. That's it. Keep it simple. ✅

**Date:** 2025-10-07
**Status:** Scope Defined - Ready to Implement
