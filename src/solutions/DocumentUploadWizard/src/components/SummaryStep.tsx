/**
 * SummaryStep.tsx
 * Step 2 of the Document Upload Wizard — Document Profile streaming.
 *
 * Displays per-document AI profile cards using the shared useAiSummary hook.
 * Each card shows streaming results as they arrive via SSE:
 *   - TL;DR bullet points
 *   - Document type badge
 *   - Keywords as tag pills
 *   - Extracted entities grouped by type
 *   - Collapsible full summary
 *
 * Error handling:
 *   - Failed profiles show inline error badges with retry option
 *   - Individual document failures do not crash the wizard
 *   - Partial SSE results are displayed gracefully as fields arrive
 *
 * @see ADR-006  - Code Pages for standalone dialogs (not PCF)
 * @see ADR-013  - AI calls through BFF endpoints only
 * @see ADR-021  - Fluent UI v9 design system (makeStyles + semantic tokens)
 */

import { useEffect, useRef, useState, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Text,
    Badge,
    Spinner,
    Button,
} from "@fluentui/react-components";
import {
    ChevronDownRegular,
    ChevronUpRegular,
    ArrowClockwiseRegular,
    DocumentRegular,
    ErrorCircleRegular,
    CheckmarkCircleRegular,
} from "@fluentui/react-icons";

import { useAiSummary } from "@spaarke/ui-components/hooks/useAiSummary";
import type {
    SummaryDocument,
    DocumentSummaryState,
    ExtractedEntities,
} from "@spaarke/ui-components/hooks/useAiSummary";

import type { IUploadedFile } from "../types";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ISummaryStepProps {
    /** Files that were uploaded in Step 1. */
    files: IUploadedFile[];
    /** BFF API base URL (e.g., "https://spe-api-dev-67e2xz.azurewebsites.net/api"). */
    apiBaseUrl: string;
    /** Token acquisition function for BFF API auth. */
    getToken: () => Promise<string>;
    /**
     * Mapping from local file ID to uploaded document metadata.
     * Required to wire useAiSummary which needs documentId, driveId, itemId.
     * Key: IUploadedFile.id, Value: { documentId, driveId, itemId }
     */
    uploadedDocumentMap: Map<string, UploadedDocumentInfo>;
    /** Whether all documents have completed profiling (exposed to parent for canAdvance). */
    onProcessingChange?: (isProcessing: boolean) => void;
    /** When true, shows a simple file list preview (no AI profiling). Used for the Review step. */
    reviewOnly?: boolean;
    /** Display name of the parent entity (for review summary). */
    parentEntityName?: string;
}

/** Metadata returned after upload, needed for AI analysis. */
export interface UploadedDocumentInfo {
    /** Dataverse document record GUID. */
    documentId: string;
    /** SharePoint Embedded drive ID. */
    driveId: string;
    /** SharePoint Embedded item ID. */
    itemId: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        flex: 1,
        minHeight: 0,
        overflowY: "auto",
    },
    header: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        flexShrink: 0,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    progressBar: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
    },
    cardList: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        flex: 1,
        minHeight: 0,
    },

    // -- Profile Card --
    card: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground2,
        border: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    cardHeader: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    cardIcon: {
        color: tokens.colorNeutralForeground3,
        fontSize: "20px",
        flexShrink: 0,
    },
    cardFileName: {
        flex: 1,
        minWidth: 0,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        color: tokens.colorNeutralForeground1,
    },
    cardStatus: {
        flexShrink: 0,
    },
    cardBody: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
    },
    cardSpinner: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground3,
    },

    // -- Sections --
    section: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    sectionLabel: {
        color: tokens.colorNeutralForeground3,
        fontWeight: tokens.fontWeightSemibold as unknown as string,
    },

    // -- TL;DR --
    tldrList: {
        margin: 0,
        paddingLeft: tokens.spacingHorizontalL,
        color: tokens.colorNeutralForeground1,
    },
    tldrItem: {
        paddingBottom: tokens.spacingVerticalXXS,
    },

    // -- Keywords --
    keywordContainer: {
        display: "flex",
        flexWrap: "wrap",
        gap: tokens.spacingHorizontalXS,
    },

    // -- Entities --
    entityGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        paddingLeft: tokens.spacingHorizontalS,
    },
    entityLabel: {
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic",
    },
    entityValues: {
        color: tokens.colorNeutralForeground1,
    },

    // -- Summary (collapsible) --
    summaryToggle: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        cursor: "pointer",
        backgroundColor: "transparent",
        border: "none",
        padding: 0,
        color: tokens.colorBrandForeground1,
        fontFamily: "inherit",
        fontSize: "inherit",
    },
    summaryText: {
        color: tokens.colorNeutralForeground2,
        whiteSpace: "pre-wrap",
        lineHeight: tokens.lineHeightBase300,
    },
    streamingText: {
        color: tokens.colorNeutralForeground2,
        whiteSpace: "pre-wrap",
        lineHeight: tokens.lineHeightBase300,
        opacity: 0.8,
    },

    // -- Error --
    errorContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        color: tokens.colorPaletteRedForeground1,
    },
    errorText: {
        flex: 1,
        color: tokens.colorPaletteRedForeground1,
    },
});

