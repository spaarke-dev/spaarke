/**
 * CustomPropertyEditor — tab component for managing custom properties on a container.
 *
 * Custom properties are key-value pairs with an optional isSearchable flag stored on
 * SharePoint Embedded containers via the Graph API. When isSearchable is true the
 * property is indexed and can be used in search queries to find containers.
 *
 * Behaviour:
 *  - Loads existing properties lazily when the tab is first rendered.
 *  - Supports adding new properties, editing existing ones, and deleting properties.
 *  - Changes are accumulated locally (optimistic local state) and persisted in a
 *    single PUT call when the user clicks Save All.
 *  - A "dirty" badge on the Save button informs the user there are unsaved changes.
 *
 * Data model:
 *  - Properties are stored as `Record<string, ContainerCustomProperty>` where the key
 *    is the property name and the value is `{ value: string; isSearchable?: boolean }`.
 *  - The PUT endpoint replaces the entire property map — deleted properties are simply
 *    omitted from the request body.
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens — no hard-coded colors.
 * ADR-012: Uses @spaarke/ui-components where applicable; Fluent v9 exclusively.
 * ADR-006: Code Page component — React 18 patterns, no PCF / ComponentFramework deps.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Button,
  Spinner,
  MessageBar,
  MessageBarBody,
  Badge,
  Table,
  TableHeader,
  TableHeaderCell,
  TableBody,
  TableRow,
  TableCell,
  TableCellLayout,
  Tooltip,
  Divider,
} from "@fluentui/react-components";
import {
  AddCircle20Regular,
  ArrowClockwise20Regular,
  Delete20Regular,
  Edit20Regular,
  Save20Regular,
  Tag20Regular,
} from "@fluentui/react-icons";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient, ApiError } from "../../services/speApiClient";
import type { ContainerCustomProperty } from "../../types/spe";
import { AddEditPropertyDialog } from "./AddEditPropertyDialog";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface CustomPropertyEditorProps {
  /** Container ID to manage properties for */
  containerId: string;
  /** Whether this tab is currently active (controls lazy load trigger) */
  isActive: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },

  toolbar: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexWrap: "wrap",
  },

  toolbarLeft: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },

  toolbarRight: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
  },

  /** Loading / error / empty state area */
  feedback: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    ...shorthands.gap(tokens.spacingVerticalM),
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground2,
  },

  /** Empty state when no properties defined */
  emptyState: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingVerticalS),
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
  },

  emptyIcon: {
    fontSize: "32px",
    opacity: 0.4,
    color: tokens.colorNeutralForeground3,
  },

  /** Properties table */
  table: {
    width: "100%",
  },

  /** Actions cell — contains edit + delete buttons */
  actionsCell: {
    width: "72px",
    textAlign: "right",
    paddingRight: tokens.spacingHorizontalXS,
  },

  actionsCellContent: {
    display: "flex",
    justifyContent: "flex-end",
    ...shorthands.gap(tokens.spacingHorizontalXXS),
  },

  /** Monospace key display */
  keyText: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground1,
    wordBreak: "break-all",
  },

  /** Unsaved change indicator badge on Save button */
  dirtyBadge: {
    position: "relative",
  },

  /** Info strip at the bottom explaining save behaviour */
  saveHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  saveArea: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.gap(tokens.spacingHorizontalM),
    paddingTop: tokens.spacingVerticalS,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke2,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * CustomPropertyEditor — self-contained editor tab for container custom properties.
 *
 * Intended to be rendered inside ContainerDetail's "Custom Properties" tab.
 * Manages its own load/save state so ContainerDetail stays clean.
 */
