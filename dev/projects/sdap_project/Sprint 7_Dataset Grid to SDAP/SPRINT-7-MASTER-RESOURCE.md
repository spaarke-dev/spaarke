# Sprint 7: Master Resource Document

**Purpose**: Central reference for all Sprint 7 tasks - shared context, patterns, and knowledge

---

## Project Context

### What We're Building
**Two Universal PCF Controls** that work together to provide complete document management with SDAP integration:

1. **Universal Dataset Grid** - Display and manage records with file operations
2. **Universal Quick Create** - Create new records with file upload (SPE integration)

### Why This Matters
- ✅ **No C# Plugins** - Pure TypeScript implementation (ADR compliance)
- ✅ **No Naked JavaScript** - Type-safe, maintainable code
- ✅ **Reusable Components** - Universal controls work across all entities
- ✅ **Consistent UX** - Standard Power Apps patterns
- ✅ **Modern Stack** - React 18, Fluent UI v9, TypeScript

### Architecture Decision (ADR)
**Decision**: Build two separate PCF controls instead of one combined control or using standard Power Apps controls with plugins.

**Rationale**:
- Standard Power Apps grid + Quick Create would require C# plugins for SPE upload (ADR violation)
- Combined grid + Quick Create in one PCF would mix concerns and limit reusability
- Two separate PCF controls provides maximum reusability and maintainability
- Power Apps provides automatic context sharing between controls

---

## Architecture Overview

### Two-PCF Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Power Apps Model-Driven App (Matter Form)                      │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Documents Subgrid (Universal Dataset Grid PCF)            │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  CommandBar                                           │ │ │
│  │  │  [+ New Document] [Download] [Delete] [Replace]      │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  DataGrid (existing documents)                        │ │ │
│  │  │  - Clickable SharePoint URLs                         │ │ │
│  │  │  - File operations on existing records               │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────────┘ │
│                                                                  │
│  User clicks "+ New Document"                                   │
│                           ↓                                      │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │  Quick Create Form (Universal Quick Create PCF)            │ │
│  │  ┌──────────────────────────────────────────────────────┐ │ │
│  │  │  Select File * [Choose File...]                      │ │ │
│  │  │  Document Name *                                      │ │ │
│  │  │  Description                                          │ │ │
│  │  │  Matter/Case (auto-filled from context)              │ │ │
│  │  │                                                        │ │ │
│  │  │                          [Cancel]  [Save]             │ │ │
│  │  └──────────────────────────────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
                           ↓
              Both PCF controls use same SDAP API client
                           ↓
┌─────────────────────────────────────────────────────────────────┐
│  Spe.Bff.Api (.NET 8 BFF)                                       │
│  - /api/v1/documents (CRUD)                                     │
│  - /api/containers/{id}/files/{path} (upload/download/delete)  │
│  - /api/containers/{id}/upload (chunked upload)                 │
│  - OBO authentication                                           │
└─────────────────────────────────────────────────────────────────┘
                           ↓
┌──────────────────────────┐    ┌──────────────────────────────┐
│  Dataverse               │    │  SharePoint Embedded         │
│  - sprk_document entity  │    │  - File storage (Graph API)  │
│  - sprk_matter entity    │    │  - Container IDs             │
└──────────────────────────┘    └──────────────────────────────┘
```

### Control Interaction Flow

**Document Creation Workflow**:
```
1. User in Matter form → Documents subgrid (Universal Dataset Grid)
2. User clicks "+ New Document" button
3. Power Apps launches Quick Create form
   - Power Apps provides context:
     * regardingObjectId = Matter GUID
     * regardingEntityName = "sprk_matter"
     * entityName = "sprk_document"
4. Universal Quick Create PCF receives context
5. Quick Create retrieves parent Matter data:
   - Matter name
   - Container ID (for SPE)
   - Owner
   - Other fields for defaults
