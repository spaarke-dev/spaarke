# Work Item 2: Create FileUploadPCF.ts

**Estimated Time:** 2 hours
**Prerequisites:** Work Item 1 complete (Manifest updated)
**Status:** Ready to Start

---

## Objective

Create the core PCF control file that handles file upload to SharePoint Embedded.

---

## Context

This is a **field-bound PCF control** that:
- Binds to `sprk_fileuploadmetadata` field on Quick Create form
- Renders file picker UI (React component)
- Handles file upload to SPE via SDAP API
- Writes SPE metadata JSON to bound field
- Supports single and multiple file uploads

**Key Difference from Dataset Controls:**
- Uses `ComponentFramework.PropertyTypes.StringProperty` (not DataSet)
- Implements `getOutputs()` to write metadata back to form field
- Simpler lifecycle: init → updateView → getOutputs → destroy

---

## File to Create

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts`

**Replace:** This will replace `UniversalQuickCreatePCF.ts` (archive old file first)

---

## Architecture Overview

```typescript
┌─────────────────────────────────────────────────────┐
│ FileUploadPCF.ts (Main Control)                     │
├─────────────────────────────────────────────────────┤
│                                                       │
│  init()                                              │
│   ├─ Initialize MSAL authentication                 │
│   ├─ Get parent context (Matter → Container ID)     │
│   └─ Render FileUploadField.tsx                     │
│                                                       │
│  handleFilesSelected(files: File[])                 │
│   ├─ Upload each file to SPE                        │
│   ├─ Collect SPE metadata                           │
│   └─ Trigger getOutputs()                           │
│                                                       │
│  getOutputs()                                        │
│   └─ Return SPE metadata JSON → speMetadata field   │
│                                                       │
│  destroy()                                           │
│   └─ Cleanup React root                             │
│                                                       │
└─────────────────────────────────────────────────────┘
```

---

## Step-by-Step Instructions

### Step 1: Archive Old File

```bash
# Create archive folder
mkdir -p /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/ARCHIVE-v1

# Move old file
mv /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalQuickCreatePCF.ts \
   /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/ARCHIVE-v1/
```

---

### Step 2: Create FileUploadPCF.ts

Create new file at: `src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts`

```typescript
/**
 * SPE File Upload PCF Control
 *
 * Field-bound control for uploading files to SharePoint Embedded from Quick Create forms.
 * Binds to sprk_fileuploadmetadata field to store SPE metadata JSON.
 *
 * @version 2.0.0
 */

import * as React from 'react';
import { createRoot, Root } from 'react-dom/client';
import { IInputs, IOutputs } from './generated/ManifestTypes';
import { MsalAuthProvider } from './services/auth/MsalAuthProvider';
import { SdapApiClientFactory } from './services/SdapApiClientFactory';
import { FileUploadService } from './services/FileUploadService';
import { SpeFileMetadata } from './types';
import { logger } from './utils/logger';
import { FileUploadField } from './components/FileUploadField';

/**
 * PCF Control State
 */
interface ControlState {
    /** Uploaded file metadata (written to bound field) */
    speMetadata: SpeFileMetadata[];
    /** Container ID from parent Matter */
    containerId: string | null;
    /** Loading state */
    isLoading: boolean;
    /** Error message */
    error: string | null;
}

