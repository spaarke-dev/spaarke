/**
 * Document Record Service
 *
 * Creates Document records in Dataverse using context.webAPI.
 * Dynamically resolves navigation properties via MetadataService for case-sensitive accuracy.
 *
 * ADR Compliance:
 * - ADR-003: Separation of Concerns (service layer)
 * - ADR-010: Configuration Over Code (uses EntityDocumentConfig)
 *
 * Key Architectural Decisions:
 * - Uses context.webAPI (works across all PCF hosts: Custom Pages, Model-Driven Apps, Canvas Apps)
 * - Queries metadata dynamically to get correct navigation property names (case-sensitive!)
 * - Caches metadata per environment + relationship for performance
 *
 * @version 2.2.0
 */

import { getEntityDocumentConfig } from '../config/EntityDocumentConfig';
import {
    ParentContext,
    FormData,
    SpeFileMetadata,
    CreateResult
} from '../types';
import { logInfo, logError } from '../utils/logger';
import { MetadataService } from './MetadataService';

/**
 * Service for creating Document records using context.webAPI
 */
export class DocumentRecordService {
    private context: ComponentFramework.Context<any>;

    /**
     * Constructor
     * @param context - PCF context (required for webAPI and metadata queries)
     */
    constructor(context: ComponentFramework.Context<any>) {
        this.context = context;
    }

    /**
     * Create multiple Document records (one per uploaded file)
     *
     * Strategy: Sequential creation (one at a time)
     * - Easier error handling
     * - Better progress tracking
     * - No risk of overwhelming API
     * - Metadata queries cached per relationship
     *
     * @param files - Array of uploaded file metadata from SPE
     * @param parentContext - Parent entity context
     * @param formData - Form data (document name, description)
     * @returns Array of creation results (success/error per file)
     */
    async createDocuments(
        files: SpeFileMetadata[],
        parentContext: ParentContext,
        formData: FormData
    ): Promise<CreateResult[]> {
        logInfo('DocumentRecordService', `Creating ${files.length} Document records`);

        const results: CreateResult[] = [];

        // Sequential creation (Option A from requirements)
        for (const file of files) {
            const result = await this.createSingleDocument(file, parentContext, formData);
            results.push(result);
        }

        // Log summary
        const successCount = results.filter(r => r.success).length;
        const failureCount = results.filter(r => !r.success).length;
        logInfo('DocumentRecordService', `Created ${successCount} records, ${failureCount} failures`);

        return results;
    }

    /**
     * Create a single Document record
     *
     * Uses context.webAPI.createRecord() - works across all PCF hosts.
     * Queries metadata dynamically for correct navigation property (case-sensitive!)
     *
     * @param file - Uploaded file metadata
     * @param parentContext - Parent entity context
     * @param formData - Form data
     * @returns Creation result
     */
    private async createSingleDocument(
        file: SpeFileMetadata,
        parentContext: ParentContext,
        formData: FormData
    ): Promise<CreateResult> {
        try {
            // Get entity configuration
            const config = getEntityDocumentConfig(parentContext.parentEntityName);
            if (!config) {
                throw new Error(`Unsupported entity type: ${parentContext.parentEntityName}`);
            }

            // CRITICAL: Query metadata to get EXACT navigation property name (case-sensitive!)
            // Example: Returns "sprk_Matter" (capital M), not "sprk_matter" (lowercase)
            // This prevents "undeclared property" errors
            const navigationPropertyName = await MetadataService.getLookupNavProp(
                this.context,
                'sprk_document',              // Child entity
                config.relationshipSchemaName // Relationship (e.g., "sprk_matter_document")
            );

            logInfo('DocumentRecordService', `Resolved navigation property: ${navigationPropertyName}`);

            // Build record payload with correct navigation property
            const payload = this.buildRecordPayload(
                file,
                parentContext,
                formData,
                navigationPropertyName,  // Dynamic from metadata (not hardcoded!)
                config.entitySetName
            );

            logInfo('DocumentRecordService', `Creating Document: ${file.name}`);

            // Create record using context.webAPI (works across all PCF hosts)
            const result = await this.context.webAPI.createRecord('sprk_document', payload);

            logInfo('DocumentRecordService', `Created Document record: ${result.id}`);

            return {
                success: true,
                fileName: file.name,
                recordId: result.id
            };

        } catch (error: any) {
            logError('DocumentRecordService', `Failed to create Document for ${file.name}`, error);

            return {
                success: false,
                fileName: file.name,
                error: error.message || 'Unknown error occurred'
            };
        }
    }

