/**
 * assistantDraftHtml — DEF-08 Part B helper.
 *
 * Converts an assistant message's plain/markdown-ish text into the minimal semantic HTML the
 * Compose editor seeds from (via `ComposeLaunchContext.draft.html` → ComposeEditor `initialHtml`).
 * This is the MANUAL fallback path ("Open in Compose" per-message affordance) — the primary Part A
 * path carries the LLM-authored `body_html` verbatim from the `compose-draft-document` ledger
 * output. This helper is deliberately conservative: it does NOT attempt full markdown rendering
 * (no external dependency); it splits paragraphs on blank lines and preserves single line breaks,
 * HTML-escaping the text so the drafted content can never inject markup into the editor.
 */

/** HTML-escape a raw text run so it renders as literal text in the editor (never markup). */
function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

/**
 * Convert assistant message text into paragraph-wrapped semantic HTML for a Compose draft seed.
 * Blank lines separate paragraphs; single newlines become `<br>`. Returns an empty paragraph for
 * empty/whitespace input so the editor always mounts renderable content.
 */
export function assistantTextToDraftHtml(text: string): string {
  const normalized = (text ?? '').replace(/\r\n/g, '\n').replace(/\r/g, '\n');
  const paragraphs = normalized
    .split(/\n{2,}/)
    .map((p) => p.trim())
    .filter((p) => p.length > 0);
  if (paragraphs.length === 0) {
    return '<p></p>';
  }
  return paragraphs
    .map((p) => `<p>${escapeHtml(p).replace(/\n/g, '<br>')}</p>`)
    .join('');
}
