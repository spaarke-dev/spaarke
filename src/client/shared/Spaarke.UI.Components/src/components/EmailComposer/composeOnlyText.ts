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
