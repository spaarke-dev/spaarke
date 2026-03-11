/**
 * uploadOrchestrator.ts
 *
 * Coordinates the 4-phase upload pipeline for the DocumentUploadWizard:
 *
 *   Phase 1: Upload files to SPE via MultiFileUploadService (parallel)
 *   Phase 2: Create sprk_document records in Dataverse via DocumentRecordService
 *   Phase 3: Kick off Document Profile playbook (background, non-blocking)
 *   Phase 4: Fire-and-forget RAG indexing POST /api/ai/rag/index-file
 *
 * Design decisions:
 *   - Per-file error isolation: one file failure does not abort the batch
 *   - Progress callbacks for per-file status updates
 *   - Phases 3 & 4 are fire-and-forget (don't block wizard completion)
 *   - Chunked upload detection: files > threshold use upload session (future)
 *
 * @see ADR-007  - All SPE operations through BFF API
 * @see ADR-013  - AI Architecture (RAG indexing)
 */

import type {
    ITokenProvider,
    IDataverseClient,
    ILogger,
    SpeFileMetadata,
    ParentContext,
    DocumentFormData,
    CreateResult,
    EntityDocumentConfig,
    UploadFilesResult,
} from "@spaarke/ui-components/services/document-upload";
import {
    SdapApiClient,
    FileUploadService,
    MultiFileUploadService,
    NavMapClient,
    DocumentRecordService,
    consoleLogger,
} from "@spaarke/ui-components/services/document-upload";
import type { EntityConfigResolver } from "@spaarke/ui-components/services/document-upload";

import { authenticatedFetch } from "@spaarke/auth";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Per-file status during the orchestrated upload pipeline. */
export type FileUploadPhase =
    | "queued"
    | "uploading"
    | "creating-record"
    | "profiling"
    | "complete"
    | "error";

/** Progress update emitted by the orchestrator for each file. */
export interface OrchestratorFileProgress {
    /** File name (correlates to the original File object). */
    fileName: string;
    /** Current phase in the pipeline. */
    phase: FileUploadPhase;
    /** Upload progress percentage (0-100, only meaningful during "uploading"). */
    uploadPercent: number;
    /** Error message if phase is "error". */
    errorMessage?: string;
    /** Created Dataverse record ID (populated after Phase 2). */
    documentRecordId?: string;
}

/** Overall result of the orchestrated upload. */
export interface OrchestratorResult {
    /** True if at least one file completed all phases successfully. */
    success: boolean;
    /** Total files attempted. */
    totalFiles: number;
    /** Files that completed all phases. */
    successCount: number;
    /** Files that failed at any phase. */
    failureCount: number;
    /** Per-file detailed results. */
    fileResults: OrchestratorFileResult[];
}

/** Detailed result for a single file across all phases. */
export interface OrchestratorFileResult {
    fileName: string;
    /** SPE metadata (populated after Phase 1 upload). */
    speMetadata?: SpeFileMetadata;
    /** Dataverse record creation result (populated after Phase 2). */
    createResult?: CreateResult;
    /** Whether this file completed successfully through all critical phases (1 & 2). */
    success: boolean;
    /** Error message if any critical phase failed. */
    errorMessage?: string;
}

/** Configuration for the upload orchestrator. */
export interface UploadOrchestratorConfig {
    /** BFF API base URL. */
    bffBaseUrl: string;
    /** Token provider for BFF API calls (SPE operations). */
    bffTokenProvider: ITokenProvider;
    /** Dataverse client for record operations. */
    dataverseClient: IDataverseClient;
    /** Entity config resolver for DocumentRecordService. */
    entityConfigResolver: EntityConfigResolver;
    /** Parent context (entity type, ID, container). */
    parentContext: ParentContext;
    /** Logger implementation. */
    logger?: ILogger;
    /** Callback invoked on 401 to clear token caches. */
    onUnauthorized?: () => void;
}

/** Chunked upload threshold: 4 MB. Files larger than this could use chunked sessions. */
const CHUNKED_UPLOAD_THRESHOLD_BYTES = 4 * 1024 * 1024;

// ---------------------------------------------------------------------------
// Upload Orchestrator
// ---------------------------------------------------------------------------

/**
 * Orchestrate the full upload pipeline for a set of files.
 *
 * @param files - Files selected by the user in Step 1
 * @param config - Orchestrator configuration
 * @param onProgress - Per-file progress callback
 * @returns Overall orchestration result
 */
