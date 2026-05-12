/**
 * wizardShellReducer.ts
 *
 * Pure reducer and initializer for the generic WizardShell navigation state.
 *
 * IMPORTANT: This file must have ZERO domain imports. It operates exclusively
 * on the generic types defined in wizardShellTypes.ts. All domain-specific
 * logic (file uploads, form values, follow-on actions) belongs in the
 * consumer's own reducer, not here.
 */
import type { IWizardShellState, IWizardStepConfig, WizardShellAction } from './wizardShellTypes';
/**
 * Build the initial WizardShell state from an ordered array of step configs.
 *
 * The first step is marked 'active'; all subsequent steps are 'pending'.
 * Only `id`, `label`, and `status` are extracted — rendering callbacks and
 * predicates remain in the config array managed by the consumer.
 *
 * @param steps - Ordered step configurations provided by the consumer.
 * @returns A fresh IWizardShellState with currentStepIndex = 0.
 */
export declare function buildInitialShellState(steps: ReadonlyArray<IWizardStepConfig>): IWizardShellState;
/**
 * Pure reducer for WizardShell navigation state.
 *
 * Handles step advancement, backward navigation, direct jump, and dynamic
 * step insertion/removal. No side effects, no domain-specific logic.
 *
 * @param state  - Current shell state.
 * @param action - One of the WizardShellAction discriminated union members.
 * @returns Updated shell state (new object if changed, same reference if no-op).
 */
export declare function wizardShellReducer(state: IWizardShellState, action: WizardShellAction): IWizardShellState;
//# sourceMappingURL=wizardShellReducer.d.ts.map