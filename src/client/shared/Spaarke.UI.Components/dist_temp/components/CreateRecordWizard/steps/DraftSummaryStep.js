/**
 * DraftSummaryStep.tsx
 * Follow-on step for AI-generated summary with recipient distribution.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * The AI fetch function is provided via props (no entity-specific imports).
 *
 * @see CreateRecordWizard — wires the fetchAiSummary callback from config
 */
import * as React from 'react';
import { Card, CardHeader, Textarea, Spinner, Text, Badge, makeStyles, tokens } from '@fluentui/react-components';
import { SparkleRegular, WarningRegular } from '@fluentui/react-icons';
import { RecipientField } from './RecipientField';
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
    stepTitle: { color: tokens.colorNeutralForeground1 },
    stepSubtitle: { color: tokens.colorNeutralForeground3 },
    aiBadge: {
        flexShrink: 0,
        marginTop: tokens.spacingVerticalXS,
    },
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
    summaryHeaderText: { color: tokens.colorBrandForeground2 },
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
    summaryTextarea: { width: '100%' },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const DraftSummaryStep = ({ summaryText, onSummaryChange, recipients, onRecipientsChange, ccRecipients, onCcRecipientsChange, onSearchContacts, fetchAiSummary, }) => {
    const styles = useStyles();
    const [summaryStatus, setSummaryStatus] = React.useState('idle');
    const hasFetchedRef = React.useRef(false);
    React.useEffect(() => {
        if (hasFetchedRef.current)
            return;
        hasFetchedRef.current = true;
        if (summaryText !== '') {
            setSummaryStatus('loaded');
            return;
        }
        if (!fetchAiSummary) {
            setSummaryStatus('loaded');
            return;
        }
        let cancelled = false;
        setSummaryStatus('loading');
        fetchAiSummary()
            .then(result => {
            if (cancelled)
                return;
            onSummaryChange(result.summary);
            setSummaryStatus('loaded');
        })
            .catch(() => {
            if (cancelled)
                return;
            setSummaryStatus('error');
        });
        return () => {
            cancelled = true;
        };
    }, []); // eslint-disable-line react-hooks/exhaustive-deps
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerRow },
            React.createElement("div", { className: styles.headerText },
                React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Draft Summary"),
                React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Review and edit the AI-generated summary below, then add recipient email addresses for distribution.")),
            summaryStatus === 'loaded' && fetchAiSummary && (React.createElement(Badge, { className: styles.aiBadge, appearance: "tint", color: "brand", icon: React.createElement(SparkleRegular, null) }, "AI Generated"))),
        React.createElement(Card, { className: styles.summaryCard },
            React.createElement(CardHeader, { className: styles.summaryCardHeader, header: React.createElement("div", { className: styles.summaryHeaderInner },
                    React.createElement(SparkleRegular, { "aria-hidden": "true", fontSize: 16 }),
                    React.createElement(Text, { size: 200, weight: "semibold", className: styles.summaryHeaderText }, "AI Draft Summary")) }),
            summaryStatus === 'loading' && (React.createElement("div", { className: styles.summaryLoading },
                React.createElement(Spinner, { size: "tiny" }),
                React.createElement(Text, { size: 200 }, "Generating summary\u2026"))),
            summaryStatus === 'error' && (React.createElement("div", { className: styles.summaryUnavailable },
                React.createElement(WarningRegular, { "aria-hidden": "true", fontSize: 16 }),
                React.createElement(Text, { size: 200 }, "Summary unavailable. You can type a summary manually below."))),
            (summaryStatus === 'loaded' || summaryStatus === 'error') && (React.createElement(Textarea, { className: styles.summaryTextarea, value: summaryText, onChange: e => onSummaryChange(e.target.value), placeholder: "Enter or edit the summary here\u2026", rows: 10, resize: "vertical", "aria-label": "Summary" }))),
        React.createElement(RecipientField, { label: "Distribute to", placeholder: "Search contacts or type email...", recipients: recipients, onRecipientsChange: onRecipientsChange, onSearch: onSearchContacts }),
        React.createElement(RecipientField, { label: "CC", placeholder: "Search contacts or type email...", recipients: ccRecipients, onRecipientsChange: onCcRecipientsChange, onSearch: onSearchContacts })));
};
//# sourceMappingURL=DraftSummaryStep.js.map