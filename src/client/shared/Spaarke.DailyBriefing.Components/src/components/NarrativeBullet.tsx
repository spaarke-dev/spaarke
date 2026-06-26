/**
 * NarrativeBullet -- renders a single AI-narrated bullet with a three-dot
 * overflow action menu.
 *
 * Each bullet shows the narrative text, a clickable regarding-name link that
 * opens the entity in a Dataverse dialog, and a Fluent v9 three-dot overflow
 * `Menu` (FR-18) replacing the prior inline 5-icon action row.
 *
 * Overflow menu items (canonical order per spec FR-18 / AC-18a):
 *   1. Mark as read                  → onCheck(primaryItemId)        (R3 FR-4)
 *   2. Remove from briefing          → onRemove(primaryItemId)       (R3 FR-5)
 *   3. Keep on briefing for 7 more days → onKeep(primaryItemId, ttl) (R3 FR-6)
 *   4. Add to To Do                  → onAddToTodo(itemIds)          (ADR-024)
 *   5. Dismiss                       → onDismiss(itemIds)            (FR-14a)
 *   6. Open record                   → onOpenRecord(type, id)        (FR-18 new)
 *
 * The inline 5-icon row is REMOVED per FR-18 ("MUST NOT preserve inline 5-icon
 * row"). The R3 actions (Check, Remove, Keep) are PRESERVED — they migrate
 * into the overflow menu unchanged in behavior. The R2 ADR-024 `Add to To Do`
 * + Dismiss callbacks are PRESERVED via the same Menu surface.
 *
 * Aggregation UX (FR-11): when `itemIds.length > 1` and the optional `items`
 * prop is supplied, an indented per-item sub-list is rendered beneath the
 * narrative line — UNCHANGED from R2.
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
 *   - ADR-024: `useInlineTodoCreate` + `TODO_REGARDING_CATALOG` preserved (the
 *     menu invokes the existing `onAddToTodo` callback unchanged)
 *   - WCAG: trigger has aria-label="More actions"; MenuItem touch targets meet
 *     ≥44×44px via Fluent v9 defaults; keyboard nav (Tab, Enter, arrows, Esc)
 *     handled by the Menu primitive out-of-box
 *   - Visual pattern: matches `DocumentRowMenu` (the canonical Spaarke
 *     three-dot pattern) and the semantic-search PCF ResultCard convention
 *
 * Component hoisted into `@spaarke/daily-briefing-components/components` by R2
 * task 011 (Wave 3 / Group A). Task 045 (R4) refactors the inline action row
 * into the overflow menu.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItem,
} from '@fluentui/react-components';
import {
  MoreHorizontalRegular,
  CheckmarkRegular,
  DismissRegular,
  CalendarAddRegular,
  AddRegular,
  OpenRegular,
} from '@fluentui/react-icons';
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
  todoMenuIcon: {
    color: tokens.colorNeutralForeground1,
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
   */
  items?: NotificationItem[];
  /**
   * Optional per-item Add-to-To-Do callback (FR-13, task 022). Distinct from
   * `onAddToTodo` (the aggregated callback). When provided, each sub-row's
   * To-Do slot becomes active.
   */
  onAddToTodoItem?: (itemId: string) => void;
  /**
   * Optional per-item Dismiss callback (FR-14, task 023). Distinct from
   * `onDismiss` (the aggregated callback, which cascades per FR-14a).
   */
  onDismissItem?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-4 — "Mark as read" action.
   *
   * Migrated into the FR-18 overflow menu as item 1. When undefined, the menu
   * item is hidden (defensive default; back-compat with consumers that have
   * not yet wired the new action layer).
   */
  onCheck?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-5 — "Remove from briefing" action.
   *
   * Migrated into the FR-18 overflow menu as item 2. Hidden when undefined.
   */
  onRemove?: (itemId: string) => void;
  /**
   * R3 task 031 / FR-6 — "Keep on briefing for 7 more days" action.
   *
   * Migrated into the FR-18 overflow menu as item 3. `currentTtlSeconds`
   * reflects the item's current `ttlinseconds` value; the service computes
   * `newTtl = currentTtlSeconds + 604800`. Hidden when undefined.
   */
  onKeep?: (itemId: string, currentTtlSeconds: number) => void;
  /**
   * FR-18 / AC-18a — "Open record" action (new in R4).
   *
   * Migrated into the FR-18 overflow menu as item 6. When supplied, the menu
   * item invokes `onOpenRecord(primaryEntityType, primaryEntityId)`. When
   * undefined, the menu item falls back to the same Xrm.Navigation.navigateTo
   * invocation that the inline regarding-name link uses (i.e., the action
   * is always available so long as primaryEntityType + primaryEntityId are
   * supplied — matching the inline link's existing precondition).
   */
  onOpenRecord?: (entityType: string, entityId: string) => void;
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
  onOpenRecord,
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

  // Resolve the Xrm globals once (used by both the inline regarding-name link
  // and the fallback "Open record" overflow-menu handler).
  const resolveXrm = ():
    | { Navigation?: { navigateTo?: (page: object, options?: object) => Promise<unknown> } }
    | undefined => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any)?.Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
  };

  const openRecordViaXrm = (entityType: string, entityId: string): void => {
    if (!entityType || !entityId) return;
    const xrm = resolveXrm();
    if (!xrm?.Navigation?.navigateTo) return;
    xrm.Navigation.navigateTo(
      {
        pageType: 'entityrecord',
        entityName: entityType,
        entityId: entityId,
      },
      { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
    ).catch(() => {
      /* user closed dialog */
    });
  };

  const handleLinkClick = (): void => {
    if (!primaryEntityType || !primaryEntityId) return;
    // FR-19 (task 047): when the parent supplies `onOpenRecord`, route the
    // regarding-name link click through it so the parent owns the
    // `Xrm.Navigation.navigateTo` call AND the 403-fallback Toaster dispatch.
    // When the prop is omitted (back-compat / standalone usage), fall back to
    // the in-component Xrm helper that silently swallows the rejection.
    if (onOpenRecord) {
      onOpenRecord(primaryEntityType, primaryEntityId);
    } else {
      openRecordViaXrm(primaryEntityType, primaryEntityId);
    }
  };

  // ---------------------------------------------------------------------------
  // FR-18 overflow-menu action handlers.
  //
  // R3 task 031 — per-item action handlers (FR-4 / FR-5 / FR-6). The 3 R3
  // actions operate at single-item granularity (`appnotificationid` GUID),
  // matching the underlying hook signatures. The primary subject of the bullet
  // is `itemIds[0]` — for aggregated bullets, the parent supplies the bullet's
  // lead item; the per-row sub-list owns its own controls.
  // ---------------------------------------------------------------------------
  const primaryItemId = itemIds[0] ?? '';

  // R3 FR-6: `currentTtlSeconds` is sourced from the corresponding
  // NotificationItem. Coalesce to 0 for pre-rollout rows with no stored TTL.
  const primaryItemTtlSeconds = items?.find(item => item.id === primaryItemId)?.ttlinseconds ?? 0;

  const handleMenuMarkAsRead = (): void => {
    if (!primaryItemId) return;
    onCheck?.(primaryItemId);
  };

  const handleMenuRemoveFromBriefing = (): void => {
    if (!primaryItemId) return;
    onRemove?.(primaryItemId);
  };

  const handleMenuKeepSevenMoreDays = (): void => {
    if (!primaryItemId) return;
    onKeep?.(primaryItemId, primaryItemTtlSeconds);
  };

  const handleMenuAddToTodo = (): void => {
    if (isTodoCreated || isTodoPending) return;
    onAddToTodo(itemIds);
  };

  const handleMenuDismiss = (): void => {
    onDismiss(itemIds);
  };

  const handleMenuOpenRecord = (): void => {
    if (!primaryEntityType || !primaryEntityId) return;
    if (onOpenRecord) {
      onOpenRecord(primaryEntityType, primaryEntityId);
    } else {
      openRecordViaXrm(primaryEntityType, primaryEntityId);
    }
  };

  // The "Add to To Do" menu item label reflects the same state the prior
  // inline button surfaced via Tooltip: created / pending / error / default.
  let addToDoLabel = 'Add to To Do';
  if (isTodoCreated) addToDoLabel = 'Added to To Do';
  else if (isTodoPending) addToDoLabel = 'Adding to To Do…';
  else if (todoError) addToDoLabel = todoError;

  // "Open record" is hidden when there is no primary entity to open (matches
  // the inline regarding-name link's precondition).
  const canOpenRecord = Boolean(primaryEntityType && primaryEntityId);

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
          FR-18 / AC-18a — Three-dot overflow menu replacing the prior inline
          5-icon action row. Fluent v9 Menu primitive handles keyboard nav
          (Tab/Enter to open, arrows to move, Enter to select, Esc to close)
          and ARIA roles out-of-box. MenuButton meets WCAG ≥44×44px touch
          target at the Fluent v9 default size.

          The 6 items render in canonical order per FR-18:
            1. Mark as read                 (R3 onCheck)
            2. Remove from briefing         (R3 onRemove)
            3. Keep on briefing for 7 more days (R3 onKeep)
            4. Add to To Do                 (ADR-024 onAddToTodo)
            5. Dismiss                      (FR-14a onDismiss)
            6. Open record                  (FR-18 onOpenRecord)

          Items 1/2/3 hide when their callback is undefined (defensive default,
          back-compat). Item 6 hides when primaryEntityType/Id are missing.
        */}
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" size="small" icon={<MoreHorizontalRegular />} aria-label="More actions" />
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              {onCheck && (
                <MenuItem icon={<CheckmarkRegular />} onClick={handleMenuMarkAsRead}>
                  Mark as read
                </MenuItem>
              )}
              {onRemove && (
                <MenuItem icon={<DismissRegular />} onClick={handleMenuRemoveFromBriefing}>
                  Remove from briefing
                </MenuItem>
              )}
              {onKeep && (
                <MenuItem icon={<CalendarAddRegular />} onClick={handleMenuKeepSevenMoreDays}>
                  Keep on briefing for 7 more days
                </MenuItem>
              )}
              <MenuItem
                icon={
                  isTodoCreated || isTodoPending ? (
                    <MicrosoftToDoIcon size={16} active={isTodoCreated} className={styles.todoMenuIcon} />
                  ) : (
                    <AddRegular />
                  )
                }
                onClick={handleMenuAddToTodo}
                disabled={isTodoCreated || isTodoPending}
              >
                {addToDoLabel}
              </MenuItem>
              <MenuItem icon={<DismissRegular />} onClick={handleMenuDismiss}>
                Dismiss
              </MenuItem>
              {canOpenRecord && (
                <MenuItem icon={<OpenRegular />} onClick={handleMenuOpenRecord}>
                  Open record
                </MenuItem>
              )}
            </MenuList>
          </MenuPopover>
        </Menu>
      </div>
    </div>
  );
};
