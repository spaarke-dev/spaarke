/**
 * TrackingFieldTrioApp — the React root for the TrackingFieldTrio PCF.
 *
 * v1.0.1 layout — CSS Grid with EXPLICIT rows so captions and controls
 * always line up:
 *   Row 1 (optional): caption | caption | caption
 *   Row 2:             Switch  | Switch  | segmented buttons
 * Grid columns: `1fr 1fr auto`. `alignItems: 'center'` in row 2 vertically
 * centers each control against its row, so Switch (short) and buttons
 * (taller) end up on the same visual line.
 *
 * Standards: Fluent UI v9 tokens only (ADR-021), React 16 (ADR-022).
 */

import * as React from 'react';
import { makeStyles, tokens, Switch, Text, Button, shorthands, mergeClasses } from '@fluentui/react-components';

// Access Permission choice values — MUST match the Dataverse OptionSet values.
export const ACCESS_PERMISSION_STANDARD = 100000000;
export const ACCESS_PERMISSION_LIMITED = 100000001;
export const ACCESS_PERMISSION_RESTRICTED = 100000002;

const ACCESS_PERMISSION_SEGMENTS: { value: number; label: string }[] = [
  { value: ACCESS_PERMISSION_STANDARD, label: 'Standard' },
  { value: ACCESS_PERMISSION_LIMITED, label: 'Limited' },
  { value: ACCESS_PERMISSION_RESTRICTED, label: 'Restricted' },
];

export interface IAccessPermissionOption {
  value: number;
  label: string;
  /** Hex color from the Dataverse OptionSet metadata (e.g., "#00B050"). */
  color?: string;
}

export interface ITrackingFieldTrioAppProps {
  monitor: boolean;
  highPriority: boolean;
  accessPermission: number | null;
  /** v1.0.1 — show field labels above each control. Default true (v1.0.3). */
  showTitle: boolean;
  /** v1.0.1 — show version badge in bottom-right. Default false (hidden). */
  showVersion: boolean;
  /** v1.0.1 — per-option colors from the bound OptionSet metadata. */
  accessPermissionOptions: IAccessPermissionOption[];
  /** v1.0.4 — field display names sourced from Dataverse metadata. */
  monitorLabel: string;
  highPriorityLabel: string;
  accessPermissionLabel: string;
  onMonitorChange: (value: boolean) => void;
  onHighPriorityChange: (value: boolean) => void;
  onAccessPermissionChange: (value: number) => void;
}

// v1.0.1 — Fallback pale backgrounds when the bound OptionSet has no
// per-option colors defined. v1.0.2 — text always uses `colorNeutralForeground1`
// (near-black) for consistent readability regardless of tint.
const FALLBACK_SEGMENT_COLORS: Record<number, { bg: string; fg: string }> = {
  [ACCESS_PERMISSION_STANDARD]: {
    bg: tokens.colorPaletteLightGreenBackground2,
    fg: tokens.colorNeutralForeground1,
  },
  [ACCESS_PERMISSION_LIMITED]: {
    bg: tokens.colorPaletteYellowBackground2,
    fg: tokens.colorNeutralForeground1,
  },
  [ACCESS_PERMISSION_RESTRICTED]: {
    bg: tokens.colorPaletteRedBackground2,
    fg: tokens.colorNeutralForeground1,
  },
};

/**
 * v1.0.1 — Convert a hex color (e.g., "#00B050") to rgba with alpha, giving
 * a pale tint suitable for a segmented-button background. Falls back to the
 * input string when parsing fails (accepts named colors, rgba(), etc.).
 */
