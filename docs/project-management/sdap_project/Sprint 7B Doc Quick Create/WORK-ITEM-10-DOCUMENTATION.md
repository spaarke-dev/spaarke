# Work Item 10: Documentation

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 3 hours
**Prerequisites:** Work Items 1-9 completed (implementation and testing)
**Status:** Ready to Start

---

## Objective

Create comprehensive documentation for administrators, developers, and end users. Document configuration, troubleshooting, architecture, and usage.

---

## Context

Documentation ensures:
1. Admins can configure and maintain the feature
2. Developers understand architecture for future enhancements
3. End users know how to use the feature
4. Support teams can troubleshoot issues

**Result:** Complete documentation package for all stakeholders.

---

## Documentation Deliverables

### 1. Administrator Guide
### 2. End User Guide
### 3. Developer Guide
### 4. Troubleshooting Guide
### 5. Configuration Reference

---

## Deliverable 1: Administrator Guide

**File:** `ADMIN-GUIDE-QUICK-CREATE.md`

**Sections:**

### 1.1 Overview
- What is Document Quick Create
- Key features (multi-file upload, SPE integration)
- Benefits (faster document creation, automatic metadata)

### 1.2 Prerequisites
- Dataverse environment
- SharePoint Embedded configured
- SDAP BFF API deployed
- Required security roles

### 1.3 Installation Steps
1. Import SpaarkeControls solution
2. Configure Document Quick Create form
3. Add PCF control to form
4. Configure SDAP API URL
5. Enable Quick Create on Document subgrid
6. Publish customizations

### 1.4 Configuration
- **SDAP API Base URL**: How to set production URL
- **Allow Multiple Files**: Enable/disable multi-select
- **Form Fields**: Which fields to include
- **Security Roles**: Required permissions

### 1.5 Testing
- How to verify installation
- Test Quick Create from Matter record
- Check Document records created
- Verify SPE metadata populated

### 1.6 Maintenance
- Updating to new versions
- Monitoring usage
- Troubleshooting common issues

---

## Deliverable 2: End User Guide

**File:** `USER-GUIDE-QUICK-CREATE.md`

**Sections:**

### 2.1 Overview
- What is Quick Create
- When to use it (vs standard form)

### 2.2 How to Create Documents

**Step-by-step with screenshots:**

1. **Navigate to Matter record**
   - Go to Documents tab
   - Screenshot: Documents subgrid

2. **Click "+ New Document"**
   - Screenshot: New Document button

3. **Quick Create opens**
   - Screenshot: Quick Create dialog

4. **Add files**
   - Click "+ Add File" button
   - Select one or multiple files
   - Screenshot: File selection

5. **Review selected files**
   - Files appear in list
   - Can remove files
   - Screenshot: File list

6. **Fill optional fields**
   - Title (required)
   - Description (optional)
   - Screenshot: Form fields

7. **Click "Save and Create Documents"**
   - Screenshot: Custom button

8. **Watch progress**
   - Progress bar shows status
   - Per-file status icons
   - Screenshot: Upload progress

9. **Done!**
   - Form closes automatically
   - New documents appear in subgrid
   - Screenshot: Updated subgrid

### 2.3 Tips and Tricks
- Can select multiple files at once (Ctrl+Click or Shift+Click)
- Can add files in batches
- Can remove files before uploading
- Progress shows real-time status
- Form closes automatically when done

### 2.4 What to Do If...
- **Button is disabled**: Select at least one file
- **Upload fails**: Check network connection, retry
- **File doesn't appear**: Refresh subgrid manually
- **Error message**: Contact support with error details

---

## Deliverable 3: Developer Guide

**File:** `DEVELOPER-GUIDE-QUICK-CREATE.md`

**Sections:**

### 3.1 Architecture Overview

