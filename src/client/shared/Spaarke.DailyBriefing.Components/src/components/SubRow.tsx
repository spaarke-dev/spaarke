/**
 * SubRow -- single indented row in the NarrativeBullet sub-list (FR-11..FR-14).
 *
 * Rendered once per underlying `NotificationItem` when an aggregated
 * `NarrativeBullet` has `itemIds.length > 1`. Composes three slot
 * sub-components, each owned by a separate Wave-9 task to enable
 * parallel-safe development:
 *
 *   - <SubRowLink /> -- task 021 (per-item entity link, FR-12)
 *   - <SubRowTodo /> -- task 022 (per-item Add-to-To-Do, FR-13)
 *   - <SubRowDismiss /> -- task 023 (per-item dismiss, FR-14)
 *
 * Created in task 020 (Wave 8) as the rendering skeleton + visual layout.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only; dark-mode parity via tokens.
 *   - Density: `fontSizeBase200` + `spacingVerticalS` per project constraint.
 *   - `colorNeutralForeground2` for sub-row text (de-emphasized vs narrative).
 *
 * Parallel-edit contract:
 *   This file (SubRow.tsx) is owned by task 020 (skeleton). After Wave 9,
 *   only the three slot files change. SubRow.tsx itself should remain stable
 *   unless layout (gap, alignment) changes -- which would be a follow-up task.
 */

import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import type { NotificationItem } from '../types/notifications';
import { formatDueDate } from '../utils/formatDueDate';
import { SubRowLink } from './SubRowLink';
import { SubRowTodo } from './SubRowTodo';
import { SubRowDismiss } from './SubRowDismiss';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    // De-emphasized vs narrative line (per project constraint)
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  bullet: {
    flexShrink: 0,
    width: '12px',
    color: tokens.colorNeutralForeground3,
    userSelect: 'none',
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  linkSlot: {
    flex: 1,
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
  },
  dueDate: {
    flexShrink: 0,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
  },
  dueDateOverdue: {
    color: tokens.colorPaletteRedForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SubRowProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
  /** Optional callback to add this single item to To Do (task 022 wiring). */
  onAddToTodoItem?: (itemId: string) => void;
  /** Optional callback to dismiss this single item (task 023 wiring). */
  onDismissItem?: (itemId: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SubRow: React.FC<SubRowProps> = ({ item, onAddToTodoItem, onDismissItem }) => {
  const styles = useStyles();
  const dueDateLabel = formatDueDate(item.dueDate);
  const isOverdue = dueDateLabel?.startsWith('Overdue') ?? false;

  return (
    <div className={styles.root}>
      {/* Decorative sub-list bullet glyph (indented). */}
      <span aria-hidden="true" className={styles.bullet}>
        &#8226;
      </span>
      {/* Slot A: per-item entity link (FR-12, task 021). */}
      <div className={styles.linkSlot}>
        <SubRowLink item={item} />
      </div>
      {/* R2.2: per-item due date hint (only when item.dueDate is set, i.e. task notifications). */}
      {dueDateLabel && (
        <span className={`${styles.dueDate} ${isOverdue ? styles.dueDateOverdue : ''}`.trim()}>{dueDateLabel}</span>
      )}
      {/* Slots B + C: per-item actions (FR-13/FR-14, tasks 022/023). */}
      <div className={styles.actions}>
        <SubRowTodo item={item} onAddToTodo={onAddToTodoItem} />
        <SubRowDismiss item={item} onDismiss={onDismissItem} />
      </div>
    </div>
  );
};
