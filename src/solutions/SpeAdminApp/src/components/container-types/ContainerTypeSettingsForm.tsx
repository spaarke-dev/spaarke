/**
 * ContainerTypeSettingsForm — editable settings form for a container type.
 *
 * Displays the configurable settings for a container type configuration:
 *   - Sharing capability (disabled / viewOnly / edit / full)
 *   - Versioning toggle + major version limit input
 *   - Storage quota (max storage per container in bytes)
 *   - Discoverability (future: whether containers appear in search)
 *   - Search enabled (whether item search is enabled within containers)
 *
 * The form does NOT manage its own state — it receives values and onChange
 * handlers from ContainerTypeDetail (controlled pattern for dirty tracking).
 *
 * ADR-021: All styles use Fluent UI v9 makeStyles + design tokens.
 * ADR-006: Code Page — React 18 patterns; no PCF / ComponentFramework deps.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Field,
  Dropdown,
  Option,
  Switch,
  Input,
  Divider,
  shorthands,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/** Sharing capability values supported by SPE Graph API */
export type SharingCapabilityValue =
  | "disabled"
  | "externalUserSharingOnly"
  | "existingExternalUserSharingOnly"
  | "externalUserAndGuestSharing";

/** The editable settings that can be saved via PUT /api/spe/containertypes/{typeId}/settings */
export interface ContainerTypeSettings {
  sharingCapability: SharingCapabilityValue;
  isItemVersioningEnabled: boolean;
  itemMajorVersionLimit: number;
  maxStoragePerBytes: number;
  isSearchEnabled: boolean;
}

export interface ContainerTypeSettingsFormProps {
  /** Current settings values (controlled). */
  settings: ContainerTypeSettings;
  /** Called whenever any field value changes. */
  onChange: (updated: ContainerTypeSettings) => void;
  /** Whether the form should be disabled (e.g., while saving). */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const SHARING_OPTIONS: { value: SharingCapabilityValue; label: string; description: string }[] = [
  {
    value: "disabled",
    label: "Disabled",
    description: "Sharing is not allowed",
  },
  {
    value: "externalUserSharingOnly",
    label: "External Users Only",
    description: "Share only with external users",
  },
  {
    value: "existingExternalUserSharingOnly",
    label: "Existing External Users",
    description: "Share only with existing external users",
  },
  {
    value: "externalUserAndGuestSharing",
    label: "External Users & Guests",
    description: "Full external sharing including guest users",
  },
];

/** Minimum version limit — SPE Graph API requires at least 1 major version. */
const MIN_VERSION_LIMIT = 1;

/** Maximum version limit — practical upper bound for the UI. */
const MAX_VERSION_LIMIT = 500;

/** 1 GB in bytes — default storage quota guidance. */
const GB = 1_073_741_824;

/** Maximum display value for the storage input (in GB). */
const MAX_STORAGE_GB = 10_000;

// ─────────────────────────────────────────────────────────────────────────────
// Styles (ADR-021 — Fluent tokens only)
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalL),
  },

  section: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
  },

  sectionTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },

  /** Row that places a switch label and description side by side. */
  switchRow: {
    display: "flex",
    alignItems: "flex-start",
    justifyContent: "space-between",
    ...shorthands.gap(tokens.spacingHorizontalM),
  },

  switchLabel: {
    display: "flex",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalXXS),
    flex: "1 1 auto",
  },

  switchLabelText: {
    color: tokens.colorNeutralForeground1,
  },

  switchDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },

  /** Indented sub-field shown when versioning is enabled. */
  subField: {
    marginLeft: tokens.spacingHorizontalL,
  },

  storageHint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

/** Convert bytes to GB (rounded to 2 decimal places). */
function bytesToGb(bytes: number): number {
  return Math.round((bytes / GB) * 100) / 100;
}

/** Convert GB to bytes (integer). */
function gbToBytes(gb: number): number {
  return Math.round(gb * GB);
}

