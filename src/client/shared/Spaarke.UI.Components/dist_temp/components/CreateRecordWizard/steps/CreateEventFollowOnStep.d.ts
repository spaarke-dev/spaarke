/**
 * CreateEventFollowOnStep.tsx
 * Follow-on step for collecting event details to create a sprk_event record
 * linked to the parent matter/project.
 *
 * Replaces the "AI Summary" (draft-summary) follow-on. Reuses the
 * CreateEventStep form component from the shared CreateEventWizard set.
 *
 * Pattern matches AssignWorkFollowOnStep: this is a pure form that collects
 * event field values. Actual sprk_event record creation happens in the entity
 * wizard's onFinish callback (CreateMatterWizard / CreateProjectWizard), after
 * the parent record has been created and its GUID is available.
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors
 *   - makeStyles with semantic tokens throughout
 *   - ADR-021: dark mode support via colorNeutral/colorBrand tokens
 *   - ADR-012: shared library component, no solution-specific imports
 */
import * as React from 'react';
import type { ICreateEventFormState } from '../../CreateEventWizard/formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
export interface ICreateEventFollowOnStepProps {
    /**
     * Data service for Dataverse operations.
     * Passed to CreateEventStep/EventService for sprk_eventtype_ref lookups.
     */
    dataService: IDataService;
    /**
     * Current event form field values (controlled by parent CreateRecordWizard).
     */
    formValues: ICreateEventFormState;
    /**
     * Called whenever any field in the event form changes.
     */
    onFormValues: (values: ICreateEventFormState) => void;
    /**
     * Called with the current validity state on every form change.
     * True when the minimum required fields (event name) are filled.
     */
    onValidChange: (isValid: boolean) => void;
}
export declare const CreateEventFollowOnStep: React.FC<ICreateEventFollowOnStepProps>;
//# sourceMappingURL=CreateEventFollowOnStep.d.ts.map