export async function orchestrateUpload(
    files: File[],
    config: UploadOrchestratorConfig,
    onProgress?: (progress: OrchestratorFileProgress) => void,
): Promise<OrchestratorResult> {
    const logger = config.logger ?? consoleLogger;

    logger.info("UploadOrchestrator", `Starting upload pipeline for ${files.length} files`);

    // Initialize service graph
    const sdapClient = new SdapApiClient({
        baseUrl: config.bffBaseUrl,
        getAccessToken: config.bffTokenProvider,
        logger,
        onUnauthorized: config.onUnauthorized,
    });

    const fileUploadService = new FileUploadService(sdapClient, logger);
    const multiFileUploadService = new MultiFileUploadService(fileUploadService, logger);

    const navMapClient = new NavMapClient({
        baseUrl: config.bffBaseUrl,
        getAccessToken: config.bffTokenProvider,
        logger,
        onUnauthorized: config.onUnauthorized,
    });

    const documentRecordService = new DocumentRecordService({
        dataverseClient: config.dataverseClient,
        navMapClient,
        getEntityConfig: config.entityConfigResolver,
        logger,
    });

    // Track per-file results
    const fileResults: OrchestratorFileResult[] = files.map((f) => ({
        fileName: f.name,
        success: false,
    }));

    // Emit initial queued status for all files
    for (const file of files) {
        onProgress?.({
            fileName: file.name,
            phase: "queued",
            uploadPercent: 0,
        });
    }

    // ── Phase 1: Upload files to SPE ────────────────────────────────────────
    logger.info("UploadOrchestrator", "Phase 1: Uploading files to SPE");

    // Emit uploading status
    for (const file of files) {
        onProgress?.({
            fileName: file.name,
            phase: "uploading",
            uploadPercent: 0,
        });
    }

    const uploadResult: UploadFilesResult = await multiFileUploadService.uploadFiles(
        {
            files,
            containerId: config.parentContext.containerId,
        },
        (progress) => {
            // Map MultiFileUploadService progress to orchestrator progress
            const percent = progress.status === "complete" ? 100
                : progress.status === "failed" ? 0
                : 50; // "uploading" — no granular % from MultiFileUploadService

            onProgress?.({
                fileName: progress.currentFileName,
                phase: progress.status === "failed" ? "error" : "uploading",
                uploadPercent: percent,
                errorMessage: progress.error,
            });
        },
    );

    // Map upload results back to fileResults
    const uploadedFileMap = new Map<string, SpeFileMetadata>();
    for (const uploaded of uploadResult.uploadedFiles) {
        uploadedFileMap.set(uploaded.name, uploaded);
    }

    // Mark failed uploads in fileResults
    for (const err of uploadResult.errors) {
        const idx = fileResults.findIndex((r) => r.fileName === err.fileName);
        if (idx >= 0) {
            fileResults[idx].errorMessage = err.error;
            onProgress?.({
                fileName: err.fileName,
                phase: "error",
                uploadPercent: 0,
                errorMessage: err.error,
            });
        }
    }

    // Mark successful uploads
    for (const [name, metadata] of uploadedFileMap) {
        const idx = fileResults.findIndex((r) => r.fileName === name);
        if (idx >= 0) {
            fileResults[idx].speMetadata = metadata;
            onProgress?.({
                fileName: name,
                phase: "uploading",
                uploadPercent: 100,
            });
        }
    }

    // ── Phase 2: Create Dataverse records ───────────────────────────────────
    const successfulUploads = uploadResult.uploadedFiles;

    if (successfulUploads.length > 0) {
        logger.info("UploadOrchestrator", `Phase 2: Creating ${successfulUploads.length} Dataverse records`);

        // Emit creating-record status
        for (const uploaded of successfulUploads) {
            onProgress?.({
                fileName: uploaded.name,
                phase: "creating-record",
                uploadPercent: 100,
            });
        }

        // Build form data — use file name as document name
        const formData: DocumentFormData = {
            documentName: "", // Will fall back to file name per file in DocumentRecordService
        };

        const createResults = await documentRecordService.createDocuments(
            successfulUploads,
            config.parentContext,
            formData,
        );

        // Map creation results back to fileResults
        for (const cr of createResults) {
            const idx = fileResults.findIndex((r) => r.fileName === cr.fileName);
            if (idx >= 0) {
                fileResults[idx].createResult = cr;
                if (cr.success) {
                    fileResults[idx].success = true;
                    fileResults[idx].errorMessage = undefined;
                    onProgress?.({
                        fileName: cr.fileName,
                        phase: "creating-record",
                        uploadPercent: 100,
                        documentRecordId: cr.recordId,
                    });
                } else {
                    fileResults[idx].errorMessage = cr.error;
                    onProgress?.({
                        fileName: cr.fileName,
                        phase: "error",
                        uploadPercent: 100,
                        errorMessage: cr.error,
                    });
                }
            }
        }

        // ── Phase 3 & 4: Background tasks (fire-and-forget) ────────────────
        const successfulRecords = createResults.filter((r) => r.success);

        if (successfulRecords.length > 0) {
            logger.info("UploadOrchestrator", `Phase 3-4: Kicking off background tasks for ${successfulRecords.length} files`);

            // Update progress to "profiling" for successful records
            for (const cr of successfulRecords) {
                onProgress?.({
                    fileName: cr.fileName,
                    phase: "profiling",
                    uploadPercent: 100,
                    documentRecordId: cr.recordId,
                });
            }

            // Fire-and-forget: don't await, don't block wizard
            void kickOffBackgroundTasks(
                successfulRecords,
                config,
                logger,
            ).then(() => {
                // Update progress to "complete" for all successful files
                for (const cr of successfulRecords) {
                    onProgress?.({
                        fileName: cr.fileName,
                        phase: "complete",
                        uploadPercent: 100,
                        documentRecordId: cr.recordId,
                    });
                }
            }).catch((err) => {
                // Background task errors are non-critical
                logger.warn("UploadOrchestrator", "Background tasks encountered errors (non-critical)", err);
                // Still mark as complete since Phases 1 & 2 succeeded
                for (const cr of successfulRecords) {
                    onProgress?.({
                        fileName: cr.fileName,
                        phase: "complete",
                        uploadPercent: 100,
                        documentRecordId: cr.recordId,
                    });
                }
            });

            // Immediately mark successful records as complete (don't wait for background)
            for (const cr of successfulRecords) {
                const idx = fileResults.findIndex((r) => r.fileName === cr.fileName);
                if (idx >= 0) {
                    onProgress?.({
                        fileName: cr.fileName,
                        phase: "complete",
                        uploadPercent: 100,
                        documentRecordId: cr.recordId,
                    });
                }
            }
        }
    }

    // ── Build final result ──────────────────────────────────────────────────
    const successCount = fileResults.filter((r) => r.success).length;
    const failureCount = fileResults.filter((r) => !r.success).length;

    const result: OrchestratorResult = {
        success: successCount > 0,
        totalFiles: files.length,
        successCount,
        failureCount,
        fileResults,
    };

    logger.info("UploadOrchestrator", `Pipeline complete: ${successCount}/${files.length} successful`);

    return result;
}

