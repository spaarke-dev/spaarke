/**
 * NextStepsStep.tsx
 * Step 3 of the "Create New Matter" wizard -- checkbox card selection for
 * optional follow-on steps.
 *
 * Layout:
 *   +---------------------------------------------------------------------+
 *   |  Next Steps                                                          |
 *   |  Select any follow-on actions to complete after creating the matter. |
 *   |                                                                      |
 *   |  +-----------------+ +-----------------+ +-----------------+        |
 *   |  | [ ] Assign       | | [ ] Draft        | | [ ] Send Email  |        |
 *   |  |    Counsel       | |    Summary       | |    to Client    |        |
 *   |  +-----------------+ +-----------------+ +-----------------+        |
 *   +---------------------------------------------------------------------+
 *
 * Selecting a card dynamically injects a follow-on step into the wizard
 * sidebar (via ADD_DYNAMIC_STEP action on WizardDialog reducer).
 * Deselecting removes that step from the sidebar.
 *
 * The parent wizard reads `selectedActions` via the `onSelectionChange` prop
 * to build `IFollowOnActions` for the finish handler.
 *
 * Constraints:
 *   - Fluent v9: Card, Text, Checkbox -- ZERO hardcoded colors
 *   - makeStyles with semantic tokens throughout
 *   - Icons: PersonRegular, DocumentTextRegular, MailRegular
 */
import * as React from 'react';
export type FollowOnActionId = 'assign-counsel' | 'draft-summary' | 'send-email';
export interface IFollowOnCardDef {
    id: FollowOnActionId;
    label: string;
    description: string;
    stepLabel: string;
    icon: React.ReactNode;
}
export interface INextStepsStepProps {
    /** Currently selected action IDs. */
    selectedActions: FollowOnActionId[];
    /** Called when selection changes. */
    onSelectionChange: (selected: FollowOnActionId[]) => void;
    /**
     * Label for the entity being created (e.g. "matter" or "project").
     * Used in subtitle and skip hint text. Defaults to "matter".
     */
    entityLabel?: string;
}
export declare const NextStepsStep: React.FC<INextStepsStepProps>;
/** Map FollowOnActionId to the IWizardStep id used in the sidebar. */
export declare const FOLLOW_ON_STEP_ID_MAP: Record<FollowOnActionId, string>;
/** Map FollowOnActionId to the sidebar step label. */
export declare const FOLLOW_ON_STEP_LABEL_MAP: Record<FollowOnActionId, string>;
//# sourceMappingURL=NextStepsStep.d.ts.map