export class SpeFileUpload implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private context: ComponentFramework.Context<IInputs>;
    private container: HTMLDivElement;
    private reactRoot: Root | null = null;

    // Services
    private authProvider: MsalAuthProvider | null = null;
    private fileUploadService: FileUploadService | null = null;

    // State
    private state: ControlState = {
        speMetadata: [],
        containerId: null,
        isLoading: false,
        error: null
    };

    /**
     * Initialize the control
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void,
        state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        logger.info('FileUploadPCF', 'Initializing SPE File Upload control');

        this.context = context;
        this.container = container;

        // Initialize services
        this.initializeServices();

        // Get Container ID from parent context
        this.loadContainerIdFromParent();

        // Render React UI
        this.renderUI(notifyOutputChanged);
    }

    /**
     * Initialize MSAL and file upload services
     */
    private async initializeServices(): Promise<void> {
        try {
            // Get SDAP API URL from parameters
            const sdapApiUrl = this.context.parameters.sdapApiBaseUrl.raw || 'https://localhost:7299/api';

            logger.info('FileUploadPCF', 'Initializing services', { sdapApiUrl });

            // Initialize MSAL authentication
            this.authProvider = new MsalAuthProvider();
            await this.authProvider.initialize();

            // Create SDAP API client
            const apiClient = SdapApiClientFactory.create(sdapApiUrl, this.authProvider);

            // Create file upload service
            this.fileUploadService = new FileUploadService(apiClient);

            logger.info('FileUploadPCF', 'Services initialized successfully');
        } catch (error) {
            logger.error('FileUploadPCF', 'Failed to initialize services', error);
            this.setState({ error: 'Failed to initialize authentication' });
        }
    }

    /**
     * Load Container ID from parent Matter record
     */
    private async loadContainerIdFromParent(): Promise<void> {
        try {
            // Check if containerid parameter is provided (manual override)
            const containerIdParam = this.context.parameters.containerid?.raw;
            if (containerIdParam) {
                logger.info('FileUploadPCF', 'Using Container ID from parameter', { containerIdParam });
                this.setState({ containerId: containerIdParam });
                return;
            }

            // Get parent entity context
            const formContext = this.context.mode as any;
            const parentEntityName = formContext?.contextInfo?.entityTypeName;
            const parentRecordId = formContext?.contextInfo?.entityId;

            if (!parentEntityName || !parentRecordId) {
                logger.warn('FileUploadPCF', 'No parent context found - user must enter Container ID manually');
                return;
            }

            logger.info('FileUploadPCF', 'Retrieving Container ID from parent', {
                parentEntityName,
                parentRecordId
            });

            // Retrieve Container ID from parent Matter
            const result = await this.context.webAPI.retrieveRecord(
                parentEntityName,
                parentRecordId,
                '?$select=sprk_containerid'
            );

            const containerId = result.sprk_containerid;

            if (containerId) {
                logger.info('FileUploadPCF', 'Container ID loaded successfully', { containerId });
                this.setState({ containerId });
            } else {
                logger.warn('FileUploadPCF', 'Parent record has no Container ID');
                this.setState({ error: 'Parent Matter has no Container ID. Please provision a SharePoint container first.' });
            }
        } catch (error) {
            logger.error('FileUploadPCF', 'Failed to load Container ID from parent', error);
            this.setState({ error: 'Failed to load Container ID from parent record' });
        }
    }

    /**
     * Handle file selection and upload
     */
    private handleFilesSelected = async (files: File[]): Promise<void> => {
        if (!this.fileUploadService) {
            this.setState({ error: 'File upload service not initialized' });
            return;
        }

        if (!this.state.containerId) {
            this.setState({ error: 'Container ID not found. Cannot upload files.' });
            return;
        }

        logger.info('FileUploadPCF', 'Files selected for upload', {
            fileCount: files.length,
            containerId: this.state.containerId
        });

        this.setState({ isLoading: true, error: null });

        const uploadedMetadata: SpeFileMetadata[] = [];

        try {
            // Upload each file
            for (const file of files) {
                logger.info('FileUploadPCF', 'Uploading file', { fileName: file.name });

                const result = await this.fileUploadService.uploadFile({
                    file,
                    containerId: this.state.containerId,
                    fileName: file.name
                });

                if (result.success && result.data) {
                    uploadedMetadata.push(result.data);
                    logger.info('FileUploadPCF', 'File uploaded successfully', {
                        fileName: file.name,
                        driveItemId: result.data.driveItemId
                    });
                } else {
                    throw new Error(result.error || 'File upload failed');
                }
            }

            // Update state with uploaded metadata
            this.setState({
                speMetadata: uploadedMetadata,
                isLoading: false
            });

            // Notify framework that outputs changed (triggers getOutputs())
            this.context.parameters.speMetadata.notifyOutputChanged?.();

            logger.info('FileUploadPCF', 'All files uploaded successfully', {
                uploadedCount: uploadedMetadata.length
            });
        } catch (error) {
            logger.error('FileUploadPCF', 'File upload failed', error);
            this.setState({
                isLoading: false,
                error: error instanceof Error ? error.message : 'File upload failed'
            });
        }
    };

    /**
     * Render React UI
     */
    private renderUI(notifyOutputChanged: () => void): void {
        const allowMultipleFiles = this.context.parameters.allowMultipleFiles.raw ?? true;

        const element = React.createElement(FileUploadField, {
            allowMultiple: allowMultipleFiles,
            containerId: this.state.containerId,
            isLoading: this.state.isLoading,
            error: this.state.error,
            uploadedFiles: this.state.speMetadata,
            onFilesSelected: this.handleFilesSelected
        });

        if (!this.reactRoot) {
            this.reactRoot = createRoot(this.container);
        }

        this.reactRoot.render(element);
    }

    /**
     * Update state and re-render
     */
    private setState(partialState: Partial<ControlState>): void {
        this.state = { ...this.state, ...partialState };
        this.renderUI(() => {});
    }

    /**
     * Called when the control needs to update the view
     */
    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;
        this.renderUI(() => {});
    }

    /**
     * Return outputs to framework (write SPE metadata to bound field)
     */
    public getOutputs(): IOutputs {
        logger.info('FileUploadPCF', 'getOutputs() called', {
            metadataCount: this.state.speMetadata.length
        });

        // Convert metadata array to JSON string
        const metadataJson = JSON.stringify(this.state.speMetadata);

        return {
            speMetadata: metadataJson
        };
    }

    /**
     * Cleanup when control is destroyed
     */
    public destroy(): void {
        logger.info('FileUploadPCF', 'Destroying control');

        if (this.reactRoot) {
            this.reactRoot.unmount();
            this.reactRoot = null;
        }
    }
}
```

---

## Key Implementation Details

### 1. Field Binding

```typescript
// Manifest defines:
<property name="speMetadata" usage="bound" of-type="Multiple" />

