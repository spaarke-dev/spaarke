/**
 * PlanPreviewCard - Renders a proposed multi-step plan with action controls.
 *
 * Displays a plan title, ordered steps with status indicators, and three
 * action buttons: Proceed (executes the plan), Edit Plan (opens inline
 * text input to send a modification message), and Cancel (aborts).
 *
 * Steps update in real-time as execution progresses — passing updated
 * `steps` props re-renders the card with current statuses and partial results.
 *
 * This card is the gate for Phase 2F compound intent execution:
 * no plan executes until the user clicks Proceed.
 *
 * @see ADR-012 - Shared Component Library (callback-based, no Xrm)
 * @see ADR-021 - Fluent UI v9 design tokens, dark mode support
 * @see spec-2E / spec-2F - Plan preview and execution gate requirements
 */
import * as React from 'react';
import { Card, makeStyles, tokens, Text, Button, Spinner, Input, mergeClasses, } from '@fluentui/react-components';
import { CheckmarkCircle20Regular, ErrorCircle20Regular, ArrowRight20Regular, Edit20Regular, Dismiss20Regular, Clock20Regular, Stop20Regular, } from '@fluentui/react-icons';
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    card: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        paddingBottom: tokens.spacingVerticalM,
    },
    header: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    planTitle: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
        color: tokens.colorNeutralForeground1,
    },
    stepList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        listStyle: 'none',
        margin: '0',
        padding: '0',
    },
    stepItem: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
    },
    stepRow: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalS,
    },
    stepIndicator: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        width: '20px',
        height: '20px',
        marginTop: '2px',
    },
    stepNumberCircle: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '18px',
        height: '18px',
        borderRadius: '50%',
        border: `1px solid ${tokens.colorNeutralForeground3}`,
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground3,
        lineHeight: '1',
    },
    stepNumberCircleRunning: {
        border: `1px solid ${tokens.colorBrandForeground1}`,
        color: tokens.colorBrandForeground1,
    },
    iconCompleted: {
        color: tokens.colorStatusSuccessForeground1,
    },
    iconFailed: {
        color: tokens.colorStatusDangerForeground1,
    },
    iconPending: {
        color: tokens.colorNeutralForeground3,
    },
    iconRunning: {
        color: tokens.colorBrandForeground1,
    },
    stepDescription: {
        flex: 1,
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
        lineHeight: tokens.lineHeightBase300,
    },
    stepDescriptionRunning: {
        fontWeight: tokens.fontWeightSemibold,
    },
    stepDescriptionCompleted: {
        color: tokens.colorNeutralForeground2,
    },
    stepDescriptionFailed: {
        color: tokens.colorStatusDangerForeground1,
    },
    stepResult: {
        marginLeft: `calc(${tokens.spacingHorizontalS} + 20px)`,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        lineHeight: tokens.lineHeightBase200,
        fontStyle: 'italic',
        display: '-webkit-box',
        WebkitLineClamp: 3,
        WebkitBoxOrient: 'vertical',
        overflow: 'hidden',
    },
    actionRow: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        flexWrap: 'wrap',
        marginTop: tokens.spacingVerticalXS,
    },
    editPlanSection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        marginTop: tokens.spacingVerticalXS,
    },
    editPlanRow: {
        display: 'flex',
        flexDirection: 'row',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    editPlanInput: {
        flex: 1,
    },
    divider: {
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
        marginTop: tokens.spacingVerticalXS,
        marginBottom: tokens.spacingVerticalXS,
    },
});
const StepIndicator = ({ step, order, styles, showExecutionIcons }) => {
    switch (step.status) {
        case 'running':
            return (React.createElement("div", { className: styles.stepIndicator },
                React.createElement(Spinner, { size: "tiny", "aria-label": "Running" })));
        case 'completed':
            return (React.createElement("div", { className: styles.stepIndicator },
                React.createElement(CheckmarkCircle20Regular, { className: styles.iconCompleted, "aria-label": "Completed" })));
        case 'failed':
            return (React.createElement("div", { className: styles.stepIndicator },
                React.createElement(ErrorCircle20Regular, { className: styles.iconFailed, "aria-label": "Failed" })));
        case 'pending':
        default:
            // During execution, show Clock icon for pending steps; before execution, show numbered circle
            if (showExecutionIcons) {
                return (React.createElement("div", { className: styles.stepIndicator },
                    React.createElement(Clock20Regular, { className: styles.iconPending, "aria-label": `Step ${order} pending` })));
            }
            return (React.createElement("div", { className: styles.stepIndicator },
                React.createElement("span", { className: styles.stepNumberCircle, "aria-label": `Step ${order}` }, order)));
    }
};
function getDescriptionClass(status, styles) {
    switch (status) {
        case 'running':
            return mergeClasses(styles.stepDescription, styles.stepDescriptionRunning);
        case 'completed':
            return mergeClasses(styles.stepDescription, styles.stepDescriptionCompleted);
        case 'failed':
            return mergeClasses(styles.stepDescription, styles.stepDescriptionFailed);
        default:
            return styles.stepDescription;
    }
}
// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────
/**
 * PlanPreviewCard
 *
 * Renders a proposed multi-step AI plan with Proceed/Edit Plan/Cancel controls
 * and per-step progress indicators. Designed to be the execution gate for
 * compound AI intents: the plan does not execute until the user clicks Proceed.
 *
 * @example
 * ```tsx
 * <PlanPreviewCard
 *   planTitle="Analyze Contract Risk and Summarize Findings"
 *   steps={[
 *     { id: 's1', description: 'Extract key clauses', status: 'completed', result: '5 clauses found' },
 *     { id: 's2', description: 'Assess risk level', status: 'running' },
 *     { id: 's3', description: 'Generate summary', status: 'pending' },
 *   ]}
 *   isExecuting={true}
 *   onProceed={handleProceed}
 *   onCancel={handleCancel}
 *   onEditPlan={handleEditPlan}
 * />
 * ```
 */
