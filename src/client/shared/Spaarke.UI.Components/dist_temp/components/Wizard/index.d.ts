/**
 * Wizard module barrel export.
 *
 * Re-exports all public components and types for the generic, domain-free
 * wizard dialog shell. Consumers import from this index to get everything
 * they need to build a multi-step wizard.
 */
export { WizardShell } from './WizardShell';
export { WizardStepper } from './WizardStepper';
export { WizardSuccessScreen } from './WizardSuccessScreen';
export { wizardShellReducer, buildInitialShellState } from './wizardShellReducer';
export type { WizardStepStatus, IWizardShellStep, WizardShellAction, IWizardShellState, IWizardStepConfig, IWizardShellHandle, IWizardSuccessConfig, IWizardShellProps, } from './wizardShellTypes';
export type { IWizardStepperProps } from './WizardStepper';
//# sourceMappingURL=index.d.ts.map