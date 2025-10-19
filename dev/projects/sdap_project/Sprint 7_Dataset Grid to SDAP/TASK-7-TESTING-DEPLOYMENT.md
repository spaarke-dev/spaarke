# Task 7: Testing, Bundle Size & Deployment

**Estimated Time**: 1-2 days
**Status**: Pending
**Prerequisites**: Tasks 1-6 complete ✅

---

## AI Coding Prompt

> Create comprehensive integration tests for SDAP file operations, validate production bundle size remains under target, execute complete manual testing checklist, and deploy the enhanced Universal Dataset Grid to production. Ensure all file operations work reliably end-to-end with proper error handling and performance.

---

## Objective

Ensure production readiness through:
1. Integration test suite for all file operations
2. Bundle size validation (<550 KB target)
3. Comprehensive manual testing
4. Production deployment
5. Post-deployment validation

---

## Context & Knowledge

### What You're Testing
All file operations integrated in Tasks 1-6:
- API Client authentication and requests
- File upload workflow
- File download workflow
- File delete workflow (with confirmation)
- File replace workflow
- SharePoint link rendering
- Metadata auto-population

### Why This Matters
- **Quality Assurance**: Catch bugs before production
- **Performance**: Validate bundle size and response times
- **Reliability**: Test error scenarios and edge cases
- **User Confidence**: Ensure production-ready quality

### Testing Strategy
- **Unit Tests**: Individual service methods (optional)
- **Integration Tests**: End-to-end workflows (required)
- **Manual Tests**: Real user scenarios (required)
- **Performance Tests**: Bundle size, API response times

---

## Implementation Steps

### Step 1: Create Integration Test Suite

**File**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/tests/SdapIntegration.test.ts`

**Requirements**:
- Use existing test framework (Jest or similar)
- Mock API responses (don't call real API in tests)
- Test success and failure scenarios
- Test error handling
- Follow existing test patterns

**Test Structure**:
```typescript
import { SdapApiClient } from '../services/SdapApiClient';
import { FileUploadService } from '../services/FileUploadService';
import { FileDownloadService } from '../services/FileDownloadService';
import { FileDeleteService } from '../services/FileDeleteService';

