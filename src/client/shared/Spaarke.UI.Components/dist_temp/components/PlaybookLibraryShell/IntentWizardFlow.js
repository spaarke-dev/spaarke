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
import { Text, MessageBar, MessageBarBody, Spinner, Badge, makeStyles, tokens, } from '@fluentui/react-components';
import { DocumentArrowUpRegular, BrainCircuit24Regular, CheckmarkCircle24Regular, } from '@fluentui/react-icons';
import { ScopeConfigurator } from '../Playbook/ScopeConfigurator';
// ---------------------------------------------------------------------------
// Intent-to-playbook mapping
// ---------------------------------------------------------------------------
/**
 * Maps known intent strings to playbook identifiers.
 * When an intent is provided, the shell looks up the playbook ID here first,
 * then falls back to fuzzy name matching against available playbooks.
 */
export const INTENT_PLAYBOOK_MAP = {
    'email-compose': 'playbook-email-draft',
    'assign-counsel': 'playbook-counsel-assign',
    'meeting-schedule': 'playbook-meeting-prep',
};
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    stepIndicator: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM,
        paddingBottom: tokens.spacingVerticalM,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
    },
    stepDot: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '28px',
        height: '28px',
        borderRadius: '50%',
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
    },
    stepDotInactive: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '28px',
        height: '28px',
        borderRadius: '50%',
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
    },
    stepDotCompleted: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '28px',
        height: '28px',
        borderRadius: '50%',
        backgroundColor: tokens.colorPaletteGreenBackground3,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    stepConnector: {
        width: '32px',
        height: '2px',
        backgroundColor: tokens.colorNeutralStroke2,
    },
    stepConnectorActive: {
        width: '32px',
        height: '2px',
        backgroundColor: tokens.colorBrandBackground,
    },
    playbookHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusMedium,
    },
    scopeSection: {
        opacity: 0.85,
    },
    lockedLabel: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        marginBottom: tokens.spacingVerticalS,
    },
    stepContent: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        minHeight: '200px',
    },
    uploadPlaceholder: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
        borderTopWidth: '2px',
        borderRightWidth: '2px',
        borderBottomWidth: '2px',
        borderLeftWidth: '2px',
        borderTopStyle: 'dashed',
        borderRightStyle: 'dashed',
        borderBottomStyle: 'dashed',
        borderLeftStyle: 'dashed',
        borderTopColor: tokens.colorNeutralStroke2,
        borderRightColor: tokens.colorNeutralStroke2,
        borderBottomColor: tokens.colorNeutralStroke2,
        borderLeftColor: tokens.colorNeutralStroke2,
        borderRadius: tokens.borderRadiusMedium,
        color: tokens.colorNeutralForeground3,
    },
    analysisPlaceholder: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
    },
    resultsPlaceholder: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalXXL,
        paddingBottom: tokens.spacingVerticalXXL,
    },
});
// ---------------------------------------------------------------------------
// Step definitions
// ---------------------------------------------------------------------------
const INTENT_STEPS = [
    { id: 'upload', label: 'Upload Files' },
    { id: 'analysis', label: 'Analysis' },
    { id: 'results', label: 'Results' },
];
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const IntentWizardFlow = ({ playbook, playbookScopes, actions, skills, knowledge, tools, isExecuting, error, }) => {
    const styles = useStyles();
    // Derive the current step from execution state.
    // In a full integration the step would advance as the analysis progresses;
    // for now we show "upload" by default, "analysis" while executing, and
    // "results" would be shown by the parent upon completion.
    const currentStep = isExecuting ? 'analysis' : 'upload';
    const currentStepIndex = INTENT_STEPS.findIndex(s => s.id === currentStep);
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.stepIndicator }, INTENT_STEPS.map((step, idx) => (React.createElement(React.Fragment, { key: step.id },
            idx > 0 && (React.createElement("div", { className: idx <= currentStepIndex
                    ? styles.stepConnectorActive
                    : styles.stepConnector })),
            React.createElement("div", { className: idx < currentStepIndex
                    ? styles.stepDotCompleted
                    : idx === currentStepIndex
                        ? styles.stepDot
                        : styles.stepDotInactive, title: step.label }, idx < currentStepIndex ? (React.createElement(CheckmarkCircle24Regular, null)) : (React.createElement(Text, { size: 200, weight: "semibold" }, idx + 1))),
            React.createElement(Text, { size: 200, weight: idx === currentStepIndex ? 'semibold' : 'regular', style: {
                    color: idx === currentStepIndex
                        ? tokens.colorNeutralForeground1
                        : tokens.colorNeutralForeground3,
                } }, step.label))))),
        React.createElement("div", { className: styles.playbookHeader },
            React.createElement(BrainCircuit24Regular, null),
            React.createElement("div", null,
                React.createElement(Text, { size: 300, weight: "semibold" }, playbook.name),
                playbook.description && (React.createElement(Text, { size: 200, block: true, style: { color: tokens.colorNeutralForeground3 } }, playbook.description))),
            React.createElement(Badge, { appearance: "outline", size: "small", color: "informative" }, "Locked")),
        error && (React.createElement(MessageBar, { intent: "error" },
            React.createElement(MessageBarBody, null, error))),
        React.createElement("div", { className: styles.stepContent },
            currentStep === 'upload' && (React.createElement("div", { className: styles.uploadPlaceholder },
                React.createElement(DocumentArrowUpRegular, null),
                React.createElement(Text, { size: 300 }, "Drop files here or click to upload"),
                React.createElement(Text, { size: 200, style: { color: tokens.colorNeutralForeground3 } }, "File upload will be connected in integration tasks."))),
            currentStep === 'analysis' && (React.createElement("div", { className: styles.analysisPlaceholder },
                React.createElement(Spinner, { size: "large", label: "Running analysis..." })))),
        React.createElement("div", { className: styles.scopeSection },
            React.createElement("div", { className: styles.lockedLabel },
                React.createElement(Text, { size: 200, weight: "semibold" }, "Scope Configuration"),
                React.createElement(Badge, { appearance: "outline", size: "small", color: "subtle" }, "Read-only")),
            React.createElement(ScopeConfigurator, { actions: actions, skills: skills, knowledge: knowledge, tools: tools, selectedActionIds: playbookScopes.actionIds, selectedSkillIds: playbookScopes.skillIds, selectedKnowledgeIds: playbookScopes.knowledgeIds, selectedToolIds: playbookScopes.toolIds, onActionChange: () => { }, onSkillChange: () => { }, onKnowledgeChange: () => { }, onToolChange: () => { }, readOnly: true }))));
};
IntentWizardFlow.displayName = 'IntentWizardFlow';
export default IntentWizardFlow;
//# sourceMappingURL=IntentWizardFlow.js.map