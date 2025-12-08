/**
 * AI Summary Carousel
 *
 * Displays AI summaries for multiple documents with navigation,
 * concurrent streaming management, and per-document status tracking.
 * Uses AiSummaryPanel for individual document display.
 *
 * ADR Compliance:
 * - ADR-006: PCF control pattern
 * - ADR-012: Fluent UI v9 components from shared library
 *
 * @version 1.0.0.0
 */

import * as React from 'react';
import {
    Button,
    Text,
    Badge,
    makeStyles,
    mergeClasses,
    tokens
} from '@fluentui/react-components';
import {
    ChevronLeftRegular,
    ChevronRightRegular,
    DocumentMultipleRegular
} from '@fluentui/react-icons';
import { AiSummaryPanel, SummaryStatus } from './AiSummaryPanel';

/**
 * Document summary state for carousel
 */
export interface DocumentSummaryState {
    /** Document identifier */
    documentId: string;

    /** File name */
    fileName: string;

    /** Summary text (may be partial during streaming) */
    summary?: string;

    /** Current status */
    status: SummaryStatus;

    /** Error message (when status is 'error') */
    error?: string;
}

/**
 * Component Props
 */
export interface AiSummaryCarouselProps {
    /** Array of document summary states */
    documents: DocumentSummaryState[];

    /** Callback for retry action on a specific document */
    onRetry?: (documentId: string) => void;

    /** Maximum concurrent streams (default: 3) */
    maxConcurrent?: number;

    /** Optional className for styling */
    className?: string;
}

