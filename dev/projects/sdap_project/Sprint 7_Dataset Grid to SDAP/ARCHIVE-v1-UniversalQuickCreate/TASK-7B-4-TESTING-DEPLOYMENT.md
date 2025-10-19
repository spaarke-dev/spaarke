# Sprint 7B - Task 4: Testing, Bundle Size & Deployment

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 4 of 4
**Estimated Time**: 1 day
**Priority**: High
**Status**: Pending
**Depends On**: Sprint 7B Tasks 1-3

---

## Task Overview

Comprehensive testing, bundle size validation, and production deployment of the Universal Quick Create PCF control. This includes integration tests, manual testing across multiple entity types, bundle size optimization, and deployment to production Dataverse environment.

---

## Success Criteria

- ✅ Integration test suite created and passing
- ✅ Manual testing checklist 100% complete
- ✅ Bundle size <400 KB validated
- ✅ Zero TypeScript errors (strict mode)
- ✅ Zero runtime errors in production
- ✅ Deployed to production Dataverse
- ✅ Documentation complete for admins
- ✅ Works across 3+ entity types (Document, Task, Contact)

---

## Testing Strategy

### 1. Unit Tests (Optional for PCF)
Power Apps PCF controls are difficult to unit test due to framework dependencies. Focus on integration and manual testing.

### 2. Integration Tests
Test key services independently:
- DefaultValueMapper
- FieldMetadataService
- FileUploadService (from Sprint 7B Task 2)

### 3. Manual Testing (Primary)
Comprehensive manual testing checklist across multiple scenarios and entity types.

### 4. Bundle Size Validation
Verify final bundle meets <400 KB target.

### 5. Production Deployment
Deploy to production Dataverse environment with monitoring.

---

## Integration Tests

### Test 1: DefaultValueMapper

```typescript
// tests/DefaultValueMapper.test.ts
import { DefaultValueMapper } from '../services/DefaultValueMapper';

describe('DefaultValueMapper', () => {
    it('should apply configured mappings correctly', () => {
        const mapper = new DefaultValueMapper({
            mappings: {
                'sprk_matter': {
                    'sprk_name': 'sprk_documenttitle',
                    'sprk_containerid': 'sprk_containerid',
                    '_ownerid_value': 'ownerid'
                }
            },
            parentEntityName: 'sprk_matter',
            parentRecordData: {
                'sprk_name': 'Test Matter',
                'sprk_containerid': 'container-123',
                '_ownerid_value': 'user-guid'
            }
        });

        const defaults = mapper.getDefaultValues();

        expect(defaults.sprk_documenttitle).toBe('Test Matter');
        expect(defaults.sprk_containerid).toBe('container-123');
        expect(defaults.ownerid).toBe('user-guid');
    });

    it('should use convention-based mapping when no config provided', () => {
        const mapper = new DefaultValueMapper({
            mappings: {},
            parentEntityName: 'sprk_matter',
            parentRecordData: {
                'sprk_containerid': 'container-123',
                '_ownerid_value': 'user-guid'
            }
        });

        const defaults = mapper.getDefaultValues();

        expect(defaults.sprk_containerid).toBe('container-123');
        expect(defaults.ownerid).toBe('user-guid');
    });

    it('should handle missing parent data gracefully', () => {
        const mapper = new DefaultValueMapper({
            mappings: {
                'sprk_matter': {
                    'sprk_name': 'sprk_documenttitle'
                }
            },
            parentEntityName: 'sprk_matter',
            parentRecordData: null
        });

        const defaults = mapper.getDefaultValues();

        expect(Object.keys(defaults).length).toBe(0);
    });
});
```

### Test 2: FieldMetadataService

