/**
 * Document Record Service
 *
 * Creates Document records in Dataverse using an IDataverseClient abstraction.
 * Current implementation (strategy pattern):
 * - ODataDataverseClient: direct OData fetch calls with token auth (Code Pages)
 * (The PCF-side PcfDataverseClient had zero instantiation sites and was deleted
 * 2026-10-03, reuse audit C-26.)
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
import { cleanGuid } from '../../utils/guid';

/**
 * Function that resolves an EntityDocumentConfig for a given entity name.
 * Allows the caller to inject their own config lookup (PCF uses EntityDocumentConfig map,
 * Code Pages may use a different source).
 */
export type EntityConfigResolver = (entityName: string) => EntityDocumentConfig | null;

/**
 * Attaches an uploaded file to the document that owns it, through the BFF
 * (`POST /api/v1/documents/{id}/file` — `SdapApiClient.attachDocumentFile`). The BFF verifies the file
 * and stamps the document's SPE pointer server-side (unified-access-control-r2 task 166 f1).
 */
export type DocumentFileAttacher = (documentId: string, file: SpeFileMetadata) => Promise<unknown>;

/**
 * Configuration for DocumentRecordService.
 */
export interface DocumentRecordServiceOptions {
  /** Dataverse client implementation (PCF or OData) */
  dataverseClient: IDataverseClient;

  /**
   * Attaches each uploaded file to its new document through the BFF. REQUIRED: the client never writes a
   * document's SPE pointer (`sprk_graphdriveid` / `sprk_graphitemid`) — those columns are field-secured and
   * writable by the BFF identity only (owner round 21 item 1).
   */
  attachFile: DocumentFileAttacher;

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
  private readonly attachFile: DocumentFileAttacher;
  private readonly logger: ILogger;

  constructor(options: DocumentRecordServiceOptions) {
    this.dataverseClient = options.dataverseClient;
    this.navMapClient = options.navMapClient;
    this.getEntityConfig = options.getEntityConfig;
    this.attachFile = options.attachFile;
    this.logger = options.logger ?? consoleLogger;
  }

