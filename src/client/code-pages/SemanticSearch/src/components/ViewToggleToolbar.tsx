/**
 * ViewToggleToolbar — 4-way view mode toggle
 *
 * Layout: spacer + Grid | Map | Treemap | Timeline buttons (right-aligned).
 * Search pane collapse is handled internally by SearchFilterPane.
 *
 * @see spec.md Section 6.1 — toolbar layout
 */

import React, { useCallback } from "react";
import {
    makeStyles,
    tokens,
    ToggleButton,
} from "@fluentui/react-components";
import {
    TextBulletListSquareRegular,
    DataScatterRegular,
    DataTreemapRegular,
    TimelineRegular,
} from "@fluentui/react-icons";
import type { ViewMode } from "../types";

// =============================================
// Props
// =============================================

export interface ViewToggleToolbarProps {
    /** Current view mode. */
    viewMode: ViewMode;
    /** Callback when view mode changes. */
    onViewModeChange: (mode: ViewMode) => void;
}

// =============================================
// View button configuration
// =============================================

interface ViewButtonConfig {
    mode: ViewMode;
    label: string;
    icon: React.ReactElement;
    ariaLabel: string;
}

const VIEW_BUTTONS: ViewButtonConfig[] = [
    {
        mode: "grid",
        label: "Grid",
        icon: <TextBulletListSquareRegular />,
        ariaLabel: "Switch to grid view",
    },
    {
        mode: "map",
        label: "Network",
        icon: <DataScatterRegular />,
        ariaLabel: "Switch to network graph view",
    },
    {
        mode: "treemap",
        label: "Treemap",
        icon: <DataTreemapRegular />,
        ariaLabel: "Switch to treemap view",
    },
    {
        mode: "timeline",
        label: "Timeline",
        icon: <TimelineRegular />,
        ariaLabel: "Switch to timeline view",
    },
];

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
});

// =============================================
// Component
// =============================================

export const ViewToggleToolbar: React.FC<ViewToggleToolbarProps> = ({
    viewMode,
    onViewModeChange,
}) => {
    const styles = useStyles();

    const handleClick = useCallback(
        (mode: ViewMode) => () => {
            onViewModeChange(mode);
        },
        [onViewModeChange],
    );

    return (
        <div className={styles.toolbar}>
            <div className={styles.spacer} />

            <div className={styles.toggleGroup}>
                {VIEW_BUTTONS.map((btn) => (
                    <ToggleButton
                        key={btn.mode}
                        checked={viewMode === btn.mode}
                        onClick={handleClick(btn.mode)}
                        icon={btn.icon}
                        size="small"
                        appearance={viewMode === btn.mode ? "primary" : "subtle"}
                        aria-label={btn.ariaLabel}
                    >
                        {btn.label}
                    </ToggleButton>
                ))}
            </div>
        </div>
    );
};

export default ViewToggleToolbar;
