/**
 * TrackingFieldTrio — entity-agnostic shared core (FR-14, task 023).
 *
 * Renders and read/writes three tracking flags: a Monitor toggle, a High
 * Priority toggle, and an access-permission segmented picker. Lifted from
 * the `TrackingFieldTrio` PCF's `TrackingFieldTrioApp.tsx` into
 * `@spaarke/ui-components` so both the OOB-form PCF (React 16/17) and the
 * Phase-3 reading-pane tracking view (React 19, task 035) can consume ONE
 * shared component.
 *
 * Entity-agnostic (FR-14): the shared core bakes in NO `sprk_communication`
 * option integers, labels, or field-display strings. Every access-permission
 * segment (value + label + color) and every field label is passed IN via
 * props (`ITrackingFieldTrioProps`, see `types.ts`). The caller (the PCF's
 * `index.ts`) supplies its own entity-specific values.
 *
 * v1.0.1-v1.0.5 layout history preserved verbatim from the PCF original —
 * CSS Grid with EXPLICIT rows so captions and controls always line up:
 *   Row 1 (optional): caption | caption | caption
 *   Row 2:             Switch  | Switch  | segmented buttons
 * Grid columns: `1fr 1fr auto`. `alignItems: 'center'` in row 2 vertically
 * centers each control against its row.
 *
 * Standards: Fluent UI v9 tokens only (ADR-021, incl. dark mode — no
 * hardcoded light-only colors). No React-18/19-only runtime API and no
 * `as React.ComponentType` cast (NFR-05) — a plain `React.FC` using only
 * `useMemo`-free, React-16-safe primitives, so it renders under the PCF's
 * platform React 16/17 AND the code page's React 19.
 *
 * Row 3 (opt-in, task 040 / teams-app-r1) — a governance toolbar with a
 * person icon (opens the access-grant modal, task 041) and an email icon
 * (opens email-members, task 042). Each icon renders only when the caller
 * supplies the corresponding callback (`onOpenGrantModal` /
 * `onOpenEmailMembers`), so existing consumers see no change. This
 * component builds ONLY the toolbar shell + click affordances — the modal
 * and email-dialog contents are implemented by the caller in tasks 041/042.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Switch,
  Text,
  Button,
  Tooltip,
  shorthands,
  mergeClasses,
} from '@fluentui/react-components';
import { PersonRegular, MailRegular } from '@fluentui/react-icons';
import type { IAccessPermissionOption, ITrackingFieldTrioProps } from './types';

/**
 * Position-based (NOT value-keyed) fallback pale backgrounds used when a
 * caller-injected segment has no `color`. Indexed by the segment's POSITION
 * in the injected `accessPermissionOptions` array (idx % length), not by any
 * entity-specific option value — this is what makes the default legitimate
 * per FR-14 ("a `FALLBACK_SEGMENT_COLORS`-style default is allowed only as a
 * prop default, not entity-specific hardcoding"). Text always uses
 * `colorNeutralForeground1` (near-black) for consistent readability
 * regardless of tint, in both light and dark themes.
 */
const DEFAULT_SEGMENT_FALLBACK_COLORS: { bg: string; fg: string }[] = [
  { bg: tokens.colorPaletteLightGreenBackground2, fg: tokens.colorNeutralForeground1 },
  { bg: tokens.colorPaletteYellowBackground2, fg: tokens.colorNeutralForeground1 },
  { bg: tokens.colorPaletteRedBackground2, fg: tokens.colorNeutralForeground1 },
];

/**
 * Convert a hex color (e.g., "#00B050") to rgba with alpha, giving a pale
 * tint suitable for a segmented-button background. Falls back to the input
 * string when parsing fails (accepts named colors, rgba(), etc.).
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
 * Resolve the pale bg + readable fg for a selected segment. Prefers the
 * caller-injected per-option color (blended pale via alpha 0.28); falls back
 * to the position-based default palette, then a neutral Fluent token if the
 * palette runs out (more segments than default colors).
 */
