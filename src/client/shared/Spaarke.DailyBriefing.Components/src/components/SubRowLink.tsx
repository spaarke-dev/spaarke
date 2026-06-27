/**
 * SubRowLink -- per-item entity link slot for the NarrativeBullet sub-list (FR-12).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the leftmost
 * slot of each indented sub-row. Implemented in task 021 (Wave 9).
 *
 * Behavior (FR-12):
 *   - The link text is the underlying `NotificationItem`'s display name
 *     (prefers `item.title`, falls back to `item.regardingName`).
 *   - On click, opens the entity record in a Dataverse modal dialog via
 *     `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName,
 *     entityId }, { target: 2, width: "80%", height: "80%" })`.
 *   - The link target uses the SUPPLIED `item.regardingEntityType` +
 *     `item.regardingId` directly from the underlying `NotificationItem`.
 *     NO AI-derived field is used (this is the dead-link fix for sub-rows).
 *
 * Graceful degradation:
 *   - When `item.regardingId` or `item.regardingEntityType` is missing/empty,
 *     the slot renders as plain (non-clickable) text -- no broken link.
 *   - When `window.Xrm.Navigation.navigateTo` is unavailable (e.g., test
 *     environment, standalone code page without host context), the click
 *     handler is a no-op -- no runtime exception.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 semantic tokens only, dark-mode parity.
 *   - FR-12: Sub-row entity link MUST use the supplied `regardingEntityType` +
 *     `regardingId` from the underlying `NotificationItem` (no AI involvement).
 *
 * Parallel-edit contract:
 *   Task 021 owns this file. Tasks 022 (SubRowTodo) and 023 (SubRowDismiss)
 *   own sibling files. The three Wave-9 agents can edit in parallel because
 *   each owns a distinct file -- no NarrativeBullet.tsx race.
 */

import * as React from 'react';
import { makeStyles, tokens, Link, Text } from '@fluentui/react-components';
import type { NotificationItem } from '../types/notifications';

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  link: {
    color: tokens.colorBrandForeground1,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    flex: 1,
    minWidth: 0,
    // Truncate long titles within the sub-row.
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    textDecorationLine: 'none',
    ':hover': {
      textDecorationLine: 'underline',
    },
  },
  plain: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    flex: 1,
    minWidth: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SubRowLinkProps {
  /** The underlying notification item this sub-row represents. */
  item: NotificationItem;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SubRowLink: React.FC<SubRowLinkProps> = ({ item }) => {
  const styles = useStyles();

  // Display name: prefer the item's title; fall back to the regarding name.
  const displayName = item.title || item.regardingName || '(untitled)';

  // FR-12 graceful degradation: if either the regarding entity type or id is
  // missing, render as plain text -- no clickable link.
  const hasTarget = Boolean(item.regardingEntityType && item.regardingId);

  const handleClick = (e: React.MouseEvent | React.KeyboardEvent) => {
    e.preventDefault();
    if (!hasTarget) return;

    // Resolve Xrm from window / parent / top (Spaarke host-context fallback
    // pattern -- mirrors NarrativeBullet.handleLinkClick). Guard against
    // missing Xrm in test or standalone environments.
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
        entityName: item.regardingEntityType,
        entityId: item.regardingId,
      },
      {
        target: 2,
        width: { value: 80, unit: '%' },
        height: { value: 80, unit: '%' },
      }
    ).catch(() => {
      /* user closed dialog or navigation cancelled -- non-fatal */
    });
  };

  // Plain-text fallback (no link target available).
  if (!hasTarget) {
    return (
      <Text className={styles.plain} truncate wrap={false}>
        {displayName}
      </Text>
    );
  }

  // Clickable Fluent v9 Link. Use href="#" + preventDefault so the element
  // is a real anchor (keyboard + accessibility) without triggering a hash
  // navigation when Xrm is unavailable.
  return (
    <Link
      href="#"
      appearance="default"
      className={styles.link}
      onClick={handleClick}
      onKeyDown={(e: React.KeyboardEvent) => {
        if (e.key === 'Enter' || e.key === ' ') {
          handleClick(e);
        }
      }}
      aria-label={`Open ${displayName}`}
      title={displayName}
    >
      {displayName}
    </Link>
  );
};
