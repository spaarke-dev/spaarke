/**
 * DocumentNode — Custom @xyflow/react v12 node component
 *
 * Migrated from PCF react-flow-renderer v10 to @xyflow/react v12.
 * Key change: NodeProps<Node<DocumentNodeData>> instead of NodeProps<DocumentNodeData>
 */

import React from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
    makeStyles, tokens, Card, CardHeader, Badge, Caption1,
    Body1Strong, mergeClasses, Link, Tooltip,
} from "@fluentui/react-components";
import {
    Document20Regular, DocumentPdf20Regular, Image20Regular, DocumentText20Regular,
    Folder20Regular, Table20Regular, SlideText20Regular, Mail20Regular, Code20Regular,
    FolderZip20Regular, Video20Regular, DocumentQuestionMark20Regular,
    Briefcase24Regular, Building24Regular, Receipt24Regular, MailInbox24Regular,
    Open16Regular, Info16Regular,
} from "@fluentui/react-icons";
import type { DocumentNode as TDocumentNode, DocumentNodeData } from "../types/graph";
import { isParentHubNode, type NodeType } from "../types/api";

const getFileTypeIcon = (fileType: string): React.ReactElement => {
    const type = fileType.toLowerCase();
    switch (type) {
        case "pdf": return <DocumentPdf20Regular />;
        case "docx": case "doc": case "txt": case "rtf": return <DocumentText20Regular />;
        case "xlsx": case "xls": case "csv": return <Table20Regular />;
        case "pptx": case "ppt": return <SlideText20Regular />;
        case "msg": case "eml": return <Mail20Regular />;
        case "jpg": case "jpeg": case "png": case "gif": case "svg": case "bmp": case "tiff": return <Image20Regular />;
        case "html": case "htm": case "xml": case "json": return <Code20Regular />;
        case "zip": case "rar": case "7z": case "tar": case "gz": return <FolderZip20Regular />;
        case "mp4": case "avi": case "mov": case "wmv": case "mkv": return <Video20Regular />;
        case "folder": return <Folder20Regular />;
        case "file": case "unknown": return <DocumentQuestionMark20Regular />;
        default: return <Document20Regular />;
    }
};

const getParentHubIcon = (nodeType: NodeType): React.ReactElement => {
    switch (nodeType) {
        case "matter": return <Briefcase24Regular />;
        case "project": return <Building24Regular />;
        case "invoice": return <Receipt24Regular />;
        case "email": return <MailInbox24Regular />;
        default: return <Document20Regular />;
    }
};

const getParentHubLabel = (nodeType: NodeType): string => {
    switch (nodeType) {
        case "matter": return "Matter";
        case "project": return "Project";
        case "invoice": return "Invoice";
        case "email": return "Email";
        default: return "Parent";
    }
};

const getRelationshipBadgeColor = (type: string): "brand" | "success" | "warning" | "informative" => {
    switch (type) {
        case "semantic": return "brand";
        case "same_matter": case "same_project": return "success";
        case "same_email": case "same_thread": return "warning";
        default: return "informative";
    }
};