// ---------------------------------------------------------------------------
// Background tasks (Phase 3 & 4)
// ---------------------------------------------------------------------------

/**
 * Kick off background tasks for successfully uploaded & recorded files.
 * These are non-blocking and non-critical.
 */
async function kickOffBackgroundTasks(
    successfulRecords: CreateResult[],
    config: UploadOrchestratorConfig,
    logger: ILogger,
): Promise<void> {
    const tasks: Promise<void>[] = [];

    for (const record of successfulRecords) {
        if (!record.recordId || !record.itemId) continue;

        // Phase 3: Document Profile playbook (fire-and-forget)
        tasks.push(
            triggerDocumentProfile(record.recordId, config, logger)
                .catch((err) => {
                    logger.warn("UploadOrchestrator", `Profile trigger failed for ${record.fileName} (non-critical)`, err);
                }),
        );

        // Phase 4: RAG indexing (fire-and-forget)
        tasks.push(
            triggerRagIndexing(
                record.driveId ?? config.parentContext.containerId,
                record.itemId,
                record.fileName,
                config,
                logger,
            ).catch((err) => {
                logger.warn("UploadOrchestrator", `RAG indexing failed for ${record.fileName} (non-critical)`, err);
            }),
        );
    }

    await Promise.allSettled(tasks);
}

/**
 * Phase 3: Trigger Document Profile playbook via BFF API.
 * POST /api/ai/tools/document-profile/enqueue
 */
