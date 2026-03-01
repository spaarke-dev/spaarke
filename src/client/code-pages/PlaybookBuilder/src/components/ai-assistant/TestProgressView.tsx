/**
 * Test Progress View - Real-time test execution progress display
 *
 * Shows test execution status with:
 * - Overall progress bar with percentage
 * - Current node being executed
 * - Node-by-node results as they complete
 * - Cancel button to abort execution
 * - Error state display
 *
 * Uses Fluent UI v9 components with design tokens for theming support.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useCallback, useMemo } from "react";
import {
    ProgressBar,
    Spinner,
    Text,
    Button,
    Badge,
    makeStyles,
    tokens,
    shorthands,
    mergeClasses,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import {
    Checkmark16Regular,
    Dismiss16Regular,
    Play16Regular,
    Stop16Regular,
    Clock16Regular,
    Warning16Regular,
    ArrowForward16Regular,
} from "@fluentui/react-icons";
import {
    useAiAssistantStore,
    type TestNodeProgress,
    type TestMode,
} from "../../stores/aiAssistantStore";

// ============================================================================
// Styles
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalM),
        ...shorthands.padding(tokens.spacingVerticalM),
    },
    header: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        ...shorthands.gap(tokens.spacingHorizontalM),
    },
    headerLeft: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    modeBadge: {
        textTransform: "capitalize",
    },
    progressSection: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
    },
    progressInfo: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    progressText: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    currentStep: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorBrandBackground2,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.border("1px", "solid", tokens.colorBrandStroke1),
    },
    currentStepLabel: {
        color: tokens.colorBrandForeground1,
        fontWeight: tokens.fontWeightSemibold,
    },
    nodeList: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalXS),
        maxHeight: "300px",
        overflowY: "auto",
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    nodeItem: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
        ...shorthands.borderRadius(tokens.borderRadiusSmall),
        backgroundColor: tokens.colorNeutralBackground2,
    },
    nodeItemRunning: {
        backgroundColor: tokens.colorBrandBackground2,
        ...shorthands.border("1px", "solid", tokens.colorBrandStroke1),
    },
    nodeItemCompleted: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
    },
    nodeItemFailed: {
        backgroundColor: tokens.colorPaletteRedBackground1,
    },
    nodeItemSkipped: {
        backgroundColor: tokens.colorNeutralBackground3,
        opacity: 0.7,
    },
    nodeIcon: {
        flexShrink: 0,
        width: "16px",
        height: "16px",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
    },
    nodeIconPending: {
        color: tokens.colorNeutralForeground3,
    },
    nodeIconRunning: {
        color: tokens.colorBrandForeground1,
    },
    nodeIconCompleted: {
        color: tokens.colorPaletteGreenForeground1,
    },
    nodeIconFailed: {
        color: tokens.colorPaletteRedForeground1,
    },
    nodeIconSkipped: {
        color: tokens.colorNeutralForeground3,
    },
    nodeLabel: {
        flex: 1,
        ...shorthands.overflow("hidden"),
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    nodeDuration: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
        flexShrink: 0,
    },
    summary: {
        display: "flex",
        flexDirection: "column",
        ...shorthands.gap(tokens.spacingVerticalS),
        ...shorthands.padding(tokens.spacingVerticalM),
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
    },
    summaryCompleted: {
        backgroundColor: tokens.colorPaletteGreenBackground1,
        ...shorthands.border("1px", "solid", tokens.colorPaletteGreenBorder1),
    },
    summaryFailed: {
        backgroundColor: tokens.colorPaletteRedBackground1,
        ...shorthands.border("1px", "solid", tokens.colorPaletteRedBorder1),
    },
    summaryStats: {
        display: "flex",
        ...shorthands.gap(tokens.spacingHorizontalL),
        flexWrap: "wrap",
    },
    statItem: {
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalXS),
    },
    statLabel: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    statValue: {
        fontWeight: tokens.fontWeightSemibold,
    },
    actions: {
        display: "flex",
        justifyContent: "flex-end",
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
});

// ============================================================================
// Helpers
// ============================================================================

/**
 * Format duration in ms to human-readable string.
 */
const formatDuration = (ms: number): string => {
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60000) return `${(ms / 1000).toFixed(1)}s`;
    const minutes = Math.floor(ms / 60000);
    const seconds = ((ms % 60000) / 1000).toFixed(0);
    return `${minutes}m ${seconds}s`;
};

