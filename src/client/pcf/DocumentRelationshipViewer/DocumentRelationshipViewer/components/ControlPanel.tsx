/**
 * ControlPanel - Filter controls for document relationship visualization
 *
 * Provides controls for:
 * - Similarity threshold slider (50-95%, default 65%)
 * - Depth limit slider (1-3 levels, default 1)
 * - Max nodes per level slider (10-50, default 25)
 * - Document type filter checkboxes
 *
 * Follows:
 * - ADR-021: Fluent UI v9 exclusively, design tokens for all colors
 * - ADR-022: React 16 compatible APIs
 * - FR-05: Control Panel specification
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Slider,
    Checkbox,
    Label,
    Card,
    CardHeader,
    Text,
    Divider,
    Badge,
} from "@fluentui/react-components";
import {
    Settings20Regular,
    Filter20Regular,
} from "@fluentui/react-icons";

/**
 * Document types available for filtering
 */
export const DOCUMENT_TYPES = [
    { key: "pdf", label: "PDF Documents" },
    { key: "docx", label: "Word Documents" },
    { key: "xlsx", label: "Excel Spreadsheets" },
    { key: "pptx", label: "PowerPoint Presentations" },
    { key: "txt", label: "Text Files" },
    { key: "other", label: "Other Types" },
] as const;

export type DocumentTypeKey = typeof DOCUMENT_TYPES[number]["key"];

/**
 * Filter settings managed by the control panel
 */
export interface FilterSettings {
    /** Similarity threshold (0.50 - 0.95), default 0.65 */
    similarityThreshold: number;
    /** Depth limit (1-3 levels), default 1 */
    depthLimit: number;
    /** Max nodes per level (10-50), default 25 */
    maxNodesPerLevel: number;
    /** Enabled document types */
    documentTypes: DocumentTypeKey[];
}

/**
 * Default filter settings per FR-05
 */
export const DEFAULT_FILTER_SETTINGS: FilterSettings = {
    similarityThreshold: 0.65,
    depthLimit: 1,
    maxNodesPerLevel: 25,
    documentTypes: ["pdf", "docx", "xlsx", "pptx", "txt", "other"],
};

/**
 * Props for ControlPanel component
 */
export interface ControlPanelProps {
    /** Current filter settings */
    settings: FilterSettings;
    /** Callback when settings change */
    onSettingsChange: (settings: FilterSettings) => void;
    /** Whether the panel is collapsed */
    collapsed?: boolean;
    /** Callback to toggle collapsed state */
    onToggleCollapse?: () => void;
}

/**
 * Styles using Fluent UI v9 design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
    container: {
        width: "280px",
        maxHeight: "100%",
        overflowY: "auto",
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow4,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    },
    headerIcon: {
        color: tokens.colorBrandForeground1,
    },
    headerTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    section: {
        padding: tokens.spacingVerticalM,
    },
    sectionTitle: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        marginBottom: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground2,
    },
    sliderContainer: {
        marginBottom: tokens.spacingVerticalM,
    },
    sliderLabel: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        marginBottom: tokens.spacingVerticalXS,
    },
    sliderValue: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorBrandForeground1,
    },
    slider: {
        width: "100%",
    },
    sliderHint: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginTop: tokens.spacingVerticalXXS,
    },
    checkboxGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
    },
    divider: {
        margin: `${tokens.spacingVerticalS} 0`,
    },
    activeFiltersBadge: {
        marginLeft: "auto",
    },
});

/**
 * ControlPanel component - Provides filter controls for the visualization
 */
