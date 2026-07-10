/**
 * NarrativeBullet -- renders a single item row deterministically from its
 * own source fields, with a primary `Add To Do` checkmark icon button AND a
 * preserved three-dot overflow action menu.
 *
 * R5 task 011 (FR-A1, 2026-07-08): PRIOR to task 010, `narrative` carried
 * per-channel LLM-authored prose and an optional `references[]` array (an
 * LLM narrative could mention several DIFFERENT entities in free text). Task
 * 010 removed the per-channel LLM call entirely — `narrative` is now built
 * server-side directly from the source item's own `Title` field
 * (`DailyBriefingNarrator.BuildDeterministicBullet`), and there is at most
 * ONE entity per bullet (its own regarding record). This component was
 * updated to match: the `references[]` prop is REMOVED (no per-channel LLM
 * narrative is ever consumed here), and `narrative` + `primaryEntityName` /
 * `primaryEntityType` / `primaryEntityId` are ALWAYS the same scalar props
 * passed together by the caller (see `ActivityNotesSection`, which
 * destructures every field from the SAME `bullet` object) — cross-item
 * pairing is structurally impossible: there is nowhere in this component's
 * props for a second item's fields to enter.
 *
 * Each bullet shows the item's title text, a clickable regarding-name link
 * that opens the entity in a Dataverse dialog (inline when the regarding
 * name is textually present in the title, else a trailing link — see
 * `NarrativeCitedText`), a PRIMARY visible Fluent v9 `Checkmark` icon button
 * ('Add To Do') and a Fluent v9 three-dot overflow `Menu` containing the
 * remaining secondary actions.
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
  Badge,
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
import { formatDueDate } from '../utils/formatDueDate';
import { SubRow } from './SubRow';
import { NarrativeCitedText, hasInlineRegardingMention } from './NarrativeCitedText';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    marginBottom: tokens.spacingVerticalXXS,
    ':hover': {
      backgroundColor: tokens.colorSubtleBackgroundHover,
    },
  },
  content: {
    flex: 1,
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  // R5 richer-rows: record description under the title, truncated to 2 lines.
  description: {
    color: tokens.colorNeutralForeground2,
    display: '-webkit-box',
    WebkitLineClamp: 2,
    WebkitBoxOrient: 'vertical' as const,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // R5 richer-rows: subtle "Updated {date}" caption — the qualifying-date signal.
  dateCaption: {
    color: tokens.colorNeutralForeground3,
  },
  metaRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    marginTop: '2px',
  },
  entityLink: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    cursor: 'pointer',
    textDecorationLine: 'none',
    ':hover': {
      textDecorationLine: 'underline',
      color: tokens.colorBrandForeground1,
    },
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
  /**
   * The item's own title text. R5 task 010 (FR-A3): built server-side
   * directly from the source record's `Title` field — deterministic, never
   * LLM-authored. R5 task 011 (FR-A1): the prior `references[]` prop (an
   * LLM-narrative-era mechanism for mentioning multiple different entities
   * in free text) was REMOVED — this component never consumes a per-channel
   * LLM narrative field. The regarding-name link below is always derived
   * from `primaryEntityName`/`primaryEntityType`/`primaryEntityId`, which
   * MUST be sourced from the SAME item as `narrative` (see
   * `ActivityNotesSection`, which destructures both from one `bullet`
   * object per row).
   */
  narrative: string;
  /** Display name of the primary entity (shown as clickable link). SAME item as `narrative`. */
  primaryEntityName: string;
  /** Dataverse logical name of the primary entity. SAME item as `narrative`. */
  primaryEntityType: string;
  /** GUID of the primary entity record. SAME item as `narrative`. */
  primaryEntityId: string;
  /** Notification IDs covered by this bullet. */
  itemIds: string[];
  /**
   * Optional deterministic due-status pill (R5 task 021 redesign) — Overdue /
   * Due today / Due soon. Rendered on the meta line; supplied by the combined
   * "Tasks" section. Derived from the item's due date, never from LLM text.
   */
  dueStatus?: 'Overdue' | 'DueToday' | 'DueSoon';
  /**
   * R5 richer-rows (2026-07-09): the record's own description/subject, shown truncated
   * beneath the title. Deterministic source field; omitted/empty → not rendered.
   */
  description?: string;
  /**
   * R5 richer-rows (2026-07-09): ISO 8601 date that qualified this record for the briefing
   * (source ModifiedOn), shown as an "Updated {date}" caption. Omitted/empty → not rendered.
   */
  date?: string;
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
  dueStatus,
  description,
  date,
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

  // R5 task 011 (FR-A1): whether the regarding-name is textually present in
  // the (deterministic, item.title-sourced) narrative — decides whether
  // NarrativeCitedText inlines it as a link, or whether the separate
  // regarding-name link line below renders instead. Both branches source
  // EXCLUSIVELY from `narrative` + `primaryEntityName`/`Type`/`Id`, which are
  // always the SAME item's fields (see NarrativeBulletProps doc comment) —
  // cross-item pairing is structurally impossible.
  const hasRegardingTarget = Boolean(primaryEntityName && primaryEntityType && primaryEntityId);
  const isRegardingInlined =
    hasRegardingTarget && hasInlineRegardingMention(narrative, primaryEntityName, primaryEntityType, primaryEntityId);

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

  // R5 task 021: deterministic status pill for the combined "Tasks" section.
  const statusBadge =
    dueStatus === 'Overdue'
      ? { label: 'Overdue', color: 'danger' as const }
      : dueStatus === 'DueToday'
        ? { label: 'Due today', color: 'warning' as const }
        : dueStatus === 'DueSoon'
          ? { label: 'Due soon', color: 'informative' as const }
          : null;

  // R5 richer-rows (2026-07-09): render the record's description + an "Updated {date}"
  // caption (the qualifying date). Both are deterministic source fields.
  const trimmedDescription = description?.trim() ?? '';
  const hasDescription = trimmedDescription.length > 0;
  const formattedUpdated = ((): string | null => {
    if (!date) return null;
    const d = new Date(date);
    if (isNaN(d.getTime())) return null;
    try {
      return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(d);
    } catch {
      return null;
    }
  })();

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
      <div className={styles.content}>
        {/* R5 task 011 (FR-A1): item text + regarding-name link, both
             deterministically sourced from THIS bullet's own fields (never a
             per-channel LLM narrative). NarrativeCitedText inlines the
             regarding name when it's textually present in `narrative`;
             otherwise the meta line below carries the (same-item) regarding
             link — never both, so there's exactly one visible link per row. */}
        <NarrativeCitedText
          narrative={narrative}
          regardingName={primaryEntityName}
          regardingEntityType={primaryEntityType}
          regardingId={primaryEntityId}
          onOpenRecord={onOpenRecord}
          textSize={300}
        />
        {/* R5 richer-rows: the record's own description, truncated to 2 lines. */}
        {hasDescription && (
          <Text size={200} className={styles.description}>
            {trimmedDescription}
          </Text>
        )}
        {/* Meta line: status pill (Tasks) or due-date pill + "Updated {date}" + (non-inlined) regarding link. */}
        {(statusBadge || singleItemDueDate || formattedUpdated || (hasRegardingTarget && !isRegardingInlined)) && (
          <div className={styles.metaRow}>
            {statusBadge && (
              <Badge appearance="tint" color={statusBadge.color} size="small">
                {statusBadge.label}
              </Badge>
            )}
            {!statusBadge && singleItemDueDate && (
              <Badge appearance="tint" color={isSingleItemOverdue ? 'danger' : 'informative'} size="small">
                {singleItemDueDate}
              </Badge>
            )}
            {formattedUpdated && (
              <Text size={200} className={styles.dateCaption}>
                Updated {formattedUpdated}
              </Text>
            )}
            {hasRegardingTarget && !isRegardingInlined && (
              <Text
                size={200}
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
          </div>
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
        {/* Operator (2026-07-09): "Add to To Do" moved into the overflow menu —
             the row now shows only the three-dot menu trigger. The menu component
             is preserved for future tools per the R7 W12 task-134 note. */}
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
              {/* Canonical position 4 (per FR-18 order): "Add to To Do" moved from a
                  standalone button into the menu (R5 task 021). */}
              <MenuItem
                icon={<CheckmarkRegular />}
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
