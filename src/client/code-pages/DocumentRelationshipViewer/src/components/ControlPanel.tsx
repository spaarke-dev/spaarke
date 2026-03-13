/**
 * ControlPanel — Visualization Settings panel (right side)
 *
 * Condensed layout:
 *   - Title "Visualization Settings" (no gear icon)
 *   - Collapsible "How It Works" info section (collapsed by default)
 *   - "Minimum Similarity" with % and slider (no section title)
 *   - "Levels" with number and slider (only shown in graph view)
 *   - "Nodes per Level" with number and slider
 *   - Document Types checkboxes
 *   - Relationship Types checkboxes
 */

import React, { useMemo, useCallback, useState } from "react";
import {
  makeStyles,
  tokens,
  Slider,
  Checkbox,
  Label,
  Card,
  Text,
  Divider,
  Link,
} from "@fluentui/react-components";
import {
  Info20Regular,
  ChevronDown20Regular,
  ChevronUp20Regular,
} from "@fluentui/react-icons";

export const DOCUMENT_TYPES = [
  { key: "pdf", label: "PDF Documents" },
  { key: "docx", label: "Word Documents" },
  { key: "xlsx", label: "Excel Spreadsheets" },
  { key: "pptx", label: "PowerPoint Presentations" },
  { key: "txt", label: "Text Files" },
  { key: "other", label: "Other Types" },
] as const;

export type DocumentTypeKey = (typeof DOCUMENT_TYPES)[number]["key"];

export const RELATIONSHIP_TYPES = [
  { key: "semantic", label: "Semantic Similarity" },
  { key: "same_matter", label: "Same Matter" },
  { key: "same_project", label: "Same Project" },
  { key: "same_email", label: "Same Email" },
  { key: "same_thread", label: "Same Thread" },
  { key: "same_invoice", label: "Same Invoice" },
] as const;

export type RelationshipTypeKey = (typeof RELATIONSHIP_TYPES)[number]["key"];

export interface FilterSettings {
  similarityThreshold: number;
  depthLimit: number;
  maxNodesPerLevel: number;
  documentTypes: DocumentTypeKey[];
  relationshipTypes: RelationshipTypeKey[];
}

export const DEFAULT_FILTER_SETTINGS: FilterSettings = {
  similarityThreshold: 0.65,
  depthLimit: 1,
  maxNodesPerLevel: 25,
  documentTypes: ["pdf", "docx", "xlsx", "pptx", "txt", "other"],
  relationshipTypes: [
    "semantic",
    "same_matter",
    "same_project",
    "same_email",
    "same_thread",
    "same_invoice",
  ],
};

export interface ControlPanelProps {
  settings: FilterSettings;
  onSettingsChange: (settings: FilterSettings) => void;
  /** Current view mode — graph-specific settings only show when viewMode === "graph" */
  viewMode?: string;
}

const useStyles = makeStyles({
  container: {
    width: "260px",
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
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  section: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
  },
  sliderRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: tokens.spacingVerticalXXS,
  },
  sliderValue: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  slider: { width: "100%" },
  sliderBlock: { marginBottom: tokens.spacingVerticalS },
  checkboxGroup: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  checkboxLabel: { fontSize: tokens.fontSizeBase200 },
  divider: { margin: `${tokens.spacingVerticalXS} 0` },
  infoSection: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
    margin: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
  },
  infoHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    cursor: "pointer",
  },
  infoContent: {
    marginTop: tokens.spacingVerticalS,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  infoTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  infoText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
  },
  sectionLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXS,
  },
  sectionHeader: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center",
    marginBottom: tokens.spacingVerticalXS,
  },
  selectLinks: {
    display: "flex",
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase100,
  },
  selectLink: {
    fontSize: tokens.fontSizeBase100,
    cursor: "pointer",
  },
});

