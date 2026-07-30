/**
 * EmailCardList.types.ts
 *
 * Type contract for `<EmailCardList />` (email-communication-solution-r5 task
 * 030, FR-03/FR-19). This is a pure presentational list: the host (task 031
 * view wiring → task 032 reading-pane shell) fetches `sprk_communication` rows,
 * filters to Email at query time via the saved-view FetchXML, and supplies the
 * already-loaded rows as `items` plus `isLoading`/`selectedId` state. The
 * component itself NEVER calls `Xrm.WebApi`, BFF, or `IDataverseClient`
 * (ADR-012 presentational-only boundary).
 */

/**
 * `sprk_communicationtype` option-set value for Email. The ONLY value
 * `<EmailCardList />` will render as a card — any other value (Teams, SMS,
 * ACS, etc.) is skipped even if present in `items` (FR-03 non-Email exclusion
 * invariant; defense-in-depth alongside the host's query-time filter).
 */
export const EMAIL_COMMUNICATION_TYPE = 100000000;

/**
 * One row from `sprk_communication`, shaped for card rendering. The host owns
 * mapping from the raw Dataverse/Graph payload to this shape (e.g. `preview`
 * is Graph's plain-text `bodyPreview` — if a caller ever derives `preview`
 * from HTML instead, it MUST route through the shared `sanitizeEmailHtml`
 * before reaching this component; `<EmailCardList />` renders `preview` as
 * plain text only and never via `dangerouslySetInnerHTML`).
 */
export interface EmailCardItem {
  /** `sprk_communicationid` (or equivalent stable row id). */
  id: string;
  /** Sender display name/address. */
  from: string;
  /** `sprk_subject` (or equivalent). */
  subject: string;
  /** Plain-text body preview (Graph `bodyPreview`) — never raw HTML. */
  preview: string;
  /** `sprk_senton`/`createdon` (or equivalent) — an ISO string or a `Date`. */
  date: string | Date;
  /** Read/unread signal driving the bold + dot unread visual. */
  isUnread: boolean;
  /**
   * Association review state for the left-of-sender status dot:
   * 🔴 requires review · 🟡 needs confirmation · 🟢 confirmed. `undefined` when
   * the row carries no association data (no dot rendered).
   */
  reviewTone?: 'red' | 'yellow' | 'green';
  /**
   * `sprk_communicationtype` option-set value. `<EmailCardList />` renders a
   * card ONLY when this equals `EMAIL_COMMUNICATION_TYPE` — any other value is
   * skipped (FR-03 invariant), regardless of whether the host pre-filtered.
   */
  communicationType: number;
}

export interface EmailCardListProps {
  /** Host-supplied rows; NOT guaranteed to be pre-filtered to Email-only (the component defends the invariant itself). */
  items: ReadonlyArray<EmailCardItem>;
  /** The currently active/open card's id (host owns selection state). */
  selectedId?: string;
  /** When true, renders `skeletonCount` skeleton cards instead of `items`. */
  isLoading?: boolean;
  /** Number of skeleton cards to render while loading. Defaults to 6. */
  skeletonCount?: number;
  /** Fired when a card is clicked or keyboard-activated (Enter/Space). */
  onSelect: (id: string) => void;
}
