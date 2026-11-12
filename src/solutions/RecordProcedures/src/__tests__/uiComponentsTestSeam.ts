/**
 * The `@spaarke/ui-components` modules the Record Procedures page imports, at their source, for the jest harness (jest.config.cjs maps
 * `@spaarke/ui-components` here so the library barrel is never loaded). Every component is the REAL shared one.
 */
export { MetricCardRow } from '../../../../client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/MetricCardRow';
export type { MetricCardConfig } from '../../../../client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/types';
export { StatusBadge } from '../../../../client/shared/Spaarke.UI.Components/src/components/StatusBadge/StatusBadge';
export type { StatusBadgeTone } from '../../../../client/shared/Spaarke.UI.Components/src/components/StatusBadge/StatusBadge';
export { WizardShell } from '../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell';
export type {
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from '../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes';