export const ControlPanel: React.FC<ControlPanelProps> = ({
  settings,
  onSettingsChange,
  viewMode,
}) => {
  const styles = useStyles();
  const [showInfo, setShowInfo] = useState(false);

  const handleSimilarityChange = useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
      onSettingsChange({ ...settings, similarityThreshold: data.value / 100 });
    },
    [settings, onSettingsChange],
  );

  const handleDepthChange = useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
      onSettingsChange({ ...settings, depthLimit: data.value });
    },
    [settings, onSettingsChange],
  );

  const handleMaxNodesChange = useCallback(
    (_ev: React.ChangeEvent<HTMLInputElement>, data: { value: number }) => {
      onSettingsChange({ ...settings, maxNodesPerLevel: data.value });
    },
    [settings, onSettingsChange],
  );

  const handleDocumentTypeChange = useCallback(
    (typeKey: DocumentTypeKey, checked: boolean) => {
      const newTypes = checked
        ? [...settings.documentTypes, typeKey]
        : settings.documentTypes.filter((t) => t !== typeKey);
      onSettingsChange({ ...settings, documentTypes: newTypes });
    },
    [settings, onSettingsChange],
  );

  const handleRelationshipTypeChange = useCallback(
    (typeKey: RelationshipTypeKey, checked: boolean) => {
      const newTypes = checked
        ? [...settings.relationshipTypes, typeKey]
        : settings.relationshipTypes.filter((t) => t !== typeKey);
      onSettingsChange({ ...settings, relationshipTypes: newTypes });
    },
    [settings, onSettingsChange],
  );

  return (
    <Card className={styles.container}>
      {/* Header — no gear icon */}
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={300}>
          Visualization Settings
        </Text>
      </div>

      {/* How It Works — collapsible, collapsed by default */}
      <div className={styles.infoSection}>
        <div
          className={styles.infoHeader}
          onClick={() => setShowInfo(!showInfo)}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: tokens.spacingHorizontalXS,
            }}
          >
            <Info20Regular />
            <Text size={200} weight="semibold">
              How It Works
            </Text>
          </div>
          {showInfo ? <ChevronUp20Regular /> : <ChevronDown20Regular />}
        </div>
        {showInfo && (
          <div className={styles.infoContent}>
            <div>
              <Text className={styles.infoTitle} size={200}>
                Similarity Score
              </Text>
              <Text className={styles.infoText}>
                The percentage indicates how closely a document's content
                matches the source. Higher = more relevant.
              </Text>
            </div>
            <div>
              <Text className={styles.infoTitle} size={200}>
                Relationship Types
              </Text>
              <Text className={styles.infoText}>
                Semantic: AI-detected content similarity. Same Matter/Project:
                shared parent record. Same Email/Thread: communication linkage.
              </Text>
            </div>
            <div>
              <Text className={styles.infoTitle} size={200}>
                Depth Levels
              </Text>
              <Text className={styles.infoText}>
                Level 1 shows directly related documents. Level 2+ shows
                documents related to those, expanding discovery.
              </Text>
            </div>
          </div>
        )}
      </div>

      {/* Minimum Similarity — just label, %, slider */}
      <div className={styles.section}>
        <div className={styles.sliderBlock}>
          <div className={styles.sliderRow}>
            <Label htmlFor="similarity-slider" size="small">
              Minimum Similarity
            </Label>
            <Text className={styles.sliderValue}>
              {Math.round(settings.similarityThreshold * 100)}%
            </Text>
          </div>
          <Slider
            id="similarity-slider"
            className={styles.slider}
            min={50}
            max={95}
            step={5}
            size="small"
            value={Math.round(settings.similarityThreshold * 100)}
            onChange={handleSimilarityChange}
          />
        </div>

        {/* Levels — only show on graph view */}
        {viewMode === "graph" && (
          <div className={styles.sliderBlock}>
            <div className={styles.sliderRow}>
              <Label htmlFor="depth-slider" size="small">
                Levels
              </Label>
              <Text className={styles.sliderValue}>{settings.depthLimit}</Text>
            </div>
            <Slider
              id="depth-slider"
              className={styles.slider}
              min={1}
              max={3}
              step={1}
              size="small"
              value={settings.depthLimit}
              onChange={handleDepthChange}
            />
          </div>
        )}

        {/* Nodes per Level */}
        <div className={styles.sliderBlock}>
          <div className={styles.sliderRow}>
            <Label htmlFor="maxnodes-slider" size="small">
              Nodes per Level
            </Label>
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
            size="small"
            value={settings.maxNodesPerLevel}
            onChange={handleMaxNodesChange}
          />
        </div>
      </div>

      <Divider className={styles.divider} />

      {/* Document Types */}
      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <Text className={styles.sectionLabel}>Document Types</Text>
          <div className={styles.selectLinks}>
            <Link
              className={styles.selectLink}
              onClick={() =>
                onSettingsChange({
                  ...settings,
                  documentTypes: DOCUMENT_TYPES.map(
                    (t) => t.key,
                  ) as unknown as DocumentTypeKey[],
                })
              }
            >
              All
            </Link>
            <Text size={100}>|</Text>
            <Link
              className={styles.selectLink}
              onClick={() =>
                onSettingsChange({ ...settings, documentTypes: [] })
              }
            >
              Clear
            </Link>
          </div>
        </div>
        <div className={styles.checkboxGroup}>
          {DOCUMENT_TYPES.map((type) => (
            <Checkbox
              key={type.key}
              label={type.label}
              size="medium"
              checked={settings.documentTypes.includes(type.key)}
              onChange={(_ev, data) =>
                handleDocumentTypeChange(type.key, data.checked === true)
              }
            />
          ))}
        </div>
      </div>

      <Divider className={styles.divider} />

      {/* Relationship Types */}
      <div className={styles.section}>
        <div className={styles.sectionHeader}>
          <Text className={styles.sectionLabel}>Relationship Types</Text>
          <div className={styles.selectLinks}>
            <Link
              className={styles.selectLink}
              onClick={() =>
                onSettingsChange({
                  ...settings,
                  relationshipTypes: RELATIONSHIP_TYPES.map(
                    (t) => t.key,
                  ) as unknown as RelationshipTypeKey[],
                })
              }
            >
              All
            </Link>
            <Text size={100}>|</Text>
            <Link
              className={styles.selectLink}
              onClick={() =>
                onSettingsChange({ ...settings, relationshipTypes: [] })
              }
            >
              Clear
            </Link>
          </div>
        </div>
        <div className={styles.checkboxGroup}>
          {RELATIONSHIP_TYPES.map((type) => (
            <Checkbox
              key={type.key}
              label={type.label}
              size="medium"
              checked={settings.relationshipTypes.includes(type.key)}
              onChange={(_ev, data) =>
                handleRelationshipTypeChange(type.key, data.checked === true)
              }
            />
          ))}
        </div>
      </div>
    </Card>
  );
};

export default ControlPanel;
