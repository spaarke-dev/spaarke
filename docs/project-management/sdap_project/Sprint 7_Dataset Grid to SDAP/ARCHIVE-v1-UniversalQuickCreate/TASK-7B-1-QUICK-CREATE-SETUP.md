# Sprint 7B - Task 1: Universal Quick Create PCF Setup

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 1 of 4
**Estimated Time**: 1-2 days
**Priority**: High
**Status**: Pending

---

## Task Overview

Create a new Universal Quick Create PCF control using React 18 + Fluent UI v9 that can be used to create new entity records with file upload to SharePoint Embedded. This control must retrieve parent entity context from Power Apps, load parent record data for default values, and provide a standard Quick Create user experience.

---

## Success Criteria

- ✅ New PCF project created: `UniversalQuickCreate`
- ✅ React 18.2.0 + Fluent UI v9.54.0 integrated
- ✅ PCF retrieves parent context via `context.mode.contextInfo`
- ✅ PCF loads parent record data via `context.webAPI.retrieveRecord()`
- ✅ Quick Create form renders with file picker at top
- ✅ Form supports dynamic field rendering based on entity type
- ✅ Standard Power Apps Quick Create UX (slide-in panel)
- ✅ Bundle size <400 KB
- ✅ Zero TypeScript errors, strict mode enabled

---

## Context & Background

### What We're Building

A **universal** Quick Create PCF control that:
1. Can be launched from any subgrid's "+ New" button
2. Automatically receives parent entity context from Power Apps
3. Retrieves parent record data to populate default values
4. Displays file picker for file upload (top of form)
5. Shows configurable metadata fields
6. **Uses MSAL authentication for SDAP API calls (Sprint 8 pattern)**
7. Uploads file to SharePoint Embedded via SDAP API
8. Creates Dataverse record with all metadata
9. Works across multiple entity types (Document, Task, Contact, etc.)

### 🔴 CRITICAL: MSAL Authentication Required

**Sprint 7B MUST use MSAL from day one** (lesson learned from Sprint 8):

✅ **Use these patterns:**
- Initialize MSAL in `init()` method: `MsalAuthProvider.getInstance().initialize()`
- Create SDAP API client via factory: `SdapApiClientFactory.create(baseUrl)`
- Handle race conditions (user clicks Save before MSAL ready)
- Use same Azure AD app registration as Sprint 7A/8

❌ **DO NOT:**
- Use PCF context tokens (`context.userSettings.accessToken`)
- Create separate authentication logic
- Bypass `SdapApiClientFactory`
- Hardcode tokens

