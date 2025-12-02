# Sprint 7B - Task 3: Configurable Default Value Mappings

**Sprint**: 7B - Universal Quick Create with SPE Upload
**Task**: 3 of 4
**Estimated Time**: 0.5-1 day
**Priority**: High
**Status**: Pending
**Depends On**: Sprint 7B Task 1 (Quick Create PCF Setup)

---

## Task Overview

Make the Universal Quick Create PCF fully configurable for different entity types by implementing dynamic field rendering and configurable default value mappings. This enables the same Quick Create control to be reused for Documents, Tasks, Contacts, and any other entity with different field requirements.

---

## Success Criteria

- ✅ Dynamic field rendering based on Power Apps form configuration
- ✅ Configurable default value mappings via manifest parameter
- ✅ Support for different field types (text, textarea, lookup, date, number, boolean)
- ✅ Convention-based mapping fallback (if no explicit config)
- ✅ Works for multiple entity types (Document, Task, Contact)
- ✅ Configuration documented with examples
- ✅ Zero breaking changes to Sprint 7B Tasks 1-2
- ✅ Bundle size remains <400 KB

---

## Context & Background

### The Problem

In Sprint 7B Tasks 1-2, we built a Quick Create control that works for Documents. But it's **hardcoded**:

```typescript
// Hardcoded for Documents only ❌
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

This won't work for:
- **Tasks** (different fields: subject, due date, priority)
- **Contacts** (different fields: first name, last name, email)
- **Custom entities** (completely different fields)

### The Solution

Make the Quick Create control **universal** by:

1. **Reading field metadata** from Power Apps form configuration
2. **Dynamically rendering fields** based on metadata
3. **Configurable default value mappings** from parent to child entity
4. **Convention-based fallback** when no explicit config provided

### User Flow (Configurable)

```
Admin Configuration (One-time):
1. Admin opens Power Apps Quick Create form for Document entity
2. Admin adds fields to form: sprk_documenttitle, sprk_description
3. Admin sets field properties: label, required, type
4. Admin configures Universal Quick Create PCF on form
5. Admin sets default value mappings parameter:
   {
     "sprk_matter": {
       "sprk_containerid": "sprk_containerid",
       "sprk_name": "sprk_documenttitle",
       "_ownerid_value": "ownerid"
     }
   }

Runtime (Automatic):
1. User clicks "+ New Document" from Matter
2. Quick Create PCF reads form field metadata from Power Apps
3. PCF renders fields dynamically (Document Title, Description)
4. PCF applies default value mappings (Matter → Document)
5. User fills form, clicks Save
6. Standard save flow (from Task 2)
```

---

## Deliverables

### 1. DefaultValueMapper Service (services/DefaultValueMapper.ts)

```typescript
import { logger } from '../utils/logger';

export interface DefaultValueMapping {
    [parentEntityName: string]: {
        [parentFieldName: string]: string; // Child field name
    };
}

export interface DefaultValueMapperOptions {
    mappings: DefaultValueMapping;
    parentEntityName: string;
    parentRecordData: ComponentFramework.WebApi.Entity | null;
}

export class DefaultValueMapper {
    constructor(private options: DefaultValueMapperOptions) {}

    /**
     * Get default values by applying configured mappings
     */
    getDefaultValues(): Record<string, any> {
        const defaults: Record<string, any> = {};

        if (!this.options.parentRecordData || !this.options.parentEntityName) {
            logger.warn('DefaultValueMapper', 'No parent data available for mappings');
            return defaults;
        }

        // Get mapping for this parent entity type
        const mappings = this.options.mappings[this.options.parentEntityName];

        if (!mappings) {
            logger.warn('DefaultValueMapper', 'No mappings configured for parent entity', {
                parentEntityName: this.options.parentEntityName
            });
            return this.getDefaultValuesByConvention();
        }

        // Apply configured mappings
        for (const [parentField, childField] of Object.entries(mappings)) {
            const parentValue = this.options.parentRecordData[parentField];

            if (parentValue !== undefined && parentValue !== null) {
                defaults[childField] = parentValue;

                logger.debug('DefaultValueMapper', 'Mapped default value', {
                    parentField,
                    childField,
                    value: parentValue
                });
            }
        }

        return defaults;
    }

