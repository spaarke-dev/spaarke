# Sprint 7B - Implementation Plan
## Document Quick Create with File Upload ONLY

**Timeline:** 5-7 days
**Focus:** File upload to SharePoint Embedded
**No Backend:** Field inheritance out of scope

---

## Overview

This sprint builds a PCF control that uploads files to SharePoint Embedded and integrates with Quick Create forms.

**What we're building:**
- File upload PCF control
- Quick Create form configuration
- Multi-file support

**What we're NOT building:**
- Backend plugins
- Field inheritance
- Automatic field mapping

---

## Phase 1: PCF Control Core (2 days)

### Day 1: Update Manifest & Create Core PCF

#### Task 1.1: Update ControlManifest.Input.xml

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml`

**Changes:**
1. Remove `data-set` binding
2. Add `speMetadata` property (bound field)
3. Remove `defaultValueMappings` (not needed)
4. Keep `sdapApiBaseUrl` and `allowMultipleFiles`

**See:** Work Item 1

---

#### Task 1.2: Create FileUploadPCF.ts

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts`

**Core Methods:**
- `init()` - Initialize, get Container ID from form
- `handleFilesSelected()` - Upload files to SPE
- `getOutputs()` - Return metadata JSON
- `destroy()` - Cleanup

**Key Logic:**
```typescript
// Get Container ID from form context
const containerid = context.parameters.containerid?.raw || "";

// Upload files
const metadata = await uploadToSpe(file, containerid);

// Store in bound field
this._metadataValue = JSON.stringify([metadata]);
this._notifyOutputChanged();
```

**See:** Work Item 2

---

### Day 2: File Upload Logic & Testing

#### Task 1.3: Implement File Upload

**Files:**
- Keep: `services/FileUploadService.ts`
- Keep: `services/SdapApiClient.ts`
- Keep: `services/auth/MsalAuthProvider.ts`

**Test:**
- Upload single file
- Verify SPE metadata returned
- Check field updated correctly

**See:** Work Item 3

---

## Phase 2: Multi-File & UI (2 days)

### Day 3: Multi-File Support

#### Task 2.1: Add Multi-File Upload

**Update:** `FileUploadPCF.ts`

**Logic:**
```typescript
async handleFilesSelected(files: FileList) {
    const uploadedFiles = [];

    for (let i = 0; i < files.length; i++) {
        const metadata = await uploadToSpe(files[i], containerid);
        uploadedFiles.push(metadata);
    }

    this._metadataValue = JSON.stringify(uploadedFiles);
    this._notifyOutputChanged();
}
```

**See:** Work Item 4

---

### Day 4: File Picker UI

#### Task 2.2: Create FileUploadField.tsx

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/components/FileUploadField.tsx`

**Features:**
- File picker (multiple files)
- File list display
- Upload button
- Progress indicators
- Error messages

**See:** Work Item 5

---

## Phase 3: Quick Create Form (1 day)

### Day 5: Form Configuration

#### Task 3.1: Configure Quick Create Form

**Steps:**
1. Create Quick Create form for Document
2. Add fields:
   - sprk_documenttitle (visible)
   - sprk_description (visible)
   - sprk_matter (visible, optional)
   - sprk_containerid (visible or hidden)
   - sprk_fileuploadmetadata (hidden, PCF bound)
3. Add PCF control to `sprk_fileuploadmetadata`
4. Configure parameters:
   - sdapApiBaseUrl
   - allowMultipleFiles: true
5. Publish form

**See:** Work Item 6

---

## Phase 4: Testing & Documentation (1-2 days)

### Day 6: Testing

#### Task 4.1: Test All Scenarios

**Test Cases:**
1. Single file upload
2. Multi-file upload (3 files)
3. From Matter subgrid
4. From "+ New" menu
5. Error: No Container ID
6. Error: Upload fails
7. Cancel operation

**See:** Work Item 7

---

### Day 7: Documentation

#### Task 4.2: Create User Guide

**Documents:**
- Quick Create form setup guide
- User instructions
- Troubleshooting guide

**See:** Work Item 8

---

## Work Items Summary

1. **Work Item 1:** Update Control Manifest
2. **Work Item 2:** Create FileUploadPCF.ts
3. **Work Item 3:** Implement File Upload Logic
4. **Work Item 4:** Add Multi-File Support
5. **Work Item 5:** Create File Picker UI
6. **Work Item 6:** Configure Quick Create Form
7. **Work Item 7:** Testing
8. **Work Item 8:** Documentation

---

## Files to Create/Modify

### Create:
- `FileUploadPCF.ts` (replaces UniversalQuickCreatePCF.ts)
- `components/FileUploadField.tsx`

### Modify:
- `ControlManifest.Input.xml`
- `index.ts` (update imports)
- `types/index.ts` (update exports)

### Keep (No Changes):
- `services/auth/MsalAuthProvider.ts`
- `services/auth/msalConfig.ts`
- `services/FileUploadService.ts`
- `services/SdapApiClient.ts`
- `services/SdapApiClientFactory.ts`
- `components/FilePickerField.tsx`
- `utils/logger.ts`

### Delete (Not Needed):
- `components/DynamicFormFields.tsx`
- `components/QuickCreateForm.tsx`
- `config/EntityFieldDefinitions.ts`
- `services/DataverseRecordService.ts`
- `types/FieldMetadata.ts`

---

## Build & Deploy

### Build PCF:
```bash
cd src/controls/UniversalQuickCreate
npm run build
```

### Build Solution:
```bash
cd UniversalQuickCreateSolution
dotnet build -c Release
```

### Deploy:
```bash
pac solution import --path bin/Release/UniversalQuickCreateSolution.zip
```

---

## Success Criteria

- ✅ PCF builds without errors
- ✅ Single file upload works
- ✅ Multi-file upload works
- ✅ SPE metadata in field
- ✅ Quick Create form functional
- ✅ Works from Matter subgrid
- ✅ Works from "+ New" menu
- ✅ Error handling functional

---

**Focus:** File upload only. No backend. Simple and fast. ✅
