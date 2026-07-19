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
// Regarding mode (R2 task 020, FR-03) — a record's threads grouped for
// collapsible-group rendering. Built by `mapRegardingReadResultToGroups`
// (`CommunicationTimeline.buildTimeline.ts`) from the `by-regarding` DTO
// (`IRegardingReadResultDto` — mirrors the BFF `RegardingReadResult`).
// ---------------------------------------------------------------------------

/**
 * One thread's collapsible-group projection for regarding mode. `messages` is
 * the SAME `TimelineMessage[]` domain shape the thread-id mode already uses —
 * a group's expanded body reuses `buildTimeline`/`MessageRow`/`ChannelBadge`
 * unchanged (no forked rendering, per FR-03 / ADR-012).
 */
export interface RegardingThreadGroup {
  /** `sprk_communicationthread` id (GUID). */
  threadId: string;
  /**
   * `sprk_name`. The BFF's by-regarding read now projects this (task 020
   * closed a gap in the task-010 DTO, which fetched the name but dropped it
   * before returning — see `communicationTimelineApi.ts` `IThreadReadResultDto`
   * header note). Falls back to a placeholder when the server value is
   * null/blank (e.g. a not-yet-named thread) rather than rendering an empty
   * group header.
   */
  name: string;
  messageCount: number;
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
// Quote into email (task 063 — cross-channel, message row -> email composer)
// ---------------------------------------------------------------------------

/**
 * Payload for `CommunicationTimelineProps.onQuoteIntoEmail`. Field names
 * deliberately mirror `<EmailComposer/>`'s own compose-mode pre-fill props
 * (`initialTo`/`initialSubject`/`initialBody`/`initialBodyFormat` —
 * `EmailComposer.types.ts` `IEmailComposerProps`) so the host can spread this
 * payload directly onto a compose-mode `<EmailComposer/>` instance it owns
 * (e.g. inside a dialog) — no second prefill mechanism (§11).
 */
export interface QuoteIntoEmailPayload {
  initialTo?: string[];
  initialSubject?: string;
  initialBody: string;
  initialBodyFormat: EmailComposerBodyFormat;
}

// ---------------------------------------------------------------------------
// Public props
// ---------------------------------------------------------------------------

/** Props shared by both render modes (R2 task 020 introduces the discriminated `mode`). */
export interface CommunicationTimelineBaseProps {
  // — Auth (injected per shared-lib decoupling rule, ADR-028) —
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded to `communicationTimelineApi` + `sendCommunication()`. */
  bffBaseUrl?: string;

  /** Poll interval in ms. Default 5000 (NFR-07, both modes). */
  pollIntervalMs?: number;

  /** Fired when a poll or send fails (the component also renders an inline error). */
  onError?: (error: Error) => void;

  /** Optional className applied to the root layout container. */
  className?: string;
}

/**
 * R1 thread-id mode (task 060) — renders ONE thread's interleaved
 * email+chat timeline + compose/send box. `mode` is optional and defaults to
 * `'thread'` so every existing call site (which never set `mode`) keeps
 * compiling and behaving unchanged.
 */
export interface CommunicationTimelineThreadModeProps extends CommunicationTimelineBaseProps {
  mode?: 'thread';

  /** The thread to render (`sprk_communication` rows sharing this thread anchor). */
  threadId: string;

  /** Prop-driven compose-box prefill (task 063 injects quoted content here without re-opening the component). */
  prefill?: CommunicationTimelinePrefill;

  /**
   * Fired when the user clicks "Quote into email" on a message row (task 063,
   * FR-13). The component reads the row's `sprk_body`/`sprk_bodyformat`,
   * formats it as an inline quote via `quoteBody()`, and hands the host a
   * payload shaped exactly like `<EmailComposer/>`'s own `initial*` pre-fill
   * props — the host owns mounting `<EmailComposer/>` (dialog/page), this
   * component never imports it for that purpose.
   */
  onQuoteIntoEmail?: (payload: QuoteIntoEmailPayload) => void;

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
}

/**
 * Regarding mode (R2 task 020, FR-03) — renders ALL of a regarding record's
 * threads as collapsible groups (name + message count), each expanding to
 * that thread's R1 interleaved timeline. Read-only: no compose box (composing
 * happens inside a specific thread — thread-id mode / the task 021 PCF — this
 * grouped view's job is discovery/organization across threads, not sending;
 * §11 — one compose surface, not a second one bolted onto the grouped view).
 * Calls the `by-regarding` endpoint (task 010) via the injected
 * `authenticatedFetch`; performs NO client-side access/privacy filtering
 * (NFR-03) — it renders exactly the access-filtered set the endpoint returns.
 */
export interface CommunicationTimelineRegardingModeProps extends CommunicationTimelineBaseProps {
  mode: 'regarding';

  /** The regarding record's logical entity name (e.g. `sprk_matter`, `contact`) — one of the 11 ADR-024 families. */
  entityType: string;
  /** The regarding record's id. */
  id: string;

  /**
   * Thread ids expanded by default. Default: none (all groups start
   * collapsed) — a record can carry many threads; showing every thread's
   * full timeline on first render would be noisy and expensive to lay out.
   * Design decision recorded in task 020 notes.
   */
  defaultExpandedThreadIds?: string[];
}

/**
 * Discriminated on `mode`. Thread-id mode (`mode` omitted or `'thread'`) is
 * the unchanged R1 surface; regarding mode (`mode: 'regarding'`) is the R2
 * FR-03 addition. Existing call sites that never set `mode` keep matching the
 * thread-id variant unchanged.
 */
export type CommunicationTimelineProps = CommunicationTimelineThreadModeProps | CommunicationTimelineRegardingModeProps;
