/**
 * DraftSummaryStep.tsx
 * Follow-on step for "Draft Summary" in the Create New Matter wizard.
 *
 * On mount: calls streamAiDraftSummary from matterService (stub / BFF).
 * Uses RecipientField for "Distribute to" and "CC" with contact lookup
 * and freeform email entry.
 *
 * Constraints:
 *   - Fluent v9: Card, Textarea, Text, Spinner, Badge
 *   - SparkleRegular for AI indicator
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import { Card, CardHeader, Textarea, Text, Badge, makeStyles, tokens, } from '@fluentui/react-components';
import { SparkleRegular, WarningRegular, } from '@fluentui/react-icons';
import { AiProgressStepper, DOCUMENT_ANALYSIS_STEPS } from '../AiProgressStepper';
import { streamAiDraftSummary } from './matterService';
import { RecipientField } from './RecipientField';
// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------
// Step IDs match DOCUMENT_ANALYSIS_STEPS from the shared library
const ALL_STEP_IDS = DOCUMENT_ANALYSIS_STEPS.map((s) => s.id);
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    headerRow: {
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalM,
    },
    headerText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    aiBadge: {
        flexShrink: 0,
        marginTop: tokens.spacingVerticalXS,
    },
    // -- AI summary card --
    summaryCard: {
        borderTopWidth: '1px',
        borderRightWidth: '1px',
        borderBottomWidth: '1px',
        borderLeftWidth: '1px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorBrandStroke2,
        borderRightColor: tokens.colorBrandStroke2,
        borderBottomColor: tokens.colorBrandStroke2,
        borderLeftColor: tokens.colorBrandStroke2,
        backgroundColor: tokens.colorBrandBackground2,
    },
    summaryCardHeader: {
        paddingBottom: tokens.spacingVerticalXS,
    },
    summaryHeaderInner: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        color: tokens.colorBrandForeground2,
    },
    summaryHeaderText: {
        color: tokens.colorBrandForeground2,
    },
    summaryLoading: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '80px',
        gap: tokens.spacingHorizontalS,
        color: tokens.colorNeutralForeground3,
    },
    summaryUnavailable: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
    },
    summaryTextarea: {
        width: '100%',
    },
});
// ---------------------------------------------------------------------------
// DraftSummaryStep (exported)
// ---------------------------------------------------------------------------
export const DraftSummaryStep = ({ formValues, summaryText, onSummaryChange, recipients, onRecipientsChange, ccRecipients, onCcRecipientsChange, onSearchContacts, authenticatedFetch, bffBaseUrl, }) => {
    const styles = useStyles();
    const [summaryStatus, setSummaryStatus] = React.useState('idle');
    const hasFetchedRef = React.useRef(false);
    const [activeStepId, setActiveStepId] = React.useState(null);
    const [completedStepIds, setCompletedStepIds] = React.useState([]);
    // -- Stream AI summary on mount (once) -- SSE-driven step state --
    React.useEffect(() => {
        if (hasFetchedRef.current)
            return;
        hasFetchedRef.current = true;
        // Only fetch if summary not already set (e.g. from parent state on re-render)
        if (summaryText !== '') {
            setSummaryStatus('loaded');
            return;
        }
        const abortController = new AbortController();
        setSummaryStatus('loading');
        setActiveStepId('document_loaded');
        setCompletedStepIds([]);
        streamAiDraftSummary(formValues.matterName, formValues.matterTypeName, formValues.practiceAreaName, {
            onProgress: (stepId) => {
                const idx = ALL_STEP_IDS.indexOf(stepId);
                setActiveStepId(stepId);
                setCompletedStepIds(ALL_STEP_IDS.slice(0, Math.max(0, idx)));
            },
        }, abortController.signal, authenticatedFetch, bffBaseUrl)
            .then((result) => {
            if (abortController.signal.aborted)
                return;
            onSummaryChange(result.summary);
            setCompletedStepIds(ALL_STEP_IDS);
            setActiveStepId(null);
            setSummaryStatus('loaded');
        })
            .catch(() => {
            if (abortController.signal.aborted)
                return;
            setSummaryStatus('error');
        });
        return () => {
            abortController.abort();
        };
    }, []); // eslint-disable-line react-hooks/exhaustive-deps
    // -- Render --
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerRow },
            React.createElement("div", { className: styles.headerText },
                React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Draft Summary"),
                React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Review and edit the AI-generated summary below, then add recipient email addresses for distribution.")),
            summaryStatus === 'loaded' && (React.createElement(Badge, { className: styles.aiBadge, appearance: "tint", color: "brand", icon: React.createElement(SparkleRegular, null) }, "AI Generated"))),
        React.createElement(Card, { className: styles.summaryCard },
            React.createElement(CardHeader, { className: styles.summaryCardHeader, header: React.createElement("div", { className: styles.summaryHeaderInner },
                    React.createElement(SparkleRegular, { "aria-hidden": "true", fontSize: 16 }),
                    React.createElement(Text, { size: 200, weight: "semibold", className: styles.summaryHeaderText }, "AI Draft Summary")) }),
            summaryStatus === 'loading' && (React.createElement(AiProgressStepper, { variant: "inline", steps: DOCUMENT_ANALYSIS_STEPS, activeStepId: activeStepId, completedStepIds: completedStepIds, title: "Generating Summary", isStreaming: true })),
            summaryStatus === 'error' && (React.createElement("div", { className: styles.summaryUnavailable },
                React.createElement(WarningRegular, { "aria-hidden": "true", fontSize: 16 }),
                React.createElement(Text, { size: 200 }, "Summary unavailable. You can type a summary manually below."))),
            (summaryStatus === 'loaded' || summaryStatus === 'error') && (React.createElement(Textarea, { className: styles.summaryTextarea, value: summaryText, onChange: (e) => onSummaryChange(e.target.value), placeholder: "Enter or edit the matter summary here\u2026", rows: 10, resize: "vertical", "aria-label": "Matter summary" }))),
        React.createElement(RecipientField, { label: "Distribute to", placeholder: "Search contacts or type email...", recipients: recipients, onRecipientsChange: onRecipientsChange, onSearch: onSearchContacts }),
        React.createElement(RecipientField, { label: "CC", placeholder: "Search contacts or type email...", recipients: ccRecipients, onRecipientsChange: onCcRecipientsChange, onSearch: onSearchContacts })));
};
//# sourceMappingURL=DraftSummaryStep.js.map