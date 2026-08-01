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

function escapeHtml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * Builds the "Review Summary Memo — {analysis name}" subject line (spec FR-14). Falls back to a generic
 * label when no analysis name is resolvable (e.g. the bound Analysis record was later renamed/removed).
 */
export function buildReviewMemoEmailSubject(response: ReviewMemoReadResponse): string {
  return `Review Summary Memo — ${response.analysisName?.trim() || 'Agreement Review'}`;
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
    <p><strong>Review Summary Memo</strong></p>
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
