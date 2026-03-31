/**
 * EmptyState — "You're all caught up!" display for the Daily Briefing
 * when there are no unread notifications.
 *
 * ADR-021: Uses Fluent v9 tokens for all styling; supports dark mode.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Caption1,
} from "@fluentui/react-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 design tokens - ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    paddingTop: "64px",
    paddingBottom: "64px",
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
    flex: "1 1 auto",
  },
  iconWrapper: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: "48px",
    lineHeight: "1",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  heading: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    textAlign: "center",
  },
  description: {
    color: tokens.colorNeutralForeground3,
    textAlign: "center",
    maxWidth: "320px",
  },
  timestamp: {
    color: tokens.colorNeutralForeground4,
    textAlign: "center",
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface EmptyStateProps {
  /** Optional last-checked timestamp. Defaults to current time if omitted. */
  lastChecked?: Date;
}

/**
 * EmptyState renders when the Daily Digest has no unread notifications.
 * Shows a success icon, "You're all caught up!" heading, and a
 * last-checked timestamp.
 */
export const EmptyState: React.FC<EmptyStateProps> = ({ lastChecked }) => {
  const styles = useStyles();

  const checkedTime = lastChecked ?? new Date();
  const timeLabel = checkedTime.toLocaleTimeString(undefined, {
    hour: "numeric",
    minute: "2-digit",
  });

  return (
    <div className={styles.container} role="status" aria-live="polite">
      <div className={styles.iconWrapper} aria-hidden="true">
        <CheckmarkCircleRegular style={{ fontSize: "48px" }} />
      </div>
      <Text size={500} className={styles.heading}>
        You're all caught up!
      </Text>
      <Text size={300} className={styles.description}>
        No unread notifications. New activity across your matters and projects
        will appear here automatically.
      </Text>
      <Caption1 className={styles.timestamp}>
        Last checked at {timeLabel}
      </Caption1>
    </div>
  );
};
