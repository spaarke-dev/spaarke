# Sprint 7B: Universal Quick Create with File Upload - Overview

**Sprint:** 7B - Quick Create Implementation
**Status:** 📋 Ready to Start
**Control Version:** To be created (v1.0.0)
**Date Created:** October 6, 2025
**Prerequisites:** Sprint 7A code complete, Sprint 8 MSAL integration complete

---

## Executive Summary

Sprint 7B implements a **Universal Quick Create PCF control** that enables users to create new Dataverse records with file upload to SharePoint Embedded. This control uses **MSAL authentication from day one** (lessons learned from Sprint 8), retrieves parent context from Power Apps, and provides a consistent Quick Create user experience.

**Key Achievement Goal:** Enable creation of test files for Sprint 7A validation AND provide production-ready document creation workflow.

---

## What We're Building

### Universal Quick Create PCF Control

**Features:**
- 📁 File picker for SharePoint Embedded upload
- 🔄 MSAL authentication (Sprint 8 pattern)
- 📋 Auto-populated fields from parent entity context
- 🎨 Fluent UI v9 components
- ⚙️ Configurable field mappings
- 🎯 Universal design (works across entity types)

**User Flow:**
```
Matter Form → Documents Subgrid → Click "+ New Document"
  ↓
Quick Create Form Opens (Universal Quick Create PCF)
  ├─ File Picker (required)
  ├─ Document Title (auto-filled from Matter)
  ├─ Description (optional)
  └─ Container ID (auto-filled from Matter)
  ↓
User selects file → Clicks Save
  ↓
PCF uploads file to SharePoint Embedded via SDAP API (MSAL auth)
  ↓
PCF creates Dataverse record with file metadata
  ↓
Form closes → Grid refreshes (shows new document)
```

---

## Critical Context from Previous Sprints

### Sprint 8 MSAL Integration (MUST USE) ✅

Sprint 8 implemented MSAL authentication for the Universal Dataset Grid. **Sprint 7B MUST use the same pattern from day one.**

**What Sprint 8 Provides:**
- `services/auth/msalConfig.ts` - MSAL configuration
- `services/auth/MsalAuthProvider.ts` - Token acquisition with caching
- `services/SdapApiClientFactory.ts` - Factory using MSAL
- `services/SdapApiClient.ts` - API client with auto-retry

**Sprint 7B Requirements:**
- ✅ Use `SdapApiClientFactory.create()` for MSAL auth
- ✅ Initialize MSAL in `init()` method
- ✅ Handle race conditions (user clicks before MSAL ready)
- ✅ Follow same error handling patterns
- ✅ Use same Azure AD app registration

**DO NOT:**
- ❌ Use PCF context tokens (`context.userSettings.accessToken`)
- ❌ Create separate authentication logic
- ❌ Bypass SdapApiClientFactory
- ❌ Implement custom token caching

### Sprint 7A Dataset Grid (Reference Pattern) ✅

Sprint 7A implemented file operations (download, delete, replace) using services + MSAL. **Sprint 7B should follow the same architectural patterns.**

**Patterns to Reuse:**
```typescript
// Service pattern (Sprint 7A)
export class FileDownloadService {
    constructor(private apiClient: SdapApiClient) {}
    async downloadFile(driveId, itemId, fileName) {
        const blob = await this.apiClient.downloadFile({ driveId, itemId });
        // ...
    }
}

// Sprint 7B equivalent
export class FileUploadService {
    constructor(private apiClient: SdapApiClient) {}
    async uploadFile(driveId, file, fileName) {
        const result = await this.apiClient.uploadFile({ driveId, file, fileName });
        // ...
    }
}
```

**Architectural Patterns:**
- ✅ Dependency injection (services receive `SdapApiClient`)
- ✅ ServiceResult return type for error handling
- ✅ Comprehensive logging via `logger`
- ✅ React hooks for state management
- ✅ Fluent UI v9 components
- ✅ Single React root architecture

---

## Architecture

### Two-PCF System

