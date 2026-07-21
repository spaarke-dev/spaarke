/**
 * ConversationView.types.ts
 *
 * Domain/prop types for `<ConversationView />` (task 011, FR-02/03) — a
 * Teams-style chat-bubble presentation layer over the SAME conversation core
 * `<CommunicationTimeline />` uses (reducer + `useThreadPoll` + `buildTimeline`,
 * task 060). Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework`
 * references. All platform I/O is injected via props (`authenticatedFetch`),
 * exactly as `<CommunicationTimeline/>`/`<EmailComposer/>` do (ADR-028 — no
 * `@spaarke/auth` import here).
 *
 * Renders a thread's messages as bubbles AND (task 013 / FR-06) hosts a
 * minimal Teams-style chat input at the bottom that sends through the EXISTING
 * send path (`sendTimelineMessage` → `sendCommunication`, ADR-045) on the ACS
 * Message branch — one send engine, not duplicated. See `ConversationView.tsx`.
 */
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { TimelineEntry } from '../CommunicationTimeline/CommunicationTimeline.buildTimeline';

export interface ConversationViewProps {
  // — Auth (injected per shared-lib decoupling rule, ADR-028) —
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded to `communicationTimelineApi`. */
  bffBaseUrl?: string;

  /** The thread to render (`sprk_communication` rows sharing this thread anchor). */
  threadId: string;

  /**
   * The CALLER's own Dataverse `systemuserid` (GUID string). Mine/others
   * bubble alignment is `message.senderSystemUserId === currentUserSystemUserId`
   * — STRICTLY an identity comparison (FR-02/FR-18), never an email-string
   * comparison. The host resolves this from its own auth/user context (e.g.
   * `context.userSettings.userId` in a PCF, or the host's session user) and
   * passes it in; this component never resolves it itself (ADR-012 —
   * context-agnostic, no platform API calls).
   */
  currentUserSystemUserId: string;

  /** Poll interval in ms. Default 5000 (NFR-07, matches the core's default). */
  pollIntervalMs?: number;

  /** Fired when a poll fails (the component also renders an inline error). */
  onError?: (error: Error) => void;

  /** Optional className applied to the root layout container. */
  className?: string;
}

/**
 * Own-bubble delivery status. The read-model (`ThreadMessageDto`) carries no
 * delivery-status field today — every message this view renders is already a
 * PERSISTED row (a successful send), so the only status this view can
 * honestly report is `'sent'`. `'delivered'`/`'failed'` are modeled now so a
 * future optimistic-send layer (task 013's compose box) can pass a richer
 * status without a breaking type change; `ConversationView` itself never
 * produces `'delivered'`/`'failed'` from persisted data alone.
 */
export type MessageBubbleStatus = 'sent' | 'delivered' | 'failed';

/** One row in the rendered list: either a day-boundary divider or a message bubble. */
export type ConversationRenderItem =
  | { kind: 'divider'; key: string; label: string }
  | { kind: 'message'; key: string; entry: TimelineEntry };
