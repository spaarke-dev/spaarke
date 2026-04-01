/**
 * CaughtUpFooter -- footer for channels with zero items.
 *
 * Displays a subtle "You're caught up on: ..." message listing the channels
 * that have no notifications, giving the user confidence that nothing was
 * missed.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 tokens only, dark mode via semantic tokens
 *   - Only rendered when there are zero-item channels
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { CheckmarkCircleRegular } from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only -- ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    marginTop: tokens.spacingVerticalXXL,
  },
  divider: {
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingTop: tokens.spacingVerticalM,
  },
  content: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  icon: {
    color: tokens.colorPaletteGreenForeground1,
    fontSize: "20px",
    flexShrink: 0,
    display: "flex",
    alignItems: "center",
  },
  text: {
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface CaughtUpFooterProps {
  /** Labels of channels that have zero items. */
  channelLabels: string[];
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CaughtUpFooter: React.FC<CaughtUpFooterProps> = ({
  channelLabels,
}) => {
  const styles = useStyles();

  // Don't render if there are no zero-item channels
  if (channelLabels.length === 0) {
    return null;
  }

  return (
    <div className={styles.root}>
      <div className={styles.divider}>
        <div className={styles.content}>
          <span className={styles.icon}>
            <CheckmarkCircleRegular />
          </span>
          <Text size={200} className={styles.text}>
            You're caught up on: {channelLabels.join(", ")}
          </Text>
        </div>
      </div>
    </div>
  );
};
