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
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItemRadio,
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
    // Back to center now that the pickers are single-line MenuButtons rather than
    // label-over-control Fields — everything in the row is one line tall again.
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexWrap: "nowrap",
  },

  /**
   * The borderless title-as-dropdown trigger (UAT round 6).
   *
   * `appearance="transparent"` already removes the fill; the explicit `border: none` is belt and
   * braces against a Fluent theme that re-adds a hairline, since "no field border box" was the
   * literal request. Weight is regular so it reads as a field label, not as a command.
   */
  compactMenuButton: {
    border: "none",
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
    whiteSpace: "nowrap",
    paddingLeft: tokens.spacingHorizontalSNudge,
    paddingRight: tokens.spacingHorizontalSNudge,
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

  // The compact variant's type-to-filter input state was removed with the Comboboxes
  // (2026-08-26). See the note above `isContextComplete` for why leaving it in place would have
  // been worse than unused code.

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

  // The two effects that mirrored context into the compact Comboboxes' input state went with
  // those Comboboxes (2026-08-26). The menus read `selectedBu` / `selectedConfig` directly, so
  // there is no second copy of the selection left to keep in sync.

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

  // ── Derive current LookupField values (full variant) ─────────────────────

  const buLookupValue: ILookupItem | null = selectedBu
    ? { id: selectedBu.businessUnitId, name: selectedBu.name }
    : null;

  const configLookupValue: ILookupItem | null = selectedConfig
    ? { id: selectedConfig.id, name: selectedConfig.name }
    : null;

  /*
   * 🔴 The compact variant's `filteredBus` / `filteredConfigs` were DELETED here, not just left
   * unused, and the reason is a bug they would have caused.
   *
   * They filtered the option lists by the Combobox's typed text. Those Comboboxes are gone (the
   * pickers are menus now), but `configInputValue` is still SEEDED from the persisted selection —
   * `React.useState(selectedConfig?.name ?? "")`. So on any load where a config was already
   * selected, the filter would have matched exactly one row and the menu would have offered the
   * operator only the container type they had already chosen, with no way to switch and nothing
   * on screen explaining why.
   *
   * `allBus` and `configs` are the right sources: `configs` is already scoped to the selected
   * business unit by the loader above, which is the only filtering these menus should do.
   */

  const isContextComplete =
    selectedBu !== null && selectedConfig !== null && selectedEnvironment !== null;

  // ── Render: compact variant ───────────────────────────────────────────────

  if (variant === "compact") {
    return (
      <div className={styles.compactRoot} role="region" aria-label="Context selector">
        {/*
          ── The field TITLE is the dropdown ──

          Operator-directed, UAT round 6: "the field title is the drop down, not a separate box;
          no field border box". These were a Fluent `Field` label stacked over a bordered
          `Combobox`; they are now a single borderless `MenuButton` whose text IS the field name,
          with the current value marked by a checkmark in the menu.

          What makes this safe rather than a loss of information: the selected scope is stated on
          every page it affects — each page header reads "{config} · {environment}" — and the
          environment badge sits immediately to the right of these buttons. The header does not
          need to repeat it a third time.

          `MenuItemRadio` (not MenuItem) is what supplies the checkmark and the radio semantics,
          so the current selection is announced, not just drawn.
        */}
        <Menu
          checkedValues={{ bu: selectedBu ? [selectedBu.businessUnitId] : [] }}
          onCheckedValueChange={(_e, data) => {
            const id = data.checkedItems[0];
            setSelectedBu(allBus.find((b) => b.businessUnitId === id) ?? null);
          }}
        >
          <MenuTrigger disableButtonEnhancement>
            <MenuButton
              appearance="transparent"
              size="medium"
              disabled={busLoading}
              title={busError ?? selectedBu?.name ?? undefined}
              className={styles.compactMenuButton}
            >
              {busLoading ? "Loading…" : busError ? "⚠ Business Unit" : "Business Unit"}
            </MenuButton>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {allBus.map((bu) => (
                <MenuItemRadio key={bu.businessUnitId} name="bu" value={bu.businessUnitId}>
                  {bu.name}
                </MenuItemRadio>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>

        {/*
          Labelled "Container Type", not "Config". The underlying record
          (sprk_specontainertypeconfig) binds a container type to a business unit, an environment,
          an owning app registration and its Key Vault secret — but "Config" named the plumbing,
          not the thing an administrator believes they are choosing. Operator-directed, UAT
          2026-08-26.
        */}
        <Menu
          checkedValues={{ config: selectedConfig ? [selectedConfig.id] : [] }}
          onCheckedValueChange={(_e, data) => {
            const id = data.checkedItems[0];
            setSelectedConfig(configs.find((c) => c.id === id) ?? null);
          }}
        >
          <MenuTrigger disableButtonEnhancement>
            <MenuButton
              appearance="transparent"
              size="medium"
              disabled={!selectedBu || configsLoading}
              title={
                !selectedBu
                  ? "Select a business unit first"
                  : selectedConfig?.name ?? undefined
              }
              className={styles.compactMenuButton}
            >
              {configsLoading ? "Loading…" : "Container Type"}
            </MenuButton>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {configs.map((cfg) => (
                <MenuItemRadio key={cfg.id} name="config" value={cfg.id}>
                  {cfg.name}
                </MenuItemRadio>
              ))}
            </MenuList>
          </MenuPopover>
        </Menu>

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
