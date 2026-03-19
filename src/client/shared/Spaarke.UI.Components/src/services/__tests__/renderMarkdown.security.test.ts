/**
 * renderMarkdown Security Tests (R2-066)
 *
 * Verifies that DOMPurify sanitization prevents XSS attacks in all
 * markdown rendering paths. These tests validate the security contract:
 * any HTML output from renderMarkdown MUST be safe for dangerouslySetInnerHTML.
 *
 * @see ADR-015 — data governance
 * @see ADR-021 — Fluent v9 design system
 */

import { renderMarkdown } from '../renderMarkdown';

describe('renderMarkdown — XSS prevention (R2-066 security audit)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Script injection vectors
  // ─────────────────────────────────────────────────────────────────────

  it('strips <script> tags from markdown input', () => {
    const malicious = 'Hello <script>alert("xss")</script> world';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('<script>');
    expect(html).not.toContain('</script>');
    expect(html).not.toContain('alert');
    expect(html).toContain('Hello');
    expect(html).toContain('world');
  });

  it('strips <script> tags embedded in markdown formatting', () => {
    const malicious = '**Bold <script>document.cookie</script> text**';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('<script>');
    expect(html).not.toContain('document.cookie');
    expect(html).toContain('<strong>');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Event handler injection vectors
  // ─────────────────────────────────────────────────────────────────────

  it('strips onerror attribute from <img> tags', () => {
    const malicious = '<img src="x" onerror="alert(1)" />';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('onerror');
    expect(html).not.toContain('alert');
    // img tag itself may be preserved (allowed tag) but without the handler
    if (html.includes('<img')) {
      expect(html).not.toMatch(/onerror\s*=/i);
    }
  });

  it('strips onload attribute from <img> tags', () => {
    const malicious = '<img src="valid.png" onload="fetch(\'https://evil.com\')" />';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('onload');
    expect(html).not.toContain('evil.com');
  });

  it('strips onclick attribute from elements', () => {
    const malicious = '<div onclick="alert(1)">Click me</div>';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('onclick');
  });

  it('strips onmouseover from <a> tags', () => {
    const malicious = '[link](javascript:void(0) "title" onmouseover="alert(1)")';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('onmouseover');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Iframe and object injection
  // ─────────────────────────────────────────────────────────────────────

  it('strips <iframe> tags', () => {
    const malicious = 'Text <iframe src="https://evil.com"></iframe> more text';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('<iframe');
    expect(html).not.toContain('</iframe>');
  });

  it('strips <object> and <embed> tags', () => {
    const malicious = '<object data="evil.swf"></object><embed src="evil.swf">';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('<object');
    expect(html).not.toContain('<embed');
  });

  // ─────────────────────────────────────────────────────────────────────
  // javascript: protocol injection
  // ─────────────────────────────────────────────────────────────────────

  it('neutralizes javascript: protocol in links', () => {
    const malicious = '[click me](javascript:alert(1))';
    const html = renderMarkdown(malicious);

    // DOMPurify should either remove the href or sanitize the protocol
    expect(html).not.toMatch(/href\s*=\s*["']javascript:/i);
  });

  it('neutralizes data: protocol SVG injection', () => {
    const malicious = '<a href="data:text/html,<script>alert(1)</script>">click</a>';
    const html = renderMarkdown(malicious);

    // Should not contain executable data URI
    expect(html).not.toContain('data:text/html');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Style-based attacks
  // ─────────────────────────────────────────────────────────────────────

  it('strips <style> tags', () => {
    const malicious = '<style>body { display: none; }</style>Content';
    const html = renderMarkdown(malicious);

    expect(html).not.toContain('<style>');
  });

  it('strips style attributes with expressions', () => {
    const malicious = '<div style="background:url(javascript:alert(1))">text</div>';
    const html = renderMarkdown(malicious);

    // style attribute should be stripped (not in ALLOWED_ATTRS)
    expect(html).not.toContain('style=');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Legitimate content preservation
  // ─────────────────────────────────────────────────────────────────────

  it('preserves legitimate markdown formatting', () => {
    const markdown = '# Heading\n\n**Bold** and *italic* text.\n\n- Item 1\n- Item 2';
    const html = renderMarkdown(markdown);

    expect(html).toContain('<h1>');
    expect(html).toContain('<strong>');
    expect(html).toContain('<em>');
    expect(html).toContain('<li>');
  });

  it('preserves code blocks without XSS', () => {
    const markdown = '```js\nconst x = "<script>alert(1)</script>";\n```';
    const html = renderMarkdown(markdown);

    expect(html).toContain('<code>');
    // Script tags inside code should be escaped, not executable
    expect(html).not.toMatch(/<script>/);
  });

  it('wraps output in sprk-markdown container', () => {
    const html = renderMarkdown('Hello world');
    expect(html).toMatch(/^<div class="sprk-markdown">/);
    expect(html).toMatch(/<\/div>$/);
  });

  it('returns empty string for empty input', () => {
    expect(renderMarkdown('')).toBe('');
    expect(renderMarkdown(null as unknown as string)).toBe('');
    expect(renderMarkdown(undefined as unknown as string)).toBe('');
  });
});
