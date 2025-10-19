# ADR Compliance: Universal Document Upload PCF

This document outlines how the Universal Document Upload PCF implementation complies with Spaarke's Architectural Decision Records (ADRs).

---

## ADR-001: Fluent UI v9 Components (CRITICAL)

**Status:** ✅ MANDATORY COMPLIANCE

**Decision:** All UI components must use Fluent UI v9 from `@fluentui/react-components`, NOT Fluent UI v8.

**Reference Implementation:** Universal Dataset Grid PCF (Sprint 5B)

### Compliance Checklist

#### ✅ DO: Import from @fluentui/react-components
```typescript
import {
    Button,
    Input,
    Field,
    Label,
    ProgressBar,
    MessageBar,
    Spinner,
    Dialog,
    DialogSurface,
    DialogBody,
    DialogTitle,
    DialogActions,
    makeStyles,
    tokens
} from '@fluentui/react-components';
```

#### ❌ DON'T: Import from @fluentui/react (v8)
```typescript
// FORBIDDEN - DO NOT USE
import { PrimaryButton, TextField, Stack } from '@fluentui/react';
```

### Required Components

| Component | Fluent UI v9 | Usage |
|-----------|--------------|-------|
| Buttons | `<Button appearance="primary">` | Upload, Cancel actions |
| Text Input | `<Input type="text">` | Description field |
| File Input | `<Input type="file" multiple>` | File selection |
| Form Fields | `<Field label="..."><Input /></Field>` | Form structure |
| Progress Bar | `<ProgressBar value={30} max={100}>` | Upload progress |
| Error Messages | `<MessageBar intent="error">` | Validation errors |
| Loading | `<Spinner label="Uploading...">` | Processing state |

### Styling Compliance

#### ✅ DO: Use makeStyles() hook
```typescript
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
        padding: tokens.spacingHorizontalXXL,
        backgroundColor: tokens.colorNeutralBackground1
    },
    buttonRow: {
        display: 'flex',
        justifyContent: 'flex-end',
        gap: tokens.spacingHorizontalM,
        marginTop: tokens.spacingVerticalXL
    },
    fileInput: {
        width: '100%',
        marginBottom: tokens.spacingVerticalM
    }
});
```

#### ✅ DO: Use design tokens
```typescript
// Spacing
tokens.spacingHorizontalXS    // 4px
tokens.spacingHorizontalS     // 8px
tokens.spacingHorizontalM     // 12px
tokens.spacingHorizontalL     // 16px
tokens.spacingHorizontalXL    // 20px
tokens.spacingHorizontalXXL   // 24px

// Colors
tokens.colorBrandBackground   // Primary brand color
tokens.colorNeutralBackground1 // Light background
tokens.colorNeutralBackground2 // Slightly darker
tokens.colorStatusSuccessForeground // Green for success
tokens.colorStatusDangerForeground  // Red for errors

// Typography
tokens.fontSizeBase300        // 14px (body text)
tokens.fontSizeBase400        // 16px (larger body)
tokens.fontSizeBase500        // 18px (headings)
tokens.fontWeightSemibold     // 600 (emphasis)
```

#### ❌ DON'T: Use inline styles or CSS classes
```typescript
// FORBIDDEN
<div style={{ padding: '20px', color: '#0078d4' }}>
<div className="my-custom-class">
```

### Layout Compliance

#### ✅ DO: Use CSS Flexbox/Grid
```typescript
const useStyles = makeStyles({
    formLayout: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL
    },
    twoColumnLayout: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: tokens.spacingHorizontalM
    }
});
```

#### ❌ DON'T: Use Stack component (v8 only)
```typescript
// FORBIDDEN - Stack is v8 component
import { Stack } from '@fluentui/react';
<Stack horizontal tokens={{ childrenGap: 10 }}>
```

---

## ADR-002: TypeScript Strict Mode

**Status:** ✅ REQUIRED

**Decision:** All TypeScript code must compile with strict mode enabled.

### tsconfig.json Requirements
```json
{
    "compilerOptions": {
        "strict": true,
        "noImplicitAny": true,
        "strictNullChecks": true,
        "strictFunctionTypes": true,
        "strictPropertyInitialization": true,
        "noImplicitThis": true,
        "alwaysStrict": true
    }
}
```

### Compliance Examples

