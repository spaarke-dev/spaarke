/**
 * SummaryResultsStep.tsx
 * Step 2 of the Summarize New File(s) wizard — displays AI-generated summary results.
 *
 * Sections rendered (conditionally):
 *   - TL;DR (always)
 *   - Summary (always)
 *   - File-by-File Highlights (multi-file only)
 *   - Related Practice Areas (if detected)
 *   - Who's Mentioned (if parties found)
 *   - Call to Action (if actionable items found)
 */
import * as React from 'react';
import { Badge, Button, makeStyles, MessageBar, MessageBarBody, Text, tokens, } from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import { AiProgressStepper } from '../AiProgressStepper';
import { DOCUMENT_ANALYSIS_STEPS } from '../AiProgressStepper';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
        overflowY: 'auto',
        maxHeight: '100%',
        paddingRight: tokens.spacingHorizontalS,
    },
    loadingContainer: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingVerticalL,
        minHeight: '300px',
    },
    sectionHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        marginBottom: tokens.spacingVerticalXS,
    },
    paragraph: {
        lineHeight: '1.6',
        whiteSpace: 'pre-wrap',
        color: tokens.colorNeutralForeground1,
    },
    fileCard: {
        border: `1px solid ${tokens.colorNeutralStroke2}`,
        borderRadius: tokens.borderRadiusMedium,
        padding: tokens.spacingVerticalM,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    fileHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    bulletList: {
        margin: 0,
        paddingLeft: '20px',
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    tagsContainer: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: tokens.spacingHorizontalS,
    },
    partiesTable: {
        width: '100%',
        borderCollapse: 'collapse',
        '& th': {
            textAlign: 'left',
            padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
            borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
            color: tokens.colorNeutralForeground3,
            fontWeight: 600,
            fontSize: tokens.fontSizeBase200,
        },
        '& td': {
            padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
            borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
            color: tokens.colorNeutralForeground1,
            fontSize: tokens.fontSizeBase300,
        },
    },
    callToActionBox: {
        backgroundColor: tokens.colorNeutralBackground4,
        border: `1px solid ${tokens.colorNeutralStroke2}`,
        borderRadius: tokens.borderRadiusMedium,
        padding: tokens.spacingVerticalM,
    },
    confidenceBadge: {
        marginLeft: 'auto',
    },
    stepTitle: {
        display: 'block',
        marginBottom: tokens.spacingVerticalXS,
    },
    stepSubtitle: {
        display: 'block',
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalM,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const SummaryResultsStep = ({ status, result, errorMessage, onRetry, activeStepId, completedStepIds, }) => {
    const styles = useStyles();
    // Loading state
    if (status === 'loading') {
        return (React.createElement("div", { className: styles.loadingContainer },
            React.createElement(AiProgressStepper, { variant: "inline", steps: DOCUMENT_ANALYSIS_STEPS, activeStepId: activeStepId, completedStepIds: completedStepIds, title: "Analyzing Files", isStreaming: true })));
    }
    // Error state
    if (status === 'error') {
        return (React.createElement("div", { className: styles.container },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Analysis Results"),
            React.createElement(MessageBar, { intent: "error" },
                React.createElement(MessageBarBody, null, errorMessage || 'An error occurred while analyzing the files.')),
            React.createElement(Button, { appearance: "primary", onClick: onRetry }, "Retry Analysis")));
    }
    // Idle state (should not normally be shown)
    if (status === 'idle' || !result) {
        return (React.createElement("div", { className: styles.container },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Analysis Results"),
            React.createElement(Text, { size: 300, style: { color: tokens.colorNeutralForeground3 } }, "Click \"Run Analysis\" to start.")));
    }
    // Success state — render all sections
    return (React.createElement("div", { className: styles.container },
        React.createElement("div", { style: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS } },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold" }, "Analysis Results"),
            React.createElement(Badge, { appearance: "tint", icon: React.createElement(SparkleRegular, null), color: "brand" }, "AI Generated"),
            result.confidence != null && (React.createElement(Badge, { appearance: "outline", className: styles.confidenceBadge },
                Math.round(result.confidence * 100),
                "% confidence"))),
        React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "TL;DR")),
            React.createElement(Text, { size: 300, className: styles.paragraph }, result.tldr)),
        React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "Summary")),
            React.createElement(Text, { size: 300, className: styles.paragraph }, result.summary)),
        result.fileHighlights && result.fileHighlights.length > 0 && (React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "File-by-File Highlights")),
            React.createElement("div", { style: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM } }, result.fileHighlights.map((file, idx) => {
                if (!file || !file.fileName)
                    return null;
                return (React.createElement("div", { key: idx, className: styles.fileCard },
                    React.createElement("div", { className: styles.fileHeader },
                        React.createElement(Text, { size: 300, weight: "semibold" }, file.fileName),
                        file.documentType && (React.createElement(Badge, { appearance: "outline", size: "small" }, file.documentType))),
                    file.summary && (React.createElement(Text, { size: 200, className: styles.paragraph }, file.summary)),
                    Array.isArray(file.highlights) && file.highlights.length > 0 && (React.createElement("ul", { className: styles.bulletList }, file.highlights.map((h, hIdx) => (React.createElement("li", { key: hIdx },
                        React.createElement(Text, { size: 200 }, h))))))));
            })))),
        result.practiceAreas && result.practiceAreas.length > 0 && (React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "Related Practice Areas")),
            React.createElement("div", { className: styles.tagsContainer }, result.practiceAreas.map((area) => (React.createElement(Badge, { key: area, appearance: "tint", color: "informative", size: "medium" }, area)))))),
        result.mentionedParties && result.mentionedParties.length > 0 && (React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "Who's Mentioned")),
            React.createElement("table", { className: styles.partiesTable },
                React.createElement("thead", null,
                    React.createElement("tr", null,
                        React.createElement("th", null, "Name"),
                        React.createElement("th", null, "Role"))),
                React.createElement("tbody", null, result.mentionedParties.map((party, idx) => (React.createElement("tr", { key: idx },
                    React.createElement("td", null, party.name),
                    React.createElement("td", null, party.role)))))))),
        result.callToAction && (React.createElement("section", null,
            React.createElement("div", { className: styles.sectionHeader },
                React.createElement(Text, { size: 400, weight: "semibold" }, "Call to Action")),
            React.createElement("div", { className: styles.callToActionBox },
                React.createElement(Text, { size: 300, className: styles.paragraph }, result.callToAction))))));
};
//# sourceMappingURL=SummaryResultsStep.js.map