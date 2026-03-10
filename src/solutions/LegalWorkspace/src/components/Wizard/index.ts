/**
 * Wizard module barrel export.
 *
 * Re-exports from the shared @spaarke/ui-components library.
 * Local copies have been removed — all Wizard components now live in
 * src/client/shared/Spaarke.UI.Components/src/components/Wizard/.
 */
export {
  WizardShell,
  WizardStepper,
  WizardSuccessScreen,
  wizardShellReducer,
  buildInitialShellState,
} from '@spaarke/ui-components/components/Wizard';

export type {
  WizardStepStatus,
  IWizardShellStep,
  WizardShellAction,
  IWizardShellState,
  IWizardStepConfig,
  IWizardShellHandle,
  IWizardSuccessConfig,
  IWizardShellProps,
  IWizardStepperProps,
} from '@spaarke/ui-components/components/Wizard';
