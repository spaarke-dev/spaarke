/**
 * ViewToggleToolbar — Graph/Grid toggle with cluster-by dropdown
 *
 * Contains:
 *   - Graph/Grid toggle buttons (ToggleButton group)
 *   - Cluster-by dropdown (visible only in graph mode)
 *
 * Saved search selector has moved to SearchFilterPane (side pane).
 *
 * @see spec.md Section 6.1 — toolbar layout
 */

import React, { useCallback } from "react";
import {
    makeStyles,
    tokens,
    ToggleButton,
    Dropdown,
    Option,
} from "@fluentui/react-components";
import {
    GridRegular,
    TextBulletListSquareRegular,
} from "@fluentui/react-icons";
import type { ViewMode, GraphClusterBy } from "../types";

// =============================================
// Cluster-by options
// =============================================

const CLUSTER_BY_OPTIONS: { value: GraphClusterBy; label: string }[] = [
    { value: "MatterType", label: "Matter Type" },
    { value: "PracticeArea", label: "Practice Area" },
    { value: "DocumentType", label: "Document Type" },
    { value: "Organization", label: "Organization" },
    { value: "PersonContact", label: "Person/Contact" },
];

// =============================================
// Props
// =============================================

export interface ViewToggleToolbarProps {
    /** Current view mode. */
    viewMode: ViewMode;
    /** Callback when view mode changes. */
    onViewModeChange: (mode: ViewMode) => void;
    /** Current cluster-by category. */
    clusterBy: GraphClusterBy;
    /** Callback when cluster-by changes. */
    onClusterByChange: (clusterBy: GraphClusterBy) => void;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    toolbar: {
        display: "flex",
        alignItems: "center",
        width: "100%",
        gap: tokens.spacingHorizontalS,
    },
    spacer: {
        flex: 1,
    },
    toggleGroup: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXXS,
    },
    clusterDropdown: {
        minWidth: "160px",
    },
});

// =============================================
// Component
// =============================================

export const ViewToggleToolbar: React.FC<ViewToggleToolbarProps> = ({
    viewMode,
    onViewModeChange,
    clusterBy,
    onClusterByChange,
}) => {
    const styles = useStyles();

    const handleGridClick = useCallback(() => {
        onViewModeChange("grid");
    }, [onViewModeChange]);

    const handleGraphClick = useCallback(() => {
        onViewModeChange("graph");
    }, [onViewModeChange]);

    const handleClusterByChange = useCallback(
        (_event: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onClusterByChange(data.optionValue as GraphClusterBy);
            }
        },
        [onClusterByChange]
    );

    return (
        <div className={styles.toolbar}>
            <div className={styles.spacer} />

            {/* View toggle */}
            <div className={styles.toggleGroup}>
                <ToggleButton
                    checked={viewMode === "grid"}
                    onClick={handleGridClick}
                    icon={<TextBulletListSquareRegular />}
                    size="small"
                    appearance={viewMode === "grid" ? "primary" : "subtle"}
                    aria-label="Switch to grid view"
                >
                    Grid
                </ToggleButton>
                <ToggleButton
                    checked={viewMode === "graph"}
                    onClick={handleGraphClick}
                    icon={<GridRegular />}
                    size="small"
                    appearance={viewMode === "graph" ? "primary" : "subtle"}
                    aria-label="Switch to graph view"
                >
                    Graph
                </ToggleButton>
            </div>

            {/* Cluster-by dropdown — visible only in graph mode */}
            {viewMode === "graph" && (
                <Dropdown
                    className={styles.clusterDropdown}
                    size="small"
                    value={
                        CLUSTER_BY_OPTIONS.find((o) => o.value === clusterBy)
                            ?.label ?? "Matter Type"
                    }
                    selectedOptions={[clusterBy]}
                    onOptionSelect={handleClusterByChange}
                    aria-label="Cluster by"
                >
                    {CLUSTER_BY_OPTIONS.map((opt) => (
                        <Option key={opt.value} value={opt.value}>
                            {opt.label}
                        </Option>
                    ))}
                </Dropdown>
            )}
        </div>
    );
};

export default ViewToggleToolbar;