/**
 * Get mode display label.
 */
const getModeLabel = (mode: TestMode | null): string => {
    switch (mode) {
        case "mock":
            return "Mock Test";
        case "quick":
            return "Quick Test";
        case "production":
            return "Production Test";
        default:
            return "Test";
    }
};

// ============================================================================
// Props
// ============================================================================

export interface TestProgressViewProps {
    /** Callback when cancel is clicked */
    onCancel?: () => void;
    /** Callback when test completes (to view results or close) */
    onComplete?: () => void;
    /** Show compact view (fewer details) */
    compact?: boolean;
}

// ============================================================================
// Node Item Component
// ============================================================================

interface NodeItemProps {
    node: TestNodeProgress;
}

const NodeItem: React.FC<NodeItemProps> = ({ node }) => {
    const styles = useStyles();

    // Get status-specific styling
    const getStatusClasses = () => {
        switch (node.status) {
            case "running":
                return { item: styles.nodeItemRunning, icon: styles.nodeIconRunning };
            case "completed":
                return { item: styles.nodeItemCompleted, icon: styles.nodeIconCompleted };
            case "failed":
                return { item: styles.nodeItemFailed, icon: styles.nodeIconFailed };
            case "skipped":
                return { item: styles.nodeItemSkipped, icon: styles.nodeIconSkipped };
            default:
                return { item: "", icon: styles.nodeIconPending };
        }
    };

    // Get status icon
    const getStatusIcon = () => {
        switch (node.status) {
            case "running":
                return <Spinner size="extra-tiny" />;
            case "completed":
                return <Checkmark16Regular />;
            case "failed":
                return <Dismiss16Regular />;
            case "skipped":
                return <ArrowForward16Regular />;
            default:
                return <Clock16Regular />;
        }
    };

    const statusClasses = getStatusClasses();

    return (
        <div className={mergeClasses(styles.nodeItem, statusClasses.item)}>
            <div className={mergeClasses(styles.nodeIcon, statusClasses.icon)}>
                {getStatusIcon()}
            </div>
            <Text className={styles.nodeLabel} size={200}>
                {node.label}
            </Text>
            {node.durationMs !== undefined && node.status === "completed" && (
                <Text className={styles.nodeDuration}>{formatDuration(node.durationMs)}</Text>
            )}
            {node.error && (
                <Badge appearance="filled" color="danger" size="small">
                    Error
                </Badge>
            )}
        </div>
    );
};

// ============================================================================
// Component
// ============================================================================

