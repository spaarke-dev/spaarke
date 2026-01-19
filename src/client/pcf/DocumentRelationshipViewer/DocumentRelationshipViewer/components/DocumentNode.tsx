/**
 * DocumentNode - Custom React Flow node for document visualization
 *
 * Renders document info with Fluent UI v9 styling:
 * - Source node: filled appearance (brand colors)
 * - Related node: outline appearance with similarity badge
 *
 * Follows:
 * - ADR-021: Fluent UI v9 exclusively, design tokens for all colors
 * - ADR-022: React 16 compatible APIs
 */

import * as React from "react";
import { Handle, Position, NodeProps } from "react-flow-renderer";
import {
    makeStyles,
    tokens,
    Card,
    CardHeader,
    Badge,
    Caption1,
    Body1Strong,
    mergeClasses,
    Link,
} from "@fluentui/react-components";
import {
    Document20Regular,
    DocumentPdf20Regular,
    Image20Regular,
    DocumentText20Regular,
    Folder20Regular,
    Table20Regular,
    SlideText20Regular,
    Mail20Regular,
    Code20Regular,
    FolderZip20Regular,
    Video20Regular,
    DocumentQuestionMark20Regular,
    // Parent hub node icons
    Briefcase24Regular,
    Building24Regular,
    Receipt24Regular,
    MailInbox24Regular,
    // Action icons
    Open16Regular,
} from "@fluentui/react-icons";
import type { DocumentNodeData } from "../types/graph";
import { isParentHubNode, type NodeType } from "../types/api";

/**
 * Get file type icon based on extension
 */
const getFileTypeIcon = (fileType: string): React.ReactElement => {
    const type = fileType.toLowerCase();
    switch (type) {
        // Document types
        case "pdf":
            return <DocumentPdf20Regular />;
        case "docx":
        case "doc":
        case "txt":
        case "rtf":
            return <DocumentText20Regular />;

        // Spreadsheet types
        case "xlsx":
        case "xls":
        case "csv":
            return <Table20Regular />;

        // Presentation types
        case "pptx":
        case "ppt":
            return <SlideText20Regular />;

        // Email types
        case "msg":
        case "eml":
            return <Mail20Regular />;

        // Image types
        case "jpg":
        case "jpeg":
        case "png":
        case "gif":
        case "svg":
        case "bmp":
        case "tiff":
            return <Image20Regular />;

        // Code/web types
        case "html":
        case "htm":
        case "xml":
        case "json":
            return <Code20Regular />;

        // Archive types
        case "zip":
        case "rar":
        case "7z":
        case "tar":
        case "gz":
            return <FolderZip20Regular />;

        // Video types
        case "mp4":
        case "avi":
        case "mov":
        case "wmv":
        case "mkv":
            return <Video20Regular />;

        // Folder
        case "folder":
            return <Folder20Regular />;

        // Unknown/default
        case "file":
        case "unknown":
            return <DocumentQuestionMark20Regular />;

        default:
            return <Document20Regular />;
    }
};

/**
 * Get icon for parent hub nodes based on node type
 */
const getParentHubIcon = (nodeType: NodeType): React.ReactElement => {
    switch (nodeType) {
        case "matter":
            return <Briefcase24Regular />;
        case "project":
            return <Building24Regular />;
        case "invoice":
            return <Receipt24Regular />;
        case "email":
            return <MailInbox24Regular />;
        default:
            return <Document20Regular />;
    }
};

/**
 * Get display label for parent hub node type
 */
const getParentHubLabel = (nodeType: NodeType): string => {
    switch (nodeType) {
        case "matter":
            return "Matter";
        case "project":
            return "Project";
        case "invoice":
            return "Invoice";
        case "email":
            return "Email";
        default:
            return "Parent";
    }
};

