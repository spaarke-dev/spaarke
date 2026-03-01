/**
 * ExecutionOverlay - Real-time playbook execution visualization
 *
 * Displays execution status, progress, and metrics during playbook execution.
 * Overlays on the canvas and shows node-level status updates.
 *
 * @version 2.0.0 (Code Page migration)
 */

import React, { useMemo } from "react";
import {
    makeStyles,
    tokens,
    shorthands,
    Text,
    Badge,
    Spinner,
    Button,
    Card,
    ProgressBar,
    Tooltip,
    mergeClasses,
} from "@fluentui/react-components";
import {
    Stop20Regular,
    Checkmark20Regular,
    Dismiss20Regular,
    Clock20Regular,
    BrainCircuit20Regular,
    Sparkle20Regular,
} from "@fluentui/react-icons";
import { useExecutionStore, type NodeExecutionStatus } from "../../stores/executionStore";
import { ConfidenceBadge, ConfidenceNodeBadge } from "./ConfidenceBadge";

const useStyles = makeStyles({
    overlay: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        pointerEvents: "none", // Allow clicks through to canvas
        zIndex: 100,
    },
    // Top status bar during execution
    statusBar: {
        position: "absolute",
        top: tokens.spacingVerticalS,
        left: "50%",
        transform: "translateX(-50%)",
        display: "flex",
        alignItems: "center",
        ...shorthands.gap(tokens.spacingHorizontalM),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        boxShadow: tokens.shadow8,
        pointerEvents: "auto", // Allow interaction with status bar
    },
    statusBarExecuting: {
        backgroundColor: tokens.colorBrandBackground,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    statusBarCompleted: {
        backgroundColor: tokens.colorPaletteGreenBackground2,
    },
    statusBarFailed: {
        backgroundColor: tokens.colorPaletteRedBackground2,
    },
    stopButton: {
        pointerEvents: "auto",
    },
    // Metrics panel (bottom right during/after execution)
    metricsPanel: {
        position: "absolute",
        bottom: tokens.spacingVerticalM,
        right: tokens.spacingHorizontalM,
        minWidth: "200px",
        pointerEvents: "auto",
    },
    metricsRow: {
        display: "flex",
        justifyContent: "space-between",
        ...shorthands.padding(tokens.spacingVerticalXS, "0"),
    },
    metricsLabel: {
        color: tokens.colorNeutralForeground2,
    },
    metricsValue: {
        fontFamily: tokens.fontFamilyMonospace,
    },
    // Node status badges (positioned over nodes via React Flow)
    nodeBadge: {
        position: "absolute",
        top: "-8px",
        right: "-8px",
        zIndex: 10,
    },
    progressRing: {
        width: "24px",
        height: "24px",
    },
});

interface ExecutionOverlayProps {
    /** Callback to stop execution */
    onStop?: () => void;
    /** Whether execution controls are visible */
    showControls?: boolean;
}

/**
 * Overlay component showing real-time execution status.
 * Renders status bar, progress, and metrics.
 */
