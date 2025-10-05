# Sprint 6: File Size Limitation Analysis
**Date:** October 4, 2025
**Status:** ✅ **ANALYSIS COMPLETE - Enhancement Required**

---

## Executive Summary

**Current State:** The SDAP API **FULLY SUPPORTS** both small file uploads (< 4MB) and large file uploads via chunked upload sessions. However, the current Sprint 6 integration plan only uses the small file upload endpoint.

**Key Finding:** ✅ **No enhancement to SDAP API required** - chunked upload infrastructure already exists and is production-ready.

**Required Action:** 📋 **Update Sprint 6 JavaScript implementation** to support chunked uploads for files ≥ 4MB.

---

## Current SDAP API Capabilities

### ✅ Small File Upload (< 4MB) - AVAILABLE

**Managed Identity (MI) Endpoints:**
- `PUT /api/drives/{driveId}/upload` - Upload files < 4MB
- Used by: Current Sprint 6 integration plan

**On-Behalf-Of (OBO) Endpoints:**
- `PUT /api/obo/containers/{id}/files/{*path}` - Upload files < 4MB as user
- Used by: Direct user uploads

**Implementation:** [UploadSessionManager.cs:23-86](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\UploadSessionManager.cs)

```csharp
public async Task<FileHandleDto?> UploadSmallAsync(
    string driveId,
    string path,
    Stream content,
    CancellationToken ct = default)
{
    // Direct PUT to Graph API /drives/{driveId}/root/ItemWithPath(path)/content
    var item = await graphClient.Drives[driveId].Root
        .ItemWithPath(path)
        .Content
        .PutAsync(content, cancellationToken: ct);

    return new FileHandleDto(...);
}
```

**Validation:** Files > 4MB throw `ArgumentException` with message: "Content size exceeds 4MB limit for small uploads. Use chunked upload instead."

---

### ✅ Large File Upload (≥ 4MB) - AVAILABLE via Chunked Upload

#### Step 1: Create Upload Session

**Managed Identity (MI) Endpoint:**
- `POST /api/containers/{containerId}/upload?path={filePath}`

**On-Behalf-Of (OBO) Endpoint:**
- `POST /api/obo/drives/{driveId}/upload-session?path={filePath}&conflictBehavior={behavior}`

**Implementation:** [UploadSessionManager.cs:88-164](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\UploadSessionManager.cs)

```csharp
public async Task<UploadSessionDto?> CreateUploadSessionAsync(
    string containerId,
    string path,
    CancellationToken ct = default)
{
    // Get drive for container
    var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive.GetAsync();

    // Create upload session
    var session = await graphClient.Drives[drive.Id].Root
        .ItemWithPath(path)
        .CreateUploadSession
        .PostAsync(createUploadSessionPostRequestBody, cancellationToken: ct);

    return new UploadSessionDto(session.UploadUrl!, session.ExpirationDateTime);
}
```

**Response:**
```json
{
  "uploadUrl": "https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}/uploadSession?uploadSessionId={sessionId}",
  "expirationDateTime": "2025-10-05T12:00:00Z"
}
```

#### Step 2: Upload Chunks

**Managed Identity (MI) Endpoint:**
- `PUT /api/upload-session/chunk`
  - Headers: `Upload-Session-Url`, `Content-Range`
  - Body: Raw chunk bytes

**On-Behalf-Of (OBO) Endpoint:**
- `PUT /api/obo/upload-session/chunk`
  - Headers: `Upload-Session-Url`, `Content-Range`
  - Body: Raw chunk bytes

**Implementation:** [UploadSessionManager.cs:166-216](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\UploadSessionManager.cs)

```csharp
public async Task<HttpResponseMessage> UploadChunkAsync(
    UploadSessionDto session,
    Stream file,
    long start,
    long length,
    CancellationToken ct = default)
{
    using var httpClient = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl);

    // Read chunk data
    var buffer = new byte[length];
    var bytesRead = await file.ReadAsync(buffer, 0, (int)length, ct);

    request.Content = new ByteArrayContent(buffer, 0, bytesRead);
    request.Content.Headers.ContentLength = bytesRead;
    request.Content.Headers.ContentRange = new ContentRangeHeaderValue(
        start, start + bytesRead - 1);

    var response = await httpClient.SendAsync(request, ct);
    return response;
}
```