6. Quick Create pre-populates form with defaults
7. User selects file, fills optional fields
8. User clicks Save
9. Quick Create PCF:
   - Uploads file to SPE via Spe.Bff.Api
   - Gets SPE metadata (URL, item ID)
   - Creates Dataverse document record with all metadata
10. Form closes, grid auto-refreshes (standard Power Apps)
```

**File Operations Workflow** (Download, Delete, Replace):
```
1. User selects document record in grid
2. User clicks operation button (Download/Delete/Replace)
3. Universal Dataset Grid PCF:
   - Calls Spe.Bff.Api for file operation
   - Updates/refreshes grid as needed
4. Grid shows updated state immediately
```

---

## Current State (Verified ✅)

### Universal Dataset Grid v2.0.7
- **Location**: `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/`
- **React Version**: 18.2.0
- **Fluent UI**: v9.54.0
- **Bundle Size**: 470 KB (production)
- **Architecture**: Single React root, props-based updates
- **Status**: Production-ready, deployed

### Key Existing Files
| File | Purpose |
|------|---------|
| `index.ts` | PCF control entry point, lifecycle methods |
| `components/CommandBar.tsx` | Toolbar with placeholder file operation buttons |
| `components/DatasetGrid.tsx` | Grid component with selection |
| `components/UniversalDatasetGridRoot.tsx` | Root React component |
| `utils/logger.ts` | Centralized logging utility |
| `components/ErrorBoundary.tsx` | React error handling |
| `providers/ThemeProvider.ts` | Power Apps theme detection |
| `types/index.ts` | TypeScript type definitions |

### SDAP BFF API (Production-Ready)
- **Status**: 8.5/10 production readiness
- **Deployment**: Accessible (URL to be configured)
- **Sprint 2**: All endpoints tested and working

---

## SDAP API Endpoints Reference

**Source of Truth**: `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs`
**Base URL**: `{SDAP_API_URL}/api` (no version prefix)

### File Operation Endpoints

All endpoints use **Managed Identity (MI)** authentication to Graph API via OBO flow.
The PCF control provides the user's access token in the `Authorization` header.

#### Upload File
```http
PUT /api/drives/{driveId}/upload?fileName={fileName}
Authorization: Bearer {userToken}
Content-Type: application/octet-stream
Body: [raw file binary - NOT multipart/form-data]

Response 201 Created:
{
  "id": "01ABC...",              // Graph Item ID → sprk_graphitemid
  "name": "Contract.pdf",         // File name → sprk_filename
  "parentId": "01DEF...",         // Parent folder ID → sprk_parentfolderid
  "size": 2458624,                // File size → sprk_filesize
  "createdDateTime": "2025-10-05T...",     // → sprk_createddatetime
  "lastModifiedDateTime": "2025-10-05T...", // → sprk_lastmodifieddatetime
  "eTag": "\"{12345}\"",          // Version tag → sprk_etag
  "isFolder": false,
  "webUrl": "https://..."         // SharePoint URL → sprk_filepath (URL field)
}
```

**Notes**:
- `driveId`: Graph API Drive ID (from sprk_graphdriveid or container)
- `fileName`: File name (required query parameter)
- Response type: `FileHandleDto` from `Spe.Bff.Api.Models.SpeFileStoreDtos`
- Returns 201 Created with location header: `/api/drives/{driveId}/items/{itemId}`

#### Download File
```http
GET /api/drives/{driveId}/items/{itemId}/content
Authorization: Bearer {userToken}

Response 200 OK:
Content-Type: application/octet-stream
Content-Disposition: attachment; filename="Contract.pdf"
Body: [raw file binary]
```

**Notes**:
- `driveId`: Graph API Drive ID (from sprk_graphdriveid)
- `itemId`: Graph API Item ID (from sprk_graphitemid)
- Returns file stream directly (not JSON)

#### Delete File
```http
DELETE /api/drives/{driveId}/items/{itemId}
Authorization: Bearer {userToken}

