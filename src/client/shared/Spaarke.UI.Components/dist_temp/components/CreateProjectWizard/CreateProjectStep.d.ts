/**
 * CreateProjectStep.tsx
 * Form step for the "Create New Project" wizard — 2-column grid with lookup fields.
 *
 * Mirrors CreateMatter/CreateRecordStep with AI pre-fill support:
 *   - On mount, sends uploaded files to BFF /workspace/projects/pre-fill
 *   - AI-extracted display names are fuzzy-matched against Dataverse lookups
 *   - Pre-filled fields show "AI" badge via AiFieldTag
 *   - Skeleton loading state while AI is processing
 *
 * Layout (CSS Grid):
 *   +---------------------------+------------------------------+
 *   |  Project Type (lookup)    |  Practice Area (lookup)       |
 *   +---------------------------+------------------------------+
 *   |  Project Name (Input, full-width) *                       |
 *   +---------------------------+------------------------------+
 *   |  Project Description (Textarea, full-width, optional)     |
 *   +----------------------------------------------------------+
 *
 * Form validation:
 *   Required: Project Name (non-empty after trim)
 *   -> `onValidChange(true)` emitted when projectName has a value
 *
 * Constraints:
 *   - Fluent v9 only: Input, Textarea, Field, Text, Skeleton, Badge
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 */
import * as React from 'react';
import { ICreateProjectFormState } from './projectFormTypes';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
export interface ICreateProjectStepProps {
    /** IDataService reference for Dataverse lookup queries. */
    dataService: IDataService;
    /** Called when form validity changes. Parent uses this to enable/disable Next. */
    onValidChange: (isValid: boolean) => void;
    /** Called on every form change with the latest form values. */
    onFormValues: (values: ICreateProjectFormState) => void;
    /**
     * Actual uploaded file objects from Step 1. Needed for multipart/form-data
     * upload to the BFF AI pre-fill endpoint. Empty array if no files uploaded.
     */
    uploadedFiles?: IUploadedFile[];
    /**
     * Initial form values from the parent. When provided with non-empty values
     * (e.g. on remount after navigating back), the step initialises from these
     * instead of starting empty. This preserves user edits and Assign Resources
     * overrides that were written to the parent's form state.
     */
    initialFormValues?: ICreateProjectFormState;
    /** MSAL-backed authenticated fetch function for BFF API calls. */
    authenticatedFetch?: typeof fetch;
    /** BFF API base URL. */
    bffBaseUrl?: string;
    /**
     * Optional navigation service. When provided, Project Type and Practice Area
     * fields use the standard Dataverse lookup side pane (openLookup) instead of
     * the inline search-as-you-type dropdown.
     *
     * Falls back to inline LookupField when absent (e.g., BFF SPA context).
     */
    navigationService?: INavigationService;
}
export declare const CreateProjectStep: React.FC<ICreateProjectStepProps>;
export { CreateProjectStep as default };
//# sourceMappingURL=CreateProjectStep.d.ts.map