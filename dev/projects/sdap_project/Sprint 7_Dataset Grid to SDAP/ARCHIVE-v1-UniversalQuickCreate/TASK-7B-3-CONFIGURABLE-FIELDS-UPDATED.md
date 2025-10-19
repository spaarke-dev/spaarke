# Sprint 7B - Task 3: Dynamic Field Rendering (UPDATED)

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 3 of 4
**Estimated Time**: 2-3 hours
**Priority**: High
**Status**: Pending
**Depends On**: Sprint 7B Task 2 (File Upload Complete)

**Updated**: 2025-10-07 - Simplified based on Tasks 1-2 completion

---

## Task Overview

Make the Universal Quick Create PCF support multiple entity types by implementing **dynamic field rendering**. Replace the hardcoded `sprk_document` fields with a configurable field definition system that allows the same control to work for Documents, Tasks, Contacts, and any other entity.

**Key Simplification:** Default value mapping already works (implemented in Tasks 1-2). This task focuses **only on dynamic field rendering**.

---

## Success Criteria

- ✅ DynamicFormFields component renders fields from configuration
- ✅ Support for field types: text, textarea, number, date, datetime, boolean, optionset
- ✅ Entity-specific field definitions (Document, Task, Contact)
- ✅ Read-only field support (container ID, owner, etc.)
- ✅ Validation support (required fields)
- ✅ Works with existing default value mapping (no changes needed)
- ✅ Zero breaking changes to Tasks 1-2
- ✅ Bundle size remains ~600-650 KB (acceptable with MSAL)

---

## What Already Works (Tasks 1-2)

### ✅ Default Value Mapping
Located in `UniversalQuickCreatePCF.ts` lines 273-304:

```typescript
private getDefaultValues(): Record<string, unknown> {
    const defaults: Record<string, unknown> = {};

    if (!this.parentRecordData || !this.parentEntityName) {
        return defaults;
    }

    // Get mapping for this parent entity type
    const mappings = this.defaultValueMappings[this.parentEntityName];

    if (!mappings) {
        logger.warn('UniversalQuickCreatePCF', 'No default value mappings found...');
        return defaults;
    }

    // Apply mappings
    for (const [parentField, childField] of Object.entries(mappings)) {
        const parentValue = this.parentRecordData[parentField];

        if (parentValue !== undefined && parentValue !== null) {
            defaults[childField] = parentValue;
        }
    }

    return defaults;
}
```

**Status:** ✅ Working, don't change

### ✅ Manifest Configuration
Located in `ControlManifest.Input.xml`:

```xml
<property name="defaultValueMappings"
          display-name-key="Default_Value_Mappings"
          description-key="JSON mapping of parent fields to child default values"
          of-type="SingleLine.Text"
          usage="input"
          required="false"
          default-value='{"sprk_matter":{"sprk_containerid":"sprk_containerid","sprk_name":"sprk_documenttitle","_ownerid_value":"ownerid"}}' />
```

**Status:** ✅ Working, don't change

---

## What Needs to Change

### ❌ Hardcoded Fields in QuickCreateForm.tsx
Located at lines 160-175:

```typescript
{entityName === 'sprk_document' && (
    <>
        <Field label="Document Title">
            <Input value={formData.sprk_documenttitle || ''} ... />
        </Field>
        <Field label="Description">
            <Textarea value={formData.sprk_description || ''} ... />
        </Field>
    </>
)}
```

**Problem:** Only works for Documents. Doesn't support Task, Contact, or other entities.

**Solution:** Replace with DynamicFormFields component that reads from field definitions.

---

## Deliverables

### 1. Field Definition Type (types/FieldMetadata.ts)

Create new file: `types/FieldMetadata.ts`