**Reference Implementation:** Universal Dataset Grid v2.0.7 [index.ts:39-176](../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts#L39-L176)

### User Flow

```
1. User in Matter form → Documents subgrid
2. User clicks "+ New Document"
3. Power Apps launches Quick Create form
   └─> Provides context: regardingObjectId, regardingEntityName, entityName
4. Universal Quick Create PCF receives context
5. PCF retrieves parent Matter record data
6. Form renders with:
   - File picker at top (required)
   - Auto-populated fields from Matter (title, owner, container ID)
   - Optional metadata fields
7. User selects file, fills optional fields
8. User clicks Save
9. PCF uploads file to SPE, creates Dataverse record
10. Form closes, grid auto-refreshes
```

### Why Universal?

The same Quick Create PCF must work for:
- **Documents** from Matter (file upload, container ID from Matter)
- **Tasks** from Matter (no file upload in future versions)
- **Contacts** from Account (no file upload in future versions)
- Any other entity with parent context

Configuration determines which fields are shown and which parent fields map to defaults.

---

## Power Apps Context Sharing

### How Power Apps Provides Context

When Quick Create is launched from a subgrid, Power Apps **automatically** provides context via the `context` object:

```typescript
// In UniversalQuickCreatePCF.init()
const formContext = (context as any).mode?.contextInfo;

if (formContext) {
    const parentEntityName = formContext.regardingEntityName;  // "sprk_matter"
    const parentRecordId = formContext.regardingObjectId;      // Matter GUID
    const entityName = formContext.entityName;                 // "sprk_document"
}
```

**Key Properties**:
- `regardingEntityName`: Parent entity logical name (e.g., "sprk_matter")
- `regardingObjectId`: Parent record GUID
- `entityName`: Entity being created (e.g., "sprk_document")

### Retrieving Parent Record Data

Once you have `regardingObjectId` and `regardingEntityName`, retrieve full parent record:

```typescript
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
```

---

## Deliverables

### 1. PCF Project Structure

```
src/controls/UniversalQuickCreate/
├── UniversalQuickCreate/
│   ├── components/
│   │   ├── QuickCreateForm.tsx          # Main form component
│   │   ├── FilePickerField.tsx          # File upload field (single + multi)
│   │   ├── DynamicFormFields.tsx        # Configurable fields
│   │   └── FormActions.tsx              # Save/Cancel buttons
│   ├── services/
│   │   ├── auth/
│   │   │   └── MsalAuthProvider.ts      # 🔴 SHARED from Sprint 8 (symlink or copy)
│   │   ├── SdapApiClient.ts             # 🔴 SHARED from Sprint 7A (already MSAL-enabled)
│   │   ├── SdapApiClientFactory.ts      # 🔴 SHARED from Sprint 7A (creates MSAL client)
│   │   ├── FileUploadService.ts         # Single file upload (Task 2)
│   │   ├── MultiFileUploadService.ts    # Multi-file adaptive upload (Task 2)
│   │   └── DefaultValueMapper.ts        # Default value mapping (Task 3)
│   ├── types/
│   │   └── index.ts                     # Type definitions
│   ├── utils/
│   │   └── logger.ts                    # Logging utility
│   ├── index.ts                         # PCF entry point
│   ├── UniversalQuickCreatePCF.ts       # PCF wrapper class (with MSAL init)
│   └── ControlManifest.Input.xml        # PCF manifest
├── generated/                            # PCF generated files
└── package.json                          # 🔴 Include @azure/msal-browser@4.24.1
```

**Key Changes from Original Plan:**
- ✅ Added MSAL authentication services
- ✅ Shared services from Sprint 7A/8 (no duplication)
- ✅ Multi-file upload support (Task 2 enhancement)
- ✅ MSAL package dependency

### 2. PCF Manifest (ControlManifest.Input.xml)

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls" constructor="UniversalQuickCreate" version="1.0.0"
           display-name-key="Universal_Quick_Create_Display_Key"
           description-key="Universal_Quick_Create_Desc_Key"
           control-type="standard">

    <!-- Primary dataset (entity being created) -->
    <data-set name="dataset" display-name-key="Dataset_Display_Key">
      <property-set name="field" display-name-key="Field_Display_Key"
                    of-type="SingleLine.Text" usage="bound" required="false" />
    </data-set>

    <!-- Configuration: Default value mappings -->
    <property name="defaultValueMappings"
              display-name-key="Default_Value_Mappings"
              description-key="JSON mapping of parent fields to child default values"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value='{"sprk_matter":{"sprk_containerid":"sprk_containerid","sprk_name":"sprk_documenttitle","_ownerid_value":"ownerid"}}' />

    <!-- Configuration: Enable file upload -->
    <property name="enableFileUpload"
              display-name-key="Enable_File_Upload"
              description-key="Enable file upload field (for Documents)"
              of-type="TwoOptions"
              usage="input"
              required="false"
              default-value="true" />

    <!-- Configuration: SDAP API Base URL -->
    <property name="sdapApiBaseUrl"
              display-name-key="SDAP_API_Base_URL"
              description-key="Base URL for SDAP BFF API"
              of-type="SingleLine.Text"
              usage="input"
              required="false"
              default-value="https://localhost:7299/api" />

    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/UniversalQuickCreate.css" order="1" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### 3. PCF Wrapper Class (UniversalQuickCreatePCF.ts)

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import { QuickCreateForm, QuickCreateFormProps } from "./components/QuickCreateForm";
import { logger } from "./utils/logger";

export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container: HTMLDivElement;
    private reactRoot: ReactDOM.Root | null = null;
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;
    private authProvider: MsalAuthProvider; // 🔴 MSAL auth provider

    // Parent context
    private parentEntityName: string = '';
    private parentRecordId: string = '';
    private parentRecordData: ComponentFramework.WebApi.Entity | null = null;

    // Entity being created
    private entityName: string = '';

    // Configuration
    private defaultValueMappings: Record<string, Record<string, string>> = {};
    private enableFileUpload: boolean = true;
    private sdapApiBaseUrl: string = '';

    constructor() {
        logger.info('UniversalQuickCreate', 'Constructor called');
    }

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        logger.info('UniversalQuickCreate', 'Initializing PCF control');

        this.context = context;
        this.notifyOutputChanged = context.factory.bindOutputChanged;

        // Create container
        this.container = document.createElement("div");
        this.container.className = "universal-quick-create-container";
        context.parameters.dataset.openDatasetItem = this.onRecordClick.bind(this);

        // 🔴 CRITICAL: Initialize MSAL authentication (Phase 1)
        // This runs async in background - token acquisition happens in Phase 2 (Task 2)
        this.initializeMsalAsync(this.container);

        // Get parent context from Power Apps
        await this.loadParentContext(context);

        // Load configuration from manifest parameters
        this.loadConfiguration(context);

        // Create React root (React 18 API)
        this.reactRoot = ReactDOM.createRoot(this.container);

        logger.info('UniversalQuickCreate', 'PCF control initialized', {
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId,
            entityName: this.entityName,
            enableFileUpload: this.enableFileUpload
        });
    }

    /**
     * 🔴 Initialize MSAL authentication provider (Phase 1)
     *
     * Runs asynchronously in background. If initialization fails, displays error to user.
     * Token acquisition will be implemented in Task 2 (file upload).
     *
     * Pattern copied from Universal Dataset Grid v2.0.7 (Sprint 8)
     *
     * @param container - PCF container element for error display
     */
    private initializeMsalAsync(container: HTMLDivElement): void {
        (async () => {
            try {
                logger.info('UniversalQuickCreate', 'Initializing MSAL authentication...');

                // Get singleton instance of MsalAuthProvider
                this.authProvider = MsalAuthProvider.getInstance();

                // Initialize MSAL (validates config, creates PublicClientApplication)
                await this.authProvider.initialize();

                logger.info('UniversalQuickCreate', 'MSAL authentication initialized successfully ✅');

                // Check if user is authenticated (for logging only - Phase 1)
                const isAuth = this.authProvider.isAuthenticated();
                logger.info('UniversalQuickCreate', `User authenticated: ${isAuth}`);

                if (isAuth) {
                    const accountInfo = this.authProvider.getAccountDebugInfo();
                    logger.info('UniversalQuickCreate', 'Account info:', accountInfo);
                }

            } catch (error) {
                logger.error('UniversalQuickCreate', 'Failed to initialize MSAL:', error);

                // Show user-friendly error message
                this.showError(
                    container,
                    'Authentication initialization failed. Please refresh the page and try again. ' +
                    'If the problem persists, contact your administrator.'
                );
            }
        })();
    }

    /**
     * Display error message in control
     *
     * Shows user-friendly error when initialization or operations fail.
     * Used for MSAL errors, API errors, etc.
     *
     * @param container - PCF container element
     * @param message - Error message to display (user-friendly, no technical details)
     */
    private showError(container: HTMLDivElement, message: string): void {
        // Create error div
        const errorDiv = document.createElement('div');
        errorDiv.style.padding = '20px';
        errorDiv.style.color = '#a4262c'; // Office UI Fabric error red
        errorDiv.style.backgroundColor = '#fde7e9'; // Light red background
        errorDiv.style.border = '1px solid #a4262c';
        errorDiv.style.borderRadius = '4px';
        errorDiv.style.fontFamily = "'Segoe UI', Tahoma, Geneva, Verdana, sans-serif";
        errorDiv.style.fontSize = '14px';
        errorDiv.style.margin = '10px';

        // Add error icon + message
        errorDiv.innerHTML = `
            <div style="display: flex; align-items: center; gap: 10px;">
                <svg width="20" height="20" viewBox="0 0 20 20" fill="none" xmlns="http://www.w3.org/2000/svg">
                    <circle cx="10" cy="10" r="9" fill="#a4262c"/>
                    <path d="M10 6v4M10 14h.01" stroke="#fff" stroke-width="2" stroke-linecap="round"/>
                </svg>
                <div>
                    <strong>Error</strong><br/>
                    ${message}
                </div>
            </div>
        `;

        // Prepend to container (show at top)
        container.insertBefore(errorDiv, container.firstChild);
    }

    private async loadParentContext(context: ComponentFramework.Context<IInputs>): Promise<void> {
        try {
            // Get parent context (automatically provided by Power Apps)
            const formContext = (context as any).mode?.contextInfo;

            if (formContext) {
                this.parentEntityName = formContext.regardingEntityName || '';
                this.parentRecordId = formContext.regardingObjectId || '';
                this.entityName = formContext.entityName || '';

                logger.info('UniversalQuickCreate', 'Parent context retrieved', {
                    parentEntityName: this.parentEntityName,
                    parentRecordId: this.parentRecordId,
                    entityName: this.entityName
                });

                // Retrieve parent record data for default values
                if (this.parentRecordId && this.parentEntityName) {
                    await this.loadParentRecordData(context);
                }
            } else {
                logger.warn('UniversalQuickCreate', 'No parent context found - Quick Create may have been launched directly');
            }

        } catch (error) {
            logger.error('UniversalQuickCreate', 'Failed to load parent context', error);
        }
    }

    private async loadParentRecordData(context: ComponentFramework.Context<IInputs>): Promise<void> {
        try {
            logger.info('UniversalQuickCreate', 'Loading parent record data', {
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

            logger.info('UniversalQuickCreate', 'Parent record data loaded', this.parentRecordData);

        } catch (error) {
            logger.error('UniversalQuickCreate', 'Failed to load parent record data', error);
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

    private loadConfiguration(context: ComponentFramework.Context<IInputs>): void {
        // Load default value mappings
        const mappingJson = context.parameters.defaultValueMappings?.raw;
        if (mappingJson) {
            try {
                this.defaultValueMappings = JSON.parse(mappingJson);
                logger.info('UniversalQuickCreate', 'Default value mappings loaded', this.defaultValueMappings);
            } catch (error) {
                logger.error('UniversalQuickCreate', 'Failed to parse default value mappings', error);
            }
        }

        // Load file upload setting
        this.enableFileUpload = context.parameters.enableFileUpload?.raw === "true" ||
                                context.parameters.enableFileUpload?.raw === true;

        // Load SDAP API base URL
        this.sdapApiBaseUrl = context.parameters.sdapApiBaseUrl?.raw ||
                             'https://localhost:7299/api';

        logger.info('UniversalQuickCreate', 'Configuration loaded', {
            enableFileUpload: this.enableFileUpload,
            sdapApiBaseUrl: this.sdapApiBaseUrl
        });
    }

    private getDefaultValues(): Record<string, any> {
        const defaults: Record<string, any> = {};

        if (!this.parentRecordData || !this.parentEntityName) {
            return defaults;
        }

        // Get mapping for this parent entity type
        const mappings = this.defaultValueMappings[this.parentEntityName];

        if (!mappings) {
            logger.warn('UniversalQuickCreate', 'No default value mappings found for parent entity', {
                parentEntityName: this.parentEntityName
            });
            return defaults;
        }

        // Apply mappings
        for (const [parentField, childField] of Object.entries(mappings)) {
            const parentValue = this.parentRecordData[parentField];

            if (parentValue !== undefined && parentValue !== null) {
                defaults[childField] = parentValue;

                logger.debug('UniversalQuickCreate', 'Mapped default value', {
                    parentField,
                    childField,
                    value: parentValue
                });
            }
        }

        return defaults;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.context = context;

        const defaultValues = this.getDefaultValues();

        // Pass to React component
        const props: QuickCreateFormProps = {
            entityName: this.entityName,
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId,
            defaultValues: defaultValues,
            enableFileUpload: this.enableFileUpload,
            sdapApiBaseUrl: this.sdapApiBaseUrl,
            context: context,
            onSave: this.handleSave.bind(this),
            onCancel: this.handleCancel.bind(this)
        };

        if (this.reactRoot) {
            this.reactRoot.render(React.createElement(QuickCreateForm, props));
        }
    }

    private async handleSave(formData: Record<string, any>, file?: File): Promise<void> {
        logger.info('UniversalQuickCreate', 'Save requested', { formData, hasFile: !!file });

        try {
            // Implementation in Task 2: File Upload with SPE Integration
            // 1. Upload file to SPE via SDAP API (if file provided)
            // 2. Get SPE metadata (URL, item ID, size)
            // 3. Create Dataverse record with form data + SPE metadata
            // 4. Close Quick Create form

            logger.info('UniversalQuickCreate', 'Save complete');

        } catch (error) {
            logger.error('UniversalQuickCreate', 'Save failed', error);
            throw error;
        }
    }

    private handleCancel(): void {
        logger.info('UniversalQuickCreate', 'Cancel requested');
        // Close Quick Create form (Power Apps handles this automatically)
    }

    private onRecordClick(reference: ComponentFramework.EntityReference): void {
        // Not used in Quick Create (used in Dataset Grid)
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        logger.info('UniversalQuickCreate', 'Destroying PCF control');

        // 🔴 Clear MSAL token cache (optional - sessionStorage will be cleared on tab close)
        if (this.authProvider) {
            logger.info('UniversalQuickCreate', 'Clearing MSAL token cache');
            this.authProvider.clearCache();
        }

        if (this.reactRoot) {
            this.reactRoot.unmount();
            this.reactRoot = null;
        }
    }
}
```

### 4. React Component (QuickCreateForm.tsx)

```typescript
import * as React from 'react';
import {
    FluentProvider,
    webLightTheme,
    makeStyles,
    Button,
    Field,
    Input,
    Textarea,
    Spinner
} from '@fluentui/react-components';
import { logger } from '../utils/logger';
import { FilePickerField } from './FilePickerField';

const useStyles = makeStyles({
    container: {
        padding: '20px',
        maxWidth: '600px',
        margin: '0 auto'
    },
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: '16px'
    },
    actions: {
        display: 'flex',
        gap: '12px',
        justifyContent: 'flex-end',
        marginTop: '24px'
    },
    loadingOverlay: {
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(255, 255, 255, 0.8)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000
    }
});

export interface QuickCreateFormProps {
    entityName: string;
    parentEntityName: string;
    parentRecordId: string;
    defaultValues: Record<string, any>;
    enableFileUpload: boolean;
    sdapApiBaseUrl: string;
    context: ComponentFramework.Context<any>;
    onSave: (formData: Record<string, any>, file?: File) => Promise<void>;
    onCancel: () => void;
}

export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    entityName,
    parentEntityName,
    parentRecordId,
    defaultValues,
    enableFileUpload,
    sdapApiBaseUrl,
    context,
    onSave,
    onCancel
}) => {
    const styles = useStyles();

    // Form state
    const [formData, setFormData] = React.useState<Record<string, any>>(defaultValues);
    const [selectedFile, setSelectedFile] = React.useState<File | undefined>();
    const [isSaving, setIsSaving] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);

    // Update form data when default values change
    React.useEffect(() => {
        setFormData(defaultValues);
        logger.info('QuickCreateForm', 'Default values applied', defaultValues);
    }, [defaultValues]);

    const handleFileChange = React.useCallback((file: File | undefined) => {
        setSelectedFile(file);
        logger.info('QuickCreateForm', 'File selected', { fileName: file?.name });
    }, []);

    const handleFieldChange = React.useCallback((fieldName: string, value: any) => {
        setFormData(prev => ({
            ...prev,
            [fieldName]: value
        }));
    }, []);

    const handleSubmit = React.useCallback(async (e: React.FormEvent) => {
        e.preventDefault();
        setError(null);

        // Validate file upload if required
        if (enableFileUpload && !selectedFile) {
            setError('Please select a file to upload.');
            return;
        }

        setIsSaving(true);

        try {
            logger.info('QuickCreateForm', 'Submitting form', { formData, hasFile: !!selectedFile });

            await onSave(formData, selectedFile);

            logger.info('QuickCreateForm', 'Form submitted successfully');

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Unknown error occurred';
            logger.error('QuickCreateForm', 'Form submission failed', err);
            setError(errorMessage);
        } finally {
            setIsSaving(false);
        }
    }, [formData, selectedFile, enableFileUpload, onSave]);

    return (
        <FluentProvider theme={webLightTheme}>
            <div className={styles.container}>
                <form onSubmit={handleSubmit} className={styles.form}>
                    {/* File Upload Field (if enabled) */}
                    {enableFileUpload && (
                        <FilePickerField
                            value={selectedFile}
                            onChange={handleFileChange}
                            required={true}
                        />
                    )}

                    {/* Dynamic form fields based on entity type */}
                    {/* For Sprint 7B Task 1, we'll render basic fields */}
                    {/* Task 3 will make this fully configurable */}

                    {entityName === 'sprk_document' && (
                        <>
                            <Field label="Document Title">
                                <Input
                                    value={formData.sprk_documenttitle || ''}
                                    onChange={(e, data) => handleFieldChange('sprk_documenttitle', data.value)}
                                />
                            </Field>

                            <Field label="Description">
                                <Textarea
                                    value={formData.sprk_description || ''}
                                    onChange={(e, data) => handleFieldChange('sprk_description', data.value)}
                                    rows={3}
                                />
                            </Field>
                        </>
                    )}

                    {/* Error message */}
                    {error && (
                        <div style={{ color: 'red', fontSize: '14px' }}>
                            {error}
                        </div>
                    )}

                    {/* Actions */}
                    <div className={styles.actions}>
                        <Button
                            appearance="secondary"
                            onClick={onCancel}
                            disabled={isSaving}
                        >
                            Cancel
                        </Button>
                        <Button
                            appearance="primary"
                            type="submit"
                            disabled={isSaving}
                        >
                            {isSaving ? 'Saving...' : 'Save'}
                        </Button>
                    </div>
                </form>

                {/* Loading overlay */}
                {isSaving && (
                    <div className={styles.loadingOverlay}>
                        <Spinner label="Saving..." size="large" />
                    </div>
                )}
            </div>
        </FluentProvider>
    );
};
```

### 5. File Picker Component (FilePickerField.tsx)

```typescript
import * as React from 'react';
import { Field, Input, makeStyles } from '@fluentui/react-components';
import { logger } from '../utils/logger';

