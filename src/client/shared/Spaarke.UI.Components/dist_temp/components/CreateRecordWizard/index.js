/**
 * CreateRecordWizard barrel export.
 *
 * Provides the reusable multi-step record creation wizard and its
 * supporting types. Entity-specific wizards import from here to
 * get the orchestration layer.
 */
// Main component
export { CreateRecordWizard } from './CreateRecordWizard';
// Follow-on steps UI
export { NextStepsStep, FOLLOW_ON_STEP_ID_MAP, FOLLOW_ON_STEP_LABEL_MAP, FOLLOW_ON_CANONICAL_ORDER, } from './FollowOnSteps';
// Step components (for direct use if needed)
export { AssignWorkFollowOnStep, WORK_ASSIGNMENT_PRIORITY } from './steps/AssignWorkFollowOnStep';
export { AssignResourcesStep } from './steps/AssignResourcesStep';
export { CreateEventFollowOnStep } from './steps/CreateEventFollowOnStep';
export { SendEmailStep } from './steps/SendEmailStep';
export { RecipientField } from './steps/RecipientField';
//# sourceMappingURL=index.js.map