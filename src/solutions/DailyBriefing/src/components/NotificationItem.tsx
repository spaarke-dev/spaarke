/**
 * NotificationItem — Renders a single notification with title, body,
 * timestamp, priority badge, and action link.
 *
 * Uses Fluent v9 tokens exclusively (ADR-021). No hard-coded colors.
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Body1,
  Caption1,
  Badge,
  Button,
  mergeClasses,
} from "@fluentui/react-components";
import {
  OpenRegular,
  CheckmarkRegular,
} from "@fluentui/react-icons";
import type { NotificationItem as NotificationItemType } from "../types/notifications";

// ---------------------------------------------------------------------------
// Styles (Fluent v9 semantic tokens only — ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    cursor: "pointer",
    transitionProperty: "background-color",
    transitionDuration: tokens.durationNormal,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  unread: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
  },
  content: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  titleRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  titleRead: {
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground3,
  },
  body: {
    color: tokens.colorNeutralForeground2,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  meta: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
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
// Priority badge color mapping (using Fluent badge colors)
// ---------------------------------------------------------------------------

const PRIORITY_BADGE_COLOR: Record<
  NotificationItemType["priority"],
  "danger" | "warning" | "informative" | "subtle"
> = {
  urgent: "danger",
  high: "warning",
  normal: "informative",
  low: "subtle",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function formatRelativeTime(isoDate: string): string {
  const now = Date.now();
  const then = new Date(isoDate).getTime();
  const diffMs = now - then;
  const diffMin = Math.floor(diffMs / 60_000);

  if (diffMin < 1) return "Just now";
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  const diffDay = Math.floor(diffHr / 24);
  if (diffDay === 1) return "Yesterday";
  return `${diffDay}d ago`;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface NotificationItemProps {
  /** The notification data. */
  item: NotificationItemType;
  /** Called when the user marks this notification as read. */
  onMarkAsRead?: (notificationId: string) => void;
  /** Called when the user clicks the action link / item row. */
  onNavigate?: (actionUrl: string) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NotificationItem: React.FC<NotificationItemProps> = ({
  item,
  onMarkAsRead,
  onNavigate,
}) => {
  const styles = useStyles();

  const handleClick = React.useCallback(() => {
    if (item.actionUrl && onNavigate) {
      onNavigate(item.actionUrl);
    }
    if (!item.isRead && onMarkAsRead) {
      onMarkAsRead(item.id);
    }
  }, [item.actionUrl, item.id, item.isRead, onMarkAsRead, onNavigate]);

  const handleMarkRead = React.useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation();
      if (onMarkAsRead) {
        onMarkAsRead(item.id);
      }
    },
    [item.id, onMarkAsRead]
  );

  return (
    <div
      className={mergeClasses(styles.root, !item.isRead && styles.unread)}
      onClick={handleClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          handleClick();
        }
      }}
      aria-label={`${item.isRead ? "" : "Unread: "}${item.title}`}
    >
      <div className={styles.content}>
        <div className={styles.titleRow}>
          <Body1
            className={mergeClasses(
              styles.title,
              item.isRead && styles.titleRead
            )}
          >
            {item.title}
          </Body1>
          {item.priority !== "normal" && (
            <Badge
              size="small"
              appearance="filled"
              color={PRIORITY_BADGE_COLOR[item.priority]}
            >
              {item.priority}
            </Badge>
          )}
        </div>
        <Caption1 className={styles.body}>{item.body}</Caption1>
        <div className={styles.meta}>
          <Caption1>{formatRelativeTime(item.createdOn)}</Caption1>
          {item.regardingName && (
            <Caption1>{item.regardingName}</Caption1>
          )}
        </div>
      </div>

      <div className={styles.actions}>
        {!item.isRead && onMarkAsRead && (
          <Button
            appearance="subtle"
            size="small"
            icon={<CheckmarkRegular />}
            onClick={handleMarkRead}
            aria-label="Mark as read"
            title="Mark as read"
          />
        )}
        {item.actionUrl && (
          <Button
            appearance="subtle"
            size="small"
            icon={<OpenRegular />}
            onClick={(e) => {
              e.stopPropagation();
              onNavigate?.(item.actionUrl);
            }}
            aria-label="Open"
            title="Open"
          />
        )}
      </div>
    </div>
  );
};