```typescript
// tests/FieldMetadataService.test.ts
import { FieldMetadataService } from '../services/FieldMetadataService';

describe('FieldMetadataService', () => {
    it('should return field metadata for sprk_document entity', async () => {
        const mockContext = {
            parameters: {}
        } as any;

        const service = new FieldMetadataService(mockContext);
        const fields = await service.getFieldMetadata('sprk_document');

        expect(fields.length).toBeGreaterThan(0);
        expect(fields[0]).toHaveProperty('name');
        expect(fields[0]).toHaveProperty('label');
        expect(fields[0]).toHaveProperty('type');
    });

    it('should return field metadata for task entity', async () => {
        const mockContext = {
            parameters: {}
        } as any;

        const service = new FieldMetadataService(mockContext);
        const fields = await service.getFieldMetadata('task');

        expect(fields.length).toBeGreaterThan(0);

        const subjectField = fields.find(f => f.name === 'subject');
        expect(subjectField).toBeDefined();
        expect(subjectField?.required).toBe(true);
    });
});
```

---

## Manual Testing Checklist

### Prerequisites

- [ ] Universal Quick Create PCF deployed to test environment
- [ ] Matter entity has Container ID field populated
- [ ] Test user has permissions to create Documents, Tasks, Contacts
- [ ] SDAP BFF API running and accessible
- [ ] SharePoint Embedded container exists

---

### Test Scenario 1: Document Creation from Matter (Happy Path)

**Setup**:
- Open a Matter record
- Navigate to Documents subgrid

**Steps**:
1. [ ] Click "+ New Document" button
2. [ ] Quick Create form opens (slide-in panel)
3. [ ] File picker field appears at top
4. [ ] Document Title field shows Matter Name as default
5. [ ] Owner field shows Matter Owner as default
6. [ ] Select a test file (PDF, <1 MB)
7. [ ] File info displays (name, size)
8. [ ] Modify Document Title (optional)
9. [ ] Fill Description (optional)
10. [ ] Click Save
11. [ ] Upload progress indicator shows
12. [ ] Form closes automatically
13. [ ] Grid refreshes automatically
14. [ ] New document appears in grid
15. [ ] SharePoint URL column shows link
16. [ ] Click SharePoint URL
17. [ ] File opens in browser

**Expected Results**:
- ✅ File uploads to SharePoint Embedded
- ✅ Dataverse record created with all metadata
- ✅ URL is clickable and works
- ✅ No errors in browser console
- ✅ Total time <15 seconds

---

### Test Scenario 2: Document Creation (Large File)

**Setup**: Same as Scenario 1

**Steps**:
1. [ ] Click "+ New Document"
2. [ ] Select large file (>10 MB, <100 MB)
3. [ ] Fill form fields
4. [ ] Click Save
5. [ ] Upload progress shows incremental updates (0% → 100%)
6. [ ] Form closes after upload completes
7. [ ] Document appears in grid

**Expected Results**:
- ✅ Progress indicator works correctly
- ✅ Large file uploads successfully
- ✅ No timeout errors
- ✅ File size in Dataverse matches actual file

---

### Test Scenario 3: Task Creation from Matter (No File Upload)

**Setup**:
- Configure Universal Quick Create for Task entity
- Set `enableFileUpload = false`
- Configure default value mappings:
  ```json
  {
    "sprk_matter": {
      "sprk_name": "subject",
      "_ownerid_value": "ownerid"
    }
  }
  ```

**Steps**:
1. [ ] Open Matter record
2. [ ] Navigate to Tasks subgrid
3. [ ] Click "+ New Task"
4. [ ] Quick Create form opens
5. [ ] File picker DOES NOT appear
6. [ ] Subject field shows Matter Name as default
7. [ ] Owner shows Matter Owner as default
8. [ ] Fill Description
9. [ ] Select Due Date
10. [ ] Select Priority
11. [ ] Click Save
12. [ ] Form closes
13. [ ] Task appears in grid

**Expected Results**:
- ✅ No file upload field shown
- ✅ Default values applied correctly
- ✅ Task created successfully
- ✅ No file upload attempted

---

### Test Scenario 4: Contact Creation from Account (Different Parent)

**Setup**:
- Configure Universal Quick Create for Contact entity
- Set `enableFileUpload = false`
- Configure default value mappings:
  ```json
  {
    "account": {
      "name": "company",
      "_ownerid_value": "ownerid"
    }
  }
  ```

