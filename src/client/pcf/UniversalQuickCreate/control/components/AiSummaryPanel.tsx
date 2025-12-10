/**
 * AI Summary Panel
 *
 * Displays an AI-generated summary for a single document with status indicator,
 * streaming text display, TL;DR bullet points, keyword tags, and entity details.
 *
 * ADR Compliance:
 * - ADR-006: PCF control pattern
 * - ADR-012: Fluent UI v9 components from shared library
 *
 * @version 3.0.0.0
 */

import * as React from 'react';
import {
    Badge,
    Button,
    Spinner,
    Text,
    makeStyles,
    mergeClasses,
    tokens
} from '@fluentui/react-components';
import {
    DocumentRegular,
    ArrowClockwiseRegular,
    DismissCircleRegular,
    InfoRegular,
    OrganizationRegular,
    PersonRegular,
    MoneyRegular,
    CalendarRegular,
    DocumentCopyRegular
} from '@fluentui/react-icons';
import { SummaryStatus, ExtractedEntities } from '../services/useAiSummary';

// Re-export for backward compatibility
export type { SummaryStatus };

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

    /** TL;DR bullet points (available after completion) */
    tldr?: string[];

    /** Comma-separated keywords (available after completion) */
    keywords?: string;

    /** Extracted entities (available after completion) */
    entities?: ExtractedEntities;

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
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        flex: 1,
        minHeight: 0, // Allow flex shrinking
        overflow: 'hidden'
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
        fontSize: tokens.fontSizeBase500,
        fontWeight: tokens.fontWeightBold,
        color: tokens.colorNeutralForeground1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
    },
    fileIcon: {
        flexShrink: 0,
        color: tokens.colorNeutralForeground3,
        fontSize: '20px'
    },
    summaryContainer: {
        flex: 1,
        minHeight: 0,
        overflowY: 'auto',
        padding: tokens.spacingVerticalL,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: tokens.borderRadiusSmall
    },
    summaryText: {
        fontSize: tokens.fontSizeBase400,
        lineHeight: tokens.lineHeightBase400,
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
    // TL;DR styles
    tldrContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        marginBottom: tokens.spacingVerticalXXL
    },
    sectionHeader: {
        color: tokens.colorBrandForeground1,
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold
    },
    tldrList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        margin: 0,
        paddingLeft: tokens.spacingHorizontalL,
        listStyleType: 'disc'
    },
    tldrItem: {
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase400,
        color: tokens.colorNeutralForeground1,
        '::marker': {
            color: tokens.colorBrandForeground1
        }
    },
    // Summary section
    summarySection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        marginBottom: tokens.spacingVerticalXXL
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
    },
    // Keywords styles
    keywordsContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        marginBottom: tokens.spacingVerticalXXL
    },
    keywordsList: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: tokens.spacingHorizontalS
    },
    keywordTag: {
        backgroundColor: tokens.colorBrandBackground2,
        color: tokens.colorBrandForeground1,
        fontSize: tokens.fontSizeBase300,
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        borderRadius: tokens.borderRadiusMedium
    },
    // Entities section styles (no accordion)
    entitiesContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
        marginTop: tokens.spacingVerticalXL
    },
    entitySection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        marginBottom: tokens.spacingVerticalM
    },
    entityTypeHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold
    },
    entityList: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: tokens.spacingHorizontalS,
        paddingLeft: tokens.spacingHorizontalL
    },
    entityItem: {
        fontSize: tokens.fontSizeBase400,
        color: tokens.colorNeutralForeground1,
        backgroundColor: tokens.colorNeutralBackground3,
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
        borderRadius: tokens.borderRadiusSmall
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
 * streaming text display, TL;DR bullets, and error handling.
 */
export const AiSummaryPanel: React.FC<AiSummaryPanelProps> = ({
    documentId,
    fileName,
    summary,
    status,
    error,
    tldr,
    keywords,
    entities,
    onRetry,
    className
}) => {
    const styles = useStyles();
    const statusConfig = getStatusConfig(status);

    // Parse keywords string into array
    const keywordsList = React.useMemo(() => {
        if (!keywords) return [];
        return keywords.split(',').map(k => k.trim()).filter(k => k.length > 0);
    }, [keywords]);

    // Check if entities has any data
    const hasEntities = React.useMemo(() => {
        if (!entities) return false;
        return (
            entities.organizations?.length > 0 ||
            entities.people?.length > 0 ||
            entities.amounts?.length > 0 ||
            entities.dates?.length > 0 ||
            entities.references?.length > 0
        );
    }, [entities]);

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

    // Render keyword tags
    const renderKeywords = () => {
        if (keywordsList.length === 0) return null;

        return (
            <div className={styles.keywordsContainer}>
                <span className={styles.sectionHeader}>Keywords</span>
                <div className={styles.keywordsList} role="list" aria-label="Document keywords">
                    {keywordsList.map((keyword, index) => (
                        <Badge
                            key={index}
                            appearance="filled"
                            className={styles.keywordTag}
                            size="small"
                        >
                            {keyword}
                        </Badge>
                    ))}
                </div>
            </div>
        );
    };

    // Render an entity type section
    const renderEntityType = (
        icon: React.ReactNode,
        label: string,
        items: string[] | undefined
    ) => {
        if (!items || items.length === 0) return null;

        return (
            <div className={styles.entitySection}>
                <div className={styles.entityTypeHeader}>
                    {icon}
                    <span>{label}</span>
                </div>
                <div className={styles.entityList} role="list">
                    {items.map((item, index) => (
                        <span key={index} className={styles.entityItem}>
                            {item}
                        </span>
                    ))}
                </div>
            </div>
        );
    };

    // Render entities section (Document Type, Organizations, People, Amounts, Dates, References)
    const renderEntities = () => {
        // Check if we have any entities including document type
        const hasAnyEntities = hasEntities || entities?.documentType;
        if (!hasAnyEntities) return null;

        return (
            <div className={styles.entitiesContainer}>
                <span className={styles.sectionHeader}>Extracted Details</span>
                {/* Document Type */}
                {entities?.documentType && (
                    <div className={styles.entitySection}>
                        <div className={styles.entityTypeHeader}>
                            <span>Document Type:</span>
                            <span style={{ fontWeight: tokens.fontWeightRegular }}>{entities.documentType}</span>
                        </div>
                    </div>
                )}
                {renderEntityType(
                    <OrganizationRegular />,
                    'Organizations',
                    entities?.organizations
                )}
                {renderEntityType(
                    <PersonRegular />,
                    'People',
                    entities?.people
                )}
                {renderEntityType(
                    <MoneyRegular />,
                    'Amounts',
                    entities?.amounts
                )}
                {renderEntityType(
                    <CalendarRegular />,
                    'Dates',
                    entities?.dates
                )}
                {renderEntityType(
                    <DocumentCopyRegular />,
                    'References',
                    entities?.references
                )}
            </div>
        );
    };

    // Render TL;DR bullet list
    const renderTldr = () => {
        if (!tldr || tldr.length === 0) return null;

        return (
            <div className={styles.tldrContainer}>
                <span className={styles.sectionHeader}>TL;DR</span>
                <ul className={styles.tldrList} role="list">
                    {tldr.map((bullet, index) => (
                        <li key={index} className={styles.tldrItem}>
                            {bullet}
                        </li>
                    ))}
                </ul>
            </div>
        );
    };

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
                // Show streaming text with cursor during generation
                return (
                    <div
                        className={styles.summaryContainer}
                        role="region"
                        aria-label="Document summary"
                        aria-live="polite"
                        aria-atomic="false"
                    >
                        <Text className={styles.summaryText}>
                            {summary || ''}
                            <span
                                className={styles.cursor}
                                aria-hidden="true"
                            />
                        </Text>
                    </div>
                );

            case 'complete':
                // Full height scrollable content: TL;DR → Keywords → Summary → Extracted Details
                return (
                    <div className={styles.summaryContainer} role="region" aria-label="Document analysis">
                        {/* TL;DR bullets */}
                        {renderTldr()}
                        {/* Keywords */}
                        {renderKeywords()}
                        {/* Summary section */}
                        {summary && (
                            <div className={styles.summarySection}>
                                <span className={styles.sectionHeader}>Summary</span>
                                <Text className={styles.summaryText}>
                                    {summary}
                                </Text>
                            </div>
                        )}
                        {/* Extracted Details (includes Document Type) */}
                        {renderEntities()}
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
