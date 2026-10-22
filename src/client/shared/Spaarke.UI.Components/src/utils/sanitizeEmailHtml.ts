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
 *   - **`style` IS allowed, but filtered declaration by declaration** —
 *     legitimate email layout relies on inline styles. DOMPurify does NOT
 *     inspect style-attribute CSS, so {@link sanitizeInlineStyle} does
 *     (ISS-003 / #1636): it drops declarations that can paint outside the
 *     message box (`position` other than static/relative, `top`/`left`/…/
 *     `inset`, `z-index`, `transform`/`translate`/`scale`/`zoom`/`offset`,
 *     viewport units, negative margins) or fetch a resource (`url()`,
 *     `image-set()`, …), plus anything it cannot parse unambiguously. A
 *     fixed-position "sign in again" overlay or a CSS tracking pixel in an
 *     inbound email is therefore inert for EVERY consumer, not only those that
 *     contain the body in a clipped box. This keeps dark-mode rendering of the
 *     surrounding Fluent component chrome intact (ADR-021) since the util
 *     never touches the host component markup, only the body HTML.
 *   - **Remote images are NOT loaded by default** (ISS-003 / #1636). An
 *     `<img src="https://…">` tells the sender the reader's IP address and read
 *     time. The `src` of every `<img>` is dropped (the element, `alt`, `width`
 *     and `height` stay), so a tracking pixel is inert. A surface that shows
 *     images on an explicit user action passes `{ allowRemoteImages: true }`
 *     for that render; nothing does today. `srcset`, `background=` and
 *     `<link>`/`<style>` are not on the allow-list, so they never load either.
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

// ---------------------------------------------------------------------------
// Inline-style filter (ISS-003 / #1636)
// ---------------------------------------------------------------------------

/** A declaration's property name, once lower-cased and vendor-prefix-stripped, must look like this or the declaration is dropped. */
const SAFE_PROPERTY = /^[a-z][a-z0-9-]*$/;

/**
 * Properties that move a box, or its paint, outside its own place in the flow: offsets and inset (only meaningful on a
 * positioned box, dropped anyway), stacking order, transforms and the individual transform properties, `zoom`, motion
 * paths (`offset-*`) and anchor positioning (`position-*`). `position` itself is handled by {@link SAFE_POSITION}.
 */
const PLACEMENT_PROPERTY =
  /^(?:top|right|bottom|left|inset(?:-[a-z-]+)?|z-index|transform(?:-[a-z-]+)?|translate|rotate|scale|perspective|zoom|offset(?:-[a-z-]+)?|position-[a-z-]+)$/;

/** The only `position` values that cannot escape the box. */
const SAFE_POSITION = /^(?:static|relative)$/;

/** Any `margin*` property (negative margins drag a box over its neighbours) and `text-indent` (a negative indent pushes text out of its box). */
const MARGIN_PROPERTY = /^(?:margin(?:-[a-z-]+)?|text-indent)$/;

/** A margin value is kept only if it is 1-4 plain non-negative lengths / `auto` (so no negative number, `calc()`, `var()`). */
const SAFE_MARGIN_VALUE =
  /^(?:auto|(?:\d+(?:\.\d+)?|\.\d+)(?:px|em|rem|%|pt|ex|ch|cm|mm|in|pc|q)?)(?:\s+(?:auto|(?:\d+(?:\.\d+)?|\.\d+)(?:px|em|rem|%|pt|ex|ch|cm|mm|in|pc|q)?)){0,3}$/i;

/** A number followed by a viewport unit (`100vw`, `50dvh`, `10vmin`, …): sizes a box to the host window, the shape of an overlay. */
const VIEWPORT_UNIT = /\d\s*[dsl]?v(?:w|h|i|b|min|max)(?![\w-])/i;

/**
 * Values that fetch a resource: `url()` (any scheme, so `https:`, `//host`, `data:`), the string forms `image-set()` /
 * `src()` / `image()`, `cross-fade()` and `element()`. Fetching on open tells the sender the reader's IP and read time.
 */
const EXTERNAL_RESOURCE_CSS = /url\s*\(|image-set\s*\(|cross-fade\s*\(|element\s*\(|\bsrc\s*\(|\bimage\s*\(/i;

/** `var()` / `env()` make a value unknowable here (`margin-top:var(--m)` with `--m:-5000px`), so such declarations are dropped. */
const INDIRECT_CSS = /\b(?:var|env)\s*\(/i;

/**
 * Returns the safe subset of an inline `style` attribute, or `''` when nothing safe is left.
 *
 * The attribute is split on EVERY `;` (quotes, parentheses and comments are deliberately ignored while splitting). Any
 * real declaration in the browser's view is therefore one of these pieces, and a piece that only looks like a
 * declaration because of how a quote or comment was opened is judged on its own text, so a hidden
 * `font-family:"/*";position:fixed;"*\/"` cannot smuggle `position:fixed` past the filter. A piece is kept only if ALL of:
 *   - it has a `property: value` shape with a plain ASCII property name (no CSS escapes, no `*hack`, no `--custom`);
 *   - it contains no backslash (CSS escapes can spell `position` / `url(` in ways a regex cannot see) and no comment
 *     delimiter;
 *   - its property is not a placement property, its `position` (if any) is static/relative, its `margin*` is a plain
 *     non-negative length, and its value has no viewport unit, no resource-fetching function and no `var()`/`env()`.
 * Kept pieces are returned verbatim (trimmed) and re-joined with `;`.
 *
 * Not covered, by design (a host that must contain a hostile body still clips it with `contain: paint` /
 * `overflow: hidden`, as the external SPA does): paint that spills out of an in-flow box without moving it, such as a
 * huge `box-shadow` spread, `outline-offset`, or a very large `font-size` / `width` in plain pixels. Also `class` / `id`
 * are allow-listed, so a body can still pick up a host stylesheet rule that happens to share a class name.
 */
function sanitizeInlineStyle(style: string): string {
  const kept: string[] = [];

  for (const piece of style.split(';')) {
    const decl = piece.trim();
    if (!decl) continue;
    if (decl.includes('\\') || decl.includes('/*') || decl.includes('*/')) continue;

    const colon = decl.indexOf(':');
    if (colon <= 0) continue;

    // `-webkit-transform` and `transform` are judged as the same property.
    const property = decl
      .slice(0, colon)
      .trim()
      .toLowerCase()
      .replace(/^-[a-z]+-/, '');
    const value = decl
      .slice(colon + 1)
      .replace(/\s*!\s*important\s*$/i, '')
      .trim();

    if (!SAFE_PROPERTY.test(property)) continue;
    if (PLACEMENT_PROPERTY.test(property)) continue;
    if (property === 'position' && !SAFE_POSITION.test(value.toLowerCase())) continue;
    if (MARGIN_PROPERTY.test(property) && !SAFE_MARGIN_VALUE.test(value)) continue;
    if (VIEWPORT_UNIT.test(value) || EXTERNAL_RESOURCE_CSS.test(value) || INDIRECT_CSS.test(value)) continue;

    kept.push(decl);
  }

  return kept.join(';');
}

/**
 * Belt-and-suspenders hardening applied AFTER DOMPurify's own attribute
 * allow-listing (which already strips `on*` handlers and non-http/https/mailto
 * `href`/`src` for non-`data:` schemes). This hook additionally:
 *   1. forces `rel="noopener noreferrer"` + `target="_blank"` on every `<a>`;
 *   2. removes any URL attribute whose scheme is not http/https/mailto —
 *      closing DOMPurify's `DATA_URI_TAGS` exception that would otherwise let a
 *      `data:` URI survive on `<img>` (and friends);
 *   3. drops the `src` of every `<img>` / `<source>` unless `allowRemoteImages`
 *      (ISS-003: a remote image is a read receipt; `alt`/`width`/`height` stay);
 *   4. drops a `style` attribute carrying dangerous script tokens, and filters
 *      the rest with {@link sanitizeInlineStyle} (overlay positioning, CSS
 *      resource fetches).
 *
 * Registered only for the duration of a single {@link sanitizeEmailHtml} call
 * (add before / remove after) so it never leaks onto other DOMPurify consumers
 * in the process (e.g. `renderMarkdown`, `quoteBody`).
 */
function hardenNode(node: Element, allowRemoteImages: boolean): void {
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

  // 3. No image is fetched on open unless the caller opted in. Every `src` that survives step 2 is http(s) (or a
  //    scheme-less value DOMPurify already dropped), i.e. remote; `alt`, `width` and `height` are kept so the layout
  //    holds and a 1x1 tracking pixel stays invisible. `srcset` / `background=` are not allow-listed at all.
  if (!allowRemoteImages && (node.nodeName === 'IMG' || node.nodeName === 'SOURCE')) {
    node.removeAttribute('src');
  }

  // 4. Scrub dangerous CSS from an allowed style attribute: the legacy script tokens drop the whole attribute; the
  //    rest is filtered declaration by declaration (overlay positioning, CSS resource fetches).
  const style = node.getAttribute('style');
  if (style !== null) {
    const safeStyle = DANGEROUS_CSS.test(style) ? '' : sanitizeInlineStyle(style);
    if (safeStyle) {
      node.setAttribute('style', safeStyle);
    } else {
      node.removeAttribute('style');
    }
  }
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/** Options for {@link sanitizeEmailHtml}. Every default is the safe one. */
export interface SanitizeEmailHtmlOptions {
  /**
   * Keep the `http(s)` `src` of `<img>` so remote images load. Default `false`: a remote image is a read receipt (the
   * sender learns the reader's IP and read time), so by default only `alt` / `width` / `height` survive.
   *
   * Pass `true` only for a render the reader asked for (an explicit "show images" action) and only in a surface whose
   * readers may be tracked. It affects `<img src>` alone: CSS `url()`, `srcset`, `background=` and every positioning
   * rule stay blocked either way.
   */
  allowRemoteImages?: boolean;
}

/**
 * Sanitizes untrusted email HTML for safe rendering via
 * `dangerouslySetInnerHTML`, using an explicit closed allow-list that forbids
 * scripts/frames/objects/forms, strips every `on*` event handler, restricts
 * URL schemes to `http`/`https`/`mailto`, and forces every anchor to
 * `rel="noopener noreferrer" target="_blank"`.
 *
 * It also keeps a body from painting over the host UI (no fixed/absolute/sticky
 * positioning, `z-index`, offsets, transforms, viewport-sized boxes or negative
 * margins in `style`) and from phoning home on open (no remote `<img src>`, no
 * CSS `url()`), see ISS-003 / #1636.
 *
 * Pure function: input string in, sanitized string out. No React, no side
 * effects beyond the transient DOMPurify hook it registers and removes within
 * the same synchronous call.
 *
 * **Client-side only**: DOMPurify requires a browser DOM (`window`/`document`).
 *
 * @param html - Untrusted email-body HTML (may be empty/null/undefined).
 * @param options - See {@link SanitizeEmailHtmlOptions}. Safe by default.
 * @returns Sanitized HTML safe for `dangerouslySetInnerHTML`. Empty string for
 *          empty input.
 *
 * @example
 * ```tsx
 * import { sanitizeEmailHtml } from "@spaarke/ui-components";
 * <div dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(message.body) }} />
 * ```
 */
export function sanitizeEmailHtml(html: string, options: SanitizeEmailHtmlOptions = {}): string {
  if (!html) {
    return '';
  }

  const allowRemoteImages = options.allowRemoteImages === true;
  DOMPurify.addHook('afterSanitizeAttributes', (node: Element) => hardenNode(node, allowRemoteImages));
  try {
    return DOMPurify.sanitize(html, SANITIZE_CONFIG) as string;
  } finally {
    // Remove only the hook we just added (last-registered on this entry point),
    // keeping DOMPurify's global hook stack untouched for other consumers.
    DOMPurify.removeHook('afterSanitizeAttributes');
  }
}
