# Complete Implementation Prompt for Claude Code

Save this as: `PCF-IMPLEMENTATION-PROMPT.md`

```markdown
# PCF Control Implementation Instructions

## 🎯 Project Overview

You are implementing a **Universal Document Upload PCF Control** that:
- Uploads multiple files to SharePoint Embedded
- Creates corresponding Dataverse Document records
- Works with ANY parent entity (Matter, Project, Account, Contact, etc.)
- Runs in a Custom Page dialog (NOT Quick Create form)
- Looks and functions exactly like a native Quick Create form

## ⚠️ Critical Architecture Rules

### 1. NEVER Use Quick Create Context API
```typescript
// ❌ NEVER DO THIS
await this.context.webAPI.createRecord("sprk_document", data);

// ✅ ALWAYS DO THIS
await Xrm.WebApi.createRecord("sprk_document", data);
```

**Why**: Quick Create forms can only create 1 record. The context becomes corrupted after the first `createRecord()` call. We use Custom Page + global `Xrm.WebApi` for unlimited record creation.

### 2. Universal Design - NO Hardcoded Entity Names
```typescript
// ❌ NEVER DO THIS
"sprk_matter@odata.bind": "/sprk_matters(guid)"

// ✅ ALWAYS DO THIS - Dynamic
const lookupField = this.getLookupFieldName(parentEntityName);
const entitySetName = this.getEntitySetName(parentEntityName);
`${lookupField}@odata.bind`: `/${entitySetName}(${parentRecordId})`
```

**Why**: This PCF must work with Matter, Project, Account, Contact, Invoice, and any future entities.

### 3. Clean OData Lookup Syntax
```typescript
// ✅ CORRECT
{
    "sprk_documentname": "file.pdf",
    "sprk_graphitemid": "itemId123",
    "sprk_matter@odata.bind": "/sprk_matters(guid)"
}

// ❌ WRONG - Don't include base field
{
    "sprk_matter": null,  // ❌ Remove this
    "sprk_matter@odata.bind": "/sprk_matters(guid)"
}
```

### 4. Fluent UI v9 Only
```typescript
// ✅ CORRECT
import { Button, Input, Field } from '@fluentui/react-components';

