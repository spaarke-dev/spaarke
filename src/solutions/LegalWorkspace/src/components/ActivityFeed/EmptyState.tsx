/**
 * ActivityFeedEmptyState — displayed in the ActivityFeed when the active
 * filter yields zero results.
 *
 * Two reasons are supported:
 *   - "no-events"   : the feed has no events at all (all-filter is empty)
 *   - "no-match"    : the selected filter category returned zero items
 *
 * Renders the shared `EmptyState` from `@spaarke/ui-components` (task 081 /
 * C-11 — this was a hand-rolled copy of the same icon + heading + description
 * shape). Only the feed's copy, icon and "Show all updates" button live here;
 * `emptyStateSpacing` keeps the pre-081 56px band and `spacingVerticalM` gap,
 * and `description` its 300px width, on top of the shared compact size.
 */

import * as React from "react";
import { makeStyles, tokens, Button } from "@fluentui/react-components";
import { FilterRegular, ListRegular } from "@fluentui/react-icons";
import { EmptyState } from "@spaarke/ui-components";
import { EventFilterCategory } from "../../types/enums";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  emptyStateSpacing: {
    paddingTop: "56px",
    paddingBottom: "56px",
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
  },
  icon: {
    color: tokens.colorNeutralForeground4,
  },
  description: {
    maxWidth: "300px",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IActivityFeedEmptyStateProps {
  /** Whether there are genuinely no events or if the filter yielded nothing */
  reason: "no-events" | "no-match";
  /** The currently active filter (used in copy for no-match case) */
  activeFilter?: EventFilterCategory;
  /** Called when the user clicks "Show all updates" in no-match state */
  onClearFilter?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ActivityFeedEmptyState: React.FC<IActivityFeedEmptyStateProps> = ({
  reason,
  activeFilter,
  onClearFilter,
}) => {
  const styles = useStyles();

  const isNoMatch = reason === "no-match";

  const heading = isNoMatch
    ? "No items in this category"
    : "No updates yet";

  const description = isNoMatch
    ? `There are no updates matching the "${activeFilter ?? "selected"}" filter. Try selecting a different category.`
    : "New activity across your matters, projects, and documents will appear here.";

  return (
    <EmptyState
      size="compact"
      className={styles.emptyStateSpacing}
      descriptionClassName={styles.description}
      ariaLabel={heading}
      icon={isNoMatch ? <FilterRegular className={styles.icon} /> : <ListRegular className={styles.icon} />}
      heading={heading}
      description={description}
      footer={
        isNoMatch && onClearFilter ? (
          <Button appearance="subtle" size="small" onClick={onClearFilter}>
            Show all updates
          </Button>
        ) : undefined
      }
    />
  );
};
