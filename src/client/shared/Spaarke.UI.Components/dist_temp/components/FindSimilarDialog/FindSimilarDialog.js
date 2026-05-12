/**
 * FindSimilarDialog - Reusable iframe dialog for the DocumentRelationshipViewer.
 *
 * Renders a near-fullscreen Dialog containing an iframe that loads the
 * DocumentRelationshipViewer Code Page web resource.
 *
 * Consumer builds the URL (since URL construction differs between PCF and
 * LegalWorkspace) and passes it in. This component just provides the dialog
 * shell with correct sizing and no scrollbars.
 *
 * Optional `embedded` prop hides the title bar chrome when the dialog is
 * rendered inside a Dataverse form (e.g., as part of a PCF control panel).
 *
 * Optional `authenticatedFetch` and `bffBaseUrl` are accepted for forward
 * compatibility with service-injected patterns but are not currently used
 * by the iframe shell itself.
 *
 * Zero hard service dependencies — fully prop-based.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see ADR-012 for shared component library patterns
 */
import * as React from 'react';
import { makeStyles, tokens, Dialog, DialogSurface, DialogBody, Button, Text, Tooltip, shorthands, } from '@fluentui/react-components';
import { DismissRegular, ArrowExpandRegular } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    surface: {
        padding: '0px',
        width: '85vw',
        maxWidth: '85vw',
        height: '85vh',
        maxHeight: '85vh',
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.overflow('hidden'),
        ...shorthands.borderRadius(tokens.borderRadiusXLarge),
    },
    titleBar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalXS,
        paddingTop: tokens.spacingVerticalXS,
        paddingRight: tokens.spacingHorizontalS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        backgroundColor: tokens.colorNeutralBackground3,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        flexShrink: 0,
    },
    body: {
        padding: '0px',
        flex: 1,
        minHeight: 0,
        position: 'relative',
    },
    frame: {
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        border: 'none',
        display: 'block',
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const FindSimilarDialog = ({ open, onClose, url, embedded = false, }) => {
    const styles = useStyles();
    const handleExpand = React.useCallback(() => {
        if (url) {
            window.open(url, '_blank', 'noopener,noreferrer');
        }
    }, [url]);
    return (React.createElement(Dialog, { open: open, onOpenChange: (_, data) => {
            if (!data.open)
                onClose();
        } },
        React.createElement(DialogSurface, { className: styles.surface },
            !embedded && (React.createElement("div", { className: styles.titleBar },
                React.createElement(Text, { weight: "semibold", size: 400 }, "Similar Documents"),
                React.createElement("div", { style: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS } },
                    React.createElement(Tooltip, { content: "Open in new tab", relationship: "label" },
                        React.createElement(Button, { appearance: "subtle", icon: React.createElement(ArrowExpandRegular, null), size: "small", onClick: handleExpand, "aria-label": "Open in new tab" })),
                    React.createElement(Tooltip, { content: "Close", relationship: "label" },
                        React.createElement(Button, { appearance: "subtle", icon: React.createElement(DismissRegular, null), size: "small", onClick: onClose, "aria-label": "Close" }))))),
            React.createElement(DialogBody, { className: styles.body }, url && React.createElement("iframe", { src: url, title: "Document Relationships", className: styles.frame })))));
};
export default FindSimilarDialog;
//# sourceMappingURL=FindSimilarDialog.js.map