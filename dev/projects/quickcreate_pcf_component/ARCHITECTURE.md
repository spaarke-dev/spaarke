# Architecture: Universal Document Upload PCF

## System Overview

The Universal Document Upload PCF control enables users to upload multiple files to SharePoint Embedded (SPE) and create corresponding Document records in Dataverse, linked to any parent entity type.

**Key Design Principle:** Parent entity agnostic - works with Matter, Project, Invoice, Account, Contact, and future entity types through configuration.

---

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     Parent Entity Form                          │
│                   (Matter / Project / Invoice / etc.)           │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  Documents Subgrid                                         │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  [+ New Document] ← Custom Command Button            │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  Document 1.pdf                                       │ │ │
│  │  │  Document 2.docx                                      │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              ↓ (User clicks button)
                              ↓
         Xrm.Navigation.navigateTo(customPage, dialogOptions)
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│          Custom Page Dialog: sprk_DocumentUploadDialog          │
│                                                                  │
│  ┌───────────────────────────────────────────────────────────┐ │
│  │  PCF Control: UniversalDocumentUploadPCF                   │ │
│  │                                                             │ │
│  │  Input Parameters (from navigation):                       │ │
│  │  • parentEntityName: "sprk_matter"                         │ │
│  │  • parentRecordId: "{GUID}"                                │ │
│  │  • containerId: "{SPE-CONTAINER-ID}"                       │ │
│  │  • parentDisplayName: "Matter #12345"                      │ │
│  │                                                             │ │
│  │  ┌─────────────────────────────────────────────────────┐  │ │
│  │  │  Upload File                                         │  │ │
│  │  │  [Add File ↑]                                        │  │ │
│  │  ├─────────────────────────────────────────────────────┤  │ │
│  │  │  Profile                                             │  │ │
│  │  │  Document Description: [_______________]             │  │ │
│  │  ├─────────────────────────────────────────────────────┤  │ │
│  │  │  Progress: 3 of 10 files                            │  │ │
│  │  │  ████████░░░░░░░░░░░░ 30%                            │  │ │
│  │  └─────────────────────────────────────────────────────┘  │ │
│  │                                                             │ │
│  │  [Upload & Create]  [Cancel]                               │ │
│  │                                                             │ │
│  │  v2.0.0.0 - Build 2025-01-10                               │ │
│  └───────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                              ↓ (User clicks Upload & Create)
                              ↓
                    ┌─────────────────────┐
                    │   Phase 1: Upload   │
                    │   Files to SPE      │
                    └─────────────────────┘
                              ↓
              SdapApiClient.uploadFile() × 10 (parallel)
                              ↓
                    OAuth2 OBO Flow → Graph API
                              ↓
                SharePoint Embedded Container
              (Files stored, metadata returned)
                              ↓
                    ┌─────────────────────┐
                    │  Phase 2: Create    │
                    │  Dataverse Records  │
                    └─────────────────────┘
                              ↓
         DocumentRecordService.createDocuments() × 10 (sequential)
                              ↓
              Xrm.WebApi.createRecord("sprk_document", data) × 10
                              ↓
                    Dataverse Web API
          (Document records created with parent lookup)
                              ↓
                    ┌─────────────────────┐
                    │  Dialog Closes      │
                    │  Subgrid Refreshes  │
                    └─────────────────────┘
```

---

## Component Architecture

### Layer 1: Presentation (UI Components)

**Fluent UI v9 React Components**

```
DocumentUploadForm.tsx (Main container)
  ├── FileSelectionField.tsx (File input + validation)
  ├── DocumentMetadataFields.tsx (Description, etc.)
  ├── UploadProgressBar.tsx (Progress display)
  └── ErrorMessageList.tsx (Error handling)
```

**Styling:**
- `makeStyles()` hook for component styles
- Design tokens from `@fluentui/react-components`
- CSS Flexbox for layout (no Stack component)

---

### Layer 2: Control Logic (PCF Framework)

**UniversalDocumentUploadPCF.ts**

```typescript
class UniversalDocumentUploadPCF implements ComponentFramework.StandardControl {
    // Input Parameters (from Custom Page)
    private parentEntityName: string;
    private parentRecordId: string;
    private containerId: string;
    private parentDisplayName: string;

    // Services
    private fileUploadService: FileUploadService;
    private documentRecordService: DocumentRecordService;
    private msalAuthProvider: MsalAuthProvider;

    // State
    private selectedFiles: File[];
    private uploadProgress: UploadProgress;
    private errors: UploadError[];

    // Lifecycle
    public init(context): void
    public updateView(context): void
    public destroy(): void

