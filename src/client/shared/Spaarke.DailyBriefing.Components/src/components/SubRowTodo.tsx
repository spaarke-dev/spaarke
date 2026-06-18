/**
 * SubRowTodo -- per-item Add-to-To-Do slot for the NarrativeBullet sub-list (FR-13).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the middle
 * slot of each indented sub-row. Implemented in task 022 (Wave 9) per FR-13:
 * each click produces a CONCRETE `sprk_todo` row whose `sprk_name`,
 * `sprk_notes`, and regarding lookup match the underlying `NotificationItem`
 * verbatim -- NOT a vague aggregated summary.
 *
 * Architecture (per ADR-024 + R2 task 013 hoist):
 *   The actual `useInlineTodoCreate` hook is instantiated ONCE at the
 *   top-level `DailyBriefingApp` (so per-item statusMap state is shared
 *   across all sub-rows of all bullets, and we make exactly one `IWebApi`
 *   binding). The hook's resolver-field + nav-prop logic is preserved
 *   byte-identical from task 013 -- this slot does NOT reimplement it.
 *
 *   This slot receives:
 *     - `onAddToTodo(itemId)` -- the parent-side adapter that ultimately calls
 *       `useInlineTodoCreate.createTodo(item)` with the SPECIFIC underlying
 *       `NotificationItem` (real title, body, regarding -- not the aggregated
 *       narrative summary).
 *     - `isCreated` / `isPending` / `error` -- optional per-item state read
 *       from the parent's single `useInlineTodoCreate` instance.
 *
 *   This split keeps the slot focused on rendering + accessibility while
 *   leaving Dataverse-write + ADR-024 regarding-resolution logic in one
 *   place (the hook), per the FR-06 / FR-13 / spec.md decomposition contract.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only; dark-mode parity.
 *   - ADR-024: `TODO_REGARDING_CATALOG` + `applyResolverFields` apply UNCHANGED
 *     -- preserved in `useInlineTodoCreate` (task 013); NOT reimplemented here.
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
import {
  makeStyles,
  tokens,
  Button,
  Tooltip,
  Spinner,
} from "@fluentui/react-components";
import { MicrosoftToDoIcon } from "@spaarke/ui-components";
import type { NotificationItem } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021; dark-mode parity)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  iconDefault: {
    color: tokens.colorNeutralForeground3,
  },
  iconActive: {
    color: tokens.colorBrandForeground1,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SubRowTodoProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
  /**
   * Callback invoked when the user clicks the Add-to-To-Do button.
   *
   * Parent-side adapter at `DailyBriefingApp` resolves this to
   * `useInlineTodoCreate.createTodo(notificationItem)` with the SPECIFIC
   * underlying `NotificationItem` (FR-13) -- never the aggregated summary.
   *
   * When omitted, the button renders disabled (placeholder/back-compat).
   */
  onAddToTodo?: (itemId: string) => void;
  /**
   * Whether a `sprk_todo` has been successfully created for this item.
   * Drives the icon-active state + "Added to To Do" tooltip.
   *
   * Read from `useInlineTodoCreate.isCreated(item.id)` at the parent.
   */
  isCreated?: boolean;
  /**
   * Whether a `sprk_todo` creation is in-flight for this item.
   * Drives the spinner-in-icon state + disabled click.
   *
   * Read from `useInlineTodoCreate.isPending(item.id)` at the parent.
   */
  isPending?: boolean;
  /**
   * Error message from the last failed creation attempt, if any.
   * Surfaced in the Tooltip on hover (so the user sees the underlying
   * error without taking up sub-row real estate). Re-click retries.
   *
   * Read from `useInlineTodoCreate.getError(item.id)` at the parent.
   */
  error?: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SubRowTodo: React.FC<SubRowTodoProps> = ({
  item,
  onAddToTodo,
  isCreated = false,
  isPending = false,
  error,
}) => {
  const styles = useStyles();

  // Tooltip content surfaces state without crowding the sub-row.
  // Priority: error > created > default.
  let tooltipContent = "Add to To Do";
  if (error) tooltipContent = error;
  else if (isCreated) tooltipContent = "Added to To Do";

  const handleClick = React.useCallback(() => {
    // Guard rails: no-op when the per-item creation is in-flight or already
    // succeeded. (Idempotence: clicking again after success should NOT create
    // a duplicate `sprk_todo` -- the hook's statusMap enforces this, but we
    // also short-circuit at the UI to avoid even attempting.)
    if (!onAddToTodo || isPending || isCreated) return;
    onAddToTodo(item.id);
  }, [onAddToTodo, isPending, isCreated, item.id]);

  // Disabled when no callback wired (placeholder mode) OR currently pending OR
  // already created. Error state does NOT disable -- user can click to retry.
  const disabled = !onAddToTodo || isPending || isCreated;

  const icon = isPending ? (
    <Spinner size="tiny" />
  ) : (
    <MicrosoftToDoIcon
      size={14}
      active={isCreated}
      className={isCreated ? styles.iconActive : styles.iconDefault}
    />
  );

  return (
    <Tooltip content={tooltipContent} relationship="label">
      <Button
        appearance="subtle"
        size="small"
        icon={icon}
        aria-label={tooltipContent}
        disabled={disabled}
        onClick={handleClick}
      />
    </Tooltip>
  );
};