// ❌ WRONG - This is v8
import { PrimaryButton, TextField, Stack } from '@fluentui/react';
```

---

## 📐 Dialog Dimensions & Display

### Navigation Options (Command Button)
```javascript
Xrm.Navigation.navigateTo({
    pageType: "custom",
    name: "sprk_universaldocumentupload_page",
    entityName: "sprk_document",
    data: {
        parentEntityName: parentEntityName,
        parentRecordId: parentRecordId,
        containerId: containerId,
        parentDisplayName: parentDisplayName
    }
}, {
    target: 2,                              // Dialog mode
    position: 1,                            // Center
    width: { value: 600, unit: "px" },      // Fixed 600px (Quick Create standard)
    height: { value: 80, unit: "%" },       // 80% viewport (Quick Create standard)
    title: "Quick Create: Document"         // Native format
});
```

### PCF Container Styling
```typescript
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        backgroundColor: '#FFFFFF',
        fontFamily: '"Segoe UI", sans-serif'
    }
});
```

---

## 🎨 Quick Create Visual Matching

### Color Palette (Use Exact Colors)
```typescript
const COLORS = {
    dialogBackground: '#FFFFFF',
    bodyBackground: '#FAF9F8',
    sectionBackground: '#FFFFFF',
    borderDefault: '#EDEBE9',
    textPrimary: '#323130',
    textSecondary: '#605E5C',
    textRequired: '#A4262C',
    primaryButton: '#0078D4',
    primaryButtonHover: '#106EBE'
};
```

### Layout Structure
```
┌─────────────────────────────────────────┐
│ Quick Create: Document              [X] │ ← Header (48px)
├─────────────────────────────────────────┤
│ ℹ️  Select up to 10 files              │ ← Body (scrollable)
│                                         │   Background: #FAF9F8
│ ┌─────────────────────────────────────┐ │   Padding: 20px
│ │ Upload File Section                 │ │
│ │ [Choose Files]                      │ │
│ │ Selected Files (3)...               │ │
│ └─────────────────────────────────────┘ │
│                                         │
│ ┌─────────────────────────────────────┐ │
│ │ Profile Section                     │ │
│ │ Document Type [▼]                   │ │
│ │ Description [        ]              │ │
│ └─────────────────────────────────────┘ │
├─────────────────────────────────────────┤
│                  [Cancel] [Upload & Save]│ ← Footer (60px)
└─────────────────────────────────────────┘
```

### Section Styling
```typescript
section: {
    backgroundColor: '#FFFFFF',
    padding: '16px',
    borderRadius: '2px',
    border: '1px solid #EDEBE9',
    display: 'flex',
    flexDirection: 'column',
    gap: '12px'
},
sectionHeader: {
    fontSize: '14px',
    fontWeight: 600,
    color: '#323130',
    marginBottom: '8px'
}
```

---

## 📋 Form Structure

### DO Include:
1. ✅ Header with "Quick Create: Document" title and close button
2. ✅ Info banner (file limits)
3. ✅ File input (multiple)
4. ✅ Selected files list with remove buttons
5. ✅ Parent entity display (read-only, shows Matter/Account name)
6. ✅ Document Type dropdown (optional)
7. ✅ Description textarea (optional, applies to ALL files)
8. ✅ Progress bar (during upload)
9. ✅ Success/error messages (after upload)
10. ✅ Footer with Cancel and "Select Files to Continue" buttons

### DO NOT Include:
- ❌ Individual "Document Name" field per file (auto-derived from filename)
- ❌ Per-file description fields (one description for all)
- ❌ Manual relationship selection (pre-populated from parent)

---

## 🔧 Control Manifest Parameters

### ControlManifest.Input.xml
```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" 
           constructor="UniversalQuickCreatePCF" 
           version="2.0.0.0"
           display-name-key="Universal_Document_Upload"
           control-type="standard">
    
    <property name="parentEntityName" 
              display-name-key="Parent_Entity_Name"
              description-key="Logical name of parent entity"
              of-type="SingleLine.Text" 
              usage="input" 
              required="true" />
    
    <property name="parentRecordId" 
              display-name-key="Parent_Record_ID"
              description-key="GUID of parent record without braces"
              of-type="SingleLine.Text" 
              usage="input" 
              required="true" />
    
    <property name="containerId" 
              display-name-key="Container_ID"
              description-key="SharePoint Embedded Container ID"
              of-type="SingleLine.Text" 
              usage="input" 
              required="true" />
    
    <property name="parentDisplayName" 
              display-name-key="Parent_Display_Name"
              description-key="Display name of parent record"
              of-type="SingleLine.Text" 
              usage="input" 
              required="false" />
    
    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/UniversalQuickCreatePCF.css" order="1"/>
    </resources>
    
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

---

## 🏗️ Component Architecture

### Files to Keep (Working Correctly)
- `SdapApiClient.ts` - SharePoint Embedded upload
- `FileUploadService.ts` - File validation
- `MultiFileUploadService.ts` - Upload orchestration (Phase 1 only)
- `LoggingService.ts` - Telemetry

### Files to Create (New)
- `DocumentRecordService.ts` - Uses Xrm.WebApi for record creation
- `EntityDocumentConfigService.ts` - Entity configuration mapping
- `sprk_subgrid_commands.js` - Command button web resource

### Files to Modify
- `UniversalQuickCreatePCF.ts` - Remove Quick Create dependencies
- `ControlManifest.Input.xml` - Add parent context parameters
- React components - Dialog styling with Fluent v9

---

## 📝 Entity Configuration Service