#### ✅ DO: Explicit typing
```typescript
// Explicit return types
async function uploadFile(file: File): Promise<ServiceResult<SpeFileMetadata>> {
    // ...
}

// Explicit parameter types
function handleError(error: Error | unknown): void {
    if (error instanceof Error) {
        console.error(error.message);
    }
}

// Null handling
const containerId: string | null = parentContext?.containerId ?? null;
if (!containerId) {
    throw new Error('Container ID is required');
}
```

#### ❌ DON'T: Implicit any
```typescript
// FORBIDDEN
function processData(data) {  // ← Implicit 'any'
    return data.value;
}

// FORBIDDEN
let result;  // ← Implicit 'any'
result = await fetchData();
```

---

## ADR-003: Separation of Concerns

**Status:** ✅ REQUIRED

**Decision:** Clear separation between UI, business logic, and data access layers.

### Layer Architecture

```
┌─────────────────────────────────────┐
│  Presentation Layer                 │
│  (React Components - Fluent UI v9)  │
│  • DocumentUploadForm.tsx           │
│  • FileSelectionField.tsx           │
│  • UploadProgressBar.tsx            │
└─────────────────────────────────────┘
              ↓
┌─────────────────────────────────────┐
│  Control Layer                      │
│  (PCF Framework)                    │
│  • UniversalDocumentUploadPCF.ts    │
│    (Orchestration, state management)│
└─────────────────────────────────────┘
              ↓
┌─────────────────────────────────────┐
│  Service Layer                      │
│  (Business Logic)                   │
│  • DocumentRecordService.ts         │
│  • FileUploadService.ts             │
│  • MsalAuthProvider.ts              │
└─────────────────────────────────────┘
              ↓
┌─────────────────────────────────────┐
│  Data Access Layer                  │
│  (External APIs)                    │
│  • SdapApiClient.ts (BFF API)       │
│  • Xrm.WebApi (Dataverse)           │
└─────────────────────────────────────┘
```

### Anti-Pattern: DO NOT Mix Layers

#### ❌ DON'T: API calls in React components
```typescript
// FORBIDDEN - React component calling API directly
function FileUploadForm() {
    const handleUpload = async () => {
        const response = await fetch('/api/upload', { /* ... */ });
        // ...
    };
}
```

#### ✅ DO: Use service layer
```typescript
// CORRECT - React component calls service
function FileUploadForm({ uploadService }: Props) {
    const handleUpload = async () => {
        const result = await uploadService.uploadFile(file);
        // ...
    };
}
```

---

## ADR-004: Single Responsibility Principle

**Status:** ✅ REQUIRED

**Decision:** Each class/module should have one clearly defined responsibility.

### Service Responsibilities

| Service | Responsibility | Dependency |
|---------|----------------|------------|
| `FileUploadService` | Upload files to SPE | `SdapApiClient` |
| `DocumentRecordService` | Create Dataverse records | `Xrm.WebApi` |
| `SdapApiClient` | HTTP client for BFF API | `MsalAuthProvider` |
| `MsalAuthProvider` | OAuth2 authentication | MSAL library |
| `EntityDocumentConfig` | Configuration mapping | None (pure data) |

### Anti-Pattern: God Classes

#### ❌ DON'T: Service that does everything
```typescript
// FORBIDDEN - Too many responsibilities
class UniversalService {
    async uploadFile() { /* ... */ }
    async createRecord() { /* ... */ }
    async authenticate() { /* ... */ }
    async validateFile() { /* ... */ }
    async logError() { /* ... */ }
}
```

#### ✅ DO: Focused services
```typescript
// CORRECT - Each service has one job
class FileUploadService {
    async uploadFile(file: File): Promise<Result> { /* ... */ }
}

class DocumentRecordService {
    async createDocument(data: RecordData): Promise<string> { /* ... */ }
}

class ValidationService {
    validateFile(file: File): ValidationResult { /* ... */ }
}
```

---

## ADR-005: Error Handling Strategy

**Status:** ✅ REQUIRED

**Decision:** Consistent error handling with user-friendly messages and detailed logging.

### Error Hierarchy

```typescript
// Custom error types
class FileUploadError extends Error {
    constructor(
        message: string,
        public readonly fileName: string,
        public readonly cause?: Error
    ) {
        super(message);
        this.name = 'FileUploadError';
    }
}

class ValidationError extends Error {
    constructor(
        message: string,
        public readonly field: string,
        public readonly value: unknown
    ) {
        super(message);
        this.name = 'ValidationError';
    }
}

class DataverseError extends Error {
    constructor(
        message: string,
        public readonly statusCode: number,
        public readonly details?: unknown
    ) {
        super(message);
        this.name = 'DataverseError';
    }
}
```

