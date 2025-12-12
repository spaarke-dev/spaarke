/**
 * Record Match Suggestions Component
 *
 * Displays ranked match suggestions with confidence scores and match reasons.
 * Each suggestion is selectable for one-click association.
 *
 * ADR Compliance:
 * - ADR-006: PCF control pattern
 * - ADR-012: Fluent UI v9 components
 *
 * @version 1.0.0
 */

import * as React from 'react';
import {
    Badge,
    Button,
    Card,
    Spinner,
    Text,
    Tooltip,
    makeStyles,
    mergeClasses,
    tokens
} from '@fluentui/react-components';
import {
    CheckmarkCircleRegular,
    DismissCircleRegular,
    InfoRegular,
    LinkRegular,
    DocumentBriefcaseRegular,
    FolderRegular,
    ReceiptRegular,
    SearchRegular
} from '@fluentui/react-icons';
import { RecordMatchSuggestion } from '../services/useRecordMatch';

/**
 * Component Props
 */
export interface RecordMatchSuggestionsProps {
    /** List of match suggestions */
    suggestions: RecordMatchSuggestion[];
    /** Whether matching is in progress */
    isLoading?: boolean;
    /** Whether association is in progress */
    isAssociating?: boolean;
    /** Error message to display */
    error?: string | null;
    /** Success message to display */
    successMessage?: string | null;
    /** Callback when a suggestion is selected */
    onSelect: (suggestion: RecordMatchSuggestion) => void;
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
        gap: tokens.spacingVerticalM
    },
    header: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        color: tokens.colorNeutralForeground2
    },
    headerIcon: {
        fontSize: '16px'
    },
    headerText: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold
    },
    loadingContainer: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalL,
        color: tokens.colorNeutralForeground3
    },
    emptyContainer: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalL,
        color: tokens.colorNeutralForeground3,
        textAlign: 'center'
    },
    emptyIcon: {
        fontSize: '32px',
        color: tokens.colorNeutralForeground4
    },
    errorContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalM,
        backgroundColor: tokens.colorPaletteRedBackground1,
        borderRadius: tokens.borderRadiusSmall,
        color: tokens.colorPaletteRedForeground1
    },
    successContainer: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalM,
        backgroundColor: tokens.colorPaletteGreenBackground1,
        borderRadius: tokens.borderRadiusSmall,
        color: tokens.colorPaletteGreenForeground1
    },
    suggestionsList: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS
    },
    suggestionCard: {
        padding: tokens.spacingVerticalM,
        cursor: 'pointer',
        transition: 'background-color 0.1s ease',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover
        }
    },
    suggestionCardDisabled: {
        opacity: 0.7,
        cursor: 'not-allowed',
        ':hover': {
            backgroundColor: 'inherit'
        }
    },
    suggestionContent: {
        display: 'flex',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalM
    },
    suggestionIcon: {
        fontSize: '24px',
        color: tokens.colorBrandForeground1,
        flexShrink: 0,
        marginTop: '2px'
    },
    suggestionDetails: {
        flex: 1,
        minWidth: 0
    },
    suggestionHeader: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: tokens.spacingHorizontalS,
        marginBottom: tokens.spacingVerticalXS
    },
    suggestionName: {
        fontSize: tokens.fontSizeBase400,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
    },
    confidenceBadge: {
        flexShrink: 0
    },
    confidenceHigh: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
        color: tokens.colorPaletteGreenForeground1
    },
    confidenceMedium: {
        backgroundColor: tokens.colorPaletteYellowBackground1,
        color: tokens.colorPaletteYellowForeground1
    },
    confidenceLow: {
        backgroundColor: tokens.colorNeutralBackground4,
        color: tokens.colorNeutralForeground3
    },
    recordType: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalXS
    },
    matchReasons: {
        display: 'flex',
        flexWrap: 'wrap',
        gap: tokens.spacingHorizontalXS,
        marginTop: tokens.spacingVerticalXS
    },
    matchReasonTag: {
        fontSize: tokens.fontSizeBase200,
        padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalS}`,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusSmall,
        color: tokens.colorNeutralForeground2
    },
    selectButton: {
        flexShrink: 0
    }
});

/**
 * Get icon for record type
 */
const getRecordTypeIcon = (recordType: string) => {
    switch (recordType.toLowerCase()) {
        case 'sprk_matter':
            return <DocumentBriefcaseRegular />;
        case 'sprk_project':
            return <FolderRegular />;
        case 'sprk_invoice':
            return <ReceiptRegular />;
        default:
            return <DocumentBriefcaseRegular />;
    }
};

/**
 * Get display name for record type
 */
const getRecordTypeDisplayName = (recordType: string) => {
    switch (recordType.toLowerCase()) {
        case 'sprk_matter':
            return 'Matter';
        case 'sprk_project':
            return 'Project';
        case 'sprk_invoice':
            return 'Invoice';
        default:
            return 'Record';
    }
};

/**
 * Get confidence badge style
 */
const getConfidenceStyle = (score: number): 'confidenceHigh' | 'confidenceMedium' | 'confidenceLow' => {
    if (score >= 0.7) return 'confidenceHigh';
    if (score >= 0.4) return 'confidenceMedium';
    return 'confidenceLow';
};

/**
 * Record Match Suggestions Component
 *
 * Displays suggestions with confidence scores and enables one-click association.
 */
export const RecordMatchSuggestions: React.FC<RecordMatchSuggestionsProps> = ({
    suggestions,
    isLoading = false,
    isAssociating = false,
    error,
    successMessage,
    onSelect,
    className
}) => {
    const styles = useStyles();

    // Render loading state
    if (isLoading) {
        return (
            <div className={mergeClasses(styles.container, className)}>
                <div className={styles.header}>
                    <SearchRegular className={styles.headerIcon} />
                    <span className={styles.headerText}>Finding Matches...</span>
                </div>
                <div className={styles.loadingContainer}>
                    <Spinner size="small" />
                    <Text>Searching for matching records...</Text>
                </div>
            </div>
        );
    }

    // Render error state
    if (error) {
        return (
            <div className={mergeClasses(styles.container, className)}>
                <div className={styles.header}>
                    <LinkRegular className={styles.headerIcon} />
                    <span className={styles.headerText}>Match Suggestions</span>
                </div>
                <div className={styles.errorContainer}>
                    <DismissCircleRegular />
                    <Text>{error}</Text>
                </div>
            </div>
        );
    }

    // Render success state
    if (successMessage) {
        return (
            <div className={mergeClasses(styles.container, className)}>
                <div className={styles.header}>
                    <LinkRegular className={styles.headerIcon} />
                    <span className={styles.headerText}>Match Suggestions</span>
                </div>
                <div className={styles.successContainer}>
                    <CheckmarkCircleRegular />
                    <Text>{successMessage}</Text>
                </div>
            </div>
        );
    }

    // Render empty state
    if (suggestions.length === 0) {
        return (
            <div className={mergeClasses(styles.container, className)}>
                <div className={styles.header}>
                    <LinkRegular className={styles.headerIcon} />
                    <span className={styles.headerText}>Match Suggestions</span>
                </div>
                <div className={styles.emptyContainer}>
                    <InfoRegular className={styles.emptyIcon} />
                    <Text>No matching records found</Text>
                    <Text size={200}>Try selecting a different record type or analyzing more documents</Text>
                </div>
            </div>
        );
    }

    // Render suggestions
    return (
        <div className={mergeClasses(styles.container, className)}>
            <div className={styles.header}>
                <LinkRegular className={styles.headerIcon} />
                <span className={styles.headerText}>
                    Match Suggestions ({suggestions.length})
                </span>
            </div>
            <div className={styles.suggestionsList}>
                {suggestions.map((suggestion) => (
                    <Card
                        key={suggestion.recordId}
                        className={mergeClasses(
                            styles.suggestionCard,
                            isAssociating && styles.suggestionCardDisabled
                        )}
                        onClick={() => !isAssociating && onSelect(suggestion)}
                        role="button"
                        aria-label={`Associate with ${suggestion.recordName}`}
                        aria-disabled={isAssociating}
                    >
                        <div className={styles.suggestionContent}>
                            <span className={styles.suggestionIcon}>
                                {getRecordTypeIcon(suggestion.recordType)}
                            </span>
                            <div className={styles.suggestionDetails}>
                                <div className={styles.suggestionHeader}>
                                    <Text
                                        className={styles.suggestionName}
                                        title={suggestion.recordName}
                                    >
                                        {suggestion.recordName}
                                    </Text>
                                    <Tooltip
                                        content={`${Math.round(suggestion.confidenceScore * 100)}% confidence`}
                                        relationship="label"
                                    >
                                        <Badge
                                            appearance="filled"
                                            className={mergeClasses(
                                                styles.confidenceBadge,
                                                styles[getConfidenceStyle(suggestion.confidenceScore)]
                                            )}
                                        >
                                            {Math.round(suggestion.confidenceScore * 100)}%
                                        </Badge>
                                    </Tooltip>
                                </div>
                                <Text className={styles.recordType}>
                                    {getRecordTypeDisplayName(suggestion.recordType)}
                                </Text>
                                {suggestion.matchReasons.length > 0 && (
                                    <div className={styles.matchReasons}>
                                        {suggestion.matchReasons.slice(0, 3).map((reason, index) => (
                                            <span key={index} className={styles.matchReasonTag}>
                                                {reason}
                                            </span>
                                        ))}
                                        {suggestion.matchReasons.length > 3 && (
                                            <Tooltip
                                                content={suggestion.matchReasons.slice(3).join(', ')}
                                                relationship="description"
                                            >
                                                <span className={styles.matchReasonTag}>
                                                    +{suggestion.matchReasons.length - 3} more
                                                </span>
                                            </Tooltip>
                                        )}
                                    </div>
                                )}
                            </div>
                            <Button
                                appearance="primary"
                                size="small"
                                icon={isAssociating ? <Spinner size="tiny" /> : <LinkRegular />}
                                className={styles.selectButton}
                                disabled={isAssociating}
                                onClick={(e) => {
                                    e.stopPropagation();
                                    onSelect(suggestion);
                                }}
                            >
                                {isAssociating ? 'Linking...' : 'Link'}
                            </Button>
                        </div>
                    </Card>
                ))}
            </div>
        </div>
    );
};

export default RecordMatchSuggestions;