### EntityDocumentConfigService.ts
```typescript
export interface EntityDocumentConfiguration {
    entityLogicalName: string;
    entitySetName: string;
    documentLookupField: string;
    containerIdField: string;
    displayNameFields: string[];
}

export const ENTITY_DOCUMENT_CONFIGS: EntityDocumentConfiguration[] = [
    {
        entityLogicalName: "sprk_matter",
        entitySetName: "sprk_matters",
        documentLookupField: "sprk_matter",
        containerIdField: "sprk_containerid",
        displayNameFields: ["sprk_name"]
    },
    {
        entityLogicalName: "account",
        entitySetName: "accounts",
        documentLookupField: "sprk_account",
        containerIdField: "sprk_containerid",
        displayNameFields: ["name"]
    },
    {
        entityLogicalName: "contact",
        entitySetName: "contacts",
        documentLookupField: "sprk_contact",
        containerIdField: "sprk_containerid",
        displayNameFields: ["fullname", "lastname"]
    }
    // Add more entities as needed
];

export class EntityDocumentConfigService {
    public static getConfigForEntity(entityName: string): EntityDocumentConfiguration | null {
        return ENTITY_DOCUMENT_CONFIGS.find(c => c.entityLogicalName === entityName) || null;
    }
    
    public static isEntitySupported(entityName: string): boolean {
        return ENTITY_DOCUMENT_CONFIGS.some(c => c.entityLogicalName === entityName);
    }
}
```

---

## 🔄 Document Record Service

### DocumentRecordService.ts (Core Logic)
```typescript
export class DocumentRecordService {
    private _logger: LoggingService;
    
    constructor(logger: LoggingService) {
        this._logger = logger;
    }
    
    /**
     * Create multiple Document records from upload results
     */
    public async createMultipleDocuments(
        request: CreateMultipleDocumentsRequest
    ): Promise<CreateMultipleDocumentsResponse> {
        const { uploadResults, parentRecordId, parentEntityName, metadata } = request;
        const createdRecords: string[] = [];
        const errors: RecordCreationError[] = [];
        
        // Validate entity is supported
        if (!EntityDocumentConfigService.isEntitySupported(parentEntityName)) {
            throw new Error(`Entity '${parentEntityName}' is not configured for document upload`);
        }
        
        // Create records sequentially
        for (const uploadResult of uploadResults) {
            if (uploadResult.status === 'rejected') {
                errors.push({
                    fileName: uploadResult.fileName,
                    error: "File upload failed"
                });
                continue;
            }
            
            try {
                const recordId = await this.createSingleDocument({
                    fileName: uploadResult.value.data.fileName,
                    speMetadata: uploadResult.value.data,
                    parentRecordId,
                    parentEntityName,
                    metadata
                });
                
                createdRecords.push(recordId);
                
            } catch (error: any) {
                errors.push({
                    fileName: uploadResult.value.data.fileName,
                    error: error.message
                });
            }
        }
        
        return {
            successCount: createdRecords.length,
            errorCount: errors.length,
            recordIds: createdRecords,
            errors
        };
    }
    
    /**
     * Create a single Document record using Xrm.WebApi
     */
    private async createSingleDocument(
        request: CreateSingleDocumentRequest
    ): Promise<string> {
        const { fileName, speMetadata, parentRecordId, parentEntityName, metadata } = request;
        
        // Get entity configuration
        const config = EntityDocumentConfigService.getConfigForEntity(parentEntityName);
        if (!config) {
            throw new Error(`No configuration found for entity: ${parentEntityName}`);
        }
        
        // Build record data
        const recordData: any = {
            "sprk_documentname": fileName,
            "sprk_filename": fileName,
            "sprk_graphitemid": speMetadata.driveItemId,
            "sprk_graphdriveid": speMetadata.driveId,
            "sprk_filesize": speMetadata.fileSize,
            
            // ✅ DYNAMIC LOOKUP - Uses configuration
            [`${config.documentLookupField}@odata.bind`]: 
                `/${config.entitySetName}(${parentRecordId})`
        };
        
        // Add optional metadata
        if (metadata?.documentType) {
            recordData["sprk_documenttype"] = metadata.documentType;
        }
        if (metadata?.description) {
            recordData["sprk_description"] = metadata.description;
        }
        
        // ✅ USE GLOBAL XRM.WEBAPI (NOT CONTEXT.WEBAPI)
        const result = await Xrm.WebApi.createRecord("sprk_document", recordData);
        
        this._logger.logInfo("Document created", {
            recordId: result.id,
            fileName: fileName,
            parentEntity: parentEntityName,
            parentRecordId: parentRecordId
        });
        
        return result.id;
    }
}

// Interfaces
interface CreateMultipleDocumentsRequest {
    uploadResults: UploadResult[];
    parentRecordId: string;
    parentEntityName: string;
    metadata?: DocumentMetadata;
}

interface CreateMultipleDocumentsResponse {
    successCount: number;
    errorCount: number;
    recordIds: string[];
    errors: RecordCreationError[];
}

interface CreateSingleDocumentRequest {
    fileName: string;
    speMetadata: SpeMetadata;
    parentRecordId: string;
    parentEntityName: string;
    metadata?: DocumentMetadata;
}

interface DocumentMetadata {
    documentType?: string;
    description?: string;
}

interface RecordCreationError {
    fileName: string;
    error: string;
}
```