/** Format bytes as a human-readable string. */
function formatBytes(bytes: number): string {
  if (bytes === 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024;
    unitIndex++;
  }
  return `${value.toFixed(2).replace(/\.?0+$/, "")} ${units[unitIndex]}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// ContainerTypeSettingsForm Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Controlled form for editing container type settings.
 * Calls onChange with the full updated settings object on every change.
 */
export const ContainerTypeSettingsForm: React.FC<ContainerTypeSettingsFormProps> = ({
  settings,
  onChange,
  disabled = false,
}) => {
  const styles = useStyles();

  // ── Local helpers to update a single field ──────────────────────────────

  const set = React.useCallback(
    <K extends keyof ContainerTypeSettings>(
      key: K,
      value: ContainerTypeSettings[K]
    ) => {
      onChange({ ...settings, [key]: value });
    },
    [settings, onChange]
  );

  // ── Storage input local state (string for controlled input) ─────────────

  const [storageGbInput, setStorageGbInput] = React.useState<string>(
    () => String(bytesToGb(settings.maxStoragePerBytes))
  );

  // Sync local storage string when settings change externally (e.g. reload)
  React.useEffect(() => {
    setStorageGbInput(String(bytesToGb(settings.maxStoragePerBytes)));
  }, [settings.maxStoragePerBytes]);

  const handleStorageBlur = React.useCallback(() => {
    const parsed = parseFloat(storageGbInput);
    if (!isNaN(parsed) && parsed > 0 && parsed <= MAX_STORAGE_GB) {
      set("maxStoragePerBytes", gbToBytes(parsed));
    } else {
      // Reset to current value on invalid input
      setStorageGbInput(String(bytesToGb(settings.maxStoragePerBytes)));
    }
  }, [storageGbInput, settings.maxStoragePerBytes, set]);

  // ── Version limit input local state ────────────────────────────────────

  const [versionLimitInput, setVersionLimitInput] = React.useState<string>(
    () => String(settings.itemMajorVersionLimit)
  );

  React.useEffect(() => {
    setVersionLimitInput(String(settings.itemMajorVersionLimit));
  }, [settings.itemMajorVersionLimit]);

  const handleVersionLimitBlur = React.useCallback(() => {
    const parsed = parseInt(versionLimitInput, 10);
    if (
      !isNaN(parsed) &&
      parsed >= MIN_VERSION_LIMIT &&
      parsed <= MAX_VERSION_LIMIT
    ) {
      set("itemMajorVersionLimit", parsed);
    } else {
      setVersionLimitInput(String(settings.itemMajorVersionLimit));
    }
  }, [versionLimitInput, settings.itemMajorVersionLimit, set]);

  // ── Render ──────────────────────────────────────────────────────────────

  const selectedSharingLabel =
    SHARING_OPTIONS.find((o) => o.value === settings.sharingCapability)?.label ??
    "Disabled";

  return (
    <div className={styles.root}>
      {/* ── Section: Sharing ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Sharing
        </Text>

        <Field
          label="Sharing Capability"
          hint="Controls who can access shared links for containers of this type."
        >
          <Dropdown
            value={selectedSharingLabel}
            selectedOptions={[settings.sharingCapability]}
            onOptionSelect={(_e, d) => {
              if (d.optionValue) {
                set("sharingCapability", d.optionValue as SharingCapabilityValue);
              }
            }}
            disabled={disabled}
            aria-label="Sharing capability"
          >
            {SHARING_OPTIONS.map((opt) => (
              <Option key={opt.value} value={opt.value}>
                {opt.label}
              </Option>
            ))}
          </Dropdown>
        </Field>
      </div>

      <Divider />

      {/* ── Section: Versioning ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Versioning
        </Text>

        <div className={styles.switchRow}>
          <div className={styles.switchLabel}>
            <Text className={styles.switchLabelText}>Enable Item Versioning</Text>
            <Text className={styles.switchDescription}>
              Track version history for files stored in containers of this type.
            </Text>
          </div>
          <Switch
            checked={settings.isItemVersioningEnabled}
            onChange={(_e, d) => set("isItemVersioningEnabled", d.checked)}
            disabled={disabled}
            aria-label="Enable item versioning"
          />
        </div>

        {settings.isItemVersioningEnabled && (
          <div className={styles.subField}>
            <Field
              label="Major Version Limit"
              hint={`Number of major versions to retain per file (${MIN_VERSION_LIMIT}–${MAX_VERSION_LIMIT}).`}
              validationState={
                parseInt(versionLimitInput, 10) < MIN_VERSION_LIMIT ||
                parseInt(versionLimitInput, 10) > MAX_VERSION_LIMIT
                  ? "error"
                  : "none"
              }
              validationMessage={
                parseInt(versionLimitInput, 10) < MIN_VERSION_LIMIT ||
                parseInt(versionLimitInput, 10) > MAX_VERSION_LIMIT
                  ? `Enter a value between ${MIN_VERSION_LIMIT} and ${MAX_VERSION_LIMIT}.`
                  : undefined
              }
            >
              <Input
                type="number"
                value={versionLimitInput}
                onChange={(_e, d) => setVersionLimitInput(d.value)}
                onBlur={handleVersionLimitBlur}
                min={MIN_VERSION_LIMIT}
                max={MAX_VERSION_LIMIT}
                disabled={disabled}
                aria-label="Major version limit"
                style={{ maxWidth: "120px" }}
              />
            </Field>
          </div>
        )}
      </div>

      <Divider />

      {/* ── Section: Storage ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Storage
        </Text>

        <Field
          label="Maximum Storage per Container (GB)"
          hint={`Current: ${formatBytes(settings.maxStoragePerBytes)}`}
        >
          <Input
            type="number"
            value={storageGbInput}
            onChange={(_e, d) => setStorageGbInput(d.value)}
            onBlur={handleStorageBlur}
            min={0.001}
            max={MAX_STORAGE_GB}
            step={1}
            disabled={disabled}
            aria-label="Maximum storage per container in gigabytes"
            style={{ maxWidth: "180px" }}
          />
        </Field>
      </div>

      <Divider />

      {/* ── Section: Search & Discoverability ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Search & Discoverability
        </Text>

        <div className={styles.switchRow}>
          <div className={styles.switchLabel}>
            <Text className={styles.switchLabelText}>Enable Item Search</Text>
            <Text className={styles.switchDescription}>
              Allow full-text search of files stored in containers of this type.
            </Text>
          </div>
          <Switch
            checked={settings.isSearchEnabled}
            onChange={(_e, d) => set("isSearchEnabled", d.checked)}
            disabled={disabled}
            aria-label="Enable item search"
          />
        </div>
      </div>
    </div>
  );
};
