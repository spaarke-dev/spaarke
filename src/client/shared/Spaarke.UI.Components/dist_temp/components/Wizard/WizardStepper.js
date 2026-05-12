/**
 * WizardStepper.tsx
 * Vertical sidebar step indicator for multi-step wizard dialogs.
 * Renders an ordered list of steps with status-driven visual states
 * (pending / active / completed). Supports dynamic steps added at runtime.
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import { CheckmarkCircleRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    stepper: {
        display: 'flex',
        flexDirection: 'column',
        gap: '0px',
        width: '200px',
        flexShrink: 0,
        paddingTop: tokens.spacingVerticalXL,
        paddingBottom: tokens.spacingVerticalXL,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRightWidth: '1px',
        borderRightStyle: 'solid',
        borderRightColor: tokens.colorNeutralStroke2,
        boxSizing: 'border-box',
    },
    stepsLabel: {
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalM,
        textTransform: 'uppercase',
        letterSpacing: '0.05em',
    },
    stepList: {
        listStyle: 'none',
        margin: '0px',
        padding: '0px',
        display: 'flex',
        flexDirection: 'column',
        gap: '0px',
    },
    stepItem: {
        display: 'flex',
        flexDirection: 'column',
        position: 'relative',
    },
    stepRow: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
    },
    stepConnector: {
        position: 'absolute',
        left: '11px', // center under the 24px icon
        top: '36px',
        bottom: '0px',
        width: '1px',
        backgroundColor: tokens.colorNeutralStroke2,
    },
    // Step indicator circle
    indicatorPending: {
        width: '22px',
        height: '22px',
        borderRadius: '50%',
        borderTopWidth: '2px',
        borderRightWidth: '2px',
        borderBottomWidth: '2px',
        borderLeftWidth: '2px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        backgroundColor: 'transparent',
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
    },
    indicatorActive: {
        width: '22px',
        height: '22px',
        borderRadius: '50%',
        borderTopWidth: '2px',
        borderRightWidth: '2px',
        borderBottomWidth: '2px',
        borderLeftWidth: '2px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorBrandForeground1,
        borderRightColor: tokens.colorBrandForeground1,
        borderBottomColor: tokens.colorBrandForeground1,
        borderLeftColor: tokens.colorBrandForeground1,
        backgroundColor: 'transparent',
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
    },
    indicatorCompleted: {
        width: '22px',
        height: '22px',
        borderRadius: '50%',
        borderTopWidth: '0px',
        borderRightWidth: '0px',
        borderBottomWidth: '0px',
        borderLeftWidth: '0px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: 'transparent',
        borderRightColor: 'transparent',
        borderBottomColor: 'transparent',
        borderLeftColor: 'transparent',
        backgroundColor: 'transparent',
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        color: tokens.colorBrandForeground1,
    },
    // Step label text
    labelPending: {
        color: tokens.colorNeutralForeground3,
        lineHeight: '1.3',
    },
    labelActive: {
        color: tokens.colorNeutralForeground1,
        fontWeight: '600',
        lineHeight: '1.3',
    },
    labelCompleted: {
        color: tokens.colorNeutralForeground2,
        lineHeight: '1.3',
    },
    // Inner dot for active state
    activeDot: {
        width: '8px',
        height: '8px',
        borderRadius: '50%',
        backgroundColor: tokens.colorBrandForeground1,
    },
});
const StepIndicator = ({ status }) => {
    const styles = useStyles();
    if (status === 'completed') {
        return (React.createElement("span", { className: styles.indicatorCompleted, "aria-hidden": "true" },
            React.createElement(CheckmarkCircleRegular, { fontSize: 22 })));
    }
    if (status === 'active') {
        return (React.createElement("span", { className: styles.indicatorActive, "aria-hidden": "true" },
            React.createElement("span", { className: styles.activeDot })));
    }
    // pending
    return React.createElement("span", { className: styles.indicatorPending, "aria-hidden": "true" });
};
const StepRow = ({ step, isLast }) => {
    const styles = useStyles();
    const labelClass = step.status === 'active'
        ? styles.labelActive
        : step.status === 'completed'
            ? styles.labelCompleted
            : styles.labelPending;
    return (React.createElement("li", { className: styles.stepItem, role: "listitem", "aria-current": step.status === 'active' ? 'step' : undefined },
        React.createElement("div", { className: styles.stepRow },
            React.createElement(StepIndicator, { status: step.status }),
            React.createElement(Text, { size: 200, className: labelClass, "aria-label": `${step.label}, ${step.status}` }, step.label)),
        !isLast && React.createElement("span", { className: styles.stepConnector, "aria-hidden": "true" })));
};
// ---------------------------------------------------------------------------
// WizardStepper (exported)
// ---------------------------------------------------------------------------
export const WizardStepper = ({ steps }) => {
    const styles = useStyles();
    return (React.createElement("nav", { className: styles.stepper, "aria-label": "Wizard steps" },
        React.createElement(Text, { size: 100, weight: "semibold", className: styles.stepsLabel, "aria-hidden": "true" }, "Steps"),
        React.createElement("ol", { className: styles.stepList, "aria-label": "Step list" }, steps.map((step, index) => (React.createElement(StepRow, { key: step.id, step: step, isLast: index === steps.length - 1 }))))));
};
//# sourceMappingURL=WizardStepper.js.map