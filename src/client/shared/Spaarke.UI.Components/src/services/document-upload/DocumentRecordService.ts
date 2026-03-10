/**
 * Document Record Service
 *
 * Creates Document records in Dataverse using an IDataverseClient abstraction.
 * Supports two implementations via strategy pattern:
 * - PcfDataverseClient: wraps ComponentFramework.WebApi (PCF controls)
 * - ODataDataverseClient: direct OData fetch calls with token auth (Code Pages)
 *
 * Queries navigation property metadata dynamically via NavMapClient -> BFF API -> Dataverse.
 *
 * ADR Compliance:
 * - ADR-003: Separation of Concerns (service layer)
 * - ADR-010: Configuration Over Code (uses EntityDocumentConfig + dynamic metadata)
 *
 * @version 1.0.0
 */

import { NavMapClient } from './NavMapClient';
import type {
    IDataverseClient,
    ILogger,
    SpeFileMetadata,
    ParentContext,
    DocumentFormData,
    CreateResult,
    EntityDocumentConfig,
} from './types';
import { consoleLogger } from './types';

/**
 * Function that resolves an EntityDocumentConfig for a given entity name.
 * Allows the caller to inject their own config lookup (PCF uses EntityDocumentConfig map,
 * Code Pages may use a different source).
 */
export type EntityConfigResolver = (entityName: string) => EntityDocumentConfig | null;

/**
 * Configuration for DocumentRecordService.
 */
export interface DocumentRecordServiceOptions {
    /** Dataverse client implementation (PCF or OData) */
    dataverseClient: IDataverseClient;

    /** NavMap client for dynamic navigation property discovery */
    navMapClient: NavMapClient;

    /** Entity configuration resolver function */
    getEntityConfig: EntityConfigResolver;

    /** Logger implementation */
    logger?: ILogger;
}

/**
 * Service for creating Document records in Dataverse.
 */
export class DocumentRecordService {
    private readonly dataverseClient: IDataverseClient;
    private readonly navMapClient: NavMapClient;
    private readonly getEntityConfig: EntityConfigResolver;
    private readonly logger: ILogger;

    constructor(options: DocumentRecordServiceOptions) {
        this.dataverseClient = options.dataverseClient;
        this.navMapClient = options.navMapClient;
        this.getEntityConfig = options.getEntityConfig;
        this.logger = options.logger ?? consoleLogger;
    }

