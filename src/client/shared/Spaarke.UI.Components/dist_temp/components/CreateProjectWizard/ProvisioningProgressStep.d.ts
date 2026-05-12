/**
 * ProvisioningProgressStep.tsx
 * Shows real-time provisioning progress during Secure Project infrastructure setup.
 *
 * Displayed in the wizard after project record creation when sprk_issecure = true.
 * Renders an animated list of provisioning steps with a spinner for the active step
 * and a checkmark / error icon for completed steps.
 *
 * States:
 *   - pending   -> neutral text
 *   - active    -> spinner + blue text
 *   - done      -> green checkmark
 *   - error     -> red X + error message
 *
 * Constraints:
 *   - Fluent v9 only: Spinner, Text, makeStyles, tokens
 *   - makeStyles with semantic tokens — ZERO hard-coded colours (ADR-021)
 *   - Supports light, dark, and high-contrast modes
 */
import * as React from 'react';
import { type ProvisioningStepKey } from './provisioningService';
export type ProvisioningStepStatus = 'pending' | 'active' | 'done' | 'error';
export interface IProvisioningStepState {
    key: ProvisioningStepKey;
    status: ProvisioningStepStatus;
}
export interface IProvisioningProgressStepProps {
    /** Current state of each provisioning step. */
    steps: IProvisioningStepState[];
    /**
     * Error message to display below the steps when provisioning fails.
     * When set, the step with status 'error' is highlighted.
     */
    errorMessage?: string;
}
export declare const ProvisioningProgressStep: React.FC<IProvisioningProgressStepProps>;
export default ProvisioningProgressStep;
//# sourceMappingURL=ProvisioningProgressStep.d.ts.map