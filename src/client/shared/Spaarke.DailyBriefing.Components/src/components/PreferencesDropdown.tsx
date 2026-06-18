/**
 * PreferencesDropdown — Gear icon button that opens a Fluent Popover
 * with Daily Digest preference controls.
 *
 * Replaces the collapsible PreferencesPanel with a compact dropdown
 * triggered from the header area.
 *
 * Features:
 * - Switch per channel (opt-out toggles): enabled by default, user disables
 * - Dropdown per configurable parameter: dueWithinDays, timeWindow, minConfidence
 * - Auto-open on workspace launch toggle
 * - Changes persist to sprk_userpreference via onUpdatePreferences callback
 *
 * ADR-021: Fluent v9 components only, design tokens for theming, dark mode support.
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/PreferencesDropdown.tsx` is now
 * a re-export shim pending full cleanup in R2 task 017.
 *
 * INTERIM IMPORT NOTE (R2 task 011 only): `types/notifications` and
 * `CHANNEL_REGISTRY` still live in the standalone DailyBriefing solution.
 * They will be hoisted in R2 task 014/015. Until then, this component reaches
 * back across the package boundary via a relative path — intentional,
 * temporary debt documented in the task POML step 3.
 */

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
  Button,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  Divider,
} from "@fluentui/react-components";
import {
  Settings20Regular,
  CheckmarkCircleFilled,
  ErrorCircleFilled,
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
  surface: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalM,
    width: "280px",
    maxHeight: "420px",
    overflowY: "auto",
  },
  title: {
    marginBottom: tokens.spacingVerticalXS,
  },
  section: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
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
  autoPopupRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingLeft: tokens.spacingHorizontalS,
  },
  saveIndicator: {
    color: tokens.colorNeutralForeground3,
    textAlign: "right" as const,
    paddingTop: tokens.spacingVerticalXS,
  },
  // ai-spaarke-ai-workspace-UI-r1 #2 (2026-06-08): footer row with Save button
  // + status message. Replaces the auto-save-on-every-change pattern with an
  // explicit Save + visible confirmation per operator feedback.
  footerRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  statusMessage: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
  },
  statusSaved: {
    color: tokens.colorStatusSuccessForeground1,
  },
  statusSaving: {
    color: tokens.colorNeutralForeground3,
  },
  statusError: {
    color: tokens.colorStatusDangerForeground1,
  },
});

// ---------------------------------------------------------------------------
// Dropdown option definitions (same as PreferencesPanel)
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

