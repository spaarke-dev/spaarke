/**
 * ResultCard component
 *
 * Displays a single search result with document info, similarity score,
 * metadata, highlighted snippet, and action buttons.
 * v1.0.33: createdOn/createdBy metadata, Preview popover, Summary popover.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useCallback, useState } from "react";
import {
    makeStyles,
    tokens,
    Card,
    CardHeader,
    Text,
    Button,
    Menu,
    MenuTrigger,
    MenuPopover,
    MenuList,
    MenuItem,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Tooltip,
    Spinner,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    DocumentPdfRegular,
    Document20Regular,
    FolderOpenRegular,
    OpenRegular,
    BranchRegular,
    GlobeRegular,
    ArrowDownloadRegular,
    Eye20Regular,
    Sparkle20Regular,
} from "@fluentui/react-icons";
import { IResultCardProps } from "../types";
import { SimilarityBadge } from "./SimilarityBadge";
import { HighlightedSnippet } from "./HighlightedSnippet";

const useStyles = makeStyles({
    card: {
        cursor: "pointer",
        "&:hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    cardCompact: {
        padding: tokens.spacingVerticalS,
    },
    header: {
        display: "flex",
        alignItems: "flex-start",
        gap: tokens.spacingHorizontalM,
    },
    icon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
    },
    titleGroup: {
        flex: 1,
        minWidth: 0,
    },
    title: {
        fontWeight: tokens.fontWeightSemibold,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    metadata: {
        display: "flex",
        flexWrap: "wrap",
        gap: tokens.spacingHorizontalXS,
        marginTop: tokens.spacingVerticalXS,
        alignItems: "center",
    },
    metaLabel: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
    metaValue: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    metaSeparator: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
    snippet: {
        marginTop: tokens.spacingVerticalS,
        paddingLeft: `calc(24px + ${tokens.spacingHorizontalM})`,
    },
    actions: {
        display: "flex",
        gap: tokens.spacingHorizontalS,
        marginTop: tokens.spacingVerticalS,
        paddingLeft: `calc(24px + ${tokens.spacingHorizontalM})`,
        flexWrap: "wrap",
    },
    badge: {
        flexShrink: 0,
    },
    previewPopover: {
        width: "880px",
        height: "85vh",
        padding: "0px",
        overflow: "hidden",
    },
    previewFrame: {
        width: "100%",
        height: "100%",
        border: "none",
        display: "block",
    },
    summaryPopover: {
        width: "480px",
        height: "620px",
        overflowY: "auto",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    summaryHeader: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    },
});

/**
 * Get file type icon based on extension.
 */
function getFileIcon(fileType: string): React.ReactElement {
    const type = fileType.toLowerCase();
    switch (type) {
        case "pdf":
            return <DocumentPdfRegular />;
        case "doc":
        case "docx":
            return <DocumentRegular />;
        default:
            return <Document20Regular />;
    }
}

/**
 * Format date for display.
 */
function formatDate(dateString: string | null): string {
    if (!dateString) {
        return "";
    }
    try {
        const date = new Date(dateString);
        if (isNaN(date.getTime())) {
            return "";
        }
        return date.toLocaleDateString(undefined, {
            year: "numeric",
            month: "short",
            day: "numeric",
        });
    } catch {
        return "";
    }
}

/**
 * ResultCard component for displaying a search result.
 *
 * @param props.result - Search result data
 * @param props.onClick - Callback when card is clicked
 * @param props.onOpenFile - Callback to open file
 * @param props.onOpenRecord - Callback to open record (inModal: boolean)
 * @param props.onFindSimilar - Callback to find similar documents
 * @param props.onPreview - Callback to get preview URL (lazy-loaded)
 * @param props.compactMode - Whether in compact mode
 */
