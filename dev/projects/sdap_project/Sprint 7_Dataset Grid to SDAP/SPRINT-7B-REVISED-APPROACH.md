# Sprint 7B - Revised Approach: File Upload Only PCF

**Version:** 2.0.0
**Date:** 2025-10-07
**Status:** ✅ APPROVED - Ready to Implement
**Previous Approach:** Universal Quick Create with field inheritance (archived)
**New Approach:** File Upload Only PCF + Backend field inheritance

---

## What Changed and Why

### Original Approach (v1.0 - Now Archived)

**Scope:** PCF control that handles:
- ❌ Field inheritance (Matter → Document)
- ❌ Dynamic form field rendering
- ❌ Parent record data retrieval
- ❌ File upload to SharePoint Embedded

**Problems:**
- Too complex for Quick Create forms
- Form context access unreliable
- Race conditions between file upload and form save
- Maintenance burden high

---

### Revised Approach (v2.0 - Current)

**Scope Split into Two Parts:**

#### Part 1: File Upload PCF Control (Sprint 7B)
- ✅ Upload file(s) to SharePoint Embedded
- ✅ Store metadata in `sprk_fileuploadmetadata` field
- ✅ MSAL authentication
- ✅ Multi-file support

#### Part 2: Backend Field Inheritance (Sprint 7B or Future)
- ✅ Dataverse plugin applies field mappings
- ✅ Matter → Document inheritance
- ✅ Reusable for bulk imports
- ✅ More reliable than frontend

**Benefits:**
- ✅ Simpler PCF (70% less code)
- ✅ Quick Create compatible
- ✅ Backend more reliable
- ✅ Clean separation of concerns
- ✅ 1-2 weeks instead of 3-4 weeks

---

## Repository Cleanup Plan

### Files to Archive (Old Approach)

These files represent the old "full Quick Create" approach and should be archived:

#### Documentation Files (Move to Archive):

```
/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/
├─ TASK-7B-1-QUICK-CREATE-SETUP.md → ARCHIVE
├─ TASK-7B-3-CONFIGURABLE-FIELDS-UPDATED.md → ARCHIVE
├─ TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md → ARCHIVE
├─ TASK-7B-4-TESTING-DEPLOYMENT.md → ARCHIVE (replaced by UPDATED version)
└─ FIELD-INHERITANCE-FLOW.md → ARCHIVE (replaced by backend design doc)
```

**Action:** Move to `/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/`

#### Source Code Files (Will be Replaced):

```
/src/controls/UniversalQuickCreate/UniversalQuickCreate/
├─ components/
│  ├─ DynamicFormFields.tsx → DELETE (not needed for file upload only)
│  ├─ QuickCreateForm.tsx → REPLACE (new simple version)
│  └─ FilePickerField.tsx → KEEP (reuse)
├─ config/
│  └─ EntityFieldDefinitions.ts → DELETE (field inheritance moved to backend)
├─ services/
│  ├─ DataverseRecordService.ts → DELETE (record creation now handled by Quick Create form)
│  ├─ FileUploadService.ts → KEEP (core functionality)
│  ├─ SdapApiClient.ts → KEEP (core functionality)
│  └─ SdapApiClientFactory.ts → KEEP (core functionality)
├─ types/
│  ├─ FieldMetadata.ts → DELETE (not needed)
│  └─ index.ts → UPDATE (remove FieldMetadata references)
└─ UniversalQuickCreatePCF.ts → REPLACE (simpler version)
```

---

### New File Structure

```
/src/controls/UniversalQuickCreate/UniversalQuickCreate/
├─ components/
│  ├─ FileUploadField.tsx → NEW (simplified file upload UI)
│  └─ FilePickerField.tsx → KEEP (existing)
├─ services/
│  ├─ auth/
│  │  ├─ MsalAuthProvider.ts → KEEP
│  │  └─ msalConfig.ts → KEEP
│  ├─ FileUploadService.ts → KEEP (maybe simplify)
│  ├─ SdapApiClient.ts → KEEP
│  └─ SdapApiClientFactory.ts → KEEP
├─ types/
│  ├─ auth.ts → KEEP
│  └─ index.ts → UPDATE
├─ utils/
│  └─ logger.ts → KEEP
├─ css/
│  └─ FileUploadControl.css → RENAME from UniversalQuickCreate.css
├─ ControlManifest.Input.xml → UPDATE (new manifest)
├─ index.ts → KEEP
└─ FileUploadPCF.ts → NEW (replaces UniversalQuickCreatePCF.ts)
```