**Diagram:**
```
┌─────────────────────────────────────────────────────────┐
│                   Quick Create Form                      │
│  ┌───────────────────────────────────────────────────┐  │
│  │         UniversalQuickCreate PCF Control          │  │
│  │                                                    │  │
│  │  ┌─────────────────┐  ┌──────────────────────┐  │  │
│  │  │ FileUploadField │  │  UploadProgress      │  │  │
│  │  │  (React)        │  │  (React)             │  │  │
│  │  └─────────────────┘  └──────────────────────┘  │  │
│  │                                                    │  │
│  │  ┌──────────────────────────────────────────────┐ │  │
│  │  │   MultiFileUploadService                     │ │  │
│  │  │   - Strategy selection                       │ │  │
│  │  │   - Batch management                         │ │  │
│  │  └──────────────────────────────────────────────┘ │  │
│  │                                                    │  │
│  │  ┌──────────────────────────────────────────────┐ │  │
│  │  │   DataverseRecordService                     │ │  │
│  │  │   - Record creation                          │ │  │
│  │  └──────────────────────────────────────────────┘ │  │
│  └───────────────────────────────────────────────────┘  │
│                                                          │
│  ┌───────────────────────────────────────────────────┐  │
│  │         Custom Button (DOM Injection)              │  │
│  └───────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                          ↓
                  ┌──────────────┐
                  │  SDAP BFF API │
                  └──────────────┘
                          ↓
            ┌──────────────────────────┐
            │  SharePoint Embedded     │
            └──────────────────────────┘
```

### 3.2 Component Structure

**File organization:**
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├── UniversalQuickCreatePCF.ts          # Main PCF class
├── components/
│   ├── FileUploadField.tsx             # File selection UI
│   └── UploadProgress.tsx              # Progress display
├── services/
│   ├── MultiFileUploadService.ts       # Upload orchestration
│   └── DataverseRecordService.ts       # Record creation
├── types/
│   └── index.ts                        # TypeScript interfaces
├── utils/
│   └── logger.ts                       # Logging utility
└── css/
    └── UniversalQuickCreate.css        # Styles