const useStyles = makeStyles({
    nodeContainer: { minWidth: "120px", maxWidth: "160px" },
    sourceCard: {
        backgroundColor: tokens.colorBrandBackground,
        border: `2px solid ${tokens.colorBrandStroke1}`,
        boxShadow: tokens.shadow8,
        "& *": { color: tokens.colorNeutralForegroundOnBrand },
    },
    relatedCard: {
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        boxShadow: tokens.shadow4,
    },
    orphanCard: {
        backgroundColor: tokens.colorNeutralBackground2,
        border: `2px dashed ${tokens.colorNeutralStroke2}`,
        boxShadow: tokens.shadow2,
        opacity: 0.9,
    },
    parentHubCard: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
        boxShadow: tokens.shadow8,
        minWidth: "120px",
        maxWidth: "150px",
    },
    parentHubIcon: {
        display: "flex", alignItems: "center", justifyContent: "center",
        width: "28px", height: "28px", borderRadius: "50%",
        backgroundColor: tokens.colorPaletteGreenBackground3,
        color: tokens.colorPaletteGreenForeground1,
    },
    compactParentHubIcon: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
        border: `2px solid ${tokens.colorPaletteGreenBorder2}`,
        color: tokens.colorPaletteGreenForeground1,
        width: "48px", height: "48px",
    },
    compactContainer: { display: "flex", alignItems: "center", justifyContent: "center", cursor: "pointer" },
    compactIcon: {
        display: "flex", alignItems: "center", justifyContent: "center",
        width: "40px", height: "40px", borderRadius: "50%", boxShadow: tokens.shadow4,
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
    cardHeader: { paddingBottom: tokens.spacingVerticalXS },
    icon: {
        display: "flex", alignItems: "center", justifyContent: "center",
        width: "20px", height: "20px", borderRadius: tokens.borderRadiusSmall,
        backgroundColor: tokens.colorNeutralBackground3,
    },
    sourceIcon: { backgroundColor: tokens.colorBrandBackgroundPressed },
    headerContent: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXXS, overflow: "hidden" },
    documentName: {
        overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
        maxWidth: "100px", fontSize: tokens.fontSizeBase100,
    },
    caption: { display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS, color: tokens.colorNeutralForeground3 },
    sourceCaption: { color: tokens.colorNeutralForegroundOnBrand, opacity: 0.8 },
    footer: {
        display: "flex", justifyContent: "space-between", alignItems: "center",
        paddingTop: tokens.spacingVerticalXS, borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    sourceFooter: { borderTop: `1px solid ${tokens.colorBrandStroke2}` },
    orphanBadge: { fontSize: tokens.fontSizeBase100 },
    openLink: {
        display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXXS,
        fontSize: tokens.fontSizeBase100, textDecoration: "none",
        color: tokens.colorBrandForegroundLink, cursor: "pointer", marginLeft: "auto",
        "&:hover": { textDecoration: "underline" },
    },
    sourceOpenLink: { color: tokens.colorNeutralForegroundOnBrand, opacity: 0.9 },
    footerRow: { display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS, width: "100%" },
    infoIcon: {
        display: "flex", alignItems: "center", justifyContent: "center",
        cursor: "pointer", padding: "2px", borderRadius: tokens.borderRadiusSmall,
        color: tokens.colorNeutralForeground3,
        "&:hover": { backgroundColor: tokens.colorNeutralBackground3, color: tokens.colorNeutralForeground1 },
    },
    sourceInfoIcon: {
        color: tokens.colorNeutralForegroundOnBrand, opacity: 0.7,
        "&:hover": { backgroundColor: tokens.colorBrandBackgroundPressed, opacity: 1 },
    },
    tooltipContent: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXXS, maxWidth: "250px" },
    tooltipTitle: { fontWeight: 600, fontSize: tokens.fontSizeBase200, wordBreak: "break-word" },
    tooltipRow: { display: "flex", fontSize: tokens.fontSizeBase100, gap: tokens.spacingHorizontalXS },
    tooltipLabel: { color: tokens.colorNeutralForeground3, minWidth: "60px" },
    tooltipValue: { color: tokens.colorNeutralForeground1, wordBreak: "break-word" },
});

const formatDate = (isoDate: string | undefined): string | null => {
    if (!isoDate) return null;
    try {
        return new Date(isoDate).toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
    } catch { return null; }
};

const buildTooltipContent = (data: DocumentNodeData, styles: ReturnType<typeof useStyles>): React.ReactElement => {
    const similarityPercent = Math.round((data.similarity ?? 0) * 100);
    return (
        <div className={styles.tooltipContent}>
            <div className={styles.tooltipTitle}>{data.name}</div>
            {data.documentType && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Doc Type:</span><span className={styles.tooltipValue}>{data.documentType}</span></div>}
            {data.fileType && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Format:</span><span className={styles.tooltipValue}>{data.fileType.toUpperCase()}</span></div>}
            {data.size != null && data.size > 0 && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Size:</span><span className={styles.tooltipValue}>{formatFileSize(data.size)}</span></div>}
            {!data.isSource && (data.similarity ?? 0) > 0 && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Similarity:</span><span className={styles.tooltipValue}>{similarityPercent}%</span></div>}
            {data.relationshipTypes && data.relationshipTypes.length > 0 && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Relations:</span><span className={styles.tooltipValue}>{data.relationshipTypes.map((r) => r.label).join(", ")}</span></div>}
            {data.parentEntityName && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Parent:</span><span className={styles.tooltipValue}>{data.parentEntityName}</span></div>}
            {formatDate(data.createdOn) && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Created:</span><span className={styles.tooltipValue}>{formatDate(data.createdOn)}</span></div>}
            {formatDate(data.modifiedOn) && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Modified:</span><span className={styles.tooltipValue}>{formatDate(data.modifiedOn)}</span></div>}
            {(data.documentId ?? data.speFileId) && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>ID:</span><span className={styles.tooltipValue} style={{ fontSize: "9px" }}>{(data.documentId ?? data.speFileId)?.substring(0, 8)}...</span></div>}
            {data.sharedKeywords && data.sharedKeywords.length > 0 && <div className={styles.tooltipRow}><span className={styles.tooltipLabel}>Keywords:</span><span className={styles.tooltipValue}>{data.sharedKeywords.slice(0, 3).join(", ")}{data.sharedKeywords.length > 3 && "..."}</span></div>}
        </div>
    );
};

function formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * Custom node component for @xyflow/react v12
 * NodeProps<DocumentNode> gives data: DocumentNodeData
 */
