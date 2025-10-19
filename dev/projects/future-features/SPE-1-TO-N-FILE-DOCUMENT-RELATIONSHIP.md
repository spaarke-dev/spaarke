# Future Feature: 1:N Relationship - Single SPE File to Multiple Document Records

**Status:** Future Sprint (Not Current Sprint)
**Priority:** Medium
**Complexity:** Medium
**Created:** 2025-10-09

---

## Business Scenario

**Use Case:** A single physical file (e.g., contract PDF, court filing) needs to be associated with multiple Dataverse Document records.

**Examples:**
- One settlement agreement linked to multiple matters
- One exhibit referenced in multiple cases
- One template used across multiple documents
- One contract shared between multiple clients

**Current State:** 1:1 relationship (each Document record has its own SPE file)
**Desired State:** 1:N relationship (multiple Document records can reference the same SPE file)

---

## Architectural Options Analysis

### Option 1: Single SPE File + Multiple Dataverse Records ⭐ **RECOMMENDED**

#### How It Works
```
SPE Container
  └── contract.pdf (driveId: XXX, itemId: YYY)
       ↑
       │ (referenced by)
       │
Dataverse sprk_document table:
  ├── Record 1: Matter A → driveId=XXX, itemId=YYY
  ├── Record 2: Matter B → driveId=XXX, itemId=YYY
  └── Record 3: Matter C → driveId=XXX, itemId=YYY
```

#### Pros
- ✅ **Cost-effective**: No duplicate file storage in SPE
- ✅ **Single source of truth**: One file in SPE
- ✅ **Simple implementation**: Works with current schema
- ✅ **Flexible metadata**: Each Document record can have different matter, document type, etc.
- ✅ **Storage efficient**: Critical for large files (contracts, depositions, etc.)
- ✅ **Version control**: One file version applies to all references

#### Cons
- ⚠️ **Deletion complexity**: Deleting file from one Document affects all
- ⚠️ **Business logic required**: Need "last record standing" deletion rules
- ⚠️ **User confusion**: Users need to understand file is shared

#### Current Schema Support
**Already Supported** - No schema changes needed:
- `sprk_graphdriveid` (text) - Stores drive/container ID
- `sprk_graphitemid` (text) - Stores file/item ID

Multiple records can store the **same** `graphdriveid` + `graphitemid` values.

---

### Option 2: SPE Custom List Column with Multi-Record Reference

#### How It Works
```
SPE Container (with custom column)
  └── contract.pdf
       └── CustomField: DataverseDocumentIds = "guid1,guid2,guid3"

Dataverse sprk_document table:
  ├── Record 1 (guid1): Matter A
  ├── Record 2 (guid2): Matter B
  └── Record 3 (guid3): Matter C
```

#### Pros
- ✅ **Bidirectional reference**: Can query SPE to find "which Dataverse records reference this"
- ✅ **Audit trail**: SPE knows its consumers

#### Cons
- ⚠️ **Complex sync**: Must update SPE whenever Dataverse records change
- ⚠️ **Tight coupling**: SPE becomes dependent on Dataverse
- ⚠️ **Performance**: Extra API calls to update SPE list item
- ⚠️ **Scalability**: Text field size limits

---

### Option 3: Junction Table (Many-to-Many)

#### How It Works
```
sprk_document ←→ sprk_DocumentFile ←→ SPE File

Schema:
  sprk_DocumentFile (new entity):
    - sprk_documentfileid (primary key)
    - sprk_documentid (lookup to sprk_document)
    - sprk_graphdriveid (text)
    - sprk_graphitemid (text)
    - sprk_filename (text)
    - sprk_filesize (whole number)
```

#### Pros
- ✅ **Proper many-to-many**: Database best practice
- ✅ **Clean separation**: File metadata separate from Document metadata
- ✅ **Easy queries**: "All documents for this file" or "All files for this document"
- ✅ **Normalization**: File data stored once in junction table

#### Cons
- ⚠️ **Schema changes**: New entity required
- ⚠️ **UI complexity**: Need to manage junction records
- ⚠️ **Migration effort**: Existing data needs migration
- ⚠️ **Learning curve**: Users need to understand junction concept

---

### Option 4: Copy File in SPE for Each Record

#### How It Works
```
SPE Container
  ├── contract_MatterA.pdf
  ├── contract_MatterB.pdf
  └── contract_MatterC.pdf (all identical content)
```

