/**
 * Wizard module barrel export.
 *
 * Re-exports all public components and types for the generic, domain-free
 * wizard dialog shell. Consumers import from this index to get everything
 * they need to build a multi-step wizard.
 */

// Components
export { WizardShell } from './WizardShell';
export { WizardStepper } from './WizardStepper';
export { WizardSuccessScreen } from './WizardSuccessScreen';

// Reducer and initializer
export { wizardShellReducer, buildInitialShellState } from './wizardShellReducer';

// Types
export type {
  WizardStepStatus,
  IWizardShellStep,
  WizardShellAction,
  IWizardShellState,
  IWizardStepConfig,
  IWizardShellHandle,
  IWizardSuccessConfig,
  IWizardShellProps,
} from './wizardShellTypes';

// WizardStepper props type
export type { IWizardStepperProps } from './WizardStepper';