describe('SDAP Integration', () => {
    let apiClient: SdapApiClient;

    beforeEach(() => {
        // Create mock API client
        apiClient = new SdapApiClient('https://localhost:7071', 'mock-token');
    });

    describe('API Client', () => {
        it('should create API client with base URL', () => {
            expect(apiClient).toBeDefined();
        });

        it('should handle authentication token', () => {
            // Verify token included in requests
        });
    });

    describe('Document CRUD', () => {
        it('should create document record', async () => {
            // Mock API response
            const mockResponse = {
                id: 'test-doc-id',
                displayName: 'test.pdf',
                matterId: 'test-matter-id',
                filePath: 'test.pdf',
                fileSize: 1024,
                mimeType: 'application/pdf',
                createdOn: new Date().toISOString(),
                modifiedOn: new Date().toISOString()
            };

            // Mock fetch
            global.fetch = jest.fn().mockResolvedValue({
                ok: true,
                status: 201,
                json: async () => mockResponse
            });

            const request = {
                displayName: 'test.pdf',
                matterId: 'test-matter-id',
                filePath: 'test.pdf',
                fileSize: 1024,
                mimeType: 'application/pdf'
            };

            const response = await apiClient.createDocument(request);

            expect(response.id).toBe('test-doc-id');
            expect(response.displayName).toBe('test.pdf');
        });

        it('should update document metadata', async () => {
            // Test updateDocument
        });

        it('should delete document record', async () => {
            // Test deleteDocument
        });

        it('should handle API errors', async () => {
            // Mock 500 error
            global.fetch = jest.fn().mockResolvedValue({
                ok: false,
                status: 500,
                statusText: 'Internal Server Error',
                json: async () => ({ message: 'Server error' })
            });

            await expect(apiClient.createDocument({} as any)).rejects.toThrow();
        });
    });

    describe('File Operations', () => {
        it('should upload file to SharePoint Embedded', async () => {
            const mockFile = new File(['test content'], 'test.txt', { type: 'text/plain' });

            global.fetch = jest.fn().mockResolvedValue({
                ok: true,
                status: 200,
                json: async () => ({
                    id: '01ABC',
                    name: 'test.txt',
                    size: 12,
                    webUrl: 'https://sharepoint.com/test.txt',
                    sharepointIds: { listItemId: '123' }
                })
            });

            const request = {
                containerId: 'test-container',
                path: 'test.txt',
                file: mockFile
            };

            const response = await apiClient.uploadFile(request);

            expect(response.id).toBe('01ABC');
            expect(response.webUrl).toBeDefined();
        });

        it('should download file from SharePoint Embedded', async () => {
            const mockBlob = new Blob(['test content'], { type: 'text/plain' });

            global.fetch = jest.fn().mockResolvedValue({
                ok: true,
                status: 200,
                blob: async () => mockBlob
            });

            const blob = await apiClient.downloadFile('container-id', 'test.txt');

            expect(blob).toBeInstanceOf(Blob);
            expect(blob.size).toBeGreaterThan(0);
        });

        it('should delete file from SharePoint Embedded', async () => {
            global.fetch = jest.fn().mockResolvedValue({
                ok: true,
                status: 204
            });

            await apiClient.deleteFile('container-id', 'test.txt');

            expect(fetch).toHaveBeenCalled();
        });
    });

    describe('File Upload Service', () => {
        it('should orchestrate full upload workflow', async () => {
            const uploadService = new FileUploadService(apiClient);
            const mockFile = new File(['test'], 'test.pdf', { type: 'application/pdf' });

            // Mock all API calls
            global.fetch = jest.fn()
                .mockResolvedValueOnce({ // uploadFile
                    ok: true,
                    json: async () => ({
                        id: '01ABC',
                        name: 'test.pdf',
                        size: 4,
                        webUrl: 'https://sharepoint.com/test.pdf',
                        sharepointIds: { listItemId: '123' }
                    })
                })
                .mockResolvedValueOnce({ // createDocument
                    ok: true,
                    json: async () => ({
                        id: 'doc-id',
                        displayName: 'test.pdf',
                        matterId: 'matter-id',
                        filePath: 'test.pdf',
                        fileSize: 4,
                        mimeType: 'application/pdf',
                        createdOn: new Date().toISOString(),
                        modifiedOn: new Date().toISOString()
                    })
                })
                .mockResolvedValueOnce({ // updateDocument
                    ok: true,
                    status: 200
                });

            const result = await uploadService.uploadFile(
                'matter-id',
                'container-id',
                mockFile
            );

            expect(result.success).toBe(true);
            expect(result.documentId).toBe('doc-id');
        });

        it('should handle upload failure', async () => {
            const uploadService = new FileUploadService(apiClient);
            const mockFile = new File(['test'], 'test.pdf', { type: 'application/pdf' });

            global.fetch = jest.fn().mockResolvedValue({
                ok: false,
                status: 500,
                statusText: 'Server Error',
                json: async () => ({ message: 'Upload failed' })
            });

            const result = await uploadService.uploadFile(
                'matter-id',
                'container-id',
                mockFile
            );

            expect(result.success).toBe(false);
            expect(result.error).toBeDefined();
        });
    });

    describe('File Replace Service', () => {
        it('should replace file successfully', async () => {
            const uploadService = new FileUploadService(apiClient);
            const newFile = new File(['updated'], 'updated.pdf', { type: 'application/pdf' });

            // Mock delete + upload + update
            global.fetch = jest.fn()
                .mockResolvedValueOnce({ ok: true, status: 204 }) // deleteFile
                .mockResolvedValueOnce({ // uploadFile
                    ok: true,
                    json: async () => ({
                        id: '02XYZ',
                        name: 'updated.pdf',
                        size: 7,
                        webUrl: 'https://sharepoint.com/updated.pdf',
                        sharepointIds: { listItemId: '456' }
                    })
                })
                .mockResolvedValueOnce({ ok: true, status: 200 }); // updateDocument

            const result = await uploadService.replaceFile(
                'doc-id',
                'container-id',
                'old.pdf',
                newFile
            );

            expect(result.success).toBe(true);
            expect(result.filePath).toBe('updated.pdf');
        });
    });
});
```

**Run Tests**:
```bash
npm test
# or
npm run test:coverage
```

**Note**: Adjust test framework and mock syntax based on your project setup. If no test framework exists, this step is optional but recommended.

---

### Step 2: Bundle Size Validation

**Build Production Bundle**:
```bash
# Navigate to PCF control directory
cd src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Build production bundle
npm run build

