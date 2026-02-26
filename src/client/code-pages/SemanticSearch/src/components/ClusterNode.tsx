/**
 * ClusterNode — Cluster visualization node for ReactFlow graph
 *
 * Renders a cluster group showing:
 *   - Category icon + label
 *   - Record count
 *   - Average similarity ProgressBar
 *   - Top 3 result name previews
 *   - Proportional sizing based on record count
 *   - Fluent v9 palette color-coding (dark-mode safe)
 *   - Collapse indicator when expanded
 *
 * @see DocumentNode.tsx (DocumentRelationshipViewer) — reference pattern
 */

import React from "react";
import { Handle, Position, type NodeProps } from "@xyflow/react";
import {
    makeStyles,
    tokens,
    Text,
    ProgressBar,
    mergeClasses,
} from "@fluentui/react-components";
import {
    DocumentRegular,
    BriefcaseRegular,
    FolderRegular,
    GridRegular,
    ChevronUpRegular,
} from "@fluentui/react-icons";
import type { ClusterNodeData, GraphClusterBy } from "../types";

// =============================================
// Palette — Fluent v9 semantic color tokens
// =============================================

const PALETTE_BACKGROUNDS = [
    tokens.colorPaletteBerryBackground2,
    tokens.colorPaletteTealBackground2,
    tokens.colorPaletteMarigoldBackground2,
    tokens.colorPaletteLavenderBackground2,
    tokens.colorPalettePeachBackground2,
    tokens.colorPaletteSteelBackground2,
    tokens.colorPalettePinkBackground2,
    tokens.colorPaletteForestBackground2,
] as const;

const PALETTE_BORDERS = [
    tokens.colorPaletteBerryForeground2,
    tokens.colorPaletteTealForeground2,
    tokens.colorPaletteMarigoldForeground2,
    tokens.colorPaletteLavenderForeground2,
    tokens.colorPalettePeachForeground2,
    tokens.colorPaletteSteelForeground2,
    tokens.colorPalettePinkForeground2,
    tokens.colorPaletteForestForeground2,
] as const;

// =============================================
// Helpers
// =============================================

/** Get an icon component for the cluster category. */
function getCategoryIcon(category: GraphClusterBy): React.ReactElement {
    switch (category) {
        case "DocumentType":
            return <DocumentRegular />;
        case "MatterType":
            return <BriefcaseRegular />;
        case "Organization":
            return <FolderRegular />;
        case "PracticeArea":
        case "PersonContact":
        default:
            return <GridRegular />;
    }
}

/** Compute node size from record count. Min 130px, max 210px. */
function computeNodeSize(recordCount: number): number {
    return Math.max(130, Math.min(210, 90 + recordCount * 4));
}

/** Truncate a name to max characters with ellipsis. */
function truncate(text: string, maxLen: number): string {
    return text.length > maxLen ? text.slice(0, maxLen - 1) + "\u2026" : text;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderRadius: tokens.borderRadiusXLarge,
        cursor: "pointer",
        textAlign: "center",
        gap: tokens.spacingVerticalXS,
        transitionProperty: "all",
        transitionDuration: tokens.durationNormal,
        boxShadow: tokens.shadow4,
    },
    expanded: {
        boxShadow: tokens.shadow16,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
    },
    icon: {
        fontSize: tokens.fontSizeBase400,
        color: tokens.colorNeutralForeground1,
    },
    label: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        maxWidth: "160px",
    },
    count: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    progressRow: {
        width: "80%",
    },
    previewList: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: "1px",
    },
    previewItem: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        maxWidth: "150px",
    },
    collapseIcon: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
});

// =============================================
// Component
// =============================================

export const ClusterNode: React.FC<NodeProps> = ({ data }) => {
    const styles = useStyles();
    const d = data as unknown as ClusterNodeData;

    // Proportional sizing
    const size = computeNodeSize(d.recordCount);

    // Palette color by hashing clusterKey
    const colorIndex =
        Math.abs(
            d.clusterKey.split("").reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
        ) % PALETTE_BACKGROUNDS.length;

    return (
        <div
            className={mergeClasses(
                styles.container,
                d.isExpanded ? styles.expanded : undefined
            )}
            style={{
                width: `${size}px`,
                minHeight: `${size * 0.7}px`,
                backgroundColor: PALETTE_BACKGROUNDS[colorIndex],
                border: `2px solid ${PALETTE_BORDERS[colorIndex]}`,
            }}
            role="button"
            tabIndex={0}
            aria-label={`${d.clusterLabel}: ${d.recordCount} result${d.recordCount !== 1 ? "s" : ""}, ${Math.round(d.avgSimilarity * 100)}% average similarity`}
        >
            <Handle
                type="target"
                position={Position.Top}
                style={{ visibility: "hidden" }}
            />

            {/* Header: icon + label */}
            <div className={styles.header}>
                <span className={styles.icon}>
                    {getCategoryIcon(d.category)}
                </span>
                <Text className={styles.label} title={d.clusterLabel}>
                    {truncate(d.clusterLabel, 25)}
                </Text>
            </div>

            {/* Record count */}
            <Text className={styles.count}>
                {d.recordCount} record{d.recordCount !== 1 ? "s" : ""}
            </Text>

            {/* Average similarity bar */}
            <div className={styles.progressRow}>
                <ProgressBar
                    value={d.avgSimilarity}
                    max={1}
                    thickness="medium"
                />
            </div>
            <Text className={styles.count}>
                {Math.round(d.avgSimilarity * 100)}% avg
            </Text>

            {/* Top 3 result previews */}
            {d.topResults && d.topResults.length > 0 && (
                <div className={styles.previewList}>
                    {d.topResults.map((r, i) => (
                        <Text key={i} className={styles.previewItem} title={r.name}>
                            {truncate(r.name, 30)}
                        </Text>
                    ))}
                </div>
            )}

            {/* Collapse indicator when expanded */}
            {d.isExpanded && (
                <span className={styles.collapseIcon}>
                    <ChevronUpRegular />
                </span>
            )}

            <Handle
                type="source"
                position={Position.Bottom}
                style={{ visibility: "hidden" }}
            />
        </div>
    );
};

export default ClusterNode;
