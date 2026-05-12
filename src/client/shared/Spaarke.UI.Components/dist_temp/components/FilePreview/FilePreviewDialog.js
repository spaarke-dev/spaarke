/**
 * FilePreviewDialog.tsx
 * Full-screen modal dialog for document preview with toolbar actions.
 *
 * Shared library version — all external service calls are injected via the
 * `services` prop (IFilePreviewServices) so the component has zero
 * environment-specific imports.
 *
 * Features:
 *   - Fluent UI v9 Dialog (85vw x 85vh, max 880px)
 *   - Iframe preview with Spinner during load, error + retry on failure
 *   - Toolbar: Open File, Open Record, Copy Link, Add/Remove Workspace
 *   - Open File: lazy-fetch open links, cascade desktop -> web -> download
 */
import * as React from 'react';
import { Dialog, DialogSurface, DialogBody, DialogTitle, DialogContent, Button, Toolbar, ToolbarButton, ToolbarDivider, Tooltip, Spinner, Text, makeStyles, shorthands, tokens, } from '@fluentui/react-components';
import { Dismiss24Regular, Open24Regular, OpenRegular, LinkRegular, StarRegular, StarFilled, } from '@fluentui/react-icons';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    surface: {
        width: '85vw',
        maxWidth: '880px',
        height: '85vh',
        maxHeight: '85vh',
        ...shorthands.padding('0px'),
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.overflow('hidden'),
        ...shorthands.borderRadius(tokens.borderRadiusXLarge),
    },
    titleBar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalS,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    titleText: {
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        flex: 1,
        minWidth: 0,
    },
    toolbar: {
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    body: {
        ...shorthands.padding('0px'),
        flex: 1,
        minHeight: 0,
        position: 'relative',
    },
    iframe: {
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        border: '0px',
    },
    centerContent: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        width: '100%',
        height: '100%',
        gap: tokens.spacingVerticalM,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const FilePreviewDialog = ({ open, documentId, documentName, onClose, services, isInWorkspace, onWorkspaceFlagChanged, }) => {
    const styles = useStyles();
    // Preview state
    const [previewUrl, setPreviewUrl] = React.useState(null);
    const [loading, setLoading] = React.useState(false);
    const [error, setError] = React.useState(false);
    // Workspace flag local state (track optimistically)
    const [inWorkspace, setInWorkspace] = React.useState(isInWorkspace ?? false);
    // Sync prop changes
    React.useEffect(() => {
        setInWorkspace(isInWorkspace ?? false);
    }, [isInWorkspace]);
    // Fetch preview URL when dialog opens
    React.useEffect(() => {
        if (!open || !documentId) {
            setPreviewUrl(null);
            setError(false);
            return;
        }
        let cancelled = false;
        setLoading(true);
        setError(false);
        (async () => {
            const url = await services.getDocumentPreviewUrl(documentId);
            if (cancelled)
                return;
            if (url) {
                setPreviewUrl(url);
            }
            else {
                setError(true);
            }
            setLoading(false);
        })();
        return () => {
            cancelled = true;
        };
    }, [open, documentId, services]);
    // Retry handler
    const handleRetry = React.useCallback(() => {
        if (!documentId)
            return;
        setLoading(true);
        setError(false);
        setPreviewUrl(null);
        (async () => {
            const url = await services.getDocumentPreviewUrl(documentId);
            if (url) {
                setPreviewUrl(url);
            }
            else {
                setError(true);
            }
            setLoading(false);
        })();
    }, [documentId, services]);
    // Open File: fetch open links and cascade desktop -> web -> preview fallback.
    const handleOpenFile = React.useCallback(async () => {
        const links = await services.getDocumentOpenLinks(documentId);
        if (links) {
            if (links.desktopUrl) {
                window.location.href = links.desktopUrl;
                return;
            }
            if (links.webUrl) {
                window.open(links.webUrl, '_blank', 'noopener,noreferrer');
                return;
            }
        }
        if (previewUrl) {
            window.open(previewUrl, '_blank', 'noopener,noreferrer');
        }
    }, [documentId, previewUrl, services]);
    // Open Record (new tab)
    const handleOpenRecord = React.useCallback(() => {
        services.navigateToEntity({
            action: 'openRecord',
            entityName: 'sprk_document',
            entityId: documentId,
            openInNewWindow: true,
        });
    }, [documentId, services]);
    // Copy Link
    const handleCopyLink = React.useCallback(async () => {
        await services.copyDocumentLink(documentId);
    }, [documentId, services]);
    // Toggle workspace flag
    const handleToggleWorkspace = React.useCallback(async () => {
        const newFlag = !inWorkspace;
        setInWorkspace(newFlag); // optimistic
        const success = await services.setWorkspaceFlag(documentId, newFlag);
        if (success) {
            onWorkspaceFlagChanged?.(newFlag);
        }
        else {
            setInWorkspace(!newFlag); // revert
        }
    }, [documentId, inWorkspace, onWorkspaceFlagChanged, services]);
    return (React.createElement(Dialog, { open: open, onOpenChange: (_, data) => {
            if (!data.open)
                onClose();
        } },
        React.createElement(DialogSurface, { className: styles.surface },
            React.createElement("div", { className: styles.titleBar },
                React.createElement(DialogTitle, { action: null, className: styles.titleText }, documentName || 'Document Preview'),
                React.createElement(Tooltip, { content: "Close", relationship: "label" },
                    React.createElement(Button, { appearance: "subtle", icon: React.createElement(Dismiss24Regular, null), "aria-label": "Close", onClick: onClose }))),
            React.createElement(Toolbar, { className: styles.toolbar, size: "small" },
                React.createElement(Tooltip, { content: "Open file", relationship: "label" },
                    React.createElement(ToolbarButton, { icon: React.createElement(Open24Regular, null), onClick: handleOpenFile }, "Open File")),
                React.createElement(Tooltip, { content: "Open record", relationship: "label" },
                    React.createElement(ToolbarButton, { icon: React.createElement(OpenRegular, null), onClick: handleOpenRecord }, "Open Record")),
                React.createElement(ToolbarDivider, null),
                React.createElement(Tooltip, { content: "Copy link to clipboard", relationship: "label" },
                    React.createElement(ToolbarButton, { icon: React.createElement(LinkRegular, null), onClick: handleCopyLink }, "Copy Link")),
                React.createElement(Tooltip, { content: inWorkspace ? 'Remove from workspace' : 'Add to workspace', relationship: "label" },
                    React.createElement(ToolbarButton, { icon: inWorkspace ? React.createElement(StarFilled, null) : React.createElement(StarRegular, null), onClick: handleToggleWorkspace }, inWorkspace ? 'Remove from Workspace' : 'Add to Workspace'))),
            React.createElement(DialogBody, { className: styles.body },
                React.createElement(DialogContent, { className: styles.body },
                    loading && (React.createElement("div", { className: styles.centerContent },
                        React.createElement(Spinner, { size: "large", label: "Loading preview...", labelPosition: "below" }))),
                    error && !loading && (React.createElement("div", { className: styles.centerContent },
                        React.createElement(Text, { size: 400, weight: "semibold" }, "Preview not available"),
                        React.createElement(Text, { size: 200, style: { color: tokens.colorNeutralForeground3 } }, "Unable to load the document preview. The file may be unsupported or temporarily unavailable."),
                        React.createElement(Button, { appearance: "primary", onClick: handleRetry }, "Retry"))),
                    previewUrl && !loading && !error && (React.createElement("iframe", { src: previewUrl, title: `Preview: ${documentName}`, className: styles.iframe, sandbox: "allow-scripts allow-same-origin allow-forms allow-popups" })))))));
};
FilePreviewDialog.displayName = 'FilePreviewDialog';
//# sourceMappingURL=FilePreviewDialog.js.map