    // Main Workflow
    private async handleUploadAndCreate(): Promise<void>
    private async uploadFilesToSPE(): Promise<UploadedFile[]>
    private async createDataverseRecords(): Promise<CreateResult[]>
    private closeDialog(): void
}
```

---

### Layer 3: Business Logic (Services)

#### FileUploadService.ts (KEEP AS-IS)
```typescript
class FileUploadService {
    async uploadFile(request: FileUploadRequest): Promise<ServiceResult<SpeFileMetadata>>
}
```
**Responsibility:** Upload single file to SPE via BFF API
**Uses:** SdapApiClient, MSAL authentication

#### DocumentRecordService.ts (NEW)
```typescript
class DocumentRecordService {
    async createDocuments(
        files: UploadedFileMetadata[],
        parentContext: ParentContext,
        formData: FormData
    ): Promise<CreateResult[]>

    private buildRecordPayload(
        file: UploadedFileMetadata,
        config: EntityDocumentConfig,
        parentContext: ParentContext
    ): Record<string, unknown>

    private getEntityConfig(entityName: string): EntityDocumentConfig
}
```
**Responsibility:** Create Document records using Xrm.WebApi
**Key Method:** `Xrm.WebApi.createRecord()` - no Quick Create limitations

#### SdapApiClient.ts (KEEP AS-IS)
```typescript
class SdapApiClient {
    async uploadFile(request: FileUploadRequest): Promise<SpeFileMetadata>
}
```
**Responsibility:** HTTP client for BFF API
**Uses:** Fetch API, MSAL token provider

---

### Layer 4: Configuration & Types

#### EntityDocumentConfig.ts (NEW)
```typescript
export interface EntityDocumentConfig {
    entityName: string;           // 'sprk_matter'
    lookupFieldName: string;      // 'sprk_matter' (on Document)
    containerIdField: string;     // 'sprk_containerid' (on parent)
    displayNameField: string;     // 'sprk_matternumber' (on parent)
    entitySetName: string;        // 'sprk_matters' (OData)
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': { /* ... */ },
    'sprk_project': { /* ... */ },
    'sprk_invoice': { /* ... */ },
    'account': { /* ... */ },
    'contact': { /* ... */ }
};
```

#### types/index.ts (EXTEND EXISTING)
```typescript
export interface ParentContext {
    entityName: string;
    recordId: string;
    displayName: string;
    containerId: string;
}

export interface UploadedFileMetadata extends SpeFileMetadata {
    localFileName: string;
    uploadTimestamp: Date;
}

export interface CreateResult {
    success: boolean;
    recordId?: string;
    fileName: string;
    error?: string;
}
```

---

## Data Flow

### Phase 1: File Upload to SharePoint Embedded

```typescript
// Sequential flow
User selects files
  → FileSelectionField validates (size, type, count)
  → User clicks "Upload & Create"
  → handleUploadAndCreate() triggered

// Parallel upload
files.forEach(file =>
  FileUploadService.uploadFile(file)
    → SdapApiClient.uploadFile()
      → MsalAuthProvider.getAccessToken()
      → fetch("PUT /api/obo/containers/{id}/files/{name}")
        → BFF API (OAuth2 OBO)
          → Microsoft Graph API
            → SharePoint Embedded
)

// Results
Promise.allSettled(uploads) → UploadedFileMetadata[]
```

**Success Criteria:**
- All files uploaded to SPE
- Each file has `driveId` and `itemId`
- Progress bar updated during uploads

---

### Phase 2: Create Dataverse Records

```typescript
// Sequential creation (one at a time)
uploadedFiles.forEach(async file =>
  DocumentRecordService.createDocuments([file])
    → buildRecordPayload(file, config, parentContext)
      → {
          sprk_documentname: file.name,
          sprk_filename: file.name,
          sprk_graphdriveid: containerId,
          sprk_graphitemid: file.itemId,
          sprk_filesize: file.size,
          sprk_description: formData.description,
          [config.lookupFieldName]: null,
          [`${config.lookupFieldName}@odata.bind`]:
            `/${config.entitySetName}(${parentRecordId})`
        }
    → Xrm.WebApi.createRecord("sprk_document", payload)
      → Dataverse Web API
        → POST /api/data/v9.0/sprk_documents
)

