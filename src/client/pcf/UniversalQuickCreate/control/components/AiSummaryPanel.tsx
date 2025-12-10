/**
 * AI Summary Panel
 *
 * Displays an AI-generated summary for a single document with status indicator,
 * streaming text display, and error handling.
 *
 * ADR Compliance:
 * - ADR-006: PCF control pattern
 * - ADR-012: Fluent UI v9 components from shared library
 *
 * @version 1.0.0.0
 */

import * as React from 'react';
import {
    Badge,
    Button,
    Card,
    CardHeader,
    Spinner,
    Text,
    makeStyles,
    mergeClasses,
    tokens
} from '@fluentui/react-components';
import {
    DocumentRegular,
    ArrowClockwiseRegular,
    CheckmarkCircleRegular,
    DismissCircleRegular,
    InfoRegular
} from '@fluentui/react-icons';

/**
 * Summary status values
 */
export type SummaryStatus =
    | 'pending'
    | 'streaming'
    | 'complete'
    | 'error'
    | 'not-supported'
    | 'skipped';

/**
 * Component Props
 */
export interface AiSummaryPanelProps {
    /** Document identifier */
    documentId: string;

    /** File name for display */
    fileName: string;

    /** Summary text (may be partial during streaming) */
    summary?: string;

    /** Current status */
    status: SummaryStatus;

    /** Error message (when status is 'error') */
    error?: string;

    /** Callback for retry action */
    onRetry?: () => void;

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
    fileInfo: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        minWidth: 0,
        flex: 1
    },
    fileName: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
    },
    fileIcon: {
        flexShrink: 0,
        color: tokens.colorNeutralForeground3
    },
    summaryContainer: {
        maxHeight: '400px',
        overflowY: 'auto',
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusSmall
    },
    summaryText: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
        color: tokens.colorNeutralForeground1,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-word'
    },
    cursor: {
        display: 'inline-block',
        width: '8px',
        height: '16px',
        backgroundColor: tokens.colorBrandForeground1,
        marginLeft: '2px',
        animationName: {
            '0%, 100%': { opacity: 1 },
            '50%': { opacity: 0 }
        },
        animationDuration: '1s',
        animationIterationCount: 'infinite'
    },
    pendingContainer: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalL,
        color: tokens.colorNeutralForeground3
    },
    errorContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM,
        backgroundColor: tokens.colorPaletteRedBackground1,
        borderRadius: tokens.borderRadiusSmall
    },
    errorText: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorPaletteRedForeground1
    },
    infoContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalM,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusSmall
    },
    infoText: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground2
    },
    retryButton: {
        alignSelf: 'flex-start'
    },
    // Badge variants
    badgePending: {
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground2
    },
    badgeStreaming: {
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1
    },
    badgeComplete: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
        color: tokens.colorPaletteGreenForeground1
    },
    badgeError: {
        backgroundColor: tokens.colorPaletteRedBackground1,
        color: tokens.colorPaletteRedForeground1
    },
    badgeSkipped: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
        color: tokens.colorPaletteYellowForeground1
    },
    badgeNotSupported: {
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground3
    }
});

/**
 * Get status badge configuration
 */
const getStatusConfig = (status: SummaryStatus) => {
    switch (status) {
        case 'pending':
            return { label: 'Pending', styleKey: 'badgePending' as const };
        case 'streaming':
            return { label: 'Generating...', styleKey: 'badgeStreaming' as const };
        case 'complete':
            return { label: 'Complete', styleKey: 'badgeComplete' as const };
        case 'error':
            return { label: 'Error', styleKey: 'badgeError' as const };
        case 'skipped':
            return { label: 'Skipped', styleKey: 'badgeSkipped' as const };
        case 'not-supported':
            return { label: 'Not Supported', styleKey: 'badgeNotSupported' as const };
        default:
            return { label: 'Unknown', styleKey: 'badgePending' as const };
    }
};

/**
 * AI Summary Panel Component
 *
 * Displays a single document's AI summary with status indicator,
 * streaming text display, and error handling.
 */
export const AiSummaryPanel: React.FC<AiSummaryPanelProps> = ({
    documentId,
    fileName,
    summary,
    status,
    error,
    onRetry,
    className
}) => {
    const styles = useStyles();
    const statusConfig = getStatusConfig(status);

    // Render status badge
    const renderStatusBadge = () => (
        <Badge
            appearance="filled"
            className={styles[statusConfig.styleKey]}
            aria-label={`Summary status: ${statusConfig.label}`}
        >
            {statusConfig.label}
        </Badge>
    );

    // Render content based on status
    const renderContent = () => {
        switch (status) {
            case 'pending':
                return (
                    <div className={styles.pendingContainer}>
                        <Spinner size="small" />
                        <Text>Preparing summary...</Text>
                    </div>
                );

            case 'streaming':
            case 'complete':
                return (
                    <div
                        className={styles.summaryContainer}
                        role="region"
                        aria-label="Document summary"
                        aria-live={status === 'streaming' ? 'polite' : 'off'}
                        aria-atomic="false"
                    >
                        <Text className={styles.summaryText}>
                            {summary || ''}
                            {status === 'streaming' && (
                                <span
                                    className={styles.cursor}
                                    aria-hidden="true"
                                />
                            )}
                        </Text>
                    </div>
                );

            case 'error':
                return (
                    <div className={styles.errorContainer}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS }}>
                            <DismissCircleRegular />
                            <Text className={styles.errorText}>
                                {error || 'An error occurred while generating the summary.'}
                            </Text>
                        </div>
                        {onRetry && (
                            <Button
                                appearance="secondary"
                                size="small"
                                icon={<ArrowClockwiseRegular />}
                                onClick={onRetry}
                                className={styles.retryButton}
                                aria-label={`Retry summary for ${fileName}`}
                            >
                                Retry
                            </Button>
                        )}
                    </div>
                );

            case 'skipped':
                return (
                    <div className={styles.infoContainer}>
                        <InfoRegular />
                        <Text className={styles.infoText}>
                            Summary skipped by user
                        </Text>
                    </div>
                );

            case 'not-supported':
                return (
                    <div className={styles.infoContainer}>
                        <InfoRegular />
                        <Text className={styles.infoText}>
                            File type not supported for AI summarization
                        </Text>
                    </div>
                );

            default:
                return null;
        }
    };

    return (
        <div
            className={mergeClasses(styles.container, className)}
            data-document-id={documentId}
            role="article"
            aria-label={`AI summary for ${fileName}`}
        >
            <div className={styles.header}>
                <div className={styles.fileInfo}>
                    <DocumentRegular className={styles.fileIcon} />
                    <Text className={styles.fileName} title={fileName}>
                        {fileName}
                    </Text>
                </div>
                {renderStatusBadge()}
            </div>
            {renderContent()}
        </div>
    );
};

export default AiSummaryPanel;
