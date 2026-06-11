/**
 * SelectionAwareToolbar — styles (Griffel makeStyles + Fluent v9 tokens)
 *
 * Per ADR-021: no hard-coded colors, no inline styles, no CSS modules.
 */
import { makeStyles, shorthands, tokens } from '@fluentui/react-components';

export const useSelectionAwareToolbarStyles = makeStyles({
  /** Outer toolbar row — flex, centered, subtle bottom border. */
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    minHeight: '36px',
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalM),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },

  /** "N selected" leading label. */
  countLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginRight: tokens.spacingHorizontalXS,
  },

  /** Action button group container. */
  actionGroup: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
});
