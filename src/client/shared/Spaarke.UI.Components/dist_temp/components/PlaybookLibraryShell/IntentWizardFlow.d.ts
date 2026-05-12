/**
 * IntentWizardFlow.tsx
 *
 * Streamlined 3-step wizard flow for intent-based playbook execution.
 *
 * When a PlaybookLibraryShell receives an `intent` prop with `mode === 'intent'`,
 * this component renders a focused Upload Files -> Analysis -> Results flow
 * instead of the full browse/custom-scope UI.
 *
 * The scope configuration is locked (read-only) because the intent fully
 * determines which playbook and scopes to use.
 */
import React from 'react';
import type { IPlaybook, IAction, ISkill, IKnowledge, ITool, IPlaybookScopes } from '../Playbook/types';
/**
 * Maps known intent strings to playbook identifiers.
 * When an intent is provided, the shell looks up the playbook ID here first,
 * then falls back to fuzzy name matching against available playbooks.
 */
export declare const INTENT_PLAYBOOK_MAP: Record<string, string>;
export interface IIntentWizardFlowProps {
    /** The resolved playbook for this intent. */
    playbook: IPlaybook;
    /** Locked scope configuration for the resolved playbook. */
    playbookScopes: IPlaybookScopes;
    /** All available actions (for scope preview rendering). */
    actions: IAction[];
    /** All available skills (for scope preview rendering). */
    skills: ISkill[];
    /** All available knowledge items (for scope preview rendering). */
    knowledge: IKnowledge[];
    /** All available tools (for scope preview rendering). */
    tools: ITool[];
    /** Whether the analysis is currently being created. */
    isExecuting: boolean;
    /** Error message to display, if any. */
    error: string | null;
}
export declare const IntentWizardFlow: React.FC<IIntentWizardFlowProps>;
export default IntentWizardFlow;
//# sourceMappingURL=IntentWizardFlow.d.ts.map