**Steps**:
1. [ ] Open Account record
2. [ ] Navigate to Contacts subgrid
3. [ ] Click "+ New Contact"
4. [ ] Quick Create form opens
5. [ ] File picker DOES NOT appear
6. [ ] Company field shows Account Name as default
7. [ ] Owner shows Account Owner as default
8. [ ] Fill First Name
9. [ ] Fill Last Name
10. [ ] Fill Email
11. [ ] Click Save
12. [ ] Contact created

**Expected Results**:
- ✅ Works with different parent entity (Account)
- ✅ Default values mapped correctly
- ✅ Contact created successfully

---

### Test Scenario 5: Error Handling - Missing Container ID

**Setup**:
- Create Matter WITHOUT Container ID

**Steps**:
1. [ ] Open Matter (no container)
2. [ ] Click "+ New Document"
3. [ ] Select file
4. [ ] Click Save
5. [ ] Error message appears: "Container ID not found"
6. [ ] Form stays open
7. [ ] User can cancel or fix issue

**Expected Results**:
- ✅ Clear error message shown
- ✅ Form doesn't close
- ✅ No partial data created
- ✅ Error logged to console

---

### Test Scenario 6: Error Handling - Upload Fails

**Setup**:
- Stop SDAP BFF API or use invalid URL

**Steps**:
1. [ ] Open Matter
2. [ ] Click "+ New Document"
3. [ ] Select file
4. [ ] Click Save
5. [ ] Upload fails
6. [ ] Error message appears
7. [ ] Form stays open
8. [ ] User can retry or cancel

**Expected Results**:
- ✅ Network error caught
- ✅ User-friendly error message
- ✅ No Dataverse record created
- ✅ Can retry after fixing connection

---

### Test Scenario 7: Error Handling - Record Creation Fails

**Setup**:
- Remove user's create permission on Document entity

**Steps**:
1. [ ] Open Matter
2. [ ] Click "+ New Document"
3. [ ] Select file
4. [ ] Click Save
5. [ ] File uploads successfully
6. [ ] Record creation fails (permission error)
7. [ ] Error message appears: "Failed to create record"
8. [ ] File exists in SharePoint but no Dataverse record

**Expected Results**:
- ✅ Error message clear
- ✅ User informed file was uploaded
- ✅ Logged for troubleshooting

---

### Test Scenario 8: Cancel Operation

**Steps**:
1. [ ] Click "+ New Document"
2. [ ] Select file
3. [ ] Fill some fields
4. [ ] Click Cancel
5. [ ] Form closes
6. [ ] No record created
7. [ ] No file uploaded

**Expected Results**:
- ✅ Cancel works immediately
- ✅ No data persisted
- ✅ Grid unchanged

---

### Test Scenario 9: Multiple Entity Types

**Steps**:
1. [ ] Test with Document entity (with file upload)
2. [ ] Test with Task entity (without file upload)
3. [ ] Test with Contact entity (without file upload)
4. [ ] Test with custom entity (if available)

**Expected Results**:
- ✅ Same PCF works for all entity types
- ✅ No code changes needed
- ✅ Configuration drives behavior

---

### Test Scenario 10: Field Types

**Setup**:
- Configure Quick Create form with all field types

**Steps**:
1. [ ] Text field renders and accepts input
2. [ ] Textarea field renders and accepts multiline
3. [ ] Number field renders and validates numeric input
4. [ ] Date field renders with date picker
5. [ ] DateTime field renders with date + time picker
6. [ ] Boolean field renders as switch
7. [ ] Dropdown (optionset) renders with options
8. [ ] All field values save correctly

**Expected Results**:
- ✅ All field types render correctly
- ✅ All field types save values correctly
- ✅ Validation works (required fields, data types)

---

## Bundle Size Validation

### Build Production Bundle

```bash
cd c:\code_files\spaarke\src\controls\UniversalQuickCreate

# Build in production mode
npm run build

# Check bundle size
dir out\controls
```

### Target Bundle Size

**Target**: <400 KB
**Breakdown**:
- React + ReactDOM: ~120 KB (gzipped)
- Fluent UI v9: ~150 KB (gzipped, tree-shaken)
- PCF code + services: ~50 KB
- SDAP API client: ~30 KB
- **Total**: ~350 KB ✅