Response 204 No Content
```

**Notes**:
- `driveId`: Graph API Drive ID (from sprk_graphdriveid)
- `itemId`: Graph API Item ID (from sprk_graphitemid)
- No response body

#### Get File Metadata
```http
GET /api/drives/{driveId}/items/{itemId}
Authorization: Bearer {userToken}

Response 200 OK:
{
  "id": "01ABC...",
  "name": "Contract.pdf",
  "parentId": "01DEF...",
  "size": 2458624,
  "createdDateTime": "2025-10-05T...",
  "lastModifiedDateTime": "2025-10-05T...",
  "eTag": "\"{12345}\"",
  "isFolder": false,
  "webUrl": "https://..."
}
```

**Notes**:
- Response type: `FileHandleDto`
- Used to verify file exists before operations

### Container Management Endpoints

#### List Drive Children
```http
GET /api/drives/{driveId}/children?itemId={itemId}
Authorization: Bearer {userToken}

Response 200 OK:
[
  { /* FileHandleDto */ },
  { /* FileHandleDto */ }
]
```

**Notes**:
- `itemId`: Optional parent folder ID (omit for root)
- Returns array of `FileHandleDto` objects

#### Get Container Drive
```http
GET /api/containers/{containerId}/drive
Authorization: Bearer {userToken}

Response 200 OK:
{
  "id": "b!ABC...",  // Drive ID
  /* other drive properties */
}
```

**Notes**:
- `containerId`: SPE Container ID (from sprk_containerid lookup)
- Returns Drive information including Drive ID

---

## Field Mappings (Dataverse ↔ SDAP API)

**Source of Truth**:
- API Response: `FileHandleDto` in `Spe.Bff.Api/Models/SpeFileStoreDtos.cs`
- Dataverse Schema: `src/Entities/sprk_Document/Entity.xml`

### Complete Field Mapping Table

| Dataverse Field | API Property | Type | Direction | Notes |
|----------------|--------------|------|-----------|-------|
| **Primary & Identifiers** |
| `sprk_documentid` | - | Guid | - | Dataverse primary key (auto-generated) |
| `sprk_documentname` | - | String(100) | User Input | Document display name in Dataverse |
| `sprk_graphitemid` | `id` | String | API → Dataverse | **Graph API Item ID** (unique file identifier) |
| `sprk_graphdriveid` | - | String | Container → Dataverse | **Graph API Drive ID** (from Matter container) |
| **File Metadata (from FileHandleDto)** |
| `sprk_filename` | `name` | String | API ↔ Dataverse | File name (e.g., "Contract.pdf") |
| `sprk_filesize` | `size` | Integer | API → Dataverse | File size in bytes |
| `sprk_createddatetime` | `createdDateTime` | DateTime | API → Dataverse | File creation timestamp (ISO 8601) |
| `sprk_lastmodifieddatetime` | `lastModifiedDateTime` | DateTime | API → Dataverse | File modification timestamp (ISO 8601) |
| `sprk_etag` | `eTag` | String | API → Dataverse | Version tag for optimistic concurrency |
| `sprk_parentfolderid` | `parentId` | String | API → Dataverse | Parent folder ID (optional) |
| `sprk_filepath` | `webUrl` | URL | API → Dataverse | **SharePoint URL** (clickable link) |
| **Additional Fields** |
| `sprk_hasfile` | - | Boolean | Calculated | `true` if `sprk_graphitemid` is not null |
| `sprk_mimetype` | - | String | `File.type` | MIME type (e.g., "application/pdf") |
| `sprk_containerid` | - | Lookup | Parent | Lookup to SPE Container record |
| `sprk_matter` | - | Lookup | Parent | Lookup to Matter record (regarding) |

### Field Population Flow

**Upload Flow** (PCF → API → Dataverse):
```typescript
// 1. User uploads file via PCF
const file = await filePicker.getFile();