```
┌─────────────────────────────────────────────────────────────────┐
│  Power Apps Model-Driven App (Matter Form)                      │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Documents Subgrid (Universal Dataset Grid v2.1.4)         │ │
│  │  - Sprint 7A: Download, Delete, Replace                    │ │
│  │  - MSAL Authentication (Sprint 8)                          │ │
│  │  - [+ New Document] button launches Quick Create           │ │
│  └────────────────────────────────────────────────────────────┘ │
│                           ↓                                      │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Quick Create Form (Universal Quick Create v1.0.0)         │ │
│  │  - Sprint 7B: File Upload + Record Creation                │ │
│  │  - MSAL Authentication (Sprint 8 pattern)                  │ │
│  │  - Auto-populate from parent context                       │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                           ↓
              Both controls use MSAL + SdapApiClient
                           ↓
┌─────────────────────────────────────────────────────────────────┐
│  Spe.Bff.Api (Azure Web App)                                    │
│  - PUT /api/obo/drives/{driveId}/upload?fileName={name}         │
│  - GET /api/obo/drives/{driveId}/items/{itemId}/content         │
│  - DELETE /api/obo/drives/{driveId}/items/{itemId}              │
│  - OBO authentication (validated in Sprint 8)                   │
└─────────────────────────────────────────────────────────────────┘
                           ↓
┌──────────────────────────┐    ┌──────────────────────────────┐
│  Dataverse               │    │  SharePoint Embedded         │
│  - sprk_document entity  │    │  - File storage (Graph API)  │
│  - sprk_matter entity    │    │  - Container management      │
└──────────────────────────┘    └──────────────────────────────┘
```

### MSAL Authentication Flow (Sprint 8 Pattern)

```
Quick Create Form → User clicks Save → FileUploadService
  ↓
SdapApiClient.uploadFile({ driveId, file, fileName })
  ↓
getAccessToken() (from SdapApiClientFactory)
  ↓
MsalAuthProvider.getToken(SPE_BFF_API_SCOPES)
  ↓
Check sessionStorage cache:
  ├─ Cache hit (5ms) → Return cached token
  └─ Cache miss (420ms) → ssoSilent() → Cache → Return
  ↓
PUT /api/obo/drives/{driveId}/upload
Authorization: Bearer {token}
Body: {file binary}
  ↓
BFF API validates token → OBO exchange → Graph API
  ↓
SharePoint Embedded stores file → Returns metadata
  ↓
FileUploadService receives FileHandleDto:
{
  id: "01ABC...",              // → sprk_graphitemid
  name: "Contract.pdf",        // → sprk_filename
  size: 2458624,               // → sprk_filesize
  webUrl: "https://...",       // → sprk_filepath
  createdDateTime: "...",      // → sprk_createddatetime
  lastModifiedDateTime: "...", // → sprk_lastmodifieddatetime
  eTag: "...",                 // → sprk_etag
  parentId: "..."              // → sprk_parentfolderid
}
  ↓
Create Dataverse record with file metadata
  ↓
Form closes → Grid refreshes
```

---

## Technology Stack

### Core Technologies (Same as Sprint 7A)

| Technology | Version | Purpose |
|------------|---------|---------|
| PCF Framework | Latest | Power Apps Component Framework |
| React | 18.2.0 | UI library |
| Fluent UI v9 | 9.54.0 | Component library |
| TypeScript | Latest | Type safety |
| MSAL Browser | 4.24.1 | Authentication (Sprint 8) |
| Webpack | 5.x | Bundling |

### Reusable Services (From Sprint 7A/8)

**MUST REUSE (Do NOT recreate):**
- `services/auth/msalConfig.ts` - MSAL configuration
- `services/auth/MsalAuthProvider.ts` - Token provider
- `services/SdapApiClientFactory.ts` - API client factory
- `services/SdapApiClient.ts` - Core API client
- `utils/logger.ts` - Logging utility
- `providers/ThemeProvider.ts` - Theme resolution
- `components/ErrorBoundary.tsx` - Error handling