// Results
CreateResult[] (success/failure per file)
```

**Success Criteria:**
- Each uploaded file has corresponding Document record
- Lookup to parent entity is correct
- SPE metadata stored correctly

---

## Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────┐
│  Parent Entities (any of):                                  │
│  • sprk_matter                                              │
│  • sprk_project                                             │
│  • sprk_invoice                                             │
│  • account                                                  │
│  • contact                                                  │
│                                                              │
│  Common Fields:                                             │
│  • sprk_containerid (Single Line Text) - SPE Container ID   │
│  • [display name field] - Entity-specific primary name      │
└─────────────────────────────────────────────────────────────┘
                           │
                           │ 1:N Relationship
                           ↓
┌─────────────────────────────────────────────────────────────┐
│  sprk_document (Document Entity)                            │
│                                                              │
│  Lookup Fields (ONE populated per record):                  │
│  • sprk_matter (Lookup to sprk_matter)                      │
│  • sprk_project (Lookup to sprk_project)                    │
│  • sprk_invoice (Lookup to sprk_invoice)                    │
│  • sprk_account (Lookup to account)                         │
│  • sprk_contact (Lookup to contact)                         │
│                                                              │
│  SPE Metadata:                                              │
│  • sprk_documentname (Single Line Text) - Display name      │
│  • sprk_filename (Single Line Text) - Original file name    │
│  • sprk_graphdriveid (Single Line Text) - SPE Container ID  │
│  • sprk_graphitemid (Single Line Text) - SPE Item ID        │
│  • sprk_filesize (Whole Number) - File size in bytes        │
│                                                              │
│  User-Editable:                                             │
│  • sprk_description (Multi-line Text) - Document notes      │
│  • ownerid (Lookup to systemuser) - Document owner          │
└─────────────────────────────────────────────────────────────┘
```

**Schema Notes:**
- Only ONE lookup field is populated per Document record
- Lookup field is determined by `parentEntityName` parameter
- Future entity types can be added by:
  1. Adding lookup field to Document entity
  2. Adding config to `ENTITY_DOCUMENT_CONFIGS`
  3. Deploying command button to that entity's subgrid

---

## Security Architecture

### Authentication Flow

```
User opens dialog from Parent form
  → Custom Page receives parentRecordId
  → PCF Control initializes
    → MsalAuthProvider.initialize()
      → PublicClientApplication.loginPopup() (if needed)
      → acquireTokenSilent() (for SPE file upload)
    → User delegated token obtained

User clicks "Upload & Create"
  → File Upload (uses user token)
    → SdapApiClient sends token to BFF API
      → BFF API exchanges token (OAuth2 OBO flow)
        → Microsoft Graph API (user context)
          → SharePoint Embedded

  → Record Creation (uses user context)
    → Xrm.WebApi.createRecord()
      → Uses current user's Dataverse session
      → Dataverse Web API (user permissions)
```

**Security Checks:**
1. User must have Dataverse Create permission on `sprk_document`
2. User must have access to parent record (Matter/Project/etc.)
3. User must have SPE permissions via OAuth2 scope
4. BFF API validates user token before file upload

---

## Error Handling Strategy

### Validation Errors (Pre-Upload)

```typescript
// File count validation
if (files.length > 10) {
    showError("Maximum 10 files allowed. Please select fewer files.");
    return;
}

// File size validation (per file)
const oversizedFiles = files.filter(f => f.size > 10 * 1024 * 1024);
if (oversizedFiles.length > 0) {
    showError(`Files exceed 10MB limit: ${oversizedFiles.map(f => f.name).join(', ')}`);
    return;
}

// Total size validation
const totalSize = files.reduce((sum, f) => sum + f.size, 0);
if (totalSize > 100 * 1024 * 1024) {
    showError(`Total size ${(totalSize / 1024 / 1024).toFixed(1)}MB exceeds 100MB limit.`);
    return;
}

// File type validation
const blockedExtensions = ['.exe', '.dll', '.bat', '.cmd', '.ps1', '.vbs', '.js', '.jar'];
const dangerousFiles = files.filter(f =>
    blockedExtensions.some(ext => f.name.toLowerCase().endsWith(ext))
);
if (dangerousFiles.length > 0) {
    showError(`File types not allowed: ${dangerousFiles.map(f => f.name).join(', ')}`);
    return;
}
```

### Upload Errors (Phase 1)