// 2. Get driveId from Matter record
const matter = await context.webAPI.retrieveRecord(
    'sprk_matter',
    matterId,
    '?$select=sprk_containerid'
);
const container = await context.webAPI.retrieveRecord(
    'sprk_container',
    matter.sprk_containerid,
    '?$select=sprk_graphdriveid'
);
const driveId = container.sprk_graphdriveid;

// 3. Upload to SDAP API
const response = await sdapClient.uploadFile({
    file,
    driveId,
    fileName: file.name
});

// 4. Map FileHandleDto → Dataverse Document
await context.webAPI.createRecord('sprk_document', {
    sprk_documentname: file.name,
    sprk_filename: response.name,
    sprk_graphitemid: response.id,
    sprk_graphdriveid: driveId,
    sprk_filesize: response.size,
    sprk_createddatetime: response.createdDateTime,
    sprk_lastmodifieddatetime: response.lastModifiedDateTime,
    sprk_etag: response.eTag,
    sprk_parentfolderid: response.parentId,
    sprk_filepath: response.webUrl,
    sprk_hasfile: true,
    sprk_mimetype: file.type,
    'sprk_matter@odata.bind': `/sprk_matters(${matterId})`
});
```

**Download Flow** (Dataverse → API):
```typescript
// 1. Get record from grid
const record = dataset.records[selectedId];

// 2. Extract API parameters from Dataverse fields
const driveId = record.getValue('sprk_graphdriveid');
const itemId = record.getValue('sprk_graphitemid');

// 3. Download from SDAP API
const blob = await sdapClient.downloadFile({ driveId, itemId });

// 4. Trigger browser download
const url = URL.createObjectURL(blob);
const link = document.createElement('a');
link.href = url;
link.download = record.getValue('sprk_filename');
link.click();
```

---

## Authentication Flow

### PCF Context → SDAP API → Graph API

1. **PCF Control** retrieves user access token:
   ```typescript
   const token = (context as any).userSettings?.accessToken;
   ```

2. **SDAP Client** includes token in requests:
   ```typescript
   headers: {
     'Authorization': `Bearer ${token}`,
     'Content-Type': 'application/json'
   }
   ```

3. **Spe.Bff.Api** validates token and performs OBO flow:
   - Receives user token from PCF
   - Exchanges for Graph token via On-Behalf-Of
   - Calls SharePoint Embedded as user
   - Returns results to PCF

4. **Managed Identity** used for Dataverse operations (server-side)

---

## Context Sharing Between PCF Controls

### How Power Apps Provides Context

When a Quick Create form is launched from a subgrid (e.g., Documents subgrid on a Matter form), Power Apps **automatically provides context** to the Quick Create PCF control via the `context` object.

**Key Context Properties**:

```typescript
// Available in Quick Create PCF's init() method
const formContext = (context as any).mode?.contextInfo;

