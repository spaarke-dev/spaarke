/**
 * ContainerTypeSettingsForm — editable settings form for a container type.
 *
 * Renders all NINE v1.0 container-type settings (spec FR-C07 / task 025) plus the beta-only
 * `isOfficeRestricted`, bound to the **Graph** settings shape:
 *
 *   Editable  — sharingCapability, isItemVersioningEnabled, itemMajorVersionLimit,
 *               maxStoragePerContainerInBytes, isSearchEnabled, isDiscoverabilityEnabled,
 *               isSharingRestricted, urlTemplate
 *   Read-only — consumingTenantOverridables (override PERMISSION metadata — task 026 owns its
 *               meaning), isOfficeRestricted (beta-only; not writable on the v1.0 surface)
 *
 * 🔴 REBOUND 2026-08-27 (task 025 completion). This form was previously bound to the **Dataverse
 * config record** (`SpeContainerTypeConfig`, field `maxStoragePerBytes`) rather than to the Graph
 * settings this screen claims to show. Two consequences, both live defects:
 *
 *   1. Every value was FABRICATED when the Dataverse record was silent — `?? "disabled"`, `?? false`,
 *      `?? 100`, `?? 1 GB`, and `isSearchEnabled: true` hard-coded with the comment "enabled by
 *      default". An admin read those as the container type's actual settings. They were the client's
 *      invention. This is the project's signature defect (spec §2.4) inside its own settings screen.
 *   2. The client property name (`maxStoragePerBytes`) did not match the Graph one
 *      (`maxStoragePerContainerInBytes`) — the same class of mismatch task 023 spent its length
 *      untangling.
 *
 * The form now takes Graph's own shape, where **every property is optional and `undefined` means
 * NOT REPORTED** — never a default. An unreported setting renders as "Not reported" and is left
 * out of the save payload entirely, because the BFF contract is "only non-null fields are applied".
 * Coercing an unknown to `false` and writing it back would turn a gap in knowledge into a change in
 * configuration.
 *
 * The form does NOT manage its own state — it receives values and onChange handlers from
 * ContainerTypeDetail (controlled pattern for dirty tracking).
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
  Badge,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  shorthands,
} from "@fluentui/react-components";
import type { ContainerTypeSettings, SharingCapability } from "../../types/spe";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * @deprecated Use {@link SharingCapability} from `types/spe`. Retained as an alias so existing
 * imports keep compiling; there is only ever one list of Graph sharing values.
 */
export type SharingCapabilityValue = SharingCapability;

/**
 * Re-exported so consumers keep a single import site. This is Graph's shape — **not** the Dataverse
 * config shape the form used to take.
 */
export type { ContainerTypeSettings };

export interface ContainerTypeSettingsFormProps {
  /** Current settings values (controlled). `undefined` members mean NOT REPORTED. */
  settings: ContainerTypeSettings;
  /** Called whenever any field value changes. */
  onChange: (updated: ContainerTypeSettings) => void;
  /** Whether the form should be disabled (e.g., while saving). */
  disabled?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const SHARING_OPTIONS: { value: SharingCapability; label: string; description: string }[] = [
  { value: "disabled", label: "Disabled", description: "Sharing is not allowed" },
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

/** 1 GB in bytes — used only for display conversion, never as a default value. */
const GB = 1_073_741_824;

/** Maximum display value for the storage input (in GB). */
const MAX_STORAGE_GB = 10_000;

/** Shown wherever Graph did not report a value. Must never be confused with a real setting. */
const NOT_REPORTED = "Not reported";

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

  /** Right-hand control cluster for a switch row — switch plus its not-reported marker. */
  switchControl: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    flexShrink: 0,
  },

  /** Indented sub-field shown when versioning is enabled. */
  subField: {
    marginLeft: tokens.spacingHorizontalL,
  },

  readOnlyValue: {
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    wordBreak: "break-word",
  },

