import * as React from "react";
import {
  makeStyles,
  tokens,
  Switch,
  Dropdown,
  Option,
  Label,
  Body1,
  Caption1,
  Subtitle2,
  Divider,
  Button,
} from "@fluentui/react-components";
import {
  SettingsRegular,
  ChevronDownRegular,
  ChevronUpRegular,
} from "@fluentui/react-icons";
import type {
  DailyDigestPreferences,
  NotificationCategory,
  DueWindowDays,
  TimeWindow,
  AiConfidenceThreshold,
} from "../types/notifications";
import { CHANNEL_REGISTRY } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
    overflow: "hidden",
  },
  headerButton: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    width: "100%",
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: "transparent",
    border: "none",
    cursor: "pointer",
    color: tokens.colorNeutralForeground1,
    "&:hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  headerLeft: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  headerIcon: {
    fontSize: "20px",
    color: tokens.colorNeutralForeground2,
  },
  content: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: `0 ${tokens.spacingHorizontalM} ${tokens.spacingVerticalM}`,
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXS,
  },
  channelRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingLeft: tokens.spacingHorizontalS,
  },
  parameterRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
  },
  parameterLabel: {
    color: tokens.colorNeutralForeground2,
  },
  dropdown: {
    minWidth: "160px",
  },
  saveIndicator: {
    color: tokens.colorNeutralForeground3,
    textAlign: "right" as const,
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Dropdown option definitions
// ---------------------------------------------------------------------------

const DUE_WINDOW_OPTIONS: { value: DueWindowDays; label: string }[] = [
  { value: 1, label: "1 day" },
  { value: 2, label: "2 days" },
  { value: 3, label: "3 days" },
  { value: 5, label: "5 days" },
  { value: 7, label: "7 days" },
];

const TIME_WINDOW_OPTIONS: { value: TimeWindow; label: string }[] = [
  { value: "12h", label: "12 hours" },
  { value: "24h", label: "24 hours" },
  { value: "48h", label: "48 hours" },
  { value: "7d", label: "7 days" },
];

const CONFIDENCE_OPTIONS: { value: AiConfidenceThreshold; label: string }[] = [
  { value: 60, label: "60% (more results)" },
  { value: 75, label: "75% (balanced)" },
  { value: 85, label: "85% (higher quality)" },
  { value: 95, label: "95% (highest quality)" },
];

/**
 * Channel entries sorted by display order for the toggle list.
 * Excludes "system" since system notifications cannot be disabled.
 */
const TOGGLEABLE_CHANNELS = Object.values(CHANNEL_REGISTRY)
  .filter((ch) => ch.category !== "system")
  .sort((a, b) => a.order - b.order);

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PreferencesPanelProps {
  /** Current user preferences (loaded or defaults). */
  preferences: DailyDigestPreferences;
  /** Callback to save updated preferences. Receives partial update. */
  onUpdatePreferences: (update: Partial<DailyDigestPreferences>) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * PreferencesPanel — Expandable settings section for Daily Digest.
 *
 * Features:
 * - Switch per channel (opt-out toggles): enabled by default, user disables
 * - Dropdown per configurable parameter: dueWithinDays, timeWindow, minConfidence
 * - Changes persist to sprk_userpreference via onUpdatePreferences callback
 * - Collapsible panel (starts collapsed)
 *
 * ADR-021: Fluent v9 components only, design tokens for theming, dark mode support.
 */
export const PreferencesPanel: React.FC<PreferencesPanelProps> = ({
  preferences,
  onUpdatePreferences,
}) => {
  const styles = useStyles();
  const [expanded, setExpanded] = React.useState(false);
  const [saving, setSaving] = React.useState(false);

  // -----------------------------------------------------------------------
  // Channel toggle handler
  // -----------------------------------------------------------------------

  const handleChannelToggle = React.useCallback(
    async (category: NotificationCategory, checked: boolean) => {
      const currentDisabled = preferences.disabledChannels;
      let updated: NotificationCategory[];

      if (checked) {
        // Enabling channel: remove from disabled list
        updated = currentDisabled.filter((c) => c !== category);
      } else {
        // Disabling channel: add to disabled list
        updated = currentDisabled.includes(category)
          ? currentDisabled
          : [...currentDisabled, category];
      }

      setSaving(true);
      try {
        await onUpdatePreferences({ disabledChannels: updated });
      } finally {
        setSaving(false);
      }
    },
    [preferences.disabledChannels, onUpdatePreferences]
  );

  // -----------------------------------------------------------------------
  // Parameter dropdown handlers
  // -----------------------------------------------------------------------

  const handleDueWindowChange = React.useCallback(
    async (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = Number(data.optionValue) as DueWindowDays;
      setSaving(true);
      try {
        await onUpdatePreferences({ dueWithinDays: value });
      } finally {
        setSaving(false);
      }
    },
    [onUpdatePreferences]
  );

  const handleTimeWindowChange = React.useCallback(
    async (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = data.optionValue as TimeWindow;
      setSaving(true);
      try {
        await onUpdatePreferences({ timeWindow: value });
      } finally {
        setSaving(false);
      }
    },
    [onUpdatePreferences]
  );

  const handleConfidenceChange = React.useCallback(
    async (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = Number(data.optionValue) as AiConfidenceThreshold;
      setSaving(true);
      try {
        await onUpdatePreferences({ minConfidence: value });
      } finally {
        setSaving(false);
      }
    },
    [onUpdatePreferences]
  );

  // -----------------------------------------------------------------------
  // Render
  // -----------------------------------------------------------------------

  return (
    <div className={styles.root}>
      {/* Collapsible header */}
      <Button
        appearance="transparent"
        className={styles.headerButton}
        onClick={() => setExpanded((prev) => !prev)}
        aria-expanded={expanded}
        aria-controls="preferences-content"
      >
        <span className={styles.headerLeft}>
          <SettingsRegular className={styles.headerIcon} />
          <Subtitle2>Preferences</Subtitle2>
        </span>
        {expanded ? <ChevronUpRegular /> : <ChevronDownRegular />}
      </Button>

      {/* Expandable content */}
      {expanded && (
        <div id="preferences-content" className={styles.content}>
          {/* Channel toggles section */}
          <div className={styles.section}>
            <Caption1 className={styles.sectionTitle}>
              Notification Channels
            </Caption1>
            {TOGGLEABLE_CHANNELS.map((channel) => {
              const isEnabled = !preferences.disabledChannels.includes(
                channel.category
              );
              return (
                <div key={channel.category} className={styles.channelRow}>
                  <Body1>{channel.label}</Body1>
                  <Switch
                    checked={isEnabled}
                    onChange={(_ev, data) =>
                      handleChannelToggle(channel.category, data.checked)
                    }
                    aria-label={`${channel.label} notifications`}
                  />
                </div>
              );
            })}
          </div>

          <Divider />

          {/* Parameter dropdowns section */}
          <div className={styles.section}>
            <Caption1 className={styles.sectionTitle}>
              Display Parameters
            </Caption1>

            {/* Due window */}
            <div className={styles.parameterRow}>
              <Label htmlFor="pref-due-window" className={styles.parameterLabel}>
                Due-soon window
              </Label>
              <Dropdown
                id="pref-due-window"
                className={styles.dropdown}
                value={
                  DUE_WINDOW_OPTIONS.find(
                    (o) => o.value === preferences.dueWithinDays
                  )?.label ?? ""
                }
                selectedOptions={[String(preferences.dueWithinDays)]}
                onOptionSelect={handleDueWindowChange}
                aria-label="Due-soon window"
              >
                {DUE_WINDOW_OPTIONS.map((opt) => (
                  <Option key={opt.value} value={String(opt.value)}>
                    {opt.label}
                  </Option>
                ))}
              </Dropdown>
            </div>

            {/* Time window */}
            <div className={styles.parameterRow}>
              <Label htmlFor="pref-time-window" className={styles.parameterLabel}>
                Recency window
              </Label>
              <Dropdown
                id="pref-time-window"
                className={styles.dropdown}
                value={
                  TIME_WINDOW_OPTIONS.find(
                    (o) => o.value === preferences.timeWindow
                  )?.label ?? ""
                }
                selectedOptions={[preferences.timeWindow]}
                onOptionSelect={handleTimeWindowChange}
                aria-label="Recency time window"
              >
                {TIME_WINDOW_OPTIONS.map((opt) => (
                  <Option key={opt.value} value={opt.value}>
                    {opt.label}
                  </Option>
                ))}
              </Dropdown>
            </div>

            {/* AI confidence threshold */}
            <div className={styles.parameterRow}>
              <Label
                htmlFor="pref-confidence"
                className={styles.parameterLabel}
              >
                AI confidence threshold
              </Label>
              <Dropdown
                id="pref-confidence"
                className={styles.dropdown}
                value={
                  CONFIDENCE_OPTIONS.find(
                    (o) => o.value === preferences.minConfidence
                  )?.label ?? ""
                }
                selectedOptions={[String(preferences.minConfidence)]}
                onOptionSelect={handleConfidenceChange}
                aria-label="AI confidence threshold"
              >
                {CONFIDENCE_OPTIONS.map((opt) => (
                  <Option key={opt.value} value={String(opt.value)}>
                    {opt.label}
                  </Option>
                ))}
              </Dropdown>
            </div>
          </div>

          {/* Save indicator */}
          {saving && (
            <Caption1 className={styles.saveIndicator}>Saving...</Caption1>
          )}
        </div>
      )}
    </div>
  );
};
