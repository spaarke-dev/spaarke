import * as React from "react";
import {
  makeStyles,
  tokens,
  Badge,
  Text,
  mergeClasses,
} from "@fluentui/react-components";
import {
  DocumentRegular,
  ReceiptRegular,
  InfoRegular,
  SparkleRegular,
  PersonRegular,
} from "@fluentui/react-icons";
import { INotificationItem, NotificationCategory } from "../../types";
import { formatRelativeTime } from "./notificationTypes";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  item: {
    display: "flex",
    flexDirection: "row",
    alignItems: "flex-start",
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground1,
    cursor: "default",
    transition: "background-color 0.15s ease",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ":last-child": {
      borderBottomWidth: "0px",
    },
  },
  itemUnread: {
    backgroundColor: tokens.colorNeutralBackground2,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
  },
  iconWrapper: {
    flex: "0 0 auto",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    width: "32px",
    height: "32px",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorNeutralBackground3,
    marginTop: tokens.spacingVerticalXXS,
  },
  body: {
    flex: "1 1 auto",
    minWidth: 0,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  titleRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    flex: "1 1 auto",
    minWidth: 0,
  },
  titleUnread: {
    fontWeight: tokens.fontWeightBold,
  },
  description: {
    color: tokens.colorNeutralForeground2,
    overflow: "hidden",
    display: "-webkit-box",
    // @ts-ignore — Griffel supports webkit vendor properties
    WebkitBoxOrient: "vertical",
    WebkitLineClamp: "2",
  },
  meta: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flexWrap: "wrap",
    marginTop: tokens.spacingVerticalXXS,
  },
  timestamp: {
    color: tokens.colorNeutralForeground4,
  },
  unreadDot: {
    flex: "0 0 auto",
    width: "8px",
    height: "8px",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandForeground1,
    alignSelf: "center",
    marginLeft: "auto",
    marginTop: "2px",
  },
});

// ---------------------------------------------------------------------------
// Category icon and badge color helpers
// ---------------------------------------------------------------------------

interface ICategoryConfig {
  icon: React.ReactElement;
  /**
   * Badge color — uses Fluent palette tokens via inline style since makeStyles
   * cannot use dynamic values for color properties derived from runtime data.
   * All colors are Fluent semantic palette tokens resolved at runtime.
   */
  badgeColor: "brand" | "danger" | "important" | "informative" | "severe" | "subtle" | "success" | "warning";
}

function getCategoryConfig(category: NotificationCategory): ICategoryConfig {
  switch (category) {
    case "Documents":
      return {
        icon: <DocumentRegular />,
        badgeColor: "brand",
      };
    case "Invoices":
      return {
        icon: <ReceiptRegular />,
        badgeColor: "success",
      };
    case "Status":
      return {
        icon: <InfoRegular />,
        badgeColor: "warning",
      };
    case "Analysis":
      return {
        icon: <SparkleRegular />,
        badgeColor: "important",
      };
    default:
      return {
        icon: <PersonRegular />,
        badgeColor: "subtle",
      };
  }
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export interface INotificationItemProps {
  notification: INotificationItem;
  onMarkAsRead?: (id: string) => void;
}

export const NotificationItem: React.FC<INotificationItemProps> = ({
  notification,
  onMarkAsRead,
}) => {
  const styles = useStyles();
  const { icon, badgeColor } = getCategoryConfig(notification.category);
  const relativeTime = formatRelativeTime(notification.timestamp);

  const handleClick = React.useCallback(() => {
    if (!notification.isRead && onMarkAsRead) {
      onMarkAsRead(notification.id);
    }
  }, [notification.id, notification.isRead, onMarkAsRead]);

  const handleKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        handleClick();
      }
    },
    [handleClick]
  );

  return (
    <div
      className={mergeClasses(
        styles.item,
        !notification.isRead && styles.itemUnread
      )}
      role="listitem"
      tabIndex={0}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
      aria-label={`${notification.category} notification: ${notification.title}. ${notification.isRead ? "Read" : "Unread"}. ${relativeTime}.`}
    >
      {/* Category icon */}
      <div className={styles.iconWrapper} aria-hidden="true">
        {icon}
      </div>

      {/* Body */}
      <div className={styles.body}>
        {/* Title row with category badge */}
        <div className={styles.titleRow}>
          <Text
            size={200}
            className={mergeClasses(
              styles.title,
              !notification.isRead && styles.titleUnread
            )}
          >
            {notification.title}
          </Text>
          <Badge
            size="small"
            color={badgeColor}
            appearance="filled"
          >
            {notification.category}
          </Badge>
        </div>

        {/* Description */}
        <Text size={200} className={styles.description}>
          {notification.description}
        </Text>

        {/* Timestamp */}
        <div className={styles.meta}>
          <Text size={100} className={styles.timestamp}>
            {relativeTime}
          </Text>
        </div>
      </div>

      {/* Unread indicator dot */}
      {!notification.isRead && (
        <div className={styles.unreadDot} aria-hidden="true" />
      )}
    </div>
  );
};
