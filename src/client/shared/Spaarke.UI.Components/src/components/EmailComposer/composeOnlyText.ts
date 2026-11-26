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

/**
 * Blocks script-executing schemes (`javascript:`/`data:`/`vbscript:`) before an (untrusted) url is rendered into an
 * `<a href>` or written beside text. Everything else (http(s), app-relative, mailto) is allowed. Moved here from
 * `EmailComposer.tsx` (task 197 fix round 1) so the record-link render and the rich-to-plain conversion share one check.
 */
export function isSafeHref(url: string): boolean {
  return !/^\s*(javascript|data|vbscript):/i.test(url);
}

/** The links a plain-text email can carry: the address is written out, so only http(s) and mailto are worth keeping. */
const isPlainTextLink = (href: string): boolean => isSafeHref(href) && /^\s*(https?:|mailto:)/i.test(href);

const LIST_TAGS = new Set(['UL', 'OL']);
const BLOCK_TAGS = new Set(['P', 'DIV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE', 'PRE']);

/** Text of an inline node: a link becomes `text (address)` (or just the address when that is its text), a break a newline. */
function inlineText(node: Node): string {
  if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? '';
  if (node.nodeType !== Node.ELEMENT_NODE) return '';
  const el = node as Element;
  if (el.tagName === 'BR') return '\n';
  const inner = Array.from(el.childNodes).map(inlineText).join('');
  if (el.tagName !== 'A') return inner;
  const href = (el.getAttribute('href') ?? '').trim();
  if (!href || !isPlainTextLink(href)) return inner;
  const shown = inner.trim();
  if (shown === '') return href;
  if (shown === href || shown === href.replace(/^mailto:/i, '')) return inner;
  return `${inner} (${href})`;
}

/** A list as lines: `• ` for a bullet, `1. `, `2. `... for a numbered item; a nested list is indented under its item. */
function listText(list: Element): string {
  const ordered = list.tagName === 'OL';
  const start = Number.parseInt(list.getAttribute('start') ?? '', 10);
  let n = Number.isFinite(start) ? start : 1;
  const lines: string[] = [];
  for (const li of Array.from(list.children).filter(c => c.tagName === 'LI')) {
    let own = '';
    const nested: string[] = [];
    for (const child of Array.from(li.childNodes)) {
      const tag = child.nodeType === Node.ELEMENT_NODE ? (child as Element).tagName : '';
      if (LIST_TAGS.has(tag)) {
        nested.push(listText(child as Element));
      } else if (BLOCK_TAGS.has(tag)) {
        own += `${own === '' ? '' : '\n'}${blockTexts(child).join('\n')}`;
      } else {
        own += inlineText(child);
      }
    }
    own = own.replace(/^\s+|\s+$/g, '');
    // A wrapper item that holds only a nested list is not an item of its own.
    if (own !== '' || nested.length === 0) {
      lines.push(`${ordered ? `${n}. ` : '• '}${own}`);
      n += 1;
    }
    for (const sub of nested) lines.push(...sub.split('\n').map(line => `  ${line}`));
  }
  return lines.join('\n');
}

/** The blocks under a node, each as text: paragraph-like blocks and lists, with loose inline text forming a block of its own. */
function blockTexts(parent: Node): string[] {
  const blocks: string[] = [];
  let run = '';
  const flush = () => {
    const text = run.replace(/\n+$/, '');
    if (text.trim() !== '') blocks.push(text);
    run = '';
  };
  for (const child of Array.from(parent.childNodes)) {
    const tag = child.nodeType === Node.ELEMENT_NODE ? (child as Element).tagName : '';
    if (LIST_TAGS.has(tag)) {
      flush();
      const list = listText(child as Element);
      if (list !== '') blocks.push(list);
    } else if (BLOCK_TAGS.has(tag)) {
      flush();
      blocks.push(...blockTexts(child));
    } else {
      run += inlineText(child);
    }
  }
  flush();
  return blocks;
}

/**
 * Plain text of a rich-text body for a send path that can only carry plain text (the decision wizard's commit route), keeping
 * what the user can see: a link whose text differs from its address becomes `text (address)` (only http(s) and mailto links;
 * anything else keeps its text), a bullet item starts `• `, a numbered item `1. `, `2. `..., and every block boundary
 * (paragraph, list, heading, quote) is one blank line. Formatting such as bold is dropped.
 *
 * Beside `toPlainText`, not instead of it: that one puts a single line break between paragraphs, and its callers (the create
 * wizards' empty-body check, `isUnchangedPlainBody`) and their tests pin that output.
 */
export function richTextToPlainText(html: string): string {
  if (!/[<&]/.test(html)) return html;
  // DOMParser builds an inert document: nothing in the markup runs.
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  return blockTexts(body)
    .join('\n\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
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
