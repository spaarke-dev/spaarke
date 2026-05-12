/**
 * CreateTodoStep.tsx
 * Entity-specific form for "Create New To Do" wizard.
 *
 * Simpler form than Event: Title, Due Date, Priority, Description.
 *
 * Accepts IDataService (not IWebApi) for shared library portability.
 */
import * as React from 'react';
import type { ICreateTodoFormState } from './formTypes';
import type { IDataService } from '../../types/serviceInterfaces';
export interface ICreateTodoStepProps {
    dataService: IDataService;
    onValidChange: (isValid: boolean) => void;
    onFormValues: (values: ICreateTodoFormState) => void;
    initialFormValues?: ICreateTodoFormState;
}
export declare const CreateTodoStep: React.FC<ICreateTodoStepProps>;
//# sourceMappingURL=CreateTodoStep.d.ts.map