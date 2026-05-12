/**
 * FollowOnSteps.tsx
 * "Next Steps" card selection UI and follow-on step ID/label mappings.
 *
 * Extracted from LegalWorkspace's CreateMatter/NextStepsStep.tsx into the
 * shared library so that all entity wizards share the same follow-on UX.
 *
 * The three optional follow-on actions are:
 *   1. Assign Work    — create a work assignment with resources linked to this record
 *   2. Create Event   — create a sprk_event linked to this matter/project
 *   3. Send Email     — compose introductory email to client
 *
 * @see CreateRecordWizard — parent component that syncs card selections
 *      with dynamic wizard steps via WizardShell.addDynamicStep.
 */
import * as React from 'react';
import type { FollowOnActionId } from './types';
/** Map FollowOnActionId → sidebar step ID. */
export declare const FOLLOW_ON_STEP_ID_MAP: Record<FollowOnActionId, string>;
/** Map FollowOnActionId → sidebar step label. */
export declare const FOLLOW_ON_STEP_LABEL_MAP: Record<FollowOnActionId, string>;
/** Canonical order for dynamic follow-on steps in the sidebar. */
export declare const FOLLOW_ON_CANONICAL_ORDER: string[];
export interface INextStepsStepProps {
    /** Currently selected action IDs. */
    selectedActions: FollowOnActionId[];
    /** Called when selection changes. */
    onSelectionChange: (selected: FollowOnActionId[]) => void;
    /** Entity label for text (e.g. "matter", "event"). Defaults to "record". */
    entityLabel?: string;
}
export declare const NextStepsStep: React.FC<INextStepsStepProps>;
//# sourceMappingURL=FollowOnSteps.d.ts.map