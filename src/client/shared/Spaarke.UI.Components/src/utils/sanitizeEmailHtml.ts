/**
 * sanitizeEmailHtml.ts (email-communication-solution-r5 · task 001 · FR-16 / NFR-03)
 *
 * ONE shared, hardened HTML sanitizer for the field-rendered email-body path
 * (`sprk_body` fallback rendered via `dangerouslySetInnerHTML`). It replaces
 * the permissive `DOMPurify.sanitize(body, { USE_PROFILES: { html: true } })`
 * inlined in `CommunicationTimeline/subcomponents/MessageRow.tsx` and
 * `ConversationView/subcomponents/MessageBubble.tsx`.
 *
 * The `USE_PROFILES:{html:true}` profile allows a broad tag/attr set and does
 * NOT restrict URL schemes or harden links, so a `javascript:` href or a
 * data-exfil link renders unmodified. This util closes that hole with an
 * EXPLICIT CLOSED ALLOW-LIST:
 *   - `ALLOWED_TAGS` / `ALLOWED_ATTR` — only listed tags/attributes survive;
 *     `script`/`iframe`/`object`/`embed`/`form`/`style`/`link`/`meta`/`base`
 *     and every `on*` event-handler attribute are dropped (they are simply not
 *     on the allow-list; `FORBID_TAGS` restates the dangerous tags for
 *     belt-and-suspenders explicitness).
 *   - `ALLOWED_URI_REGEXP` — `href`/`src` restricted to `http`/`https`/`mailto`
 *     schemes; `javascript:`, `data:`, `vbscript:`, `file:`, `tel:` etc. are
 *     neutralized (attribute removed).
 *   - an `afterSanitizeAttributes` hook stamps `rel="noopener noreferrer"` +
 *     `target="_blank"` on EVERY `<a>` so anchors can neither reach back into
 *     the opener nor navigate the host frame.
 *
 * Scope decisions (task 001):
 *   - **`data:` URIs are NOT allowed** (secure default). The field-body path
 *     renders Graph's stripped `uniqueBody`, which does not carry inline
 *     data-URI images; permitting `data:` would also admit `data:text/html`
 *     script vectors. Inline `.eml` images are handled on the server-side
 *     `.eml` render path (task 010) + sandboxed iframe (defense-in-depth), not
 *     here. Revisit only if a reuse of this util for the `.eml` client display
 *     branch requires data-URI images — that is an owner decision (NFR-03).
 *   - **`style` IS allowed** — legitimate email layout relies on inline styles;
 *     DOMPurify sanitizes style-attribute CSS (strips `expression()`,
 *     `url(javascript:)`, etc.). This keeps dark-mode rendering of the
 *     surrounding Fluent component chrome intact (ADR-021) since the util
 *     never touches the host component markup, only the body HTML.
 *
 * React-agnostic (ADR-022): pure TS, no React import, no React-runtime API, no
 * `as React.ComponentType` cast — Layer-1-safe and reusable by any surface
 * (Code Page, PCF under platform React, SPA). Reuses the existing
 * `dompurify ^3.4` dependency (NFR: no new sanitizer dependency).
 *
 * @module sanitizeEmailHtml
 * @see ADR-021 — Fluent v9 design system, dark mode
 * @see ADR-022 — PCF platform libraries (React-version-agnostic Layer-1 logic)
 * @see ADR-012 — shared component library
 */

import DOMPurify from 'dompurify';

// ---------------------------------------------------------------------------
// Allow-list configuration (closed sets)
// ---------------------------------------------------------------------------

/**
 * Permitted tags: block/inline text formatting, headings, lists, tables,
 * anchors, images, quotes, and structural wrappers commonly present in email
 * bodies. Deliberately EXCLUDES `script`, `iframe`, `object`, `embed`, `form`,
 * `style`, `link`, `meta`, `base` (script/navigation/style-injection vectors).
 */
const ALLOWED_TAGS: string[] = [
  // headings + paragraphs + line/section breaks
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'p',
  'br',
  'hr',
  'div',
  'span',
  'section',
  'article',
  'header',
  'footer',
  'address',
  'center',
  'pre',
  // inline text semantics
  'a',
  'b',
  'strong',
  'i',
  'em',
  'u',
  's',
  'strike',
  'del',
  'ins',
  'mark',
  'small',
  'sub',
  'sup',
  'q',
  'cite',
  'abbr',
  'code',
  'kbd',
  'samp',
  'var',
  'font',
  'wbr',
  'bdi',
  'bdo',
  'time',
  // lists
  'ul',
  'ol',
  'li',
  'dl',
  'dt',
  'dd',
  // quotes
  'blockquote',
  // tables
  'table',
  'caption',
  'colgroup',
  'col',
  'thead',
  'tbody',
  'tfoot',
  'tr',
  'th',
  'td',
  // media
  'img',
  'picture',
  'source',
  'figure',
  'figcaption',
];

/**
 * Permitted attributes. NO `on*` event handlers. `href`/`src` values are
 * additionally gated by {@link ALLOWED_URI_REGEXP}. `target`/`rel` are listed
 * so the anchor-hardening hook's values are retained; the hook overwrites them
 * to the safe values regardless of any attacker-supplied value.
 */
const ALLOWED_ATTR: string[] = [
  // links + media (NO `srcset` — comma-separated URL list not scheme-checked by
  // DOMPurify; inline email images use `src`)
  'href',
  'src',
  'alt',
  'title',
  'target',
  'rel',
  // presentation (non-executable)
  'style',
  'class',
  'id',
  'dir',
  'lang',
  'width',
  'height',
  'align',
  'valign',
  'bgcolor',
  'color',
  'face',
  'size',
  'border',
  'cellpadding',
  'cellspacing',
  'span',
  'colspan',
  'rowspan',
];

