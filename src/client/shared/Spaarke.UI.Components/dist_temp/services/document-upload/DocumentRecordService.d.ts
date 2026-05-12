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
import type { IDataverseClient, ILogger, SpeFileMetadata, ParentContext, DocumentFormData, CreateResult, EntityDocumentConfig } from './types';
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
export declare class DocumentRecordService {
    private readonly dataverseClient;
    private readonly navMapClient;
    private readonly getEntityConfig;
    private readonly logger;
    constructor(options: DocumentRecordServiceOptions);
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
    createDocuments(files: SpeFileMetadata[], parentContext: ParentContext, formData: DocumentFormData): Promise<CreateResult[]>;
    /**
     * Update Document record with AI summary.
     *
     * Updates the sprk_FileSummary and sprk_FileSummaryDate fields.
     *
     * @param documentId - Dataverse Document GUID
     * @param summary - AI-generated summary text
     * @returns true if successful
     */
    updateSummary(documentId: string, summary: string): Promise<boolean>;
    /**
     * Create a single Document record.
     *
     * Uses IDataverseClient abstraction -- works across PCF and Code Page contexts.
     * Queries metadata dynamically for correct navigation property (case-sensitive).
     */
    private createSingleDocument;
    /**
     * Build Dataverse record payload.
     *
     * Uses OData @odata.bind syntax for lookup fields.
     * Preserves dynamic navigation property lookup (case-sensitive).
     */
    private buildRecordPayload;
}
//# sourceMappingURL=DocumentRecordService.d.ts.map