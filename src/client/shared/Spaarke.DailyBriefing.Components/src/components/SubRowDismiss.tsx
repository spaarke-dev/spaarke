/**
 * SubRowDismiss -- per-item Dismiss slot for the NarrativeBullet sub-list (FR-14).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the rightmost
 * slot of each indented sub-row. Implemented in task 023 (Wave 9).
 *
 * Behavior (FR-14):
 *   - On click, invokes `onDismiss(item.id)` -- the parent wires this to
 *     `useBriefingActions.markAsRead(item.id)` which marks ONLY the specific
 *     underlying `appnotification` row as read.
 *   - Optimistic UI: the sub-row fades immediately on click (the button hides;
 *     parent listens to the consumer's refetch loop to remove the row entirely
 *     on the next render cycle).
 *   - On failure (callback returns `false`), the optimistic fade is reverted
 *     so the user can retry. The parent surfaces a toast.
 *   - Disabled while a previous click is in flight (prevents double-fire).
 *
 * Cascade behavior (FR-14a) is owned upstream at `DailyBriefingApp.handleDismiss`,
 * which iterates `itemIds[]` and calls `markAsRead(id)` for each when the
 * AGGREGATED Dismiss button on `NarrativeBullet` is clicked. NarrativeBullet
 * itself is not touched by this task -- the cascade was already implemented
 * via the existing `onDismiss(itemIds)` contract at the consumer layer.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only, dark-mode parity.
 *   - FR-14: Sub-row Dismiss MUST mark only the specific `appnotification`
 *     as read; sibling sub-rows remain visible.
 *
 * Parallel-edit contract:
 *   Task 023 owns this file. Tasks 021 (SubRowLink) and 022 (SubRowTodo) own
 *   sibling files. The three Wave-9 agents can edit in parallel because
 *   each owns a distinct file -- no NarrativeBullet.tsx race.
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens, Button, Tooltip } from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import type { NotificationItem } from '../types/notifications';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  iconDefault: {
    color: tokens.colorNeutralForeground3,
  },
  // Optimistic fade applied to the entire button container while the
  // markAsRead is in flight. On failure we revert to full opacity so the
  // user can retry; on success the parent refetch removes the row.
  fading: {
    opacity: 0.35,
    pointerEvents: 'none',
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SubRowDismissProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
  /**
   * Callback invoked with the underlying notification ID when the user clicks
   * Dismiss. The parent typically wires this to
   * `useBriefingActions.markAsRead(item.id)`.
   *
   * Should return a Promise<boolean>: `true` on success, `false` on failure.
   * When `false` (or the Promise rejects), the optimistic fade is reverted so
   * the user can retry. The parent is responsible for surfacing a toast on
   * failure.
   *
   * If omitted, the button is rendered disabled.
   */
  onDismiss?: (itemId: string) => Promise<boolean> | boolean | void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SubRowDismiss: React.FC<SubRowDismissProps> = ({ item, onDismiss }) => {
  const styles = useStyles();

  // Local optimistic state: while `pending` is true, the button fades and
  // is disabled. On success we keep it faded (parent removes the row via
  // refetch). On failure we revert.
  const [pending, setPending] = React.useState(false);
  const [dismissed, setDismissed] = React.useState(false);

  const handleClick = React.useCallback(async () => {
    if (!onDismiss || pending || dismissed) return;
    setPending(true);
    try {
      const result = onDismiss(item.id);
      const ok =
        typeof result === 'boolean'
          ? result
          : result && typeof (result as Promise<boolean>).then === 'function'
            ? await result
            : true; // void callbacks treated as success (optimistic)
      if (ok) {
        setDismissed(true);
      } else {
        // Revert optimistic fade so user can retry.
        setPending(false);
      }
    } catch {
      setPending(false);
    }
  }, [onDismiss, pending, dismissed, item.id]);

  const isFaded = pending || dismissed;

  return (
    <Tooltip content="Dismiss" relationship="label">
      <Button
        appearance="subtle"
        size="small"
        icon={<DismissRegular className={styles.iconDefault} />}
        className={mergeClasses(isFaded && styles.fading)}
        aria-label="Dismiss"
        disabled={!onDismiss || pending || dismissed}
        onClick={handleClick}
      />
    </Tooltip>
  );
};