**NEW for Sprint 7B:**
- `services/FileUploadService.ts` - Single file upload logic
- `services/MultiFileUploadService.ts` - Multi-file upload with adaptive strategy
- `services/ParentContextService.ts` - Parent data retrieval
- `components/QuickCreateForm.tsx` - Form UI
- `components/FilePicker.tsx` - File selection (supports multiple)
- `components/UploadProgress.tsx` - Progress indicator for long-running uploads
- `types/quickCreate.ts` - Quick Create types
- `types/multiFileUpload.ts` - Multi-file upload types

---

## Power Apps Context Integration

### Context Provided by Power Apps

When "+ New Document" is clicked from a subgrid, Power Apps automatically provides:

```typescript
// Available in init() method
const formContext = (context as any).mode?.contextInfo;

if (formContext) {
    // Parent entity
    const parentEntityName = formContext.regardingEntityName;  // "sprk_matter"
    const parentRecordId = formContext.regardingObjectId;      // Matter GUID

    // Current entity
    const entityName = formContext.entityName;                 // "sprk_document"
}
```

### Retrieving Parent Record Data

```typescript
// ParentContextService
async loadParentData(context, parentEntityName, parentRecordId) {
    const selectFields = this.getParentSelectFields(parentEntityName);

    const parentRecord = await context.webAPI.retrieveRecord(
        parentEntityName,
        parentRecordId,
        `?$select=${selectFields}`
    );

    return parentRecord;
}

getParentSelectFields(entityName: string): string {
    const fieldMappings: Record<string, string[]> = {
        'sprk_matter': [
            'sprk_name',
            'sprk_containerid',           // ← Container ID for SPE
            '_ownerid_value',
            '_sprk_primarycontact_value',
            'sprk_matternumber'
        ]
    };

    return (fieldMappings[entityName] || ['name']).join(',');
}
```

### Field Mapping Configuration

```typescript
// Default value mappings (parent → child)
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",    // Container ID for SPE
    "sprk_name": "sprk_documenttitle",         // Matter name → Document title
    "_ownerid_value": "ownerid",               // Owner
    "_sprk_primarycontact_value": "sprk_contact"
  }
}
```

**Critical Field:** `sprk_containerid`
- This is the **Container ID** from the parent Matter
- Used to determine the **Graph Drive ID** for SharePoint Embedded
- MUST be retrieved and passed to upload service

---

## Multi-File Upload with Adaptive Strategy

### Overview

Sprint 7B supports **multiple file uploads** with an **adaptive upload strategy** that optimizes for both speed and reliability based on file size and count.

**Key Features:**
- ✅ Upload 1-10 files in a single Quick Create
- ✅ Automatic strategy selection (fast for small files, safe for large files)
- ✅ One Document record created per file
- ✅ Shared metadata across all documents (description, category, etc.)
- ✅ Progress indicators for user feedback
- ✅ Future-ready for large batch server processing

### Adaptive Upload Strategy Decision Tree

```typescript
function determineUploadStrategy(files: File[]): 'sync-parallel' | 'long-running' {
    const totalFiles = files.length;
    const largestFile = Math.max(...files.map(f => f.size));
    const totalSize = files.reduce((sum, f) => sum + f.size, 0);

    // Strategy 1: Sync Parallel (Fast & Simple)
    // Use when: 1-3 files AND all <10MB AND total <20MB
    if (
        totalFiles <= 3 &&
        largestFile < 10 * 1024 * 1024 &&      // 10MB
        totalSize < 20 * 1024 * 1024            // 20MB
    ) {
        return 'sync-parallel';
    }

    // Strategy 2: Long-Running Process (Safe)
    // Use when: >3 files OR any file >10MB OR total >20MB
    return 'long-running';
}
```

### Upload Strategies

#### Strategy 1: Sync Parallel Upload (Small/Few Files)

**When:** 1-3 files, all <10MB, total <20MB

**Performance:**
```
Example: 2 files × 3MB each (6MB total)
Timeline:
  [MSAL 420ms] + [Upload 3s parallel] + [Dataverse 400ms] = ~4 seconds

User waits 4 seconds - ACCEPTABLE ✅
```