if (formContext) {
    // Parent entity information
    const parentEntityName = formContext.regardingEntityName;    // "sprk_matter"
    const parentRecordId = formContext.regardingObjectId;        // Matter GUID

    // Current entity (what we're creating)
    const entityName = formContext.entityName;                   // "sprk_document"
}
```

**Important**: This context is provided **automatically** by Power Apps. You do not need to:
- Pass parameters between controls
- Implement custom messaging
- Store state in global variables

### Retrieving Parent Entity Data

Once you have the `regardingObjectId` and `regardingEntityName`, you can retrieve the full parent record using the Dataverse Web API:

```typescript
export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private parentEntityName: string = '';
    private parentRecordId: string = '';
    private parentRecordData: ComponentFramework.WebApi.Entity | null = null;

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // Get parent context
        const formContext = (context as any).mode?.contextInfo;

        if (formContext) {
            this.parentEntityName = formContext.regardingEntityName;
            this.parentRecordId = formContext.regardingObjectId;

            // Retrieve parent record data for default values
            if (this.parentRecordId && this.parentEntityName) {
                await this.loadParentRecordData(context);
            }
        }

        // Continue with PCF initialization...
    }

    private async loadParentRecordData(context: ComponentFramework.Context<IInputs>): Promise<void> {
        try {
            logger.info('QuickCreate', 'Loading parent record data', {
                entityName: this.parentEntityName,
                recordId: this.parentRecordId
            });

            // Build select query based on parent entity type
            const selectFields = this.getParentSelectFields(this.parentEntityName);

            this.parentRecordData = await context.webAPI.retrieveRecord(
                this.parentEntityName,
                this.parentRecordId,
                `?$select=${selectFields}`
            );

            logger.info('QuickCreate', 'Parent record data loaded', this.parentRecordData);

        } catch (error) {
            logger.error('QuickCreate', 'Failed to load parent record data', error);
        }
    }

    private getParentSelectFields(entityName: string): string {
        // Define fields to retrieve based on parent entity type
        const fieldMappings: Record<string, string[]> = {
            'sprk_matter': [
                'sprk_name',
                'sprk_containerid',
                '_ownerid_value',
                '_sprk_primarycontact_value',
                'sprk_matternumber'
            ],
            'account': [
                'name',
                '_ownerid_value'
            ],
            'contact': [
                'fullname',
                '_ownerid_value'
            ]
        };

        return (fieldMappings[entityName] || ['name']).join(',');
    }
}
```

---

## Configurable Default Value Mappings

### Overview

The Universal Quick Create PCF must support **different default value mappings** for different parent-child entity relationships. For example:
- Creating a **Document** from a **Matter** → Populate container ID, matter name, owner
- Creating a **Task** from a **Matter** → Populate matter name, owner, due date
- Creating a **Contact** from an **Account** → Populate account name, address

### Implementation Approach

We'll use a **configuration-driven approach** where default value mappings are defined in the PCF manifest and retrieved at runtime.

#### Option 1: Manifest Parameters (Recommended)

Define mappings as a JSON string in the PCF manifest parameter:

**ControlManifest.Input.xml**:
```xml
<property name="defaultValueMappings"
          display-name-key="Default Value Mappings"
          description-key="JSON mapping of parent fields to child default values"
          of-type="SingleLine.Text"
          usage="input"
          required="false"
          default-value='{"sprk_matter":{"sprk_containerid":"sprk_containerid","sprk_name":"sprk_documenttitle","_ownerid_value":"ownerid"}}' />
```

**Power Apps Form Customization**:
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid",
    "_sprk_primarycontact_value": "sprk_contact"
  },
  "account": {
    "name": "sprk_accountname",
    "_ownerid_value": "ownerid"
  }
}
```

**PCF Code**:
```typescript
export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private defaultValueMappings: Record<string, Record<string, string>> = {};

    public init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // Parse mapping configuration from manifest parameter
        const mappingJson = context.parameters.defaultValueMappings?.raw;

        if (mappingJson) {
            try {
                this.defaultValueMappings = JSON.parse(mappingJson);
                logger.info('QuickCreate', 'Default value mappings loaded', this.defaultValueMappings);
            } catch (error) {
                logger.error('QuickCreate', 'Failed to parse default value mappings', error);
            }
        }

        // Continue initialization...
    }

    private getDefaultValues(): Record<string, any> {
        const defaults: Record<string, any> = {};

        if (!this.parentRecordData || !this.parentEntityName) {
            return defaults;
        }

        // Get mapping for this parent entity type
        const mappings = this.defaultValueMappings[this.parentEntityName];

        if (!mappings) {
            logger.warn('QuickCreate', 'No default value mappings found for parent entity', {
                parentEntityName: this.parentEntityName
            });
            return defaults;
        }

        // Apply mappings
        for (const [parentField, childField] of Object.entries(mappings)) {
            const parentValue = this.parentRecordData[parentField];

            if (parentValue !== undefined && parentValue !== null) {
                defaults[childField] = parentValue;

                logger.debug('QuickCreate', 'Mapped default value', {
                    parentField,
                    childField,
                    value: parentValue
                });
            }
        }

        return defaults;
    }
}
```