const useStyles = makeStyles({
    fileInfo: {
        marginTop: '8px',
        fontSize: '12px',
        color: '#666'
    }
});

export interface FilePickerFieldProps {
    value?: File;
    onChange: (file: File | undefined) => void;
    required?: boolean;
}

export const FilePickerField: React.FC<FilePickerFieldProps> = ({
    value,
    onChange,
    required = false
}) => {
    const styles = useStyles();

    const handleFileChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        onChange(file);

        if (file) {
            logger.info('FilePickerField', 'File selected', {
                name: file.name,
                size: file.size,
                type: file.type
            });
        }
    }, [onChange]);

    const formatFileSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    return (
        <Field label="Select File" required={required}>
            <Input
                type="file"
                onChange={handleFileChange}
            />
            {value && (
                <div className={styles.fileInfo}>
                    {value.name} ({formatFileSize(value.size)})
                </div>
            )}
        </Field>
    );
};
```

---

## Implementation Steps

### Step 1: Create PCF Project (30 min)

```bash
# Navigate to controls directory
cd c:\code_files\spaarke\src\controls

# Create new PCF project
pac pcf init --namespace Spaarke.Controls --name UniversalQuickCreate --template dataset --run-npm-install

cd UniversalQuickCreate
```

### Step 2: Install Dependencies (15 min)

```bash
# React + Fluent UI
npm install react@18.2.0 react-dom@18.2.0
npm install @fluentui/react-components@9.54.0
npm install --save-dev @types/react@18.2.0 @types/react-dom@18.2.0