---

### Backend Plugin (New)

```
/src/plugins/Spaarke.Plugins.FieldInheritance/
├─ DocumentFieldInheritancePlugin.cs → NEW
├─ Models/
│  └─ SpeFileMetadata.cs → NEW
└─ Spaarke.Plugins.FieldInheritance.csproj → NEW
```

---

## Detailed Cleanup Actions

### Step 1: Archive Old Documentation

Create archive folder and move old docs:

```bash
# Create archive folder
mkdir -p "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate"

# Move old docs
mv "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/TASK-7B-1-QUICK-CREATE-SETUP.md" \
   "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/"

mv "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/TASK-7B-3-CONFIGURABLE-FIELDS-UPDATED.md" \
   "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/"

mv "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md" \
   "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/"

mv "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/TASK-7B-4-TESTING-DEPLOYMENT.md" \
   "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/"

mv "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/FIELD-INHERITANCE-FLOW.md" \
   "/c/code_files/spaarke/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/ARCHIVE-v1-UniversalQuickCreate/"
```

**Create README in archive:**

```markdown
# Archive: Universal Quick Create v1.0 (Full Form Approach)

This folder contains documentation for the original Sprint 7B approach that attempted to
build a full Quick Create form replacement with field inheritance in the PCF control.

**Archived Date:** 2025-10-07
**Reason:** Approach revised to simpler "File Upload Only" PCF + Backend plugin
**Status:** Not Implemented - Design only

## Why Archived

The original approach was too complex for Quick Create forms:
- Form context access unreliable
- Field inheritance fragile
- High maintenance burden

## New Approach (v2.0)

See current Sprint 7B documentation in parent folder:
- File Upload Only PCF control
- Backend plugin for field inheritance
- Simpler, more reliable, faster to implement
```

---

### Step 2: Delete Unused Source Files

```bash
# Delete components that aren't needed
rm "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/components/DynamicFormFields.tsx"
rm "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/components/QuickCreateForm.tsx"

# Delete config (field definitions moved to backend)
rm "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityFieldDefinitions.ts"
rmdir "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/config"

# Delete DataverseRecordService (not needed - Quick Create handles save)
rm "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DataverseRecordService.ts"

# Delete FieldMetadata types (not needed)
rm "/c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/types/FieldMetadata.ts"
```

---

### Step 3: Rename Control

The control is no longer "UniversalQuickCreate" - it's specifically a file upload control.

**Option A: Rename folder and control (Recommended)**

```bash
# Rename main folder
mv "/c/code_files/spaarke/src/controls/UniversalQuickCreate" \
   "/c/code_files/spaarke/src/controls/SpeFileUpload"

# Update namespace in manifest
# Spaarke.Controls.UniversalQuickCreate → Spaarke.Controls.SpeFileUpload
```

**Option B: Keep folder name, rename control internally**

```bash
# Keep folder as is for now
# Update manifest:
# - Display name: "SPE File Upload"
# - Constructor: "SpeFileUpload"
# - Description: "Upload files to SharePoint Embedded"
```

**Recommendation:** Option B for now (less disruption), can rename folder later if needed.

---

### Step 4: Update Documentation Index

Keep active documentation:

```
/dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/
├─ SPRINT-7B-REVISED-APPROACH.md → NEW (this file)
├─ TASK-7B-REVISED-IMPLEMENTATION-PLAN.md → NEW
├─ TASK-7B-FILE-UPLOAD-PCF-SPEC.md → NEW
├─ TASK-7B-BACKEND-PLUGIN-SPEC.md → NEW
│
├─ TASK-7B-1-COMPLETION-SUMMARY.md → KEEP (still relevant)
├─ TASK-7B-2-COMPLETION-SUMMARY.md → KEEP (file upload core)
├─ TASK-7B-2-FILE-UPLOAD-SPE.md → KEEP (still relevant)
├─ TASK-7B-2-IMPLEMENTATION-GUIDE.md → KEEP (still relevant)
├─ TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md → UPDATE (now part of core)
├─ TASK-7B-4-FIELD-MAPPING-UPDATE.md → KEEP (backend plugin uses this)
├─ TASK-7B-4-TESTING-DEPLOYMENT-UPDATED.md → UPDATE
│
└─ ARCHIVE-v1-UniversalQuickCreate/
   ├─ README.md
   ├─ TASK-7B-1-QUICK-CREATE-SETUP.md
   ├─ TASK-7B-3-CONFIGURABLE-FIELDS-UPDATED.md
   ├─ TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md
   ├─ TASK-7B-4-TESTING-DEPLOYMENT.md
   └─ FIELD-INHERITANCE-FLOW.md
```