```typescript
// Parallel upload with error capture
const uploadResults = await Promise.allSettled(
    files.map(file => fileUploadService.uploadFile({ file, driveId: containerId }))
);

const successfulUploads: UploadedFileMetadata[] = [];
const failedUploads: { file: File; error: string }[] = [];

uploadResults.forEach((result, index) => {
    if (result.status === 'fulfilled' && result.value.success) {
        successfulUploads.push(result.value.data);
    } else {
        failedUploads.push({
            file: files[index],
            error: result.status === 'rejected' ? result.reason.message : result.value.error
        });
    }
});

// Decision point
if (failedUploads.length > 0) {
    if (successfulUploads.length === 0) {
        // Total failure - abort
        showError("All uploads failed. Please try again.");
        return;
    } else {
        // Partial failure - ask user
        const proceed = await confirmDialog(
            `${successfulUploads.length} files uploaded successfully.\n` +
            `${failedUploads.length} files failed.\n\n` +
            `Continue creating records for successful uploads?`
        );
        if (!proceed) return;
    }
}
```

### Record Creation Errors (Phase 2)

```typescript
// Sequential creation with error capture
const createResults: CreateResult[] = [];

for (const file of successfulUploads) {
    try {
        const recordId = await documentRecordService.createDocument(file, parentContext);
        createResults.push({
            success: true,
            recordId,
            fileName: file.name
        });
    } catch (error) {
        createResults.push({
            success: false,
            fileName: file.name,
            error: error.message
        });
    }
}

// Summary
const successCount = createResults.filter(r => r.success).length;
const failureCount = createResults.filter(r => !r.success).length;

if (failureCount > 0) {
    showSummary({
        success: successCount,
        failed: failureCount,
        errors: createResults.filter(r => !r.success)
    });
}
```

---

## Performance Considerations

### File Upload (Phase 1)
- **Parallel uploads**: 10 files upload simultaneously
- **Expected time**: ~5-10 seconds for 100MB total (depends on network)
- **Progress tracking**: Real-time updates per file

### Record Creation (Phase 2)
- **Sequential creation**: One record at a time
- **Expected time**: ~1-2 seconds per record (Dataverse API latency)
- **Total**: 10-20 seconds for 10 records
- **Rationale**: Simplicity over speed (10 records is acceptable)

### Future Optimization (If Needed)
- Batch requests via `$batch` endpoint (create multiple records in one HTTP call)
- Parallel record creation (if Xrm.WebApi supports it reliably)

---

## Deployment Architecture

### Solution Package Structure

```
SpaarkeDocumentUpload_2_0_0_0.zip
│
├── Controls/
│   └── sprk_UniversalDocumentUploadPCF.xml (version 2.0.0.0)
│
├── CustomPages/
│   └── sprk_DocumentUploadDialog.json (version 2.0.0.0)
│
├── WebResources/
│   ├── sprk_subgrid_commands.js (version 2.0.0.0)
│   └── sprk_entity_document_config.json (configuration)
│
├── RibbonCustomizations/
│   └── sprk_document_upload_button.xml
│       (Command buttons for each entity)
│
└── solution.xml (version 2.0.0.0)
```

### Deployment Steps
1. Import solution into target environment
2. Publish all customizations
3. For each entity (Matter, Project, Invoice, Account, Contact):
   - Add `sprk_containerid` field (if not exists)
   - Add Documents subgrid to form
   - Add command button to subgrid
4. Verify version in dialog footer

---

## Testing Strategy

### Unit Testing (TypeScript)
- File validation logic
- Payload building logic
- Configuration resolution

### Integration Testing (Manual)
1. Upload 1 file → Verify 1 record created
2. Upload 10 files → Verify 10 records created
3. Upload 11 files → Verify validation error
4. Upload file > 10MB → Verify validation error
5. Upload .exe file → Verify blocked
6. Upload with partial failure → Verify error handling
7. Test on each entity type → Verify lookup correctness

### User Acceptance Testing
- Real Matter/Project/Invoice/Account/Contact records
- Real SPE container
- Production-like data volumes

---

## Monitoring & Logging

### Client-Side Logging
```typescript
logger.info('DocumentUpload', 'Upload started', { fileCount, totalSize });
logger.info('DocumentUpload', 'Phase 1 complete', { successCount, failureCount });
logger.info('DocumentUpload', 'Phase 2 complete', { recordsCreated });
logger.error('DocumentUpload', 'Upload failed', { error, fileName });
```

### Server-Side Monitoring (BFF API)
- Track upload success rate
- Monitor OBO token exchange failures
- Alert on repeated 403/404 errors

---

## Future Enhancements

### Phase 2 (Future)
- Drag-and-drop file selection
- File preview thumbnails
- Document version history
- Batch upload to multiple parent records

### Phase 3 (Future)
- Large file support (>100MB with chunked upload)
- Unlimited file count (streaming upload)
- Background upload (close dialog, upload continues)
- Duplicate detection (prevent re-uploading same file)

---

**Next Step:** Review [ADR-COMPLIANCE.md](./ADR-COMPLIANCE.md) for architectural decision records.
