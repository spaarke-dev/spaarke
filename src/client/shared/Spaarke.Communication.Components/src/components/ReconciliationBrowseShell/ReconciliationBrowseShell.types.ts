/**
 * ReconciliationBrowseShell.types.ts
 *
 * Type contract for `<ReconciliationBrowseShell />` — the Pillar E reconciliation
 * BROWSE shell (email-communication-intelligence-r2 task 053, spec UI model §99 +
 * NFR-11). It steps "N of M" through the Needs-review queue and hosts a two-pane
 * body: a LEFT reader (the reused `EmailReadingPaneShell` in `hideList` mode +
 * the attachment-text-folding `EmailBodyView`) and a RIGHT slot for the three
 * reconcile tabs (Related to = task 052 · Fields = 055 · Tasks = 056/057).
 *
 * Shell chrome: built on the canonical `SprkModal` shell + its `nav` ("N of M")
 * contract from `@spaarke/ui-components` (ADR-050). It is NOT a hand-rolled modal
 * and NOT `RecordNavigationModalShell`. (Deviation from the POML's literal
 * "BrowseModal preset": that preset wraps children in a fixed `1fr + 320px`
 * file-preview stage+metadata grid that cannot host a two-pane reader+tabs body;
 * `SprkModal` + `nav` is the same shell + same browse mechanism the preset is
 * built on, minus the preview grid — owner-approved 2026-08-07, see
 * notes/053-browse-shell-and-reader-complete.md.)
 *
 * React-version note (ADR-022 / NFR-05): `React.FC` + standard hooks only.
 * Fluent v9 semantic tokens (ADR-021) — light + dark via the host FluentProvider.
 */
import type * as React from 'react';
import type { AuthenticatedFetchFn, ReconciliationAttachmentContent } from '../EmailBody/EmailBodyView.types';
import type { EmailToolbarActionHandlers } from '../EmailReadingPaneShell/EmailReadingPaneShell.types';

/**
 * One record the browse shell steps through + renders in the left reader. The
 * host resolves every field (record subject/recipients, the archived `.eml`
 * document id, `sprk_body`, and the normalized attachment text) — the shell is
 * presentational (ADR-012), it does not fetch.
 */
export interface ReconciliationBrowseRecord {
  /** `sprk_communicationid` — stable key + the reader's `selectedId`. */
  id: string;
  /** `sprk_subject` — the modal title + the reader title bar. */
  subject?: string | null;
  /** Sender — reader recipients block. */
  from?: string | null;
  /** To recipients — reader recipients block. */
  to?: string | null;
  /** Cc recipients — hidden when empty. */
  cc?: string | null;
  /** Bcc recipients — hidden when empty. */
  bcc?: string | null;
  /**
   * The resolved `.eml` archive document id (the related document flagged
   * `sprk_isemailarchive`). Present ⇒ the reader renders the server-sanitized
   * `.eml`; absent ⇒ it degrades to `body`. Resolution stays in the host.
   */
  emlDocumentId?: string | null;
  /** `sprk_body` HTML for the degradation path (client-sanitized by the reader). */
  body?: string | null;
  /**
   * Attachment contents, folded into the reader as readable normalized text
   * (NFR-11 — the same surface task 054 anchors citations into).
   */
  attachments?: ReadonlyArray<ReconciliationAttachmentContent>;
}

export interface ReconciliationBrowseShellProps {
  /** Whether the shell is open. */
  open: boolean;
  /** Close callback — wired to the × and ESC/backdrop. */
  onClose: () => void;
  /**
   * The ordered Needs-review queue the shell steps through. Same order as the
   * task-050 reconciliation grid so "N of M" matches the list the reviewer sees.
   */
  queue: ReadonlyArray<ReconciliationBrowseRecord>;
  /**
   * 0-based index of the record to show first — the grid row the reviewer
   * opened (wired through the task-050 `onRecordOpen` override). Defaults to 0.
   * Re-applied whenever the shell (re)opens or a different row is opened.
   */
  initialIndex?: number;
  /** Observability — fired when the shell advances to a new record (does NOT control the index). */
  onIndexChange?: (index: number, record: ReconciliationBrowseRecord) => void;
  /**
   * RIGHT-pane slot — the three reconcile tabs (Related to = task 052 · Fields =
   * 055 · Tasks = 056/057). Invoked with the current record + index. This task
   * ships the slot; the tabs are added by 052/055/056/057.
   */
  renderTabs?: (record: ReconciliationBrowseRecord, index: number) => React.ReactNode;
  /**
   * Optional reader toolbar handlers forwarded to `EmailReadingPaneShell`
   * (Reply / Save to SharePoint / Open full form …). Omitted handlers no-op.
   */
  readerActions?: EmailToolbarActionHandlers;
  /**
   * Fired when an attachment row is activated INSIDE the open-original overlay —
   * the host wires the real file open (e.g. a `preview-url` fetch). The shell
   * owns opening/closing the overlay; this is only the "actually open it" seam.
   */
  onOpenOriginalActivate?: (attachment: ReconciliationAttachmentContent, record: ReconciliationBrowseRecord) => void;
  /** Test/host seam forwarded to `EmailBodyView`'s `eml-render` fetch. */
  authenticatedFetch?: AuthenticatedFetchFn;
  /** `--sprk-ui-scale` forwarded to `SprkModal` + the overlay. */
  uiScale?: number;
}
