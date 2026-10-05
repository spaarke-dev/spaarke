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
import { DismissRegular, ArrowClockwiseRegular, AlertRegular } from "@fluentui/react-icons";
import { EmptyState } from "@spaarke/ui-components";
import { INotificationItem, NotificationCategory } from "../../types";
import { NotificationItem } from "./NotificationItem";
import { NotificationFilters } from "./NotificationFilters";

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
  // Spacing of the pre-081 local NotificationPanel EmptyState, kept on top of
  // the shared compact EmptyState (task 081 / C-11) so the panel looks as before.
  emptyStateSpacing: {
    paddingTop: "48px",
    paddingBottom: "48px",
    paddingLeft: tokens.spacingHorizontalXXL,
    paddingRight: tokens.spacingHorizontalXXL,
    gap: tokens.spacingVerticalM,
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
            size="compact"
            className={styles.emptyStateSpacing}
            icon={<AlertRegular style={{ color: tokens.colorNeutralForeground4 }} />}
            heading={hasAnyNotifications ? "No matching notifications" : "No notifications"}
            description={
              hasAnyNotifications
                ? "No notifications match the selected filters. Try removing some filters to see more results."
                : "You're all caught up. New activity across your matters and projects will appear here."
            }
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