function getSelectedSegmentColors(idx: number, option: IAccessPermissionOption): { bg: string; fg: string } {
  if (option.color) {
    return { bg: hexToRgba(option.color, 0.28), fg: tokens.colorNeutralForeground1 };
  }
  return (
    DEFAULT_SEGMENT_FALLBACK_COLORS[idx % DEFAULT_SEGMENT_FALLBACK_COLORS.length] || {
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
    // Positioning context for the governance toolbar, which sits top-right (task 073 UAT).
    position: 'relative',
    // Zero top/bottom padding so the caption row sits directly under the
    // form section title, matching the vertical rhythm of adjacent cards.
    ...shorthands.padding(0, tokens.spacingHorizontalXL),
  },
  caption: {
    // Matches the standard Dataverse form field label style: Segoe UI 14px,
    // regular weight, secondary foreground color.
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
  // Control header (task 073 UAT #3) — opt-in via the `title` prop. A full-width
  // 32px-tall row: title on the left (14px semibold), governance icons on the
  // right. Only rendered when a `title` is supplied.
  header: {
    gridColumn: '1 / -1',
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: '32px',
    columnGap: tokens.spacingHorizontalM,
    // Breathing room below the header/title/toolbar row before the field row
    // (owner UAT v1.0.20 follow-up).
    marginBottom: tokens.spacingVerticalS,
  },
  headerTitle: {
    // 14px semibold per owner UAT #3 (fontSizeBase300 = 14px, fontWeightSemibold
    // = "semi bold"); token-only so it resolves in light + dark (ADR-021).
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    lineHeight: tokens.lineHeightBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  headerActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  // Governance toolbar (person + email icons — task 040; repositioned task 073 UAT). Sits at the
  // TOP-RIGHT of the control box — mirroring the header-action placement of the Messages / Tasks &
  // Events cards — rather than as a bottom row. Absolute within the position:relative container. Icons
  // resolve color via Fluent's default `currentColor` fill (ADR-021) — no hardcoded colors here.
  toolbar: {
    position: 'absolute',
    top: 0,
    right: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalXS,
  },
  toolbarIconButton: {
    minWidth: 'auto',
    paddingLeft: 0,
    paddingRight: 0,
  },
});

export const TrackingFieldTrio: React.FC<ITrackingFieldTrioProps> = ({
  monitor,
  highPriority,
  accessPermission,
  title,
  showTitle = true,
  showVersion = false,
  versionText,
  accessPermissionOptions,
  monitorLabel,
  highPriorityLabel,
  accessPermissionLabel,
  onMonitorChange,
  onHighPriorityChange,
  onAccessPermissionChange,
  onOpenGrantModal,
  onOpenEmailMembers,
  canGrantAccess,
}) => {
  const styles = useStyles();
  // Fail-open only when the caller hasn't wired an access decision at all
  // (canGrantAccess omitted); an explicit `false` disables the icon.
  const grantEnabled = canGrantAccess !== false;

  // Governance icons (person + email) — rendered EITHER inside the opt-in header
  // row (when `title` is set, task 073 UAT #3) OR in the prior absolute
  // top-right toolbar (no title — unchanged for existing consumers).
  const toolbarIcons =
    onOpenGrantModal || onOpenEmailMembers ? (
      <>
        {onOpenGrantModal && (
          <Tooltip
            content={grantEnabled ? 'Grant access' : 'You do not have permission to grant access'}
            relationship="label"
          >
            <Button
              className={styles.toolbarIconButton}
              appearance="subtle"
              size="small"
              icon={<PersonRegular />}
              aria-label="Grant access"
              disabled={!grantEnabled}
              // No handler at all when disabled — a genuinely disabled Fluent
              // Button (not merely dimmed) with no dead click.
              onClick={grantEnabled ? onOpenGrantModal : undefined}
            />
          </Tooltip>
        )}
        {onOpenEmailMembers && (
          <Tooltip content="Email members" relationship="label">
            <Button
              className={styles.toolbarIconButton}
              appearance="subtle"
              size="small"
              icon={<MailRegular />}
              aria-label="Email members"
              onClick={onOpenEmailMembers}
            />
          </Tooltip>
        )}
      </>
    ) : null;

  return (
    <div className={styles.container}>
      {/* Control header (task 073 UAT #3, always-on since v1.0.20) — a 32px row
          with the optional title on the LEFT (14px semibold) and the governance
          icons (grant/email) on the RIGHT. Renders whenever there are icons OR a
          title, so the toolbar is ALWAYS in a proper header row rather than
          floating (owner UAT: "toolbars not in the title row"). When no title is
          set, an empty spacer keeps the icons right-aligned. */}
      {(title || toolbarIcons) && (
        <div className={styles.header}>
          {title ? <Text className={styles.headerTitle}>{title}</Text> : <span />}
          {toolbarIcons && <div className={styles.headerActions}>{toolbarIcons}</div>}
        </div>
      )}
      {/* Row 1 (only rendered when showTitle=true) — field labels, sourced
          from the caller's own field metadata (entity-agnostic). */}
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
        aria-label={accessPermissionLabel}
      >
        {accessPermissionOptions.map((opt, idx) => {
          const isSelected = accessPermission === opt.value;
          const selectedColors = isSelected ? getSelectedSegmentColors(idx, opt) : null;
          return (
            <Button
              key={opt.value}
              appearance="subtle"
              role="radio"
              aria-checked={isSelected}
              onClick={() => onAccessPermissionChange(opt.value)}
              className={mergeClasses(
                styles.segment,
                !isSelected && styles.segmentUnselected,
                idx > 0 && !isSelected && styles.segmentSeparator
              )}
              // Selected background/foreground come from the injected option's
              // color (pale via rgba(alpha=0.28)) or the position-based
              // fallback palette.
              style={selectedColors ? { backgroundColor: selectedColors.bg, color: selectedColors.fg } : undefined}
            >
              {opt.label}
            </Button>
          );
        })}
      </div>

      {showVersion && versionText && <span className={styles.versionFooter}>{versionText}</span>}
    </div>
  );
};