**User Experience:**
```
User clicks Save
  ↓
┌────────────────────────────┐
│ Uploading 2 files...       │
│ ████████████████ 100%      │
│                            │
│ ✓ Contract.pdf             │
│ ✓ Agreement.docx           │
└────────────────────────────┘
  ↓
[4 seconds later]
  ↓
Form closes automatically
Grid shows 2 new documents
```

**Implementation:**
```typescript
async handleSyncParallelUpload(
    files: File[],
    driveId: string,
    metadata: SharedMetadata
): Promise<ServiceResult<DocumentRecord[]>> {

    // Upload all files in parallel
    const uploadPromises = files.map(async (file) => {
        const result = await this.uploadService.uploadFile({
            driveId,
            file,
            fileName: file.name
        });
        return { file, result };
    });

    const uploadResults = await Promise.all(uploadPromises);

    // Create Dataverse records sequentially (avoid throttling)
    const docRecords: DocumentRecord[] = [];
    for (const { file, result } of uploadResults) {
        const doc = await this.createDocumentRecord(result, driveId, file, metadata);
        docRecords.push(doc);
    }

    return { success: true, data: docRecords };
}
```

**Pros:**
- ⚡ Fast (3-5 seconds)
- ✅ Simple error handling
- ✅ Immediate feedback
- ✅ Form closes when done

**Cons:**
- ⏳ User must wait (but short wait is acceptable)

---

#### Strategy 2: Long-Running Process (Large/Many Files)

**When:** >3 files OR any file >10MB OR total >20MB

**Performance:**
```
Example: 5 files × 15MB each (75MB total)
Timeline:
  Batch 1 (3 files): [MSAL 420ms] + [Upload 12s parallel] + [Dataverse 600ms] = ~13s
  Batch 2 (2 files): [MSAL 5ms] + [Upload 12s parallel] + [Dataverse 400ms] = ~12s

Total: ~25 seconds (batched upload)
```

**User Experience:**
```
User clicks Save
  ↓
Form shows progress (cannot close during upload):
┌────────────────────────────────────┐
│ Uploading 5 files...               │
│ Estimated time: 25 seconds         │
│                                    │
│ ████████░░░░░░░░░░░░ 40% (2 of 5) │
│                                    │
│ ✓ Contract.pdf (15.2 MB)           │
│ ✓ Agreement.docx (14.8 MB)         │
│ ↻ Invoice.xlsx (uploading...)      │
│ ⏳ Receipt.pdf (waiting)            │
│ ⏳ Report.docx (waiting)            │
│                                    │
│ Please keep this window open       │
└────────────────────────────────────┘
  ↓
[25 seconds later]
  ↓
┌────────────────────────────────────┐
│ ✓ Upload complete!                 │
│ 5 documents created successfully   │
│                                    │
│ Closing in 2 seconds...            │
└────────────────────────────────────┘
  ↓
Form closes automatically
Grid shows 5 new documents
```

**Implementation:**
```typescript
async handleLongRunningUpload(
    files: File[],
    driveId: string,
    metadata: SharedMetadata
): Promise<ServiceResult<DocumentRecord[]>> {

    // Show progress UI
    this.setState({
        uploading: true,
        canClose: false,
        uploadProgress: { current: 0, total: files.length }
    });

    // Calculate adaptive batch size
    const batchSize = this.calculateBatchSize(files);
    const batches = this.chunkArray(files, batchSize);

    const results: DocumentRecord[] = [];

    // Process batches
    for (const batch of batches) {
        // Upload batch in parallel
        const uploadPromises = batch.map(file =>
            this.uploadService.uploadFile({ driveId, file, fileName: file.name })
        );

        const uploadResults = await Promise.all(uploadPromises);

        // Create Dataverse records sequentially
        for (let i = 0; i < batch.length; i++) {
            const docRecord = await this.createDocumentRecord(
                uploadResults[i],
                driveId,
                batch[i],
                metadata
            );

            results.push(docRecord);

            // Update progress
            this.setState(prev => ({
                uploadProgress: {
                    current: prev.uploadProgress.current + 1,
                    total: files.length
                }
            }));
        }
    }

    // Enable close
    this.setState({ canClose: true });

    // Auto-close after 2 seconds
    setTimeout(() => this.closeForm(), 2000);

    return { success: true, data: results };
}

private calculateBatchSize(files: File[]): number {
    const avgSize = files.reduce((sum, f) => sum + f.size, 0) / files.length;

    if (avgSize < 1_000_000) return 5;       // <1MB: batch of 5
    if (avgSize < 5_000_000) return 3;       // 1-5MB: batch of 3
    return 2;                                 // >5MB: batch of 2
}
```

