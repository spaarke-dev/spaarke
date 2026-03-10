/**
 * ResultCard component
 *
 * Displays a single search result using the 2-row DocumentCard design pattern:
 *   Row 1: File icon (40px circle) | Title + description | Actions (icon-only)
 *   Row 2: Similarity badge + metadata (created date, created by)
 *
 * Action button order (L→R): Preview, Summary, Open File, Find Similar
 * Preview opens the shared FilePreviewDialog (not a popover).
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import { useCallback, useState } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Button,
    Tooltip,
    Popover,
    PopoverTrigger,
    PopoverSurface,
    Spinner,
    shorthands,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    DocumentPdfRegular,
    DocumentTextRegular,
    TableRegular,
    SlideTextRegular,
    ImageRegular,
    MailRegular,
    Eye20Regular,
    Sparkle20Regular,
    DocumentSearchRegular,
    FolderOpenRegular,
} from "@fluentui/react-icons";
import { IResultCardProps } from "../types";
import { FilePreviewDialog } from "./FilePreviewDialog";

// ---------------------------------------------------------------------------
// File icon mapping (mirrors LegalWorkspace fileIconMap.ts)
// ---------------------------------------------------------------------------

type IconComponent = typeof DocumentRegular;

function getFileIcon(fileType: string): IconComponent {
    const ext = fileType?.toLowerCase().trim() ?? "";
    switch (ext) {
        case "pdf":
            return DocumentPdfRegular;
        case "doc":
        case "docx":
        case "rtf":
        case "odt":
        case "txt":
            return DocumentTextRegular;
        case "xls":
        case "xlsx":
        case "csv":
            return TableRegular;
        case "ppt":
        case "pptx":
            return SlideTextRegular;
        case "jpg":
        case "jpeg":
        case "png":
        case "gif":
        case "bmp":
        case "svg":
            return ImageRegular;
        case "msg":
        case "eml":
            return MailRegular;
        default:
            return DocumentRegular;
    }
}

// ---------------------------------------------------------------------------
// Date formatter
// ---------------------------------------------------------------------------

function formatShortDate(dateString: string | null): string {
    if (!dateString) return "";
    try {
        const d = new Date(dateString);
        if (isNaN(d.getTime())) return "";
        return d.toLocaleDateString(undefined, {
            month: "short",
            day: "numeric",
            year: "numeric",
        });
    } catch {
        return "";
    }
}

// ---------------------------------------------------------------------------
// Styles (DocumentCard 2-row pattern)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    card: {
        display: "flex",
        flexDirection: "column",
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalL,
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        boxShadow: tokens.shadow2,
        marginBottom: tokens.spacingVerticalXS,
        borderLeftWidth: "3px",
        borderLeftStyle: "solid",
        borderLeftColor: tokens.colorBrandStroke1,
        cursor: "pointer",
        transitionProperty: "background-color, box-shadow",
        transitionDuration: tokens.durationFaster,
        transitionTimingFunction: tokens.curveEasyEase,
        ":hover": {
            backgroundColor: tokens.colorNeutralBackground1Hover,
            boxShadow: tokens.shadow4,
        },
        ":focus-visible": {
            outlineStyle: "solid",
            outlineWidth: "2px",
            outlineColor: tokens.colorBrandStroke1,
            outlineOffset: "-2px",
        },
    },
    mainRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "flex-start",
        gap: tokens.spacingHorizontalL,
    },
    typeIconCircle: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        flexShrink: 0,
        width: "40px",
        height: "40px",
        ...shorthands.borderRadius("50%"),
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
        marginTop: "2px",
    },
    contentColumn: {
        flex: "1 1 0",
        minWidth: 0,
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    primaryRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flexWrap: "nowrap",
        minWidth: 0,
    },
    title: {
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground1,
        fontWeight: tokens.fontWeightSemibold,
        flexShrink: 1,
        minWidth: 0,
    },
    description: {
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground3,
        flexShrink: 1,
        minWidth: 0,
    },
    secondaryRow: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flexWrap: "wrap",
    },
    metaText: {
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground3,
    },
    actionsColumn: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: tokens.spacingHorizontalXXS,
        flexShrink: 0,
        marginLeft: tokens.spacingHorizontalL,
    },
    // Summary popover
    summaryPopover: {
        width: "480px",
        maxHeight: "400px",
        overflowY: "auto",
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    summaryHeader: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottomWidth: "1px",
        borderBottomStyle: "solid",
        borderBottomColor: tokens.colorNeutralStroke2,
    },
    centered: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        ...shorthands.padding("16px"),
    },
});

// ---------------------------------------------------------------------------
// Similarity badge inline (small pill for secondary row)
// ---------------------------------------------------------------------------

const ScoreBadge: React.FC<{ score: number }> = ({ score }) => {
    const pct = Math.round(score * 100);
    return (
        <span
            role="img"
            aria-label={`Relevance: ${pct}%`}
            style={{
                display: "inline-flex",
                alignItems: "center",
                justifyContent: "center",
                borderRadius: tokens.borderRadiusSmall,
                paddingTop: "1px",
                paddingBottom: "1px",
                paddingLeft: tokens.spacingHorizontalXS,
                paddingRight: tokens.spacingHorizontalXS,
                fontSize: tokens.fontSizeBase100,
                fontWeight: tokens.fontWeightSemibold,
                lineHeight: tokens.lineHeightBase100,
                whiteSpace: "nowrap",
                backgroundColor: tokens.colorBrandBackground2,
                color: tokens.colorBrandForeground1,
                flexShrink: 0,
            }}
        >
            {pct}%
        </span>
    );
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ResultCard component — 2-row DocumentCard design pattern.
 */
