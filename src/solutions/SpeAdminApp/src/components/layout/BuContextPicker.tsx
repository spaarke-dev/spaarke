/**
 * BuContextPicker.tsx
 *
 * Cascading context picker for Business Unit → Container Type Config → Environment.
 * This component drives the entire SPE Admin App context — all downstream pages
 * depend on the selected BU, config, and environment.
 *
 * Cascade rules:
 *   1. BU dropdown populates from GET /api/spe/businessunits (all BUs).
 *   2. Config dropdown filters to configs whose businessUnitId matches the selected BU.
 *   3. Environment is auto-populated (read-only) from the selected config's environment.
 *
 * Variants:
 *   "full"    — Three-column horizontal bar, each with label + lookup. Default.
 *   "compact" — Inline header row: two Comboboxes + environment badge. For AppShell header.
 *
 * State persistence: handled by BuContext (localStorage). This component only drives
 * the UI and delegates state to useBuContext().
 *
 * ADR-021: All styles via makeStyles + tokens (no hard-coded colors).
 * ADR-012: Uses LookupField from @spaarke/ui-components (full variant only).
 * ADR-022: React 18 (Code Page — bundled createRoot). Not PCF.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  Badge,
  Combobox,
  Field,
  Option,
  shorthands,
} from "@fluentui/react-components";
import {
  Building20Regular,
  Cube20Regular,
  Globe20Regular,
  Globe16Regular,
  CheckmarkCircle20Filled,
} from "@fluentui/react-icons";
import { LookupField } from "@spaarke/ui-components";
import type { ILookupItem } from "@spaarke/ui-components";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient } from "../../services/speApiClient";
import type { BusinessUnit, SpeContainerTypeConfig, SpeEnvironment } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Props
// ─────────────────────────────────────────────────────────────────────────────

export interface BuContextPickerProps {
  /**
   * "full"    — Three-column horizontal bar with labels. Use on a dedicated context row.
   * "compact" — Inline header row: two Comboboxes + environment badge. Use in AppShell header.
   * @default "full"
   */
  variant?: "full" | "compact";
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: makeStyles + tokens, zero hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Root container (full variant): horizontal flex bar for the three cascading pickers.
   */
  root: {
    display: "flex",
    flexDirection: "row",
    flexWrap: "wrap",
    alignItems: "flex-start",
    ...shorthands.gap(tokens.spacingHorizontalL),
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
  },

  /** Individual picker column (full variant). */
  pickerColumn: {
    flex: "1 1 180px",
    minWidth: "160px",
    maxWidth: "320px",
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
  },

  /** Section label row (icon + label text). */
  labelRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalXS),
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalXXS,
  },

  labelText: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
    letterSpacing: "0.04em",
  },

  /** Environment display (full variant) — read-only pill. */
  environmentDisplay: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
  },

  environmentValue: {
    display: "inline-flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalXS),
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: "1px",
    borderRightWidth: "1px",
    borderBottomWidth: "1px",
    borderLeftWidth: "1px",
    borderTopStyle: "solid",
    borderRightStyle: "solid",
    borderBottomStyle: "solid",
    borderLeftStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke1,
    borderRightColor: tokens.colorNeutralStroke1,
    borderBottomColor: tokens.colorNeutralStroke1,
    borderLeftColor: tokens.colorNeutralStroke1,
    minHeight: "32px",
  },

  environmentValueText: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase200,
  },

  environmentPlaceholder: {
    color: tokens.colorNeutralForeground4,
    fontStyle: "italic",
    fontSize: tokens.fontSizeBase200,
  },

  /** Context status bar (full variant). */
  statusBar: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorStatusSuccessBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorStatusSuccessBackground3,
  },

  statusBarText: {
    color: tokens.colorStatusSuccessForeground1,
    fontSize: tokens.fontSizeBase200,
  },

  loadingRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    paddingTop: tokens.spacingVerticalXS,
  },

  // ── Compact variant styles ──────────────────────────────────────────────────

  /**
   * Root container for the compact inline variant.
   * Renders as a horizontal flex row suitable for embedding in an app header.
   */
  compactRoot: {
    display: "flex",
    flexDirection: "row",
    // flex-end, not center: the Fields now stack label-over-control, so their controls only line
    // up with the environment badge when the row is bottom-aligned.
    alignItems: "flex-end",
    ...shorthands.gap(tokens.spacingHorizontalM),
    flexWrap: "nowrap",
  },

  /** Each compact item (a labelled Field, or the environment badge) in the header row. */
  compactItem: {
    minWidth: "170px",
    maxWidth: "230px",
  },

  compactLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    whiteSpace: "nowrap",
  },

  compactSeparator: {
    color: tokens.colorNeutralStroke1,
    fontSize: tokens.fontSizeBase200,
    userSelect: "none",
  },

  compactEnvBadge: {
    display: "inline-flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalXXS),
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers — convert domain types to ILookupItem
// ─────────────────────────────────────────────────────────────────────────────

function buToLookupItem(bu: BusinessUnit): ILookupItem {
  return { id: bu.businessUnitId, name: bu.name };
}

function configToLookupItem(cfg: SpeContainerTypeConfig): ILookupItem {
  return { id: cfg.id, name: cfg.name };
}

// ─────────────────────────────────────────────────────────────────────────────
// BuContextPicker Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * BuContextPicker — three-level cascading context selector.
 *
 * Full variant renders as a horizontal bar:
 *   [Business Unit ▼]   [Container Type Config ▼]   [Environment (read-only)]
 *
 * Compact variant renders as an inline header row:
 *   Business Unit [▼]  /  Config [▼]  ·  [Environment badge]
 *
 * All state is managed through the shared BuContext — this component is
 * purely a UI driver that delegates selection to useBuContext().
 */
export const BuContextPicker: React.FC<BuContextPickerProps> = ({ variant = "full" }) => {
  const styles = useStyles();
  const {
    selectedBu,
    selectedConfig,
    selectedEnvironment,
    setSelectedBu,
    setSelectedConfig,
    setSelectedEnvironment,
  } = useBuContext();

  // ── Local data state ─────────────────────────────────────────────────────

  const [allBus, setAllBus] = React.useState<BusinessUnit[]>([]);
  const [busLoading, setBusLoading] = React.useState(false);
  const [busError, setBusError] = React.useState<string | null>(null);

  const [configs, setConfigs] = React.useState<SpeContainerTypeConfig[]>([]);
  const [configsLoading, setConfigsLoading] = React.useState(false);
  const [configsError, setConfigsError] = React.useState<string | null>(null);

  // Compact variant: track combobox input values separately to support type-to-filter
  const [buInputValue, setBuInputValue] = React.useState(selectedBu?.name ?? "");
  const [configInputValue, setConfigInputValue] = React.useState(selectedConfig?.name ?? "");

  // ── Load Business Units on mount ─────────────────────────────────────────

  React.useEffect(() => {
    let cancelled = false;
    setBusLoading(true);
    setBusError(null);

    speApiClient.businessUnits
      .list()
      .then((bus) => {
        if (!cancelled) {
          setAllBus(bus);
          setBusLoading(false);
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          const message =
            err instanceof Error ? err.message : "Failed to load Business Units";
          setBusError(message);
          setBusLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  // ── Load Configs when BU changes ─────────────────────────────────────────

  React.useEffect(() => {
    if (!selectedBu) {
      setConfigs([]);
      setConfigsError(null);
      return;
    }

    let cancelled = false;
    setConfigsLoading(true);
    setConfigsError(null);
    setConfigs([]);

    speApiClient.configs
      .list({ businessUnitId: selectedBu.businessUnitId })
      .then((cfgs) => {
        if (!cancelled) {
          setConfigs(cfgs);
          setConfigsLoading(false);

          if (selectedConfig && selectedConfig.businessUnitId !== selectedBu.businessUnitId) {
            setSelectedConfig(null);
          }
        }
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          const message =
            err instanceof Error ? err.message : "Failed to load Container Type Configs";
          setConfigsError(message);
          setConfigsLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedBu?.businessUnitId]);

  // ── Sync compact input values with context ────────────────────────────────

  React.useEffect(() => {
    setBuInputValue(selectedBu?.name ?? "");
  }, [selectedBu]);

  React.useEffect(() => {
    setConfigInputValue(selectedConfig?.name ?? "");
  }, [selectedConfig]);

  // ── Derive environment from selected config ───────────────────────────────

  React.useEffect(() => {
    if (!selectedConfig) {
      setSelectedEnvironment(null);
      return;
    }

    const derivedEnv: SpeEnvironment = {
      id: selectedConfig.environmentId,
      name: selectedConfig.environmentName,
      tenantId: "",
      tenantName: "",
      rootSiteUrl: "",
      isDefault: false,
      status: "active",
    };

    setSelectedEnvironment(derivedEnv);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig?.id]);

  // ── LookupField search handlers (full variant) ────────────────────────────

  const handleBuSearch = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const q = query.toLowerCase();
      return allBus
        .filter((bu) => bu.name.toLowerCase().includes(q))
        .map(buToLookupItem);
    },
    [allBus]
  );

  const handleConfigSearch = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const q = query.toLowerCase();
      return configs
        .filter((cfg) => cfg.name.toLowerCase().includes(q))
        .map(configToLookupItem);
    },
    [configs]
  );

  // ── Selection change handlers (full variant) ──────────────────────────────

  const handleBuChange = React.useCallback(
    (item: ILookupItem | null) => {
      if (item === null) {
        setSelectedBu(null);
        return;
      }
      const bu = allBus.find((b) => b.businessUnitId === item.id) ?? null;
      setSelectedBu(bu);
    },
    [allBus, setSelectedBu]
  );

  const handleConfigChange = React.useCallback(
    (item: ILookupItem | null) => {
      if (item === null) {
        setSelectedConfig(null);
        return;
      }
      const cfg = configs.find((c) => c.id === item.id) ?? null;
      setSelectedConfig(cfg);
    },
    [configs, setSelectedConfig]
  );

  // ── Selection handlers (compact variant) ─────────────────────────────────

  const handleBuOptionSelect = React.useCallback(
    (_: React.SyntheticEvent, data: { optionValue?: string; optionText?: string }) => {
      const bu = allBus.find((b) => b.businessUnitId === data.optionValue) ?? null;
      setSelectedBu(bu);
    },
    [allBus, setSelectedBu]
  );

  const handleConfigOptionSelect = React.useCallback(
    (_: React.SyntheticEvent, data: { optionValue?: string; optionText?: string }) => {
      const cfg = configs.find((c) => c.id === data.optionValue) ?? null;
      setSelectedConfig(cfg);
    },
    [configs, setSelectedConfig]
  );

  // ── Derive current LookupField values (full variant) ─────────────────────

  const buLookupValue: ILookupItem | null = selectedBu
    ? { id: selectedBu.businessUnitId, name: selectedBu.name }
    : null;

  const configLookupValue: ILookupItem | null = selectedConfig
    ? { id: selectedConfig.id, name: selectedConfig.name }
    : null;

  // Filter combobox options by current input (compact variant)
  const filteredBus = allBus.filter((bu) =>
    bu.name.toLowerCase().includes(buInputValue.toLowerCase())
  );
  const filteredConfigs = configs.filter((cfg) =>
    cfg.name.toLowerCase().includes(configInputValue.toLowerCase())
  );

  const isContextComplete =
    selectedBu !== null && selectedConfig !== null && selectedEnvironment !== null;

  // ── Render: compact variant ───────────────────────────────────────────────

  if (variant === "compact") {
    return (
      <div className={styles.compactRoot} role="region" aria-label="Context selector">
        {/*
          Labels are spelled out and sit ABOVE their control as a Fluent Field, matching the
          label-over-control pattern already used in the Container Type details pane. Previously
          they were abbreviated ("BU", "Config") and rendered as loose text beside a bare box.
          Operator-directed, UAT 2026-08-26.
        */}
        <Field label="Business Unit" className={styles.compactItem}>
          <Combobox
            size="small"
            placeholder={busLoading ? "Loading…" : busError ? "⚠ Load failed" : "Business unit…"}
            value={buInputValue}
            selectedOptions={selectedBu ? [selectedBu.businessUnitId] : []}
            onInput={(e) => setBuInputValue((e.target as HTMLInputElement).value)}
            onOptionSelect={handleBuOptionSelect}
            onBlur={() => {
              // Reset input to selected value if user typed but didn't pick
              setBuInputValue(selectedBu?.name ?? "");
            }}
            title={busError ?? undefined}
            style={{ minWidth: "160px", maxWidth: "220px" }}
          >
            {filteredBus.map((bu) => (
              <Option key={bu.businessUnitId} value={bu.businessUnitId}>
                {bu.name}
              </Option>
            ))}
          </Combobox>
        </Field>

        {/*
          Labelled "Container Type", not "Config". The underlying record
          (sprk_specontainertypeconfig) binds a container type to a business unit, an environment,
          an owning app registration and its Key Vault secret — but "Config" named the plumbing,
          not the thing an administrator believes they are choosing. Operator-directed, UAT
          2026-08-26.
        */}
        <Field label="Container Type" className={styles.compactItem}>
          <Combobox
            size="small"
            placeholder={
              !selectedBu
                ? "Select a business unit first"
                : configsLoading
                  ? "Loading…"
                  : "Container type…"
            }
            disabled={!selectedBu || configsLoading}
            value={configInputValue}
            selectedOptions={selectedConfig ? [selectedConfig.id] : []}
            onInput={(e) => setConfigInputValue((e.target as HTMLInputElement).value)}
            onOptionSelect={handleConfigOptionSelect}
            onBlur={() => {
              setConfigInputValue(selectedConfig?.name ?? "");
            }}
            style={{ minWidth: "160px", maxWidth: "220px" }}
          >
            {filteredConfigs.map((cfg) => (
              <Option key={cfg.id} value={cfg.id}>
                {cfg.name}
              </Option>
            ))}
          </Combobox>
        </Field>

        {/* Environment badge (read-only) */}
        {selectedEnvironment && (
          <>
            <div className={styles.compactEnvBadge}>
              <Globe16Regular aria-hidden="true" style={{ color: tokens.colorNeutralForeground3 }} />
              <Badge
                appearance="tint"
                color={isContextComplete ? "success" : "informative"}
                size="medium"
              >
                {selectedEnvironment.name}
              </Badge>
            </div>
          </>
        )}
      </div>
    );
  }

  // ── Render: full variant ──────────────────────────────────────────────────

  return (
    <>
      <div className={styles.root} role="region" aria-label="Context selector">
        {/* ── Business Unit Column ── */}
        <div className={styles.pickerColumn}>
          <div className={styles.labelRow}>
            <Building20Regular aria-hidden="true" />
            <Text className={styles.labelText}>Business Unit</Text>
          </div>

          {busLoading ? (
            <div className={styles.loadingRow}>
              <Spinner size="tiny" />
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Loading&hellip;
              </Text>
            </div>
          ) : busError ? (
            <MessageBar intent="error">
              <MessageBarBody>{busError}</MessageBarBody>
            </MessageBar>
          ) : (
            <LookupField
              label="Business Unit"
              placeholder="Search business units..."
              value={buLookupValue}
              onChange={handleBuChange}
              onSearch={handleBuSearch}
              required
              minSearchLength={0}
            />
          )}
        </div>

        {/* ── Container Type Config Column ── */}
        <div className={styles.pickerColumn}>
          <div className={styles.labelRow}>
            <Cube20Regular aria-hidden="true" />
            <Text className={styles.labelText}>Container Type Config</Text>
            {selectedBu && (
              <Badge
                size="small"
                appearance="tint"
                color="informative"
                aria-label={`${configs.length} configs`}
              >
                {configs.length}
              </Badge>
            )}
          </div>

          {configsLoading ? (
            <div className={styles.loadingRow}>
              <Spinner size="tiny" />
              <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                Loading&hellip;
              </Text>
            </div>
          ) : configsError ? (
            <MessageBar intent="error">
              <MessageBarBody>{configsError}</MessageBarBody>
            </MessageBar>
          ) : (
            <LookupField
              label="Container Type Config"
              placeholder={
                selectedBu
                  ? "Search configs..."
                  : "Select a Business Unit first"
              }
              value={configLookupValue}
              onChange={handleConfigChange}
              onSearch={handleConfigSearch}
              required
              minSearchLength={0}
            />
          )}
        </div>

        {/* ── Environment Column (read-only, derived from config) ── */}
        <div className={styles.pickerColumn}>
          <div className={styles.labelRow}>
            <Globe20Regular aria-hidden="true" />
            <Text className={styles.labelText}>Environment</Text>
          </div>

          <div className={styles.environmentDisplay}>
            <div
              className={styles.environmentValue}
              aria-label={
                selectedEnvironment
                  ? `Environment: ${selectedEnvironment.name}`
                  : "No environment selected"
              }
            >
              {selectedEnvironment ? (
                <Text className={styles.environmentValueText}>
                  {selectedEnvironment.name}
                </Text>
              ) : (
                <Text className={styles.environmentPlaceholder}>
                  {selectedConfig
                    ? "Deriving environment\u2026"
                    : "Select a config"}
                </Text>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* ── Context-complete status bar ── */}
      {isContextComplete && (
        <div className={styles.statusBar} role="status" aria-live="polite">
          <CheckmarkCircle20Filled
            aria-hidden="true"
            style={{ color: tokens.colorStatusSuccessForeground1 }}
          />
          <Text className={styles.statusBarText}>
            Context active &mdash; {selectedBu!.name} / {selectedConfig!.name} /{" "}
            {selectedEnvironment!.name}
          </Text>
        </div>
      )}
    </>
  );
};
