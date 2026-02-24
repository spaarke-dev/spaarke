/**
 * ControlPanel — Filter controls for document relationship visualization
 * Copied from PCF version with React import cleanup (React 19 named imports).
 */

import React, { useMemo, useCallback } from "react";
import {
    makeStyles, tokens, Slider, Checkbox, Label, Card,
    Text, Divider, Badge,
} from "@fluentui/react-components";
import { Settings20Regular, Filter20Regular } from "@fluentui/react-icons";

export const DOCUMENT_TYPES = [
    { key: "pdf", label: "PDF Documents" },
    { key: "docx", label: "Word Documents" },
    { key: "xlsx", label: "Excel Spreadsheets" },
    { key: "pptx", label: "PowerPoint Presentations" },
    { key: "txt", label: "Text Files" },
    { key: "other", label: "Other Types" },
] as const;

export type DocumentTypeKey = typeof DOCUMENT_TYPES[number]["key"];

export interface FilterSettings {
    similarityThreshold: number;
    depthLimit: number;
    maxNodesPerLevel: number;
    documentTypes: DocumentTypeKey[];
}

export const DEFAULT_FILTER_SETTINGS: FilterSettings = {
    similarityThreshold: 0.65,
    depthLimit: 1,
    maxNodesPerLevel: 25,
    documentTypes: ["pdf", "docx", "xlsx", "pptx", "txt", "other"],
};

export interface ControlPanelProps {
    settings: FilterSettings;
    onSettingsChange: (settings: FilterSettings) => void;
}

const useStyles = makeStyles({
    container: {
        width: "280px", maxHeight: "100%", overflowY: "auto",
        backgroundColor: tokens.colorNeutralBackground1,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        borderRadius: tokens.borderRadiusMedium, boxShadow: tokens.shadow4,
    },
    header: { display: "flex", alignItems: "center", gap: tokens.spacingHorizontalS, padding: tokens.spacingVerticalM, borderBottom: `1px solid ${tokens.colorNeutralStroke2}` },
    headerIcon: { color: tokens.colorBrandForeground1 },
    headerTitle: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground1 },
    section: { padding: tokens.spacingVerticalM },
    sectionTitle: { display: "flex", alignItems: "center", gap: tokens.spacingHorizontalXS, marginBottom: tokens.spacingVerticalS, color: tokens.colorNeutralForeground2 },
    sliderContainer: { marginBottom: tokens.spacingVerticalM },
    sliderLabel: { display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: tokens.spacingVerticalXS },
    sliderValue: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorBrandForeground1 },
    slider: { width: "100%" },
    sliderHint: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3, marginTop: tokens.spacingVerticalXXS },
    checkboxGroup: { display: "flex", flexDirection: "column", gap: tokens.spacingVerticalXS },
    divider: { margin: `${tokens.spacingVerticalS} 0` },
    activeFiltersBadge: { marginLeft: "auto" },
});

export const ControlPanel: React.FC<ControlPanelProps> = ({ settings, onSettingsChange }) => {
    const styles = useStyles();

    const activeFiltersCount = useMemo(() => {
        let count = 0;
        if (settings.similarityThreshold !== DEFAULT_FILTER_SETTINGS.similarityThreshold) count++;
        if (settings.depthLimit !== DEFAULT_FILTER_SETTINGS.depthLimit) count++;
        if (settings.maxNodesPerLevel !== DEFAULT_FILTER_SETTINGS.maxNodesPerLevel) count++;
        if (settings.documentTypes.length !== DOCUMENT_TYPES.length) count++;
        return count;
    }, [settings]);

    const handleSimilarityChange = useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({ ...settings, similarityThreshold: data.value / 100 });
        }, [settings, onSettingsChange]
    );

    const handleDepthChange = useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({ ...settings, depthLimit: data.value });
        }, [settings, onSettingsChange]
    );

    const handleMaxNodesChange = useCallback(
        (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
            onSettingsChange({ ...settings, maxNodesPerLevel: data.value });
        }, [settings, onSettingsChange]
    );

    const handleDocumentTypeChange = useCallback(
        (typeKey: DocumentTypeKey, checked: boolean) => {
            const newTypes = checked
                ? [...settings.documentTypes, typeKey]
                : settings.documentTypes.filter((t) => t !== typeKey);
            onSettingsChange({ ...settings, documentTypes: newTypes });
        }, [settings, onSettingsChange]
    );

    return (
        <Card className={styles.container}>
            <div className={styles.header}>
                <Settings20Regular className={styles.headerIcon} />
                <Text className={styles.headerTitle}>Visualization Settings</Text>
                {activeFiltersCount > 0 && (
                    <Badge className={styles.activeFiltersBadge} appearance="filled" color="brand" size="small">
                        {activeFiltersCount} active
                    </Badge>
                )}
            </div>

            <div className={styles.section}>
                <div className={styles.sectionTitle}><Text size={200} weight="semibold">Similarity Settings</Text></div>
                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="similarity-slider">Minimum Similarity</Label>
                        <Text className={styles.sliderValue}>{Math.round(settings.similarityThreshold * 100)}%</Text>
                    </div>
                    <Slider id="similarity-slider" className={styles.slider} min={50} max={95} step={5} value={Math.round(settings.similarityThreshold * 100)} onChange={handleSimilarityChange} />
                    <Text className={styles.sliderHint}>Higher values show more similar documents only</Text>
                </div>
            </div>

            <Divider className={styles.divider} />

            <div className={styles.section}>
                <div className={styles.sectionTitle}><Text size={200} weight="semibold">Graph Settings</Text></div>
                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="depth-slider">Depth Limit</Label>
                        <Text className={styles.sliderValue}>{settings.depthLimit} {settings.depthLimit === 1 ? "level" : "levels"}</Text>
                    </div>
                    <Slider id="depth-slider" className={styles.slider} min={1} max={3} step={1} value={settings.depthLimit} onChange={handleDepthChange} />
                    <Text className={styles.sliderHint}>How many levels of related documents to show</Text>
                </div>
                <div className={styles.sliderContainer}>
                    <div className={styles.sliderLabel}>
                        <Label htmlFor="maxnodes-slider">Max Nodes per Level</Label>
                        <Text className={styles.sliderValue}>{settings.maxNodesPerLevel}</Text>
                    </div>
                    <Slider id="maxnodes-slider" className={styles.slider} min={10} max={50} step={5} value={settings.maxNodesPerLevel} onChange={handleMaxNodesChange} />
                    <Text className={styles.sliderHint}>Maximum documents shown at each depth level</Text>
                </div>
            </div>

            <Divider className={styles.divider} />

            <div className={styles.section}>
                <div className={styles.sectionTitle}><Filter20Regular /><Text size={200} weight="semibold">Document Types</Text></div>
                <div className={styles.checkboxGroup}>
                    {DOCUMENT_TYPES.map((type) => (
                        <Checkbox
                            key={type.key}
                            label={type.label}
                            checked={settings.documentTypes.includes(type.key)}
                            onChange={(_ev, data) => handleDocumentTypeChange(type.key, data.checked === true)}
                        />
                    ))}
                </div>
            </div>
        </Card>
    );
};

export default ControlPanel;
