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
export type AuthenticatedFetchFn = (url: string, init?: RequestInit) => Promise<Response>;

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
}
