/**
 * SubRowDismiss -- per-item Dismiss slot for the NarrativeBullet sub-list (FR-14).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the rightmost
 * slot of each indented sub-row. This file is the placeholder created in task
 * 020 (Wave 8); the real per-item dismissal behavior is implemented in task
 * 023 (Wave 9 -- marks only the specific underlying `appnotification` row as
 * read; sub-row fades/hides on success; aggregated-bullet Dismiss button
 * cascades per FR-14a).
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only, dark-mode parity.
 *   - FR-14: Sub-row Dismiss MUST mark only the specific `appnotification`
 *     as read; sibling sub-rows remain visible.
 *   - FR-14a: The aggregated-bullet Dismiss button (already present on
 *     NarrativeBullet) MUST cascade to all `itemIds[]` -- that behavior is
 *     tuned in task 023 as well.
 *
 * Parallel-edit contract:
 *   Task 023 owns this file. Tasks 021 (SubRowLink) and 022 (SubRowTodo) own
 *   sibling files. The three Wave-9 agents can edit in parallel because
 *   each owns a distinct file -- no NarrativeBullet.tsx race.
 */

import * as React from "react";
import { makeStyles, tokens, Button } from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import type { NotificationItem } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  iconDefault: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SubRowDismissProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
  /** Callback to dismiss this single item. Task 023 wires real behavior. */
  onDismiss?: (itemId: string) => void;
}

// ---------------------------------------------------------------------------
// Component (placeholder -- task 023 fills in per-item dismissal)
// ---------------------------------------------------------------------------

export const SubRowDismiss: React.FC<SubRowDismissProps> = ({ item, onDismiss }) => {
  const styles = useStyles();

  // Placeholder: render a disabled small Dismiss button. Task 023 wires
  // per-item appnotification.isread update per FR-14.
  return (
    <Button
      appearance="subtle"
      size="small"
      icon={<DismissRegular className={styles.iconDefault} />}
      aria-label="Dismiss"
      disabled={!onDismiss}
      onClick={() => onDismiss?.(item.id)}
    />
  );
};