export const CustomPropertyEditor: React.FC<CustomPropertyEditorProps> = ({
  containerId,
  isActive,
}) => {
  const styles = useStyles();
  const { selectedConfig } = useBuContext();

  // ── Server state ───────────────────────────────────────────────────────────

  /** Properties as loaded from the API — source of truth for reset */
  const [serverProperties, setServerProperties] = React.useState<
    Record<string, ContainerCustomProperty>
  >({});
  const [loading, setLoading] = React.useState(false);
  const [loadError, setLoadError] = React.useState<string | null>(null);
  const [loaded, setLoaded] = React.useState(false);

  // ── Local (draft) state ───────────────────────────────────────────────────

  /** Local working copy — accumulates adds/edits/deletes before save */
  const [localProperties, setLocalProperties] = React.useState<
    Record<string, ContainerCustomProperty>
  >({});

  /** Whether local state differs from server state */
  const isDirty = React.useMemo(() => {
    const localKeys = Object.keys(localProperties).sort();
    const serverKeys = Object.keys(serverProperties).sort();
    if (localKeys.join(",") !== serverKeys.join(",")) return true;
    for (const key of localKeys) {
      const local = localProperties[key];
      const server = serverProperties[key];
      if (local.value !== server.value || (local.isSearchable ?? false) !== (server.isSearchable ?? false))
        return true;
    }
    return false;
  }, [localProperties, serverProperties]);

  // ── Save state ─────────────────────────────────────────────────────────────

  const [saving, setSaving] = React.useState(false);
  const [saveError, setSaveError] = React.useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = React.useState(false);

  // ── Dialog state ───────────────────────────────────────────────────────────

  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [editingKey, setEditingKey] = React.useState<string | null>(null);
  const [editingValue, setEditingValue] = React.useState<ContainerCustomProperty | null>(null);

  // ── Load ───────────────────────────────────────────────────────────────────

  const loadProperties = React.useCallback(async () => {
    if (!selectedConfig || !containerId) return;
    setLoading(true);
    setLoadError(null);
    setSaveError(null);
    setSaveSuccess(false);
    try {
      const data = await speApiClient.containers.listCustomProperties(
        containerId,
        selectedConfig.id,
      );
      setServerProperties(data);
      setLocalProperties(data);
      setLoaded(true);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Failed to load custom properties.";
      setLoadError(message);
    } finally {
      setLoading(false);
    }
  }, [containerId, selectedConfig]);

  // Lazy load: trigger when the tab becomes active for the first time
  React.useEffect(() => {
    if (isActive && !loaded && !loading) {
      void loadProperties();
    }
  }, [isActive, loaded, loading, loadProperties]);

  // Reset when containerId changes (new container opened)
  React.useEffect(() => {
    setServerProperties({});
    setLocalProperties({});
    setLoaded(false);
    setLoadError(null);
    setSaveError(null);
    setSaveSuccess(false);
  }, [containerId]);

  // ── Dialog handlers ───────────────────────────────────────────────────────

  const handleAddClick = React.useCallback(() => {
    setEditingKey(null);
    setEditingValue(null);
    setDialogOpen(true);
  }, []);

  const handleEditClick = React.useCallback(
    (key: string) => {
      setEditingKey(key);
      setEditingValue(localProperties[key] ?? null);
      setDialogOpen(true);
    },
    [localProperties],
  );

  const handleDialogDismiss = React.useCallback(() => {
    setDialogOpen(false);
    setEditingKey(null);
    setEditingValue(null);
  }, []);

  const handleDialogConfirm = React.useCallback(
    (key: string, value: ContainerCustomProperty) => {
      setLocalProperties((prev) => ({ ...prev, [key]: value }));
      setSaveError(null);
      setSaveSuccess(false);
      setDialogOpen(false);
      setEditingKey(null);
      setEditingValue(null);
    },
    [],
  );

  const handleDeleteClick = React.useCallback((key: string) => {
    setLocalProperties((prev) => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
    setSaveError(null);
    setSaveSuccess(false);
  }, []);

  // ── Save ───────────────────────────────────────────────────────────────────

  const handleSave = React.useCallback(async () => {
    if (!selectedConfig || !containerId) return;
    setSaving(true);
    setSaveError(null);
    setSaveSuccess(false);
    try {
      const saved = await speApiClient.containers.updateCustomProperties(
        containerId,
        selectedConfig.id,
        localProperties,
      );
      setServerProperties(saved);
      setLocalProperties(saved);
      setSaveSuccess(true);
      // Clear success indicator after a few seconds
      setTimeout(() => setSaveSuccess(false), 3000);
    } catch (err) {
      const message =
        err instanceof ApiError ? err.message : "Failed to save custom properties.";
      setSaveError(message);
    } finally {
      setSaving(false);
    }
  }, [containerId, selectedConfig, localProperties]);

  // ── Discard local changes ─────────────────────────────────────────────────

  const handleDiscard = React.useCallback(() => {
    setLocalProperties(serverProperties);
    setSaveError(null);
    setSaveSuccess(false);
  }, [serverProperties]);

  // ── Render: loading ───────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className={`${styles.root} ${styles.feedback}`}>
        <Spinner size="small" label="Loading custom properties…" />
      </div>
    );
  }

  // ── Render: load error ────────────────────────────────────────────────────

  if (loadError) {
    return (
      <div className={`${styles.root} ${styles.feedback}`}>
        <MessageBar intent="error">
          <MessageBarBody>{loadError}</MessageBarBody>
        </MessageBar>
        <Button
          size="small"
          appearance="secondary"
          icon={<ArrowClockwise20Regular />}
          onClick={() => void loadProperties()}
        >
          Retry
        </Button>
      </div>
    );
  }

  // ── Render: editor ────────────────────────────────────────────────────────

  const propertyEntries = Object.entries(localProperties);

  return (
    <div className={styles.root}>
      {/* Toolbar */}
      <div className={styles.toolbar}>
        <div className={styles.toolbarLeft}>
          <Text size={300} weight="semibold">
            Custom Properties
          </Text>
          {propertyEntries.length > 0 && (
            <Badge appearance="filled" color="brand" size="small">
              {propertyEntries.length}
            </Badge>
          )}
          {isDirty && (
            <Badge appearance="tint" color="warning" size="small">
              Unsaved changes
            </Badge>
          )}
        </div>

        <div className={styles.toolbarRight}>
          <Button
            size="small"
            appearance="primary"
            icon={<AddCircle20Regular />}
            onClick={handleAddClick}
          >
            Add Property
          </Button>
        </div>
      </div>

      <Divider />

      {/* Save error */}
      {saveError && (
        <MessageBar intent="error">
          <MessageBarBody>{saveError}</MessageBarBody>
        </MessageBar>
      )}

      {/* Save success */}
      {saveSuccess && (
        <MessageBar intent="success">
          <MessageBarBody>Custom properties saved successfully.</MessageBarBody>
        </MessageBar>
      )}

      {/* Empty state */}
      {propertyEntries.length === 0 ? (
        <div className={styles.emptyState}>
          <Tag20Regular className={styles.emptyIcon} />
          <Text size={300}>No custom properties defined</Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Use &quot;Add Property&quot; to tag this container with metadata.
          </Text>
        </div>
      ) : (
        /* Properties table */
        <Table className={styles.table} size="small" aria-label="Container custom properties">
          <TableHeader>
            <TableRow>
              <TableHeaderCell>Name</TableHeaderCell>
              <TableHeaderCell>Value</TableHeaderCell>
              <TableHeaderCell>Searchable</TableHeaderCell>
              <TableHeaderCell style={{ width: "72px" }} />
            </TableRow>
          </TableHeader>
          <TableBody>
            {propertyEntries.map(([key, prop]) => (
              <TableRow key={key}>
                {/* Key */}
                <TableCell>
                  <TableCellLayout>
                    <span className={styles.keyText}>{key}</span>
                  </TableCellLayout>
                </TableCell>

                {/* Value */}
                <TableCell>
                  <TableCellLayout>
                    <Text size={200} style={{ wordBreak: "break-word" }}>
                      {prop.value}
                    </Text>
                  </TableCellLayout>
                </TableCell>

                {/* Searchable */}
                <TableCell>
                  <TableCellLayout>
                    {prop.isSearchable ? (
                      <Badge appearance="tint" color="success" size="small">
                        Yes
                      </Badge>
                    ) : (
                      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                        No
                      </Text>
                    )}
                  </TableCellLayout>
                </TableCell>

                {/* Actions */}
                <TableCell className={styles.actionsCell}>
                  <div className={styles.actionsCellContent}>
                    <Tooltip content="Edit property" relationship="label" positioning="above">
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<Edit20Regular />}
                        aria-label={`Edit property ${key}`}
                        onClick={() => handleEditClick(key)}
                      />
                    </Tooltip>
                    <Tooltip content="Delete property" relationship="label" positioning="above">
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<Delete20Regular />}
                        aria-label={`Delete property ${key}`}
                        onClick={() => handleDeleteClick(key)}
                      />
                    </Tooltip>
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {/* Save / discard strip */}
      {loaded && (
        <div className={styles.saveArea}>
          <Text className={styles.saveHint}>
            {isDirty
              ? "You have unsaved changes. Click Save to persist them to the container."
              : "Properties are up to date."}
          </Text>
          <div style={{ display: "flex", gap: tokens.spacingHorizontalS }}>
            {isDirty && (
              <Button size="small" appearance="secondary" onClick={handleDiscard}>
                Discard
              </Button>
            )}
            <Button
              size="small"
              appearance={isDirty ? "primary" : "secondary"}
              icon={saving ? <Spinner size="tiny" /> : <Save20Regular />}
              onClick={() => void handleSave()}
              disabled={saving || !isDirty}
            >
              {saving ? "Saving…" : "Save All"}
            </Button>
          </div>
        </div>
      )}

      {/* Add / edit dialog */}
      <AddEditPropertyDialog
        open={dialogOpen}
        editingKey={editingKey}
        editingValue={editingValue}
        onConfirm={handleDialogConfirm}
        onDismiss={handleDialogDismiss}
      />
    </div>
  );
};
