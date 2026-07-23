/**
 * quoteBody.test.ts (task 063, FR-13)
 *
 * Unit coverage for the pure, channel-agnostic `quoteBody()` helper. Pure
 * function, no I/O, no platform APIs — ADR-038 domain-logic behavior
 * contract (MAINTAIN-class), not scaffolding.
 */
import { quoteBody } from '../quoteBody';
import {
  BODY_FORMAT_HTML,
  BODY_FORMAT_PLAIN_TEXT,
} from '../../components/CommunicationTimeline/CommunicationTimeline.types';

describe('quoteBody — HTML target', () => {
  it('wraps a plain-text source in a <blockquote> as escaped <p> lines', () => {
    const result = quoteBody('Hello\nWorld', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML);
    expect(result).toContain('<blockquote>');
    expect(result).toContain('</blockquote>');
    expect(result).toContain('<p>Hello</p>');
    expect(result).toContain('<p>World</p>');
  });

  it('wraps an HTML source in a <blockquote>, sanitized', () => {
    const result = quoteBody('<p>Hi <strong>there</strong></p>', BODY_FORMAT_HTML, BODY_FORMAT_HTML);
    expect(result).toContain('<blockquote>');
    expect(result).toContain('<strong>there</strong>');
  });

  it('strips <script> tags from an untrusted HTML source (XSS)', () => {
    const result = quoteBody('<p>Hi</p><script>alert(1)</script>', BODY_FORMAT_HTML, BODY_FORMAT_HTML);
    expect(result).not.toContain('<script>');
    expect(result).not.toContain('alert(1)');
  });

  it('strips inline event-handler attributes from an untrusted HTML source (XSS)', () => {
    const result = quoteBody('<img src="x" onerror="alert(1)">', BODY_FORMAT_HTML, BODY_FORMAT_HTML);
    expect(result).not.toContain('onerror');
  });

  it('prepends an attribution line as a <p> before the <blockquote>', () => {
    const result = quoteBody('Body text', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML, {
      sender: 'jane@example.com',
      sentOn: '2026-07-01T10:00:00Z',
    });
    const attributionIndex = result.indexOf('jane@example.com');
    const blockquoteIndex = result.indexOf('<blockquote>');
    expect(attributionIndex).toBeGreaterThan(-1);
    expect(blockquoteIndex).toBeGreaterThan(-1);
    expect(attributionIndex).toBeLessThan(blockquoteIndex);
    expect(result).toContain('wrote:');
  });
});

describe('quoteBody — plain-text target', () => {
  it('prefixes each line of a plain-text source with "> "', () => {
    const result = quoteBody('Hello\nWorld', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT);
    expect(result).toBe('> Hello\n> World');
  });

  it('degrades an HTML source to plain text before prefixing with "> "', () => {
    const result = quoteBody('<p>Hello</p><p>World</p>', BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT);
    expect(result).toBe('> Hello\n> World');
  });

  it('strips inline formatting tags but keeps their text content when degrading HTML', () => {
    const result = quoteBody(
      '<p>Hi <strong>there</strong>, <em>friend</em></p>',
      BODY_FORMAT_HTML,
      BODY_FORMAT_PLAIN_TEXT
    );
    expect(result).toBe('> Hi there, friend');
  });

  it('strips <script> content entirely when degrading HTML to plain text (XSS)', () => {
    const result = quoteBody('<p>Hi</p><script>alert(1)</script>', BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT);
    expect(result).not.toContain('alert(1)');
    expect(result).not.toContain('<script>');
  });

  it('converts <br> to a line break before prefixing', () => {
    const result = quoteBody('Line1<br>Line2', BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT);
    expect(result).toBe('> Line1\n> Line2');
  });

  it('prepends an attribution line above the quoted lines', () => {
    const result = quoteBody('Body text', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT, {
      sender: 'Jane Doe',
      sentOn: '2026-07-01T10:00:00Z',
    });
    const lines = result.split('\n');
    expect(lines[0]).toContain('Jane Doe');
    expect(lines[0]).toContain('wrote:');
    expect(lines[1]).toBe('> Body text');
  });

  it('renders a sender-only attribution line when sentOn is absent', () => {
    const result = quoteBody('Body', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT, { sender: 'Jane Doe' });
    expect(result.split('\n')[0]).toBe('Jane Doe wrote:');
  });

  it('preserves blank lines as a bare ">"', () => {
    const result = quoteBody('Para 1\n\nPara 2', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT);
    expect(result).toBe('> Para 1\n>\n> Para 2');
  });
});

describe('quoteBody — empty / whitespace source handling', () => {
  it('returns an empty string for an empty source and no attribution', () => {
    expect(quoteBody('', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML)).toBe('');
    expect(quoteBody('', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT)).toBe('');
  });

  it('returns an empty string for a whitespace-only source and no attribution', () => {
    expect(quoteBody('   \n\t  ', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT)).toBe('');
  });

  it('returns an empty string for null/undefined source and no attribution', () => {
    expect(quoteBody(null, BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML)).toBe('');
    expect(quoteBody(undefined, BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML)).toBe('');
  });

  it('still renders the attribution line when the source is empty', () => {
    const result = quoteBody('', BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_PLAIN_TEXT, { sender: 'Jane Doe' });
    expect(result).toBe('Jane Doe wrote:');
  });

  it('does not throw for empty HTML-degrade-to-text source', () => {
    expect(() => quoteBody('   ', BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT)).not.toThrow();
  });
});

describe('quoteBody — round trip (email -> message -> email) stays sane', () => {
  it('re-quoting an already-quoted HTML body produces valid nested markup without throwing', () => {
    const originalEmailHtml = '<p>Original email body</p>';

    // Email -> message (HTML target, e.g. "Quote into message").
    const intoMessage = quoteBody(originalEmailHtml, BODY_FORMAT_HTML, BODY_FORMAT_HTML, {
      sender: 'alice@example.com',
      sentOn: '2026-07-01T10:00:00Z',
    });
    expect(intoMessage).toContain('Original email body');

    // Message -> email (HTML target, e.g. "Quote into email" on the reply).
    const backIntoEmail = quoteBody(intoMessage, BODY_FORMAT_HTML, BODY_FORMAT_HTML, {
      sender: 'bob@example.com',
      sentOn: '2026-07-01T11:00:00Z',
    });

    expect(backIntoEmail).toContain('Original email body');
    expect(backIntoEmail).not.toContain('<script>');
    // Two nested attributions + two nested blockquotes — no XSS vector introduced by re-quoting.
    expect((backIntoEmail.match(/<blockquote>/g) ?? []).length).toBeGreaterThanOrEqual(2);
  });

  it('re-quoting into plain text and back to HTML stays legible (no raw ">"-noise leaking into HTML)', () => {
    const originalEmailHtml = '<p>Line one</p><p>Line two</p>';

    const intoMessageText = quoteBody(originalEmailHtml, BODY_FORMAT_HTML, BODY_FORMAT_PLAIN_TEXT, {
      sender: 'alice@example.com',
    });
    expect(intoMessageText).toBe('alice@example.com wrote:\n> Line one\n> Line two');

    const backIntoEmailHtml = quoteBody(intoMessageText, BODY_FORMAT_PLAIN_TEXT, BODY_FORMAT_HTML);
    expect(backIntoEmailHtml).toContain('&gt; Line one');
    expect(backIntoEmailHtml).toContain('<blockquote>');
  });
});
