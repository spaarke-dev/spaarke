/**
 * WizardSuccessScreen.tsx
 *
 * Generic, domain-free success screen rendered by WizardShell after the
 * consumer's `onFinish` callback resolves with an IWizardSuccessConfig.
 *
 * Layout:
 *   +--------------------------------------------------------------+
 *   |                       [icon]                                  |
 *   |                     title text                                |
 *   |                    body content                               |
 *   |              [Action 1]   [Action 2]                          |
 *   |                                                               |
 *   |  -- Warnings (optional) ------------------------------------ |
 *   |  ! Warning message 1                                         |
 *   |  ! Warning message 2                                         |
 *   +--------------------------------------------------------------+
 *
 * All content is injected via IWizardSuccessConfig — this component
 * has ZERO domain-specific knowledge.
 *
 * Constraints:
 *   - Fluent v9 only: Text, MessageBar — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 *   - No domain-specific imports
 */
import * as React from 'react';
import type { IWizardSuccessConfig } from './wizardShellTypes';
interface Props {
    config: IWizardSuccessConfig;
}
export declare const WizardSuccessScreen: React.FC<Props>;
export {};
//# sourceMappingURL=WizardSuccessScreen.d.ts.map