```

### 3.3 Key Classes

**UniversalQuickCreatePCF**
- Main PCF control class
- Manages state and UI rendering
- Orchestrates workflow
- Button management

**MultiFileUploadService**
- Upload strategy selection (sync-parallel vs long-running)
- Batch management
- Progress tracking
- Error handling

**DataverseRecordService**
- Creates Document records
- Populates SPE metadata
- Handles parent relationships

### 3.4 Upload Strategies

**Sync-Parallel:**
- Trigger: ≤3 files, each <10MB, total <20MB
- Method: `Promise.all()` - parallel upload
- Performance: ~3-4 seconds
- Use: Small file sets

**Long-Running:**
- Trigger: >3 files OR large files
- Method: Sequential batches (2-5 files per batch)
- Performance: ~17-25 seconds for 5 files
- Use: Large file sets, reliability

**Adaptive Batch Size:**
```typescript
if (avgSize < 1MB) return 5;   // Small files: 5 per batch
if (avgSize < 5MB) return 3;   // Medium files: 3 per batch
return 2;                       // Large files: 2 per batch
```

### 3.5 Data Flow

**File Upload Flow:**
1. User selects files → `handleFilesChange()`
2. Files stored in state, button enabled
3. User clicks button → `handleSaveAndCreateDocuments()`
4. Strategy selected → `determineUploadStrategy()`
5. Upload starts → `uploadFiles()` with progress callback
6. For each file:
   - Upload to SPE via SDAP API
   - Get metadata response
   - Create Dataverse record
   - Update progress
7. All complete → close form, refresh subgrid

### 3.6 Extension Points

**Adding New Fields:**
```typescript
// In DataverseRecordService.createDocument()
const recordData = {
    ...request.formData,
    sprk_sharepointurl: request.speMetadata.sharePointUrl,
    // Add custom field:
    sprk_customfield: request.customValue
};
```

**Custom Validation:**
```typescript
// In FileUploadField.tsx
const validateFile = (file: File): string | null => {
    // Add custom validation logic
    if (file.name.includes('DRAFT')) {
        return 'Draft documents not allowed';
    }
    return null;
};
```

**Custom Progress UI:**
```typescript
// Replace UploadProgress component with custom implementation
// Implement same interface: UploadProgressProps
```

### 3.7 Dependencies

**NPM Packages:**
- `react@18.2.0`
- `react-dom@18.2.0`
- `@fluentui/react-components@9.54.0`
- `@fluentui/react-icons@2.0.239`
- `@azure/msal-browser@4.24.1`

**PCF Framework:**
- `pcf-scripts` (build tooling)
- `pcf-start` (local testing)

---

## Deliverable 4: Troubleshooting Guide

**File:** `TROUBLESHOOTING-QUICK-CREATE.md`

**Sections:**

### 4.1 Common Issues

**Issue: Control doesn't appear in Quick Create**

**Symptoms:**
- Quick Create shows text field instead of PCF
- "+ Add File" button not visible

**Causes & Fixes:**
1. **Solution not imported**
   - Fix: Import SpaarkeControls.zip (Work Item 8)
   - Verify: Solutions → SpaarkeControls → Controls
2. **Control not configured on form**
   - Fix: Edit form → Field properties → Controls → Add control
   - Set "Universal Quick Create" as active for Web
3. **Browser cache**
   - Fix: Hard refresh (Ctrl+F5) or clear cache
4. **Form not published**
   - Fix: Publish all customizations

---

**Issue: Standard "Save and Close" button still visible**

**Symptoms:**
- Both standard and custom button show
- Custom button not in footer

**Causes & Fixes:**
1. **CSS injection failed**
   - Check console for errors
   - Verify `hideStandardButtons()` called in init()
2. **Footer not found**
   - Check console logs for footer selector errors
   - Update footer selectors in `findFormFooter()`
3. **CSS specificity**
   - Add `!important` to CSS rules
   - Increase selector specificity

---

**Issue: Upload fails with network error**

**Symptoms:**
- Error message: "Network error"
- Files show red X icon

**Causes & Fixes:**
1. **SDAP API unreachable**
   - Check API URL in control properties
   - Verify API is running
   - Test API endpoint directly
2. **CORS issue**
   - Configure CORS on SDAP API
   - Allow origin: `https://*.dynamics.com`
3. **Authentication failure**
   - Check MSAL configuration
   - Verify user has access to SPE
4. **Firewall/proxy**
   - Check network settings
   - Allow outbound HTTPS to API

---

**Issue: Subgrid doesn't refresh after upload**

**Symptoms:**
- Upload succeeds
- Form closes
- New records not visible (until manual refresh)

**Causes & Fixes:**
1. **Parent context not extracted**
   - Check `extractParentContext()` logs
   - Verify parent window accessible
2. **Grid control name mismatch**
   - Update grid name in `refreshParentSubgrid()`
   - Check actual grid name in form designer
3. **Permissions**
   - Verify user has read permission on Documents
   - Check security roles

---

**Issue: Progress bar stuck**

**Symptoms:**
- Upload starts
- Progress bar stops updating
- Form doesn't close

**Causes & Fixes:**
1. **Progress callback not firing**
   - Check `handleUploadProgress()` called
   - Verify `notifyOutputChanged()` called
2. **API timeout**
   - Check API logs for errors
   - Increase timeout threshold
3. **JavaScript error**
   - Check browser console
   - Fix error in code

---

### 4.2 Debugging Steps

**Enable Verbose Logging:**
```typescript
// In logger.ts
const LOG_LEVEL = 'debug';  // Change from 'info'
```

**Check Browser Console:**
1. Open Developer Tools (F12)
2. Go to Console tab
3. Filter by "UniversalQuickCreate"
4. Look for errors or warnings

**Check Network Tab:**
1. Open Developer Tools → Network
2. Filter by "SDAP" or API domain
3. Check request/response
4. Verify 200/201 status codes