---

### Step 5: Update Active Documentation

#### Update: TASK-7B-4-TESTING-DEPLOYMENT-UPDATED.md

Add note at top:

```markdown
# ⚠️ IMPORTANT: Approach Revised

**Date:** 2025-10-07

This document has been updated to reflect the revised Sprint 7B approach:
- PCF control: File upload only (no field inheritance)
- Field inheritance: Backend plugin (separate component)

See [SPRINT-7B-REVISED-APPROACH.md](SPRINT-7B-REVISED-APPROACH.md) for details.

---
```

#### Update: TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md

Add note:

```markdown
# ⚠️ Multi-File Now Part of Core

**Date:** 2025-10-07

Multi-file upload is now part of the core File Upload PCF control, not a separate enhancement.

See [TASK-7B-FILE-UPLOAD-PCF-SPEC.md](TASK-7B-FILE-UPLOAD-PCF-SPEC.md) for implementation.

---
```

---

## New Implementation Plan

### Sprint 7B Revised Scope

#### Part 1: File Upload PCF Control (5 days)

**Goal:** Create PCF control that uploads files to SPE and stores metadata

**Tasks:**
1. Update manifest (`ControlManifest.Input.xml`)
   - Change to field-bound control
   - Bind to `sprk_fileuploadmetadata`
   - Remove dataset binding
   - Update display name/description

2. Create `FileUploadPCF.ts` (replace `UniversalQuickCreatePCF.ts`)
   - Remove field inheritance logic
   - Remove form context complexity
   - Focus on file upload only
   - Read Container ID from parent context
   - Store metadata in bound field

3. Create `FileUploadField.tsx` (replace `QuickCreateForm.tsx`)
   - Simple file picker UI
   - Multi-file support
   - Upload progress indicators
   - File list display

4. Keep existing services:
   - `MsalAuthProvider.ts`
   - `FileUploadService.ts`
   - `SdapApiClient.ts`

5. Test file upload functionality

**Deliverable:** Working file upload PCF that binds to `sprk_fileuploadmetadata`

---

#### Part 2: Backend Field Inheritance Plugin (3 days)

**Goal:** Create Dataverse plugin that applies field inheritance on Document create

**Tasks:**
1. Create plugin project
   - `Spaarke.Plugins.FieldInheritance`
   - Target: .NET Framework 4.6.2 or .NET 6

2. Implement `DocumentFieldInheritancePlugin.cs`
   - Trigger: OnCreate, Pre-Operation
   - Read `sprk_fileuploadmetadata` JSON
   - Get parent Matter from context
   - Apply field mappings
   - Populate SPE fields
   - Handle multi-file (create additional records)
   - Clear `sprk_fileuploadmetadata`

3. Write unit tests
   - Test field inheritance logic
   - Test JSON parsing
   - Test multi-file scenario

4. Register plugin in Dataverse

**Deliverable:** Plugin that applies field inheritance reliably

---

#### Part 3: Quick Create Form Setup (1 day)

**Tasks:**
1. Create Quick Create form for Document
2. Add fields:
   - `sprk_documenttitle` (visible)
   - `sprk_description` (visible)
   - `sprk_matter` (visible/auto)
   - `sprk_fileuploadmetadata` (hidden, PCF bound)
3. Configure PCF control parameters
4. Test end-to-end

**Deliverable:** Working Quick Create form with file upload

---

#### Part 4: Testing & Documentation (2 days)

**Tasks:**
1. Test single file upload
2. Test multi-file upload
3. Test field inheritance
4. Test from Matter subgrid
5. Document configuration
6. Document troubleshooting

**Deliverable:** Production-ready solution

---

### Total Timeline: 11 days (2 weeks)

| Phase | Duration | Status |
|-------|----------|--------|
| Phase 1: File Upload PCF | 5 days | Not Started |
| Phase 2: Backend Plugin | 3 days | Not Started |
| Phase 3: Form Setup | 1 day | Not Started |
| Phase 4: Testing & Docs | 2 days | Not Started |
| **Total** | **11 days** | **Field created ✅** |

