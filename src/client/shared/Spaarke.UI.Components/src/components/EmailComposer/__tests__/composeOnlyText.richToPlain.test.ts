/**
 * composeOnlyText.richToPlain.test.ts - task 197 fix round 1 (F2-a).
 *
 * `richTextToPlainText` is the conversion for a send path that carries plain text only (the decision wizard's commit route): it
 * keeps what the user can see, where `toPlainText` (kept as is for its other callers) drops links' addresses and list markers.
 */
import { richTextToPlainText, toPlainText } from '../composeOnlyText';

describe('richTextToPlainText', () => {
  it('writes a link whose text differs from its address as "text (address)"', () => {
    expect(richTextToPlainText('<p>see <a href="https://portal/x">the invoice</a></p>')).toBe(
      'see the invoice (https://portal/x)'
    );
  });

  it('keeps a link whose text is its address once, and a mailto link whose text is the address', () => {
    expect(richTextToPlainText('<p><a href="https://portal/x">https://portal/x</a></p>')).toBe('https://portal/x');
    expect(richTextToPlainText('<p><a href="mailto:a@b.example">a@b.example</a></p>')).toBe('a@b.example');
  });

  it('drops a javascript:, data: or other non-http(s)/mailto address and keeps the text', () => {
    expect(
      richTextToPlainText(
        '<p><a href="javascript:alert(1)">x</a><a href="data:text/html,hi">y</a><a href="ftp://h">z</a></p>'
      )
    ).toBe('xyz');
    expect(richTextToPlainText('<p><a>no address</a></p>')).toBe('no address');
  });

  it('numbers an ordered list and bullets an unordered one', () => {
    expect(richTextToPlainText('<ol><li>Pay</li><li>File</li></ol>')).toBe('1. Pay\n2. File');
    expect(richTextToPlainText('<ul><li>Pay</li><li>File</li></ul>')).toBe('• Pay\n• File');
    expect(richTextToPlainText('<ol start="3"><li>Pay</li></ol>')).toBe('3. Pay');
  });

  it('indents a nested list under its item', () => {
    expect(richTextToPlainText('<ul><li>Top<ul><li>Inner</li></ul></li><li>Next</li></ul>')).toBe(
      '• Top\n  • Inner\n• Next'
    );
  });

  it('puts a blank line at every block boundary: paragraph, list, heading, quote', () => {
    expect(
      richTextToPlainText('<p>Intro</p><ul><li>a</li></ul><p>Outro</p><h2>Head</h2><blockquote>Quote</blockquote>')
    ).toBe('Intro\n\n• a\n\nOutro\n\nHead\n\nQuote');
  });

  it('keeps a line break inside a paragraph as one line break and decodes entities literally', () => {
    expect(richTextToPlainText('<p>a<br>b &amp; c &lt; d</p>')).toBe('a\nb & c < d');
  });

  it('runs no markup: an img with a handler is only text-free markup', () => {
    expect(richTextToPlainText('<p>Acme <img src=x onerror="window.__xss=1"></p>')).toBe('Acme');
    expect((window as unknown as { __xss?: number }).__xss).toBeUndefined();
  });

  it('leaves text with no markup or entity unchanged', () => {
    expect(richTextToPlainText('a\nb')).toBe('a\nb');
  });

  it('must-not-fire: toPlainText keeps its own output (one line break between paragraphs, no list markers)', () => {
    expect(toPlainText('<p>Line one</p><p>Line two<br>three</p>', 'HTML')).toBe('Line one\nLine two\nthree');
  });
});
