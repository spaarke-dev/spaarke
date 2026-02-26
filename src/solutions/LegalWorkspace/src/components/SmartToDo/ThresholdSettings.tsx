/**
 * ThresholdSettings — Fluent v9 Popover for configuring Kanban column thresholds.
 *
 * Lets users adjust the "Today" and "Tomorrow" score thresholds that determine
 * how to-do items are assigned to Kanban columns based on their To Do Score.
 *
 * The component renders a controlled Popover. The parent passes a trigger element
 * as `children` (typically a settings icon button). Local state is synced from
 * props.preferences each time the popover opens, so unsaved edits are discarded
 * on close.
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens — zero hardcoded hex/rgb
 *   - makeStyles (Griffel) for all custom styles
 *   - Support light, dark, and high-contrast modes (automatic via token system)
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Popover,
  PopoverTrigger,
  PopoverSurface,
  SpinButton,
  Field,
} from "@fluentui/react-components";
import type {
  SpinButtonChangeEvent,
  SpinButtonOnChangeData,
} from "@fluentui/react-components";
import {
  DEFAULT_TODAY_THRESHOLD,
  DEFAULT_TOMORROW_THRESHOLD,
  ITodoKanbanPreferences,
} from "../../hooks/useUserPreferences";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  surface: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingHorizontalL,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    minWidth: "280px",
  },
  fieldRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: tokens.spacingHorizontalM,
  },
  footer: {
    display: "flex",
    flexDirection: "row",
    justifyContent: "space-between",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
  },
  validationError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IThresholdSettingsProps {
  /** Controlled open state. */
  open: boolean;
  /** Called when open state should change. */
  onOpenChange: (open: boolean) => void;
  /** Current preferences. */
  preferences: ITodoKanbanPreferences;
  /** Called when user saves new thresholds. */
  onSave: (prefs: ITodoKanbanPreferences) => void;
  /** The trigger element (settings button). */
  children: React.ReactElement;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

const ThresholdSettings: React.FC<IThresholdSettingsProps> = ({
  open,
  onOpenChange,
  preferences,
  onSave,
  children,
}) => {
  const styles = useStyles();

  // ── Local state ──────────────────────────────────────────────────────────
  const [todayValue, setTodayValue] = React.useState<number>(
    preferences.todayThreshold
  );
  const [tomorrowValue, setTomorrowValue] = React.useState<number>(
    preferences.tomorrowThreshold
  );
  const [validationError, setValidationError] = React.useState<string | null>(
    null
  );

  // Sync local state from props each time the popover opens
  React.useEffect(() => {
    if (open) {
      setTodayValue(preferences.todayThreshold);
      setTomorrowValue(preferences.tomorrowThreshold);
      setValidationError(null);
    }
  }, [open, preferences.todayThreshold, preferences.tomorrowThreshold]);

  // ── Validation ───────────────────────────────────────────────────────────

  const validate = React.useCallback(
    (today: number, tomorrow: number): string | null => {
      if (today <= tomorrow) {
        return "Today threshold must be greater than Tomorrow threshold.";
      }
      return null;
    },
    []
  );

  // ── SpinButton handlers ──────────────────────────────────────────────────

  const handleTodayChange = React.useCallback(
    (_event: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
      const newValue =
        data.value ?? (data.displayValue ? Number(data.displayValue) : todayValue);

      // Guard against NaN from invalid display input
      const safeValue = Number.isNaN(newValue) ? todayValue : newValue;
      setTodayValue(safeValue);
      setValidationError(validate(safeValue, tomorrowValue));
    },
    [todayValue, tomorrowValue, validate]
  );

  const handleTomorrowChange = React.useCallback(
    (_event: SpinButtonChangeEvent, data: SpinButtonOnChangeData) => {
      const newValue =
        data.value ?? (data.displayValue ? Number(data.displayValue) : tomorrowValue);

      const safeValue = Number.isNaN(newValue) ? tomorrowValue : newValue;
      setTomorrowValue(safeValue);
      setValidationError(validate(todayValue, safeValue));
    },
    [todayValue, tomorrowValue, validate]
  );

  // ── Reset to Defaults ────────────────────────────────────────────────────

  const handleReset = React.useCallback(() => {
    setTodayValue(DEFAULT_TODAY_THRESHOLD);
    setTomorrowValue(DEFAULT_TOMORROW_THRESHOLD);
    setValidationError(null);
  }, []);

  // ── Save ─────────────────────────────────────────────────────────────────

  const handleSave = React.useCallback(() => {
    onSave({
      todayThreshold: todayValue,
      tomorrowThreshold: tomorrowValue,
    });
    onOpenChange(false);
  }, [todayValue, tomorrowValue, onSave, onOpenChange]);

  // ── Render ───────────────────────────────────────────────────────────────

  return (
    <Popover
      open={open}
      onOpenChange={(_event, data) => onOpenChange(data.open)}
      positioning="below-end"
    >
      <PopoverTrigger disableButtonEnhancement>
        {children}
      </PopoverTrigger>

      <PopoverSurface className={styles.surface}>
        {/* Title */}
        <Text weight="semibold" size={400}>
          Column Thresholds
        </Text>

        {/* Description */}
        <Text size={200}>
          Items are assigned to Kanban columns based on their To Do Score.
        </Text>

        {/* Today threshold */}
        <Field label="Today threshold">
          <div className={styles.fieldRow}>
            <SpinButton
              value={todayValue}
              onChange={handleTodayChange}
              min={0}
              max={100}
              step={5}
              aria-label="Today threshold score"
            />
          </div>
        </Field>

        {/* Tomorrow threshold */}
        <Field label="Tomorrow threshold">
          <div className={styles.fieldRow}>
            <SpinButton
              value={tomorrowValue}
              onChange={handleTomorrowChange}
              min={0}
              max={100}
              step={5}
              aria-label="Tomorrow threshold score"
            />
          </div>
        </Field>

        {/* Validation error */}
        {validationError && (
          <Text className={styles.validationError} role="alert">
            {validationError}
          </Text>
        )}

        {/* Footer: Reset + Save */}
        <div className={styles.footer}>
          <Button appearance="subtle" onClick={handleReset}>
            Reset to Defaults
          </Button>
          <Button
            appearance="primary"
            onClick={handleSave}
            disabled={validationError !== null}
          >
            Save
          </Button>
        </div>
      </PopoverSurface>
    </Popover>
  );
};

ThresholdSettings.displayName = "ThresholdSettings";

export default React.memo(ThresholdSettings);

// Also export as named export for barrel file convenience
export const ThresholdSettingsPopover = React.memo(ThresholdSettings);
ThresholdSettingsPopover.displayName = "ThresholdSettings";