export const TestProgressView: React.FC<TestProgressViewProps> = ({
    onCancel,
    onComplete,
    compact = false,
}) => {
    const styles = useStyles();

    // Store state
    const { testExecution, resetTestExecution } = useAiAssistantStore();

    // Calculate progress
    const progress = useMemo(() => {
        const { nodesProgress } = testExecution;
        if (nodesProgress.length === 0) return 0;

        const completed = nodesProgress.filter(
            (n) => n.status === "completed" || n.status === "failed" || n.status === "skipped"
        ).length;

        return completed / nodesProgress.length;
    }, [testExecution.nodesProgress]);

    // Calculate stats
    const stats = useMemo(() => {
        const { nodesProgress } = testExecution;
        return {
            total: nodesProgress.length,
            completed: nodesProgress.filter((n) => n.status === "completed").length,
            failed: nodesProgress.filter((n) => n.status === "failed").length,
            skipped: nodesProgress.filter((n) => n.status === "skipped").length,
            pending: nodesProgress.filter((n) => n.status === "pending").length,
            running: nodesProgress.filter((n) => n.status === "running").length,
        };
    }, [testExecution.nodesProgress]);

    // Current node
    const currentNode = useMemo(() => {
        if (!testExecution.currentNodeId) return null;
        return testExecution.nodesProgress.find((n) => n.nodeId === testExecution.currentNodeId);
    }, [testExecution.currentNodeId, testExecution.nodesProgress]);

    // Is test complete?
    const isComplete = !testExecution.isActive && testExecution.nodesProgress.length > 0;
    const hasError = testExecution.error !== null || stats.failed > 0;

    // Handle cancel
    const handleCancel = useCallback(() => {
        onCancel?.();
    }, [onCancel]);

    // Handle done/close
    const handleDone = useCallback(() => {
        resetTestExecution();
        onComplete?.();
    }, [resetTestExecution, onComplete]);

    // Don't render if no test execution
    if (!testExecution.isActive && testExecution.nodesProgress.length === 0) {
        return null;
    }

    return (
        <div className={styles.container}>
            {/* Header */}
            <div className={styles.header}>
                <div className={styles.headerLeft}>
                    {testExecution.isActive ? (
                        <Spinner size="tiny" />
                    ) : hasError ? (
                        <Warning16Regular style={{ color: tokens.colorPaletteRedForeground1 }} />
                    ) : (
                        <Checkmark16Regular style={{ color: tokens.colorPaletteGreenForeground1 }} />
                    )}
                    <Text weight="semibold">
                        {testExecution.isActive
                            ? "Running Test..."
                            : hasError
                            ? "Test Failed"
                            : "Test Complete"}
                    </Text>
                    <Badge appearance="outline" size="small" className={styles.modeBadge}>
                        {getModeLabel(testExecution.mode)}
                    </Badge>
                </div>
            </div>

            {/* Error message */}
            {testExecution.error && (
                <MessageBar intent="error">
                    <MessageBarBody>{testExecution.error}</MessageBarBody>
                </MessageBar>
            )}

            {/* Progress bar */}
            {testExecution.isActive && (
                <div className={styles.progressSection}>
                    <div className={styles.progressInfo}>
                        <Text className={styles.progressText}>
                            {stats.completed + stats.failed + stats.skipped} of {stats.total} nodes
                        </Text>
                        <Text className={styles.progressText}>{Math.round(progress * 100)}%</Text>
                    </div>
                    <ProgressBar value={progress} thickness="large" />
                </div>
            )}

            {/* Current step indicator */}
            {testExecution.isActive && currentNode && (
                <div className={styles.currentStep}>
                    <Spinner size="extra-tiny" />
                    <Text className={styles.currentStepLabel}>{currentNode.label}</Text>
                </div>
            )}

            {/* Node list */}
            {!compact && testExecution.nodesProgress.length > 0 && (
                <div className={styles.nodeList}>
                    {testExecution.nodesProgress.map((node) => (
                        <NodeItem key={node.nodeId} node={node} />
                    ))}
                </div>
            )}

            {/* Summary (when complete) */}
            {isComplete && (
                <div
                    className={mergeClasses(
                        styles.summary,
                        hasError ? styles.summaryFailed : styles.summaryCompleted
                    )}
                >
                    <div className={styles.summaryStats}>
                        <div className={styles.statItem}>
                            <Text className={styles.statLabel}>Completed:</Text>
                            <Text className={styles.statValue}>{stats.completed}</Text>
                        </div>
                        {stats.failed > 0 && (
                            <div className={styles.statItem}>
                                <Text className={styles.statLabel}>Failed:</Text>
                                <Text className={styles.statValue} style={{ color: tokens.colorPaletteRedForeground1 }}>
                                    {stats.failed}
                                </Text>
                            </div>
                        )}
                        {stats.skipped > 0 && (
                            <div className={styles.statItem}>
                                <Text className={styles.statLabel}>Skipped:</Text>
                                <Text className={styles.statValue}>{stats.skipped}</Text>
                            </div>
                        )}
                        <div className={styles.statItem}>
                            <Text className={styles.statLabel}>Duration:</Text>
                            <Text className={styles.statValue}>{formatDuration(testExecution.totalDurationMs)}</Text>
                        </div>
                    </div>
                    {testExecution.analysisId && (
                        <Text size={200} style={{ color: tokens.colorNeutralForeground2 }}>
                            Analysis ID: {testExecution.analysisId}
                        </Text>
                    )}
                </div>
            )}

            {/* Actions */}
            <div className={styles.actions}>
                {testExecution.isActive ? (
                    <Button
                        appearance="secondary"
                        icon={<Stop16Regular />}
                        onClick={handleCancel}
                    >
                        Cancel
                    </Button>
                ) : (
                    <Button appearance="primary" onClick={handleDone}>
                        Done
                    </Button>
                )}
            </div>
        </div>
    );
};

export default TestProgressView;
