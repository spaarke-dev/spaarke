/**
 * CreateEventFollowOnStep.tsx
 * Follow-on step for collecting event details to create a sprk_event record
 * linked to the parent matter/project.
 *
 * Replaces the "AI Summary" (draft-summary) follow-on. Reuses the
 * CreateEventStep form component from the shared CreateEventWizard set.
 *
 * Pattern matches AssignWorkFollowOnStep: this is a pure form that collects
 * event field values. Actual sprk_event record creation happens in the entity
 * wizard's onFinish callback (CreateMatterWizard / CreateProjectWizard), after
 * the parent record has been created and its GUID is available.
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors
 *   - makeStyles with semantic tokens throughout
 *   - ADR-021: dark mode support via colorNeutral/colorBrand tokens
 *   - ADR-012: shared library component, no solution-specific imports
 */
import * as React from 'react';
import { Text, makeStyles, tokens } from '@fluentui/react-components';
import { CreateEventStep } from '../../CreateEventWizard/CreateEventStep';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
        marginBottom: tokens.spacingVerticalXS,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalM,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const CreateEventFollowOnStep = ({ dataService, formValues, onFormValues, onValidChange, }) => {
    const styles = useStyles();
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", null,
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Create Event"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Enter details for the event. It will be created and linked to the record when you click Finish.")),
        React.createElement(CreateEventStep, { dataService: dataService, onValidChange: onValidChange, onFormValues: onFormValues, initialFormValues: formValues })));
};
//# sourceMappingURL=CreateEventFollowOnStep.js.map