**Pros:**
- ✅ Handles large files safely
- ✅ Shows detailed progress
- ✅ Batched upload prevents browser overload
- ✅ Adaptive batch sizing optimizes performance

**Cons:**
- ⏳ User must wait (but progress is visible)
- ⚠️ Form cannot be closed during upload
- ⚠️ If user refreshes page, upload cancels

---

### Future Enhancement: Large Batch Server Processing

**When:** >10 files OR total size >100MB OR user preference

**Architecture:**
```
Quick Create PCF
  ↓
Azure Service Bus Queue
  ↓
Azure Function (Background Processing)
  ├─ Upload files to SPE (chunked, resumable)
  ├─ Create Dataverse records
  ├─ Send notifications
  └─ Update job status
  ↓
User notification (email/toast)
```

**Benefits:**
- ✅ Form closes immediately
- ✅ Upload continues even if browser closes
- ✅ Resumable uploads for very large files
- ✅ Batch processing optimizations
- ✅ Email notification on completion

**Implementation (Future Sprint):**
```typescript
async handleServerBatchUpload(
    files: File[],
    driveId: string,
    metadata: SharedMetadata
): Promise<ServiceResult> {

    // Create upload job record
    const jobId = await context.webAPI.createRecord('sprk_uploadjob', {
        sprk_name: `Upload ${files.length} files`,
        sprk_status: 'Queued',
        sprk_filecount: files.length,
        sprk_totalsize: files.reduce((sum, f) => sum + f.size, 0)
    });

    // Upload files to Azure Blob Storage (staging)
    await this.uploadFilesToBlobStorage(files, jobId);

    // Queue processing message
    await serviceBusClient.sendMessage({
        jobId: jobId,
        driveId: driveId,
        metadata: metadata,
        userId: context.userSettings.userId
    });

    // Close form immediately
    this.showNotification(
        'Upload queued! You will be notified when complete.',
        'info'
    );

    return { success: true };
}
```

**Note:** This will be implemented in a future sprint when we have very large batch upload requirements.

---

## File Upload Workflow (Detailed)

### Step-by-Step File Upload Process (Single File)

```typescript
// 1. User selects file in Quick Create form
const file = await filePicker.getFile();  // File object from browser

// 2. Get Container ID from parent Matter record
const parentRecord = await ParentContextService.loadParentData(...);
const containerId = parentRecord.sprk_containerid;  // GUID

// 3. Get Drive ID from Container
// Option A: Container record has driveId stored
const container = await context.webAPI.retrieveRecord(
    'sprk_container',
    containerId,
    '?$select=sprk_graphdriveid'
);
const driveId = container.sprk_graphdriveid;

// Option B: Call SDAP API to get drive from container
const driveId = await sdapClient.getDriveIdFromContainer(containerId);

// 4. Upload file to SharePoint Embedded
const uploadResult = await FileUploadService.uploadFile({
    driveId: driveId,
    file: file,
    fileName: file.name
});

// uploadResult (FileHandleDto):
{
    id: "01ABC123...",                    // Graph Item ID
    name: "Contract.pdf",
    size: 2458624,
    createdDateTime: "2025-10-06T...",
    lastModifiedDateTime: "2025-10-06T...",
    eTag: "\"{12345}\"",
    parentId: "01DEF456...",
    webUrl: "https://contoso.sharepoint.com/..."
}

// 5. Create Dataverse record with file metadata
await context.webAPI.createRecord('sprk_document', {
    // Basic fields
    sprk_documentname: file.name,

    // File metadata from FileHandleDto
    sprk_graphitemid: uploadResult.id,
    sprk_graphdriveid: driveId,
    sprk_filename: uploadResult.name,
    sprk_filesize: uploadResult.size,
    sprk_createddatetime: uploadResult.createdDateTime,
    sprk_lastmodifieddatetime: uploadResult.lastModifiedDateTime,
    sprk_etag: uploadResult.eTag,
    sprk_parentfolderid: uploadResult.parentId,
    sprk_filepath: uploadResult.webUrl,
    sprk_hasfile: true,
    sprk_mimetype: file.type,

    // Parent relationship
    'sprk_matter@odata.bind': `/sprk_matters(${parentRecordId})`
});

// 6. Close form → Grid auto-refreshes
```