**Chunk Upload Flow:**
1. Read file chunk (typically 320 KB - 10 MB)
2. Set `Content-Range: bytes {start}-{end}/{totalSize}` header
3. PUT chunk to upload session URL
4. Graph API responds:
   - `202 Accepted` - More chunks expected, returns `nextExpectedRanges`
   - `200/201 OK/Created` - Upload complete, returns DriveItem

**Example:**
```http
PUT https://graph.microsoft.com/.../uploadSession?uploadSessionId=xxx
Content-Range: bytes 0-327679/10485760
Content-Length: 327680

<binary chunk data>
```

Response:
```json
{
  "expirationDateTime": "2025-10-05T12:00:00Z",
  "nextExpectedRanges": ["327680-10485760"]
}
```

---

### OBO Chunked Upload Implementation

**Fully Implemented in:** [OBOEndpoints.cs:79-173](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\OBOEndpoints.cs)

```csharp
// Create session
app.MapPost("/api/obo/drives/{driveId}/upload-session", async (...) => {
    var session = await speFileStore.CreateUploadSessionAsUserAsync(
        userToken, driveId, path, behavior, ct);
    return TypedResults.Ok(session);
});

// Upload chunk
app.MapPut("/api/obo/upload-session/chunk", async (...) => {
    var result = await speFileStore.UploadChunkAsUserAsync(
        userToken, uploadSessionUrl, contentRange, chunkData, ct);

    return result.StatusCode switch {
        200 => TypedResults.Ok(result.CompletedItem),      // Complete
        201 => TypedResults.Created("", result.CompletedItem), // Complete
        202 => TypedResults.Accepted("", result.CompletedItem), // More chunks
        _ => TypedResults.Problem(...)
    };
});
```

---

## Current Sprint 6 Integration Gap

### What Sprint 6 Currently Implements

**From [TASK-1-TECHNICAL-SPECIFICATION.md](TASK-1-TECHNICAL-SPECIFICATION.md):**

```json
{
  "fileConfig": {
    "maxFileSize": 4194304,  // 4MB limit
    "allowedExtensions": [".pdf", ".docx", ".xlsx", ...],
    "uploadChunkSize": 327680  // Defined but not used
  }
}
```

**Add File Operation (Current Plan):**
1. User selects file via file picker
2. JavaScript validates file size < 4MB
3. If > 4MB → Show error: "File size exceeds 4MB limit"
4. If < 4MB → Call `PUT /api/drives/{driveId}/upload`

**Gap:** Files ≥ 4MB are rejected by client-side validation, even though API supports them via chunked upload.

---

## User Impact Analysis

### Typical Document Sizes

| Document Type | Typical Size | Current Sprint 6 Support |
|---------------|--------------|--------------------------|
| Text documents (.txt, .docx) | 50 KB - 500 KB | ✅ Supported |
| Spreadsheets (.xlsx) | 100 KB - 2 MB | ✅ Supported |
| Presentations (.pptx) | 1 MB - 10 MB | ⚠️ Only if < 4MB |
| PDFs (simple) | 100 KB - 1 MB | ✅ Supported |
| PDFs (scanned/images) | 5 MB - 50 MB | ❌ **NOT Supported** |
| CAD drawings (.dwg, .dxf) | 1 MB - 100 MB | ❌ **NOT Supported** |
| Images (photos) | 2 MB - 10 MB | ⚠️ Only if < 4MB |
| Videos | 10 MB - 500 MB | ❌ **NOT Supported** |
| Archives (.zip, .7z) | 5 MB - 1 GB | ❌ **NOT Supported** |

### Real-World Scenarios

**❌ BLOCKED Scenarios (4MB limit):**
1. **Legal department** - Scanning contracts with exhibits (typically 10-50 MB PDFs)
2. **Engineering team** - Uploading CAD drawings (20-100 MB)
3. **Marketing team** - Uploading high-res images (5-15 MB per image)
4. **Sales team** - Uploading presentation decks with embedded videos (20-50 MB)
5. **HR department** - Uploading employee handbooks with images (8-12 MB)

**✅ WORKING Scenarios (< 4MB):**
1. Basic contracts and agreements
2. Text-based reports
3. Simple spreadsheets
4. Low-res images

