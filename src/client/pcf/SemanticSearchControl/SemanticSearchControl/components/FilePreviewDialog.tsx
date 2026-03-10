/**
 * FilePreviewDialog — Full-screen modal for document preview.
 *
 * Mirrors the LegalWorkspace FilePreviewDialog but uses callbacks
 * instead of importing service modules directly, so it works within
 * the PCF control's service layer.
 */

import * as React from "react";
import {
    Dialog,
    DialogSurface,
    DialogBody,
    DialogTitle,
    DialogContent,
    Button,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
    Tooltip,
    Spinner,
    Text,
    makeStyles,
    shorthands,
    tokens,
} from "@fluentui/react-components";
import {
    Dismiss24Regular,
    Open24Regular,
    OpenRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IFilePreviewDialogProps {
    open: boolean;
    documentName: string;
    onClose: () => void;
    /** Fetch the preview embed URL. Called when the dialog opens. */
    fetchPreviewUrl: () => Promise<string | null>;
    /** Open the file in desktop or web app. */
    onOpenFile: (mode: "desktop" | "web") => void;
    /** Open the Dataverse record in a new tab. */
    onOpenRecord: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    surface: {
        width: "85vw",
        maxWidth: "880px",
        height: "85vh",
        maxHeight: "85vh",
        ...shorthands.padding("0px"),
        display: "flex",
        flexDirection: "column",
        ...shorthands.overflow("hidden"),
        ...shorthands.borderRadius(tokens.borderRadiusXLarge),
    },
    titleBar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalS,
        borderBottomWidth: "1px",
        borderBottomStyle: "solid",
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    titleText: {
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        flex: 1,
        minWidth: 0,
    },
    toolbar: {
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottomWidth: "1px",
        borderBottomStyle: "solid",
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    body: {
        ...shorthands.padding("0px"),
        flex: 1,
        minHeight: 0,
        position: "relative" as const,
    },
    iframe: {
        position: "absolute" as const,
        top: 0,
        left: 0,
        width: "100%",
        height: "100%",
        ...shorthands.borderWidth("0px"),
    },
    centerContent: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        width: "100%",
        height: "100%",
        gap: tokens.spacingVerticalM,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const FilePreviewDialog: React.FC<IFilePreviewDialogProps> = ({
    open,
    documentName,
    onClose,
    fetchPreviewUrl,
    onOpenFile,
    onOpenRecord,
}) => {
    const styles = useStyles();

    const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
    const [loading, setLoading] = React.useState(false);
    const [error, setError] = React.useState(false);

    // Fetch preview URL when dialog opens
    React.useEffect(() => {
        if (!open) {
            setPreviewUrl(null);
            setError(false);
            return;
        }

        let cancelled = false;
        setLoading(true);
        setError(false);

        void (async () => {
            const url = await fetchPreviewUrl();
            if (cancelled) return;
            if (url) {
                setPreviewUrl(url);
            } else {
                setError(true);
            }
            setLoading(false);
        })();

        return () => {
            cancelled = true;
        };
    }, [open, fetchPreviewUrl]);

    const handleRetry = React.useCallback(() => {
        setLoading(true);
        setError(false);
        setPreviewUrl(null);
        void (async () => {
            const url = await fetchPreviewUrl();
            if (url) {
                setPreviewUrl(url);
            } else {
                setError(true);
            }
            setLoading(false);
        })();
    }, [fetchPreviewUrl]);

    const handleOpenFile = React.useCallback(() => {
        onOpenFile("desktop");
    }, [onOpenFile]);

    return (
        <Dialog
            open={open}
            onOpenChange={(_, data) => {
                if (!data.open) onClose();
            }}
        >
            <DialogSurface className={styles.surface}>
                {/* Title bar */}
                <div className={styles.titleBar}>
                    <DialogTitle action={null} className={styles.titleText}>
                        {documentName || "Document Preview"}
                    </DialogTitle>
                    <Tooltip content="Close" relationship="label">
                        <Button
                            appearance="subtle"
                            icon={<Dismiss24Regular />}
                            aria-label="Close"
                            onClick={onClose}
                        />
                    </Tooltip>
                </div>

                {/* Toolbar */}
                <Toolbar className={styles.toolbar} size="small">
                    <Tooltip content="Open file" relationship="label">
                        <ToolbarButton
                            icon={<Open24Regular />}
                            onClick={handleOpenFile}
                        >
                            Open File
                        </ToolbarButton>
                    </Tooltip>
                    <Tooltip content="Open record" relationship="label">
                        <ToolbarButton
                            icon={<OpenRegular />}
                            onClick={onOpenRecord}
                        >
                            Open Record
                        </ToolbarButton>
                    </Tooltip>
                    <ToolbarDivider />
                </Toolbar>

                {/* Preview content */}
                <DialogBody className={styles.body}>
                    <DialogContent className={styles.body}>
                        {loading && (
                            <div className={styles.centerContent}>
                                <Spinner size="large" label="Loading preview..." labelPosition="below" />
                            </div>
                        )}
                        {error && !loading && (
                            <div className={styles.centerContent}>
                                <Text size={400} weight="semibold">
                                    Preview not available
                                </Text>
                                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                                    Unable to load the document preview. The file may be unsupported or temporarily unavailable.
                                </Text>
                                <Button appearance="primary" onClick={handleRetry}>
                                    Retry
                                </Button>
                            </div>
                        )}
                        {previewUrl && !loading && !error && (
                            <iframe
                                src={previewUrl}
                                title={`Preview: ${documentName}`}
                                className={styles.iframe}
                                sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
                            />
                        )}
                    </DialogContent>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

FilePreviewDialog.displayName = "FilePreviewDialog";