export const PlanPreviewCard = ({ planTitle, steps, isExecuting, onProceed, onCancel, onEditPlan, onCancelExecution, }) => {
    const styles = useStyles();
    // Edit Plan mode state
    const [isEditMode, setIsEditMode] = React.useState(false);
    const [editMessage, setEditMessage] = React.useState('');
    const handleEditPlanToggle = React.useCallback(() => {
        setIsEditMode(prev => !prev);
        setEditMessage('');
    }, []);
    const handleEditSubmit = React.useCallback(() => {
        const trimmed = editMessage.trim();
        if (trimmed) {
            onEditPlan(trimmed);
            setIsEditMode(false);
            setEditMessage('');
        }
    }, [editMessage, onEditPlan]);
    const handleEditKeyDown = React.useCallback((e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            handleEditSubmit();
        }
        if (e.key === 'Escape') {
            setIsEditMode(false);
            setEditMessage('');
        }
    }, [handleEditSubmit]);
    return (React.createElement(Card, { className: styles.card, role: "region", "aria-label": `Plan preview: ${planTitle}` },
        React.createElement("div", { className: styles.header },
            React.createElement(Text, { className: styles.planTitle }, planTitle)),
        React.createElement("ol", { className: styles.stepList, "aria-label": "Plan steps" }, steps.map((step, index) => (React.createElement("li", { key: step.id, className: styles.stepItem },
            React.createElement("div", { className: styles.stepRow },
                React.createElement(StepIndicator, { step: step, order: index + 1, styles: styles, showExecutionIcons: isExecuting }),
                React.createElement(Text, { className: getDescriptionClass(step.status, styles) }, step.description)),
            step.result && (React.createElement(Text, { className: styles.stepResult, "aria-label": `Step ${index + 1} result` }, step.result)))))),
        React.createElement("div", { className: styles.divider, role: "separator" }),
        isEditMode && (React.createElement("div", { className: styles.editPlanSection },
            React.createElement("div", { className: styles.editPlanRow },
                React.createElement(Input, { className: styles.editPlanInput, placeholder: "How would you like to change this plan?", value: editMessage, onChange: (_e, data) => setEditMessage(data.value), onKeyDown: handleEditKeyDown, autoFocus: true, "aria-label": "Edit plan message" }),
                React.createElement(Button, { appearance: "primary", size: "small", onClick: handleEditSubmit, disabled: !editMessage.trim(), "aria-label": "Submit plan edit" }, "Submit"),
                React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(Dismiss20Regular, null), onClick: handleEditPlanToggle, "aria-label": "Cancel edit" })))),
        React.createElement("div", { className: styles.actionRow }, isExecuting ? (
        /* During execution: show Cancel Execution button that aborts the SSE stream */
        React.createElement(Button, { appearance: "subtle", icon: React.createElement(Stop20Regular, null), onClick: onCancelExecution, "aria-label": "Cancel execution" }, "Cancel Execution")) : (
        /* Before execution: show Proceed / Edit Plan / Cancel */
        React.createElement(React.Fragment, null,
            React.createElement(Button, { appearance: "primary", icon: React.createElement(ArrowRight20Regular, null), onClick: onProceed, "aria-label": "Proceed with plan" }, "Proceed"),
            React.createElement(Button, { appearance: "secondary", icon: React.createElement(Edit20Regular, null), onClick: handleEditPlanToggle, "aria-label": "Edit plan", "aria-pressed": isEditMode }, "Edit Plan"),
            React.createElement(Button, { appearance: "subtle", icon: React.createElement(Dismiss20Regular, null), onClick: onCancel, "aria-label": "Cancel plan" }, "Cancel"))))));
};
//# sourceMappingURL=PlanPreviewCard.js.map