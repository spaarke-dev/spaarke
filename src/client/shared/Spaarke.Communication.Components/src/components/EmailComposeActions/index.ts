/**
 * `@spaarke/communication-components` — `EmailComposeActions` (task 036,
 * FR-09/FR-10/FR-15). Own barrel per this task's guardrail (do not edit
 * `components/index.ts` — the orchestrator adds the Wave-5 barrel export
 * after all P3b tasks land).
 */
export { useEmailComposeActions } from './useEmailComposeActions';
export type {
  EmailComposeActionsDeps,
  OpenComposerOptions,
  UseEmailComposeActionsResult,
} from './EmailComposeActions.types';
export { fetchCommunicationPrefill } from './fetchCommunicationPrefill';
export { OpenFullFormButton } from './OpenFullFormButton';
export type { OpenFullFormButtonProps } from './OpenFullFormButton';