export const ControlPanel: React.FC<ControlPanelProps> = ({
    settings,
    onSettingsChange,
}) => {
    const styles = useStyles();

    // Count active filters (non-default values)
    const activeFiltersCount = React.useMemo(() => {
        let count = 0;
        if (settings.similarityThreshold !== DEFAULT_FILTER_SETTINGS.similarityThreshold) count++;
        if (settings.depthLimit !== DEFAULT_FILTER_SETTINGS.depthLimit) count++;
        if (settings.maxNodesPerLevel !== DEFAULT_FILTER_SETTINGS.maxNodesPerLevel) count++;
        if (settings.documentTypes.length !== DOCUMENT_TYPES.length) count++;
        return count;
    }, [settings]);

    // Handle similarity threshold change
    const handleSimilarityChange = React.useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({
                ...settings,
                similarityThreshold: data.value / 100, // Convert from percentage
            });
        },
        [settings, onSettingsChange]
    );

    // Handle depth limit change
    const handleDepthChange = React.useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({
                ...settings,
                depthLimit: data.value,
            });
        },
        [settings, onSettingsChange]
    );

    // Handle max nodes change
    const handleMaxNodesChange = React.useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({
                ...settings,
                maxNodesPerLevel: data.value,
            });
        },
        [settings, onSettingsChange]
    );

    // Handle document type checkbox change
    const handleDocumentTypeChange = React.useCallback(
        (typeKey: DocumentTypeKey, checked: boolean) => {
            const newTypes = checked
                ? [...settings.documentTypes, typeKey]
                : settings.documentTypes.filter((t) => t !== typeKey);
            onSettingsChange({
                ...settings,
                documentTypes: newTypes,
            });
        },
        [settings, onSettingsChange]
    );

    // Format similarity value for display
    const formatSimilarity = (value: number): string => {
        return `${Math.round(value * 100)}%`;
    };

    return (
        <Card className={styles.container}>
            {/* Header */}
            <div className={styles.header}>
                <Settings20Regular className={styles.headerIcon} />
                <Text className={styles.headerTitle}>Visualization Settings</Text>
                {activeFiltersCount > 0 && (
                    <Badge
                        className={styles.activeFiltersBadge}
                        appearance="filled"
                        color="brand"
                        size="small"
                    >
                        {activeFiltersCount} active
                    </Badge>
                )}
            </div>

            {/* Similarity Threshold Section */}
            <div className={styles.section}>
                <div className={styles.sectionTitle}>
                    <Text size={200} weight="semibold">
                        Similarity Settings
                    </Text>
                </div>

                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="similarity-slider">Minimum Similarity</Label>
                        <Text className={styles.sliderValue}>
                            {formatSimilarity(settings.similarityThreshold)}
                        </Text>
                    </div>
                    <Slider
                        id="similarity-slider"
                        className={styles.slider}
                        min={50}
                        max={95}
                        step={5}
                        value={Math.round(settings.similarityThreshold * 100)}
                        onChange={handleSimilarityChange}
                    />
                    <Text className={styles.sliderHint}>
                        Higher values show more similar documents only
                    </Text>
                </div>
            </div>

            <Divider className={styles.divider} />

            {/* Graph Settings Section */}
            <div className={styles.section}>
                <div className={styles.sectionTitle}>
                    <Text size={200} weight="semibold">
                        Graph Settings
                    </Text>
                </div>

                {/* Depth Limit */}
                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="depth-slider">Depth Limit</Label>
                        <Text className={styles.sliderValue}>
                            {settings.depthLimit} {settings.depthLimit === 1 ? "level" : "levels"}
                        </Text>
                    </div>
                    <Slider
                        id="depth-slider"
                        className={styles.slider}
                        min={1}
                        max={3}
                        step={1}
                        value={settings.depthLimit}
                        onChange={handleDepthChange}
                    />
                    <Text className={styles.sliderHint}>
                        How many levels of related documents to show
                    </Text>
                </div>

                {/* Max Nodes */}
                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="maxnodes-slider">Max Nodes per Level</Label>
                        <Text className={styles.sliderValue}>
                            {settings.maxNodesPerLevel}
                        </Text>
                    </div>
                    <Slider
                        id="maxnodes-slider"
                        className={styles.slider}
                        min={10}
                        max={50}
                        step={5}
                        value={settings.maxNodesPerLevel}
                        onChange={handleMaxNodesChange}
                    />
                    <Text className={styles.sliderHint}>
                        Maximum documents shown at each depth level
                    </Text>
                </div>
            </div>

            <Divider className={styles.divider} />

            {/* Document Type Filters Section */}
            <div className={styles.section}>
                <div className={styles.sectionTitle}>
                    <Filter20Regular />
                    <Text size={200} weight="semibold">
                        Document Types
                    </Text>
                </div>

                <div className={styles.checkboxGroup}>
                    {DOCUMENT_TYPES.map((type) => (
                        <Checkbox
                            key={type.key}
                            label={type.label}
                            checked={settings.documentTypes.includes(type.key)}
                            onChange={(_ev, data) =>
                                handleDocumentTypeChange(type.key, data.checked === true)
                            }
                        />
                    ))}
                </div>
            </div>
        </Card>
    );
};

export default ControlPanel;