### If Bundle Too Large

**Optimization Steps**:

1. **Enable Tree-Shaking**:
```javascript
// webpack.config.js
optimization: {
    usedExports: true,
    sideEffects: false
}
```

2. **Check for Duplicate Dependencies**:
```bash
npm ls react
npm ls react-dom
npm ls @fluentui/react-components
```

3. **Use Fluent UI Tree-Shakable Imports**:
```typescript
// Instead of:
import { Button, Input } from '@fluentui/react-components';

// Use:
import { Button } from '@fluentui/react-components/Button';
import { Input } from '@fluentui/react-components/Input';
```

4. **Analyze Bundle**:
```bash
npm install --save-dev webpack-bundle-analyzer
npm run build -- --analyze
```

---

## Deployment Process

### Step 1: Build Solution

```bash
cd c:\code_files\spaarke

# Build Universal Quick Create PCF
cd src\controls\UniversalQuickCreate
npm run build

# Return to solution root
cd ..\..

# Create or update solution
pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path src\controls\UniversalQuickCreate

# Build solution
msbuild /t:build /restore
```

### Step 2: Validate Build

```bash
# Check solution output
dir bin\Debug\*.zip

# Expected: Solution.zip with UniversalQuickCreate component
```

### Step 3: Deploy to Test Environment

```bash
# Authenticate to test environment
pac auth create --url https://your-test-env.crm.dynamics.com

# Import solution
pac solution import --path bin\Debug\Solution.zip --async
```

### Step 4: Configure Quick Create Forms

**For Document Entity**:
1. Open Power Apps maker portal
2. Navigate to Tables > Document > Forms
3. Create or edit Quick Create form
4. Add fields: sprk_documenttitle, sprk_description
5. Add Universal Quick Create control
6. Configure parameters:
   - `defaultValueMappings`: `{"sprk_matter":{"sprk_containerid":"sprk_containerid","sprk_name":"sprk_documenttitle","_ownerid_value":"ownerid"}}`
   - `enableFileUpload`: `true`
   - `sdapApiBaseUrl`: `https://your-api.azurewebsites.net/api`
7. Publish form

**For Task Entity**:
1. Create/edit Task Quick Create form
2. Add fields: subject, description, scheduledend, prioritycode
3. Add Universal Quick Create control
4. Configure parameters:
   - `defaultValueMappings`: `{"sprk_matter":{"sprk_name":"subject","_ownerid_value":"ownerid"}}`
   - `enableFileUpload`: `false`
   - `sdapApiBaseUrl`: `https://your-api.azurewebsites.net/api`
5. Publish form

### Step 5: Manual Testing in Test Environment

Execute all manual testing scenarios from checklist above.

### Step 6: Deploy to Production

**After successful test environment validation**:

```bash
# Authenticate to production environment
pac auth create --url https://your-prod-env.crm.dynamics.com

# Import solution (as managed solution)
pac solution import --path bin\Debug\Solution.zip --async --convert-to-managed true
```

### Step 7: Production Smoke Test

Execute key scenarios in production:
1. [ ] Document creation from Matter
2. [ ] Task creation from Matter
3. [ ] Verify no errors in browser console
4. [ ] Verify performance acceptable

---

## Rollback Plan

**If issues discovered in production**:

1. **Immediate Rollback**:
   - Revert to previous solution version
   - Or disable Quick Create forms using Universal Quick Create
   - Fall back to standard Quick Create forms

2. **Fix Forward**:
   - Create hotfix branch
   - Fix issue
   - Deploy hotfix to test
   - Validate
   - Deploy to production

---

## Documentation for Admins

### Quick Create Configuration Guide

Create `docs/UNIVERSAL-QUICK-CREATE-ADMIN-GUIDE.md`:

