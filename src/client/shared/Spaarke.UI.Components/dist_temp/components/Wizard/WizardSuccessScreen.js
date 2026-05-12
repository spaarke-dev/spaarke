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
import { Text, MessageBar, MessageBarBody, makeStyles, tokens } from '@fluentui/react-components';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: tokens.spacingVerticalL,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXL,
        textAlign: 'center',
    },
    iconWrapper: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: tokens.spacingVerticalS,
    },
    titleText: {
        color: tokens.colorNeutralForeground1,
    },
    body: {
        color: tokens.colorNeutralForeground2,
        maxWidth: '400px',
    },
    actionsRow: {
        display: 'flex',
        gap: tokens.spacingHorizontalM,
        justifyContent: 'center',
        marginTop: tokens.spacingVerticalS,
    },
    warningsSection: {
        width: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        marginTop: tokens.spacingVerticalM,
        alignItems: 'stretch',
        textAlign: 'left',
    },
});
// ---------------------------------------------------------------------------
// WizardSuccessScreen (exported)
// ---------------------------------------------------------------------------
export const WizardSuccessScreen = ({ config }) => {
    const styles = useStyles();
    const hasWarnings = config.warnings != null && config.warnings.length > 0;
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.iconWrapper, "aria-hidden": "true" }, config.icon),
        React.createElement(Text, { as: "h2", size: 600, weight: "semibold", className: styles.titleText }, config.title),
        React.createElement("div", { className: styles.body }, config.body),
        hasWarnings && (React.createElement("div", { className: styles.warningsSection, "aria-live": "polite" }, config.warnings.map((warning, i) => (React.createElement(MessageBar, { key: i, intent: "warning" },
            React.createElement(MessageBarBody, null, warning))))))));
};
//# sourceMappingURL=WizardSuccessScreen.js.map