#### Pros
- ✅ **Complete independence**: Each record owns its file
- ✅ **Simple deletion**: No cascade concerns

#### Cons
- ❌ **Storage waste**: Duplicate files consume storage quota
- ❌ **Higher costs**: SPE storage is billed per GB
- ❌ **Version divergence**: Copies can get out of sync
- ❌ **Not scalable**: Large files (depositions, videos) = expensive

---

## Recommended Solution: Option 1

### Why Option 1?

1. **Legal industry alignment**: Documents are often shared across matters
2. **Cost management**: Large legal files (depositions, videos) shouldn't be duplicated
3. **Version control**: One source of truth for the file content
4. **Works with current schema**: No migration required
5. **Progressive enhancement**: Can add junction table later if needed

---

## Implementation Guide

### Phase 1: Core Functionality

#### 1.1 Upload File Once, Create Multiple Records

```typescript
// Service: MultiDocumentLinkService.ts

export interface LinkFileToMultipleDocumentsRequest {
    file: File;
    driveId: string;
    documents: DocumentMetadata[];  // Array of document metadata
}

export interface DocumentMetadata {
    matterId: string;
    matterName: string;
    documentType?: string;
    description?: string;
}

async linkFileToMultipleDocuments(
    request: LinkFileToMultipleDocumentsRequest
): Promise<ServiceResult<string[]>> {
    try {
        // Step 1: Upload file to SPE ONCE
        logger.info('MultiDocumentLinkService', 'Uploading file to SPE', {
            fileName: request.file.name,
            documentCount: request.documents.length
        });

        const fileMetadata = await this.fileUploadService.uploadFile({
            file: request.file,
            driveId: request.driveId
        });

        if (!fileMetadata.success || !fileMetadata.data) {
            throw new Error('File upload failed');
        }

        const spe = fileMetadata.data;

        // Step 2: Create multiple Dataverse Document records
        logger.info('MultiDocumentLinkService', 'Creating multiple Document records');

        const createdRecordIds: string[] = [];

        for (const doc of request.documents) {
            const recordData = {
                // Each record gets unique name
                sprk_documentname: `${spe.fileName} (${doc.matterName})`,

                // Each record links to different matter
                sprk_matter: doc.matterId,

                // All records share same SPE file reference
                sprk_graphdriveid: request.driveId,
                sprk_graphitemid: spe.driveItemId,
                sprk_filename: spe.fileName,
                sprk_filesize: spe.fileSize,

                // Optional: different metadata per record
                sprk_documenttype: doc.documentType,
                sprk_documentdescription: doc.description
            };

            const result = await this.context.webAPI.createRecord(
                'sprk_document',
                recordData
            );

            createdRecordIds.push(result.id);
        }

        logger.info('MultiDocumentLinkService', 'Created all Document records', {
            recordCount: createdRecordIds.length
        });

        return {
            success: true,
            data: createdRecordIds
        };

    } catch (error) {
        logger.error('MultiDocumentLinkService', 'Failed to link file', error);
        return {
            success: false,
            error: error instanceof Error ? error.message : 'Failed to link file'
        };
    }
}
```

#### 1.2 Link Existing SPE File to New Documents

```typescript
// Service: ExistingFileLinkService.ts

export interface LinkExistingFileRequest {
    driveId: string;
    itemId: string;
    documents: DocumentMetadata[];
}

async linkExistingFile(
    request: LinkExistingFileRequest
): Promise<ServiceResult<string[]>> {
    try {
        // Step 1: Get file metadata from SPE
        const fileInfo = await this.apiClient.getFileMetadata({
            driveId: request.driveId,
            itemId: request.itemId
        });

        if (!fileInfo.success || !fileInfo.data) {
            throw new Error('File not found in SPE');
        }

        const spe = fileInfo.data;

        // Step 2: Create Document records pointing to existing file
        const createdRecordIds: string[] = [];

        for (const doc of request.documents) {
            const recordData = {
                sprk_documentname: `${spe.fileName} (${doc.matterName})`,
                sprk_matter: doc.matterId,
                sprk_graphdriveid: request.driveId,
                sprk_graphitemid: request.itemId,
                sprk_filename: spe.fileName,
                sprk_filesize: spe.fileSize,
                sprk_documenttype: doc.documentType,
                sprk_documentdescription: doc.description
            };

            const result = await this.context.webAPI.createRecord(
                'sprk_document',
                recordData
            );

            createdRecordIds.push(result.id);
        }

        return {
            success: true,
            data: createdRecordIds
        };

    } catch (error) {
        logger.error('ExistingFileLinkService', 'Failed to link existing file', error);
        return {
            success: false,
            error: error instanceof Error ? error.message : 'Failed to link file'
        };
    }
}
```

