/**
 * SubRowLink -- per-item entity link slot for the NarrativeBullet sub-list (FR-12).
 *
 * Used by `NarrativeBullet` when `itemIds.length > 1` to render the leftmost
 * slot of each indented sub-row. This file is the placeholder created in task
 * 020 (Wave 8); the real entity-link behavior is implemented in task 021
 * (Wave 9 -- per-item entity link uses supplied `regardingId`, opens record
 * via `Xrm.Navigation.navigateTo({ pageType: "entityrecord", ... })`).
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

import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import type { NotificationItem } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  placeholder: {
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
// Component (placeholder -- task 021 fills in real entity-link behavior)
// ---------------------------------------------------------------------------

export const SubRowLink: React.FC<SubRowLinkProps> = ({ item }) => {
  const styles = useStyles();

  // Placeholder: render the regarding-name as plain compact text. Task 021
  // replaces this with the clickable entity link that calls
  // Xrm.Navigation.navigateTo per FR-12.
  return (
    <Text className={styles.placeholder} truncate wrap={false}>
      {item.regardingName || item.title}
    </Text>
  );
};
