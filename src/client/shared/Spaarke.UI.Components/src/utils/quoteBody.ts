/**
 * quoteBody.ts (task 063, FR-13)
 *
 * Pure, channel-agnostic inline-quote formatting helper. `sprk_body` /
 * `sprk_bodyformat` are already channel-agnostic (every `sprk_communication`
 * row — email or message — carries the same two fields), so cross-channel
 * "quote into…" is a **read → format → prefill** operation, NOT an
 * extraction: no `.eml` parsing is needed (that is only required for
 * byte-exact forwarding with original attachments, out of R1 scope per
 * design §6.3).
 *
 * Honors the Dataverse `sprk_bodyformat` choice-int convention established by
 * task 060 / the BFF (`BODY_FORMAT_PLAIN_TEXT` / `BODY_FORMAT_HTML` — see
 * `components/CommunicationTimeline/CommunicationTimeline.types.ts`, mirrors
 * `Sprk.Bff.Api Models/BodyFormat.cs`) rather than the friendly string unions
 * (`'html'|'text'` / `'HTML'|'PlainText'`) each composer surface uses
 * locally — callers convert at the boundary (see `CommunicationTimeline.tsx`
 * `handleQuoteIntoMessage`/`handleQuoteIntoEmail`).
 *
 * No `@azure/communication-*` import (NFR-04) — this is pure string
 * reformatting of an already-persisted body. Any HTML this helper *emits* is
 * sanitized via the same `dompurify` dependency `MessageRow.tsx` uses before
 * `dangerouslySetInnerHTML` — untrusted external HTML (source bodies, most
 * commonly inbound email) never reaches a composer unsanitized.
 */
import DOMPurify from 'dompurify';
import {
  BODY_FORMAT_PLAIN_TEXT,
  BODY_FORMAT_HTML,
} from '../components/CommunicationTimeline/CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** The two Dataverse `sprk_bodyformat` choice-int values `quoteBody` accepts/produces. */
export type QuoteBodyFormat = typeof BODY_FORMAT_PLAIN_TEXT | typeof BODY_FORMAT_HTML;

/** Optional "On {date}, {sender} wrote:" attribution line, prepended ahead of the quoted content. */
export interface QuoteAttribution {
  /** Display name or email address of the original sender. */
  sender?: string;
  /** ISO 8601 timestamp of the original message (`sentOn`/`sentAt`). Invalid/unparseable values are dropped silently. */
  sentOn?: string;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch] as string
  );
}

/**
 * Degrades an HTML body to plain text sanely: block-level boundaries (`<br>`,
 * `</p>`, `</div>`, `</li>`, `</tr>`, `</h1-6>`, `</blockquote>`) become
 * newlines *before* tag-stripping so paragraphs don't run together, then
 * DOMPurify strips all remaining tags (`KEEP_CONTENT: true` retains the
 * decoded text nodes — this also does the HTML-entity decoding for us).
 */
function htmlToPlainText(html: string): string {
  const trimmed = html.trim();
  if (!trimmed) return '';

  const withLineBreaks = trimmed.replace(/<br\s*\/?>/gi, '\n').replace(/<\/(p|div|li|tr|h[1-6]|blockquote)>/gi, '\n');

  const stripped = DOMPurify.sanitize(withLineBreaks, { ALLOWED_TAGS: [], KEEP_CONTENT: true });

  return stripped
    .split('\n')
    .map(line => line.replace(/[ \t]+$/g, ''))
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

/** Escapes plain text into one `<p>` per line (mirrors `EmailComposer.reducer.ts`'s `escapeHtml` usage for forwarded headers). */
function plainTextToHtml(text: string): string {
  const trimmed = text.trim();
  if (!trimmed) return '';
  return trimmed
    .split('\n')
    .map(line => `<p>${escapeHtml(line)}</p>`)
    .join('');
}

function formatAttributionLine(attribution?: QuoteAttribution): string | undefined {
  if (!attribution) return undefined;
  const sender = attribution.sender?.trim();

  let dateLabel: string | undefined;
  if (attribution.sentOn) {
    const parsed = new Date(attribution.sentOn);
    if (!Number.isNaN(parsed.getTime())) dateLabel = parsed.toLocaleString();
  }

  if (sender && dateLabel) return `On ${dateLabel}, ${sender} wrote:`;
  if (sender) return `${sender} wrote:`;
  if (dateLabel) return `On ${dateLabel}:`;
  return undefined;
}

// ---------------------------------------------------------------------------
// quoteBody
// ---------------------------------------------------------------------------

/**
 * Formats `sourceBody` as inline-quoted content honoring `targetFormat` —
 * an HTML `<blockquote>` when the target composer is HTML, `>`-prefixed
 * lines when the target composer is plain text — converting the source
 * representation first when it differs from the target (e.g. an inbound
 * HTML email quoted into a plain-text message body degrades to text before
 * `>`-prefixing).
 *
 * Pure: no I/O, no platform APIs, no ACS/`.eml` dependency (ADR-012, NFR-04).
 *
 * @param sourceBody - The source `sprk_body` value (may be null/undefined/whitespace-only).
 * @param sourceFormat - The source's `sprk_bodyformat` (`BODY_FORMAT_PLAIN_TEXT` / `BODY_FORMAT_HTML`).
 * @param targetFormat - The target composer's body format.
 * @param attribution - Optional sender/date attribution line.
 * @returns Inline-quoted content honoring `targetFormat`. Empty string when there is nothing to quote
 *          (empty/whitespace source AND no attribution).
 */
export function quoteBody(
  sourceBody: string | null | undefined,
  sourceFormat: QuoteBodyFormat,
  targetFormat: QuoteBodyFormat,
  attribution?: QuoteAttribution
): string {
  const raw = (sourceBody ?? '').trim();
  const attributionLine = formatAttributionLine(attribution);

  if (!raw && !attributionLine) return '';

  if (targetFormat === BODY_FORMAT_HTML) {
    const innerHtml = raw
      ? sourceFormat === BODY_FORMAT_HTML
        ? DOMPurify.sanitize(raw, { USE_PROFILES: { html: true } })
        : plainTextToHtml(raw)
      : '';
    const attributionHtml = attributionLine ? `<p>${escapeHtml(attributionLine)}</p>` : '';
    const blockquote = `<blockquote>${innerHtml}</blockquote>`;
    return `${attributionHtml}${blockquote}`;
  }

  // targetFormat === BODY_FORMAT_PLAIN_TEXT
  const plainBody = raw ? (sourceFormat === BODY_FORMAT_HTML ? htmlToPlainText(raw) : raw) : '';
  const quotedLines = plainBody
    ? plainBody
        .split('\n')
        .map(line => (line.length > 0 ? `> ${line}` : '>'))
        .join('\n')
    : '';

  return [attributionLine, quotedLines].filter(part => !!part).join('\n');
}
