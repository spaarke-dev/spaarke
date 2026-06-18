/**
 * SubRowTodo -- per-item Add-to-To-Do slot for the NarrativeBullet sub-list (FR-13).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the middle
 * slot of each indented sub-row. This file is the placeholder created in task
 * 020 (Wave 8); the real per-item To-Do creation behavior is implemented in
 * task 022 (Wave 9 -- invokes `useInlineTodoCreate` with the specific
 * underlying `NotificationItem` to create a concrete `sprk_todo` row, not a
 * vague aggregated summary).
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only, dark-mode parity.
 *   - FR-13: Sub-row Add-to-To-Do MUST create a concrete `sprk_todo` whose
 *     `sprk_name` matches the underlying notification title; regarding lookup
 *     resolves to the underlying notification's `regardingId`.
 *
 * Parallel-edit contract:
 *   Task 022 owns this file. Tasks 021 (SubRowLink) and 023 (SubRowDismiss)
 *   own sibling files. The three Wave-9 agents can edit in parallel because
 *   each owns a distinct file -- no NarrativeBullet.tsx race.
 */

import * as React from "react";
import { makeStyles, tokens, Button } from "@fluentui/react-components";
import { MicrosoftToDoIcon } from "@spaarke/ui-components";
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

export interface SubRowTodoProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
  /** Callback to add this single item to To Do. Task 022 wires real behavior. */
  onAddToTodo?: (itemId: string) => void;
}

// ---------------------------------------------------------------------------
// Component (placeholder -- task 022 fills in useInlineTodoCreate wiring)
// ---------------------------------------------------------------------------

export const SubRowTodo: React.FC<SubRowTodoProps> = ({ item, onAddToTodo }) => {
  const styles = useStyles();

  // Placeholder: render a disabled small Add-to-To-Do button. Task 022 wires
  // useInlineTodoCreate with the specific NotificationItem per FR-13.
  return (
    <Button
      appearance="subtle"
      size="small"
      icon={<MicrosoftToDoIcon size={14} className={styles.iconDefault} />}
      aria-label="Add to To Do"
      disabled={!onAddToTodo}
      onClick={() => onAddToTodo?.(item.id)}
    />
  );
};