```markdown
# Universal Quick Create - Admin Configuration Guide

## Overview

The Universal Quick Create PCF control enables file upload and configurable
default values when creating records from Quick Create forms.

## Configuration Steps

### 1. Add Control to Quick Create Form

1. Open Power Apps maker portal
2. Navigate to: Tables > [Entity] > Forms > Quick Create Form
3. Add/remove fields as needed
4. Insert Universal Quick Create control
5. Configure control parameters (see below)
6. Publish form

### 2. Configure Default Value Mappings

Set the `defaultValueMappings` parameter to map parent entity fields to
child entity default values.

**Syntax**:
```json
{
  "parent_entity_name": {
    "parent_field_name": "child_field_name"
  }
}
```

**Example (Document from Matter)**:
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  }
}
```

**Important Notes**:
- For lookup fields, use `_fieldname_value` syntax (e.g., `_ownerid_value`)
- Container ID mapping required for file upload to SharePoint
- Parent entity name is logical name (not display name)

### 3. Enable/Disable File Upload

Set `enableFileUpload` parameter:
- `true` - Show file picker (for Documents)
- `false` - Hide file picker (for Tasks, Contacts)

### 4. Set SDAP API URL

Set `sdapApiBaseUrl` parameter to your SDAP BFF API endpoint:
- Test: `https://your-test-api.azurewebsites.net/api`
- Production: `https://your-prod-api.azurewebsites.net/api`

## Common Configurations

### Document from Matter
- Enable file upload: `true`
- Mappings: Container ID, Name → Title, Owner

### Task from Matter
- Enable file upload: `false`
- Mappings: Name → Subject, Owner

### Contact from Account
- Enable file upload: `false`
- Mappings: Name → Company, Owner, Address fields

## Troubleshooting

### File Upload Fails
- Check Container ID exists on parent Matter
- Verify SDAP API URL correct
- Check user permissions on SharePoint

