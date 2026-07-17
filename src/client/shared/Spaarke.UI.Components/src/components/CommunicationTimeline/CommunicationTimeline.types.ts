/**
 * CommunicationTimeline.types.ts
 *
 * Domain types for the `<CommunicationTimeline />` polling timeline (task 060,
 * FR-10). Context-agnostic (ADR-012): no `Xrm`/`ComponentFramework` references.
 * All platform I/O is injected via props (`authenticatedFetch`), exactly as
 * `<EmailComposer/>` does (ADR-028 — no `@spaarke/auth` import here).
 */
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { ICommunicationAssociation, CommunicationSendMode } from '../../services/communicationApi';
import type { EmailComposerBodyFormat } from '../EmailComposer/EmailComposer.types';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Dataverse choice-int constants (mirrors Sprk.Bff.Api Models/CommunicationType.cs
// + Models/BodyFormat.cs — see communicationTimelineApi.ts file header for the
// full enum + wire-format note)
// ---------------------------------------------------------------------------

export const COMMUNICATION_TYPE_EMAIL = 100000000;
export const COMMUNICATION_TYPE_MESSAGE = 100000004;

export const BODY_FORMAT_PLAIN_TEXT = 100000000;
export const BODY_FORMAT_HTML = 100000001;

// ---------------------------------------------------------------------------
// Channel + body format (timeline's own friendly domain shape)
// ---------------------------------------------------------------------------

/** The two channel types the timeline badges. Non-Message types (TeamsMessage/SMS/Notification/null) render as 'email'. */
export type TimelineChannelType = 'email' | 'message';

/** Rendering format for a message body, derived from the Dataverse `sprk_bodyformat` int. */
export type TimelineBodyFormat = 'html' | 'text';

// ---------------------------------------------------------------------------
// Message + thread domain shape
// ---------------------------------------------------------------------------

export interface TimelineAttachment {
  /** `sprk_communicationattachment` id — stable key for the row. */
  id: string;
  /** `sprk_document` GUID, when the attachment resolved to a governed Document. */
  documentId?: string;
  fileName?: string;
  attachmentType?: number;
}

export interface TimelineMessage {
  /** `sprk_communication` id (GUID). */
  id: string;
  channelType: TimelineChannelType;
  /** Raw `sprk_communicationtype` int, kept for callers that need the exact wire value. */
  channelTypeRaw: number | null;
  sender?: string | null;
  /** Display/sort timestamp — `sentAt ?? createdOn`. */
  sentOn?: string | null;
  /** Raw `createdOn` — used as the incremental-poll cursor (see `useThreadPoll`). */
  createdOn?: string | null;
  body?: string | null;
  bodyFormat: TimelineBodyFormat;
  /**
   * `sprk_inreplyto` pointer. For inbound email this is an RFC-2822
   * `Internet-Message-Id` string (rarely equal to another row's `id` GUID —
   * see `CommunicationTimeline.buildTimeline.ts` for how nesting degrades
   * gracefully when no in-thread parent match is found).
   */
  inReplyTo?: string | null;
  /** Access-filter-derived privilege flag (ADR-015 — never gates the read; badge-only signal). */
  privilege: number;
  attachments: TimelineAttachment[];
}

export interface TimelineThread {
  threadId: string;
  messages: TimelineMessage[];
}

// ---------------------------------------------------------------------------
// Unread state
// ---------------------------------------------------------------------------

export interface UnreadState {
  unreadCount: number;
  /** ISO 8601 last-seen watermark sent as `?since=` on the unread-count poll. */
  since?: string;
  /** Local clock time the last-seen watermark was advanced (for UI/debug). */
  lastSeenAt?: string;
}

// ---------------------------------------------------------------------------
// Compose prefill (task 063 quoting seam — keep prop-driven, no re-mount needed)
// ---------------------------------------------------------------------------

export interface CommunicationTimelinePrefill {
  to?: string[];
  subject?: string;
  body?: string;
  bodyFormat?: EmailComposerBodyFormat;
}

// ---------------------------------------------------------------------------
// Public props
// ---------------------------------------------------------------------------

export interface CommunicationTimelineProps {
  /** The thread to render (`sprk_communication` rows sharing this thread anchor). */
  threadId: string;

  // — Auth (injected per shared-lib decoupling rule, ADR-028) —
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded to `communicationTimelineApi` + `sendCommunication()`. */
  bffBaseUrl?: string;

  /** Poll interval in ms. Default 5000 (NFR-07). */
  pollIntervalMs?: number;

  /** Prop-driven compose-box prefill (task 063 injects quoted content here without re-opening the component). */
  prefill?: CommunicationTimelinePrefill;

  /** Recipient directory lookup, forwarded to the compose box's `RecipientField`. */
  onSearchRecipients?: (query: string) => Promise<ILookupItem[]>;

  /** Entity associations to stamp on any message sent from this timeline. */
  associations?: ICommunicationAssociation[];
  /** Send mode for outbound messages. Default `'sharedMailbox'` (matches `sendCommunication` default). */
  sendMode?: CommunicationSendMode;
  /** Archive sent `.eml` to SPE. Default `false` (matches `sendCommunication` default). */
  archiveToSpe?: boolean;

  /** Fired after a successful send. */
  onSendComplete?: (result: { communicationId: string }) => void;
  /** Fired when a poll or send fails (the component also renders an inline error). */
  onError?: (error: Error) => void;

  /** Optional className applied to the root layout container. */
  className?: string;
}