// Control reads from:
this.context.parameters.speMetadata.raw  // Current field value (string)

// Control writes to:
public getOutputs(): IOutputs {
    return {
        speMetadata: JSON.stringify(this.state.speMetadata)
    };
}
```

### 2. Parent Context Access

```typescript
// Get parent entity context (Matter)
const formContext = this.context.mode as any;
const parentEntityName = formContext?.contextInfo?.entityTypeName;  // "sprk_matter"
const parentRecordId = formContext?.contextInfo?.entityId;  // GUID

// Retrieve Container ID from parent
const result = await this.context.webAPI.retrieveRecord(
    parentEntityName,
    parentRecordId,
    '?$select=sprk_containerid'
);
```

**Important:** Parent context may not always be available in Quick Create forms. The control falls back to manual Container ID entry via `containerid` parameter.

### 3. Multi-File Upload

```typescript
// Upload files sequentially
for (const file of files) {
    const result = await this.fileUploadService.uploadFile({
        file,
        containerId: this.state.containerId,
        fileName: file.name
    });

    if (result.success) {
        uploadedMetadata.push(result.data);
    }
}

// All metadata written to single field as JSON array
this.state.speMetadata = [metadata1, metadata2, metadata3];
```

---

## Verification

After creating this file, verify:

1. **File exists:**
   ```bash
   ls -la /c/code_files/spaarke/src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts
   ```

2. **Imports are correct:**
   - MsalAuthProvider from `./services/auth/MsalAuthProvider`
   - FileUploadService from `./services/FileUploadService`
   - SdapApiClientFactory from `./services/SdapApiClientFactory`
   - FileUploadField from `./components/FileUploadField` (Work Item 5)

3. **TypeScript compiles:**
   ```bash
   cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
   npm run build
   ```

   **Expected:** No errors (FileUploadField.tsx will be created in Work Item 5)

---

## Dependencies

### Required Files (Already Exist):
- ✅ `services/auth/MsalAuthProvider.ts`
- ✅ `services/FileUploadService.ts`
- ✅ `services/SdapApiClientFactory.ts`
- ✅ `types/index.ts` (SpeFileMetadata type)
- ✅ `utils/logger.ts`

### To Be Created:
- ⏳ `components/FileUploadField.tsx` (Work Item 5)
- ⏳ `generated/ManifestTypes.d.ts` (auto-generated after manifest update)

---

## Troubleshooting

### Error: Cannot find module './components/FileUploadField'

**Cause:** FileUploadField.tsx not created yet

**Fix:** Create stub component temporarily:

```typescript
// Temporary stub
export const FileUploadField = (props: any) => {
    return null;
};
```

Or skip Work Item 2 verification until Work Item 5 is complete.

---

### Error: Cannot read property 'contextInfo' of undefined

**Cause:** Parent context not available in Quick Create form

**Fix:** This is expected. Control should handle gracefully:
```typescript
if (!parentEntityName || !parentRecordId) {
    logger.warn('FileUploadPCF', 'No parent context - manual Container ID required');
    // User must enter Container ID via parameter
    return;
}
```

---

### Error: speMetadata property not found in IInputs

**Cause:** Manifest not updated or types not regenerated

**Fix:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm run build  # Regenerates ManifestTypes.d.ts
```

---

## Complete File Location

**Path:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/FileUploadPCF.ts`

**Size:** ~8 KB

**Lines:** ~300

---

## Next Steps

After completing this work item:

1. ✅ Verify file created successfully
2. ✅ Verify imports are correct
3. ⏳ Move to Work Item 3: Verify FileUploadService.ts
4. ⏳ Move to Work Item 5: Create FileUploadField.tsx (React UI)

---

**Status:** Ready for implementation
**Estimated Time:** 2 hours
**Next:** Work Item 3 - Verify File Upload Service