export const ExecutionOverlay: React.FC<ExecutionOverlayProps> = ({
    onStop,
    showControls = true,
}) => {
    const styles = useStyles();

    const { status, nodeStates, totalTokensUsed, error, startedAt, completedAt, overallConfidence } =
        useExecutionStore();

    // Calculate execution metrics
    const metrics = useMemo(() => {
        const nodes = Array.from(nodeStates.values());
        const completedNodes = nodes.filter((n) => n.status === "completed").length;
        const failedNodes = nodes.filter((n) => n.status === "failed").length;
        const runningNodes = nodes.filter((n) => n.status === "running").length;
        const totalNodes = nodes.length;

        // Calculate duration
        let duration = "";
        if (startedAt) {
            const start = new Date(startedAt).getTime();
            const end = completedAt ? new Date(completedAt).getTime() : Date.now();
            const seconds = Math.floor((end - start) / 1000);
            if (seconds < 60) {
                duration = `${seconds}s`;
            } else {
                const minutes = Math.floor(seconds / 60);
                const remainingSeconds = seconds % 60;
                duration = `${minutes}m ${remainingSeconds}s`;
            }
        }

        return {
            completedNodes,
            failedNodes,
            runningNodes,
            totalNodes,
            duration,
            progress: totalNodes > 0 ? (completedNodes / totalNodes) * 100 : 0,
        };
    }, [nodeStates, startedAt, completedAt]);

    // Don't render anything if not executing and no results
    if (status === "idle" && nodeStates.size === 0) {
        return null;
    }

    const isExecuting = status === "running";
    const isCompleted = status === "completed";
    const isFailed = status === "failed";

    return (
        <div className={styles.overlay}>
            {/* Status Bar */}
            {showControls && (
                <div
                    className={mergeClasses(
                        styles.statusBar,
                        isExecuting && styles.statusBarExecuting,
                        isCompleted && styles.statusBarCompleted,
                        isFailed && styles.statusBarFailed
                    )}
                >
                    {/* Status Icon */}
                    {isExecuting && <Spinner size="tiny" />}
                    {isCompleted && <Checkmark20Regular />}
                    {isFailed && <Dismiss20Regular />}

                    {/* Status Text */}
                    <Text weight="semibold">
                        {isExecuting && "Executing..."}
                        {isCompleted && "Execution Complete"}
                        {isFailed && "Execution Failed"}
                    </Text>

                    {/* Progress */}
                    {isExecuting && metrics.totalNodes > 0 && (
                        <Text size={200}>
                            {metrics.completedNodes}/{metrics.totalNodes} nodes
                        </Text>
                    )}

                    {/* Duration */}
                    {metrics.duration && (
                        <Badge appearance="outline" icon={<Clock20Regular />}>
                            {metrics.duration}
                        </Badge>
                    )}

                    {/* Stop Button */}
                    {isExecuting && onStop && (
                        <Tooltip content="Stop execution" relationship="label">
                            <Button
                                className={styles.stopButton}
                                appearance="subtle"
                                icon={<Stop20Regular />}
                                size="small"
                                onClick={onStop}
                            />
                        </Tooltip>
                    )}
                </div>
            )}

            {/* Metrics Panel (shows during and after execution) */}
            {(isExecuting || isCompleted || isFailed) && (
                <Card className={styles.metricsPanel} size="small">
                    <Text weight="semibold" size={200}>
                        Execution Metrics
                    </Text>

                    {/* Progress Bar */}
                    {isExecuting && (
                        <ProgressBar
                            value={metrics.progress / 100}
                            max={1}
                            thickness="medium"
                        />
                    )}

                    {/* Stats */}
                    <div className={styles.metricsRow}>
                        <Text className={styles.metricsLabel} size={200}>
                            Completed
                        </Text>
                        <Text className={styles.metricsValue} size={200}>
                            {metrics.completedNodes}
                        </Text>
                    </div>

                    {metrics.failedNodes > 0 && (
                        <div className={styles.metricsRow}>
                            <Text className={styles.metricsLabel} size={200}>
                                Failed
                            </Text>
                            <Text
                                className={styles.metricsValue}
                                size={200}
                                style={{ color: tokens.colorPaletteRedForeground1 }}
                            >
                                {metrics.failedNodes}
                            </Text>
                        </div>
                    )}

                    {metrics.runningNodes > 0 && (
                        <div className={styles.metricsRow}>
                            <Text className={styles.metricsLabel} size={200}>
                                Running
                            </Text>
                            <Text className={styles.metricsValue} size={200}>
                                {metrics.runningNodes}
                            </Text>
                        </div>
                    )}

                    {totalTokensUsed > 0 && (
                        <div className={styles.metricsRow}>
                            <Text className={styles.metricsLabel} size={200}>
                                <BrainCircuit20Regular
                                    style={{ verticalAlign: "middle", marginRight: "4px" }}
                                />
                                Tokens
                            </Text>
                            <Text className={styles.metricsValue} size={200}>
                                {totalTokensUsed.toLocaleString()}
                            </Text>
                        </div>
                    )}

                    {metrics.duration && (
                        <div className={styles.metricsRow}>
                            <Text className={styles.metricsLabel} size={200}>
                                Duration
                            </Text>
                            <Text className={styles.metricsValue} size={200}>
                                {metrics.duration}
                            </Text>
                        </div>
                    )}

                    {/* Overall Confidence */}
                    {overallConfidence !== null && overallConfidence !== undefined && (
                        <div className={styles.metricsRow}>
                            <Text className={styles.metricsLabel} size={200}>
                                <Sparkle20Regular
                                    style={{ verticalAlign: "middle", marginRight: "4px" }}
                                />
                                Confidence
                            </Text>
                            <ConfidenceBadge confidence={overallConfidence} size="compact" />
                        </div>
                    )}

                    {/* Error message */}
                    {error && (
                        <Text
                            size={200}
                            style={{
                                color: tokens.colorPaletteRedForeground1,
                                marginTop: tokens.spacingVerticalS,
                            }}
                        >
                            {error}
                        </Text>
                    )}
                </Card>
            )}
        </div>
    );
};

/**
 * Helper component to render execution status badge on a node.
 * Used by custom node components.
 */
interface NodeExecutionBadgeProps {
    nodeId: string;
}

export const NodeExecutionBadge: React.FC<NodeExecutionBadgeProps> = ({ nodeId }) => {
    const styles = useStyles();
    const nodeState = useExecutionStore((state) => state.nodeStates.get(nodeId));

    if (!nodeState) {
        return null;
    }

    const { status, progress, confidence } = nodeState;

    return (
        <div className={styles.nodeBadge}>
            {status === "running" && (
                <Tooltip content={`Running${progress ? ` (${progress}%)` : ""}`} relationship="label">
                    <Badge appearance="filled" color="brand" icon={<Spinner size="extra-tiny" />} />
                </Tooltip>
            )}
            {status === "completed" && (
                confidence !== undefined ? (
                    <ConfidenceNodeBadge confidence={confidence} />
                ) : (
                    <Tooltip content="Completed" relationship="label">
                        <Badge appearance="filled" color="success" icon={<Checkmark20Regular />} />
                    </Tooltip>
                )
            )}
            {status === "failed" && (
                <Tooltip content={nodeState.error ?? "Failed"} relationship="label">
                    <Badge appearance="filled" color="danger" icon={<Dismiss20Regular />} />
                </Tooltip>
            )}
            {status === "pending" && (
                <Badge appearance="outline" color="informative" icon={<Clock20Regular />} />
            )}
        </div>
    );
};

/**
 * Get CSS class name for node based on execution status.
 * Used by custom node components for visual styling.
 */
export function getNodeExecutionClassName(status: NodeExecutionStatus | undefined): string {
    switch (status) {
        case "running":
            return "node-executing";
        case "completed":
            return "node-completed";
        case "failed":
            return "node-failed";
        case "skipped":
            return "node-skipped";
        default:
            return "";
    }
}