---

## 🎨 Fluent UI v9 Components Reference

### Available Components
```typescript
import {
    Button,
    Input,
    Field,
    Label,
    Text,
    Textarea,
    Dropdown,
    Option,
    ProgressBar,
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
    Spinner,
    makeStyles,
    tokens
} from '@fluentui/react-components';

import {
    Dismiss24Regular,
    Dismiss16Regular,
    DocumentRegular,
    ArrowUpload24Regular
} from '@fluentui/react-icons';
```

### Button Patterns
```tsx
// Primary action
<Button appearance="primary" onClick={handleSave}>
    Upload & Save
</Button>

// Secondary action
<Button appearance="outline" onClick={handleCancel}>
    Cancel
</Button>

// Icon button
<Button appearance="subtle" icon={<Dismiss24Regular />} />
```

### Form Field Pattern
```tsx
<Field 
    label={
        <span>
            File Name
            <span style={{ color: '#A4262C', marginLeft: '4px' }}>*</span>
        </span>
    }
    required
>
    <Input type="file" multiple />
</Field>
```

### Dropdown Pattern
```tsx
<Dropdown
    placeholder="Select document type"
    value={documentType}
    onOptionSelect={(e, data) => setDocumentType(data.optionValue)}
>
    <Option value="contract">Contract</Option>
    <Option value="invoice">Invoice</Option>
</Dropdown>
```

### Progress Pattern
```tsx
<ProgressBar value={progress / 100} />
<Text>{progressMessage}</Text>
```

### Message Pattern
```tsx
<MessageBar intent="success">
    <MessageBarBody>
        <MessageBarTitle>Success!</MessageBarTitle>
        Successfully uploaded 5 documents.
    </MessageBarBody>
</MessageBar>
```

---

## ✅ Validation Rules

### File Validation
```typescript
const MAX_FILES = 10;
const MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB
const MAX_TOTAL_SIZE = 20 * 1024 * 1024; // 20MB

// Validate file count
if (selectedFiles.length > MAX_FILES) {
    setError(`Maximum ${MAX_FILES} files allowed`);
    return;
}

// Validate individual file sizes
const oversizedFiles = selectedFiles.filter(f => f.size > MAX_FILE_SIZE);
if (oversizedFiles.length > 0) {
    setError(`Files must be under 10MB`);
    return;
}

// Validate total size
const totalSize = selectedFiles.reduce((sum, f) => sum + f.size, 0);
if (totalSize > MAX_TOTAL_SIZE) {
    setError(`Total size must be under 20MB`);
    return;
}
```