export const DocumentNode: React.FC<NodeProps<TDocumentNode>> = ({ data, selected }) => {
    const styles = useStyles();
    const isSource = data.isSource ?? false;
    const isOrphanFile = data.isOrphanFile ?? false;
    const fileType = data.fileType ?? "unknown";
    const compactMode = data.compactMode ?? false;
    const nodeType = data.nodeType ?? (isSource ? "source" : isOrphanFile ? "orphan" : "related");
    const isParentHub = isParentHubNode(nodeType);
    const recordUrl = data.recordUrl;
    const tooltipContent = buildTooltipContent(data, styles);

    const handleOpenRecord = (e: React.MouseEvent) => {
        e.stopPropagation();
        if (recordUrl) window.open(recordUrl, "_blank", "noopener,noreferrer");
    };

    if (compactMode) {
        return (
            <>
                <Handle type="target" position={Position.Top} style={{ width: "6px", height: "6px", background: isParentHub ? tokens.colorPaletteGreenBorder2 : tokens.colorBrandBackground, border: `1px solid ${tokens.colorNeutralBackground1}` }} />
                <div className={mergeClasses(styles.compactContainer, styles.compactIcon, isParentHub ? styles.compactParentHubIcon : isSource ? styles.compactSourceIcon : isOrphanFile ? styles.compactOrphanIcon : styles.compactRelatedIcon)}>
                    {isParentHub ? getParentHubIcon(nodeType) : getFileTypeIcon(fileType)}
                </div>
                <Handle type="source" position={Position.Bottom} style={{ width: "6px", height: "6px", background: isParentHub ? tokens.colorPaletteGreenBorder2 : tokens.colorBrandBackground, border: `1px solid ${tokens.colorNeutralBackground1}` }} />
            </>
        );
    }

    if (isParentHub) {
        return (
            <>
                <Card className={mergeClasses(styles.nodeContainer, styles.parentHubCard)} selected={selected} size="small">
                    <CardHeader className={styles.cardHeader} image={<div className={styles.parentHubIcon}>{getParentHubIcon(nodeType)}</div>} header={<div className={styles.headerContent}><Body1Strong className={styles.documentName}>{data.name}</Body1Strong><Caption1 className={styles.caption}>{getParentHubLabel(nodeType)}</Caption1></div>} />
                    <div className={styles.footer}>
                        <div className={styles.footerRow}>
                            <Badge appearance="tint" color="success" size="small">{getParentHubLabel(nodeType)}</Badge>
                            <Tooltip content={tooltipContent} relationship="description" positioning="above" withArrow><div className={styles.infoIcon}><Info16Regular /></div></Tooltip>
                        </div>
                    </div>
                </Card>
                <Handle type="target" position={Position.Right} style={{ width: "8px", height: "8px", background: tokens.colorPaletteGreenBorder2, border: `1px solid ${tokens.colorNeutralBackground1}` }} />
            </>
        );
    }

    return (
        <>
            <Handle type="source" position={Position.Left} style={{ width: "6px", height: "6px", background: tokens.colorBrandBackground, border: `1px solid ${tokens.colorNeutralBackground1}` }} />
            <Card className={mergeClasses(styles.nodeContainer, isSource ? styles.sourceCard : isOrphanFile ? styles.orphanCard : styles.relatedCard)} selected={selected} size="small">
                <CardHeader className={styles.cardHeader} image={<div className={mergeClasses(styles.icon, isSource && styles.sourceIcon)}>{getFileTypeIcon(fileType)}</div>} header={<div className={styles.headerContent}><Body1Strong className={styles.documentName}>{data.name}</Body1Strong>{isSource && <Caption1 className={mergeClasses(styles.caption, styles.sourceCaption)}>Source</Caption1>}</div>} />
                {!isSource && (
                    <div className={styles.footer}>
                        <div className={styles.footerRow}>
                            {data.relationshipTypes && data.relationshipTypes.length > 0 ? (
                                data.relationshipTypes.slice(0, 2).map((rel) => (
                                    <Badge key={rel.type} appearance="outline" color={getRelationshipBadgeColor(rel.type)} size="small">{rel.label}</Badge>
                                ))
                            ) : isOrphanFile ? <Badge className={styles.orphanBadge} appearance="outline" color="warning" size="small">File only</Badge> : <span />}
                            <Tooltip content={tooltipContent} relationship="description" positioning="above" withArrow><div className={styles.infoIcon}><Info16Regular /></div></Tooltip>
                            {recordUrl && <Link className={styles.openLink} onClick={handleOpenRecord} title="Open in Dataverse"><Open16Regular /></Link>}
                        </div>
                    </div>
                )}
                {isSource && (
                    <div className={mergeClasses(styles.footer, styles.sourceFooter)}>
                        <div className={styles.footerRow}>
                            <Badge appearance="filled" color="brand" size="small">Source</Badge>
                            <Tooltip content={tooltipContent} relationship="description" positioning="above" withArrow><div className={mergeClasses(styles.infoIcon, styles.sourceInfoIcon)}><Info16Regular /></div></Tooltip>
                            {recordUrl && <Link className={mergeClasses(styles.openLink, styles.sourceOpenLink)} onClick={handleOpenRecord} title="Open in Dataverse"><Open16Regular /></Link>}
                        </div>
                    </div>
                )}
            </Card>
            <Handle type="target" position={Position.Right} style={{ width: "6px", height: "6px", background: tokens.colorBrandBackground, border: `1px solid ${tokens.colorNeutralBackground1}` }} />
        </>
    );
};

export default DocumentNode;
