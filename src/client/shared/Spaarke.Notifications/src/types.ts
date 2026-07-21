/**
 * Wire-contract mirrors of the notification-spine's server-side types (task 013
 * `Services/Notifications/Envelopes/*.cs`, task 020 `SignalRDeliveryService.cs`).
 *
 * These are TypeScript MIRRORS, not re-derivations — the discriminator values and
 * JSON property names below are copied verbatim from the shipped C# types so a
 * typo here cannot silently diverge from the server contract. If the server
 * contract changes, this file changes too (single source of truth for the CLIENT
 * side; the server file is the single source of truth for the WIRE format).
 */

// =============================================================================
// The closed `kind` taxonomy (design.md §10 decision #5; mirrors
// `Sprk.Bff.Api.Services.Notifications.Envelopes.NotificationKind` +
// `NotificationKindJsonConverter`'s exact kebab-case wire values).
//
// ACTIVE kinds have a defined envelope shape + a real producer/consumer.
// RESERVED kinds are valid discriminator VALUES with NO envelope shape and NO
// consumer wired anywhere yet — they exist so a later producer can go active
// without colliding with an ad-hoc string of the same intent. This library MUST
// treat both ACTIVE and RESERVED as "known" (compile-time literal union) so an
// RESERVED kind going active later does not require a client library update to
// stop being logged as "unknown" — it will simply have no registered handler
// until a host registers one (see `kindRouter.ts`).
// =============================================================================

/** Kinds with a defined envelope shape + wired producer/consumer (this project). */
export const ACTIVE_NOTIFICATION_KINDS = [
  'suggestion',
  'communication-assessed',
  'communication-arrived',
] as const;

/** Valid discriminator values reserved for future kinds — no envelope shape, no consumer yet. */
export const RESERVED_NOTIFICATION_KINDS = ['job-complete', 'share', 'system-alert'] as const;

/** The full closed taxonomy (active + reserved) this library version recognizes. */
export const ALL_NOTIFICATION_KINDS = [
  ...ACTIVE_NOTIFICATION_KINDS,
  ...RESERVED_NOTIFICATION_KINDS,
] as const;

/** The closed notification `kind` discriminator. Mirrors `NotificationKind` (C#). */
export type NotificationKind = (typeof ALL_NOTIFICATION_KINDS)[number];

/** True when `kind` is a member of the locked taxonomy this library version recognizes. */
export function isKnownNotificationKind(kind: string): kind is NotificationKind {
  return (ALL_NOTIFICATION_KINDS as readonly string[]).includes(kind);
}

// =============================================================================
// Envelope shapes (task 013). IDs + minimal DISPLAY metadata only — never a
// message body, never privileged content beyond the access-gated optional
// `snippet`, never a pre-authorized action token (NFR-02/NFR-03). Field lists
// are verbatim mirrors of `CommunicationEnvelope.cs` / `SuggestionEnvelope.cs`.
// =============================================================================

/** Mirrors `Sprk.Bff.Api.Services.Notifications.Envelopes.CommunicationEnvelope`. */
export interface CommunicationEnvelope {
  kind: 'communication-arrived' | 'communication-assessed';
  /** The `sprk_communication` GUID. */
  communicationId: string;
  /** The `sprk_communicationthread` GUID. */
  threadId: string;
  /** `"email"` | `"message"` | `"sms"`. */
  channel: string;
  /** `"inbound"` | `"outbound"`. */
  direction: string;
  /** ADR-024 regarding record id. */
  regardingRecordId: string;
  /** Sender DISPLAY NAME ONLY — never an address, never content. */
  senderDisplay: string;
  /** OPTIONAL short excerpt — only populated when non-private/non-privileged. */
  snippet?: string;
  /** Unread-badge delta, e.g. +1. */
  badgeDelta: number;
}

/** Mirrors `Sprk.Bff.Api.Services.Notifications.Envelopes.SuggestionEnvelope`. */
export interface SuggestionEnvelope {
  kind: 'suggestion';
  /** The suggestion's identity GUID. */
  suggestionId: string;
  /** Producer origin: `"daily-briefing"` | `"event"` | `"insight"`. */
  source: string;
  /** ADR-024 regarding record id. */
  regardingRecordId: string;
  /** Short display title. */
  title: string;
  /** OPTIONAL short excerpt — access-checked by the producer before inclusion. */
  snippet?: string;
  /** Drives the renderer + the dispatch it re-enters. NEVER a pre-authorized action token. */
  actionHint: string;
  /** ISO 8601 datetime string — when the suggestion expires. */
  expiresAt: string;
}

/** Union of the two currently-shipped envelope shapes. A future kind may add a new member. */
export type NotificationEnvelope = CommunicationEnvelope | SuggestionEnvelope;

// =============================================================================
// The signal-only payload pushed over SignalR (mirrors `NotificationSignal` in
// `SignalRDeliveryService.cs`). Carries ONLY routing info — no envelope, no
// content — the spine is a "changed → fetch" accelerator (NFR-02/03).
// =============================================================================

/** Mirrors `Sprk.Bff.Api.Services.Notifications.NotificationSignal`. */
export interface NotificationSignal {
  outboxRowId: string;
  kind: string;
}

// =============================================================================
// A single item as returned by task 022's pending/poll endpoint (FR-06).
// Mirrors the shipped `Sprk.Bff.Api.Api.Notifications.PendingNotificationItem`
// (see pollFallback.ts header for the confirmation note).
// =============================================================================

/** One pending row from `GET /api/notifications/pending` (task 022, confirmed shape). */
export interface PendingNotificationItem {
  outboxRowId: string;
  kind: string;
  /** The full envelope (task 013 shape) — present because the poll path re-fetches full detail. */
  envelope: NotificationEnvelope | Record<string, unknown>;
  regardingRecordId?: string | null;
  expiresAt?: string | null;
}

// =============================================================================
// The normalized event this library's public `registerHandler` API delivers.
// Live SignalR pushes are signal-only (no `envelope`); poll-fallback results
// carry the full envelope. Handlers must not assume `envelope` is present.
// =============================================================================

export type NotificationEventSource = 'live' | 'poll';

export interface NotificationEvent {
  outboxRowId: string;
  kind: NotificationKind;
  /** Present when the event came from the poll-fallback path; absent for live SignalR pushes (signal-only). */
  envelope?: NotificationEnvelope | Record<string, unknown>;
  /** Which delivery path produced this event. */
  source: NotificationEventSource;
}