#### Option 2: Convention-Based Mapping (Fallback)

If no explicit mapping is configured, use **naming conventions** to auto-map fields:

```typescript
private getDefaultValuesByConvention(): Record<string, any> {
    const defaults: Record<string, any> = {};

    if (!this.parentRecordData) {
        return defaults;
    }

    // Convention: If parent has "sprk_containerid", child gets "sprk_containerid"
    // Convention: If parent has "ownerid", child gets "ownerid"
    const conventionMappings = [
        { parent: 'sprk_containerid', child: 'sprk_containerid' },
        { parent: '_ownerid_value', child: 'ownerid' },
        { parent: 'sprk_name', child: 'sprk_name' }
    ];

    for (const mapping of conventionMappings) {
        const value = this.parentRecordData[mapping.parent];

        if (value !== undefined && value !== null) {
            defaults[mapping.child] = value;
        }
    }

    return defaults;
}
```

### Entity-Specific Mapping Examples

#### Document from Matter
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid",
    "_sprk_primarycontact_value": "sprk_contact",
    "sprk_matternumber": "sprk_matternumber"
  }
}
```

**Result**: When creating a Document from a Matter:
- Matter's `sprk_containerid` → Document's `sprk_containerid` (for SPE operations)
- Matter's `sprk_name` → Document's `sprk_documenttitle`
- Matter's owner → Document's owner
- Matter's primary contact → Document's contact
- Matter's matter number → Document's matter number

#### Task from Matter
```json
{
  "sprk_matter": {
    "sprk_name": "sprk_subject",
    "_ownerid_value": "ownerid",
    "_sprk_primarycontact_value": "sprk_regardingcontact"
  }
}
```

#### Contact from Account
```json
{
  "account": {
    "name": "sprk_company",
    "_ownerid_value": "ownerid",
    "address1_line1": "address1_line1",
    "address1_city": "address1_city"
  }
}
```

### How Form Fields Receive Default Values

The Universal Quick Create PCF will expose the default values to the React form components:

```typescript
// In UniversalQuickCreatePCF
public updateView(context: ComponentFramework.Context<IInputs>): void {
    const defaultValues = this.getDefaultValues();

    // Pass to React component
    const props: QuickCreateFormProps = {
        entityName: this.entityName,
        defaultValues: defaultValues,
        context: context,
        onSave: this.handleSave,
        onCancel: this.handleCancel
    };

    this.reactRoot.render(React.createElement(QuickCreateForm, props));
}
```

```typescript
// In React component
export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    entityName,
    defaultValues,
    context,
    onSave
}) => {
    // Initialize form with default values
    const [formData, setFormData] = React.useState<Record<string, any>>(defaultValues);

    return (
        <FluentProvider theme={webLightTheme}>
            <form onSubmit={handleSubmit}>
                {/* File upload field */}
                <Field label="Select File" required>
                    <Input
                        type="file"
                        onChange={handleFileChange}
                    />
                </Field>

                {/* Auto-populated from Matter */}
                <Field label="Document Title">
                    <Input
                        value={formData.sprk_documenttitle || ''}
                        onChange={(e) => setFormData({ ...formData, sprk_documenttitle: e.target.value })}
                    />
                </Field>

                {/* Other fields... */}
            </form>
        </FluentProvider>
    );
};
```

---

## Code Patterns & Standards

### 1. Logger Usage (Existing Pattern)

```typescript
import { logger } from '../utils/logger';

// Info - normal operations
logger.info('ComponentName', 'Operation started', optionalData);

// Debug - detailed diagnostics
logger.debug('ComponentName', 'Variable state', { value: x });

// Error - failures (with error object)
logger.error('ComponentName', 'Operation failed', error);

