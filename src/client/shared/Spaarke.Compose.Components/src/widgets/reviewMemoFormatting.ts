/**
 * reviewMemoFormatting.ts — FR-14 (ai-advanced-capabilities-agreements-r1 task 051) shared types +
 * pure formatting for the "Create Summary Memo" toolbar control's "Email memo" action.
 *
 * Mirrors the server's read-path contract exactly (`GET /api/ai/chat/sessions/{sessionId}/review-memo`,
 * `Sprk.Bff.Api.Api.Ai.ReviewMemoReadResponse` / `Sprk.Bff.Api.Services.Ai.ReviewMemo.ReviewMemoDocument`)
 * — mirror-first, not re-derived. This file owns ONLY presentation formatting (an HTML email body from
 * the already-persisted record); it never re-derives or fabricates memo CONTENT (render-from-persisted,
 * the project's binding FR-14 constraint — both the .docx download and the email body must derive from
 * the SAME server-persisted record so exports ≡ the durable artifact).
 *
 * Sibling of `../composeResultFormat.ts` / `advisoryNoteFormatting.ts` — small, pure, no-React formatting
 * modules kept next to the widgets that consume them (§11 reuse: no new "formatting library" surface).
 */

/** One assembled memo row — mirrors `Sprk.Bff.Api.Services.Ai.ReviewMemo.ReviewMemoSection` field-for-field. */
export interface ReviewMemoSection {
  location: string;
  before: string;
  after: string;
  why: string;
  flaggedClause: string;
  standardRef: string;
  riskLevel?: string | null;
}

/** The persisted memo document — mirrors `ReviewMemoDocument` field-for-field. */
export interface ReviewMemoDocument {
  schemaVersion: string;
  overallRisk: string;
  sectionCount: number;
  sections: readonly ReviewMemoSection[];
}

/** Response body of `GET /api/ai/chat/sessions/{sessionId}/review-memo` — mirrors `ReviewMemoReadResponse`. */
export interface ReviewMemoReadResponse {
  analysisId: string;
  analysisName?: string | null;
  documentName?: string | null;
  memo: ReviewMemoDocument;
}

/**
 * The `code` extension values the memo endpoints stamp on their ProblemDetails responses
 * (mirrors `Sprk.Bff.Api.Api.Ai.ReviewMemoEndpoints`) — the machine signal the toolbar uses to
 * pick an honest, actionable banner instead of a dead-end "Failed (400)" message.
 */
export type MemoProblemCode = 'session-not-bound' | 'no-completed-review' | 'no-memo';

/** Banner shown when the session's Analysis has no persisted Review Summary yet (404 / `no-memo`). */
export const MEMO_NO_MEMO_MESSAGE = 'Generate the Review Summary first — none has been created for this review yet.';

/**
 * Banner for the WRITE path's own negative (400 / `no-completed-review`): the user asked to generate a
 * Review Summary with no flagged findings in hand.
 *
 * R8 §GAPS-5 Phase 3. `MemoProblemCode` has always DECLARED this code and `selectMemoNegativeMessage`
 * never handled it — harmless while nothing POSTed, and reachable the moment something does. A summary
 * of nothing is itself the defect, so this refuses rather than persisting an empty artifact (the same
 * rule R8 item 8 applies to the change summary).
 */
export const MEMO_NO_FINDINGS_MESSAGE =
  'There are no review findings to summarise yet. Run a review on this document first.';

/** Confirmation after a successful generate — names the count so "it worked" is verifiable, not implied. */
export function buildReviewSummaryGeneratedMessage(sectionCount: number): string {
  const noun = sectionCount === 1 ? 'finding' : 'findings';
  return `Review Summary created (${sectionCount} ${noun}). Use Download (.docx) or Email to share it.`;
}

/** One section on the WRITE payload — mirrors `Sprk.Bff.Api.Services.Ai.ReviewMemo.ReviewMemoSectionInput`. */
export interface GenerateReviewSummarySectionInput {
  sectionRef?: string;
  quotedText: string;
  afterText?: string;
  assessment?: string;
  standardRef?: string;
  flaggedClause?: string;
  riskLevel?: string;
}

/** The WRITE payload — mirrors `Sprk.Bff.Api.Services.Ai.ReviewMemo.GenerateReviewMemoRequest`. */
export interface GenerateReviewSummaryRequest {
  overallRisk: string;
  sections: GenerateReviewSummarySectionInput[];
}