### Parameter Validation (in init())
```typescript
public init(context: ComponentFramework.Context<IInputs>, ...): void {
    // Extract parameters
    this._parentEntityName = context.parameters.parentEntityName.raw || '';
    this._parentRecordId = context.parameters.parentRecordId.raw || '';
    this._containerId = context.parameters.containerId.raw || '';
    
    // Validate required
    if (!this._parentEntityName || !this._parentRecordId || !this._containerId) {
        this.showError("Missing required parameters");
        return;
    }
    
    // Validate GUID format
    const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!guidRegex.test(this._parentRecordId)) {
        this.showError("Invalid parent record ID format");
        return;
    }
    
    // Validate entity is supported
    if (!EntityDocumentConfigService.isEntitySupported(this._parentEntityName)) {
        this.showError(`Entity '${this._parentEntityName}' is not configured for document upload`);
        return;
    }
    
    this.renderComponent();
}
```

---

## 📊 State Management

### React Component State
```typescript
const [files, setFiles] = useState<File[]>([]);
const [documentType, setDocumentType] = useState<string>('');
const [description, setDescription] = useState<string>('');
const [uploading, setUploading] = useState(false);
const [progress, setProgress] = useState(0);
const [progressMessage, setProgressMessage] = useState('');
const [uploadResult, setUploadResult] = useState<UploadResult | null>(null);
const [validationError, setValidationError] = useState<string>('');
```

### Upload Flow
```typescript
const handleUploadAndSave = async () => {
    if (files.length === 0) return;
    
    setUploading(true);
    setProgress(0);
    setProgressMessage('Uploading files...');
    
    try {
        // Phase 1: Upload files to SharePoint Embedded
        const uploadResults = await uploadToSharePoint(files);
        setProgress(50);
        setProgressMessage('Creating document records...');
        
        // Phase 2: Create Dataverse records
        const metadata = { documentType, description };
        const recordResults = await createDocumentRecords(uploadResults, metadata);
        
        setProgress(100);
        setUploadResult(recordResults);
        
        // Close dialog after brief delay
        setTimeout(() => onClose(recordResults), 1500);
        
    } catch (error: any) {
        setValidationError(error.message);
    } finally {
        setUploading(false);
    }
};
```

---

## 🧪 Testing Checklist