/**
 * Explicit forbidden tags — redundant with the closed {@link ALLOWED_TAGS}
 * (anything absent is already dropped) but restated for defense-in-depth and
 * to make the security intent auditable at a glance (NFR-03).
 */
const FORBID_TAGS: string[] = ['script', 'iframe', 'object', 'embed', 'form', 'style', 'link', 'meta', 'base'];

/**
 * URL schemes restricted to `http`, `https`, and `mailto`. Anything else
 * (`javascript:`, `data:`, `vbscript:`, `file:`, `tel:`, scheme-relative, …)
 * fails the match and DOMPurify removes the offending `href`/`src`.
 */
const ALLOWED_URI_REGEXP = /^(?:https?|mailto):/i;

const SANITIZE_CONFIG: DOMPurify.Config = {
  ALLOWED_TAGS,
  ALLOWED_ATTR,
  FORBID_TAGS,
  ALLOWED_URI_REGEXP,
  // Drop `data-*` attributes — not needed for field-body rendering.
  ALLOW_DATA_ATTR: false,
  // Always return a string for `dangerouslySetInnerHTML`.
  RETURN_DOM: false,
  RETURN_DOM_FRAGMENT: false,
};

// ---------------------------------------------------------------------------
// Post-sanitize hardening hook
// ---------------------------------------------------------------------------

/** URL-bearing attributes whose scheme we re-verify defensively. */
const URL_ATTRS = ['href', 'src', 'xlink:href'];

/** Detects a leading URI scheme (`scheme:` before any `/`, `?`, `#`). */
const HAS_SCHEME = /^[a-z][a-z0-9+.-]*:/i;

/**
 * Dangerous CSS tokens that must not survive inside an allowed `style`
 * attribute. `expression()`/`-moz-binding` are legacy script vectors;
 * `javascript:`/`vbscript:` cover `url(javascript:…)`; `behavior:` + `@import`
 * pull remote/executable resources. Presence of any → drop the whole `style`.
 */
const DANGEROUS_CSS = /expression\s*\(|javascript\s*:|vbscript\s*:|behavior\s*:|-moz-binding|@import/i;

/**
 * Belt-and-suspenders hardening applied AFTER DOMPurify's own attribute
 * allow-listing (which already strips `on*` handlers and non-http/https/mailto
 * `href`/`src` for non-`data:` schemes). This hook additionally:
 *   1. forces `rel="noopener noreferrer"` + `target="_blank"` on every `<a>`;
 *   2. removes any URL attribute whose scheme is not http/https/mailto —
 *      closing DOMPurify's `DATA_URI_TAGS` exception that would otherwise let a
 *      `data:` URI survive on `<img>` (and friends);
 *   3. drops a `style` attribute carrying dangerous CSS tokens.
 *
 * Registered only for the duration of a single {@link sanitizeEmailHtml} call
 * (add before / remove after) so it never leaks onto other DOMPurify consumers
 * in the process (e.g. `renderMarkdown`, `quoteBody`).
 */
function hardenNode(node: Element): void {
  // 1. Anchors → force safe rel/target.
  if (node.nodeName === 'A') {
    node.setAttribute('rel', 'noopener noreferrer');
    node.setAttribute('target', '_blank');
  }

  // 2. Enforce the URL-scheme allow-list on URL-bearing attributes (closes the
  //    `data:`-on-img gap DOMPurify's DATA_URI_TAGS otherwise permits).
  for (const attr of URL_ATTRS) {
    if (!node.hasAttribute(attr)) continue;
    const value = (node.getAttribute(attr) ?? '').trim();
    if (HAS_SCHEME.test(value) && !ALLOWED_URI_REGEXP.test(value)) {
      node.removeAttribute(attr);
    }
  }

  // 3. Scrub dangerous CSS from an allowed style attribute.
  if (node.hasAttribute('style') && DANGEROUS_CSS.test(node.getAttribute('style') ?? '')) {
    node.removeAttribute('style');
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Sanitizes untrusted email HTML for safe rendering via
 * `dangerouslySetInnerHTML`, using an explicit closed allow-list that forbids
 * scripts/frames/objects/forms, strips every `on*` event handler, restricts
 * URL schemes to `http`/`https`/`mailto`, and forces every anchor to
 * `rel="noopener noreferrer" target="_blank"`.
 *
 * Pure function: input string in, sanitized string out. No React, no side
 * effects beyond the transient DOMPurify hook it registers and removes within
 * the same synchronous call.
 *
 * **Client-side only**: DOMPurify requires a browser DOM (`window`/`document`).
 *
 * @param html - Untrusted email-body HTML (may be empty/null/undefined).
 * @returns Sanitized HTML safe for `dangerouslySetInnerHTML`. Empty string for
 *          empty input.
 *
 * @example
 * ```tsx
 * import { sanitizeEmailHtml } from "@spaarke/ui-components";
 * <div dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(message.body) }} />
 * ```
 */
export function sanitizeEmailHtml(html: string): string {
  if (!html) {
    return '';
  }

  DOMPurify.addHook('afterSanitizeAttributes', hardenNode);
  try {
    return DOMPurify.sanitize(html, SANITIZE_CONFIG) as string;
  } finally {
    // Remove only the hook we just added (last-registered on this entry point),
    // keeping DOMPurify's global hook stack untouched for other consumers.
    DOMPurify.removeHook('afterSanitizeAttributes');
  }
}