    /**
     * Build Dataverse record payload
     *
     * Uses OData @odata.bind syntax for lookup fields.
     *
     * @param file - File metadata
     * @param parentContext - Parent context
     * @param formData - Form data
     * @param navigationPropertyName - Navigation property name (e.g., "sprk_matter")
     * @param entitySetName - Entity set name (e.g., "sprk_matters")
     * @returns Record payload object
     */
    private buildRecordPayload(
        file: SpeFileMetadata,
        parentContext: ParentContext,
        formData: FormData,
        navigationPropertyName: string,
        entitySetName: string
    ): any {
        // Sanitize GUID (remove curly braces, convert to lowercase)
        const sanitizedGuid = parentContext.parentRecordId
            .replace(/[{}]/g, '')  // Remove curly braces if present
            .toLowerCase();         // OData expects lowercase GUIDs

        // Base payload (per ARCHITECTURE.md reference spec)
        const payload: any = {
            // Document name (use form input or file name as fallback)
            sprk_documentname: formData.documentName || file.name,

            // File metadata
            sprk_filename: file.name,
            sprk_filesize: file.size,

            // SharePoint Embedded metadata
            sprk_graphitemid: file.id,
            sprk_graphdriveid: parentContext.containerId,

            // Optional description (field name: sprk_documentdescription, not sprk_description)
            sprk_documentdescription: formData.description || null,

            // Parent lookup using @odata.bind with single-valued navigation property
            // Per MS docs: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-entity-web-api
            [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${sanitizedGuid})`
        };

        logInfo('DocumentRecordService', `Lookup binding: ${navigationPropertyName}@odata.bind = /${entitySetName}(${sanitizedGuid})`);
        logInfo('DocumentRecordService', `Payload:`, JSON.stringify(payload, null, 2));

        return payload;
    }

    /**
     * Create Document records via relationship URL (Option B)
     *
     * Alternative approach: POST to relationship endpoint instead of @odata.bind
     * Example: POST /sprk_matters(guid)/sprk_matter_document
     *
     * Use case: When @odata.bind has issues or for specific business requirements
     *
     * @param files - Array of uploaded file metadata from SPE
     * @param parentContext - Parent entity context
     * @param formData - Form data (document name, description)
     * @returns Array of creation results (success/error per file)
     */
    async createDocumentsViaRelationship(
        files: SpeFileMetadata[],
        parentContext: ParentContext,
        formData: FormData
    ): Promise<CreateResult[]> {
        logInfo('DocumentRecordService', `Creating ${files.length} Document records via relationship URL`);

        const results: CreateResult[] = [];

        // Get entity configuration
        const config = getEntityDocumentConfig(parentContext.parentEntityName);
        if (!config) {
            throw new Error(`Unsupported entity type: ${parentContext.parentEntityName}`);
        }

        // Query collection navigation property for parent → child relationship
        const collectionNavProp = await MetadataService.getCollectionNavProp(
            this.context,
            parentContext.parentEntityName,    // Parent entity (e.g., "sprk_matter")
            config.relationshipSchemaName      // Relationship (e.g., "sprk_matter_document")
        );

        // Sanitize GUID
        const sanitizedGuid = parentContext.parentRecordId
            .replace(/[{}]/g, '')
            .toLowerCase();

        // Relationship URL: /sprk_matters(guid)/sprk_matter_document
        const relationshipUrl = `/${config.entitySetName}(${sanitizedGuid})/${collectionNavProp}`;

        logInfo('DocumentRecordService', `Relationship URL: ${relationshipUrl}`);

        // NOTE: context.webAPI.createRecord() doesn't support relationship URL as 3rd parameter
        // This method would require using Web API directly via fetch/XMLHttpRequest
        // For now, Option B is not implemented - use createDocuments() (Option A) instead
        //
        // Example of how this would work with direct Web API:
        // const response = await fetch(`${baseUrl}/api/data/v9.2${relationshipUrl}`, {
        //     method: 'POST',
        //     headers: { 'Content-Type': 'application/json' },
        //     body: JSON.stringify(payload)
        // });

        throw new Error(
            'Option B (relationship URL) is not yet implemented. ' +
            'Please use createDocuments() method (Option A with @odata.bind) instead.'
        );

        const successCount = results.filter(r => r.success).length;
        const failureCount = results.filter(r => !r.success).length;
        logInfo('DocumentRecordService', `Created ${successCount} records, ${failureCount} failures`);

        return results;
    }
}