---

## What We're Keeping from v1.0

### Code Components:
- ✅ `MsalAuthProvider.ts` - MSAL authentication (core)
- ✅ `FileUploadService.ts` - File upload logic (core)
- ✅ `SdapApiClient.ts` - SDAP API integration (core)
- ✅ `FilePickerField.tsx` - File picker UI (reuse)

### Documentation:
- ✅ `TASK-7B-1-COMPLETION-SUMMARY.md` - MSAL setup still relevant
- ✅ `TASK-7B-2-COMPLETION-SUMMARY.md` - File upload still core
- ✅ `TASK-7B-2-FILE-UPLOAD-SPE.md` - Still relevant
- ✅ `TASK-7B-4-FIELD-MAPPING-UPDATE.md` - Backend plugin uses this

### Patterns:
- ✅ Field mapping JSON format (reused in plugin)
- ✅ Lookup vs simple field detection logic (reused in plugin)
- ✅ MSAL token caching pattern
- ✅ Error handling patterns

---

## What We're Removing/Archiving

### Components Not Needed:
- ❌ `DynamicFormFields.tsx` - Form rendering moved to Quick Create
- ❌ `QuickCreateForm.tsx` - Quick Create handles form
- ❌ `EntityFieldDefinitions.ts` - Config moved to backend
- ❌ `DataverseRecordService.ts` - Quick Create saves records
- ❌ `FieldMetadata.ts` - Types not needed

### Documentation Archived:
- ❌ `TASK-7B-1-QUICK-CREATE-SETUP.md` - Old approach
- ❌ `TASK-7B-3-CONFIGURABLE-FIELDS-UPDATED.md` - Old approach
- ❌ `TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md` - Now in backend
- ❌ `FIELD-INHERITANCE-FLOW.md` - Replaced by backend design

---

## Migration Checklist

### Immediate Actions (Today):

- [x] Field `sprk_fileuploadmetadata` created in Dataverse ✅
- [ ] Create archive folder for v1.0 docs
- [ ] Move old docs to archive
- [ ] Create archive README
- [ ] Delete unused source files
- [ ] Update active documentation with notes

### Development Phase (Next 2 weeks):

- [ ] Update manifest for file-bound control
- [ ] Create `FileUploadPCF.ts`
- [ ] Create `FileUploadField.tsx`
- [ ] Test file upload
- [ ] Create backend plugin project
- [ ] Implement field inheritance logic
- [ ] Register and test plugin
- [ ] Configure Quick Create form
- [ ] End-to-end testing
- [ ] Documentation updates

---

## Success Criteria

### File Upload PCF:
- ✅ User can select single file
- ✅ User can select multiple files
- ✅ Files upload to SharePoint Embedded
- ✅ Metadata stored in `sprk_fileuploadmetadata`
- ✅ MSAL authentication works
- ✅ Error handling robust

### Backend Plugin:
- ✅ Field inheritance applied on create
- ✅ Matter lookup populated
- ✅ Container ID copied from Matter
- ✅ SPE fields populated from metadata
- ✅ Multi-file creates additional records
- ✅ Metadata field cleared after processing

### Quick Create Form:
- ✅ Opens from Matter subgrid
- ✅ Opens from "+ New" menu
- ✅ File upload works
- ✅ Form saves successfully
- ✅ Document created with all fields

---

## Questions & Decisions

### Q1: Rename control folder?

**Decision:** Keep as `UniversalQuickCreate` for now, can rename later if needed.

**Rationale:** Less disruption, solution already exists in Dataverse with this name.

---

### Q2: Keep old solution package?

**Decision:** Yes, keep `UniversalQuickCreateSolution.zip` but rebuild with new code.

**Rationale:** Solution name can stay the same, just update the control inside it.

---

### Q3: Backend plugin in same solution?

**Decision:** Separate solution for plugin, then include in managed solution later.

**Rationale:** Easier to develop and test independently, then combine for deployment.

---

## Next Steps

1. **Execute cleanup** (run migration checklist)
2. **Review new implementation plan** (next doc)
3. **Start Phase 1** (File Upload PCF)
4. **Daily standups** to track progress

---

**Status:** ✅ Approved
**Date:** 2025-10-07
**Ready to Proceed:** Yes
**Estimated Completion:** 2025-10-22 (2 weeks from start)
