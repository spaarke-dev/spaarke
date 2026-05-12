/**
 * SummarizeFilesDialog.tsx
 * Multi-step wizard dialog for "Summarize New File(s)".
 *
 * Uses WizardShell with 3 static steps + dynamic follow-on steps:
 *   0 — Upload file(s)       (FileUploadZone + UploadedFileList)
 *   1 — Run Analysis          (SummaryResultsStep — AI-generated summary)
 *   2 — Next Steps            (SummaryNextStepsStep — card selection)
 *   3+ — Follow-on steps:
 *        - Send Email          (SummarizeSendEmailStep)
 *        - Create Project      (SummarizeCreateProjectStep)
 *        - Work on Analysis    (SummarizeAnalysisStep)
 *
 * Dynamic steps are injected/removed via shellRef.current.addDynamicStep()
 * / removeDynamicStep(), mirroring the CreateMatter/WizardDialog pattern.
 *
 * This shared library version accepts `authenticatedFetch`, `bffBaseUrl`,
 * `dataService`, and `navigationService` as props — no platform-specific
 * imports are used.
 */
import * as React from 'react';
import type { AuthenticatedFetchFn } from './summarizeService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
export interface ISummarizeFilesDialogProps {
    open: boolean;
    onClose: () => void;
    /** IDataService for Dataverse operations. */
    dataService?: IDataService;
    /** Navigation service for opening entity records. */
    navigationService?: INavigationService;
    /** Authenticated fetch function for BFF API calls. */
    authenticatedFetch?: AuthenticatedFetchFn;
    /** Base URL for the BFF API. */
    bffBaseUrl?: string;
    /** When true, hides the built-in dialog chrome (for Dataverse embedded mode). */
    embedded?: boolean;
}
export declare const SummarizeFilesDialog: React.FC<ISummarizeFilesDialogProps>;
export default SummarizeFilesDialog;
//# sourceMappingURL=SummarizeFilesDialog.d.ts.map