  /**
   * Create the row, then have the BFF attach its file (task 166 f1). A row whose file the BFF refuses is removed
   * (best effort — `deleteRecord` when the client offers it) so no "document" is left without its file, and the
   * refusal propagates to the caller's per-file error.
   */
  private async createAndAttach(
    payload: Record<string, unknown>,
    file: SpeFileMetadata
  ): Promise<{ id: string }> {
    const result = await this.dataverseClient.createRecord('sprk_document', payload);
    try {
      await this.attachFile(result.id, file);
    } catch (attachError) {
      if (typeof this.dataverseClient.deleteRecord === 'function') {
        try {
          await this.dataverseClient.deleteRecord('sprk_document', result.id);
        } catch (deleteError) {
          this.logger.warn('DocumentRecordService', `Could not remove document ${result.id} whose file was not attached`, deleteError);
        }
      }
      throw attachError;
    }
    return result;
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
   * @param searchIndexName - Optional pre-resolved `sprk_searchindexname` from the
   *                         FR-WIZ-06 3-step chain (parent record → parent's BU → empty).
   *                         When non-empty, written to every Document payload created in
   *                         this batch. When empty / undefined the field is OMITTED so the
   *                         BFF tenant-default chain (FR-BFF-04) takes over server-side.
   *                         Per spec FR-WIZ-07 (2026-06-07).
   * @param searchIndexId - Optional GUID of `sprk_aisearchindex` lookup target. Cascaded from
   *                        parent's `sprk_ai_search_index` lookup (Phase G canonical field).
   *                        When non-empty, bound as `sprk_AI_Search_Index@odata.bind` on the
   *                        new Document. When empty/undefined, the field is OMITTED so the
   *                        Matter→Document Dataverse field mapping (or BFF resolver fallback)
   *                        handles cascade server-side.
   * @returns Array of creation results (success/error per file)
   */
  async createDocuments(
    files: SpeFileMetadata[],
    parentContext: ParentContext,
    formData: DocumentFormData,
    searchIndexName?: string,
    searchIndexId?: string
  ): Promise<CreateResult[]> {
    this.logger.info('DocumentRecordService', `Creating ${files.length} Document records`);

    const results: CreateResult[] = [];

    // Sequential creation
    for (const file of files) {
      const result = await this.createSingleDocument(file, parentContext, formData, searchIndexName, searchIndexId);
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
      const sanitizedGuid = cleanGuid(documentId);

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
    formData: DocumentFormData,
    searchIndexName?: string,
    searchIndexId?: string
  ): Promise<CreateResult> {
    try {
      // Unassociated mode: create document without parent lookup binding
      const isUnassociated = !parentContext.parentEntityName || !parentContext.parentRecordId;
      if (isUnassociated) {
        // NO SPE pointer, sprk_hasfile or sprk_filepath: the BFF stamps them when it attaches the file
        // (task 166 f1 — see buildRecordPayload). sprk_containerid stays NULL on sprk_document (design.md INV).
        const payload: Record<string, unknown> = {
          sprk_documentname: formData.documentName || file.name,
          sprk_filename: file.name,
          sprk_filesize: file.size,
          sprk_documentdescription: formData.description || null,
        };
        // FR-WIZ-07: include sprk_searchindexname when the caller resolved a non-empty value
        // via the FR-WIZ-06 3-step chain (parent record → parent's BU → empty). Empty / undefined
        // means the field is OMITTED so the BFF tenant-default chain (FR-BFF-04) applies server-side.
        // No INV-5 guard needed here — payload is freshly assembled and has no pre-existing value.
        if (this._isNonEmptyIndexName(searchIndexName)) {
          payload.sprk_searchindexname = searchIndexName;
        }
        // Phase G: bind sprk_ai_search_index lookup when the caller resolved a GUID
        // (parent's lookup → parent's BU's lookup). Nav-prop is sprk_AI_Search_Index (PascalCase).
        if (searchIndexId && searchIndexId.trim() !== '') {
          payload['sprk_AI_Search_Index@odata.bind'] = `/sprk_aisearchindexes(${searchIndexId.trim()})`;
        }
        this.logger.info('DocumentRecordService', `Creating unassociated Document: ${file.name}`);
        const result = await this.createAndAttach(payload, file);
        this.logger.info('DocumentRecordService', `Created unassociated Document record: ${result.id}`);
        return {
          success: true,
          fileName: file.name,
          recordId: result.id,
          documentId: result.id,
          driveId: file.driveId,
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
        const navMetadata = await this.navMapClient.getLookupNavigation('sprk_document', config.relationshipSchemaName);
        navigationPropertyName = navMetadata.navigationPropertyName;
        targetEntitySetName = navMetadata.targetEntity + 's';
        this.logger.info(
          'DocumentRecordService',
          `Using navigation property: ${navigationPropertyName} (source: ${navMetadata.source})`
        );
      } catch (navError) {
        if (config.navigationPropertyName) {
          navigationPropertyName = config.navigationPropertyName;
          targetEntitySetName = config.entitySetName;
          this.logger.warn(
            'DocumentRecordService',
            `NavMap failed, using hardcoded fallback: ${navigationPropertyName}`,
            navError
          );
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
        targetEntitySetName,
        searchIndexName,
        searchIndexId
      );

      this.logger.info('DocumentRecordService', `Creating Document: ${file.name}`);

      // Create record using IDataverseClient, then have the BFF attach its file (task 166 f1)
      const result = await this.createAndAttach(payload, file);

      this.logger.info('DocumentRecordService', `Created Document record: ${result.id}`);

      return {
        success: true,
        fileName: file.name,
        recordId: result.id,
        documentId: result.id,
        driveId: file.driveId,
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
   *
   * @param searchIndexName - Optional pre-resolved `sprk_searchindexname` from the FR-WIZ-06
   *                          3-step chain (parent record → parent's BU → empty). When non-empty,
   *                          included in the payload; when empty / undefined, the field is
   *                          OMITTED so the BFF tenant-default chain (FR-BFF-04) takes over
   *                          server-side. Per spec FR-WIZ-07 (2026-06-07).
   * @param searchIndexId - Optional GUID of `sprk_aisearchindex` lookup target. When non-empty,
   *                        bound as `sprk_AI_Search_Index@odata.bind`. When empty/undefined, the
   *                        Matter→Document Dataverse field mapping cascades the parent's value.
   *
   *                          CRITICAL design invariant (design.md INV): the canonical Document
   *                          container field is `sprk_graphdriveid`. `sprk_containerid` stays
   *                          NULL on `sprk_document` — Phase F backfill audit depends on this,
   *                          so this method MUST NOT set `sprk_containerid` under any circumstance.
   */
  private buildRecordPayload(
    file: SpeFileMetadata,
    parentContext: ParentContext,
    formData: DocumentFormData,
    navigationPropertyName: string,
    entitySetName: string,
    searchIndexName?: string,
    searchIndexId?: string
  ): Record<string, unknown> {
    // Sanitize GUID (remove curly braces, convert to lowercase)
    const sanitizedGuid = cleanGuid(parentContext.parentRecordId);

    const payload: Record<string, unknown> = {
      // Document name (use form input or file name as fallback)
      sprk_documentname: formData.documentName || file.name,

      // File metadata
      sprk_filename: file.name,
      sprk_filesize: file.size,

      // 🔴 NO SharePoint Embedded POINTER here (unified-access-control-r2 task 166 f1; owner round 21 item 1 (i)).
      // `sprk_graphdriveid` / `sprk_graphitemid` are the pointer every later download and RAG index-file call
      // follows AS THE APPLICATION, so they are field-secured and writable by the BFF identity only. The row is
      // created without them; createAndAttach then asks the BFF to attach the file, and the BFF stamps the pointer
      // (from the SERVER's upload answer — task 076), `sprk_hasfile` and `sprk_filepath` after verifying that the
      // caller created the row and uploaded the file, and that the file sits in the container derived for the row.
      // sprk_containerid stays NULL on sprk_document (design.md INV — Phase F backfill audit depends on this).

      // Optional description
      sprk_documentdescription: formData.description || null,

      // Parent lookup using @odata.bind with single-valued navigation property
      [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${sanitizedGuid})`,
    };

    // FR-WIZ-07: include sprk_searchindexname when the caller resolved a non-empty value via the
    // FR-WIZ-06 3-step chain (parent record → parent's BU → empty). Empty / undefined means the
    // field is OMITTED so the BFF tenant-default chain (FR-BFF-04) applies server-side. No INV-5
    // guard needed here — payload is freshly assembled and has no pre-existing value.
    if (this._isNonEmptyIndexName(searchIndexName)) {
      payload.sprk_searchindexname = searchIndexName;
    }

    // Phase G: bind sprk_ai_search_index lookup when caller resolved a GUID
    // (parent's lookup → parent's BU's lookup). Nav-prop is sprk_AI_Search_Index (PascalCase).
    if (searchIndexId && searchIndexId.trim() !== '') {
      payload['sprk_AI_Search_Index@odata.bind'] = `/sprk_aisearchindexes(${searchIndexId.trim()})`;
    }

    this.logger.info(
      'DocumentRecordService',
      `Lookup binding: ${navigationPropertyName}@odata.bind = /${entitySetName}(${sanitizedGuid})`
    );

    return payload;
  }

  /**
   * Treat `undefined`, `null`, empty / whitespace-only string, and non-string types as "empty"
   * for the FR-WIZ-06 cascade (mirrors the semantics in
   * `searchIndexResolver.ts` (task 026) and `EntityCreationService._hasExplicitValue` (task 020)).
   */
  private _isNonEmptyIndexName(value: unknown): value is string {
    return typeof value === 'string' && value.trim().length > 0;
  }
}
