/**
 * SearchResultsTreemap — Div-based treemap visualization
 *
 * Displays search results grouped by category with tile sizes proportional
 * to relevance score. Supports drill-down into individual groups via a
 * breadcrumb-style navigation.
 *
 * Features:
 *   - Absolutely-positioned div tiles sized by useTreemapLayout
 *   - Drill-down into category groups via group header click
 *   - Breadcrumb navigation when drilled
 *   - Hover tooltips with full name, score, group, domain
 *   - Click to navigate to result
 *   - Loading / empty states with Fluent Spinner / Text
 *
 * @see useTreemapLayout — layout hook
 * @see colorScale — getCategoryColor
 * @see ADR-021 — Fluent UI v9 design system
 */

import React, { useState, useCallback, useMemo, useRef, useEffect } from "react";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
} from "@fluentui/react-components";
import type { DocumentSearchResult, RecordSearchResult, SearchDomain, VisualizationColorBy } from "../types";
import { useTreemapLayout, type TreemapTile } from "../hooks/useTreemapLayout";
import { getCategoryColor } from "../utils/colorScale";
import { getResultDomain, extractClusterKey, type SearchResult } from "../utils/groupResults";

// =============================================
// Props
// =============================================

export interface SearchResultsTreemapProps {
    /** Search results to display in treemap. */
    results: (DocumentSearchResult | RecordSearchResult)[];
    /** Category field for grouping tiles. */
    groupBy: VisualizationColorBy;
    /** Whether to show tile labels (always shown if tile width > 80). */
    showLabels: boolean;
    /** Whether results are still loading. */
    isLoading: boolean;
    /** Active search domain tab. */
    activeDomain: SearchDomain;
    /** Callback when a result tile is clicked. */
    onResultClick: (resultId: string, domain: SearchDomain) => void;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        position: "relative",
        flex: 1,
        overflow: "hidden",
        width: "100%",
        height: "100%",
        display: "flex",
        flexDirection: "column",
    },
    breadcrumb: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        minHeight: "28px",
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    breadcrumbLink: {
        cursor: "pointer",
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorBrandForeground1,
        ":hover": {
            textDecorationLine: "underline",
        },
    },
    breadcrumbSeparator: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    breadcrumbCurrent: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    tilesContainer: {
        position: "relative",
        flex: 1,
        overflow: "hidden",
    },
    tile: {
        position: "absolute",
        overflow: "hidden",
        cursor: "pointer",
        borderRadius: tokens.borderRadiusSmall,
        display: "flex",
        flexDirection: "column",
        justifyContent: "space-between",
        transitionProperty: "opacity, border-color",
        transitionDuration: "0.15s",
        transitionTimingFunction: "ease",
        boxSizing: "border-box",
        ":hover": {
            opacity: 0.9,
        },
    },
    tileLabel: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
        paddingTop: "4px",
        paddingLeft: "4px",
        paddingRight: "4px",
        paddingBottom: 0,
        fontWeight: tokens.fontWeightRegular,
    },
    scoreBadge: {
        position: "absolute",
        bottom: "3px",
        right: "3px",
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusCircular,
        paddingLeft: "4px",
        paddingRight: "4px",
        paddingTop: "1px",
        paddingBottom: "1px",
        opacity: 0.85,
    },
    groupHeader: {
        position: "absolute",
        cursor: "pointer",
        display: "flex",
        alignItems: "center",
        paddingLeft: "4px",
        paddingRight: "4px",
        borderRadius: tokens.borderRadiusSmall,
        zIndex: 2,
        ":hover": {
            textDecorationLine: "underline",
        },
    },
    groupHeaderLabel: {
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
    },
    centerMessage: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
    },
    tooltip: {
        position: "absolute",
        pointerEvents: "none",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow8,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        zIndex: 20,
        maxWidth: "240px",
    },
    tooltipName: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
    },
    tooltipDetail: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
});

// =============================================
// Component
// =============================================