```typescript
/**
 * Field metadata for dynamic form rendering
 */
export interface FieldMetadata {
    /** Field logical name (e.g., "sprk_documenttitle") */
    name: string;

    /** Display label (e.g., "Document Title") */
    label: string;

    /** Field type */
    type: 'text' | 'textarea' | 'number' | 'date' | 'datetime' | 'boolean' | 'optionset';

    /** Is this field required? */
    required?: boolean;

    /** Maximum length for text fields */
    maxLength?: number;

    /** Options for optionset fields */
    options?: Array<{ label: string; value: string | number }>;

    /** Is this field read-only? (auto-populated from parent) */
    readOnly?: boolean;
}

/**
 * Entity field configuration
 */
export interface EntityFieldConfiguration {
    /** Entity logical name */
    entityName: string;

    /** Fields to render */
    fields: FieldMetadata[];

    /** Does this entity support file upload? */
    supportsFileUpload: boolean;
}
```

### 2. Entity Field Definitions (config/EntityFieldDefinitions.ts)

Create new file: `config/EntityFieldDefinitions.ts`

```typescript
import { EntityFieldConfiguration } from '../types/FieldMetadata';

/**
 * Hardcoded field definitions for supported entity types
 *
 * This is the practical approach since Power Apps doesn't expose
 * Quick Create form metadata to PCF controls.
 *
 * To add a new entity type:
 * 1. Add entry to this object
 * 2. Define fields array
 * 3. Set supportsFileUpload flag
 * 4. Update manifest defaultValueMappings if needed
 */
export const ENTITY_FIELD_DEFINITIONS: Record<string, EntityFieldConfiguration> = {
    /**
     * Document Entity (sprk_document)
     * Used for file-based records in SPE
     */
    'sprk_document': {
        entityName: 'sprk_document',
        supportsFileUpload: true,
        fields: [
            {
                name: 'sprk_documenttitle',
                label: 'Document Title',
                type: 'text',
                required: true,
                maxLength: 200
            },
            {
                name: 'sprk_description',
                label: 'Description',
                type: 'textarea',
                required: false
            }
        ]
    },

    /**
     * Task Entity (task)
     * Standard Dynamics 365 entity
     */
    'task': {
        entityName: 'task',
        supportsFileUpload: false,
        fields: [
            {
                name: 'subject',
                label: 'Subject',
                type: 'text',
                required: true,
                maxLength: 200
            },
            {
                name: 'description',
                label: 'Description',
                type: 'textarea',
                required: false
            },
            {
                name: 'scheduledend',
                label: 'Due Date',
                type: 'date',
                required: false
            },
            {
                name: 'prioritycode',
                label: 'Priority',
                type: 'optionset',
                required: false,
                options: [
                    { label: 'Low', value: 0 },
                    { label: 'Normal', value: 1 },
                    { label: 'High', value: 2 }
                ]
            }
        ]
    },

    /**
     * Contact Entity (contact)
     * Standard Dynamics 365 entity
     */
    'contact': {
        entityName: 'contact',
        supportsFileUpload: false,
        fields: [
            {
                name: 'firstname',
                label: 'First Name',
                type: 'text',
                required: true,
                maxLength: 50
            },
            {
                name: 'lastname',
                label: 'Last Name',
                type: 'text',
                required: true,
                maxLength: 50
            },
            {
                name: 'emailaddress1',
                label: 'Email',
                type: 'text',
                required: false,
                maxLength: 100
            },
            {
                name: 'telephone1',
                label: 'Phone',
                type: 'text',
                required: false,
                maxLength: 50
            }
        ]
    }
};

/**
 * Get field configuration for an entity
 *
 * @param entityName - Entity logical name
 * @returns Field configuration or null if not defined
 */
export function getEntityFieldConfiguration(entityName: string): EntityFieldConfiguration | null {
    return ENTITY_FIELD_DEFINITIONS[entityName] || null;
}
```

### 3. DynamicFormFields Component (components/DynamicFormFields.tsx)

Create new file: `components/DynamicFormFields.tsx`

