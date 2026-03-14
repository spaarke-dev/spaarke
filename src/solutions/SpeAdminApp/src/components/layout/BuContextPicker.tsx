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
 * State persistence: handled by BuContext (localStorage). This component only drives
 * the UI and delegates state to useBuContext().
 *
 * ADR-021: All styles via makeStyles + tokens (no hard-coded colors).
 * ADR-012: Uses LookupField from @spaarke/ui-components.
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
  shorthands,
} from "@fluentui/react-components";
import {
  Building20Regular,
  Cube20Regular,
  Globe20Regular,
  CheckmarkCircle20Filled,
} from "@fluentui/react-icons";
import { LookupField } from "@spaarke/ui-components";
import type { ILookupItem } from "@spaarke/ui-components";
import { useBuContext } from "../../contexts/BuContext";
import { speApiClient } from "../../services/speApiClient";
import type { BusinessUnit, SpeContainerTypeConfig, SpeEnvironment } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021: makeStyles + tokens, zero hard-coded colors)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  /**
   * Root container: horizontal flex bar for the three cascading pickers.
   * Wraps on narrow viewports so each field takes full width.
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

  /**
   * Individual picker column — flex item that shrinks for narrow screens.
   */
  pickerColumn: {
    flex: "1 1 180px",
    minWidth: "160px",
    maxWidth: "320px",
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
  },

  /**
   * Section label row (icon + label text).
   */
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

  /**
   * Environment display — read-only pill showing the derived environment.
   */
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

  /**
   * Context status bar — shown when all three levels are selected.
   */
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
 * Renders as a horizontal bar at the top of the SPE Admin App:
 *   [Business Unit ▼]   [Container Type Config ▼]   [Environment (read-only)]
 *
 * All state is managed through the shared BuContext — this component is
 * purely a UI driver that delegates selection to useBuContext().
 *
 * Data loading:
 *   - Business Units: loaded once on mount from /api/spe/businessunits
 *   - Configs: loaded (or re-loaded) whenever selectedBu changes
 *   - Environment: derived from selectedConfig.environmentId / environmentName
 *     (already embedded in the config record — no separate API call needed)
 *
 * Persistence: BuContext writes to localStorage on every setter call, so
 *   selections survive page navigation within the Code Page.
 */
export const BuContextPicker: React.FC = () => {
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

  /** All business units fetched from the API. */
  const [allBus, setAllBus] = React.useState<BusinessUnit[]>([]);
  const [busLoading, setBusLoading] = React.useState(false);
  const [busError, setBusError] = React.useState<string | null>(null);

  /** Configs for the currently selected BU. */
  const [configs, setConfigs] = React.useState<SpeContainerTypeConfig[]>([]);
  const [configsLoading, setConfigsLoading] = React.useState(false);
  const [configsError, setConfigsError] = React.useState<string | null>(null);

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

          // Validate current selectedConfig still belongs to this BU
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

  // ── Derive environment from selected config ───────────────────────────────

  /**
   * Environment is derived from the selected config — the config record embeds
   * both environmentId and environmentName, so no additional API call is needed.
   * We construct a minimal SpeEnvironment from those fields and store it in context.
   */
  React.useEffect(() => {
    if (!selectedConfig) {
      // Config cleared — clear environment too (BuContext handles this cascade,
      // but we call explicitly to keep environment in sync with config changes
      // triggered externally, e.g. stale localStorage validation).
      setSelectedEnvironment(null);
      return;
    }

    // Build a minimal SpeEnvironment from the embedded fields on the config.
    // Full environment details (tenantId, graphEndpoint, etc.) are available via
    // GET /api/spe/environments/{id} if any page needs them.
    const derivedEnv: SpeEnvironment = {
      id: selectedConfig.environmentId,
      name: selectedConfig.environmentName,
      // These fields are not embedded on the config — default to empty strings.
      // Pages that need full environment details should fetch via speApiClient.environments.
      tenantId: "",
      tenantName: "",
      rootSiteUrl: "",
      graphEndpoint: "",
      isDefault: false,
      status: "active",
    };

    setSelectedEnvironment(derivedEnv);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedConfig?.id]);

  // ── LookupField search handlers ───────────────────────────────────────────

  /**
   * BU search: filter the already-loaded allBus list by name (client-side).
   * Returns items whose name contains the query string (case-insensitive).
   * Also returns all items when the query is empty (minSearchLength defaults to 1,
   * so LookupField won't call this with an empty string).
   */
  const handleBuSearch = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const q = query.toLowerCase();
      return allBus
        .filter((bu) => bu.name.toLowerCase().includes(q))
        .map(buToLookupItem);
    },
    [allBus]
  );

  /**
   * Config search: filter the already-loaded configs list by name (client-side).
   * Configs are already scoped to the selected BU by the API call above.
   */
  const handleConfigSearch = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const q = query.toLowerCase();
      return configs
        .filter((cfg) => cfg.name.toLowerCase().includes(q))
        .map(configToLookupItem);
    },
    [configs]
  );

  // ── Selection change handlers ─────────────────────────────────────────────

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

  // ── Derive current LookupField values ────────────────────────────────────

  const buLookupValue: ILookupItem | null = selectedBu
    ? { id: selectedBu.businessUnitId, name: selectedBu.name }
    : null;

  const configLookupValue: ILookupItem | null = selectedConfig
    ? { id: selectedConfig.id, name: selectedConfig.name }
    : null;

  // Context is "complete" when all three levels are selected
  const isContextComplete =
    selectedBu !== null && selectedConfig !== null && selectedEnvironment !== null;

  // ── Render ────────────────────────────────────────────────────────────────

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