/**
 * Styles using Fluent UI v9 design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
    nodeContainer: {
        minWidth: "100px",
        maxWidth: "130px",
    },
    sourceCard: {
        backgroundColor: tokens.colorBrandBackground,
        border: `2px solid ${tokens.colorBrandStroke1}`,
        boxShadow: tokens.shadow8,
        "& *": {
            color: tokens.colorNeutralForegroundOnBrand,
        },
    },
    relatedCard: {
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        boxShadow: tokens.shadow4,
    },
    // Orphan file styles - dashed border, muted appearance
    orphanCard: {
        backgroundColor: tokens.colorNeutralBackground2,
        border: `2px dashed ${tokens.colorNeutralStroke2}`,
        boxShadow: tokens.shadow2,
        opacity: 0.9,
    },
    // Parent hub node styles - larger, accent colored
    parentHubCard: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
        boxShadow: tokens.shadow8,
        minWidth: "120px",
        maxWidth: "150px",
    },
    parentHubIcon: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "28px",
        height: "28px",
        borderRadius: "50%",
        backgroundColor: tokens.colorPaletteGreenBackground3,
        color: tokens.colorPaletteGreenForeground1,
    },
    compactParentHubIcon: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
        color: tokens.colorPaletteGreenForeground1,
        width: "48px",
        height: "48px",
    },
    // Compact mode styles - icon only
    compactContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        cursor: "pointer",
    },
    compactIcon: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "40px",
        height: "40px",
        borderRadius: "50%",
        boxShadow: tokens.shadow4,
    },
    compactSourceIcon: {
        backgroundColor: tokens.colorBrandBackground,
        border: `2px solid ${tokens.colorBrandStroke1}`,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    compactRelatedIcon: {
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        color: tokens.colorNeutralForeground1,
    },
    compactOrphanIcon: {
        backgroundColor: tokens.colorNeutralBackground2,
        border: `2px dashed ${tokens.colorNeutralStroke2}`,
        color: tokens.colorNeutralForeground3,
        opacity: 0.9,
    },
    cardHeader: {
        paddingBottom: tokens.spacingVerticalXS,
    },
    icon: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        width: "20px",
        height: "20px",
        borderRadius: tokens.borderRadiusSmall,
        backgroundColor: tokens.colorNeutralBackground3,
    },
    sourceIcon: {
        backgroundColor: tokens.colorBrandBackgroundPressed,
    },
    headerContent: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        overflow: "hidden",
    },
    documentName: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        maxWidth: "80px",
        fontSize: tokens.fontSizeBase100,
    },
    caption: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        color: tokens.colorNeutralForeground3,
    },
    sourceCaption: {
        color: tokens.colorNeutralForegroundOnBrand,
        opacity: 0.8,
    },
    footer: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        paddingTop: tokens.spacingVerticalXS,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    sourceFooter: {
        borderTop: `1px solid ${tokens.colorBrandStroke2}`,
    },
    similarityBadge: {
        marginLeft: "auto",
    },
    orphanBadge: {
        fontSize: tokens.fontSizeBase100,
    },
    openLink: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXXS,
        fontSize: tokens.fontSizeBase100,
        textDecoration: "none",
        color: tokens.colorBrandForegroundLink,
        cursor: "pointer",
        "&:hover": {
            textDecoration: "underline",
        },
    },
    sourceOpenLink: {
        color: tokens.colorNeutralForegroundOnBrand,
        opacity: 0.9,
    },
    footerRow: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        width: "100%",
    },
});

/**
 * DocumentNode component for React Flow
 */
