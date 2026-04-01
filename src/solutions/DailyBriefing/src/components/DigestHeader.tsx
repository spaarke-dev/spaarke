/**
 * DigestHeader — Header bar for the Daily Briefing digest.
 *
 * Shows the title, unread count, refresh button, and a slot for
 * the preferences dropdown.
 *
 * ADR-021: Uses Fluent v9 tokens for all styling; supports dark mode.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Title2,
  Text,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import {
  AlertRegular,
  ArrowClockwiseRegular,
} from "@fluentui/react-icons";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  icon: {
    fontSize: "24px",
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  titleGroup: {
    flex: 1,
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  unreadCount: {
    color: tokens.colorNeutralForeground3,
  },
  actions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DigestHeaderProps {
  /** Total number of unread notifications across all channels. */
  totalUnreadCount: number;
  /** Called when the user clicks the refresh button. */
  onRefresh?: () => void;
  /** Slot for the preferences dropdown (rendered in the actions area). */
  preferencesSlot?: React.ReactNode;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const DigestHeader: React.FC<DigestHeaderProps> = ({
  totalUnreadCount,
  onRefresh,
  preferencesSlot,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <AlertRegular className={styles.icon} />
      <div className={styles.titleGroup}>
        <Title2>Daily Briefing</Title2>
        {totalUnreadCount > 0 && (
          <Text size={200} className={styles.unreadCount}>
            {totalUnreadCount} unread
          </Text>
        )}
      </div>
      <div className={styles.actions}>
        {onRefresh && (
          <Tooltip content="Refresh notifications" relationship="label">
            <Button
              appearance="subtle"
              size="small"
              icon={<ArrowClockwiseRegular />}
              onClick={onRefresh}
              aria-label="Refresh notifications"
            />
          </Tooltip>
        )}
        {preferencesSlot}
      </div>
    </div>
  );
};