export interface PreferencesDropdownProps {
  /** Current user preferences (loaded or defaults). */
  preferences: DailyDigestPreferences;
  /** Callback to save updated preferences. Receives partial update. */
  onUpdatePreferences: (update: Partial<DailyDigestPreferences>) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const PreferencesDropdown: React.FC<PreferencesDropdownProps> = ({
  preferences,
  onUpdatePreferences,
}) => {
  const styles = useStyles();

  // ai-spaarke-ai-workspace-UI-r1 #2 (2026-06-08): explicit-save model.
  // Replaces auto-save-on-every-change with a pending-state buffer + explicit
  // Save button. Operator feedback: "need to expose Preferences and add a
  // save button with confirm that saved." The previous auto-save UX had no
  // visible confirmation that work was persisted.
  //
  // pendingPrefs holds the user's in-flight edits; preferences (prop) holds
  // the last known persisted state. They diverge while the user is editing
  // and re-converge on a successful Save.
  const [pendingPrefs, setPendingPrefs] = React.useState<DailyDigestPreferences>(
    preferences,
  );
  const [status, setStatus] = React.useState<
    'idle' | 'saving' | 'saved' | 'error'
  >('idle');
  const statusTimerRef = React.useRef<number | null>(null);

  // Sync pendingPrefs from prop when the parent fetches fresh state (e.g.
  // after a successful save or a background refresh). Only resets when the
  // user has no unsaved edits to avoid clobbering work in progress.
  React.useEffect(() => {
    setPendingPrefs(prev =>
      JSON.stringify(prev) === JSON.stringify(preferences) ? prev : preferences,
    );
    // We want this effect to fire ONLY when the prop changes (not when
    // pendingPrefs changes internally).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preferences]);

  React.useEffect(() => {
    return () => {
      if (statusTimerRef.current !== null) {
        window.clearTimeout(statusTimerRef.current);
      }
    };
  }, []);

  const isDirty = React.useMemo(
    () => JSON.stringify(pendingPrefs) !== JSON.stringify(preferences),
    [pendingPrefs, preferences],
  );

  const scheduleStatusClear = React.useCallback((ms: number) => {
    if (statusTimerRef.current !== null) {
      window.clearTimeout(statusTimerRef.current);
    }
    statusTimerRef.current = window.setTimeout(() => {
      setStatus('idle');
      statusTimerRef.current = null;
    }, ms);
  }, []);

  // -----------------------------------------------------------------------
  // Local-state handlers (no longer persist immediately)
  // -----------------------------------------------------------------------

  const handleChannelToggle = React.useCallback(
    (category: NotificationCategory, checked: boolean) => {
      setPendingPrefs(prev => {
        const currentDisabled = prev.disabledChannels;
        const updated: NotificationCategory[] = checked
          ? currentDisabled.filter(c => c !== category)
          : currentDisabled.includes(category)
            ? currentDisabled
            : [...currentDisabled, category];
        return { ...prev, disabledChannels: updated };
      });
    },
    [],
  );

  const handleDueWindowChange = React.useCallback(
    (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = Number(data.optionValue) as DueWindowDays;
      setPendingPrefs(prev => ({ ...prev, dueWithinDays: value }));
    },
    [],
  );

  const handleTimeWindowChange = React.useCallback(
    (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = data.optionValue as TimeWindow;
      setPendingPrefs(prev => ({ ...prev, timeWindow: value }));
    },
    [],
  );

  const handleConfidenceChange = React.useCallback(
    (_event: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      const value = Number(data.optionValue) as AiConfidenceThreshold;
      setPendingPrefs(prev => ({ ...prev, minConfidence: value }));
    },
    [],
  );

  const handleAutoPopupToggle = React.useCallback(
    (_ev: unknown, data: { checked: boolean }) => {
      setPendingPrefs(prev => ({ ...prev, autoPopup: data.checked }));
    },
    [],
  );

  // -----------------------------------------------------------------------
  // Save handler
  // -----------------------------------------------------------------------

  const handleSave = React.useCallback(async () => {
    // Compute partial diff so only changed keys are sent to the persistence
    // layer (preserves the existing onUpdatePreferences contract that accepts
    // Partial<DailyDigestPreferences>).
    const partial: Partial<DailyDigestPreferences> = {};
    const allKeys = Object.keys(pendingPrefs) as Array<
      keyof DailyDigestPreferences
    >;
    for (const k of allKeys) {
      if (JSON.stringify(pendingPrefs[k]) !== JSON.stringify(preferences[k])) {
        (partial as Record<string, unknown>)[k] = pendingPrefs[k];
      }
    }
    if (Object.keys(partial).length === 0) return; // nothing to save

    setStatus('saving');
    try {
      await onUpdatePreferences(partial);
      setStatus('saved');
      scheduleStatusClear(3000);
    } catch (err) {
      console.error('[PreferencesDropdown] save failed:', err);
      setStatus('error');
      scheduleStatusClear(6000);
    }
  }, [pendingPrefs, preferences, onUpdatePreferences, scheduleStatusClear]);

  // -----------------------------------------------------------------------
  // Render
  // -----------------------------------------------------------------------

  return (
    <Popover positioning="below-end" trapFocus>
      <PopoverTrigger disableButtonEnhancement>
        <Button
          appearance="subtle"
          icon={<Settings20Regular />}
          aria-label="Preferences"
        />
      </PopoverTrigger>

      <PopoverSurface className={styles.surface}>
        {/* Title */}
        <Subtitle2 className={styles.title}>Preferences</Subtitle2>

        {/* Channel toggles section */}
        <div className={styles.section}>
          <Caption1 className={styles.sectionTitle}>Channels</Caption1>
          {TOGGLEABLE_CHANNELS.map((channel) => {
            const isEnabled = !pendingPrefs.disabledChannels.includes(
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
            <Label htmlFor="pref-dd-due-window" className={styles.parameterLabel}>
              Due-soon window
            </Label>
            <Dropdown
              id="pref-dd-due-window"
              className={styles.dropdown}
              value={
                DUE_WINDOW_OPTIONS.find(
                  (o) => o.value === pendingPrefs.dueWithinDays
                )?.label ?? ""
              }
              selectedOptions={[String(pendingPrefs.dueWithinDays)]}
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
            <Label htmlFor="pref-dd-time-window" className={styles.parameterLabel}>
              Recency window
            </Label>
            <Dropdown
              id="pref-dd-time-window"
              className={styles.dropdown}
              value={
                TIME_WINDOW_OPTIONS.find(
                  (o) => o.value === pendingPrefs.timeWindow
                )?.label ?? ""
              }
              selectedOptions={[pendingPrefs.timeWindow]}
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
              htmlFor="pref-dd-confidence"
              className={styles.parameterLabel}
            >
              AI confidence threshold
            </Label>
            <Dropdown
              id="pref-dd-confidence"
              className={styles.dropdown}
              value={
                CONFIDENCE_OPTIONS.find(
                  (o) => o.value === pendingPrefs.minConfidence
                )?.label ?? ""
              }
              selectedOptions={[String(pendingPrefs.minConfidence)]}
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

        <Divider />

        {/* Auto-popup toggle */}
        <div className={styles.autoPopupRow}>
          <Body1>Auto-open on launch</Body1>
          <Switch
            checked={pendingPrefs.autoPopup}
            onChange={handleAutoPopupToggle}
            aria-label="Auto-open digest on workspace launch"
          />
        </div>

        {/* Footer: status message + explicit Save button.
            ai-spaarke-ai-workspace-UI-r1 #2 (2026-06-08). */}
        <div className={styles.footerRow}>
          <span
            className={`${styles.statusMessage} ${
              status === 'saved'
                ? styles.statusSaved
                : status === 'error'
                  ? styles.statusError
                  : styles.statusSaving
            }`}
            role="status"
            aria-live="polite"
          >
            {status === 'saving' && 'Saving…'}
            {status === 'saved' && (
              <>
                <CheckmarkCircleFilled />
                Saved
              </>
            )}
            {status === 'error' && (
              <>
                <ErrorCircleFilled />
                Save failed — try again
              </>
            )}
            {status === 'idle' && isDirty && (
              <Caption1>Unsaved changes</Caption1>
            )}
          </span>
          <Button
            appearance="primary"
            size="small"
            onClick={handleSave}
            disabled={!isDirty || status === 'saving'}
            aria-label="Save preferences"
          >
            Save
          </Button>
        </div>
      </PopoverSurface>
    </Popover>
  );
};