    /**
     * Get default values using convention-based mapping
     * Fallback when no explicit mappings configured
     */
    private getDefaultValuesByConvention(): Record<string, any> {
        const defaults: Record<string, any> = {};

        if (!this.options.parentRecordData) {
            return defaults;
        }

        logger.info('DefaultValueMapper', 'Using convention-based mappings');

        // Convention: Field names match between parent and child
        const conventionMappings = [
            { parent: 'sprk_containerid', child: 'sprk_containerid' },
            { parent: '_ownerid_value', child: 'ownerid' },
            { parent: 'sprk_name', child: 'sprk_name' },
            { parent: 'name', child: 'sprk_name' }
        ];

        for (const mapping of conventionMappings) {
            const value = this.options.parentRecordData[mapping.parent];

            if (value !== undefined && value !== null) {
                defaults[mapping.child] = value;

                logger.debug('DefaultValueMapper', 'Convention-based mapping applied', {
                    parentField: mapping.parent,
                    childField: mapping.child,
                    value
                });
            }
        }

        return defaults;
    }

    /**
     * Merge default values with user-entered values
     * User-entered values take precedence
     */
    static mergeValues(
        defaults: Record<string, any>,
        userValues: Record<string, any>
    ): Record<string, any> {
        return {
            ...defaults,
            ...userValues // User values override defaults
        };
    }
}
```

### 2. DynamicFormFields Component (components/DynamicFormFields.tsx)

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
import { logger } from '../utils/logger';

const useStyles = makeStyles({
    fieldContainer: {
        marginBottom: '16px'
    }
});

export interface FieldMetadata {
    name: string;
    label: string;
    type: 'text' | 'textarea' | 'number' | 'date' | 'datetime' | 'boolean' | 'lookup' | 'optionset';
    required?: boolean;
    maxLength?: number;
    options?: Array<{ label: string; value: string | number }>;
}

export interface DynamicFormFieldsProps {
    fields: FieldMetadata[];
    values: Record<string, any>;
    onChange: (fieldName: string, value: any) => void;
    readOnlyFields?: string[];
}

export const DynamicFormFields: React.FC<DynamicFormFieldsProps> = ({
    fields,
    values,
    onChange,
    readOnlyFields = []
}) => {
    const styles = useStyles();

    const renderField = (field: FieldMetadata) => {
        const isReadOnly = readOnlyFields.includes(field.name);
        const value = values[field.name];

        logger.debug('DynamicFormFields', 'Rendering field', {
            name: field.name,
            type: field.type,
            value,
            isReadOnly
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
                            value={value || ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={isReadOnly}
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
                            disabled={isReadOnly}
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
                            value={value ? new Date(value).toISOString().split('T')[0] : ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={isReadOnly}
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
                            value={value ? new Date(value).toISOString().slice(0, 16) : ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={isReadOnly}
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
                            disabled={isReadOnly}
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
                            disabled={isReadOnly}
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
                            value={value || ''}
                            onChange={(e, data) => onChange(field.name, data.value)}
                            disabled={isReadOnly}
                            maxLength={field.maxLength}
                        />
                    </Field>
                );
        }
    };

    return (
        <>
            {fields.map(field => renderField(field))}
        </>
    );
};
```

### 3. FieldMetadataService (services/FieldMetadataService.ts)