/**
 * Styles
 */
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingVerticalM,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`
    },
    header: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalM
    },
    headerLeft: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS
    },
    headerIcon: {
        color: tokens.colorNeutralForeground3
    },
    headerTitle: {
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    navigation: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS
    },
    navButton: {
        minWidth: '32px'
    },
    pageIndicator: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground2,
        minWidth: '60px',
        textAlign: 'center'
    },
    panelContainer: {
        position: 'relative'
    },
    statusSummary: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalS,
        borderTop: `1px solid ${tokens.colorNeutralStroke2}`
    },
    statusItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS
    },
    statusCount: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2
    },
    // Badge variants for status counts
    badgeComplete: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
        color: tokens.colorPaletteGreenForeground1
    },
    badgeStreaming: {
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1
    },
    badgePending: {
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground2
    },
    badgeError: {
        backgroundColor: tokens.colorPaletteRedBackground1,
        color: tokens.colorPaletteRedForeground1
    }
});

/**
 * Calculate aggregate status counts
 */
const getStatusCounts = (documents: DocumentSummaryState[]) => {
    return documents.reduce(
        (acc, doc) => {
            switch (doc.status) {
                case 'complete':
                    acc.complete++;
                    break;
                case 'streaming':
                    acc.streaming++;
                    break;
                case 'pending':
                    acc.pending++;
                    break;
                case 'error':
                    acc.error++;
                    break;
                case 'skipped':
                case 'not-supported':
                    acc.skipped++;
                    break;
            }
            return acc;
        },
        { complete: 0, streaming: 0, pending: 0, error: 0, skipped: 0 }
    );
};

/**
 * AI Summary Carousel Component
 *
 * Displays multiple document summaries with navigation and
 * aggregate status tracking. Uses AiSummaryPanel for individual display.
 */
export const AiSummaryCarousel: React.FC<AiSummaryCarouselProps> = ({
    documents,
    onRetry,
    maxConcurrent = 3,
    className
}) => {
    const styles = useStyles();
    const [currentIndex, setCurrentIndex] = React.useState(0);
    const containerRef = React.useRef<HTMLDivElement>(null);

    // Ensure currentIndex stays within bounds
    React.useEffect(() => {
        if (currentIndex >= documents.length && documents.length > 0) {
            setCurrentIndex(documents.length - 1);
        }
    }, [documents.length, currentIndex]);

    // Navigation handlers
    const handlePrevious = React.useCallback(() => {
        setCurrentIndex((prev) => Math.max(0, prev - 1));
    }, []);

    const handleNext = React.useCallback(() => {
        setCurrentIndex((prev) => Math.min(documents.length - 1, prev + 1));
    }, [documents.length]);

    // Keyboard navigation
    const handleKeyDown = React.useCallback(
        (event: React.KeyboardEvent) => {
            if (event.key === 'ArrowLeft') {
                event.preventDefault();
                handlePrevious();
            } else if (event.key === 'ArrowRight') {
                event.preventDefault();
                handleNext();
            }
        },
        [handlePrevious, handleNext]
    );

    // Handle retry for current document
    const handleRetry = React.useCallback(() => {
        const currentDoc = documents[currentIndex];
        if (currentDoc && onRetry) {
            onRetry(currentDoc.documentId);
        }
    }, [documents, currentIndex, onRetry]);

    // Get status counts for aggregate display
    const statusCounts = React.useMemo(
        () => getStatusCounts(documents),
        [documents]
    );

    // Count currently streaming (for concurrent limit info)
    const streamingCount = statusCounts.streaming;

    // Don't render if no documents
    if (documents.length === 0) {
        return null;
    }

    // For single document, render AiSummaryPanel directly
    if (documents.length === 1) {
        const doc = documents[0];
        return (
            <AiSummaryPanel
                documentId={doc.documentId}
                fileName={doc.fileName}
                summary={doc.summary}
                status={doc.status}
                error={doc.error}
                onRetry={onRetry ? handleRetry : undefined}
                className={className}
            />
        );
    }

    const currentDoc = documents[currentIndex];
    const canGoPrevious = currentIndex > 0;
    const canGoNext = currentIndex < documents.length - 1;

    return (
        <div
            ref={containerRef}
            className={mergeClasses(styles.container, className)}
            onKeyDown={handleKeyDown}
            tabIndex={0}
            role="region"
            aria-label={`AI summaries for ${documents.length} documents`}
            aria-roledescription="carousel"
        >
            {/* Header with title and navigation */}
            <div className={styles.header}>
                <div className={styles.headerLeft}>
                    <DocumentMultipleRegular className={styles.headerIcon} />
                    <Text className={styles.headerTitle}>AI Summaries</Text>
                </div>

                {/* Navigation controls */}
                <div
                    className={styles.navigation}
                    role="group"
                    aria-label="Carousel navigation"
                >
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<ChevronLeftRegular />}
                        onClick={handlePrevious}
                        disabled={!canGoPrevious}
                        className={styles.navButton}
                        aria-label="Previous document"
                    />
                    <Text
                        className={styles.pageIndicator}
                        aria-live="polite"
                        aria-atomic="true"
                    >
                        {currentIndex + 1} of {documents.length}
                    </Text>
                    <Button
                        appearance="subtle"
                        size="small"
                        icon={<ChevronRightRegular />}
                        onClick={handleNext}
                        disabled={!canGoNext}
                        className={styles.navButton}
                        aria-label="Next document"
                    />
                </div>
            </div>

            {/* Current document panel */}
            <div
                className={styles.panelContainer}
                role="group"
                aria-roledescription="slide"
                aria-label={`Document ${currentIndex + 1} of ${documents.length}: ${currentDoc.fileName}`}
            >
                <AiSummaryPanel
                    documentId={currentDoc.documentId}
                    fileName={currentDoc.fileName}
                    summary={currentDoc.summary}
                    status={currentDoc.status}
                    error={currentDoc.error}
                    onRetry={onRetry ? handleRetry : undefined}
                />
            </div>

            {/* Aggregate status summary */}
            <div
                className={styles.statusSummary}
                role="status"
                aria-label="Summary status overview"
            >
                {statusCounts.complete > 0 && (
                    <div className={styles.statusItem}>
                        <Badge
                            appearance="filled"
                            className={styles.badgeComplete}
                            size="small"
                        >
                            {statusCounts.complete}
                        </Badge>
                        <Text className={styles.statusCount}>complete</Text>
                    </div>
                )}
                {statusCounts.streaming > 0 && (
                    <div className={styles.statusItem}>
                        <Badge
                            appearance="filled"
                            className={styles.badgeStreaming}
                            size="small"
                        >
                            {statusCounts.streaming}
                        </Badge>
                        <Text className={styles.statusCount}>
                            generating{streamingCount > maxConcurrent ? ` (${maxConcurrent} max)` : ''}
                        </Text>
                    </div>
                )}
                {statusCounts.pending > 0 && (
                    <div className={styles.statusItem}>
                        <Badge
                            appearance="filled"
                            className={styles.badgePending}
                            size="small"
                        >
                            {statusCounts.pending}
                        </Badge>
                        <Text className={styles.statusCount}>pending</Text>
                    </div>
                )}
                {statusCounts.error > 0 && (
                    <div className={styles.statusItem}>
                        <Badge
                            appearance="filled"
                            className={styles.badgeError}
                            size="small"
                        >
                            {statusCounts.error}
                        </Badge>
                        <Text className={styles.statusCount}>failed</Text>
                    </div>
                )}
            </div>
        </div>
    );
};

export default AiSummaryCarousel;
