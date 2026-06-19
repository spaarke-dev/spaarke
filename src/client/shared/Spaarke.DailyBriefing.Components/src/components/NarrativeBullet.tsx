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
import { DismissRegular } from '@fluentui/react-icons';
import { MicrosoftToDoIcon } from '@spaarke/ui-components';
import type { NotificationItem } from '../types/notifications';
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
}) => {
  const styles = useStyles();

  // FR-11: render sub-list only for aggregated bullets (itemIds.length > 1)
  // AND only when the underlying `items` are supplied. Single-item bullets
  // render unchanged. If `items` is omitted (back-compat with existing
  // consumers), the sub-list is suppressed.
  const showSubList = itemIds.length > 1 && Array.isArray(items) && items.length > 0;

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