```typescript
import { logger } from '../utils/logger';
import { FieldMetadata } from '../components/DynamicFormFields';

export class FieldMetadataService {
    constructor(private context: ComponentFramework.Context<any>) {}

    /**
     * Get field metadata from Power Apps dataset
     * In Quick Create, Power Apps provides field metadata via the dataset parameter
     */
    async getFieldMetadata(entityName: string): Promise<FieldMetadata[]> {
        try {
            logger.info('FieldMetadataService', 'Loading field metadata', { entityName });

            // Option 1: Get fields from dataset columns (if available)
            const datasetFields = this.getFieldsFromDataset();
            if (datasetFields.length > 0) {
                return datasetFields;
            }

            // Option 2: Use hardcoded field definitions based on entity type
            // This is a fallback if Power Apps doesn't provide dataset metadata
            const entityFields = this.getFieldsByEntityType(entityName);

            logger.info('FieldMetadataService', 'Field metadata loaded', {
                fieldCount: entityFields.length
            });

            return entityFields;

        } catch (error) {
            logger.error('FieldMetadataService', 'Failed to load field metadata', error);
            return [];
        }
    }

    private getFieldsFromDataset(): FieldMetadata[] {
        const fields: FieldMetadata[] = [];

        // Get dataset columns (if available)
        const dataset = (this.context.parameters as any).dataset;

        if (!dataset || !dataset.columns) {
            logger.warn('FieldMetadataService', 'Dataset columns not available');
            return fields;
        }

        for (const column of dataset.columns) {
            fields.push({
                name: column.name,
                label: column.displayName || column.name,
                type: this.mapDataTypeToFieldType(column.dataType),
                required: column.isRequired || false
            });
        }

        return fields;
    }

    private mapDataTypeToFieldType(dataType: string): FieldMetadata['type'] {
        const typeMap: Record<string, FieldMetadata['type']> = {
            'SingleLine.Text': 'text',
            'Multiple': 'textarea',
            'Whole.None': 'number',
            'Decimal': 'number',
            'DateAndTime.DateOnly': 'date',
            'DateAndTime.DateAndTime': 'datetime',
            'TwoOptions': 'boolean',
            'Lookup.Simple': 'lookup',
            'OptionSet': 'optionset'
        };

        return typeMap[dataType] || 'text';
    }

    /**
     * Fallback: Hardcoded field definitions by entity type
     * Used when Power Apps doesn't provide dataset metadata
     */
    private getFieldsByEntityType(entityName: string): FieldMetadata[] {
        const entityFieldMappings: Record<string, FieldMetadata[]> = {
            'sprk_document': [
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
            ],
            'task': [
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
            ],
            'contact': [
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
        };

        return entityFieldMappings[entityName] || [];
    }
}
```

### 4. Update UniversalQuickCreatePCF.ts

```typescript
// In UniversalQuickCreatePCF.ts

import { DefaultValueMapper } from './services/DefaultValueMapper';
import { FieldMetadataService } from './services/FieldMetadataService';
import { FieldMetadata } from './components/DynamicFormFields';

export class UniversalQuickCreate implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    // ... existing properties ...

    private defaultValueMapper: DefaultValueMapper | null = null;
    private fieldMetadataService: FieldMetadataService | null = null;
    private fieldMetadata: FieldMetadata[] = [];

    public async init(context: ComponentFramework.Context<IInputs>): Promise<void> {
        // ... existing init code ...

        // Initialize field metadata service
        this.fieldMetadataService = new FieldMetadataService(context);

        // Load field metadata for this entity
        this.fieldMetadata = await this.fieldMetadataService.getFieldMetadata(this.entityName);

        // Initialize default value mapper
        this.defaultValueMapper = new DefaultValueMapper({
            mappings: this.defaultValueMappings,
            parentEntityName: this.parentEntityName,
            parentRecordData: this.parentRecordData
        });

        logger.info('UniversalQuickCreate', 'Field metadata loaded', {
            fieldCount: this.fieldMetadata.length
        });
    }

    private getDefaultValues(): Record<string, any> {
        if (!this.defaultValueMapper) {
            return {};
        }

        return this.defaultValueMapper.getDefaultValues();
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
            fieldMetadata: this.fieldMetadata,  // NEW: Pass field metadata
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
}
```

