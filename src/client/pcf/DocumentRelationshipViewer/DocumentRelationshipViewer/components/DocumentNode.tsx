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
} from "@fluentui/react-icons";
import type { DocumentNodeData } from "../types/graph";

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
 * Get similarity badge appearance based on score
 * - 90-100%: green (success)
 * - 75-89%: blue (informative)
 * - 65-74%: yellow (warning)
 * - <65%: gray (subtle)
 */
const getSimilarityAppearance = (
    similarity: number
): "filled" | "outline" | "tint" => {
    if (similarity >= 0.9) return "filled";
    if (similarity >= 0.75) return "tint";
    return "outline";
};

const getSimilarityColor = (
    similarity: number
): "success" | "informative" | "warning" | "subtle" => {
    if (similarity >= 0.9) return "success";
    if (similarity >= 0.75) return "informative";
    if (similarity >= 0.65) return "warning";
    return "subtle";
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
                        background: tokens.colorBrandBackground,
                        border: `1px solid ${tokens.colorNeutralBackground1}`,
                    }}
                />
                <div
                    className={mergeClasses(
                        styles.compactContainer,
                        styles.compactIcon,
                        isSource
                            ? styles.compactSourceIcon
                            : isOrphanFile
                                ? styles.compactOrphanIcon
                                : styles.compactRelatedIcon
                    )}
                    title={`${data.name}${isOrphanFile ? " (File only)" : ""}${similarity > 0 ? ` (${Math.round(similarity * 100)}%)` : ""}`}
                >
                    {getFileTypeIcon(fileType)}
                </div>
                <Handle
                    type="source"
                    position={Position.Bottom}
                    style={{
                        width: "6px",
                        height: "6px",
                        background: tokens.colorBrandBackground,
                        border: `1px solid ${tokens.colorNeutralBackground1}`,
                    }}
                />
            </>
        );
    }

    // Full mode: card display with details
    return (
        <>
            {/* Handle positioned on left side for radial layout */}
            <Handle
                type="target"
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
                            <Caption1
                                className={mergeClasses(
                                    styles.caption,
                                    isSource && styles.sourceCaption
                                )}
                            >
                                {fileType.toUpperCase()}
                                {data.size && ` • ${formatFileSize(data.size)}`}
                            </Caption1>
                        </div>
                    }
                />

                {/* Footer with similarity badge (only for related nodes) or orphan indicator */}
                {!isSource && (similarity > 0 || isOrphanFile) && (
                    <div className={styles.footer}>
                        {isOrphanFile ? (
                            <>
                                <Badge
                                    className={styles.orphanBadge}
                                    appearance="outline"
                                    color="warning"
                                    size="small"
                                >
                                    File only
                                </Badge>
                                {similarity > 0 && (
                                    <Badge
                                        className={styles.similarityBadge}
                                        appearance={getSimilarityAppearance(similarity)}
                                        color={getSimilarityColor(similarity)}
                                        size="small"
                                    >
                                        {Math.round(similarity * 100)}%
                                    </Badge>
                                )}
                            </>
                        ) : (
                            <>
                                <Caption1 className={styles.caption}>
                                    Similarity
                                </Caption1>
                                <Badge
                                    className={styles.similarityBadge}
                                    appearance={getSimilarityAppearance(similarity)}
                                    color={getSimilarityColor(similarity)}
                                    size="small"
                                >
                                    {Math.round(similarity * 100)}%
                                </Badge>
                            </>
                        )}
                    </div>
                )}

                {/* Source node label */}
                {isSource && (
                    <div className={styles.footer}>
                        <Badge appearance="filled" color="brand" size="small">
                            Source Document
                        </Badge>
                    </div>
                )}
            </Card>

            {/* Output handle on right side for radial layout */}
            <Handle
                type="source"
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
