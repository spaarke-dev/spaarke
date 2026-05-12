/**
 * EnterInfoStep.tsx
 * Step 3: "Enter Info" -- core work assignment fields.
 *
 * Fields: Name (required), Description, Matter Type, Practice Area,
 *         Priority (required), Response Due Date (required).
 *
 * Pre-fill:
 *   - From the record selected in Step 1 (matching fields)
 *   - From AI processing output when no record is associated
 *   The orchestrator passes pre-filled values via initialValues.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import type { IUploadedFile } from '../FileUpload/fileUploadTypes';
import type { ICreateWorkAssignmentFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export interface IEnterInfoStepProps {
    dataService: IDataService;
    onValidChange: (isValid: boolean) => void;
    onFormValues: (values: Pick<ICreateWorkAssignmentFormState, 'name' | 'description' | 'matterTypeId' | 'matterTypeName' | 'practiceAreaId' | 'practiceAreaName' | 'priority' | 'responseDueDate'>) => void;
    initialValues?: Partial<ICreateWorkAssignmentFormState>;
    /** Files uploaded in the Add Files step -- used for AI pre-fill when no record is selected. */
    uploadedFiles?: IUploadedFile[];
    /** True when initialValues came from a selected record (skip AI pre-fill). */
    hasInitialValues?: boolean;
    /** Authenticated fetch function for BFF API calls. */
    authenticatedFetch: AuthenticatedFetchFn;
    /** BFF API base URL. */
    bffBaseUrl: string;
}
export declare const EnterInfoStep: React.FC<IEnterInfoStepProps>;
//# sourceMappingURL=EnterInfoStep.d.ts.map