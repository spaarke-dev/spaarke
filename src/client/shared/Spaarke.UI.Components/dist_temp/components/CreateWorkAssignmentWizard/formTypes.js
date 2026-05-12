/**
 * formTypes.ts
 * Form state types for the Work Assignment wizard.
 *
 * Entity: sprk_workassignment
 */
export const EMPTY_WORK_ASSIGNMENT_FORM = {
    recordType: '',
    recordId: '',
    recordName: '',
    assignWithoutRecord: false,
    name: '',
    description: '',
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    priority: 100000001, // Normal
    responseDueDate: '',
};
export const EMPTY_ASSIGN_WORK_STATE = {
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedLawFirmId: '',
    assignedLawFirmName: '',
    assignedLawFirmAttorneyId: '',
    assignedLawFirmAttorneyName: '',
    notifyResources: false,
};
export const EMPTY_FOLLOW_ON_EVENT_STATE = {
    eventName: 'Assign Work',
    eventDescription: '',
    eventPriority: 100000001,
    eventDueDate: '',
    eventFinalDueDate: '',
    assignedToId: '',
    assignedToName: '',
    addTodo: false,
};
// ---------------------------------------------------------------------------
// Step ID mappings for dynamic step injection
// ---------------------------------------------------------------------------
export const WA_FOLLOW_ON_STEP_ID_MAP = {
    'assign-work': 'followon-wa-assign-work',
    'send-email': 'followon-wa-send-email',
    'create-event': 'followon-wa-create-event',
};
export const WA_FOLLOW_ON_STEP_LABEL_MAP = {
    'assign-work': 'Assign Work',
    'send-email': 'Send Email',
    'create-event': 'Create Event',
};
export const WA_FOLLOW_ON_CANONICAL_ORDER = [
    'followon-wa-assign-work',
    'followon-wa-send-email',
    'followon-wa-create-event',
];
//# sourceMappingURL=formTypes.js.map