### 5. Update QuickCreateForm.tsx

```typescript
// In QuickCreateForm.tsx

import { DynamicFormFields, FieldMetadata } from './DynamicFormFields';

export interface QuickCreateFormProps {
    entityName: string;
    parentEntityName: string;
    parentRecordId: string;
    defaultValues: Record<string, any>;
    fieldMetadata: FieldMetadata[];  // NEW
    enableFileUpload: boolean;
    sdapApiBaseUrl: string;
    context: ComponentFramework.Context<any>;
    onSave: (formData: Record<string, any>, file?: File, onProgress?: (progress: number) => void) => Promise<void>;
    onCancel: () => void;
}

export const QuickCreateForm: React.FC<QuickCreateFormProps> = ({
    entityName,
    parentEntityName,
    parentRecordId,
    defaultValues,
    fieldMetadata,  // NEW
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
    const [uploadProgress, setUploadProgress] = React.useState<number>(0);
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

    // ... existing handleSubmit code ...

    // Read-only fields (auto-populated from parent, user can't edit)
    const readOnlyFields = [
        'sprk_containerid',
        'ownerid',
        'sprk_matter'
    ];

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

                    {/* REPLACED: Hardcoded fields with DynamicFormFields */}
                    <DynamicFormFields
                        fields={fieldMetadata}
                        values={formData}
                        onChange={handleFieldChange}
                        readOnlyFields={readOnlyFields}
                    />

                    {/* Upload progress indicator */}
                    {isSaving && uploadProgress > 0 && uploadProgress < 100 && (
                        <Field label="Upload Progress">
                            <ProgressBar value={uploadProgress / 100} />
                            <div style={{ fontSize: '12px', marginTop: '4px' }}>
                                {uploadProgress}%
                            </div>
                        </Field>
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
                        <Spinner
                            label={uploadProgress > 0 ? `Uploading... ${uploadProgress}%` : 'Saving...'}
                            size="large"
                        />
                    </div>
                )}
            </div>
        </FluentProvider>
    );
};
```

---

## Configuration Examples

### Example 1: Document from Matter

**Manifest Parameter** (`defaultValueMappings`):
```json
{
  "sprk_matter": {
    "sprk_containerid": "sprk_containerid",
    "sprk_name": "sprk_documenttitle",
    "_ownerid_value": "ownerid",
    "_sprk_primarycontact_value": "sprk_contact"
  }
}
```

**Power Apps Form Configuration**:
- Add fields to Quick Create form: `sprk_documenttitle`, `sprk_description`
- Set `sprk_documenttitle` as required
- Add Universal Quick Create PCF control

**Result**:
- Document Title auto-populated from Matter Name
- Owner auto-populated from Matter Owner
- Container ID auto-populated (hidden field)
- User can edit title, fill description

---

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

**Power Apps Form Configuration**:
- Add fields: `subject`, `description`, `scheduledend`, `prioritycode`
- Disable file upload: Set `enableFileUpload` = false

**Result**:
- Subject auto-populated from Matter Name
- Owner auto-populated from Matter Owner
- User fills description, due date, priority
- No file upload field shown

---

### Example 3: Contact from Account

**Manifest Parameter**:
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

**Power Apps Form Configuration**:
- Add fields: `firstname`, `lastname`, `emailaddress1`, `telephone1`
- Disable file upload: Set `enableFileUpload` = false

**Result**:
- Company auto-populated from Account Name
- Address auto-populated from Account Address
- User fills first name, last name, email, phone

---

## Implementation Steps

### Step 1: Create DefaultValueMapper Service (1 hour)

1. Create `services/DefaultValueMapper.ts`
2. Implement `getDefaultValues()` with configured mappings
3. Implement `getDefaultValuesByConvention()` as fallback
4. Add `mergeValues()` static helper
5. Add comprehensive logging

