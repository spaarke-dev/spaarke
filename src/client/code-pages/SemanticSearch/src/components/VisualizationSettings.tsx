/**
 * VisualizationSettings — Expandable settings panel in the toolbar row
 *
 * Renders a collapsible section (default: expanded) with view-specific settings.
 * Placed in the toolbar row to the right of the view toggle buttons.
 *
 * Shared settings (all views):
 *   - Relevance Threshold slider (client-side filter on displayed results)
 *   - Search Mode dropdown
 *
 * Network-only settings:
 *   - Cluster By dropdown
 *   - Minimum Similarity slider
 *
 * Treemap-only settings:
 *   - Cluster By dropdown
 *   - Show Labels toggle
 *
 * Timeline-only settings:
 *   - Date Field dropdown
 *   - Cluster By dropdown
 *
 * @see ADR-021 for Fluent UI v9 design system requirements
 */

import React, { useCallback, useState, useEffect, useRef } from "react";
import {
    makeStyles,
    tokens,
    Slider,
    Dropdown,
    Option,
    Label,
    Divider,
    Button,
    Checkbox,
    Text,
    Popover,
    PopoverTrigger,
    PopoverSurface,
} from "@fluentui/react-components";
import { ChevronDown20Regular, ChevronUp20Regular, Info20Regular } from "@fluentui/react-icons";
import type {
    ViewMode,
    HybridMode,
    VisualizationColorBy,
    TimelineDateField,
} from "../types";

// =============================================
// Option Constants
// =============================================

const CLUSTER_BY_OPTIONS: { value: VisualizationColorBy; label: string }[] = [
    { value: "DocumentType", label: "Document Type" },
    { value: "MatterType", label: "Entity Type" },
    { value: "Organization", label: "Related Entity" },
    { value: "PracticeArea", label: "File Type" },
    { value: "PersonContact", label: "Created By" },
];

const SEARCH_MODE_OPTIONS: { value: HybridMode; label: string }[] = [
    { value: "rrf", label: "Hybrid (RRF)" },
    { value: "vectorOnly", label: "Vector Only" },
    { value: "keywordOnly", label: "Keyword Only" },
];

const DATE_FIELD_OPTIONS: { value: TimelineDateField; label: string }[] = [
    { value: "createdAt", label: "Created Date" },
    { value: "updatedAt", label: "Modified Date" },
    { value: "modifiedAt", label: "Last Modified" },
];

// =============================================
// Help Content (view-specific)
// =============================================

interface HelpSection {
    title: string;
    body: string;
}

const VIEW_HELP: Record<ViewMode, { heading: string; sections: HelpSection[] }> = {
    grid: {
        heading: "How the Results Grid Works",
        sections: [
            {
                title: "Similarity Score",
                body: "The percentage badge indicates how closely a document's content matches your query's meaning. Higher = more relevant. Use the Relevance Threshold slider to hide low-scoring results.",
            },
            {
                title: "Search Modes",
                body: "Hybrid (default): Combines meaning-based and keyword search for best overall results. Concept Only: Pure meaning-based search \u2014 good for abstract queries. Keyword Only: Traditional exact-word matching \u2014 good for specific terms or clause numbers.",
            },
            {
                title: "Sorting & Selection",
                body: "Click column headers to sort. Select rows to enable bulk actions (open, email, download) in the command bar above.",
            },
        ],
    },
    map: {
        heading: "How the Network Graph Works",
        sections: [
            {
                title: "Nodes & Connections",
                body: "Each circle represents a search result. Lines between circles indicate metadata similarity \u2014 results that share attributes (type, related entity, author) are connected. The percentage on each line shows the similarity strength.",
            },
            {
                title: "Circle Size & Color",
                body: "Larger circles have higher relevance scores. Colors group results by the selected Cluster By category. Hover any node to see its details and highlight its connections.",
            },
            {
                title: "Cluster By",
                body: "Groups results by a shared attribute: Document Type, Entity Type, Related Entity, File Type, or Created By. Connected clusters reveal how your results relate to each other.",
            },
            {
                title: "Interacting",
                body: "Drag nodes to rearrange the layout. Scroll to zoom in/out. Click a node to open a document preview. Click and drag the background to pan.",
            },
            {
                title: "Minimum Similarity",
                body: "Filters which results appear in the graph based on their search relevance score. Increase to focus on the most relevant results only.",
            },
        ],
    },
    treemap: {
        heading: "How the Treemap Works",
        sections: [
            {
                title: "Tile Size",
                body: "Each rectangle represents a search result. Larger tiles have higher relevance scores. Results are grouped into colored regions by the selected Cluster By category.",
            },
            {
                title: "Cluster By",
                body: "Controls how results are grouped into regions: Document Type, Entity Type, Related Entity, File Type, or Created By. Larger regions contain more matching results.",
            },
            {
                title: "Interacting",
                body: "Click a tile to open the document preview. Toggle Show Labels to display result names on each tile.",
            },
        ],
    },
    timeline: {
        heading: "How the Timeline Works",
        sections: [
            {
                title: "Date Axis",
                body: "Results are plotted along a horizontal timeline based on the selected Date Field (Created Date, Modified Date, or Last Modified). This reveals when documents were created or last changed.",
            },
            {
                title: "Cluster By",
                body: "Colors results by a shared attribute so you can see patterns over time \u2014 for example, which document types were created in which periods.",
            },
            {
                title: "Interacting",
                body: "Click a point to open the document preview. Hover to see details including the date value and relevance score.",
            },
        ],
    },
};

