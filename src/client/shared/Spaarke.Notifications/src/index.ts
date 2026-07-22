// Types (mirrors of the server-side wire contract — task 013 envelopes, task 020 signal)
export type {
  NotificationKind,
  NotificationEnvelope,
  CommunicationEnvelope,
  SuggestionEnvelope,
  NotificationSignal,
  PendingNotificationItem,
  NotificationEvent,
  NotificationEventSource,
} from './types';
export { ACTIVE_NOTIFICATION_KINDS, RESERVED_NOTIFICATION_KINDS, ALL_NOTIFICATION_KINDS, isKnownNotificationKind } from './types';

// Negotiate/connect (task 021 step 3)
export { negotiate, connectSignalR, SignalRUnavailableError } from './negotiate';
export type { NegotiateResponse } from './negotiate';

// Kind-routing (task 021 step 4)
export { KindRouter } from './kindRouter';
export type { NotificationHandler } from './kindRouter';

// Poll fallback (task 021 step 5)
export { startPollFallback, DEFAULT_POLL_INTERVAL_MS, DEFAULT_POLL_MAX_BACKOFF_MS } from './pollFallback';
export type { PollFallbackOptions, PollFallbackHandle } from './pollFallback';

// The public entrypoint — see README.md
export { NotificationsClient } from './NotificationsClient';
export type { NotificationsClientOptions, NotificationsConnectionState } from './NotificationsClient';