---

## Azure AD Configuration (Sprint 8)

### App Registration 1: Dataverse/PCF Control

**Name:** Sparke DSM-SPE Dev 2
**Client ID:** `170c98e1-d486-4355-bcbe-170454e0207c`
**Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2`
**Redirect URI:** `https://spaarkedev1.crm.dynamics.com`

**API Permissions (Delegated):**
- Microsoft Graph / User.Read
- SPE BFF API / user_impersonation

**Purpose:** Represents BOTH PCF controls (Dataset Grid AND Quick Create)

### App Registration 2: SPE BFF API

**Name:** SPE BFF API
**Client ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
**Application ID URI:** `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Exposed API:**
- Scope: `user_impersonation`

**API Permissions (for OBO):**
- Microsoft Graph / Files.Read.All (Delegated)
- Microsoft Graph / Files.ReadWrite.All (Delegated)
- Microsoft Graph / Sites.Read.All (Delegated)
- Microsoft Graph / Sites.ReadWrite.All (Delegated)

**Note:** Sprint 7B uses the SAME app registrations as Sprint 7A/8. No new Azure AD setup required.

---

## SDAP BFF API Endpoints

**Base URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`

### Upload File Endpoint

```http
PUT /api/obo/drives/{driveId}/upload?fileName={fileName}
Authorization: Bearer {msalToken}
Content-Type: application/octet-stream
Body: [raw file binary - NOT multipart/form-data]

Response 201 Created:
{
  "id": "01ABC...",
  "name": "Contract.pdf",
  "parentId": "01DEF...",
  "size": 2458624,
  "createdDateTime": "2025-10-06T...",
  "lastModifiedDateTime": "2025-10-06T...",
  "eTag": "\"{12345}\"",
  "isFolder": false,
  "webUrl": "https://..."
}
```

**Important:**
- Body is RAW file binary (NOT multipart/form-data)
- `fileName` passed as query parameter
- `driveId` is Graph API Drive ID (from Container)
- Returns `FileHandleDto` with all metadata

### Get Drive from Container (If Needed)

```http
GET /api/containers/{containerId}/drive
Authorization: Bearer {msalToken}

Response 200 OK:
{
  "id": "b!ABC...",  // Drive ID
  // other drive properties
}
```

---

## Dataverse Field Mappings

### sprk_document Entity Fields

| Dataverse Field | Source | Type | Required |
|----------------|--------|------|----------|
| **Primary & Identifiers** |
| `sprk_documentid` | Auto-generated | Guid | System |
| `sprk_documentname` | User input or file.name | String(100) | Yes |
| **File Metadata (from FileHandleDto)** |
| `sprk_graphitemid` | FileHandleDto.id | String | Yes |
| `sprk_graphdriveid` | Container lookup | String | Yes |
| `sprk_filename` | FileHandleDto.name | String | Yes |
| `sprk_filesize` | FileHandleDto.size | Integer | Yes |
| `sprk_createddatetime` | FileHandleDto.createdDateTime | DateTime | Yes |
| `sprk_lastmodifieddatetime` | FileHandleDto.lastModifiedDateTime | DateTime | Yes |
| `sprk_etag` | FileHandleDto.eTag | String | Yes |
| `sprk_parentfolderid` | FileHandleDto.parentId | String | Optional |
| `sprk_filepath` | FileHandleDto.webUrl | URL | Yes |
| **Additional Fields** |
| `sprk_hasfile` | Calculated (true if itemId exists) | Boolean | Yes |
| `sprk_mimetype` | file.type | String | Optional |
| `sprk_containerid` | Parent Matter lookup | Lookup | Yes |
| `sprk_matter` | Parent relationship | Lookup | Yes |

