/**
 * CreateEventStep.tsx
 * Entity-specific form for "Create New Event" wizard.
 *
 * Fields:
 *   - Event Name (required, Input)
 *   - Event Type (LookupField -> sprk_eventtype_ref)
 *   - Due Date (Input type="date")
 *   - Priority (Dropdown: Low/Normal/High/Urgent)
 *   - Description (Textarea)
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *
 * @see IDataService — high-level data access abstraction
 */
import * as React from 'react';
import type { ICreateEventFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateEventStepProps {
    dataService: IDataService;
    onValidChange: (isValid: boolean) => void;
    onFormValues: (values: ICreateEventFormState) => void;
    initialFormValues?: ICreateEventFormState;
}
export declare const CreateEventStep: React.FC<ICreateEventStepProps>;
//# sourceMappingURL=CreateEventStep.d.ts.map