/**
 * EmailBodyView.types.ts
 *
 * Type contract for `<EmailBodyView />` — the reading-pane BODY sub-view
 * (email-communication-solution-r5 task 033, spec FR-06/FR-19, NFR-02/NFR-03;
 * design Lens 2/6). It fills the `renderBody(selectedId)` slot of the task-032
 * `EmailReadingPaneShell` (see `EmailReadingPaneShell.types.ts` →
 * `EmailPaneSlotRenderer`) and renders the selected email **as sent**:
 *
 *   1. `.eml` path — when the selected `sprk_communication` has an archived
 *      `.eml` document (the related document flagged `sprk_isemailarchive`),
 *      fetch `GET /api/documents/{emlDocumentId}/eml-render` via `@spaarke/auth`
 *      `authenticatedFetch` and render the returned SERVER-SANITIZED HTML inside
 *      a **sandboxed iframe** (`sandbox=""` — NO `allow-scripts`, NO
 *      `allow-same-origin`; NFR-03 defense-in-depth). Inline `cid:` images arrive
 *      pre-resolved to `data:` URIs server-side.
 *   2. Degradation path — when NO `.eml` archive exists OR the render fetch
 *      fails, render `sprk_body` CLIENT-sanitized via the shared
 *      `sanitizeEmailHtml` (task 001) plus a visible "full history unavailable"
 *      note. An archive-less email is a NORMAL state, not an error.
 *
 * The component does NOT resolve which related document is the archive, and does
 * NOT load the record — the host (the code page / widget that already loads the
 * `sprk_communication` rows + related documents for the card list) resolves the
 * archive doc id + body and supplies them as props. This keeps record/archive
 * resolution in the host (ADR-012 presentational boundary) and avoids adding a
 * second BFF surface (task-033 escalation trigger #1: no new BFF surface beyond
 * `eml-render`).
 *
 * React-version note (ADR-022 / NFR-05): `React.FC` + standard hooks only — no
 * React-18/19-only runtime API and no `as React.ComponentType` cast. Layer-2
 * (React 19 code-page) view; not shared across the PCF boundary.
 */

/**
 * The subset of `@spaarke/auth` `authenticatedFetch` this view depends on. The
 * component imports the real free-function by default; this alias exists so the
 * dependency is named + documented and so tests can `jest.mock('@spaarke/auth')`.
 */
import type { EmailCitation } from '../../logic/citations';

export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

/**
 * One attachment's extracted content, folded into the reader as readable
 * normalized text (email-communication-intelligence-r2 task 053, NFR-11). This
 * is the SAME normalized text the AI-extraction pipeline ran over — so body +
 * attachment text form ONE addressable surface that task 054 anchors proposal
 * citations into. The host resolves + supplies this text (ADR-012 presentational
 * boundary — the component renders it, it does not fetch it), mirroring how
 * `emlDocumentId`/`body` are host-resolved.
 */
export interface ReconciliationAttachmentContent {
  /** `sprk_communicationattachmentid` — stable key. */
  attachmentId: string;
  /** Display name (carries the extension), e.g. `Engagement Letter.pdf`. */
  name: string;
  /**
   * The normalized extracted text. When absent AND `extractable === false`,
   * the reader shows a "content not available as text" note instead of the text
   * (the negative NFR-11 case) — it does NOT crash and open-original stays
   * available. When absent but `extractable` is unset/true, the fold is treated
   * as still-loading/empty and simply renders no text block.
   */
  text?: string | null;
  /**
   * `false` ⇒ this attachment type could not be extracted to readable text
   * (e.g. an image or an unsupported binary). Drives the "not available as text"
   * note. Unset/`true` ⇒ extractable (text is authoritative).
   */
  extractable?: boolean;
  /**
   * `sprk_document` id backing the "Open original" affordance. When present, an
   * "Open original" link renders and fires `onOpenOriginal`. When absent, no
   * open-original link renders for this fold.
   */
  documentId?: string | null;
}

export interface EmailBodyViewProps {
  /**
   * The selected `sprk_communication` id. Supplied by the shell's
   * `renderBody(selectedId)` slot call. Used only for keying/telemetry — the
   * body content itself comes from `emlDocumentId` + `body` (host-resolved).
   */
  selectedId: string;

  /**
   * The resolved `.eml` archive document id — the related document flagged
   * `sprk_isemailarchive = true`. `undefined`/`null`/empty ⇒ no archive exists
   * ⇒ degrade to `sprk_body` (a normal state, NOT an error). Resolution stays
   * in the host (no new BFF surface).
   */
  emlDocumentId?: string | null;

  /**
   * `sprk_body` HTML (Graph's stripped `uniqueBody`) used for the degradation
   * path. Rendered ONLY after client sanitization via `sanitizeEmailHtml`.
   * May be empty — the note still renders.
   */
  body?: string | null;

  /**
   * True when the HOST failed to LOAD the selected record itself (not the eml
   * render). Drives the ERROR state with a retry affordance. An archive-less
   * record is NOT an error — for that, leave this false and omit `emlDocumentId`.
   */
  recordLoadError?: boolean;

  /** Retry affordance invoked from the `recordLoadError` state. */
  onRetryRecord?: () => void;

  /**
   * Optional override for the authenticated fetch used to call `eml-render`.
   * Defaults to `@spaarke/auth` `authenticatedFetch`. Present as a host/test
   * seam; production callers normally omit it.
   */
  authenticatedFetch?: AuthenticatedFetchFn;

  /**
   * Attachment contents folded into the reader as readable normalized text
   * BELOW the body (task 053, NFR-11) — attachments render as TEXT, not chips,
   * so body + attachment text read as one continuous surface. Omitted/empty ⇒
   * no fold (backward-compatible for every existing caller, e.g. `EmailWorkspace`).
   */
  attachments?: ReadonlyArray<ReconciliationAttachmentContent>;

  /**
   * Fired when an attachment fold's "Open original" link is clicked. The host
   * (or the `ReconciliationBrowseShell`) opens the raw `.eml`/file in an overlay
   * preview. Only rendered for folds that carry a `documentId`.
   */
  onOpenOriginal?: (attachment: ReconciliationAttachmentContent) => void;

  /**
   * The proposal citation to anchor into the reader (task 054, NFR-11). When set,
   * the reader resolves it over its normalized text (body + folded attachment
   * text) via `resolveQuotedCitation` and jumps to + highlights the exact passage
   * — in a folded attachment (inline) or, for a body-sourced quote, an ephemeral
   * "cited passage" callout (the `.eml` body renders in a sandboxed iframe the
   * parent cannot reach into, NFR-03). An unlocatable/forged quote surfaces a
   * "source not locatable" affordance and does NOT navigate. Omitted ⇒ no anchor.
   */
  activeCitation?: EmailCitation;
}