---

## Sprint 7B Task Breakdown

Sprint 7B is divided into 4 tasks:

### Task 1: Quick Create Setup & MSAL Integration
**Time:** 1-2 days
**Deliverables:**
- New PCF project created
- MSAL initialized (Sprint 8 pattern)
- React + Fluent UI v9 integrated
- Parent context retrieval working
- Basic form rendering

**[→ TASK-7B-1-QUICK-CREATE-SETUP.md](TASK-7B-1-QUICK-CREATE-SETUP.md)**

### Task 2: File Upload & SharePoint Embedded Integration (Multi-File with Adaptive Strategy)
**Time:** 2-3 days
**Deliverables:**
- File picker component (supports multiple files)
- FileUploadService created (single file)
- MultiFileUploadService created (adaptive strategy)
- Sync-parallel upload strategy (small/few files)
- Long-running batched upload strategy (large/many files)
- Progress indicators for both strategies
- Upload to SPE working with MSAL
- Dataverse record creation (one per file)
- Full file lifecycle tested with 1, 3, 5, and 10 files

**[→ TASK-7B-2-FILE-UPLOAD-SPE.md](TASK-7B-2-FILE-UPLOAD-SPE.md)**

### Task 3: Default Value Mappings & Configuration
**Time:** 1 day
**Deliverables:**
- Configurable field mappings
- Parent data auto-population
- Field validation
- Dynamic form rendering

**[→ TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md](TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md)**

### Task 4: Testing, Deployment & Sprint 7A Validation
**Time:** 1 day
**Deliverables:**
- Build and deploy Quick Create
- Create real test files
- **Return to Sprint 7A Task 3 (Manual Testing)**
- Validate Sprint 7A download/delete/replace
- Complete Sprint 7A compliance report

**[→ TASK-7B-4-TESTING-DEPLOYMENT.md](TASK-7B-4-TESTING-DEPLOYMENT.md)**

**Total Estimated Time:** 4-6 days

---

## Code Reuse Strategy

### DO Reuse from Sprint 7A/8

**Services (Copy & Reference):**
```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/
├── services/
│   ├── auth/
│   │   ├── msalConfig.ts           ← COPY to Quick Create
│   │   └── MsalAuthProvider.ts     ← COPY to Quick Create
│   ├── SdapApiClientFactory.ts     ← COPY to Quick Create
│   └── SdapApiClient.ts            ← COPY to Quick Create
├── utils/
│   └── logger.ts                   ← COPY to Quick Create
└── providers/
    └── ThemeProvider.ts            ← COPY to Quick Create
```

**Components (Reference for Patterns):**
- `ErrorBoundary.tsx` - Copy pattern
- React root setup pattern - Copy from `index.ts`

### DO NOT Reuse

**Grid-Specific Components:**
- `DatasetGrid.tsx` - Specific to grid control
- `CommandBar.tsx` - Specific to grid control
- `UniversalDatasetGridRoot.tsx` - Specific to grid control

**Grid-Specific Services:**
- `FileDownloadService.ts` - Not needed in Quick Create
- `FileDeleteService.ts` - Not needed in Quick Create
- `FileReplaceService.ts` - Not needed in Quick Create

### NEW for Quick Create

**Services:**
- `FileUploadService.ts` - Upload logic
- `ParentContextService.ts` - Parent data retrieval
- `DefaultValueMappingService.ts` - Field mapping logic

**Components:**
- `QuickCreateForm.tsx` - Main form component
- `FilePicker.tsx` - File selection UI
- `FormField.tsx` - Dynamic field renderer

---

## Success Criteria

### Sprint 7B Completion Checklist

- [ ] Universal Quick Create PCF created and deployed
- [ ] MSAL authentication working (Sprint 8 pattern)
- [ ] Single file upload to SharePoint Embedded working
- [ ] **Multi-file upload with adaptive strategy working**
  - [ ] Sync-parallel strategy (1-3 small files)
  - [ ] Long-running strategy (large/many files)
  - [ ] Progress indicators for both strategies