export const SearchResultsTreemap: React.FC<SearchResultsTreemapProps> = ({
    results,
    groupBy,
    showLabels,
    isLoading,
    activeDomain,
    onResultClick,
}) => {
    const styles = useStyles();

    // Container sizing via ResizeObserver
    const tilesContainerRef = useRef<HTMLDivElement | null>(null);
    const [dimensions, setDimensions] = useState<{ width: number; height: number }>({
        width: 0,
        height: 0,
    });

    const tilesContainerCallbackRef = useCallback(
        (node: HTMLDivElement | null) => {
            if (tilesContainerRef.current) {
                const obs = (tilesContainerRef.current as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver;
                obs?.disconnect();
                tilesContainerRef.current = null;
            }

            if (node) {
                tilesContainerRef.current = node;
                const rect = node.getBoundingClientRect();
                setDimensions({ width: rect.width, height: rect.height });

                const observer = new ResizeObserver((entries) => {
                    for (const entry of entries) {
                        const { width, height } = entry.contentRect;
                        setDimensions({ width, height });
                    }
                });
                observer.observe(node);
                (node as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver = observer;
            }
        },
        [],
    );

    // Cleanup ResizeObserver on unmount
    useEffect(() => {
        return () => {
            const node = tilesContainerRef.current;
            if (node) {
                const obs = (node as unknown as { __resizeObserver?: ResizeObserver }).__resizeObserver;
                obs?.disconnect();
            }
        };
    }, []);

    // Drill-down state
    const [drilledGroup, setDrilledGroup] = useState<string | null>(null);

    // Filter results to drilled group if applicable
    const effectiveResults = useMemo<(DocumentSearchResult | RecordSearchResult)[]>(() => {
        if (!drilledGroup) return results;
        return results.filter(
            (r) => extractClusterKey(r as SearchResult, groupBy) === drilledGroup,
        );
    }, [results, drilledGroup, groupBy]);

    // Layout hook
    const { tiles, groups } = useTreemapLayout(
        effectiveResults,
        groupBy,
        dimensions.width,
        dimensions.height,
    );

    // Hover state
    const [hoveredTileId, setHoveredTileId] = useState<string | null>(null);
    const [tooltipPos, setTooltipPos] = useState<{ x: number; y: number }>({ x: 0, y: 0 });

    // Reset drill-down when results or groupBy change
    useEffect(() => {
        setDrilledGroup(null);
    }, [results, groupBy]);

    // Event handlers
    const handleTileMouseEnter = useCallback((tileId: string) => {
        setHoveredTileId(tileId);
    }, []);

    const handleTileMouseLeave = useCallback(() => {
        setHoveredTileId(null);
    }, []);

    const handleTileMouseMove = useCallback(
        (e: React.MouseEvent) => {
            const container = tilesContainerRef.current;
            if (!container) return;
            const rect = container.getBoundingClientRect();
            setTooltipPos({ x: e.clientX - rect.left + 12, y: e.clientY - rect.top + 12 });
        },
        [],
    );

    const handleTileClick = useCallback(
        (tile: TreemapTile) => {
            onResultClick(tile.id, getResultDomain(tile.result));
        },
        [onResultClick],
    );

    const handleGroupHeaderClick = useCallback((groupKey: string) => {
        setDrilledGroup(groupKey);
    }, []);

    const handleBreadcrumbReset = useCallback(() => {
        setDrilledGroup(null);
    }, []);

    // Drilled group label
    const drilledGroupLabel = useMemo(() => {
        if (!drilledGroup) return "";
        const group = groups.find((g) => g.key === drilledGroup);
        return group?.label ?? drilledGroup;
    }, [drilledGroup, groups]);

    // Hovered tile for tooltip
    const hoveredTile = useMemo(() => {
        if (!hoveredTileId) return null;
        return tiles.find((t) => t.id === hoveredTileId) ?? null;
    }, [hoveredTileId, tiles]);

    // Compute group header positions (first tile of each group, when not drilled)
    const groupHeaders = useMemo(() => {
        if (drilledGroup) return [];

        const headers: { key: string; label: string; count: number; x: number; y: number; foreground: string }[] = [];
        const seenGroups = new Set<string>();

        for (const tile of tiles) {
            if (!seenGroups.has(tile.group)) {
                seenGroups.add(tile.group);
                const group = groups.find((g) => g.key === tile.group);
                const colors = getCategoryColor(tile.group);
                headers.push({
                    key: tile.group,
                    label: group?.label ?? tile.group,
                    count: group?.count ?? 0,
                    x: tile.x0,
                    y: Math.max(0, tile.y0 - 18),
                    foreground: colors.foreground,
                });
            }
        }

        return headers;
    }, [tiles, groups, drilledGroup]);

    // Loading state
    if (isLoading) {
        return (
            <div className={styles.container}>
                <div className={styles.tilesContainer}>
                    <div className={styles.centerMessage}>
                        <Spinner size="medium" label="Computing treemap layout..." />
                    </div>
                </div>
            </div>
        );
    }

    // Empty state
    if (results.length === 0 || tiles.length === 0) {
        return (
            <div className={styles.container}>
                <div className={styles.tilesContainer}>
                    <div className={styles.centerMessage}>
                        <Text size={400} weight="semibold">
                            No results to visualize
                        </Text>
                        <Text size={200}>
                            Run a search to see results in treemap view
                        </Text>
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            {/* Breadcrumb — visible when drilled into a group */}
            {drilledGroup && (
                <div className={styles.breadcrumb}>
                    <span
                        className={styles.breadcrumbLink}
                        onClick={handleBreadcrumbReset}
                        role="button"
                        tabIndex={0}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") handleBreadcrumbReset();
                        }}
                    >
                        All Results
                    </span>
                    <span className={styles.breadcrumbSeparator}>&gt;</span>
                    <span className={styles.breadcrumbCurrent}>{drilledGroupLabel}</span>
                </div>
            )}

            {/* Tiles area */}
            <div
                className={styles.tilesContainer}
                ref={tilesContainerCallbackRef}
                onMouseMove={handleTileMouseMove}
            >
                {/* Group headers (non-drilled view) */}
                {groupHeaders.map((header) => (
                    <div
                        key={`header-${header.key}`}
                        className={styles.groupHeader}
                        style={{
                            left: header.x,
                            top: header.y,
                            color: header.foreground,
                        }}
                        onClick={() => handleGroupHeaderClick(header.key)}
                        role="button"
                        tabIndex={0}
                        onKeyDown={(e) => {
                            if (e.key === "Enter" || e.key === " ") handleGroupHeaderClick(header.key);
                        }}
                        title={`Click to drill into ${header.label}`}
                    >
                        <span className={styles.groupHeaderLabel}>
                            {header.label} ({header.count})
                        </span>
                    </div>
                ))}

                {/* Treemap tiles */}
                {tiles.map((tile) => {
                    const colors = getCategoryColor(tile.group);
                    const isHovered = hoveredTileId === tile.id;
                    const shouldShowLabel = showLabels || tile.width > 80;

                    return (
                        <div
                            key={tile.id}
                            className={styles.tile}
                            style={{
                                left: tile.x0,
                                top: tile.y0,
                                width: tile.width,
                                height: tile.height,
                                backgroundColor: colors.background,
                                border: `1px solid ${isHovered ? colors.foreground : colors.background}`,
                                opacity: isHovered ? 0.9 : 0.85,
                            }}
                            onMouseEnter={() => handleTileMouseEnter(tile.id)}
                            onMouseLeave={handleTileMouseLeave}
                            onClick={() => handleTileClick(tile)}
                            role="button"
                            tabIndex={0}
                            onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") handleTileClick(tile);
                            }}
                            aria-label={`${tile.name} — score ${Math.round(tile.score * 100)}%`}
                        >
                            {shouldShowLabel && tile.height > 20 && (
                                <span className={styles.tileLabel}>{tile.name}</span>
                            )}
                            {tile.width > 50 && tile.height > 30 && (
                                <span className={styles.scoreBadge}>
                                    {Math.round(tile.score * 100)}%
                                </span>
                            )}
                        </div>
                    );
                })}

                {/* Hover tooltip */}
                {hoveredTile && (
                    <div
                        className={styles.tooltip}
                        style={{ left: tooltipPos.x, top: tooltipPos.y }}
                    >
                        <div className={styles.tooltipName}>{hoveredTile.name}</div>
                        <div className={styles.tooltipDetail}>
                            Score: {Math.round(hoveredTile.score * 100)}%
                        </div>
                        <div className={styles.tooltipDetail}>
                            Group: {hoveredTile.group}
                        </div>
                        <div className={styles.tooltipDetail}>
                            Domain: {getResultDomain(hoveredTile.result)}
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default SearchResultsTreemap;