### Default Values Not Applied
- Verify mapping JSON syntax correct
- Check parent field names use correct syntax
- Ensure parent record has values in mapped fields
```

---

## Success Metrics

### Performance
- ✅ Quick Create form loads <1s
- ✅ File upload <2s for <1 MB files
- ✅ Total save time <15s for typical files
- ✅ No timeout errors

### Reliability
- ✅ Error rate <1%
- ✅ File upload success rate >99%
- ✅ No data loss (orphaned files)

### Usability
- ✅ Works across 3+ entity types
- ✅ Configuration takes <5 minutes
- ✅ Error messages clear and actionable
- ✅ Standard Power Apps UX maintained

### Technical
- ✅ Bundle size <400 KB
- ✅ Zero TypeScript errors
- ✅ Zero console errors in production
- ✅ Passes all manual test scenarios

---

## Completion Checklist

### Development
- [ ] All Sprint 7B Tasks 1-3 completed
- [ ] Code reviewed
- [ ] No TypeScript errors
- [ ] Logger outputs verified

### Testing
- [ ] Integration tests passing
- [ ] All manual test scenarios passed
- [ ] Tested on 3+ entity types
- [ ] Error scenarios handled

### Bundle Size
- [ ] Production build created
- [ ] Bundle size validated <400 KB
- [ ] No duplicate dependencies
- [ ] Tree-shaking enabled

### Deployment
- [ ] Solution built
- [ ] Deployed to test environment
- [ ] Test environment smoke test passed
- [ ] Deployed to production
- [ ] Production smoke test passed

### Documentation
- [ ] Admin configuration guide created
- [ ] Configuration examples documented
- [ ] Troubleshooting guide created
- [ ] README updated

---

## 🔴 CRITICAL: Return to Sprint 7A After Task 4

**After Sprint 7B Task 4 completes:**

Sprint 7B Task 4 will create REAL test files in SharePoint Embedded. These are needed to complete Sprint 7A testing.

**Action Required:**
1. Complete Sprint 7B Task 4 (this task)
2. Use Quick Create to upload 3-5 test files to SharePoint Embedded
3. **Return to Sprint 7A Task 3 (Manual Testing)**
4. Use real Document records to test:
   - Download file (Sprint 7A feature)
   - Replace file (Sprint 7A feature)
   - Delete file (Sprint 7A feature)
5. Validate MSAL authentication works end-to-end in Sprint 7A

**Reference Documents:**
- [SPRINT-7A-REMEDIAL-TASKS.md](SPRINT-7A-REMEDIAL-TASKS.md)
- [TASK-7A.3-MANUAL-TESTING.md](TASK-7A.3-MANUAL-TESTING.md)

**Why This Matters:**
Sprint 7A is code-compliant with MSAL but untested due to placeholder itemIds. Sprint 7B creates the real data needed to test Sprint 7A.

---

## Multi-File Upload Testing (Added for Sprint 7B)

### Test Scenario 1: Sync-Parallel Upload (Fast Path)

**Setup:**
- 3 PDF files × 2MB each (6MB total)

**Steps:**
1. Open Quick Create from Matter subgrid
2. Select all 3 files in file picker
3. Fill in Description: "Test sync-parallel"
4. Click Save

**Expected:**
- Console: "Using sync-parallel upload strategy"
- Upload completes in ~3-4 seconds
- Form closes automatically
- Grid shows 3 new Document records
- All 3 documents have Description: "Test sync-parallel"
- All 3 documents linked to same Matter

**Verify Console:**
```
[MultiFileUploadService] Using sync-parallel upload strategy
[MultiFileUploadService] Uploading 3 files in parallel
[MsalAuthProvider] Token acquired in 420ms (File 1)
[MsalAuthProvider] Token retrieved from cache (5ms) (File 2)
[MsalAuthProvider] Token retrieved from cache (5ms) (File 3)
[MultiFileUploadService] All 3 files uploaded successfully
```

### Test Scenario 2: Long-Running Upload (Safe Path)

**Setup:**
- 5 scanned documents × 15MB each (75MB total)

**Steps:**
1. Open Quick Create from Matter subgrid
2. Select all 5 files in file picker
3. Fill in Description: "Test long-running"
4. Click Save

**Expected:**
- Console: "Using long-running upload strategy"
- Progress indicator shows:
  - "Uploading 5 files..."
  - "Estimated time: 25 seconds"
  - File-by-file status (✓ uploaded, ↻ uploading, ⏳ waiting)
- Upload completes in ~25 seconds
- Form closes automatically 2 seconds after completion
- Grid shows 5 new Document records
- All 5 documents have Description: "Test long-running"

**Verify Console:**
```
[MultiFileUploadService] Using long-running upload strategy
[MultiFileUploadService] Batch size: 3 (adaptive)
[MultiFileUploadService] Uploading batch 1/2 (files 1-3)
[MultiFileUploadService] Batch 1 complete
[MultiFileUploadService] Uploading batch 2/2 (files 4-5)
[MultiFileUploadService] Batch 2 complete
[MultiFileUploadService] All 5 files uploaded successfully
```

### Test Scenario 3: Threshold Boundary (Strategy Selection)

**Setup:**
- 3 files × 10MB each (30MB total)

**Steps:**
1. Open Quick Create from Matter subgrid
2. Select all 3 files
3. Click Save

**Expected:**
- Console: "Using long-running upload strategy" (exceeds 20MB total threshold)
- Progress indicator shown (not sync-parallel)

**Verify:** Strategy decision logic works correctly at thresholds

### Test Scenario 4: Maximum Files (Stress Test)

**Setup:**
- 10 files × 5MB each (50MB total)

**Steps:**
1. Open Quick Create from Matter subgrid
2. Select all 10 files
3. Fill in Description: "Maximum batch"
4. Click Save

**Expected:**
- Console: "Using long-running upload strategy"
- Batch size: 3 (adaptive for 5MB files)
- Upload completes in ~35 seconds
- Grid shows 10 new Document records
- All 10 documents created successfully

### Test Scenario 5: Partial Failure (Error Handling)

**Setup:**
- 5 files (4 valid PDFs + 1 invalid file type)

**Steps:**
1. Open Quick Create from Matter subgrid
2. Select all 5 files
3. Click Save

**Expected:**
- 4 files upload successfully
- 1 file fails with error
- Summary shown: "4 of 5 files uploaded successfully"
- Grid shows 4 new Document records
- Error message displays which file failed

**Verify:** Partial success handling works correctly

### Test Scenario 6: MSAL Token Caching (Performance)

**Setup:**
- 10 files × 5MB each

**Steps:**
1. Open Quick Create (first time in session)
2. Select all 10 files
3. Monitor console during upload

**Expected Console:**
```
File 1: [MsalAuthProvider] Token not in cache, acquiring via ssoSilent (420ms)
File 2: [MsalAuthProvider] Token retrieved from cache (5ms)
File 3: [MsalAuthProvider] Token retrieved from cache (5ms)
...
File 10: [MsalAuthProvider] Token retrieved from cache (5ms)
```

**Verify:** Token caching provides 82x performance improvement for multi-file

---

## MSAL Authentication Testing (Added for Sprint 8 Compliance)

### Test 1: Initial Authentication

**Steps:**
1. Launch Quick Create (first time in browser session)
2. Open browser console

**Expected Console:**
```
[UniversalQuickCreate] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[MsalAuthProvider] MSAL initialized successfully
[UniversalQuickCreate] MSAL authentication initialized successfully ✅
[UniversalQuickCreate] User authenticated: true
[UniversalQuickCreate] Account info: { username: "user@domain.com", ... }
```

### Test 2: Token Acquisition During Upload

**Steps:**
1. Upload single file
2. Monitor console for token acquisition

**Expected Console (First Upload):**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token not in cache, acquiring via ssoSilent
[MsalAuthProvider] Token acquired in 420ms
[MsalAuthProvider] Token cached with expiry: 2025-10-06T15:30:00.000Z
```

