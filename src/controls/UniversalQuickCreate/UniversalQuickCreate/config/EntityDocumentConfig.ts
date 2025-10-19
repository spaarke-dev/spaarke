/**
 * Entity-Document Configuration
 *
 * Configuration-driven mapping for multi-entity document upload support.
 * Defines how Document records relate to different parent entity types.
 *
 * ADR Compliance:
 * - ADR-010: Configuration Over Code
 * - ADR-003: Separation of Concerns
 *
 * @version 2.0.0.0
 */

/**
 * Configuration for a parent entity that supports document uploads
 */
export interface EntityDocumentConfig {
    /** Entity logical name (e.g., "sprk_matter") */
    entityName: string;

    /** Lookup field name on Document entity (e.g., "sprk_matter") */
    lookupFieldName: string;

    /**
     * Relationship schema name for metadata queries (e.g., "sprk_matter_document")
     * Used by MetadataService to query navigation properties dynamically
     */
    relationshipSchemaName: string;

    /**
     * Navigation property name for OData @odata.bind (DEPRECATED - use MetadataService instead)
     * @deprecated Use MetadataService.getLookupNavProp() for case-sensitive accuracy
     */
    navigationPropertyName?: string;

    /** Container ID field name on parent entity (e.g., "sprk_containerid") */
    containerIdField: string;

    /** Display name field on parent entity (e.g., "sprk_matternumber") */
    displayNameField: string;

    /** Entity set name for OData (e.g., "sprk_matters") */
    entitySetName: string;
}

/**
 * Configuration mapping for all supported parent entities
 *
 * To add a new entity:
 * 1. Add entry to this object
 * 2. Ensure Document entity has lookup field
 * 3. Ensure parent entity has sprk_containerid field
 * 4. Deploy command button to entity form
 *
 * No code changes needed - configuration-driven!
 */
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    /**
     * Matter Entity (sprk_matter)
     * Legal matters with associated documents
     */
    'sprk_matter': {
        entityName: 'sprk_matter',
        lookupFieldName: 'sprk_matter',
        relationshipSchemaName: 'sprk_matter_document',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_matternumber',
        entitySetName: 'sprk_matters'
    },

    /**
     * Project Entity (sprk_project)
     * Projects with associated documents
     */
    'sprk_project': {
        entityName: 'sprk_project',
        lookupFieldName: 'sprk_project',
        relationshipSchemaName: 'sprk_project_document',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_projectname',
        entitySetName: 'sprk_projects'
    },

    /**
     * Invoice Entity (sprk_invoice)
     * Invoices with associated documents (receipts, supporting docs)
     */
    'sprk_invoice': {
        entityName: 'sprk_invoice',
        lookupFieldName: 'sprk_invoice',
        relationshipSchemaName: 'sprk_invoice_document',
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_invoicenumber',
        entitySetName: 'sprk_invoices'
    },

    /**
     * Account Entity (account)
     * Standard Dynamics account with documents
     */
    'account': {
        entityName: 'account',
        lookupFieldName: 'sprk_account',
        relationshipSchemaName: 'account_document',
        containerIdField: 'sprk_containerid',
        displayNameField: 'name',
        entitySetName: 'accounts'
    },

    /**
     * Contact Entity (contact)
     * Standard Dynamics contact with documents
     */
    'contact': {
        entityName: 'contact',
        lookupFieldName: 'sprk_contact',
        relationshipSchemaName: 'contact_document',
        containerIdField: 'sprk_containerid',
        displayNameField: 'fullname',
        entitySetName: 'contacts'
    }
};

/**
 * Get configuration for a parent entity
 *
 * @param entityName - Parent entity logical name
 * @returns Configuration or null if entity not supported
 *
 * @example
 * ```typescript
 * const config = getEntityDocumentConfig('sprk_matter');
 * if (config) {
 *     console.log(config.lookupFieldName); // "sprk_matter"
 *     console.log(config.entitySetName); // "sprk_matters"
 * }
 * ```
 */
export function getEntityDocumentConfig(entityName: string): EntityDocumentConfig | null {
    return ENTITY_DOCUMENT_CONFIGS[entityName] || null;
}

/**
 * Check if an entity supports document uploads
 *
 * @param entityName - Entity logical name to check
 * @returns True if entity has configuration defined
 *
 * @example
 * ```typescript
 * if (isEntitySupported('sprk_matter')) {
 *     // Show upload dialog
 * } else {
 *     // Show error: "This entity does not support document uploads"
 * }
 * ```
 */
export function isEntitySupported(entityName: string): boolean {
    return entityName in ENTITY_DOCUMENT_CONFIGS;
}

/**
 * Get all supported entity names
 *
 * @returns Array of supported entity logical names
 *
 * @example
 * ```typescript
 * const supported = getSupportedEntities();
 * console.log(supported); // ["sprk_matter", "sprk_project", ...]
 * ```
 */
export function getSupportedEntities(): string[] {
    return Object.keys(ENTITY_DOCUMENT_CONFIGS);
}
