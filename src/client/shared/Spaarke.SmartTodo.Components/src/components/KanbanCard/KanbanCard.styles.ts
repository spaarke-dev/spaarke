/**
 * KanbanCard styles — Fluent v9 + Griffel + semantic tokens (ADR-021).
 *
 * Hoisted from `src/solutions/SmartTodo/src/components/KanbanCard.tsx` per
 * R4 task 102 (E-1, 2026-06-18). Bit-for-bit visual parity with the Code
 * Page version — no token swaps, no layout tweaks. Behaviour is preserved
 * to keep the existing UAT-approved card unchanged after the hoist.
 *
 * @see ADR-012 Shared Component Library
 * @see ADR-021 Fluent UI v9 Design System
 */

import { makeStyles, tokens } from '@fluentui/react-components';

export const useKanbanCardStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: 'pointer',
    transitionProperty: 'background-color',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: '2px',
      outlineColor: tokens.colorBrandStroke1,
      outlineOffset: '-2px',
    },
  },

  cardSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Selected,
    },
  },

  cardCompleted: {
    opacity: '0.6',
  },

  scoreCircle: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
    borderRadius: '50%',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    lineHeight: '1',
  },

  contentColumn: {
    flex: '1 1 0',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },

  title: {
    display: 'block',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },

  titleCompleted: {
    textDecorationLine: 'line-through',
    textDecorationColor: tokens.colorNeutralForeground3,
    color: tokens.colorNeutralForeground3,
  },

  metadataRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },

  fieldLabel: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  fieldValue: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },

  actionsColumn: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    flexShrink: 0,
    gap: tokens.spacingVerticalXXS,
  },

  /**
   * Selection checkbox column — leading edge (upper-left per FR-27).
   * Always rendered to keep layout stable; visually hidden until hover or
   * when `isMultiSelected` is true (R4 task 060).
   */
  selectionColumn: {
    display: 'flex',
    flexShrink: 0,
    alignItems: 'flex-start',
    paddingTop: '2px',
  },
});