**Expected Console (Second Upload):**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token found in cache
[MsalAuthProvider] Token retrieved from cache (5ms)
```

### Test 3: 401 Auto-Retry (Token Expiration)

**Simulation:**
- Wait for token to expire (1 hour)
- Or manually clear sessionStorage

**Steps:**
1. Upload file
2. Verify automatic retry with fresh token

**Expected Console:**
```
[SdapApiClient] 401 Unauthorized response - clearing token cache and retrying
[MsalAuthProvider] Cache cleared
[MsalAuthProvider] Token acquired in 420ms
[SdapApiClient] Retrying request with fresh token
[SdapApiClient] File uploaded successfully
```

---

## Next Steps After Sprint 7B

### Immediate Actions

1. ✅ **Complete Sprint 7B Task 4** (this task)
2. 🔴 **Return to Sprint 7A Task 3** (Manual Testing with real files)
3. ✅ **Update Sprint 7A Documentation** (Task 4)

### Potential Enhancements (Future Sprints)

1. **Server-Side Batch Processing**
   - Azure Service Bus queue for very large batches (>10 files, >100MB)
   - Email notification when complete
   - Resumable uploads

2. **Lookup Field Support**
   - Render lookup fields with entity picker
   - Search and select parent records

3. **Validation Rules**
   - Custom validation rules per entity
   - Cross-field validation
   - Async validation (e.g., unique values)

4. **File Preview**
   - Show file preview before upload
   - Image thumbnails
   - PDF preview

5. **Template Support**
   - Quick Create templates per entity
   - Pre-filled values from templates

6. **Offline Support**
   - Queue uploads when offline
   - Sync when connection restored

---

## References

- **Master Resource**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)
- **Architecture Decision**: [ARCHITECTURE-DECISION-TWO-PCF-APPROACH.md](ARCHITECTURE-DECISION-TWO-PCF-APPROACH.md)
- **Sprint 7B Task 1**: [TASK-7B-1-QUICK-CREATE-SETUP.md](TASK-7B-1-QUICK-CREATE-SETUP.md)
- **Sprint 7B Task 2**: [TASK-7B-2-FILE-UPLOAD-SPE.md](TASK-7B-2-FILE-UPLOAD-SPE.md)
- **Sprint 7B Task 3**: [TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md](TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md)

---

**Sprint 7B Complete!** 🎉

The Universal Quick Create PCF control is now production-ready and deployed, enabling file upload to SharePoint Embedded with configurable default values across multiple entity types.