### Visual Testing
- [ ] Dialog opens at 600px width
- [ ] Dialog height is 80% of viewport
- [ ] Title is "Quick Create: Document"
- [ ] Background colors match (#FFFFFF, #FAF9F8)
- [ ] Borders are #EDEBE9
- [ ] Buttons match Quick Create styling
- [ ] Required fields have red asterisk
- [ ] Sections have proper spacing

### Functional Testing
- [ ] File input accepts multiple files
- [ ] File validation works (count, size)
- [ ] Selected files display with remove buttons
- [ ] Progress bar shows during upload
- [ ] Success message shows after completion
- [ ] Error messages show on failure
- [ ] ESC key closes dialog
- [ ] Cancel button closes dialog
- [ ] Upload & Save button disabled when no files

### Entity Testing
- [ ] Works with Matter entity
- [ ] Works with Account entity
- [ ] Works with Contact entity
- [ ] Lookup field created correctly for each
- [ ] Container ID retrieved correctly for each

### Integration Testing
- [ ] Files upload to SharePoint Embedded
- [ ] Document records created in Dataverse
- [ ] Lookup to parent entity works
- [ ] Subgrid refreshes after upload
- [ ] Multiple files create multiple records

---

## 🚨 Common Mistakes to Avoid

### 1. Using context.webAPI Instead of Xrm.WebApi
```typescript
// ❌ WRONG
await this.context.webAPI.createRecord("sprk_document", data);

// ✅ CORRECT
await Xrm.WebApi.createRecord("sprk_document", data);
```

### 2. Hardcoding Entity Names
```typescript
// ❌ WRONG
"sprk_matter@odata.bind": "/sprk_matters(guid)"

// ✅ CORRECT
const config = EntityDocumentConfigService.getConfigForEntity(parentEntityName);
`${config.documentLookupField}@odata.bind`: `/${config.entitySetName}(${parentRecordId})`
```

### 3. Including Base Field with @odata.bind
```typescript
// ❌ WRONG
{
    "sprk_matter": null,
    "sprk_matter@odata.bind": "/sprk_matters(guid)"
}

// ✅ CORRECT
{
    "sprk_matter@odata.bind": "/sprk_matters(guid)"
}
```

### 4. Using Fluent UI v8
```typescript
// ❌ WRONG
import { PrimaryButton, TextField } from '@fluentui/react';

// ✅ CORRECT
import { Button, Input } from '@fluentui/react-components';
```

### 5. Adding Individual Document Name Fields
```typescript
// ❌ WRONG - Don't ask user to name each file
<Input label="Document Name for file 1" />
<Input label="Document Name for file 2" />

// ✅ CORRECT - Auto-derive from filename
recordData["sprk_documentname"] = file.name;
```

---

## 📚 TypeScript Type Safety

### Always Define Interfaces
```typescript
interface UploadResult {
    status: 'fulfilled' | 'rejected';
    fileName: string;
    data?: SpeMetadata;
    error?: string;
}

interface SpeMetadata {
    driveItemId: string;
    driveId: string;
    fileName: string;
    fileSize: number;
    webUrl: string;
}
```

### Use Proper Typing in Functions
```typescript
// ✅ GOOD
public async createDocument(request: CreateDocumentRequest): Promise<string> {
    // Implementation
}

// ❌ BAD
public async createDocument(data: any): Promise<any> {
    // Implementation
}
```

---

## 🎯 Success Criteria

Your implementation is complete when:

1. ✅ Dialog opens from Documents subgrid on any entity
2. ✅ Dialog looks identical to Quick Create form
3. ✅ User can select multiple files
4. ✅ Files upload to SharePoint Embedded
5. ✅ Document records created in Dataverse
6. ✅ All records linked to correct parent entity
7. ✅ Works with Matter, Account, Contact, and other configured entities
8. ✅ Progress indicator shows during upload
9. ✅ Success/error messages display correctly
10. ✅ Subgrid refreshes automatically after completion
11. ✅ No console errors
12. ✅ ESC key closes dialog
13. ✅ Validation prevents oversized/too many files
14. ✅ Code uses TypeScript strict typing throughout
15. ✅ Uses Fluent UI v9 components only

---

## 📞 Key Implementation Questions

Before starting, confirm you understand:

1. **Why can't we use `context.webAPI.createRecord()`?**
   - Quick Create context designed for single record only
   - Second call causes context corruption and 400 errors
   - Solution: Use global `Xrm.WebApi.createRecord()`

2. **Why must it be entity-agnostic?**
   - Same PCF used for Matter, Project, Account, Contact, Invoice, etc.
   - Configuration-based lookup field mapping
   - No hardcoded entity names

3. **Why 600px width fixed?**
   - Matches native Quick Create form dimensions exactly
   - Ensures consistent UX across all entities

4. **Why single description for all files?**
   - Simpler UX for batch uploads
   - Matches Quick Create pattern (one form, multiple results)
   - Can be enhanced later if needed

---

## 🚀 Start Implementation

Begin with Phase 1 (Foundation):
1. Update ControlManifest.Input.xml
2. Create sprk_subgrid_commands.js
3. Configure custom page
4. Add command button to subgrid

Then proceed to:
- Phase 2: Service layer (DocumentRecordService)
- Phase 3: PCF component refactor
- Phase 4: UI components with Fluent v9
- Phase 5: Testing and validation

**Follow this document exactly for a production-ready implementation!**
```

---

This is your **complete, comprehensive prompt** that Claude Code can follow from start to finish. Save it and provide it when starting implementation.