  muted: {
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

/** Format bytes as a human-readable string. `undefined` is NOT REPORTED, never "0 B". */
function formatBytes(bytes: number | undefined): string {
  if (bytes === undefined || bytes === null) return NOT_REPORTED;
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
// Tri-state switch
// ─────────────────────────────────────────────────────────────────────────────

/**
 * A boolean setting that may be `true`, `false`, or **not reported**.
 *
 * A plain `<Switch checked={undefined}>` renders identically to `checked={false}` — which is exactly
 * the collapse this project exists to remove, and here it would be worse than cosmetic: the admin
 * would see "off", believe it, and a save would then write `false` for a setting whose real value
 * nobody knows. The badge keeps the third state visible, and until the admin actually toggles the
 * control the value stays `undefined` and is omitted from the save.
 */
const TriStateSwitch: React.FC<{
  label: string;
  description: string;
  value: boolean | undefined;
  onToggle: (next: boolean) => void;
  disabled?: boolean;
  styles: ReturnType<typeof useStyles>;
}> = ({ label, description, value, onToggle, disabled, styles }) => (
  <div className={styles.switchRow}>
    <div className={styles.switchLabel}>
      <Text className={styles.switchLabelText}>{label}</Text>
      <Text className={styles.switchDescription}>{description}</Text>
    </div>
    <div className={styles.switchControl}>
      {value === undefined && (
        <Badge appearance="outline" color="informative" title="Graph did not report this setting">
          {NOT_REPORTED}
        </Badge>
      )}
      <Switch
        checked={value ?? false}
        onChange={(_e, d) => onToggle(d.checked)}
        disabled={disabled}
        aria-label={label}
      />
    </div>
  </div>
);

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

  const set = React.useCallback(
    <K extends keyof ContainerTypeSettings>(key: K, value: ContainerTypeSettings[K]) => {
      onChange({ ...settings, [key]: value });
    },
    [settings, onChange]
  );

  // ── Storage input local state (string for controlled input) ─────────────
  // Empty string represents "not reported" — distinct from "0".

  const [storageGbInput, setStorageGbInput] = React.useState<string>(() =>
    settings.maxStoragePerContainerInBytes === undefined
      ? ""
      : String(bytesToGb(settings.maxStoragePerContainerInBytes))
  );

  React.useEffect(() => {
    setStorageGbInput(
      settings.maxStoragePerContainerInBytes === undefined
        ? ""
        : String(bytesToGb(settings.maxStoragePerContainerInBytes))
    );
  }, [settings.maxStoragePerContainerInBytes]);

  const handleStorageBlur = React.useCallback(() => {
    if (storageGbInput.trim() === "") {
      // Cleared: return to not-reported rather than writing 0. The BFF treats null as
      // "leave unchanged", so this is the only value that means "I am not setting this".
      set("maxStoragePerContainerInBytes", undefined);
      return;
    }
    const parsed = parseFloat(storageGbInput);
    if (!isNaN(parsed) && parsed > 0 && parsed <= MAX_STORAGE_GB) {
      set("maxStoragePerContainerInBytes", gbToBytes(parsed));
    } else {
      setStorageGbInput(
        settings.maxStoragePerContainerInBytes === undefined
          ? ""
          : String(bytesToGb(settings.maxStoragePerContainerInBytes))
      );
    }
  }, [storageGbInput, settings.maxStoragePerContainerInBytes, set]);

  // ── Version limit input local state ────────────────────────────────────

  const [versionLimitInput, setVersionLimitInput] = React.useState<string>(() =>
    settings.itemMajorVersionLimit === undefined ? "" : String(settings.itemMajorVersionLimit)
  );

  React.useEffect(() => {
    setVersionLimitInput(
      settings.itemMajorVersionLimit === undefined ? "" : String(settings.itemMajorVersionLimit)
    );
  }, [settings.itemMajorVersionLimit]);

  const handleVersionLimitBlur = React.useCallback(() => {
    if (versionLimitInput.trim() === "") {
      set("itemMajorVersionLimit", undefined);
      return;
    }
    const parsed = parseInt(versionLimitInput, 10);
    if (!isNaN(parsed) && parsed >= MIN_VERSION_LIMIT && parsed <= MAX_VERSION_LIMIT) {
      set("itemMajorVersionLimit", parsed);
    } else {
      setVersionLimitInput(
        settings.itemMajorVersionLimit === undefined ? "" : String(settings.itemMajorVersionLimit)
      );
    }
  }, [versionLimitInput, settings.itemMajorVersionLimit, set]);

  const versionLimitOutOfRange =
    versionLimitInput.trim() !== "" &&
    (parseInt(versionLimitInput, 10) < MIN_VERSION_LIMIT ||
      parseInt(versionLimitInput, 10) > MAX_VERSION_LIMIT);

  // ── Render ──────────────────────────────────────────────────────────────

  const selectedSharingLabel = settings.sharingCapability
    ? (SHARING_OPTIONS.find(o => o.value === settings.sharingCapability)?.label ??
      settings.sharingCapability)
    : NOT_REPORTED;

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
            selectedOptions={settings.sharingCapability ? [settings.sharingCapability] : []}
            placeholder={NOT_REPORTED}
            onOptionSelect={(_e, d) => {
              if (d.optionValue) {
                set("sharingCapability", d.optionValue as SharingCapability);
              }
            }}
            disabled={disabled}
            aria-label="Sharing capability"
          >
            {SHARING_OPTIONS.map(opt => (
              <Option key={opt.value} value={opt.value}>
                {opt.label}
              </Option>
            ))}
          </Dropdown>
        </Field>

        <TriStateSwitch
          label="Restrict Sharing"
          description="A separate restriction flag — distinct from Sharing Capability, which selects WHICH sharing is allowed. Both exist and neither substitutes for the other."
          value={settings.isSharingRestricted}
          onToggle={v => set("isSharingRestricted", v)}
          disabled={disabled}
          styles={styles}
        />
      </div>

      <Divider />

      {/* ── Section: Versioning ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Versioning
        </Text>

        <TriStateSwitch
          label="Enable Item Versioning"
          description="Track version history for files stored in containers of this type."
          value={settings.isItemVersioningEnabled}
          onToggle={v => set("isItemVersioningEnabled", v)}
          disabled={disabled}
          styles={styles}
        />

        {/*
          Shown whenever versioning is on OR a limit was reported. Hiding it purely on the toggle
          would conceal a reported limit whenever versioning happens to read false — an admin would
          have no way to see a value Graph is returning.
        */}
        {(settings.isItemVersioningEnabled || settings.itemMajorVersionLimit !== undefined) && (
          <div className={styles.subField}>
            <Field
              label="Major Version Limit"
              hint={`Number of major versions to retain per file (${MIN_VERSION_LIMIT}–${MAX_VERSION_LIMIT}). Leave blank to leave unchanged.`}
              validationState={versionLimitOutOfRange ? "error" : "none"}
              validationMessage={
                versionLimitOutOfRange
                  ? `Enter a value between ${MIN_VERSION_LIMIT} and ${MAX_VERSION_LIMIT}.`
                  : undefined
              }
            >
              <Input
                type="number"
                value={versionLimitInput}
                placeholder={NOT_REPORTED}
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

        {/*
          FR-E02 / task 051. The blast radius is stated on the control itself, not buried in docs.

          This is the ONLY storage ceiling Graph offers, and it is type-wide: one value governs every
          container of this type. An admin arriving from a specific container's detail page is very
          likely to think they are capping that container — they are capping all of them. Graph has no
          per-container ceiling at all: `fileStorageContainerSettings` carries no storage property on
          either API version, and a container-scope PATCH returns 200 while silently discarding the
          value (measured live 2026-08-27 — notes/task-051-findings.md §1).
        */}
        <Field
          label="Maximum Storage per Container (GB)"
          hint={`Current: ${formatBytes(settings.maxStoragePerContainerInBytes)}. Leave blank to leave unchanged.`}
        >
          <Input
            type="number"
            value={storageGbInput}
            placeholder={NOT_REPORTED}
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

        <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalS }}>
          <MessageBarBody>
            <MessageBarTitle>This limit applies to every container of this type</MessageBarTitle>
            It is not a per-container setting. Changing it changes the ceiling for all existing and
            future containers of this container type. SharePoint Embedded does not support giving
            individual containers different storage limits.
          </MessageBarBody>
        </MessageBar>
      </div>

      <Divider />

      {/* ── Section: Search & Discoverability ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Search &amp; Discoverability
        </Text>

        <TriStateSwitch
          label="Enable Item Search"
          description="Allow full-text search of files stored in containers of this type."
          value={settings.isSearchEnabled}
          onToggle={v => set("isSearchEnabled", v)}
          disabled={disabled}
          styles={styles}
        />

        <TriStateSwitch
          label="Enable Discoverability"
          description="Whether containers of this type are discoverable. Together with Item Search this governs whether content is findable at all."
          value={settings.isDiscoverabilityEnabled}
          onToggle={v => set("isDiscoverabilityEnabled", v)}
          disabled={disabled}
          styles={styles}
        />
      </div>

      <Divider />

      {/* ── Section: Addressing ── */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Addressing
        </Text>

        <Field
          label="URL Template"
          hint="Applied to containers of this type. Leave blank to leave unchanged."
        >
          <Input
            value={settings.urlTemplate ?? ""}
            placeholder={NOT_REPORTED}
            onChange={(_e, d) => set("urlTemplate", d.value === "" ? undefined : d.value)}
            disabled={disabled}
            aria-label="URL template"
          />
        </Field>
      </div>

      <Divider />

      {/*
        ── Section: Read-only ──

        Both of these RENDER (FR-C07 AC-1 asks the nine to render) but are deliberately not editable:

        • consumingTenantOverridables is override PERMISSION metadata, not a setting value. It is a
          comma-delimited flag string whose members include values outside the SDK's enum, so a free
          text box would let a typo silently widen or revoke what a consuming tenant may override.
          Task 026 owns rendering its meaning, in prose, above this form.
        • isOfficeRestricted is beta-only — absent from the v1.0 schema this screen writes through,
          so there is nothing to write it with.
      */}
      <div className={styles.section}>
        <Text weight="semibold" size={300} className={styles.sectionTitle}>
          Reported by Graph (read-only)
        </Text>

        <Field
          label="Consuming Tenant Overridables"
          hint="Which settings a consuming tenant may override. Override permission metadata, not a value — edited on the container type registration, not here."
        >
          <Text className={styles.readOnlyValue}>
            {settings.consumingTenantOverridables && settings.consumingTenantOverridables.length > 0
              ? settings.consumingTenantOverridables
              : <span className={styles.muted}>{NOT_REPORTED}</span>}
          </Text>
        </Field>

        <Field
          label="Office Restricted"
          hint="Beta-only property. Absent from the v1.0 schema this form writes through, so it is shown but cannot be changed here."
        >
          <Text className={styles.readOnlyValue}>
            {settings.isOfficeRestricted === undefined
              ? <span className={styles.muted}>{NOT_REPORTED}</span>
              : settings.isOfficeRestricted
                ? "Yes"
                : "No"}
          </Text>
        </Field>
      </div>
    </div>
  );
};
