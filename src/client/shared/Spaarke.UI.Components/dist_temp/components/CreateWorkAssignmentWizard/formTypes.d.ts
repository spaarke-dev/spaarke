/**
 * formTypes.ts
 * Form state types for the Work Assignment wizard.
 *
 * Entity: sprk_workassignment
 */
export interface ICreateWorkAssignmentFormState {
    /** Step 1: Record Type — which entity type the work relates to. */
    recordType: '' | 'matter' | 'project' | 'invoice' | 'event';
    /** Step 1: Selected record GUID. */
    recordId: string;
    /** Step 1: Selected record display name. */
    recordName: string;
    /** Step 1: If true, no specific record is linked. */
    assignWithoutRecord: boolean;
    /** Step 3: Work assignment name (required). Maps to sprk_name. */
    name: string;
    /** Step 3: Description. Maps to sprk_description. */
    description: string;
    /** Step 3: Matter Type lookup GUID. */
    matterTypeId: string;
    /** Step 3: Matter Type display name. */
    matterTypeName: string;
    /** Step 3: Practice Area lookup GUID. */
    practiceAreaId: string;
    /** Step 3: Practice Area display name. */
    practiceAreaName: string;
    /** Step 3: Priority option set (100000000-100000003). */
    priority: number;
    /** Step 3: Response Due Date — ISO date string. Maps to sprk_responseduedate. */
    responseDueDate: string;
}
export declare const EMPTY_WORK_ASSIGNMENT_FORM: ICreateWorkAssignmentFormState;
export type WorkAssignmentFollowOnId = 'assign-work' | 'send-email' | 'create-event';
export interface IAssignWorkState {
    assignedAttorneyId: string;
    assignedAttorneyName: string;
    assignedParalegalId: string;
    assignedParalegalName: string;
    assignedLawFirmId: string;
    assignedLawFirmName: string;
    assignedLawFirmAttorneyId: string;
    assignedLawFirmAttorneyName: string;
    notifyResources: boolean;
}
export declare const EMPTY_ASSIGN_WORK_STATE: IAssignWorkState;
export interface ICreateFollowOnEventState {
    eventName: string;
    eventDescription: string;
    eventPriority: number;
    eventDueDate: string;
    eventFinalDueDate: string;
    assignedToId: string;
    assignedToName: string;
    addTodo: boolean;
}
export declare const EMPTY_FOLLOW_ON_EVENT_STATE: ICreateFollowOnEventState;
export interface ICreateWorkAssignmentResult {
    status: 'success' | 'partial' | 'error';
    workAssignmentId?: string;
    workAssignmentName?: string;
    errorMessage?: string;
    warnings: string[];
}
export declare const WA_FOLLOW_ON_STEP_ID_MAP: Record<WorkAssignmentFollowOnId, string>;
export declare const WA_FOLLOW_ON_STEP_LABEL_MAP: Record<WorkAssignmentFollowOnId, string>;
export declare const WA_FOLLOW_ON_CANONICAL_ORDER: string[];
//# sourceMappingURL=formTypes.d.ts.map