    /**
     * Create multiple Document records (one per uploaded file).
     *
     * Strategy: Sequential creation (one at a time).
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
        formData: DocumentFormData
    ): Promise<CreateResult[]> {
        this.logger.info('DocumentRecordService', `Creating ${files.length} Document records`);

        const results: CreateResult[] = [];

        // Sequential creation
        for (const file of files) {
            const result = await this.createSingleDocument(file, parentContext, formData);
            results.push(result);
        }

        const successCount = results.filter(r => r.success).length;
        const failureCount = results.filter(r => !r.success).length;
        this.logger.info('DocumentRecordService', `Created ${successCount} records, ${failureCount} failures`);

        return results;
    }

    /**
     * Update Document record with AI summary.
     *
     * Updates the sprk_FileSummary and sprk_FileSummaryDate fields.
     *
     * @param documentId - Dataverse Document GUID
     * @param summary - AI-generated summary text
     * @returns true if successful
     */
    async updateSummary(documentId: string, summary: string): Promise<boolean> {
        try {
            const sanitizedGuid = documentId
                .replace(/[{}]/g, '')
                .toLowerCase();

            const payload: Record<string, unknown> = {
                sprk_filesummary: summary,
                sprk_filesummarydate: new Date().toISOString(),
            };

            this.logger.info('DocumentRecordService', `Updating summary for document: ${sanitizedGuid}`);

            await this.dataverseClient.updateRecord('sprk_document', sanitizedGuid, payload);

            this.logger.info('DocumentRecordService', `Summary updated for document: ${sanitizedGuid}`);
            return true;
        } catch (error) {
            this.logger.error('DocumentRecordService', `Failed to update summary for ${documentId}`, error);
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /**
     * Create a single Document record.
     *
     * Uses IDataverseClient abstraction -- works across PCF and Code Page contexts.
     * Queries metadata dynamically for correct navigation property (case-sensitive).
     */
    private async createSingleDocument(
        file: SpeFileMetadata,
        parentContext: ParentContext,
        formData: DocumentFormData
    ): Promise<CreateResult> {
        try {
            // Unassociated mode: create document without parent lookup binding
            const isUnassociated = !parentContext.parentEntityName || !parentContext.parentRecordId;
            if (isUnassociated) {
                const payload: Record<string, unknown> = {
                    sprk_documentname: formData.documentName || file.name,
                    sprk_filename: file.name,
                    sprk_filesize: file.size,
                    sprk_graphitemid: file.id,
                    sprk_graphdriveid: parentContext.containerId,
                    sprk_filepath: file.webUrl || null,
                    sprk_documentdescription: formData.description || null,
                };
                this.logger.info('DocumentRecordService', `Creating unassociated Document: ${file.name}`);
                const result = await this.dataverseClient.createRecord('sprk_document', payload);
                this.logger.info('DocumentRecordService', `Created unassociated Document record: ${result.id}`);
                return {
                    success: true,
                    fileName: file.name,
                    recordId: result.id,
                    documentId: result.id,
                    driveId: parentContext.containerId,
                    itemId: file.id,
                };
            }

            // Get entity configuration
            const config = this.getEntityConfig(parentContext.parentEntityName);
            if (!config) {
                throw new Error(`Unsupported entity type: ${parentContext.parentEntityName}`);
            }

            // Query navigation property metadata dynamically via BFF API.
            // Falls back to hardcoded config.navigationPropertyName if NavMap is unavailable.
            this.logger.info('DocumentRecordService', `Querying navigation metadata for ${parentContext.parentEntityName}`);

            let navigationPropertyName: string;
            let targetEntitySetName: string;

            try {
                const navMetadata = await this.navMapClient.getLookupNavigation(
                    'sprk_document',
                    config.relationshipSchemaName
                );
                navigationPropertyName = navMetadata.navigationPropertyName;
                targetEntitySetName = navMetadata.targetEntity + 's';
                this.logger.info('DocumentRecordService', `Using navigation property: ${navigationPropertyName} (source: ${navMetadata.source})`);
            } catch (navError) {
                if (config.navigationPropertyName) {
                    navigationPropertyName = config.navigationPropertyName;
                    targetEntitySetName = config.entitySetName;
                    this.logger.warn('DocumentRecordService', `NavMap failed, using hardcoded fallback: ${navigationPropertyName}`, navError);
                } else {
                    throw navError;
                }
            }

            // Build record payload with correct navigation property
            const payload = this.buildRecordPayload(
                file,
                parentContext,
                formData,
                navigationPropertyName,
                targetEntitySetName
            );

            this.logger.info('DocumentRecordService', `Creating Document: ${file.name}`);

            // Create record using IDataverseClient
            const result = await this.dataverseClient.createRecord('sprk_document', payload);

            this.logger.info('DocumentRecordService', `Created Document record: ${result.id}`);

            return {
                success: true,
                fileName: file.name,
                recordId: result.id,
                documentId: result.id,
                driveId: parentContext.containerId,
                itemId: file.id,
            };
        } catch (error: unknown) {
            const errorMessage = error instanceof Error ? error.message : 'Unknown error occurred';
            this.logger.error('DocumentRecordService', `Failed to create Document for ${file.name}`, error);

            return {
                success: false,
                fileName: file.name,
                error: errorMessage,
            };
        }
    }

    /**
     * Build Dataverse record payload.
     *
     * Uses OData @odata.bind syntax for lookup fields.
     * Preserves dynamic navigation property lookup (case-sensitive).
     */
    private buildRecordPayload(
        file: SpeFileMetadata,
        parentContext: ParentContext,
        formData: DocumentFormData,
        navigationPropertyName: string,
        entitySetName: string
    ): Record<string, unknown> {
        // Sanitize GUID (remove curly braces, convert to lowercase)
        const sanitizedGuid = parentContext.parentRecordId
            .replace(/[{}]/g, '')
            .toLowerCase();

        const payload: Record<string, unknown> = {
            // Document name (use form input or file name as fallback)
            sprk_documentname: formData.documentName || file.name,

            // File metadata
            sprk_filename: file.name,
            sprk_filesize: file.size,

            // SharePoint Embedded metadata
            sprk_graphitemid: file.id,
            sprk_graphdriveid: parentContext.containerId,

            // SharePoint file URL
            sprk_filepath: file.webUrl || null,

            // Optional description
            sprk_documentdescription: formData.description || null,

            // Parent lookup using @odata.bind with single-valued navigation property
            [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${sanitizedGuid})`,
        };

        this.logger.info('DocumentRecordService', `Lookup binding: ${navigationPropertyName}@odata.bind = /${entitySetName}(${sanitizedGuid})`);

        return payload;
    }
}