export const ResultCard: React.FC<IResultCardProps> = ({
    result,
    onClick,
    onOpenFile,
    onOpenRecord,
    onFindSimilar,
    onPreview,
    onSummary,
    onEmailDocument,
    onCopyLink,
    onToggleWorkspace,
    isInWorkspace,
    compactMode,
}) => {
    const styles = useStyles();

    // FilePreviewDialog state
    const [previewOpen, setPreviewOpen] = useState(false);

    // Summary popover state
    const [summaryData, setSummaryData] = useState<{ summary: string | null; tldr: string | null } | null>(null);
    const [summaryLoading, setSummaryLoading] = useState(false);
    const [summaryError, setSummaryError] = useState(false);

    // Resolve file icon
    const IconComponent = getFileIcon(result.fileType);

    // Card double-click opens record in new tab
    const handleCardDoubleClick = useCallback(() => {
        onOpenRecord(false);
    }, [onOpenRecord]);

    // Card single-click selects the document
    const handleCardClick = useCallback(
        (ev: React.MouseEvent) => {
            if ((ev.target as HTMLElement).closest("button")) return;
            onClick();
        },
        [onClick]
    );

    const handleCardKeyDown = useCallback(
        (e: React.KeyboardEvent) => {
            if (e.key === "Enter") {
                e.preventDefault();
                onOpenRecord(false);
            }
        },
        [onOpenRecord]
    );

    // Stop propagation on action clicks
    const handleActionsClick = useCallback((e: React.MouseEvent) => {
        e.stopPropagation();
    }, []);

    // Preview — opens FilePreviewDialog
    const handlePreviewClick = useCallback(
        (e: React.MouseEvent) => {
            e.stopPropagation();
            setPreviewOpen(true);
        },
        []
    );

    // Open file — prefer desktop app
    const handleOpenFileClick = useCallback(
        (e: React.MouseEvent) => {
            e.stopPropagation();
            onOpenFile("desktop");
        },
        [onOpenFile]
    );

    // Open record in new tab (for FilePreviewDialog toolbar)
    const handleOpenRecord = useCallback(() => {
        onOpenRecord(false);
    }, [onOpenRecord]);

    const formattedDate = formatShortDate(result.createdAt);

    // Build aria label
    const cardAriaLabel = [
        result.name,
        result.documentType,
        formattedDate ? `Created: ${formattedDate}` : "",
    ].filter(Boolean).join(", ");

    return (
        <>
            <div
                className={styles.card}
                role="listitem"
                tabIndex={0}
                aria-label={cardAriaLabel}
                onClick={handleCardClick}
                onDoubleClick={handleCardDoubleClick}
                onKeyDown={handleCardKeyDown}
            >
                <div className={styles.mainRow}>
                    {/* File type icon in 40px circle */}
                    <div
                        className={styles.typeIconCircle}
                        aria-label={result.fileType || "Document"}
                        role="img"
                    >
                        <IconComponent fontSize={20} />
                    </div>

                    {/* Content: 2 rows */}
                    <div className={styles.contentColumn}>
                        {/* Row 1: Title + document type */}
                        <div className={styles.primaryRow}>
                            <Text as="span" size={400} className={styles.title}>
                                {result.name}
                            </Text>
                            {result.documentType && (
                                <Text as="span" size={300} className={styles.description}>
                                    {result.documentType}
                                </Text>
                            )}
                        </div>

                        {/* Row 2: Score badge + metadata */}
                        <div className={styles.secondaryRow}>
                            <ScoreBadge score={result.combinedScore} />
                            {formattedDate && (
                                <Text as="span" size={200} className={styles.metaText}>
                                    Created: {formattedDate}
                                </Text>
                            )}
                            {result.createdBy && (
                                <Text as="span" size={200} className={styles.metaText}>
                                    By: {result.createdBy}
                                </Text>
                            )}
                        </div>
                    </div>

                    {/* Actions: icon-only buttons — Preview, Summary, Open File, Find Similar */}
                    <div className={styles.actionsColumn} role="toolbar" onClick={handleActionsClick}>
                        {/* 1. Preview */}
                        <Tooltip content="Preview" relationship="label">
                            <Button
                                appearance="subtle"
                                size="medium"
                                icon={<Eye20Regular aria-hidden="true" />}
                                aria-label="Preview document"
                                onClick={handlePreviewClick}
                            />
                        </Tooltip>

                        {/* 2. Summary */}
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
                                <Tooltip content="Summary" relationship="label">
                                    <Button
                                        appearance="subtle"
                                        size="medium"
                                        icon={<Sparkle20Regular aria-hidden="true" />}
                                        aria-label="Summary"
                                        onClick={(ev) => ev.stopPropagation()}
                                    />
                                </Tooltip>
                            </PopoverTrigger>
                            <PopoverSurface className={styles.summaryPopover}>
                                <Text className={styles.summaryHeader}>Summary</Text>
                                {summaryLoading && (
                                    <div className={styles.centered}>
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

                        {/* 3. Open File */}
                        <Tooltip content="Open file" relationship="label">
                            <Button
                                appearance="subtle"
                                size="medium"
                                icon={<FolderOpenRegular aria-hidden="true" />}
                                aria-label="Open file"
                                onClick={handleOpenFileClick}
                            />
                        </Tooltip>

                        {/* 4. Find Similar */}
                        <Tooltip content="Find Similar" relationship="label">
                            <Button
                                appearance="subtle"
                                size="medium"
                                icon={<DocumentSearchRegular aria-hidden="true" />}
                                aria-label="Find Similar"
                                onClick={(e: React.MouseEvent) => {
                                    e.stopPropagation();
                                    onFindSimilar();
                                }}
                            />
                        </Tooltip>
                    </div>
                </div>
            </div>

            {/* FilePreviewDialog — same component used in CorporateWorkspace */}
            <FilePreviewDialog
                open={previewOpen}
                documentName={result.name}
                onClose={() => setPreviewOpen(false)}
                fetchPreviewUrl={onPreview}
                onOpenFile={onOpenFile}
                onOpenRecord={handleOpenRecord}
                onEmailDocument={onEmailDocument}
                onCopyLink={onCopyLink}
                onToggleWorkspace={onToggleWorkspace}
                isInWorkspace={isInWorkspace}
            />
        </>
    );
};

export default ResultCard;