export const DocumentNode: React.FC<NodeProps<DocumentNodeData>> = ({
    data,
    selected,
}) => {
    const styles = useStyles();
    const isSource = data.isSource ?? false;
    const isOrphanFile = data.isOrphanFile ?? false;
    const similarity = data.similarity ?? 0;
    const fileType = data.fileType ?? "unknown";
    const compactMode = data.compactMode ?? false;
    const nodeType = data.nodeType ?? (isSource ? "source" : isOrphanFile ? "orphan" : "related");
    const isParentHub = isParentHubNode(nodeType);
    const relationshipLabel = data.relationshipLabel;
    const recordUrl = data.recordUrl;

    // Handle click on "Open" link to navigate to Dataverse record
    const handleOpenRecord = (e: React.MouseEvent) => {
        e.stopPropagation(); // Prevent node selection
        if (recordUrl) {
            window.open(recordUrl, "_blank", "noopener,noreferrer");
        }
    };

    // Compact mode: icon-only display for fieldBound mode
    if (compactMode) {
        return (
            <>
                <Handle
                    type="target"
                    position={Position.Top}
                    style={{
                        width: "6px",
                        height: "6px",
                        background: isParentHub ? tokens.colorPaletteGreenBorder2 : tokens.colorBrandBackground,
                        border: `1px solid ${tokens.colorNeutralBackground1}`,
                    }}
                />
                <div
                    className={mergeClasses(
                        styles.compactContainer,
                        styles.compactIcon,
                        isParentHub
                            ? styles.compactParentHubIcon
                            : isSource
                                ? styles.compactSourceIcon
                                : isOrphanFile
                                    ? styles.compactOrphanIcon
                                    : styles.compactRelatedIcon
                    )}
                    title={isParentHub
                        ? `${getParentHubLabel(nodeType)}: ${data.name}`
                        : `${data.name}${relationshipLabel ? ` • ${relationshipLabel}` : ""}${isOrphanFile ? " (File only)" : ""}${similarity > 0 ? ` (${Math.round(similarity * 100)}%)` : ""}`
                    }
                >
                    {isParentHub ? getParentHubIcon(nodeType) : getFileTypeIcon(fileType)}
                </div>
                <Handle
                    type="source"
                    position={Position.Bottom}
                    style={{
                        width: "6px",
                        height: "6px",
                        background: isParentHub ? tokens.colorPaletteGreenBorder2 : tokens.colorBrandBackground,
                        border: `1px solid ${tokens.colorNeutralBackground1}`,
                    }}
                />
            </>
        );
    }

    // Parent hub nodes: special rendering with centered icon and name
    // Handle on RIGHT side - documents connect TO parent hubs from the left
    if (isParentHub) {
        return (
            <>
                <Card
                    className={mergeClasses(styles.nodeContainer, styles.parentHubCard)}
                    selected={selected}
                    size="small"
                >
                    <CardHeader
                        className={styles.cardHeader}
                        image={
                            <div className={styles.parentHubIcon}>
                                {getParentHubIcon(nodeType)}
                            </div>
                        }
                        header={
                            <div className={styles.headerContent}>
                                <Body1Strong className={styles.documentName}>
                                    {data.name}
                                </Body1Strong>
                                <Caption1 className={styles.caption}>
                                    {getParentHubLabel(nodeType)}
                                </Caption1>
                            </div>
                        }
                    />
                    <div className={styles.footer}>
                        <Badge appearance="tint" color="success" size="small">
                            {getParentHubLabel(nodeType)}
                        </Badge>
                    </div>
                </Card>
                {/* Target handle on RIGHT - parent receives connections from documents */}
                <Handle
                    type="target"
                    position={Position.Right}
                    style={{
                        width: "8px",
                        height: "8px",
                        background: tokens.colorPaletteGreenBorder2,
                        border: `1px solid ${tokens.colorNeutralBackground1}`,
                    }}
                />
            </>
        );
    }

    // Full mode: card display with details
    // Source handle on LEFT - documents connect TO parent hubs on the right
    return (
        <>
            {/* Source handle on LEFT - outgoing edge to parent hub */}
            <Handle
                type="source"
                position={Position.Left}
                style={{
                    width: "6px",
                    height: "6px",
                    background: tokens.colorBrandBackground,
                    border: `1px solid ${tokens.colorNeutralBackground1}`,
                }}
            />

            <Card
                className={mergeClasses(
                    styles.nodeContainer,
                    isSource
                        ? styles.sourceCard
                        : isOrphanFile
                            ? styles.orphanCard
                            : styles.relatedCard
                )}
                selected={selected}
                size="small"
            >
                <CardHeader
                    className={styles.cardHeader}
                    image={
                        <div
                            className={mergeClasses(
                                styles.icon,
                                isSource && styles.sourceIcon
                            )}
                        >
                            {getFileTypeIcon(fileType)}
                        </div>
                    }
                    header={
                        <div className={styles.headerContent}>
                            <Body1Strong className={styles.documentName}>
                                {data.name}
                            </Body1Strong>
                            {/* Show "Source" label for source node only - relationship type is in footer for related nodes */}
                            {isSource && (
                                <Caption1
                                    className={mergeClasses(
                                        styles.caption,
                                        styles.sourceCaption
                                    )}
                                >
                                    Source
                                </Caption1>
                            )}
                        </div>
                    }
                />

                {/* Footer for related nodes: relationship type and Open link */}
                {!isSource && (
                    <div className={styles.footer}>
                        <div className={styles.footerRow}>
                            {/* Relationship type badge */}
                            {relationshipLabel ? (
                                <Badge
                                    appearance="outline"
                                    color="informative"
                                    size="small"
                                >
                                    {relationshipLabel}
                                </Badge>
                            ) : isOrphanFile ? (
                                <Badge
                                    className={styles.orphanBadge}
                                    appearance="outline"
                                    color="warning"
                                    size="small"
                                >
                                    File only
                                </Badge>
                            ) : (
                                <span /> // Empty spacer
                            )}
                            {/* Open record link */}
                            {recordUrl && (
                                <Link
                                    className={styles.openLink}
                                    onClick={handleOpenRecord}
                                    title="Open in Dataverse"
                                >
                                    <Open16Regular />
                                    Open
                                </Link>
                            )}
                        </div>
                    </div>
                )}

                {/* Footer for source node: Source badge and Open link */}
                {isSource && (
                    <div className={mergeClasses(styles.footer, styles.sourceFooter)}>
                        <div className={styles.footerRow}>
                            <Badge appearance="filled" color="brand" size="small">
                                Source
                            </Badge>
                            {recordUrl && (
                                <Link
                                    className={mergeClasses(styles.openLink, styles.sourceOpenLink)}
                                    onClick={handleOpenRecord}
                                    title="Open in Dataverse"
                                >
                                    <Open16Regular />
                                    Open
                                </Link>
                            )}
                        </div>
                    </div>
                )}
            </Card>

            {/* Target handle on RIGHT - receives semantic relationship connections */}
            <Handle
                type="target"
                position={Position.Right}
                style={{
                    width: "6px",
                    height: "6px",
                    background: tokens.colorBrandBackground,
                    border: `1px solid ${tokens.colorNeutralBackground1}`,
                }}
            />
        </>
    );
};

/**
 * Format file size to human-readable string
 */
function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default DocumentNode;
