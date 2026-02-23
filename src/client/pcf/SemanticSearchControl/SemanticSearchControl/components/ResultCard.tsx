/**
 * ResultCard component
 *
 * Displays a single search result with document info, similarity score,
 * metadata, highlighted snippet, and action buttons.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useCallback } from "react";
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
    Divider,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    DocumentPdfRegular,
    Document20Regular,
    FolderOpenRegular,
    OpenRegular,
    MoreHorizontalRegular,
    BranchRegular,
    GlobeRegular,
    ArrowDownloadRegular,
} from "@fluentui/react-icons";
import { IResultCardProps, SearchResult } from "../types";
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
        gap: tokens.spacingHorizontalS,
        marginTop: tokens.spacingVerticalXS,
    },
    metaItem: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    metaSeparator: {
        color: tokens.colorNeutralForeground3,
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
    },
    badge: {
        flexShrink: 0,
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
        return "—";
    }
    try {
        const date = new Date(dateString);
        if (isNaN(date.getTime())) {
            return "—";
        }
        return date.toLocaleDateString(undefined, {
            year: "numeric",
            month: "short",
            day: "numeric",
        });
    } catch {
        return "—";
    }
}

/**
 * ResultCard component for displaying a search result.
 *
 * @param props.result - Search result data
 * @param props.onClick - Callback when card is clicked
 * @param props.onOpenFile - Callback to open file
 * @param props.onOpenRecord - Callback to open record (inModal: boolean)
 * @param props.compactMode - Whether in compact mode
 */
export const ResultCard: React.FC<IResultCardProps> = ({
    result,
    onClick,
    onOpenFile,
    onOpenRecord,
    onFindSimilar,
    compactMode,
}) => {
    const styles = useStyles();

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
                            <Text className={styles.metaItem}>
                                {result.documentType}
                            </Text>
                            {result.matterName && (
                                <>
                                    <Text className={styles.metaSeparator}>
                                        |
                                    </Text>
                                    <Text className={styles.metaItem}>
                                        {result.matterName}
                                    </Text>
                                </>
                            )}
                            <Text className={styles.metaSeparator}>|</Text>
                            <Text className={styles.metaItem}>
                                {formatDate(result.createdAt)}
                            </Text>
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
            </div>
        </Card>
    );
};

export default ResultCard;