---

### Phase 2: Smart Deletion Logic

#### 2.1 Check Reference Count Before Deletion

```typescript
// Service: SafeFileDeleteService.ts

async deleteDocument(documentId: string): Promise<ServiceResult<void>> {
    try {
        // Step 1: Get the document record
        const doc = await this.context.webAPI.retrieveRecord(
            'sprk_document',
            documentId,
            '?$select=sprk_graphdriveid,sprk_graphitemid,sprk_filename'
        );

        // Step 2: Find other documents referencing the same file
        const fetchXml = `
            <fetch>
                <entity name="sprk_document">
                    <attribute name="sprk_documentid"/>
                    <filter type="and">
                        <condition attribute="sprk_graphitemid" operator="eq" value="${doc.sprk_graphitemid}"/>
                        <condition attribute="sprk_documentid" operator="ne" value="${documentId}"/>
                    </filter>
                </entity>
            </fetch>
        `;

        const otherDocs = await this.context.webAPI.retrieveMultipleRecords(
            'sprk_document',
            `?fetchXml=${encodeURIComponent(fetchXml)}`
        );

        // Step 3: Delete based on reference count
        if (otherDocs.entities.length > 0) {
            // Other records exist - ONLY delete Dataverse record
            await this.context.webAPI.deleteRecord('sprk_document', documentId);

            logger.info('SafeFileDeleteService', 'Document unlinked (file preserved)', {
                documentId,
                remainingReferences: otherDocs.entities.length,
                fileName: doc.sprk_filename
            });

            return {
                success: true,
                message: `Document unlinked. File "${doc.sprk_filename}" still exists (${otherDocs.entities.length} other references)`
            };

        } else {
            // Last record - delete BOTH Dataverse record AND SPE file
            await this.fileDeleteService.deleteFile({
                driveId: doc.sprk_graphdriveid,
                itemId: doc.sprk_graphitemid
            });

            await this.context.webAPI.deleteRecord('sprk_document', documentId);

            logger.info('SafeFileDeleteService', 'Document and file deleted', {
                documentId,
                fileName: doc.sprk_filename
            });

            return {
                success: true,
                message: `Document and file "${doc.sprk_filename}" deleted (last reference)`
            };
        }

    } catch (error) {
        logger.error('SafeFileDeleteService', 'Delete failed', error);
        return {
            success: false,
            error: error instanceof Error ? error.message : 'Delete failed'
        };
    }
}
```

#### 2.2 User Confirmation Dialog

```typescript
// Component: DeleteDocumentDialog.tsx

async showDeleteConfirmation(documentId: string): Promise<boolean> {
    // Check reference count
    const refCount = await this.getReferenceCount(documentId);

    let message: string;
    let warningLevel: 'info' | 'warning' | 'error';

    if (refCount > 1) {
        message = `This document references a file shared with ${refCount - 1} other document(s).\n\n` +
                  `The file will NOT be deleted - only this document record will be removed.`;
        warningLevel = 'info';
    } else {
        message = `This is the ONLY document referencing this file.\n\n` +
                  `Both the document record AND the file will be PERMANENTLY deleted.`;
        warningLevel = 'warning';
    }

    return await showConfirmDialog({
        title: 'Delete Document',
        message,
        warningLevel,
        confirmText: 'Delete',
        cancelText: 'Cancel'
    });
}
```

---

### Phase 3: UI Enhancements

#### 3.1 Quick Create: Multi-Matter Selection

