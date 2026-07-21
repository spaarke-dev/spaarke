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
import type { TimelineMessage } from '../CommunicationTimeline/CommunicationTimeline.types';

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

  /**
   * Fired when the user activates the open-icon on an EMAIL-in-flow block
   * (task 021, FR-04). Email-type communications render as a compact
   * subject/from/to block (`<EmailInFlowBlock />`) rather than a chat bubble;
   * this callback hands the row back so the HOST can open the extended
   * `<SendEmailDialog />` (task 020: `threadId` / `regarding` / `recordLink`)
   * in view/detail for that email — reading its now-enriched
   * subject/from/to/body, or composing with thread + regarding context.
   *
   * ConversationView stays context-agnostic (ADR-012): it has
   * `authenticatedFetch` / `bffBaseUrl` / `threadId` but NOT the
   * `regarding` / recipient-directory / user context the dialog needs, so it
   * never mounts the dialog itself. This decoupled seam mirrors task 023's
   * `onPin`. Omit it and the open-icon is a no-op.
   */
  onOpenEmail?: (message: TimelineMessage) => void;

  /** Optional className applied to the root layout container. */
  className?: string;
}

/**
 * Imperative handle exposed by `<ConversationView />` via `React.forwardRef`
 * + `useImperativeHandle` (task 023, FR-05). Minimal + additive: it does NOT
 * change the component's scroll model — it only lets a host (e.g. a
 * `MessageQuickView` "open→pin" action, a search result, or the right-pane
 * PCF) jump the already-rendered list to a specific message and flash a
 * transient highlight on it. `forwardRef` is transparent to existing callers
 * that pass no ref (`<ConversationView .../>` keeps working unchanged).
 */
export interface ConversationViewHandle {
  /**
   * Scrolls the bubble whose `TimelineMessage.id === messageId` into view and
   * applies a transient highlight (auto-clears after ~1.5s). No-op when the
   * id is not currently rendered (e.g. hidden by the in-conversation filters,
   * or not in this thread) — never throws.
   */
  scrollToMessage(messageId: string): void;
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
  { kind: 'divider'; key: string; label: string } | { kind: 'message'; key: string; entry: TimelineEntry };
