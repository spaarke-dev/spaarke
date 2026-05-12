/**
 * SelectWorkStep.tsx
 * Step 1: "Work to Assign" -- select the entity record this work relates to.
 *
 * Uses DataverseLookupField for record selection (same pattern as
 * AssociateToStep, CreateMatter, CreateProject).
 *
 * UAT pattern:
 *   - Record Type dropdown + DataverseLookupField (Xrm.Utility.lookupObjects)
 *   - Selected record display via DataverseLookupField chip
 *   - Step is marked isSkippable: true so the footer Skip button appears
 *   - Next is only enabled when a record is selected
 */
import * as React from 'react';
import type { INavigationService } from '../../types/serviceInterfaces';
import type { ICreateWorkAssignmentFormState } from './formTypes';
export interface ISelectWorkStepProps {
    onValidChange: (isValid: boolean) => void;
    onFormValues: (values: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName'>) => void;
    initialValues?: Pick<ICreateWorkAssignmentFormState, 'recordType' | 'recordId' | 'recordName'>;
    /** Navigation service for opening Dataverse lookup side pane. */
    navigationService?: INavigationService;
}
export declare const SelectWorkStep: React.FC<ISelectWorkStepProps>;
//# sourceMappingURL=SelectWorkStep.d.ts.map