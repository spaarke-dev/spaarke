/**
 * KanbanCard — Generic, slot-based card primitive for use inside KanbanBoard.
 *
 * Domain-agnostic shell. Consumers compose the score circle, title, metadata
 * rows, and actions (e.g., pin button) via slot props. The visual design
 * (left accent border, score-circle anchor, title row, metadata rows, right
 * actions column, hover/focus states) is preserved exactly from the
 * SmartTodo-local card per the R2 baseline (NFR-10).
 *
 * Hoisted per smart-todo-decoupling-r3 task 010 (NFR-02 + FR-08).
 *
 * Migration: consumers previously holding an `<IEvent>`-specific KanbanCard
 * convert to this primitive by computing slot content in the parent (where the
 * data shape is known) and passing it down. See
 * `src/solutions/SmartTodo/src/components/KanbanCard.tsx` for a worked
 * adapter that wraps this primitive with `IEvent`-specific rendering.
 *
 * Design constraints:
 *   - ALL colours from Fluent UI v9 semantic tokens (ADR-021)
 *   - makeStyles (Griffel) only for custom styles
 *   - Dark mode + high-contrast supported via token system
 *   - Keyboard accessible (tabIndex + Enter/Space activate onClick)
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import type { IKanbanCardProps } from './types';

// ---------------------------------------------------------------------------
// Styles (preserved exactly from the SmartTodo-local KanbanCard.tsx baseline)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
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

  scoreWrapper: {
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '40px',
    height: '40px',
  },

  contentColumn: {
    flex: '1 1 0',
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },

  actionsColumn: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-end',
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Generic Kanban card primitive.
 *
 * @example
 *   <KanbanCard
 *     accentColor={col.accentColor}
 *     isSelected={isSelected}
 *     isCompleted={isCompleted}
 *     onClick={() => onSelect(itemId)}
 *     ariaLabel={`Task ${title}. Due Mar 5.`}
 *     scoreSlot={<div style={{ ...scoreColors }}>{score}</div>}
 *     titleSlot={<span>{title}</span>}
 *     metadataSlot={
 *       <div>
 *         <span>Due: Mar 5</span>
 *         <span>Assigned: J. Smith</span>
 *       </div>
 *     }
 *     actionsSlot={<PinButton pinned={isPinned} onToggle={onPinToggle} />}
 *   />
 */
export const KanbanCard: React.FC<IKanbanCardProps> = React.memo(
  ({
    scoreSlot,
    titleSlot,
    metadataSlot,
    actionsSlot,
    accentColor,
    isSelected = false,
    isCompleted = false,
    onClick,
    ariaLabel,
  }) => {
    const styles = useStyles();

    const handleClick = React.useCallback(() => {
      onClick?.();
    }, [onClick]);

    const handleKeyDown = React.useCallback(
      (ev: React.KeyboardEvent<HTMLDivElement>) => {
        if (ev.key === 'Enter' || ev.key === ' ') {
          ev.preventDefault();
          handleClick();
        }
      },
      [handleClick]
    );

    const cardClassName = [
      styles.card,
      isSelected ? styles.cardSelected : '',
      isCompleted ? styles.cardCompleted : '',
    ]
      .filter(Boolean)
      .join(' ');

    // Left accent border — runtime colour prop, so inline style is necessary
    const accentStyle: React.CSSProperties | undefined = accentColor
      ? {
          borderLeftWidth: '3px',
          borderLeftStyle: 'solid',
          borderLeftColor: accentColor,
        }
      : undefined;

    return (
      <div
        className={cardClassName}
        style={accentStyle}
        role="listitem"
        tabIndex={0}
        aria-label={ariaLabel}
        aria-selected={isSelected}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
      >
        {scoreSlot != null && (
          <div className={styles.scoreWrapper} aria-hidden="true">
            {scoreSlot}
          </div>
        )}

        <div className={styles.contentColumn}>
          {titleSlot}
          {metadataSlot}
        </div>

        {actionsSlot != null && (
          <div className={styles.actionsColumn}>{actionsSlot}</div>
        )}
      </div>
    );
  }
);

KanbanCard.displayName = 'KanbanCard';
