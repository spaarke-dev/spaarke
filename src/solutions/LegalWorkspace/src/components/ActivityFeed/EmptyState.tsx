/**
 * EmptyState — displayed in the ActivityFeed when the active filter yields
 * zero results.
 *
 * Two reasons are supported:
 *   - "no-events"   : the feed has no events at all (all-filter is empty)
 *   - "no-match"    : the selected filter category returned zero items
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
} from "@fluentui/react-components";
import { FilterRegular, ListRegular } from "@fluentui/react-icons";
import { EventFilterCategory } from "../../types/enums";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: "56px",
    paddingBottom: "56px",
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
    flex: "1 1 auto",
  },
  iconWrapper: {
    color: tokens.colorNeutralForeground4,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  heading: {
    color: tokens.colorNeutralForeground2,
    fontWeight: tokens.fontWeightSemibold,
    textAlign: "center",
  },
  description: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
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
    <div
      className={styles.container}
      role="status"
      aria-live="polite"
      aria-label={heading}
    >
      <div className={styles.iconWrapper} aria-hidden="true">
        {isNoMatch ? (
          <FilterRegular style={{ fontSize: "48px" }} />
        ) : (
          <ListRegular style={{ fontSize: "48px" }} />
        )}
      </div>

      <Text size={400} className={styles.heading}>
        {heading}
      </Text>

      <Text size={200} className={styles.description}>
        {description}
      </Text>

      {isNoMatch && onClearFilter && (
        <Button
          appearance="subtle"
          size="small"
          onClick={onClearFilter}
        >
          Show all updates
        </Button>
      )}
    </div>
  );
};
