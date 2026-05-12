/**
 * NextStepsSelectionStep.tsx
 * Step 4: "Next Steps" -- card selection grid for follow-on actions.
 *
 * Cards: Assign Work, Send Email, Create an Event
 * Follows the same checkbox-card pattern as CreateRecordWizard/FollowOnSteps.
 */
import * as React from 'react';
import type { WorkAssignmentFollowOnId } from './formTypes';
export interface INextStepsSelectionStepProps {
    selectedActions: WorkAssignmentFollowOnId[];
    onSelectedActionsChange: (actions: WorkAssignmentFollowOnId[]) => void;
}
export declare const NextStepsSelectionStep: React.FC<INextStepsSelectionStepProps>;
//# sourceMappingURL=NextStepsSelectionStep.d.ts.map