```typescript
// Component: MultiMatterFileUpload.tsx

export const MultiMatterFileUpload: React.FC = () => {
    const [selectedFile, setSelectedFile] = useState<File | null>(null);
    const [selectedMatters, setSelectedMatters] = useState<Matter[]>([]);

    const handleUpload = async () => {
        if (!selectedFile || selectedMatters.length === 0) return;

        const result = await multiDocumentLinkService.linkFileToMultipleDocuments({
            file: selectedFile,
            driveId: containerDriveId,
            documents: selectedMatters.map(matter => ({
                matterId: matter.sprk_matterid,
                matterName: matter.sprk_mattername,
                documentType: documentType,
                description: description
            }))
        });

        if (result.success) {
            showSuccess(`File uploaded and linked to ${selectedMatters.length} matters`);
            closeForm();
        } else {
            showError(result.error || 'Upload failed');
        }
    };

    return (
        <Stack tokens={{ childrenGap: 16 }}>
            <FileUploadField
                label="Select File"
                onChange={setSelectedFile}
            />

            <MatterPickerField
                label="Select Matters"
                multiple={true}
                onChange={setSelectedMatters}
                description="Select one or more matters to link this file"
            />

            <Text>
                {selectedMatters.length > 0 &&
                    `File will be linked to ${selectedMatters.length} matter(s)`
                }
            </Text>

            <PrimaryButton
                text="Upload & Link"
                onClick={handleUpload}
                disabled={!selectedFile || selectedMatters.length === 0}
            />
        </Stack>
    );
};
```

#### 3.2 Dataset Grid: Show Reference Count

```typescript
// Component: DocumentGrid.tsx

// Add calculated column showing reference count
const columns = [
    { key: 'name', name: 'Document Name' },
    { key: 'matter', name: 'Matter' },
    { key: 'refCount', name: 'Shared With', onRender: (item) => {
        if (item.referenceCount > 1) {
            return (
                <Tooltip content={`This file is referenced by ${item.referenceCount} documents`}>
                    <Icon iconName="Share" /> {item.referenceCount - 1} other(s)
                </Tooltip>
            );
        }
        return <Text>—</Text>;
    }}
];

// Load reference counts on grid load
async loadReferenceCount(document: Document): Promise<number> {
    const fetchXml = `
        <fetch aggregate="true">
            <entity name="sprk_document">
                <attribute name="sprk_documentid" aggregate="count" alias="count"/>
                <filter>
                    <condition attribute="sprk_graphitemid" operator="eq" value="${document.sprk_graphitemid}"/>
                </filter>
            </entity>
        </fetch>
    `;

    const result = await context.webAPI.retrieveMultipleRecords(
        'sprk_document',
        `?fetchXml=${encodeURIComponent(fetchXml)}`
    );

    return parseInt(result.entities[0].count) || 0;
}
```

#### 3.3 Link Existing File Button

```typescript
// Component: LinkExistingFileButton.tsx

const handleLinkExistingFile = async () => {
    // Step 1: Show file picker from SPE
    const selectedFile = await showSpeFilePicker({
        driveId: containerDriveId,
        title: 'Select File to Link',
        allowMultiple: false
    });

    if (!selectedFile) return;

    // Step 2: Show matter picker
    const selectedMatters = await showMatterPicker({
        title: 'Select Matters',
        allowMultiple: true,
        excludeMattersWithFile: selectedFile.itemId  // Optional: don't show matters already linked
    });

    if (!selectedMatters || selectedMatters.length === 0) return;

    // Step 3: Create Document records
    const result = await existingFileLinkService.linkExistingFile({
        driveId: containerDriveId,
        itemId: selectedFile.itemId,
        documents: selectedMatters.map(matter => ({
            matterId: matter.sprk_matterid,
            matterName: matter.sprk_mattername
        }))
    });

    if (result.success) {
        showSuccess(`File "${selectedFile.name}" linked to ${selectedMatters.length} matters`);
        refreshGrid();
    } else {
        showError(result.error || 'Link failed');
    }
};
```

---

## Data Model

### Current Schema (No Changes Required)

```
sprk_document
├── sprk_documentid (primary key)
├── sprk_documentname (text, required)
├── sprk_matter (lookup to sprk_matter)
├── sprk_graphdriveid (text) ← Same value for shared files
├── sprk_graphitemid (text) ← Same value for shared files
├── sprk_filename (text)
├── sprk_filesize (whole number)
├── sprk_documenttype (choice, optional)
└── sprk_documentdescription (text, optional)
```

### Example Data

**SPE Container:**
```
Drive ID: b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
  └── settlement-agreement.pdf
       Item ID: 01LBYCMX3XR5DW62JZ2NDI2I6IU553FAEL
```

