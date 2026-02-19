import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { AlertRegular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: "48px",
    paddingBottom: "48px",
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
    flex: "1 1 auto",
  },
  iconWrapper: {
    color: tokens.colorNeutralForeground4,
    fontSize: "48px",
    lineHeight: "1",
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
    maxWidth: "280px",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface IEmptyStateProps {
  /** Whether empty because filters returned nothing, or because there are no notifications at all */
  reason: "no-notifications" | "no-match";
}

export const EmptyState: React.FC<IEmptyStateProps> = ({ reason }) => {
  const styles = useStyles();

  const heading =
    reason === "no-notifications"
      ? "No notifications"
      : "No matching notifications";

  const description =
    reason === "no-notifications"
      ? "You're all caught up. New activity across your matters and projects will appear here."
      : "No notifications match the selected filters. Try removing some filters to see more results.";

  return (
    <div className={styles.container} role="status" aria-live="polite">
      <div className={styles.iconWrapper} aria-hidden="true">
        <AlertRegular style={{ fontSize: "48px" }} />
      </div>
      <Text size={400} className={styles.heading}>
        {heading}
      </Text>
      <Text size={200} className={styles.description}>
        {description}
      </Text>
    </div>
  );
};
