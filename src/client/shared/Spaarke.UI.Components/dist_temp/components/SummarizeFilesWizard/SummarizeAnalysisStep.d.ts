/**
 * SummarizeAnalysisStep.tsx
 * Follow-on step for "Work on Analysis" in the Summarize Files wizard.
 *
 * When the user clicks a playbook card this step:
 *   1. Queries the current user's business unit to obtain sprk_containerid.
 *   2. Creates sprk_document records for each uploaded file via BFF
 *      POST /api/v1/documents (ADR-013: AI features use BFF, not Xrm.WebApi).
 *   3. Collects the created document IDs.
 *   4. Opens the PlaybookLibrary Code Page via navigationService.openDialog,
 *      passing documentIds as a comma-separated query parameter.
 *
 * NFR-05: document creation must complete within 3 seconds for ≤10 files.
 * Partial failures are handled gracefully — successfully-created documents
 * are still passed to PlaybookLibrary; failed documents are reported to the
 * user via a warning MessageBar.
 */
import * as React from 'react';
import type { AuthenticatedFetchFn } from '../Playbook';
import type { IDataService } from '../../types/serviceInterfaces';
import type { INavigationService } from '../../types/serviceInterfaces';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
/**
 * Result of creating a single sprk_document record.
 */
export interface ICreateDocumentResult {
    fileName: string;
    documentId?: string;
    error?: string;
}
export interface ISummarizeAnalysisStepProps {
    /** IDataService reference for Dataverse operations. */
    dataService: IDataService;
    /** Navigation service for opening entity records and Code Page dialogs. */
    navigationService?: INavigationService;
    /** Files uploaded in the wizard (used to create sprk_document records). */
    uploadedFiles?: IUploadedFile[];
    /** Authenticated fetch function for BFF API calls (required for document creation). */
    authenticatedFetch?: AuthenticatedFetchFn;
    /** Base URL of the BFF API (e.g. "https://spe-api-dev.azurewebsites.net"). */
    bffBaseUrl?: string;
}
export declare const SummarizeAnalysisStep: React.FC<ISummarizeAnalysisStepProps>;
//# sourceMappingURL=SummarizeAnalysisStep.d.ts.map