**Dataverse sprk_document Records:**
```
Record 1:
  sprk_documentid: guid-1
  sprk_documentname: "settlement-agreement.pdf (Matter A)"
  sprk_matter: matter-a-guid
  sprk_graphdriveid: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
  sprk_graphitemid: "01LBYCMX3XR5DW62JZ2NDI2I6IU553FAEL"

Record 2:
  sprk_documentid: guid-2
  sprk_documentname: "settlement-agreement.pdf (Matter B)"
  sprk_matter: matter-b-guid
  sprk_graphdriveid: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"  ← Same
  sprk_graphitemid: "01LBYCMX3XR5DW62JZ2NDI2I6IU553FAEL"  ← Same

Record 3:
  sprk_documentid: guid-3
  sprk_documentname: "settlement-agreement.pdf (Matter C)"
  sprk_matter: matter-c-guid
  sprk_graphdriveid: "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"  ← Same
  sprk_graphitemid: "01LBYCMX3XR5DW62JZ2NDI2I6IU553FAEL"  ← Same
```

---

## Queries & Reports

### Find All Documents Referencing a File

```xml
<fetch>
    <entity name="sprk_document">
        <attribute name="sprk_documentid"/>
        <attribute name="sprk_documentname"/>
        <attribute name="sprk_matter"/>
        <filter>
            <condition attribute="sprk_graphitemid" operator="eq" value="01LBYCMX3XR5DW62JZ2NDI2I6IU553FAEL"/>
        </filter>
        <link-entity name="sprk_matter" from="sprk_matterid" to="sprk_matter" alias="matter">
            <attribute name="sprk_mattername"/>
        </link-entity>
    </entity>
</fetch>
```

### Find Shared Files (Referenced by Multiple Documents)

```xml
<fetch aggregate="true">
    <entity name="sprk_document">
        <attribute name="sprk_graphitemid" groupby="true" alias="itemId"/>
        <attribute name="sprk_filename" groupby="true" alias="fileName"/>
        <attribute name="sprk_documentid" aggregate="count" alias="refCount"/>
        <filter>
            <condition attribute="sprk_graphitemid" operator="not-null"/>
        </filter>
        <order alias="refCount" descending="true"/>
    </entity>
</fetch>
```

### Find Orphaned Files (SPE file exists but no Dataverse records)

**This requires cross-system query:**
1. Get all files from SPE container
2. Get all unique `sprk_graphitemid` values from Dataverse
3. Compare and find mismatches

---

## Testing Strategy

### Test Case 1: Upload File, Link to Multiple Matters

**Steps:**
1. Open Quick Create form
2. Select file: `contract.pdf`
3. Select matters: Matter A, Matter B, Matter C
4. Click "Upload & Link"

**Expected:**
- ✅ File uploaded to SPE once
- ✅ 3 Document records created in Dataverse
- ✅ All 3 records have same `sprk_graphitemid`
- ✅ Each record linked to different matter

### Test Case 2: Delete Document (Not Last Reference)

**Steps:**
1. Delete Document record for Matter A
2. Check remaining records (Matter B, Matter C)
3. Check SPE container

**Expected:**
- ✅ Document record deleted
- ✅ File still exists in SPE
- ✅ Other Document records (Matter B, C) unchanged

### Test Case 3: Delete Document (Last Reference)

**Steps:**
1. Delete Document records for Matter B and Matter C
2. Check SPE container

**Expected:**
- ✅ All Document records deleted
- ✅ File deleted from SPE
- ✅ No orphaned files

### Test Case 4: Link Existing File to New Matter

**Steps:**
1. Click "Link Existing File"
2. Select existing file from SPE
3. Select new matter: Matter D
4. Click "Link"

**Expected:**
- ✅ No new file upload
- ✅ New Document record created for Matter D
- ✅ New record references existing `sprk_graphitemid`

---

## Performance Considerations

### Database Queries
- **Reference count checks**: Add index on `sprk_graphitemid` for fast lookups
- **Aggregate queries**: Use FetchXML aggregate for "shared files" reports

### SPE API Calls
- **Upload once**: Eliminates duplicate upload overhead
- **Batch linking**: Create multiple Dataverse records in parallel (Promise.all)

### UI Responsiveness
- **Lazy load reference counts**: Don't calculate on every grid load (use on-demand)
- **Background deletion**: Show confirmation immediately, delete async