```typescript
import * as React from 'react';
import {
    Field,
    Input,
    Textarea,
    Dropdown,
    Option,
    Switch,
    makeStyles
} from '@fluentui/react-components';
import { FieldMetadata } from '../types/FieldMetadata';
import { logger } from '../utils/logger';

const useStyles = makeStyles({
    fieldContainer: {
        marginBottom: '16px'
    }
});

export interface DynamicFormFieldsProps {
    /** Fields to render */
    fields: FieldMetadata[];

    /** Current field values */
    values: Record<string, unknown>;

    /** Callback when field value changes */
    onChange: (fieldName: string, value: unknown) => void;
}

/**
 * DynamicFormFields Component
 *
 * Renders form fields dynamically based on field metadata.
 * Supports: text, textarea, number, date, datetime, boolean, optionset
 */
export const DynamicFormFields: React.FC<DynamicFormFieldsProps> = ({
    fields,
    values,
    onChange
}) => {
    const styles = useStyles();

    const renderField = React.useCallback((field: FieldMetadata) => {
        const value = values[field.name];

        logger.debug('DynamicFormFields', 'Rendering field', {
            name: field.name,
            type: field.type,
            value,
            readOnly: field.readOnly
        });

        switch (field.type) {
            case 'textarea':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Textarea
                            value={(value as string) || ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={field.readOnly}
                            rows={3}
                        />
                    </Field>
                );

            case 'number':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Input
                            type="number"
                            value={value?.toString() || ''}
                            onChange={(e, data) => onChange(field.name, parseFloat(data.value) || 0)}
                            disabled={field.readOnly}
                        />
                    </Field>
                );

            case 'date':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Input
                            type="date"
                            value={value ? new Date(value as string).toISOString().split('T')[0] : ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={field.readOnly}
                        />
                    </Field>
                );

            case 'datetime':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Input
                            type="datetime-local"
                            value={value ? new Date(value as string).toISOString().slice(0, 16) : ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={field.readOnly}
                        />
                    </Field>
                );

            case 'boolean':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        className={styles.fieldContainer}
                    >
                        <Switch
                            checked={value === true}
                            onChange={(e, data) => onChange(field.name, data.checked)}
                            disabled={field.readOnly}
                        />
                    </Field>
                );

            case 'optionset':
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Dropdown
                            value={value?.toString() || ''}
                            onOptionSelect={(e, data) => onChange(field.name, data.optionValue)}
                            disabled={field.readOnly}
                        >
                            {field.options?.map(option => (
                                <Option key={option.value} value={option.value.toString()}>
                                    {option.label}
                                </Option>
                            ))}
                        </Dropdown>
                    </Field>
                );

            case 'text':
            default:
                return (
                    <Field
                        key={field.name}
                        label={field.label}
                        required={field.required}
                        className={styles.fieldContainer}
                    >
                        <Input
                            type="text"
                            value={(value as string) || ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={field.readOnly}
                            maxLength={field.maxLength}
                        />
                    </Field>
                );
        }
    }, [values, onChange, styles]);

    return (
        <>
            {fields.map(field => renderField(field))}
        </>
    );
};
```

### 4. Update UniversalQuickCreatePCF.ts

**Changes:**
1. Import entity field definitions
2. Load field configuration in init()
3. Pass field configuration to React component

```typescript
// Add imports at top
import { getEntityFieldConfiguration } from './config/EntityFieldDefinitions';
import { EntityFieldConfiguration } from './types/FieldMetadata';

export class UniversalQuickCreatePCF implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // ... existing properties ...

    private entityFieldConfig: EntityFieldConfiguration | null = null;

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // ... existing init code ...

        // Load entity field configuration
        this.entityFieldConfig = getEntityFieldConfiguration(this.entityName);

        if (!this.entityFieldConfig) {
            logger.warn('UniversalQuickCreatePCF', 'No field configuration found for entity', {
                entityName: this.entityName
            });

            // Fallback: Use minimal configuration
            this.entityFieldConfig = {
                entityName: this.entityName,
                supportsFileUpload: false,
                fields: [
                    { name: 'name', label: 'Name', type: 'text', required: true }
                ]
            };
        }

        logger.info('UniversalQuickCreatePCF', 'Field configuration loaded', {
            entityName: this.entityName,
            fieldCount: this.entityFieldConfig.fields.length,
            supportsFileUpload: this.entityFieldConfig.supportsFileUpload
        });

        // Override enableFileUpload based on entity configuration
        // (Manifest parameter still takes precedence if explicitly set)
        if (!context.parameters.enableFileUpload?.raw && this.entityFieldConfig) {
            this.enableFileUpload = this.entityFieldConfig.supportsFileUpload;
        }

        // ... rest of init ...
    }

    private renderReactComponent(): void {
        if (!this.reactRoot) {
            return;
        }

        const defaultValues = this.getDefaultValues();

        const props: QuickCreateFormProps = {
            entityName: this.entityName,
            parentEntityName: this.parentEntityName,
            parentRecordId: this.parentRecordId,
            defaultValues: defaultValues,
            fieldMetadata: this.entityFieldConfig?.fields || [],  // NEW
            enableFileUpload: this.enableFileUpload,
            sdapApiBaseUrl: this.sdapApiBaseUrl,
            context: this.context,
            onSave: this.handleSave.bind(this),
            onCancel: this.handleCancel.bind(this)
        };

        this.reactRoot.render(React.createElement(QuickCreateForm, props));
    }
}
```

