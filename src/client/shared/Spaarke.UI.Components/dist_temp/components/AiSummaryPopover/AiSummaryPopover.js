/**
 * AiSummaryPopover - Reusable AI Summary popover component.
 *
 * Displays a popover with AI-generated summary content (TLDR + full summary).
 * Fetches summary lazily on first open via callback prop. Includes copy-to-clipboard.
 *
 * Consumer provides a trigger element and an async fetch callback.
 * Zero service dependencies — fully callback-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */
import * as React from 'react';
import { useCallback, useState } from 'react';
import { makeStyles, tokens, Text, Button, Tooltip, Popover, PopoverTrigger, PopoverSurface, Spinner, shorthands, } from '@fluentui/react-components';
import { Sparkle20Filled, CopyRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    surface: {
        width: '480px',
        maxHeight: '400px',
        overflowY: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    headerRow: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingBottom: tokens.spacingVerticalXS,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
    },
    headerLabel: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
    },
    centered: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        ...shorthands.padding('16px'),
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const AiSummaryPopover = ({ trigger, onFetchSummary, positioning = 'after', withArrow = true, }) => {
    const styles = useStyles();
    const [data, setData] = useState(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(false);
    const [copied, setCopied] = useState(false);
    const handleCopy = useCallback(() => {
        if (!data)
            return;
        const text = [data.tldr, data.summary].filter(Boolean).join('\n\n');
        void navigator.clipboard.writeText(text).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 2000);
        });
    }, [data]);
    const handleOpenChange = useCallback((_ev, openData) => {
        if (openData.open && !data && !loading) {
            setLoading(true);
            setError(false);
            void onFetchSummary()
                .then(sd => {
                setData(sd);
                setLoading(false);
                return sd;
            })
                .catch(() => {
                setError(true);
                setLoading(false);
            });
        }
    }, [data, loading, onFetchSummary]);
    return (React.createElement(Popover, { positioning: positioning, withArrow: withArrow, onOpenChange: handleOpenChange },
        React.createElement(PopoverTrigger, { disableButtonEnhancement: true }, trigger),
        React.createElement(PopoverSurface, { className: styles.surface },
            React.createElement("div", { className: styles.headerRow },
                React.createElement(Text, { className: styles.headerLabel },
                    React.createElement(Sparkle20Filled, { "aria-hidden": "true" }),
                    "AI Summary"),
                data && !loading && (React.createElement(Tooltip, { content: copied ? 'Copied!' : 'Copy', relationship: "label" },
                    React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(CopyRegular, null), "aria-label": "Copy summary", onClick: handleCopy })))),
            loading && (React.createElement("div", { className: styles.centered },
                React.createElement(Spinner, { size: "small", label: "Loading summary..." }))),
            error && React.createElement(Text, null, "Summary not available for this document."),
            data && !loading && (React.createElement(React.Fragment, null,
                data.tldr && React.createElement(Text, { weight: "semibold" }, data.tldr),
                data.summary && React.createElement(Text, { style: { whiteSpace: 'pre-wrap' } }, data.summary),
                !data.summary && !data.tldr && React.createElement(Text, null, "No summary available for this document."))))));
};
export default AiSummaryPopover;
//# sourceMappingURL=AiSummaryPopover.js.map