function hexToRgba(hex: string, alpha: number): string {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex.trim());
  if (!m) return hex;
  const r = parseInt(m[1], 16);
  const g = parseInt(m[2], 16);
  const b = parseInt(m[3], 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

/**
 * v1.0.1 — Resolve the pale bg + readable fg for a selected segment. Prefers
 * the Dataverse OptionSet per-option color (blended pale via alpha 0.28);
 * falls back to fluent tokens keyed on the well-known choice values.
 */
function getSelectedSegmentColors(value: number, options: IAccessPermissionOption[]): { bg: string; fg: string } {
  const opt = options.find(o => o.value === value);
  if (opt?.color) {
    // v1.0.2 — text always dark (`colorNeutralForeground1`) regardless of
    // option-set color, for consistent readability on pale backgrounds.
    return { bg: hexToRgba(opt.color, 0.28), fg: tokens.colorNeutralForeground1 };
  }
  return (
    FALLBACK_SEGMENT_COLORS[value] || {
      bg: tokens.colorNeutralBackground4,
      fg: tokens.colorNeutralForeground1,
    }
  );
}

const useStyles = makeStyles({
  container: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr auto',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    alignItems: 'center',
    width: '100%',
    boxSizing: 'border-box',
    // v1.0.3 — zero top/bottom padding so the caption row sits directly under
    // the form section title, matching the vertical rhythm of adjacent cards
    // (e.g., MATTER INFORMATION on the left).
    ...shorthands.padding(0, tokens.spacingHorizontalXL),
  },
  caption: {
    // v1.0.4 — match the standard Dataverse form field label style:
    // Segoe UI 14 px, regular weight, secondary foreground color.
    fontFamily: '"Segoe UI", system-ui, sans-serif',
    fontSize: '14px',
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
    lineHeight: '20px',
    alignSelf: 'end',
  },
  controlCell: {
    display: 'flex',
    alignItems: 'center',
    // Common min-height so Switch (~24px) and button group (~32px) stabilize
    // vertically within the row and their baselines line up.
    minHeight: '32px',
  },
  segmentGroup: {
    display: 'flex',
    flexDirection: 'row',
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.overflow('hidden'),
  },
  segment: {
    ...shorthands.borderRadius(0),
    ...shorthands.border(0),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    minWidth: '76px',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    minHeight: '28px',
  },
  segmentUnselected: {
    backgroundColor: tokens.colorNeutralBackground4,
    color: tokens.colorNeutralForeground2,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground4Hover,
      color: tokens.colorNeutralForeground2,
    },
  },
  segmentSeparator: {
    borderLeftWidth: '1px',
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
  },
  versionFooter: {
    gridColumn: '1 / -1',
    fontSize: '10px',
    color: tokens.colorNeutralForeground4,
    textAlign: 'right',
    marginTop: tokens.spacingVerticalXS,
  },
});

export const TrackingFieldTrioApp: React.FC<ITrackingFieldTrioAppProps> = ({
  monitor,
  highPriority,
  accessPermission,
  showTitle,
  showVersion,
  accessPermissionOptions,
  monitorLabel,
  highPriorityLabel,
  accessPermissionLabel,
  onMonitorChange,
  onHighPriorityChange,
  onAccessPermissionChange,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.container}>
      {/* Row 1 (only rendered when showTitle=true) — field labels.
          v1.0.4 — labels come from Dataverse field metadata (display name),
          falling back to the hardcoded English defaults in index.ts. */}
      {showTitle && (
        <>
          <Text className={styles.caption}>{monitorLabel}</Text>
          <Text className={styles.caption}>{highPriorityLabel}</Text>
          <Text className={styles.caption}>{accessPermissionLabel}</Text>
        </>
      )}

      {/* Row 2 (or row 1 when captions hidden) — controls */}
      <div className={styles.controlCell}>
        <Switch
          checked={monitor}
          onChange={(_, data) => onMonitorChange(data.checked)}
          label={monitor ? 'Yes' : 'No'}
          labelPosition="after"
        />
      </div>
      <div className={styles.controlCell}>
        <Switch
          checked={highPriority}
          onChange={(_, data) => onHighPriorityChange(data.checked)}
          label={highPriority ? 'Yes' : 'No'}
          labelPosition="after"
        />
      </div>
      <div
        className={mergeClasses(styles.controlCell, styles.segmentGroup)}
        role="radiogroup"
        aria-label="Access Permission"
      >
        {ACCESS_PERMISSION_SEGMENTS.map((seg, idx) => {
          const isSelected = accessPermission === seg.value;
          const selectedColors = isSelected ? getSelectedSegmentColors(seg.value, accessPermissionOptions) : null;
          return (
            <Button
              key={seg.value}
              appearance="subtle"
              role="radio"
              aria-checked={isSelected}
              onClick={() => onAccessPermissionChange(seg.value)}
              className={mergeClasses(
                styles.segment,
                !isSelected && styles.segmentUnselected,
                idx > 0 && !isSelected && styles.segmentSeparator
              )}
              // Selected background/foreground come from OptionSet metadata
              // (pale via rgba(alpha=0.28)) or the fallback fluent tokens.
              style={selectedColors ? { backgroundColor: selectedColors.bg, color: selectedColors.fg } : undefined}
            >
              {seg.label}
            </Button>
          );
        })}
      </div>

      {showVersion && <span className={styles.versionFooter}>v1.0.4 • Built 2026-06-30</span>}
    </div>
  );
};
