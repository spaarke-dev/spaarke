/**
 * SummaryNextStepsStep.tsx
 * Step 3 of the Summarize New File(s) wizard — checkbox card selection for
 * optional follow-on steps.
 *
 * Layout matches CreateMatter/NextStepsStep:
 *   +-------------------+ +-------------------+ +-------------------+
 *   | []  Send          | | []  Create        | | []  Work on      |
 *   |     Email         | |     Project       | |     Analysis     |
 *   +-------------------+ +-------------------+ +-------------------+
 *
 * Selecting a card dynamically injects a follow-on step into the wizard
 * sidebar (via ADD_DYNAMIC_STEP action on WizardShell).
 * Deselecting removes that step from the sidebar.
 */
import * as React from 'react';
export type SummaryActionId = 'send-email' | 'create-project' | 'work-on-analysis';
export interface IFollowOnCardDef {
    id: SummaryActionId;
    label: string;
    description: string;
    stepLabel: string;
    icon: React.ReactNode;
}
export interface ISummaryNextStepsStepProps {
    /** Currently selected action IDs. */
    selectedActions: SummaryActionId[];
    /** Called when selection changes. */
    onSelectionChange: (actions: SummaryActionId[]) => void;
    /** Whether "Include only short summary" is toggled for email. */
    includeShortSummary: boolean;
    /** Toggle for short summary in email. */
    onIncludeShortSummaryChange: (checked: boolean) => void;
}
/** Map SummaryActionId to the IWizardStep id used in the sidebar. */
export declare const FOLLOW_ON_STEP_ID_MAP: Record<SummaryActionId, string>;
/** Map SummaryActionId to the sidebar step label. */
export declare const FOLLOW_ON_STEP_LABEL_MAP: Record<SummaryActionId, string>;
/** Canonical order array for dynamic step insertion. */
export declare const FOLLOW_ON_CANONICAL_ORDER: string[];
export declare const SummaryNextStepsStep: React.FC<ISummaryNextStepsStepProps>;
//# sourceMappingURL=SummaryNextStepsStep.d.ts.map