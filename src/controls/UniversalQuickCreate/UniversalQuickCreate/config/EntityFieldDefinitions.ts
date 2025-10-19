/**
 * Entity Field Definitions
 *
 * Hardcoded field definitions for supported entity types.
 *
 * This is the practical approach since Power Apps doesn't expose Quick Create
 * form metadata to PCF controls. Each entity type has an explicit field list.
 *
 * To add a new entity type:
 * 1. Add entry to ENTITY_FIELD_DEFINITIONS object
 * 2. Define fields array with field metadata
 * 3. Set supportsFileUpload flag
 * 4. Update manifest defaultValueMappings parameter if needed
 *
 * @version 1.0.0
 */

import { EntityFieldConfiguration } from '../types/FieldMetadata';

/**
 * Entity field definitions for supported entity types
 */
export const ENTITY_FIELD_DEFINITIONS: Record<string, EntityFieldConfiguration> = {
    /**
     * Document Entity (sprk_document)
     *
     * Used for file-based records in SharePoint Embedded.
     * Supports file upload with SPE metadata.
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
     *
     * Standard Dynamics 365 entity for task management.
     * Does not support file upload.
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
     *
     * Standard Dynamics 365 entity for contact management.
     * Does not support file upload.
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
 * @param entityName - Entity logical name (e.g., "sprk_document", "task")
 * @returns Field configuration or null if entity not defined
 *
 * @example
 * ```typescript
 * const config = getEntityFieldConfiguration('sprk_document');
 * if (config) {
 *     console.log(config.fields.length); // 2 (title + description)
 *     console.log(config.supportsFileUpload); // true
 * }
 * ```
 */
export function getEntityFieldConfiguration(entityName: string): EntityFieldConfiguration | null {
    return ENTITY_FIELD_DEFINITIONS[entityName] || null;
}