### Error Handling Pattern

#### ✅ DO: Try-catch with typed errors
```typescript
async function uploadFile(file: File): Promise<ServiceResult<SpeFileMetadata>> {
    try {
        // Validation
        const validation = validateFile(file);
        if (!validation.isValid) {
            throw new ValidationError(validation.error, 'file', file.name);
        }

        // Upload
        const result = await sdapClient.uploadFile({ file, driveId });
        return { success: true, data: result };

    } catch (error) {
        // Type-safe error handling
        if (error instanceof ValidationError) {
            logger.error('Validation failed', { field: error.field, value: error.value });
            return { success: false, error: `Invalid ${error.field}: ${error.message}` };
        }

        if (error instanceof FileUploadError) {
            logger.error('Upload failed', { fileName: error.fileName, cause: error.cause });
            return { success: false, error: `Failed to upload ${error.fileName}` };
        }

        // Unknown error
        logger.error('Unexpected error', error);
        return { success: false, error: 'An unexpected error occurred' };
    }
}
```

### User-Friendly Error Messages

#### ✅ DO: Translate technical errors
```typescript
function getUserFriendlyErrorMessage(error: Error): string {
    if (error.message.includes('403')) {
        return 'Access denied. You do not have permission to upload files to this container.';
    }
    if (error.message.includes('404')) {
        return 'Container not found. Please verify the Matter has a valid SharePoint container.';
    }
    if (error.message.includes('413')) {
        return 'File is too large. Maximum file size is 10MB.';
    }
    return 'An error occurred while uploading the file. Please try again.';
}
```

---

## ADR-006: Logging Standards

**Status:** ✅ REQUIRED

**Decision:** Structured logging with consistent format and levels.

### Log Levels

```typescript
logger.debug('Component initialized', { props });     // Development only
logger.info('Upload started', { fileCount, totalSize }); // Normal operations
logger.warn('Partial failure', { successCount, failureCount }); // Recoverable issues
logger.error('Upload failed', { error, context });    // Critical failures
```

### Log Format

```typescript
// Component name, action, structured data
logger.info('DocumentUploadPCF', 'File validation passed', {
    fileName: file.name,
    fileSize: file.size,
    fileType: file.type,
    timestamp: new Date().toISOString()
});

logger.error('DocumentRecordService', 'Record creation failed', {
    fileName: file.name,
    parentEntityName: parentContext.entityName,
    parentRecordId: parentContext.recordId,
    error: error.message,
    stack: error.stack
});
```

### PII Protection

#### ❌ DON'T: Log sensitive data
```typescript
// FORBIDDEN - Contains user email
logger.info('User authenticated', { email: user.email, password: user.password });

// FORBIDDEN - Contains record data
logger.info('Record created', { fullRecord: recordData });
```

#### ✅ DO: Log IDs and metadata only
```typescript
// CORRECT - No PII
logger.info('User authenticated', { userId: user.id });

// CORRECT - IDs only
logger.info('Record created', { recordId: result.id, entityName: 'sprk_document' });
```

---

## ADR-007: Dependency Injection

**Status:** ✅ RECOMMENDED

**Decision:** Use constructor injection for testability and loose coupling.

### Pattern

#### ✅ DO: Inject dependencies
```typescript
class UniversalDocumentUploadPCF {
    private fileUploadService: FileUploadService;
    private documentRecordService: DocumentRecordService;

    public init(context: ComponentFramework.Context<IInputs>): void {
        // Inject dependencies
        const apiClient = SdapApiClientFactory.create(apiBaseUrl);
        this.fileUploadService = new FileUploadService(apiClient);
        this.documentRecordService = new DocumentRecordService();
    }
}

class FileUploadService {
    constructor(
        private apiClient: SdapApiClient
    ) {}

    async uploadFile(request: FileUploadRequest): Promise<ServiceResult> {
        return this.apiClient.uploadFile(request);
    }
}
```

#### ❌ DON'T: Create dependencies internally
```typescript
// FORBIDDEN - Tight coupling, hard to test
class FileUploadService {
    async uploadFile(request: FileUploadRequest): Promise<ServiceResult> {
        const apiClient = new SdapApiClient();  // ← Bad: creates own dependency
        return apiClient.uploadFile(request);
    }
}
```

---

## ADR-008: Immutable State

**Status:** ✅ RECOMMENDED