// ---------------------------------------------------------------------------
// DocumentProfileCard — per-document profile display
// ---------------------------------------------------------------------------

interface DocumentProfileCardProps {
    doc: DocumentSummaryState;
    onRetry: (documentId: string) => void;
}

function DocumentProfileCard({ doc, onRetry }: DocumentProfileCardProps): JSX.Element {
    const styles = useStyles();
    const [summaryExpanded, setSummaryExpanded] = useState(false);

    const toggleSummary = useCallback(() => {
        setSummaryExpanded((prev) => !prev);
    }, []);

    const handleRetry = useCallback(() => {
        onRetry(doc.documentId);
    }, [onRetry, doc.documentId]);

    // Status badge
    const statusBadge = useMemo(() => {
        switch (doc.status) {
            case "pending":
                return <Badge appearance="outline" color="informative" size="small">Pending</Badge>;
            case "streaming":
                return <Badge appearance="tint" color="brand" size="small">Analyzing...</Badge>;
            case "complete":
                return (
                    <Badge
                        appearance="filled"
                        color="success"
                        size="small"
                        icon={<CheckmarkCircleRegular />}
                    >
                        Complete
                    </Badge>
                );
            case "error":
                return (
                    <Badge
                        appearance="filled"
                        color="danger"
                        size="small"
                        icon={<ErrorCircleRegular />}
                    >
                        Error
                    </Badge>
                );
            case "skipped":
            case "not-supported":
                return <Badge appearance="outline" color="warning" size="small">Skipped</Badge>;
            default:
                return null;
        }
    }, [doc.status]);

    // Parse keywords into array
    const keywordList = useMemo(() => {
        if (!doc.keywords) return [];
        return doc.keywords.split(",").map((k) => k.trim()).filter(Boolean);
    }, [doc.keywords]);

    // Collect non-empty entity groups
    const entityGroups = useMemo(() => {
        if (!doc.entities) return [];
        const groups: { label: string; values: string[] }[] = [];
        const e: ExtractedEntities = doc.entities;
        if (e.organizations?.length > 0) groups.push({ label: "Organizations", values: e.organizations });
        if (e.people?.length > 0) groups.push({ label: "People", values: e.people });
        if (e.amounts?.length > 0) groups.push({ label: "Amounts", values: e.amounts });
        if (e.dates?.length > 0) groups.push({ label: "Dates", values: e.dates });
        if (e.references?.length > 0) groups.push({ label: "References", values: e.references });
        return groups;
    }, [doc.entities]);

    return (
        <div className={styles.card}>
            {/* Card header: file name + status */}
            <div className={styles.cardHeader}>
                <DocumentRegular className={styles.cardIcon} aria-hidden="true" />
                <Text size={300} weight="semibold" className={styles.cardFileName} title={doc.fileName}>
                    {doc.fileName}
                </Text>
                <span className={styles.cardStatus}>{statusBadge}</span>
            </div>

            {/* Card body */}
            <div className={styles.cardBody}>
                {/* Streaming / pending spinner */}
                {(doc.status === "streaming" || doc.status === "pending") && (
                    <div className={styles.cardSpinner}>
                        <Spinner size="tiny" />
                        <Text size={200}>
                            {doc.status === "pending"
                                ? "Waiting to start analysis..."
                                : "Analyzing document..."}
                        </Text>
                    </div>
                )}

                {/* Streaming raw text (shown while streaming before structured result) */}
                {doc.status === "streaming" && doc.summary && !doc.tldr && (
                    <Text size={200} className={styles.streamingText}>
                        {doc.summary}
                    </Text>
                )}

                {/* Document type badge */}
                {doc.documentType && (
                    <div className={styles.section}>
                        <Text size={200} className={styles.sectionLabel}>
                            Document Type
                        </Text>
                        <div>
                            <Badge appearance="tint" color="brand" size="medium">
                                {doc.documentType}
                            </Badge>
                        </div>
                    </div>
                )}

                {/* TL;DR bullet points */}
                {doc.tldr && doc.tldr.length > 0 && (
                    <div className={styles.section}>
                        <Text size={200} className={styles.sectionLabel}>
                            TL;DR
                        </Text>
                        <ul className={styles.tldrList}>
                            {doc.tldr.map((item, idx) => (
                                <li key={idx} className={styles.tldrItem}>
                                    <Text size={200}>{item}</Text>
                                </li>
                            ))}
                        </ul>
                    </div>
                )}

                {/* Keywords tag pills */}
                {keywordList.length > 0 && (
                    <div className={styles.section}>
                        <Text size={200} className={styles.sectionLabel}>
                            Keywords
                        </Text>
                        <div className={styles.keywordContainer}>
                            {keywordList.map((keyword, idx) => (
                                <Badge key={idx} appearance="outline" size="small">
                                    {keyword}
                                </Badge>
                            ))}
                        </div>
                    </div>
                )}

                {/* Entities */}
                {entityGroups.length > 0 && (
                    <div className={styles.section}>
                        <Text size={200} className={styles.sectionLabel}>
                            Entities
                        </Text>
                        <div className={styles.entityGroup}>
                            {entityGroups.map((group) => (
                                <div key={group.label}>
                                    <Text size={200} className={styles.entityLabel}>
                                        {group.label}:
                                    </Text>{" "}
                                    <Text size={200} className={styles.entityValues}>
                                        {group.values.join(", ")}
                                    </Text>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Collapsible full summary */}
                {doc.summary && doc.status === "complete" && (
                    <div className={styles.section}>
                        <button
                            type="button"
                            className={styles.summaryToggle}
                            onClick={toggleSummary}
                            aria-expanded={summaryExpanded}
                        >
                            {summaryExpanded ? (
                                <ChevronUpRegular aria-hidden="true" />
                            ) : (
                                <ChevronDownRegular aria-hidden="true" />
                            )}
                            <Text size={200} weight="semibold">
                                {summaryExpanded ? "Hide Summary" : "Show Summary"}
                            </Text>
                        </button>
                        {summaryExpanded && (
                            <Text size={200} className={styles.summaryText}>
                                {doc.summary}
                            </Text>
                        )}
                    </div>
                )}

                {/* Error state */}
                {doc.status === "error" && (
                    <div className={styles.errorContainer}>
                        <ErrorCircleRegular aria-hidden="true" />
                        <Text size={200} className={styles.errorText}>
                            {doc.error || "Analysis failed"}
                        </Text>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ArrowClockwiseRegular />}
                            onClick={handleRetry}
                        >
                            Retry
                        </Button>
                    </div>
                )}

                {/* Partial storage warning */}
                {doc.partialStorage && doc.storageMessage && (
                    <Text size={200} style={{ color: tokens.colorPaletteYellowForeground1 }}>
                        {doc.storageMessage}
                    </Text>
                )}
            </div>
        </div>
    );
}

// ---------------------------------------------------------------------------
// SummaryStep (exported)
// ---------------------------------------------------------------------------

export function SummaryStep({
    files,
    apiBaseUrl,
    getToken,
    uploadedDocumentMap,
    onProcessingChange,
    reviewOnly = false,
    parentEntityName,
}: ISummaryStepProps): JSX.Element {
    const styles = useStyles();

    if (reviewOnly) {
        return (
            <ReviewOnlyStep
                files={files}
                parentEntityName={parentEntityName}
            />
        );
    }

    return (
        <ProfileStep
            files={files}
            apiBaseUrl={apiBaseUrl}
            getToken={getToken}
            uploadedDocumentMap={uploadedDocumentMap}
            onProcessingChange={onProcessingChange}
        />
    );
}

// ---------------------------------------------------------------------------
// ReviewOnlyStep — simple file list for the Review step
// ---------------------------------------------------------------------------

function ReviewOnlyStep({
    files,
    parentEntityName,
}: {
    files: IUploadedFile[];
    parentEntityName?: string;
}): JSX.Element {
    const styles = useStyles();
    const totalSize = files.reduce((sum, f) => sum + (f.sizeBytes ?? 0), 0);

    const formatSize = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    return (
        <div className={styles.root}>
            <div className={styles.header}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Review
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    {parentEntityName
                        ? `${files.length} file${files.length !== 1 ? "s" : ""} will be uploaded to ${parentEntityName}.`
                        : `${files.length} file${files.length !== 1 ? "s" : ""} ready for upload.`}
                </Text>
            </div>
            <div className={styles.cardList}>
                {files.map((file) => (
                    <div key={file.id} className={styles.card}>
                        <div className={styles.cardHeader}>
                            <DocumentRegular className={styles.cardIcon} aria-hidden="true" />
                            <Text size={300} weight="semibold" className={styles.cardFileName} title={file.name}>
                                {file.name}
                            </Text>
                            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                                {formatSize(file.sizeBytes ?? 0)}
                            </Text>
                        </div>
                    </div>
                ))}
            </div>
            {files.length > 1 && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    Total: {formatSize(totalSize)}
                </Text>
            )}
        </div>
    );
}

// ---------------------------------------------------------------------------
// ProfileStep — AI profiling with streaming (Processing step)
// ---------------------------------------------------------------------------

function ProfileStep({
    files,
    apiBaseUrl,
    getToken,
    uploadedDocumentMap,
    onProcessingChange,
}: Omit<ISummaryStepProps, "reviewOnly" | "parentEntityName">): JSX.Element {
    const styles = useStyles();
    const hasInitialized = useRef(false);

    // Wire up the shared AI summary hook
    const {
        documents,
        isProcessing,
        completedCount,
        errorCount,
        addDocuments,
        retry,
    } = useAiSummary({
        apiBaseUrl,
        getToken,
        maxConcurrent: 3,
        autoStart: true,
    });

    // Notify parent of processing state changes
    useEffect(() => {
        onProcessingChange?.(isProcessing);
    }, [isProcessing, onProcessingChange]);

    // Add documents for profiling on mount (once)
    useEffect(() => {
        if (hasInitialized.current) return;
        if (uploadedDocumentMap.size === 0) return;

        const summaryDocs: SummaryDocument[] = [];
        for (const file of files) {
            const info = uploadedDocumentMap.get(file.id);
            if (info) {
                summaryDocs.push({
                    documentId: info.documentId,
                    driveId: info.driveId,
                    itemId: info.itemId,
                    fileName: file.name,
                });
            }
        }

        if (summaryDocs.length > 0) {
            hasInitialized.current = true;
            addDocuments(summaryDocs);
        }
    }, [files, uploadedDocumentMap, addDocuments]);

    // Progress text
    const totalCount = documents.length;
    const progressText = totalCount > 0
        ? `${completedCount} of ${totalCount} analyzed${errorCount > 0 ? ` (${errorCount} failed)` : ""}`
        : "Preparing document analysis...";

    return (
        <div className={styles.root}>
            {/* Header */}
            <div className={styles.header}>
                <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                    Document Profile
                </Text>
                <Text size={200} className={styles.stepSubtitle}>
                    AI is analyzing your uploaded documents. Results appear as they stream in.
                </Text>
            </div>

            {/* Progress indicator */}
            <div className={styles.progressBar}>
                {isProcessing && <Spinner size="tiny" />}
                {!isProcessing && totalCount > 0 && completedCount === totalCount && (
                    <CheckmarkCircleRegular style={{ color: tokens.colorPaletteGreenForeground1 }} />
                )}
                <Text size={200}>{progressText}</Text>
            </div>

            {/* Profile cards */}
            <div className={styles.cardList}>
                {documents.map((doc) => (
                    <DocumentProfileCard
                        key={doc.documentId}
                        doc={doc}
                        onRetry={retry}
                    />
                ))}
            </div>

            {/* Empty state when no docs available yet */}
            {totalCount === 0 && (
                <div className={styles.cardSpinner}>
                    <Spinner size="tiny" />
                    <Text size={200}>Preparing document analysis...</Text>
                </div>
            )}
        </div>
    );
}