**Check Dataverse Logs:**
1. Go to Settings → System Jobs
2. Filter by user and date
3. Look for errors in record creation

---

### 4.3 Performance Issues

**Issue: Upload very slow**

**Diagnosis:**
- Check file sizes
- Check network speed
- Check API performance

**Fixes:**
- Adjust batch size thresholds
- Use sync-parallel for small files
- Optimize API endpoint

---

## Deliverable 5: Configuration Reference

**File:** `CONFIGURATION-REFERENCE.md`

**Sections:**

### 5.1 Control Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| sdapApiBaseUrl | Text | `https://localhost:7299/api` | SDAP BFF API endpoint |
| allowMultipleFiles | Yes/No | Yes | Enable multi-file selection |
| enableFileUpload | Yes/No | Yes | Show file upload UI |

### 5.2 Form Configuration

**Required Fields:**
- sprk_fileuploadmetadata (bound field for PCF)

**Recommended Fields:**
- sprk_documenttitle (Title)
- sprk_description (Description)
- ownerid (Owner)

**Form Type:**
- Quick Create

### 5.3 Security Roles

**Required Permissions:**

| Entity | Create | Read | Write | Append | Append To |
|--------|--------|------|-------|--------|-----------|
| sprk_document | ✅ | ✅ | ✅ | ✅ | ✅ |
| sprk_matter | - | ✅ | - | - | ✅ |

**Note:** User must have create permission on Documents and read on parent entity.

### 5.4 API Configuration

**SDAP BFF API Requirements:**
- Endpoint: `/api/spe/upload`
- Method: POST
- Authentication: Bearer token (MSAL)
- CORS: Allow `https://*.dynamics.com`

### 5.5 Environment Variables

**SDAP API URL:**
- Dev: `https://localhost:7299/api`
- Test: `https://test-api.yourdomain.com/api`
- Prod: `https://api.yourdomain.com/api`

**Update in:**
- Control properties (form designer)
- OR solution configuration XML

---

## Documentation Checklist

- [ ] Administrator Guide created
- [ ] End User Guide created with screenshots
- [ ] Developer Guide created with architecture diagrams
- [ ] Troubleshooting Guide created
- [ ] Configuration Reference created
- [ ] All guides reviewed for accuracy
- [ ] Screenshots captured
- [ ] Diagrams created
- [ ] Code examples tested
- [ ] Links verified
- [ ] Spelling/grammar checked
- [ ] Version numbers included
- [ ] Last updated date added

---

## Documentation Storage

**Location:** `dev/projects/sdap_project/Sprint 7B Doc Quick Create/docs/`

**Structure:**
```
docs/
├── ADMIN-GUIDE-QUICK-CREATE.md
├── USER-GUIDE-QUICK-CREATE.md
├── DEVELOPER-GUIDE-QUICK-CREATE.md
├── TROUBLESHOOTING-QUICK-CREATE.md
├── CONFIGURATION-REFERENCE.md
├── screenshots/
│   ├── quick-create-dialog.png
│   ├── file-upload-ui.png
│   ├── progress-display.png
│   └── new-documents-subgrid.png
└── diagrams/
    └── architecture-overview.png
```

---

## Screenshots Needed

1. **Documents subgrid with "+ New Document" button**
2. **Quick Create dialog with PCF control**
3. **File selection dialog**
4. **Selected files list**
5. **Custom "Save and Create Documents" button**
6. **Upload progress UI**
7. **Completed upload**
8. **Updated subgrid with new records**
9. **Document record with SPE metadata**
10. **Form designer - control configuration**

---

## Maintenance

**Updating Documentation:**
- Update when features change
- Add new troubleshooting items as discovered
- Update screenshots after UI changes
- Version documentation with solution version

---

**Status:** Ready for creation
**Time:** 3 hours
**Prerequisites:** Implementation and testing complete
**Deliverables:** 5 documentation files + screenshots