/** Risk band used when neither the Action nor the findings supply one — never invented as a severity. */
export const UNSPECIFIED_OVERALL_RISK = 'Unspecified';

/**
 * Builds the `POST .../review-memo` payload from the panel's live findings (R8 §GAPS-5 Phase 3 — the
 * write half that had no caller, so both READ actions always hit the "generate first" banner).
 *
 * Mapping notes, each of which is a decision rather than a mechanical copy:
 *
 * - **`quotedText` is the only hard requirement.** Findings without one are DROPPED — they carry no
 *   document span, so there is nothing for the memo's before/after columns to be about. The count of
 *   dropped rows is returned rather than swallowed (see {@link BuiltReviewSummaryRequest.droppedCount}),
 *   because silently shipping fewer findings than the panel shows is the failure this project exists to
 *   stop. In practice this drops nothing: the projections upstream already require `quotedText`.
 * - **The four grounding fields pass through as-is, `undefined` included** (decision D3). The server
 *   relaxed them from `required` and renders an em dash for each absent one. Sending a partially
 *   grounded finding with a visible gap beats excluding it.
 * - **No `afterText`** (decision D2). Per-finding accept/reject is not tracked durably anywhere, and the
 *   server assembler already treats its absence as "the original text stands" — which is correct for
 *   both a rejected suggestion and an untouched clause. Adding a guessed value would be worse than
 *   omitting it.
 * - **`overallRisk` prefers the server-asserted value**, falling back to the caller-derived band and
 *   finally to {@link UNSPECIFIED_OVERALL_RISK}. The field is `required` server-side, so it cannot be
 *   omitted; inventing a severity would be worse than naming the absence.
 */
export function buildGenerateReviewSummaryRequest(
  findings: readonly {
    sectionRef?: string;
    quotedText: string;
    riskLevel?: string;
    standardRef?: string;
    flaggedClause?: string;
    assessment?: string;
  }[],
  overallRisk?: string,
  derivedOverallRisk?: string
): BuiltReviewSummaryRequest {
  const sections: GenerateReviewSummarySectionInput[] = [];
  let droppedCount = 0;
  for (const finding of findings) {
    if (!finding.quotedText || finding.quotedText.trim().length === 0) {
      droppedCount += 1;
      continue;
    }
    sections.push({
      sectionRef: finding.sectionRef,
      quotedText: finding.quotedText,
      assessment: finding.assessment,
      standardRef: finding.standardRef,
      flaggedClause: finding.flaggedClause,
      riskLevel: finding.riskLevel,
    });
  }
  return {
    request: {
      overallRisk: overallRisk?.trim() || derivedOverallRisk?.trim() || UNSPECIFIED_OVERALL_RISK,
      sections,
    },
    droppedCount,
  };
}

/** The built payload plus what it had to leave behind — the caller MUST surface a non-zero drop count. */
export interface BuiltReviewSummaryRequest {
  request: GenerateReviewSummaryRequest;
  /** Findings excluded for having no `quotedText`. Non-zero MUST be reported, never silently absorbed. */
  droppedCount: number;
}

/**
 * Banner shown when the Compose session is NOT bound to an Analysis (400 / `session-not-bound`) —
 * the direct-Compose door. This is DISTINCT from "no completed review": a review may well have
 * completed; the memo simply has nowhere durable to be saved until the document is saved.
 *
 * UAT (2026-08-18, owner): the Document + Analysis are created when the user **Saves** — NOT when a
 * file is uploaded or a review runs (the user may not want to persist it). So the guidance is simply
 * "Save the document" (which creates its Analysis); the memo is available afterward. The Assistant
 * History / "Promote to Analysis…" path is deliberately REMOVED from this flow — the user does not go
 * to conversation History to link an Analysis.
 */
export const MEMO_SESSION_NOT_BOUND_MESSAGE =
  'This document isn’t saved yet, so there’s nowhere to save the Review Summary. ' +
  'Save the document first — that creates its Analysis — then generate the Review Summary again. ' +
  'Any completed review is preserved.';

