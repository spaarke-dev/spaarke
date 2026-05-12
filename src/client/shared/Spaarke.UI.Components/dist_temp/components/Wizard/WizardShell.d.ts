/**
 * WizardShell.tsx
 *
 * Generic, domain-free wizard dialog shell.
 *
 * Layout:
 *   +------------------------------------------------------+
 *   | Title bar                              [X Close]      |
 *   +------------------------------------------------------+
 *   | Sidebar ~200px  |  Content area (flex: 1)             |
 *   | WizardStepper   |    - Error bar (if finishError)     |
 *   |                 |    - Success screen (if finished)    |
 *   |                 |    - Step content (renderContent)    |
 *   +------------------------------------------------------+
 *   | [Cancel]        [spinner]  [custom] [Back]  [Next]    |
 *   +------------------------------------------------------+
 *
 * The shell handles:
 *   - Navigation state via useReducer (wizardShellReducer)
 *   - Dynamic step insertion/removal via imperative handle
 *   - Finish flow with async onFinish, error display, and success screen
 *   - Layout, styles, and footer button logic
 *
 * Domain-specific content is injected via IWizardStepConfig.renderContent
 * callbacks. The shell has ZERO domain imports.
 *
 * Constraints:
 *   - Fluent v9 only: Dialog, Text, Button, Spinner, MessageBar — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 *   - No domain-specific imports
 */
import * as React from 'react';
import type { IWizardShellProps, IWizardShellHandle } from './wizardShellTypes';
export declare const WizardShell: React.ForwardRefExoticComponent<IWizardShellProps & React.RefAttributes<IWizardShellHandle>>;
//# sourceMappingURL=WizardShell.d.ts.map