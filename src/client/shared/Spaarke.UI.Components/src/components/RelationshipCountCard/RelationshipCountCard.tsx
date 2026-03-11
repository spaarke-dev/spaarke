/**
 * RelationshipCountCard - Displays a count of semantically related documents
 * with drill-through capability.
 *
 * Callback-based component with zero service dependencies.
 * Supports loading, error, zero-count, and normal states.
 *
 * @see ADR-012 - Shared component library (callback-based props)
 * @see ADR-021 - Fluent UI v9 design tokens
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Card,
    Text,
    Spinner,
    Button,
    Badge,
    mergeClasses,
} from "@fluentui/react-components";
import {
    DocumentSearch20Regular,
    ArrowRight16Regular,
    Warning20Regular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IRelationshipCountCardProps {
    /** Card title. Defaults to "RELATED DOCUMENTS". */
    title?: string;
    /** Number of semantically related documents. */
    count: number;
    /** Whether the count is currently being loaded. */
    isLoading?: boolean;
    /** Error message to display. Pass null or undefined for no error. */
    error?: string | null;
    /** Called when the user clicks to open/drill-through to related documents. */
    onOpen: () => void;
    /** Timestamp of the last relationship analysis. */
    lastUpdated?: Date;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    card: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingVerticalM + " " + tokens.spacingHorizontalM,
        minWidth: "200px",
        cursor: "default",
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    headerIcon: {
        color: tokens.colorBrandForeground1,
    },
    title: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground2,
        textTransform: "uppercase",
        letterSpacing: "0.05em",
    },
    body: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalM,
    },
    countContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    count: {
        fontSize: tokens.fontSizeHero800,
        fontWeight: tokens.fontWeightBold,
        lineHeight: tokens.lineHeightHero800,
        color: tokens.colorNeutralForeground1,
    },
    zeroCount: {
        color: tokens.colorNeutralForeground3,
    },
    spinnerContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "48px",
    },
    errorContainer: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        color: tokens.colorPaletteRedForeground1,
    },
    errorIcon: {
        color: tokens.colorPaletteRedForeground1,
        flexShrink: 0,
    },
    footer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
    },
    lastUpdated: {
        color: tokens.colorNeutralForeground3,
    },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Format a Date for display as a relative or short timestamp.
 */
function formatLastUpdated(date: Date): string {
    return new Intl.DateTimeFormat("en-US", {
        month: "short",
        day: "numeric",
        hour: "numeric",
        minute: "2-digit",
    }).format(date);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RelationshipCountCard: React.FC<IRelationshipCountCardProps> = ({
    title = "RELATED DOCUMENTS",
    count,
    isLoading = false,
    error,
    onOpen,
    lastUpdated,
}) => {
    const styles = useStyles();

    // ── Loading state ────────────────────────────────────────────────────
    if (isLoading) {
        return (
            <Card className={styles.card}>
                <div className={styles.header}>
                    <DocumentSearch20Regular className={styles.headerIcon} />
                    <Text className={styles.title} size={200}>
                        {title}
                    </Text>
                </div>
                <div className={styles.spinnerContainer}>
                    <Spinner size="small" label="Loading..." />
                </div>
            </Card>
        );
    }

    // ── Error state ──────────────────────────────────────────────────────
    if (error) {
        return (
            <Card className={styles.card}>
                <div className={styles.header}>
                    <DocumentSearch20Regular className={styles.headerIcon} />
                    <Text className={styles.title} size={200}>
                        {title}
                    </Text>
                </div>
                <div className={styles.errorContainer}>
                    <Warning20Regular className={styles.errorIcon} />
                    <Text size={200}>{error}</Text>
                </div>
            </Card>
        );
    }

    // ── Normal / Zero-count state ────────────────────────────────────────
    const isZero = count === 0;

    return (
        <Card className={styles.card}>
            <div className={styles.header}>
                <DocumentSearch20Regular className={styles.headerIcon} />
                <Text className={styles.title} size={200}>
                    {title}
                </Text>
            </div>
            <div className={styles.body}>
                <div className={styles.countContainer}>
                    <Text
                        className={mergeClasses(
                            styles.count,
                            isZero && styles.zeroCount
                        )}
                    >
                        {count}
                    </Text>
                    {!isZero && (
                        <Badge
                            appearance="filled"
                            color="brand"
                            size="small"
                        >
                            found
                        </Badge>
                    )}
                </div>
                {!isZero && (
                    <Button
                        appearance="subtle"
                        icon={<ArrowRight16Regular />}
                        iconPosition="after"
                        size="small"
                        onClick={onOpen}
                    >
                        View
                    </Button>
                )}
            </div>
            {isZero && (
                <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                    No related documents found
                </Text>
            )}
            {lastUpdated && (
                <div className={styles.footer}>
                    <Text className={styles.lastUpdated} size={100}>
                        Updated {formatLastUpdated(lastUpdated)}
                    </Text>
                </div>
            )}
        </Card>
    );
};

export default RelationshipCountCard;
