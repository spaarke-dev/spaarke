import { useState, useCallback } from "react";
import { INotificationItem } from "../types";
import { MOCK_NOTIFICATIONS } from "../components/NotificationPanel/notificationTypes";

export interface IUseNotificationsResult {
  /** All notification items (read + unread) */
  notifications: INotificationItem[];
  /** Whether notifications are loading */
  isLoading: boolean;
  /** Number of unread notifications */
  unreadCount: number;
  /** Mark a single notification as read by ID */
  markAsRead: (id: string) => void;
  /** Mark all notifications as read */
  markAllAsRead: () => void;
  /** Refresh the notification list (for future backend integration) */
  refresh: () => void;
}

/**
 * useNotifications hook — R1 mock implementation.
 *
 * Returns mock notification data. Designed so that future iterations can
 * replace the internals with Xrm.WebApi calls or a BFF polling endpoint
 * without changing the hook's public interface.
 *
 * Future integration points:
 *   - Replace MOCK_NOTIFICATIONS with: Xrm.WebApi.retrieveMultipleRecords("sprk_event", ...)
 *   - Or: fetch(`${BFF_BASE}/api/workspace/notifications`)
 *   - Add polling interval via useEffect + setInterval
 */
export function useNotifications(): IUseNotificationsResult {
  const [notifications, setNotifications] = useState<INotificationItem[]>(
    MOCK_NOTIFICATIONS
  );
  const [isLoading] = useState<boolean>(false);

  const unreadCount = notifications.filter((n) => !n.isRead).length;

  const markAsRead = useCallback((id: string) => {
    setNotifications((prev) =>
      prev.map((n) => (n.id === id ? { ...n, isRead: true } : n))
    );
  }, []);

  const markAllAsRead = useCallback(() => {
    setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
  }, []);

  const refresh = useCallback(() => {
    // No-op in R1 (mock data is static).
    // Future: trigger Xrm.WebApi fetch or BFF poll here.
  }, []);

  return {
    notifications,
    isLoading,
    unreadCount,
    markAsRead,
    markAllAsRead,
    refresh,
  };
}
