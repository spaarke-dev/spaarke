/**
 * ChannelHeading -- simple heading row for each notification channel.
 *
 * Displays the channel icon, label, and item count. No accordion behaviour;
 * this is a flat section heading.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingBottom: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalM,
  },
  icon: {
    fontSize: "20px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
  },
  label: {
    flex: 1,
  },
  count: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ChannelHeadingProps {
  /** Channel icon element (from channelIcons). */
  icon: React.ReactElement;
  /** Channel display label. */
  label: string;
  /** Number of items in this channel. */
  itemCount: number;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ChannelHeading: React.FC<ChannelHeadingProps> = ({
  icon,
  label,
  itemCount,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <span className={styles.icon}>{icon}</span>
      <Text size={400} weight="semibold" className={styles.label}>
        {label}
      </Text>
      <Text size={200} className={styles.count}>
        {itemCount} {itemCount === 1 ? "item" : "items"}
      </Text>
    </div>
  );
};
