/**
 * sanitizeEmailHtml — XSS-hardening tests (email-communication-solution-r5 · task 001 · FR-16 / NFR-03)
 *
 * Verifies the shared field-body email-HTML sanitizer neutralizes the malicious
 * vectors the retired permissive `USE_PROFILES:{html:true}` profile let through,
 * while preserving legitimate email formatting. These assertions are the
 * non-negotiable security contract for the `dangerouslySetInnerHTML` field-body
 * render path in `MessageRow`/`MessageBubble`.
 *
 * @see ADR-021 — Fluent v9 design system
 * @see ADR-022 — React-agnostic Layer-1 logic
 */

import DOMPurify from 'dompurify';
import { sanitizeEmailHtml } from '../sanitizeEmailHtml';

/** Parse sanitized HTML into a live DOM for attribute-level assertions. */
function parse(html: string): HTMLDivElement {
  const container = document.createElement('div');
  container.innerHTML = html;
  return container;
}

describe('sanitizeEmailHtml — malicious HTML neutralization (NFR-03)', () => {
  // ── <script> / frame / object / embed stripping ────────────────────────

  it('strips <script> tags (tag and payload absent)', () => {
    const out = sanitizeEmailHtml('Hello <script>alert(1)</script> world');
    expect(out).not.toContain('<script');
    expect(out).not.toContain('</script>');
    expect(out).not.toContain('alert(1)');
    expect(out).toContain('Hello');
    expect(out).toContain('world');
    expect(parse(out).querySelector('script')).toBeNull();
  });

  it('strips <iframe>, <object>, and <embed> tags', () => {
    const out = sanitizeEmailHtml(
      '<iframe src="https://evil.com"></iframe><object data="x.swf"></object><embed src="x.swf">'
    );
    const dom = parse(out);
    expect(dom.querySelector('iframe')).toBeNull();
    expect(dom.querySelector('object')).toBeNull();
    expect(dom.querySelector('embed')).toBeNull();
    expect(out).not.toContain('<iframe');
    expect(out).not.toContain('<object');
    expect(out).not.toContain('<embed');
  });

  it('strips <form> tags', () => {
    const out = sanitizeEmailHtml('<form action="https://evil.com"><input name="x"></form>');
    expect(parse(out).querySelector('form')).toBeNull();
  });

  // ── event-handler (on*) attribute stripping ────────────────────────────

  it('removes onerror from an <img>', () => {
    const out = sanitizeEmailHtml('<img src="https://x/y.png" onerror="alert(1)">');
    expect(out).not.toMatch(/onerror\s*=/i);
    expect(out).not.toContain('alert(1)');
    const img = parse(out).querySelector('img');
    // img itself is allowed (legit https src) but must carry no handler
    expect(img?.getAttribute('onerror')).toBeNull();
  });

  it('removes onclick / onload / onmouseover handlers from any element', () => {
    const out = sanitizeEmailHtml(
      '<div onclick="a()">x</div><span onmouseover="b()">y</span><img src="https://x/z.png" onload="c()">'
    );
    parse(out)
      .querySelectorAll('*')
      .forEach(el => {
        expect(el.getAttribute('onclick')).toBeNull();
        expect(el.getAttribute('onmouseover')).toBeNull();
        expect(el.getAttribute('onload')).toBeNull();
      });
  });

  // ── URL-scheme restriction (http / https / mailto only) ────────────────

  it('neutralizes a javascript: href', () => {
    const out = sanitizeEmailHtml('<a href="javascript:alert(1)">click</a>');
    expect(out).not.toMatch(/href\s*=\s*["']?javascript:/i);
    const a = parse(out).querySelector('a');
    expect(a?.getAttribute('href')).toBeNull();
  });

  it('neutralizes a data:text/html href', () => {
    const out = sanitizeEmailHtml('<a href="data:text/html,<script>alert(1)</script>">click</a>');
    expect(out).not.toContain('data:text/html');
    expect(parse(out).querySelector('a')?.getAttribute('href')).toBeNull();
  });

  it('drops a data: image src (data URIs not allowed on the field-body path)', () => {
    const out = sanitizeEmailHtml(
      '<img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==">'
    );
    const img = parse(out).querySelector('img');
    // src removed (scheme not allowed); img element itself may or may not remain
    expect(img?.getAttribute('src') ?? '').not.toContain('data:');
  });

  // ── anchor hardening ───────────────────────────────────────────────────

  it('forces rel="noopener noreferrer" target="_blank" on a benign https anchor', () => {
    const out = sanitizeEmailHtml('<a href="https://example.com/path">link</a>');
    const a = parse(out).querySelector('a');
    expect(a).not.toBeNull();
    expect(a?.getAttribute('href')).toBe('https://example.com/path');
    expect(a?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(a?.getAttribute('target')).toBe('_blank');
  });

  it('overwrites an attacker-supplied target/rel with the safe values', () => {
    const out = sanitizeEmailHtml('<a href="https://x.com" target="_self" rel="opener">link</a>');
    const a = parse(out).querySelector('a');
    expect(a?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(a?.getAttribute('target')).toBe('_blank');
  });

  it('preserves a mailto: anchor', () => {
    const out = sanitizeEmailHtml('<a href="mailto:a@b.com">mail</a>');
    expect(parse(out).querySelector('a')?.getAttribute('href')).toBe('mailto:a@b.com');
  });

  // ── legitimate content preservation ────────────────────────────────────

  it('preserves bold/italic/links/lists/tables and inline https images', () => {
    const out = sanitizeEmailHtml(
      '<b>bold</b> <i>it</i>' +
        '<ul><li>one</li><li>two</li></ul>' +
        '<table><thead><tr><th>H</th></tr></thead><tbody><tr><td>C</td></tr></tbody></table>' +
        '<a href="https://x.com">L</a>' +
        '<img src="https://cdn.example.com/logo.png" alt="logo" width="100">'
    );
    const dom = parse(out);
    expect(dom.querySelector('b')?.textContent).toBe('bold');
    expect(dom.querySelector('i')?.textContent).toBe('it');
    expect(dom.querySelectorAll('li').length).toBe(2);
    expect(dom.querySelector('table')).not.toBeNull();
    expect(dom.querySelector('th')?.textContent).toBe('H');
    expect(dom.querySelector('td')?.textContent).toBe('C');
    const img = dom.querySelector('img');
    expect(img?.getAttribute('src')).toBe('https://cdn.example.com/logo.png');
    expect(img?.getAttribute('alt')).toBe('logo');
  });

  it('preserves inline style + class (email layout / dark-mode chrome, ADR-021)', () => {
    const out = sanitizeEmailHtml('<p style="margin:0" class="lead">para</p>');
    const p = parse(out).querySelector('p');
    expect(p?.getAttribute('class')).toBe('lead');
    expect(p?.getAttribute('style') ?? '').toContain('margin');
  });

  it('sanitizes dangerous CSS inside an allowed style attribute', () => {
    const out = sanitizeEmailHtml('<div style="background:url(javascript:alert(1))">x</div>');
    expect(out.toLowerCase()).not.toContain('javascript:');
  });

  // ── empty / falsy input ────────────────────────────────────────────────

  it('returns empty string for empty/null/undefined input', () => {
    expect(sanitizeEmailHtml('')).toBe('');
    expect(sanitizeEmailHtml(null as unknown as string)).toBe('');
    expect(sanitizeEmailHtml(undefined as unknown as string)).toBe('');
  });

  // ── hook isolation (no global leak onto other DOMPurify consumers) ──────

  it('does not leak the anchor hook onto subsequent bare DOMPurify calls', () => {
    // Force a sanitize, then confirm a follow-up bare DOMPurify usage is unaffected.
    sanitizeEmailHtml('<a href="https://x.com">a</a>');
    const plain = DOMPurify.sanitize('<a href="https://y.com">b</a>');
    expect(plain).not.toContain('target="_blank"');
    expect(plain).not.toContain('noopener');
  });
});
