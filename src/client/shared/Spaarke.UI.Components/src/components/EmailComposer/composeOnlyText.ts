/**
 * composeOnlyText.ts - plain text of a compose-only composer's body (spaarke-ontology-platform-r1 task 182).
 *
 * `toPlainText` moved here from `DecisionWizard/steps/EmailMessageFields.tsx` (task 160) so every compose-only host shares
 * one copy: the decision wizard commits its body as plain text, and the create wizards' Send Email step uses it to tell an
 * empty rich-text body ("<p></p>") from a written one. `plainTextToHtml` is its inverse for that step, which opens in rich
 * text (where the composer shows the template picker, AI draft and attach tools) over a plain-text pre-fill.
 *
 * No Xrm, no React (ADR-012).
 */
import type { EmailComposerBodyFormat } from './EmailComposer.types';

/** Plain text of a body the user switched to rich text: block ends become line breaks, tags are dropped. */
export function toPlainText(body: string, format: EmailComposerBodyFormat): string {
  if (format !== 'HTML' || !/[<&]/.test(body)) return body;
  // DOMParser builds an inert document: nothing in the markup runs (an `innerHTML` write on a detached element would run
  // <img onerror>, and the body carries the record name).
  const withBreaks = body.replace(/<\s*br\s*\/?>/gi, '\n').replace(/<\s*\/\s*(p|div|li|h[1-6])\s*>/gi, '\n');
  const text = new DOMParser().parseFromString(withBreaks, 'text/html').body.textContent;
  return text.replace(/\n{3,}/g, '\n\n').trim();
}

const escapeHtml = (text: string): string =>
  text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');

/** Rich-text form of a plain-text body: blank-line-separated blocks become paragraphs, single line breaks become <br>. */
export function plainTextToHtml(text: string): string {
  const normalized = text.replace(/\r\n?/g, '\n');
  if (normalized.trim() === '') return '';
  return normalized
    .split(/\n{2,}/)
    .map(block => `<p>${block.split('\n').map(escapeHtml).join('<br>')}</p>`)
    .join('');
}

/** Text as the rich-text editor shows it: runs of spaces and tabs collapse, line ends are trimmed. */
function visibleText(text: string): string {
  return text
    .replace(/\u00a0/g, ' ')
    .split('\n')
    .map(line => line.replace(/[ \t]+/g, ' ').trim())
    .join('\n')
    .trim();
}

/** Elements a plain paragraph is written with: anything else (bold, a link, a list...) is formatting. */
const PLAIN_ELEMENTS = new Set(['P', 'BR', 'SPAN', 'DIV']);

/** True when a rich-text body carries formatting: an element other than paragraphs, breaks and spans, or a style. */
function hasFormatting(html: string): boolean {
  if (!/</.test(html)) return false;
  // DOMParser builds an inert document: nothing in the markup runs.
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  return Array.from(body.querySelectorAll('*')).some(el => {
    if (!PLAIN_ELEMENTS.has(el.tagName)) return true;
    const style = (el.getAttribute('style') ?? '').replace(/white-space\s*:\s*pre-wrap\s*;?/gi, '').trim();
    return style !== '';
  });
}

/**
 * True when a plain-text body opened as rich-text paragraphs (`plainTextToHtml`) is still that plain text: the rich-text
 * editor re-serialises the paragraphs and collapses spacing, which is not an edit. Its visible text is unchanged and it
 * has no formatting. Formatting added to the same text is an edit.
 */
export function isUnchangedPlainBody(previousPlain: string, nextHtml: string): boolean {
  if (hasFormatting(nextHtml)) return false;
  const asShown = (html: string) => visibleText(toPlainText(html, 'HTML'));
  return asShown(nextHtml) === asShown(plainTextToHtml(previousPlain));
}