// Warn - non-critical issues
logger.warn('ComponentName', 'Fallback used', reason);
```

### 2. Error Handling Pattern

```typescript
try {
    logger.info('Service', 'Starting operation');

    const result = await apiCall();

    logger.info('Service', 'Operation complete');
    return { success: true, data: result };

} catch (error) {
    logger.error('Service', 'Operation failed', error);

    return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
    };
}
```

### 3. React Component Pattern (Existing)

```typescript
import * as React from 'react';
import { logger } from '../utils/logger';

interface MyComponentProps {
    dataset: ComponentFramework.PropertyTypes.DataSet;
    context: ComponentFramework.Context<IInputs>;
}

export const MyComponent: React.FC<MyComponentProps> = ({ dataset, context }) => {
    const [state, setState] = React.useState<Type>(initialValue);

    const handleAction = React.useCallback(async () => {
        try {
            logger.info('MyComponent', 'Action started');
            // Implementation
        } catch (error) {
            logger.error('MyComponent', 'Action failed', error);
        }
    }, [dependencies]);

    return (
        <div>
            {/* JSX */}
        </div>
    );
};
```

### 4. Service Class Pattern (NEW - To Implement)

```typescript
import { logger } from '../utils/logger';

export class MyService {
    constructor(private apiClient: SdapApiClient) {}

    async performOperation(params: Params): Promise<Result> {
        try {
            logger.info('MyService', 'Operation started', params);

            const result = await this.apiClient.someMethod(params);

            logger.info('MyService', 'Operation complete');
            return { success: true, data: result };

        } catch (error) {
            logger.error('MyService', 'Operation failed', error);
            return {
                success: false,
                error: error instanceof Error ? error.message : 'Unknown error'
            };
        }
    }
}
```

---

## TypeScript Standards

### Interface Naming
- Props: `ComponentNameProps`
- API Request: `OperationRequest`
- API Response: `OperationResponse`
- Service Result: `OperationResult`
- Types/Enums: `DescriptiveName`

### File Organization
```
services/
  ├── SdapApiClient.ts          # API client class
  ├── SdapApiClientFactory.ts   # Factory with context
  ├── FileUploadService.ts      # Upload orchestration
  ├── FileDownloadService.ts    # Download logic
  └── FileDeleteService.ts      # Delete logic

types/
  └── index.ts                  # All type definitions

components/
  ├── CommandBar.tsx            # Toolbar (existing)
  ├── DatasetGrid.tsx           # Grid (existing)
  └── ConfirmDialog.tsx         # Confirmation dialog (new)
```

---

## Import Patterns

### Existing Imports (Use These)
```typescript
// PCF Framework
import { IInputs, IOutputs } from './generated/ManifestTypes';

// React
import * as React from 'react';

// Fluent UI v9
import {
    Button,
    Toolbar,
    ToolbarButton,
    DataGrid,
    DataGridHeader,
    DataGridBody,
    // ... other components
} from '@fluentui/react-components';

// Icons (Fluent UI)
import {
    Add24Regular,
    Delete24Regular,
    ArrowDownload24Regular,
    ArrowUpload24Regular,
    ArrowSync24Regular
} from '@fluentui/react-icons';

// Utilities (existing)
import { logger } from '../utils/logger';
import { resolveTheme } from '../providers/ThemeProvider';
```

---

## PCF Context API Reference

### Key Properties Used in Sprint 7

```typescript
// User Settings (for authentication)
context.userSettings.accessToken      // User's access token (may not exist)

// Web API (for Dataverse queries)
context.webAPI.retrieveRecord(
    entityType: string,
    id: string,
    options?: string
): Promise<ComponentFramework.WebApi.Entity>

// Dataset (grid data)
dataset.records                       // Record dictionary
dataset.columns                       // Column metadata
dataset.refresh()                     // Refresh grid data
dataset.getSelectedRecordIds()        // Selected row IDs