- [ ] Dataverse record creation working (one per file)
- [ ] Parent context retrieval working
- [ ] Default value mappings working
- [ ] Form closes and grid refreshes
- [ ] **Real test files created in SharePoint Embedded** (enables Sprint 7A testing)

### Sprint 7A Validation (Post-7B)

- [ ] Return to Sprint 7A Task 3 (Manual Testing)
- [ ] Test download with real files ✅
- [ ] Test delete with real files ✅
- [ ] Test replace with real files ✅
- [ ] Verify MSAL token caching (82x improvement) ✅
- [ ] Complete Sprint 7A compliance report ✅
- [ ] Update Universal Dataset Grid if needed

---

## Critical Reminders

### 🔴 MUST DO: Return to Sprint 7A After 7B

**After Sprint 7B Task 4 (Testing & Deployment):**

1. ✅ Quick Create deployed and working
2. ✅ Real test files created in SharePoint Embedded
3. → **RETURN TO SPRINT 7A TASK 3** (Manual Testing)
4. Test download/delete/replace with real files
5. Complete Sprint 7A MSAL compliance validation
6. Update Universal Dataset Grid documentation
7. Create final Sprint 7A compliance report

**Do NOT forget this step!** Sprint 7B creates the test data needed to validate Sprint 7A.

### 🔴 MUST DO: Use MSAL from Day One

- ✅ Initialize MSAL in `init()` method
- ✅ Use `SdapApiClientFactory.create()` for all API calls
- ✅ Handle race conditions (wait for MSAL if not ready)
- ✅ Use Sprint 8 error handling patterns
- ❌ DO NOT use PCF context tokens
- ❌ DO NOT create custom authentication

### 🔴 MUST DO: Follow Sprint 7A Patterns

- ✅ Service-oriented architecture
- ✅ Dependency injection
- ✅ ServiceResult return types
- ✅ Comprehensive logging
- ✅ React hooks patterns
- ✅ Fluent UI v9 components

### 🔴 MUST DO: Implement Adaptive Multi-File Upload

- ✅ Support multiple file selection (HTML `<input multiple>`)
- ✅ Implement strategy decision logic (sync vs long-running)
- ✅ Sync-parallel for small/few files (1-3 files, <10MB, <20MB total)
- ✅ Long-running batched for large/many files (>3 files OR >10MB OR >20MB total)
- ✅ Progress indicators for user feedback
- ✅ One Document record per file with shared metadata
- ✅ Plan for future large batch server processing (Service Bus queue)

---

## References

### Sprint Documentation
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - Sprint 7 overview
- [SPRINT-7A-COMPLIANCE-REVIEW-SUMMARY.md](SPRINT-7A-COMPLIANCE-REVIEW-SUMMARY.md) - 7A status
- [SPRINT-8-COMPLETION-REVIEW.md](../Sprint 8 - MSAL Integration/SPRINT-8-COMPLETION-REVIEW.md) - MSAL details

### Task Documents
- [TASK-7B-1-QUICK-CREATE-SETUP.md](TASK-7B-1-QUICK-CREATE-SETUP.md)
- [TASK-7B-2-FILE-UPLOAD-SPE.md](TASK-7B-2-FILE-UPLOAD-SPE.md)
- [TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md](TASK-7B-3-DEFAULT-VALUE-MAPPINGS.md)
- [TASK-7B-4-TESTING-DEPLOYMENT.md](TASK-7B-4-TESTING-DEPLOYMENT.md)

### Code References
- [UniversalDatasetGrid (Sprint 7A)](../../../../src/controls/UniversalDatasetGrid/)
- [MsalAuthProvider (Sprint 8)](../../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts)
- [SdapApiClient (Sprint 7A/8)](../../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)

---

**Document Owner:** Sprint 7B Implementation
**Created:** October 6, 2025
**Status:** 📋 Ready to Start
**Next Action:** Review Task 7B-1 (Quick Create Setup)