async function triggerDocumentProfile(
    documentId: string,
    config: UploadOrchestratorConfig,
    logger: ILogger,
): Promise<void> {
    try {
        const response = await authenticatedFetch(
            "/api/ai/tools/document-profile/enqueue",
            {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ documentId }),
            },
        );

        if (response.ok) {
            logger.info("UploadOrchestrator", `Document Profile enqueued for: ${documentId}`);
        } else {
            logger.warn("UploadOrchestrator", `Document Profile enqueue returned ${response.status} for: ${documentId}`);
        }
    } catch (err) {
        logger.warn("UploadOrchestrator", `Document Profile trigger failed for: ${documentId}`, err);
    }
}

/**
 * Phase 4: Trigger RAG indexing via BFF API.
 * POST /api/ai/rag/index-file
 */
async function triggerRagIndexing(
    driveId: string,
    itemId: string,
    fileName: string,
    config: UploadOrchestratorConfig,
    logger: ILogger,
): Promise<void> {
    try {
        const response = await authenticatedFetch(
            "/api/ai/rag/index-file",
            {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ driveId, itemId, fileName }),
            },
        );

        if (response.ok) {
            logger.info("UploadOrchestrator", `RAG indexing triggered for: ${fileName}`);
        } else {
            logger.warn("UploadOrchestrator", `RAG indexing returned ${response.status} for: ${fileName}`);
        }
    } catch (err) {
        logger.warn("UploadOrchestrator", `RAG indexing trigger failed for: ${fileName}`, err);
    }
}

// ---------------------------------------------------------------------------
// Entity Config (default for sprk_document parent types)
// ---------------------------------------------------------------------------

/**
 * Default entity document configurations for known parent entity types.
 * Used by the orchestrator's EntityConfigResolver.
 */
const ENTITY_CONFIGS: Record<string, EntityDocumentConfig> = {
    sprk_matter: {
        entityName: "sprk_matter",
        lookupFieldName: "sprk_matter",
        relationshipSchemaName: "sprk_matter_document",
        navigationPropertyName: "sprk_Matter",
        containerIdField: "sprk_containerid",
        displayNameField: "sprk_matternumber",
        entitySetName: "sprk_matters",
    },
    sprk_project: {
        entityName: "sprk_project",
        lookupFieldName: "sprk_project",
        relationshipSchemaName: "sprk_Project_Document_1n",
        navigationPropertyName: "sprk_Project",
        containerIdField: "sprk_containerid",
        displayNameField: "sprk_projectname",
        entitySetName: "sprk_projects",
    },
    sprk_invoice: {
        entityName: "sprk_invoice",
        lookupFieldName: "sprk_invoice",
        relationshipSchemaName: "sprk_invoice_document",
        navigationPropertyName: "sprk_Invoice",
        containerIdField: "sprk_containerid",
        displayNameField: "sprk_invoicenumber",
        entitySetName: "sprk_invoices",
    },
    account: {
        entityName: "account",
        lookupFieldName: "sprk_account",
        relationshipSchemaName: "account_document",
        containerIdField: "sprk_containerid",
        displayNameField: "name",
        entitySetName: "accounts",
    },
    contact: {
        entityName: "contact",
        lookupFieldName: "sprk_contact",
        relationshipSchemaName: "contact_document",
        containerIdField: "sprk_containerid",
        displayNameField: "fullname",
        entitySetName: "contacts",
    },
    sprk_communication: {
        entityName: "sprk_communication",
        lookupFieldName: "sprk_communication",
        relationshipSchemaName: "sprk_Communication_Document_1n",
        containerIdField: "sprk_containerid",
        displayNameField: "sprk_name",
        entitySetName: "sprk_communications",
    },
    sprk_workassignment: {
        entityName: "sprk_workassignment",
        lookupFieldName: "sprk_workassignment",
        relationshipSchemaName: "sprk_WorkAssignment_Document_1n",
        navigationPropertyName: "sprk_WorkAssignment",
        containerIdField: "sprk_containerid",
        displayNameField: "sprk_name",
        entitySetName: "sprk_workassignments",
    },
};

/**
 * Set of entity logical names that have upload configurations.
 * Used by AssociateToStep to filter the dropdown to only supported entities.
 */
export const SUPPORTED_ENTITY_TYPES = new Set(Object.keys(ENTITY_CONFIGS));

/**
 * Default entity config resolver.
 * Returns config for known parent entity types, or null for unknown types.
 */
export function defaultEntityConfigResolver(entityName: string): EntityDocumentConfig | null {
    return ENTITY_CONFIGS[entityName] ?? null;
}
