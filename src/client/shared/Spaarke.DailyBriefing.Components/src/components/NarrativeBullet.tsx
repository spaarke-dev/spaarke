/**
 * NarrativeBullet -- renders a single AI-narrated bullet with a primary
 * `Add To Do` checkmark icon button AND a preserved three-dot overflow
 * action menu.
 *
 * Each bullet shows the narrative text, a clickable regarding-name link that
 * opens the entity in a Dataverse dialog, a PRIMARY visible Fluent v9
 * `Checkmark` icon button ('Add To Do') and a Fluent v9 three-dot overflow
 * `Menu` containing the remaining secondary actions.
 *
 * Per Wave 12 task 134 (operator MVP spec, wave12 plan §2.1):
 *   "for the tools only need the 'Add To Do' (so just the checkmark — but in
 *    future we will add other tools so don't remove the three dot tool menu)"
 *
 * The three-dot overflow menu component is PRESERVED in the component tree
 * for future tool additions (operator emphasis on extensibility). Today it
 * contains the 5 remaining secondary actions:
 *
 * Visible primary tool (R7 W12 task 134):
 *   • Add To Do (Fluent v9 Checkmark icon) → onAddToTodo(itemIds)  (ADR-024)
 *
 * Overflow menu items (canonical order, post-task-134):
 *   1. Mark as read                  → onCheck(primaryItemId)        (R3 FR-4)
 *   2. Remove from briefing          → onRemove(primaryItemId)       (R3 FR-5)
 *   3. Keep on briefing for 7 more days → onKeep(primaryItemId, ttl) (R3 FR-6)
 *   4. Dismiss                       → onDismiss(itemIds)            (FR-14a)
 *   5. Open record                   → onOpenRecord(type, id)        (FR-18)
 *
 * (Pre-task-134 the overflow menu included "Add to To Do" as item 4; task 134
 * promotes it to a visible primary tool while preserving the menu component.)
 *
 * The inline 5-icon row is REMOVED per R4 FR-18 ("MUST NOT preserve inline 5-icon
 * row"). The R3 actions (Check, Remove, Keep) are PRESERVED — they migrate
 * into the overflow menu unchanged in behavior. The R2 ADR-024 `Add to To Do`
 * + Dismiss callbacks are PRESERVED — Add To Do becomes primary visible tool,
 * Dismiss remains in the menu.
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
  Button,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuButton,
  MenuPopover,
  MenuList,
  MenuItem,
} from '@fluentui/react-components';
import {
  MoreVerticalRegular,
  CheckmarkRegular,
  DismissRegular,
  CalendarAddRegular,
  OpenRegular,
} from '@fluentui/react-icons';
import type { NotificationItem } from '../types/notifications';
import type { NarrativeBulletReferenceResult } from '../services/briefingService';
import { formatDueDate } from '../utils/formatDueDate';
import { SubRow } from './SubRow';
import { NarrativeCitedText } from './NarrativeCitedText';

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
  // Task 134: primary visible 'Add To Do' button styling.
  // Default checkmark uses brand foreground (semantic token; dark-mode safe).
  // Created state uses the same brand color so the "added" affordance reads
  // immediately. ADR-021 compliance: NO hardcoded colors anywhere.
  addTodoIcon: {
    color: tokens.colorBrandForeground1,
  },
  addTodoIconDisabled: {
    color: tokens.colorNeutralForegroundDisabled,
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
  /**
   * R7 W12 feedback items 2/3/4 (2026-07-01) — per-bullet entity references.
   * When supplied and non-empty, the narrative text is rendered with inline
   * hyperlinks on mentioned entity names + trailing [N] citations for implicit
   * refs (via <NarrativeCitedText />). The separate regarding-name link line
   * below the narrative is suppressed to avoid duplication.
   * When omitted or empty, the classic plain-text + separate-link render is
   * preserved (back-compat).
   */
  references?: NarrativeBulletReferenceResult[];
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
  references,
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
        {references && references.length > 0 ? (
          // R7 W12 feedback items 2/3/4 (2026-07-01): inline entity-name links
          // + trailing [N] citations. Renderer handles both mentioned + implicit
          // refs; the separate regarding-name line below is suppressed because
          // the mentioned refs already surface it inline.
          <NarrativeCitedText
            narrative={narrative}
            references={references}
            onOpenRecord={onOpenRecord}
            textSize={300}
          />
        ) : (
          <Text size={300} className={styles.narrativeText}>
            {narrative}
          </Text>
        )}
        {/* Legacy standalone regarding-name link — only rendered when the
             bullet has NO references[] (back-compat / narrator degraded path). */}
        {(!references || references.length === 0) && primaryEntityName && primaryEntityType && primaryEntityId && (
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
          R7 Wave 12 task 134 — PRIMARY visible 'Add To Do' tool.

          Operator MVP requirement (wave12 plan §2.1):
            "for the tools only need the 'Add To Do' (so just the checkmark —
             but in future we will add other tools so don't remove the three
             dot tool menu)"

          Implementation:
            - Fluent v9 IconButton with CheckmarkRegular icon (Fluent UI v9 per
              ADR-006; semantic-token color per ADR-021 — no hardcoded colors).
            - aria-label "Add To Do" (operator wording). DISTINCT from the
              SubRowTodo aria-label "Add to To Do" so per-bullet vs per-sub-row
              targeting is unambiguous for tests and screen readers.
            - Wraps in Fluent v9 Tooltip surfacing live state (default / added
              / pending / error) — matches the SubRowTodo tooltip pattern.
            - Disabled when isTodoCreated or isTodoPending (prevents duplicates).
            - Delegates to the SAME `handleMenuAddToTodo` handler used by the
              prior menu item → onAddToTodo(itemIds) → parent's
              useInlineTodoCreate (ADR-024 wiring unchanged).
        */}
        <Tooltip content={addToDoLabel} relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={
              <CheckmarkRegular
                className={isTodoCreated || isTodoPending ? styles.addTodoIconDisabled : styles.addTodoIcon}
              />
            }
            aria-label="Add To Do"
            onClick={handleMenuAddToTodo}
            disabled={isTodoCreated || isTodoPending}
          />
        </Tooltip>
        {/*
          R4 FR-18 / R7 Wave 12 task 134 — Three-dot overflow menu PRESERVED
          for future tool additions per operator emphasis. Today it contains
          the 5 secondary actions (Add To Do promoted to primary above).

          Fluent v9 Menu primitive handles keyboard nav (Tab/Enter to open,
          arrows to move, Enter to select, Esc to close) and ARIA roles
          out-of-box. MenuButton meets WCAG ≥44×44px touch target at the
          Fluent v9 default size.

          Canonical order (post-task-134):
            1. Mark as read                     (R3 onCheck)
            2. Remove from briefing             (R3 onRemove)
            3. Keep on briefing for 7 more days (R3 onKeep)
            4. Dismiss                          (FR-14a onDismiss)
            5. Open record                      (FR-18 onOpenRecord)

          Items 1/2/3 hide when their callback is undefined (defensive default,
          back-compat). Item 5 hides when primaryEntityType/Id are missing.
        */}
        <Menu>
          <MenuTrigger disableButtonEnhancement>
            <MenuButton appearance="subtle" size="small" icon={<MoreVerticalRegular />} aria-label="More actions" />
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