export const ResultCard: React.FC<IResultCardProps> = ({
    result,
    onClick,
    onOpenFile,
    onOpenRecord,
    onFindSimilar,
    onPreview,
    onSummary,
    compactMode,
}) => {
    const styles = useStyles();

    // Preview popover state — URL is lazy-loaded via onPreview callback
    const [previewUrl, setPreviewUrl] = useState<string | null>(null);
    const [previewLoading, setPreviewLoading] = useState(false);
    const [previewError, setPreviewError] = useState(false);

    // Summary popover state — lazy-loaded from Dataverse via onSummary callback
    const [summaryData, setSummaryData] = useState<{ summary: string | null; tldr: string | null } | null>(null);
    const [summaryLoading, setSummaryLoading] = useState(false);
    const [summaryError, setSummaryError] = useState(false);

    // Handle card click
    const handleCardClick = useCallback(
        (ev: React.MouseEvent) => {
            // Don't trigger if clicking on action buttons
            if ((ev.target as HTMLElement).closest("button")) {
                return;
            }
            onClick();
        },
        [onClick]
    );

    // Handle Open File in Web
    const handleOpenFileWeb = useCallback(
        (ev: React.MouseEvent) => {
            ev.stopPropagation();
            onOpenFile("web");
        },
        [onOpenFile]
    );

    // Handle Open File in Desktop Application
    const handleOpenFileDesktop = useCallback(
        (ev: React.MouseEvent) => {
            ev.stopPropagation();
            onOpenFile("desktop");
        },
        [onOpenFile]
    );

    // Handle Open Record in Modal
    const handleOpenRecordModal = useCallback(() => {
        onOpenRecord(true);
    }, [onOpenRecord]);

    // Handle Open Record in New Tab
    const handleOpenRecordNewTab = useCallback(() => {
        onOpenRecord(false);
    }, [onOpenRecord]);

    // Handle Find Similar
    const handleFindSimilar = useCallback(
        (ev: React.MouseEvent) => {
            ev.stopPropagation();
            onFindSimilar();
        },
        [onFindSimilar]
    );

    const cardClassName = compactMode
        ? `${styles.card} ${styles.cardCompact}`
        : styles.card;

    const formattedDate = formatDate(result.createdAt);

    return (
        <Card
            className={cardClassName}
            onClick={handleCardClick}
        >
            {/* Header with icon, title, and badge */}
            <CardHeader
                image={getFileIcon(result.fileType)}
                header={
                    <div className={styles.titleGroup}>
                        <Text className={styles.title} block>
                            {result.name}
                        </Text>
                        <div className={styles.metadata}>
                            {formattedDate && (
                                <>
                                    <Text className={styles.metaLabel}>Created On:</Text>
                                    <Text className={styles.metaValue}>{formattedDate}</Text>
                                </>
                            )}
                            {result.createdBy && (
                                <>
                                    {formattedDate && <Text className={styles.metaSeparator}>&middot;</Text>}
                                    <Text className={styles.metaLabel}>createdBy:</Text>
                                    <Text className={styles.metaValue}>{result.createdBy}</Text>
                                </>
                            )}
                        </div>
                    </div>
                }
                action={
                    <div className={styles.badge}>
                        <SimilarityBadge score={result.combinedScore} />
                    </div>
                }
            />

            {/* Highlighted snippet */}
            {!compactMode && (result.highlights?.length ?? 0) > 0 && (
                <div className={styles.snippet}>
                    <HighlightedSnippet
                        text={result.highlights[0]}
                        maxLength={200}
                    />
                </div>
            )}

            {/* Action buttons */}
            <div className={styles.actions}>
                <Menu>
                    <MenuTrigger disableButtonEnhancement>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<FolderOpenRegular />}
                        >
                            Open File
                        </Button>
                    </MenuTrigger>
                    <MenuPopover>
                        <MenuList>
                            <MenuItem icon={<GlobeRegular />} onClick={handleOpenFileWeb}>
                                Open in Web
                            </MenuItem>
                            <MenuItem icon={<ArrowDownloadRegular />} onClick={handleOpenFileDesktop}>
                                Open in Desktop
                            </MenuItem>
                        </MenuList>
                    </MenuPopover>
                </Menu>

                <Menu>
                    <MenuTrigger disableButtonEnhancement>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<OpenRegular />}
                        >
                            Open Record
                        </Button>
                    </MenuTrigger>
                    <MenuPopover>
                        <MenuList>
                            <MenuItem onClick={handleOpenRecordModal}>
                                Open in Dialog
                            </MenuItem>
                            <MenuItem onClick={handleOpenRecordNewTab}>
                                Open in New Tab
                            </MenuItem>
                        </MenuList>
                    </MenuPopover>
                </Menu>

                <Button
                    appearance="subtle"
                    size="small"
                    icon={<BranchRegular />}
                    onClick={handleFindSimilar}
                >
                    Find Similar
                </Button>

                {/* Preview — lazy-loads document URL on popover open */}
                <Popover
                    positioning="after"
                    withArrow
                    onOpenChange={(_ev, data) => {
                        if (data.open && !previewUrl && !previewLoading) {
                            setPreviewLoading(true);
                            setPreviewError(false);
                            void onPreview()
                                .then((url) => {
                                    setPreviewUrl(url);
                                    setPreviewLoading(false);
                                    return url;
                                })
                                .catch(() => {
                                    setPreviewError(true);
                                    setPreviewLoading(false);
                                });
                        }
                    }}
                >
                    <PopoverTrigger disableButtonEnhancement>
                        <Tooltip content="Preview document" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Eye20Regular />}
                                onClick={(ev) => ev.stopPropagation()}
                            >
                                Preview
                            </Button>
                        </Tooltip>
                    </PopoverTrigger>
                    <PopoverSurface className={styles.previewPopover}>
                        {previewLoading && (
                            <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%" }}>
                                <Spinner size="medium" label="Loading preview..." />
                            </div>
                        )}
                        {previewError && (
                            <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", padding: "16px" }}>
                                <Text>Preview not available for this document.</Text>
                            </div>
                        )}
                        {previewUrl && !previewLoading && (
                            <iframe
                                src={previewUrl}
                                title={result.name}
                                className={styles.previewFrame}
                            />
                        )}
                    </PopoverSurface>
                </Popover>

                {/* Summary — lazy-loads from Dataverse via onSummary callback */}
                <Popover
                    positioning="after"
                    withArrow
                    onOpenChange={(_ev, data) => {
                        if (data.open && !summaryData && !summaryLoading) {
                            setSummaryLoading(true);
                            setSummaryError(false);
                            void onSummary()
                                .then((sd) => {
                                    setSummaryData(sd);
                                    setSummaryLoading(false);
                                    return sd;
                                })
                                .catch(() => {
                                    setSummaryError(true);
                                    setSummaryLoading(false);
                                });
                        }
                    }}
                >
                    <PopoverTrigger disableButtonEnhancement>
                        <Tooltip content="View AI summary" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Sparkle20Regular />}
                                onClick={(ev) => ev.stopPropagation()}
                            >
                                Summary
                            </Button>
                        </Tooltip>
                    </PopoverTrigger>
                    <PopoverSurface className={styles.summaryPopover}>
                        <Text className={styles.summaryHeader}>Summary</Text>
                        {summaryLoading && (
                            <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: "16px", flex: 1 }}>
                                <Spinner size="small" label="Loading summary..." />
                            </div>
                        )}
                        {summaryError && (
                            <Text>Summary not available for this document.</Text>
                        )}
                        {summaryData && !summaryLoading && (
                            <>
                                {summaryData.tldr && (
                                    <Text weight="semibold">{summaryData.tldr}</Text>
                                )}
                                {summaryData.summary && (
                                    <Text style={{ whiteSpace: "pre-wrap" }}>{summaryData.summary}</Text>
                                )}
                                {!summaryData.summary && !summaryData.tldr && (
                                    <Text>No summary available for this document.</Text>
                                )}
                            </>
                        )}
                    </PopoverSurface>
                </Popover>
            </div>
        </Card>
    );
};

export default ResultCard;