### 5. Update QuickCreateForm.tsx

**Changes:**
1. Add `fieldMetadata` prop
2. Import DynamicFormFields
3. Replace hardcoded fields with DynamicFormFields

```typescript
// Add import
import { DynamicFormFields } from './DynamicFormFields';
import { FieldMetadata } from '../types/FieldMetadata';

export interface QuickCreateFormProps {
    entityName: string;
    parentEntityName: string;
    parentRecordId: string;
    defaultValues: Record<string, unknown>;
    fieldMetadata: FieldMetadata[];  // NEW
    enableFileUpload: boolean;
    sdapApiBaseUrl: string;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    context: ComponentFramework.Context<any>;
    onSave: (formData: Record<string, unknown>, file?: File) => Promise<void>;
    onCancel: () => void;
}

export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    entityName,
    defaultValues,
    fieldMetadata,  // NEW
    enableFileUpload,
    onSave,
    onCancel
}) => {
    // ... existing state and handlers ...

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

                    {/* REPLACE hardcoded fields with dynamic rendering */}
                    <DynamicFormFields
                        fields={fieldMetadata}
                        values={formData}
                        onChange={handleFieldChange}
                    />

                    {/* Error message */}
                    {error && (
                        <div style={{ color: 'red', fontSize: '14px' }}>
                            {error}
                        </div>
                    )}

                    {/* Actions */}
                    <div className={styles.actions}>
                        <Button appearance="secondary" onClick={onCancel} disabled={isSaving}>
                            Cancel
                        </Button>
                        <Button appearance="primary" type="submit" disabled={isSaving}>
                            {isSaving ? 'Saving...' : 'Save'}
                        </Button>
                    </div>
                </form>

                {/* Loading overlay with upload progress */}
                {isSaving && (
                    <div className={styles.loadingOverlay}>
                        <Spinner label={uploadStatus || 'Saving...'} size="large" />
                    </div>
                )}
            </div>
        </FluentProvider>
    );
};
```

---

## Implementation Steps

### Step 1: Create Type Definitions (15 min)

1. Create `types/FieldMetadata.ts`
2. Define `FieldMetadata` interface
3. Define `EntityFieldConfiguration` interface

### Step 2: Create Entity Field Definitions (30 min)

1. Create `config/` directory
2. Create `config/EntityFieldDefinitions.ts`
3. Add definitions for Document, Task, Contact
4. Add `getEntityFieldConfiguration()` helper

### Step 3: Create DynamicFormFields Component (1 hour)

1. Create `components/DynamicFormFields.tsx`
2. Implement field rendering for each type:
   - Text input
   - Textarea
   - Number input
   - Date/DateTime picker
   - Boolean switch
   - Dropdown (optionset)
3. Add read-only support
4. Test each field type

### Step 4: Update UniversalQuickCreatePCF (30 min)

1. Import entity field definitions
2. Load field configuration in `init()`
3. Add fallback for unknown entities
4. Pass `fieldMetadata` to React component
5. Override `enableFileUpload` based on entity config

### Step 5: Update QuickCreateForm Component (15 min)