# Check bundle size (Windows)
dir out\bundle.js

# Check bundle size (Unix/Mac)
ls -lh out/bundle.js
```

**Validation Criteria**:
- **Current Baseline**: 470 KB (v2.0.7)
- **Estimated Addition**: ~50-80 KB (SDAP services)
- **Target**: <550 KB
- **Hard Limit**: 5 MB (5,120 KB)

**If bundle size exceeds target**:
1. Run bundle analyzer:
   ```bash
   npx webpack-bundle-analyzer out/bundle.js
   ```

2. Check for duplicate dependencies
3. Verify tree-shaking working correctly
4. Consider code splitting (defer to future sprint if needed)

**Document Bundle Size**:
```
Sprint 7 Bundle Size:
- v2.0.7 (baseline): 470 KB
- v2.1.0 (with SDAP): [ACTUAL SIZE] KB
- Change: +[DELTA] KB
- Status: [PASS/FAIL target <550 KB]
```

---

### Step 3: Manual Testing Checklist

**Prerequisites**:
1. Deploy control to test environment
2. Configure SDAP_API_URL environment variable
3. Create test matter record with container ID
4. Prepare test files (PDF, DOCX, XLSX, JPG - various sizes)

#### Upload Testing
- [ ] Select matter/case record (no documents yet)
- [ ] Click Upload button → File picker opens
- [ ] Select small file (<1 MB) → Upload succeeds
- [ ] Verify grid refreshes automatically
- [ ] Verify new record appears with:
  - [ ] Correct filename
  - [ ] Correct file size
  - [ ] MIME type populated
  - [ ] SharePoint URL populated
  - [ ] SharePoint item ID populated
- [ ] Click SharePoint URL → Opens in new tab
- [ ] Verify file accessible in SharePoint
- [ ] Upload medium file (10-50 MB) → Succeeds
- [ ] Upload large file (100+ MB) → Succeeds or fails gracefully
- [ ] Cancel file picker → No error, no new record
- [ ] Upload duplicate filename → Handles gracefully

#### Download Testing
- [ ] Select document record
- [ ] Click Download button → Browser download starts
- [ ] Verify correct filename in download
- [ ] Open downloaded file → Content matches original
- [ ] Download different file types (PDF, DOCX, XLSX, JPG)
- [ ] Select record with no file → Shows error
- [ ] Disconnect network → Click Download → Shows error

#### Delete Testing
- [ ] Select document record
- [ ] Click Delete button → Confirmation dialog appears
- [ ] Verify dialog shows correct filename
- [ ] Click Cancel → Dialog closes, record still exists
- [ ] Click Delete again
- [ ] Click Confirm → Record deleted
- [ ] Verify grid refreshes (record removed)
- [ ] Verify file deleted from SharePoint (check SPE)
- [ ] Attempt to delete same record again → Should fail (404)

#### Replace Testing
- [ ] Select document record
- [ ] Note original filename, size, URL
- [ ] Click Replace button → File picker opens
- [ ] Select different file (different name/size)
- [ ] Verify upload completes
- [ ] Verify grid refreshes
- [ ] Verify same record (ID unchanged) with:
  - [ ] New filename
  - [ ] New file size
  - [ ] New MIME type
  - [ ] New SharePoint URL
- [ ] Click new SharePoint URL → Opens new file
- [ ] Verify old file deleted from SharePoint
- [ ] Cancel file picker → No changes to record

#### Field Mapping Testing
- [ ] Upload file → Verify all fields populated:
  - [ ] sprk_name = filename
  - [ ] sprk_filepath = filename
  - [ ] sprk_filesize = bytes (correct)
  - [ ] sprk_mimetype = correct type
  - [ ] sprk_fileurl = SharePoint URL
  - [ ] sprk_spitemid = item ID
- [ ] Replace file → Verify ALL fields updated
- [ ] SharePoint URL column shows clickable link
- [ ] Clicking link opens SharePoint (new tab)
- [ ] Clicking link does NOT select row

#### Error Scenarios
- [ ] Upload with no container ID → Shows error
- [ ] Upload with invalid container ID → API returns error
- [ ] Download non-existent file → Shows error
- [ ] Delete with network disconnected → Shows error
- [ ] API returns 401 (unauthorized) → Shows error
- [ ] API returns 500 (server error) → Shows error gracefully

#### Performance Testing
- [ ] Upload 5 files sequentially → All succeed, reasonable time
- [ ] Upload 50 MB file → Completes within acceptable time
- [ ] Download 50 MB file → Completes within acceptable time
- [ ] Grid with 100+ records → Renders without lag
- [ ] Sort columns → Fast response
- [ ] Select/deselect rows → No lag

#### Cross-Browser Testing
- [ ] Edge (primary browser)
- [ ] Chrome
- [ ] Firefox (if supported by Power Apps)

#### Mobile/Responsive Testing (if applicable)
- [ ] Tablet view
- [ ] Mobile view (if supported)

---

### Step 4: Production Deployment

**Build Solution**:
```bash
# Navigate to solution directory
cd SparkSolution  # Or your solution name