// =============================================
// Props
// =============================================

export interface VisualizationSettingsProps {
    /** Current view mode — controls which settings are visible. */
    viewMode: ViewMode;

    // --- Shared settings (all views) ---
    threshold: number;
    onThresholdChange: (value: number) => void;
    searchMode: HybridMode;
    onSearchModeChange: (mode: HybridMode) => void;

    // --- Map settings ---
    mapColorBy: VisualizationColorBy;
    onMapColorByChange: (value: VisualizationColorBy) => void;
    mapMinSimilarity: number;
    onMapMinSimilarityChange: (value: number) => void;

    // --- Treemap settings ---
    treemapGroupBy: VisualizationColorBy;
    onTreemapGroupByChange: (value: VisualizationColorBy) => void;
    treemapShowLabels: boolean;
    onTreemapShowLabelsChange: (value: boolean) => void;

    // --- Timeline settings ---
    timelineDateField: TimelineDateField;
    onTimelineDateFieldChange: (value: TimelineDateField) => void;
    timelineColorBy: VisualizationColorBy;
    onTimelineColorByChange: (value: VisualizationColorBy) => void;
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    surface: {
        width: "300px",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        maxHeight: "70vh",
        overflowY: "auto",
        overflowX: "hidden",
        boxSizing: "border-box",
    },
    section: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        marginBottom: tokens.spacingVerticalS,
    },
    sectionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
    sliderRow: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    sliderValue: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorBrandForeground1,
        fontWeight: tokens.fontWeightSemibold,
        minWidth: "36px",
        textAlign: "right" as const,
    },
    hint: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    dropdown: {
        width: "100%",
        minWidth: 0,
    },
    triggerButton: {
        minWidth: "auto",
    },
    infoBox: {
        marginTop: tokens.spacingVerticalS,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusMedium,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
    },
    infoHeader: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        marginBottom: tokens.spacingVerticalXS,
    },
    infoHeading: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground1,
    },
    infoIcon: {
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
    },
    infoSectionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground1,
        marginTop: tokens.spacingVerticalS,
        marginBottom: "2px",
    },
    infoBody: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: "1.4",
    },
});

// =============================================
// Component
// =============================================