### Step 2: Create DynamicFormFields Component (2-3 hours)

1. Create `components/DynamicFormFields.tsx`
2. Implement field rendering for each type:
   - Text input
   - Textarea
   - Number input
   - Date/DateTime picker
   - Boolean switch
   - Dropdown (optionset)
3. Add read-only field support
4. Add validation support
5. Test each field type

### Step 3: Create FieldMetadataService (1 hour)

1. Create `services/FieldMetadataService.ts`
2. Implement `getFieldMetadata()` with dataset reading
3. Implement `getFieldsByEntityType()` fallback
4. Add type mapping logic
5. Test with different entity types

### Step 4: Update UniversalQuickCreatePCF (30 min)

1. Import new services
2. Initialize `FieldMetadataService` in `init()`
3. Load field metadata
4. Update `getDefaultValues()` to use `DefaultValueMapper`
5. Pass `fieldMetadata` to React component

### Step 5: Update QuickCreateForm Component (1 hour)

1. Add `fieldMetadata` prop
2. Replace hardcoded fields with `DynamicFormFields`
3. Define read-only fields array
4. Test dynamic rendering

### Step 6: Testing (2-3 hours)

Test with different entity types:
1. **Document** (with file upload)
2. **Task** (without file upload)
3. **Contact** (without file upload)
4. Custom entity

Verify:
- Fields render correctly
- Default values applied
- User can edit fields
- Read-only fields can't be edited
- Save works for all entity types

---

## Testing Checklist

### Document Entity

- [ ] File upload field appears at top
- [ ] Document Title field renders
- [ ] Description field renders
- [ ] Default values applied from Matter:
  - [ ] Document Title = Matter Name
  - [ ] Owner = Matter Owner
  - [ ] Container ID populated (hidden)
- [ ] User can edit Document Title
- [ ] User can fill Description
- [ ] Save creates document with file upload

### Task Entity

- [ ] File upload field DOES NOT appear
- [ ] Subject field renders
- [ ] Description field renders
- [ ] Due Date field renders
- [ ] Priority dropdown renders with options
- [ ] Default values applied from Matter:
  - [ ] Subject = Matter Name
  - [ ] Owner = Matter Owner
- [ ] Save creates task (no file upload)

### Contact Entity

- [ ] File upload field DOES NOT appear
- [ ] First Name field renders
- [ ] Last Name field renders
- [ ] Email field renders
- [ ] Phone field renders
- [ ] Default values applied from Account
- [ ] Save creates contact

### Configuration

- [ ] JSON mapping parsed correctly
- [ ] Invalid JSON logged as error, uses fallback
- [ ] Convention-based mapping works when no config
- [ ] Multiple parent entity types supported

---

## Common Issues & Solutions

### Issue 1: Fields Not Rendering

**Symptom**: Form is blank or shows no fields

**Causes**:
- Field metadata not loaded
- Entity name incorrect
- No fallback fields defined

**Solution**:
```typescript
// Always provide fallback
if (this.fieldMetadata.length === 0) {
    logger.warn('QuickCreate', 'No field metadata - using minimal fields');
    this.fieldMetadata = [
        { name: 'name', label: 'Name', type: 'text', required: true }
    ];
}
```

### Issue 2: Default Values Not Applied

**Symptom**: Form fields are empty when they should be pre-populated

**Causes**:
- Mapping configuration incorrect
- Parent field name wrong (should use `_fieldname_value` for lookups)
- Parent record data not loaded

**Solution**:
```typescript
// Log mapping process for debugging
logger.debug('DefaultValueMapper', 'Applying mappings', {
    mappings: this.options.mappings,
    parentData: this.options.parentRecordData
});

// Check for common mistakes
if (parentField === 'ownerid') {
    logger.warn('DefaultValueMapper', 'Use _ownerid_value, not ownerid');
}
```