# Restore dependencies
dotnet restore

# Build release configuration
dotnet build --configuration Release

# Verify solution package created
ls bin/Release/*.zip
```

**Deploy to Environment**:
```bash
# Option 1: Use Power Apps CLI
pac solution import --path bin/Release/SparkSolution_*.zip --environment [env-url]

# Option 2: Manual deployment via Power Platform admin center
# 1. Navigate to https://admin.powerplatform.microsoft.com
# 2. Select environment
# 3. Solutions > Import
# 4. Upload bin/Release/SparkSolution_*.zip
# 5. Import and publish
```

**Update Version Number**:
1. Edit `ControlManifest.Input.xml`
2. Update version attribute: `version="2.1.0"`
3. Rebuild solution

**Deployment Checklist**:
- [ ] Solution builds without errors
- [ ] Version number updated to 2.1.0
- [ ] Solution package <50 MB (check zip size)
- [ ] Import succeeds without errors
- [ ] All customizations published
- [ ] No warnings in solution checker

---

### Step 5: Post-Deployment Validation

**Immediate Validation** (within 1 hour):
- [ ] Control loads in production app
- [ ] No JavaScript errors in browser console
- [ ] Upload functionality works
- [ ] Download functionality works
- [ ] Delete functionality works
- [ ] Replace functionality works
- [ ] SharePoint links clickable
- [ ] All metadata fields populated

**24-Hour Monitoring**:
- [ ] No error reports from users
- [ ] API logs show successful operations
- [ ] No performance degradation
- [ ] Bundle size confirmed in production

**User Acceptance Testing**:
- [ ] Select 2-3 power users for UAT
- [ ] Provide test script (simplified version of manual test)
- [ ] Collect feedback
- [ ] Address any issues

---

## Validation Criteria

Before marking Sprint 7 complete, verify:

- [ ] Integration tests created (or documented as optional)
- [ ] All integration tests pass
- [ ] Production bundle size <550 KB
- [ ] Manual testing checklist 100% complete
- [ ] All file operations work end-to-end
- [ ] Error handling tested and working
- [ ] Solution deployed to production
- [ ] Post-deployment validation complete
- [ ] No critical bugs reported
- [ ] Performance acceptable (<2s per operation)
- [ ] Documentation updated (README, CHANGELOG)

---

## Expected Outcomes

After completing this task:

✅ **Comprehensive test coverage** for all file operations
✅ **Bundle size validated** (<550 KB target)
✅ **Production deployment** successful
✅ **Post-deployment validation** confirms quality
✅ **User acceptance** criteria met
✅ **Sprint 7 complete** - SDAP integration production-ready

---

## Documentation Updates

### Update README.md
Add section documenting new features:
```markdown
## File Operations (v2.1.0)

The Universal Dataset Grid now supports file operations via SDAP:

- **Upload**: Select a record and click Upload to add files
- **Download**: Click Download to retrieve files from SharePoint
- **Delete**: Delete files with confirmation dialog
- **Replace**: Update files with new versions
- **SharePoint Links**: Click URLs to open files in SharePoint

### Configuration

Set SDAP API URL via environment variable:
```bash
SDAP_API_URL=https://your-api.azurewebsites.net
```
```

### Update CHANGELOG.md
```markdown
## [2.1.0] - 2025-10-05

### Added
- SDAP BFF API integration for file operations
- File upload functionality with browser file picker
- File download with browser download dialog
- File delete with confirmation dialog
- File replace (update) functionality
- Clickable SharePoint URLs in grid
- Auto-population of SharePoint metadata fields
- Comprehensive error handling for all operations
- Integration test suite for SDAP operations

### Changed
- Bundle size increased from 470 KB to [ACTUAL] KB (+[DELTA] KB)
- CommandBar buttons now functional (previously placeholders)

### Fixed
- Grid refresh after file operations
- Proper cleanup of blob URLs on download
```

---

## Troubleshooting

### Issue: Tests fail with "fetch is not defined"
**Solution**: Install and configure `node-fetch` or `jest-fetch-mock`:
```bash
npm install --save-dev jest-fetch-mock
```

Add to test setup:
```typescript
import { enableFetchMocks } from 'jest-fetch-mock';
enableFetchMocks();
```

### Issue: Bundle size exceeds target
**Solution**:
1. Check for duplicate dependencies in package.json
2. Verify Fluent UI tree-shaking working
3. Remove unused imports
4. Consider lazy loading for less-used features

### Issue: Deployment fails with "Solution is invalid"
**Solution**:
1. Run solution checker: `pac solution check`
2. Fix reported issues
3. Rebuild and redeploy

### Issue: Production control loads but buttons don't work
**Solution**:
1. Check browser console for errors
2. Verify SDAP_API_URL configured
3. Check CORS configuration on API
4. Verify user has permissions

---

## Performance Benchmarks

Document actual performance:

| Operation | Target | Actual | Status |
|-----------|--------|--------|--------|
| Bundle Size | <550 KB | [X] KB | ✅/❌ |
| Upload (1 MB) | <2s | [X]s | ✅/❌ |
| Upload (50 MB) | <30s | [X]s | ✅/❌ |
| Download (1 MB) | <2s | [X]s | ✅/❌ |
| Download (50 MB) | <30s | [X]s | ✅/❌ |
| Delete | <1s | [X]s | ✅/❌ |
| Replace (1 MB) | <3s | [X]s | ✅/❌ |

---

## Sprint 7 Completion Criteria

**All tasks complete**:
- [x] Task 1: API Client Setup
- [x] Task 2: File Upload Integration
- [x] Task 3: File Download Integration
- [x] Task 4: File Delete Integration
- [x] Task 5: File Replace Integration
- [x] Task 6: Field Mapping & SharePoint Links
- [x] Task 7: Testing, Bundle Size & Deployment

**Quality gates passed**:
- [x] All tests pass (if implemented)
- [x] Bundle size <550 KB
- [x] Manual testing 100% complete
- [x] Production deployment successful
- [x] Post-deployment validation complete
- [x] No critical bugs

**Documentation updated**:
- [x] README.md
- [x] CHANGELOG.md
- [x] SPRINT-7-WRAP-UP.md created

---

## Next Steps

After Sprint 7 completion:
- Monitor production for 1 week
- Collect user feedback
- Plan Sprint 8 (future enhancements):
  - Chunked upload for large files (>4 MB)
  - Batch delete (multiple files)
  - Drag-and-drop upload
  - Progress indicators
  - User notifications (toast messages)

---

## Master Resource

For additional context, see:
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - Full reference
- All previous task files (TASK-1 through TASK-6)

---

**Task Owner**: AI-Directed Coding Session
**Estimated Completion**: 1-2 days
**Status**: Ready to Begin (after Tasks 1-6)
