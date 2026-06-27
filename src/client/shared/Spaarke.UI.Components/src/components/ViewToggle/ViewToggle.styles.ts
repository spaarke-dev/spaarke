/**
 * ViewToggle — styles (Griffel makeStyles + Fluent v9 tokens)
 *
 * Per ADR-021: no hard-coded colors, no inline styles, no CSS modules.
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useViewToggleStyles = makeStyles({
  /** Outer segmented-control group. */
  group: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '0',
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.overflow('hidden'),
  },

  /** Each segment button. */
  segment: {
    minWidth: 'auto',
    // Strip the per-button border so the group's own border is the only
    // visible edge. Reset radii so the two buttons share the same group radius.
    ...shorthands.border('0'),
    ...shorthands.borderRadius('0'),
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },

  /** Selected segment receives a subtle accent fill. */
  segmentSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    color: tokens.colorNeutralForeground1Selected,
  },
});
