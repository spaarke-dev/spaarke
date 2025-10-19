/**
 * Field Metadata Type Definitions
 *
 * Used for dynamic form field rendering in Universal Quick Create PCF control.
 *
 * @version 1.0.0
 */

/**
 * Field metadata for dynamic form rendering
 */
export interface FieldMetadata {
    /** Field logical name (e.g., "sprk_documenttitle", "subject", "firstname") */
    name: string;

    /** Display label shown to user (e.g., "Document Title", "Subject", "First Name") */
    label: string;

    /** Field type - determines which input component to render */
    type: 'text' | 'textarea' | 'number' | 'date' | 'datetime' | 'boolean' | 'optionset';

    /** Is this field required? User must provide a value before saving */
    required?: boolean;

    /** Maximum length for text fields (used for validation) */
    maxLength?: number;

    /** Options for optionset/dropdown fields */
    options?: { label: string; value: string | number }[];

    /** Is this field read-only? (auto-populated from parent, user cannot edit) */
    readOnly?: boolean;
}

/**
 * Entity field configuration
 *
 * Defines all fields to render for a specific entity type.
 */
export interface EntityFieldConfiguration {
    /** Entity logical name (e.g., "sprk_document", "task", "contact") */
    entityName: string;

    /** Fields to render in the Quick Create form */
    fields: FieldMetadata[];

    /** Does this entity support file upload to SharePoint Embedded? */
    supportsFileUpload: boolean;
}
