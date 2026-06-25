/**
 * NarrativeBullet -- renders a single AI-narrated bullet with action buttons.
 *
 * Each bullet shows the narrative text, a clickable record link that opens
 * the entity in a Dataverse dialog, and two action buttons: "Add to To Do"
 * and "Dismiss".
 *
 * Aggregation UX (FR-11): when `itemIds.length > 1` and the optional `items`
 * prop is supplied, an indented per-item sub-list is rendered beneath the
 * narrative line. Each sub-row composes three slot sub-components:
 *
 *   - <SubRowLink />    -- per-item entity link (task 021, FR-12)
 *   - <SubRowTodo />    -- per-item Add-to-To-Do (task 022, FR-13)
 *   - <SubRowDismiss /> -- per-item Dismiss     (task 023, FR-14)
 *
 * Single-item bullets (`itemIds.length === 1`) render UNCHANGED -- no
 * sub-list -- preserving the existing UX.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *   - Opens records via Xrm.Navigation.navigateTo
 *
 * Hoisted into `@spaarke/daily-briefing-components/components` by R2 task 011
 * (Wave 3 / Group A). Source of truth; the original-location file at
 * `src/solutions/DailyBriefing/src/components/NarrativeBullet.tsx` is now a
 * re-export shim pending full cleanup in R2 task 017.
 *
 * Task 020 (Wave 8): adds sub-list rendering skeleton + slot wiring. The 3
 * per-row controls (link, To-Do, Dismiss) are real-implemented by tasks
 * 021/022/023 (Wave 9, parallel-safe -- each task edits its own slot file).
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Button, Tooltip, Spinner } from '@fluentui/react-components';
import { CalendarAddRegular, CheckmarkRegular, DismissRegular } from '@fluentui/react-icons';
import { MicrosoftToDoIcon } from '@spaarke/ui-components';
import type { NotificationItem } from '../types/notifications';
import { formatDueDate } from '../utils/formatDueDate';
import { SubRow } from './SubRow';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    marginBottom: tokens.spacingVerticalL,
  },
  bullet: {
    color: tokens.colorNeutralForeground1,
    flexShrink: 0,
    lineHeight: tokens.lineHeightBase400,
    userSelect: 'none',
  },
  content: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  narrativeText: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
  },
  entityLink: {
    color: tokens.colorBrandForeground1,
    cursor: 'pointer',
    textDecorationLine: 'none',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
  dueDateRow: {
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase200,
    marginTop: tokens.spacingVerticalXXS,
  },
  dueDateOverdue: {
    color: tokens.colorPaletteRedForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  todoIconDefault: {
    color: tokens.colorNeutralForeground3,
  },
  todoIconActive: {
    color: tokens.colorBrandForeground1,
  },
  // FR-11: per-item sub-list (rendered only when itemIds.length > 1).
  subList: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    // Indent under the narrative + entity-link column.
    marginTop: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    // Subtle left border for visual grouping (semantic token).
    borderLeftWidth: '2px',
    borderLeftStyle: 'solid',
    borderLeftColor: tokens.colorNeutralStroke2,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface NarrativeBulletProps {
  /** AI-generated narrative text for this bullet. */
  narrative: string;
  /** Display name of the primary entity (shown as clickable link). */
  primaryEntityName: string;
  /** Dataverse logical name of the primary entity. */
  primaryEntityType: string;
  /** GUID of the primary entity record. */
  primaryEntityId: string;
  /** Notification IDs covered by this bullet. */
  itemIds: string[];
  /** Callback to add the covered notifications to To Do. */
  onAddToTodo: (itemIds: string[]) => void;
  /** Callback to dismiss the covered notifications. */
  onDismiss: (itemIds: string[]) => void;
  /** Whether a To Do has been created for this bullet. */
  isTodoCreated: boolean;
  /** Whether a To Do creation is in progress. */
  isTodoPending: boolean;
  /** Error message from a failed To Do creation. */
  todoError?: string;
  /**
   * Underlying notification items corresponding to `itemIds` (FR-11).
   *
   * When `itemIds.length > 1` AND `items` is supplied, an indented per-item
   * sub-list is rendered beneath the narrative line. When omitted or when
   * `itemIds.length === 1`, the sub-list is NOT rendered (existing UX
   * preserved).
   *
   * Items SHOULD be passed in the same order as `itemIds`. Each sub-row's
   * 3 slot sub-components (link / To-Do / Dismiss) use the `NotificationItem`
   * verbatim per FR-12/FR-13/FR-14 -- no AI involvement on the sub-row data.
   *
   * Wired by tasks 021/022/023 (Wave 9) into the slot components.
   */
  items?: NotificationItem[];
  /**
   * Optional per-item Add-to-To-Do callback (FR-13, task 022).
   *
   * Distinct from `onAddToTodo` (the aggregated callback). When provided,
   * each sub-row's To-Do slot becomes active; task 022 wires
   * `useInlineTodoCreate` with the specific underlying `NotificationItem`.
   */
  onAddToTodoItem?: (itemId: string) => void;
  /**
   * Optional per-item Dismiss callback (FR-14, task 023).
   *
   * Distinct from `onDismiss` (the aggregated callback, which cascades per
   * FR-14a). When provided, each sub-row's Dismiss slot becomes active and
   * marks only the specific `appnotification` row as read.
   */
  onDismissItem?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-4 — "Mark as read" action.
   *
   * When supplied, a Check button (`CheckmarkRegular`) renders FIRST in the
   * action row. Clicking invokes `onCheck(firstItemId)`. The parent wraps
   * the `markChecked` hook handler with an optimistic-update + toast callback.
   *
   * Defensive default: when undefined, the button is hidden (the existing
   * NarrativeBullet remains backward-compatible with consumers that have not
   * yet wired the new action layer).
   */
  onCheck?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-5 — "Remove from briefing" action.
   *
   * When supplied, a Remove button (`DismissRegular`) renders SECOND in the
   * action row. Clicking invokes `onRemove(firstItemId)`. The parent wraps
   * the `markRemoved` hook handler with optimistic UI + toast callback.
   *
   * Defensive default: hidden when undefined.
   */
  onRemove?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-6 — "Keep on briefing for 7 more days" action.
   *
   * When supplied, a Keep button (`CalendarAddRegular`) renders THIRD in the
   * action row. Clicking invokes `onKeep(firstItemId, currentTtlSeconds)`.
   * The parent wraps the `extendTtl` hook handler with optimistic UI + toast
   * callback (the toast renders the new effective expiry date).
   *
   * `currentTtlSeconds` reflects the item's current `ttlinseconds` value; the
   * service computes `newTtl = currentTtlSeconds + 604800`. If the consumer
   * cannot resolve a value (e.g., not selected on the query), pass `0` —
   * the service interprets this as "extend by 7 days from now".
   *
   * Defensive default: hidden when undefined.
   */
  onKeep?: (itemId: string, currentTtlSeconds: number) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NarrativeBullet: React.FC<NarrativeBulletProps> = ({
  narrative,
  primaryEntityName,
  primaryEntityType,
  primaryEntityId,
  itemIds,
  onAddToTodo,
  onDismiss,
  isTodoCreated,
  isTodoPending,
  todoError,
  items,
  onAddToTodoItem,
  onDismissItem,
  onCheck,
  onRemove,
  onKeep,
}) => {
  const styles = useStyles();

  // FR-11: render sub-list only for aggregated bullets (itemIds.length > 1)
  // AND only when the underlying `items` are supplied. Single-item bullets
  // render unchanged. If `items` is omitted (back-compat with existing
  // consumers), the sub-list is suppressed.
  const showSubList = itemIds.length > 1 && Array.isArray(items) && items.length > 0;

  // R2.2: per-item due-date hint for SINGLE-item bullets. Aggregated bullets
  // delegate to SubRow (which renders each item's due date in its own row).
  // Showing a single due-date on an aggregated bullet would misrepresent
  // multi-item due dates.
  const singleItemDueDate =
    !showSubList && Array.isArray(items) && items.length === 1 ? formatDueDate(items[0].dueDate) : null;
  const isSingleItemOverdue = singleItemDueDate?.startsWith('Overdue') ?? false;

  const handleLinkClick = () => {
    if (!primaryEntityType || !primaryEntityId) return;
    const xrm =
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window as any)?.Xrm ??
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window.parent as any)?.Xrm ??
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      (window.top as any)?.Xrm;
    if (!xrm?.Navigation?.navigateTo) return;
    xrm.Navigation.navigateTo(
      {
        pageType: 'entityrecord',
        entityName: primaryEntityType,
        entityId: primaryEntityId,
      },
      { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
    ).catch(() => {
      /* user closed dialog */
    });
  };

  const handleAddToTodo = () => {
    if (!isTodoCreated && !isTodoPending) {
      onAddToTodo(itemIds);
    }
  };

  const handleDismiss = () => {
    onDismiss(itemIds);
  };

  // R3 task 031 — per-item action handlers (FR-4 / FR-5 / FR-6).
  //
  // The 3 new actions operate at single-item granularity (`appnotificationid`
  // GUID), matching the underlying hook signatures `markChecked(id) /
  // markRemoved(id) / extendTtl(id, currentTtl)`. The primary subject of the
  // bullet is `itemIds[0]` — for aggregated bullets, the parent supplies the
  // bullet's lead item; the per-row Sub-list owns its own (future) controls.
  const primaryItemId = itemIds[0] ?? '';

  // `currentTtlSeconds` is sourced from the corresponding NotificationItem
  // when supplied. NotificationItem does not currently surface `ttlinseconds`
  // (the column is not in `NOTIFICATION_SELECT` — see notificationService.ts);
  // until task 020's service-layer owner re-selects + propagates it, we pass
  // `0` so the service computes `newTtl = 0 + 604800` (= 7 days from now).
  // This matches the spec FR-6 invariant for items without a stored TTL.
  const primaryItemTtlSeconds = 0;

  const handleCheck = () => {
    if (!primaryItemId) return;
    onCheck?.(primaryItemId);
  };

  const handleRemove = () => {
    if (!primaryItemId) return;
    onRemove?.(primaryItemId);
  };

  const handleKeep = () => {
    if (!primaryItemId) return;
    onKeep?.(primaryItemId, primaryItemTtlSeconds);
  };

  // Determine To Do button tooltip
  let todoTooltip = 'Add to To Do';
  if (isTodoCreated) todoTooltip = 'Added to To Do';
  if (todoError) todoTooltip = todoError;

  return (
    <div className={styles.root}>
      <Text size={300} className={styles.bullet}>
        &bull;
      </Text>
      <div className={styles.content}>
        <Text size={300} className={styles.narrativeText}>
          {narrative}
        </Text>
        {primaryEntityName && primaryEntityType && primaryEntityId && (
          <Text
            size={300}
            className={styles.entityLink}
            onClick={handleLinkClick}
            role="link"
            tabIndex={0}
            onKeyDown={(e: React.KeyboardEvent) => {
              if (e.key === 'Enter' || e.key === ' ') handleLinkClick();
            }}
          >
            {primaryEntityName} &#8599;
          </Text>
        )}
        {/* R2.2: single-item due-date hint (task notifications only — others have item.dueDate=null). */}
        {singleItemDueDate && (
          <Text
            size={200}
            className={`${styles.dueDateRow} ${isSingleItemOverdue ? styles.dueDateOverdue : ''}`.trim()}
          >
            {singleItemDueDate}
          </Text>
        )}
        {/* FR-11: per-item sub-list for aggregated bullets (itemIds.length > 1). */}
        {showSubList && (
          <div className={styles.subList} role="list" aria-label={`${items!.length} underlying notifications`}>
            {items!.map(item => (
              <div key={item.id} role="listitem">
                <SubRow item={item} onAddToTodoItem={onAddToTodoItem} onDismissItem={onDismissItem} />
              </div>
            ))}
          </div>
        )}
      </div>
      <div className={styles.actions}>
        {/*
          R3 task 031 — 3 new per-item actions (FR-4 / FR-5 / FR-6).
          Each renders only when its callback prop is wired by the parent
          (defensive default per task POML step 5). Owner-specified icon set:
          CheckmarkRegular / DismissRegular / CalendarAddRegular. Owner-specified
          tooltips per spec.md. Order: Check → Remove → Keep → (existing) Add to
          To Do → (existing) Dismiss. Existing "Add to To Do" button is preserved
          unchanged to satisfy ADR-024 regression-free invariant.
        */}
        {onCheck && (
          <Tooltip content="Mark as read" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<CheckmarkRegular />}
              onClick={handleCheck}
              aria-label="Mark as read"
            />
          </Tooltip>
        )}
        {onRemove && (
          <Tooltip content="Remove from briefing" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<DismissRegular />}
              onClick={handleRemove}
              aria-label="Remove from briefing"
            />
          </Tooltip>
        )}
        {onKeep && (
          <Tooltip content="Keep on briefing for 7 more days" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<CalendarAddRegular />}
              onClick={handleKeep}
              aria-label="Keep on briefing for 7 more days"
            />
          </Tooltip>
        )}
        <Tooltip content={todoTooltip} relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={
              isTodoPending ? (
                <Spinner size="tiny" />
              ) : (
                <MicrosoftToDoIcon
                  size={16}
                  active={isTodoCreated}
                  className={isTodoCreated ? styles.todoIconActive : styles.todoIconDefault}
                />
              )
            }
            onClick={handleAddToTodo}
            disabled={isTodoCreated || isTodoPending}
            aria-label={todoTooltip}
          />
        </Tooltip>
        <Tooltip content="Dismiss" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<DismissRegular />}
            onClick={handleDismiss}
            aria-label="Dismiss"
          />
        </Tooltip>
      </div>
    </div>
  );
};
