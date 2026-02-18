import * as React from "react";
import {
  makeStyles,
  tokens,
  Title3,
  Button,
  CounterBadge,
} from "@fluentui/react-components";
import { AlertRegular } from "@fluentui/react-icons";
import { ThemeToggle } from "./ThemeToggle";
import { NotificationPanel } from "../NotificationPanel/NotificationPanel";
import { useNotifications } from "../../hooks/useNotifications";

const useStyles = makeStyles({
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    boxSizing: "border-box",
  },
  title: {
    color: tokens.colorNeutralForeground1,
    flex: "1 1 auto",
    minWidth: 0,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  actions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
    flex: "0 0 auto",
  },
  notificationWrapper: {
    position: "relative",
    display: "inline-flex",
    alignItems: "center",
  },
  badge: {
    position: "absolute",
    top: "0px",
    right: "0px",
    transform: "translate(30%, -30%)",
    pointerEvents: "none",
  },
});

export const PageHeader: React.FC = () => {
  const styles = useStyles();
  const [isNotificationPanelOpen, setIsNotificationPanelOpen] =
    React.useState<boolean>(false);

  const {
    notifications,
    isLoading,
    unreadCount,
    markAsRead,
    markAllAsRead,
    refresh,
  } = useNotifications();

  const handleNotificationClick = React.useCallback(() => {
    setIsNotificationPanelOpen((prev) => !prev);
  }, []);

  const handleClosePanel = React.useCallback(() => {
    setIsNotificationPanelOpen(false);
  }, []);

  return (
    <>
      <header className={styles.header} role="banner">
        <Title3 className={styles.title}>Legal Operations Workspace</Title3>

        <div className={styles.actions}>
          <div className={styles.notificationWrapper}>
            <Button
              appearance="subtle"
              icon={<AlertRegular />}
              onClick={handleNotificationClick}
              aria-label={
                unreadCount > 0
                  ? `Notifications (${unreadCount} unread)`
                  : "Notifications"
              }
              aria-expanded={isNotificationPanelOpen}
              aria-controls="notification-panel"
            />
            {unreadCount > 0 && (
              <CounterBadge
                className={styles.badge}
                count={unreadCount}
                size="small"
                color="danger"
                appearance="filled"
                aria-hidden="true"
              />
            )}
            {/* Screen reader live region for notification count changes */}
            <span
              role="status"
              aria-live="polite"
              aria-atomic="true"
              style={{ position: "absolute", width: "1px", height: "1px", overflow: "hidden", clip: "rect(0,0,0,0)", whiteSpace: "nowrap" }}
            >
              {unreadCount > 0 ? `${unreadCount} unread notification${unreadCount === 1 ? "" : "s"}` : ""}
            </span>
          </div>

          <ThemeToggle />
        </div>
      </header>

      {/* Notification panel drawer — rendered adjacent to header but portals to document.body */}
      <NotificationPanel
        isOpen={isNotificationPanelOpen}
        onClose={handleClosePanel}
        notifications={notifications}
        isLoading={isLoading}
        onMarkAsRead={markAsRead}
        onMarkAllAsRead={markAllAsRead}
        onRefresh={refresh}
      />
    </>
  );
};