### User Experience

**Current behavior:**
```
User: [Selects 15 MB scanned contract PDF]
System: "File size exceeds 4MB limit. Please select a smaller file."
User: "This is a critical legal document, I can't make it smaller!"
Result: ❌ User blocked, manual workaround needed (email, USB drive, etc.)
```

**Expected behavior:**
```
User: [Selects 15 MB scanned contract PDF]
System: "Uploading... 25%... 50%... 75%... 100%"
System: "File uploaded successfully!"
Result: ✅ User can upload any size file up to SharePoint limits
```

---

## Recommended Enhancement

### Enhancement Scope

**Priority:** 🔴 **HIGH** - Critical for production use

**Effort:** 8-12 hours

**Sprint:** Sprint 6 Phase 3 (JavaScript Integration) or Sprint 7

### Implementation Strategy

#### Option A: Sprint 6 Phase 3 Enhancement (Recommended)

**Add to Phase 3 JavaScript Integration (20 hours → 28 hours):**

1. **Detect File Size** (1 hour)
   - If file < 4MB → Use existing `PUT /api/drives/{driveId}/upload`
   - If file ≥ 4MB → Use chunked upload flow

2. **Implement Chunked Upload** (6 hours)
   ```javascript
   Spaarke.DocumentGrid.uploadLargeFile = async function(file, driveId, path) {
       // Step 1: Create upload session
       const session = await createUploadSession(driveId, path);

       // Step 2: Upload chunks
       const chunkSize = 327680; // 320 KB
       const totalChunks = Math.ceil(file.size / chunkSize);

       for (let i = 0; i < totalChunks; i++) {
           const start = i * chunkSize;
           const end = Math.min(start + chunkSize, file.size);
           const chunk = file.slice(start, end);

           const response = await uploadChunk(session.uploadUrl, chunk, start, end, file.size);

           // Update progress
           const progress = Math.round((end / file.size) * 100);
           Spaarke.Documents.Utils.showLoading(`Uploading... ${progress}%`);

           if (response.completedItem) {
               return response.completedItem; // Upload complete
           }
       }
   };
   ```

3. **Add Progress Indicator** (2 hours)
   - Show percentage progress during chunked upload
   - Show estimated time remaining
   - Allow cancellation

4. **Error Handling** (2 hours)
   - Handle chunk upload failures
   - Implement retry logic for failed chunks
   - Resume from last successful chunk
   - Handle session expiration (24 hour limit)

5. **Testing** (2 hours)
   - Test with 10 MB file
   - Test with 50 MB file
   - Test with network interruption
   - Test cancellation

**Benefits:**
- ✅ Complete file upload capability in Sprint 6
- ✅ Better user experience from day 1
- ✅ No technical debt

**Risks:**
- ⚠️ Sprint 6 timeline extends by 8-12 hours (10-15% increase)
- ⚠️ More complexity in initial release

#### Option B: Sprint 7 Enhancement (Defer)

**Keep 4MB limit in Sprint 6, add chunked upload in Sprint 7:**

1. **Sprint 6** - Ship with 4MB limit
   - Document limitation clearly
   - Provide workaround documentation
   - Gather user feedback

2. **Sprint 7** - Add chunked upload (12 hours)
   - Full chunked upload implementation
   - Enhanced progress indicators
   - Resume capability
   - Better error handling

**Benefits:**
- ✅ Sprint 6 ships on time (76 hours)
- ✅ More time to polish chunked upload experience
- ✅ Can gather user feedback on 4MB limit

**Risks:**
- ❌ Users blocked from uploading large files for weeks
- ❌ Negative first impression
- ❌ Workarounds create bad habits

---

## Technical Implementation Details

### JavaScript Chunked Upload Pattern

