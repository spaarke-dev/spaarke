/**
 * AssignWorkStep.tsx
 * Follow-on step: "Assign Work" -- assign internal resources and law firm.
 *
 * Sections:
 *   - Internal Resources: Assigned Attorney (contact), Assigned Paralegal (contact)
 *   - Assigned Law Firm: Law Firm (organization), Law Firm Attorney (contact filtered by firm)
 *   - Notify assigned resources checkbox
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import type { IAssignWorkState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
export interface IAssignWorkStepProps {
    dataService: IDataService;
    authenticatedFetch: AuthenticatedFetchFn;
    bffBaseUrl: string;
    containerId?: string;
    onFormValues: (values: IAssignWorkState) => void;
    initialValues?: IAssignWorkState;
}
export declare const AssignWorkStep: React.FC<IAssignWorkStepProps>;
//# sourceMappingURL=AssignWorkStep.d.ts.map