**Decision:** Prefer immutable data structures for state management.

### React State Pattern

#### ✅ DO: Immutable updates
```typescript
const [uploadProgress, setUploadProgress] = useState<UploadProgress>({
    current: 0,
    total: 0,
    status: 'idle'
});

// Update immutably
setUploadProgress(prev => ({
    ...prev,
    current: prev.current + 1,
    status: 'uploading'
}));
```

#### ❌ DON'T: Mutate state
```typescript
// FORBIDDEN
uploadProgress.current += 1;  // ← Mutates state directly
setUploadProgress(uploadProgress);  // ← Same reference, won't trigger re-render
```

### Array Operations

#### ✅ DO: Use spread operator / filter / map
```typescript
// Add item
const newFiles = [...selectedFiles, newFile];

// Remove item
const filtered = selectedFiles.filter(f => f.name !== fileName);

// Update item
const updated = selectedFiles.map(f =>
    f.name === fileName ? { ...f, status: 'uploaded' } : f
);
```

#### ❌ DON'T: Mutate arrays
```typescript
// FORBIDDEN
selectedFiles.push(newFile);  // ← Mutates array
selectedFiles[0].status = 'uploaded';  // ← Mutates object
```

---

## ADR-009: API Response Wrapping

**Status:** ✅ REQUIRED

**Decision:** All service methods return typed `ServiceResult<T>` for consistent error handling.

### ServiceResult Type

```typescript
export interface ServiceResult<T> {
    success: boolean;
    data?: T;
    error?: string;
}

// Usage
async function uploadFile(file: File): Promise<ServiceResult<SpeFileMetadata>> {
    try {
        const data = await api.upload(file);
        return { success: true, data };
    } catch (error) {
        return { success: false, error: error.message };
    }
}

// Consumer
const result = await uploadFile(file);
if (result.success) {
    console.log('Uploaded:', result.data.id);
} else {
    console.error('Failed:', result.error);
}
```

---

## ADR-010: Configuration Over Code

**Status:** ✅ REQUIRED

**Decision:** Entity-specific behavior driven by configuration, not code changes.

### EntityDocumentConfig Pattern

```typescript
// Configuration file (add new entities here)
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': {
        entityName: 'sprk_matter',
        lookupFieldName: 'sprk_matter',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_matternumber',
        entitySetName: 'sprk_matters'
    },
    // Add new entity - NO code changes needed
    'sprk_project': {
        entityName: 'sprk_project',
        lookupFieldName: 'sprk_project',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_projectname',
        entitySetName: 'sprk_projects'
    }
};

// Service uses configuration (generic code)
class DocumentRecordService {
    private getEntityConfig(entityName: string): EntityDocumentConfig {
        const config = ENTITY_DOCUMENT_CONFIGS[entityName];
        if (!config) {
            throw new Error(`No configuration found for entity: ${entityName}`);
        }
        return config;
    }

    async createDocument(file: UploadedFile, parentContext: ParentContext): Promise<string> {
        const config = this.getEntityConfig(parentContext.entityName);

        const payload = {
            sprk_documentname: file.name,
            sprk_graphitemid: file.itemId,
            [config.lookupFieldName]: null,
            [`${config.lookupFieldName}@odata.bind`]: `/${config.entitySetName}(${parentContext.recordId})`
        };

        const result = await Xrm.WebApi.createRecord('sprk_document', payload);
        return result.id;
    }
}
```

---

## Compliance Summary

| ADR | Title | Status | Priority |
|-----|-------|--------|----------|
| ADR-001 | Fluent UI v9 Components | ✅ Compliant | CRITICAL |
| ADR-002 | TypeScript Strict Mode | ✅ Compliant | HIGH |
| ADR-003 | Separation of Concerns | ✅ Compliant | HIGH |
| ADR-004 | Single Responsibility | ✅ Compliant | MEDIUM |
| ADR-005 | Error Handling Strategy | ✅ Compliant | HIGH |
| ADR-006 | Logging Standards | ✅ Compliant | MEDIUM |
| ADR-007 | Dependency Injection | ✅ Compliant | MEDIUM |
| ADR-008 | Immutable State | ✅ Compliant | MEDIUM |
| ADR-009 | API Response Wrapping | ✅ Compliant | HIGH |
| ADR-010 | Configuration Over Code | ✅ Compliant | HIGH |

---

**Next Step:** Review [PHASE-1-SETUP.md](./PHASE-1-SETUP.md) to begin implementation.