export const VisualizationSettings: React.FC<VisualizationSettingsProps> = ({
    viewMode,
    threshold,
    onThresholdChange,
    searchMode,
    onSearchModeChange,
    mapColorBy,
    onMapColorByChange,
    mapMinSimilarity,
    onMapMinSimilarityChange,
    treemapGroupBy,
    onTreemapGroupByChange,
    treemapShowLabels,
    onTreemapShowLabelsChange,
    timelineDateField,
    onTimelineDateFieldChange,
    timelineColorBy,
    onTimelineColorByChange,
}) => {
    const styles = useStyles();
    const [isOpen, setIsOpen] = useState(true);

    // Re-open settings panel whenever the view tab changes
    const prevViewMode = useRef(viewMode);
    useEffect(() => {
        if (viewMode !== prevViewMode.current) {
            prevViewMode.current = viewMode;
            setIsOpen(true);
        }
    }, [viewMode]);

    // --- Shared handlers ---
    const handleThresholdChange = useCallback(
        (_ev: unknown, data: { value: number }) => {
            onThresholdChange(data.value);
        },
        [onThresholdChange],
    );

    const handleSearchModeChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onSearchModeChange(data.optionValue as HybridMode);
            }
        },
        [onSearchModeChange],
    );

    // --- Map handlers ---
    const handleMapColorByChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onMapColorByChange(data.optionValue as VisualizationColorBy);
            }
        },
        [onMapColorByChange],
    );

    const handleMapSimilarityChange = useCallback(
        (_ev: unknown, data: { value: number }) => {
            onMapMinSimilarityChange(data.value);
        },
        [onMapMinSimilarityChange],
    );

    // --- Treemap handlers ---
    const handleTreemapGroupByChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onTreemapGroupByChange(data.optionValue as VisualizationColorBy);
            }
        },
        [onTreemapGroupByChange],
    );

    const handleTreemapShowLabelsChange = useCallback(
        (_ev: unknown, data: { checked: boolean | "mixed" }) => {
            onTreemapShowLabelsChange(data.checked === true);
        },
        [onTreemapShowLabelsChange],
    );

    // --- Timeline handlers ---
    const handleTimelineDateFieldChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onTimelineDateFieldChange(data.optionValue as TimelineDateField);
            }
        },
        [onTimelineDateFieldChange],
    );

    const handleTimelineColorByChange = useCallback(
        (_ev: unknown, data: { optionValue?: string }) => {
            if (data.optionValue) {
                onTimelineColorByChange(data.optionValue as VisualizationColorBy);
            }
        },
        [onTimelineColorByChange],
    );

    return (
        <Popover
            positioning="below-end"
            open={isOpen}
            onOpenChange={(_ev, data) => setIsOpen(data.open)}
        >
            <PopoverTrigger disableButtonEnhancement>
                <Button
                    className={styles.triggerButton}
                    appearance="subtle"
                    size="small"
                    icon={isOpen ? <ChevronUp20Regular /> : <ChevronDown20Regular />}
                    aria-label="Visualization settings"
                />
            </PopoverTrigger>

            <PopoverSurface className={styles.surface}>
                {/* === Shared: Relevance Threshold === */}
                <div className={styles.section}>
                    <div className={styles.sliderRow}>
                        <Label className={styles.sectionTitle}>
                            Relevance Threshold
                        </Label>
                        <Text className={styles.sliderValue}>
                            {threshold}%
                        </Text>
                    </div>
                    <Slider
                        min={0}
                        max={100}
                        value={threshold}
                        onChange={handleThresholdChange}
                        aria-label="Relevance threshold"
                    />
                    <Text className={styles.hint}>
                        Hide results below this score
                    </Text>
                </div>

                {/* === Shared: Search Mode === */}
                <div className={styles.section}>
                    <Label className={styles.sectionTitle}>Search Mode</Label>
                    <Dropdown
                        className={styles.dropdown}
                        size="small"
                        value={
                            SEARCH_MODE_OPTIONS.find(
                                (o) => o.value === searchMode,
                            )?.label ?? "Hybrid (RRF)"
                        }
                        selectedOptions={[searchMode]}
                        onOptionSelect={handleSearchModeChange}
                        aria-label="Search mode"
                    >
                        {SEARCH_MODE_OPTIONS.map((opt) => (
                            <Option key={opt.value} value={opt.value}>
                                {opt.label}
                            </Option>
                        ))}
                    </Dropdown>
                </div>

                {/* === Network-only settings === */}
                {viewMode === "map" && (
                    <>
                        <Divider />
                        <div className={styles.section}>
                            <Label className={styles.sectionTitle}>Cluster By</Label>
                            <Dropdown
                                className={styles.dropdown}
                                size="small"
                                value={
                                    CLUSTER_BY_OPTIONS.find(
                                        (o) => o.value === mapColorBy,
                                    )?.label ?? "Document Type"
                                }
                                selectedOptions={[mapColorBy]}
                                onOptionSelect={handleMapColorByChange}
                                aria-label="Cluster by category"
                            >
                                {CLUSTER_BY_OPTIONS.map((opt) => (
                                    <Option key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </Option>
                                ))}
                            </Dropdown>
                        </div>
                        <div className={styles.section}>
                            <div className={styles.sliderRow}>
                                <Label className={styles.sectionTitle}>
                                    Minimum Similarity
                                </Label>
                                <Text className={styles.sliderValue}>
                                    {mapMinSimilarity}%
                                </Text>
                            </div>
                            <Slider
                                min={0}
                                max={100}
                                step={5}
                                value={mapMinSimilarity}
                                onChange={handleMapSimilarityChange}
                                aria-label="Minimum similarity"
                            />
                            <Text className={styles.hint}>
                                Filter out low-scoring results from graph
                            </Text>
                        </div>
                    </>
                )}

                {/* === Treemap-only settings === */}
                {viewMode === "treemap" && (
                    <>
                        <Divider />
                        <div className={styles.section}>
                            <Label className={styles.sectionTitle}>Cluster By</Label>
                            <Dropdown
                                className={styles.dropdown}
                                size="small"
                                value={
                                    CLUSTER_BY_OPTIONS.find(
                                        (o) => o.value === treemapGroupBy,
                                    )?.label ?? "Entity Type"
                                }
                                selectedOptions={[treemapGroupBy]}
                                onOptionSelect={handleTreemapGroupByChange}
                                aria-label="Cluster by category"
                            >
                                {CLUSTER_BY_OPTIONS.map((opt) => (
                                    <Option key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </Option>
                                ))}
                            </Dropdown>
                        </div>
                        <div className={styles.section}>
                            <Checkbox
                                checked={treemapShowLabels}
                                onChange={handleTreemapShowLabelsChange}
                                label="Show Labels"
                            />
                            <Text className={styles.hint}>
                                Show result names on tiles
                            </Text>
                        </div>
                    </>
                )}

                {/* === Timeline-only settings === */}
                {viewMode === "timeline" && (
                    <>
                        <Divider />
                        <div className={styles.section}>
                            <Label className={styles.sectionTitle}>Date Field</Label>
                            <Dropdown
                                className={styles.dropdown}
                                size="small"
                                value={
                                    DATE_FIELD_OPTIONS.find(
                                        (o) => o.value === timelineDateField,
                                    )?.label ?? "Created Date"
                                }
                                selectedOptions={[timelineDateField]}
                                onOptionSelect={handleTimelineDateFieldChange}
                                aria-label="Date field"
                            >
                                {DATE_FIELD_OPTIONS.map((opt) => (
                                    <Option key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </Option>
                                ))}
                            </Dropdown>
                        </div>
                        <div className={styles.section}>
                            <Label className={styles.sectionTitle}>Cluster By</Label>
                            <Dropdown
                                className={styles.dropdown}
                                size="small"
                                value={
                                    CLUSTER_BY_OPTIONS.find(
                                        (o) => o.value === timelineColorBy,
                                    )?.label ?? "Document Type"
                                }
                                selectedOptions={[timelineColorBy]}
                                onOptionSelect={handleTimelineColorByChange}
                                aria-label="Cluster by category"
                            >
                                {CLUSTER_BY_OPTIONS.map((opt) => (
                                    <Option key={opt.value} value={opt.value}>
                                        {opt.label}
                                    </Option>
                                ))}
                            </Dropdown>
                        </div>
                    </>
                )}
                {/* === Contextual Help Info Box === */}
                <Divider />
                <div className={styles.infoBox}>
                    <div className={styles.infoHeader}>
                        <Text className={styles.infoHeading}>
                            {VIEW_HELP[viewMode].heading}
                        </Text>
                        <Info20Regular className={styles.infoIcon} />
                    </div>
                    {VIEW_HELP[viewMode].sections.map((sec) => (
                        <div key={sec.title}>
                            <Text block className={styles.infoSectionTitle}>
                                {sec.title}
                            </Text>
                            <Text block className={styles.infoBody}>
                                {sec.body}
                            </Text>
                        </div>
                    ))}
                </div>
            </PopoverSurface>
        </Popover>
    );
};

export default VisualizationSettings;
