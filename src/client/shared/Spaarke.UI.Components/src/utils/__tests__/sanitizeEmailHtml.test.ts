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
    // the <img> element survives (its remote src is dropped by default, ISS-003) but must carry no handler
    expect(img).not.toBeNull();
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

  it('preserves bold/italic/links/lists/tables; a remote https image keeps its alt but loses its src (ISS-003)', () => {
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
    expect(img).not.toBeNull();
    expect(img?.getAttribute('src')).toBeNull();
    expect(img?.getAttribute('alt')).toBe('logo');
    expect(out).not.toContain('cdn.example.com');
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

// ===========================================================================
// ISS-003 — overlay positioning + remote content (GitHub #1636)
// ===========================================================================

/**
 * `<tag id="x" style="css">inner</tag>`. Double quotes in the CSS are entity-escaped so they reach the sanitizer inside
 * the attribute value instead of ending the attribute (the HTML parser decodes them back, as it does for a real email).
 */
function withStyle(css: string, inner = 'hi', tag = 'div'): string {
  return `<${tag} id="x" style="${css.replace(/"/g, '&quot;')}">${inner}</${tag}>`;
}

/** The sanitized `style` attribute of the first element matching `selector` ('' when absent). */
function styleOf(html: string, selector: string): string {
  return parse(html).querySelector(selector)?.getAttribute('style') ?? '';
}

describe('sanitizeEmailHtml — overlay / positioning neutralisation (ISS-003)', () => {
  const OVERLAY =
    '<div id="overlay" style="position:fixed; z-index:99999; top:0; left:0; width:100vw; height:100vh; background:#fff">' +
    '<p>Session expired. Sign in to continue</p></div>';

  it('neutralises a full-viewport fixed overlay: no position, z-index, offsets or viewport sizes survive', () => {
    const out = sanitizeEmailHtml(OVERLAY);
    const style = styleOf(out, '#overlay');
    expect(style).not.toMatch(/position/i);
    expect(style).not.toMatch(/z-index/i);
    expect(style).not.toMatch(/(^|;)\s*(top|left|right|bottom|inset)\s*:/i);
    expect(style).not.toMatch(/100v[wh]/i);
    // The content is kept (this neutralises, it does not blank) and the harmless background survives.
    expect(parse(out).querySelector('p')?.textContent).toBe('Session expired. Sign in to continue');
    expect(style).toMatch(/background:\s*#fff/i);
  });

  it.each([
    ['absolute', 'position:absolute;top:0;left:0'],
    ['sticky', 'position:sticky;top:0'],
    ['-webkit-sticky', 'position:-webkit-sticky;top:0'],
    ['uppercase + !important + spaces', 'POSITION : Fixed !important'],
    ['inset shorthand', 'inset:0'],
    ['inset logical longhand', 'inset-inline-start:0;inset-block-start:0'],
    ['z-index alone', 'z-index:2147483647'],
    ['transform', 'transform:translate(-9999px,-9999px)'],
    ['vendor-prefixed transform', '-webkit-transform:scale(50)'],
    ['translate / scale / zoom', 'translate:0 -5000px;scale:40;zoom:20'],
    ['offset-path', 'offset-path:path("M0 0 L9999 0");offset-distance:100%'],
    ['viewport width', 'width:100vw'],
    ['viewport height in calc()', 'height:calc(100vh - 0px)'],
    ['dynamic viewport units', 'min-height:100dvh;max-width:100svw'],
    ['negative margin (pulls the box over neighbours)', 'margin-top:-5000px'],
    ['negative margin shorthand', 'margin:0 0 0 -9999px'],
    ['calc margin', 'margin:calc(0px - 2000px)'],
    ['negative text-indent', 'text-indent:-9999px'],
  ])('drops %s', (_label, css) => {
    const out = sanitizeEmailHtml(withStyle(`color:red;${css}`));
    const style = styleOf(out, '#x');
    // The harmless declaration alongside it is kept ...
    expect(style).toMatch(/color:\s*red/i);
    // ... and none of the dangerous property or value survives.
    expect(style).not.toMatch(
      /position|inset|z-index|transform|translate|scale|zoom|offset|v[wh]\b|dv[wh]|sv[wh]|vmin|vmax|margin|text-indent/i
    );
    expect(parse(out).querySelector('#x')?.textContent).toBe('hi');
  });

  it.each([
    ['css escape in the property name', 'po\\73ition:fixed'],
    ['css escape in the value', 'position:\\66ixed'],
    ['comment between name and colon', 'position/**/:fixed'],
    ['comment hiding a declaration inside a string', 'font-family:"/*";position:fixed;"*/"'],
    ['a string broken by a newline', "font-family:'x\n;position:fixed"],
    ['entity-encoded colon', 'position&#58;fixed'],
    ['var() indirection', 'margin-top:var(--m)'],
    ['custom property carrying a value', '--p:fixed;position:var(--p)'],
  ])('is not fooled by %s', (_label, css) => {
    const out = sanitizeEmailHtml(withStyle(`color:red;${css}`));
    const style = styleOf(out, '#x');
    expect(style).not.toMatch(/position|fixed|var\(|--/i);
    expect(style).not.toMatch(/\\/);
    expect(style).not.toMatch(/margin/i);
    expect(parse(out).querySelector('#x')?.textContent).toBe('hi');
  });

  it('keeps position:relative / static (inert once top/left/inset are gone) but never fixed, absolute or sticky', () => {
    expect(styleOf(sanitizeEmailHtml('<div id="x" style="position:relative;top:-9999px">a</div>'), '#x')).toBe(
      'position:relative'
    );
    expect(styleOf(sanitizeEmailHtml('<div id="x" style="position: static">a</div>'), '#x')).toMatch(/static/);
  });

  it('removes the style attribute entirely when nothing safe is left', () => {
    const out = sanitizeEmailHtml('<div id="x" style="position:fixed;inset:0;z-index:9">hi</div>');
    expect(parse(out).querySelector('#x')?.hasAttribute('style')).toBe(false);
  });

  it('still drops the whole style for the legacy script tokens (existing DANGEROUS_CSS behaviour)', () => {
    const out = sanitizeEmailHtml('<div id="x" style="color:red;width:expression(alert(1))">hi</div>');
    expect(parse(out).querySelector('#x')?.hasAttribute('style')).toBe(false);
  });

  it('neutralises the overlay on every element, including table cells and inline spans', () => {
    const out = sanitizeEmailHtml(
      '<table style="position:fixed"><tr><td style="position:absolute;z-index:5">c</td></tr></table>' +
        '<span style="position:sticky">s</span>'
    );
    for (const el of Array.from(parse(out).querySelectorAll('*'))) {
      expect(el.getAttribute('style') ?? '').not.toMatch(/position|z-index/i);
    }
  });

  it('keeps normal email formatting: colours, fonts, spacing, widths, alignment, borders, plain backgrounds', () => {
    const css =
      'color:#c00; font-family:Arial, "Segoe UI", sans-serif; font-size:14px; font-weight:bold; font-style:italic; ' +
      'text-align:center; text-decoration:underline; line-height:1.4; padding:8px 12px; margin:0 auto; width:600px; ' +
      'max-width:100%; background-color:#eeeeee; border:1px solid #ccc; border-collapse:collapse; vertical-align:top; text-indent:20px';
    const out = sanitizeEmailHtml(withStyle(css, '<tr><td>c</td></tr>', 'table'));
    const style = styleOf(out, '#x');
    for (const decl of [
      'color:#c00',
      'font-family:Arial, "Segoe UI", sans-serif',
      'font-size:14px',
      'font-weight:bold',
      'font-style:italic',
      'text-align:center',
      'text-decoration:underline',
      'line-height:1.4',
      'padding:8px 12px',
      'margin:0 auto',
      'width:600px',
      'max-width:100%',
      'background-color:#eeeeee',
      'border:1px solid #ccc',
      'border-collapse:collapse',
      'vertical-align:top',
      'text-indent:20px',
    ]) {
      expect(style).toContain(decl);
    }
  });

  it('keeps bold, font colour, nested tables and links together in one realistic message', () => {
    const out = sanitizeEmailHtml(
      '<table width="600" cellpadding="4" bgcolor="#f4f4f4"><tr><td align="center">' +
        '<font color="#003366"><b>Invoice 42</b></font> ' +
        '<a href="https://portal.example.com/i/42" style="color:#0066cc;text-decoration:none">View</a>' +
        '<table><tr><td>inner</td></tr></table></td></tr></table>'
    );
    const dom = parse(out);
    expect(dom.querySelectorAll('table')).toHaveLength(2);
    expect(dom.querySelector('b')?.textContent).toBe('Invoice 42');
    // (Presentational attributes such as `color` / `bgcolor` / `width` are dropped by the existing URI-regexp interplay
    // with DOMPurify, independent of this change; the elements and the inline `style` are what survive.)
    expect(dom.querySelector('font')?.textContent).toBe('Invoice 42');
    expect(dom.querySelector('td')?.textContent).toContain('Invoice 42');
    const a = dom.querySelector('a');
    expect(a?.getAttribute('href')).toBe('https://portal.example.com/i/42');
    expect(a?.getAttribute('style')).toContain('color:#0066cc');
    expect(a?.getAttribute('rel')).toBe('noopener noreferrer');
  });
});

describe('sanitizeEmailHtml — remote content is not loaded by default (ISS-003)', () => {
  it('does not load a remote tracking pixel: the <img> src is dropped and the URL appears nowhere', () => {
    const out = sanitizeEmailHtml('<p>Hi</p><img src="https://t.example/p.gif" width=1 height=1>');
    const img = parse(out).querySelector('img');
    expect(img?.hasAttribute('src') ?? false).toBe(false);
    expect(out).not.toContain('t.example');
    expect(parse(out).querySelector('p')?.textContent).toBe('Hi');
  });

  it.each([
    ['http', 'http://t.example/p.gif'],
    ['https with query', 'https://t.example/p.gif?uid=42&t=1'],
    ['upper-case scheme', 'HTTPS://t.example/p.gif'],
    ['leading whitespace', '  https://t.example/p.gif'],
    ['protocol-relative', '//t.example/p.gif'],
    ['relative', '/tracking/p.gif'],
  ])('drops the src of a remote image (%s)', (_label, src) => {
    const out = sanitizeEmailHtml(`<img src="${src}" alt="logo">`);
    const img = parse(out).querySelector('img');
    expect(img?.hasAttribute('src') ?? false).toBe(false);
    expect(img?.getAttribute('alt')).toBe('logo');
    expect(out).not.toContain('t.example');
  });

  it('drops the src of an <img> nested in a link and in a picture', () => {
    const out = sanitizeEmailHtml(
      '<a href="https://x.com"><img src="https://t.example/a.gif" alt="a"></a>' +
        '<picture><source src="https://t.example/b.gif"><img src="https://t.example/c.gif" alt="c"></picture>'
    );
    for (const el of Array.from(parse(out).querySelectorAll('img, source'))) {
      expect(el.hasAttribute('src')).toBe(false);
    }
    expect(out).not.toContain('t.example');
  });

  it('keeps the link around a blocked image working', () => {
    const out = sanitizeEmailHtml('<a href="https://x.com/go"><img src="https://t.example/a.gif" alt="Go"></a>');
    expect(parse(out).querySelector('a')?.getAttribute('href')).toBe('https://x.com/go');
  });

  // data: and cid: images are NOT rendered on this path today (see the file header). That is unchanged.
  it('data: images keep their current behaviour: src removed', () => {
    const out = sanitizeEmailHtml('<img src="data:image/png;base64,iVBORw0KGgo=" alt="inline">');
    const img = parse(out).querySelector('img');
    expect(img?.hasAttribute('src') ?? false).toBe(false);
    expect(img?.getAttribute('alt')).toBe('inline');
    expect(out).not.toContain('data:');
  });

  it('cid: inline attachments keep their current behaviour: src removed', () => {
    const out = sanitizeEmailHtml('<img src="cid:image001.png@01D9" alt="inline">');
    const img = parse(out).querySelector('img');
    expect(img?.hasAttribute('src') ?? false).toBe(false);
    expect(img?.getAttribute('alt')).toBe('inline');
    expect(out).not.toContain('cid:');
  });

  it('srcset never survives (img or picture/source): it is a comma-separated URL list', () => {
    const out = sanitizeEmailHtml(
      '<img src="https://t.example/a.gif" srcset="https://t.example/a1.gif 1x, https://t.example/a2.gif 2x" alt="a">' +
        '<picture><source srcset="https://t.example/b.gif" media="(min-width:1px)"><img alt="b"></picture>'
    );
    expect(out).not.toMatch(/srcset/i);
    expect(out).not.toContain('t.example');
  });

  it('background attributes (table / td / body-style) never survive', () => {
    const out = sanitizeEmailHtml(
      '<table background="https://t.example/bg.gif"><tr><td background="https://t.example/bg2.gif">c</td></tr></table>'
    );
    expect(out).not.toMatch(/background\s*=/i);
    expect(out).not.toContain('t.example');
    expect(parse(out).querySelector('td')?.textContent).toBe('c');
  });

  it.each([
    ['background shorthand', 'background:url(https://t.example/p.gif) no-repeat'],
    ['background-image', 'background-image:url("https://t.example/p.gif")'],
    ['upper-case URL with spaces', "background-image: URL( 'https://t.example/p.gif' )"],
    ['list-style-image', 'list-style-image:url(https://t.example/p.gif)'],
    ['cursor', 'cursor:url(https://t.example/p.cur), auto'],
    ['border-image', 'border-image:url(https://t.example/p.png) 30'],
    ['content', 'content:url(https://t.example/p.gif)'],
    ['mask', '-webkit-mask-image:url(https://t.example/p.png)'],
    ['image-set with a string', 'background-image:image-set("https://t.example/p.gif" 1x)'],
    ['-webkit-image-set', 'background-image:-webkit-image-set(url(https://t.example/p.gif) 1x)'],
    ['cross-fade', 'background-image:cross-fade(url(https://t.example/p.gif), red, 50%)'],
    ['url with a data: payload', 'background:url(data:image/gif;base64,R0lGODlhAQABAAAAACw=)'],
    ['url with a protocol-relative host', 'background:url(//t.example/p.gif)'],
  ])('blocks CSS url() / image functions: %s', (_label, css) => {
    const out = sanitizeEmailHtml(withStyle(`color:red;${css}`));
    const style = styleOf(out, '#x');
    expect(style).not.toMatch(/url\s*\(|image-set|cross-fade|t\.example|data:/i);
    expect(out).not.toContain('t.example');
    // the harmless declaration alongside it is kept
    expect(style).toMatch(/color:\s*red/i);
  });

  it('keeps a background colour even next to a blocked url()', () => {
    const out = sanitizeEmailHtml(
      '<div id="x" style="background-color:#eee;background-image:url(https://t.example/p.gif)">hi</div>'
    );
    expect(styleOf(out, '#x')).toBe('background-color:#eee');
  });

  it('does not touch the anchor href of a normal link (clicking is a user action, not an auto-load)', () => {
    const out = sanitizeEmailHtml('<a href="https://t.example/click?id=1">x</a>');
    expect(parse(out).querySelector('a')?.getAttribute('href')).toBe('https://t.example/click?id=1');
  });
});

describe('sanitizeEmailHtml — allowRemoteImages opt-in (ISS-003)', () => {
  const PIXEL = '<img src="https://t.example/p.gif" alt="px">';

  it('is off by default, with an empty options object, and for any value other than literal true', () => {
    for (const out of [
      sanitizeEmailHtml(PIXEL),
      sanitizeEmailHtml(PIXEL, {}),
      sanitizeEmailHtml(PIXEL, { allowRemoteImages: false }),
      sanitizeEmailHtml(PIXEL, { allowRemoteImages: 'yes' as unknown as boolean }),
    ]) {
      expect(out).not.toContain('t.example');
    }
  });

  it('{ allowRemoteImages: true } keeps the http(s) src of an <img>', () => {
    const out = sanitizeEmailHtml(`${PIXEL}<img src="http://cdn.example/logo.png" alt="logo">`, {
      allowRemoteImages: true,
    });
    const srcs = Array.from(parse(out).querySelectorAll('img')).map(i => i.getAttribute('src'));
    expect(srcs).toEqual(['https://t.example/p.gif', 'http://cdn.example/logo.png']);
  });

  it('the opt-in widens ONLY <img src>: handlers, odd schemes, srcset, background=, CSS url() and positioning stay blocked', () => {
    const out = sanitizeEmailHtml(
      '<img src="https://t.example/a.gif" onerror="x()" srcset="https://t.example/a2.gif 2x" alt="a">' +
        '<img src="data:image/png;base64,iVBORw0KGgo=" alt="d"><img src="cid:logo@01" alt="c">' +
        '<table background="https://t.example/bg.gif"><tr>' +
        '<td id="x" style="color:red;background:url(https://t.example/b.gif);position:fixed;z-index:9;width:100vw">c</td></tr></table>',
      { allowRemoteImages: true }
    );
    const imgs = Array.from(parse(out).querySelectorAll('img'));
    expect(imgs[0].getAttribute('src')).toBe('https://t.example/a.gif');
    expect(imgs[0].getAttribute('onerror')).toBeNull();
    expect(imgs[1].hasAttribute('src')).toBe(false);
    expect(imgs[2].hasAttribute('src')).toBe(false);
    expect(out).not.toMatch(/srcset|background\s*=|onerror|a2\.gif|b\.gif|bg\.gif/i);
    expect(styleOf(out, '#x')).toBe('color:red');
  });

  it('does not leak between calls: an opt-in call does not make the next default call permissive', () => {
    sanitizeEmailHtml(PIXEL, { allowRemoteImages: true });
    expect(sanitizeEmailHtml(PIXEL)).not.toContain('t.example');
  });

  it('does not leak the hook onto a bare DOMPurify call after an opt-in call', () => {
    sanitizeEmailHtml(PIXEL, { allowRemoteImages: true });
    const plain = DOMPurify.sanitize('<a href="https://y.com">b</a><img src="https://t.example/p.gif">');
    expect(plain).not.toContain('noopener');
    expect(plain).toContain('t.example/p.gif');
  });
});