### Issue 3: Read-Only Fields Editable

**Symptom**: User can edit fields that should be read-only (like container ID)

**Causes**:
- Read-only fields array not includes field name
- Field name mismatch

**Solution**:
```typescript
// Define read-only fields clearly
const readOnlyFields = [
    'sprk_containerid',  // From parent Matter
    'ownerid',           // From parent Matter
    'sprk_matter'        // Parent relationship
];
```

---

## Documentation

### For Admins: How to Configure Universal Quick Create

**Step 1: Configure Default Value Mappings**

When adding Universal Quick Create PCF to a form:

1. Open Power Apps form designer
2. Add Universal Quick Create control
3. Set `defaultValueMappings` parameter:

```json
{
  "parent_entity_name": {
    "parent_field_name": "child_field_name",
    ...
  }
}
```

**Tips**:
- For lookup fields, use `_fieldname_value` syntax (e.g., `_ownerid_value`)
- Map container ID for file upload to work
- Map owner for proper record ownership

**Step 2: Configure Fields**

1. Add/remove fields on Quick Create form
2. Set field labels, required status
3. Fields will auto-render in control

**Step 3: Enable/Disable File Upload**

Set `enableFileUpload` parameter:
- `true` - Show file picker (for Documents)
- `false` - Hide file picker (for Tasks, Contacts)

---

## Success Metrics

- ✅ Same Quick Create PCF works for 3+ entity types
- ✅ Configuration takes <5 minutes per entity
- ✅ No code changes needed for new entity types
- ✅ Default value mapping accuracy 100%
- ✅ All field types render correctly
- ✅ Bundle size remains <400 KB

---

## Next Steps

After completing this task:

1. **Task 4**: Testing, Bundle Size & Deployment
   - Integration tests for multiple entity types
   - Manual testing checklist
   - Configuration documentation
   - Production deployment

---

## References

- **Master Resource**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) - See "Configurable Default Value Mappings" section
- **Sprint 7B Task 1**: [TASK-7B-1-QUICK-CREATE-SETUP.md](TASK-7B-1-QUICK-CREATE-SETUP.md)
- **Sprint 7B Task 2**: [TASK-7B-2-FILE-UPLOAD-SPE.md](TASK-7B-2-FILE-UPLOAD-SPE.md)
- **Power Apps PCF Docs**: https://learn.microsoft.com/en-us/power-apps/developer/component-framework/

---

**AI Coding Prompt**:

```
Implement configurable default value mappings and dynamic field rendering for the Universal Quick Create PCF:

1. Create DefaultValueMapper service that:
   - Applies configured mappings from manifest parameter
   - Falls back to convention-based mapping
   - Handles lookup field syntax (_fieldname_value)
   - Logs all mapping operations

2. Create DynamicFormFields component that:
   - Renders fields dynamically based on metadata
   - Supports text, textarea, number, date, datetime, boolean, optionset
   - Handles read-only fields
   - Uses Fluent UI v9 components

3. Create FieldMetadataService that:
   - Reads field metadata from Power Apps dataset (if available)
   - Falls back to hardcoded definitions by entity type
   - Maps Power Apps data types to field types

4. Update UniversalQuickCreatePCF to:
   - Initialize FieldMetadataService
   - Load field metadata for entity
   - Use DefaultValueMapper for default values
   - Pass field metadata to React component

5. Update QuickCreateForm to:
   - Replace hardcoded fields with DynamicFormFields
   - Define read-only fields array
   - Handle all field types correctly

Configuration examples for Document, Task, and Contact entities are provided in the task file.

Key technical requirements:
- Support multiple entity types without code changes
- JSON configuration via manifest parameter
- Convention-based fallback
- All field types supported
- Read-only field support

Refer to SPRINT-7-MASTER-RESOURCE.md "Configurable Default Value Mappings" section for detailed patterns.
```