/**
 * Selects the honest banner message for a non-OK memo response from its HTTP status + ProblemDetails
 * `code`, SPLITTING the two formerly-conflated negatives (agreements-r1 UAT round-1 #2): "no memo
 * persisted yet" (generate first) vs. "session not bound to an Analysis" (promote first — the review
 * is not lost). Returns `null` for any other/unknown non-OK response so the caller falls through to a
 * generic error (never masks a real transport/server failure as one of these guided states).
 */
export function selectMemoNegativeMessage(status: number, code?: string | null): string | null {
  if (status === 404 || code === 'no-memo') return MEMO_NO_MEMO_MESSAGE;
  if (status === 400 && code === 'session-not-bound') return MEMO_SESSION_NOT_BOUND_MESSAGE;
  // R8 §GAPS-5 Phase 3 — the WRITE path's negative. Declared in `MemoProblemCode` since task 051 but
  // unhandled until a caller existed to reach it; without this arm the server's guided 400 degrades to
  // the generic "Failed (400)" this function exists to prevent.
  if (status === 400 && code === 'no-completed-review') return MEMO_NO_FINDINGS_MESSAGE;
  return null;
}

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * Builds the "Review Summary — {analysis name}" subject line (spec FR-14). Falls back to a generic
 * label when no analysis name is resolvable (e.g. the bound Analysis record was later renamed/removed).
 *
 * R8 §GAPS-5 Phase 4 (2026-09-07): "Memo" dropped from the user-facing name — it collided with
 * `sprk_memo`, the Notepad entity. The function name keeps `ReviewMemo` (code identifier, cosmetic).
 */
export function buildReviewMemoEmailSubject(response: ReviewMemoReadResponse): string {
  return `Review Summary — ${response.analysisName?.trim() || 'Agreement Review'}`;
}

/**
 * Deterministic HTML email body for the "Email memo" toolbar action — mirrors the server's
 * `ReviewMemoDocumentBuilder` .docx layout: title, doc/analysis metadata, then one table row per
 * section with {location, before, after, why, golden-ref}. Pure presentation formatting (no AI, no
 * domain logic) — the memo CONTENT itself is entirely the server-persisted record.
 *
 * Inline styles (not Fluent v9 tokens) are deliberate here: this HTML is handed to `<EmailComposer />`
 * as the message body, which downstream travels through Graph `sendMail` to arbitrary external email
 * clients — those do not resolve CSS custom properties, so literal inline styles are the correct,
 * standard approach for email HTML (unlike in-app UI, which ADR-021 governs).
 */
export function buildReviewMemoEmailBody(response: ReviewMemoReadResponse): string {
  const { memo, analysisName, documentName } = response;

  const metaParts: string[] = [];
  if (documentName) metaParts.push(`Document: ${escapeHtml(documentName)}`);
  if (analysisName) metaParts.push(`Analysis: ${escapeHtml(analysisName)}`);
  metaParts.push(`Overall Risk: ${escapeHtml(memo.overallRisk)}`);
  metaParts.push(`Sections: ${String(memo.sectionCount)}`);

  const cellStyle = 'padding:6px;border:1px solid #ccc;text-align:left;vertical-align:top;';
  const headerCellStyle = `${cellStyle}font-weight:bold;`;

  const rows =
    memo.sections.length === 0
      ? `<tr><td colspan="5" style="${cellStyle}">No flagged sections.</td></tr>`
      : memo.sections
          .map(
            section => `
      <tr>
        <td style="${cellStyle}">${escapeHtml(section.location)}</td>
        <td style="${cellStyle}">${escapeHtml(section.before)}</td>
        <td style="${cellStyle}">${escapeHtml(section.after)}</td>
        <td style="${cellStyle}">${escapeHtml(section.why)}</td>
        <td style="${cellStyle}">${escapeHtml(section.standardRef)}</td>
      </tr>`
          )
          .join('');

  return `
    <p><strong>Review Summary</strong></p>
    <p>${metaParts.join(' &middot; ')}</p>
    <table style="border-collapse:collapse;width:100%;">
      <thead>
        <tr>
          <th style="${headerCellStyle}">Location</th>
          <th style="${headerCellStyle}">Before</th>
          <th style="${headerCellStyle}">After</th>
          <th style="${headerCellStyle}">Why</th>
          <th style="${headerCellStyle}">Golden Ref</th>
        </tr>
      </thead>
      <tbody>${rows}</tbody>
    </table>
  `.trim();
}
