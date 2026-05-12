/**
 * WizardStepper.tsx
 * Vertical sidebar step indicator for multi-step wizard dialogs.
 * Renders an ordered list of steps with status-driven visual states
 * (pending / active / completed). Supports dynamic steps added at runtime.
 */
import * as React from 'react';
import type { IWizardShellStep } from './wizardShellTypes';
export interface IWizardStepperProps {
    /** Ordered step descriptors to render in the sidebar. */
    steps: IWizardShellStep[];
}
export declare const WizardStepper: React.FC<IWizardStepperProps>;
//# sourceMappingURL=WizardStepper.d.ts.map