// Notify Output Changed (trigger re-render)
notifyOutputChanged()                 // Call after data changes
```

---

## Common Workflows

### File Upload Workflow (Full Sequence)
1. User clicks Upload button in CommandBar
2. Get selected record's matter ID
3. Retrieve matter record to get container ID
4. Open browser file picker
5. User selects file
6. Upload file to SharePoint Embedded (SDAP API)
7. Create Dataverse document record (SDAP API)
8. Update document with SharePoint metadata (SDAP API)
9. Refresh grid to show new record
10. Notify output changed

### File Download Workflow
1. User selects record and clicks Download button
2. Get container ID and file path from record
3. Download file blob from SDAP API
4. Create browser download link
5. Trigger download
6. Clean up blob URL

### File Delete Workflow
1. User selects record and clicks Delete button
2. Show confirmation dialog
3. User confirms
4. Delete file from SharePoint Embedded (SDAP API)
5. Delete Dataverse document record (SDAP API)
6. Refresh grid to remove record
7. Notify output changed

---

## Environment Configuration

### Development
```typescript
SDAP_API_URL=https://localhost:7071  // Local SDAP API
```

### Production (To Be Configured)
```typescript
SDAP_API_URL=https://{app-service}.azurewebsites.net
```

---

## Known Issues & Considerations

### Issue 1: SPE Container ID Format
**Problem**: Dataverse may store GUID format, SharePoint expects b!... format
**Solution**: Verify format in Spe.Bff.Api, add conversion if needed

### Issue 2: Access Token Retrieval
**Problem**: Token location varies by PCF version
**Solution**: Try multiple properties:
1. `context.userSettings.accessToken`
2. `context.page.accessToken`
3. Fallback to error if neither exists

### Issue 3: Large Files (>4 MB)
**Problem**: Simple upload has size limits
**Solution**: Implement chunked upload using `createUploadSession` endpoint

---

## Bundle Size Targets

- **Current**: 470 KB (v2.0.7)
- **Estimated Addition**: ~50-80 KB (SDAP services)
- **Target**: <550 KB
- **Limit**: 5 MB (5,120 KB)
- **Buffer**: 4,570 KB remaining (89% headroom)

---

## Testing Strategy

### Unit Tests (Task 7)
- API client methods
- Service orchestration logic
- Error handling paths

### Integration Tests (Task 7)
- End-to-end file operations
- API connectivity
- Authentication flow

### Manual Testing (Each Task)
- Browser file picker
- Upload/download/delete operations
- Grid refresh behavior
- Error scenarios

---

## Success Criteria (Overall Sprint)

### Technical
- ✅ Bundle size <550 KB
- ✅ All file operations <2s response time
- ✅ Zero TypeScript compilation errors
- ✅ Zero runtime errors in production

### User Experience
- ✅ Single-click file operations
- ✅ Automatic metadata population
- ✅ Clickable SharePoint URLs
- ✅ Confirmation for destructive actions
- ✅ Real-time grid updates

### Business
- ✅ Replace JavaScript web resource
- ✅ Improve file operation reliability
- ✅ Reduce user training requirements

---

## References

### Sprint Documentation
- [Sprint 2 Wrap-Up](../Sprint%202/SPRINT-2-WRAP-UP-REPORT.md) - SDAP current state
- [Sprint 5B Summary](../../UniversalDatasetGrid/SPRINT_5B_SUMMARY.md) - Grid v2.0.7 completion
- [SDAP Assessment](../SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md) - Full project status

### Code Documentation
- [Universal Grid README](../../UniversalDatasetGrid/README.md) - Component overview
- [Component API](../../UniversalDatasetGrid/COMPONENT_API.md) - API reference
- [Virtualization Investigation](../../UniversalDatasetGrid/VIRTUALIZATION_INVESTIGATION.md) - Performance notes

### External Resources
- [PCF Framework Docs](https://learn.microsoft.com/power-apps/developer/component-framework/)
- [Fluent UI v9 Docs](https://react.fluentui.dev/)
- [SharePoint Embedded Docs](https://learn.microsoft.com/sharepoint/dev/embedded/)

---

**Last Updated**: 2025-10-05
**Version**: 1.0
**Purpose**: Master reference for Sprint 7 tasks
