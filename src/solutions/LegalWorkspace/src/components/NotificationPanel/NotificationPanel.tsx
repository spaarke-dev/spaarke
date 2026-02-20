import * as React from "react";
import {
  makeStyles,
  tokens,
  OverlayDrawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  Button,
  Text,
  Spinner,
} from "@fluentui/react-components";
import { DismissRegular, ArrowClockwiseRegular } from "@fluentui/react-icons";
import { INotificationItem, NotificationCategory } from "../../types";
import { NotificationItem } from "./NotificationItem";
import { NotificationFilters } from "./NotificationFilters";
import { EmptyState } from "./EmptyState";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  drawerBody: {
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    padding: "0px",
  },
  headerActions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  markAllButton: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1,
    minWidth: "auto",
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    height: "auto",
  },
  unreadCountText: {
    color: tokens.colorNeutralForeground3,
    marginLeft: tokens.spacingHorizontalXS,
  },
  notificationList: {
    flex: "1 1 auto",
    overflowY: "auto",
    overflowX: "hidden",
    display: "flex",
    flexDirection: "column",
  },
  loadingWrapper: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalXXL,
    flex: "1 1 auto",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface INotificationPanelProps {
  /** Whether the drawer is open */
  isOpen: boolean;
  /** Called to close the drawer */
  onClose: () => void;
  /** Notification items to display */
  notifications: INotificationItem[];
  /** Whether notification data is loading */
  isLoading?: boolean;
  /** Mark a single notification as read */
  onMarkAsRead?: (id: string) => void;
  /** Mark all notifications as read */
  onMarkAllAsRead?: () => void;
  /** Refresh notifications (manual refresh) */
  onRefresh?: () => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NotificationPanel: React.FC<INotificationPanelProps> = ({
  isOpen,
  onClose,
  notifications,
  isLoading = false,
  onMarkAsRead,
  onMarkAllAsRead,
  onRefresh,
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // Filter state — multi-select, empty set = show all
  // ---------------------------------------------------------------------------
  const [activeFilters, setActiveFilters] = React.useState<Set<NotificationCategory>>(
    new Set()
  );

  const handleToggleFilter = React.useCallback((category: NotificationCategory) => {
    setActiveFilters((prev) => {
      const next = new Set(prev);
      if (next.has(category)) {
        next.delete(category);
      } else {
        next.add(category);
      }
      return next;
    });
  }, []);

  // ---------------------------------------------------------------------------
  // Filtered list — OR logic: if any filters active, show items matching any
  // ---------------------------------------------------------------------------
  const filteredNotifications = React.useMemo(() => {
    if (activeFilters.size === 0) return notifications;
    return notifications.filter((n) => activeFilters.has(n.category));
  }, [notifications, activeFilters]);

  const unreadCount = React.useMemo(
    () => notifications.filter((n) => !n.isRead).length,
    [notifications]
  );

  const hasAnyNotifications = notifications.length > 0;
  const hasFilteredNotifications = filteredNotifications.length > 0;

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  const renderBody = () => {
    if (isLoading) {
      return (
        <div className={styles.loadingWrapper}>
          <Spinner label="Loading notifications..." labelPosition="below" />
        </div>
      );
    }

    return (
      <>
        {/* Filter bar */}
        <NotificationFilters
          activeFilters={activeFilters}
          onToggleFilter={handleToggleFilter}
        />

        {/* Notification list or empty state */}
        {!hasFilteredNotifications ? (
          <EmptyState
            reason={hasAnyNotifications ? "no-match" : "no-notifications"}
          />
        ) : (
          <div
            className={styles.notificationList}
            role="list"
            aria-label="Notifications"
            aria-live="polite"
          >
            {filteredNotifications.map((n) => (
              <NotificationItem
                key={n.id}
                notification={n}
                onMarkAsRead={onMarkAsRead}
              />
            ))}
          </div>
        )}
      </>
    );
  };

  return (
    <OverlayDrawer
      position="end"
      open={isOpen}
      onOpenChange={(_e, data) => {
        if (!data.open) onClose();
      }}
      aria-label="Notifications panel"
      style={{ width: "400px", maxWidth: "100vw" }}
    >
      <DrawerHeader>
        <DrawerHeaderTitle
          action={
            <div className={styles.headerActions}>
              {/* Unread count */}
              {unreadCount > 0 && (
                <Text size={200} className={styles.unreadCountText}>
                  {unreadCount} unread
                </Text>
              )}

              {/* Mark all as read */}
              {unreadCount > 0 && onMarkAllAsRead && (
                <Button
                  className={styles.markAllButton}
                  appearance="transparent"
                  size="small"
                  onClick={onMarkAllAsRead}
                  aria-label="Mark all notifications as read"
                >
                  Mark all read
                </Button>
              )}

              {/* Refresh button */}
              {onRefresh && (
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ArrowClockwiseRegular />}
                  onClick={onRefresh}
                  aria-label="Refresh notifications"
                  title="Refresh notifications"
                />
              )}

              {/* Close button */}
              <Button
                appearance="subtle"
                size="small"
                icon={<DismissRegular />}
                onClick={onClose}
                aria-label="Close notifications panel"
                title="Close"
              />
            </div>
          }
        >
          Notifications
        </DrawerHeaderTitle>
      </DrawerHeader>

      <DrawerBody className={styles.drawerBody}>
        {renderBody()}
      </DrawerBody>
    </OverlayDrawer>
  );
};
