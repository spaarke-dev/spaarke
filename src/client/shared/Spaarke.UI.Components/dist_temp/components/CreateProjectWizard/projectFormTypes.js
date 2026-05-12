/**
 * projectFormTypes.ts
 * Form state types for Create New Project wizard.
 *
 * Mirrors the pattern from CreateMatter/formTypes.ts, adapted for
 * the sprk_project entity fields.
 */
/** Empty default for initializing useReducer or useState. */
export const EMPTY_PROJECT_FORM = {
    projectTypeId: '',
    projectTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    projectName: '',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    description: '',
    isSecure: false,
};
//# sourceMappingURL=projectFormTypes.js.map