# 🔴 CRITICAL: MSAL Authentication (Sprint 8 requirement)
npm install @azure/msal-browser@4.24.1
```

Update `package.json` with peer dependencies handling.

### Step 3: Update Manifest (30 min)

1. Edit `ControlManifest.Input.xml` with configuration from Deliverables section
2. Add `defaultValueMappings`, `enableFileUpload`, `sdapApiBaseUrl` properties
3. Run `npm run build` to regenerate types

### Step 4: Create Project Structure (15 min)

```bash
mkdir components services types utils
```

### Step 5: Copy/Link Shared Services (30 min)

**Copy logger utility:**

```bash
cp ../UniversalDatasetGrid/UniversalDatasetGrid/utils/logger.ts utils/
```

**🔴 CRITICAL: Copy MSAL and SDAP services from Sprint 7A/8:**

```bash
# Create services directory structure
mkdir -p services/auth

# Copy MSAL authentication (from Sprint 8)
cp ../UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts services/auth/

# Copy SDAP API client services (from Sprint 7A - already MSAL-enabled)
cp ../UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts services/
cp ../UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts services/

# Copy types (for SpeFileMetadata, ServiceResult, etc.)
cp ../UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts types/
```

**Why copy instead of npm package?**
- PCF controls bundle all dependencies (no shared packages between controls)
- Ensures version consistency
- Allows customization if needed

**Verify copies:**
```bash
ls services/auth/MsalAuthProvider.ts
ls services/SdapApiClient.ts
ls services/SdapApiClientFactory.ts
```

### Step 6: Implement PCF Wrapper Class (1-2 hours)

Create `UniversalQuickCreatePCF.ts` with code from Deliverables section.

Key focus areas:
- **🔴 MSAL initialization in init() method** (async background)
- Parent context retrieval
- Parent record data loading
- Default value mapping
- Configuration loading
- **🔴 Error display for MSAL failures**
- **🔴 MSAL cache clearing in destroy() method**

### Step 7: Implement React Components (2-3 hours)

1. Create `QuickCreateForm.tsx` - Main form component
2. Create `FilePickerField.tsx` - File picker component
3. Create `FormActions.tsx` - Save/Cancel buttons
4. Add basic styling

### Step 8: Build & Test Locally (1 hour)

```bash
npm run build
npm start watch
```

Test in a test harness environment:
- Verify form renders
- Verify file picker works
- Verify Save/Cancel buttons
- Check console for logger output

### Step 9: Bundle Size Validation (30 min)

```bash
npm run build