1. Add `fieldMetadata` prop
2. Import `DynamicFormFields`
3. Replace hardcoded fields (lines 160-175) with `<DynamicFormFields />`
4. Remove entity-specific conditional rendering

### Step 6: Build and Test (30 min)

1. Build development: `npm run build`
2. Build production: `npm run build:prod`
3. Verify TypeScript: `npx tsc --noEmit`
4. Verify ESLint: `npm run lint`
5. Check bundle size

---

## Testing Checklist

### Document Entity
- [ ] File upload field appears
- [ ] Document Title field renders
- [ ] Description field renders
- [ ] Default values applied from Matter
- [ ] Save works (file upload + Dataverse record)

### Task Entity
- [ ] File upload field DOES NOT appear
- [ ] Subject field renders
- [ ] Description field renders
- [ ] Due Date field renders
- [ ] Priority dropdown renders with 3 options
- [ ] Default values applied from Matter
- [ ] Save works (Dataverse record only, no file)

### Contact Entity
- [ ] File upload field DOES NOT appear
- [ ] First Name field renders
- [ ] Last Name field renders
- [ ] Email field renders
- [ ] Phone field renders
- [ ] Save works (Dataverse record only)

### Configuration
- [ ] Unknown entity shows fallback fields
- [ ] Logging shows field configuration loaded
- [ ] Bundle size ~600-650 KB

---

## Configuration Examples

### Example 1: Document from Matter (Already Works)

**Manifest Parameter** (`defaultValueMappings`):
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid"
  }
}
```

**Entity Definition** (in `EntityFieldDefinitions.ts`):
```typescript
'sprk_document': {
    entityName: 'sprk_document',
    supportsFileUpload: true,
    fields: [
        { name: 'sprk_documenttitle', label: 'Document Title', type: 'text', required: true },
        { name: 'sprk_description', label: 'Description', type: 'textarea' }
    ]
}
```

### Example 2: Task from Matter

**Manifest Parameter**:
```json
{
  "sprk_matter": {
    "sprk_name": "subject",
    "_ownerid_value": "ownerid"
  }
}
```

**Entity Definition** (in `EntityFieldDefinitions.ts`):
```typescript
'task': {
    entityName: 'task',
    supportsFileUpload: false,
    fields: [
        { name: 'subject', label: 'Subject', type: 'text', required: true },
        { name: 'description', label: 'Description', type: 'textarea' },
        { name: 'scheduledend', label: 'Due Date', type: 'date' },
        { name: 'prioritycode', label: 'Priority', type: 'optionset', options: [...] }
    ]
}
```

---

## Key Simplifications from Original Task

| Original Task | Simplified Approach | Reason |
|---------------|---------------------|--------|
| DefaultValueMapper service | Use existing getDefaultValues() | Already works perfectly |
| FieldMetadataService | Hardcoded EntityFieldDefinitions | Power Apps doesn't expose form metadata |
| Convention-based fallback | Explicit JSON configuration | More predictable, easier to debug |
| Read-only fields array | readOnly flag in field definition | Cleaner data model |
| Multiple files/services | 3 files (types, config, component) | Simpler, easier to maintain |

---

## Success Metrics

- ✅ Same Quick Create PCF works for 3+ entity types
- ✅ Adding new entity takes <15 minutes (add to EntityFieldDefinitions.ts)
- ✅ No PCF code changes needed for new entity types (config only)
- ✅ All field types render correctly
- ✅ Bundle size ~600-650 KB (acceptable with MSAL)

---

## Next Steps

After completing this task:

1. **Task 4**: Testing & Deployment
   - Test all 3 entity types in Dataverse
   - Verify default value mappings work
   - Deploy to production environment
   - Return to Sprint 7A Task 3 for manual testing

---

## References

- **Task 2 Completion**: [TASK-7B-2-COMPLETION-SUMMARY.md](TASK-7B-2-COMPLETION-SUMMARY.md)
- **Sprint 7B Task 1**: [TASK-7B-1-COMPLETION-SUMMARY.md](TASK-7B-1-COMPLETION-SUMMARY.md)
- **Power Apps PCF Docs**: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/

---

**Status**: Ready to implement