---

## Migration Strategy (If Needed)

If you have existing 1:1 relationships and want to consolidate:

```typescript
// Migration script: ConsolidateDuplicateFiles.ts

async consolidateDuplicateFiles() {
    // Find files with identical content (same filename + size)
    const duplicates = await this.findDuplicateFiles();

    for (const group of duplicates) {
        // Keep first file, update others to reference it
        const primaryFile = group[0];

        for (let i = 1; i < group.length; i++) {
            const duplicate = group[i];

            // Update Dataverse record to point to primary file
            await this.context.webAPI.updateRecord('sprk_document', duplicate.documentId, {
                sprk_graphdriveid: primaryFile.driveId,
                sprk_graphitemid: primaryFile.itemId
            });

            // Delete duplicate file from SPE
            await this.fileDeleteService.deleteFile({
                driveId: duplicate.driveId,
                itemId: duplicate.itemId
            });
        }
    }
}
```

---

## User Documentation (Draft)

### For End Users

**Linking Files to Multiple Matters**

1. **Upload a new file and link to multiple matters:**
   - Click "Upload File" on any matter
   - Select file from your computer
   - Check boxes for additional matters to link
   - Click "Upload & Link to X Matters"

2. **Link an existing file to another matter:**
   - Click "Link Existing File"
   - Browse files in the container
   - Select file to link
   - Choose matter(s) to link to
   - Click "Link"

3. **Understanding shared files:**
   - Look for the "Share" icon in the document list
   - Hover to see how many other matters reference this file
   - Deleting a document doesn't delete the file if other matters use it

4. **Deleting shared files:**
   - System will warn you if file is shared
   - "Unlink" removes the document record only
   - "Delete" (last reference) removes both record and file

---

## Context from Chat Session

### Original Question
> "one of our future features is 1:N for files to Document records where we can upload a file and associate it to multiple existing Documents. This is not for this sprint, but while we are looking at SPE how would we support that"

### Key Insights from Discussion
1. **No schema changes needed**: Current `sprk_graphdriveid` + `sprk_graphitemid` fields already support this
2. **Cost driver**: Legal files (depositions, videos) are large - duplicating them is expensive
3. **Version control**: One source of truth prevents divergence
4. **Microsoft Graph alignment**: Field names follow Microsoft's conventions

### Related Current Sprint Work
- ✅ BFF API registration with container type (certificate auth)
- ✅ Multi-file upload via Quick Create
- ✅ Field mapping: `sprk_graphdriveid` + `sprk_graphitemid` storage
- ✅ OBO flow working for file uploads

### Technical Foundations Already in Place
- MSAL authentication for BFF API
- FileUploadService handling SPE uploads
- DataverseRecordService creating records
- MultiFileUploadService orchestrating uploads + record creation

---

## Next Steps for Implementation

### Sprint Planning Checklist

**Pre-Implementation:**
- [ ] User story refinement with stakeholders
- [ ] UI/UX mockups for multi-matter selection
- [ ] Define deletion business rules (confirm vs auto-delete)
- [ ] Performance testing with large file counts

**Implementation Order:**
1. [ ] Backend: SafeFileDeleteService with reference counting
2. [ ] Backend: MultiDocumentLinkService for linking
3. [ ] UI: Multi-matter picker component
4. [ ] UI: Reference count indicator in grid
5. [ ] UI: Delete confirmation dialogs
6. [ ] Testing: End-to-end scenarios
7. [ ] Documentation: User guide and admin guide

**Estimated Complexity:** 5-8 story points (Medium complexity)

---

## Questions for Future Refinement

1. **Business Rules:**
   - Should users be able to "unlink" without deleting the file?
   - Who can delete the actual file (last reference)?
   - Should we track "primary" document for a shared file?

2. **UI/UX:**
   - How to make it obvious a file is shared?
   - Should we show "file usage" report in the UI?
   - Bulk operations: link/unlink multiple files at once?

3. **Permissions:**
   - Can users link files across matters they don't own?
   - Should file deletion require elevated permissions?

4. **Reporting:**
   - Dashboard showing "most referenced files"?
   - Audit log for link/unlink operations?

---

**Document Version:** 1.0
**Last Updated:** 2025-10-09
**Related Sprint:** Future Feature (TBD)
**Dependencies:** Current sprint file upload functionality