```javascript
Spaarke.DocumentGrid.ChunkedUpload = {
    // Create upload session
    createSession: async function(driveId, fileName) {
        const token = await Spaarke.DocumentGrid.getAuthToken();
        const response = await fetch(
            `${config.apiBaseUrl}/api/obo/drives/${driveId}/upload-session?path=/${fileName}&conflictBehavior=rename`,
            {
                method: "POST",
                headers: {
                    "Authorization": `Bearer ${token}`,
                    "Content-Type": "application/json"
                }
            }
        );

        if (!response.ok) throw new Error("Failed to create upload session");

        return await response.json(); // { uploadUrl, expirationDateTime }
    },

    // Upload single chunk
    uploadChunk: async function(uploadUrl, chunk, start, end, totalSize) {
        const contentRange = `bytes ${start}-${end - 1}/${totalSize}`;

        const response = await fetch(uploadUrl, {
            method: "PUT",
            headers: {
                "Content-Range": contentRange,
                "Content-Length": chunk.size.toString()
            },
            body: chunk
        });

        if (!response.ok && response.status !== 202) {
            throw new Error(`Chunk upload failed: ${response.status}`);
        }

        const result = await response.json();
        return {
            statusCode: response.status,
            nextExpectedRanges: result.nextExpectedRanges,
            completedItem: result.id ? result : null // DriveItem if complete
        };
    },

    // Upload file with progress
    uploadFile: async function(file, driveId, fileName, onProgress) {
        const chunkSize = 327680; // 320 KB (Microsoft recommended)

        // Create session
        const session = await this.createSession(driveId, fileName);

        // Upload chunks
        let start = 0;
        while (start < file.size) {
            const end = Math.min(start + chunkSize, file.size);
            const chunk = file.slice(start, end);

            const result = await this.uploadChunk(
                session.uploadUrl,
                chunk,
                start,
                end,
                file.size
            );

            // Update progress
            const progress = Math.round((end / file.size) * 100);
            if (onProgress) onProgress(progress);

            // Check if complete
            if (result.completedItem) {
                return result.completedItem;
            }

            // Next chunk
            start = end;
        }

        throw new Error("Upload completed but no item returned");
    }
};
```

### Usage in Add File Command

```javascript
Spaarke.DocumentGrid.addFile = async function(context) {
    const file = await Spaarke.Documents.Utils.showFilePicker();
    if (!file) return;

    const selectedRecord = context.selectedRecords[0];
    const driveId = selectedRecord.getValue(config.fieldMappings.graphDriveId);

    try {
        Spaarke.Documents.Utils.showLoading("Preparing upload...");

        let uploadedItem;

        if (file.size < 4 * 1024 * 1024) {
            // Small file - use simple upload
            uploadedItem = await Spaarke.DocumentGrid.uploadSmallFile(file, driveId);
        } else {
            // Large file - use chunked upload
            uploadedItem = await Spaarke.DocumentGrid.ChunkedUpload.uploadFile(
                file,
                driveId,
                file.name,
                (progress) => {
                    Spaarke.Documents.Utils.showLoading(`Uploading... ${progress}%`);
                }
            );
        }

        // Update Dataverse record
        await Spaarke.DocumentGrid.updateDocumentMetadata(
            context.selectedRecordIds[0],
            uploadedItem
        );

        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showSuccess("Upload Complete", `File ${file.name} uploaded successfully`);

        context.refreshGrid();

    } catch (error) {
        Spaarke.Documents.Utils.hideLoading();
        Spaarke.Documents.Utils.showError("Upload Failed", error.message);
    }
};
```

---

## Recommended Configuration Updates

### Update fileConfig Schema

**From [TASK-1-TECHNICAL-SPECIFICATION.md](TASK-1-TECHNICAL-SPECIFICATION.md):**

```json
{
  "fileConfig": {
    "maxFileSize": 4194304,           // ❌ Current: 4MB hard limit
    "allowedExtensions": [...],
    "uploadChunkSize": 327680          // ✅ Already defined
  }
}
```

**Enhanced Configuration:**

```json
{
  "fileConfig": {
    "maxFileSize": 104857600,         // ✅ New: 100MB (or higher, up to SharePoint limit)
    "smallFileThreshold": 4194304,    // ✅ New: 4MB threshold for simple vs chunked
    "uploadChunkSize": 327680,        // ✅ Existing: 320 KB chunks (Microsoft recommended)
    "allowedExtensions": [...],
    "showProgressBar": true,          // ✅ New: Show progress for chunked uploads
    "allowCancellation": true         // ✅ New: Allow users to cancel uploads
  }
}
```

---

## Decision Matrix

