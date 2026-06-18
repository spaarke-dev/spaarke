/**
 * Re-export shim — notificationService now lives in
 * `@spaarke/daily-briefing-components/services/notificationService`.
 *
 * R2 task 015 / FR-07 (2026-06-18):
 *   The notification fetch/group/mark-read service was hoisted into the
 *   shared `@spaarke/daily-briefing-components` package so the package no
 *   longer reaches back across the solution boundary. This file is kept as
 *   a re-export shim so any pre-existing consumer that imports from this
 *   path continues to work unchanged. New consumers SHOULD import from
 *   `@spaarke/daily-briefing-components/services` directly.
 *
 * Cleanup: remove this file once all in-repo consumers are migrated
 * (tracked as shared-lib-hygiene follow-up, not in R2 scope).
 */

export {
  fetchNotifications,
  fetchAndGroupNotifications,
  groupByCategory,
  markNotificationRead,
  markAllNotificationsRead,
} from "@spaarke/daily-briefing-components/services";