# Check bundle size
ls -lh out/controls
```

Target: <400 KB

If over target:
- Enable tree-shaking
- Review dependencies
- Consider code splitting

### Step 10: Deploy to Dataverse (1 hour)

```bash
# Build solution
cd ../..
pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path src/controls/UniversalQuickCreate

# Pack solution
msbuild /t:build /restore

# Deploy to Dataverse
pac solution import --path bin/Debug/Solution.zip
```

---

## Testing Checklist

### Local Testing (Test Harness)

- [ ] PCF control loads without errors
- [ ] Form renders with file picker at top
- [ ] File picker allows file selection
- [ ] File info displays (name, size)
- [ ] Save button is enabled when file selected
- [ ] Cancel button works
- [ ] Logger outputs appear in console

### Dataverse Testing (Matter → Document)

- [ ] Quick Create launches from "+ New Document" on Matter form
- [ ] **🔴 MSAL initializes in background (check console logs)**
- [ ] **🔴 User authentication detected (console: "User authenticated: true")**
- [ ] PCF retrieves parent Matter context
- [ ] PCF loads Matter record data
- [ ] Form displays with pre-populated defaults:
  - [ ] Document Title = Matter Name
  - [ ] Owner = Matter Owner
  - [ ] Container ID populated (hidden field)
- [ ] File picker appears at top
- [ ] User can select file
- [ ] Save button enabled (file upload in Task 2)
- [ ] Cancel closes form

**Expected Console Output:**
```
[UniversalQuickCreate] Initializing MSAL authentication...
[MsalAuthProvider] Initializing MSAL...
[MsalAuthProvider] MSAL initialized successfully
[UniversalQuickCreate] MSAL authentication initialized successfully ✅
[UniversalQuickCreate] User authenticated: true
[UniversalQuickCreate] Account info: { username: "user@domain.com", ... }
```

### Bundle Size

- [ ] Bundle <400 KB
- [ ] No duplicate dependencies
- [ ] Tree-shaking enabled

### Error Handling

- [ ] Parent context missing → graceful degradation
- [ ] Parent record load fails → logs error, continues
- [ ] Invalid mapping JSON → logs error, uses empty mappings

---

## Common Issues & Solutions

### Issue 1: Parent Context Not Available

**Symptom**: `formContext` is undefined or null

**Causes**:
- Quick Create launched directly (not from subgrid)
- Power Apps version too old

**Solution**:
```typescript
if (!formContext) {
    logger.warn('QuickCreate', 'No parent context - using standalone mode');
    // Fallback: Allow form to work without parent defaults
}
```

### Issue 2: webAPI.retrieveRecord Fails

**Symptom**: "Record not found" or permission error

**Causes**:
- Insufficient permissions
- Parent record deleted
- Invalid entity name

**Solution**:
```typescript
try {
    this.parentRecordData = await context.webAPI.retrieveRecord(...);
} catch (error) {
    logger.error('QuickCreate', 'Failed to load parent - using empty defaults', error);
    this.parentRecordData = null; // Continue without defaults
}
```

### Issue 3: Bundle Size Too Large

**Symptom**: Bundle >400 KB

**Solutions**:
1. Check for duplicate React/ReactDOM
2. Enable webpack tree-shaking
3. Use Fluent UI v9 (smaller than v8)
4. Lazy load components

---

## Success Metrics

- ✅ Bundle size <400 KB (may increase slightly due to MSAL, acceptable up to 500 KB)
- ✅ Zero TypeScript errors (strict mode)
- ✅ PCF loads <1s
- ✅ **🔴 MSAL authentication initializes successfully in background**
- ✅ **🔴 User authentication status logged (for verification)**
- ✅ Parent context retrieved 100% (when launched from subgrid)
- ✅ Default values populated correctly
- ✅ Standard Quick Create UX maintained

**Note:** Bundle size may increase from ~200 KB to ~450 KB due to MSAL library. This is acceptable as authentication is critical for security.

---

## Next Steps

After completing this task:

1. **Task 2**: File Upload with SPE Integration
   - Implement file upload to SharePoint Embedded
   - Create Dataverse record with metadata
   - Handle upload progress and errors

2. **Task 3**: Configurable Default Value Mappings
   - Make field rendering fully dynamic
   - Support multiple entity types
   - Document configuration patterns

3. **Task 4**: Testing, Bundle Size & Deployment
   - Integration tests
   - Manual testing checklist
   - Production deployment

---

## References

- **Master Resource**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)
- **Architecture Decision**: [ARCHITECTURE-DECISION-TWO-PCF-APPROACH.md](ARCHITECTURE-DECISION-TWO-PCF-APPROACH.md)
- **Sprint 7A Task 1**: [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) - Shared SDAP API client
- **PCF Documentation**: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/
- **React 18 Docs**: https://react.dev/
- **Fluent UI v9**: https://react.fluentui.dev/

---

**AI Coding Prompt**:

```
Create a Universal Quick Create PCF control using React 18 and Fluent UI v9 that:

1. Retrieves parent entity context from Power Apps via context.mode.contextInfo
2. Loads parent record data using context.webAPI.retrieveRecord()
3. Renders a Quick Create form with:
   - File picker field at top (FilePickerField component)
   - Auto-populated fields from parent entity
   - Optional metadata fields
   - Save/Cancel buttons

4. Implements the following classes:
   - UniversalQuickCreatePCF.ts: PCF wrapper class with parent context loading
   - QuickCreateForm.tsx: Main React form component
   - FilePickerField.tsx: File upload field component

5. Uses the logger utility for all operations
6. Handles errors gracefully (parent context missing, record load fails)
7. Targets bundle size <400 KB
8. Follows existing code patterns from Universal Dataset Grid v2.0.7

Key technical requirements:
- React 18.2.0 with createRoot() API
- Fluent UI v9.54.0
- TypeScript strict mode
- Power Apps context via context.mode.contextInfo
- Parent data via context.webAPI.retrieveRecord()

Refer to SPRINT-7-MASTER-RESOURCE.md for context sharing patterns and default value mapping examples.
```