| Factor | Option A: Sprint 6 | Option B: Sprint 7 | Recommendation |
|--------|-------------------|-------------------|----------------|
| **User Impact** | ✅ Full capability from day 1 | ❌ Users blocked for weeks | **Option A** |
| **Timeline** | ⚠️ +10-15% (8-12 hours) | ✅ No impact to Sprint 6 | Depends on deadline |
| **Technical Risk** | ⚠️ More complexity initially | ✅ Lower initial risk | **Option A** (risk is low) |
| **User Satisfaction** | ✅ High - works as expected | ❌ Low - blocked workflows | **Option A** |
| **Technical Debt** | ✅ None | ⚠️ Known limitation to fix later | **Option A** |
| **API Readiness** | ✅ API already supports it | ✅ API already supports it | Equal |
| **Testing Effort** | ⚠️ More testing upfront | ✅ Less initial testing | **Option B** |

### Recommendation: **Option A - Implement in Sprint 6 Phase 3**

**Rationale:**
1. **API Already Supports It** - No backend work required, just JavaScript
2. **User Experience Critical** - 4MB limit will block real-world usage
3. **Low Technical Risk** - Chunked upload is well-documented pattern
4. **Better First Impression** - Users won't encounter artificial limitations
5. **Moderate Effort** - 8-12 hours is manageable within 76-hour sprint

**Updated Sprint 6 Effort:**
- Phase 1: 8 hours ✅ Complete
- Phase 2: 16 hours
- Phase 3: 20 → **28 hours** (add chunked upload)
- Phase 4: 8 hours
- Phase 5: 16 hours
- Phase 6: 8 hours
- **Total: 84 hours** (11% increase)

---

## Alternative: Quick Win for Sprint 6

If time is truly constrained, implement a **hybrid approach**:

### Hybrid Option C: Progressive Enhancement

**Sprint 6 (Ship with basic support):**
- Small files (< 4MB): ✅ Full support with simple upload
- Large files (≥ 4MB): ⚠️ Show informative message with workaround

**Enhanced Error Message:**
```javascript
if (file.size >= 4 * 1024 * 1024) {
    Spaarke.Documents.Utils.showError(
        "Large File Detected",
        `This file (${formatFileSize(file.size)}) exceeds the current 4MB limit.\n\n` +
        `Large file upload support is coming soon!\n\n` +
        `For now, please:\n` +
        `1. Compress the file if possible, or\n` +
        `2. Use the SharePoint interface directly, or\n` +
        `3. Contact support for assistance`
    );
    return;
}
```

**Sprint 7 (Add full support):**
- Implement chunked upload (12 hours)
- Remove 4MB limitation
- Add progress indicators

**Benefits:**
- ✅ Sprint 6 ships on time
- ✅ Users understand limitation is temporary
- ✅ Workaround guidance provided
- ✅ Sets expectation for future enhancement

---

## Conclusion

### Key Findings

1. ✅ **SDAP API fully supports large file uploads** via chunked upload sessions
2. ✅ **No backend enhancement required** - API endpoints already exist
3. ❌ **Current Sprint 6 plan has artificial 4MB limitation**
4. ⚠️ **4MB limit will block real-world production usage**

### Recommended Action Plan

**RECOMMENDED: Option A - Implement in Sprint 6 Phase 3**

**Changes Required:**
1. Update Phase 3 JavaScript Integration (20 → 28 hours)
   - Add chunked upload detection (file size check)
   - Implement chunked upload flow
   - Add progress indicators
   - Add error handling and retry logic

2. Update Configuration Schema
   - Increase `maxFileSize` to 100MB
   - Add `smallFileThreshold` (4MB)
   - Add `showProgressBar` option

3. Update Testing Plan
   - Add large file test scenarios
   - Test network interruption handling
   - Test cancellation

4. Update Documentation
   - Document chunked upload capability
   - Update user guide with file size limits
   - Add troubleshooting for large file uploads

**Total Sprint 6 Effort:** 76 → 84 hours (11% increase)

**User Impact:** ✅ Full production capability, no blocked workflows

---

**Analysis Prepared By:** AI Agent
**Date:** October 4, 2025
**Status:** Ready for stakeholder decision
**Next Step:** Choose Option A, B, or C and update Sprint 6 plan accordingly
