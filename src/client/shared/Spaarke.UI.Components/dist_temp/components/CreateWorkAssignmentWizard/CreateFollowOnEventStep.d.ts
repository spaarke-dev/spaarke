/**
 * CreateFollowOnEventStep.tsx
 * Follow-on step: "Create Event" -- linked to the work assignment.
 *
 * Fields: Name (required, default "Assign Work"), Description, Priority,
 *         Due Date, Final Due Date, Assigned To (systemuser).
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import type { ICreateFollowOnEventState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateFollowOnEventStepProps {
    dataService: IDataService;
    onValidChange: (isValid: boolean) => void;
    onFormValues: (values: ICreateFollowOnEventState) => void;
    initialValues?: ICreateFollowOnEventState;
}
export declare const CreateFollowOnEventStep: React.FC<ICreateFollowOnEventStepProps>;
//# sourceMappingURL=CreateFollowOnEventStep.d.ts.map